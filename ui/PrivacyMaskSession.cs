namespace PartyPilot.Desktop;

internal sealed class PrivacyMaskSession : IDisposable
{
    private readonly List<PrivacyMaskOverlayWindow> _windows = [];
    private bool _disposed;
    public event Action<string>? Ended;

    private PrivacyMaskSession() { }

    public static PrivacyMaskSession Start(IReadOnlyList<LaunchedClient> clients,
        IReadOnlyList<PrivacyMaskBox> boxes)
    {
        if (!OperatingSystem.IsWindows() || clients.Count is < 1 or > 3 ||
            clients.Any(client => client.Pid <= 0 || client.WindowHandle == 0) ||
            clients.Select(client => client.Pid).Distinct().Count() != clients.Count ||
            boxes.Count is < 1 or > PrivacyMaskPlan.MaxBoxes ||
            boxes.Any(box => box.Client < 1 || box.Client > clients.Count))
            throw new InvalidOperationException("Privacy masks need bound game windows and valid regions.");
        var session = new PrivacyMaskSession();
        try
        {
            for (var index = 0; index < clients.Count; index++)
            {
                var clientBoxes = boxes.Where(box => box.Client == index + 1).ToArray();
                if (clientBoxes.Length == 0) continue;
                var window = new PrivacyMaskOverlayWindow(clients[index], clientBoxes);
                session._windows.Add(window);
                window.TargetLost += session.OnTargetLost;
                window.Show();
                if (window.TargetIsLost) throw new InvalidOperationException("A bound game window disappeared.");
            }
            return session;
        }
        catch { session.Dispose(); throw; }
    }

    private void OnTargetLost() => Ended?.Invoke("绑定的游戏窗口已消失或进程身份发生变化。");

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var window in _windows)
        {
            window.TargetLost -= OnTargetLost;
            window.Close();
        }
        _windows.Clear();
    }
}
