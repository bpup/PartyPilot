using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace PartyPilot.Desktop;

public sealed class PrivacyMaskOverlayWindow : Window
{
    private readonly LaunchedClient _client;
    private readonly IReadOnlyList<PrivacyMaskBox> _boxes;
    private readonly Canvas _canvas = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    public bool TargetIsLost { get; private set; }
    public event Action? TargetLost;

    public PrivacyMaskOverlayWindow(LaunchedClient client, IReadOnlyList<PrivacyMaskBox> boxes)
    {
        _client = client;
        _boxes = boxes;
        Title = $"PartyPilot Privacy · {client.Pid}";
        SystemDecorations = SystemDecorations.None;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        CanResize = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Content = _canvas;
        Width = 800;
        Height = 600;
        SizeChanged += (_, _) => Draw();
        Opened += (_, _) =>
        {
            if (!OperatingSystem.IsWindows()) return;
            EnableClickThrough();
            _timer.Start();
            SyncTarget();
        };
        Closed += (_, _) => _timer.Stop();
        _timer.Tick += (_, _) => SyncTarget();
    }

    private void Draw()
    {
        _canvas.Children.Clear();
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        foreach (var box in _boxes)
        {
            var (x, y, width, height) = box.InClient(Bounds.Width, Bounds.Height);
            var mask = new Border { Width = width, Height = height, Background = Brushes.Black };
            Canvas.SetLeft(mask, x);
            Canvas.SetTop(mask, y);
            _canvas.Children.Add(mask);
        }
    }

    private void SyncTarget()
    {
        var handle = (nint)_client.WindowHandle;
        if (!IsWindow(handle) || IsIconic(handle) ||
            GetWindowThreadProcessId(handle, out var pid) == 0 || pid != _client.Pid ||
            !GetClientRect(handle, out var rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
        {
            Opacity = 0;
            TargetIsLost = true;
            _timer.Stop();
            TargetLost?.Invoke();
            return;
        }
        var origin = new NativePoint();
        if (!ClientToScreen(handle, ref origin)) { Opacity = 0; return; }
        Position = new PixelPoint(origin.X, origin.Y);
        var scaling = RenderScaling > 0 ? RenderScaling : 1;
        Width = (rect.Right - rect.Left) / scaling;
        Height = (rect.Bottom - rect.Top) / scaling;
        Opacity = GetForegroundWindow() == handle ? 1 : 0;
    }

    private void EnableClickThrough()
    {
        var handle = TryGetPlatformHandle()?.Handle ?? 0;
        if (handle == 0) throw new InvalidOperationException("Privacy mask has no native window handle.");
        Marshal.SetLastPInvokeError(0);
        var oldStyle = GetWindowLongPtr(handle, -20);
        if (oldStyle == 0 && Marshal.GetLastPInvokeError() != 0)
            throw new InvalidOperationException("Cannot read privacy mask window style.");
        Marshal.SetLastPInvokeError(0);
        var newStyle = oldStyle | (nint)0x20 | (nint)0x80000 | (nint)0x8000000 | (nint)0x80;
        if (SetWindowLongPtr(handle, -20, newStyle) == 0 && Marshal.GetLastPInvokeError() != 0)
            throw new InvalidOperationException("Cannot enable privacy mask click-through.");
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
    [DllImport("user32.dll")] private static extern bool IsWindow(nint handle);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint handle);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint handle, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint handle, ref NativePoint point);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern nint GetWindowLongPtr(nint handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern nint SetWindowLongPtr(nint handle, int index, nint value);
}
