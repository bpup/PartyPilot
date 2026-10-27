using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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
    private readonly RadarVisibility _visibility;
    private RadarTerrainCapture? _terrain;
    private readonly bool _renderOnly;
    private readonly Canvas _canvas = new();
    private Canvas? _terrainLayer;
    private Canvas? _markerLayer;
    private RadarFrame? _drawnFrame;
    private readonly DispatcherTimer _positionTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private RadarFrame? _frame;
    private WriteableBitmap? _terrainBitmap;
    private (RadarTerrainCapture Terrain, double GridX, double GridY, int Width, int Height)?
        _terrainRenderKey;
    private DateTime _terrainRenderedAt;
    public bool TargetIsLost { get; private set; }
    public bool TerrainVisible => _terrainBitmap is not null;

    public event Action? TargetLost;

    public RadarOverlayWindow(LaunchedClient client, double scale, RadarVisibility visibility,
        RadarTerrainCapture? terrain = null,
        bool renderOnly = false)
    {
        _client = client;
        _scale = scale;
        _visibility = visibility;
        _terrain = terrain;
        _renderOnly = renderOnly;
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
            if (OperatingSystem.IsWindows() && !_renderOnly)
            {
                EnableClickThrough();
                _positionTimer.Start();
                SyncTarget();
            }
        };
        Closed += (_, _) => _positionTimer.Stop();
        Closed += (_, _) => ReleaseTerrainBitmap();
        NamePrivacy.Changed += OnNamePrivacyChanged;
        Closed += (_, _) => NamePrivacy.Changed -= OnNamePrivacyChanged;
        SizeChanged += (_, _) => Draw();
        _positionTimer.Tick += (_, _) => SyncTarget();
    }

    private void OnNamePrivacyChanged()
    {
        if (Dispatcher.UIThread.CheckAccess()) Draw();
        else Dispatcher.UIThread.Post(Draw);
    }

    public static string DisplayLabel(RadarMarker marker, bool dormantCandidate = false)
    {
        var label = marker.Category switch
        {
            "player" => NamePrivacy.Mask(marker.Label),
            "loot" => marker.Label ?? string.Empty,
            "quest" => marker.Label == "任务目标" ? "任务目标" : "任务线索 · " + LocalizeGenericLabel(marker.Label),
            "boss" => "首领地点 · " + LocalizeGenericLabel(marker.Label),
            "waypoint" => "传送点",
            "checkpoint" => "检查点",
            "portal" => marker.Label is { } portalName ? "传送门 · " + portalName : "传送门",
            "transition" => marker.Label is { } areaName ? "区域入口 · " + LocalizeGenericLabel(areaName) : "区域入口",
            "essence" => "精华",
            _ => marker.Path.Split('/').Last()
        };
        return dormantCandidate || marker.QuestStateCandidate ? "候选 · " + label : label;
    }

    private static string? LocalizeGenericLabel(string? label) => label switch
    {
        "Quest marker" => "任务标记",
        "Campaign landmark" => "战役地点",
        "Campaign boss arena" => "战役首领区域",
        "Campaign exit" => "战役出口",
        "Boss arena" => "首领区域",
        _ => label
    };

    public void Apply(RadarFrame frame)
    {
        if (frame.ProcessId != _client.Pid) return;
        _frame = frame;
        Draw();
    }

    public void Apply(RadarFrame frame, RadarTerrainCapture? terrain)
    {
        if (frame.ProcessId != _client.Pid) return;
        _terrain = terrain;
        Apply(frame);
    }

    public void ApplyMotion(RadarFrame frame, RadarTerrainCapture? terrain)
    {
        if (frame.ProcessId != _client.Pid) return;
        var drawn = _drawnFrame;
        var previousTerrain = _terrain;
        _frame = frame;
        _terrain = terrain;
        if (drawn is null || _markerLayer is null || _terrainLayer is null ||
            !ReferenceEquals(previousTerrain, terrain) ||
            terrain?.Matches(frame) != terrain?.Matches(drawn) ||
            terrain != null && _terrainRenderKey is { } rendered &&
                (rendered.GridX != frame.GridX || rendered.GridY != frame.GridY) &&
                frame.SeenUtc - _terrainRenderedAt >= TimeSpan.FromMilliseconds(250))
        {
            Draw();
            return;
        }
        var centerX = Bounds.Width / 2;
        var centerY = Bounds.Height / 2;
        var (markerX, markerY) = RadarProjection.ProjectGrid(
            frame.PlayerX / RadarProjection.WorldUnitsPerGridUnit,
            frame.PlayerY / RadarProjection.WorldUnitsPerGridUnit,
            drawn.PlayerX / RadarProjection.WorldUnitsPerGridUnit,
            drawn.PlayerY / RadarProjection.WorldUnitsPerGridUnit,
            centerX, centerY, _scale * RadarProjection.WorldUnitsPerGridUnit);
        Canvas.SetLeft(_markerLayer, markerX - centerX);
        Canvas.SetTop(_markerLayer, markerY - centerY);
        if (terrain != null && frame.GridX is { } newX && frame.GridY is { } newY &&
            drawn.GridX is { } oldX && drawn.GridY is { } oldY)
        {
            var (terrainX, terrainY) = RadarProjection.ProjectGrid(newX, newY,
                oldX, oldY, centerX, centerY, _scale * RadarProjection.WorldUnitsPerGridUnit);
            Canvas.SetLeft(_terrainLayer, terrainX - centerX);
            Canvas.SetTop(_terrainLayer, terrainY - centerY);
        }
    }

    private void Draw()
    {
        _canvas.Children.Clear();
        _drawnFrame = null;
        _terrainLayer = null;
        _markerLayer = null;
        if (_frame == null || DateTime.UtcNow - _frame.SeenUtc > TimeSpan.FromSeconds(2))
        {
            ReleaseTerrainBitmap();
            return;
        }
        var centerX = Bounds.Width / 2;
        var centerY = Bounds.Height / 2;
        _drawnFrame = _frame;
        _terrainLayer = new Canvas { Width = Bounds.Width, Height = Bounds.Height };
        _markerLayer = new Canvas { Width = Bounds.Width, Height = Bounds.Height };
        _canvas.Children.Add(_terrainLayer);
        var terrainDrawn = false;
        if (_terrain?.Matches(_frame) == true &&
            Bounds.Width is >= 1 and <= 4096 && Bounds.Height is >= 1 and <= 4096)
        {
            var width = (int)Math.Ceiling(Bounds.Width);
            var height = (int)Math.Ceiling(Bounds.Height);
            if ((long)width * height <= 4_194_304)
            {
                var key = (_terrain, _frame.GridX!.Value, _frame.GridY!.Value, width, height);
                var sameCaptureAndSize = _terrainBitmap is not null &&
                    _terrainRenderKey is { } previous &&
                    ReferenceEquals(previous.Terrain, _terrain) &&
                    previous.Width == width && previous.Height == height;
                if (!sameCaptureAndSize ||
                    _terrainRenderKey != key &&
                    _frame.SeenUtc - _terrainRenderedAt >= TimeSpan.FromMilliseconds(250))
                {
                    ReleaseTerrainBitmap();
                    var pixels = RadarTerrainRaster.Render(_terrain, _frame, width, height,
                        _scale * RadarProjection.WorldUnitsPerGridUnit);
                    var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96),
                        PixelFormats.Bgra8888, AlphaFormat.Premul);
                    using (var locked = bitmap.Lock())
                    {
                        var rowBytes = width * 4;
                        for (var row = 0; row < height; row++)
                            Marshal.Copy(pixels, row * rowBytes,
                                locked.Address + row * locked.RowBytes, rowBytes);
                    }
                    _terrainBitmap = bitmap;
                    _terrainRenderKey = key;
                    _terrainRenderedAt = _frame.SeenUtc;
                }
                var image = new Image
                {
                    Source = _terrainBitmap, Width = Bounds.Width, Height = Bounds.Height,
                    Stretch = Stretch.Fill
                };
                if (_terrainRenderKey is { } rendered &&
                    (rendered.GridX != key.Item2 || rendered.GridY != key.Item3))
                {
                    var (offsetX, offsetY) = RadarProjection.ProjectGrid(key.Item2, key.Item3,
                        rendered.GridX, rendered.GridY, centerX, centerY,
                        _scale * RadarProjection.WorldUnitsPerGridUnit);
                    Canvas.SetLeft(image, offsetX - centerX);
                    Canvas.SetTop(image, offsetY - centerY);
                }
                _terrainLayer.Children.Add(image);
                terrainDrawn = true;
                foreach (var room in _terrain.Rooms)
                {
                    var (x, y) = RadarProjection.ProjectGrid(_frame.GridX!.Value,
                        _frame.GridY!.Value, room.GridX, room.GridY, centerX, centerY,
                        _scale * RadarProjection.WorldUnitsPerGridUnit);
                    if (!double.IsFinite(x) || !double.IsFinite(y) ||
                        x < 8 || y < 8 || x > Bounds.Width - 8 || y > Bounds.Height - 8) continue;
                    AddDot(_terrainLayer, x, y, 10, MarkerBrush(room.Category));
                    if (string.IsNullOrEmpty(room.Label)) continue;
                    var label = new TextBlock
                    {
                        Text = room.Label,
                        Foreground = Brushes.White,
                        Background = new SolidColorBrush(Color.FromArgb(190, 8, 12, 20)),
                        FontSize = 12
                    };
                    Canvas.SetLeft(label, x + 9);
                    Canvas.SetTop(label, y - 8);
                    _terrainLayer.Children.Add(label);
                }
            }
        }
        if (!terrainDrawn) ReleaseTerrainBitmap();
        AddDot(_canvas, centerX, centerY, 12, Brushes.White);
        _canvas.Children.Add(_markerLayer);
        foreach (var marker in _frame.Markers.Concat(_frame.DormantCandidates))
        {
            var dormantCandidate = _frame.DormantCandidates.Contains(marker);
            var (x, y) = RadarProjection.Project(_frame.PlayerX, _frame.PlayerY,
                marker, centerX, centerY, _scale);
            if (!double.IsFinite(x) || !double.IsFinite(y) ||
                x < 8 || y < 8 || x > Bounds.Width - 8 || y > Bounds.Height - 8) continue;
            AddDot(_markerLayer, x, y, marker.Category == "monster" ? 7 : 10,
                dormantCandidate ? Brushes.Orange : MarkerBrush(marker.Category));
            if (marker.Category is "checkpoint" or "waypoint" or "portal" or "transition" or
                "player" or "loot" or "quest" or "boss" or "essence")
            {
                var label = new TextBlock
                {
                    Text = DisplayLabel(marker, dormantCandidate),
                    Foreground = Brushes.White,
                    Background = new SolidColorBrush(Color.FromArgb(190, 8, 12, 20)),
                    FontSize = 12
                };
                Canvas.SetLeft(label, x + 9);
                Canvas.SetTop(label, y - 8);
                _markerLayer.Children.Add(label);
            }
        }
    }

    private void ReleaseTerrainBitmap()
    {
        _terrainBitmap?.Dispose();
        _terrainBitmap = null;
        _terrainRenderKey = null;
    }

    private static void AddDot(Canvas layer, double x, double y, double diameter, IBrush brush)
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
        layer.Children.Add(circle);
    }

    private static IBrush MarkerBrush(string category) => category switch
    {
        "monster" => Brushes.OrangeRed,
        "player" => Brushes.Cyan,
        "league" => Brushes.Magenta,
        "loot" => Brushes.Yellow,
        "quest" => Brushes.YellowGreen,
        "campaign" => Brushes.YellowGreen,
        "boss" => Brushes.Red,
        "essence" => Brushes.MediumOrchid,
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
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern nint GetWindowLongPtr(nint handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern nint SetWindowLongPtr(nint handle, int index, nint value);
}
