using PartyPilot.Desktop;

internal static class AutoLoginStartupPlanSmoke
{
    internal static void Run()
    {
        var split = PartyTopologies.ForPlayerCount(2).Single(topology => topology.Id == "2-split");
        var clients = new[]
        {
            new LaunchedClient(101, 201, "game.exe"),
            new LaunchedClient(102, 202, "game.exe")
        };
        var calibration = new LoginCalibration(.42, .52, .43, .62, 1000, 800, split.Id);
        var configs = new[]
        {
            new AutoLoginStartupConfiguration(true,
                new AccountCredential("a@example.invalid", "synthetic-a"), calibration),
            new AutoLoginStartupConfiguration(true,
                new AccountCredential("b@example.invalid", "synthetic-b"), calibration)
        };
        if (!AutoLoginStartupPlan.TryCreate(split, clients, configs, out var ready, out _) ||
            ready.Select(item => item.Client).SequenceEqual([1, 2]) == false)
            throw new Exception("Two standalone clients were not prepared in source order.");
        if (AutoLoginStartupPlan.TryCreate(split, clients,
                [configs[0], configs[1] with { Calibration = null }], out _, out _) ||
            AutoLoginStartupPlan.TryCreate(split, clients,
                [configs[0], configs[1] with
                {
                    Credential = new AccountCredential("A@example.invalid", "synthetic-b")
                }], out _, out _) ||
            AutoLoginStartupPlan.TryCreate(split, clients[..1], configs, out _, out _))
            throw new Exception("Incomplete, duplicate, or mismatched automatic login plan was accepted.");
        var manual = configs.Select(config => config with { Enabled = false }).ToArray();
        if (!AutoLoginStartupPlan.TryCreate(split, clients, manual, out var none, out _) || none.Count != 0)
            throw new Exception("Manual login unexpectedly scheduled credential input.");

        foreach (var playerCount in new[] { 4, 6 })
        {
            var topology = PartyTopologies.ForPlayerCount(playerCount).Single(item =>
                item.Id == (playerCount == 4 ? "4-balanced" : "6-balanced"));
            var bound = Enumerable.Range(1, topology.ClientCount)
                .Select(client => new LaunchedClient(100 + client, 200 + client, "game.exe"))
                .ToArray();
            var perClient = Enumerable.Range(1, topology.ClientCount)
                .Select(client => new AutoLoginStartupConfiguration(true,
                    new AccountCredential($"client{client}@example.invalid", "synthetic"),
                    new LoginCalibration(.42, .52, .43, .62, 1000, 800, topology.Id)))
                .ToArray();
            if (!AutoLoginStartupPlan.TryCreate(topology, bound, perClient,
                    out var planned, out _) ||
                !planned.Select(item => item.Client).SequenceEqual(
                    Enumerable.Range(1, topology.ClientCount)))
                throw new Exception("Dual-role clients did not receive one login plan per window.");
            if (AutoLoginStartupPlan.TryCreate(topology, bound,
                    perClient.Select((item, index) => index == 1 ? item with
                    {
                        Credential = perClient[0].Credential
                    } : item).ToArray(), out _, out _))
                throw new Exception("Duplicate accounts across dual-role clients were accepted.");
        }
    }
}
