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
    private HashSet<int> _followerPads;
    private readonly PartyTopology _topology;
    private readonly Dictionary<int, int> _padToPlayer;
    private readonly Dictionary<int, int> _playerToPad;
    private readonly ConcurrentQueue<(int Player, string State)> _pendingMenu = new();
    private readonly ConcurrentQueue<int> _pendingFocus = new();
    private readonly SemaphoreSlim _controlSignal = new(0);
    private readonly CancellationTokenSource _controlCancellation = new();
    private readonly Task<string> _readError;
    private Task _readFrames = Task.CompletedTask;
    private Task _writeControls = Task.CompletedTask;
    private string? _controlFailure;
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
        _followerPads = initialFrame.PadCommands.Select(command => command.Pad).ToHashSet();
        _padToPlayer = topology.Slots.ToDictionary(slot => slot.RuntimePad, slot => slot.PartyPlayer);
        _playerToPad = topology.Slots.ToDictionary(slot => slot.PartyPlayer, slot => slot.RuntimePad);
        _readError = readError;
        Current = initialFrame;
        if (bridge.AutoFlaskEnabled)
        {
            bridge.MenuGuardChanged += OnMenuGuardChanged;
            foreach (var slot in topology.Slots)
                _pendingMenu.Enqueue((slot.PartyPlayer, bridge.GetMenuGuardState(slot.RuntimePad)));
            _controlSignal.Release();
        }
        if (bridge.LastFocusedLeadsEnabled)
        {
            bridge.ManualFocusChanged += OnManualFocusChanged;
            if (_padToPlayer.TryGetValue(bridge.LastManualFocusedSlot, out var currentLeader) &&
                currentLeader != initialFrame.FocusedPlayer)
            {
                _pendingFocus.Enqueue(currentLeader);
                _controlSignal.Release();
            }
        }
    }

    private void OnMenuGuardChanged(int pad, string state)
    {
        if (!_stopping && _padToPlayer.TryGetValue(pad, out var player))
        {
            _pendingMenu.Enqueue((player, state));
            _controlSignal.Release();
        }
    }

    private void OnManualFocusChanged(int pad)
    {
        if (!_stopping && _padToPlayer.TryGetValue(pad, out var player))
        {
            _pendingFocus.Enqueue(player);
            _controlSignal.Release();
        }
    }

    public static async Task<LiveFollowSession> StartAsync(string executable, PartyTopology topology,
        IReadOnlyList<LaunchedClient> clients, BridgeSession bridge, LiveFollowPlan plan,
        IReadOnlyDictionary<int, string>? memberNames = null,
        IReadOnlyDictionary<int, int>? followTargets = null,
        IReadOnlyDictionary<int, double>? playerStops = null)
    {
        if (topology.Id is not ("2-split" or "3-three-singles") ||
            clients.Count != topology.ClientCount ||
            topology.Slots.Count != topology.ClientCount ||
            topology.Slots.Any(slot => slot.LocalPlayer != 1) ||
            topology.ClientCount == 3 && !bridge.LastFocusedLeadsEnabled)
            throw new InvalidOperationException("Live control requires a supported one-player-per-client layout.");
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
            "-roster", string.Join(',', topology.Slots.Select(slot =>
                $"{slot.PartyPlayer}:{clients[slot.Client - 1].Pid}")), "-leader", "1",
            "-stop", plan.Stop.ToString("R", CultureInfo.InvariantCulture),
            "-catchup", plan.Catchup.ToString("R", CultureInfo.InvariantCulture),
            "-max-age", plan.MaxAge, "-log-max-age", plan.LogMaxAge
        }) start.ArgumentList.Add(argument);
        foreach (var slot in topology.Slots)
        {
            start.ArgumentList.Add("-exe");
            start.ArgumentList.Add($"{slot.PartyPlayer}={clients[slot.Client - 1].Executable}");
        }
        if (plan.LeaderName.Length > 0)
        {
            start.ArgumentList.Add("-leader-name");
            start.ArgumentList.Add(plan.LeaderName);
        }
        if (memberNames is not null)
        {
            foreach (var (player, name) in memberNames.OrderBy(item => item.Key))
            {
                if (!topology.Slots.Any(slot => slot.PartyPlayer == player) ||
                    string.IsNullOrWhiteSpace(name) || name.Length > 128)
                    throw new ArgumentException("Live follow member names require a bound player name.");
                start.ArgumentList.Add("-member-name");
                start.ArgumentList.Add($"{player}={name.Trim()}");
            }
        }
        if (followTargets is not null)
        {
            foreach (var (player, target) in followTargets.OrderBy(item => item.Key))
            {
                if (!topology.Slots.Any(slot => slot.PartyPlayer == player) ||
                    target == player || !topology.Slots.Any(slot => slot.PartyPlayer == target))
                    throw new ArgumentException("Live follow target must belong to a different active player.");
                start.ArgumentList.Add("-follow-target");
                start.ArgumentList.Add($"{player}={target}");
            }
        }
        if (playerStops is not null)
        {
            foreach (var (player, stop) in playerStops.OrderBy(item => item.Key))
            {
                if (!topology.Slots.Any(slot => slot.PartyPlayer == player) ||
                    !double.IsFinite(stop) || stop is < 0 or > 200)
                    throw new ArgumentException("Individual stop distance needs an active player and a value from 0 to 200.");
                start.ArgumentList.Add("-stop-player");
                start.ArgumentList.Add($"{player}={stop.ToString("R", CultureInfo.InvariantCulture)}");
            }
        }
        if (bridge.AutoFlaskEnabled) start.ArgumentList.Add("-auto-flask");
        if (bridge.LastFocusedLeadsEnabled) start.ArgumentList.Add("-last-focused-leads");
        var process = Process.Start(start) ?? throw new InvalidOperationException("Live follow did not start.");
        var readError = process.StandardError.ReadToEndAsync();
        FollowBridgeFrame? initialFrame = null;
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
                        ? LiveFollowFrame.ParseDynamic(first, topology)
                        : LiveFollowFrame.Parse(first, 2, topology.Slots[1].RuntimePad);
                } while (frame.FocusedPlayer != initialLeader);
            }
            initialFrame = frame;
            foreach (var command in frame.PadCommands)
                await bridge.SendAutomationAsync(command.Command);
            if (bridge.AutoFlaskEnabled)
            {
                foreach (var request in frame.Flasks)
                {
                    var pad = topology.Slots.Single(slot => slot.PartyPlayer == request.Player).RuntimePad;
                    await bridge.SendFlaskAsync(pad, request.Resource);
                }
            }
            return new LiveFollowSession(process, bridge, topology, readError, frame);
        }
        catch (Exception error)
        {
            if (initialFrame is not null)
            {
                foreach (var pad in initialFrame.PadCommands.Select(command => command.Pad))
                {
                    try { await bridge.SendAutomationAsync(LiveFollowFrame.Waiting(pad)); }
                    catch (Exception stopError) when (stopError is InvalidOperationException or IOException) { }
                }
            }
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
        if (_bridge.AutoFlaskEnabled || _bridge.LastFocusedLeadsEnabled)
            _writeControls = WriteControlsAsync();
        _readFrames = ReadFramesAsync();
    }

    private async Task WriteControlsAsync()
    {
        try
        {
            while (true)
            {
                await _controlSignal.WaitAsync(_controlCancellation.Token);
                await FlushControlsAsync();
            }
        }
        catch (OperationCanceledException) when (_controlCancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            _controlFailure = error.Message;
            if (!_process.HasExited) _process.Kill();
        }
    }

    private async Task ReadFramesAsync()
    {
        string? failure = null;
        try
        {
            while (!_stopping)
            {
                var line = await _process.StandardOutput.ReadLineAsync();
                if (line is null) break;
                var frame = _bridge.LastFocusedLeadsEnabled
                    ? LiveFollowFrame.ParseDynamic(line, _topology)
                    : LiveFollowFrame.Parse(line, 2, _topology.Slots[1].RuntimePad);
                if (_stopping) break;
                var nextPads = frame.PadCommands.Select(command => command.Pad).ToHashSet();
                foreach (var oldPad in _followerPads.Except(nextPads))
                    await _bridge.SendAutomationAsync(LiveFollowFrame.Waiting(oldPad));
                _followerPads = nextPads;
                foreach (var command in frame.PadCommands)
                    await _bridge.SendAutomationAsync(command.Command);
                if (_bridge.AutoFlaskEnabled)
                {
                    foreach (var request in frame.Flasks)
                        await _bridge.SendFlaskAsync(_playerToPad[request.Player], request.Resource);
                }
                var changed = Current.Status != frame.Status || Current.Reason != frame.Reason ||
                    Current.FocusedPlayer != frame.FocusedPlayer ||
                    !Current.PadCommands.Select(command => (command.Pad, command.Status, command.Reason))
                        .SequenceEqual(frame.PadCommands.Select(command => (command.Pad, command.Status, command.Reason)));
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
            _controlCancellation.Cancel();
            if (!_stopping)
            {
                if (!_process.HasExited) _process.Kill();
                await _process.WaitForExitAsync();
                try
                {
                    foreach (var pad in _followerPads)
                        await _bridge.SendAutomationAsync(LiveFollowFrame.Waiting(pad));
                }
                catch (Exception error) when (error is InvalidOperationException or IOException) { }
                var diagnostics = (await _readError).Trim();
                if (diagnostics.Length > 300) diagnostics = diagnostics[..300];
                Dispatcher.UIThread.Post(() => Ended?.Invoke(failure ?? _controlFailure ?? diagnostics));
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
        _controlCancellation.Cancel();
        if (!_process.HasExited) _process.Kill();
        await _process.WaitForExitAsync();
        await _readFrames;
        await _writeControls;
        await _readError;
        try
        {
            foreach (var pad in _followerPads)
                await _bridge.SendAutomationAsync(LiveFollowFrame.Waiting(pad));
        }
        catch (Exception error) when (error is InvalidOperationException or IOException) { }
        _process.Dispose();
        _controlCancellation.Dispose();
        _controlSignal.Dispose();
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
