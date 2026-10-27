using PartyPilot.Desktop;

internal static class AutoPartyRecoverySmoke
{
    public static void Run()
    {
        var expected = new[]
        {
            new AutoPartyMenuStep(0x0010, 100, 500),
            new AutoPartyMenuStep(0x0004, 80, 350),
            new AutoPartyMenuStep(0x0004, 80, 550),
            new AutoPartyMenuStep(0x1000, 80, 500)
        };
        if (!AutoPartyRecoverySession.AcceptSequence.SequenceEqual(expected) ||
            expected.Any(step => !BridgeSession.ValidAutoPartyPulse(step.Buttons, step.HoldMilliseconds)) ||
            !BridgeSession.ValidAutoPartyPulse(0x2000, 60) ||
            !BridgeSession.ValidAutoPartyPulse(0x0010, 80) ||
            BridgeSession.ValidAutoPartyPulse(0x1000, 60) ||
            BridgeSession.ValidAutoPartyPulse(0x2000, 80))
            throw new Exception("The target-version invite acceptance or roster menu sequence changed.");

        var topology = PartyTopologies.DefaultFor(5);
        if (AutoPartyInviterSelection.DefaultLeaderPlayer(topology, 5, true) != 5 ||
            AutoPartyInviterSelection.DefaultLeaderPlayer(topology, 1, true) != 1 ||
            AutoPartyInviterSelection.DefaultLeaderPlayer(topology, 6, true) != 1 ||
            AutoPartyInviterSelection.DefaultLeaderPlayer(topology, 5, false) != 1 ||
            AutoPartyInviterSelection.Select(topology, new HashSet<int> { 1, 2 }, 1, 5) != 1 ||
            AutoPartyInviterSelection.Select(topology, new HashSet<int> { 2, 3 }, 1, 5) != 3 ||
            AutoPartyInviterSelection.Select(topology, new HashSet<int> { 2, 3 }, 1, 1) != 2 ||
            AutoPartyInviterSelection.Select(topology, new HashSet<int> { 1, 3 }, 5, 1) != 3)
            throw new Exception("Auto Party inviter priority differs from the supplied build.");

        CheckLeaseRenewalAsync().GetAwaiter().GetResult();
    }

    private static async Task CheckLeaseRenewalAsync()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var renewed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new UnpumpedContext());
        Task lease;
        try
        {
            lease = AutoPartyRecoverySession.KeepBridgeLeaseAsync(_ =>
            {
                if (Interlocked.Increment(ref count) == 3) renewed.TrySetResult();
                return Task.CompletedTask;
            }, TimeSpan.FromMilliseconds(10), cancellation.Token);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
        await renewed.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();
        try { await lease; }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        if (count < 3) throw new Exception("Auto Party stopped renewing its bridge lease during recovery.");

        using var failureCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var failed = AutoPartyRecoverySession.KeepBridgeLeaseAsync(_ =>
            throw new IOException("Bridge renewal failed"), TimeSpan.FromMilliseconds(10),
            failureCancellation.Token);
        try
        {
            await failed;
            throw new Exception("A failed Auto Party bridge renewal was ignored.");
        }
        catch (IOException error) when (error.Message == "Bridge renewal failed") { }
    }

    private sealed class UnpumpedContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) { }
    }
}
