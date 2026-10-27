using PartyPilot.Desktop;
using PartyPilot.Common;

internal static class HybridBridgeReadySmoke
{
    internal static void Run()
    {
        const string ready = "{\"event\":\"ready\",\"slots\":[1,2,3,4]," +
            "\"physical_mode\":false,\"hybrid_mode\":true,\"menu_guard_enabled\":true," +
            "\"hybrid_player_pads\":\"2:3,3:4\"," +
            "\"hybrid_window_map\":\"1:10:100,2:20:200,3:20:200\"}";
        if (!BridgeSession.IsExpectedHybridReady(ready, [1, 2, 3, 4], "2:3,3:4",
                "1:10:100,2:20:200,3:20:200") ||
            BridgeSession.IsExpectedHybridReady(ready, [1, 2, 3, 4], "2:3,3:4",
                "1:10:100,2:20:200,3:20:200", menuGuardEnabled: false) ||
            !BridgeSession.IsExpectedHybridReady(ready.Replace("\"menu_guard_enabled\":true",
                "\"menu_guard_enabled\":false"), [1, 2, 3, 4], "2:3,3:4",
                "1:10:100,2:20:200,3:20:200", menuGuardEnabled: false) ||
            BridgeSession.IsExpectedHybridReady(ready.Replace("\"physical_mode\":false",
                "\"physical_mode\":true"), [1, 2, 3, 4], "2:3,3:4",
                "1:10:100,2:20:200,3:20:200") ||
            BridgeSession.IsExpectedHybridReady(ready, [1, 2, 3, 4], "2:3,3:4",
                "1:10:100,2:21:200,3:20:200"))
            throw new Exception("Desktop accepted a keyboard bridge with mismatched windows or physical mode.");

        if (!HybridBindingConfig.TryCreate(HybridBindingConfig.Definitions
                .Select(item => item.DefaultKey).ToArray(),
                [new HybridCustomAction("额外确认", "第二个 A 键", 70, 11, true)],
                [11, 19], out var bindings) ||
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
        var followerMirror = FollowerMirrorConfig.Default.WithPlayer(2, 1 | 4);
        var configured = ready[..^1] + ",\"follower_mirror\":\"" + followerMirror.Encode() + "\"}";
        if (!BridgeSession.IsExpectedHybridReady(configured, [1, 2, 3, 4], "2:3,3:4",
                "1:10:100,2:20:200,3:20:200", followerMirror: followerMirror) ||
            BridgeSession.IsExpectedHybridReady(ready, [1, 2, 3, 4], "2:3,3:4",
                "1:10:100,2:20:200,3:20:200", followerMirror: followerMirror) ||
            BridgeSession.IsExpectedHybridReady(configured, [1, 2, 3, 4], "2:3,3:4",
                "1:10:100,2:20:200,3:20:200", followerMirror: FollowerMirrorConfig.Default))
            throw new Exception("Desktop did not require the exact Hybrid follower mirror setting.");
        var client3 = new PrivateClient3Plan(30, 300, Path.GetFullPath("Game3.exe"), false);
        const string privateReady = "{\"event\":\"ready\",\"slots\":[1,2,3,4]," +
            "\"physical_mode\":false,\"hybrid_mode\":true,\"menu_guard_enabled\":true," +
            "\"hybrid_player_pads\":\"2:3,3:5\"," +
            "\"hybrid_window_map\":\"1:10:100,2:20:200,3:30:300\"," +
            "\"private_client3_pid\":30,\"private_client3_window_handle\":300," +
            "\"private_client3_slots\":[5]}";
        if (!BridgeSession.IsExpectedHybridReady(privateReady, [1, 2, 3, 4], "2:3,3:5",
                "1:10:100,2:20:200,3:30:300", privatePlan: client3) ||
            BridgeSession.IsExpectedHybridReady(privateReady.Replace("\"private_client3_pid\":30",
                "\"private_client3_pid\":20"), [1, 2, 3, 4], "2:3,3:5",
                "1:10:100,2:20:200,3:30:300", privatePlan: client3) ||
            BridgeSession.IsExpectedHybridReady(privateReady.Replace("\"private_client3_slots\":[5]",
                "\"private_client3_slots\":[5,6]"), [1, 2, 3, 4], "2:3,3:5",
                "1:10:100,2:20:200,3:30:300", privatePlan: client3))
            throw new Exception("Hybrid Client 3 receipt did not bind the private process and pad set.");
        var path = Path.Combine(Path.GetTempPath(), $"partypilot-hybrid-{Guid.NewGuid():N}.json");
        try
        {
            var store = new HybridBindingPreferenceStore(path);
            if (!store.Save(bindings) || !store.Load().Success ||
                store.Load().Bindings.Encode() != bindings.Encode() ||
                store.Load().Bindings.CustomActions.Single().Label != "额外确认" ||
                store.Load().Bindings.CustomActions.Single().Description != "第二个 A 键" ||
                !store.Load().Bindings.CustomActions.Single().BlockDuringMenu ||
                !store.Load().Bindings.MenuBlockedBaseActions.SequenceEqual([11, 19]))
                throw new Exception("Hybrid binding preference did not round-trip.");
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new
            {
                keys = string.Join(',', HybridBindingConfig.Definitions.Select(item => item.DefaultKey)),
                custom = Array.Empty<HybridCustomAction>()
            }));
            if (!store.Load().Success ||
                !store.Load().Bindings.MenuBlockedBaseActions.SequenceEqual([19]))
                throw new Exception("Older Hybrid binding preferences lost their default menu mask.");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
