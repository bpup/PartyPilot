using PartyPilot.Desktop;

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
                File.WriteAllText(path, "Historical has left the party.\n");
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
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
