using System.Text.Json;
using PartyPilot.Desktop;

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
        var inputLog = Path.Combine(directory, "input.jsonl");
        var gameA = Path.Combine(directory, "GameA.exe");
        var gameB = Path.Combine(directory, "GameB.exe");
        try
        {
            File.WriteAllText(executable, "#!/usr/bin/env python3\nimport sys,json\n" +
                "def value(name): return sys.argv[sys.argv.index(name)+1] if name in sys.argv else None\n" +
                "if '--hybrid-keyboard' not in sys.argv or value('--slots') != '3': sys.exit(2)\n" +
                "if value('--hybrid-player-pads') != '2:3' or value('--hybrid-window-map') != '1:101:201,2:102:202': sys.exit(2)\n" +
                "games=[sys.argv[i+1] for i,arg in enumerate(sys.argv[:-1]) if arg=='--game-exe']\n" +
                $"if games != [{JsonSerializer.Serialize(gameA)},{JsonSerializer.Serialize(gameB)}]: sys.exit(2)\n" +
                "print('{\"event\":\"ready\",\"slots\":[3],\"physical_mode\":false,\"hybrid_mode\":true,\"hybrid_player_pads\":\"2:3\",\"hybrid_window_map\":\"1:101:201,2:102:202\"}', flush=True)\n" +
                $"for line in sys.stdin:\n    with open({JsonSerializer.Serialize(inputLog)}, 'a') as out: out.write(line)\n");
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var topology = PartyTopologies.ForPlayerCount(2).Single(item => item.Id == "2-split");
            await using var bridge = await BridgeSession.StartHybridAsync(executable, topology.Slots,
            [
                new LaunchedClient(101, 201, gameA),
                new LaunchedClient(102, 202, gameB)
            ]);
            if (!bridge.IsHybridMode || !bridge.IsRunning)
                throw new Exception("Desktop rejected a ready keyboard bridge.");
            Exception? sendError = null;
            await using var input = new HybridKeyEventPublisher(bridge.SendHybridKeyAsync,
                error => sendError = error);
            if (!input.TryPublish(0x57, true) || !input.TryPublish(0x57, false))
                throw new Exception("Desktop could not queue keyboard events for the bridge.");
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < deadline && (!File.Exists(inputLog) || File.ReadAllLines(inputLog).Length < 2))
                await Task.Delay(20);
            if (sendError is not null || !File.Exists(inputLog) || File.ReadAllLines(inputLog).Length != 2 ||
                File.ReadAllLines(inputLog).Select(line => JsonDocument.Parse(line).RootElement.GetProperty("down").GetBoolean())
                    .SequenceEqual([true, false]) == false)
                throw new Exception("Keyboard bridge did not receive ordered key down/up events.");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }
}
