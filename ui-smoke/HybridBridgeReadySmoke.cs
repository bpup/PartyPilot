using PartyPilot.Desktop;

internal static class HybridBridgeReadySmoke
{
    internal static void Run()
    {
        const string ready = "{\"event\":\"ready\",\"slots\":[1,2,3,4]," +
            "\"physical_mode\":false,\"hybrid_mode\":true," +
            "\"hybrid_player_pads\":\"2:3,3:4\"," +
            "\"hybrid_window_map\":\"1:10:100,2:20:200,3:20:200\"}";
        if (!BridgeSession.IsExpectedHybridReady(ready, [1, 2, 3, 4], "2:3,3:4",
                "1:10:100,2:20:200,3:20:200") ||
            BridgeSession.IsExpectedHybridReady(ready.Replace("\"physical_mode\":false",
                "\"physical_mode\":true"), [1, 2, 3, 4], "2:3,3:4",
                "1:10:100,2:20:200,3:20:200") ||
            BridgeSession.IsExpectedHybridReady(ready, [1, 2, 3, 4], "2:3,3:4",
                "1:10:100,2:21:200,3:20:200"))
            throw new Exception("Desktop accepted a keyboard bridge with mismatched windows or physical mode.");
    }
}
