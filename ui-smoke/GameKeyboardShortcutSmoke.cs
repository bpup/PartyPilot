using PartyPilot.Desktop;
using PartyPilot.Common;

internal static class GameKeyboardShortcutSmoke
{
    internal static void Run()
    {
        if (!GameKeyboardShortcutHook.IsHybridVirtualKey(0x70) ||
            !GameKeyboardShortcutHook.IsHybridVirtualKey(0x75) ||
            !GameKeyboardShortcutHook.IsHybridVirtualKey(0x57) ||
            !GameKeyboardShortcutHook.IsHybridVirtualKey(65538) ||
            GameKeyboardShortcutHook.IsHybridVirtualKey(0x76) ||
            GameKeyboardShortcutHook.IsHybridVirtualKey(65540))
            throw new Exception("The desktop keyboard hook did not match the hybrid input set.");
        using var hook = new GameKeyboardShortcutHook(() => true, () => { }, () => { });
        var customKeys = HybridBindingConfig.Definitions.Select(item => item.DefaultKey).ToArray();
        customKeys[6] = 73;
        if (!HybridBindingConfig.TryCreate(customKeys,
                [new HybridCustomAction("extra", "", 70, 11)], out var custom) || custom is null)
            throw new Exception("Custom keyboard map could not be created.");
        hook.SetHybridBindings(custom);
        if (!hook.IsMappedHybridKey(73) || !hook.IsMappedHybridKey(70) ||
            hook.IsMappedHybridKey(87) ||
            !hook.IsMappedHybridKey(18))
            throw new Exception("The desktop hook ignored the configured keyboard map or modifier alias.");
        if (hook.SetKeys(0x77, 0x77) || !hook.SetKeys(0x77, 0x76) ||
            hook.HandleKey(0x77, true, false) != (false, false, false) ||
            hook.HandleKey(0x77, true, true) != (true, true, false) ||
            hook.HandleKey(0x77, true, true) != (true, false, false) ||
            hook.HandleKey(0x76, true, true) != (true, false, true) ||
            hook.HandleKey(0x76, true, true) != (true, false, false) ||
            hook.HandleKey(0x76, false, true) != (true, false, false) ||
            hook.HandleKey(0x76, true, true) != (true, false, true) ||
            hook.HandleKey(0x77, false, true) != (true, false, false) ||
            hook.HandleKey(0x77, true, true) != (true, true, false) ||
            hook.HandleKey(0x75, true, true) != (false, false, false))
            throw new Exception("Game shortcut routing duplicated or confused F7 and F8.");
        if (!hook.SetKeys(0, 0x76) ||
            hook.HandleKey(0x77, true, true) != (false, false, false))
            throw new Exception("Disabling Auto Party key left it active.");
		if (GameKeyboardShortcutHook.MouseVirtualKey(0x204, 0) != 65538 ||
			GameKeyboardShortcutHook.MouseVirtualKey(0x207, 0) != 65539 ||
			GameKeyboardShortcutHook.MouseVirtualKey(0x20B, 1 << 16) != 65540 ||
			GameKeyboardShortcutHook.MouseVirtualKey(0x20B, 2 << 16) != 65541 ||
			GameKeyboardShortcutHook.MouseVirtualKey(0x20B, 3 << 16) != 65542 ||
			GameKeyboardShortcutHook.MouseVirtualKey(0x20B, 13 << 16) != 65552 ||
			GameKeyboardShortcutHook.MouseVirtualKey(0x20B, 14 << 16) != 0 ||
			!hook.SetKeys(65540, 65541) || hook.SetKeys(65540, 65540) ||
			hook.HandleMouse(0x20B, 1 << 16, true) != (true, true, false) ||
			hook.HandleMouse(0x20B, 1 << 16, true) != (true, false, false) ||
			hook.HandleMouse(0x20C, 1 << 16, true) != (true, false, false) ||
			hook.HandleMouse(0x20B, 2 << 16, false) != (false, false, false) ||
			hook.HandleMouse(0x20B, 2 << 16, true) != (true, false, true))
			throw new Exception("Mouse shortcuts did not use the game foreground and press gate.");
        if (!hook.SetKeys(65552, 0) ||
            hook.HandleMouse(0x20B, 13 << 16, true) != (true, true, false) ||
            hook.HandleMouse(0x20C, 13 << 16, true) != (true, false, false))
            throw new Exception("Extended mouse shortcut did not use the target button mapping.");
    }
}
