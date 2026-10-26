using System.Text.Json;
using PartyPilot.Desktop;

internal static class ProcessPipelineSmoke
{
    public static async Task RunAsync()
    {
        var triple = PartyTopologies.ForPlayerCount(3)
            .Single(item => item.Id == "3-three-singles");
        var tripleFrame = LiveFollowFrame.ParseDynamic(
            "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"following\",\"x\":1,\"y\":0},\"3\":{\"state\":\"waiting\"}}}",
            triple);
        var switchedTriple = LiveFollowFrame.ParseDynamic(
            "{\"focused\":3,\"commands\":{\"1\":{\"state\":\"waiting\"},\"2\":{\"state\":\"following\",\"x\":0,\"y\":1}}}",
            triple);
        if (!tripleFrame.PadCommands.Select(command => command.Pad).SequenceEqual([3, 5]) ||
            !switchedTriple.PadCommands.Select(command => command.Pad).SequenceEqual([1, 3]))
            throw new Exception("Three-client live follow did not map independent roles to P1/P3/P5.");
        try
        {
            LiveFollowFrame.ParseDynamic(
                "{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\"},\"4\":{\"state\":\"waiting\"}}}", triple);
            throw new Exception("Three-client live follow accepted an unknown role.");
        }
        catch (InvalidDataException) { }
        if (OperatingSystem.IsWindows()) return; // The local fake executables use a Unix shebang.
        var directory = Path.Combine(Path.GetTempPath(), "partypilot-pipeline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var log = Path.Combine(directory, "bridge-input.jsonl");
        var bridgeExe = Path.Combine(directory, "fake-bridge");
        var couchBridgeExe = Path.Combine(directory, "fake-couch-bridge");
        var wgiBridgeExe = Path.Combine(directory, "fake-wgi-bridge");
        var client3BridgeExe = Path.Combine(directory, "fake-client3-bridge");
        var followExe = Path.Combine(directory, "fake-follow");
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
                "print('{\"event\":\"ready\",\"slots\":[1,3],\"physical_mode\":true,\"pairing_protected\":true,\"hidhide_preflight\":true,\"last_focused_leads\":false,\"auto_flask_enabled\":true,\"right_stick_follower_override\":false,\"direct_right_stick_slot\":0,\"focus_pad_map\":\"1:1,2:3\",\"private_client3_pid\":0,\"private_client3_window_handle\":0,\"private_client3_slots\":[]}', flush=True)\n" +
                $"for line in sys.stdin:\n    with open({JsonSerializer.Serialize(log)}, 'a') as out: out.write(line)\n");
            File.WriteAllText(followExe, "#!/usr/bin/env python3\nimport sys,json\n" +
                "if '-auto-flask' not in sys.argv: sys.exit(2)\n" +
                "print('{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\",\"reason\":\"missing position\"}}}', flush=True)\n" +
                "states = [json.loads(sys.stdin.readline()) for _ in range(2)]\n" +
                "if {(s['slot'],s['state']) for s in states} != {(1,'clear'),(2,'clear')}: sys.exit(2)\n" +
                "print('{\"focused\":1,\"commands\":{\"2\":{\"state\":\"following\",\"x\":1,\"y\":0,\"manual_override_eligible\":true}},\"flasks\":[{\"player\":2,\"resource\":\"life\",\"percent\":30,\"reason\":\"threshold\"}]}', flush=True)\n");
            File.WriteAllText(dynamicBridgeExe, "#!/usr/bin/env python3\nimport sys\n" +
                "if '--last-focused-leads' not in sys.argv: sys.exit(2)\n" +
                "print('{\"event\":\"ready\",\"slots\":[1,3],\"physical_mode\":true,\"pairing_protected\":true,\"hidhide_preflight\":true,\"last_focused_leads\":true,\"auto_flask_enabled\":false,\"right_stick_follower_override\":false,\"direct_right_stick_slot\":0,\"focus_pad_map\":\"1:1,2:3\",\"private_client3_pid\":0,\"private_client3_window_handle\":0,\"private_client3_slots\":[]}', flush=True)\n" +
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
                "print('{\"event\":\"ready\",\"slots\":[1,3],\"physical_mode\":true,\"pairing_protected\":true,\"hidhide_preflight\":true,\"last_focused_leads\":true,\"auto_flask_enabled\":false,\"right_stick_follower_override\":false,\"direct_right_stick_slot\":0,\"focus_pad_map\":\"1:1,2:3\",\"private_client3_pid\":0,\"private_client3_window_handle\":0,\"private_client3_slots\":[]}', flush=True)\n" +
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
                "print('{\"event\":\"ready\",\"slots\":[1,3],\"physical_mode\":true,\"pairing_protected\":true,\"hidhide_preflight\":true,\"last_focused_leads\":true,\"auto_flask_enabled\":false,\"right_stick_follower_override\":false,\"direct_right_stick_slot\":0,\"focus_pad_map\":\"1:1,2:3,3:5\",\"private_client3_pid\":103,\"private_client3_window_handle\":203,\"private_client3_slots\":[5]}', flush=True)\n" +
                "focused=False\n" +
                $"for line in sys.stdin:\n    with open({JsonSerializer.Serialize(tripleLog)}, 'a') as out: out.write(line)\n" +
                "    command=json.loads(line)\n" +
                "    if not focused and command.get('slot')==5 and command.get('state')=='following':\n" +
                "        print('{\"event\":\"manual_focus\",\"slot\":5}', flush=True); focused=True\n");
            File.WriteAllText(tripleFollowExe, "#!/usr/bin/env python3\nimport sys,json,time\n" +
                "if '-roster' not in sys.argv or sys.argv[sys.argv.index('-roster')+1] != '1:101,2:102,3:103': sys.exit(2)\n" +
                "if '-last-focused-leads' not in sys.argv: sys.exit(2)\n" +
                "if '-follow-target' not in sys.argv or sys.argv[sys.argv.index('-follow-target')+1] != '2=3': sys.exit(2)\n" +
                "print('{\"focused\":1,\"commands\":{\"2\":{\"state\":\"following\",\"x\":1,\"y\":0},\"3\":{\"state\":\"following\",\"x\":0,\"y\":1}}}', flush=True)\n" +
                "focus=json.loads(sys.stdin.readline())\n" +
                "if focus != {'type':'manual_focus','slot':3}: sys.exit(2)\n" +
                "print('{\"focused\":3,\"commands\":{\"1\":{\"state\":\"following\",\"x\":1,\"y\":0},\"2\":{\"state\":\"following\",\"x\":0,\"y\":1}}}', flush=True)\n" +
                "time.sleep(0.3)\n");
            File.WriteAllText(couchBridgeExe, "#!/usr/bin/env python3\nimport sys\n" +
                "if '--focus-pad-map' not in sys.argv or sys.argv[sys.argv.index('--focus-pad-map')+1] != '1:1,2:2': sys.exit(2)\n" +
                "if '--right-stick-follower-override' not in sys.argv or '--direct-right-stick-slot' not in sys.argv or sys.argv[sys.argv.index('--direct-right-stick-slot')+1] != '2': sys.exit(2)\n" +
                "print('{\"event\":\"ready\",\"slots\":[1,2],\"physical_mode\":true,\"pairing_protected\":true,\"hidhide_preflight\":true,\"last_focused_leads\":false,\"auto_flask_enabled\":false,\"right_stick_follower_override\":true,\"direct_right_stick_slot\":2,\"focus_pad_map\":\"1:1,2:2\",\"private_client3_pid\":0,\"private_client3_window_handle\":0,\"private_client3_slots\":[]}', flush=True)\n" +
                "for line in sys.stdin: pass\n");
            File.WriteAllText(wgiBridgeExe, "#!/usr/bin/env python3\nimport sys\n" +
                "if '--physical-wgi' not in sys.argv or '--physical-index' in sys.argv: sys.exit(2)\n" +
                "if '--focus-pad-map' not in sys.argv or sys.argv[sys.argv.index('--focus-pad-map')+1] != '1:1,2:2,3:3,4:4': sys.exit(2)\n" +
                "print('{\"event\":\"ready\",\"slots\":[1,2,3,4],\"physical_mode\":true,\"physical_wgi\":true,\"pairing_protected\":true,\"hidhide_preflight\":true,\"last_focused_leads\":false,\"auto_flask_enabled\":false,\"right_stick_follower_override\":false,\"direct_right_stick_slot\":0,\"focus_pad_map\":\"1:1,2:2,3:3,4:4\",\"private_client3_pid\":0,\"private_client3_window_handle\":0,\"private_client3_slots\":[]}', flush=True)\n" +
                "for line in sys.stdin: pass\n");
            File.WriteAllText(client3BridgeExe, "#!/usr/bin/env python3\nimport sys\n" +
                "def value(flag):\n    return sys.argv[sys.argv.index(flag)+1] if flag in sys.argv else None\n" +
                "if value('--slots') != '1,3,4' or value('--focus-pad-map') != '1:1,2:3,3:4,4:5,5:6': sys.exit(2)\n" +
                "if value('--client3-pid') != '103' or value('--client3-window-handle') != '203' or value('--client3-players') != '5,6': sys.exit(2)\n" +
                "games = [sys.argv[i+1] for i, arg in enumerate(sys.argv[:-1]) if arg == '--game-exe']\n" +
                "if len(games) != 3 or value('--client3-game-exe') != games[2]: sys.exit(2)\n" +
                "print('{\"event\":\"ready\",\"slots\":[1,3,4],\"physical_mode\":true,\"pairing_protected\":true,\"hidhide_preflight\":true,\"last_focused_leads\":false,\"auto_flask_enabled\":false,\"right_stick_follower_override\":false,\"direct_right_stick_slot\":0,\"focus_pad_map\":\"1:1,2:3,3:4,4:5,5:6\",\"private_client3_pid\":103,\"private_client3_window_handle\":203,\"private_client3_slots\":[5,6]}', flush=True)\n" +
                "for line in sys.stdin: pass\n");
            File.SetUnixFileMode(bridgeExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(followExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
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
            await using var bridge = await BridgeSession.StartAsync(bridgeExe, topology.Slots, 0,
                @"HID\VID_1234&PID_5678\DEVICE", ["C:/GameA.exe", "D:/GameB.exe"], false, true);
            if (!bridge.IsRunning) throw new Exception("Bridge readiness did not leave a live prelaunch process.");
            Console.WriteLine("Pipeline smoke: fake bridge ready.");
            bridge.StartReading();
            var clients = new[]
            {
                new LaunchedClient(101, 201, "C:/GameA.exe"),
                new LaunchedClient(102, 202, "D:/GameB.exe")
            };
            if (!LiveFollowPlan.TryCreate("2", "3", "10", "750ms", "30m", null, out var plan) || plan is null)
                throw new Exception("The pipeline test plan is invalid.");
            Console.WriteLine("Pipeline smoke: starting fake follow.");
            await using (var follow = await LiveFollowSession.StartAsync(followExe, topology, clients, bridge, plan))
            {
                Console.WriteLine("Pipeline smoke: fake follow ready.");
                follow.StartReading();
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (DateTime.UtcNow < deadline && (!File.Exists(log) || File.ReadAllLines(log).Length < 4))
                    await Task.Delay(20);
            }
            var lines = File.Exists(log) ? File.ReadAllLines(log) : [];
            if (lines.Length < 4 ||
                !lines[0].Contains("\"slot\":3,\"state\":\"waiting\"") ||
                !lines[1].Contains("\"slot\":3,\"state\":\"following\"") ||
                !lines[1].Contains("\"manual_override_eligible\":true") ||
                !lines[2].Contains("\"type\":\"flask\",\"slot\":3,\"resource\":\"life\"") ||
                !lines[^1].Contains("\"slot\":3,\"state\":\"waiting\""))
                throw new Exception("Live follow did not hand off movement/flasks and neutralize on exit: " + string.Join(" | ", lines));
            Console.WriteLine("Desktop decision-to-bridge movement, flask, and stop handoff checked with local fake executables.");
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
                    followTargets: new Dictionary<int, int> { [2] = 3 }))
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
            Console.WriteLine("Three independent clients and P5 private follow routing checked with local fake executables.");
            var couch = PartyTopologies.ForPlayerCount(2).Single(item => item.Id == "2-couch");
            await using (var pairedBridge = await BridgeSession.StartAsync(couchBridgeExe, couch.Slots, 0,
                @"HID\VID_1234&PID_5678\DEVICE", ["C:/GameA.exe"], true))
            {
                pairedBridge.StartReading();
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
}
