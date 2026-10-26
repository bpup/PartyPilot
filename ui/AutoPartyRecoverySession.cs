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
    private readonly SemaphoreSlim _focusGate = new(1, 1);
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
            var result = await AutoPartyRecoveryScheduler.RunAsync(_topology.ClientCount,
                requestedTargetClient, (client, stable, cancellation) =>
                    RecoverClientAsync(client, stable, cancellation), token);
            FailedClient = result.FailedClient;
            return result.Complete;
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

    private async Task<bool> RecoverClientAsync(int targetClient, IReadOnlySet<int> stableClients, CancellationToken token)
    {
        var coordinator = new AutoPartyCoordinator();
        var receiver = _clients[targetClient - 1];
        var receiverSlot = _topology.Slots.First(slot => slot.Client == targetClient).RuntimePad;
        var expected = stableClients.Sum(client => _topology.Clients[client - 1].Count) +
            _topology.Clients[targetClient - 1].Count;
        var initialLeader = AutoPartyInviterSelection.DefaultLeaderPlayer(_topology,
            _bridge.LastManualFocusedSlot, _bridge.LastFocusedLeadsEnabled);
        var initialInviter = AutoPartyInviterSelection.Select(_topology, stableClients,
            initialLeader, _bridge.LastFocusedSlot);
        coordinator.Begin(targetClient, initialInviter, DateTimeOffset.UtcNow);
        while (coordinator.Snapshot.Attempt < AutoPartyCoordinator.MaximumAttempts)
        {
            token.ThrowIfCancellationRequested();
            if (coordinator.Snapshot.Attempt > 0) await _bridge.RenewAutoPartyAsync(token);
            var leader = AutoPartyInviterSelection.DefaultLeaderPlayer(_topology,
                _bridge.LastManualFocusedSlot, _bridge.LastFocusedLeadsEnabled);
            var inviterClient = AutoPartyInviterSelection.Select(_topology, stableClients,
                leader, _bridge.LastFocusedSlot);
            var inviter = _clients[inviterClient - 1];
            var inviterSlot = _topology.Slots.First(slot => slot.Client == inviterClient).RuntimePad;
            if (!PoeChatInviteService.IsWindowOwned(inviter) || !PoeChatInviteService.IsWindowOwned(receiver))
                throw new InvalidOperationException("An exact inviter or receiver window is unavailable.");
            var attempt = coordinator.BeginAttempt(inviterClient, DateTimeOffset.UtcNow);
            _status($"客户端 {targetClient}：第 {attempt.Attempt}/10 次全新邀请。");
            (bool Ok, string Detail) sent;
            await _focusGate.WaitAsync(token);
            try
            {
                sent = await PoeChatInviteService.SendInviteAsync(inviter, _names.ForClient(targetClient), token);
                if (sent.Ok)
                    await PulseUnderFocusAsync(inviter, inviterSlot, 0x2000, 60, 140, token);
            }
            finally { _focusGate.Release(); }
            if (!sent.Ok)
            {
                _status($"客户端 {targetClient} 邀请未发送：{sent.Detail}");
                coordinator.Retry(DateTimeOffset.UtcNow);
                await Task.Delay(coordinator.CurrentRetryDelayMilliseconds, token);
                continue;
            }
            coordinator.Accepting(DateTimeOffset.UtcNow);
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
        var initial = await CaptureFocusedCountAsync(receiver, token);
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
                var count = await CaptureFocusedCountAsync(receiver, token);
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
            if (!token.IsCancellationRequested)
                await PulseAsync(receiver, slot, 0x2000, 60, 100, CancellationToken.None);
        }
    }

    private static async Task<int?> CaptureCountAsync(LaunchedClient client, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return await Task.Run<int?>(() => PartyRosterProbe.TryCount(client, out var count) ? count : null, token);
    }

    private async Task<int?> CaptureFocusedCountAsync(LaunchedClient client, CancellationToken token)
    {
        await _focusGate.WaitAsync(token);
        try
        {
            if (!await PoeChatInviteService.FocusExactWindowAsync(client, token))
                throw new InvalidOperationException("Receiver window was unavailable during roster verification.");
            return await CaptureCountAsync(client, token);
        }
        finally { _focusGate.Release(); }
    }

    private async Task PulseAsync(LaunchedClient client, int slot, ushort buttons,
        int holdMilliseconds, int delayAfterMilliseconds, CancellationToken token)
    {
        await _focusGate.WaitAsync(token);
        try
        {
            if (!await PoeChatInviteService.FocusExactWindowAsync(client, token))
                throw new InvalidOperationException("Exact game window was unavailable during Auto Party input.");
            await PulseUnderFocusAsync(client, slot, buttons, holdMilliseconds, 20, token);
        }
        finally { _focusGate.Release(); }
        await Task.Delay(Math.Max(0, delayAfterMilliseconds - 20), token);
    }

    private async Task PulseUnderFocusAsync(LaunchedClient client, int slot, ushort buttons,
        int holdMilliseconds, int delayAfterMilliseconds, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!PoeChatInviteService.IsExactForeground(client))
            throw new InvalidOperationException("Exact game window lost foreground during Auto Party input.");
        await _bridge.PulseAutoPartyAsync(slot, buttons, holdMilliseconds, token);
        await Task.Delay(holdMilliseconds + delayAfterMilliseconds, token);
    }
}
