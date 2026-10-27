using System.Text.Json;
using PartyPilot.Desktop;
using PartyPilot.Common;

internal static class HybridBridgePipelineSmoke
{
    internal static async Task RunAsync()
    {
        var sent = new List<(int Key, bool Down)>();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (var publisher = new HybridKeyEventPublisher((key, down) =>
        {
            sent.Add((key, down));
            if (sent.Count == 2) completion.TrySetResult();
            return Task.CompletedTask;
        }, error => completion.TrySetException(error)))
        {
            if (!publisher.TryPublish(0x57, true) || !publisher.TryPublish(0x57, false))
                throw new Exception("Keyboard event publisher rejected two ordinary key events.");
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        if (!sent.SequenceEqual([(0x57, true), (0x57, false)]))
            throw new Exception("Keyboard event publisher reordered key down/up events.");
        if (OperatingSystem.IsWindows()) return; // This fake executable uses a Unix shebang.
        var directory = Path.Combine(Path.GetTempPath(), "partypilot-hybrid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "fake-hybrid-bridge");
        var followExecutable = Path.Combine(directory, "fake-hybrid-follow");
        var inputLog = Path.Combine(directory, "input.jsonl");
        var gameA = Path.Combine(directory, "GameA.exe");
        var gameB = Path.Combine(directory, "GameB.exe");
        try
        {
            File.WriteAllText(executable, "#!/usr/bin/env python3\nimport sys,json\n" +
                "def value(name): return sys.argv[sys.argv.index(name)+1] if name in sys.argv else None\n" +
                "if '--hybrid-keyboard' not in sys.argv or value('--slots') != '1,2': sys.exit(2)\n" +
                "if '--disable-menu-guard' not in sys.argv: sys.exit(2)\n" +
                "if value('--hybrid-player-pads') != '2:2' or value('--hybrid-window-map') != '1:101:201,2:102:202': sys.exit(2)\n" +
                "games=[sys.argv[i+1] for i,arg in enumerate(sys.argv[:-1]) if arg=='--game-exe']\n" +
                $"if games != [{JsonSerializer.Serialize(gameA)},{JsonSerializer.Serialize(gameB)}]: sys.exit(2)\n" +
                "keys=value('--hybrid-binding-keys')\nif keys is None: sys.exit(2)\n" +
                "print(json.dumps({'event':'ready','slots':[1,2],'physical_mode':False,'hybrid_mode':True,'menu_guard_enabled':False,'hybrid_player_pads':'2:2','hybrid_window_map':'1:101:201,2:102:202','hybrid_binding_keys':keys}), flush=True)\n" +
                $"for line in sys.stdin:\n    with open({JsonSerializer.Serialize(inputLog)}, 'a') as out: out.write(line)\n    message=json.loads(line)\n    if message.get('type') == 'hybrid_pairing_complete': print('{{\"event\":\"hybrid_pairing_complete\"}}', flush=True)\n    if message.get('type','').startswith('auto_party_'): print(json.dumps({{'event':'auto_party_control','type':message['type'],'slot':message.get('slot',0),'buttons':message.get('buttons',0)}}), flush=True)\n");
            File.WriteAllText(followExecutable, "#!/usr/bin/env python3\nimport sys\n" +
                "if '-roster' not in sys.argv or sys.argv[sys.argv.index('-roster')+1] != '1:101,2:102': sys.exit(2)\n" +
                "print('{\"focused\":1,\"commands\":{\"2\":{\"state\":\"following\",\"x\":1,\"y\":0}}}', flush=True)\n" +
                "for line in sys.stdin: pass\n");
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(followExecutable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var topology = PartyTopologies.ForPlayerCount(2).Single(item => item.Id == "2-split");
            var clients = new[]
            {
                new LaunchedClient(101, 201, gameA),
                new LaunchedClient(102, 202, gameB)
            };
            var customKeys = HybridBindingConfig.Definitions.Select(item => item.DefaultKey).ToArray();
            customKeys[6] = 73;
            if (!HybridBindingConfig.TryCreate(customKeys,
                    [new HybridCustomAction("额外确认", "", 70, 11)], out var bindings) || bindings is null)
                throw new Exception("Fake bridge could not create a custom keyboard map.");
            await using var bridge = await BridgeSession.StartHybridAsync(executable, topology.Slots,
                clients, bindings, menuGuardEnabled: false);
            if (!bridge.IsHybridMode || !bridge.IsRunning)
                throw new Exception("Desktop rejected a ready keyboard bridge.");
            bridge.StartReading();
            Exception? sendError = null;
            await using var input = new HybridKeyEventPublisher(bridge.SendHybridKeyAsync,
                error => sendError = error);
            if (!input.TryPublish(0x49, true) || !input.TryPublish(0x49, false))
                throw new Exception("Desktop could not queue keyboard events for the bridge.");
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < deadline && (!File.Exists(inputLog) || File.ReadAllLines(inputLog).Length < 2))
                await Task.Delay(20);
            if (sendError is not null || !File.Exists(inputLog) || File.ReadAllLines(inputLog).Length != 2 ||
                File.ReadAllLines(inputLog).Select(line => JsonDocument.Parse(line).RootElement.GetProperty("down").GetBoolean())
                    .SequenceEqual([true, false]) == false ||
                File.ReadAllLines(inputLog).Any(line => JsonDocument.Parse(line).RootElement.GetProperty("key").GetInt32() != 73))
                throw new Exception("Keyboard bridge did not receive ordered key down/up events.");
            await bridge.EnableHybridMainFollowMirrorAsync();
            if (!File.ReadAllLines(inputLog).Any(line =>
                    JsonDocument.Parse(line).RootElement.TryGetProperty("type", out var type) &&
                    type.GetString() == "hybrid_pairing_complete"))
                throw new Exception("Desktop did not wait for the bridge pairing completion acknowledgement.");
            if (!LiveFollowPlan.TryCreate("2", "10", "750ms", "30m", null, out var plan) || plan is null)
                throw new Exception("Keyboard follow plan was invalid.");
            await using (var follow = await LiveFollowSession.StartAsync(followExecutable,
                topology, clients, bridge, plan))
            {
                if (follow.Current.FollowerPad != 3 || follow.Current.Status != "following")
                    throw new Exception("Keyboard bridge did not accept a live follower decision.");
                while (DateTime.UtcNow < deadline && File.ReadAllLines(inputLog).Length < 4)
                    await Task.Delay(20);
                var commands = File.ReadAllLines(inputLog);
                if (commands.Length < 4 ||
                    JsonDocument.Parse(commands[3]).RootElement.GetProperty("slot").GetInt32() != 2)
                    throw new Exception("Live follow did not route P2 movement to the keyboard virtual pad.");
            }
            await bridge.BeginAutoPartyAsync(CancellationToken.None);
            await bridge.PulseAutoPartyAsync(1, 0x2000, 60, CancellationToken.None);
            await bridge.PulseAutoPartyAsync(3, 0x1000, 80, CancellationToken.None);
            await bridge.EndAutoPartyAsync(CancellationToken.None);
            if (!File.ReadAllLines(inputLog).Any(line =>
                    JsonDocument.Parse(line).RootElement.TryGetProperty("type", out var type) &&
                    type.GetString() == "auto_party_pulse" &&
                    JsonDocument.Parse(line).RootElement.GetProperty("slot").GetInt32() == 2))
                throw new Exception("Hybrid Auto Party did not translate the two-player follower pad.");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }
}
