namespace PartyPilot.Bridge;

internal sealed record ControllerAutoDiscoveryResult(bool Ready, string? Device,
    int? XInputIndex, string Message);

internal static class ControllerAutoDiscovery
{
    internal static ControllerAutoDiscoveryResult Select(HidHideSnapshot snapshot,
        string bridgeExecutable, IReadOnlyList<string> gameExecutables, bool useWgi,
        IReadOnlyList<int> connectedXInput, Func<string, bool> readableWgi)
    {
        if (gameExecutables.Count is < 1 or > 3 ||
            gameExecutables.Any(string.IsNullOrWhiteSpace))
            return Fail("请先选择有效的游戏程序路径。");
        if (!snapshot.Active || snapshot.Inverse)
            return Fail("请在 HidHide 中开启手柄隐藏，再重试。");
        var candidates = snapshot.HiddenDevices.Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(device => ControllerDeviceId.TryReadVidPid(device, out _, out _) &&
                gameExecutables.All(game => HidHidePreflight.Evaluate(snapshot, device,
                    bridgeExecutable, game).ConfigurationMatches))
            .Where(device => !useWgi || readableWgi(device)).ToArray();
        if (candidates.Length == 0)
            return Fail("没有找到已对游戏隐藏、且允许本程序读取的手柄。请检查 HidHide 设置。");
        if (candidates.Length != 1)
            return Fail("检测到多个可用手柄。请在 HidHide 中仅保留这次使用的手柄，再重试。");
        if (useWgi)
            return new(true, candidates[0], null, "已找到唯一可读的手柄候选，并检查隔离配置。");
        if (connectedXInput.Count != 1 || connectedXInput[0] is < 0 or > 3)
            return Fail(connectedXInput.Count == 0
                ? "没有读到实体手柄。请连接手柄后重试。"
                : "检测到多个手柄输入。请暂时断开其他手柄，再重试。");
        return new(true, candidates[0], connectedXInput[0],
            "已找到唯一可读的手柄候选，并检查隔离配置。");
    }

#if WINDOWS
    internal static ControllerAutoDiscoveryResult Discover(
        IReadOnlyList<string> gameExecutables, bool useWgi)
    {
        if (!OperatingSystem.IsWindows()) return Fail("手柄自动检测需要 Windows。");
        var bridge = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(bridge) || gameExecutables.Count is < 1 or > 3 ||
            gameExecutables.Any(path => !Path.IsPathFullyQualified(path) || !File.Exists(path)))
            return Fail("请先选择有效的游戏程序路径。");
        var snapshot = HidHidePreflight.Read();
        int[] connected = useWgi ? [] : Enumerable.Range(0, 4)
            .Where(index => XInputNative.TryRead((uint)index, out _)).ToArray();
        return Select(snapshot, bridge, gameExecutables.Select(Path.GetFullPath).ToArray(),
            useWgi, connected, device =>
            {
                try { return WgiPhysicalSource.Open(device).TryRead(out _); }
                catch (InvalidOperationException) { return false; }
            });
    }
#endif

    private static ControllerAutoDiscoveryResult Fail(string message) =>
        new(false, null, null, message);
}
