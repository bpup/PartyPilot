using System.Text.Json;

namespace PartyPilot.Bridge;

internal readonly record struct BridgeCommand(int Slot, short X, short Y, bool Active);

internal static class BridgeProtocol
{
    public static bool TryParse(string line, out BridgeCommand command)
    {
        command = default;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("slot", out var slotElement) ||
                !slotElement.TryGetInt32(out var slot) ||
                slot is < 1 or > 6 ||
                !root.TryGetProperty("state", out var stateElement) ||
                stateElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var state = stateElement.GetString();
            if (state is "waiting" or "holding")
            {
                command = new BridgeCommand(slot, 0, 0, false);
                return true;
            }
            if (state is not ("following" or "catching_up") ||
                !root.TryGetProperty("x", out var xElement) ||
                !root.TryGetProperty("y", out var yElement) ||
                !xElement.TryGetDouble(out var x) ||
                !yElement.TryGetDouble(out var y) ||
                !double.IsFinite(x) || !double.IsFinite(y))
            {
                return false;
            }

            var length = Math.Sqrt(x * x + y * y);
            if (length < 0.001 || length > 1.001) return false;
            command = new BridgeCommand(
                slot,
                (short)Math.Clamp(Math.Round(x * 32767), short.MinValue, short.MaxValue),
                (short)Math.Clamp(Math.Round(y * 32767), short.MinValue, short.MaxValue),
                true);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
