namespace PartyPilot.Desktop;

// Screenshot geometry is a candidate until a real PoE2 capture confirms it.
// Every stage returns no point when the current frame lacks visual evidence.
internal static class PartyWarpVision
{
    internal sealed record PortraitRows(int FirstY, int SecondY);
    internal sealed record MenuAction(int X, int Y);

    internal static PortraitRows? FindTwoPortraits(byte[] pixels, int width, int height)
    {
        if (!Valid(pixels, width, height) || width < 320 || height < 480) return null;
        var scale = Math.Clamp(height / 1308d, 0.7, 1.35);
        var left = Math.Max(2, (int)Math.Round(4 * scale));
        var right = Math.Min(width - 2, (int)Math.Round(115 * scale));
        var top = Math.Max(35, (int)Math.Round(45 * scale));
        var bottom = Math.Min(height - 35, (int)Math.Round(780 * scale));
        if (right - left < 40 || bottom - top < 160) return null;
        var scores = new double[height];
        for (var y = top; y < bottom; y++)
        {
            var sum = 0d;
            var count = 0;
            for (var x = left + 2; x < right; x += 3)
            {
                var current = (y * width + x) * 4;
                var prior = current - 8;
                sum += Math.Abs(pixels[current] - pixels[prior]) +
                       Math.Abs(pixels[current + 1] - pixels[prior + 1]) +
                       Math.Abs(pixels[current + 2] - pixels[prior + 2]);
                count++;
            }
            scores[y] = count == 0 ? 0 : sum / count;
        }
        var radius = Math.Max(14, (int)Math.Round(25 * scale));
        var prefix = new double[height + 1];
        for (var y = 0; y < height; y++) prefix[y + 1] = prefix[y] + scores[y];
        var candidates = new List<(int Y, double Score)>();
        for (var y = top + radius; y < bottom - radius; y++)
        {
            var score = (prefix[y + radius + 1] - prefix[y - radius]) / (radius * 2 + 1);
            if (score >= 28) candidates.Add((y, score));
        }
        var separation = Math.Max(55, (int)Math.Round(65 * scale));
        var peaks = new List<(int Y, double Score)>();
        foreach (var candidate in candidates.OrderByDescending(item => item.Score))
        {
            if (peaks.All(item => Math.Abs(item.Y - candidate.Y) >= separation))
                peaks.Add(candidate);
            if (peaks.Count == 8) break;
        }
        var minimumGap = (int)Math.Round(75 * scale);
        var maximumGap = (int)Math.Round(170 * scale);
        var pairs = from first in peaks
            from second in peaks
            where second.Y > first.Y && second.Y - first.Y >= minimumGap &&
                  second.Y - first.Y <= maximumGap
            orderby first.Y, second.Y
            select new PortraitRows(first.Y, second.Y);
        return pairs.FirstOrDefault();
    }

    internal static MenuAction? FindFirstAction(byte[] before, byte[] after,
        int width, int height)
    {
        if (!Valid(before, width, height) || !Valid(after, width, height) ||
            width < 320 || height < 480) return null;
        var scale = Math.Clamp(height / 1308d, 0.7, 1.35);
        var left = Math.Min(width - 1, (int)Math.Round(70 * scale));
        var right = Math.Min(width, (int)Math.Round(550 * scale));
        var top = Math.Max(0, (int)Math.Round(25 * scale));
        var bottom = Math.Min(height, (int)Math.Round(850 * scale));
        if (right - left < 180 || bottom - top < 220) return null;
        var changed = new bool[height];
        for (var y = top; y < bottom; y++)
        {
            var hits = 0;
            var count = 0;
            for (var x = left; x < right; x += 4)
            {
                var i = (y * width + x) * 4;
                var delta = Math.Abs(before[i] - after[i]) +
                            Math.Abs(before[i + 1] - after[i + 1]) +
                            Math.Abs(before[i + 2] - after[i + 2]);
                if (delta > 48) hits++;
                count++;
            }
            changed[y] = count > 0 && hits >= count * 0.17;
        }
        var regionTop = -1;
        var regionBottom = -1;
        var runStart = -1;
        var lastHit = -1;
        for (var y = top; y < bottom; y++)
        {
            if (changed[y])
            {
                if (runStart < 0) runStart = y;
                lastHit = y;
            }
            if (runStart >= 0 && (y - lastHit > 4 || y == bottom - 1))
            {
                if (lastHit - runStart >= 125 * scale &&
                    (regionTop < 0 || runStart < regionTop))
                {
                    regionTop = runStart;
                    regionBottom = lastHit;
                }
                runStart = -1;
            }
        }
        if (regionTop < 0) return null;
        var borderRows = new List<int>();
        var searchLeft = Math.Min(right - 60, left + (int)Math.Round(20 * scale));
        var searchRight = right - (int)Math.Round(15 * scale);
        for (var y = regionTop + (int)Math.Round(70 * scale); y <= regionBottom; y++)
        {
            var gold = 0;
            var count = 0;
            for (var x = searchLeft; x < searchRight; x += 3)
            {
                var i = (y * width + x) * 4;
                var blue = after[i];
                var green = after[i + 1];
                var red = after[i + 2];
                if (red >= 75 && green >= 55 && red >= blue + 15 && green >= blue + 4)
                    gold++;
                count++;
            }
            if (count > 0 && gold >= count * 0.55 &&
                (borderRows.Count == 0 || y - borderRows[^1] > 3)) borderRows.Add(y);
        }
        for (var index = 0; index + 1 < borderRows.Count; index++)
        {
            var gap = borderRows[index + 1] - borderRows[index];
            if (gap >= 25 * scale && gap <= 105 * scale)
                return new MenuAction((searchLeft + searchRight) / 2,
                    (borderRows[index] + borderRows[index + 1]) / 2);
        }
        return null;
    }

    private static bool Valid(byte[]? pixels, int width, int height) =>
        pixels is not null && width > 0 && height > 0 &&
        (long)width * height * 4 <= pixels.LongLength;
}
