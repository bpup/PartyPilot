namespace PartyPilot.Desktop;

// One source crossing can request at most three entry interactions per destination.
// The pause leaves time for a real area change to arrive before another press.
internal sealed class TransitionInteractionBudget
{
    private static readonly TimeSpan ConfirmationWindow = TimeSpan.FromSeconds(4);
    private readonly Dictionary<int, (DateTime SourceChangedUtc,
        int Attempts, DateTime LastAttemptUtc)> _attempts = new();

    internal int TryReserve(TransitionInteractionCandidate candidate, DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc ||
            candidate.SourceChangedUtc.Kind != DateTimeKind.Utc ||
            nowUtc < candidate.SourceChangedUtc) return 0;
        if (!_attempts.TryGetValue(candidate.DestinationClient, out var state) ||
            state.SourceChangedUtc != candidate.SourceChangedUtc)
        {
            _attempts[candidate.DestinationClient] = (candidate.SourceChangedUtc,
                1, nowUtc);
            return 1;
        }
        if (state.Attempts >= 3 || nowUtc < state.LastAttemptUtc ||
            nowUtc - state.LastAttemptUtc < ConfirmationWindow) return 0;
        var next = state.Attempts + 1;
        _attempts[candidate.DestinationClient] = (state.SourceChangedUtc,
            next, nowUtc);
        return next;
    }
}
