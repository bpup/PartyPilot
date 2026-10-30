using System.Diagnostics;
using System.Text.Json;

namespace PartyPilot.Desktop;

// Keeps one process-local mapping alive until the main input bridge adopts it.
internal sealed class PrivateClientPreResumeSession : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task<string> _stderr;
    private readonly int _client;
    private readonly int _pid;
    private readonly int[] _slots;

    private PrivateClientPreResumeSession(Process process, Task<string> stderr,
        int client, int pid, int[] slots)
    {
        _process = process;
        _stderr = stderr;
        _client = client;
        _pid = pid;
        _slots = slots;
    }

    internal int Client => _client;

    internal static bool TryReadLauncherNotice(string? line, int expectedClient,
        string expectedExecutable, out int pid)
    {
        pid = 0;
        try
        {
            using var json = JsonDocument.Parse(line ?? "");
            var root = json.RootElement;
            return expectedClient is >= 1 and <= 3 &&
                root.TryGetProperty("event", out var kind) &&
                kind.GetString() == "private_client_pre_resume" &&
                root.TryGetProperty("client", out var client) &&
                client.TryGetInt32(out var actualClient) && actualClient == expectedClient &&
                root.TryGetProperty("pid", out var process) &&
                process.TryGetInt32(out pid) && pid > 0 &&
                root.TryGetProperty("executable", out var executable) &&
                string.Equals(Path.GetFullPath(executable.GetString() ?? ""),
                    Path.GetFullPath(expectedExecutable), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is JsonException or ArgumentException or
            InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }

    internal static async Task<PrivateClientPreResumeSession> StartAsync(string bridgeExecutable,
        int client, int pid, string gameExecutable, int[] slots)
    {
        var first = client * 2 - 1;
        if (!OperatingSystem.IsWindows() || client is < 1 or > 3 || pid <= 0 ||
            !Path.IsPathFullyQualified(bridgeExecutable) || !File.Exists(bridgeExecutable) ||
            !Path.IsPathFullyQualified(gameExecutable) || !File.Exists(gameExecutable) ||
            !(slots.SequenceEqual([first]) || slots.SequenceEqual([first, first + 1])))
            throw new InvalidOperationException("启动前输入需要已核对的客户端和游戏进程。");
        var start = new ProcessStartInfo(bridgeExecutable)
        {
            WorkingDirectory = Path.GetDirectoryName(bridgeExecutable)!,
            UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
        {
            "--prepare-private-client", "--client", client.ToString(),
            "--pid", pid.ToString(), "--game-exe", Path.GetFullPath(gameExecutable),
            "--slots", string.Join(',', slots)
        }) start.ArgumentList.Add(argument);
        var process = Process.Start(start) ??
            throw new InvalidOperationException("客户端私有输入桥未启动。");
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            var loaded = await process.StandardOutput.ReadLineAsync()
                .WaitAsync(TimeSpan.FromSeconds(15));
            if (!Matches(loaded, "private_client_loaded", client, pid, slots))
                throw new InvalidOperationException($"客户端 {client} 的私有输入未在游戏恢复前加载。");
            return new PrivateClientPreResumeSession(process, stderr, client, pid, slots);
        }
        catch (Exception error)
        {
            await StopAsync(process);
            var detail = (await stderr).Trim();
            process.Dispose();
            if (detail.Length > 500) detail = detail[..500];
            throw new InvalidOperationException(string.IsNullOrEmpty(detail)
                ? error.Message : $"{error.Message} {detail}", error);
        }
    }

    internal async Task ConfirmResumeAsync()
    {
        await _process.StandardInput.WriteLineAsync($"resume {_client} {_pid}");
        await _process.StandardInput.FlushAsync();
        var ready = await _process.StandardOutput.ReadLineAsync()
            .WaitAsync(TimeSpan.FromSeconds(40));
        if (!Matches(ready, "private_client_ready", _client, _pid, _slots,
                checkIndices: true))
            throw new InvalidOperationException($"客户端 {_client} 的输入未在游戏恢复后就绪。");
    }

    internal async Task HandoffAsync()
    {
        if (_process.HasExited)
            throw new InvalidOperationException($"客户端 {_client} 的私有输入已提前退出。");
        await _process.StandardInput.WriteLineAsync($"handoff {_client} {_pid}");
        await _process.StandardInput.FlushAsync();
        var receipt = await _process.StandardOutput.ReadLineAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));
        if (!Matches(receipt, "private_client_transferred", _client, _pid, null))
            throw new InvalidOperationException($"客户端 {_client} 的私有输入未完成移交。");
        await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
        if (_process.ExitCode != 0)
            throw new InvalidOperationException($"客户端 {_client} 的私有输入移交失败。");
    }

    internal static bool Matches(string? line, string expectedEvent, int client,
        int pid, int[]? slots, bool checkIndices = false)
    {
        try
        {
            using var json = JsonDocument.Parse(line ?? "");
            var root = json.RootElement;
            return root.TryGetProperty("event", out var kind) &&
                kind.GetString() == expectedEvent &&
                root.TryGetProperty("client", out var boundClient) &&
                boundClient.TryGetInt32(out var number) && number == client &&
                root.TryGetProperty("pid", out var process) &&
                process.TryGetInt32(out var boundPid) && boundPid == pid &&
                (slots is null || root.TryGetProperty("slots", out var boundSlots) &&
                    boundSlots.ValueKind == JsonValueKind.Array &&
                    boundSlots.EnumerateArray().Select(slot => slot.GetInt32())
                        .SequenceEqual(slots)) &&
                (!checkIndices || root.TryGetProperty("local_indices", out var indices) &&
                    indices.ValueKind == JsonValueKind.Array &&
                    indices.EnumerateArray().Select(index => index.GetInt32())
                        .SequenceEqual(slots!.Length == 2 ? [0, 1] : [0]));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or
            FormatException)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(_process);
        await _stderr;
        _process.Dispose();
    }

    private static async Task StopAsync(Process process)
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
