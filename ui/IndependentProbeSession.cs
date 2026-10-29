using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace PartyPilot.Desktop;

// Owns the optional standalone position producer for a live follow session.
internal sealed class IndependentProbeSession : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task _output;
    private readonly Task<string> _error;
    private readonly ConcurrentDictionary<int, bool> _recognizedBuilds = new();

    internal event Action? DiagnosticsChanged;

    private IndependentProbeSession(Process process)
    {
        _process = process;
        _output = DrainAsync(process.StandardOutput);
        _error = ReadErrorAsync(process.StandardError);
    }

    internal bool IsRunning => !_process.HasExited;

    internal bool HasUnknownBuild(int processId) =>
        _recognizedBuilds.TryGetValue(processId, out var recognized) && !recognized;

    internal static string? FindExecutable()
    {
        const string file = "PartyPilot.Probe.exe";
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "probe", file),
            Path.Combine(AppContext.BaseDirectory, "probe", file),
            Path.Combine(Environment.CurrentDirectory, "release", "probe", file)
        };
        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
    }

    internal static async Task<IndependentProbeSession> StartAsync(string executable)
    {
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable))
            throw new FileNotFoundException("独立位置探针不存在。", executable);
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("--watch");
        start.ArgumentList.Add("--experimental-ordered-udp");
        var process = Process.Start(start) ??
            throw new InvalidOperationException("独立位置探针未能启动。");
        var session = new IndependentProbeSession(process);
        await Task.Delay(150);
        if (process.HasExited)
        {
            var detail = await session._error;
            await session.DisposeAsync();
            throw new InvalidOperationException("独立位置探针提前退出：" + detail.Trim());
        }
        return session;
    }

    private async Task DrainAsync(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            if (line.Length > 4096) continue;
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("pid", out var pid) &&
                    pid.ValueKind == JsonValueKind.Number &&
                    pid.TryGetInt32(out var processId) && processId > 0 &&
                    root.TryGetProperty("recognizedBuild", out var build) &&
                    build.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    var recognized = build.GetBoolean();
                    var changed = !_recognizedBuilds.TryGetValue(processId, out var prior) ||
                        prior != recognized;
                    _recognizedBuilds[processId] = recognized;
                    if (changed) DiagnosticsChanged?.Invoke();
                }
            }
            catch (JsonException) { }
        }
        DiagnosticsChanged?.Invoke();
    }

    private static async Task<string> ReadErrorAsync(StreamReader reader)
    {
        var last = string.Empty;
        while (await reader.ReadLineAsync() is { } line)
            last = line.Length <= 300 ? line : line[..300];
        return last;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited) _process.Kill();
            await _process.WaitForExitAsync();
            await _output;
            await _error;
        }
        finally { _process.Dispose(); }
    }
}
