using System.Text.Json;
using PartyPilot.Common;
using PartyPilot.Desktop;

internal static class HybridClient3PipelineSmoke
{
    internal static async Task RunAsync()
    {
        if (OperatingSystem.IsWindows()) return; // Fake executable uses a Unix shebang.
        await RunLayoutAsync("3-three-singles", "2:3,3:5", "5", 5);
        await RunLayoutAsync("3-three-singles", "2:3,3:5", "5", 5, adopted: true);
        await RunLayoutAsync("5-main-solo", "2:3,3:4,4:5,5:6", "5,6", 6);
        await RunLayoutAsync("6-balanced", "2:2,3:3,4:4,5:5,6:6", "5,6", 6);
    }

    private static async Task RunLayoutAsync(string id, string playerPads,
        string privatePads, int lastPad, bool adopted = false)
    {
        if (OperatingSystem.IsWindows()) return;
        var directory = Path.Combine(Path.GetTempPath(),
            "partypilot-hybrid-client3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "fake-hybrid-client3");
        var inputLog = Path.Combine(directory, "input.jsonl");
        var games = Enumerable.Range(1, 3)
            .Select(index => Path.Combine(directory, $"Game{index}.exe")).ToArray();
        var playerCount = id == "3-three-singles" ? 3 : id == "6-balanced" ? 6 : 5;
        var topology = PartyTopologies.ForPlayerCount(playerCount)
            .Single(item => item.Id == id);
        var clients = new[]
        {
            new LaunchedClient(101, 201, games[0]),
            new LaunchedClient(102, 202, games[1]),
            new LaunchedClient(103, 203, games[2])
        };
        var windowMap = string.Join(',', topology.Slots.Select(slot =>
            $"{slot.PartyPlayer}:{clients[slot.Client - 1].Pid}:{clients[slot.Client - 1].WindowHandle}"));
        try
        {
            File.WriteAllText(executable, "#!/usr/bin/env python3\nimport sys,json\n" +
                "def value(name): return sys.argv[sys.argv.index(name)+1] if name in sys.argv else None\n" +
                "if '--hybrid-keyboard' not in sys.argv or value('--slots') != '1,2,3,4': sys.exit(2)\n" +
                $"if value('--hybrid-player-pads') != {JsonSerializer.Serialize(playerPads)}: sys.exit(2)\n" +
                $"if value('--hybrid-window-map') != {JsonSerializer.Serialize(windowMap)}: sys.exit(2)\n" +
                $"if value('--client3-players') != {JsonSerializer.Serialize(privatePads)} or value('--client3-pid') != '103' or value('--client3-window-handle') != '203': sys.exit(2)\n" +
                $"if value('--client3-game-exe') != {JsonSerializer.Serialize(games[2])}: sys.exit(2)\n" +
                $"if ('--client3-adopt-existing' in sys.argv) != {(adopted ? "True" : "False")}: sys.exit(2)\n" +
                "games=[sys.argv[i+1] for i,arg in enumerate(sys.argv[:-1]) if arg=='--game-exe']\n" +
                $"if games != {JsonSerializer.Serialize(games)}: sys.exit(2)\n" +
                "print(json.dumps({'event':'ready','slots':[1,2,3,4],'physical_mode':False,'hybrid_mode':True,'menu_guard_enabled':True," +
                "'hybrid_player_pads':value('--hybrid-player-pads'),'hybrid_window_map':value('--hybrid-window-map')," +
                "'follower_mirror':value('--follower-mirror'),'private_client3_pid':103,'private_client3_window_handle':203,'private_client3_adopted':('--client3-adopt-existing' in sys.argv)," +
                $"'private_client3_slots':{JsonSerializer.Serialize(privatePads == "5" ? new[] { 5 } : new[] { 5, 6 })}}}), flush=True)\n" +
                $"for line in sys.stdin:\n    with open({JsonSerializer.Serialize(inputLog)}, 'a') as out: out.write(line)\n    message=json.loads(line)\n    if message.get('type') == 'hybrid_pairing_complete': print('{{\"event\":\"hybrid_pairing_complete\"}}', flush=True)\n    if message.get('type','').startswith('auto_party_'): print(json.dumps({{'event':'auto_party_control','type':message['type'],'slot':message.get('slot',0),'buttons':message.get('buttons',0)}}), flush=True)\n");
            File.SetUnixFileMode(executable,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            await using var bridge = await BridgeSession.StartHybridAsync(executable,
                topology.Slots, clients, followerMirror: FollowerMirrorConfig.Default,
                adoptExistingPrivate: adopted);
            bridge.StartReading();
            await bridge.EnableHybridMainFollowMirrorAsync();
            await bridge.SendAutomationAsync(JsonSerializer.Serialize(new
                { slot = lastPad, state = "following", x = 1, y = 0 }));
            if (id == "6-balanced")
                await bridge.SendAutomationAsync(JsonSerializer.Serialize(new
                    { slot = 2, state = "following", x = 1, y = 0 }));
            await bridge.BeginAutoPartyAsync(CancellationToken.None);
            await bridge.PulseAutoPartyAsync(lastPad, 0x1000, 80, CancellationToken.None);
            await bridge.EndAutoPartyAsync(CancellationToken.None);
            var lines = File.ReadAllLines(inputLog).Select(line =>
                JsonDocument.Parse(line).RootElement.Clone()).ToArray();
            if (!lines.Any(root => root.TryGetProperty("state", out var state) &&
                    state.GetString() == "following" && root.GetProperty("slot").GetInt32() == lastPad) ||
                !lines.Any(root => root.TryGetProperty("type", out var type) &&
                    type.GetString() == "auto_party_pulse" &&
                    root.GetProperty("slot").GetInt32() == lastPad) ||
                id == "6-balanced" && !lines.Any(root =>
                    root.TryGetProperty("state", out var state) && state.GetString() == "following" &&
                    root.GetProperty("slot").GetInt32() == 2))
                throw new Exception($"Hybrid {id} did not route Client 3 follow and menu input.");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }
}
