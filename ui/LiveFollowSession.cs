using System.Diagnostics;
using System.Globalization;
using System.Collections.Concurrent;
using System.Text.Json;
using Avalonia.Threading;

namespace PartyPilot.Desktop;

internal sealed class LiveFollowSession : IAsyncDisposable
{
    private readonly Process _process;
    private readonly BridgeSession _bridge;
    private int _followerPad;
    private readonly PartyTopology _topology;
    private readonly Dictionary<int, int> _padToPlayer;
    private readonly Dictionary<int, int> _playerToPad;
    private readonly ConcurrentQueue<(int Player, string State)> _pendingMenu = new();
    private readonly ConcurrentQueue<int> _pendingFocus = new();
    private readonly Task<string> _readError;
    private Task _readFrames = Task.CompletedTask;
    private bool _stopping;

    public FollowBridgeFrame Current { get; private set; }
    public event Action<FollowBridgeFrame>? StatusChanged;
    public event Action<string>? Ended;

    private LiveFollowSession(Process process, BridgeSession bridge, PartyTopology topology,
        Task<string> readError, FollowBridgeFrame initialFrame)
    {
        _process = process;
        _bridge = bridge;
        _topology = topology;
        _followerPad = initialFrame.FollowerPad;
        _padToPlayer = topology.Slots.ToDictionary(slot => slot.RuntimePad, slot => slot.PartyPlayer);
        _playerToPad = topology.Slots.ToDictionary(slot => slot.PartyPlayer, slot => slot.RuntimePad);
        _readError = readError;
        Current = initialFrame;
        if (bridge.AutoFlaskEnabled)
        {
            bridge.MenuGuardChanged += OnMenuGuardChanged;
            foreach (var slot in topology.Slots)
                _pendingMenu.Enqueue((slot.PartyPlayer, bridge.GetMenuGuardState(slot.RuntimePad)));
        }
        if (bridge.LastFocusedLeadsEnabled) bridge.ManualFocusChanged += OnManualFocusChanged;
    }

    private void OnMenuGuardChanged(int pad, string state)
    {
        if (!_stopping && _padToPlayer.TryGetValue(pad, out var player))
            _pendingMenu.Enqueue((player, state));
    }

    private void OnManualFocusChanged(int pad)
    {
        if (!_stopping && _padToPlayer.TryGetValue(pad, out var player))
            _pendingFocus.Enqueue(player);
    }

    public static async Task<LiveFollowSession> StartAsync(string executable, PartyTopology topology,
        IReadOnlyList<LaunchedClient> clients, BridgeSession bridge, LiveFollowPlan plan,
        IReadOnlyDictionary<int, string>? memberNames = null)
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
            RedirectStandardInput = bridge.AutoFlaskEnabled || bridge.LastFocusedLeadsEnabled,
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
        if (plan.LeaderName.Length > 0)
        {
            start.ArgumentList.Add("-leader-name");
            start.ArgumentList.Add(plan.LeaderName);
        }
        if (memberNames is not null)
        {
            foreach (var (player, name) in memberNames.OrderBy(item => item.Key))
            {
                if (player is not (1 or 2) || string.IsNullOrWhiteSpace(name) || name.Length > 128)
                    throw new ArgumentException("Live follow member names require a bound P1 or P2 name.");
                start.ArgumentList.Add("-member-name");
                start.ArgumentList.Add($"{player}={name.Trim()}");
            }
        }
        if (bridge.AutoFlaskEnabled) start.ArgumentList.Add("-auto-flask");
        if (bridge.LastFocusedLeadsEnabled) start.ArgumentList.Add("-last-focused-leads");
        var process = Process.Start(start) ?? throw new InvalidOperationException("Live follow did not start.");
        var readError = process.StandardError.ReadToEndAsync();
        try
        {
            var initialLeader = bridge.LastFocusedLeadsEnabled
                ? topology.Slots.Single(slot => slot.RuntimePad == bridge.LastManualFocusedSlot).PartyPlayer : 1;
            if (initialLeader != 1)
            {
                await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
                    { type = "manual_focus", slot = initialLeader }));
                await process.StandardInput.FlushAsync();
            }
            FollowBridgeFrame frame;
            using (var startup = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            {
                do
                {
                    var first = await process.StandardOutput.ReadLineAsync(startup.Token);
                    if (first is null) throw new InvalidDataException("Live follow exited without a decision frame.");
                    frame = bridge.LastFocusedLeadsEnabled
                        ? LiveFollowFrame.ParseDynamic(first, topology) : LiveFollowFrame.Parse(first, 2, 3);
                } while (frame.FocusedPlayer != initialLeader);
            }
            await bridge.SendAutomationAsync(frame);
            return new LiveFollowSession(process, bridge, topology, readError, frame);
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
            while (!_stopping)
            {
                await FlushControlsAsync();
                var line = await _process.StandardOutput.ReadLineAsync();
                if (line is null) break;
                var frame = _bridge.LastFocusedLeadsEnabled
                    ? LiveFollowFrame.ParseDynamic(line, _topology)
                    : LiveFollowFrame.Parse(line, 2, _followerPad);
                if (_stopping) break;
                if (frame.FollowerPad != _followerPad)
                {
                    await _bridge.SendAutomationAsync(LiveFollowFrame.Waiting(_followerPad));
                    _followerPad = frame.FollowerPad;
                }
                await _bridge.SendAutomationAsync(frame);
                if (_bridge.AutoFlaskEnabled)
                {
                    foreach (var request in frame.Flasks)
                        await _bridge.SendFlaskAsync(_playerToPad[request.Player], request.Resource);
                }
                var changed = Current.Status != frame.Status || Current.Reason != frame.Reason ||
                    Current.FocusedPlayer != frame.FocusedPlayer || Current.FollowerPad != frame.FollowerPad;
                Current = frame;
                if (changed) Dispatcher.UIThread.Post(() => StatusChanged?.Invoke(frame));
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

    private async Task FlushControlsAsync()
    {
        if (!_bridge.AutoFlaskEnabled && !_bridge.LastFocusedLeadsEnabled) return;
        while (_pendingFocus.TryDequeue(out var player))
            await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
                { type = "manual_focus", slot = player }));
        while (_pendingMenu.TryDequeue(out var item))
        {
            await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
            {
                type = "menu_guard", slot = item.Player, state = item.State
            }));
        }
        await _process.StandardInput.FlushAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _stopping = true;
        _bridge.MenuGuardChanged -= OnMenuGuardChanged;
        _bridge.ManualFocusChanged -= OnManualFocusChanged;
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
