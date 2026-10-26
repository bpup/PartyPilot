using PartyPilot.Desktop;

internal static class AutoPartyRecoverySchedulerSmoke
{
    internal static async Task RunAsync()
    {
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var starts = 0;
        var parallel = await AutoPartyRecoveryScheduler.RunAsync(3, 0, async (client, stable, _) =>
        {
            if (client is not (2 or 3) || !stable.SetEquals([1]))
                throw new Exception("Parallel recovery did not use the same initial stable Client 1.");
            if (Interlocked.Increment(ref starts) == 2) bothStarted.TrySetResult();
            await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            return client == 2;
        }, CancellationToken.None);
        if (starts != 2 || parallel != (false, 3))
            throw new Exception("Three-client recovery did not launch both independent targets.");

        var directed = await AutoPartyRecoveryScheduler.RunAsync(3, 1, (client, stable, _) =>
            Task.FromResult(client == 1 && stable.SetEquals([2, 3])), CancellationToken.None);
        if (directed != (true, 0))
            throw new Exception("Directed recovery did not retain the other stable clients.");
        var twoClient = await AutoPartyRecoveryScheduler.RunAsync(2, 0, (client, stable, _) =>
            Task.FromResult(client == 2 && stable.SetEquals([1])), CancellationToken.None);
        if (twoClient != (true, 0))
            throw new Exception("Two-client recovery changed its target or stable baseline.");
    }
}
