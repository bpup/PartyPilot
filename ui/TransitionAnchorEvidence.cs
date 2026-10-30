namespace PartyPilot.Desktop;

internal sealed record TransitionAnchorMatch(int SourceClient, int DestinationClient,
    string Identity, double DestinationX, double DestinationY, double PlayerX, double PlayerY,
    int DestinationProcessId, uint DestinationAreaHash,
    DateTime DestinationSeenUtc, DateTime SourceChangedUtc,
    IReadOnlyList<TransitionPoint>? Approach = null);

internal readonly record struct TransitionPoint(double X, double Y);

// Read-only evidence from visible radar entities. A matching path and label
// does not establish that the object is currently interactable.
internal sealed class TransitionAnchorEvidence
{
    private const double WorldPerGridUnit = RadarProjection.WorldUnitsPerGridUnit;
    private readonly object _gate = new();
    private readonly Dictionary<int, RadarFrame> _latest = new();
    private readonly Dictionary<int, (string Identity, DateTime ChangedUtc,
        TransitionPoint SourceAnchor, TransitionPoint[] Approach)> _used = new();
    private readonly Dictionary<int, (DateTime ChangedUtc, int DriverPlayer)> _drivers = new();
    private readonly Dictionary<int, (int ProcessId, uint AreaHash,
        List<TransitionPoint> Points)> _trails = new();
    private readonly Dictionary<int, (DateTime ChangedUtc, int DriverPlayer, int Index)> _replay = new();
    private readonly Dictionary<int, (DateTime ChangedUtc, int DriverPlayer,
        TransitionPoint Target, double BestDistance, DateTime ProgressUtc, bool Blocked)> _progress = new();

    internal void Observe(int client, RadarFrame frame)
    {
        if (client is < 1 or > 3 || frame.AreaHash == 0) return;
        lock (_gate)
        {
            if (_latest.TryGetValue(client, out var previous))
            {
                if (frame.SeenUtc <= previous.SeenUtc)
                    return;
                if (frame.ProcessId != previous.ProcessId)
                {
                    _used.Remove(client);
                    _trails.Remove(client);
                    _drivers.Clear();
                    _replay.Clear();
                    _progress.Clear();
                }
                if (frame.AreaHash != previous.AreaHash)
                {
                    _used.Remove(client);
                    _drivers.Clear();
                    _replay.Clear();
                    _progress.Clear();
                    if (frame.ProcessId == previous.ProcessId &&
                        frame.SeenUtc - previous.SeenUtc <= TimeSpan.FromSeconds(2) &&
                        NearestVisibleAnchor(previous) is { } anchor)
                        _used[client] = (Identity(anchor), frame.SeenUtc,
                            new TransitionPoint(anchor.X, anchor.Y),
                            CaptureApproach(client, previous, anchor));
                }
            }
            CapturePosition(client, frame);
            _latest[client] = frame;
        }
    }

    internal TransitionPoint? ReplayTarget(TransitionAnchorMatch match,
        int driverPlayer, double x, double y)
    {
        if (match.Approach is not { Count: >= 2 } points ||
            !double.IsFinite(x) || !double.IsFinite(y)) return null;
        lock (_gate)
        {
            var start = _replay.TryGetValue(match.DestinationClient, out var progress) &&
                progress.ChangedUtc == match.SourceChangedUtc &&
                progress.DriverPlayer == driverPlayer ? progress.Index : 0;
            var nearest = -1;
            var distance = double.PositiveInfinity;
            for (var i = start; i < points.Count; i++)
            {
                var candidate = DistanceSquared(x, y, points[i].X, points[i].Y);
                if (candidate < distance)
                {
                    nearest = i;
                    distance = candidate;
                }
            }
            if (nearest < 0 || distance > Math.Pow(12 * WorldPerGridUnit, 2)) return null;
            var index = Math.Max(start, nearest);
            _replay[match.DestinationClient] = (match.SourceChangedUtc, driverPlayer, index);
            for (var i = index + 1; i < points.Count; i++)
                if (DistanceSquared(x, y, points[i].X, points[i].Y) >=
                    Math.Pow(6 * WorldPerGridUnit, 2)) return points[i];
            return points[^1];
        }
    }

    internal void ResetMovement()
    {
        lock (_gate)
        {
            _replay.Clear();
            _progress.Clear();
        }
    }

    internal bool AllowMovement(TransitionAnchorMatch match, int driverPlayer,
        TransitionPoint current, TransitionPoint target, DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc || !double.IsFinite(current.X) ||
            !double.IsFinite(current.Y) || !double.IsFinite(target.X) ||
            !double.IsFinite(target.Y)) return false;
        var distance = Math.Sqrt(DistanceSquared(current.X, current.Y,
            target.X, target.Y)) / WorldPerGridUnit;
        lock (_gate)
        {
            if (!_progress.TryGetValue(match.DestinationClient, out var state) ||
                state.ChangedUtc != match.SourceChangedUtc ||
                state.DriverPlayer != driverPlayer ||
                Math.Sqrt(DistanceSquared(target.X, target.Y,
                    state.Target.X, state.Target.Y)) > WorldPerGridUnit ||
                nowUtc < state.ProgressUtc)
            {
                _progress[match.DestinationClient] = (match.SourceChangedUtc,
                    driverPlayer, target, distance, nowUtc, false);
                return true;
            }
            if (state.Blocked) return false;
            if (distance <= state.BestDistance - 0.75)
            {
                _progress[match.DestinationClient] = (state.ChangedUtc,
                    state.DriverPlayer, state.Target, distance, nowUtc, false);
                return true;
            }
            if (nowUtc - state.ProgressUtc < TimeSpan.FromSeconds(3)) return true;
            _progress[match.DestinationClient] = (state.ChangedUtc,
                state.DriverPlayer, state.Target, state.BestDistance,
                state.ProgressUtc, true);
            return false;
        }
    }

    internal int SelectDriver(TransitionAnchorMatch match, int firstPlayer,
        double firstDistance, int secondPlayer, double secondDistance)
    {
        lock (_gate)
        {
            if (_drivers.TryGetValue(match.DestinationClient, out var selected) &&
                selected.ChangedUtc == match.SourceChangedUtc &&
                (selected.DriverPlayer == firstPlayer || selected.DriverPlayer == secondPlayer))
                return selected.DriverPlayer;
            var driver = secondDistance < firstDistance ? secondPlayer : firstPlayer;
            _drivers[match.DestinationClient] = (match.SourceChangedUtc, driver);
            return driver;
        }
    }

    internal TransitionAnchorMatch? Match(FollowHandoffRequest request,
        PartyTopology topology, DateTime nowUtc)
    {
        var source = topology.Slots.FirstOrDefault(slot =>
            slot.PartyPlayer == request.TargetPlayer)?.Client ?? 0;
        return Match(source, request.DestinationClient, nowUtc);
    }

    internal TransitionAnchorMatch? Match(int source, int destinationClient, DateTime nowUtc)
    {
        if (source is < 1 or > 3 || destinationClient is < 1 or > 3 ||
            source == destinationClient ||
            nowUtc.Kind != DateTimeKind.Utc) return null;
        lock (_gate)
        {
            if (!_used.TryGetValue(source, out var used) ||
                nowUtc < used.ChangedUtc || nowUtc - used.ChangedUtc > TimeSpan.FromSeconds(12) ||
                !_latest.TryGetValue(destinationClient, out var destination) ||
                nowUtc < destination.SeenUtc ||
                nowUtc - destination.SeenUtc > TimeSpan.FromSeconds(2)) return null;
            var matches = destination.Markers.Where(marker =>
                IsAnchor(marker) && string.Equals(Identity(marker), used.Identity,
                    StringComparison.OrdinalIgnoreCase)).OrderBy(marker =>
                DistanceSquared(marker.X, marker.Y, destination.PlayerX, destination.PlayerY))
                .ToArray();
            // A path and label may identify several entrances in one area. Their
            // IDs are local to each client, so proximity cannot prove which one
            // corresponds to the entrance the leader used.
            if (matches.Length != 1) return null;
            var nearest = matches[0];
            var approach = used.Approach.Select(point => new TransitionPoint(
                nearest.X + point.X - used.SourceAnchor.X,
                nearest.Y + point.Y - used.SourceAnchor.Y)).ToArray();
            return new TransitionAnchorMatch(source, destinationClient,
                used.Identity, nearest.X, nearest.Y, destination.PlayerX, destination.PlayerY,
                destination.ProcessId, destination.AreaHash,
                destination.SeenUtc, used.ChangedUtc, approach);
        }
    }

    private void CapturePosition(int client, RadarFrame frame)
    {
        if (!double.IsFinite(frame.PlayerX) || !double.IsFinite(frame.PlayerY)) return;
        if (!_trails.TryGetValue(client, out var trail) || trail.ProcessId != frame.ProcessId ||
            trail.AreaHash != frame.AreaHash)
        {
            _trails[client] = (frame.ProcessId, frame.AreaHash,
                [new TransitionPoint(frame.PlayerX, frame.PlayerY)]);
            return;
        }
        var points = trail.Points;
        var previous = points[^1];
        var distance = Math.Sqrt(DistanceSquared(frame.PlayerX, frame.PlayerY,
            previous.X, previous.Y)) / WorldPerGridUnit;
        if (distance >= 40)
        {
            points.Clear();
            points.Add(new TransitionPoint(frame.PlayerX, frame.PlayerY));
        }
        else if (distance >= 1.5)
            points.Add(new TransitionPoint(frame.PlayerX, frame.PlayerY));
        if (points.Count > 256) points.RemoveRange(0, points.Count - 256);
        var length = 0d;
        for (var i = points.Count - 1; i > 0; i--)
        {
            length += Math.Sqrt(DistanceSquared(points[i].X, points[i].Y,
                points[i - 1].X, points[i - 1].Y)) / WorldPerGridUnit;
            if (length > 200)
            {
                points.RemoveRange(0, i);
                break;
            }
        }
    }

    private TransitionPoint[] CaptureApproach(int client, RadarFrame source,
        RadarMarker anchor)
    {
        if (!_trails.TryGetValue(client, out var trail) ||
            trail.ProcessId != source.ProcessId || trail.AreaHash != source.AreaHash ||
            Math.Sqrt(DistanceSquared(source.PlayerX, source.PlayerY,
                anchor.X, anchor.Y)) > 7 * WorldPerGridUnit)
            return [];
        var reverse = new List<TransitionPoint> { new(anchor.X, anchor.Y),
            new(source.PlayerX, source.PlayerY) };
        var remaining = 90d;
        for (var i = trail.Points.Count - 1; i >= 0 && remaining > 0; i--)
        {
            var last = reverse[^1];
            var point = trail.Points[i];
            var distance = Math.Sqrt(DistanceSquared(last.X, last.Y,
                point.X, point.Y)) / WorldPerGridUnit;
            if (distance < 0.25) continue;
            if (distance > remaining)
            {
                var ratio = remaining / distance;
                reverse.Add(new TransitionPoint(last.X + (point.X - last.X) * ratio,
                    last.Y + (point.Y - last.Y) * ratio));
                break;
            }
            reverse.Add(point);
            remaining -= distance;
        }
        reverse.Reverse();
        return reverse.Count >= 3 ? reverse.ToArray() : [];
    }

    private static RadarMarker? NearestVisibleAnchor(RadarFrame frame)
    {
        var candidates = frame.Markers.Where(IsAnchor).Select(marker => new
        {
            Marker = marker,
            Distance = DistanceSquared(marker.X, marker.Y, frame.PlayerX, frame.PlayerY)
        }).Where(item => item.Distance <= Math.Pow(28 * WorldPerGridUnit, 2))
        .OrderBy(item => item.Distance).Select(item => item.Marker).ToArray();
        return candidates.Length > 0 &&
            candidates.Count(marker => string.Equals(Identity(marker),
                Identity(candidates[0]), StringComparison.OrdinalIgnoreCase)) == 1
            ? candidates[0] : null;
    }

    private static bool IsAnchor(RadarMarker marker) =>
        marker.Category is "transition" or "waypoint" or "checkpoint" &&
        marker.NavigationIsValid == true && marker.NavigationIsTargetable == true &&
        marker.Path.StartsWith("Metadata/", StringComparison.OrdinalIgnoreCase);

    private static string Identity(RadarMarker marker) =>
        $"{marker.Category}|{marker.Path}|{marker.Label ?? string.Empty}";

    private static double DistanceSquared(double x, double y, double otherX, double otherY) =>
        (x - otherX) * (x - otherX) + (y - otherY) * (y - otherY);
}
