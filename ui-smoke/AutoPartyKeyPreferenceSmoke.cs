using PartyPilot.Desktop;

internal static class AutoPartyKeyPreferenceSmoke
{
    internal static void Run()
    {
        var path = Path.Combine(Path.GetTempPath(), "partypilot-party-key-" +
            Guid.NewGuid().ToString("N"), "key.json");
        var store = new AutoPartyKeyPreferenceStore(path);
        if (store.Load() != (true, "F8") ||
            AutoPartyKeyBinding.Normalize("f24") != "F24" ||
            AutoPartyKeyBinding.VirtualKey("F24") != 135 ||
            AutoPartyKeyBinding.VirtualKey("Numpad 9") != 105 ||
            AutoPartyKeyBinding.Normalize("M12") is not null ||
            AutoPartyKeyBinding.Normalize("F25") is not null ||
            !store.Save("F9") || store.Load() != (true, "F9") ||
            !store.Save("None") || store.Load() != (true, "None") ||
            store.Save("M12"))
            throw new Exception("Auto Party keyboard settings lost their supported range or default.");
        File.WriteAllText(path, "{\"Key\":\"F25\"}");
        if (store.Load().Success)
            throw new Exception("An unsupported Auto Party key was accepted from storage.");
        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }
}
