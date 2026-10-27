namespace PartyPilot.Desktop;

internal sealed record AutoLoginStartupConfiguration(bool Enabled, AccountCredential? Credential,
    LoginCalibration? Calibration);
internal sealed record AutoLoginStartupItem(int Client, LaunchedClient Window,
    AccountCredential Credential, LoginCalibration Calibration);

internal static class AutoLoginStartupPlan
{
    internal static bool TryCreate(PartyTopology topology, IReadOnlyList<LaunchedClient> clients,
        IReadOnlyList<AutoLoginStartupConfiguration> configurations,
        out IReadOnlyList<AutoLoginStartupItem> items, out string reason)
    {
        items = [];
        reason = string.Empty;
        if (clients.Count != topology.ClientCount || configurations.Count != topology.ClientCount)
        {
            reason = "游戏窗口绑定与客户端布局不一致；未发送登录输入。";
            return false;
        }
        var ready = new List<AutoLoginStartupItem>();
        for (var index = 0; index < topology.ClientCount; index++)
        {
            if (!configurations[index].Enabled) continue;
            var configured = configurations[index];
            if (configured.Credential is not { } credential ||
                configured.Calibration is not { } calibration ||
                !AccountProfileStore.IsValidEmail(credential.Email) ||
                string.IsNullOrEmpty(credential.Password) ||
                !LoginCalibration.TryCreate(calibration.UsernameX, calibration.UsernameY,
                    calibration.PasswordX, calibration.PasswordY, calibration.Width,
                    calibration.Height, calibration.LayoutKey, out _) ||
                calibration.LayoutKey != topology.Id)
            {
                reason = $"客户端 {index + 1} 已选择自动填入，但账号或登录字段标记不完整；请先完成标记或选择手动登录。";
                return false;
            }
            ready.Add(new(index + 1, clients[index], credential, calibration));
        }
        if (ready.GroupBy(item => item.Credential.Email, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            reason = "多个独立客户端保存了同一账号邮箱；已停止所有自动填入。";
            return false;
        }
        items = ready;
        return true;
    }
}
