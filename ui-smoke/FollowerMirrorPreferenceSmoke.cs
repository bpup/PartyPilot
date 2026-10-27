using PartyPilot.Common;
using PartyPilot.Desktop;

internal static class FollowerMirrorPreferenceSmoke
{
    internal static void Run()
    {
        var path = Path.Combine(Path.GetTempPath(),
            $"partypilot-follower-mirror-{Guid.NewGuid():N}", "follower-mirror.json");
        try
        {
            var store = new FollowerMirrorPreferenceStore(path);
            if (!store.Load().Success || store.Load().Config.Encode() != FollowerMirrorConfig.Default.Encode())
                throw new Exception("New follower settings did not use the target defaults.");
            var selected = FollowerMirrorConfig.Default.WithPlayer(2, 1 | 2 | 128);
            if (!store.Save(selected) || store.Load().Config.Encode() != selected.Encode())
                throw new Exception("Per-player follower settings did not survive reload.");
            File.WriteAllText(path, "{\"players\":\"1:8191\"}");
            if (store.Load().Success)
                throw new Exception("Incomplete follower settings were silently accepted.");
            var receipt = "{\"event\":\"ready\",\"slots\":[1],\"physical_mode\":true," +
                "\"pairing_protected\":true,\"hidhide_preflight\":true,\"auto_flask_enabled\":false," +
                "\"menu_guard_enabled\":true,\"last_focused_leads\":false," +
                "\"right_stick_follower_override\":false,\"direct_right_stick_slot\":0," +
                "\"focus_pad_map\":\"1:1\",\"private_client3_pid\":0," +
                "\"private_client3_window_handle\":0,\"private_client3_slots\":[]," +
                "\"follower_mirror\":\"" + selected.Encode() + "\"}";
            if (!BridgeSession.IsExpectedReady(receipt, [1], "1:1", false, 0, false, false,
                    null, followerMirror: selected) ||
                BridgeSession.IsExpectedReady(receipt, [1], "1:1", false, 0, false, false,
                    null, followerMirror: FollowerMirrorConfig.Default))
                throw new Exception("Bridge receipt did not verify exact follower settings.");
        }
        finally
        {
            if (Directory.Exists(Path.GetDirectoryName(path)))
                Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
