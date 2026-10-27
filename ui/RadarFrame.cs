using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PartyPilot.Desktop;

public sealed record RadarMarker(uint Id, string Category, string Path, string? Label,
    string? ItemPath, int? Stack, string? Rarity, double X, double Y);

public sealed record RadarFrame(int ProcessId, DateTime SeenUtc, uint AreaHash, string? AreaAddress,
    double PlayerX, double PlayerY,
    IReadOnlyList<RadarMarker> Markers)
{
    public string? StateAreaAddress { get; init; }
    public double? GridX { get; init; }
    public double? GridY { get; init; }
}

public static class RadarFrames
{
    public static bool TryParse(ReadOnlySpan<byte> json, int expectedPid, DateTime nowUtc,
        out RadarFrame? frame)
    {
        frame = null;
        try
        {
            using var document = JsonDocument.Parse(json.ToArray());
            var root = document.RootElement;
            if (!root.TryGetProperty("ProcessId", out var pidValue) || !pidValue.TryGetInt32(out var pid) ||
                pid != expectedPid ||
                !root.TryGetProperty("RadarStatus", out var status) || status.GetString() != "ok" ||
                !root.TryGetProperty("RadarSeenUtc", out var seenValue) ||
                !seenValue.TryGetDateTime(out var seenUtc) ||
                seenUtc.Kind != DateTimeKind.Utc ||
                nowUtc - seenUtc < TimeSpan.Zero || nowUtc - seenUtc > TimeSpan.FromSeconds(2) ||
                !root.TryGetProperty("AreaHash", out var areaValue) || !areaValue.TryGetUInt32(out var areaHash) ||
                !root.TryGetProperty("RadarAreaAddress", out var addressValue) ||
                addressValue.ValueKind is not (JsonValueKind.String or JsonValueKind.Null) ||
                (addressValue.ValueKind == JsonValueKind.String &&
                 (addressValue.GetString() is not { } address || !ValidAddress(address))) ||
                !TryFinite(root, "X", out var playerX) || !TryFinite(root, "Y", out var playerY) ||
                !root.TryGetProperty("RadarEntities", out var entities) ||
                entities.ValueKind != JsonValueKind.Array || entities.GetArrayLength() > 96)
                return false;
            var markers = new List<RadarMarker>(entities.GetArrayLength());
            string? stateAreaAddress = null;
            if (root.TryGetProperty("RadarStateAreaAddress", out var stateAreaValue) &&
                stateAreaValue.ValueKind != JsonValueKind.Null)
            {
                if (stateAreaValue.ValueKind != JsonValueKind.String ||
                    stateAreaValue.GetString() is not { } parsedStateAddress || !ValidAddress(parsedStateAddress)) return false;
                stateAreaAddress = parsedStateAddress;
            }
            double? gridX = null;
            double? gridY = null;
            if (root.TryGetProperty("RadarGridX", out var gridXValue) &&
                gridXValue.ValueKind != JsonValueKind.Null)
            {
                if (!gridXValue.TryGetDouble(out var value) || !double.IsFinite(value)) return false;
                gridX = value;
            }
            if (root.TryGetProperty("RadarGridY", out var gridYValue) &&
                gridYValue.ValueKind != JsonValueKind.Null)
            {
                if (!gridYValue.TryGetDouble(out var value) || !double.IsFinite(value)) return false;
                gridY = value;
            }
            if (gridX.HasValue != gridY.HasValue) return false;
            var ids = new HashSet<uint>();
            foreach (var item in entities.EnumerateArray())
            {
                if (!item.TryGetProperty("EntityId", out var idValue) || !idValue.TryGetUInt32(out var id) ||
                    id == 0 || !ids.Add(id) ||
                    !item.TryGetProperty("Category", out var categoryValue) ||
                    categoryValue.GetString() is not string category || !KnownCategory(category) ||
                    !item.TryGetProperty("Path", out var pathValue) ||
                    pathValue.GetString() is not string path ||
                    Encoding.UTF8.GetByteCount(path) > 256 ||
                    !path.StartsWith("Metadata/", StringComparison.Ordinal) ||
                    !TryFinite(item, "X", out var x) || !TryFinite(item, "Y", out var y))
                    return false;
                string? label = null;
                if (item.TryGetProperty("Label", out var labelValue) && labelValue.ValueKind != JsonValueKind.Null)
                {
                    if (labelValue.ValueKind != JsonValueKind.String) return false;
                    label = labelValue.GetString();
                }
                string? itemPath = null;
                if (item.TryGetProperty("ItemPath", out var itemPathValue) && itemPathValue.ValueKind != JsonValueKind.Null)
                {
                    if (itemPathValue.ValueKind != JsonValueKind.String) return false;
                    itemPath = itemPathValue.GetString();
                }
                int? stack = null;
                if (item.TryGetProperty("Stack", out var stackValue) && stackValue.ValueKind != JsonValueKind.Null)
                {
                    if (!stackValue.TryGetInt32(out var parsedStack)) return false;
                    stack = parsedStack;
                }
                string? rarity = null;
                if (item.TryGetProperty("Rarity", out var rarityValue) && rarityValue.ValueKind != JsonValueKind.Null)
                {
                    if (rarityValue.ValueKind != JsonValueKind.String) return false;
                    rarity = rarityValue.GetString();
                }
                if (category == "player" &&
                    (!path.StartsWith("Metadata/Characters/", StringComparison.OrdinalIgnoreCase) ||
                     string.IsNullOrWhiteSpace(label) || Encoding.UTF8.GetByteCount(label) > 64 ||
                     itemPath != null || stack != null || rarity != null))
                    return false;
                if (category == "loot" &&
                    (!WorldItemWrapperPath(path) || string.IsNullOrWhiteSpace(label) ||
                     Encoding.UTF8.GetByteCount(label) > 96 ||
                     itemPath?.StartsWith("Metadata/Items/", StringComparison.OrdinalIgnoreCase) != true ||
                     Encoding.UTF8.GetByteCount(itemPath) > 256 ||
                     stack is < 1 or > 999_999 ||
                     (rarity != null && rarity is not ("normal" or "magic" or "rare" or "unique")))) return false;
                if (category == "boss" &&
                    (!path.Equals("Metadata/Monsters/HyenaMonster/Objects/BossRoomMinimapIcon",
                        StringComparison.OrdinalIgnoreCase) || label != "Rathbreaker" ||
                     itemPath != null || stack != null || rarity != null)) return false;
                if (category == "quest" &&
                    (string.IsNullOrWhiteSpace(label) || Encoding.UTF8.GetByteCount(label) > 96 ||
                     itemPath != null || stack != null || rarity != null)) return false;
                if (category is not ("player" or "loot" or "quest" or "boss") &&
                    (label != null || itemPath != null || stack != null || rarity != null)) return false;
                markers.Add(new RadarMarker(id, category, path, label, itemPath, stack, rarity, x, y));
            }
            frame = new RadarFrame(pid, seenUtc, areaHash, addressValue.GetString(), playerX, playerY, markers)
            { StateAreaAddress = stateAreaAddress, GridX = gridX, GridY = gridY };
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    private static bool TryFinite(JsonElement element, string name, out double value)
    {
        value = 0;
        return element.TryGetProperty(name, out var field) && field.TryGetDouble(out value) && double.IsFinite(value);
    }

    private static bool ValidAddress(string value) => value.Length is >= 4 and <= 18 &&
        value.StartsWith("0x", StringComparison.Ordinal) &&
        value.AsSpan(2).IndexOfAnyExcept("0123456789ABCDEFabcdef") < 0 &&
        ulong.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var address) &&
        address > 0;

    private static bool KnownCategory(string value) => value is
        "monster" or "checkpoint" or "waypoint" or "portal" or "transition" or "npc" or "chest" or "shrine" or "player" or "league" or "loot" or "quest" or "boss";

    private static bool WorldItemWrapperPath(string path)
    {
        var name = path.TrimEnd('/').Split('/').Last();
        var suffix = name.IndexOf('@');
        if (suffix >= 0) name = name[..suffix];
        return name.Equals("WorldItem", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class RadarLandmarkCache
{
    private const int MaximumLandmarks = 256;
    private readonly Dictionary<uint, RadarMarker> _landmarks = new();
    private uint _areaHash;
    private string? _areaAddress;
    private DateTime _lastSeenUtc;

    public bool TryApply(RadarFrame frame, out RadarFrame? enriched)
    {
        enriched = null;
        if (_lastSeenUtc != default && frame.SeenUtc <= _lastSeenUtc)
            return false;
        enriched = Apply(frame);
        return true;
    }

    public RadarFrame Apply(RadarFrame frame)
    {
        if (_lastSeenUtc != default && frame.SeenUtc < _lastSeenUtc)
            return frame with { Markers = [] };
        if (frame.AreaHash == 0 || frame.AreaAddress == null)
        {
            _landmarks.Clear();
            _areaHash = 0;
            _areaAddress = null;
            _lastSeenUtc = frame.SeenUtc;
            return frame;
        }
        if (_areaHash != frame.AreaHash || !string.Equals(_areaAddress, frame.AreaAddress, StringComparison.Ordinal))
        {
            _landmarks.Clear();
            _areaHash = frame.AreaHash;
            _areaAddress = frame.AreaAddress;
        }
        _lastSeenUtc = frame.SeenUtc;
        foreach (var marker in frame.Markers)
        {
            if (marker.Category is not ("waypoint" or "checkpoint" or "transition"))
            {
                _landmarks.Remove(marker.Id);
                continue;
            }
            if (!_landmarks.ContainsKey(marker.Id) && _landmarks.Count >= MaximumLandmarks)
                _landmarks.Remove(_landmarks.Keys.First());
            _landmarks[marker.Id] = marker;
        }
        var visibleIds = frame.Markers.Select(marker => marker.Id).ToHashSet();
        return frame with { Markers = frame.Markers.Concat(_landmarks.Values.Where(marker =>
            !visibleIds.Contains(marker.Id))).ToArray() };
    }
}

public static class RadarProjection
{
    public static bool TryScale(string? text, out double scale) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out scale) &&
        double.IsFinite(scale) && scale > 0;

    public static (double X, double Y) Project(double playerX, double playerY,
        RadarMarker marker, double centerX, double centerY, double pixelsPerUnit) =>
        (centerX + (marker.X - playerX) * pixelsPerUnit,
         centerY + (marker.Y - playerY) * pixelsPerUnit);
}

public sealed class RadarVisibility
{
    private bool _keyWasDown;
    public bool Enabled { get; private set; }

    public void Observe(bool gameForeground, bool keyDown)
    {
        if (gameForeground && keyDown && !_keyWasDown) Enabled = !Enabled;
        _keyWasDown = keyDown;
    }
}
