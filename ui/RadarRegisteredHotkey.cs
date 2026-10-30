using System.Runtime.InteropServices;

namespace PartyPilot.Desktop;

// A thread-scoped Windows hotkey receives WM_HOTKEY without owning an overlay
// window. When registration fails, RadarSession keeps its key-state fallback.
internal sealed class RadarRegisteredHotkey : IDisposable
{
    private const int HotkeyId = 21251;
    private const uint WmHotkey = 0x0312;
    private const uint WmQuit = 0x0012;
    private const uint NoRepeat = 0x4000;
    private readonly RadarHotkeyBinding _binding;
    private readonly Action _pressed;
    private readonly Thread _thread;
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private uint _threadId;
    private int _disposed;

    internal uint ThreadId => Volatile.Read(ref _threadId);

    private RadarRegisteredHotkey(RadarHotkeyBinding binding, Action pressed)
    {
        _binding = binding;
        _pressed = pressed;
        _thread = new Thread(Run) { IsBackground = true, Name = "PartyPilot radar hotkey" };
        _thread.Start();
    }

    internal static RadarRegisteredHotkey? TryStart(RadarHotkeyBinding binding, Action pressed)
    {
        // Tab is also the game's map key. Keep it in RadarSession's key-state
        // path so the game can receive the original key press.
        if (!OperatingSystem.IsWindows() || UsesPassThroughPolling(binding)) return null;
        var monitor = new RadarRegisteredHotkey(binding, pressed);
        if (monitor._ready.Task.Wait(TimeSpan.FromSeconds(2)) && monitor._ready.Task.Result)
            return monitor;
        monitor.Dispose();
        return null;
    }

    internal static bool UsesPassThroughPolling(RadarHotkeyBinding binding) =>
        binding.VirtualKey == 9;

    private void Run()
    {
        try
        {
            Volatile.Write(ref _threadId, GetCurrentThreadId());
            // Ensure PostThreadMessage can wake this thread during disposal.
            PeekMessage(out _, 0, 0, 0, 0);
            if (Volatile.Read(ref _disposed) != 0)
            {
                _ready.TrySetResult(false);
                return;
            }
            var modifiers = NoRepeat | (_binding.Control ? 2u : 0u) |
                (_binding.Alt ? 1u : 0u) | (_binding.Shift ? 4u : 0u);
            if (!RegisterHotKey(0, HotkeyId, modifiers, (uint)_binding.VirtualKey))
            {
                _ready.TrySetResult(false);
                return;
            }
            _ready.TrySetResult(true);
            try
            {
                while (GetMessage(out var message, 0, 0, 0) > 0)
                {
                    if (message.Message == WmHotkey && message.WParam == HotkeyId &&
                        Volatile.Read(ref _disposed) == 0)
                        _pressed();
                }
            }
            finally { UnregisterHotKey(0, HotkeyId); }
        }
        catch { _ready.TrySetResult(false); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        var id = ThreadId;
        if (id != 0) PostThreadMessage(id, WmQuit, 0, 0);
        if (Thread.CurrentThread != _thread) _thread.Join(TimeSpan.FromSeconds(2));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint Hwnd;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public NativePoint Point;
        public uint Private;
    }

    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(
        nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("user32.dll")] private static extern int GetMessage(
        out NativeMessage message, nint window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PeekMessage(
        out NativeMessage message, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(
        uint threadId, uint message, nuint wParam, nint lParam);
}
