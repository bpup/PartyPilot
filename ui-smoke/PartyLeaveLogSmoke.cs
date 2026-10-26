using PartyPilot.Desktop;
using System.Text;

internal static class PartyLeaveLogSmoke
{
    internal static async Task RunAsync()
    {
        var left = PartyLeaveLogParser.Parse("2026/08/09 : Test Target has left the party.", 2);
        var removed = PartyLeaveLogParser.Parse("2026/08/09 : Test Target was removed from the party.", 2);
        if (left != new PartyLeaveSignal(PartyLeaveKind.MemberLeft, "Test Target", 2) ||
            removed != left ||
            PartyLeaveLogParser.Parse("2026/08/09 : Test Target has left the area.", 2) is not null ||
            PartyLeaveLogParser.Parse("You have been kicked from the party.", 1)?.Kind != PartyLeaveKind.LocalPlayerLeft ||
            PartyLeaveLogParser.Parse("The party has been disbanded.", 1)?.Kind != PartyLeaveKind.PartyDisbanded)
            throw new Exception("Target-version party leave parsing changed.");
        var topology = PartyTopologies.DefaultFor(5);
        string?[] names = ["P1", "P2", "P3", "P4", "P5"];
        if (PartyLeaveRouting.ResolveTarget(new(PartyLeaveKind.MemberLeft, "p4", 1), topology, names) != 2 ||
            PartyLeaveRouting.ResolveTarget(new(PartyLeaveKind.LocalPlayerLeft, null, 3), topology, names) != 3 ||
            PartyLeaveRouting.ResolveTarget(new(PartyLeaveKind.PartyDisbanded, null, 2), topology, names) != 0 ||
            PartyLeaveRouting.ResolveTarget(new(PartyLeaveKind.MemberLeft, "Unknown", 2), topology, names) != -1)
            throw new Exception("Party leave signal routed to the wrong client.");

        var root = Path.Combine(Path.GetTempPath(), "partypilot-party-log-" + Guid.NewGuid().ToString("N"));
        var paths = Enumerable.Range(1, 2).Select(client => Path.Combine(root, client.ToString(), "logs", "Client.txt")).ToArray();
        try
        {
            foreach (var path in paths)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "Historical has left the party.\n",
                    path == paths[0] ? Encoding.Unicode : Encoding.UTF8);
            }
            var clients = paths.Select((path, index) => new LaunchedClient(index + 1, index + 1,
                Path.Combine(Directory.GetParent(Path.GetDirectoryName(path)!)!.FullName, "PathOfExile.exe"))).ToArray();
            using var monitor = new PartyLeaveLogMonitor(clients);
            var observed = new List<PartyLeaveSignal>();
            var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            monitor.Signal += signal => { lock (observed) observed.Add(signal); received.TrySetResult(); };
            await Task.Delay(350);
            lock (observed) if (observed.Count != 0) throw new Exception("Historical party lines were replayed.");
            File.AppendAllText(paths[1], "Fresh has left the par");
            await Task.Delay(350);
            lock (observed) if (observed.Count != 0) throw new Exception("An incomplete party line triggered recovery.");
            File.AppendAllText(paths[1], "ty.\n");
            await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
            lock (observed) if (observed.Count != 1 ||
                observed[0] != new PartyLeaveSignal(PartyLeaveKind.MemberLeft, "Fresh", 2))
                throw new Exception("Fresh completed party line was not attributed to Client 2.");
            File.AppendAllText(paths[0], "Wide has left the party.\n", Encoding.Unicode);
            await WaitForCountAsync(observed, 2);
            lock (observed) if (observed[1] != new PartyLeaveSignal(PartyLeaveKind.MemberLeft, "Wide", 1))
                throw new Exception("A UTF-16 party log line was not decoded for Client 1.");
            File.WriteAllText(paths[1], "Rotated has left the party.\n", Encoding.UTF8);
            await WaitForCountAsync(observed, 3);
            lock (observed) if (observed[2] != new PartyLeaveSignal(PartyLeaveKind.MemberLeft, "Rotated", 2))
                throw new Exception("A truncated replacement log was not read from its beginning.");

            var delayedGame = Path.Combine(root, "delayed", "PathOfExile.exe");
            var otherGame = Path.Combine(root, "other", "PathOfExile.exe");
            var delayedClients = new[]
            {
                new LaunchedClient(3, 3, delayedGame),
                new LaunchedClient(4, 4, otherGame)
            };
            using var delayed = new PartyLeaveLogMonitor(delayedClients);
            var created = new TaskCompletionSource<PartyLeaveSignal>(TaskCreationOptions.RunContinuationsAsynchronously);
            delayed.Signal += signal => created.TrySetResult(signal);
            var delayedPath = Path.Combine(root, "delayed", "logs", "Client.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(delayedPath)!);
            File.WriteAllText(delayedPath, "Created has left the party.\n");
            if (await created.Task.WaitAsync(TimeSpan.FromSeconds(3)) !=
                new PartyLeaveSignal(PartyLeaveKind.MemberLeft, "Created", 1))
                throw new Exception("A log created after monitoring began was not read.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static async Task WaitForCountAsync(List<PartyLeaveSignal> observed, int expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (true)
        {
            lock (observed) if (observed.Count >= expected) return;
            await Task.Delay(25, timeout.Token);
        }
    }
}
