using System.Runtime.InteropServices;

namespace PartyPilot.Desktop;

// A low-level hook avoids taking the chosen key from unrelated applications.
internal sealed class AutoPartyKeyboardHook : IDisposable
{
    private const int HookKeyboardLowLevel = 13;
    private const int KeyDown = 0x100;
    private const int KeyUp = 0x101;
    private const int SysKeyDown = 0x104;
    private const int SysKeyUp = 0x105;
    private readonly Func<bool> _gameForeground;
    private readonly Action _pressed;
    private readonly HookProcedure _procedure;
    private readonly AutoPartyHotkeyGate _gate = new();
    private int _virtualKey = 0x77;
    private nint _handle;
    internal bool IsStarted => _handle != 0;

    internal AutoPartyKeyboardHook(Func<bool> gameForeground, Action pressed)
    {
        _gameForeground = gameForeground;
        _pressed = pressed;
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

    internal void SetKey(int virtualKey)
    {
        _virtualKey = virtualKey;
        _gate.Reset();
    }

    private nint Callback(int code, nint message, nint data)
    {
        if (code < 0 || (long)message is not (KeyDown or KeyUp or SysKeyDown or SysKeyUp) ||
            _virtualKey == 0 || Marshal.ReadInt32(data) != _virtualKey)
            return CallNextHookEx(_handle, code, message, data);
        try
        {
            var result = _gate.Handle((long)message is KeyDown or SysKeyDown,
                _gameForeground());
            if (result.Trigger) _pressed();
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
