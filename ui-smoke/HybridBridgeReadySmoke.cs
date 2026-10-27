using PartyPilot.Desktop;
using PartyPilot.Common;

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

        if (!HybridBindingConfig.TryCreate(HybridBindingConfig.Definitions
                .Select(item => item.DefaultKey).ToArray(),
                [new HybridCustomAction("额外确认", "第二个 A 键", 70, 11)], out var bindings) ||
            bindings is null)
            throw new Exception("An extra Hybrid action could not be configured.");
        var confirmed = ready[..^1] + ",\"hybrid_binding_keys\":\"" + bindings.Encode() + "\"}";
        if (!BridgeSession.IsExpectedHybridReady(confirmed, [1, 2, 3, 4], "2:3,3:4",
                "1:10:100,2:20:200,3:20:200", bindings.Encode()) ||
            BridgeSession.IsExpectedHybridReady(ready, [1, 2, 3, 4], "2:3,3:4",
                "1:10:100,2:20:200,3:20:200", bindings.Encode()) ||
            BridgeSession.IsExpectedHybridReady(confirmed, [1, 2, 3, 4], "2:3,3:4",
                "1:10:100,2:20:200,3:20:200", bindings.Encode() + ",0"))
            throw new Exception("Desktop did not require the exact configured Hybrid key map.");
        var path = Path.Combine(Path.GetTempPath(), $"partypilot-hybrid-{Guid.NewGuid():N}.json");
        try
        {
            var store = new HybridBindingPreferenceStore(path);
            if (!store.Save(bindings) || !store.Load().Success ||
                store.Load().Bindings.Encode() != bindings.Encode() ||
                store.Load().Bindings.CustomActions.Single().Label != "额外确认" ||
                store.Load().Bindings.CustomActions.Single().Description != "第二个 A 键")
                throw new Exception("Hybrid binding preference did not round-trip.");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
