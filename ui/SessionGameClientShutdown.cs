using System.Diagnostics;

namespace PartyPilot.Desktop;

internal sealed record SessionGameClientIdentity(int Pid, int SessionId, string ProcessName,
    string Executable, DateTime StartedUtc);

internal sealed record SessionGameClientCloseResult(int Pid, bool Closed, bool Forced, string Detail);

internal static class SessionGameClientShutdown
{
    private static readonly string[] KnownNames =
    [
        "PathOfExile", "PathOfExile_x64", "PathOfExileSteam",
        "PathOfExile_x64Steam", "PathOfExileEGS", "PathOfExile_x64EGS"
    ];

    internal static IReadOnlyList<SessionGameClientIdentity> Capture(
        IReadOnlyList<LaunchedClient> clients)
    {
        if (!OperatingSystem.IsWindows()) return [];
        var identities = new List<SessionGameClientIdentity>(clients.Count);
        foreach (var client in clients.DistinctBy(client => client.Pid))
        {
            if (!Known(client.Executable)) continue;
            try
            {
                using var process = Process.GetProcessById(client.Pid);
                if (!process.HasExited &&
                    string.Equals(Path.GetFullPath(process.MainModule?.FileName ?? ""),
                        Path.GetFullPath(client.Executable), StringComparison.OrdinalIgnoreCase) &&
                    Known(process.ProcessName))
                    identities.Add(new SessionGameClientIdentity(process.Id, process.SessionId,
                        process.ProcessName, Path.GetFullPath(client.Executable),
                        process.StartTime.ToUniversalTime()));
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or
                System.ComponentModel.Win32Exception or UnauthorizedAccessException or IOException)
            {
                // An unverified process is not eligible for session shutdown.
            }
        }
        return identities;
    }

    internal static bool Matches(SessionGameClientIdentity expected,
        SessionGameClientIdentity observed) =>
        expected.Pid == observed.Pid && expected.SessionId == observed.SessionId &&
        Known(expected.ProcessName) && Known(observed.ProcessName) &&
        string.Equals(Path.GetFileNameWithoutExtension(expected.ProcessName),
            Path.GetFileNameWithoutExtension(observed.ProcessName), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Path.GetFullPath(expected.Executable), Path.GetFullPath(observed.Executable),
            StringComparison.OrdinalIgnoreCase) &&
        (expected.StartedUtc - observed.StartedUtc).Duration() <= TimeSpan.FromSeconds(1);

    internal static async Task<IReadOnlyList<SessionGameClientCloseResult>> CloseAsync(
        IReadOnlyList<SessionGameClientIdentity> identities)
    {
        if (!OperatingSystem.IsWindows()) return [];
        return await Task.WhenAll(identities.Select(CloseOneAsync));
    }

    private static async Task<SessionGameClientCloseResult> CloseOneAsync(
        SessionGameClientIdentity identity)
    {
        try
        {
            using var process = Process.GetProcessById(identity.Pid);
            if (process.HasExited)
                return new(identity.Pid, true, false, "已退出");
            if (!TryObserve(process, out var observed) || !Matches(identity, observed))
                return new(identity.Pid, false, false, "进程身份已变化，未关闭");
            if (process.CloseMainWindow() && await WaitForExitAsync(process, TimeSpan.FromSeconds(3)))
                return new(identity.Pid, true, false, "已通过游戏窗口关闭");
        }
        catch (ArgumentException) { return new(identity.Pid, true, false, "已退出"); }
        catch (InvalidOperationException) { return new(identity.Pid, true, false, "已退出"); }
        catch (Exception) { /* Recheck identity before the target version's forced fallback. */ }
        try
        {
            using var process = Process.GetProcessById(identity.Pid);
            if (process.HasExited)
                return new(identity.Pid, true, false, "正常关闭期间已退出");
            if (!TryObserve(process, out var observed) || !Matches(identity, observed))
                return new(identity.Pid, false, false, "强制关闭前进程身份已变化，未关闭");
            process.Kill(entireProcessTree: false);
            var exited = await WaitForExitAsync(process, TimeSpan.FromSeconds(2));
            return new(identity.Pid, exited, true,
                exited ? "正常关闭超时后已终止" : "强制关闭超时");
        }
        catch (ArgumentException) { return new(identity.Pid, true, false, "正常关闭期间已退出"); }
        catch (InvalidOperationException) { return new(identity.Pid, true, false, "正常关闭期间已退出"); }
        catch (Exception error) { return new(identity.Pid, false, false, $"关闭失败：{error.Message}"); }
    }

    private static bool TryObserve(Process process, out SessionGameClientIdentity observed)
    {
        observed = null!;
        try
        {
            process.Refresh();
            if (process.HasExited || process.MainModule?.FileName is not { } path) return false;
            observed = new(process.Id, process.SessionId, process.ProcessName,
                Path.GetFullPath(path), process.StartTime.ToUniversalTime());
            return true;
        }
        catch (Exception) { return false; }
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        if (process.HasExited) return true;
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
            return true;
        }
        catch (OperationCanceledException) { return process.HasExited; }
    }

    private static bool Known(string? path) =>
        KnownNames.Contains(Path.GetFileNameWithoutExtension(path ?? ""),
            StringComparer.OrdinalIgnoreCase);
}
