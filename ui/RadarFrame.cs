using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PartyPilot.Desktop;

public sealed record RadarMarker(uint Id, string Category, string Path, string? Label,
    string? ItemPath, int? Stack, string? Rarity, double X, double Y);

public sealed record RadarFrame(int ProcessId, DateTime SeenUtc, double PlayerX, double PlayerY,
    IReadOnlyList<RadarMarker> Markers);

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
                !TryFinite(root, "X", out var playerX) || !TryFinite(root, "Y", out var playerY) ||
                !root.TryGetProperty("RadarEntities", out var entities) ||
                entities.ValueKind != JsonValueKind.Array || entities.GetArrayLength() > 96)
                return false;
            var markers = new List<RadarMarker>(entities.GetArrayLength());
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
                if (category is not ("player" or "loot") &&
                    (label != null || itemPath != null || stack != null || rarity != null)) return false;
                markers.Add(new RadarMarker(id, category, path, label, itemPath, stack, rarity, x, y));
            }
            frame = new RadarFrame(pid, seenUtc, playerX, playerY, markers);
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

    private static bool KnownCategory(string value) => value is
        "monster" or "checkpoint" or "waypoint" or "portal" or "transition" or "npc" or "chest" or "shrine" or "player" or "league" or "loot";

    private static bool WorldItemWrapperPath(string path)
    {
        var name = path.TrimEnd('/').Split('/').Last();
        var suffix = name.IndexOf('@');
        if (suffix >= 0) name = name[..suffix];
        return name.Equals("WorldItem", StringComparison.OrdinalIgnoreCase);
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
    public bool Enabled { get; private set; } = true;

    public void Observe(bool gameForeground, bool keyDown)
    {
        if (gameForeground && keyDown && !_keyWasDown) Enabled = !Enabled;
        _keyWasDown = keyDown;
    }
}
