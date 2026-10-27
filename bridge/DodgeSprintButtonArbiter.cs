namespace PartyPilot.Bridge;

// Matches the supplied 0.6.17 DodgeSprintButtonArbiter timing for the shared B input.
internal sealed class DodgeSprintButtonArbiter
{
    private bool _lastOutputDown;
    private bool _lastMirroredDown;
    private bool _lastSyntheticDown;
    private DateTime _releaseUntil;
    private DateTime _pressUntil;

    internal ushort Resolve(ushort buttons, bool syntheticSprintDown, DateTime nowUtc)
    {
        const ushort b = 0x2000;
        var mirroredDown = (buttons & b) != 0;
        var mirroredEdge = mirroredDown && !_lastMirroredDown;
        var syntheticEdge = syntheticSprintDown && !_lastSyntheticDown &&
            _lastOutputDown && !mirroredDown;
        if (mirroredEdge || syntheticEdge)
        {
            if (_lastOutputDown)
            {
                _releaseUntil = nowUtc.AddMilliseconds(75);
                _pressUntil = _releaseUntil.AddMilliseconds(125);
            }
            else if (nowUtc < _releaseUntil)
                _pressUntil = _releaseUntil.AddMilliseconds(125);
            else
            {
                _releaseUntil = default;
                _pressUntil = nowUtc.AddMilliseconds(125);
            }
        }
        var outputDown = nowUtc >= _releaseUntil &&
            (nowUtc < _pressUntil || mirroredDown || syntheticSprintDown);
        _lastOutputDown = outputDown;
        _lastMirroredDown = mirroredDown;
        _lastSyntheticDown = syntheticSprintDown;
        return outputDown ? (ushort)(buttons | b) : (ushort)(buttons & ~b);
    }

    internal void Reset()
    {
        _lastOutputDown = _lastMirroredDown = _lastSyntheticDown = false;
        _releaseUntil = _pressUntil = default;
    }
}
