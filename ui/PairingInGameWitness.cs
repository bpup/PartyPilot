using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace PartyPilot.Desktop;

internal readonly record struct PairingInGameSample(int ProcessId, DateTime SeenUtc,
    uint AreaHash, long IngameDataAddress, long PlayerAddress,
    double X, double Y, double Z, double Life, double LifeMax)
{
    internal static bool TryParse(ReadOnlySpan<byte> data, out PairingInGameSample sample)
    {
        sample = default;
        try
        {
            using var document = JsonDocument.Parse(data.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("Kind", out var kind) || kind.GetString() != "pairing_ingame" ||
                !root.TryGetProperty("ProcessId", out var pid) || !pid.TryGetInt32(out var processId) ||
                !root.TryGetProperty("SeenUtc", out var seen) || !seen.TryGetDateTime(out var seenUtc) ||
                !root.TryGetProperty("AreaHash", out var area) || !area.TryGetUInt32(out var areaHash) ||
                !root.TryGetProperty("IngameDataAddress", out var dataAddress) ||
                    !dataAddress.TryGetInt64(out var ingameDataAddress) ||
                !root.TryGetProperty("PlayerAddress", out var playerAddressValue) ||
                    !playerAddressValue.TryGetInt64(out var playerAddress) ||
                !root.TryGetProperty("X", out var xValue) || !xValue.TryGetDouble(out var x) ||
                !root.TryGetProperty("Y", out var yValue) || !yValue.TryGetDouble(out var y) ||
                !root.TryGetProperty("Z", out var zValue) || !zValue.TryGetDouble(out var z) ||
                !root.TryGetProperty("Life", out var lifeValue) || !lifeValue.TryGetDouble(out var life) ||
                !root.TryGetProperty("LifeMax", out var maxValue) || !maxValue.TryGetDouble(out var lifeMax))
                return false;
            sample = new PairingInGameSample(processId, seenUtc, areaHash,
                ingameDataAddress, playerAddress, x, y, z, life, lifeMax);
            return true;
        }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}

// A single-local-player client is witnessed only after the A release, with
// fresh render and vitals samples from the exact bound process for 250 ms.
internal sealed class PairingInGameTracker
{
    private PairingInGameSample? _first;
    private DateTime _lastSeenUtc;

    internal void Reset()
    {
        _first = null;
        _lastSeenUtc = default;
    }

    internal bool Observe(PairingInGameSample sample, int expectedPid,
        DateTime aReleasedUtc, DateTime receivedUtc)
    {
        if (sample.ProcessId != expectedPid) return false;
        if (expectedPid <= 0 || aReleasedUtc.Kind != DateTimeKind.Utc ||
            receivedUtc.Kind != DateTimeKind.Utc || sample.SeenUtc.Kind != DateTimeKind.Utc ||
            sample.SeenUtc < aReleasedUtc || sample.SeenUtc > receivedUtc ||
            receivedUtc - sample.SeenUtc > TimeSpan.FromMilliseconds(500) ||
            sample.AreaHash == 0 || sample.IngameDataAddress <= 0 || sample.PlayerAddress <= 0 ||
            !double.IsFinite(sample.X) || !double.IsFinite(sample.Y) || !double.IsFinite(sample.Z) ||
            !double.IsFinite(sample.Life) || !double.IsFinite(sample.LifeMax) ||
            sample.Life <= 0 || sample.LifeMax <= 0)
        {
            Reset();
            return false;
        }
        if (_first is not { } first || first.ProcessId != sample.ProcessId ||
            first.AreaHash != sample.AreaHash ||
            first.IngameDataAddress != sample.IngameDataAddress ||
            first.PlayerAddress != sample.PlayerAddress || sample.SeenUtc < _lastSeenUtc ||
            sample.SeenUtc - _lastSeenUtc > TimeSpan.FromMilliseconds(500))
        {
            _first = sample;
            _lastSeenUtc = sample.SeenUtc;
            return false;
        }
        _lastSeenUtc = sample.SeenUtc;
        return sample.SeenUtc - first.SeenUtc >= TimeSpan.FromMilliseconds(250);
    }
}

internal sealed class PairingInGameWitness : IAsyncDisposable
{
    private readonly UdpClient _receiver;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _reading;

    private PairingInGameWitness(UdpClient receiver,
        Action<PairingInGameSample, DateTime> onSample)
    {
        _receiver = receiver;
        _reading = ReadAsync(onSample);
    }

    internal static PairingInGameWitness? TryStart(Action<PairingInGameSample, DateTime> onSample,
        int port = 47621)
    {
        try
        {
            var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
            return new PairingInGameWitness(receiver, onSample);
        }
        catch (SocketException) { return null; }
    }

    private async Task ReadAsync(Action<PairingInGameSample, DateTime> onSample)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var datagram = await _receiver.ReceiveAsync(_stop.Token).ConfigureAwait(false);
                if (datagram.RemoteEndPoint.Address.Equals(IPAddress.Loopback) &&
                    PairingInGameSample.TryParse(datagram.Buffer, out var sample))
                    onSample(sample, DateTime.UtcNow);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { break; }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _receiver.Dispose();
        await _reading.ConfigureAwait(false);
        _stop.Dispose();
    }
}
