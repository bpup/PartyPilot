using System.Text;
using System.Text.RegularExpressions;

namespace PartyPilot.Desktop;

internal enum PartyLeaveKind { MemberLeft, LocalPlayerLeft, PartyDisbanded }
internal sealed record PartyLeaveSignal(PartyLeaveKind Kind, string? MemberName, int Client);

internal static class PartyLeaveRouting
{
    internal static int ResolveTarget(PartyLeaveSignal signal, PartyTopology topology,
        IReadOnlyList<string?> playerNames)
    {
        if (signal.Client < 1 || signal.Client > topology.ClientCount || playerNames.Count != topology.PlayerCount)
            return -1;
        if (signal.Kind == PartyLeaveKind.PartyDisbanded) return 0;
        if (signal.Kind == PartyLeaveKind.LocalPlayerLeft) return signal.Client;
        if (signal.Kind != PartyLeaveKind.MemberLeft || string.IsNullOrWhiteSpace(signal.MemberName)) return -1;
        for (var client = 1; client <= topology.ClientCount; client++)
            if (topology.Clients[client - 1].Any(player => string.Equals(
                playerNames[player - 1]?.Trim(), signal.MemberName, StringComparison.OrdinalIgnoreCase)))
                return client;
        return -1;
    }
}

internal static partial class PartyLeaveLogParser
{
    [GeneratedRegex(@"(?<name>[^:]+?)\s+has\s+left\s+the\s+party\.?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MemberLeft();
    [GeneratedRegex(@"(?<name>[^:]+?)\s+(?:has\s+been\s+kicked|was\s+removed)\s+from\s+the\s+party\.?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MemberRemoved();

    internal static PartyLeaveSignal? Parse(string? line, int client)
    {
        if (string.IsNullOrWhiteSpace(line) || client is < 1 or > 3) return null;
        var text = line.Trim();
        if (text.Contains("left the area", StringComparison.OrdinalIgnoreCase)) return null;
        if (text.Contains("the party has been disbanded", StringComparison.OrdinalIgnoreCase))
            return new(PartyLeaveKind.PartyDisbanded, null, client);
        if (text.Contains("you have left the party", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("you have been kicked from the party", StringComparison.OrdinalIgnoreCase))
            return new(PartyLeaveKind.LocalPlayerLeft, null, client);
        var match = MemberLeft().Match(text);
        if (!match.Success) match = MemberRemoved().Match(text);
        if (!match.Success) return null;
        var name = match.Groups["name"].Value.Trim();
        return name.Length == 0 ? null : new(PartyLeaveKind.MemberLeft, name, client);
    }
}

internal sealed class PartyLeaveLogMonitor : IDisposable
{
    private sealed class Tail(int client, string path, long offset)
    {
        internal int Client { get; } = client;
        internal string Path { get; } = path;
        internal long Offset = offset;
        internal List<byte> Pending { get; } = [];
        internal bool DiscardLine;
    }

    private readonly Tail[] _tails;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    internal event Action<PartyLeaveSignal>? Signal;
    internal event Action<string>? Error;

    internal PartyLeaveLogMonitor(IReadOnlyList<LaunchedClient> clients)
    {
        if (clients.Count is < 2 or > 3) throw new ArgumentException("Two or three bound clients are required.");
        var paths = clients.Select(client => Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(client.Executable) ?? string.Empty, "logs", "Client.txt"))).ToArray();
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length ||
            paths.Any(path => !File.Exists(path)))
            throw new InvalidOperationException("Every client needs a separate, existing logs/Client.txt file.");
        _tails = paths.Select((path, index) => new Tail(index + 1, path, new FileInfo(path).Length)).ToArray();
        _loop = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            foreach (var tail in _tails)
            {
                if (_stop.IsCancellationRequested) break;
                try { ReadTail(tail); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    Error?.Invoke($"客户端 {tail.Client} 日志读取失败：{error.Message}");
                }
            }
            try { await Task.Delay(250, _stop.Token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void ReadTail(Tail tail)
    {
        using var stream = new FileStream(tail.Path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length < tail.Offset)
        {
            tail.Offset = stream.Length;
            tail.Pending.Clear();
            tail.DiscardLine = false;
            return;
        }
        if (stream.Length == tail.Offset) return;
        stream.Seek(tail.Offset, SeekOrigin.Begin);
        var buffer = new byte[8192];
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            tail.Offset += read;
            for (var index = 0; index < read; index++)
            {
                var value = buffer[index];
                if (value == (byte)'\n')
                {
                    var line = tail.DiscardLine ? null : Encoding.UTF8.GetString(tail.Pending.ToArray()).TrimEnd('\r');
                    tail.Pending.Clear();
                    tail.DiscardLine = false;
                    var signal = PartyLeaveLogParser.Parse(line, tail.Client);
                    if (signal is not null) Signal?.Invoke(signal);
                }
                else if (!tail.DiscardLine && tail.Pending.Count < 65536) tail.Pending.Add(value);
                else { tail.Pending.Clear(); tail.DiscardLine = true; }
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _ = _loop.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
    }
}
