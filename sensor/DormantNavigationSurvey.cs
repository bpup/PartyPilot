namespace PartyPilot.Sensor;

// Surveys a bounded slice each time the same, locally bound sleeping map is read.
// Every retained candidate is re-read while present in that map; an identity
// change or failed map lookup must call Reset before any old result is sent.
internal sealed class DormantNavigationSurvey
{
    internal sealed record Candidate(uint EntityId, string Category, string Path, string? Label,
        float X, float Y, float Z);
    internal sealed record Result(Candidate[] NavigationCandidates, int BatchScanned,
        int TotalEntities, bool SweepCompleted, int RememberedCount, bool OutputTruncated);

    private const int BatchSize = 256;
    private const int MaxRemembered = 256;
    private const int MaxOutput = 24;
    private readonly Dictionary<uint, (long Address, Candidate Marker)> _remembered = new();
    private string? _identity;
    private int _nextIndex;

    internal Result Capture(string identity, AreaEntityMapSnapshotReader.Entry[] entries,
        Func<long, string?> readPath,
        Func<long, RawRenderPositionReader.Position?> readPosition,
        float playerX, float playerY)
    {
        if (string.IsNullOrEmpty(identity) || !float.IsFinite(playerX) || !float.IsFinite(playerY))
        {
            Reset();
            return new([], 0, 0, false, 0, false);
        }
        if (_identity != identity)
        {
            Reset();
            _identity = identity;
        }
        var current = entries.ToDictionary(entry => entry.Id, entry => entry.Address);
        foreach (var (id, remembered) in _remembered.ToArray())
        {
            if (!current.TryGetValue(id, out var address) || address != remembered.Address)
            {
                _remembered.Remove(id);
                continue;
            }
            var refreshed = Read(id, address, readPath, readPosition);
            if (refreshed == null) _remembered.Remove(id);
            else _remembered[id] = (address, refreshed);
        }
        if (_nextIndex >= entries.Length) _nextIndex = 0;
        var start = _nextIndex;
        var scanned = Math.Min(BatchSize, entries.Length - start);
        for (var index = start; index < start + scanned; index++)
        {
            var entry = entries[index];
            if (_remembered.ContainsKey(entry.Id)) continue;
            var candidate = Read(entry.Id, entry.Address, readPath, readPosition);
            if (candidate != null) _remembered[entry.Id] = (entry.Address, candidate);
        }
        _nextIndex += scanned;
        var completed = entries.Length > 0 && _nextIndex == entries.Length;
        if (completed) _nextIndex = 0;
        var ranked = _remembered.Values.Select(value => value.Marker)
            .OrderBy(candidate => Priority(candidate.Category))
            .ThenBy(candidate => Distance(candidate, playerX, playerY))
            .ThenBy(candidate => candidate.EntityId).ToArray();
        foreach (var extra in ranked.Skip(MaxRemembered)) _remembered.Remove(extra.EntityId);
        return new(ranked.Take(MaxOutput).ToArray(), scanned, entries.Length,
            completed, _remembered.Count, ranked.Length > MaxOutput);
    }

    internal void Reset()
    {
        _identity = null;
        _nextIndex = 0;
        _remembered.Clear();
    }

    private static Candidate? Read(uint id, long address,
        Func<long, string?> readPath,
        Func<long, RawRenderPositionReader.Position?> readPosition)
    {
        try
        {
            var path = readPath(address);
            if (path == null || path.Length > 256) return null;
            if (path.StartsWith("Metadata/Effects/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("Metadata/Projectiles/", StringComparison.OrdinalIgnoreCase)) return null;
            var dormantQuestLabel = RadarClassifier.DormantQuestLabel(path);
            var category = dormantQuestLabel != null ? "quest" : RadarClassifier.Category(path);
            if (category == null || !RadarClassifier.IsNavigation(category)) return null;
            var position = readPosition(address);
            if (position == null || !float.IsFinite(position.WorldX) ||
                !float.IsFinite(position.WorldY) || !float.IsFinite(position.WorldZ)) return null;
            var label = category == "quest" ? dormantQuestLabel :
                category == "boss" ? RadarClassifier.BossLabel(path) : null;
            if (category is "quest" or "boss" && string.IsNullOrWhiteSpace(label)) return null;
            return new(id, category, path, label,
                position.WorldX, position.WorldY, position.WorldZ);
        }
        catch { return null; }
    }

    private static int Priority(string category) => category switch
    {
        "quest" => 0,
        "waypoint" => 1,
        "checkpoint" => 2,
        "transition" => 3,
        "essence" => 4,
        "boss" => 5,
        _ => 6
    };

    private static double Distance(Candidate marker, float x, float y)
    {
        var dx = (double)marker.X - x;
        var dy = (double)marker.Y - y;
        return dx * dx + dy * dy;
    }
}
