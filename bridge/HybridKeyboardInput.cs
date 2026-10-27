using PartyPilot.Common;

namespace PartyPilot.Bridge;

[Flags]
internal enum HybridAction : uint
{
    None = 0,
    Up = 1 << 0, Left = 1 << 1, Down = 1 << 2, Right = 1 << 3,
    Precision = 1 << 4, Confirm = 1 << 5, Cancel = 1 << 6,
    AlternateCancel = 1 << 7, X = 1 << 8, Y = 1 << 9,
    LeftBumper = 1 << 10, RightBumper = 1 << 11,
    LeftTrigger = 1 << 12, RightTrigger = 1 << 13,
    DpadUp = 1 << 14, DpadDown = 1 << 15,
    DpadLeft = 1 << 16, DpadRight = 1 << 17,
    Start = 1 << 18, View = 1 << 19
}

internal static class HybridKeyboardInput
{
    private static readonly HybridAction[] BoundActions =
    [
        HybridAction.Up, HybridAction.Left, HybridAction.Down, HybridAction.Right,
        HybridAction.Precision, HybridAction.Confirm, HybridAction.Cancel,
        HybridAction.AlternateCancel, HybridAction.X, HybridAction.Y,
        HybridAction.LeftBumper, HybridAction.RightBumper,
        HybridAction.LeftTrigger, HybridAction.RightTrigger,
        HybridAction.DpadUp, HybridAction.DpadDown,
        HybridAction.DpadLeft, HybridAction.DpadRight,
        HybridAction.Start, HybridAction.View
    ];

    internal static HybridAction ActionForKey(int key, HybridBindingConfig? bindings = null)
    {
        bindings ??= HybridBindingConfig.Default;
        var actions = HybridAction.None;
        for (var index = 0; index < BoundActions.Length; index++)
            if (HybridBindingConfig.Matches(bindings.KeyAt(index + 6), key)) actions |= BoundActions[index];
        foreach (var custom in bindings.CustomActions)
            if (HybridBindingConfig.Matches(custom.Key, key)) actions |= BoundActions[custom.ActionIndex - 6];
        return actions;
    }

    internal static int FocusForKey(int key, HybridBindingConfig? bindings = null) =>
        (bindings ?? HybridBindingConfig.Default).FocusForKey(key);

    internal static PadReport Map(HybridAction actions)
    {
        var x = (actions.HasFlag(HybridAction.Right) ? 1f : 0f) -
                (actions.HasFlag(HybridAction.Left) ? 1f : 0f);
        var y = (actions.HasFlag(HybridAction.Up) ? 1f : 0f) -
                (actions.HasFlag(HybridAction.Down) ? 1f : 0f);
        var length = MathF.Sqrt(x * x + y * y);
        if (length > 1f) { x /= length; y /= length; }
        if (actions.HasFlag(HybridAction.Precision)) { x *= 0.45f; y *= 0.45f; }
        ushort buttons = 0;
        if (actions.HasFlag(HybridAction.Confirm)) buttons |= 0x1000;
        if (actions.HasFlag(HybridAction.Cancel) || actions.HasFlag(HybridAction.AlternateCancel)) buttons |= 0x2000;
        if (actions.HasFlag(HybridAction.X)) buttons |= 0x4000;
        if (actions.HasFlag(HybridAction.Y)) buttons |= 0x8000;
        if (actions.HasFlag(HybridAction.LeftBumper)) buttons |= 0x0100;
        if (actions.HasFlag(HybridAction.RightBumper)) buttons |= 0x0200;
        if (actions.HasFlag(HybridAction.DpadUp)) buttons |= 0x0001;
        if (actions.HasFlag(HybridAction.DpadDown)) buttons |= 0x0002;
        if (actions.HasFlag(HybridAction.DpadLeft)) buttons |= 0x0004;
        if (actions.HasFlag(HybridAction.DpadRight)) buttons |= 0x0008;
        if (actions.HasFlag(HybridAction.Start)) buttons |= 0x0010;
        if (actions.HasFlag(HybridAction.View)) buttons |= 0x0020;
        return new PadReport(buttons,
            actions.HasFlag(HybridAction.LeftTrigger) ? byte.MaxValue : (byte)0,
            actions.HasFlag(HybridAction.RightTrigger) ? byte.MaxValue : (byte)0,
            Axis(x), Axis(y), 0, 0);
    }

    internal static PadReport MapMainFollowMirror(HybridAction actions,
        bool normalMainFollowActive, bool mainExplicitlyFocused) =>
        normalMainFollowActive && !mainExplicitlyFocused
            ? Map(actions & (HybridAction.Confirm | HybridAction.Cancel)) : default;

    private static short Axis(float value) => (short)Math.Clamp(
        (int)MathF.Round(Math.Clamp(value, -1f, 1f) * 32767f), -32768, 32767);
}

internal readonly record struct HybridKeyboardSnapshot(int FocusedPlayer, PadReport Report,
    bool FocusRequested = false, bool ConfirmReleased = false);

internal sealed class HybridKeyboardState
{
    private readonly HybridBindingConfig _bindings;
    private readonly HashSet<int> _availablePlayers;
    private readonly HashSet<int> _pressed = [];
    private readonly HashSet<int> _blockedUntilRelease = [];
    private readonly HashSet<int> _mainMirrorBlockedUntilRelease = [];
    private bool _gameForeground;
    private bool _mainMirrorEnabled;
    private bool _mainExplicitFocus;

    internal HybridKeyboardState(IEnumerable<int> availablePlayers, HybridBindingConfig? bindings = null)
    {
        _bindings = bindings ?? HybridBindingConfig.Default;
        _availablePlayers = [.. availablePlayers.Where(player => player is >= 2 and <= 6)];
    }

    internal int FocusedPlayer { get; private set; } = 1;
    internal bool GameForeground => _gameForeground;
    internal bool PairingComplete => _mainMirrorEnabled;
    internal ushort MainFollowMirrorButtons
    {
        get
        {
            if (!_mainMirrorEnabled || !_gameForeground || FocusedPlayer != 1 || _mainExplicitFocus)
                return 0;
            var actions = HybridAction.None;
            foreach (var key in _pressed)
                if (HybridKeyboardInput.FocusForKey(key, _bindings) == 0 &&
                    !_blockedUntilRelease.Contains(key) && !_mainMirrorBlockedUntilRelease.Contains(key))
                    actions |= HybridKeyboardInput.ActionForKey(key, _bindings);
            return HybridKeyboardInput.MapMainFollowMirror(actions, true, false).Buttons;
        }
    }

    internal bool EnableMainFollowMirror()
    {
        if (_mainMirrorEnabled) return false;
        _mainMirrorEnabled = true;
        BlockHeldMainActions();
        return true;
    }

    internal HybridKeyboardSnapshot Handle(int virtualKey, bool down, bool gameForeground)
    {
        SetForeground(gameForeground);
        if (!down)
        {
            var confirmReleased = _gameForeground && FocusedPlayer > 1 &&
                _pressed.Contains(virtualKey) && !_blockedUntilRelease.Contains(virtualKey) &&
                HybridKeyboardInput.FocusForKey(virtualKey, _bindings) == 0 &&
                (HybridKeyboardInput.ActionForKey(virtualKey, _bindings) & HybridAction.Confirm) != 0;
            _pressed.Remove(virtualKey);
            _blockedUntilRelease.Remove(virtualKey);
            _mainMirrorBlockedUntilRelease.Remove(virtualKey);
            return Snapshot() with { ConfirmReleased = confirmReleased };
        }
        if (!_gameForeground)
        {
            _pressed.Add(virtualKey);
            _blockedUntilRelease.Add(virtualKey);
            if (IsMainMirrorAction(virtualKey)) _mainMirrorBlockedUntilRelease.Add(virtualKey);
            return Snapshot();
        }
        if (!_pressed.Add(virtualKey)) return Snapshot();
        if (_blockedUntilRelease.Contains(virtualKey)) return Snapshot();
        var focus = HybridKeyboardInput.FocusForKey(virtualKey, _bindings);
        if (focus != 0 && (focus == 1 || _availablePlayers.Contains(focus)))
        {
            _mainExplicitFocus = focus == 1 && (FocusedPlayer != 1 || !_mainExplicitFocus);
            FocusedPlayer = focus;
            _blockedUntilRelease.UnionWith(_pressed);
            BlockHeldMainActions();
            return Snapshot() with { FocusRequested = true };
        }
        if (IsMainMirrorAction(virtualKey) &&
            (!_mainMirrorEnabled || FocusedPlayer != 1 || _mainExplicitFocus))
            _mainMirrorBlockedUntilRelease.Add(virtualKey);
        return Snapshot();
    }

    internal HybridKeyboardSnapshot SetForeground(bool foreground)
    {
        if (_gameForeground && !foreground)
        {
            _blockedUntilRelease.UnionWith(_pressed);
            BlockHeldMainActions();
        }
        _gameForeground = foreground;
        return Snapshot();
    }

    internal HybridKeyboardSnapshot Snapshot()
    {
        if (!_gameForeground || FocusedPlayer == 1) return new(FocusedPlayer, default);
        var actions = HybridAction.None;
        foreach (var key in _pressed)
            if (HybridKeyboardInput.FocusForKey(key, _bindings) == 0 &&
                !_blockedUntilRelease.Contains(key))
                actions |= HybridKeyboardInput.ActionForKey(key, _bindings);
        return new(FocusedPlayer, HybridKeyboardInput.Map(actions));
    }

    private void BlockHeldMainActions()
    {
        foreach (var key in _pressed)
            if (IsMainMirrorAction(key)) _mainMirrorBlockedUntilRelease.Add(key);
    }

    private bool IsMainMirrorAction(int key) =>
        HybridKeyboardInput.FocusForKey(key, _bindings) == 0 &&
        (HybridKeyboardInput.ActionForKey(key, _bindings) &
            (HybridAction.Confirm | HybridAction.Cancel)) != 0;
}
