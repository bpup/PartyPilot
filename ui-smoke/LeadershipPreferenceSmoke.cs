using PartyPilot.Desktop;

internal static class LeadershipPreferenceSmoke
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "partypilot-leadership-smoke-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "leadership.json");
        try
        {
            var store = new LeadershipPreferenceStore(path);
            var first = store.Load();
            if (!first.Success || !first.LastFocusedPlayerLeads)
                throw new Exception("New profiles must use the observed enabled default.");
            if (!store.Save(false).Success || store.Load() is not { Success: true, LastFocusedPlayerLeads: false })
                throw new Exception("An explicitly disabled leadership choice did not survive reload.");
            File.WriteAllText(path, "{}");
            if (store.Load().Success)
                throw new Exception("Missing preference field silently changed leadership behavior.");
            if (!store.Save(true).Success || store.Load() is not { Success: true, LastFocusedPlayerLeads: true })
                throw new Exception("Repaired leadership choice did not survive reload.");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
