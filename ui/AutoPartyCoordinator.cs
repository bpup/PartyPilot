namespace PartyPilot.Desktop;

internal enum AutoPartyState
{
    Off,
    Stable,
    Missing,
    SelectingInviter,
    SendingInvite,
    AcceptingInvite,
    Verifying,
    RetryWait,
    Failed
}

internal sealed record AutoPartySnapshot(
    AutoPartyState State, int TargetClient, int InviterClient, int Attempt,
    string Status, DateTimeOffset ChangedAt);

// Recovery state and retry schedule from the supplied 0.6.17 build.
// This class does not send game input or decide that a roster is verified.
internal sealed class AutoPartyCoordinator
{
    internal const int MaximumAttempts = 10;
    private static readonly int[] RetryDelaysMilliseconds = [250, 400, 650, 900];

    internal AutoPartySnapshot Snapshot { get; private set; } =
        new(AutoPartyState.Off, 0, 0, 0, "Auto Party is off.", DateTimeOffset.MinValue);

    internal int CurrentRetryDelayMilliseconds =>
        RetryDelaysMilliseconds[Math.Clamp(Snapshot.Attempt - 1, 0, RetryDelaysMilliseconds.Length - 1)];

    internal AutoPartySnapshot Begin(int targetClient, int inviterClient, DateTimeOffset now)
    {
        if (targetClient is < 1 or > 3 || inviterClient is < 1 or > 3 || targetClient == inviterClient)
            throw new ArgumentOutOfRangeException(nameof(targetClient));
        return Set(AutoPartyState.Missing, targetClient, inviterClient, 0,
            $"Client {targetClient} is missing; selecting a stable inviter.", now);
    }

    internal AutoPartySnapshot BeginAttempt(int inviterClient, DateTimeOffset now)
    {
        if (Snapshot.TargetClient == 0 || inviterClient == Snapshot.TargetClient)
            throw new InvalidOperationException("No serialized Auto Party recovery is active.");
        var attempt = Snapshot.Attempt + 1;
        return attempt > MaximumAttempts
            ? Set(AutoPartyState.Failed, Snapshot.TargetClient, inviterClient, Snapshot.Attempt,
                "Maximum fresh-invite attempts reached.", now)
            : Set(AutoPartyState.SendingInvite, Snapshot.TargetClient, inviterClient, attempt,
                $"Sending fresh invite attempt {attempt}/{MaximumAttempts}.", now);
    }

    internal AutoPartySnapshot Accepting(DateTimeOffset now) =>
        Set(AutoPartyState.AcceptingInvite, Snapshot.TargetClient, Snapshot.InviterClient, Snapshot.Attempt,
            $"Client {Snapshot.TargetClient} is accepting the fresh invite.", now);

    internal AutoPartySnapshot Verifying(DateTimeOffset now) =>
        Set(AutoPartyState.Verifying, Snapshot.TargetClient, Snapshot.InviterClient, Snapshot.Attempt,
            $"Verifying Client {Snapshot.TargetClient} party roster stability.", now);

    internal AutoPartySnapshot Retry(DateTimeOffset now) => Snapshot.Attempt >= MaximumAttempts
        ? Set(AutoPartyState.Failed, Snapshot.TargetClient, Snapshot.InviterClient, Snapshot.Attempt,
            "Maximum fresh-invite attempts reached.", now)
        : Set(AutoPartyState.RetryWait, Snapshot.TargetClient, Snapshot.InviterClient, Snapshot.Attempt,
            "Roster verification failed; the next attempt will send a new invite.", now);

    internal AutoPartySnapshot Stable(DateTimeOffset now) =>
        Set(AutoPartyState.Stable, 0, 0, 0, "Desired party is stable.", now);

    internal AutoPartySnapshot Stop(string status, DateTimeOffset now) =>
        Set(AutoPartyState.Off, 0, 0, 0, status, now);

    private AutoPartySnapshot Set(AutoPartyState state, int targetClient, int inviterClient,
        int attempt, string status, DateTimeOffset now) =>
        Snapshot = new(state, targetClient, inviterClient, attempt, status, now);
}
