using PartyPilot.Desktop;

internal static class FollowTargetPreferenceSmoke
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "partypilot-follow-target-smoke-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "follow-targets.json");
        try
        {
            var store = new FollowTargetPreferenceStore(path);
            if (store.Load() is not { Success: true, Targets: [0, 0, 0, 0, 0, 0] })
                throw new Exception("New profiles must use the observed Current MAIN defaults.");
            if (!store.Save([0, 3, 1, 0, 0, 0]).Success ||
                store.Load() is not { Success: true, Targets: [0, 3, 1, 0, 0, 0] })
                throw new Exception("Six explicit follow targets did not survive reload.");
            File.WriteAllText(path, "{\"Targets\":[0,3]}");
            if (store.Load().Success)
                throw new Exception("Incomplete follow settings were silently accepted.");
            File.WriteAllText(path, "{\"Targets\":[0,3,9,0,0,0]}");
            if (store.Load().Success)
                throw new Exception("Out-of-range follow target was silently accepted.");
            if (!store.Save([0, 0, 0, 0, 0, 0]).Success ||
                store.Load() is not { Success: true, Targets: [0, 0, 0, 0, 0, 0] })
                throw new Exception("Repaired follow target choices did not survive reload.");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
