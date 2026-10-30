using System.Runtime.InteropServices;

namespace PartyPilot.Desktop;

internal sealed class WindowsLoginInputPort : ILoginInputPort, IWarpInputPort
{
    private readonly LaunchedClient _client;

    internal WindowsLoginInputPort(LaunchedClient client) => _client = client;
    internal static int NativeInputSize => Marshal.SizeOf<KeyboardInputEvent>();

    public Task<bool> FocusAsync(CancellationToken token) =>
        PoeChatInviteService.FocusExactWindowAsync(_client, token);

    public Task<LoginWindowFrame?> CaptureAsync(CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        var origin = default(NativePoint);
        if (!PoeChatInviteService.IsExactForeground(_client) ||
            !ClientToScreen((nint)_client.WindowHandle, ref origin)) return null;
        var frame = LoginCalibrationCapture.TryCapture(_client);
        return frame is null ? null :
            new LoginWindowFrame(frame.Pixels, frame.Width, frame.Height, origin.X, origin.Y);
    }, token);

    public bool IsExact(LoginWindowFrame frame)
    {
        var origin = default(NativePoint);
        if (!PoeChatInviteService.IsExactForeground(_client) ||
            !GetClientRect((nint)_client.WindowHandle, out var rect) ||
            !ClientToScreen((nint)_client.WindowHandle, ref origin)) return false;
        return rect.Right - rect.Left == frame.Width && rect.Bottom - rect.Top == frame.Height &&
               Math.Abs(origin.X - frame.ScreenX) <= 2 && Math.Abs(origin.Y - frame.ScreenY) <= 2;
    }

    public bool TryGetCursor(out (int X, int Y) cursor)
    {
        cursor = default;
        if (!OperatingSystem.IsWindows() || !GetCursorPos(out var point)) return false;
        cursor = (point.X, point.Y);
        return true;
    }

    public bool Click(int screenX, int screenY)
    {
        if (!PoeChatInviteService.IsExactForeground(_client) || !SetCursorPos(screenX, screenY) ||
            !PoeChatInviteService.IsExactForeground(_client)) return false;
        MouseEvent(2, 0, 0, 0, 0);
        MouseEvent(4, 0, 0, 0, 0);
        return PoeChatInviteService.IsExactForeground(_client);
    }

    public bool RightClick(int screenX, int screenY)
    {
        if (!PoeChatInviteService.IsExactForeground(_client) || !SetCursorPos(screenX, screenY) ||
            !PoeChatInviteService.IsExactForeground(_client)) return false;
        MouseEvent(8, 0, 0, 0, 0);
        MouseEvent(16, 0, 0, 0, 0);
        return PoeChatInviteService.IsExactForeground(_client);
    }

    public bool SendChord(ushort modifier, ushort key) =>
        PoeChatInviteService.IsExactForeground(_client) && Send(
        [
            Key(modifier, false), Key(key, false), Key(key, true), Key(modifier, true)
        ]) && PoeChatInviteService.IsExactForeground(_client);

    public bool SendKey(ushort key) => PoeChatInviteService.IsExactForeground(_client) &&
        Send([Key(key, false), Key(key, true)]) &&
        PoeChatInviteService.IsExactForeground(_client);

    public bool SetClipboard(string value)
    {
        if (!PoeChatInviteService.IsExactForeground(_client) || !TryOpenClipboard()) return false;
        nint block = 0;
        try
        {
            if (!EmptyClipboard()) return false;
            block = GlobalAlloc(0x42, checked((nuint)((value.Length + 1) * sizeof(char))));
            if (block == 0) return false;
            var address = GlobalLock(block);
            if (address == 0) return false;
            try
            {
                var chars = (value + "\0").ToCharArray();
                Marshal.Copy(chars, 0, address, chars.Length);
                Array.Clear(chars);
            }
            finally { GlobalUnlock(block); }
            if (SetClipboardData(13, block) == 0) return false;
            block = 0;
            return PoeChatInviteService.IsExactForeground(_client);
        }
        finally
        {
            CloseClipboard();
            if (block != 0) GlobalFree(block);
        }
    }

    public bool ClearClipboard()
    {
        if (!OperatingSystem.IsWindows() || !TryOpenClipboard()) return false;
        try { return EmptyClipboard(); }
        finally { CloseClipboard(); }
    }

    public void RestoreCursor((int X, int Y) cursor)
    {
        if (OperatingSystem.IsWindows()) SetCursorPos(cursor.X, cursor.Y);
    }

    private static bool TryOpenClipboard()
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            if (OpenClipboard(0)) return true;
            Thread.Sleep(35);
        }
        return false;
    }

    private static KeyboardInputEvent Key(ushort code, bool up) => new()
    {
        Type = 1,
        Data = new InputUnion { Keyboard = new KeyboardData { VirtualKey = code, Flags = up ? 2u : 0u } }
    };

    private static bool Send(KeyboardInputEvent[] events) =>
        SendInput((uint)events.Length, events, Marshal.SizeOf<KeyboardInputEvent>()) == events.Length;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInputEvent { public uint Type; public InputUnion Data; }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardData Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardData
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint window, ref NativePoint point);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", EntryPoint = "mouse_event")]
    private static extern void MouseEvent(uint flags, uint x, uint y, uint data, nuint extra);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count,
        [In] KeyboardInputEvent[] events, int size);
    [DllImport("user32.dll")] private static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern nint SetClipboardData(uint format, nint block);
    [DllImport("kernel32.dll")] private static extern nint GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint block);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint block);
    [DllImport("kernel32.dll")] private static extern nint GlobalFree(nint block);
}
