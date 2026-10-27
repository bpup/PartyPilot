using PartyPilot.Desktop;

internal static class MenuGuardPreferenceSmoke
{
    internal static void Run()
    {
        var path = Path.Combine(Path.GetTempPath(),
            $"partypilot-menu-guard-{Guid.NewGuid():N}", "menu-guard.json");
        try
        {
            var store = new MenuGuardPreferenceStore(path);
            if (store.Load() != (true, true) || !store.Save(false) ||
                store.Load() != (true, false))
                throw new Exception("Disabled menu protection did not survive reload.");
            File.WriteAllText(path, "{}");
            if (store.Load().Success)
                throw new Exception("Missing menu protection setting was silently accepted.");
            if (!store.Save(true) || store.Load() != (true, true))
                throw new Exception("Enabled menu protection did not survive reload.");
        }
        finally
        {
            if (Directory.Exists(Path.GetDirectoryName(path)))
                Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
