using System.Text.Json;
using PartyPilot.Desktop;

internal static class ProcessPipelineSmoke
{
    public static async Task RunAsync()
    {
        if (OperatingSystem.IsWindows()) return; // The local fake executables use a Unix shebang.
        var directory = Path.Combine(Path.GetTempPath(), "partypilot-pipeline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var log = Path.Combine(directory, "bridge-input.jsonl");
        var bridgeExe = Path.Combine(directory, "fake-bridge");
        var followExe = Path.Combine(directory, "fake-follow");
        try
        {
            File.WriteAllText(bridgeExe, "#!/usr/bin/env python3\nimport sys\n" +
                "if '--focus-pad-map' not in sys.argv or sys.argv[sys.argv.index('--focus-pad-map')+1] != '1:1,2:3': sys.exit(2)\n" +
                "print('{\"event\":\"ready\",\"slots\":[1,3],\"physical_mode\":true,\"pairing_protected\":true,\"hidhide_preflight\":true,\"right_stick_follower_override\":false,\"focus_pad_map\":\"1:1,2:3\"}', flush=True)\n" +
                $"for line in sys.stdin:\n    with open({JsonSerializer.Serialize(log)}, 'a') as out: out.write(line)\n");
            File.WriteAllText(followExe, "#!/usr/bin/env python3\nimport time\n" +
                "print('{\"focused\":1,\"commands\":{\"2\":{\"state\":\"waiting\",\"reason\":\"missing position\"}}}', flush=True)\n" +
                "time.sleep(0.05)\n" +
                "print('{\"focused\":1,\"commands\":{\"2\":{\"state\":\"following\",\"x\":1,\"y\":0,\"manual_override_eligible\":true}}}', flush=True)\n");
            File.SetUnixFileMode(bridgeExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(followExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Console.WriteLine("Pipeline smoke: starting fake bridge.");
            var topology = PartyTopologies.ForPlayerCount(2).Single(item => item.Id == "2-split");
            await using var bridge = await BridgeSession.StartAsync(bridgeExe, topology.Slots, 0,
                @"HID\VID_1234&PID_5678\DEVICE", ["C:/GameA.exe", "D:/GameB.exe"], false);
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
                while (DateTime.UtcNow < deadline && (!File.Exists(log) || File.ReadAllLines(log).Length < 3))
                    await Task.Delay(20);
            }
            var lines = File.Exists(log) ? File.ReadAllLines(log) : [];
            if (lines.Length < 3 ||
                !lines[0].Contains("\"slot\":3,\"state\":\"waiting\"") ||
                !lines[1].Contains("\"slot\":3,\"state\":\"following\"") ||
                !lines[1].Contains("\"manual_override_eligible\":true") ||
                !lines[^1].Contains("\"slot\":3,\"state\":\"waiting\""))
                throw new Exception("Live follow did not hand off movement and neutralize on exit: " + string.Join(" | ", lines));
            Console.WriteLine("Desktop decision-to-bridge process handoff and stop checked with local fake executables.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
