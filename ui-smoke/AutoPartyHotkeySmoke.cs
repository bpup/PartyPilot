using PartyPilot.Desktop;

internal static class AutoPartyHotkeySmoke
{
    internal static void Run()
    {
        var gate = new AutoPartyHotkeyGate();
        if (gate.Handle(true, false) != (false, false) ||
            gate.Handle(true, true) != (true, true) ||
            gate.Handle(true, true) != (true, false) ||
            gate.Handle(false, false) != (false, false) ||
            gate.Handle(true, true) != (true, true) ||
            gate.Handle(false, true) != (true, false))
            throw new Exception("F8 did not trigger once per game-foreground key press.");
    }
}
