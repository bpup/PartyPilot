namespace PartyPilot.Bridge;

internal sealed record ControllerCandidateResult(GamingControllerGroup? Group,
    string? Device, int? XInputIndex, string Message)
{
    internal bool Ready => Group is not null && Device is not null;
}

internal static class ControllerCandidateSelection
{
    internal static ControllerCandidateResult Select(
        IReadOnlyList<GamingControllerGroup> gamingInventory, bool useWgi,
        IReadOnlyList<int> connectedXInput, Func<string, bool> readableWgi)
    {
        if (gamingInventory.Count == 0)
            return Fail("Windows 有手柄输入，但设备清单中没有当前连接的游戏设备。");
        // XInput exposes a slot, not the PnP device path. With more than one
        // physical candidate, a slot cannot identify which path may be hidden.
        if (!useWgi && (connectedXInput.Count != 1 || connectedXInput[0] is < 0 or > 3 ||
            gamingInventory.Count != 1))
            return Fail("检测到多个手柄或无法对应实体设备；请只连接这次使用的手柄。");
        // XInput needs the inventory identity, which may come from the XUSB path.
        // WGI opens by instance path, so it still requires VID/PID in that path.
        var candidates = gamingInventory.SelectMany(group => group.Interfaces
                .Where(item => item.Vendor.HasValue && item.Product.HasValue &&
                    (!useWgi || ControllerDeviceId.TryReadVidPid(item.InstancePath,
                        out _, out _)) &&
                    (!useWgi || readableWgi(item.InstancePath)))
                .Take(1)
                .Select(item => (Group: group, Device: item.InstancePath)))
            .ToArray();
        if (candidates.Length != 1)
            return Fail(candidates.Length == 0
                ? "设备已连接，但无法把游戏输入唯一对应到实体手柄。"
                : "检测到多个可读手柄；请只连接这次使用的手柄。");
        return new(candidates[0].Group, candidates[0].Device,
            useWgi ? null : connectedXInput[0], string.Empty);
    }

    private static ControllerCandidateResult Fail(string message) =>
        new(null, null, null, message);
}
