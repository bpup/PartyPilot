namespace PartyPilot.Bridge;

// A release belongs to the virtual route that received the matching press.
internal sealed class PairingSignalTracker
{
    private bool _wasDown;
    private bool _neutralSeen;
    private int _pressSlot;
    private bool _focusChanged;

    public int Observe(bool aDown, int focusedSlot)
    {
        if (!_neutralSeen)
        {
            _neutralSeen = !aDown;
            _wasDown = aDown;
            return 0;
        }
        if (aDown && !_wasDown)
        {
            _pressSlot = focusedSlot;
            _focusChanged = false;
        }
        else if (aDown && _wasDown && focusedSlot != _pressSlot)
        {
            _focusChanged = true;
        }
        else if (!aDown && _wasDown)
        {
            var completedSlot = !_focusChanged && focusedSlot == _pressSlot ? _pressSlot : 0;
            _pressSlot = 0;
            _focusChanged = false;
            _wasDown = false;
            return completedSlot;
        }
        _wasDown = aDown;
        return 0;
    }

    public void Reset()
    {
        _wasDown = false;
        _neutralSeen = false;
        _pressSlot = 0;
        _focusChanged = false;
    }
}
