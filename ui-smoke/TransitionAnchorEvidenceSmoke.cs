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
            Positions =
            [
                new FollowPositionWitness(3, 202, 42, now.AddMilliseconds(-200), 200, 200),
                new FollowPositionWitness(4, 202, 42, now.AddMilliseconds(-200), 190, 200)
            ],
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
        var firstProgress = now.AddSeconds(1);
        moving.Observe(2, Frame(202, firstProgress, 42, 200, 200,
            entrance with { Id = 20, X = 400, Y = 201 }));
        var stationary = live with
        {
            Positions = live.Positions.Select(position => position with
                { SeenUtc = firstProgress }).ToArray()
        };
        if (TransitionApproachPlan.Apply(stationary, topology, moving,
                true, false, firstProgress).PadCommands[0].Status != "approaching_transition")
            throw new Exception("Fresh entrance approach stopped before the progress window.");
        var stallTime = firstProgress.AddMilliseconds(3100);
        moving.Observe(2, Frame(202, stallTime, 42, 200, 200,
            entrance with { Id = 20, X = 400, Y = 201 }));
        var stalled = TransitionApproachPlan.Apply(stationary with
        {
            Positions = stationary.Positions.Select(position => position with
                { SeenUtc = stallTime }).ToArray()
        }, topology, moving, true, false, stallTime);
        if (stalled.PadCommands.Any(command => command.Status != "approach_blocked" ||
                !command.Command.Contains("\"waiting\"", StringComparison.Ordinal)))
            throw new Exception("Stalled entrance approach kept moving a local character.");
        moving.ResetMovement();
        var progressing = new TransitionAnchorEvidence();
        var target = new TransitionPoint(400, 201);
        if (!progressing.AllowMovement(matched!, 3,
                new TransitionPoint(200, 200), target, now) ||
            !progressing.AllowMovement(matched!, 3,
                new TransitionPoint(300, 200), target, now.AddSeconds(2)) ||
            !progressing.AllowMovement(matched!, 3,
                new TransitionPoint(300, 200), target, now.AddSeconds(4)) ||
            progressing.AllowMovement(matched!, 3,
                new TransitionPoint(300, 200), target, now.AddMilliseconds(5100)))
            throw new Exception("Entrance progress did not reset the stall timer.");
        var movingSecond = new TransitionAnchorEvidence();
        movingSecond.Observe(1, Frame(101, now.AddMilliseconds(-400), 41, 100, 100, entrance));
        movingSecond.Observe(2, Frame(202, now.AddMilliseconds(-300), 42, 200, 200,
            entrance with { Id = 20, X = 400, Y = 201 }));
        movingSecond.Observe(1, Frame(101, now.AddMilliseconds(-100), 43, 300, 300));
        var secondLeads = TransitionApproachPlan.Apply(live with
        {
            Positions =
            [
                live.Positions[0],
                live.Positions[1] with { X = 300 }
            ]
        }, topology, movingSecond, true, false, now);
        if (secondLeads.PadCommands[1].Status != "approaching_transition" ||
            secondLeads.PadCommands[0].Status != "approaching_transition" ||
            movingSecond.SelectDriver(movingSecond.Match(1, 2, now)!, 3, 1, 4, 100) != 4)
            throw new Exception("Nearer P4 did not lead while P3 followed the driver.");
        var breadcrumb = new TransitionAnchorEvidence();
        var routeAnchor = entrance with { X = 110, Y = 100 };
        breadcrumb.Observe(1, Frame(101, now.AddMilliseconds(-800), 41,
            0, 0, routeAnchor));
        breadcrumb.Observe(1, Frame(101, now.AddMilliseconds(-700), 41,
            0, 100, routeAnchor));
        breadcrumb.Observe(1, Frame(101, now.AddMilliseconds(-600), 41,
            105, 100, routeAnchor));
        breadcrumb.Observe(2, Frame(202, now.AddMilliseconds(-300), 42,
            290, 0, routeAnchor with { X = 400, Y = 100 }));
        breadcrumb.Observe(1, Frame(101, now.AddMilliseconds(-100), 43,
            300, 300));
        var route = breadcrumb.Match(1, 2, now);
        if (route?.Approach is not { Count: >= 3 } ||
            breadcrumb.ReplayTarget(route, 3, 290, 0) is not { } next ||
            next.X >= 350 || next.Y < 90)
            throw new Exception("Leader approach did not retain its turn near the entrance.");
        var breadcrumbFrame = live with
        {
            Positions =
            [
                live.Positions[0] with { X = 290, Y = 0 },
                live.Positions[1] with { X = 280, Y = 0 }
            ]
        };
        var replayed = TransitionApproachPlan.Apply(breadcrumbFrame, topology,
            breadcrumb, true, false, now);
        using (var replayCommand = System.Text.Json.JsonDocument.Parse(
            replayed.PadCommands[0].Command))
        {
            if (replayed.PadCommands[0].Status != "approaching_transition" ||
                replayCommand.RootElement.GetProperty("x").GetDouble() >= 0)
                throw new Exception("Destination driver ignored the saved entrance turn.");
        }
        var discontinuous = new TransitionAnchorEvidence();
        discontinuous.Observe(1, Frame(101, now.AddMilliseconds(-800), 41,
            -500, 0, routeAnchor));
        discontinuous.Observe(1, Frame(101, now.AddMilliseconds(-400), 41,
            105, 100, routeAnchor));
        discontinuous.Observe(2, Frame(202, now.AddMilliseconds(-300), 42,
            290, 0, routeAnchor with { X = 400, Y = 100 }));
        discontinuous.Observe(1, Frame(101, now.AddMilliseconds(-100), 43,
            300, 300));
        if (discontinuous.Match(1, 2, now)?.Approach is { Count: > 0 })
            throw new Exception("A position jump was reused as a walkable breadcrumb.");
        if (evidence.Match(handoff, topology, now.AddSeconds(13)) is not null)
            throw new Exception("Expired transition remained eligible.");
        var duplicateDestination = new TransitionAnchorEvidence();
        duplicateDestination.Observe(1, Frame(101, now.AddMilliseconds(-400), 41,
            100, 100, entrance));
        duplicateDestination.Observe(2, Frame(202, now.AddMilliseconds(-300), 42,
            200, 200, entrance with { Id = 20, X = 205, Y = 201 },
            entrance with { Id = 21, X = 450, Y = 201 }));
        duplicateDestination.Observe(1, Frame(101, now.AddMilliseconds(-100), 43,
            300, 300));
        if (duplicateDestination.Match(handoff, topology, now) is not null)
            throw new Exception("A repeated destination entrance was chosen only by proximity.");
        var duplicateSource = new TransitionAnchorEvidence();
        duplicateSource.Observe(1, Frame(101, now.AddMilliseconds(-400), 41,
            100, 100, entrance, entrance with { Id = 11, X = 115, Y = 105 }));
        duplicateSource.Observe(2, Frame(202, now.AddMilliseconds(-300), 42,
            200, 200, entrance with { Id = 20, X = 205, Y = 201 }));
        duplicateSource.Observe(1, Frame(101, now.AddMilliseconds(-100), 43,
            300, 300));
        if (duplicateSource.Match(handoff, topology, now) is not null)
            throw new Exception("A repeated source entrance was treated as an identified transition.");
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
        var dualEvidence = new TransitionAnchorEvidence();
        dualEvidence.Observe(1, Frame(101, now.AddMilliseconds(-400), 41,
            100, 100, entrance));
        dualEvidence.Observe(2, Frame(202, now.AddMilliseconds(-300), 42,
            200, 200, entrance with { Id = 20, X = 205, Y = 201 }));
        dualEvidence.Observe(1, Frame(101, now.AddMilliseconds(-100), 43,
            300, 300));
        if (TransitionApproachPlan.BuildInteraction(live with { Positions = [] },
                topology, dualEvidence, true, false, now) is not null)
            throw new Exception("Dual-character destination was allowed to interact without partner position.");
        var positions = new FollowPositionWitness[]
        {
            new(3, 202, 42, now.AddMilliseconds(-200), 200, 200),
            new(4, 202, 42, now.AddMilliseconds(-200), 205, 202)
        };
        var nearCandidate = TransitionApproachPlan.BuildInteraction(live with
            { Positions = positions }, topology, dualEvidence, true, false, now);
        var wrongAreaCandidate = TransitionApproachPlan.BuildInteraction(live with
            {
                Positions = [positions[0], positions[1] with { AreaHash = 99 }]
            }, topology, dualEvidence, true, false, now);
        var farCandidate = TransitionApproachPlan.BuildInteraction(live with
            {
                Positions = [positions[0], positions[1] with { X = 1000 }]
            }, topology, dualEvidence, true, false, now);
        if (nearCandidate?.DriverPlayer != 4 || wrongAreaCandidate is not null ||
            farCandidate is not null)
            throw new Exception($"Dual-character entry guard failed: near={nearCandidate}, " +
                $"wrongArea={wrongAreaCandidate}, far={farCandidate}.");
        var spreadEvidence = new TransitionAnchorEvidence();
        spreadEvidence.Observe(1, Frame(101, now.AddMilliseconds(-400), 41,
            100, 100, entrance));
        spreadEvidence.Observe(2, Frame(202, now.AddMilliseconds(-300), 42,
            500, 200, entrance with { Id = 20, X = 205, Y = 201 }));
        spreadEvidence.Observe(1, Frame(101, now.AddMilliseconds(-100), 43,
            300, 300));
        var spread = live with
        {
            Positions =
            [
                positions[0] with { X = 500 },
                positions[1]
            ]
        };
        var spreadRoute = TransitionApproachPlan.Apply(spread, topology,
            spreadEvidence, true, false, now);
        if (spreadRoute.PadCommands[0].Status != "approaching_transition" ||
            spreadRoute.PadCommands[1].Status != "waiting" ||
            TransitionApproachPlan.BuildInteraction(spreadRoute, topology,
                spreadEvidence, true, false, now)?.DriverPlayer != 4)
            throw new Exception("P4 entry did not wait for a valid following P3 within partner range.");
        var positionsJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            positions = positions.Select(item => new
            {
                slot = item.Player, process_id = item.ProcessId,
                area_hash = item.AreaHash, seen_utc = item.SeenUtc,
                x = item.X, y = item.Y
            })
        });
        static bool RejectsPositions(string json, PartyTopology topology)
        {
            try { LiveFollowFrame.ParsePositions(json, topology); return false; }
            catch (InvalidDataException) { return true; }
        }
        if (LiveFollowFrame.ParsePositions(positionsJson, topology).Count != 2 ||
            !RejectsPositions(positionsJson.Replace("\"slot\":4", "\"slot\":3"), topology))
            throw new Exception("Dual-character position witness parsing failed.");
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
        var budget = new TransitionInteractionBudget();
        if (budget.TryReserve(candidate, now) != 1 ||
            budget.TryReserve(candidate, now.AddSeconds(3)) != 0 ||
            budget.TryReserve(candidate, now.AddSeconds(4)) != 2 ||
            budget.TryReserve(candidate, now.AddSeconds(8)) != 3 ||
            budget.TryReserve(candidate, now.AddSeconds(12)) != 0 ||
            budget.TryReserve(candidate with { DriverPlayer = 4, DriverPad = 4 },
                now.AddSeconds(12)) != 0 ||
            budget.TryReserve(candidate with { SourceChangedUtc = now.AddSeconds(13) },
                now.AddSeconds(13)) != 1)
            throw new Exception("Entrance interaction exceeded three attempts or retried before area confirmation.");
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
