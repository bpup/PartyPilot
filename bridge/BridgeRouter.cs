namespace PartyPilot.Bridge;

internal readonly record struct PadReport(
    ushort Buttons, byte LeftTrigger, byte RightTrigger,
    short LeftX, short LeftY, short RightX, short RightY)
{
    public static PadReport FromPhysical(XInputGamepad pad) => new(
        pad.Buttons, pad.LeftTrigger, pad.RightTrigger,
        pad.LeftX, pad.LeftY, pad.RightX, pad.RightY);
    public static PadReport FromMovement(short x, short y) => new(0, 0, 0, x, y, 0, 0);
}

internal interface IPadOutput
{
    void SetReport(PadReport report);
}

internal sealed class BridgeRouter
{
    private readonly Dictionary<int, PadRoute> _routes;
    private bool _physicalMode;
    private bool _rightStickFollowerOverride;
    private int _mainSlot;
    private int _focusedSlot;
    private PadReport _physicalReport;
    private bool _physicalConnected;
    private short _manualX;
    private short _manualY;
    private ushort _previousButtons;

    public int FocusedSlot => _physicalMode ? _focusedSlot : 0;

    public BridgeRouter(IReadOnlyDictionary<int, IPadOutput> outputs)
    {
        _routes = outputs.ToDictionary(entry => entry.Key, entry => new PadRoute(entry.Value));
    }

    public bool ConfigurePhysical(int mainSlot, bool rightStickFollowerOverride = false)
    {
        if (!_routes.ContainsKey(mainSlot)) return false;
        _physicalMode = true;
        _rightStickFollowerOverride = rightStickFollowerOverride;
        _mainSlot = mainSlot;
        _focusedSlot = mainSlot;
        NeutralAll();
        return true;
    }

    public bool ApplyLine(string line, DateTime nowUtc)
    {
        if (BridgeProtocol.TryParseFocus(line, out var focused))
        {
            if (!_physicalMode || !_routes.ContainsKey(focused))
            {
                NeutralAll();
                return false;
            }
            _focusedSlot = focused;
            RenderAll();
            return true;
        }
        if (!BridgeProtocol.TryParse(line, out var command) ||
            !_routes.TryGetValue(command.Slot, out var route))
        {
            NeutralAll();
            return false;
        }
        route.Automation = command.Active ? PadReport.FromMovement(command.X, command.Y) : default;
        route.Active = command.Active;
        route.EligibleForManualMovement = command.EligibleForManualMovement;
        route.LastUpdateUtc = nowUtc;
        Render(command.Slot, route);
        return true;
    }

    public void SetPhysical(XInputGamepad gamepad)
    {
        if (!_physicalMode) return;
        const ushort leftThumb = 0x0040;
        const ushort rightThumb = 0x0080;
        var previousFocus = _focusedSlot;
        if ((gamepad.Buttons & leftThumb) != 0 && (_previousButtons & leftThumb) == 0 && _routes.ContainsKey(1))
            _focusedSlot = 1;
        else if ((gamepad.Buttons & rightThumb) != 0 && (_previousButtons & rightThumb) == 0 && _routes.ContainsKey(2))
            _focusedSlot = 2;
        _previousButtons = gamepad.Buttons;
        gamepad.Buttons &= unchecked((ushort)~(leftThumb | rightThumb));
        _physicalConnected = true;
        _manualX = gamepad.RightX;
        _manualY = gamepad.RightY;
        if (_rightStickFollowerOverride && _focusedSlot == _mainSlot)
        {
            gamepad.RightX = 0;
            gamepad.RightY = 0;
        }
        _physicalReport = PadReport.FromPhysical(gamepad);
        if (previousFocus != _focusedSlot || _rightStickFollowerOverride) RenderAll();
        else Render(_focusedSlot, _routes[_focusedSlot]);
    }

    public void PhysicalDisconnected()
    {
        if (!_physicalMode) return;
        NeutralAll();
    }

    public void Tick(DateTime nowUtc)
    {
        foreach (var (slot, route) in _routes)
        {
            if ((route.Active || route.EligibleForManualMovement) &&
                nowUtc - route.LastUpdateUtc > TimeSpan.FromMilliseconds(500))
            {
                route.Automation = default;
                route.Active = false;
                route.EligibleForManualMovement = false;
                Render(slot, route);
            }
        }
    }

    public void NeutralAll()
    {
        _physicalConnected = false;
        _physicalReport = default;
        _manualX = 0;
        _manualY = 0;
        _previousButtons = 0;
        foreach (var route in _routes.Values)
        {
            route.Automation = default;
            route.Active = false;
            route.EligibleForManualMovement = false;
            route.Output.SetReport(default);
        }
    }

    private void RenderAll()
    {
        foreach (var (slot, route) in _routes) Render(slot, route);
    }

    private void Render(int slot, PadRoute route)
    {
        if (_physicalMode && slot == _focusedSlot)
        {
            route.Output.SetReport(_physicalConnected ? _physicalReport : default);
            return;
        }
        if (_rightStickFollowerOverride && _physicalConnected && _focusedSlot == _mainSlot &&
            route.EligibleForManualMovement &&
            (Math.Abs((int)_manualX) > 8000 || Math.Abs((int)_manualY) > 8000))
        {
            route.Output.SetReport(PadReport.FromMovement(_manualX, _manualY));
            return;
        }
        route.Output.SetReport(route.Automation);
    }

    private sealed class PadRoute(IPadOutput output)
    {
        public IPadOutput Output { get; } = output;
        public PadReport Automation { get; set; }
        public DateTime LastUpdateUtc { get; set; }
        public bool Active { get; set; }
        public bool EligibleForManualMovement { get; set; }
    }
}
