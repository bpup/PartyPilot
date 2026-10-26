using Windows.Gaming.Input;

namespace PartyPilot.Bridge;

// Reads a single hardware controller by VID/PID through Windows Gaming Input.
// Ambiguous devices are rejected: a HidHide instance ID is not a WGI identity.
internal sealed class WgiPhysicalSource
{
    private readonly ushort _vendor;
    private readonly ushort _product;
    private readonly string _nonRoamableId;
    private RawGameController _raw;
    private Gamepad _gamepad;

    private WgiPhysicalSource(ushort vendor, ushort product, RawGameController raw, Gamepad gamepad)
    {
        _vendor = vendor;
        _product = product;
        _raw = raw;
        _gamepad = gamepad;
        _nonRoamableId = raw.NonRoamableId;
    }

    internal static WgiPhysicalSource Open(string deviceInstance)
    {
        if (!OperatingSystem.IsWindows() ||
            !ControllerDeviceId.TryReadVidPid(deviceInstance, out var vendor, out var product))
            throw new InvalidOperationException("A Windows controller device ID with VID/PID is required.");
        var matches = CompatibleControllers(vendor, product);
        if (matches.Count != 1 || string.IsNullOrWhiteSpace(matches[0].Raw.NonRoamableId))
            throw new InvalidOperationException(
                "The selected controller does not resolve to one unique Windows Gaming Input device.");
        return new WgiPhysicalSource(vendor, product, matches[0].Raw, matches[0].Pad);
    }

    internal bool TryRead(out XInputGamepad report)
    {
        report = default;
        try
        {
            var matches = CompatibleControllers(_vendor, _product);
            var selected = WgiIdentitySelection.SelectClaimedIndex(
                matches.Select(item => item.Raw.NonRoamableId).ToArray(), _nonRoamableId);
            if (selected < 0) return false;
            _raw = matches[selected].Raw;
            _gamepad = matches[selected].Pad;
            var reading = _gamepad.GetCurrentReading();
            report = new XInputGamepad
            {
                Buttons = MapButtons(reading.Buttons),
                LeftTrigger = ScaleTrigger(reading.LeftTrigger),
                RightTrigger = ScaleTrigger(reading.RightTrigger),
                LeftX = ScaleAxis(reading.LeftThumbstickX),
                LeftY = ScaleAxis(reading.LeftThumbstickY),
                RightX = ScaleAxis(reading.RightThumbstickX),
                RightY = ScaleAxis(reading.RightThumbstickY)
            };
            return true;
        }
        catch
        {
            report = default;
            return false;
        }
    }

    private static List<(RawGameController Raw, Gamepad Pad)> CompatibleControllers(
        ushort vendor, ushort product)
    {
        var result = new List<(RawGameController, Gamepad)>();
        foreach (var raw in RawGameController.RawGameControllers)
        {
            if (raw.HardwareVendorId != vendor || raw.HardwareProductId != product) continue;
            try
            {
                var pad = Gamepad.FromGameController(raw);
                if (pad is not null) result.Add((raw, pad));
            }
            catch
            {
                // One incompatible raw device must not hide a readable claimant.
            }
        }
        return result;
    }

    private static ushort MapButtons(GamepadButtons buttons)
    {
        ushort result = 0;
        if (buttons.HasFlag(GamepadButtons.DPadUp)) result |= 0x0001;
        if (buttons.HasFlag(GamepadButtons.DPadDown)) result |= 0x0002;
        if (buttons.HasFlag(GamepadButtons.DPadLeft)) result |= 0x0004;
        if (buttons.HasFlag(GamepadButtons.DPadRight)) result |= 0x0008;
        if (buttons.HasFlag(GamepadButtons.Menu)) result |= 0x0010;
        if (buttons.HasFlag(GamepadButtons.View)) result |= 0x0020;
        if (buttons.HasFlag(GamepadButtons.LeftThumbstick)) result |= 0x0040;
        if (buttons.HasFlag(GamepadButtons.RightThumbstick)) result |= 0x0080;
        if (buttons.HasFlag(GamepadButtons.LeftShoulder)) result |= 0x0100;
        if (buttons.HasFlag(GamepadButtons.RightShoulder)) result |= 0x0200;
        if (buttons.HasFlag(GamepadButtons.A)) result |= 0x1000;
        if (buttons.HasFlag(GamepadButtons.B)) result |= 0x2000;
        if (buttons.HasFlag(GamepadButtons.X)) result |= 0x4000;
        if (buttons.HasFlag(GamepadButtons.Y)) result |= 0x8000;
        return result;
    }

    private static byte ScaleTrigger(double value) =>
        (byte)Math.Round(Math.Clamp(value, 0, 1) * 255, MidpointRounding.AwayFromZero);

    private static short ScaleAxis(double value)
    {
        var bounded = Math.Clamp(value, -1, 1);
        return bounded <= -1 ? short.MinValue :
            (short)Math.Round(bounded * short.MaxValue, MidpointRounding.AwayFromZero);
    }
}
