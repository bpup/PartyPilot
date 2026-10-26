using System.Text.Json;

namespace PartyPilot.Bridge;

internal readonly record struct BridgeCommand(int Slot, short X, short Y, bool Active, bool EligibleForManualMovement);
internal readonly record struct PairingControl(string Type, int Slot, int Pid, long WindowHandle);
internal readonly record struct FlaskCommand(int Slot, string Resource);

internal static class BridgeProtocol
{
    public static bool TryParseFlask(string line, out FlaskCommand command)
    {
        command = default;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
                type.GetString() != "flask" ||
                !root.TryGetProperty("slot", out var slot) || !slot.TryGetInt32(out var value) ||
                value is < 1 or > 4 ||
                !root.TryGetProperty("resource", out var resource) || resource.ValueKind != JsonValueKind.String ||
                resource.GetString() is not ("life" or "mana"))
                return false;
            command = new FlaskCommand(value, resource.GetString()!);
            return true;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    public static bool TryParsePairingControl(string line, out PairingControl control)
    {
        control = default;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
                return false;
            var name = type.GetString();
            if (name == "finish_pairing")
            {
                control = new PairingControl(name, 0, 0, 0);
                return true;
            }
            if (name != "arm_pairing" || !root.TryGetProperty("slot", out var slot) ||
                !slot.TryGetInt32(out var value) || value is < 1 or > 4 ||
                !root.TryGetProperty("pid", out var pidElement) || !pidElement.TryGetInt32(out var pid) || pid <= 0 ||
                !root.TryGetProperty("window_handle", out var handleElement) ||
                !handleElement.TryGetInt64(out var handle) || handle <= 0)
                return false;
            control = new PairingControl(name, value, pid, handle);
            return true;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    public static bool TryParseFocus(string line, out int slot)
    {
        slot = 0;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var type) || type.GetString() != "focus" ||
                !root.TryGetProperty("slot", out var value) || !value.TryGetInt32(out slot))
                return false;
            return slot is >= 1 and <= 6;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

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
            var manualEligible = false;
            if (root.TryGetProperty("manual_override_eligible", out var manualValue))
            {
                if (manualValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
                manualEligible = manualValue.GetBoolean();
            }
            if (state is "waiting" or "holding")
            {
                if (state == "waiting" && manualEligible) return false;
                command = new BridgeCommand(slot, 0, 0, false, manualEligible);
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
                true, manualEligible);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
