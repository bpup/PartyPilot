using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;

namespace PartyPilot.Desktop;

public sealed class RadarOverlayWindow : Window
{
    private const int ExStyle = -20;
    private static readonly nint Transparent = 0x20;
    private static readonly nint Layered = 0x80000;
    private static readonly nint NoActivate = 0x8000000;
    private static readonly nint ToolWindow = 0x80;
    private readonly LaunchedClient _client;
    private readonly double _scale;
    private readonly RadarVisibility _visibility = new();
    private readonly Canvas _canvas = new();
    private readonly DispatcherTimer _positionTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private RadarFrame? _frame;
    public bool TargetIsLost { get; private set; }

    public event Action? TargetLost;

    public RadarOverlayWindow(LaunchedClient client, double scale)
    {
        _client = client;
        _scale = scale;
        Title = $"PartyPilot Radar · {client.Pid}";
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
        Opened += (_, _) =>
        {
            if (OperatingSystem.IsWindows())
            {
                EnableClickThrough();
                _positionTimer.Start();
                SyncTarget();
            }
        };
        Closed += (_, _) => _positionTimer.Stop();
        SizeChanged += (_, _) => Draw();
        _positionTimer.Tick += (_, _) => SyncTarget();
    }

    public void Apply(RadarFrame frame)
    {
        if (frame.ProcessId != _client.Pid) return;
        _frame = frame;
        Draw();
    }

    private void Draw()
    {
        _canvas.Children.Clear();
        if (_frame == null || DateTime.UtcNow - _frame.SeenUtc > TimeSpan.FromSeconds(2)) return;
        var centerX = Bounds.Width / 2;
        var centerY = Bounds.Height / 2;
        AddDot(centerX, centerY, 12, Brushes.White);
        foreach (var marker in _frame.Markers)
        {
            var (x, y) = RadarProjection.Project(_frame.PlayerX, _frame.PlayerY,
                marker, centerX, centerY, _scale);
            if (!double.IsFinite(x) || !double.IsFinite(y) ||
                x < 8 || y < 8 || x > Bounds.Width - 8 || y > Bounds.Height - 8) continue;
            AddDot(x, y, marker.Category == "monster" ? 7 : 10, MarkerBrush(marker.Category));
            if (marker.Category is "checkpoint" or "waypoint" or "portal" or "transition")
            {
                var label = new TextBlock
                {
                    Text = marker.Path.Split('/').Last(),
                    Foreground = Brushes.White,
                    Background = new SolidColorBrush(Color.FromArgb(190, 8, 12, 20)),
                    FontSize = 12
                };
                Canvas.SetLeft(label, x + 9);
                Canvas.SetTop(label, y - 8);
                _canvas.Children.Add(label);
            }
        }
    }

    private void AddDot(double x, double y, double diameter, IBrush brush)
    {
        var circle = new Ellipse
        {
            Width = diameter,
            Height = diameter,
            Fill = brush,
            Stroke = Brushes.Black,
            StrokeThickness = 1
        };
        Canvas.SetLeft(circle, x - diameter / 2);
        Canvas.SetTop(circle, y - diameter / 2);
        _canvas.Children.Add(circle);
    }

    private static IBrush MarkerBrush(string category) => category switch
    {
        "monster" => Brushes.OrangeRed,
        "portal" => Brushes.MediumPurple,
        "waypoint" => Brushes.DeepSkyBlue,
        "checkpoint" => Brushes.LimeGreen,
        "transition" => Brushes.Gold,
        "chest" => Brushes.Gold,
        "npc" => Brushes.LightSkyBlue,
        "shrine" => Brushes.LightGreen,
        _ => Brushes.White
    };

    private void SyncTarget()
    {
        if (!OperatingSystem.IsWindows()) return;
        var handle = (nint)_client.WindowHandle;
        if (!IsWindow(handle) || GetWindowThreadProcessId(handle, out var processId) == 0 ||
            processId != _client.Pid || !GetClientRect(handle, out var rect) ||
            rect.Right <= rect.Left || rect.Bottom <= rect.Top)
        {
            _positionTimer.Stop();
            Opacity = 0;
            TargetIsLost = true;
            TargetLost?.Invoke();
            return;
        }
        var point = new NativePoint();
        if (!ClientToScreen(handle, ref point))
        {
            Opacity = 0;
            return;
        }
        Position = new PixelPoint(point.X, point.Y);
        var scaling = RenderScaling <= 0 ? 1 : RenderScaling;
        Width = (rect.Right - rect.Left) / scaling;
        Height = (rect.Bottom - rect.Top) / scaling;
        var foreground = GetForegroundWindow() == handle;
        _visibility.Observe(foreground, GetAsyncKeyState(0x09) < 0);
        Opacity = foreground && _visibility.Enabled ? 1 : 0;
        if (_frame != null && DateTime.UtcNow - _frame.SeenUtc > TimeSpan.FromSeconds(2))
        {
            _frame = null;
            Draw();
        }
    }

    private void EnableClickThrough()
    {
        var handle = TryGetPlatformHandle()?.Handle ?? 0;
        if (handle == 0) throw new InvalidOperationException("Radar overlay has no native window handle.");
        Marshal.SetLastPInvokeError(0);
        var oldStyle = GetWindowLongPtr(handle, ExStyle);
        if (oldStyle == 0 && Marshal.GetLastPInvokeError() != 0)
            throw new InvalidOperationException("The radar overlay could not read native window styles.");
        Marshal.SetLastPInvokeError(0);
        var result = SetWindowLongPtr(handle, ExStyle, oldStyle | Transparent | Layered | NoActivate | ToolWindow);
        if (result == 0 && Marshal.GetLastPInvokeError() != 0)
            throw new InvalidOperationException("The radar overlay could not enable click-through mode.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("user32.dll")] private static extern bool IsWindow(nint handle);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint handle, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint handle, ref NativePoint point);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern nint GetWindowLongPtr(nint handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern nint SetWindowLongPtr(nint handle, int index, nint value);
}
