using System.Text.Json;
using PartyPilot.Common;
using PartyPilot.Desktop;

internal static class HybridPrelaunchSmoke
{
    internal static async Task RunAsync()
    {
        var receipt = JsonSerializer.Serialize(new
        {
            @event = "prelaunch_ready", slots = new[] { 1, 2 }, hybrid_mode = true,
            physical_mode = false, hybrid_player_pads = "2:2", hybrid_binding_keys = "keys",
            menu_guard_enabled = true, follower_mirror = "mirror"
        });
        if (!BridgeSession.IsExpectedHybridPrelaunch(receipt, [1, 2], "2:2",
                "keys", true, "mirror") ||
            BridgeSession.IsExpectedHybridPrelaunch(receipt.Replace("prelaunch_ready", "ready"),
                [1, 2], "2:2", "keys", true, "mirror") ||
            BridgeSession.IsExpectedHybridPrelaunch(receipt.Replace("2:2", "2:3"),
                [1, 2], "2:2", "keys", true, "mirror"))
            throw new Exception("Desktop did not distinguish prelaunch pads from bound game windows.");
        if (OperatingSystem.IsWindows()) return;
        var directory = Path.Combine(Path.GetTempPath(), "partypilot-prelaunch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var bridgePath = Path.Combine(directory, "fake-bridge");
            var gameA = Path.Combine(directory, "GameA.exe");
            var gameB = Path.Combine(directory, "GameB.exe");
            var logPath = Path.Combine(directory, "commands.jsonl");
            File.WriteAllText(gameA, "fake");
            File.WriteAllText(gameB, "fake");
            File.WriteAllText(bridgePath, "#!/usr/bin/env python3\nimport sys,json\n" +
                "def value(name): return sys.argv[sys.argv.index(name)+1] if name in sys.argv else None\n" +
                "if '--hybrid-prelaunch' not in sys.argv or value('--slots') != '1,2' or value('--hybrid-player-pads') != '2:2' or '--hybrid-window-map' in sys.argv: sys.exit(2)\n" +
                "print(json.dumps({'event':'prelaunch_ready','slots':[1,2],'hybrid_mode':True,'physical_mode':False,'hybrid_player_pads':'2:2','hybrid_binding_keys':value('--hybrid-binding-keys'),'menu_guard_enabled':True,'follower_mirror':value('--follower-mirror')}),flush=True)\n" +
                "line=sys.stdin.readline()\n" +
                $"with open({JsonSerializer.Serialize(logPath)},'a') as out: out.write(line)\n" +
                "request=json.loads(line)\n" +
                "if request.get('type') != 'bind_hybrid' or request.get('window_map') != '1:101:201,2:102:202' or request.get('player_pads') != '2:2': sys.exit(3)\n" +
                "print(json.dumps({'event':'ready','slots':[1,2],'physical_mode':False,'hybrid_mode':True,'menu_guard_enabled':True,'hybrid_player_pads':'2:2','hybrid_window_map':request['window_map'],'hybrid_binding_keys':value('--hybrid-binding-keys'),'follower_mirror':value('--follower-mirror')}),flush=True)\n" +
                $"for line in sys.stdin:\n    with open({JsonSerializer.Serialize(logPath)},'a') as out: out.write(line)\n");
            File.SetUnixFileMode(bridgePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var topology = PartyTopologies.ForPlayerCount(2).Single(item => item.Id == "2-split");
            var bindings = HybridBindingConfig.Default;
            await using var bridge = await BridgeSession.StartHybridPrelaunchAsync(bridgePath,
                topology.Slots, [gameA, gameB], bindings, true, FollowerMirrorConfig.Default);
            if (File.Exists(logPath))
                throw new Exception("Prelaunch sent a game-window command before the games were bound.");
            await bridge.BindHybridAsync(topology.Slots,
                [new LaunchedClient(101, 201, gameA), new LaunchedClient(102, 202, gameB)],
                bindings, true, FollowerMirrorConfig.Default);
            bridge.StartReading();
            await bridge.SendHybridKeyAsync(87, true);
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < deadline &&
                   (!File.Exists(logPath) || File.ReadAllLines(logPath).Length < 2))
                await Task.Delay(20);
            if (!File.Exists(logPath) || File.ReadAllLines(logPath).Length != 2 ||
                JsonDocument.Parse(File.ReadAllLines(logPath)[0]).RootElement.GetProperty("type").GetString() != "bind_hybrid" ||
                JsonDocument.Parse(File.ReadAllLines(logPath)[1]).RootElement.GetProperty("type").GetString() != "hybrid_key")
                throw new Exception("Keyboard prelaunch did not preserve a single bridge across binding and input.");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
        await VerifySixPrelaunchAsync();
    }

    private static async Task VerifySixPrelaunchAsync()
    {
        if (OperatingSystem.IsWindows()) return;
        var directory = Path.Combine(Path.GetTempPath(),
            "partypilot-six-prelaunch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var bridgePath = Path.Combine(directory, "fake-bridge");
            var games = Enumerable.Range(1, 3)
                .Select(index => Path.Combine(directory, $"Game{index}.exe")).ToArray();
            foreach (var game in games) File.WriteAllText(game, "fake");
            File.WriteAllText(bridgePath, "#!/usr/bin/env python3\nimport sys,json\n" +
                "def value(name): return sys.argv[sys.argv.index(name)+1] if name in sys.argv else None\n" +
                "games=[sys.argv[i+1] for i,arg in enumerate(sys.argv[:-1]) if arg=='--game-exe']\n" +
                $"if games != {JsonSerializer.Serialize(games)} or value('--slots') != '1,2,3,4' or value('--hybrid-player-pads') != '2:2,3:3,4:4,5:5,6:6' or '--hybrid-window-map' in sys.argv: sys.exit(2)\n" +
                "print(json.dumps({'event':'prelaunch_ready','slots':[1,2,3,4],'hybrid_mode':True,'physical_mode':False,'hybrid_player_pads':value('--hybrid-player-pads'),'hybrid_binding_keys':value('--hybrid-binding-keys'),'menu_guard_enabled':True,'follower_mirror':value('--follower-mirror')}),flush=True)\n" +
                "for line in sys.stdin: pass\n");
            File.SetUnixFileMode(bridgePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var topology = PartyTopologies.ForPlayerCount(6)
                .Single(item => item.Id == "6-balanced");
            await using var bridge = await BridgeSession.StartHybridPrelaunchAsync(bridgePath,
                topology.Slots, games, HybridBindingConfig.Default, true,
                FollowerMirrorConfig.Default);
            if (!bridge.IsHybridMode || !bridge.IsRunning)
                throw new Exception("Six-player keyboard prelaunch did not keep its virtual pads ready.");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }
}
