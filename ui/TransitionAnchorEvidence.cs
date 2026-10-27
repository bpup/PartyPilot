namespace PartyPilot.Desktop;

internal sealed record TransitionAnchorMatch(int SourceClient, int DestinationClient,
    string Identity, double DestinationX, double DestinationY, DateTime SourceChangedUtc);

// Read-only evidence from visible radar entities. A matching path and label
// does not establish that the object is currently interactable.
internal sealed class TransitionAnchorEvidence
{
    private const double WorldPerGridUnit = 10.869565217391305;
    private readonly object _gate = new();
    private readonly Dictionary<int, RadarFrame> _latest = new();
    private readonly Dictionary<int, (string Identity, DateTime ChangedUtc)> _used = new();

    internal void Observe(int client, RadarFrame frame)
    {
        if (client is < 1 or > 3 || frame.AreaHash == 0) return;
        lock (_gate)
        {
            if (_latest.TryGetValue(client, out var previous))
            {
                if (frame.ProcessId != previous.ProcessId || frame.SeenUtc <= previous.SeenUtc)
                    return;
                if (frame.AreaHash != previous.AreaHash)
                {
                    _used.Remove(client);
                    if (frame.SeenUtc - previous.SeenUtc <= TimeSpan.FromSeconds(2) &&
                        NearestVisibleAnchor(previous) is { } anchor)
                        _used[client] = (Identity(anchor), frame.SeenUtc);
                }
            }
            _latest[client] = frame;
        }
    }

    internal TransitionAnchorMatch? Match(FollowHandoffRequest request,
        PartyTopology topology, DateTime nowUtc)
    {
        var source = topology.Slots.FirstOrDefault(slot =>
            slot.PartyPlayer == request.TargetPlayer)?.Client ?? 0;
        if (source == 0 || source == request.DestinationClient ||
            nowUtc.Kind != DateTimeKind.Utc) return null;
        lock (_gate)
        {
            if (!_used.TryGetValue(source, out var used) ||
                nowUtc < used.ChangedUtc || nowUtc - used.ChangedUtc > TimeSpan.FromSeconds(12) ||
                !_latest.TryGetValue(request.DestinationClient, out var destination) ||
                nowUtc < destination.SeenUtc ||
                nowUtc - destination.SeenUtc > TimeSpan.FromSeconds(2)) return null;
            var matches = destination.Markers.Where(marker =>
                IsAnchor(marker) && string.Equals(Identity(marker), used.Identity,
                    StringComparison.OrdinalIgnoreCase)).OrderBy(marker =>
                DistanceSquared(marker.X, marker.Y, destination.PlayerX, destination.PlayerY))
                .ToArray();
            if (matches.Length == 0) return null;
            var nearest = matches[0];
            return new TransitionAnchorMatch(source, request.DestinationClient,
                used.Identity, nearest.X, nearest.Y, used.ChangedUtc);
        }
    }

    private static RadarMarker? NearestVisibleAnchor(RadarFrame frame) =>
        frame.Markers.Where(IsAnchor).Select(marker => new
        {
            Marker = marker,
            Distance = DistanceSquared(marker.X, marker.Y, frame.PlayerX, frame.PlayerY)
        }).Where(item => item.Distance <= Math.Pow(28 * WorldPerGridUnit, 2))
        .OrderBy(item => item.Distance).Select(item => item.Marker).FirstOrDefault();

    private static bool IsAnchor(RadarMarker marker) =>
        marker.Category is "transition" or "waypoint" or "checkpoint" &&
        marker.NavigationIsValid == true && marker.NavigationIsTargetable == true &&
        marker.Path.StartsWith("Metadata/", StringComparison.OrdinalIgnoreCase);

    private static string Identity(RadarMarker marker) =>
        $"{marker.Category}|{marker.Path}|{marker.Label ?? string.Empty}";

    private static double DistanceSquared(double x, double y, double otherX, double otherY) =>
        (x - otherX) * (x - otherX) + (y - otherY) * (y - otherY);
}
