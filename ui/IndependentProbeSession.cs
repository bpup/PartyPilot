using System.Diagnostics;

namespace PartyPilot.Desktop;

// Owns the optional standalone position producer for a live follow session.
internal sealed class IndependentProbeSession : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task _output;
    private readonly Task<string> _error;

    private IndependentProbeSession(Process process)
    {
        _process = process;
        _output = DrainAsync(process.StandardOutput);
        _error = ReadErrorAsync(process.StandardError);
    }

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

    private static async Task DrainAsync(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is not null) { }
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
