using System.Text.Json;
using PartyPilot.Common;

namespace PartyPilot.Bridge;

internal readonly record struct BridgeCommand(int Slot, short X, short Y, bool Active,
    bool EligibleForManualMovement, bool SprintDown, bool SuppressMirroredB,
    bool CatchUpPriority = false);
internal readonly record struct PairingControl(string Type, int Slot, int Pid, long WindowHandle);
internal readonly record struct FlaskCommand(int Slot, string Resource);
internal readonly record struct RecoveryActionCommand(int Slot, string Input);
internal readonly record struct AutoCastCommand(int Slot, string Input);
internal readonly record struct AutoPartyControl(string Type, int Slot, ushort Buttons, int HoldMilliseconds);
internal readonly record struct CombatControl(string Type, int Slot, PadReport Pulse, int HoldMilliseconds);
internal readonly record struct HybridKeyControl(int VirtualKey, bool Down);

internal static class BridgeProtocol
{
    public static bool TryParseWarpToMainSubscription(string line, out bool enabled)
    {
        enabled = false;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2 ||
                !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
                type.GetString() != "warp_to_main_subscription" ||
                !root.TryGetProperty("enabled", out var value) ||
                value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
            enabled = value.GetBoolean();
            return true;
        }
        catch (JsonException) { return false; }
    }

    public static bool TryParseMainInputSubscription(string line, out bool enabled)
    {
        enabled = false;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2 ||
                !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
                type.GetString() != "main_input_subscription" ||
                !root.TryGetProperty("enabled", out var value) ||
                value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
            enabled = value.GetBoolean();
            return true;
        }
        catch (JsonException) { return false; }
    }

    public static bool IsHybridPairingComplete(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                root.EnumerateObject().Count() == 1 &&
                root.TryGetProperty("type", out var type) &&
                type.ValueKind == JsonValueKind.String &&
                type.GetString() == "hybrid_pairing_complete";
        }
        catch (JsonException) { return false; }
    }

    public static bool TryParseHybridKey(string line, out HybridKeyControl command,
        HybridBindingConfig? bindings = null)
    {
        command = default;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var type) ||
                type.ValueKind != JsonValueKind.String || type.GetString() != "hybrid_key" ||
                !root.TryGetProperty("key", out var key) || !key.TryGetInt32(out var virtualKey) ||
                !root.TryGetProperty("down", out var down) ||
                down.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !(bindings ?? HybridBindingConfig.Default).ContainsKey(virtualKey)) return false;
            command = new HybridKeyControl(virtualKey, down.GetBoolean());
            return true;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    public static bool TryParseCombat(string line, out CombatControl command)
    {
        command = default;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
                type.GetString() is not ("combat_begin" or "combat_pulse" or "combat_end") ||
                !root.TryGetProperty("slot", out var slot) || !slot.TryGetInt32(out var value) ||
                value is < 1 or > 6) return false;
            var name = type.GetString()!;
            if (name is "combat_begin" or "combat_end")
            {
                command = new CombatControl(name, value, default, 0);
                return true;
            }
            if (!root.TryGetProperty("input", out var input) ||
                input.ValueKind != JsonValueKind.String ||
                !TryCombatInput(input.GetString(), out var pulse) ||
                !root.TryGetProperty("hold_ms", out var hold) ||
                !hold.TryGetInt32(out var duration) || duration is < 45 or > 160)
                return false;
            var hasRightX = root.TryGetProperty("right_x", out var rightX);
            var hasRightY = root.TryGetProperty("right_y", out var rightY);
            if (hasRightX != hasRightY) return false;
            if (hasRightX)
            {
                if (!rightX.TryGetInt16(out var x) || !rightY.TryGetInt16(out var y)) return false;
                pulse = pulse with { RightX = x, RightY = y };
            }
            command = new CombatControl(name, value, pulse, duration);
            return true;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    internal static bool TryCombatInput(string? input, out PadReport pulse)
    {
        pulse = default;
        var value = input?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(value)) return false;
        var trigger = value.StartsWith("LT+", StringComparison.Ordinal);
        if (trigger) value = value[3..];
        var buttons = value switch
        {
            "D-PAD UP" => (ushort)0x0001,
            "D-PAD DOWN" => (ushort)0x0002,
            "D-PAD LEFT" => (ushort)0x0004,
            "D-PAD RIGHT" => (ushort)0x0008,
            "LB" => (ushort)0x0100,
            "RB" => (ushort)0x0200,
            "A" => (ushort)0x1000,
            "B" => (ushort)0x2000,
            "X" => (ushort)0x4000,
            "Y" => (ushort)0x8000,
            _ => (ushort)0
        };
        if (value == "LT" && !trigger) pulse = new PadReport(0, 255, 0, 0, 0, 0, 0);
        else if (value == "RT") pulse = new PadReport(0, trigger ? (byte)255 : (byte)0,
            255, 0, 0, 0, 0);
        else if (buttons != 0) pulse = new PadReport(buttons, trigger ? (byte)255 : (byte)0,
            0, 0, 0, 0, 0);
        else return false;
        return !trigger || value is "A" or "B" or "X" or "Y" or "LB" or "RB" or "RT" or
            "D-PAD UP" or "D-PAD DOWN" or "D-PAD LEFT" or "D-PAD RIGHT";
    }

    internal static bool ValidCombatPulse(PadReport pulse)
    {
        const ushort allowed = 0xF30F;
        if (pulse.LeftX != 0 || pulse.LeftY != 0 ||
            (pulse.Buttons & ~allowed) != 0 ||
            System.Numerics.BitOperations.PopCount((uint)pulse.Buttons) > 1 ||
            pulse.LeftTrigger is not (0 or 255) || pulse.RightTrigger is not (0 or 255) ||
            (pulse.Buttons != 0 && pulse.RightTrigger != 0)) return false;
        return pulse.Buttons != 0 || pulse.LeftTrigger != 0 || pulse.RightTrigger != 0;
    }

    public static bool TryParseAutoParty(string line, out AutoPartyControl command)
    {
        command = default;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
                type.GetString() is not ("auto_party_begin" or "auto_party_pulse" or "auto_party_renew" or "auto_party_end"))
                return false;
            var name = type.GetString()!;
            if (name is "auto_party_begin" or "auto_party_renew" or "auto_party_end")
            {
                command = new AutoPartyControl(name, 0, 0, 0);
                return true;
            }
            if (!root.TryGetProperty("slot", out var slot) || !slot.TryGetInt32(out var value) ||
                value is < 1 or > 6 ||
                !root.TryGetProperty("buttons", out var buttons) || !buttons.TryGetUInt16(out var pressed) ||
                !root.TryGetProperty("hold_ms", out var hold) || !hold.TryGetInt32(out var duration) ||
                !ValidAutoPartyPulse(pressed, duration)) return false;
            command = new AutoPartyControl(name, value, pressed, duration);
            return true;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    internal static bool ValidAutoPartyPulse(ushort buttons, int duration) => buttons switch
    {
        0x0010 => duration is 80 or 100,
        0x0004 => duration == 80,
        0x0002 => duration == 80,
        0x1000 => duration is 60 or 80,
        0x2000 => duration == 60,
        _ => false
    };

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
                value is < 1 or > 6 ||
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

    public static bool IsRecoveryInput(string? input) => input is
        "X" or "Y" or "LB" or "RB" or "LT" or "RT" or "D-pad Up" or "D-pad Down";

    public static bool IsAutoCastInput(string? input) => input is
        "D-pad Up" or "D-pad Down" or "A" or "B" or "X" or "Y" or "LB" or "RB";

    public static bool TryParseAutoCast(string line, out AutoCastCommand command)
    {
        command = default;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 3 ||
                !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
                type.GetString() != "auto_cast" ||
                !root.TryGetProperty("slot", out var slot) || !slot.TryGetInt32(out var value) ||
                value is < 1 or > 6 ||
                !root.TryGetProperty("input", out var input) || input.ValueKind != JsonValueKind.String ||
                !IsAutoCastInput(input.GetString())) return false;
            command = new AutoCastCommand(value, input.GetString()!);
            return true;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    public static bool TryParseRecoveryAction(string line, out RecoveryActionCommand command)
    {
        command = default;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
                type.GetString() != "recovery_action" ||
                !root.TryGetProperty("slot", out var slot) || !slot.TryGetInt32(out var value) ||
                value is < 1 or > 6 ||
                !root.TryGetProperty("input", out var input) || input.ValueKind != JsonValueKind.String ||
                !IsRecoveryInput(input.GetString())) return false;
            command = new RecoveryActionCommand(value, input.GetString()!);
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
                !slot.TryGetInt32(out var value) || value is < 1 or > 6 ||
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
            var sprintDown = false;
            if (root.TryGetProperty("sprint_down", out var sprintValue))
            {
                if (sprintValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
                sprintDown = sprintValue.GetBoolean();
            }
            if (sprintDown && state != "catching_up") return false;
            var catchUpPriority = false;
            if (root.TryGetProperty("catch_up_priority", out var priorityValue))
            {
                if (priorityValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
                catchUpPriority = priorityValue.GetBoolean();
            }
            if (catchUpPriority && state != "catching_up") return false;
            var mirrorMovement = false;
            if (root.TryGetProperty("mirror_movement", out var mirrorValue))
            {
                if (mirrorValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
                mirrorMovement = mirrorValue.GetBoolean();
            }
            if (mirrorMovement && state != "holding") return false;
            var attackDirection = false;
            if (root.TryGetProperty("attack_direction", out var attackValue))
            {
                if (attackValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
                attackDirection = attackValue.GetBoolean();
            }
            if (attackDirection && (state == "waiting" || mirrorMovement || sprintDown)) return false;
            var suppressMirroredB = false;
            if (root.TryGetProperty("suppress_mirrored_b", out var suppressValue))
            {
                if (suppressValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
                suppressMirroredB = suppressValue.GetBoolean();
            }
            if (suppressMirroredB && (state == "waiting" || sprintDown)) return false;
            var mirrorIdle = false;
            if (root.TryGetProperty("mirror_idle", out var idleValue))
            {
                if (idleValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
                mirrorIdle = idleValue.GetBoolean();
            }
            if (mirrorIdle)
            {
                if (state is not ("following" or "catching_up") || sprintDown || mirrorMovement || attackDirection ||
                    !root.TryGetProperty("x", out var idleX) || !idleX.TryGetDouble(out var x) || x != 0 ||
                    !root.TryGetProperty("y", out var idleY) || !idleY.TryGetDouble(out var y) || y != 0)
                    return false;
                command = new BridgeCommand(slot, 0, 0, false, manualEligible, false,
                    suppressMirroredB, catchUpPriority);
                return true;
            }
            if (state is "waiting" or "holding")
            {
                if (state == "waiting" && manualEligible) return false;
                if (mirrorMovement || attackDirection) return TryParseMovement(root, slot, manualEligible, false,
                    suppressMirroredB, catchUpPriority, out command);
                command = new BridgeCommand(slot, 0, 0, false, manualEligible, false, suppressMirroredB);
                return true;
            }
            if (state is not ("following" or "catching_up")) return false;
            return TryParseMovement(root, slot, manualEligible, sprintDown, suppressMirroredB,
                catchUpPriority, out command);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseMovement(JsonElement root, int slot, bool manualEligible,
        bool sprintDown, bool suppressMirroredB, bool catchUpPriority, out BridgeCommand command)
    {
        command = default;
        if (!root.TryGetProperty("x", out var xElement) ||
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
                true, manualEligible, sprintDown, suppressMirroredB, catchUpPriority);
            return true;
    }
}
