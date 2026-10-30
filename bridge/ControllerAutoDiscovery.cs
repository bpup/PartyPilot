using System.Diagnostics;

namespace PartyPilot.Bridge;

internal sealed record ControllerAutoDiscoveryResult(bool Ready, string? Device,
    int? XInputIndex, string Message);

internal static class ControllerAutoDiscovery
{
    internal static ControllerAutoDiscoveryResult Select(HidHideSnapshot snapshot,
        IReadOnlyList<GamingControllerGroup> gamingInventory,
        string bridgeExecutable, IReadOnlyList<string> gameExecutables, bool useWgi,
        IReadOnlyList<int> connectedXInput, Func<string, bool> readableWgi,
        bool windowsInputVisible)
    {
        if (gameExecutables.Count is < 1 or > 3 ||
            gameExecutables.Any(string.IsNullOrWhiteSpace))
            return Fail("请先选择有效的游戏程序路径。");
        if (!windowsInputVisible)
            return Fail("PartyPilot 当前读不到手柄输入；手柄可能未连接，也可能被隔离设置挡住。");
        return SelectFromInventory(snapshot, bridgeExecutable, gameExecutables,
            useWgi, connectedXInput, readableWgi, gamingInventory);
    }

    private static ControllerAutoDiscoveryResult SelectFromInventory(HidHideSnapshot snapshot,
        string bridgeExecutable, IReadOnlyList<string> gameExecutables, bool useWgi,
        IReadOnlyList<int> connectedXInput, Func<string, bool> readableWgi,
        IReadOnlyList<GamingControllerGroup> gamingInventory)
    {
        var selected = ControllerCandidateSelection.Select(gamingInventory, useWgi,
            connectedXInput, readableWgi);
        if (!selected.Ready)
            return Fail(selected.Message);
        var hidden = snapshot.HiddenDevices.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selected.Group!.Interfaces.Any(item => !hidden.Contains(item.InstancePath)))
            return Fail("已发现手柄，但尚未完成游戏隔离。");
        if (!snapshot.Active || snapshot.Inverse ||
            gameExecutables.Any(game => !HidHidePreflight.Evaluate(snapshot,
                selected.Device!, bridgeExecutable, game).ConfigurationMatches))
            return Fail("已发现手柄，但 HidHide 隔离设置尚未就绪。");
        return new(true, selected.Device, selected.XInputIndex,
            "已找到唯一可读的手柄，并核对同一设备的所有游戏接口已隔离。");
    }

#if WINDOWS
    internal static async Task<ControllerAutoDiscoveryResult> DiscoverAsync(
        IReadOnlyList<string> gameExecutables, bool useWgi,
        int ownerPid, long ownerStartTicks, bool prepareIsolation)
    {
        if (!OperatingSystem.IsWindows()) return Fail("手柄自动检测需要 Windows。");
        var bridge = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(bridge) || gameExecutables.Count is < 1 or > 3 ||
            gameExecutables.Any(path => !Path.IsPathFullyQualified(path) || !File.Exists(path)))
            return Fail("请先选择有效的游戏程序路径。");
        var lease = new HidHideControllerLeaseManager(new HidHideCliControllerSettings(),
            HidHideControllerLeaseManager.DefaultJournalPath);
        if (!await lease.RecoverStaleAsync())
            return Fail("上次的手柄隔离尚未恢复，请先检查 HidHide 设置。");
        int[] connected = useWgi ? [] : Enumerable.Range(0, 4)
            .Where(index => XInputNative.TryRead((uint)index, out _)).ToArray();
        var visible = useWgi ? WgiPhysicalSource.HasReadableGamepad() : connected.Length > 0;
        if (!visible)
            return Fail("PartyPilot 当前读不到手柄输入；手柄可能未连接，也可能被隔离设置挡住。");
        try { _ = HidHidePreflight.Read(); }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or
            InvalidDataException or UnauthorizedAccessException)
        {
            return Fail("Windows 已读到手柄输入，但无法读取 HidHide 隔离状态：" + error.Message);
        }
        IReadOnlyList<GamingControllerGroup> inventory;
        try { inventory = await HidHideGamingInventory.CaptureAsync(); }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or
            InvalidDataException or UnauthorizedAccessException or OperationCanceledException)
        {
            return Fail("Windows 已读到手柄输入，但无法读取当前游戏设备清单：" + error.Message);
        }
        bool ReadableWgi(string device)
        {
            try { return WgiPhysicalSource.Open(device).TryRead(out _); }
            catch (InvalidOperationException) { return false; }
        }
        var selected = ControllerCandidateSelection.Select(inventory, useWgi,
            connected, ReadableWgi);
        if (!selected.Ready)
            return Fail(selected.Message);
        var normalizedGames = gameExecutables.Select(Path.GetFullPath).ToArray();
        if (!prepareIsolation)
            return Select(HidHidePreflight.Read(), inventory, bridge, normalizedGames,
                useWgi, connected, ReadableWgi, visible);
        async Task StartWatcherAsync()
        {
            var watch = new ProcessStartInfo(bridge)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            watch.ArgumentList.Add("--watch-controller-lease");
            watch.ArgumentList.Add("--owner-pid");
            watch.ArgumentList.Add(ownerPid.ToString());
            watch.ArgumentList.Add("--owner-start");
            watch.ArgumentList.Add(ownerStartTicks.ToString());
            using var watcher = Process.Start(watch) ??
                throw new IOException("手柄隔离恢复监视器未能启动。");
            try
            {
                var ready = await watcher.StandardOutput.ReadLineAsync()
                    .WaitAsync(TimeSpan.FromSeconds(5));
                if (ready != "controller-lease-watching" || watcher.HasExited)
                    throw new IOException("手柄隔离恢复监视器未能就绪。");
            }
            catch
            {
                if (!watcher.HasExited) watcher.Kill(entireProcessTree: true);
                throw;
            }
        }
        var prepared = await lease.PrepareAsync(selected, bridge, normalizedGames,
            ownerPid, ownerStartTicks, startWatcher: StartWatcherAsync);
        if (!prepared.Ready)
            return Fail(prepared.Message);
        try
        {
            var current = HidHidePreflight.Read();
            var verified = Select(current, inventory, bridge, normalizedGames,
                useWgi, connected, ReadableWgi, true);
            if (!verified.Ready || useWgi && !ReadableWgi(selected.Device!) ||
                !useWgi && !XInputNative.TryRead((uint)selected.XInputIndex!.Value,
                    out _))
            {
                var restored = prepared.LeaseId is null ||
                    await lease.ReleaseAsync(ownerPid, ownerStartTicks);
                return Fail(restored
                    ? verified.Ready ? "隔离后无法读取所选手柄，已撤销本次隔离。" :
                        verified.Message
                    : "手柄隔离复核失败，恢复未完成；请检查 HidHide 设置。");
            }
            return verified;
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or
            InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            var restored = prepared.LeaseId is null ||
                await lease.ReleaseAsync(ownerPid, ownerStartTicks);
            return Fail(restored
                ? "手柄隔离复核失败，已撤销本次设置：" + error.Message
                : "手柄隔离复核失败，恢复未完成：" + error.Message);
        }
    }
#endif

    private static ControllerAutoDiscoveryResult Fail(string message) =>
        new(false, null, null, message);
}
