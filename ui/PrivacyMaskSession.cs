using System.Net;
using System.Net.Sockets;
using Avalonia.Threading;

namespace PartyPilot.Desktop;

internal sealed class PrivacyMaskSession : IDisposable
{
    private readonly List<PrivacyMaskOverlayWindow> _windows = [];
    private readonly UdpClient? _receiver;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _receiveTask;
    private bool _disposed;
    public event Action<string>? Ended;

    private PrivacyMaskSession(bool autoNameplates)
    {
        if (autoNameplates) _receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 47619));
    }

    public static PrivacyMaskSession Start(IReadOnlyList<LaunchedClient> clients,
        IReadOnlyList<PrivacyMaskBox> boxes, bool autoNameplates = false)
    {
        if (!OperatingSystem.IsWindows() || clients.Count is < 1 or > 3 ||
            clients.Any(client => client.Pid <= 0 || client.WindowHandle == 0) ||
            clients.Select(client => client.Pid).Distinct().Count() != clients.Count ||
            (boxes.Count == 0 && !autoNameplates) || boxes.Count > PrivacyMaskPlan.MaxBoxes ||
            boxes.Any(box => box.Client < 1 || box.Client > clients.Count))
            throw new InvalidOperationException("Privacy masks need bound game windows and valid regions.");
        var session = new PrivacyMaskSession(autoNameplates);
        try
        {
            for (var index = 0; index < clients.Count; index++)
            {
                var clientBoxes = boxes.Where(box => box.Client == index + 1).ToArray();
                if (clientBoxes.Length == 0 && !autoNameplates) continue;
                var window = new PrivacyMaskOverlayWindow(clients[index], clientBoxes,
                    autoNameplates, index + 1);
                session._windows.Add(window);
                window.TargetLost += session.OnTargetLost;
                window.Show();
                if (window.TargetIsLost) throw new InvalidOperationException("A bound game window disappeared.");
            }
            if (session._receiver is not null) session._receiveTask = session.ReceiveLoopAsync();
            return session;
        }
        catch { session.Dispose(); throw; }
    }

    private void OnTargetLost() => Ended?.Invoke("绑定的游戏窗口已消失或进程身份发生变化。");

    private async Task ReceiveLoopAsync()
    {
        if (_receiver is null) return;
        var windows = _windows.ToArray();
        try
        {
            while (!_cancellation.IsCancellationRequested)
            {
                var packet = await _receiver.ReceiveAsync(_cancellation.Token);
                if (!IPAddress.IsLoopback(packet.RemoteEndPoint.Address)) continue;
                foreach (var window in windows)
                {
                    if (!PrivacyGameplayFrames.TryParse(packet.Buffer, window.ProcessId,
                            DateTime.UtcNow, out var frame) || frame is null) continue;
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!_disposed) window.ObserveGameplay(frame.SeenUtc);
                    });
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (_cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            Dispatcher.UIThread.Post(() => { if (!_disposed) Ended?.Invoke(error.Message); });
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cancellation.Cancel();
        _receiver?.Dispose();
        foreach (var window in _windows)
        {
            window.TargetLost -= OnTargetLost;
            window.Close();
        }
        _windows.Clear();
        _cancellation.Dispose();
    }
}
