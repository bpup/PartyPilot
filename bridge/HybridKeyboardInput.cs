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
    // Win32 virtual keys; 65538 is the right mouse button in the desktop hook.
    internal static HybridAction ActionForKey(int key) => key switch
    {
        0x57 => HybridAction.Up, 0x41 => HybridAction.Left,
        0x53 => HybridAction.Down, 0x44 => HybridAction.Right,
        0xA4 or 0x12 => HybridAction.Precision, 0x45 => HybridAction.Confirm,
        0x51 => HybridAction.Cancel, 0x20 => HybridAction.AlternateCancel,
        0x31 => HybridAction.X, 0x32 => HybridAction.Y,
        0x33 => HybridAction.LeftBumper, 0x34 => HybridAction.RightBumper,
        0xA0 or 0x10 => HybridAction.LeftTrigger, 65538 => HybridAction.RightTrigger,
        0x26 => HybridAction.DpadUp, 0x28 => HybridAction.DpadDown,
        0x25 => HybridAction.DpadLeft, 0x27 => HybridAction.DpadRight,
        0x0D => HybridAction.Start, 0x08 => HybridAction.View,
        _ => HybridAction.None
    };

    internal static int FocusForKey(int key) => key is >= 0x70 and <= 0x75 ? key - 0x6F : 0;

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
    bool FocusRequested = false);

internal sealed class HybridKeyboardState
{
    private readonly HashSet<int> _availablePlayers;
    private readonly HashSet<int> _pressed = [];
    private readonly HashSet<int> _blockedUntilRelease = [];
    private bool _gameForeground;

    internal HybridKeyboardState(IEnumerable<int> availablePlayers)
    {
        _availablePlayers = [.. availablePlayers.Where(player => player is >= 2 and <= 6)];
    }

    internal int FocusedPlayer { get; private set; } = 1;
    internal bool GameForeground => _gameForeground;

    internal HybridKeyboardSnapshot Handle(int virtualKey, bool down, bool gameForeground)
    {
        SetForeground(gameForeground);
        if (!down)
        {
            _pressed.Remove(virtualKey);
            _blockedUntilRelease.Remove(virtualKey);
            return Snapshot();
        }
        if (!_gameForeground)
        {
            _pressed.Add(virtualKey);
            _blockedUntilRelease.Add(virtualKey);
            return Snapshot();
        }
        if (!_pressed.Add(virtualKey)) return Snapshot();
        if (_blockedUntilRelease.Contains(virtualKey)) return Snapshot();
        var focus = HybridKeyboardInput.FocusForKey(virtualKey);
        if (focus != 0 && (focus == 1 || _availablePlayers.Contains(focus)))
        {
            FocusedPlayer = focus;
            _blockedUntilRelease.UnionWith(_pressed);
            return Snapshot() with { FocusRequested = true };
        }
        return Snapshot();
    }

    internal HybridKeyboardSnapshot SetForeground(bool foreground)
    {
        if (_gameForeground && !foreground) _blockedUntilRelease.UnionWith(_pressed);
        _gameForeground = foreground;
        return Snapshot();
    }

    internal HybridKeyboardSnapshot Snapshot()
    {
        if (!_gameForeground || FocusedPlayer == 1) return new(FocusedPlayer, default);
        var actions = HybridAction.None;
        foreach (var key in _pressed)
            if (!_blockedUntilRelease.Contains(key)) actions |= HybridKeyboardInput.ActionForKey(key);
        return new(FocusedPlayer, HybridKeyboardInput.Map(actions));
    }
}
