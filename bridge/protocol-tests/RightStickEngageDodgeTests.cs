using PartyPilot.Bridge;

internal static class RightStickEngageDodgeTests
{
    internal static void Run()
    {
        if (!RightStickEngageDodge.TryNormalizeBinding("rb", out var binding) || binding != "RB" ||
            RightStickEngageDodge.TryNormalizeBinding("Start", out _))
            throw new Exception("Right-stick dodge accepted a button outside the target's options.");
        var control = new RightStickEngageDodge();
        var steering = PadReport.FromMovement(111, 222) with { Buttons = 0x2100 };
        PadReport At(long ms, short rightX, short rightY = 0, bool eligible = true,
            int player = 2, int main = 1, string button = "B") =>
            control.Apply(player, main, true, eligible, button, rightX, rightY, steering, ms);
        At(0, 0);
        if ((At(10, 24000, -12000).Buttons & 0x2000) != 0 ||
            (At(50, 24000, -12000).Buttons & 0x2100) != 0x2100 ||
            At(50, 24000, -12000).LeftX != 24000 ||
            (At(150, 24000, -12000).Buttons & 0x2000) != 0 ||
            (At(200, -24000).Buttons & 0x2000) != 0)
            throw new Exception("Right-stick engagement pulse or held-stick latch differs from target timing.");
        At(210, 0);
        At(220, 24000);
        if ((At(260, 24000).Buttons & 0x2000) == 0)
            throw new Exception("A release and re-engagement did not pulse again.");
        control.Reset();
        At(0, 0);
        At(10, 24000);
        At(20, 0);
        if ((At(50, 0).Buttons & 0x2000) == 0 || At(50, 0).LeftX != 24000 ||
            At(190, 0) != steering)
            throw new Exception("A short right-stick flick lost its bounded dodge pulse.");
        control.Reset();
        At(0, 0);
        At(10, 24000);
        if (At(50, 24000, eligible: false) != steering ||
            (At(60, 24000).Buttons & 0x2000) != 0)
            throw new Exception("Ineligible follower replayed a cancelled dodge.");
        At(70, 0);
        At(80, 24000);
        if (At(500, 24000) != steering || (At(550, 24000).Buttons & 0x2000) != 0)
            throw new Exception("Stale physical samples replayed an engagement.");
        control.Reset();
        At(0, 0, button: "RT");
        At(10, 24000, button: "RT");
        if (At(50, 24000, button: "RT").RightTrigger != byte.MaxValue ||
            At(150, 24000, button: "RT").RightTrigger != 0)
            throw new Exception("Selected trigger binding did not pulse and release.");
        control.Reset();
        At(0, 0);
        At(10, 24000);
        if (At(50, 24000, player: 1, main: 1) != steering ||
            At(50, 24000, player: 2, main: 2) != steering)
            throw new Exception("MAIN received a synthetic follower dodge.");
        foreach (var player in Enumerable.Range(1, 6))
        {
            control.Reset();
            At(0, 0, player: player, main: 3);
            At(10, 24000, player: player, main: 3);
            var output = At(50, 24000, player: player, main: 3);
            if (player == 3 ? output != steering :
                (output.Buttons & 0x2000) == 0 || output.LeftX != 24000)
                throw new Exception("An active P1–P6 follower missed or leaked the dodge pulse.");
        }
        control.Reset();
        At(0, 0);
        At(10, 24000);
        if (At(50, 24000, player: 2, main: 3) != steering ||
            (At(90, 24000, player: 2, main: 3).Buttons & 0x2000) != 0)
            throw new Exception("Changing MAIN replayed a held right-stick gesture.");
        control.Reset();
        At(0, 0, player: 2);
        At(0, 0, player: 4);
        At(10, 24000, player: 2);
        At(10, 24000, player: 4);
        control.ResetPlayer(2);
        if (At(50, 24000, player: 2) != steering ||
            At(50, 24000, player: 4).LeftX != 24000)
            throw new Exception("One follower's combat reset cancelled another follower's dodge.");

        var outputs = new Dictionary<int, IPadOutput> { [1] = new Capture(), [3] = new Capture() };
        var router = new BridgeRouter(outputs);
        if (router.ConfigurePhysical(1, rightStickFollowerOverride: false,
                focusPads: new Dictionary<int, int> { [1] = 1, [2] = 3 },
                engageDodgeEnabled: true) ||
            !router.ConfigurePhysical(1, rightStickFollowerOverride: true,
                focusPads: new Dictionary<int, int> { [1] = 1, [2] = 3 },
                engageDodgeEnabled: true))
            throw new Exception("Right-stick dodge configuration escaped follower override gating.");
        var epoch = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        router.SetPhysical(default, epoch);
        if (!router.ApplyLine("{\"slot\":3,\"state\":\"following\",\"x\":1,\"y\":0,\"manual_override_eligible\":true}", epoch))
            throw new Exception("Follower eligibility did not reach the bridge router.");
        router.SetPhysical(new XInputGamepad { RightX = 24000 }, epoch.AddMilliseconds(10));
        router.Tick(epoch.AddMilliseconds(50));
        if ((((Capture)outputs[3]).Report.Buttons & 0x2000) == 0 ||
            (((Capture)outputs[1]).Report.Buttons & 0x2000) != 0)
            throw new Exception("Bridge did not route the synthetic dodge only to its eligible follower.");
        if (!router.BeginCombat(3, epoch.AddMilliseconds(60)) ||
            !router.PulseCombat(3, new PadReport(0x1000, 0, 0, 0, 0, 0, 0), 80,
                epoch.AddMilliseconds(70)))
            throw new Exception("Combat pulse could not take ownership of the follower input.");
        router.Tick(epoch.AddMilliseconds(151));
        if ((((Capture)outputs[3]).Report.Buttons & 0x2000) != 0)
            throw new Exception("Old right-stick engagement replayed after a combat pulse.");
        router.EndCombat(3);
        router.SetPhysical(new XInputGamepad { RightX = 0 }, epoch.AddMilliseconds(160));
        router.SetPhysical(new XInputGamepad { RightX = 24000 }, epoch.AddMilliseconds(170));
        router.Tick(epoch.AddMilliseconds(210));
        if ((((Capture)outputs[3]).Report.Buttons & 0x2000) == 0)
            throw new Exception("Fresh engagement after combat did not remain available.");
        router.SetMenuGuardActive(true);
        if ((((Capture)outputs[3]).Report.Buttons & 0x2000) != 0)
            throw new Exception("Menu protection did not cancel a synthetic dodge.");
    }

    private sealed class Capture : IPadOutput
    {
        internal PadReport Report { get; private set; }
        public void SetReport(PadReport report) => Report = report;
    }
}
