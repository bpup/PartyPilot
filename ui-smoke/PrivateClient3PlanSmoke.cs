using PartyPilot.Desktop;

internal static class PrivateClient3PlanSmoke
{
    public static void Run()
    {
        var clients = new[]
        {
            new LaunchedClient(101, 201, Path.GetFullPath("PathOfExile_x64.exe")),
            new LaunchedClient(102, 202, Path.GetFullPath("PathOfExile_x64.exe")),
            new LaunchedClient(103, 203, Path.GetFullPath("PathOfExile_x64.exe"))
        };
        foreach (var topology in Enumerable.Range(1, 6).SelectMany(PartyTopologies.ForPlayerCount)
                     .Where(item => item.ClientCount == 3))
        {
            if (!PrivateClient3Plan.TryCreate(topology, clients, out var plan) || plan is null ||
                plan.Pid != 103 || plan.WindowHandle != 203 ||
                plan.IncludePad6 != (topology.Slots.Count(slot => slot.Client == 3) == 2))
                throw new Exception($"Client 3 slots were not derived from topology {topology.Id}.");
            var ready = plan.IncludePad6
                ? "{\"event\":\"private_client3_ready\",\"pid\":103,\"players\":[5,6],\"local_indices\":[0,1]}"
                : "{\"event\":\"private_client3_ready\",\"pid\":103,\"players\":[5],\"local_indices\":[0]}";
            if (!plan.MatchesReady(ready) ||
                plan.MatchesReady(ready.Replace("\"pid\":103", "\"pid\":102")) ||
                plan.MatchesReady(ready.Replace("\"local_indices\":[0", "\"local_indices\":[1")) ||
                plan.MatchesReady("not JSON"))
                throw new Exception("Client 3 accepted an incorrect ready identity or slot mapping.");
        }
        if (PrivateClient3Plan.TryCreate(PartyTopologies.DefaultFor(2), clients, out _) ||
            PrivateClient3Plan.TryCreate(PartyTopologies.ForPlayerCount(6).Single(), clients[..2], out _) ||
            PrivateClient3Plan.TryCreate(PartyTopologies.ForPlayerCount(6).Single(),
                [clients[0], clients[1], clients[2] with { WindowHandle = 0 }], out _))
            throw new Exception("Client 3 accepted an incomplete process/window binding.");

        var composite = PrivateClient3Plan.TryCreate(PartyTopologies.ForPlayerCount(6).Single(),
            clients, out var sixPlan) ? sixPlan : null;
        if (composite is null) throw new Exception("Six-player Client 3 binding was unavailable.");
        const string focusMap = "1:1,2:2,3:3,4:4,5:5,6:6";
        const string bridgeReady = """
        {"event":"ready","slots":[1,2,3,4],"physical_mode":true,"physical_wgi":true,"pairing_protected":true,"hidhide_preflight":true,"auto_flask_enabled":false,"right_stick_follower_override":false,"direct_right_stick_slot":0,"focus_pad_map":"1:1,2:2,3:3,4:4,5:5,6:6","private_client3_pid":103,"private_client3_window_handle":203,"private_client3_slots":[5,6]}
        """;
        if (!BridgeSession.IsExpectedReady(bridgeReady, [1, 2, 3, 4], focusMap,
                false, 0, false, true, composite) ||
            BridgeSession.IsExpectedReady(bridgeReady.Replace("\"private_client3_pid\":103",
                "\"private_client3_pid\":102"), [1, 2, 3, 4], focusMap,
                false, 0, false, true, composite) ||
            BridgeSession.IsExpectedReady(bridgeReady.Replace("\"private_client3_slots\":[5,6]",
                "\"private_client3_slots\":[5]"), [1, 2, 3, 4], focusMap,
                false, 0, false, true, composite) ||
            BridgeSession.IsExpectedReady(bridgeReady, [1, 2, 3, 4], focusMap,
                false, 0, false, true, null))
            throw new Exception("Composite bridge ready contract accepted the wrong private process or slots.");
    }
}
