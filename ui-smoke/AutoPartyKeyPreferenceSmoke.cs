using PartyPilot.Desktop;

internal static class AutoPartyKeyPreferenceSmoke
{
    internal static void Run()
    {
        var path = Path.Combine(Path.GetTempPath(), "partypilot-party-key-" +
            Guid.NewGuid().ToString("N"), "key.json");
        var store = new GameShortcutPreferenceStore("F8", "auto-party-key.json", path);
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
        var follow = new GameShortcutPreferenceStore("F7", "follow-pause-key.json",
            Path.Combine(Path.GetDirectoryName(path)!, "follow.json"));
        if (follow.Load() != (true, "F7") || !follow.Save("F6") ||
            follow.Load() != (true, "F6"))
            throw new Exception("Follow pause default and saved key were not preserved.");
        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }
}
