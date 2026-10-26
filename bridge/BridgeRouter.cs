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
    private int _focusedSlot;
    private PadReport _physicalReport;
    private bool _physicalConnected;
    private ushort _previousButtons;

    public BridgeRouter(IReadOnlyDictionary<int, IPadOutput> outputs)
    {
        _routes = outputs.ToDictionary(entry => entry.Key, entry => new PadRoute(entry.Value));
    }

    public bool ConfigurePhysical(int mainSlot)
    {
        if (!_routes.ContainsKey(mainSlot)) return false;
        _physicalMode = true;
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
        _physicalReport = PadReport.FromPhysical(gamepad);
        if (previousFocus != _focusedSlot) RenderAll();
        else Render(_focusedSlot, _routes[_focusedSlot]);
    }

    public void PhysicalDisconnected()
    {
        if (!_physicalMode) return;
        _physicalConnected = false;
        _physicalReport = default;
        _previousButtons = 0;
        Render(_focusedSlot, _routes[_focusedSlot]);
    }

    public void Tick(DateTime nowUtc)
    {
        foreach (var (slot, route) in _routes)
        {
            if (route.Active && nowUtc - route.LastUpdateUtc > TimeSpan.FromMilliseconds(500))
            {
                route.Automation = default;
                route.Active = false;
                Render(slot, route);
            }
        }
    }

    public void NeutralAll()
    {
        _physicalConnected = false;
        _physicalReport = default;
        _previousButtons = 0;
        foreach (var route in _routes.Values)
        {
            route.Automation = default;
            route.Active = false;
            route.Output.SetReport(default);
        }
    }

    private void RenderAll()
    {
        foreach (var (slot, route) in _routes) Render(slot, route);
    }

    private void Render(int slot, PadRoute route)
    {
        route.Output.SetReport(_physicalMode && slot == _focusedSlot
            ? (_physicalConnected ? _physicalReport : default)
            : route.Automation);
    }

    private sealed class PadRoute(IPadOutput output)
    {
        public IPadOutput Output { get; } = output;
        public PadReport Automation { get; set; }
        public DateTime LastUpdateUtc { get; set; }
        public bool Active { get; set; }
    }
}
