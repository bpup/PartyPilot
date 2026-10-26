using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace PartyPilot.Desktop;

public sealed class PrivacyMaskOverlayWindow : Window
{
    private readonly LaunchedClient _client;
    private readonly IReadOnlyList<PrivacyMaskBox> _boxes;
    private readonly bool _autoNameplates;
    private readonly int _clientNumber;
    private readonly PrivacyNameplateTracker _tracker = new();
    private IReadOnlyList<PrivacyMaskBox> _activeBoxes;
    private IReadOnlyList<PrivacyMaskBox> _autoBoxes = [];
    private DateTime _lastGameplayUtc;
    private readonly Canvas _canvas = new();
    private readonly List<Image> _images = [];
    private readonly List<WriteableBitmap> _bitmaps = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private int _captureInFlight;
    private int _generation;
    private long _captureSequence;
    private long _appliedSequence;
    private bool _wasForeground;
    private DateTime _lastFrameUtc;
    public bool TargetIsLost { get; private set; }
    public int ProcessId => _client.Pid;
    public event Action? TargetLost;

    public PrivacyMaskOverlayWindow(LaunchedClient client, IReadOnlyList<PrivacyMaskBox> boxes,
        bool autoNameplates = false, int clientNumber = 1)
    {
        _client = client;
        _boxes = boxes;
        _autoNameplates = autoNameplates;
        _clientNumber = clientNumber;
        _activeBoxes = boxes;
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
        Closed += (_, _) =>
        {
            _generation++;
            _tracker.Reset();
            _timer.Stop();
            ClearFrames();
        };
        _timer.Tick += (_, _) => SyncTarget();
    }

    private void Draw()
    {
        _generation++;
        ClearFrames();
        _canvas.Children.Clear();
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        foreach (var box in _activeBoxes)
        {
            var (x, y, width, height) = box.InClient(Bounds.Width, Bounds.Height);
            var mask = new Border { Width = width, Height = height, Background = Brushes.Black };
            Canvas.SetLeft(mask, x);
            Canvas.SetTop(mask, y);
            _canvas.Children.Add(mask);
            var image = new Image { Width = width, Height = height, Stretch = Stretch.Fill, IsVisible = false };
            Canvas.SetLeft(image, x);
            Canvas.SetTop(image, y);
            _canvas.Children.Add(image);
            _images.Add(image);
        }
    }

    private void ClearFrames()
    {
        HideFrames();
        _images.Clear();
    }

    private void HideFrames()
    {
        foreach (var image in _images) { image.Source = null; image.IsVisible = false; }
        foreach (var bitmap in _bitmaps) bitmap.Dispose();
        _bitmaps.Clear();
        _lastFrameUtc = default;
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
        var foreground = GetForegroundWindow() == handle;
        Opacity = foreground ? 1 : 0;
        if (!foreground)
        {
            if (_wasForeground)
            {
                _generation++;
                _tracker.Reset();
                _autoBoxes = [];
                SetActiveBoxes(_boxes);
                HideFrames();
            }
            _wasForeground = false;
            return;
        }
        _wasForeground = true;
        var physicalWidth = rect.Right - rect.Left;
        var physicalHeight = rect.Bottom - rect.Top;
        var gameplayFresh = _autoNameplates && _lastGameplayUtc != default &&
            DateTime.UtcNow - _lastGameplayUtc <= TimeSpan.FromSeconds(2);
        if (!gameplayFresh)
        {
            _tracker.Reset();
            _autoBoxes = [];
            SetActiveBoxes(_boxes);
        }
        else if (Volatile.Read(ref _captureInFlight) == 0)
        {
            var coverage = _tracker.Coverage(physicalWidth, physicalHeight, DateTime.UtcNow);
            var moving = coverage.Select(bounds => PrivacyNameplateDetector.Expand(_clientNumber,
                bounds, physicalWidth, physicalHeight)).ToArray();
            if (!_autoBoxes.SequenceEqual(moving))
            {
                _autoBoxes = moving;
                SetActiveBoxes(_boxes.Concat(moving).ToArray());
            }
        }
        if (_lastFrameUtc != default && DateTime.UtcNow - _lastFrameUtc > TimeSpan.FromMilliseconds(250))
            foreach (var image in _images) image.IsVisible = false;
        RequestCapture(handle, physicalWidth, physicalHeight);
    }

    public void ObserveGameplay(DateTime seenUtc)
    {
        var age = DateTime.UtcNow - seenUtc;
        if (seenUtc.Kind == DateTimeKind.Utc && age >= TimeSpan.Zero &&
            age <= TimeSpan.FromSeconds(2))
            _lastGameplayUtc = seenUtc;
    }

    private void SetActiveBoxes(IReadOnlyList<PrivacyMaskBox> boxes)
    {
        if (_activeBoxes.SequenceEqual(boxes)) return;
        _activeBoxes = boxes;
        Draw();
    }

    private void RequestCapture(nint handle, int width, int height)
    {
        var gameplayFresh = _autoNameplates && _lastGameplayUtc != default &&
            DateTime.UtcNow - _lastGameplayUtc <= TimeSpan.FromSeconds(2);
        if ((_activeBoxes.Count == 0 && !gameplayFresh) ||
            _images.Count != _activeBoxes.Count ||
            Interlocked.CompareExchange(ref _captureInFlight, 1, 0) != 0) return;
        var generation = _generation;
        var sequence = Interlocked.Increment(ref _captureSequence);
        var trackerRevision = _tracker.Revision;
        var currentActive = _activeBoxes;
        _ = Task.Run(() =>
        {
            PrivacyBlurFrame?[]? frames = null;
            IReadOnlyList<PrivacyMaskBox> active = currentActive;
            IReadOnlyList<PrivacyMaskBox> detectedAuto = [];
            try
            {
                var pixels = PrivacyWindowCapture.TryCapture(handle, width, height);
                if (pixels is not null)
                {
                    if (gameplayFresh && generation == Volatile.Read(ref _generation))
                    {
                        var detected = PrivacyNameplateDetector.Locate(pixels, width, height);
                        var observedAt = DateTime.UtcNow;
                        _tracker.Observe(detected, width, height, observedAt, trackerRevision);
                        detectedAuto = _tracker.Coverage(width, height, observedAt)
                            .Select(bounds => PrivacyNameplateDetector.Expand(_clientNumber,
                                bounds, width, height)).ToArray();
                    }
                    active = _boxes.Concat(detectedAuto).ToArray();
                    frames = active.Select(box => PrivacyBlur.Build(pixels, width, height, box)).ToArray();
                }
            }
            catch { /* A failed capture leaves the opaque masks visible. */ }
            finally { Interlocked.Exchange(ref _captureInFlight, 0); }
            Dispatcher.UIThread.Post(() =>
            {
                if (generation != _generation || TargetIsLost || !_wasForeground ||
                    sequence <= _appliedSequence) return;
                _appliedSequence = sequence;
                if (frames is null || frames.Any(frame => frame is null))
                {
                    HideFrames();
                    return;
                }
                _autoBoxes = detectedAuto;
                SetActiveBoxes(active);
                if (frames.Length != _images.Count) { HideFrames(); return; }
                try { ShowFrames(frames!); }
                catch { HideFrames(); }
            });
        });
    }

    private void ShowFrames(IReadOnlyList<PrivacyBlurFrame?> frames)
    {
        for (var index = 0; index < frames.Count; index++)
        {
            var frame = frames[index]!;
            var bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height),
                new Vector(96, 96), PixelFormats.Bgra8888, AlphaFormat.Premul);
            using (var locked = bitmap.Lock())
            {
                var rowBytes = frame.Width * 4;
                for (var row = 0; row < frame.Height; row++)
                    Marshal.Copy(frame.Pixels, row * rowBytes,
                        locked.Address + row * locked.RowBytes, rowBytes);
            }
            if (_bitmaps.Count > index)
            {
                _images[index].Source = null;
                _bitmaps[index].Dispose();
                _bitmaps[index] = bitmap;
            }
            else _bitmaps.Add(bitmap);
            _images[index].Source = bitmap;
            _images[index].IsVisible = true;
        }
        _lastFrameUtc = DateTime.UtcNow;
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
