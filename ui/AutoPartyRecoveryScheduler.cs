namespace PartyPilot.Desktop;

internal static class AutoPartyRecoveryScheduler
{
    internal static async Task<(bool Complete, int FailedClient)> RunAsync(int clientCount,
        int requestedTargetClient,
        Func<int, IReadOnlySet<int>, CancellationToken, Task<bool>> recover,
        CancellationToken token)
    {
        if (clientCount is < 2 or > 3 || requestedTargetClient < 0 || requestedTargetClient > clientCount)
            throw new ArgumentOutOfRangeException(nameof(requestedTargetClient));
        var stable = requestedTargetClient == 0
            ? new HashSet<int> { 1 }
            : Enumerable.Range(1, clientCount).Where(client => client != requestedTargetClient).ToHashSet();
        var targets = requestedTargetClient == 0
            ? Enumerable.Range(2, clientCount - 1).ToArray()
            : [requestedTargetClient];
        if (targets.Length == 2)
        {
            var attempts = targets.Select(client => recover(client, new HashSet<int>(stable), token)).ToArray();
            var results = await Task.WhenAll(attempts);
            var failed = Array.FindIndex(results, result => !result);
            return failed < 0 ? (true, 0) : (false, targets[failed]);
        }
        foreach (var target in targets)
        {
            token.ThrowIfCancellationRequested();
            if (!await recover(target, stable, token)) return (false, target);
            stable.Add(target);
        }
        return (true, 0);
    }
}
