using System.Runtime.InteropServices;
using PartyPilot.Common;

namespace PartyPilot.Desktop;

// A low-level hook avoids taking the chosen key from unrelated applications.
internal sealed class GameKeyboardShortcutHook : IDisposable
{
    private const int HookKeyboardLowLevel = 13;
    private const int HookMouseLowLevel = 14;
    private const int KeyDown = 0x100;
    private const int KeyUp = 0x101;
    private const int SysKeyDown = 0x104;
    private const int SysKeyUp = 0x105;
    private const int RightDown = 0x204;
    private const int RightUp = 0x205;
    private const int MiddleDown = 0x207;
    private const int MiddleUp = 0x208;
    private const int XDown = 0x20B;
    private const int XUp = 0x20C;
    private readonly Func<bool> _gameForeground;
    private readonly Action _autoPartyPressed;
    private readonly Action _followPausePressed;
    private readonly HookProcedure _procedure;
    private readonly HookProcedure _mouseProcedure;
    private readonly AutoPartyHotkeyGate _autoPartyGate = new();
    private readonly AutoPartyHotkeyGate _followPauseGate = new();
    private int _autoPartyKey = 0x77;
    private int _followPauseKey = 0x76;
    private Func<int, bool, bool, bool>? _hybridInput;
    private HybridBindingConfig _hybridBindings = HybridBindingConfig.Default;
    private nint _handle;
    private nint _mouseHandle;
    internal bool IsStarted => _handle != 0 && _mouseHandle != 0;

    internal GameKeyboardShortcutHook(Func<bool> gameForeground,
        Action autoPartyPressed, Action followPausePressed)
    {
        _gameForeground = gameForeground;
        _autoPartyPressed = autoPartyPressed;
        _followPausePressed = followPausePressed;
        _procedure = Callback;
        _mouseProcedure = MouseCallback;
    }

    internal bool Start()
    {
        if (!OperatingSystem.IsWindows()) return false;
        if (IsStarted) return true;
        _handle = SetWindowsHookExW(HookKeyboardLowLevel, _procedure,
            GetModuleHandleW(null), 0);
        if (_handle == 0) return false;
        _mouseHandle = SetWindowsHookExW(HookMouseLowLevel, _mouseProcedure,
            GetModuleHandleW(null), 0);
        if (_mouseHandle != 0) return true;
        UnhookWindowsHookEx(_handle);
        _handle = 0;
        return false;
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

    internal void SetHybridHandler(Func<int, bool, bool, bool>? handler) =>
        _hybridInput = handler;

    internal void SetHybridBindings(HybridBindingConfig bindings) => _hybridBindings = bindings;

    internal bool IsMappedHybridKey(int key) => _hybridBindings.ContainsKey(key);

    internal static bool IsHybridVirtualKey(int key) => HybridBindingConfig.Default.ContainsKey(key);

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

    internal static int MouseVirtualKey(int message, int mouseData)
    {
        if (message is RightDown or RightUp) return 65538;
        if (message is MiddleDown or MiddleUp) return 65539;
        if (message is XDown or XUp)
            return ((mouseData >> 16) & 0xffff) switch
            {
                1 => 65540,
                2 => 65541,
                _ => 0
            };
        return 0;
    }

    internal (bool Consume, bool AutoParty, bool FollowPause) HandleMouse(
        int message, int mouseData, bool gameForeground)
    {
        var key = MouseVirtualKey(message, mouseData);
        return key == 0 ? default : HandleKey(key,
            message is RightDown or MiddleDown or XDown, gameForeground);
    }

    private nint MouseCallback(int code, nint message, nint data)
    {
        if (code < 0) return CallNextHookEx(_mouseHandle, code, message, data);
        var eventId = (int)message;
        if (eventId is not (RightDown or RightUp or MiddleDown or MiddleUp or XDown or XUp) ||
            (Marshal.ReadInt32(data, 12) & 1) != 0)
            return CallNextHookEx(_mouseHandle, code, message, data);
        try
        {
            var mouseData = Marshal.ReadInt32(data, 8);
            var key = MouseVirtualKey(eventId, mouseData);
            if (key != _autoPartyKey && key != _followPauseKey &&
                (_hybridInput is null || !IsMappedHybridKey(key)))
                return CallNextHookEx(_mouseHandle, code, message, data);
            var foreground = _gameForeground();
            var down = eventId is RightDown or MiddleDown or XDown;
            var result = HandleMouse(eventId, mouseData, foreground);
            if (result.AutoParty) _autoPartyPressed();
            if (result.FollowPause) _followPausePressed();
            if (result.Consume || (_hybridInput is not null && IsMappedHybridKey(key) &&
                _hybridInput(key, down, foreground))) return 1;
        }
        catch
        {
            // An input hook must leave unrelated mouse input usable.
        }
        return CallNextHookEx(_mouseHandle, code, message, data);
    }

    private nint Callback(int code, nint message, nint data)
    {
        if (code < 0 || (long)message is not (KeyDown or KeyUp or SysKeyDown or SysKeyUp))
            return CallNextHookEx(_handle, code, message, data);
        var key = Marshal.ReadInt32(data);
        if ((Marshal.ReadInt32(data, 8) & 0x10) != 0)
            return CallNextHookEx(_handle, code, message, data);
        try
        {
            if (key != _autoPartyKey && key != _followPauseKey &&
                (_hybridInput is null || !IsMappedHybridKey(key)))
                return CallNextHookEx(_handle, code, message, data);
            var foreground = _gameForeground();
            var down = (long)message is KeyDown or SysKeyDown;
            var result = HandleKey(key, down, foreground);
            if (result.AutoParty) _autoPartyPressed();
            if (result.FollowPause) _followPausePressed();
            if (result.Consume || (_hybridInput is not null && IsMappedHybridKey(key) &&
                _hybridInput(key, down, foreground))) return 1;
        }
        catch
        {
            // A hook callback must always leave unrelated keyboard input usable.
        }
        return CallNextHookEx(_handle, code, message, data);
    }

    public void Dispose()
    {
        if (_handle != 0) UnhookWindowsHookEx(_handle);
        if (_mouseHandle != 0) UnhookWindowsHookEx(_mouseHandle);
        _handle = _mouseHandle = 0;
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
