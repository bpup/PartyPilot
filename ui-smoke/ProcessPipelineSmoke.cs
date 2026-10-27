using System.Text.Json;
using PartyPilot.Desktop;

internal static class ProcessPipelineSmoke
{
    public static async Task RunAsync()
    {
        var paired = PartyTopologies.ForPlayerCount(4)
            .Single(item => item.Id == "4-balanced");
        static FollowPadCommand Waiting(int player) => new(player, player, "{}", "waiting", null);
        var outward = new FollowBridgeFrame("{}", "waiting", null)
        {
            Handoffs = [new FollowHandoffRequest(2, 3, 2)],
            PadCommands = [Waiting(3), Waiting(4)]
        };
        var returnTrip = new FollowBridgeFrame("{}", "waiting", null)
        {
            Handoffs = [new FollowHandoffRequest(1, 1, 4)],
            PadCommands = [Waiting(1), Waiting(2)]
        };
        if (PartyWarpRoutes.FromFrame(outward, paired)?.RemotePortraitIndex != 1 ||
            PartyWarpRoutes.FromFrame(outward with
                { Handoffs = [new FollowHandoffRequest(2, 3, 1)] }, paired)?.RemotePortraitIndex != 0 ||
            PartyWarpRoutes.FromFrame(returnTrip, paired)?.RemotePortraitIndex != 1 ||
            PartyWarpRoutes.FromFrame(returnTrip with
                { Handoffs = [new FollowHandoffRequest(1, 1, 3)] }, paired)?.RemotePortraitIndex != 0 ||
            PartyWarpRoutes.FromFrame(returnTrip with { PadCommands = [Waiting(1)] }, paired) is not null ||
            PartyWarpRoutes.FromFrame(returnTrip with
                { PadCommands = [Waiting(1), Waiting(2) with { Status = "following" }] }, paired) is not null ||
            PartyWarpRoutes.FromFrame(outward with
                { Handoffs = [new FollowHandoffRequest(2, 3, 2),
                    new FollowHandoffRequest(1, 1, 4)] }, paired) is not null)
            throw new Exception("Paired warp routes accepted an incomplete or ambiguous destination.");
        const int visionWidth = 1280;
        const int visionHeight = 720;
        var portraitPixels = new byte[visionWidth * visionHeight * 4];
        for (var y = 150; y < 195; y++)
        for (var x = 4; x < 80; x++)
        for (var channel = 0; channel < 3; channel++)
            portraitPixels[(y * visionWidth + x) * 4 + channel] = (byte)(x % 3 == 0 ? 220 : 20);
        for (var y = 270; y < 315; y++)
        for (var x = 4; x < 80; x++)
        for (var channel = 0; channel < 3; channel++)
            portraitPixels[(y * visionWidth + x) * 4 + channel] = (byte)(x % 3 == 0 ? 220 : 20);
        var portraits = PartyWarpVision.FindTwoPortraits(portraitPixels, visionWidth, visionHeight);
        if (portraits is null || portraits.FirstY is < 150 or > 195 ||
            portraits.SecondY is < 270 or > 315 ||
            PartyWarpVision.FindTwoPortraits(new byte[portraitPixels.Length],
                visionWidth, visionHeight) is not null)
            throw new Exception($"Warp portrait detection did not require two live textured rows: {portraits}.");
        var menuBefore = new byte[portraitPixels.Length];
        var menuAfter = new byte[portraitPixels.Length];
        for (var y = 50; y < 450; y++)
        for (var x = 80; x < 450; x++)
        {
            var offset = (y * visionWidth + x) * 4;
            menuAfter[offset] = 80;
            menuAfter[offset + 1] = 80;
            menuAfter[offset + 2] = 80;
        }
        foreach (var y in new[] { 185, 240 })
        for (var x = 84; x < 373; x++)
        {
            var offset = (y * visionWidth + x) * 4;
            menuAfter[offset] = 65;
            menuAfter[offset + 1] = 115;
            menuAfter[offset + 2] = 140;
        }
        var menuAction = PartyWarpVision.FindFirstAction(menuBefore, menuAfter,
            visionWidth, visionHeight);
        if (menuAction is null || menuAction.Y is < 190 or > 235 ||
            PartyWarpVision.FindFirstAction(menuBefore, menuBefore,
                visionWidth, visionHeight) is not null)
            throw new Exception("Warp action detection did not require a changed menu and live action borders.");
        var warpBefore = new LoginWindowFrame(portraitPixels, visionWidth, visionHeight, 100, 200);
        var warpMenu = new byte[portraitPixels.Length];
        for (var index = 0; index < warpMenu.Length; index++)
            warpMenu[index] = menuAfter[index] != 0 ? menuAfter[index] : portraitPixels[index];
        var warpPort = new FakeWarpPort(warpBefore,
            new LoginWindowFrame(warpMenu, visionWidth, visionHeight, 100, 200), warpBefore);
        var warpClick = await new PartyWarpClickService(warpPort, TimeSpan.Zero)
            .TryClickAsync(0, CancellationToken.None);
        if (!warpClick.ActionClicked || !warpClick.MenuDismissed ||
            warpPort.RightClicks.Count != 1 || warpPort.LeftClicks.Count != 1 ||
            warpPort.RestoredCursor != (4, 5))
            throw new Exception("Bound warp click did not require current portraits and menu dismissal.");
        var noMenu = new FakeWarpPort(warpBefore, warpBefore);
        var noMenuResult = await new PartyWarpClickService(noMenu, TimeSpan.Zero)
            .TryClickAsync(1, CancellationToken.None);
        if (noMenuResult.ActionClicked || noMenu.LeftClicks.Count != 0)
            throw new Exception("Warp clicked without a visually confirmed current menu.");
        var triple = PartyTopologies.ForPlayerCount(3)
            .Single(item => item.Id == "3-three-singles");
        var tripleFrame = LiveFollowFrame.ParseDynamic(
            "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"following\",\"x\":1,\"y\":0},\"3\":{\"state\":\"waiting\"}}}",
            triple);
        var plainPriorityFixture = LiveFollowFrame.Parse(
            "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"catching_up\",\"x\":1,\"y\":0}}}",
            2, 3);
        var priorityFrame = LiveFollowFrame.ApplyCatchUpPriority(plainPriorityFixture, enabled: true);
        if (!priorityFrame.Command.Contains("\"catch_up_priority\":true", StringComparison.Ordinal) ||
            !priorityFrame.PadCommands[0].Command.Contains("\"catch_up_priority\":true", StringComparison.Ordinal) ||
            LiveFollowFrame.ApplyCatchUpPriority(plainPriorityFixture, enabled: false) != plainPriorityFixture)
            throw new Exception("Desktop did not pass through the observed catch-up priority option.");
        var attackFrame = LiveFollowFrame.Parse(
            "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"holding\",\"x\":0,\"y\":1,\"attack_direction\":true}}}",
            2, 3);
        if (!attackFrame.Command.Contains("\"attack_direction\":true", StringComparison.Ordinal) ||
            !attackFrame.Command.Contains("\"y\":1", StringComparison.Ordinal))
            throw new Exception("Desktop dropped attack direction during a trail hold.");
        var switchedTriple = LiveFollowFrame.ParseDynamic(
            "{\"focused\":3,\"commands\":{\"1\":{\"state\":\"waiting\"},\"2\":{\"state\":\"following\",\"x\":0,\"y\":1}}}",
            triple);
        if (!tripleFrame.PadCommands.Select(command => command.Pad).SequenceEqual([3, 5]) ||
            !switchedTriple.PadCommands.Select(command => command.Pad).SequenceEqual([1, 3]))
            throw new Exception("Three-client live follow did not map independent roles to P1/P3/P5.");
        var tripleFlasks = LiveFollowFrame.ParseDynamic(
            "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\"},\"3\":{\"state\":\"waiting\"}}," +
            "\"flasks\":[{\"player\":1,\"resource\":\"life\",\"percent\":20,\"reason\":\"threshold\"}," +
            "{\"player\":2,\"resource\":\"mana\",\"percent\":20,\"reason\":\"threshold\"}," +
            "{\"player\":3,\"resource\":\"life\",\"percent\":20,\"reason\":\"threshold\"}]}", triple);
        if (tripleFlasks.Flasks.Count != 3 ||
            tripleFlasks.Flasks[2] != new FlaskBridgeRequest(3, "life"))
            throw new Exception("Three-client live follow dropped P5's flask request.");
        try
        {
            LiveFollowFrame.ParseDynamic(
                "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\"},\"4\":{\"state\":\"waiting\"}}}", triple);
            throw new Exception("Three-client live follow accepted an unknown role.");
        }
        catch (InvalidDataException) { }
        var fourTopology = PartyTopologies.ForPlayerCount(4).Single(item => item.Id == "4-balanced");
        var fourRoster = LiveFollowSession.RosterArguments(fourTopology,
            [new LaunchedClient(101, 201, "C:/GameA.exe"),
             new LaunchedClient(102, 202, "D:/GameB.exe")]);
        if (!fourRoster.SequenceEqual(["-experimental-ordered-input",
                "-role-bind", "1:1:1:101", "-role-bind", "2:1:2:101",
                "-role-bind", "3:2:1:102", "-role-bind", "4:2:2:102"]))
            throw new Exception("Four-player desktop startup lost an ordered local role binding.");
        var fourSources = LiveFollowSession.ExecutableArguments(fourTopology,
            [new LaunchedClient(101, 201, "C:/GameA.exe"),
             new LaunchedClient(102, 202, "D:/GameB.exe")]);
        if (!fourSources.SequenceEqual(["-exe", "1=C:/GameA.exe", "-exe", "2=D:/GameB.exe"]))
            throw new Exception("Four-player live logs were bound by role instead of client.");
        var sessionLogs = LiveFollowSession.LogStartArguments(
            [new LaunchedClient(101, 201, "C:/GameA.exe") { LogStartOffset = 42 },
             new LaunchedClient(102, 202, "D:/GameB.exe")]);
        if (!sessionLogs.SequenceEqual(["-log-start-offset", "1=42"]))
            throw new Exception("Live follow lost the launched client's log boundary or replayed an attached client.");
        var logRoot = Path.Combine(Path.GetTempPath(),
            "partypilot-log-start-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(logRoot, "logs"));
            var gamePath = Path.Combine(logRoot, "PathOfExile_x64.exe");
            var logPath = Path.Combine(logRoot, "logs", "Client.txt");
            File.WriteAllText(logPath, "previous session\n");
            var startOffset = LaunchedClients.CaptureLogStartOffset(gamePath);
            if (startOffset != "previous session\n".Length ||
                LaunchedClients.CaptureLogStartOffset("relative-game.exe") is not null)
                throw new Exception("Desktop did not capture the exact pre-launch game log boundary.");
            File.AppendAllText(logPath, "current session\n");
            if (!LiveFollowSession.LogStartArguments(
                    [new LaunchedClient(101, 201, gamePath) { LogStartOffset = startOffset }])
                .SequenceEqual(["-log-start-offset", $"1={startOffset}"]))
                throw new Exception("Desktop did not pass the captured launch boundary to live follow.");
        }
        finally { if (Directory.Exists(logRoot)) Directory.Delete(logRoot, true); }
        var fourFrame = LiveFollowFrame.ParseDynamic(
            "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"catching_up\",\"x\":1,\"y\":0}," +
            "\"3\":{\"state\":\"following\",\"x\":0,\"y\":1},\"4\":{\"state\":\"waiting\"}}}", fourTopology);
        var fixedFourFrame = LiveFollowSession.ParseFrame(
            "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"catching_up\",\"x\":1,\"y\":0}," +
            "\"3\":{\"state\":\"following\",\"x\":0,\"y\":1},\"4\":{\"state\":\"waiting\"}}}",
            fourTopology, lastFocusedLeads: false);
        if (!fixedFourFrame.PadCommands.Select(command => command.Player).SequenceEqual([2, 3, 4]))
            throw new Exception("Fixed P1 leadership lost followers in the four-player layout.");
        var crossArea = LiveFollowSession.ParseFrame(
            "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\"}," +
            "\"3\":{\"state\":\"waiting\"},\"4\":{\"state\":\"waiting\"}}," +
            "\"handoffs\":[{\"destination_client\":2,\"driver_player\":3," +
            "\"target_player\":1,\"state\":\"warp_requested\"}]}",
            fourTopology, lastFocusedLeads: false);
        if (crossArea.Handoffs.Count != 1 ||
            crossArea.Handoffs[0] != new FollowHandoffRequest(2, 3, 1) ||
            crossArea.PadCommands.Any(command => command.Status != "waiting"))
            throw new Exception("Cross-area request changed a neutral follower or lost its bound client.");
        try
        {
            LiveFollowSession.ParseFrame(
                "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\"}," +
                "\"3\":{\"state\":\"waiting\"},\"4\":{\"state\":\"waiting\"}}," +
                "\"handoffs\":[{\"destination_client\":2,\"driver_player\":4," +
                "\"target_player\":1,\"state\":\"warp_requested\"}]}",
                fourTopology, lastFocusedLeads: false);
            throw new Exception("Cross-area request accepted P4 as the wrong client driver.");
        }
        catch (InvalidDataException) { }
        var fourSwitched = LiveFollowFrame.ParseDynamic(
            "{\"focused\":4,\"commands\":{\"1\":{\"state\":\"waiting\"}," +
            "\"2\":{\"state\":\"waiting\"},\"3\":{\"state\":\"waiting\"}}}", fourTopology);
        if (!fourFrame.PadCommands.Select(command => command.Pad).SequenceEqual([2, 3, 4]) ||
            fourFrame.PadCommands[0].Status != "catching_up" ||
            !fourFrame.PadCommands[1].Command.Contains("\"slot\":3", StringComparison.Ordinal) ||
            !fourSwitched.PadCommands.Select(command => command.Pad).SequenceEqual([1, 2, 3]))
            throw new Exception("Four-player follow frames lost a local role or pad mapping.");
        try
        {
            LiveFollowFrame.ParseDynamic(
                "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\"}," +
                "\"3\":{\"state\":\"waiting\"}}}", fourTopology);
            throw new Exception("Four-player follow accepted a missing P4 command.");
        }
        catch (InvalidDataException) { }
        foreach (var id in new[] { "5-third-solo", "5-main-solo" })
        {
            var five = PartyTopologies.ForPlayerCount(5).Single(item => item.Id == id);
            var fiveClients = new[]
            {
                new LaunchedClient(101, 201, "C:/GameA.exe"),
                new LaunchedClient(102, 202, "D:/GameB.exe"),
                new LaunchedClient(103, 203, "E:/GameC.exe")
            };
            var expectedRoster = new[] { "-experimental-ordered-input" }.Concat(
                five.Slots.SelectMany(slot => new[] { "-role-bind",
                    $"{slot.PartyPlayer}:{slot.Client}:{slot.LocalPlayer}:{fiveClients[slot.Client - 1].Pid}" }));
            if (!LiveFollowSession.RosterArguments(five, fiveClients).SequenceEqual(expectedRoster) ||
                !LiveFollowSession.ExecutableArguments(five, fiveClients).SequenceEqual(
                    ["-exe", "1=C:/GameA.exe", "-exe", "2=D:/GameB.exe", "-exe", "3=E:/GameC.exe"]))
                throw new Exception($"Five-player {id} did not bind each role and client log.");
            var fiveFrame = LiveFollowSession.ParseFrame(
                "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"catching_up\",\"x\":1,\"y\":0}," +
                "\"3\":{\"state\":\"waiting\"},\"4\":{\"state\":\"waiting\"}," +
                "\"5\":{\"state\":\"following\",\"x\":0,\"y\":1}}}",
                five, lastFocusedLeads: false);
            if (!fiveFrame.PadCommands.Select(command => command.Player).SequenceEqual([2, 3, 4, 5]) ||
                fiveFrame.PadCommands.Last().Pad != (id == "5-third-solo" ? 5 : 6))
                throw new Exception($"Five-player {id} lost the Client 3 follower pad.");
        }
        var six = PartyTopologies.ForPlayerCount(6).Single(item => item.Id == "6-balanced");
        var sixRoster = LiveFollowSession.RosterArguments(six,
            [new LaunchedClient(101, 201, "C:/GameA.exe"),
             new LaunchedClient(102, 202, "D:/GameB.exe"),
             new LaunchedClient(103, 203, "E:/GameC.exe")]);
        if (!sixRoster.SequenceEqual(["-experimental-ordered-input",
                "-role-bind", "1:1:1:101", "-role-bind", "2:1:2:101",
                "-role-bind", "3:2:1:102", "-role-bind", "4:2:2:102",
                "-role-bind", "5:3:1:103", "-role-bind", "6:3:2:103"]))
            throw new Exception("Six-player desktop startup lost an ordered local role binding.");
        var sixSources = LiveFollowSession.ExecutableArguments(six,
            [new LaunchedClient(101, 201, "C:/GameA.exe"),
             new LaunchedClient(102, 202, "D:/GameB.exe"),
             new LaunchedClient(103, 203, "E:/GameC.exe")]);
        if (!sixSources.SequenceEqual(["-exe", "1=C:/GameA.exe", "-exe", "2=D:/GameB.exe",
                "-exe", "3=E:/GameC.exe"]))
            throw new Exception("Six-player live logs were bound by role instead of client.");
        var sixFrame = LiveFollowFrame.ParseDynamic(
            "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"catching_up\",\"x\":1,\"y\":0,\"sprint_down\":true}," +
            "\"3\":{\"state\":\"following\",\"x\":0,\"y\":1},\"4\":{\"state\":\"waiting\"}," +
            "\"5\":{\"state\":\"waiting\"},\"6\":{\"state\":\"following\",\"x\":0,\"y\":-1}}}", six);
        var fixedSixFrame = LiveFollowSession.ParseFrame(
            "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"catching_up\",\"x\":1,\"y\":0}," +
            "\"3\":{\"state\":\"following\",\"x\":0,\"y\":1},\"4\":{\"state\":\"waiting\"}," +
            "\"5\":{\"state\":\"waiting\"},\"6\":{\"state\":\"following\",\"x\":0,\"y\":-1}}}",
            six, lastFocusedLeads: false);
        if (!fixedSixFrame.PadCommands.Select(command => command.Player).SequenceEqual([2, 3, 4, 5, 6]))
            throw new Exception("Fixed P1 leadership lost followers in the six-player layout.");
        var sixPriority = LiveFollowFrame.ApplyCatchUpPriority(sixFrame, enabled: true);
        var sixSwitched = LiveFollowFrame.ParseDynamic(
            "{\"focused\":6,\"commands\":{\"1\":{\"state\":\"waiting\"},\"2\":{\"state\":\"waiting\"}," +
            "\"3\":{\"state\":\"waiting\"},\"4\":{\"state\":\"waiting\"},\"5\":{\"state\":\"waiting\"}}}", six);
        if (!sixFrame.PadCommands.Select(command => command.Pad).SequenceEqual([2, 3, 4, 5, 6]) ||
            sixFrame.PadCommands[0].Status != "catching_up" ||
            sixFrame.PadCommands[4].Status != "following" ||
            !sixFrame.PadCommands[4].Command.Contains("\"slot\":6", StringComparison.Ordinal) ||
            !sixPriority.PadCommands[0].Command.Contains("\"catch_up_priority\":true", StringComparison.Ordinal) ||
            sixPriority.PadCommands.Skip(1).Any(command => command.Command.Contains("catch_up_priority", StringComparison.Ordinal)) ||
            !sixSwitched.PadCommands.Select(command => command.Pad).SequenceEqual([1, 2, 3, 4, 5]))
            throw new Exception("Six-player follow frames lost a role, pad, or catch-up state.");
        try
        {
            LiveFollowFrame.ParseDynamic(
                "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\"},\"3\":{\"state\":\"waiting\"}," +
                "\"4\":{\"state\":\"waiting\"},\"5\":{\"state\":\"waiting\"}}}", six);
            throw new Exception("Six-player follow accepted a missing P6 command.");
        }
        catch (InvalidDataException) { }
        if (OperatingSystem.IsWindows()) return; // The local fake executables use a Unix shebang.
        var directory = Path.Combine(Path.GetTempPath(), "partypilot-pipeline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var log = Path.Combine(directory, "bridge-input.jsonl");
        var bridgeExe = Path.Combine(directory, "fake-bridge");
        var mainInputBridgeExe = Path.Combine(directory, "fake-main-input-bridge");
        var combatBridgeExe = Path.Combine(directory, "fake-combat-bridge");
        var combatLog = Path.Combine(directory, "combat-input.jsonl");
        var mainInputFollowExe = Path.Combine(directory, "fake-main-input-follow");
        var mainInputLog = Path.Combine(directory, "main-input-control.jsonl");
        var attackInputFollowExe = Path.Combine(directory, "fake-attack-input-follow");
        var attackInputLog = Path.Combine(directory, "attack-input-control.jsonl");
        var couchBridgeExe = Path.Combine(directory, "fake-couch-bridge");
        var wgiBridgeExe = Path.Combine(directory, "fake-wgi-bridge");
        var client3BridgeExe = Path.Combine(directory, "fake-client3-bridge");
        var followExe = Path.Combine(directory, "fake-follow");
        var customFollowExe = Path.Combine(directory, "fake-custom-follow");
        var terrainFollowExe = Path.Combine(directory, "fake-terrain-follow");
        var disabledClassicExe = Path.Combine(directory, "fake-disabled-classic-follow");
        var aggressiveFollowExe = Path.Combine(directory, "fake-aggressive-follow");
        var dynamicBridgeExe = Path.Combine(directory, "fake-dynamic-bridge");
        var dynamicFollowExe = Path.Combine(directory, "fake-dynamic-follow");
        var dynamicLog = Path.Combine(directory, "dynamic-bridge-input.jsonl");
        var switchBridgeExe = Path.Combine(directory, "fake-switch-bridge");
        var switchFollowExe = Path.Combine(directory, "fake-switch-follow");
        var switchLog = Path.Combine(directory, "switch-bridge-input.jsonl");
        var tripleBridgeExe = Path.Combine(directory, "fake-triple-bridge");
        var tripleFollowExe = Path.Combine(directory, "fake-triple-follow");
        var tripleLog = Path.Combine(directory, "triple-bridge-input.jsonl");
        try
        {
            File.WriteAllText(bridgeExe, "#!/usr/bin/env python3\nimport sys\n" +
                "if '--focus-pad-map' not in sys.argv or sys.argv[sys.argv.index('--focus-pad-map')+1] != '1:1,2:3': sys.exit(2)\n" +
                "if '--enable-auto-flask' not in sys.argv: sys.exit(2)\n" +
                "if '--disable-menu-guard' not in sys.argv: sys.exit(2)\n" +
                "print('{\"event\":\"ready\",\"slots\":[1,3],\"physical_mode\":true,\"pairing_protected\":true,\"hidhide_preflight\":true,\"menu_guard_enabled\":false,\"last_focused_leads\":false,\"auto_flask_enabled\":true,\"right_stick_follower_override\":false,\"direct_right_stick_slot\":0,\"focus_pad_map\":\"1:1,2:3\",\"private_client3_pid\":0,\"private_client3_window_handle\":0,\"private_client3_slots\":[]}', flush=True)\n" +
                $"for line in sys.stdin:\n    with open({JsonSerializer.Serialize(log)}, 'a') as out: out.write(line)\n");
            File.WriteAllText(mainInputBridgeExe, "#!/usr/bin/env python3\nimport sys,json\n" +
                "print('{\"event\":\"ready\",\"slots\":[1,3],\"physical_mode\":true,\"pairing_protected\":true,\"hidhide_preflight\":true,\"menu_guard_enabled\":true,\"last_focused_leads\":false,\"auto_flask_enabled\":false,\"right_stick_follower_override\":false,\"direct_right_stick_slot\":0,\"focus_pad_map\":\"1:1,2:3\",\"private_client3_pid\":0,\"private_client3_window_handle\":0,\"private_client3_slots\":[]}', flush=True)\n" +
                "for line in sys.stdin:\n" +
                "    command=json.loads(line)\n" +
                "    if command.get('type') != 'main_input_subscription': continue\n" +
                "    enabled=command['enabled']\n" +
                "    print(json.dumps({'event':'main_input_subscription','enabled':enabled}), flush=True)\n" +
                "    if enabled:\n" +
                "        print('{\"event\":\"main_input\",\"slot\":1,\"left_x\":40000,\"left_y\":0,\"b_down\":true,\"attack_active\":false,\"blocked\":false}', flush=True)\n" +
                "        print('{\"event\":\"main_input\",\"slot\":1,\"left_x\":1234,\"left_y\":-2345,\"b_down\":true,\"attack_active\":true,\"blocked\":false}', flush=True)\n");
            File.WriteAllText(combatBridgeExe, "#!/usr/bin/env python3\nimport sys,json\n" +
                "if '--enable-combat' not in sys.argv: sys.exit(2)\n" +
                "print('{\"event\":\"ready\",\"slots\":[1,3],\"physical_mode\":true,\"pairing_protected\":true,\"hidhide_preflight\":true,\"menu_guard_enabled\":true,\"last_focused_leads\":false,\"auto_flask_enabled\":false,\"combat_enabled\":true,\"right_stick_follower_override\":false,\"direct_right_stick_slot\":0,\"focus_pad_map\":\"1:1,2:3\",\"private_client3_pid\":0,\"private_client3_window_handle\":0,\"private_client3_slots\":[]}', flush=True)\n" +
                "for line in sys.stdin:\n" +
                "    command=json.loads(line)\n" +
                $"    with open({JsonSerializer.Serialize(combatLog)}, 'a') as out: out.write(line)\n" +
                "    print(json.dumps({'event':'combat_control','type':command['type'],'slot':command['slot']}), flush=True)\n");
            File.WriteAllText(mainInputFollowExe, "#!/usr/bin/env python3\nimport sys,json,time\n" +
                "if '-accept-main-input' not in sys.argv or '-close-range-mirror' not in sys.argv: sys.exit(2)\n" +
                "if '-close-mirror-duration' not in sys.argv or sys.argv[sys.argv.index('-close-mirror-duration')+1] != '650ms': sys.exit(2)\n" +
                "if '-mirror-b-players' not in sys.argv or sys.argv[sys.argv.index('-mirror-b-players')+1] != '1,2': sys.exit(2)\n" +
                "print('{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\"}}}', flush=True)\n" +
                "for line in sys.stdin:\n" +
                "    control=json.loads(line)\n" +
                "    if control.get('type') == 'main_input':\n" +
                "        if control.get('slot') != 1 or abs(control.get('left_x',0)-1234/32767) > 0.0001 or abs(control.get('left_y',0)+2345/32767) > 0.0001 or not control.get('b_down') or not control.get('attack_active') or control.get('blocked') or control.get('observed_at_unix_ms',0) <= 0: sys.exit(2)\n" +
                $"        open({JsonSerializer.Serialize(mainInputLog)}, 'w').write(line)\n" +
                "        time.sleep(0.5)\n        break\n");
            File.WriteAllText(attackInputFollowExe, "#!/usr/bin/env python3\nimport sys,json\n" +
                "if '-accept-main-input' not in sys.argv or '-mirror-attack-direction' not in sys.argv or '-close-range-mirror' in sys.argv: sys.exit(2)\n" +
                "print('{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\"}}}', flush=True)\n" +
                "for line in sys.stdin:\n" +
                "    command=json.loads(line)\n" +
                "    if command.get('type')!='main_input': continue\n" +
                "    if not command.get('attack_active') or command.get('blocked'): sys.exit(2)\n" +
                $"    open({JsonSerializer.Serialize(attackInputLog)}, 'w').write(line)\n" +
                "    break\n");
            File.WriteAllText(followExe, "#!/usr/bin/env python3\nimport sys,json\n" +
                "if '-auto-flask' not in sys.argv: sys.exit(2)\n" +
                "if '-flask-support-file' not in sys.argv: sys.exit(2)\n" +
                "support=json.load(open(sys.argv[sys.argv.index('-flask-support-file')+1]))\n" +
                "if support['bindings'][0]['drinker_player'] != 2 or support['bindings'][0]['monitor']['target_player'] != 1: sys.exit(2)\n" +
                "if '-flask-rules-file' not in sys.argv: sys.exit(2)\n" +
                "rules=json.load(open(sys.argv[sys.argv.index('-flask-rules-file')+1]))\n" +
                "if len(rules['players']) != 2 or rules['players'][1]['life_trigger'] != 60 or not rules['players'][1]['combined_defence']: sys.exit(2)\n" +
                "if '-classic-sprint-catchup' in sys.argv: sys.exit(2)\n" +
                "print('{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\",\"reason\":\"missing position\"}}}', flush=True)\n" +
                "states = [json.loads(sys.stdin.readline()) for _ in range(2)]\n" +
                "if {(s['slot'],s['state']) for s in states} != {(1,'clear'),(2,'clear')}: sys.exit(2)\n" +
                "print('{\"focused\":1,\"commands\":{\"2\":{\"state\":\"catching_up\",\"x\":1,\"y\":0,\"manual_override_eligible\":true,\"sprint_down\":true}},\"flasks\":[{\"player\":2,\"resource\":\"life\",\"percent\":30,\"reason\":\"support\"}]}', flush=True)\n" +
                "for line in sys.stdin:\n    control=json.loads(line)\n    if control.get('type')!='follow_pause': sys.exit(2)\n    if control.get('state')=='paused': print('{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\",\"reason\":\"follow paused\"}}}', flush=True)\n    elif control.get('state')=='following': print('{\"focused\":1,\"commands\":{\"2\":{\"state\":\"following\",\"x\":1,\"y\":0}}}', flush=True)\n    else: sys.exit(2)\n");
            File.WriteAllText(customFollowExe, "#!/usr/bin/env python3\nimport sys\n" +
                "if '-auto-flask' not in sys.argv or '-flask-custom-file' not in sys.argv: sys.exit(2)\n" +
                "print('{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\"}},\"flasks\":[{\"player\":2,\"resource\":\"action\",\"percent\":30,\"reason\":\"custom Energy Shield ≤50% · priority 50\",\"direct_input\":\"X\"}]}', flush=True)\n" +
                "for line in sys.stdin: pass\n");
            var terrainClient1 = Path.Combine(directory, "terrain-client-1.json");
            var terrainClient2 = Path.Combine(directory, "terrain-client-2.json");
            File.WriteAllText(terrainClient1, "{}");
            File.WriteAllText(terrainClient2, "{}");
            File.WriteAllText(terrainFollowExe, "#!/usr/bin/env python3\nimport sys\n" +
                $"expected = {{'1={terrainClient1}', '2={terrainClient2}'}}\n" +
                "bindings = {sys.argv[i+1] for i, arg in enumerate(sys.argv[:-1]) if arg == '-terrain-binding'}\n" +
                "if bindings != expected or '-classic-sprint-catchup' not in sys.argv or '-classic-collision-dodge' not in sys.argv: sys.exit(2)\n" +
                "print('{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\"}}}', flush=True)\n" +
                "for line in sys.stdin: pass\n");
            File.WriteAllText(disabledClassicExe, "#!/usr/bin/env python3\nimport sys\n" +
                "if '-disable-sprint-catchup' not in sys.argv or '-classic-sprint-catchup' not in sys.argv: sys.exit(2)\n" +
                "if '-classic-collision-dodge' in sys.argv or '-terrain-binding' in sys.argv: sys.exit(2)\n" +
                "print('{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\"}}}', flush=True)\n" +
                "for line in sys.stdin: pass\n");
            File.WriteAllText(aggressiveFollowExe, "#!/usr/bin/env python3\nimport sys\n" +
                "if '-aggressive-blink-catchup' not in sys.argv or '-classic-sprint-catchup' in sys.argv: sys.exit(2)\n" +
                "if '-disable-sprint-catchup' not in sys.argv: sys.exit(2)\n" +
                "if '-aggressive-sprint-retry-delay-ms' not in sys.argv or sys.argv[sys.argv.index('-aggressive-sprint-retry-delay-ms')+1] != '900': sys.exit(2)\n" +
                "print('{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\"}}}', flush=True)\n" +
                "for line in sys.stdin: pass\n");
            File.WriteAllText(dynamicBridgeExe, "#!/usr/bin/env python3\nimport sys\n" +
                "if '--last-focused-leads' not in sys.argv: sys.exit(2)\n" +
                "print('{\"event\":\"ready\",\"slots\":[1,3],\"physical_mode\":true,\"pairing_protected\":true,\"hidhide_preflight\":true,\"menu_guard_enabled\":true,\"last_focused_leads\":true,\"auto_flask_enabled\":false,\"right_stick_follower_override\":false,\"direct_right_stick_slot\":0,\"focus_pad_map\":\"1:1,2:3\",\"private_client3_pid\":0,\"private_client3_window_handle\":0,\"private_client3_slots\":[]}', flush=True)\n" +
                "print('{\"event\":\"manual_focus\",\"slot\":3}', flush=True)\n" +
                $"for line in sys.stdin:\n    with open({JsonSerializer.Serialize(dynamicLog)}, 'a') as out: out.write(line)\n");
            File.WriteAllText(dynamicFollowExe, "#!/usr/bin/env python3\nimport sys,json,time\n" +
                "if '-last-focused-leads' not in sys.argv: sys.exit(2)\n" +
                "if '-member-name' not in sys.argv or sys.argv[sys.argv.index('-member-name')+1] != '2=Second': sys.exit(2)\n" +
                "focus=json.loads(sys.stdin.readline())\n" +
                "if focus != {'type':'manual_focus','slot':2}: sys.exit(2)\n" +
                "print('{\"focused\":2,\"commands\":{\"1\":{\"state\":\"following\",\"x\":0,\"y\":1}}}', flush=True)\n" +
                "time.sleep(0.3)\n");
            File.WriteAllText(switchBridgeExe, "#!/usr/bin/env python3\nimport sys,json\n" +
                "if '--last-focused-leads' not in sys.argv: sys.exit(2)\n" +
                "print('{\"event\":\"ready\",\"slots\":[1,3],\"physical_mode\":true,\"pairing_protected\":true,\"hidhide_preflight\":true,\"menu_guard_enabled\":true,\"last_focused_leads\":true,\"auto_flask_enabled\":false,\"right_stick_follower_override\":false,\"direct_right_stick_slot\":0,\"focus_pad_map\":\"1:1,2:3\",\"private_client3_pid\":0,\"private_client3_window_handle\":0,\"private_client3_slots\":[]}', flush=True)\n" +
                "phase=0\n" +
                $"for line in sys.stdin:\n    with open({JsonSerializer.Serialize(switchLog)}, 'a') as out: out.write(line)\n" +
                "    command=json.loads(line)\n" +
                "    if phase==0 and command.get('slot')==3 and command.get('state')=='following':\n" +
                "        print('{\"event\":\"manual_focus\",\"slot\":3}', flush=True); phase=1\n" +
                "    elif phase==1 and command.get('slot')==1 and command.get('state')=='following':\n" +
                "        print('{\"event\":\"manual_focus\",\"slot\":1}', flush=True); phase=2\n");
            File.WriteAllText(switchFollowExe, "#!/usr/bin/env python3\nimport sys,json,time\n" +
                "if '-last-focused-leads' not in sys.argv: sys.exit(2)\n" +
                "print('{\"focused\":1,\"commands\":{\"2\":{\"state\":\"following\",\"x\":1,\"y\":0}}}', flush=True)\n" +
                "for expected, follower in [(2,1),(1,2)]:\n" +
                "    focus=json.loads(sys.stdin.readline())\n" +
                "    if focus != {'type':'manual_focus','slot':expected}: sys.exit(2)\n" +
                "    print(json.dumps({'focused':expected,'commands':{str(follower):{'state':'following','x':1,'y':0}}}), flush=True)\n" +
                "time.sleep(0.3)\n");
            File.WriteAllText(tripleBridgeExe, "#!/usr/bin/env python3\nimport sys,json\n" +
                "if '--last-focused-leads' not in sys.argv or '--client3-players' not in sys.argv or sys.argv[sys.argv.index('--client3-players')+1] != '5': sys.exit(2)\n" +
                "print(json.dumps({'event':'ready','slots':[1,3],'physical_mode':True,'pairing_protected':True,'hidhide_preflight':True,'menu_guard_enabled':True,'last_focused_leads':True,'auto_flask_enabled':False,'right_stick_follower_override':False,'direct_right_stick_slot':0,'focus_pad_map':'1:1,2:3,3:5','private_client3_pid':103,'private_client3_window_handle':203,'private_client3_slots':[5],'private_client3_adopted':'--client3-adopt-existing' in sys.argv}), flush=True)\n" +
                "focused=False\n" +
                $"for line in sys.stdin:\n    with open({JsonSerializer.Serialize(tripleLog)}, 'a') as out: out.write(line)\n" +
                "    command=json.loads(line)\n" +
                "    if not focused and command.get('slot')==5 and command.get('state')=='following':\n" +
                "        print('{\"event\":\"manual_focus\",\"slot\":5}', flush=True); focused=True\n");
            File.WriteAllText(tripleFollowExe, "#!/usr/bin/env python3\nimport sys,json,time\n" +
                "if '-roster' not in sys.argv or sys.argv[sys.argv.index('-roster')+1] != '1:101,2:102,3:103': sys.exit(2)\n" +
                "if '-last-focused-leads' not in sys.argv: sys.exit(2)\n" +
                "if '-follow-target' not in sys.argv or sys.argv[sys.argv.index('-follow-target')+1] != '2=3': sys.exit(2)\n" +
                "if '-stop-player' not in sys.argv or sys.argv[sys.argv.index('-stop-player')+1] != '2=37.25': sys.exit(2)\n" +
                "print('{\"focused\":1,\"commands\":{\"2\":{\"state\":\"following\",\"x\":1,\"y\":0},\"3\":{\"state\":\"following\",\"x\":0,\"y\":1}}}', flush=True)\n" +
                "focus=json.loads(sys.stdin.readline())\n" +
                "if focus != {'type':'manual_focus','slot':3}: sys.exit(2)\n" +
                "print('{\"focused\":3,\"commands\":{\"1\":{\"state\":\"following\",\"x\":1,\"y\":0},\"2\":{\"state\":\"following\",\"x\":0,\"y\":1}}}', flush=True)\n" +
                "time.sleep(0.3)\n");
            File.WriteAllText(couchBridgeExe, "#!/usr/bin/env python3\nimport sys,json\n" +
                "if '--focus-pad-map' not in sys.argv or sys.argv[sys.argv.index('--focus-pad-map')+1] != '1:1,2:2': sys.exit(2)\n" +
                "if '--right-stick-follower-override' not in sys.argv or '--direct-right-stick-slot' not in sys.argv or sys.argv[sys.argv.index('--direct-right-stick-slot')+1] != '2': sys.exit(2)\n" +
                "dodge='--right-stick-dodge-on-engage' in sys.argv\n" +
                "if dodge and ('--right-stick-dodge-button' not in sys.argv or sys.argv[sys.argv.index('--right-stick-dodge-button')+1] != 'RB'): sys.exit(2)\n" +
                "print(json.dumps({'event':'ready','slots':[1,2],'physical_mode':True,'pairing_protected':True,'hidhide_preflight':True,'menu_guard_enabled':True,'last_focused_leads':False,'auto_flask_enabled':False,'right_stick_follower_override':True,'right_stick_dodge_on_engage':dodge,'right_stick_dodge_button':'RB' if dodge else 'B','direct_right_stick_slot':2,'focus_pad_map':'1:1,2:2','private_client3_pid':0,'private_client3_window_handle':0,'private_client3_slots':[]}), flush=True)\n" +
                "for line in sys.stdin: pass\n");
            File.WriteAllText(wgiBridgeExe, "#!/usr/bin/env python3\nimport sys\n" +
                "if '--physical-wgi' not in sys.argv or '--physical-index' in sys.argv: sys.exit(2)\n" +
                "if '--focus-pad-map' not in sys.argv or sys.argv[sys.argv.index('--focus-pad-map')+1] != '1:1,2:2,3:3,4:4': sys.exit(2)\n" +
                "print('{\"event\":\"ready\",\"slots\":[1,2,3,4],\"physical_mode\":true,\"physical_wgi\":true,\"pairing_protected\":true,\"hidhide_preflight\":true,\"menu_guard_enabled\":true,\"last_focused_leads\":false,\"auto_flask_enabled\":false,\"right_stick_follower_override\":false,\"direct_right_stick_slot\":0,\"focus_pad_map\":\"1:1,2:2,3:3,4:4\",\"private_client3_pid\":0,\"private_client3_window_handle\":0,\"private_client3_slots\":[]}', flush=True)\n" +
                "for line in sys.stdin: pass\n");
            File.WriteAllText(client3BridgeExe, "#!/usr/bin/env python3\nimport sys\n" +
                "def value(flag):\n    return sys.argv[sys.argv.index(flag)+1] if flag in sys.argv else None\n" +
                "if value('--slots') != '1,3,4' or value('--focus-pad-map') != '1:1,2:3,3:4,4:5,5:6': sys.exit(2)\n" +
                "if value('--client3-pid') != '103' or value('--client3-window-handle') != '203' or value('--client3-players') != '5,6': sys.exit(2)\n" +
                "games = [sys.argv[i+1] for i, arg in enumerate(sys.argv[:-1]) if arg == '--game-exe']\n" +
                "if len(games) != 3 or value('--client3-game-exe') != games[2]: sys.exit(2)\n" +
                "print('{\"event\":\"ready\",\"slots\":[1,3,4],\"physical_mode\":true,\"pairing_protected\":true,\"hidhide_preflight\":true,\"menu_guard_enabled\":true,\"last_focused_leads\":false,\"auto_flask_enabled\":false,\"right_stick_follower_override\":false,\"direct_right_stick_slot\":0,\"focus_pad_map\":\"1:1,2:3,3:4,4:5,5:6\",\"private_client3_pid\":103,\"private_client3_window_handle\":203,\"private_client3_slots\":[5,6]}', flush=True)\n" +
                "for line in sys.stdin: pass\n");
            File.SetUnixFileMode(bridgeExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(mainInputBridgeExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(combatBridgeExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(mainInputFollowExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(attackInputFollowExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(followExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(customFollowExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(terrainFollowExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(disabledClassicExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(aggressiveFollowExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(dynamicBridgeExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(dynamicFollowExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(switchBridgeExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(switchFollowExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(tripleBridgeExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(tripleFollowExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(couchBridgeExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(wgiBridgeExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(client3BridgeExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Console.WriteLine("Pipeline smoke: starting fake bridge.");
            var topology = PartyTopologies.ForPlayerCount(2).Single(item => item.Id == "2-split");
            await using (var combatBridge = await BridgeSession.StartAsync(combatBridgeExe,
                topology.Slots, 0, @"HID\VID_1234&PID_5678\DEVICE", ["C:/GameA.exe", "D:/GameB.exe"],
                false, combatEnabled: true))
            {
                combatBridge.StartReading();
                if (!combatBridge.CombatEnabled ||
                    BridgeSession.ValidCombatInput("LT+L3", 85) ||
                    BridgeSession.ValidCombatInput("LT+X", 44))
                    throw new Exception("Combat input gate accepted an unsupported binding or duration.");
                await combatBridge.BeginCombatAsync(3);
                await combatBridge.PulseCombatAsync(3, "LT+X", 85, 32767, 0);
                await combatBridge.EndCombatAsync(3);
                var sent = File.ReadAllLines(combatLog).Select(line => JsonDocument.Parse(line)).ToArray();
                try
                {
                    if (sent.Length != 3 || sent[0].RootElement.GetProperty("type").GetString() != "combat_begin" ||
                        sent[1].RootElement.GetProperty("input").GetString() != "LT+X" ||
                        sent[1].RootElement.GetProperty("hold_ms").GetInt32() != 85 ||
                        sent[1].RootElement.GetProperty("right_x").GetInt32() != 32767 ||
                        sent[1].RootElement.GetProperty("right_y").GetInt32() != 0 ||
                        sent[2].RootElement.GetProperty("type").GetString() != "combat_end")
                        throw new Exception("Combat ownership and pulse did not cross the desktop process boundary.");
                }
                finally { foreach (var document in sent) document.Dispose(); }
            }
            await using (var mainInputBridge = await BridgeSession.StartAsync(mainInputBridgeExe,
                topology.Slots, 0, @"HID\VID_1234&PID_5678\DEVICE", ["C:/GameA.exe", "D:/GameB.exe"], false))
            {
                var sampleReceived = new TaskCompletionSource<MainInputSample>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                mainInputBridge.MainInputChanged += sample => sampleReceived.TrySetResult(sample);
                mainInputBridge.StartReading();
                if (mainInputBridge.CombatEnabled) throw new Exception("Combat must remain disabled by default.");
                try
                {
                    await mainInputBridge.BeginCombatAsync(3);
                    throw new Exception("A bridge without combat enabled accepted a combat lease.");
                }
                catch (InvalidOperationException) { }
                await mainInputBridge.SetMainInputSubscriptionAsync(true);
                var sample = await sampleReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));
                if (sample.Pad != 1 || sample.LeftX != 1234 || sample.LeftY != -2345 ||
                    !sample.BDown || !sample.AttackActive || sample.Blocked ||
                    sample.ReceivedAtUtc.Kind != DateTimeKind.Utc)
                    throw new Exception("Main input event was not bound to the physical pad sample.");
                await mainInputBridge.SetMainInputSubscriptionAsync(false);
            }
            await using var bridge = await BridgeSession.StartAsync(bridgeExe, topology.Slots, 0,
                @"HID\VID_1234&PID_5678\DEVICE", ["C:/GameA.exe", "D:/GameB.exe"], false, true,
                menuGuardEnabled: false);
            if (!bridge.IsRunning) throw new Exception("Bridge readiness did not leave a live prelaunch process.");
            Console.WriteLine("Pipeline smoke: fake bridge ready.");
            bridge.StartReading();
            var clients = new[]
            {
                new LaunchedClient(101, 201, "C:/GameA.exe"),
                new LaunchedClient(102, 202, "D:/GameB.exe")
            };
            if (!LiveFollowPlan.TryCreate("2", "10", "750ms", "30m", null, out var plan) || plan is null)
                throw new Exception("The pipeline test plan is invalid.");
            await using (var inputBridge = await BridgeSession.StartAsync(mainInputBridgeExe,
                topology.Slots, 0, @"HID\VID_1234&PID_5678\DEVICE", ["C:/GameA.exe", "D:/GameB.exe"], false))
            {
                inputBridge.StartReading();
                await using var inputFollow = await LiveFollowSession.StartAsync(mainInputFollowExe,
                    topology, clients, inputBridge, plan, relayMainInput: true,
                    closeRangeMirror: true, closeMirrorDuration: 650);
                inputFollow.StartReading();
                var inputDeadline = DateTime.UtcNow.AddSeconds(3);
                while (!File.Exists(mainInputLog) && DateTime.UtcNow < inputDeadline)
                    await Task.Delay(20);
                if (!File.Exists(mainInputLog))
                    throw new Exception("Main controller sample did not reach the live follow process.");
            }
            Console.WriteLine("Main controller input passed through bridge, desktop, and live process fakes.");
            await using (var attackBridge = await BridgeSession.StartAsync(mainInputBridgeExe,
                topology.Slots, 0, @"HID\VID_1234&PID_5678\DEVICE", ["C:/GameA.exe", "D:/GameB.exe"], false))
            {
                attackBridge.StartReading();
                await using var attackFollow = await LiveFollowSession.StartAsync(attackInputFollowExe,
                    topology, clients, attackBridge, plan, mirrorAttackDirection: true,
                    relayMainInput: true);
                attackFollow.StartReading();
                var attackDeadline = DateTime.UtcNow.AddSeconds(3);
                while (!File.Exists(attackInputLog) && DateTime.UtcNow < attackDeadline)
                    await Task.Delay(20);
                if (!File.Exists(attackInputLog))
                    throw new Exception("Attack direction did not subscribe to main input independently.");
            }
            Console.WriteLine("Attack direction subscribed without close-range mirroring.");
            Console.WriteLine("Pipeline smoke: starting fake follow.");
            var supportFile = new FlaskSupportPreferenceStore(directory).Save(2,
                [FlaskSupportPreferenceStore.Default(2, 1) with
                {
                    SupportBuild = true,
                    Monitor = new FlaskSupportMonitor(true, 1, "Life", 50, "Life", false)
                }]).RuntimeFile ?? throw new Exception("Drinker test file was not saved.");
            var ruleFile = new FlaskRulePreferenceStore(directory).Save(2,
                [FlaskRulePreferenceStore.DefaultFor(1),
                    FlaskRulePreferenceStore.DefaultFor(2) with { LifeTrigger = 60, CombinedDefence = true }])
                .RuntimeFile ?? throw new Exception("Player flask rule test file was not saved.");
            await using (var follow = await LiveFollowSession.StartAsync(followExe, topology, clients, bridge, plan,
                flaskSupportFile: supportFile, flaskRulesFile: ruleFile))
            {
                Console.WriteLine("Pipeline smoke: fake follow ready.");
                follow.StartReading();
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (DateTime.UtcNow < deadline && (!File.Exists(log) || File.ReadAllLines(log).Length < 3))
                    await Task.Delay(20);
                follow.SetPaused(true);
                while (DateTime.UtcNow < deadline && follow.Current.Reason != "follow paused")
                    await Task.Delay(20);
                if (follow.Current.Reason != "follow paused")
                    throw new Exception("The paused follow frame was not received.");
                follow.SetPaused(false);
                while (DateTime.UtcNow < deadline && follow.Current.Status != "following")
                    await Task.Delay(20);
                if (follow.Current.Status != "following")
                    throw new Exception("The resumed follow frame was not received.");
            }
            var neutralDeadline = DateTime.UtcNow.AddSeconds(2);
            string[] lines;
            do
            {
                lines = File.Exists(log) ? File.ReadAllLines(log) : [];
                if (lines.Length >= 6 && lines[^1].Contains("\"slot\":3,\"state\":\"waiting\""))
                    break;
                await Task.Delay(20);
            } while (DateTime.UtcNow < neutralDeadline);
            if (lines.Length < 6 ||
                !lines[0].Contains("\"slot\":3,\"state\":\"waiting\"") ||
                !lines[1].Contains("\"slot\":3,\"state\":\"catching_up\"") ||
                !lines[1].Contains("\"manual_override_eligible\":true") ||
                !lines[1].Contains("\"sprint_down\":true") ||
                !lines[1].Contains("\"catch_up_priority\":true") ||
                !lines[2].Contains("\"type\":\"flask\",\"slot\":3,\"resource\":\"life\"") ||
                !lines.Any(line => line.Contains("\"slot\":3,\"state\":\"following\"")) ||
                !lines.Any(line => line.Contains("\"slot\":3,\"state\":\"waiting\"")) ||
                !lines[^1].Contains("\"slot\":3,\"state\":\"waiting\""))
                throw new Exception("Live follow did not hand off movement/flasks and neutralize on exit: " + string.Join(" | ", lines));
            Console.WriteLine("Desktop movement, flask, pause/resume, and stop handoff checked with local fake executables.");
            var customFile = Path.Combine(directory, "custom-recovery.json");
            File.Copy(Path.GetFullPath("testdata/flask-custom-live.json"), customFile);
            await using (var customFollow = await LiveFollowSession.StartAsync(customFollowExe,
                topology, clients, bridge, plan, flaskCustomFile: customFile))
            {
                customFollow.StartReading();
                var customDeadline = DateTime.UtcNow.AddSeconds(3);
                while ((!File.Exists(log) || !File.ReadAllLines(log).Any(line =>
                           line.Contains("\"type\":\"recovery_action\",\"slot\":3,\"input\":\"X\""))) &&
                       DateTime.UtcNow < customDeadline)
                    await Task.Delay(20);
                if (!File.Exists(log) || !File.ReadAllLines(log).Any(line =>
                        line.Contains("\"type\":\"recovery_action\",\"slot\":3,\"input\":\"X\"")))
                    throw new Exception("Desktop did not hand off custom X recovery to P2's pad.");
            }
            Console.WriteLine("Desktop custom recovery decision reached the assigned bridge pad.");
            try
            {
                await LiveFollowSession.StartAsync(terrainFollowExe, topology, clients, bridge, plan,
                    classicCollisionDodge: true);
                throw new Exception("Collision dodge started without terrain bindings.");
            }
            catch (ArgumentException) { }
            await using (var disabledClassic = await LiveFollowSession.StartAsync(disabledClassicExe,
                topology, clients, bridge, plan, classicCollisionDodge: true,
                classicSprintCatchUp: true, sprintCatchUp: false))
            {
                if (disabledClassic.Current.FocusedPlayer != 1)
                    throw new Exception("Disabled sprint still required terrain for inactive collision recovery.");
            }
            await using (var terrainFollow = await LiveFollowSession.StartAsync(terrainFollowExe,
                topology, clients, bridge, plan,
                terrainBindings: new Dictionary<int, string>
                {
                    [1] = terrainClient1, [2] = terrainClient2
                }, classicCollisionDodge: true, classicSprintCatchUp: true))
            {
                if (terrainFollow.Current.FocusedPlayer != 1)
                    throw new Exception("Bound terrain follow did not return its initial frame.");
            }
            Console.WriteLine("Desktop terrain bindings and collision option reached the live process.");
            await using (var aggressiveFollow = await LiveFollowSession.StartAsync(aggressiveFollowExe,
                topology, clients, bridge, plan, sprintCatchUp: false, aggressiveBlinkCatchUp: true,
                aggressiveSprintRetryDelay: 900))
            {
                if (aggressiveFollow.Current.FocusedPlayer != 1)
                    throw new Exception("Aggressive sprint options did not reach the live process.");
            }
            Console.WriteLine("Desktop sprint master switch and aggressive options reached the live process.");
            await using (var dynamicBridge = await BridgeSession.StartAsync(dynamicBridgeExe, topology.Slots, 0,
                @"HID\VID_1234&PID_5678\DEVICE", ["C:/GameA.exe", "D:/GameB.exe"], false,
                lastFocusedLeads: true))
            {
                dynamicBridge.StartReading();
                var focusDeadline = DateTime.UtcNow.AddSeconds(5);
                while (dynamicBridge.LastManualFocusedSlot != 3 && DateTime.UtcNow < focusDeadline)
                    await Task.Delay(20);
                if (dynamicBridge.LastManualFocusedSlot != 3)
                    throw new Exception("The fake bridge did not report P2 manual focus.");
                await using (var dynamicFollow = await LiveFollowSession.StartAsync(dynamicFollowExe,
                    topology, clients, dynamicBridge, plan,
                    new Dictionary<int, string> { [2] = "Second" }))
                {
                    if (dynamicFollow.Current.FocusedPlayer != 2 || dynamicFollow.Current.FollowerPad != 1)
                        throw new Exception("The desktop did not start live follow with P2 as MAIN.");
                    dynamicFollow.StartReading();
                    var commandDeadline = DateTime.UtcNow.AddSeconds(5);
                    while ((!File.Exists(dynamicLog) || File.ReadAllLines(dynamicLog).Length < 1) &&
                           DateTime.UtcNow < commandDeadline)
                        await Task.Delay(20);
                }
            }
            if (!File.Exists(dynamicLog) ||
                !File.ReadAllLines(dynamicLog).Any(line => line.Contains("\"slot\":1,\"state\":\"following\"")))
                throw new Exception("Dynamic MAIN did not route P1 movement to pad P1.");
            Console.WriteLine("P2 MAIN startup and P1 follower routing checked with local fake executables.");
            await using (var switchBridge = await BridgeSession.StartAsync(switchBridgeExe, topology.Slots, 0,
                @"HID\VID_1234&PID_5678\DEVICE", ["C:/GameA.exe", "D:/GameB.exe"], false,
                lastFocusedLeads: true))
            {
                switchBridge.StartReading();
                await using (var switchFollow = await LiveFollowSession.StartAsync(switchFollowExe,
                    topology, clients, switchBridge, plan))
                {
                    switchFollow.StartReading();
                    var switchDeadline = DateTime.UtcNow.AddSeconds(5);
                    while ((switchFollow.Current.FocusedPlayer != 1 ||
                            !File.Exists(switchLog) || File.ReadAllLines(switchLog).Count(line => line.Contains("\"state\":\"following\"")) < 3) &&
                           DateTime.UtcNow < switchDeadline)
                        await Task.Delay(20);
                }
            }
            var switchCommands = File.Exists(switchLog) ? File.ReadAllLines(switchLog) : [];
            var movementSlots = switchCommands.Where(line => line.Contains("\"state\":\"following\""))
                .Select(line => JsonDocument.Parse(line).RootElement.GetProperty("slot").GetInt32()).ToArray();
            if (!movementSlots.SequenceEqual([3, 1, 3]) ||
                !switchCommands.Any(line => line.Contains("\"slot\":3,\"state\":\"waiting\"")) ||
                !switchCommands.Any(line => line.Contains("\"slot\":1,\"state\":\"waiting\"")))
                throw new Exception("Runtime MAIN switching did not neutralize old pads and route new followers: " +
                    string.Join(" | ", switchCommands));
            Console.WriteLine("P1 to P2 to P1 live MAIN switching checked with local fake executables.");
            var tripleGame = Path.GetFullPath(Path.Combine(directory, "Client3-PathOfExile_x64.exe"));
            var tripleClients = new[]
            {
                new LaunchedClient(101, 201, "C:/GameA.exe"),
                new LaunchedClient(102, 202, "D:/GameB.exe"),
                new LaunchedClient(103, 203, tripleGame)
            };
            if (!PrivateClient3Plan.TryCreate(triple, tripleClients, out var triplePlan) || triplePlan is null)
                throw new Exception("Three single-player clients did not produce a P5 input binding.");
            await using (var tripleBridge = await BridgeSession.StartAsync(tripleBridgeExe, triple.Slots, 0,
                @"HID\VID_1234&PID_5678\DEVICE", tripleClients.Select(client => client.Executable).ToArray(),
                false, privatePlan: triplePlan, lastFocusedLeads: true))
            {
                tripleBridge.StartReading();
                await using (var tripleFollow = await LiveFollowSession.StartAsync(tripleFollowExe,
                    triple, tripleClients, tripleBridge, plan,
                    followTargets: new Dictionary<int, int> { [2] = 3 },
                    playerStops: new Dictionary<int, double> { [2] = 37.25 }))
                {
                    tripleFollow.StartReading();
                    var tripleDeadline = DateTime.UtcNow.AddSeconds(5);
                    while ((tripleFollow.Current.FocusedPlayer != 3 ||
                            !File.Exists(tripleLog) || File.ReadAllLines(tripleLog).Count(line =>
                                line.Contains("\"state\":\"following\"")) < 4) &&
                           DateTime.UtcNow < tripleDeadline)
                        await Task.Delay(20);
                }
            }
            var tripleCommands = File.Exists(tripleLog) ? File.ReadAllLines(tripleLog) : [];
            var tripleMovementPads = tripleCommands.Where(line => line.Contains("\"state\":\"following\""))
                .Select(line => JsonDocument.Parse(line).RootElement.GetProperty("slot").GetInt32()).ToArray();
            if (!tripleMovementPads.SequenceEqual([3, 5, 1, 3]) ||
                !tripleCommands.Any(line => line.Contains("\"slot\":5,\"state\":\"waiting\"")))
                throw new Exception("Three-client MAIN switch did not route P1/P3/P5 safely: " +
                    string.Join(" | ", tripleCommands));
            await using (var adoptedBridge = await BridgeSession.StartAsync(tripleBridgeExe,
                triple.Slots, 0, @"HID\VID_1234&PID_5678\DEVICE",
                tripleClients.Select(client => client.Executable).ToArray(), false,
                privatePlan: triplePlan, lastFocusedLeads: true, adoptExistingPrivate: true))
            {
                if (!adoptedBridge.IsRunning)
                    throw new Exception("Three-client bridge did not retain adopted private input.");
            }
            Console.WriteLine("Three independent clients and P5 private follow routing checked with local fake executables.");
            File.WriteAllText(tripleBridgeExe, "#!/usr/bin/env python3\nimport sys\n" +
                "if '--enable-auto-flask' not in sys.argv or '--client3-players' not in sys.argv: sys.exit(2)\n" +
                "print('{\"event\":\"ready\",\"slots\":[1,3],\"physical_mode\":true,\"pairing_protected\":true,\"hidhide_preflight\":true,\"menu_guard_enabled\":true,\"last_focused_leads\":true,\"auto_flask_enabled\":true,\"right_stick_follower_override\":false,\"direct_right_stick_slot\":0,\"focus_pad_map\":\"1:1,2:3,3:5\",\"private_client3_pid\":103,\"private_client3_window_handle\":203,\"private_client3_slots\":[5]}', flush=True)\n" +
                $"for line in sys.stdin:\n    with open({JsonSerializer.Serialize(tripleLog)}, 'a') as out: out.write(line)\n");
            File.SetUnixFileMode(tripleBridgeExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.WriteAllText(tripleFollowExe, "#!/usr/bin/env python3\nimport sys,time\n" +
                "if '-auto-flask' not in sys.argv: sys.exit(2)\n" +
                "print('{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\"},\"3\":{\"state\":\"waiting\"}},\"flasks\":[{\"player\":3,\"resource\":\"life\",\"percent\":20,\"reason\":\"threshold\"}]}', flush=True)\n" +
                "for line in sys.stdin: pass\n");
            File.SetUnixFileMode(tripleFollowExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            await using (var flaskBridge = await BridgeSession.StartAsync(tripleBridgeExe, triple.Slots, 0,
                @"HID\VID_1234&PID_5678\DEVICE", tripleClients.Select(client => client.Executable).ToArray(),
                false, autoFlaskEnabled: true, privatePlan: triplePlan, lastFocusedLeads: true))
            {
                flaskBridge.StartReading();
                await using (var flaskFollow = await LiveFollowSession.StartAsync(tripleFollowExe,
                    triple, tripleClients, flaskBridge, plan))
                {
                    flaskFollow.StartReading();
                    var flaskDeadline = DateTime.UtcNow.AddSeconds(5);
                    while (!File.ReadAllLines(tripleLog).Any(line =>
                               line.Contains("\"type\":\"flask\",\"slot\":5,\"resource\":\"life\"")) &&
                           DateTime.UtcNow < flaskDeadline)
                        await Task.Delay(20);
                }
            }
            if (!File.ReadAllLines(tripleLog).Any(line =>
                    line.Contains("\"type\":\"flask\",\"slot\":5,\"resource\":\"life\"")))
                throw new Exception("Desktop did not send a P5 flask request to the private-slot bridge.");
            Console.WriteLine("P3 flask decision-to-P5 bridge routing checked with local fake executables.");
            var couch = PartyTopologies.ForPlayerCount(2).Single(item => item.Id == "2-couch");
            await using (var pairedBridge = await BridgeSession.StartAsync(couchBridgeExe, couch.Slots, 0,
                @"HID\VID_1234&PID_5678\DEVICE", ["C:/GameA.exe"], true))
            {
                pairedBridge.StartReading();
            }
            await using (var dodgeBridge = await BridgeSession.StartAsync(couchBridgeExe, couch.Slots, 0,
                @"HID\VID_1234&PID_5678\DEVICE", ["C:/GameA.exe"], true,
                engageDodgeEnabled: true, engageDodgeBinding: "RB"))
            {
                if (!dodgeBridge.IsRunning)
                    throw new Exception("Optional right-stick dodge bridge ended during startup.");
            }
            Console.WriteLine("Same-client right-stick bridge startup arguments checked with a local fake executable.");
            var four = PartyTopologies.ForPlayerCount(4).Single();
            await using (var wgiBridge = await BridgeSession.StartAsync(wgiBridgeExe, four.Slots, -1,
                @"HID\VID_1234&PID_5678\DEVICE", ["C:/GameA.exe", "D:/GameB.exe"],
                false, useWgi: true))
            {
                wgiBridge.StartReading();
            }
            Console.WriteLine("Four public pads and WGI startup arguments checked with a local fake executable.");
            var five = PartyTopologies.ForPlayerCount(5).Single(item => item.Id == "5-main-solo");
            var thirdExecutable = Path.GetFullPath(Path.Combine(directory, "PathOfExile_x64.exe"));
            var thirdClients = new[]
            {
                new LaunchedClient(101, 201, "C:/GameA.exe"),
                new LaunchedClient(102, 202, "D:/GameB.exe"),
                new LaunchedClient(103, 203, thirdExecutable)
            };
            if (!PrivateClient3Plan.TryCreate(five, thirdClients, out var client3Plan) || client3Plan is null)
                throw new Exception("Five-player Client 3 binding was rejected.");
            await using (var compositeBridge = await BridgeSession.StartAsync(client3BridgeExe, five.Slots, 0,
                @"HID\VID_1234&PID_5678\DEVICE", ["C:/GameA.exe", "D:/GameB.exe", thirdExecutable],
                false, privatePlan: client3Plan))
            {
                compositeBridge.StartReading();
            }
            Console.WriteLine("Five-player composite bridge startup and Client 3 ready receipt checked with a local fake executable.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FakeWarpPort(params LoginWindowFrame[] captures) : IWarpInputPort
    {
        private int _next;
        internal readonly List<(int X, int Y)> RightClicks = [];
        internal readonly List<(int X, int Y)> LeftClicks = [];
        internal (int X, int Y)? RestoredCursor;
        public Task<bool> FocusAsync(CancellationToken token) => Task.FromResult(true);
        public Task<LoginWindowFrame?> CaptureAsync(CancellationToken token) =>
            Task.FromResult<LoginWindowFrame?>(_next < captures.Length ? captures[_next++] : null);
        public bool IsExact(LoginWindowFrame frame) => true;
        public bool TryGetCursor(out (int X, int Y) cursor) { cursor = (4, 5); return true; }
        public bool RightClick(int screenX, int screenY)
        { RightClicks.Add((screenX, screenY)); return true; }
        public bool Click(int screenX, int screenY)
        { LeftClicks.Add((screenX, screenY)); return true; }
        public void RestoreCursor((int X, int Y) cursor) => RestoredCursor = cursor;
    }
}
