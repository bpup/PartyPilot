namespace PartyPilot.Sensor;

// Read-only candidate state matching the supplied 0.6.17 tracker rules.
// It does not establish that ExileCore2 observations match the target probe.
public sealed class RadarMonsterTracker
{
    private sealed class Observation
    {
        public uint Id;
        public string Path = "";
        public bool HadReadableLife;
        public bool Dead;
        public int MissingLifeFrames;
        public long LastFrame;
    }

    private readonly Dictionary<long, Observation> _observations = new();
    private long _frame;

    public void BeginFrame() => _frame++;

    public bool Observe(long address, uint id, string path, bool readerAlive,
        bool entityValid, int hpCurrent, int hpMax)
    {
        if (!_observations.TryGetValue(address, out var observation) ||
            observation.Id != id || !string.Equals(observation.Path, path, StringComparison.Ordinal))
        {
            observation = new Observation { Id = id, Path = path };
            _observations[address] = observation;
        }
        observation.LastFrame = _frame;
        if (!readerAlive || !entityValid || hpMax > 0 && hpCurrent <= 0)
        {
            observation.Dead = true;
            return false;
        }
        if (hpMax > 0)
        {
            observation.HadReadableLife = true;
            observation.MissingLifeFrames = 0;
            observation.Dead = false;
            return true;
        }
        if (observation.Dead) return false;
        if (observation.HadReadableLife && ++observation.MissingLifeFrames >= 2)
        {
            observation.Dead = true;
            return false;
        }
        return true;
    }

    public void EndFrame()
    {
        foreach (var address in _observations.Where(pair =>
                     !pair.Value.Dead && _frame - pair.Value.LastFrame > 30)
                 .Select(pair => pair.Key).ToArray())
            _observations.Remove(address);
    }

    public void Reset()
    {
        _observations.Clear();
        _frame = 0;
    }
}
