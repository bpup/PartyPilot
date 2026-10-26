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
    private IReadOnlyDictionary<int, int> _focusPads = new Dictionary<int, int>();
    private bool _physicalMode;
    private bool _rightStickFollowerOverride;
    private int _mainSlot;
    private int _focusedSlot;
    private int _isolatedSlot;
    private DateTime _lastFocusToggleUtc = DateTime.MinValue;
    private PadReport _physicalReport;
    private bool _physicalConnected;
    private short _manualX;
    private short _manualY;
    private ushort _previousButtons;
    private byte _previousLeftTrigger;

    public int FocusedSlot => _physicalMode ? _focusedSlot : 0;

    public BridgeRouter(IReadOnlyDictionary<int, IPadOutput> outputs)
    {
        _routes = outputs.ToDictionary(entry => entry.Key, entry => new PadRoute(entry.Value));
    }

    public bool ConfigurePhysical(int mainSlot, bool rightStickFollowerOverride = false,
        IReadOnlyDictionary<int, int>? focusPads = null)
    {
        if (!_routes.ContainsKey(mainSlot)) return false;
        var explicitFocusPads = focusPads is not null;
        focusPads ??= _routes.Keys.Where(slot => slot is >= 1 and <= 4)
            .ToDictionary(slot => slot, slot => slot);
        if (focusPads.Count == 0 || focusPads.Any(entry => entry.Key is < 1 or > 4 ||
            !_routes.ContainsKey(entry.Value)) || focusPads.Values.Distinct().Count() != focusPads.Count ||
            (explicitFocusPads && (!focusPads.TryGetValue(1, out var firstPad) || firstPad != mainSlot)))
            return false;
        _focusPads = new Dictionary<int, int>(focusPads);
        _physicalMode = true;
        _rightStickFollowerOverride = rightStickFollowerOverride;
        _mainSlot = mainSlot;
        _focusedSlot = mainSlot;
        _isolatedSlot = 0;
        _lastFocusToggleUtc = DateTime.MinValue;
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
            // An external focus command for MAIN resumes the ordinary shared route.
            _isolatedSlot = focused == _mainSlot ? 0 : focused;
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

    public void SetPhysical(XInputGamepad gamepad, DateTime? observedAtUtc = null)
    {
        if (!_physicalMode) return;
        const ushort leftThumb = 0x0040;
        const ushort rightThumb = 0x0080;
        var nowUtc = observedAtUtc ?? DateTime.UtcNow;
        var previousFocus = _focusedSlot;
        var previousIsolation = _isolatedSlot;
        var left = (gamepad.Buttons & leftThumb) != 0;
        var right = (gamepad.Buttons & rightThumb) != 0;
        var previousLeft = (_previousButtons & leftThumb) != 0;
        var previousRight = (_previousButtons & rightThumb) != 0;
        var leftTrigger = gamepad.LeftTrigger >= 30;
        var previousLeftTrigger = _previousLeftTrigger >= 30;
        var leftRole = leftTrigger && _focusPads.ContainsKey(3) ? 3 : 1;
        var rightRole = leftTrigger && _focusPads.ContainsKey(4) ? 4 : 2;
        var leftEdge = left && (!previousLeft || (leftRole == 3 && !previousLeftTrigger));
        var rightEdge = right && (!previousRight || (rightRole == 4 && !previousLeftTrigger));
        var pressedRole = leftEdge == rightEdge ? 0 : leftEdge ? leftRole : rightRole;
        var promoteChord = pressedRole is 3 or 4 &&
            _isolatedSlot == (_focusPads.TryGetValue(pressedRole - 2, out var prefixPad) ? prefixPad : -1) &&
            (pressedRole == 3 ? previousLeft : previousRight) && !previousLeftTrigger;
        if (pressedRole != 0 && _focusPads.TryGetValue(pressedRole, out var pressedSlot) &&
            (promoteChord || nowUtc - _lastFocusToggleUtc >= TimeSpan.FromMilliseconds(350)))
        {
            _isolatedSlot = _isolatedSlot == pressedSlot ? 0 : pressedSlot;
            _focusedSlot = _isolatedSlot == 0 ? _mainSlot : _isolatedSlot;
            _lastFocusToggleUtc = nowUtc;
        }
        _previousButtons = gamepad.Buttons;
        _previousLeftTrigger = gamepad.LeftTrigger;
        gamepad.Buttons &= unchecked((ushort)~(leftThumb | rightThumb));
        if (leftTrigger && ((left && leftRole == 3) || (right && rightRole == 4)))
            gamepad.LeftTrigger = 0;
        _physicalConnected = true;
        _manualX = gamepad.RightX;
        _manualY = gamepad.RightY;
        if (_rightStickFollowerOverride && _isolatedSlot == 0)
        {
            gamepad.RightX = 0;
            gamepad.RightY = 0;
        }
        _physicalReport = PadReport.FromPhysical(gamepad);
        if (previousFocus != _focusedSlot || previousIsolation != _isolatedSlot || _rightStickFollowerOverride) RenderAll();
        else Render(_focusedSlot, _routes[_focusedSlot]);
    }

    public void PhysicalDisconnected()
    {
        if (!_physicalMode) return;
        NeutralAll();
        _isolatedSlot = 0;
        _focusedSlot = _mainSlot;
        _lastFocusToggleUtc = DateTime.MinValue;
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
        _previousLeftTrigger = 0;
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
        if (_physicalMode && _isolatedSlot != 0)
        {
            route.Output.SetReport(default);
            return;
        }
        if (_rightStickFollowerOverride && _physicalConnected && _isolatedSlot == 0 &&
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
