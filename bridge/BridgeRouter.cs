using PartyPilot.Common;

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

internal interface IReportBatch
{
    void BeginBatch();
    void EndBatch();
    void AbortBatch();
}

internal sealed class BridgeRouter
{
    private readonly Dictionary<int, PadRoute> _routes;
    private readonly IReportBatch? _reportBatch;
    private IReadOnlyDictionary<int, int> _focusPads = new Dictionary<int, int>();
    private IReadOnlyDictionary<int, int> _playerForPad = new Dictionary<int, int>();
    private FollowerMirrorConfig _followerMirror = FollowerMirrorConfig.Default;
    private bool _physicalMode;
    private HybridKeyboardState? _hybridKeyboard;
    private HybridBindingConfig _hybridBindings = HybridBindingConfig.Default;
    private bool _hybridFocusRequested;
    private bool _hybridConfirmReleased;
    private IReadOnlyDictionary<int, int> _hybridPlayerPads = new Dictionary<int, int>();
    private bool _menuGuardActive;
    private bool _rightStickFollowerOverride;
    private bool _lastFocusedLeads;
    private int _directRightStickSlot;
    private bool _allowDirectRightStick;
    private int _mainSlot;
    private int _initialMainSlot;
    private int _focusedSlot;
    private int _lastManualFocusSlot;
    private int _isolatedSlot;
    private DateTime _lastFocusToggleUtc = DateTime.MinValue;
    private PadReport _physicalReport;
    private bool _physicalConnected;
    private bool _followerMirrorEnabled;
    private short _manualX;
    private short _manualY;
    private ushort _previousButtons;
    private byte _previousLeftTrigger;
    private bool _autoPartyActive;
    private DateTime _autoPartyExpiresUtc;
    private int _autoPartySlot;
    private ushort _autoPartyButtons;
    private DateTime _autoPartyButtonsUntilUtc;

    public bool BeginCombat(int slot, DateTime nowUtc)
    {
        if (!_physicalMode || !_physicalConnected || _autoPartyActive || _menuGuardActive ||
            _isolatedSlot != 0 || slot == _focusedSlot || !_routes.TryGetValue(slot, out var route) ||
            nowUtc.Kind != DateTimeKind.Utc || route.CombatLeaseUntilUtc > nowUtc) return false;
        route.CombatLeaseUntilUtc = nowUtc.AddSeconds(10);
        route.CombatPulseUntilUtc = default;
        route.CombatPulse = default;
        route.LifeUntilUtc = route.ManaUntilUtc = DateTime.MinValue;
        Render(slot, route);
        return true;
    }

    public bool PulseCombat(int slot, PadReport pulse, int holdMilliseconds, DateTime nowUtc)
    {
        if (!_routes.TryGetValue(slot, out var route) || !_physicalMode || !_physicalConnected ||
            _autoPartyActive || _menuGuardActive || _isolatedSlot != 0 || slot == _focusedSlot ||
            nowUtc.Kind != DateTimeKind.Utc || nowUtc >= route.CombatLeaseUntilUtc ||
            holdMilliseconds is < 45 or > 160 ||
            !BridgeProtocol.ValidCombatPulse(pulse) ||
            route.CombatPulseUntilUtc > nowUtc) return false;
        route.CombatPulse = pulse;
        route.CombatPulseUntilUtc = nowUtc.AddMilliseconds(holdMilliseconds);
        route.CombatLeaseUntilUtc = nowUtc.AddSeconds(10);
        Render(slot, route);
        return true;
    }

    public void EndCombat(int slot)
    {
        if (!_routes.TryGetValue(slot, out var route)) return;
        route.CombatLeaseUntilUtc = default;
        route.CombatPulseUntilUtc = default;
        route.CombatPulse = default;
        Render(slot, route);
    }

    public int FocusedSlot => _physicalMode ? _focusedSlot : 0;
    public int LastManualFocusSlot => _physicalMode ? _lastManualFocusSlot : 0;
    public bool AutoPartyActive => _autoPartyActive;
    public int AutoPartySlot => _autoPartySlot;
    public bool AutoPartyPulseActive(DateTime nowUtc) => _autoPartyActive &&
        _autoPartyButtons != 0 && nowUtc.Kind == DateTimeKind.Utc &&
        nowUtc < _autoPartyButtonsUntilUtc;
    public bool HybridPairingComplete => _hybridKeyboard?.PairingComplete == true;
    public ushort HybridMenuButtons => _hybridKeyboard?.MenuButtons ?? 0;
    public int HybridFocusForKey(int key) => _hybridBindings.FocusForKey(key);
    public int HybridFocusedPlayer => _hybridKeyboard?.FocusedPlayer ?? 0;
    public bool HybridFocusRequested => _hybridFocusRequested;
    public bool HybridConfirmReleased => _hybridConfirmReleased;

    public BridgeRouter(IReadOnlyDictionary<int, IPadOutput> outputs, IReportBatch? reportBatch = null)
    {
        _routes = outputs.ToDictionary(entry => entry.Key, entry => new PadRoute(entry.Value));
        _reportBatch = reportBatch;
    }

    public bool ConfigurePhysical(int mainSlot, bool rightStickFollowerOverride = false,
        IReadOnlyDictionary<int, int>? focusPads = null, int directRightStickSlot = 0,
        bool lastFocusedLeads = false, FollowerMirrorConfig? followerMirror = null)
    {
        if (_hybridKeyboard is not null || !_routes.ContainsKey(mainSlot)) return false;
        if (directRightStickSlot != 0 && (!rightStickFollowerOverride ||
            directRightStickSlot == mainSlot || !_routes.ContainsKey(directRightStickSlot))) return false;
        var explicitFocusPads = focusPads is not null;
        focusPads ??= _routes.Keys.Where(slot => slot is >= 1 and <= 6)
            .ToDictionary(slot => slot, slot => slot);
        if (focusPads.Count == 0 || focusPads.Any(entry => entry.Key is < 1 or > 6 ||
            !_routes.ContainsKey(entry.Value)) || focusPads.Values.Distinct().Count() != focusPads.Count ||
            (explicitFocusPads && (!focusPads.TryGetValue(1, out var firstPad) || firstPad != mainSlot)))
            return false;
        _focusPads = new Dictionary<int, int>(focusPads);
        _playerForPad = focusPads.ToDictionary(entry => entry.Value, entry => entry.Key);
        _followerMirror = followerMirror ?? FollowerMirrorConfig.Default;
        _physicalMode = true;
        _rightStickFollowerOverride = rightStickFollowerOverride;
        _lastFocusedLeads = lastFocusedLeads;
        _directRightStickSlot = directRightStickSlot;
        _mainSlot = mainSlot;
        _initialMainSlot = mainSlot;
        _focusedSlot = mainSlot;
        _lastManualFocusSlot = mainSlot;
        _isolatedSlot = 0;
        _lastFocusToggleUtc = DateTime.MinValue;
        NeutralAll();
        return true;
    }

    public bool ConfigureHybridKeyboard(IReadOnlyDictionary<int, int> playerPads,
        HybridBindingConfig? bindings = null, FollowerMirrorConfig? followerMirror = null)
    {
        if (_physicalMode || playerPads.Count == 0 ||
            playerPads.Any(entry => entry.Key is < 2 or > 6 || !_routes.ContainsKey(entry.Value)) ||
            playerPads.Values.Distinct().Count() != playerPads.Count) return false;
        _hybridPlayerPads = new Dictionary<int, int>(playerPads);
        _playerForPad = playerPads.ToDictionary(entry => entry.Value, entry => entry.Key);
        _followerMirror = followerMirror ?? FollowerMirrorConfig.Default;
        _hybridBindings = bindings ?? HybridBindingConfig.Default;
        _hybridKeyboard = new HybridKeyboardState(playerPads.Keys, _hybridBindings);
        NeutralAll();
        return true;
    }

    public bool EnableHybridMainFollowMirror()
    {
        if (_hybridKeyboard?.EnableMainFollowMirror() != true) return false;
        RenderAll();
        return true;
    }

    public bool ApplyHybridKey(int virtualKey, bool down, bool gameForeground)
    {
        _hybridFocusRequested = false;
        _hybridConfirmReleased = false;
        if (_hybridKeyboard is null || virtualKey == 0 ||
            (HybridKeyboardInput.FocusForKey(virtualKey, _hybridBindings) == 0 &&
             HybridKeyboardInput.ActionForKey(virtualKey, _hybridBindings) == HybridAction.None)) return false;
        if (_autoPartyActive)
        {
            _hybridKeyboard.Handle(virtualKey, down, gameForeground: false);
            return true;
        }
        if (_hybridKeyboard.GameForeground && !gameForeground) ClearHybridAutomation();
        var sample = _hybridKeyboard.Handle(virtualKey, down, gameForeground);
        _hybridFocusRequested = sample.FocusRequested;
        _hybridConfirmReleased = sample.ConfirmReleased;
        RenderAll();
        return true;
    }

    public void SetHybridForeground(bool gameForeground)
    {
        if (_hybridKeyboard is null || _hybridKeyboard.GameForeground == gameForeground) return;
        if (!gameForeground) ClearHybridAutomation();
        _hybridKeyboard.SetForeground(gameForeground);
        RenderAll();
    }

    public void SetHybridMenuOpen(bool open)
    {
        if (_hybridKeyboard?.SetMenuOpen(open) == true) RenderAll();
    }

    private void ClearHybridAutomation()
    {
        foreach (var route in _routes.Values)
        {
            route.Automation = default;
            route.Active = false;
            route.EligibleForManualMovement = false;
        }
    }

    public bool ApplyLine(string line, DateTime nowUtc)
    {
        if (_autoPartyActive)
        {
            // Drop valid follow/focus input while Auto Party owns every pad; malformed input still fails closed.
            if ((BridgeProtocol.TryParseFocus(line, out var focusSlot) && _routes.ContainsKey(focusSlot)) ||
                (BridgeProtocol.TryParse(line, out var movement) && _routes.ContainsKey(movement.Slot))) return true;
            NeutralAll();
            return false;
        }
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
            ReleaseCombatAll();
            RenderAll();
            return true;
        }
        if (!BridgeProtocol.TryParse(line, out var command) ||
            !_routes.TryGetValue(command.Slot, out var route) ||
            (_hybridKeyboard is not null &&
             (!_hybridKeyboard.PairingComplete || !_hybridPlayerPads.Values.Contains(command.Slot))))
        {
            NeutralAll();
            return false;
        }
        route.Automation = command.Active
            ? PadReport.FromMovement(command.X, command.Y) with
                { Buttons = command.SprintDown ? (ushort)0x2000 : (ushort)0 }
            : default;
        route.Active = command.Active;
        route.EligibleForManualMovement = command.EligibleForManualMovement;
        route.LastUpdateUtc = nowUtc;
        Render(command.Slot, route);
        return true;
    }

    public void SetPhysical(XInputGamepad gamepad, DateTime? observedAtUtc = null,
        bool allowDirectRightStick = true)
    {
        if (!_physicalMode) return;
        if (_autoPartyActive) return;
        const ushort leftThumb = 0x0040;
        const ushort rightThumb = 0x0080;
        const ushort dpadUp = 0x0001;
        const ushort dpadDown = 0x0002;
        var nowUtc = observedAtUtc ?? DateTime.UtcNow;
        var previousFocus = _focusedSlot;
        var previousIsolation = _isolatedSlot;
        var left = (gamepad.Buttons & leftThumb) != 0;
        var right = (gamepad.Buttons & rightThumb) != 0;
        var previousLeft = (_previousButtons & leftThumb) != 0;
        var previousRight = (_previousButtons & rightThumb) != 0;
        var up = (gamepad.Buttons & dpadUp) != 0;
        var down = (gamepad.Buttons & dpadDown) != 0;
        var previousUp = (_previousButtons & dpadUp) != 0;
        var previousDown = (_previousButtons & dpadDown) != 0;
        var leftTrigger = gamepad.LeftTrigger >= 30;
        var previousLeftTrigger = _previousLeftTrigger >= 30;
        var leftRole = leftTrigger && _focusPads.ContainsKey(3) ? 3 : 1;
        var rightRole = leftTrigger && _focusPads.ContainsKey(4) ? 4 : 2;
        var leftEdge = left && (!previousLeft || (leftRole == 3 && !previousLeftTrigger));
        var rightEdge = right && (!previousRight || (rightRole == 4 && !previousLeftTrigger));
        var fifthEdge = leftTrigger && _focusPads.ContainsKey(5) && up &&
            (!previousUp || !previousLeftTrigger);
        var sixthEdge = leftTrigger && _focusPads.ContainsKey(6) && down &&
            (!previousDown || !previousLeftTrigger);
        var edgeCount = (leftEdge ? 1 : 0) + (rightEdge ? 1 : 0) +
            (fifthEdge ? 1 : 0) + (sixthEdge ? 1 : 0);
        var pressedRole = edgeCount == 1
            ? fifthEdge ? 5 : sixthEdge ? 6 : leftEdge ? leftRole : rightRole : 0;
        var promoteChord = (pressedRole is 3 or 4 &&
            _isolatedSlot == (_focusPads.TryGetValue(pressedRole - 2, out var prefixPad) ? prefixPad : -1) &&
            (pressedRole == 3 ? previousLeft : previousRight) && !previousLeftTrigger) ||
            (pressedRole is 5 or 6 && _isolatedSlot != 0);
        if (pressedRole != 0 && _focusPads.TryGetValue(pressedRole, out var pressedSlot) &&
            (promoteChord || nowUtc - _lastFocusToggleUtc >= TimeSpan.FromMilliseconds(350)))
        {
            _lastManualFocusSlot = pressedSlot;
            if (_lastFocusedLeads && _mainSlot != pressedSlot)
            {
                _mainSlot = pressedSlot;
                var newMain = _routes[pressedSlot];
                newMain.Automation = default;
                newMain.Active = false;
                newMain.EligibleForManualMovement = false;
            }
            _isolatedSlot = _isolatedSlot == pressedSlot ? 0 : pressedSlot;
            _focusedSlot = _isolatedSlot == 0 ? _mainSlot : _isolatedSlot;
            _lastFocusToggleUtc = nowUtc;
        }
        _previousButtons = gamepad.Buttons;
        _previousLeftTrigger = gamepad.LeftTrigger;
        gamepad.Buttons &= unchecked((ushort)~(leftThumb | rightThumb));
        if (leftTrigger && ((up && _focusPads.ContainsKey(5)) ||
                            (down && _focusPads.ContainsKey(6))))
            gamepad.Buttons &= unchecked((ushort)~(dpadUp | dpadDown));
        if (leftTrigger && ((left && leftRole == 3) || (right && rightRole == 4) ||
                            (up && _focusPads.ContainsKey(5)) ||
                            (down && _focusPads.ContainsKey(6))))
            gamepad.LeftTrigger = 0;
        _physicalConnected = true;
        _allowDirectRightStick = allowDirectRightStick;
        _manualX = gamepad.RightX;
        _manualY = gamepad.RightY;
        if (_rightStickFollowerOverride && _isolatedSlot == 0)
        {
            gamepad.RightX = 0;
            gamepad.RightY = 0;
        }
        _physicalReport = PadReport.FromPhysical(gamepad);
        if (previousFocus != _focusedSlot || previousIsolation != _isolatedSlot)
            ReleaseCombatAll();
        if (previousFocus != _focusedSlot || previousIsolation != _isolatedSlot ||
            _rightStickFollowerOverride || _followerMirrorEnabled) RenderAll();
        else Render(_focusedSlot, _routes[_focusedSlot]);
    }

    public void PhysicalDisconnected()
    {
        if (!_physicalMode) return;
        NeutralAll();
        _isolatedSlot = 0;
        _mainSlot = _initialMainSlot;
        _focusedSlot = _mainSlot;
        _lastManualFocusSlot = _mainSlot;
        _lastFocusToggleUtc = DateTime.MinValue;
    }

    public void SetFollowerMirrorEnabled(bool enabled)
    {
        enabled = enabled && _physicalMode && _physicalConnected;
        if (_followerMirrorEnabled == enabled) return;
        _followerMirrorEnabled = enabled;
        foreach (var route in _routes.Values) route.DodgeSprint.Reset();
        RenderAll();
    }

    public void SetMenuGuardActive(bool active)
    {
        if (_menuGuardActive == active) return;
        _menuGuardActive = active;
        if (active)
        {
            ReleaseCombatAll();
            foreach (var route in _routes.Values)
                route.LifeUntilUtc = route.ManaUntilUtc = DateTime.MinValue;
        }
        RenderAll();
    }

    public bool BeginAutoParty(DateTime nowUtc)
    {
        if ((!_physicalMode && !HybridPairingComplete) || _autoPartyActive ||
            nowUtc.Kind != DateTimeKind.Utc) return false;
        NeutralAll();
        _autoPartyActive = true;
        _autoPartyExpiresUtc = nowUtc.AddSeconds(10);
        return true;
    }

    public bool PulseAutoParty(int slot, ushort buttons, int holdMilliseconds, DateTime nowUtc)
    {
        if (_autoPartyActive && nowUtc.Kind == DateTimeKind.Utc &&
            _autoPartyButtons != 0 && nowUtc >= _autoPartyButtonsUntilUtc)
        {
            var releasedSlot = _autoPartySlot;
            _autoPartySlot = 0;
            _autoPartyButtons = 0;
            _autoPartyButtonsUntilUtc = default;
            Render(releasedSlot, _routes[releasedSlot]);
        }
        if (!_autoPartyActive || !_routes.ContainsKey(slot) ||
            (_hybridKeyboard is not null && slot != 1 && !_hybridPlayerPads.Values.Contains(slot)) ||
            nowUtc.Kind != DateTimeKind.Utc || nowUtc >= _autoPartyExpiresUtc ||
            !BridgeProtocol.ValidAutoPartyPulse(buttons, holdMilliseconds) ||
            _autoPartyButtons != 0) return false;
        _autoPartySlot = slot;
        _autoPartyButtons = buttons;
        _autoPartyButtonsUntilUtc = nowUtc.AddMilliseconds(holdMilliseconds);
        _autoPartyExpiresUtc = nowUtc.AddSeconds(10);
        Render(slot, _routes[slot]);
        return true;
    }

    public bool RenewAutoParty(DateTime nowUtc)
    {
        if (!_autoPartyActive || nowUtc.Kind != DateTimeKind.Utc || nowUtc >= _autoPartyExpiresUtc)
            return false;
        _autoPartyExpiresUtc = nowUtc.AddSeconds(10);
        return true;
    }

    public void EndAutoParty()
    {
        if (!_autoPartyActive) return;
        NeutralAll();
    }

    public bool PulseFlask(int slot, string resource, DateTime nowUtc)
    {
        if (!_routes.TryGetValue(slot, out var route) || !_physicalMode || !_physicalConnected ||
            _menuGuardActive || (_isolatedSlot != 0 && slot != _focusedSlot) ||
            route.CombatLeaseUntilUtc > nowUtc ||
            nowUtc.Kind != DateTimeKind.Utc || resource is not ("life" or "mana"))
            return false;
        if (resource == "life") route.LifeUntilUtc = nowUtc.AddMilliseconds(120);
        else route.ManaUntilUtc = nowUtc.AddMilliseconds(120);
        Render(slot, route);
        return true;
    }

    public void Tick(DateTime nowUtc)
    {
        if (_autoPartyActive)
        {
            if (nowUtc >= _autoPartyExpiresUtc) { EndAutoParty(); return; }
            if (_autoPartyButtons != 0 && nowUtc >= _autoPartyButtonsUntilUtc)
            {
                var slot = _autoPartySlot;
                _autoPartySlot = 0;
                _autoPartyButtons = 0;
                _autoPartyButtonsUntilUtc = default;
                Render(slot, _routes[slot]);
            }
            return;
        }
        foreach (var (slot, route) in _routes)
        {
            var expired = false;
            if (route.CombatLeaseUntilUtc != default && nowUtc >= route.CombatLeaseUntilUtc)
            {
                route.CombatLeaseUntilUtc = default;
                route.CombatPulseUntilUtc = default;
                route.CombatPulse = default;
                expired = true;
            }
            else if (route.CombatPulseUntilUtc != default && nowUtc >= route.CombatPulseUntilUtc)
            {
                route.CombatPulseUntilUtc = default;
                route.CombatPulse = default;
                expired = true;
            }
            if (route.LifeUntilUtc != DateTime.MinValue && nowUtc >= route.LifeUntilUtc)
            {
                route.LifeUntilUtc = DateTime.MinValue;
                expired = true;
            }
            if (route.ManaUntilUtc != DateTime.MinValue && nowUtc >= route.ManaUntilUtc)
            {
                route.ManaUntilUtc = DateTime.MinValue;
                expired = true;
            }
            if ((route.Active || route.EligibleForManualMovement) &&
                nowUtc - route.LastUpdateUtc > TimeSpan.FromMilliseconds(500))
            {
                route.Automation = default;
                route.Active = false;
                route.EligibleForManualMovement = false;
                Render(slot, route);
            }
            else if (expired) Render(slot, route);
        }
    }

    public void NeutralAll()
    {
        _hybridFocusRequested = false;
        _hybridConfirmReleased = false;
        _hybridKeyboard?.SetForeground(false);
        _hybridKeyboard?.SetMenuOpen(false);
        _autoPartyActive = false;
        _autoPartyExpiresUtc = default;
        _autoPartySlot = 0;
        _autoPartyButtons = 0;
        _autoPartyButtonsUntilUtc = default;
        _menuGuardActive = false;
        _physicalConnected = false;
        _followerMirrorEnabled = false;
        _physicalReport = default;
        _manualX = 0;
        _manualY = 0;
        _allowDirectRightStick = false;
        _previousButtons = 0;
        _previousLeftTrigger = 0;
        _reportBatch?.BeginBatch();
        try
        {
            foreach (var route in _routes.Values)
            {
                route.Automation = default;
                route.Active = false;
                route.EligibleForManualMovement = false;
                route.LifeUntilUtc = route.ManaUntilUtc = DateTime.MinValue;
                route.CombatLeaseUntilUtc = route.CombatPulseUntilUtc = default;
                route.CombatPulse = default;
                route.DodgeSprint.Reset();
                route.Output.SetReport(default);
            }
            _reportBatch?.EndBatch();
        }
        catch { _reportBatch?.AbortBatch(); throw; }
    }

    private void RenderAll()
    {
        _reportBatch?.BeginBatch();
        try
        {
            foreach (var (slot, route) in _routes) Render(slot, route);
            _reportBatch?.EndBatch();
        }
        catch { _reportBatch?.AbortBatch(); throw; }
    }

    private void Render(int slot, PadRoute route)
    {
        if (_autoPartyActive)
        {
            route.DodgeSprint.Reset();
            route.Output.SetReport(slot == _autoPartySlot && _autoPartyButtons != 0
                ? new PadReport(_autoPartyButtons, 0, 0, 0, 0, 0, 0) : default);
            return;
        }
        if (_hybridKeyboard is not null && !_hybridKeyboard.GameForeground)
        {
            route.Output.SetReport(default);
            return;
        }
        if (_hybridKeyboard is not null && !_hybridPlayerPads.Values.Contains(slot))
        {
            route.Output.SetReport(default);
            return;
        }
        if (_hybridKeyboard is not null && _hybridKeyboard.FocusedPlayer != 1)
        {
            var focused = _hybridPlayerPads[_hybridKeyboard.FocusedPlayer];
            route.Output.SetReport(slot == focused ? _hybridKeyboard.Snapshot().Report : default);
            return;
        }
        if (_physicalMode && slot == _focusedSlot)
        {
            route.DodgeSprint.Reset();
            route.Output.SetReport(_physicalConnected ? WithFlasks(_physicalReport, route) : default);
            return;
        }
        if (_menuGuardActive)
        {
            route.DodgeSprint.Reset();
            route.Output.SetReport(default);
            return;
        }
        if (_physicalMode && _isolatedSlot != 0)
        {
            route.DodgeSprint.Reset();
            route.Output.SetReport(default);
            return;
        }
        if (route.CombatPulseUntilUtc != default)
        {
            route.DodgeSprint.Reset();
            route.Output.SetReport(route.CombatPulse);
            return;
        }
        if (_rightStickFollowerOverride && _physicalConnected && _isolatedSlot == 0 &&
            (route.EligibleForManualMovement ||
             (_allowDirectRightStick && slot == _directRightStickSlot)) &&
            (Math.Abs((int)_manualX) > 8000 || Math.Abs((int)_manualY) > 8000))
        {
            route.Output.SetReport(WithFlasks(WithFollowerMirror(
                PadReport.FromMovement(_manualX, _manualY), slot, route), route));
            return;
        }
        var automation = route.Automation;
        if (_hybridKeyboard is not null && _playerForPad.TryGetValue(slot, out var hybridPlayer))
            automation = automation with
            {
                Buttons = (ushort)(automation.Buttons |
                    (_hybridKeyboard.MainFollowMirrorButtons & _followerMirror.ButtonsForPlayer(hybridPlayer)))
            };
        route.Output.SetReport(WithFlasks(WithFollowerMirror(automation, slot, route), route));
    }

    private PadReport WithFollowerMirror(PadReport automation, int slot, PadRoute route)
    {
        if (!_followerMirrorEnabled || !_physicalConnected)
        {
            route.DodgeSprint.Reset();
            return automation;
        }
        if (!_playerForPad.TryGetValue(slot, out var player))
        {
            route.DodgeSprint.Reset();
            return automation;
        }
        var input = (ushort)(_physicalReport.Buttons & _followerMirror.ButtonsForPlayer(player));
        var buttons = route.DodgeSprint.Resolve(input,
            (automation.Buttons & 0x2000) != 0, DateTime.UtcNow);
        return automation with
        {
            Buttons = (ushort)((automation.Buttons & ~0x2000) | buttons),
            LeftTrigger = _followerMirror.LeftTriggerForPlayer(player) ? _physicalReport.LeftTrigger : (byte)0,
            RightTrigger = _followerMirror.RightTriggerForPlayer(player) ? _physicalReport.RightTrigger : (byte)0
        };
    }

    private PadReport WithFlasks(PadReport report, PadRoute route)
    {
        if (_menuGuardActive) return report;
        var buttons = (ushort)((route.LifeUntilUtc != DateTime.MinValue ? 0x0004 : 0) |
                               (route.ManaUntilUtc != DateTime.MinValue ? 0x0008 : 0));
        return report with { Buttons = (ushort)(report.Buttons | buttons) };
    }

    private void ReleaseCombatAll()
    {
        foreach (var route in _routes.Values)
        {
            route.CombatLeaseUntilUtc = default;
            route.CombatPulseUntilUtc = default;
            route.CombatPulse = default;
        }
    }

    private sealed class PadRoute(IPadOutput output)
    {
        public IPadOutput Output { get; } = output;
        public PadReport Automation { get; set; }
        public DateTime LastUpdateUtc { get; set; }
        public bool Active { get; set; }
        public bool EligibleForManualMovement { get; set; }
        public DateTime LifeUntilUtc { get; set; }
        public DateTime ManaUntilUtc { get; set; }
        public DateTime CombatLeaseUntilUtc { get; set; }
        public DateTime CombatPulseUntilUtc { get; set; }
        public PadReport CombatPulse { get; set; }
        public DodgeSprintButtonArbiter DodgeSprint { get; } = new();
    }
}
