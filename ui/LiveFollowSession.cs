using System.Diagnostics;
using System.Globalization;
using System.Collections.Concurrent;
using System.Text.Json;
using Avalonia.Threading;
using PartyPilot.Common;

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
    private readonly ConcurrentQueue<bool> _pendingPause = new();
    private readonly object _mainInputGate = new();
    private MainInputSample? _pendingMainInput;
    private readonly bool _relayMainInput;
    private readonly bool _catchUpPriority;
    private readonly int _customRuleCount;
    private readonly IReadOnlyList<LaunchedClient> _clients;
    private readonly TransitionAnchorEvidence _transitionAnchors = new();
    private readonly bool _experimentalWarpClick;
    private readonly CancellationTokenSource _warpCancellation = new();
    private CancellationTokenSource? _warpAttemptCancellation;
    private Task _warpTask = Task.CompletedTask;
    private FollowHandoffRequest? _attemptedWarp;
    private int _inputSubscriptionActive;
    private readonly SemaphoreSlim _controlSignal = new(0);
    private readonly CancellationTokenSource _controlCancellation = new();
    private readonly Task<string> _readError;
    private Task _readFrames = Task.CompletedTask;
    private Task _writeControls = Task.CompletedTask;
    private string? _controlFailure;
    private bool _stopping;

    public FollowBridgeFrame Current { get; private set; }
    public bool Paused { get; private set; }
    public event Action<FollowBridgeFrame>? StatusChanged;
    public event Action<string>? WarpStatusChanged;
    public event Action<string>? Ended;

    internal void ObserveRadarFrame(RadarFrame frame)
    {
        if (_stopping) return;
        for (var index = 0; index < _clients.Count; index++)
            if (_clients[index].Pid == frame.ProcessId)
            {
                _transitionAnchors.Observe(index + 1, frame);
                break;
            }
    }

    internal TransitionAnchorMatch? MatchTransitionAnchor(FollowHandoffRequest request) =>
        _transitionAnchors.Match(request, _topology, DateTime.UtcNow);

    private LiveFollowSession(Process process, BridgeSession bridge, PartyTopology topology,
        Task<string> readError, FollowBridgeFrame initialFrame, bool relayMainInput,
        bool catchUpPriority, int customRuleCount, IReadOnlyList<LaunchedClient> clients,
        bool experimentalWarpClick)
    {
        _process = process;
        _bridge = bridge;
        _topology = topology;
        _followerPads = initialFrame.PadCommands.Select(command => command.Pad).ToHashSet();
        _padToPlayer = topology.Slots.ToDictionary(slot => slot.RuntimePad, slot => slot.PartyPlayer);
        _playerToPad = topology.Slots.ToDictionary(slot => slot.PartyPlayer, slot => slot.RuntimePad);
        _readError = readError;
        _relayMainInput = relayMainInput;
        _catchUpPriority = catchUpPriority;
        _customRuleCount = customRuleCount;
        _clients = clients.ToArray();
        _experimentalWarpClick = experimentalWarpClick;
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
        if (relayMainInput) bridge.MainInputChanged += OnMainInputChanged;
    }

    private void OnMainInputChanged(MainInputSample sample)
    {
        if (_stopping || !_padToPlayer.ContainsKey(sample.Pad)) return;
        bool signal;
        lock (_mainInputGate)
        {
            signal = _pendingMainInput is null;
            _pendingMainInput = sample;
        }
        if (signal) _controlSignal.Release();
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

    public void SetPaused(bool paused)
    {
        if (_stopping || _process.HasExited) throw new InvalidOperationException("Live follow has stopped.");
        Paused = paused;
        if (paused) _warpAttemptCancellation?.Cancel();
        _pendingPause.Enqueue(paused);
        _controlSignal.Release();
    }

    private static int ReadCustomRuleCount(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 ||
            !root.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Custom recovery settings need a rules array.");
        return rules.GetArrayLength();
    }

    internal static IReadOnlyList<string> RosterArguments(PartyTopology topology,
        IReadOnlyList<LaunchedClient> clients)
    {
        if (clients.Count != topology.ClientCount || clients.Any(client => client.Pid <= 0))
            throw new ArgumentException("Live roster needs a process for every client.");
        if (topology.Id is "4-balanced" or "5-third-solo" or "5-main-solo" or "6-balanced")
            return ["-experimental-ordered-input", .. topology.Slots.SelectMany(slot =>
                new[] { "-role-bind",
                    $"{slot.PartyPlayer}:{slot.Client}:{slot.LocalPlayer}:{clients[slot.Client - 1].Pid}" })];
        if (topology.Id is "2-split" or "3-three-singles")
            return ["-roster", string.Join(',', topology.Slots.Select(slot =>
                $"{slot.PartyPlayer}:{clients[slot.Client - 1].Pid}"))];
        throw new ArgumentException("Live roster needs a supported layout.");
    }

    internal static IReadOnlyList<string> ExecutableArguments(PartyTopology topology,
        IReadOnlyList<LaunchedClient> clients)
    {
        if (clients.Count != topology.ClientCount)
            throw new ArgumentException("Live log sources need one process per client.");
        if (topology.Id is "4-balanced" or "5-third-solo" or "5-main-solo" or "6-balanced")
            return [.. clients.SelectMany((client, index) =>
                new[] { "-exe", $"{index + 1}={client.Executable}" })];
        return [.. topology.Slots.SelectMany(slot =>
            new[] { "-exe", $"{slot.PartyPlayer}={clients[slot.Client - 1].Executable}" })];
    }

    internal static IReadOnlyList<string> LogStartArguments(IReadOnlyList<LaunchedClient> clients) =>
        [.. clients.SelectMany((client, index) => client.LogStartOffset is { } offset
            ? new[] { "-log-start-offset", $"{index + 1}={offset}" }
            : Array.Empty<string>())];

    internal static FollowBridgeFrame ParseFrame(string json, PartyTopology topology,
        bool lastFocusedLeads, int customRuleCount = 0)
    {
        var parsed = lastFocusedLeads || topology.PlayerCount > 2
            ? LiveFollowFrame.ParseDynamic(json, topology, customRuleCount)
            : LiveFollowFrame.Parse(json, 2, topology.Slots[1].RuntimePad,
                customRuleCount: customRuleCount);
        return parsed with { Handoffs = LiveFollowFrame.ParseHandoffs(json, topology) };
    }

    public static async Task<LiveFollowSession> StartAsync(string executable, PartyTopology topology,
        IReadOnlyList<LaunchedClient> clients, BridgeSession bridge, LiveFollowPlan plan,
        IReadOnlyDictionary<int, string>? memberNames = null,
        IReadOnlyDictionary<int, int>? followTargets = null,
        IReadOnlyDictionary<int, double>? playerStops = null,
        IReadOnlyDictionary<int, string>? terrainBindings = null,
        bool classicCollisionDodge = false,
        bool classicSprintCatchUp = false,
        bool sprintCatchUp = true,
        bool sprintCatchUpPriority = true,
        bool mirrorAttackDirection = false,
        bool aggressiveBlinkCatchUp = false,
        int aggressiveSprintRetryDelay = 800,
        bool relayMainInput = false,
        bool closeRangeMirror = false,
        int closeMirrorDuration = 400,
        FollowerMirrorConfig? followerMirror = null,
        string? flaskSupportFile = null,
        string? flaskRulesFile = null,
        string? flaskCustomFile = null,
        bool experimentalWarpClick = false)
    {
        if (experimentalWarpClick &&
            (topology.Id is not ("4-balanced" or "5-third-solo" or "5-main-solo" or "6-balanced") ||
             clients.Count != topology.ClientCount))
            throw new ArgumentException("Experimental warp requires a paired four-to-six-player layout.");
        if (aggressiveBlinkCatchUp && classicSprintCatchUp)
            throw new ArgumentException("Classic and aggressive sprint modes cannot be combined.");
        if (aggressiveBlinkCatchUp && aggressiveSprintRetryDelay is < 500 or > 1500)
            throw new ArgumentOutOfRangeException(nameof(aggressiveSprintRetryDelay));
        if (closeRangeMirror && !relayMainInput)
            throw new ArgumentException("Close-range mirror requires main-controller input.");
        if (mirrorAttackDirection && !relayMainInput)
            throw new ArgumentException("Attack direction requires main-controller input.");
        if (flaskSupportFile is not null &&
            (!bridge.AutoFlaskEnabled || !Path.IsPathFullyQualified(flaskSupportFile) ||
             !File.Exists(flaskSupportFile)))
            throw new ArgumentException("Drinker support needs enabled flasks and an existing settings file.");
        if (flaskRulesFile is not null &&
            (!bridge.AutoFlaskEnabled || !Path.IsPathFullyQualified(flaskRulesFile) ||
             !File.Exists(flaskRulesFile)))
            throw new ArgumentException("Player flask rules need enabled flasks and an existing settings file.");
        if (flaskCustomFile is not null &&
            (!bridge.AutoFlaskEnabled || !Path.IsPathFullyQualified(flaskCustomFile) ||
             !File.Exists(flaskCustomFile)))
            throw new ArgumentException("Custom recovery rules need enabled flasks and an existing settings file.");
        var customRuleCount = flaskCustomFile is null ? 0 : ReadCustomRuleCount(flaskCustomFile);
        if (closeRangeMirror && closeMirrorDuration is < 50 or > 5000)
            throw new ArgumentOutOfRangeException(nameof(closeMirrorDuration));
        var orderedTopology = topology.Id is "4-balanced" or "5-third-solo" or "5-main-solo" or "6-balanced";
        if (topology.Id is not ("2-split" or "3-three-singles" or "4-balanced" or
                "5-third-solo" or "5-main-solo" or "6-balanced") ||
            clients.Count != topology.ClientCount ||
            (!orderedTopology && (topology.Slots.Count != topology.ClientCount ||
                topology.Slots.Any(slot => slot.LocalPlayer != 1))))
            throw new InvalidOperationException("Live control requires a supported layout.");
        var terrain = terrainBindings ?? new Dictionary<int, string>();
        var terrainClients = topology.Slots.GroupBy(slot => slot.Client).ToArray();
        if ((terrain.Count != 0 &&
             (terrain.Count != topology.Slots.Count ||
              terrain.Keys.Any(player => !topology.Slots.Any(slot => slot.PartyPlayer == player)) ||
              terrain.Values.Any(path => !Path.IsPathFullyQualified(path) || !File.Exists(path)) ||
              terrainClients.Any(client => client.Select(slot => terrain[slot.PartyPlayer])
                  .Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1) ||
              terrainClients.Select(client => terrain[client.First().PartyPlayer])
                  .Distinct(StringComparer.OrdinalIgnoreCase).Count() != topology.ClientCount)) ||
            (classicCollisionDodge && (orderedTopology ||
                sprintCatchUp && terrain.Count != topology.ClientCount)))
            throw new ArgumentException("Live terrain requires one distinct binding file per client.");
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };
        foreach (var argument in RosterArguments(topology, clients))
            start.ArgumentList.Add(argument);
        foreach (var argument in new[]
        {
            "-leader", "1",
            "-stop", plan.Stop.ToString("R", CultureInfo.InvariantCulture),
            "-catchup", plan.Catchup.ToString("R", CultureInfo.InvariantCulture),
            "-max-age", plan.MaxAge, "-log-max-age", plan.LogMaxAge
        }) start.ArgumentList.Add(argument);
        foreach (var argument in ExecutableArguments(topology, clients))
            start.ArgumentList.Add(argument);
        foreach (var argument in LogStartArguments(clients))
            start.ArgumentList.Add(argument);
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
        foreach (var (player, path) in terrain.OrderBy(entry => entry.Key))
        {
            start.ArgumentList.Add("-terrain-binding");
            start.ArgumentList.Add($"{player}={path}");
        }
        if (classicSprintCatchUp) start.ArgumentList.Add("-classic-sprint-catchup");
        if (!sprintCatchUp) start.ArgumentList.Add("-disable-sprint-catchup");
        if (!sprintCatchUpPriority) start.ArgumentList.Add("-disable-catch-up-priority");
        if (mirrorAttackDirection) start.ArgumentList.Add("-mirror-attack-direction");
        if (aggressiveBlinkCatchUp)
        {
            start.ArgumentList.Add("-aggressive-blink-catchup");
            start.ArgumentList.Add("-aggressive-sprint-retry-delay-ms");
            start.ArgumentList.Add(aggressiveSprintRetryDelay.ToString(CultureInfo.InvariantCulture));
        }
        if (relayMainInput) start.ArgumentList.Add("-accept-main-input");
        if (closeRangeMirror) start.ArgumentList.Add("-close-range-mirror");
        if (closeRangeMirror)
        {
            start.ArgumentList.Add("-close-mirror-duration");
            start.ArgumentList.Add($"{closeMirrorDuration}ms");
            start.ArgumentList.Add("-mirror-b-players");
            var mirror = followerMirror ?? FollowerMirrorConfig.Default;
            var players = topology.Slots.Where(slot =>
                (mirror.ButtonsForPlayer(slot.PartyPlayer) & 0x2000) != 0)
                .Select(slot => slot.PartyPlayer.ToString(CultureInfo.InvariantCulture));
            var allowed = string.Join(',', players);
            start.ArgumentList.Add(allowed.Length == 0 ? "none" : allowed);
        }
        if (classicCollisionDodge && sprintCatchUp) start.ArgumentList.Add("-classic-collision-dodge");
        if (bridge.AutoFlaskEnabled) start.ArgumentList.Add("-auto-flask");
        if (flaskSupportFile is not null)
        {
            start.ArgumentList.Add("-flask-support-file");
            start.ArgumentList.Add(flaskSupportFile);
        }
        if (flaskRulesFile is not null)
        {
            start.ArgumentList.Add("-flask-rules-file");
            start.ArgumentList.Add(flaskRulesFile);
        }
        if (flaskCustomFile is not null)
        {
            start.ArgumentList.Add("-flask-custom-file");
            start.ArgumentList.Add(flaskCustomFile);
        }
        if (bridge.LastFocusedLeadsEnabled) start.ArgumentList.Add("-last-focused-leads");
        start.ArgumentList.Add("-allow-follow-pause");
        var process = Process.Start(start) ?? throw new InvalidOperationException("Live follow did not start.");
        var readError = process.StandardError.ReadToEndAsync();
        FollowBridgeFrame? initialFrame = null;
        LiveFollowSession? session = null;
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
                    frame = ParseFrame(first, topology, bridge.LastFocusedLeadsEnabled,
                        customRuleCount);
                    frame = LiveFollowFrame.ApplyCatchUpPriority(frame,
                        sprintCatchUp && sprintCatchUpPriority);
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
                    if (request.DirectInput is { } input)
                        await bridge.SendRecoveryActionAsync(pad, input);
                    else
                        await bridge.SendFlaskAsync(pad, request.Resource);
                }
            }
            session = new LiveFollowSession(process, bridge, topology, readError, frame,
                relayMainInput, sprintCatchUp && sprintCatchUpPriority, customRuleCount,
                clients, experimentalWarpClick);
            if (relayMainInput)
            {
                await bridge.SetMainInputSubscriptionAsync(true);
                Volatile.Write(ref session._inputSubscriptionActive, 1);
            }
            return session;
        }
        catch (Exception error)
        {
            if (session is not null)
            {
                bridge.MainInputChanged -= session.OnMainInputChanged;
                if (relayMainInput && bridge.IsRunning)
                {
                    try { await bridge.SetMainInputSubscriptionAsync(false); }
                    catch (Exception stopError) when (stopError is IOException or InvalidOperationException or TimeoutException) { }
                }
            }
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
                var frame = ParseFrame(line, _topology, _bridge.LastFocusedLeadsEnabled,
                    _customRuleCount);
                frame = LiveFollowFrame.ApplyCatchUpPriority(frame, _catchUpPriority);
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
                    {
                        var pad = _playerToPad[request.Player];
                        if (request.DirectInput is { } input)
                            await _bridge.SendRecoveryActionAsync(pad, input);
                        else
                            await _bridge.SendFlaskAsync(pad, request.Resource);
                    }
                }
                var changed = Current.Status != frame.Status || Current.Reason != frame.Reason ||
                    Current.FocusedPlayer != frame.FocusedPlayer ||
                    !Current.Handoffs.SequenceEqual(frame.Handoffs) ||
                    !Current.PadCommands.Select(command => (command.Pad, command.Status, command.Reason))
                        .SequenceEqual(frame.PadCommands.Select(command => (command.Pad, command.Status, command.Reason)));
                Current = frame;
                if (changed) Dispatcher.UIThread.Post(() => StatusChanged?.Invoke(frame));
                QueueVisualWarp(frame);
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
            _warpAttemptCancellation?.Cancel();
            await StopMainInputAsync();
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

    private void QueueVisualWarp(FollowBridgeFrame frame)
    {
        if (!_experimentalWarpClick || _stopping) return;
        var visualRoute = PartyWarpRoutes.FromFrame(frame, _topology);
        var controllerRoute = PartyWarpControllerPlan.FromFrame(frame, _topology);
        var request = visualRoute?.Request ?? controllerRoute?.Request;
        if (request is null || Paused)
        {
            _warpAttemptCancellation?.Cancel();
            _attemptedWarp = null;
            return;
        }
        if (_attemptedWarp == request || !_warpTask.IsCompleted) return;
        _attemptedWarp = request;
        _warpAttemptCancellation?.Dispose();
        _warpAttemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _warpCancellation.Token);
        _warpTask = RunCrossAreaWarpAsync(request, visualRoute, controllerRoute,
            _warpAttemptCancellation.Token);
    }

    private async Task RunCrossAreaWarpAsync(FollowHandoffRequest request,
        PartyWarpRoute? visualRoute, PartyWarpControllerRoute? controllerRoute,
        CancellationToken token)
    {
        var original = _clients.FirstOrDefault(PoeChatInviteService.IsExactForeground);
        var destination = _clients[request.DestinationClient - 1];
        try
        {
            string detail;
            if (visualRoute is not null)
            {
                var result = await new PartyWarpClickService(new WindowsLoginInputPort(destination))
                    .TryClickAsync(visualRoute.RemotePortraitIndex, token);
                detail = result.Detail;
            }
            else if (controllerRoute is not null)
            {
                await PartyWarpControllerRunner.RunAsync(
                    new BridgePartyWarpControllerPort(_bridge, destination), controllerRoute, token);
                detail = "已发送手柄队伍名单传送序列；仍需区域日志确认是否传送成功。";
            }
            else return;
            Dispatcher.UIThread.Post(() => WarpStatusChanged?.Invoke(detail));
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            Dispatcher.UIThread.Post(() => WarpStatusChanged?.Invoke(
                "视觉传送停止：" + error.Message));
        }
        finally
        {
            if (original is not null && original.Pid != destination.Pid &&
                PoeChatInviteService.IsExactForeground(destination))
            {
                try { await PoeChatInviteService.FocusExactWindowAsync(original,
                    CancellationToken.None); }
                catch { /* The original window may have exited. */ }
            }
        }
    }

    private async Task FlushControlsAsync()
    {
        while (_pendingPause.TryDequeue(out var paused))
            await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
                { type = "follow_pause", state = paused ? "paused" : "following" }));
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
        MainInputSample? input;
        lock (_mainInputGate)
        {
            input = _pendingMainInput;
            _pendingMainInput = null;
        }
        if (input is { } sample && _padToPlayer.TryGetValue(sample.Pad, out var inputPlayer) &&
            DateTime.UtcNow - sample.ReceivedAtUtc <= TimeSpan.FromMilliseconds(400))
            await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
            {
                type = "main_input", slot = inputPlayer,
                left_x = Math.Clamp(sample.LeftX / 32767.0, -1.0, 1.0),
                left_y = Math.Clamp(sample.LeftY / 32767.0, -1.0, 1.0),
                b_down = sample.BDown, blocked = sample.Blocked,
                attack_active = sample.AttackActive,
                observed_at_unix_ms = new DateTimeOffset(sample.ReceivedAtUtc).ToUnixTimeMilliseconds()
            }));
        await _process.StandardInput.FlushAsync();
    }

    private async Task StopMainInputAsync()
    {
        if (!_relayMainInput) return;
        _bridge.MainInputChanged -= OnMainInputChanged;
        if (Interlocked.Exchange(ref _inputSubscriptionActive, 0) == 0 || !_bridge.IsRunning) return;
        try { await _bridge.SetMainInputSubscriptionAsync(false); }
        catch (Exception error) when (error is IOException or InvalidOperationException or TimeoutException) { }
    }

    public async ValueTask DisposeAsync()
    {
        _stopping = true;
        _warpCancellation.Cancel();
        _warpAttemptCancellation?.Cancel();
        _bridge.MenuGuardChanged -= OnMenuGuardChanged;
        _bridge.ManualFocusChanged -= OnManualFocusChanged;
        await StopMainInputAsync();
        _controlCancellation.Cancel();
        if (!_process.HasExited) _process.Kill();
        await _process.WaitForExitAsync();
        await _readFrames;
        await _warpTask;
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
        _warpAttemptCancellation?.Dispose();
        _warpCancellation.Dispose();
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
