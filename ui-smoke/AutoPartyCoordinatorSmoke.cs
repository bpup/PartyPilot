using PartyPilot.Desktop;

internal static class AutoPartyCoordinatorSmoke
{
    public static void Run()
    {
        var now = DateTimeOffset.UtcNow;
        var coordinator = new AutoPartyCoordinator();
        if (coordinator.Snapshot.State != AutoPartyState.Off) throw new Exception("Auto Party was not off initially.");
        var missing = coordinator.Begin(3, 1, now);
        var first = coordinator.BeginAttempt(1, now.AddMilliseconds(1));
        var accepting = coordinator.Accepting(now.AddMilliseconds(2));
        var verifying = coordinator.Verifying(now.AddMilliseconds(3));
        var retry = coordinator.Retry(now.AddMilliseconds(4));
        var second = coordinator.BeginAttempt(2, now.AddMilliseconds(5));
        if (missing.State != AutoPartyState.Missing || first.State != AutoPartyState.SendingInvite ||
            first.Attempt != 1 || accepting.State != AutoPartyState.AcceptingInvite ||
            verifying.State != AutoPartyState.Verifying || retry.State != AutoPartyState.RetryWait ||
            second.State != AutoPartyState.SendingInvite || second.Attempt != 2 ||
            second.InviterClient != 2 || coordinator.CurrentRetryDelayMilliseconds != 400)
            throw new Exception("Fresh invitation state or inviter selection changed.");

        for (var attempt = 3; attempt <= AutoPartyCoordinator.MaximumAttempts; attempt++)
        {
            coordinator.Retry(now.AddSeconds(attempt));
            if (coordinator.BeginAttempt(1, now.AddSeconds(attempt + 1)).Attempt != attempt)
                throw new Exception("An invitation attempt was skipped.");
        }
        if (coordinator.CurrentRetryDelayMilliseconds != 900 ||
            coordinator.Retry(now.AddSeconds(20)).State != AutoPartyState.Failed ||
            coordinator.BeginAttempt(1, now.AddSeconds(21)).State != AutoPartyState.Failed ||
            coordinator.Snapshot.Attempt != 10)
            throw new Exception("Auto Party exceeded its bounded retry schedule.");
        if (coordinator.Stable(now.AddSeconds(22)).State != AutoPartyState.Stable ||
            coordinator.Snapshot.Attempt != 0 ||
            coordinator.Stop("Stopped", now.AddSeconds(23)).State != AutoPartyState.Off)
            throw new Exception("Auto Party did not clear its recovery state.");
    }
}
