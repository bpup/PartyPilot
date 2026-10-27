using PartyPilot.Desktop;

internal static class RadarHotkeySmoke
{
    internal static void Run()
    {
        if (!RadarHotkeyBinding.TryParse("Tab", out var tab) || tab.VirtualKey != 9 ||
            !RadarHotkeyBinding.TryParse("ctrl+shift+f9", out var modified) ||
            modified.DisplayText != "Ctrl+Shift+F9" || modified.VirtualKey != 120 ||
            !RadarHotkeyBinding.TryParse("NumPad3", out var numpad) || numpad.VirtualKey != 99 ||
            !RadarHotkeyBinding.TryParse("Ctrl+Numpad +", out var numpadAdd) ||
            numpadAdd.VirtualKey != 107 || numpadAdd.DisplayText != "Ctrl+Numpad +" ||
            !RadarHotkeyBinding.TryParse("Oem1", out var semicolon) ||
            semicolon.DisplayText != ";" ||
            RadarHotkeyBinding.TryParse("Ctrl+Alt", out _) ||
            RadarHotkeyBinding.TryParse("M12", out _) ||
            !modified.IsDown(key => key is 120 or 0x11 or 0x10) ||
            modified.IsDown(key => key is 120 or 0x11) ||
            modified.IsDown(key => key is 120 or 0x11 or 0x10 or 0x12))
            throw new Exception("Radar hotkey parser or modifier matching changed.");
        if (!RadarRegisteredHotkey.UsesPassThroughPolling(tab) ||
            RadarRegisteredHotkey.UsesPassThroughPolling(modified))
            throw new Exception("The game's Tab map key must remain pass-through.");

        var folder = Path.Combine(Path.GetTempPath(), "partypilot-radar-key-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "key.json");
        var store = new RadarHotkeyPreferenceStore(path);
        try
        {
            if (store.Load() != (true, tab) || !store.Save(modified) ||
                store.Load() != (true, modified))
                throw new Exception("Radar hotkey preference was not preserved.");
            File.WriteAllText(path, "[]");
            if (store.Load().Success)
                throw new Exception("Invalid radar hotkey storage was accepted.");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }

        var visibility = new RadarVisibility();
        if (visibility.Enabled) throw new Exception("Radar opened before Tab was pressed.");
        visibility.Observe(true, true);
        visibility.Observe(true, true);
        if (!visibility.Enabled) throw new Exception("Tab did not open radar or repeated while held.");
        visibility.Observe(false, false);
        visibility.Observe(true, true);
        if (visibility.Enabled) throw new Exception("A later Tab press did not hide radar.");
        visibility.Observe(false, true);
        visibility.Observe(true, true);
        if (visibility.Enabled)
            throw new Exception("A held shortcut retoggled radar after leaving and reentering the game.");

        var registered = new RadarVisibility();
        registered.ObserveRegisteredPress(false);
        if (registered.Enabled)
            throw new Exception("A registered shortcut outside the game opened radar.");
        registered.ObserveRegisteredPress(true);
        registered.ObserveRegisteredPress(true);
        if (registered.Enabled)
            throw new Exception("Two separate registered Tab presses did not toggle radar twice.");
    }
}
