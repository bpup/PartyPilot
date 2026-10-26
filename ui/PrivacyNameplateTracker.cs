namespace PartyPilot.Desktop;

public readonly record struct PrivacyLabelMotion(PrivacyPixelRect Bounds, DateTime ObservedUtc,
    double VelocityX, double VelocityY);

public sealed class PrivacyNameplateTracker
{
    private readonly object _gate = new();
    private IReadOnlyList<PrivacyPixelRect> _tracked = [];
    private IReadOnlyList<PrivacyLabelMotion> _motion = [];
    private DateTime _lastDetectedUtc;
    private int _width;
    private int _height;
    private long _revision;

    public long Revision { get { lock (_gate) return _revision; } }

    public void Observe(IReadOnlyList<PrivacyPixelRect> detected, int width, int height,
        DateTime observedUtc, long? expectedRevision = null)
    {
        lock (_gate)
        {
            if (expectedRevision.HasValue && expectedRevision.Value != _revision) return;
            if (width <= 0 || height <= 0) { ResetLocked(); return; }
            if (width != _width || height != _height)
            {
                ResetLocked();
                _width = width;
                _height = height;
            }
            if (detected.Count == 0) return;
            var merged = Merge(_tracked, detected, width, height);
            _motion = UpdateMotion(_motion, merged, detected, width, height, observedUtc);
            _tracked = merged;
            _lastDetectedUtc = observedUtc;
        }
    }

    public IReadOnlyList<PrivacyPixelRect> Coverage(int width, int height, DateTime nowUtc)
    {
        lock (_gate)
        {
            if (width != _width || height != _height || _lastDetectedUtc == default ||
                nowUtc - _lastDetectedUtc > TimeSpan.FromMilliseconds(140)) return [];
            return CalculateCoverage(_motion, width, height, nowUtc);
        }
    }

    public void Reset() { lock (_gate) ResetLocked(); }

    private void ResetLocked()
    {
        _revision++;
        _tracked = [];
        _motion = [];
        _lastDetectedUtc = default;
        _width = 0;
        _height = 0;
    }

    public static IReadOnlyList<PrivacyPixelRect> Merge(IReadOnlyList<PrivacyPixelRect> previous,
        IReadOnlyList<PrivacyPixelRect> current, int width, int height)
    {
        var old = previous.Where(Valid).Take(2).ToArray();
        var fresh = current.Where(Valid).Take(4).ToArray();
        if (fresh.Length == 0) return old;
        var separation = Math.Max(42, (int)Math.Round(height * 0.04));
        if (old.Length == 0) return PrivacyNameplateDetector.SelectTwo(fresh, height);
        if (old.Length == 1)
        {
            var nearest = Enumerable.Range(0, fresh.Length).MinBy(index => DistanceSquared(old[0], fresh[index]));
            var result = new List<PrivacyPixelRect> { Clamp(fresh[nearest], width, height) };
            var second = fresh.Where((_, index) => index != nearest)
                .OrderByDescending(candidate => DistanceSquared(fresh[nearest], candidate))
                .FirstOrDefault(candidate => DistanceSquared(fresh[nearest], candidate) >=
                    separation * separation);
            if (Valid(second)) result.Add(second);
            return result;
        }
        if (fresh.Length == 1)
        {
            var nearest = DistanceSquared(old[0], fresh[0]) <= DistanceSquared(old[1], fresh[0]) ? 0 : 1;
            old[nearest] = Clamp(fresh[0], width, height);
            return old;
        }
        var firstIndex = 0;
        var secondIndex = 1;
        var best = double.MaxValue;
        for (var first = 0; first < fresh.Length; first++)
            for (var second = 0; second < fresh.Length; second++)
            {
                if (first == second) continue;
                var distance = DistanceSquared(old[0], fresh[first]) +
                    DistanceSquared(old[1], fresh[second]);
                if (distance >= best) continue;
                best = distance;
                firstIndex = first;
                secondIndex = second;
            }
        return [Clamp(fresh[firstIndex], width, height), Clamp(fresh[secondIndex], width, height)];
    }

    public static IReadOnlyList<PrivacyLabelMotion> UpdateMotion(
        IReadOnlyList<PrivacyLabelMotion> previous, IReadOnlyList<PrivacyPixelRect> merged,
        IReadOnlyList<PrivacyPixelRect> fresh, int width, int height, DateTime observedUtc)
    {
        var result = new List<PrivacyLabelMotion>(merged.Count);
        var used = new HashSet<int>();
        for (var index = 0; index < merged.Count; index++)
        {
            var current = merged[index];
            var nearest = Enumerable.Range(0, fresh.Count).Where(candidate => !used.Contains(candidate))
                .OrderBy(candidate => DistanceSquared(current, fresh[candidate])).DefaultIfEmpty(-1).First();
            var matching = nearest >= 0 && Math.Sqrt(DistanceSquared(current, fresh[nearest])) <=
                Math.Max(36, current.Height * 2d);
            var old = index < previous.Count ? previous[index] : default;
            if (!matching)
            {
                result.Add(old.ObservedUtc == default ? new(current, observedUtc, 0, 0) :
                    old with { Bounds = current });
                continue;
            }
            used.Add(nearest);
            if (old.ObservedUtc == default)
            {
                result.Add(new(current, observedUtc, 0, 0));
                continue;
            }
            var elapsedMs = (observedUtc - old.ObservedUtc).TotalMilliseconds;
            var dx = CenterX(current) - CenterX(old.Bounds);
            var dy = CenterY(current) - CenterY(old.Bounds);
            if (elapsedMs < 3 || elapsedMs > 250 || Math.Abs(dx) > width * 0.2 ||
                Math.Abs(dy) > height * 0.25)
            {
                result.Add(new(current, observedUtc, 0, 0));
                continue;
            }
            var vx = Math.Clamp(dx / elapsedMs, -4, 4);
            var vy = Math.Clamp(dy / elapsedMs, -4, 4);
            var velocityX = Math.Abs(dx) < 0.75 ? 0 : Math.Sign(vx) != Math.Sign(old.VelocityX)
                ? vx : vx * 0.88 + old.VelocityX * 0.12;
            var velocityY = Math.Abs(dy) < 0.75 ? 0 : Math.Sign(vy) != Math.Sign(old.VelocityY)
                ? vy : vy * 0.88 + old.VelocityY * 0.12;
            result.Add(new(current, observedUtc, velocityX, velocityY));
        }
        return result;
    }

    public static IReadOnlyList<PrivacyPixelRect> CalculateCoverage(
        IReadOnlyList<PrivacyLabelMotion> motion, int width, int height, DateTime nowUtc)
    {
        if (width <= 0 || height <= 0) return [];
        var result = new List<PrivacyPixelRect>(motion.Count);
        foreach (var item in motion)
        {
            if (!Valid(item.Bounds)) continue;
            var age = item.ObservedUtc == default ? TimeSpan.Zero : nowUtc - item.ObservedUtc;
            if (age < TimeSpan.Zero) age = TimeSpan.Zero;
            var horizon = Math.Min(120, age.TotalMilliseconds + 48);
            var dx = Math.Clamp(item.VelocityX * horizon, -width * 0.12, width * 0.12);
            var dy = Math.Clamp(item.VelocityY * horizon, -height * 0.16, height * 0.16);
            var predictedX = (int)Math.Round(item.Bounds.X + dx);
            var predictedY = (int)Math.Round(item.Bounds.Y + dy);
            var left = Math.Min(item.Bounds.X, predictedX);
            var top = Math.Min(item.Bounds.Y, predictedY);
            var right = Math.Max(item.Bounds.Right, predictedX + item.Bounds.Width);
            var bottom = Math.Max(item.Bounds.Bottom, predictedY + item.Bounds.Height);
            result.Add(Clamp(new PrivacyPixelRect(left, top, right - left, bottom - top), width, height));
        }
        return result;
    }

    private static bool Valid(PrivacyPixelRect bounds) => bounds.Width > 0 && bounds.Height > 0;
    private static double CenterX(PrivacyPixelRect bounds) => bounds.X + bounds.Width / 2d;
    private static double CenterY(PrivacyPixelRect bounds) => bounds.Y + bounds.Height / 2d;
    private static double DistanceSquared(PrivacyPixelRect a, PrivacyPixelRect b)
    {
        var dx = CenterX(a) - CenterX(b);
        var dy = CenterY(a) - CenterY(b);
        return dx * dx + dy * dy;
    }

    private static PrivacyPixelRect Clamp(PrivacyPixelRect bounds, int width, int height)
    {
        var boxWidth = Math.Clamp(bounds.Width, 1, width);
        var boxHeight = Math.Clamp(bounds.Height, 1, height);
        return new PrivacyPixelRect(Math.Clamp(bounds.X, 0, width - boxWidth),
            Math.Clamp(bounds.Y, 0, height - boxHeight), boxWidth, boxHeight);
    }
}
