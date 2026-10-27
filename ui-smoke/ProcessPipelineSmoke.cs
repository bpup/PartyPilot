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
                "if '-classic-sprint-catchup' in sys.argv: sys.exit(2)\n" +
                "print('{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\",\"reason\":\"missing position\"}}}', flush=True)\n" +
                "states = [json.loads(sys.stdin.readline()) for _ in range(2)]\n" +
                "if {(s['slot'],s['state']) for s in states} != {(1,'clear'),(2,'clear')}: sys.exit(2)\n" +
                "print('{\"focused\":1,\"commands\":{\"2\":{\"state\":\"catching_up\",\"x\":1,\"y\":0,\"manual_override_eligible\":true,\"sprint_down\":true}},\"flasks\":[{\"player\":2,\"resource\":\"life\",\"percent\":30,\"reason\":\"threshold\"}]}', flush=True)\n" +
                "for line in sys.stdin:\n    control=json.loads(line)\n    if control.get('type')!='follow_pause': sys.exit(2)\n    if control.get('state')=='paused': print('{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\",\"reason\":\"follow paused\"}}}', flush=True)\n    elif control.get('state')=='following': print('{\"focused\":1,\"commands\":{\"2\":{\"state\":\"following\",\"x\":1,\"y\":0}}}', flush=True)\n    else: sys.exit(2)\n");
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
            await using (var follow = await LiveFollowSession.StartAsync(followExe, topology, clients, bridge, plan))
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
}
