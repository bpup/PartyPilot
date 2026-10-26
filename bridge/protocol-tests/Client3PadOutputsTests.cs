using PartyPilot.Bridge;

internal static class Client3PadOutputsTests
{
    internal static void Run()
    {
        var endpoints = new Client3PadOutputs();
        var output = new FakeClient3Output();
        if (endpoints.Bind(output, true))
            throw new Exception("Client 3 endpoints bound before Ready.");
        output.IsReady = true;
        if (!endpoints.Bind(output, true) || !endpoints.IsReady ||
            endpoints.Bind(new FakeClient3Output { IsReady = true }, true))
            throw new Exception("Client 3 endpoints accepted an invalid binding.");
        var p5 = new PadReport(0x1000, 0, 0, 505, 0, 0, 0);
        var p6 = new PadReport(0x2000, 0, 0, 606, 0, 0, 0);
        endpoints.P5.SetReport(p5);
        endpoints.P6.SetReport(p6);
        if (output.P5 != p5 || output.P6 != p6)
            throw new Exception("Client 3 endpoints did not share the two-slot report.");
        endpoints.P5.SetReport(default);
        if (output.P5 != default || output.P6 != p6)
            throw new Exception("Neutral P5 cleared P6 unexpectedly.");
        output.AcceptSubmissions = false;
        endpoints.P6.SetReport(default);
        if (endpoints.IsReady || output.NeutralCount != 2)
            throw new Exception("Failed Client 3 submission did not detach and neutralize.");
        var oldSubmitCount = output.SubmitCount;
        endpoints.P5.SetReport(p5);
        if (output.SubmitCount != oldSubmitCount)
            throw new Exception("Detached Client 3 accepted an old endpoint report.");

        var solo = new Client3PadOutputs();
        var soloOutput = new FakeClient3Output { IsReady = true };
        if (!solo.Bind(soloOutput, false))
            throw new Exception("P5-only Client 3 failed to bind.");
        solo.P6.SetReport(default);
        solo.P5.SetReport(p5);
        if (!solo.IsReady || soloOutput.P5 != p5 || soloOutput.P6 != default)
            throw new Exception("P5-only Client 3 was detached by a neutral P6 report.");
        solo.P6.SetReport(p6);
        if (solo.IsReady || soloOutput.P5 != default || soloOutput.P6 != default)
            throw new Exception("P5-only Client 3 accepted non-neutral P6 input.");

        var lost = new Client3PadOutputs();
        var lostOutput = new FakeClient3Output { IsReady = true };
        lost.Bind(lostOutput, true);
        lost.P5.SetReport(p5);
        lostOutput.IsReady = false;
        lost.P6.SetReport(p6);
        if (lost.IsReady || lostOutput.P5 != default || lostOutput.P6 != default)
            throw new Exception("Client 3 loss did not neutralize both slots.");

        var routed = new Client3PadOutputs();
        var routedOutput = new FakeClient3Output { IsReady = true };
        routed.Bind(routedOutput, true);
        var main = new FakePad();
        var router = new BridgeRouter(new Dictionary<int, IPadOutput>
        {
            [1] = main, [5] = routed.P5, [6] = routed.P6
        }, routed);
        if (!router.ConfigurePhysical(1, focusPads: new Dictionary<int, int>
            { [1] = 1, [5] = 5, [6] = 6 }))
            throw new Exception("Composite route rejected explicit P5/P6 focus pads.");
        var now = new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc);
        var before = routedOutput.SubmitCount;
        router.SetPhysical(new XInputGamepad { Buttons = 0x0001, LeftTrigger = 180,
            LeftX = 505 }, now);
        if (routedOutput.SubmitCount != before + 1 || routedOutput.P5.LeftX != 505 ||
            routedOutput.P6 != default || main.Report != default)
            throw new Exception("P5 focus did not publish one combined private submission.");
        router.SetPhysical(default, now.AddMilliseconds(10));
        before = routedOutput.SubmitCount;
        router.SetPhysical(new XInputGamepad { Buttons = 0x0002, LeftTrigger = 180,
            LeftX = 606 }, now.AddMilliseconds(100));
        if (routedOutput.SubmitCount != before + 1 || routedOutput.P5 != default ||
            routedOutput.P6.LeftX != 606 || main.Report != default)
            throw new Exception("P6 focus exposed a mixed old/new private report.");
    }

    private sealed class FakeClient3Output : IClient3PadOutput
    {
        public bool IsReady { get; set; }
        public bool AcceptSubmissions { get; set; } = true;
        public int NeutralCount { get; private set; }
        public int SubmitCount { get; private set; }
        public PadReport P5 { get; private set; }
        public PadReport P6 { get; private set; }

        public bool Submit(PadReport p5, PadReport p6)
        {
            SubmitCount++;
            if (!AcceptSubmissions) return false;
            P5 = p5;
            P6 = p6;
            return true;
        }

        public void NeutralAll()
        {
            NeutralCount++;
            P5 = P6 = default;
        }
    }
}
