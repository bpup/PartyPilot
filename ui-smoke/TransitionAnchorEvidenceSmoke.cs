using PartyPilot.Desktop;

internal static class TransitionAnchorEvidenceSmoke
{
    internal static async Task RunAsync()
    {
        var now = DateTime.UtcNow;
        var topology = PartyTopologies.DefaultFor(4);
        var handoff = new FollowHandoffRequest(2, 3, 1);
        var entrance = new RadarMarker(10, "transition",
            "Metadata/Terrain/Transition/Door", "The Door", null, null, null, 110, 105,
            NavigationIsValid: true, NavigationIsTargetable: true);
        RadarFrame Frame(int pid, DateTime seen, uint area, double x, double y,
            params RadarMarker[] markers) => new(pid, seen, area, "0xABCDEF", x, y, markers);
        var evidence = new TransitionAnchorEvidence();
        evidence.Observe(1, Frame(101, now.AddMilliseconds(-400), 41, 100, 100, entrance));
        evidence.Observe(2, Frame(202, now.AddMilliseconds(-300), 42, 200, 200,
            entrance with { Id = 20, X = 205, Y = 201 }));
        evidence.Observe(1, Frame(101, now.AddMilliseconds(-100), 43, 300, 300));
        var matched = evidence.Match(handoff, topology, now);
        if (matched is null || matched.SourceClient != 1 || matched.DestinationClient != 2 ||
            matched.DestinationX != 205 || matched.DestinationY != 201)
            throw new Exception("Fresh matching transition was not recognized across clients.");
        var approach = TransitionApproachPlan.Build(matched with { DestinationX = 400 },
            topology, now);
        if (approach is null || approach.DriverPlayer != 3 || approach.DriverPad != 3 ||
            approach.DistanceGrid <= 7 ||
            TransitionApproachPlan.Build(matched, topology, now) is not null ||
            TransitionApproachPlan.Build(matched with { DestinationX = 400 },
                topology, now.AddSeconds(1)) is not null)
            throw new Exception("Transition approach did not stop for close or stale position.");
        using (var command = System.Text.Json.JsonDocument.Parse(approach.Command))
        {
            var input = command.RootElement;
            if (input.GetProperty("slot").GetInt32() != 3 ||
                input.GetProperty("state").GetString() != "following" ||
                input.GetProperty("x").GetDouble() <= 0 ||
                input.GetProperty("y").GetDouble() <= 0)
                throw new Exception("Transition approach routed movement to the wrong pad or direction.");
        }
        var moving = new TransitionAnchorEvidence();
        moving.Observe(1, Frame(101, now.AddMilliseconds(-400), 41, 100, 100, entrance));
        moving.Observe(2, Frame(202, now.AddMilliseconds(-300), 42, 200, 200,
            entrance with { Id = 20, X = 400, Y = 201 }));
        moving.Observe(1, Frame(101, now.AddMilliseconds(-100), 43, 300, 300));
        var waiting = "{\"slot\":3,\"state\":\"waiting\"}";
        var live = new FollowBridgeFrame(waiting, "waiting", null)
        {
            FocusedPlayer = 1,
            PadCommands =
            [
                new FollowPadCommand(3, 3, waiting, "waiting", "different or unknown instance"),
                new FollowPadCommand(4, 4, "{\"slot\":4,\"state\":\"waiting\"}",
                    "waiting", "different or unknown instance")
            ]
        };
        var routed = TransitionApproachPlan.Apply(live, topology, moving, true, false, now);
        if (routed.PadCommands[0].Status != "approaching_transition" ||
            routed.PadCommands[1].Status != "waiting" ||
            TransitionApproachPlan.Apply(live, topology, moving, false, false, now) != live ||
            TransitionApproachPlan.Apply(live, topology, moving, true, true, now) != live ||
            TransitionApproachPlan.Apply(live with
            {
                Handoffs = [handoff]
            }, topology, moving, true, false, now).PadCommands[0].Status != "waiting" ||
            TransitionApproachPlan.Apply(live with
            {
                FocusedPlayer = 2
            }, topology, moving, true, false, now).PadCommands[0].Status != "waiting")
            throw new Exception("Transition routing escaped its opt-in, area or local-role guard.");
        if (evidence.Match(handoff, topology, now.AddSeconds(13)) is not null)
            throw new Exception("Expired transition remained eligible.");
        evidence.Observe(2, Frame(202, now, 42, 200, 200,
            entrance with { Id = 21, Label = "Another Door", X = 205, Y = 201 }));
        if (evidence.Match(handoff, topology, now) is not null)
            throw new Exception("Different transition identity was accepted.");

        var far = new TransitionAnchorEvidence();
        far.Observe(1, Frame(101, now.AddMilliseconds(-400), 41, 1000, 1000, entrance));
        far.Observe(1, Frame(101, now.AddMilliseconds(-100), 43, 300, 300));
        far.Observe(2, Frame(202, now, 42, 200, 200, entrance));
        if (far.Match(handoff, topology, now) is not null)
            throw new Exception("Remote transition was accepted as the leader's used entrance.");
        var inactive = new TransitionAnchorEvidence();
        inactive.Observe(1, Frame(101, now.AddMilliseconds(-400), 41, 100, 100,
            entrance with { NavigationIsTargetable = false }));
        inactive.Observe(1, Frame(101, now.AddMilliseconds(-100), 43, 300, 300));
        inactive.Observe(2, Frame(202, now, 42, 200, 200, entrance));
        if (inactive.Match(handoff, topology, now) is not null)
            throw new Exception("Non-targetable entrance was accepted as used.");
        if (TransitionApproachPlan.BuildInteraction(live, topology, evidence, true,
                false, now) is not null)
            throw new Exception("Dual-character destination was allowed to interact without partner position.");
        var singles = PartyTopologies.ForPlayerCount(3).Single(item =>
            item.Id == "3-three-singles");
        var singleEvidence = new TransitionAnchorEvidence();
        singleEvidence.Observe(1, Frame(101, now.AddMilliseconds(-400), 41,
            100, 100, entrance));
        singleEvidence.Observe(2, Frame(202, now.AddMilliseconds(-300), 42,
            200, 200, entrance with { Id = 20, X = 205, Y = 201 }));
        singleEvidence.Observe(1, Frame(101, now.AddMilliseconds(-100), 43,
            300, 300));
        var singleWaiting = new FollowBridgeFrame("{\"slot\":3,\"state\":\"waiting\"}",
            "waiting", null)
        {
            FocusedPlayer = 1,
            PadCommands = [new FollowPadCommand(2, 3,
                "{\"slot\":3,\"state\":\"waiting\"}", "waiting",
                "different or unknown instance")]
        };
        var candidate = TransitionApproachPlan.BuildInteraction(singleWaiting,
            singles, singleEvidence, true, false, now);
        if (candidate is null || candidate.DriverPlayer != 2 || candidate.DriverPad != 3 ||
            TransitionApproachPlan.BuildInteraction(singleWaiting, singles, singleEvidence,
                false, false, now) is not null ||
            TransitionApproachPlan.BuildInteraction(singleWaiting with
            {
                Handoffs = [new FollowHandoffRequest(2, 2, 1)]
            }, singles, singleEvidence, true, false, now) is not null)
            throw new Exception("Single-character entry interaction eligibility was incorrect.");
        var port = new FakeInteractionPort();
        await TransitionInteractionRunner.RunAsync(port, candidate.DriverPad,
            CancellationToken.None, (_, _) => Task.CompletedTask);
        if (!port.Calls.SequenceEqual(["begin", "pulse:3", "end"]))
            throw new Exception("Entry A pulse did not hold and release the bridge lease.");
        var failing = new FakeInteractionPort { FailPulse = true };
        try
        {
            await TransitionInteractionRunner.RunAsync(failing, candidate.DriverPad,
                CancellationToken.None, (_, _) => Task.CompletedTask);
            throw new Exception("Failed entry pulse was accepted.");
        }
        catch (InvalidOperationException) { }
        if (!failing.Calls.SequenceEqual(["begin", "pulse:3", "end"]))
            throw new Exception("Failed entry pulse retained the bridge lease.");
        Console.WriteLine("Read-only transition evidence rejected stale, distant and mismatched entrances.");
    }

    private sealed class FakeInteractionPort : ITransitionInteractionPort
    {
        internal readonly List<string> Calls = [];
        internal bool FailPulse;
        public Task BeginAsync(CancellationToken token)
        {
            Calls.Add("begin");
            return Task.CompletedTask;
        }
        public Task PulseAAsync(int pad, CancellationToken token)
        {
            Calls.Add($"pulse:{pad}");
            if (FailPulse) throw new InvalidOperationException("pulse failed");
            return Task.CompletedTask;
        }
        public Task EndAsync(CancellationToken token)
        {
            Calls.Add("end");
            return Task.CompletedTask;
        }
    }
}
