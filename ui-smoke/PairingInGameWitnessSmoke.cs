using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using PartyPilot.Desktop;

internal static class PairingInGameWitnessSmoke
{
    public static async Task RunAsync()
    {
        var released = new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc);
        var wire = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Kind = "pairing_ingame", ProcessId = 202, SeenUtc = released.AddMilliseconds(10),
            AreaHash = 123u, IngameDataAddress = 456L, PlayerAddress = 789L,
            X = 10.0, Y = 20.0, Z = 0.0, Life = 90.0, LifeMax = 100.0
        });
        if (!PairingInGameSample.TryParse(wire, out var first) ||
            PairingInGameSample.TryParse("{\"Kind\":\"other\"}"u8, out _))
            throw new Exception("Bound in-game proof wire was not parsed strictly.");
        var tracker = new PairingInGameTracker();
        if (tracker.Observe(first, 202, released, released.AddMilliseconds(20)) ||
            tracker.Observe(first with { ProcessId = 303 }, 202, released,
                released.AddMilliseconds(100)) ||
            !tracker.Observe(first with { SeenUtc = released.AddMilliseconds(270) },
                202, released, released.AddMilliseconds(280)))
            throw new Exception("Two fresh samples from the same bound client were not required.");
        tracker.Reset();
        if (tracker.Observe(first with { SeenUtc = released.AddMilliseconds(-1) },
                202, released, released.AddMilliseconds(20)) ||
            tracker.Observe(first, 202, released, released.AddMilliseconds(20)) ||
            tracker.Observe(first with { AreaHash = 124, SeenUtc = released.AddMilliseconds(270) },
                202, released, released.AddMilliseconds(280)) ||
            tracker.Observe(first with { AreaHash = 124, Life = 0,
                SeenUtc = released.AddMilliseconds(550) },
                202, released, released.AddMilliseconds(560)))
            throw new Exception("Old, area-changing, or dead-player proof advanced pairing.");
        tracker.Reset();
        if (tracker.Observe(first, 202, released, released.AddMilliseconds(20)) ||
            tracker.Observe(first with { SeenUtc = released.AddSeconds(2) },
                202, released, released.AddSeconds(2).AddMilliseconds(10)))
            throw new Exception("A long sensor gap was treated as continuous in-game proof.");

        int port;
        using (var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            port = ((IPEndPoint)reservation.Client.LocalEndPoint!).Port;
        var received = new TaskCompletionSource<PairingInGameSample>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var witness = PairingInGameWitness.TryStart((sample, _) =>
            received.TrySetResult(sample), port) ??
            throw new Exception("Pairing witness could not bind the loopback test port.");
        using var sender = new UdpClient();
        await sender.SendAsync(wire, wire.Length, new IPEndPoint(IPAddress.Loopback, port));
        if (await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(2))) != received.Task ||
            received.Task.Result.ProcessId != 202)
            throw new Exception("Pairing witness did not receive its loopback sensor sample.");
    }
}
