using PartyPilot.Desktop;

internal static class RightStickDodgePreferenceSmoke
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(),
            "partypilot-dodge-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "right-stick-dodge.json");
        try
        {
            var store = new RightStickDodgePreferenceStore(path);
            var initial = store.Load();
            if (!initial.Success || initial.Enabled || initial.Binding != "B" ||
                !store.Save(true, "rb"))
                throw new Exception("The target's disabled/B default or binding save failed.");
            var saved = store.Load();
            if (!saved.Success || !saved.Enabled || saved.Binding != "RB" ||
                store.Save(true, "Start"))
                throw new Exception("Right-stick dodge setting accepted an unknown binding.");
            File.WriteAllText(path, "{\"enabled\":true,\"binding\":\"Start\"}");
            if (store.Load().Success)
                throw new Exception("Invalid right-stick dodge preference was silently accepted.");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
