using System.Text.Json;

namespace PartyPilot.Desktop;

internal readonly record struct MainInputSample(int Pad, short LeftX, short LeftY,
    bool BDown, bool Blocked, DateTime ReceivedAtUtc)
{
    internal static bool TryParse(JsonElement root, IReadOnlySet<int> assignedPads,
        DateTime receivedAtUtc, out MainInputSample sample)
    {
        sample = default;
        if (root.ValueKind != JsonValueKind.Object || receivedAtUtc.Kind != DateTimeKind.Utc ||
            !root.TryGetProperty("slot", out var padValue) ||
            !padValue.TryGetInt32(out var pad) || !assignedPads.Contains(pad) ||
            !root.TryGetProperty("left_x", out var xValue) ||
            !xValue.TryGetInt16(out var x) ||
            !root.TryGetProperty("left_y", out var yValue) ||
            !yValue.TryGetInt16(out var y) ||
            !root.TryGetProperty("b_down", out var bValue) ||
            bValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            !root.TryGetProperty("blocked", out var blockedValue) ||
            blockedValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        sample = new MainInputSample(pad, x, y, bValue.GetBoolean(),
            blockedValue.GetBoolean(), receivedAtUtc);
        return true;
    }
}
