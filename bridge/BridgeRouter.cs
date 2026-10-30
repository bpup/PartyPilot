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
    private readonly RightStickEngageDodge _engageDodge = new();
    private bool _engageDodgeEnabled;
    private string _engageDodgeBinding = "B";
    private long _engageDodgeNow;
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
    private bool _warpToMainShortcutEnabled;
    private bool _warpToMainWasPressed;
    public bool WarpToMainPressed { get; private set; }
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
        route.CombatChordFullAtUtc = default;
        route.CombatPulse = default;
        route.LifeUntilUtc = route.ManaUntilUtc = DateTime.MinValue;
        route.RecoveryUntilUtc.Clear();
        ResetEngageDodgeForSlot(slot);
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
        var chord = pulse.LeftTrigger == 255 && pulse.Buttons != 0;
        route.CombatChordFullAtUtc = chord ? nowUtc.AddMilliseconds(45) : default;
        route.CombatPulseUntilUtc = nowUtc.AddMilliseconds(holdMilliseconds + (chord ? 45 : 0));
        route.CombatLeaseUntilUtc = nowUtc.AddSeconds(10);
        ResetEngageDodgeForSlot(slot);
        Render(slot, route);
        return true;
    }

    public void EndCombat(int slot)
    {
        if (!_routes.TryGetValue(slot, out var route)) return;
        route.CombatLeaseUntilUtc = default;
        route.CombatPulseUntilUtc = default;
        route.CombatChordFullAtUtc = default;
        route.CombatPulse = default;
        Render(slot, route);
    }

    public int FocusedSlot => _physicalMode ? _focusedSlot : 0;
    public void SetWarpToMainShortcutEnabled(bool enabled)
    {
        _warpToMainShortcutEnabled = enabled && _physicalMode;
        _warpToMainWasPressed = _warpToMainShortcutEnabled &&
            (_physicalReport.Buttons & 0x0008) != 0 && _physicalReport.LeftTrigger > 30;
        WarpToMainPressed = false;
    }
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
        bool lastFocusedLeads = false, FollowerMirrorConfig? followerMirror = null,
        bool engageDodgeEnabled = false, string engageDodgeBinding = "B")
    {
        if (_hybridKeyboard is not null || !_routes.ContainsKey(mainSlot)) return false;
        if (engageDodgeEnabled && (!rightStickFollowerOverride ||
            !RightStickEngageDodge.TryNormalizeBinding(engageDodgeBinding, out engageDodgeBinding)))
            return false;
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
        _engageDodgeEnabled = engageDodgeEnabled;
        _engageDodgeBinding = engageDodgeBinding;
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
            route.SuppressMirroredB = false;
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
        route.SuppressMirroredB = command.SuppressMirroredB;
        route.CatchUpPriority = command.CatchUpPriority;
        route.LastUpdateUtc = nowUtc;
        Render(command.Slot, route);
        return true;
    }

    public void SetPhysical(XInputGamepad gamepad, DateTime? observedAtUtc = null,
        bool allowDirectRightStick = true)
    {
        WarpToMainPressed = false;
        if (!_physicalMode) return;
        var warpChord = _warpToMainShortcutEnabled &&
            (gamepad.Buttons & 0x0008) != 0 && gamepad.LeftTrigger > 30;
        WarpToMainPressed = warpChord && !_warpToMainWasPressed;
        _warpToMainWasPressed = warpChord;
        if (_autoPartyActive) return;
        const ushort leftThumb = 0x0040;
        const ushort rightThumb = 0x0080;
        const ushort dpadUp = 0x0001;
        const ushort dpadDown = 0x0002;
        var nowUtc = observedAtUtc ?? DateTime.UtcNow;
        _engageDodgeNow = nowUtc.Ticks / TimeSpan.TicksPerMillisecond;
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
        var pressedRole = 0;
        var highestParts = 0;
        var tied = false;
        void Consider(bool edge, int role)
        {
            if (!edge) return;
            var parts = role >= 3 ? 2 : 1;
            if (parts > highestParts)
            {
                pressedRole = role;
                highestParts = parts;
                tied = false;
            }
            else if (parts == highestParts) tied = true;
        }
        Consider(leftEdge, leftRole);
        Consider(rightEdge, rightRole);
        Consider(fifthEdge, 5);
        Consider(sixthEdge, 6);
        if (tied) pressedRole = 0;
        // While one player has manual focus, the reserved P3–P6 chords can
        // immediately switch to another player. Repeating the active chord
        // still needs a release edge before returning to shared control.
        var promoteChord = pressedRole is >= 3 and <= 6 && _isolatedSlot != 0;
        // A second edge on the already focused role returns to shared follow
        // even inside the normal 350 ms switch debounce.
        if (pressedRole != 0 && _focusPads.TryGetValue(pressedRole, out var pressedSlot) &&
            (promoteChord ||
             pressedSlot == _isolatedSlot && nowUtc > _lastFocusToggleUtc ||
             nowUtc - _lastFocusToggleUtc >= TimeSpan.FromMilliseconds(350)))
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
        if (warpChord)
        {
            gamepad.Buttons &= unchecked((ushort)~0x0008);
            gamepad.LeftTrigger = 0;
        }
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
        {
            ReleaseCombatAll();
            _engageDodge.Reset();
        }
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
            _engageDodge.Reset();
            ReleaseCombatAll();
            foreach (var route in _routes.Values)
            {
                route.LifeUntilUtc = route.ManaUntilUtc = DateTime.MinValue;
                route.RecoveryUntilUtc.Clear();
            }
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

    public bool PulseRecoveryAction(int slot, string input, DateTime nowUtc)
    {
        if (!_routes.TryGetValue(slot, out var route) || !_physicalMode || !_physicalConnected ||
            _autoPartyActive || _menuGuardActive || (_isolatedSlot != 0 && slot != _focusedSlot) ||
            route.CombatLeaseUntilUtc > nowUtc || nowUtc.Kind != DateTimeKind.Utc ||
            !BridgeProtocol.IsRecoveryInput(input)) return false;
        var until = nowUtc.AddMilliseconds(120);
        if (!route.RecoveryUntilUtc.TryGetValue(input, out var previous) || until > previous)
            route.RecoveryUntilUtc[input] = until;
        Render(slot, route);
        return true;
    }

    public bool PulseAutoCast(int slot, string input, DateTime nowUtc)
    {
        if (!_routes.TryGetValue(slot, out var route) || !_physicalMode || !_physicalConnected ||
            _autoPartyActive || _menuGuardActive || (_isolatedSlot != 0 && slot != _focusedSlot) ||
            route.CombatLeaseUntilUtc > nowUtc || nowUtc.Kind != DateTimeKind.Utc ||
            !BridgeProtocol.IsAutoCastInput(input)) return false;
        var until = nowUtc.AddMilliseconds(120);
        if (!route.RecoveryUntilUtc.TryGetValue(input, out var previous) || until > previous)
            route.RecoveryUntilUtc[input] = until;
        Render(slot, route);
        return true;
    }

    public void Tick(DateTime nowUtc)
    {
        _engageDodgeNow = nowUtc.Ticks / TimeSpan.TicksPerMillisecond;
        if (_autoPartyActive)
        {
            if (nowUtc >= _autoPartyExpiresUtc)
            {
                EndAutoParty();
                RefreshPrivateReports();
                return;
            }
            if (_autoPartyButtons != 0 && nowUtc >= _autoPartyButtonsUntilUtc)
            {
                var slot = _autoPartySlot;
                _autoPartySlot = 0;
                _autoPartyButtons = 0;
                _autoPartyButtonsUntilUtc = default;
                Render(slot, _routes[slot]);
            }
            RefreshPrivateReports();
            return;
        }
        foreach (var (slot, route) in _routes)
        {
            var expired = false;
            if (route.CombatLeaseUntilUtc != default && nowUtc >= route.CombatLeaseUntilUtc)
            {
                route.CombatLeaseUntilUtc = default;
                route.CombatPulseUntilUtc = default;
                route.CombatChordFullAtUtc = default;
                route.CombatPulse = default;
                expired = true;
            }
            else if (route.CombatPulseUntilUtc != default && nowUtc >= route.CombatPulseUntilUtc)
            {
                route.CombatPulseUntilUtc = default;
                route.CombatChordFullAtUtc = default;
                route.CombatPulse = default;
                expired = true;
            }
            else if (route.CombatChordFullAtUtc != default && nowUtc >= route.CombatChordFullAtUtc)
            {
                route.CombatChordFullAtUtc = default;
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
            foreach (var input in route.RecoveryUntilUtc.Where(entry => nowUtc >= entry.Value)
                         .Select(entry => entry.Key).ToArray())
            {
                route.RecoveryUntilUtc.Remove(input);
                expired = true;
            }
            if ((route.Active || route.EligibleForManualMovement) &&
                nowUtc - route.LastUpdateUtc > TimeSpan.FromMilliseconds(500))
            {
                route.Automation = default;
                route.Active = false;
                route.EligibleForManualMovement = false;
                route.SuppressMirroredB = false;
                route.CatchUpPriority = false;
                Render(slot, route);
            }
            else if (expired || _engageDodgeEnabled) Render(slot, route);
        }
        RefreshPrivateReports();
    }

    // The private XInput reader neutralizes a sample after 80 ms. Keep the
    // current reports fresh while a direction or button is held unchanged.
    private void RefreshPrivateReports()
    {
        if (_reportBatch is null) return;
        _reportBatch.BeginBatch();
        try { _reportBatch.EndBatch(); }
        catch { _reportBatch.AbortBatch(); throw; }
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
        _engageDodge.Reset();
        _physicalConnected = false;
        _followerMirrorEnabled = false;
        _physicalReport = default;
        _manualX = 0;
        _manualY = 0;
        _allowDirectRightStick = false;
        _previousButtons = 0;
        _previousLeftTrigger = 0;
        WarpToMainPressed = false;
        _reportBatch?.BeginBatch();
        try
        {
            foreach (var route in _routes.Values)
            {
                route.Automation = default;
                route.Active = false;
                route.EligibleForManualMovement = false;
                route.SuppressMirroredB = false;
                route.CatchUpPriority = false;
                route.LifeUntilUtc = route.ManaUntilUtc = DateTime.MinValue;
                route.RecoveryUntilUtc.Clear();
                route.CombatLeaseUntilUtc = route.CombatPulseUntilUtc = default;
                route.CombatChordFullAtUtc = default;
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
            route.Output.SetReport(_physicalConnected ? WithRecovery(_physicalReport, route) : default);
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
            var pulse = route.CombatChordFullAtUtc == default ? route.CombatPulse :
                route.CombatPulse with { Buttons = 0, RightTrigger = 0 };
            route.Output.SetReport(WithEngageDodge(pulse, slot, false));
            return;
        }
        if (_rightStickFollowerOverride && _physicalConnected && _isolatedSlot == 0 &&
            (route.EligibleForManualMovement ||
             (_allowDirectRightStick && slot == _directRightStickSlot)) &&
            (Math.Abs((int)_manualX) > 8000 || Math.Abs((int)_manualY) > 8000))
        {
            route.Output.SetReport(WithEngageDodge(WithRecovery(WithFollowerMirror(
                PadReport.FromMovement(_manualX, _manualY), slot, route, allowPriority: false), route), slot, true));
            return;
        }
        var automation = route.Automation;
        if (_hybridKeyboard is not null && _playerForPad.TryGetValue(slot, out var hybridPlayer))
            automation = automation with
            {
                Buttons = (ushort)(automation.Buttons |
                    (_hybridKeyboard.MainFollowMirrorButtons & _followerMirror.ButtonsForPlayer(hybridPlayer)))
            };
        route.Output.SetReport(WithEngageDodge(
            WithRecovery(WithFollowerMirror(automation, slot, route), route), slot,
            route.EligibleForManualMovement || _allowDirectRightStick && slot == _directRightStickSlot));
    }

    private PadReport WithEngageDodge(PadReport report, int slot, bool eligible)
    {
        if (!_engageDodgeEnabled || !_playerForPad.TryGetValue(slot, out var player) ||
            !_playerForPad.TryGetValue(_mainSlot, out var main)) return report;
        return _engageDodge.Apply(player, main, true,
            eligible && _physicalConnected && _isolatedSlot == 0 && !_menuGuardActive,
            _engageDodgeBinding, _manualX, _manualY, report,
            _engageDodgeNow == 0 ? DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond : _engageDodgeNow);
    }

    private void ResetEngageDodgeForSlot(int slot)
    {
        if (_playerForPad.TryGetValue(slot, out var player))
            _engageDodge.ResetPlayer(player);
    }

    private PadReport WithFollowerMirror(PadReport automation, int slot, PadRoute route,
        bool allowPriority = true)
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
        if (allowPriority && route.CatchUpPriority) input &= 0x38FF;
        if (route.SuppressMirroredB) input = (ushort)(input & ~0x2000);
        var buttons = route.DodgeSprint.Resolve(input,
            (automation.Buttons & 0x2000) != 0, DateTime.UtcNow);
        return automation with
        {
            Buttons = (ushort)((automation.Buttons & ~0x2000) | buttons),
            LeftTrigger = allowPriority && route.CatchUpPriority ? (byte)0 :
                _followerMirror.LeftTriggerForPlayer(player) ? _physicalReport.LeftTrigger : (byte)0,
            RightTrigger = allowPriority && route.CatchUpPriority ? (byte)0 :
                _followerMirror.RightTriggerForPlayer(player) ? _physicalReport.RightTrigger : (byte)0
        };
    }

    private PadReport WithRecovery(PadReport report, PadRoute route)
    {
        if (_menuGuardActive) return report;
        var buttons = (ushort)((route.LifeUntilUtc != DateTime.MinValue ? 0x0004 : 0) |
                               (route.ManaUntilUtc != DateTime.MinValue ? 0x0008 : 0));
        var leftTrigger = report.LeftTrigger;
        var rightTrigger = report.RightTrigger;
        foreach (var input in route.RecoveryUntilUtc.Keys)
        {
            switch (input)
            {
                case "A": buttons |= 0x1000; break;
                case "B": buttons |= 0x2000; break;
                case "X": buttons |= 0x4000; break;
                case "Y": buttons |= 0x8000; break;
                case "LB": buttons |= 0x0100; break;
                case "RB": buttons |= 0x0200; break;
                case "D-pad Up": buttons |= 0x0001; break;
                case "D-pad Down": buttons |= 0x0002; break;
                case "LT": leftTrigger = byte.MaxValue; break;
                case "RT": rightTrigger = byte.MaxValue; break;
            }
        }
        return report with { Buttons = (ushort)(report.Buttons | buttons),
            LeftTrigger = leftTrigger, RightTrigger = rightTrigger };
    }

    private void ReleaseCombatAll()
    {
        foreach (var route in _routes.Values)
        {
            route.CombatLeaseUntilUtc = default;
            route.CombatPulseUntilUtc = default;
            route.CombatChordFullAtUtc = default;
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
        public bool SuppressMirroredB { get; set; }
        public bool CatchUpPriority { get; set; }
        public DateTime LifeUntilUtc { get; set; }
        public DateTime ManaUntilUtc { get; set; }
        public Dictionary<string, DateTime> RecoveryUntilUtc { get; } = new(StringComparer.Ordinal);
        public DateTime CombatLeaseUntilUtc { get; set; }
        public DateTime CombatPulseUntilUtc { get; set; }
        public DateTime CombatChordFullAtUtc { get; set; }
        public PadReport CombatPulse { get; set; }
        public DodgeSprintButtonArbiter DodgeSprint { get; } = new();
    }
}
