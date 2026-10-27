namespace PartyPilot.Desktop;

internal interface IPartyWarpControllerPort
{
    Task BeginAsync(CancellationToken token);
    Task RenewAsync(CancellationToken token);
    Task EndAsync(CancellationToken token);
    Task<bool> FocusAsync(CancellationToken token);
    bool HasExactForeground();
    Task PulseAsync(int pad, ushort buttons, int holdMs, CancellationToken token);
}

internal sealed class BridgePartyWarpControllerPort(BridgeSession bridge, LaunchedClient client)
    : IPartyWarpControllerPort
{
    public Task BeginAsync(CancellationToken token) => bridge.BeginAutoPartyAsync(token);
    public Task RenewAsync(CancellationToken token) => bridge.RenewAutoPartyAsync(token);
    public Task EndAsync(CancellationToken token) => bridge.EndAutoPartyAsync(token);
    public Task<bool> FocusAsync(CancellationToken token) =>
        PoeChatInviteService.FocusExactWindowAsync(client, token);
    public bool HasExactForeground() => PoeChatInviteService.IsExactForeground(client);
    public Task PulseAsync(int pad, ushort buttons, int holdMs, CancellationToken token) =>
        bridge.PulseAutoPartyAsync(pad, buttons, holdMs, token);
}

internal static class PartyWarpControllerRunner
{
    internal static async Task RunAsync(IPartyWarpControllerPort port,
        PartyWarpControllerRoute route, CancellationToken token,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        var began = false;
        try
        {
            await port.BeginAsync(token);
            began = true;
            using var leaseCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            var keepAlive = AutoPartyRecoverySession.KeepBridgeLeaseAsync(port.RenewAsync,
                TimeSpan.FromSeconds(2), leaseCancellation.Token);
            try
            {
                if (!await port.FocusAsync(token) || !port.HasExactForeground())
                    throw new InvalidOperationException("目标游戏窗口未能保持前台。");
                foreach (var step in PartyWarpControllerPlan.ForTopologyRosterIndex(
                    route.RosterIndex, route.PlayerCount))
                {
                    token.ThrowIfCancellationRequested();
                    if (!port.HasExactForeground())
                        throw new InvalidOperationException("目标游戏窗口失去前台，已停止手柄传送。");
                    if (step.Buttons != 0)
                        await port.PulseAsync(route.DriverPad, step.Buttons, step.HoldMs, token);
                    await (delay ?? Task.Delay)(TimeSpan.FromMilliseconds(
                        step.HoldMs + step.DelayAfterMs), token);
                }
            }
            finally
            {
                leaseCancellation.Cancel();
                try { await keepAlive; }
                catch (OperationCanceledException) when (leaseCancellation.IsCancellationRequested) { }
            }
        }
        finally
        {
            if (began)
            {
                try { await port.EndAsync(CancellationToken.None); }
                catch (Exception error) when (error is InvalidOperationException or IOException or TimeoutException)
                {
                    // A stopped bridge has already released its virtual pads.
                }
            }
        }
    }
}
