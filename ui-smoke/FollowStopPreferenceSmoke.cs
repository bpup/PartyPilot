using PartyPilot.Desktop;

internal static class FollowStopPreferenceSmoke
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "partypilot-follow-stop-smoke-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "follow-stops.json");
        try
        {
            var store = new FollowStopPreferenceStore(path);
            if (store.Load() is not { Success: true, Stops: [null, null, null, null, null, null] })
                throw new Exception("A new profile unexpectedly assigned uncalibrated stop distances.");
            if (!store.Save([null, null, 37.25, null, null, null]).Success ||
                store.Load() is not { Success: true, Stops: [null, null, 37.25, null, null, null] })
                throw new Exception("Physical slot P3's stop distance did not survive reload.");
            File.WriteAllText(path, "{\"Stops\":[null,37.25]}");
            if (store.Load().Success)
                throw new Exception("Incomplete stop settings were silently accepted.");
            File.WriteAllText(path, "{\"Stops\":[null,null,201,null,null,null]}");
            if (store.Load().Success)
                throw new Exception("Out-of-range stop distance was silently accepted.");
            if (!store.Save([null, null, 38.5, null, null, null]).Success ||
                store.Load() is not { Success: true, Stops: [null, null, 38.5, null, null, null] })
                throw new Exception("Explicit stop distance repair did not survive reload.");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
