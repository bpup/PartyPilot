namespace PartyPilot.Bridge;

internal sealed record HidHideIsolationPlan(string SelectedDevice,
    string[] PathsToHide, bool RegisterBridge, bool EnableCloak,
    bool DisableInverse);

internal static class HidHideIsolationPlanner
{
    internal static bool TryCreate(HidHideSnapshot snapshot, ControllerCandidateResult selected,
        string bridgeExecutable, IReadOnlyList<string> gameExecutables,
        out HidHideIsolationPlan? plan, out string problem)
    {
        plan = null;
        problem = string.Empty;
        if (!selected.Ready || selected.Group is null || selected.Device is null ||
            string.IsNullOrWhiteSpace(bridgeExecutable) || gameExecutables.Count is < 1 or > 3 ||
            gameExecutables.Any(string.IsNullOrWhiteSpace))
        {
            problem = "实体手柄或游戏程序尚未确认。";
            return false;
        }
        if (gameExecutables.Any(game => HidHidePreflight.IsApplicationAllowed(snapshot, game)))
        {
            problem = "游戏程序已在 HidHide 允许列表中，无法安全隔离实体手柄。";
            return false;
        }
        if (selected.Group.Key.StartsWith(@"ROOT\", StringComparison.OrdinalIgnoreCase) ||
            selected.Group.Key.StartsWith(@"SWD\", StringComparison.OrdinalIgnoreCase))
        {
            problem = "候选手柄来自软件设备容器，不能自动修改其隔离。";
            return false;
        }
        var selectedPaths = selected.Group.Interfaces
            .Select(item => item.InstancePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (selectedPaths.Length is < 1 or > 16 ||
            !selectedPaths.Contains(selected.Device, StringComparer.OrdinalIgnoreCase))
        {
            problem = "当前设备清单不能确定要隔离的手柄接口。";
            return false;
        }
        var selectedSet = selectedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Enabling cloak or changing inverse mode would also change how all
        // existing hidden devices behave. Do not take ownership of their state.
        if ((!snapshot.Active || snapshot.Inverse) &&
            snapshot.HiddenDevices.Any(path => !selectedSet.Contains(path)))
        {
            problem = "HidHide 中有其他设备设置，无法安全切换全局隔离状态。";
            return false;
        }
        var hidden = snapshot.HiddenDevices.ToHashSet(StringComparer.OrdinalIgnoreCase);
        plan = new HidHideIsolationPlan(selected.Device,
            selectedPaths.Where(path => !hidden.Contains(path)).ToArray(),
            !HidHidePreflight.IsApplicationAllowed(snapshot, bridgeExecutable),
            !snapshot.Active, snapshot.Inverse);
        return true;
    }
}
