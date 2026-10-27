using System.Globalization;
using System.Text.Json;

namespace PartyPilot.Desktop;

// Looks only in the sensor's diagnostic directory. Metadata is a cheap filter;
// TryLoad still verifies the complete packed file before any pixels are used.
public sealed class RadarTerrainCatalog
{
    private readonly string _directory;
    private readonly Dictionary<int, RadarTerrainCapture> _current = new();
    private readonly Dictionary<int, DateTime> _nextScanUtc = new();
    private readonly Dictionary<int, int> _scanCursor = new();
    private readonly Dictionary<int, (uint Hash, string? Area, string? StateArea)> _scanIdentity = new();

    public RadarTerrainCatalog(string directory)
    {
        if (!Path.IsPathFullyQualified(directory))
            throw new ArgumentException("Terrain directory must be absolute.", nameof(directory));
        _directory = directory;
    }

    public static string SensorDirectory => Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "PartyPilot", "terrain");

    public RadarTerrainCapture? FindMatching(RadarFrame frame, DateTime nowUtc)
    {
        var identity = (frame.AreaHash, frame.AreaAddress, frame.StateAreaAddress);
        if (!_scanIdentity.TryGetValue(frame.ProcessId, out var previousIdentity) ||
            previousIdentity != identity)
        {
            _scanIdentity[frame.ProcessId] = identity;
            _scanCursor[frame.ProcessId] = 0;
        }
        if (_current.TryGetValue(frame.ProcessId, out var current) &&
            current.Matches(frame) && current.MetadataUnchanged())
            return current;
        _current.Remove(frame.ProcessId);
        if (_nextScanUtc.TryGetValue(frame.ProcessId, out var next) && nowUtc < next)
            return null;
        _nextScanUtc[frame.ProcessId] = nowUtc.AddSeconds(2);
        if (frame.AreaHash == 0 || frame.GridX is null || frame.GridY is null ||
            !Directory.Exists(_directory)) return null;
        try
        {
            var files = Directory.EnumerateFiles(_directory,
                $"terrain-{frame.ProcessId}-*.json", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .Where(file => file.Length is >= 1 and <= 65536)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Name, StringComparer.Ordinal)
                .ToArray();
            var cursor = _scanCursor[frame.ProcessId];
            if (cursor >= files.Length) cursor = 0;
            foreach (var path in files.Skip(cursor).Take(128))
            {
                if (!MetadataMatches(path.FullName, frame) ||
                    !RadarTerrainCapture.TryLoad(path.FullName, out var capture) ||
                    capture is null || !capture.Matches(frame)) continue;
                _current[frame.ProcessId] = capture;
                _scanCursor[frame.ProcessId] = 0;
                return capture;
            }
            _scanCursor[frame.ProcessId] = cursor + 128 >= files.Length ? 0 : cursor + 128;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    private static bool MetadataMatches(string path, RadarFrame frame)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            if (!root.TryGetProperty("ProcessId", out var pid) ||
                !pid.TryGetInt32(out var processId) || processId != frame.ProcessId ||
                !root.TryGetProperty("AreaHash", out var hash) ||
                !uint.TryParse(hash.GetString(), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var areaHash) || areaHash != frame.AreaHash ||
                !root.TryGetProperty("Anchor", out var anchor) ||
                !root.TryGetProperty("AreaAddress", out var address) ||
                !root.TryGetProperty("SeenUtc", out var seen) ||
                !seen.TryGetDateTime(out var seenUtc) || seenUtc > frame.SeenUtc)
                return false;
            var liveAddress = anchor.GetString() == "ingame_data_address"
                ? frame.AreaAddress : frame.StateAreaAddress;
            return string.Equals(address.GetString(), liveAddress,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }
}
