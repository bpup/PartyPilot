using System.Diagnostics;
using System.Text.Json;

namespace PartyPilot.Desktop;

// Runtime pads 5/6 belong to Client 3 even when their party player numbers differ.
public sealed record PrivateClient3Plan(int Pid, long WindowHandle, string Executable, bool IncludePad6)
{
    public static bool TryCreate(PartyTopology topology, IReadOnlyList<LaunchedClient> clients,
        out PrivateClient3Plan? plan)
    {
        plan = null;
        if (topology.ClientCount != 3 || clients.Count != 3) return false;
        var pads = topology.Slots.Where(slot => slot.Client == 3)
            .Select(slot => slot.RuntimePad).ToArray();
        if (!pads.SequenceEqual([5]) && !pads.SequenceEqual([5, 6])) return false;
        var client = clients[2];
        if (client.Pid <= 0 || client.WindowHandle <= 0 ||
            !Path.IsPathFullyQualified(client.Executable)) return false;
        plan = new PrivateClient3Plan(client.Pid, client.WindowHandle,
            Path.GetFullPath(client.Executable), pads.Length == 2);
        return true;
    }

    public bool MatchesReady(string line)
    {
        try
        {
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                   root.TryGetProperty("event", out var kind) &&
                   kind.ValueKind == JsonValueKind.String && kind.GetString() == "private_client3_ready" &&
                   root.TryGetProperty("pid", out var pid) && pid.TryGetInt32(out var actualPid) &&
                   actualPid == Pid &&
                   root.TryGetProperty("players", out var players) &&
                   players.ValueKind == JsonValueKind.Array &&
                   players.EnumerateArray().Select(item => item.GetInt32())
                       .SequenceEqual(IncludePad6 ? [5, 6] : [5]) &&
                   root.TryGetProperty("local_indices", out var indices) &&
                   indices.ValueKind == JsonValueKind.Array &&
                   indices.EnumerateArray().Select(item => item.GetInt32())
                       .SequenceEqual(IncludePad6 ? [0, 1] : [0]);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }
}

internal sealed class PrivateClient3Session : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task<string> _stderr;
    private Task _readEvents = Task.CompletedTask;
    private bool _stopping;

    public event Action<string>? Ended;

    private PrivateClient3Session(Process process, Task<string> stderr)
    {
        _process = process;
        _stderr = stderr;
    }

    public static async Task<PrivateClient3Session> StartAsync(string executable, PrivateClient3Plan plan)
    {
        if (!OperatingSystem.IsWindows() || !Path.IsPathFullyQualified(executable) ||
            !File.Exists(executable))
            throw new InvalidOperationException("Client 3 input requires the Windows bridge executable.");
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
        {
            "--attach-private-client3", "--pid", plan.Pid.ToString(),
            "--window-handle", plan.WindowHandle.ToString(), "--game-exe", plan.Executable,
            "--players", plan.IncludePad6 ? "5,6" : "5"
        }) start.ArgumentList.Add(argument);
        var process = Process.Start(start) ??
            throw new InvalidOperationException("Client 3 input bridge did not start.");
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            var ready = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(45));
            if (ready is null || !plan.MatchesReady(ready))
                throw new InvalidOperationException("Client 3 input bridge did not confirm its bound process and slots.");
            return new PrivateClient3Session(process, stderr);
        }
        catch (Exception error)
        {
            await StopProcessAsync(process);
            var detail = (await stderr).Trim();
            process.Dispose();
            if (detail.Length > 500) detail = detail[..500];
            throw new InvalidOperationException(string.IsNullOrEmpty(detail)
                ? error.Message : $"{error.Message} {detail}", error);
        }
    }

    public void StartReading()
    {
        if (_readEvents != Task.CompletedTask)
            throw new InvalidOperationException("Client 3 input events are already being read.");
        _readEvents = ReadEventsAsync();
    }

    public async Task SendAutomationAsync(string command)
    {
        if (_stopping || _process.HasExited)
            throw new InvalidOperationException("Client 3 input bridge is no longer running.");
        await _process.StandardInput.WriteLineAsync(command);
        await _process.StandardInput.FlushAsync();
    }

    private async Task ReadEventsAsync()
    {
        string? reason = null;
        try
        {
            while (await _process.StandardOutput.ReadLineAsync() is not null) { }
        }
        catch (Exception error) { reason = error.Message; }
        if (!_stopping) Ended?.Invoke(reason ?? "Client 3 input bridge exited.");
    }

    public async ValueTask DisposeAsync()
    {
        _stopping = true;
        await StopProcessAsync(_process);
        await _readEvents;
        await _stderr;
        _process.Dispose();
    }

    private static async Task StopProcessAsync(Process process)
    {
        if (process.HasExited) return;
        try { process.StandardInput.Close(); } catch (InvalidOperationException) { }
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (TimeoutException)
        {
            if (!process.HasExited) process.Kill();
            await process.WaitForExitAsync();
        }
    }
}
