namespace PartyPilot.Bridge;

// Each game process owns local channels 5/6. Party slot numbers only select
// which process-local endpoint receives a report; they are never XInput indices.
internal sealed class ProcessLocalPadOutputs : IReportBatch
{
    private sealed record ClientPads(int FirstSlot, int? SecondSlot, Client3PadOutputs Pads);

    private readonly Dictionary<int, ClientPads> _clients = new();
    private readonly Dictionary<int, IPadOutput> _endpoints = new();

    internal ProcessLocalPadOutputs(IReadOnlyDictionary<int, IReadOnlyList<int>> slotsByClient)
    {
        if (slotsByClient.Count is < 1 or > 3 ||
            !slotsByClient.Keys.Order().SequenceEqual(Enumerable.Range(1, slotsByClient.Count)))
            throw new ArgumentException("Process-local outputs need 1–3 consecutive client numbers.",
                nameof(slotsByClient));

        foreach (var (client, slots) in slotsByClient.OrderBy(item => item.Key))
        {
            var firstSlot = client * 2 - 1;
            if (slots is null || slots.Count is < 1 or > 2 ||
                slots[0] != firstSlot ||
                slots.Count == 2 && slots[1] != firstSlot + 1)
                throw new ArgumentException("Each client needs its own first slot and optional second slot.",
                    nameof(slotsByClient));
            var pads = new Client3PadOutputs();
            _clients.Add(client, new ClientPads(slots[0], slots.Count == 2 ? slots[1] : null, pads));
            _endpoints.Add(slots[0], pads.P5);
            if (slots.Count == 2) _endpoints.Add(slots[1], pads.P6);
        }
    }

    internal IReadOnlyDictionary<int, IPadOutput> Endpoints => _endpoints;

    internal bool IsReady => _clients.Values.All(client => client.Pads.IsReady);

    internal bool Bind(int client, IClient3PadOutput output)
    {
        if (!_clients.TryGetValue(client, out var pads)) return false;
        return pads.Pads.Bind(output, pads.SecondSlot is not null);
    }

    internal void UnbindAll()
    {
        foreach (var client in _clients.Values) client.Pads.Unbind();
    }

    public void BeginBatch()
    {
        foreach (var client in _clients.Values) client.Pads.BeginBatch();
    }

    public void EndBatch()
    {
        try
        {
            foreach (var client in _clients.Values) client.Pads.EndBatch();
            if (!IsReady) UnbindAll();
        }
        catch
        {
            UnbindAll();
            throw;
        }
    }

    public void AbortBatch() => UnbindAll();
}
