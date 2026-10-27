using System.Diagnostics;
using System.Text.Json;

namespace PartyPilot.Desktop;

// Retains the pre-resume private mapping until a bound bridge adopts that same mapping.
internal sealed class Client3PreResumeSession : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task<string> _stderr;
    private readonly PrivateClient3Plan _plan;

    private Client3PreResumeSession(Process process, Task<string> stderr, PrivateClient3Plan plan)
    {
        _process = process;
        _stderr = stderr;
        _plan = plan;
    }

    internal static bool TryReadLauncherNotice(string? line, string expectedExecutable, out int pid)
    {
        pid = 0;
        try
        {
            if (line is null) return false;
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("event", out var kind) ||
                kind.GetString() != "client3_pre_resume" ||
                !root.TryGetProperty("pid", out var number) ||
                !number.TryGetInt32(out pid) || pid <= 0 ||
                !root.TryGetProperty("executable", out var path) ||
                !string.Equals(Path.GetFullPath(path.GetString() ?? ""),
                    Path.GetFullPath(expectedExecutable), StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or
            InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }

    internal static async Task<Client3PreResumeSession> StartAsync(string bridgeExecutable,
        int pid, string gameExecutable, bool includeP6)
    {
        if (!OperatingSystem.IsWindows() || pid <= 0 ||
            !Path.IsPathFullyQualified(bridgeExecutable) || !File.Exists(bridgeExecutable) ||
            !Path.IsPathFullyQualified(gameExecutable) || !File.Exists(gameExecutable))
            throw new InvalidOperationException("Client 3 pre-resume input needs a bound Windows game process and bridge.");
        var plan = new PrivateClient3Plan(pid, 0, Path.GetFullPath(gameExecutable), includeP6);
        var start = new ProcessStartInfo(bridgeExecutable)
        {
            WorkingDirectory = Path.GetDirectoryName(bridgeExecutable)!,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
        {
            "--prepare-private-client3", "--pid", pid.ToString(),
            "--game-exe", plan.Executable, "--players", includeP6 ? "5,6" : "5"
        }) start.ArgumentList.Add(argument);
        var process = Process.Start(start) ??
            throw new InvalidOperationException("Client 3 pre-resume bridge did not start.");
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            var loaded = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
            if (!MatchesLoaded(loaded, plan))
                throw new InvalidOperationException("Client 3 private mapping was not loaded before resume.");
            return new Client3PreResumeSession(process, stderr, plan);
        }
        catch (Exception error)
        {
            if (!process.HasExited) process.Kill();
            await process.WaitForExitAsync();
            var diagnostic = (await stderr).Trim();
            process.Dispose();
            if (diagnostic.Length > 500) diagnostic = diagnostic[..500];
            throw new InvalidOperationException(string.IsNullOrEmpty(diagnostic)
                ? error.Message : $"{error.Message} {diagnostic}", error);
        }
    }

    internal async Task ConfirmResumeAsync()
    {
        await _process.StandardInput.WriteLineAsync($"resume {_plan.Pid}");
        await _process.StandardInput.FlushAsync();
        var ready = await _process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(40));
        if (!_plan.MatchesReady(ready ?? ""))
        {
            var diagnostic = _process.HasExited ? (await _stderr).Trim() : "";
            if (diagnostic.Length > 500) diagnostic = diagnostic[..500];
            throw new InvalidOperationException(string.IsNullOrEmpty(diagnostic)
                ? "Client 3 private XInput hooks did not confirm readiness after resume."
                : $"Client 3 private XInput hooks did not confirm readiness: {diagnostic}");
        }
    }

    internal async Task HandoffAsync()
    {
        if (_process.HasExited)
            throw new InvalidOperationException("Client 3 private input ended before handoff.");
        await _process.StandardInput.WriteLineAsync($"handoff {_plan.Pid}");
        await _process.StandardInput.FlushAsync();
        var receipt = await _process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        if (!MatchesTransfer(receipt, _plan.Pid))
            throw new InvalidOperationException("Client 3 did not confirm private mapping ownership transfer.");
        await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
        if (_process.ExitCode != 0)
            throw new InvalidOperationException("Client 3 private input bridge exited during handoff.");
    }

    internal static bool MatchesTransfer(string? receipt, int expectedPid)
    {
        try
        {
            using var json = JsonDocument.Parse(receipt ?? "");
            var root = json.RootElement;
            return expectedPid > 0 && root.TryGetProperty("event", out var kind) &&
                kind.GetString() == "private_client3_transferred" &&
                root.TryGetProperty("pid", out var pid) && pid.GetInt32() == expectedPid;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    private static bool MatchesLoaded(string? line, PrivateClient3Plan plan)
    {
        try
        {
            if (line is null) return false;
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            return root.TryGetProperty("event", out var kind) &&
                   kind.GetString() == "private_client3_loaded" &&
                   root.TryGetProperty("pid", out var pid) && pid.GetInt32() == plan.Pid &&
                   root.TryGetProperty("players", out var players) &&
                   players.EnumerateArray().Select(player => player.GetInt32())
                       .SequenceEqual(plan.IncludePad6 ? [5, 6] : [5]);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            try { _process.StandardInput.Close(); } catch (InvalidOperationException) { }
            try { await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (TimeoutException)
            {
                if (!_process.HasExited) _process.Kill();
                await _process.WaitForExitAsync();
            }
        }
        await _stderr;
        _process.Dispose();
    }
}
