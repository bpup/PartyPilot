using System.Diagnostics;
using System.Globalization;
using Avalonia.Threading;

namespace PartyPilot.Desktop;

internal sealed class LiveFollowSession : IAsyncDisposable
{
    private readonly Process _process;
    private readonly BridgeSession _bridge;
    private readonly int _followerPad;
    private readonly Task<string> _readError;
    private Task _readFrames = Task.CompletedTask;
    private bool _stopping;

    public string Status { get; private set; }
    public event Action<string>? StatusChanged;
    public event Action<string>? Ended;

    private LiveFollowSession(Process process, BridgeSession bridge, int followerPad,
        Task<string> readError, string initialStatus)
    {
        _process = process;
        _bridge = bridge;
        _followerPad = followerPad;
        _readError = readError;
        Status = initialStatus;
    }

    public static async Task<LiveFollowSession> StartAsync(string executable, PartyTopology topology,
        IReadOnlyList<LaunchedClient> clients, BridgeSession bridge, LiveFollowPlan plan)
    {
        if (topology.Id != "2-split" || clients.Count != 2 ||
            topology.Slots.Count != 2 || topology.Slots[1].PartyPlayer != 2 ||
            topology.Slots[1].RuntimePad != 3)
            throw new InvalidOperationException("Live control currently requires two separate single-role clients.");
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
        {
            "-roster", $"1:{clients[0].Pid},2:{clients[1].Pid}", "-leader", "1",
            "-exe", $"1={clients[0].Executable}", "-exe", $"2={clients[1].Executable}",
            "-stop", plan.Stop.ToString("R", CultureInfo.InvariantCulture),
            "-resume", plan.Resume.ToString("R", CultureInfo.InvariantCulture),
            "-catchup", plan.Catchup.ToString("R", CultureInfo.InvariantCulture),
            "-max-age", plan.MaxAge, "-log-max-age", plan.LogMaxAge
        }) start.ArgumentList.Add(argument);
        var process = Process.Start(start) ?? throw new InvalidOperationException("Live follow did not start.");
        var readError = process.StandardError.ReadToEndAsync();
        try
        {
            var first = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
            if (first is null) throw new InvalidDataException("Live follow exited without a decision frame.");
            var frame = LiveFollowFrame.Parse(first, 2, 3);
            await bridge.SendAutomationAsync(frame);
            return new LiveFollowSession(process, bridge, 3, readError, frame.Status);
        }
        catch (Exception error)
        {
            if (!process.HasExited) process.Kill();
            await process.WaitForExitAsync();
            var diagnostics = (await readError).Trim();
            process.Dispose();
            if (diagnostics.Length > 300) diagnostics = diagnostics[..300];
            throw new InvalidOperationException(
                string.IsNullOrEmpty(diagnostics) ? error.Message : $"{error.Message} {diagnostics}", error);
        }
    }

    public void StartReading()
    {
        if (_readFrames != Task.CompletedTask) throw new InvalidOperationException("Live follow is already being read.");
        _readFrames = ReadFramesAsync();
    }

    private async Task ReadFramesAsync()
    {
        string? failure = null;
        try
        {
            string? line;
            while (!_stopping && (line = await _process.StandardOutput.ReadLineAsync()) is not null)
            {
                var frame = LiveFollowFrame.Parse(line, 2, _followerPad);
                if (_stopping) break;
                await _bridge.SendAutomationAsync(frame);
                if (Status == frame.Status) continue;
                Status = frame.Status;
                Dispatcher.UIThread.Post(() => StatusChanged?.Invoke(frame.Status));
            }
        }
        catch (Exception error)
        {
            failure = error.Message;
            if (!_process.HasExited) _process.Kill();
        }
        finally
        {
            if (!_stopping)
            {
                if (!_process.HasExited) _process.Kill();
                await _process.WaitForExitAsync();
                try
                {
                    await _bridge.SendAutomationAsync(LiveFollowFrame.Waiting(_followerPad));
                }
                catch (Exception error) when (error is InvalidOperationException or IOException) { }
                var diagnostics = (await _readError).Trim();
                if (diagnostics.Length > 300) diagnostics = diagnostics[..300];
                Dispatcher.UIThread.Post(() => Ended?.Invoke(failure ?? diagnostics));
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stopping = true;
        if (!_process.HasExited) _process.Kill();
        await _process.WaitForExitAsync();
        await _readFrames;
        await _readError;
        try
        {
            await _bridge.SendAutomationAsync(LiveFollowFrame.Waiting(_followerPad));
        }
        catch (Exception error) when (error is InvalidOperationException or IOException) { }
        _process.Dispose();
    }

    public static string? FindExecutable()
    {
        const string file = "PartyPilot-LiveFollow.exe";
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, file),
            Path.Combine(AppContext.BaseDirectory, "..", file),
            Path.Combine(Environment.CurrentDirectory, "release", file)
        };
        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
    }
}
