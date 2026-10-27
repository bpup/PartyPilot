namespace PartyPilot.Desktop;

// Keep one pending UI dispatch per game process while retaining its newest
// movement sample. Full radar frames use their separate ordered dispatch path.
internal sealed class RadarMotionMailbox
{
    private readonly object _gate = new();
    private RadarMotionFrame? _latest;
    private bool _scheduled;

    public bool Offer(RadarMotionFrame frame)
    {
        lock (_gate)
        {
            if (_latest is not null && frame.SeenUtc <= _latest.SeenUtc) return false;
            _latest = frame;
            if (_scheduled) return false;
            _scheduled = true;
            return true;
        }
    }

    public RadarMotionFrame? TakeLatest()
    {
        lock (_gate)
        {
            var latest = _latest;
            _latest = null;
            _scheduled = false;
            return latest;
        }
    }
}
