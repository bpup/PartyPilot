using PartyPilot.Bridge;

internal static class ProcessLocalPadOutputsTests
{
    internal static void Run()
    {
        var routes = new ProcessLocalPadOutputs(new Dictionary<int, IReadOnlyList<int>>
        {
            [1] = [1, 2], [2] = [3, 4], [3] = [5, 6]
        });
        var processes = Enumerable.Range(1, 3).Select(_ => new FakeOutput { IsReady = true }).ToArray();
        foreach (var client in Enumerable.Range(1, 3))
            if (!routes.Bind(client, processes[client - 1]))
                throw new Exception($"Client {client} did not bind its local input.");
        if (!routes.IsReady || routes.Endpoints.Keys.Order().SequenceEqual(Enumerable.Range(1, 6)) == false)
            throw new Exception("Six party slots were not assigned to three local bridges.");

        var reports = Enumerable.Range(1, 6)
            .ToDictionary(slot => slot, slot => PadReport.FromMovement((short)(slot * 100), 0));
        routes.BeginBatch();
        foreach (var (slot, report) in reports) routes.Endpoints[slot].SetReport(report);
        routes.EndBatch();
        foreach (var client in Enumerable.Range(1, 3))
        {
            var output = processes[client - 1];
            if (output.First != reports[client * 2 - 1] ||
                output.Second != reports[client * 2] || output.Submissions != 1)
                throw new Exception($"Client {client} received another client's party slot.");
        }

        var router = new BridgeRouter(routes.Endpoints, routes);
        var firstTick = DateTime.UtcNow;
        router.Tick(firstTick);
        router.Tick(firstTick.AddMilliseconds(40));
        foreach (var client in Enumerable.Range(1, 3))
        {
            var output = processes[client - 1];
            if (output.Submissions != 3 || output.First != reports[client * 2 - 1] ||
                output.Second != reports[client * 2])
                throw new Exception($"Client {client} held input was not refreshed on router ticks.");
        }

        processes[1].Accept = false;
        routes.BeginBatch();
        routes.Endpoints[3].SetReport(default);
        routes.EndBatch();
        if (routes.IsReady || processes.Any(output => output.First != default || output.Second != default))
            throw new Exception("A failed local bridge did not neutralize all processes.");
        routes.BeginBatch();
        routes.Endpoints[1].SetReport(reports[1]);
        routes.EndBatch();
        if (processes[0].First != default)
            throw new Exception("A detached local bridge accepted a later report.");

        var singles = new ProcessLocalPadOutputs(new Dictionary<int, IReadOnlyList<int>>
        {
            [1] = [1], [2] = [3], [3] = [5]
        });
        var solo = new FakeOutput { IsReady = true };
        if (!singles.Bind(2, solo)) throw new Exception("Single-slot Client 2 did not bind.");
        singles.Endpoints[3].SetReport(reports[3]);
        if (solo.First != reports[3] || solo.Second != default)
            throw new Exception("Single-slot Client 2 was routed outside its own local channel.");
        singles.UnbindAll();
        if (solo.First != default) throw new Exception("Unbind left a local slot active.");

        foreach (var invalid in new Dictionary<int, IReadOnlyList<int>>[]
        {
            new() { [2] = [1] },
            new() { [1] = [1, 1] },
            new() { [1] = [1], [2] = [1] },
            new() { [1] = [2] },
            new() { [1] = [1], [2] = [4, 3] },
            new() { [1] = [0] },
            new() { [1] = [1, 2, 3] }
        })
        {
            try { _ = new ProcessLocalPadOutputs(invalid); }
            catch (ArgumentException) { continue; }
            throw new Exception("Invalid process-local party layout was accepted.");
        }
    }

    private sealed class FakeOutput : IClient3PadOutput
    {
        public bool IsReady { get; set; }
        public bool Accept { get; set; } = true;
        public PadReport First { get; private set; }
        public PadReport Second { get; private set; }
        public int Submissions { get; private set; }

        public bool Submit(PadReport first, PadReport second)
        {
            Submissions++;
            if (!Accept) return false;
            First = first;
            Second = second;
            return true;
        }

        public void NeutralAll() => (First, Second) = (default, default);
    }
}
