namespace PartyPilot.Bridge;

internal interface IPadOutput
{
    void SetStick(short x, short y);
}

internal sealed class BridgeRouter
{
    private readonly Dictionary<int, PadRoute> _routes;

    public BridgeRouter(IReadOnlyDictionary<int, IPadOutput> outputs)
    {
        _routes = outputs.ToDictionary(
            entry => entry.Key,
            entry => new PadRoute(entry.Value));
    }

    public bool ApplyLine(string line, DateTime nowUtc)
    {
        if (!BridgeProtocol.TryParse(line, out var command) ||
            !_routes.TryGetValue(command.Slot, out var route))
        {
            NeutralAll();
            return false;
        }
        route.Output.SetStick(command.X, command.Y);
        route.Active = command.Active;
        route.LastUpdateUtc = nowUtc;
        return true;
    }

    public void Tick(DateTime nowUtc)
    {
        foreach (var route in _routes.Values)
        {
            if (route.Active && nowUtc - route.LastUpdateUtc > TimeSpan.FromMilliseconds(500))
            {
                route.Output.SetStick(0, 0);
                route.Active = false;
            }
        }
    }

    public void NeutralAll()
    {
        foreach (var route in _routes.Values)
        {
            route.Output.SetStick(0, 0);
            route.Active = false;
        }
    }

    private sealed class PadRoute(IPadOutput output)
    {
        public IPadOutput Output { get; } = output;
        public DateTime LastUpdateUtc { get; set; }
        public bool Active { get; set; }
    }
}
