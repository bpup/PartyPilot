namespace PartyPilot.Desktop;

// Target 0.6.17 handles one press per key-down cycle while a game window owns focus.
internal sealed class AutoPartyHotkeyGate
{
    private bool _down;

    internal void Reset() => _down = false;

    internal (bool Consume, bool Trigger) Handle(bool isDown, bool gameForeground)
    {
        if (!isDown)
        {
            _down = false;
            return (gameForeground, false);
        }
        if (!gameForeground) return (false, false);
        if (_down) return (true, false);
        _down = true;
        return (true, true);
    }
}
