namespace PartyPilot.Desktop;

internal readonly record struct AutoPartyMenuStep(ushort Buttons, int HoldMilliseconds, int DelayAfterMilliseconds);

internal sealed class AutoPartyRecoverySession
{
    internal static readonly AutoPartyMenuStep[] AcceptSequence =
    [
        new(0x0010, 100, 500), // Game menu
        new(0x0004, 80, 350),  // Current Party
        new(0x0004, 80, 550),  // Fresh invitation
        new(0x1000, 80, 500)   // Accept
    ];

    private readonly BridgeSession _bridge;
    private readonly PartyTopology _topology;
    private readonly IReadOnlyList<LaunchedClient> _clients;
    private readonly AutoPartyInviteNames _names;
    private readonly Action<string> _status;
    internal int FailedClient { get; private set; }

    internal AutoPartyRecoverySession(BridgeSession bridge, PartyTopology topology,
        IReadOnlyList<LaunchedClient> clients, AutoPartyInviteNames names, Action<string> status)
    {
        if (topology.ClientCount is < 2 or > 3 || clients.Count != topology.ClientCount)
            throw new ArgumentException("Auto Party requires every active client binding.");
        _bridge = bridge;
        _topology = topology;
        _clients = clients;
        _names = names;
        _status = status;
    }

    internal async Task<bool> RunAsync(CancellationToken token, int requestedTargetClient = 0)
    {
        if (requestedTargetClient < 0 || requestedTargetClient > _topology.ClientCount)
            throw new ArgumentOutOfRangeException(nameof(requestedTargetClient));
        if (_clients.Any(client => !PoeChatInviteService.IsWindowOwned(client)))
            throw new InvalidOperationException("An exact game window is unavailable for Auto Party.");
        var began = false;
        try
        {
            await _bridge.BeginAutoPartyAsync(token);
            began = true;
            var stableClients = requestedTargetClient == 0
                ? new HashSet<int> { 1 }
                : Enumerable.Range(1, _topology.ClientCount).Where(client => client != requestedTargetClient).ToHashSet();
            IEnumerable<int> targets = requestedTargetClient == 0
                ? Enumerable.Range(2, _topology.ClientCount - 1)
                : [requestedTargetClient];
            foreach (var targetClient in targets)
            {
                if (!await RecoverClientAsync(targetClient, stableClients, token))
                {
                    FailedClient = targetClient;
                    return false;
                }
                stableClients.Add(targetClient);
            }
            return true;
        }
        finally
        {
            if (began)
            {
                try { await _bridge.EndAutoPartyAsync(CancellationToken.None); }
                catch (Exception error) when (error is InvalidOperationException or IOException or TimeoutException)
                {
                    // A closed bridge has already neutralized its virtual pads.
                }
            }
        }
    }

    private async Task<bool> RecoverClientAsync(int targetClient, HashSet<int> stableClients, CancellationToken token)
    {
        var coordinator = new AutoPartyCoordinator();
        var receiver = _clients[targetClient - 1];
        var receiverSlot = _topology.Slots.First(slot => slot.Client == targetClient).RuntimePad;
        var expected = stableClients.Sum(client => _topology.Clients[client - 1].Count) +
            _topology.Clients[targetClient - 1].Count;
        var initialInviter = AutoPartyInviterSelection.Select(_topology, stableClients, 1, _bridge.LastFocusedSlot);
        coordinator.Begin(targetClient, initialInviter, DateTimeOffset.UtcNow);
        while (coordinator.Snapshot.Attempt < AutoPartyCoordinator.MaximumAttempts)
        {
            token.ThrowIfCancellationRequested();
            if (coordinator.Snapshot.Attempt > 0) await _bridge.RenewAutoPartyAsync(token);
            var inviterClient = AutoPartyInviterSelection.Select(_topology, stableClients, 1, _bridge.LastFocusedSlot);
            var inviter = _clients[inviterClient - 1];
            var inviterSlot = _topology.Slots.First(slot => slot.Client == inviterClient).RuntimePad;
            if (!PoeChatInviteService.IsWindowOwned(inviter) || !PoeChatInviteService.IsWindowOwned(receiver))
                throw new InvalidOperationException("An exact inviter or receiver window is unavailable.");
            var attempt = coordinator.BeginAttempt(inviterClient, DateTimeOffset.UtcNow);
            _status($"客户端 {targetClient}：第 {attempt.Attempt}/10 次全新邀请。");
            var sent = await PoeChatInviteService.SendInviteAsync(inviter, _names.ForClient(targetClient), token);
            if (!sent.Ok)
            {
                _status($"客户端 {targetClient} 邀请未发送：{sent.Detail}");
                coordinator.Retry(DateTimeOffset.UtcNow);
                await Task.Delay(coordinator.CurrentRetryDelayMilliseconds, token);
                continue;
            }
            await PulseAsync(inviter, inviterSlot, 0x2000, 60, 140, token);
            coordinator.Accepting(DateTimeOffset.UtcNow);
            if (!await PoeChatInviteService.FocusExactWindowAsync(receiver, token))
                throw new InvalidOperationException($"Client {targetClient} window could not be focused.");
            foreach (var step in AcceptSequence)
                await PulseAsync(receiver, receiverSlot, step.Buttons,
                    step.HoldMilliseconds, step.DelayAfterMilliseconds, token);
            coordinator.Verifying(DateTimeOffset.UtcNow);
            if (await VerifyRosterAsync(receiver, receiverSlot, expected, token))
            {
                coordinator.Stable(DateTimeOffset.UtcNow);
                _status($"客户端 {targetClient} 画面候选人数达到 {expected}；角色身份仍待游戏内核对。");
                return true;
            }
            coordinator.Retry(DateTimeOffset.UtcNow);
            _status($"客户端 {targetClient} 候选人数未达到 {expected}，等待下一次全新邀请。");
            await Task.Delay(coordinator.CurrentRetryDelayMilliseconds, token);
        }
        _status($"客户端 {targetClient} 连续 10 次邀请后仍未得到目标人数画面。");
        return false;
    }

    private async Task<bool> VerifyRosterAsync(LaunchedClient receiver, int slot, int expected,
        CancellationToken token)
    {
        await Task.Delay(450, token);
        if (!await PoeChatInviteService.FocusExactWindowAsync(receiver, token))
            throw new InvalidOperationException("Receiver window was unavailable during roster verification.");
        var initial = await CaptureCountAsync(receiver, token);
        _status(initial is int firstCount
            ? $"客户端画面候选人数 {firstCount}/{expected}。"
            : "客户端画面无法读取，暂不能核验人数。");
        if (initial is int observed && observed >= expected)
        {
            await PulseAsync(receiver, slot, 0x2000, 60, 100, token);
            return true;
        }
        await PulseAsync(receiver, slot, 0x0010, 80, 500, token);
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(3000);
        try
        {
            while (DateTimeOffset.UtcNow < deadline)
            {
                token.ThrowIfCancellationRequested();
                var count = await CaptureCountAsync(receiver, token);
                _status(count is int value
                    ? $"客户端画面候选人数 {value}/{expected}。"
                    : "客户端画面无法读取，暂不能核验人数。");
                if (count is int confirmed && confirmed >= expected) return true;
                await Task.Delay(500, token);
            }
            return false;
        }
        finally
        {
            await PulseAsync(receiver, slot, 0x2000, 60, 100, CancellationToken.None);
        }
    }

    private static async Task<int?> CaptureCountAsync(LaunchedClient client, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return await Task.Run<int?>(() => PartyRosterProbe.TryCount(client, out var count) ? count : null, token);
    }

    private async Task PulseAsync(LaunchedClient client, int slot, ushort buttons,
        int holdMilliseconds, int delayAfterMilliseconds, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!PoeChatInviteService.IsExactForeground(client))
            throw new InvalidOperationException("Exact game window lost foreground during Auto Party input.");
        await _bridge.PulseAutoPartyAsync(slot, buttons, holdMilliseconds, token);
        await Task.Delay(holdMilliseconds + delayAfterMilliseconds, token);
    }
}
