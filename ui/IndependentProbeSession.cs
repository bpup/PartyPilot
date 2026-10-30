using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace PartyPilot.Desktop;

// Owns the optional standalone position producer for a live follow session.
internal sealed class IndependentProbeSession : IAsyncDisposable
{
    private static readonly SemaphoreSlim SharedGate = new(1, 1);
    private static IndependentProbeSession? _shared;
    private static int _leases;

    private readonly Process _process;
    private readonly string _executable;
    private readonly Task _output;
    private readonly Task<string> _error;
    private bool _stopped;
    private readonly ConcurrentDictionary<int, bool> _recognizedBuilds = new();
    private readonly ConcurrentDictionary<int, byte> _readFailures = new();

    internal event Action? DiagnosticsChanged;

    private IndependentProbeSession(Process process, string executable)
    {
        _process = process;
        _executable = executable;
        _output = DrainAsync(process.StandardOutput);
        _error = ReadErrorAsync(process.StandardError);
    }

    internal bool IsRunning => !_process.HasExited;

    internal bool HasUnknownBuild(int processId) =>
        _recognizedBuilds.TryGetValue(processId, out var recognized) && !recognized;

    internal bool HasReadFailure(int processId) => _readFailures.ContainsKey(processId);

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
        await SharedGate.WaitAsync();
        try
        {
            if (_shared is { } existing)
            {
                if (!existing.IsRunning || !string.Equals(existing._executable, executable,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("已启动的独立探针不可复用。");
                _leases++;
                return existing;
            }
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
            var session = new IndependentProbeSession(process, executable);
            await Task.Delay(150);
            if (process.HasExited)
            {
                var detail = await session._error;
                await session.StopProcessAsync();
                throw new InvalidOperationException("独立位置探针提前退出：" + detail.Trim());
            }
            _shared = session;
            _leases = 1;
            return session;
        }
        finally { SharedGate.Release(); }
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
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("pid", out var pid) ||
                    pid.ValueKind != JsonValueKind.Number ||
                    !pid.TryGetInt32(out var processId) || processId <= 0)
                    continue;
                if (root.TryGetProperty("status", out var status) &&
                    status.ValueKind == JsonValueKind.String &&
                    status.GetString() is "attach_failed" or "capture_failed")
                {
                    _recognizedBuilds.TryRemove(processId, out _);
                    if (_readFailures.TryAdd(processId, 0)) DiagnosticsChanged?.Invoke();
                }
                else if (root.TryGetProperty("recognizedBuild", out var build) &&
                         build.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    var recognized = build.GetBoolean();
                    var changed = !_recognizedBuilds.TryGetValue(processId, out var prior) ||
                        prior != recognized;
                    _recognizedBuilds[processId] = recognized;
                    if (_readFailures.TryRemove(processId, out _)) changed = true;
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
        await SharedGate.WaitAsync();
        try
        {
            if (!ReferenceEquals(_shared, this)) return;
            if (--_leases > 0) return;
            _shared = null;
            _leases = 0;
            await StopProcessAsync();
        }
        finally { SharedGate.Release(); }
    }

    private async Task StopProcessAsync()
    {
        if (_stopped) return;
        _stopped = true;
        try
        {
            try { if (!_process.HasExited) _process.Kill(); }
            catch (InvalidOperationException) { /* Process exited between check and kill. */ }
            await _process.WaitForExitAsync();
            await _output;
            await _error;
        }
        finally { _process.Dispose(); }
    }
}
