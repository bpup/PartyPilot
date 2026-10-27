using System.Runtime.InteropServices;

namespace PartyPilot.Desktop;

// A low-level hook avoids taking the chosen key from unrelated applications.
internal sealed class GameKeyboardShortcutHook : IDisposable
{
    private const int HookKeyboardLowLevel = 13;
    private const int KeyDown = 0x100;
    private const int KeyUp = 0x101;
    private const int SysKeyDown = 0x104;
    private const int SysKeyUp = 0x105;
    private readonly Func<bool> _gameForeground;
    private readonly Action _autoPartyPressed;
    private readonly Action _followPausePressed;
    private readonly HookProcedure _procedure;
    private readonly AutoPartyHotkeyGate _autoPartyGate = new();
    private readonly AutoPartyHotkeyGate _followPauseGate = new();
    private int _autoPartyKey = 0x77;
    private int _followPauseKey = 0x76;
    private nint _handle;
    internal bool IsStarted => _handle != 0;

    internal GameKeyboardShortcutHook(Func<bool> gameForeground,
        Action autoPartyPressed, Action followPausePressed)
    {
        _gameForeground = gameForeground;
        _autoPartyPressed = autoPartyPressed;
        _followPausePressed = followPausePressed;
        _procedure = Callback;
    }

    internal bool Start()
    {
        if (!OperatingSystem.IsWindows()) return false;
        if (_handle != 0) return true;
        _handle = SetWindowsHookExW(HookKeyboardLowLevel, _procedure,
            GetModuleHandleW(null), 0);
        return _handle != 0;
    }

    internal bool SetKeys(int autoPartyKey, int followPauseKey)
    {
        if (autoPartyKey != 0 && autoPartyKey == followPauseKey) return false;
        _autoPartyKey = autoPartyKey;
        _followPauseKey = followPauseKey;
        _autoPartyGate.Reset();
        _followPauseGate.Reset();
        return true;
    }

    internal (bool Consume, bool AutoParty, bool FollowPause) HandleKey(
        int key, bool isDown, bool gameForeground)
    {
        if (key == 0) return default;
        if (key == _autoPartyKey)
        {
            var result = _autoPartyGate.Handle(isDown, gameForeground);
            return (result.Consume, result.Trigger, false);
        }
        if (key == _followPauseKey)
        {
            var result = _followPauseGate.Handle(isDown, gameForeground);
            return (result.Consume, false, result.Trigger);
        }
        return default;
    }

    private nint Callback(int code, nint message, nint data)
    {
        if (code < 0 || (long)message is not (KeyDown or KeyUp or SysKeyDown or SysKeyUp))
            return CallNextHookEx(_handle, code, message, data);
        var key = Marshal.ReadInt32(data);
        try
        {
            if (key != _autoPartyKey && key != _followPauseKey)
                return CallNextHookEx(_handle, code, message, data);
            var result = HandleKey(key, (long)message is KeyDown or SysKeyDown,
                _gameForeground());
            if (result.AutoParty) _autoPartyPressed();
            if (result.FollowPause) _followPausePressed();
            if (result.Consume) return 1;
        }
        catch
        {
            // A hook callback must always leave unrelated keyboard input usable.
        }
        return CallNextHookEx(_handle, code, message, data);
    }

    public void Dispose()
    {
        if (_handle == 0) return;
        UnhookWindowsHookEx(_handle);
        _handle = 0;
    }

    private delegate nint HookProcedure(int code, nint message, nint data);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookExW(int hook, HookProcedure procedure, nint module, uint thread);
    [DllImport("kernel32.dll")]
    private static extern nint GetModuleHandleW(string? moduleName);
    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);
}
