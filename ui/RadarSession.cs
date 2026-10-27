using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace PartyPilot.Desktop;

internal sealed class RadarSession : IAsyncDisposable
{
    private readonly UdpClient _receiver;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Dictionary<int, RadarOverlayWindow> _windows;
    private readonly Dictionary<int, RadarLandmarkCache> _landmarks = new();
    private readonly Dictionary<int, nint> _gameHandles;
    private readonly RadarVisibility _visibility;
    private readonly Dictionary<int, RadarTerrainCapture>? _manualTerrains;
    private readonly RadarTerrainCatalog? _terrainCatalog;
    private readonly DispatcherTimer _hotkeyTimer = new() { Interval = TimeSpan.FromMilliseconds(25) };
    private RadarHotkeyBinding _hotkey;
    private RadarRegisteredHotkey? _registeredHotkey;
    private readonly Task _receiveTask;
    private bool _disposed;

    public event Action<string>? Ended;
    public event Action<int, int, bool>? FrameReceived;

    private RadarSession(UdpClient receiver, Dictionary<int, RadarOverlayWindow> windows,
        Dictionary<int, nint> gameHandles, RadarHotkeyBinding hotkey, RadarVisibility visibility,
        IReadOnlyDictionary<int, RadarTerrainCapture>? manualTerrains,
        RadarTerrainCatalog? terrainCatalog)
    {
        _receiver = receiver;
        _windows = windows;
        foreach (var pid in windows.Keys) _landmarks.Add(pid, new RadarLandmarkCache());
        _gameHandles = gameHandles;
        _visibility = visibility;
        _hotkey = hotkey;
        _registeredHotkey = RadarRegisteredHotkey.TryStart(hotkey,
            () => Dispatcher.UIThread.Post(OnRegisteredHotkey));
        _manualTerrains = manualTerrains is null ? null : new(manualTerrains);
        _terrainCatalog = terrainCatalog;
        foreach (var window in windows.Values) window.TargetLost += OnTargetLost;
        _hotkeyTimer.Tick += OnHotkeyTimer;
        _hotkeyTimer.Start();
        _receiveTask = ReceiveLoopAsync();
    }

    internal void SetHotkey(RadarHotkeyBinding hotkey)
    {
        if (hotkey.VirtualKey == 0) throw new ArgumentException("Radar hotkey is invalid.");
        _registeredHotkey?.Dispose();
        _hotkey = hotkey;
        _visibility.Observe(false, false);
        _registeredHotkey = RadarRegisteredHotkey.TryStart(hotkey,
            () => Dispatcher.UIThread.Post(OnRegisteredHotkey));
    }

    private void OnRegisteredHotkey()
    {
        if (_disposed) return;
        var foreground = GetForegroundWindow();
        _visibility.ObserveRegisteredPress(_windows.Any(item =>
            !item.Value.TargetIsLost && _gameHandles[item.Key] == foreground));
    }

    private void OnHotkeyTimer(object? sender, EventArgs args)
    {
        if (_disposed) return;
        var foreground = GetForegroundWindow();
        var gameForeground = _windows.Any(item => !item.Value.TargetIsLost &&
            _gameHandles[item.Key] == foreground);
        _visibility.Observe(gameForeground,
            _hotkey.IsDown(key => GetAsyncKeyState(key) < 0));
    }

    public static RadarSession Start(IReadOnlyList<LaunchedClient> clients, double scale,
        RadarHotkeyBinding hotkey, IReadOnlyDictionary<int, RadarTerrainCapture>? terrains = null,
        RadarTerrainCatalog? terrainCatalog = null)
    {
        if (!OperatingSystem.IsWindows() || clients.Count is < 1 or > 3 ||
            clients.Any(client => client.Pid <= 0 || client.WindowHandle == 0) ||
            clients.Select(client => client.Pid).Distinct().Count() != clients.Count ||
            !double.IsFinite(scale) || scale <= 0)
            throw new InvalidOperationException("Radar requires 1–3 bound game windows and an explicit scale.");
        if (hotkey.VirtualKey == 0) throw new ArgumentException("Radar hotkey is invalid.");
        var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 47618));
        var windows = new Dictionary<int, RadarOverlayWindow>();
        var visibility = new RadarVisibility();
        RadarSession? session = null;
        try
        {
            foreach (var client in clients)
            {
                RadarTerrainCapture? terrain = null;
                if (terrains is not null) terrains.TryGetValue(client.Pid, out terrain);
                var window = new RadarOverlayWindow(client, scale, visibility, terrain);
                windows.Add(client.Pid, window);
            }
            session = new RadarSession(receiver, windows,
                clients.ToDictionary(client => client.Pid, client => (nint)client.WindowHandle),
                hotkey, visibility, terrains, terrainCatalog);
            foreach (var window in windows.Values) window.Show();
            if (windows.Values.Any(window => window.TargetIsLost))
                throw new InvalidOperationException("A bound game window disappeared before radar startup completed.");
            return session;
        }
        catch
        {
            if (session != null) session.DisposeAsync().AsTask().GetAwaiter().GetResult();
            else
            {
                foreach (var window in windows.Values) window.Close();
                receiver.Dispose();
            }
            throw;
        }
    }

    private async Task ReceiveLoopAsync()
    {
        var lastQueued = new Dictionary<int, DateTime>();
        try
        {
            while (!_cancellation.IsCancellationRequested)
            {
                var packet = await _receiver.ReceiveAsync(_cancellation.Token);
                if (!IPAddress.IsLoopback(packet.RemoteEndPoint.Address) || packet.Buffer.Length > 65507) continue;
                foreach (var (pid, window) in _windows)
                {
                    if (!RadarFrames.TryParse(packet.Buffer, pid, DateTime.UtcNow, out var frame) || frame == null)
                        continue;
                    if (lastQueued.TryGetValue(pid, out var previous) && frame.SeenUtc <= previous)
                        break;
                    lastQueued[pid] = frame.SeenUtc;
                    RadarTerrainCapture? terrain = null;
                    if (_manualTerrains is not null && _manualTerrains.TryGetValue(pid, out var manual) &&
                        manual.Matches(frame))
                    {
                        if (manual.MetadataUnchanged()) terrain = manual;
                        else if (RadarTerrainCapture.TryLoad(manual.MetadataPath, out var refreshed) &&
                                 refreshed?.Matches(frame) == true)
                        {
                            _manualTerrains[pid] = refreshed;
                            terrain = refreshed;
                        }
                    }
                    terrain ??= _terrainCatalog?.FindMatching(frame, DateTime.UtcNow);
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (_disposed) return;
                        if (!_landmarks[pid].TryApply(frame, out var enriched) || enriched is null)
                            return;
                        window.Apply(enriched, terrain);
                        FrameReceived?.Invoke(pid, enriched.Markers.Count, window.TerrainVisible);
                    });
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (_cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Dispatcher.UIThread.Post(() => Ended?.Invoke(exception.Message));
        }
    }

    private void OnTargetLost() => Ended?.Invoke("绑定的游戏窗口已消失或进程身份发生变化。");

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _registeredHotkey?.Dispose();
        _hotkeyTimer.Stop();
        _hotkeyTimer.Tick -= OnHotkeyTimer;
        _cancellation.Cancel();
        _receiver.Dispose();
        await _receiveTask;
        foreach (var window in _windows.Values)
        {
            window.TargetLost -= OnTargetLost;
            window.Close();
        }
        _cancellation.Dispose();
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
}
