using PartyPilot.Desktop;

internal static class GameKeyboardShortcutSmoke
{
    internal static void Run()
    {
        using var hook = new GameKeyboardShortcutHook(() => true, () => { }, () => { });
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
			GameKeyboardShortcutHook.MouseVirtualKey(0x20B, 3 << 16) != 0 ||
			!hook.SetKeys(65540, 65541) || hook.SetKeys(65540, 65540) ||
			hook.HandleMouse(0x20B, 1 << 16, true) != (true, true, false) ||
			hook.HandleMouse(0x20B, 1 << 16, true) != (true, false, false) ||
			hook.HandleMouse(0x20C, 1 << 16, true) != (true, false, false) ||
			hook.HandleMouse(0x20B, 2 << 16, false) != (false, false, false) ||
			hook.HandleMouse(0x20B, 2 << 16, true) != (true, false, true))
			throw new Exception("Mouse shortcuts did not use the game foreground and press gate.");
    }
}
