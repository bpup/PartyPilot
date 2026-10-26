using System.Net;
using System.Net.Sockets;
using Avalonia.Threading;

namespace PartyPilot.Desktop;

internal sealed class RadarSession : IAsyncDisposable
{
    private readonly UdpClient _receiver;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Dictionary<int, RadarOverlayWindow> _windows;
    private readonly Task _receiveTask;
    private bool _disposed;

    public event Action<string>? Ended;
    public event Action<int, int>? FrameReceived;

    private RadarSession(UdpClient receiver, Dictionary<int, RadarOverlayWindow> windows)
    {
        _receiver = receiver;
        _windows = windows;
        foreach (var window in windows.Values) window.TargetLost += OnTargetLost;
        _receiveTask = ReceiveLoopAsync();
    }

    public static RadarSession Start(IReadOnlyList<LaunchedClient> clients, double scale)
    {
        if (!OperatingSystem.IsWindows() || clients.Count is < 1 or > 3 ||
            clients.Any(client => client.Pid <= 0 || client.WindowHandle == 0) ||
            clients.Select(client => client.Pid).Distinct().Count() != clients.Count ||
            !double.IsFinite(scale) || scale <= 0)
            throw new InvalidOperationException("Radar requires 1–3 bound game windows and an explicit scale.");
        var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 47618));
        var windows = new Dictionary<int, RadarOverlayWindow>();
        RadarSession? session = null;
        try
        {
            foreach (var client in clients)
            {
                var window = new RadarOverlayWindow(client, scale);
                windows.Add(client.Pid, window);
            }
            session = new RadarSession(receiver, windows);
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
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (_disposed) return;
                        window.Apply(frame);
                        FrameReceived?.Invoke(pid, frame.Markers.Count);
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
}
