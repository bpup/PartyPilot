using System.Diagnostics;
using System.Collections.Concurrent;
using System.Text.Json;
using PartyPilot.Common;
using System.Text.Json.Nodes;
using Avalonia.Threading;

namespace PartyPilot.Desktop;

internal sealed class BridgeSession : IAsyncDisposable
{
    private readonly Process _process;
    private Task _readEvents = Task.CompletedTask;
    private readonly Task<string> _readError;
    private readonly ConcurrentDictionary<int, string> _menuStates = new();
    private readonly HashSet<int> _slots;
    private readonly HashSet<int> _allSlots;
    private readonly IReadOnlyDictionary<int, int> _hybridOutputForRuntime;
    private readonly SemaphoreSlim _inputGate = new(1, 1);
    private readonly SemaphoreSlim _partyGate = new(1, 1);
    private AutoPartyPending? _pendingAutoParty;
    private TaskCompletionSource? _pendingHybridPairingComplete;
    private int _lastFocusedSlot;
    private int _lastManualFocusedSlot;
    private bool _stopping;
    private bool _hybridPrelaunchPending;
    private string? _hybridPrelaunchPads;
    private int[]? _hybridPrelaunchSlots;

    public bool AutoFlaskEnabled { get; }
    internal bool IsHybridMode { get; }
    internal bool LastFocusedLeadsEnabled { get; }
    internal int LastFocusedSlot => Volatile.Read(ref _lastFocusedSlot);
    internal int LastManualFocusedSlot => Volatile.Read(ref _lastManualFocusedSlot);
    internal bool IsRunning
    {
        get
        {
            try { return !_stopping && !_process.HasExited; }
            catch (InvalidOperationException) { return false; }
        }
    }

    public event Action<int>? AReleased;
    public event Action<int, int, long>? PairingArmed;
    public event Action? PairingCompleted;
    public event Action<string>? LoginStatusChanged;
    public event Action<int, string>? MenuGuardChanged;
    public event Action<string>? Ended;
    public event Action? AutoPartyExpired;
    public event Action<int>? ManualFocusChanged;
    public event Action<int, int, long>? HybridFocusChanged;
    public event Action<int, int, long>? HybridConfirmReleased;

    private sealed record AutoPartyPending(string Type, int Slot, ushort Buttons,
        TaskCompletionSource Completion);

    private BridgeSession(Process process, Task<string> readError, bool autoFlaskEnabled,
        bool lastFocusedLeads,
        int[] slots, int[] allSlots, bool hybridMode = false,
        IReadOnlyDictionary<int, int>? hybridOutputForRuntime = null)
    {
        _process = process;
        _readError = readError;
        AutoFlaskEnabled = autoFlaskEnabled;
        IsHybridMode = hybridMode;
        LastFocusedLeadsEnabled = lastFocusedLeads;
        _slots = slots.ToHashSet();
        _allSlots = allSlots.ToHashSet();
        _hybridOutputForRuntime = hybridOutputForRuntime ?? new Dictionary<int, int>();
        _lastFocusedSlot = slots[0];
        _lastManualFocusedSlot = slots[0];
    }

    public static async Task<BridgeSession> StartAsync(string executable, IReadOnlyList<PartySlot> partySlots, int physicalIndex,
        string deviceInstance, IReadOnlyList<string> gameExecutables, bool rightStickFollowerOverride,
        bool autoFlaskEnabled = false, bool useWgi = false, PrivateClient3Plan? privatePlan = null,
        bool lastFocusedLeads = false, bool menuGuardEnabled = true,
        FollowerMirrorConfig? followerMirror = null)
    {
        var slots = partySlots.Where(item => item.Client != 3).Select(item => item.RuntimePad).ToArray();
        var privateSlots = partySlots.Where(item => item.Client == 3)
            .Select(item => item.RuntimePad).ToArray();
        if (slots.Length is < 1 or > 4 || slots.Any(slot => slot is < 1 or > 4) ||
            slots.Distinct().Count() != slots.Length ||
            (!useWgi && (physicalIndex is < 0 or > 3 || slots.Length > 3)) ||
            !partySlots.Select(item => item.PartyPlayer).SequenceEqual(Enumerable.Range(1, partySlots.Count)) ||
            string.IsNullOrWhiteSpace(deviceInstance) || gameExecutables.Count is < 1 or > 3 ||
            gameExecutables.Any(string.IsNullOrWhiteSpace) ||
            (privatePlan is null && privateSlots.Length != 0) ||
            (privatePlan is not null &&
             (!privateSlots.SequenceEqual(privatePlan.IncludePad6 ? [5, 6] : [5]) ||
              gameExecutables.Count != 3 ||
              !string.Equals(Path.GetFullPath(gameExecutables[2]), privatePlan.Executable,
                  StringComparison.OrdinalIgnoreCase))))
        {
            throw new ArgumentException("The bridge needs assigned public pads, a selected HidHide device, and a matching Client 3 binding when used.");
        }
        var focusPadMap = string.Join(',', partySlots.Select(item => $"{item.PartyPlayer}:{item.RuntimePad}"));
        var directRightStickSlot = rightStickFollowerOverride && partySlots.Count == 2 &&
            partySlots[0].Client == partySlots[1].Client ? partySlots[1].RuntimePad : 0;
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
            "--enable-controller", "--slots", string.Join(',', slots),
            "--focus-pad-map", focusPadMap,
            "--main-slot", slots[0].ToString(),
            "--physical-hidden-confirmed", "--pairing-protected", "--device-instance", deviceInstance
        })
        {
            start.ArgumentList.Add(argument);
        }
        if (useWgi) start.ArgumentList.Add("--physical-wgi");
        else
        {
            start.ArgumentList.Add("--physical-index");
            start.ArgumentList.Add(physicalIndex.ToString());
        }
        foreach (var game in gameExecutables)
        {
            start.ArgumentList.Add("--game-exe");
            start.ArgumentList.Add(game);
        }
        if (privatePlan is not null)
        {
            foreach (var argument in new[]
            {
                "--client3-pid", privatePlan.Pid.ToString(),
                "--client3-window-handle", privatePlan.WindowHandle.ToString(),
                "--client3-game-exe", privatePlan.Executable,
                "--client3-players", privatePlan.IncludePad6 ? "5,6" : "5"
            }) start.ArgumentList.Add(argument);
        }
        if (rightStickFollowerOverride) start.ArgumentList.Add("--right-stick-follower-override");
        if (autoFlaskEnabled) start.ArgumentList.Add("--enable-auto-flask");
        if (!menuGuardEnabled) start.ArgumentList.Add("--disable-menu-guard");
        if (followerMirror is not null)
        {
            start.ArgumentList.Add("--follower-mirror");
            start.ArgumentList.Add(followerMirror.Encode());
        }
        if (lastFocusedLeads) start.ArgumentList.Add("--last-focused-leads");
        if (directRightStickSlot != 0)
        {
            start.ArgumentList.Add("--direct-right-stick-slot");
            start.ArgumentList.Add(directRightStickSlot.ToString());
        }
        var process = Process.Start(start) ?? throw new InvalidOperationException("Virtual controller bridge did not start.");
        var readError = process.StandardError.ReadToEndAsync();
        try
        {
            var readyLine = await process.StandardOutput.ReadLineAsync().WaitAsync(
                TimeSpan.FromSeconds(privatePlan is null ? 15 : 45));
            if (readyLine is null || !IsExpectedReady(readyLine, slots, focusPadMap,
                rightStickFollowerOverride, directRightStickSlot, autoFlaskEnabled, useWgi, privatePlan,
                lastFocusedLeads, menuGuardEnabled, followerMirror))
            {
                throw new InvalidOperationException("Virtual controller bridge did not confirm the requested pad slots.");
            }
            return new BridgeSession(process, readError, autoFlaskEnabled, lastFocusedLeads, slots,
                partySlots.Select(item => item.RuntimePad).ToArray());
        }
        catch (Exception error)
        {
            await StopProcessAsync(process);
            var diagnostics = (await readError).Trim();
            process.Dispose();
            if (diagnostics.Length > 500) diagnostics = diagnostics[..500];
            throw new InvalidOperationException(
                string.IsNullOrEmpty(diagnostics) ? error.Message : $"{error.Message} {diagnostics}", error);
        }
    }

    public void StartReading()
    {
        if (_readEvents != Task.CompletedTask) throw new InvalidOperationException("Bridge events are already being read.");
        _readEvents = ReadEventsAsync();
    }

    internal static async Task<BridgeSession> StartHybridPrelaunchAsync(string executable,
        IReadOnlyList<PartySlot> partySlots, IReadOnlyList<string> gameExecutables,
        HybridBindingConfig bindings, bool menuGuardEnabled,
        FollowerMirrorConfig followerMirror)
    {
        var followers = partySlots.Skip(1).ToArray();
        var slots = followers.Length == 1 ? new[] { 1, 2 } : new[] { 1, 2, 3, 4 };
        var runtimePads = partySlots.Select(slot => slot.RuntimePad).ToArray();
        if (!(runtimePads.SequenceEqual([1, 3]) || runtimePads.SequenceEqual([1, 3, 4]) ||
              runtimePads.SequenceEqual([1, 3, 5]) ||
              runtimePads.SequenceEqual([1, 3, 4, 5, 6])) ||
            gameExecutables.Count != (runtimePads.Max() >= 5 ? 3 : 2) ||
            gameExecutables.Any(path => !Path.IsPathFullyQualified(path) || !File.Exists(path)))
            throw new ArgumentException("Keyboard prelaunch needs the selected topology and existing game executables.");
        var outputForRuntime = followers.ToDictionary(slot => slot.RuntimePad,
            slot => followers.Length == 1 ? 2 : slot.RuntimePad);
        var playerPads = string.Join(',', followers.Select(slot =>
            $"{slot.PartyPlayer}:{outputForRuntime[slot.RuntimePad]}"));
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = false,
            RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (var argument in new[] { "--enable-controller", "--hybrid-keyboard",
            "--hybrid-prelaunch", "--slots", string.Join(',', slots),
            "--hybrid-player-pads", playerPads, "--hybrid-binding-keys", bindings.Encode(),
            "--follower-mirror", followerMirror.Encode() }) start.ArgumentList.Add(argument);
        if (!menuGuardEnabled) start.ArgumentList.Add("--disable-menu-guard");
        foreach (var game in gameExecutables)
        {
            start.ArgumentList.Add("--game-exe");
            start.ArgumentList.Add(game);
        }
        var process = Process.Start(start) ??
            throw new InvalidOperationException("Keyboard prelaunch bridge did not start.");
        var readError = process.StandardError.ReadToEndAsync();
        try
        {
            var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
            if (!IsExpectedHybridPrelaunch(line, slots, playerPads, bindings.Encode(),
                    menuGuardEnabled, followerMirror.Encode()))
                throw new InvalidOperationException("Keyboard bridge did not confirm prelaunch virtual pads.");
            return new BridgeSession(process, readError, false, false, slots,
                [1, ..outputForRuntime.Values], hybridMode: true,
                hybridOutputForRuntime: outputForRuntime)
            {
                _hybridPrelaunchPending = true,
                _hybridPrelaunchPads = playerPads,
                _hybridPrelaunchSlots = slots
            };
        }
        catch (Exception error)
        {
            await StopProcessAsync(process);
            var diagnostic = (await readError).Trim();
            process.Dispose();
            throw new InvalidOperationException(string.IsNullOrEmpty(diagnostic)
                ? error.Message : $"{error.Message} {diagnostic}", error);
        }
    }

    internal async Task BindHybridAsync(IReadOnlyList<PartySlot> partySlots,
        IReadOnlyList<LaunchedClient> clients, HybridBindingConfig bindings,
        bool menuGuardEnabled, FollowerMirrorConfig followerMirror)
    {
        if (!_hybridPrelaunchPending || !IsRunning || _hybridPrelaunchSlots is null ||
            _hybridPrelaunchPads is null || clients.Count is < 2 or > 3 ||
            partySlots.Any(slot => slot.Client > clients.Count))
            throw new InvalidOperationException("Keyboard prelaunch bridge is not waiting for these game windows.");
        var windowMap = string.Join(',', partySlots.Select(slot =>
            $"{slot.PartyPlayer}:{clients[slot.Client - 1].Pid}:{clients[slot.Client - 1].WindowHandle}"));
        var privatePlan = clients.Count == 3 ? new PrivateClient3Plan(clients[2].Pid,
            clients[2].WindowHandle, Path.GetFullPath(clients[2].Executable),
            partySlots.Any(slot => slot.RuntimePad == 6)) : null;
        await WriteCommandAsync(JsonSerializer.Serialize(new
        {
            type = "bind_hybrid", player_pads = _hybridPrelaunchPads, window_map = windowMap,
            client3 = privatePlan is null ? null : new
            {
                pid = privatePlan.Pid, window_handle = privatePlan.WindowHandle,
                game_exe = privatePlan.Executable,
                players = privatePlan.IncludePad6 ? "5,6" : "5"
            }
        }, new JsonSerializerOptions { DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }));
        var line = await _process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(45));
        if (line is null || !IsExpectedHybridReady(line, _hybridPrelaunchSlots,
                _hybridPrelaunchPads, windowMap, bindings.Encode(), menuGuardEnabled,
                followerMirror, privatePlan))
            throw new InvalidOperationException("Keyboard bridge did not confirm bound game windows.");
        _hybridPrelaunchPending = false;
    }

    internal static bool IsExpectedHybridPrelaunch(string? line, int[] slots, string playerPads,
        string bindingKeys, bool menuGuardEnabled, string followerMirror)
    {
        try
        {
            using var json = JsonDocument.Parse(line ?? "");
            var root = json.RootElement;
            return root.TryGetProperty("event", out var kind) && kind.GetString() == "prelaunch_ready" &&
                root.TryGetProperty("hybrid_mode", out var hybrid) && hybrid.GetBoolean() &&
                root.TryGetProperty("physical_mode", out var physical) && !physical.GetBoolean() &&
                root.TryGetProperty("slots", out var selected) &&
                selected.EnumerateArray().Select(value => value.GetInt32()).SequenceEqual(slots) &&
                root.TryGetProperty("hybrid_player_pads", out var pads) &&
                pads.GetString() == playerPads &&
                root.TryGetProperty("hybrid_binding_keys", out var keys) &&
                keys.GetString() == bindingKeys &&
                root.TryGetProperty("menu_guard_enabled", out var guard) &&
                guard.GetBoolean() == menuGuardEnabled &&
                root.TryGetProperty("follower_mirror", out var mirror) &&
                mirror.GetString() == followerMirror;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    internal static async Task<BridgeSession> StartHybridAsync(string executable,
        IReadOnlyList<PartySlot> partySlots, IReadOnlyList<LaunchedClient> clients,
        HybridBindingConfig? bindings = null, bool menuGuardEnabled = true,
        FollowerMirrorConfig? followerMirror = null)
    {
        var runtimePads = partySlots.Select(slot => slot.RuntimePad).ToArray();
        var supportedLayout = runtimePads.SequenceEqual([1, 3]) ||
            runtimePads.SequenceEqual([1, 3, 4]) ||
            runtimePads.SequenceEqual([1, 3, 5]) ||
            runtimePads.SequenceEqual([1, 3, 4, 5, 6]);
        if (!supportedLayout || clients.Count != (runtimePads.Max() >= 5 ? 3 : 2) ||
            !partySlots.Select(slot => slot.PartyPlayer).SequenceEqual(Enumerable.Range(1, partySlots.Count)) ||
            partySlots.Any(slot => slot.Client != (slot.RuntimePad - 1) / 2 + 1 ||
                slot.LocalPlayer != (slot.RuntimePad - 1) % 2 + 1) ||
            clients.Any(client => client.Pid <= 0 || client.WindowHandle <= 0 ||
                string.IsNullOrWhiteSpace(client.Executable) || !Path.IsPathFullyQualified(client.Executable)))
            throw new ArgumentException("Keyboard bridge requires bound game windows for P1 and every follower.");
        var followers = partySlots.Skip(1).ToArray();
        var slots = followers.Length == 1 ? new[] { 1, 2 } : new[] { 1, 2, 3, 4 };
        var outputForRuntime = followers.ToDictionary(slot => slot.RuntimePad,
            slot => followers.Length == 1 ? 2 : slot.RuntimePad);
        var playerPads = string.Join(',', followers.Select(slot =>
            $"{slot.PartyPlayer}:{outputForRuntime[slot.RuntimePad]}"));
        var privatePlan = clients.Count == 3 ? new PrivateClient3Plan(clients[2].Pid,
            clients[2].WindowHandle, Path.GetFullPath(clients[2].Executable),
            runtimePads.Contains(6)) : null;
        var windowMap = string.Join(',', partySlots.Select(slot =>
            $"{slot.PartyPlayer}:{clients[slot.Client - 1].Pid}:{clients[slot.Client - 1].WindowHandle}"));
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
            "--enable-controller", "--hybrid-keyboard", "--slots", string.Join(',', slots),
            "--hybrid-player-pads", playerPads, "--hybrid-window-map", windowMap
        }) start.ArgumentList.Add(argument);
        if (bindings is not null)
        {
            start.ArgumentList.Add("--hybrid-binding-keys");
            start.ArgumentList.Add(bindings.Encode());
        }
        if (!menuGuardEnabled) start.ArgumentList.Add("--disable-menu-guard");
        if (followerMirror is not null)
        {
            start.ArgumentList.Add("--follower-mirror");
            start.ArgumentList.Add(followerMirror.Encode());
        }
        if (privatePlan is not null)
        {
            foreach (var argument in new[]
            {
                "--client3-pid", privatePlan.Pid.ToString(),
                "--client3-window-handle", privatePlan.WindowHandle.ToString(),
                "--client3-game-exe", privatePlan.Executable,
                "--client3-players", privatePlan.IncludePad6 ? "5,6" : "5"
            }) start.ArgumentList.Add(argument);
        }
        foreach (var client in clients)
        {
            start.ArgumentList.Add("--game-exe");
            start.ArgumentList.Add(client.Executable);
        }
        var process = Process.Start(start) ??
            throw new InvalidOperationException("Keyboard virtual controller bridge did not start.");
        var readError = process.StandardError.ReadToEndAsync();
        try
        {
            var readyLine = await process.StandardOutput.ReadLineAsync().WaitAsync(
                TimeSpan.FromSeconds(privatePlan is null ? 15 : 45));
            if (readyLine is null || !IsExpectedHybridReady(readyLine, slots, playerPads,
                    windowMap, bindings?.Encode(), menuGuardEnabled, followerMirror, privatePlan))
                throw new InvalidOperationException("Keyboard bridge did not confirm its pad and window binding.");
            return new BridgeSession(process, readError, false, false, slots,
                [1, ..outputForRuntime.Values], hybridMode: true,
                hybridOutputForRuntime: outputForRuntime);
        }
        catch (Exception error)
        {
            await StopProcessAsync(process);
            var diagnostics = (await readError).Trim();
            process.Dispose();
            if (diagnostics.Length > 500) diagnostics = diagnostics[..500];
            throw new InvalidOperationException(
                string.IsNullOrEmpty(diagnostics) ? error.Message : $"{error.Message} {diagnostics}", error);
        }
    }

    internal static bool IsExpectedHybridReady(string line, int[] slots,
        string playerPads, string windowMap, string? bindingKeys = null,
        bool menuGuardEnabled = true, FollowerMirrorConfig? followerMirror = null,
        PrivateClient3Plan? privatePlan = null)
    {
        try
        {
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            return root.TryGetProperty("event", out var kind) && kind.GetString() == "ready" &&
                root.TryGetProperty("hybrid_mode", out var hybrid) && hybrid.GetBoolean() &&
                root.TryGetProperty("physical_mode", out var physical) && !physical.GetBoolean() &&
                root.TryGetProperty("menu_guard_enabled", out var menuGuard) &&
                menuGuard.GetBoolean() == menuGuardEnabled &&
                (followerMirror is null || root.TryGetProperty("follower_mirror", out var mirror) &&
                 mirror.GetString() == followerMirror.Encode()) &&
                (privatePlan is null ||
                 root.TryGetProperty("private_client3_pid", out var privatePid) &&
                 privatePid.GetInt32() == privatePlan.Pid &&
                 root.TryGetProperty("private_client3_window_handle", out var privateWindow) &&
                 privateWindow.GetInt64() == privatePlan.WindowHandle &&
                 root.TryGetProperty("private_client3_slots", out var privatePads) &&
                 privatePads.EnumerateArray().Select(value => value.GetInt32())
                     .SequenceEqual(privatePlan.IncludePad6 ? [5, 6] : [5])) &&
                root.TryGetProperty("slots", out var selected) &&
                selected.EnumerateArray().Select(value => value.GetInt32()).SequenceEqual(slots.Order()) &&
                root.TryGetProperty("hybrid_player_pads", out var pads) && pads.GetString() == playerPads &&
                root.TryGetProperty("hybrid_window_map", out var windows) && windows.GetString() == windowMap &&
                (bindingKeys is null || root.TryGetProperty("hybrid_binding_keys", out var keys) &&
                 keys.GetString() == bindingKeys);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    internal static bool IsExpectedReady(string line, int[] slots, string focusPadMap,
        bool rightStickFollowerOverride, int directRightStickSlot, bool autoFlaskEnabled, bool useWgi,
        PrivateClient3Plan? privatePlan, bool lastFocusedLeads = false,
        bool menuGuardEnabled = true, FollowerMirrorConfig? followerMirror = null)
    {
        try
        {
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            return root.TryGetProperty("event", out var kind) && kind.GetString() == "ready" &&
                   root.TryGetProperty("physical_mode", out var mode) && mode.GetBoolean() &&
                   (!useWgi || root.TryGetProperty("physical_wgi", out var source) && source.GetBoolean()) &&
                   root.TryGetProperty("pairing_protected", out var protectedMode) && protectedMode.GetBoolean() &&
                   root.TryGetProperty("hidhide_preflight", out var preflight) && preflight.GetBoolean() &&
                   root.TryGetProperty("auto_flask_enabled", out var flaskMode) &&
                   flaskMode.GetBoolean() == autoFlaskEnabled &&
                   root.TryGetProperty("menu_guard_enabled", out var menuGuard) &&
                   menuGuard.GetBoolean() == menuGuardEnabled &&
                   (followerMirror is null || root.TryGetProperty("follower_mirror", out var mirror) &&
                    mirror.GetString() == followerMirror.Encode()) &&
                   root.TryGetProperty("last_focused_leads", out var dynamicMain) &&
                   dynamicMain.GetBoolean() == lastFocusedLeads &&
                   root.TryGetProperty("right_stick_follower_override", out var rightStick) &&
                   rightStick.GetBoolean() == rightStickFollowerOverride &&
                   root.TryGetProperty("direct_right_stick_slot", out var directSlot) &&
                   directSlot.GetInt32() == directRightStickSlot &&
                   root.TryGetProperty("focus_pad_map", out var focusMap) &&
                   focusMap.GetString() == focusPadMap &&
                   root.TryGetProperty("slots", out var selected) &&
                   selected.EnumerateArray().Select(value => value.GetInt32()).SequenceEqual(slots.Order()) &&
                   root.TryGetProperty("private_client3_pid", out var privatePid) &&
                   privatePid.GetInt32() == (privatePlan?.Pid ?? 0) &&
                   root.TryGetProperty("private_client3_window_handle", out var privateWindow) &&
                   privateWindow.GetInt64() == (privatePlan?.WindowHandle ?? 0) &&
                   root.TryGetProperty("private_client3_slots", out var privatePads) &&
                   privatePads.EnumerateArray().Select(value => value.GetInt32())
                       .SequenceEqual(privatePlan is null ? [] :
                           privatePlan.IncludePad6 ? [5, 6] : [5]);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    public async Task ArmPairingAsync(int slot, LaunchedClient client)
    {
        if (_stopping || _process.HasExited)
        {
            throw new InvalidOperationException("Virtual controller bridge is no longer running.");
        }
        await WriteCommandAsync(JsonSerializer.Serialize(new
        {
            type = "arm_pairing", slot, pid = client.Pid, window_handle = client.WindowHandle
        }));
    }

    public async Task FinishPairingAsync()
    {
        if (_stopping || _process.HasExited)
        {
            throw new InvalidOperationException("Virtual controller bridge is no longer running.");
        }
        await WriteCommandAsync(JsonSerializer.Serialize(new { type = "finish_pairing" }));
    }

    internal Task SendAutomationAsync(FollowBridgeFrame frame) => SendAutomationAsync(frame.Command);

    internal async Task SendAutomationAsync(string command)
    {
        if (_stopping || _process.HasExited)
            throw new InvalidOperationException("Virtual controller bridge is no longer running.");
        if (IsHybridMode)
        {
            var message = JsonNode.Parse(command)?.AsObject();
            if (message is not null && message["slot"]?.GetValue<int>() is int runtimeSlot &&
                _hybridOutputForRuntime.TryGetValue(runtimeSlot, out var outputSlot) &&
                runtimeSlot != outputSlot)
            {
                message["slot"] = outputSlot;
                command = message.ToJsonString();
            }
        }
        await WriteCommandAsync(command);
    }

    internal Task SendHybridKeyAsync(int virtualKey, bool down)
    {
        if (!IsHybridMode || _stopping || _process.HasExited)
            throw new InvalidOperationException("Keyboard bridge is not running.");
        return WriteCommandAsync(JsonSerializer.Serialize(new
        {
            type = "hybrid_key", key = virtualKey, down
        }));
    }

    internal async Task EnableHybridMainFollowMirrorAsync()
    {
        if (!IsHybridMode || _stopping || _process.HasExited || _pendingHybridPairingComplete is not null)
            throw new InvalidOperationException("Keyboard bridge is not ready to complete pairing.");
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref _pendingHybridPairingComplete, pending);
        try
        {
            await WriteCommandAsync(JsonSerializer.Serialize(new { type = "hybrid_pairing_complete" }));
            await pending.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally { Volatile.Write(ref _pendingHybridPairingComplete, null); }
    }

    internal string GetMenuGuardState(int slot) => _menuStates.TryGetValue(slot, out var state) ? state : "clear";

    internal async Task SendFlaskAsync(int slot, string resource)
    {
        if (!AutoFlaskEnabled || !_allSlots.Contains(slot) || resource is not ("life" or "mana") ||
            _stopping || _process.HasExited)
            throw new InvalidOperationException("Automatic flask input is unavailable for this pad.");
        await WriteCommandAsync(JsonSerializer.Serialize(new { type = "flask", slot, resource }));
    }

    internal Task BeginAutoPartyAsync(CancellationToken token) =>
        SendAutoPartyControlAsync("auto_party_begin", 0, 0, 0, token);

    internal Task RenewAutoPartyAsync(CancellationToken token) =>
        SendAutoPartyControlAsync("auto_party_renew", 0, 0, 0, token);

    internal Task PulseAutoPartyAsync(int slot, ushort buttons, int holdMilliseconds, CancellationToken token)
    {
        if (IsHybridMode && _hybridOutputForRuntime.TryGetValue(slot, out var outputSlot))
            slot = outputSlot;
        if (!_allSlots.Contains(slot) ||
            (IsHybridMode && slot != 1 && !_hybridOutputForRuntime.Values.Contains(slot)) ||
            !ValidAutoPartyPulse(buttons, holdMilliseconds))
            throw new ArgumentOutOfRangeException(nameof(slot), "Auto Party requires an assigned menu pad and a target-version pulse.");
        return SendAutoPartyControlAsync("auto_party_pulse", slot, buttons, holdMilliseconds, token);
    }

    internal Task EndAutoPartyAsync(CancellationToken token) =>
        SendAutoPartyControlAsync("auto_party_end", 0, 0, 0, token);

    internal static bool ValidAutoPartyPulse(ushort buttons, int duration) => buttons switch
    {
        0x0010 => duration is 80 or 100,
        0x0004 or 0x1000 => duration == 80,
        0x2000 => duration == 60,
        _ => false
    };

    private async Task SendAutoPartyControlAsync(string type, int slot, ushort buttons,
        int holdMilliseconds, CancellationToken token)
    {
        await _partyGate.WaitAsync(token);
        try
        {
            if (_stopping || _process.HasExited)
                throw new InvalidOperationException("Virtual controller bridge is no longer running.");
            var pending = new AutoPartyPending(type, slot, buttons,
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            Volatile.Write(ref _pendingAutoParty, pending);
            var command = type == "auto_party_pulse"
                ? JsonSerializer.Serialize(new { type, slot, buttons, hold_ms = holdMilliseconds })
                : JsonSerializer.Serialize(new { type });
            await WriteCommandAsync(command, token);
            await pending.Completion.Task.WaitAsync(TimeSpan.FromSeconds(2), token);
        }
        finally
        {
            Volatile.Write(ref _pendingAutoParty, null);
            _partyGate.Release();
        }
    }

    private async Task WriteCommandAsync(string command, CancellationToken token = default)
    {
        await _inputGate.WaitAsync(token);
        try
        {
            if (_stopping || _process.HasExited)
                throw new InvalidOperationException("Virtual controller bridge is no longer running.");
            await _process.StandardInput.WriteLineAsync(command);
            await _process.StandardInput.FlushAsync();
        }
        finally { _inputGate.Release(); }
    }

    private async Task ReadEventsAsync()
    {
        string? failure = null;
        try
        {
            string? line;
            while ((line = await _process.StandardOutput.ReadLineAsync()) is not null)
            {
                try
                {
                    using var json = JsonDocument.Parse(line);
                    var root = json.RootElement;
                    if (!root.TryGetProperty("event", out var kind) || kind.ValueKind != JsonValueKind.String)
                        continue;
                    if (kind.GetString() == "a_release" &&
                        root.TryGetProperty("slot", out var value) && value.TryGetInt32(out var slot))
                    {
                        Dispatcher.UIThread.Post(() => AReleased?.Invoke(slot));
                    }
                    else if (kind.GetString() == "pairing_armed" &&
                        root.TryGetProperty("slot", out var armedSlot) && armedSlot.TryGetInt32(out var armedSlotValue) &&
                        root.TryGetProperty("pid", out var armedPid) && armedPid.TryGetInt32(out var pid) &&
                        root.TryGetProperty("window_handle", out var armedWindow) && armedWindow.TryGetInt64(out var handle))
                    {
                        _menuStates.Clear();
                        Dispatcher.UIThread.Post(() => PairingArmed?.Invoke(armedSlotValue, pid, handle));
                    }
                    else if (kind.GetString() == "menu_guard" &&
                        root.TryGetProperty("slot", out var menuSlot) && menuSlot.TryGetInt32(out var menuSlotValue) &&
                        _allSlots.Contains(menuSlotValue) &&
                        root.TryGetProperty("state", out var menuState) && menuState.ValueKind == JsonValueKind.String &&
                        menuState.GetString() is "clear" or "menu_detected" or "settling")
                    {
                        var state = menuState.GetString()!;
                        _menuStates[menuSlotValue] = state;
                        Dispatcher.UIThread.Post(() => MenuGuardChanged?.Invoke(menuSlotValue, state));
                    }
                    else if (kind.GetString() == "pairing_complete")
                    {
                        _menuStates.Clear();
                        Dispatcher.UIThread.Post(() => PairingCompleted?.Invoke());
                    }
                    else if (kind.GetString() == "focus_changed" &&
                        root.TryGetProperty("slot", out var focusedSlot) &&
                        focusedSlot.TryGetInt32(out var focusedPad) && _allSlots.Contains(focusedPad))
                        Volatile.Write(ref _lastFocusedSlot, focusedPad);
                    else if (kind.GetString() == "manual_focus" &&
                        root.TryGetProperty("slot", out var manualSlot) &&
                        manualSlot.TryGetInt32(out var manualPad) && _allSlots.Contains(manualPad))
                    {
                        Volatile.Write(ref _lastManualFocusedSlot, manualPad);
                        // Live follow only enqueues this event; dispatching through the UI loop can
                        // stall a controller process that is waiting for the focus command.
                        ManualFocusChanged?.Invoke(manualPad);
                    }
                    else if (kind.GetString() == "hybrid_focus_changed" && IsHybridMode &&
                        root.TryGetProperty("player", out var hybridPlayer) &&
                        hybridPlayer.TryGetInt32(out var player) && player is >= 1 and <= 6 &&
                        root.TryGetProperty("pid", out var hybridPid) &&
                        hybridPid.TryGetInt32(out var hybridPidValue) && hybridPidValue > 0 &&
                        root.TryGetProperty("window_handle", out var hybridWindow) &&
                        hybridWindow.TryGetInt64(out var hybridHandleValue) && hybridHandleValue > 0)
                        Dispatcher.UIThread.Post(() => HybridFocusChanged?.Invoke(player, hybridPidValue, hybridHandleValue));
                    else if (kind.GetString() == "hybrid_confirm_released" && IsHybridMode &&
                        root.TryGetProperty("player", out var confirmPlayer) &&
                        confirmPlayer.TryGetInt32(out var confirmedPlayer) && confirmedPlayer is >= 2 and <= 6 &&
                        root.TryGetProperty("pid", out var confirmPid) &&
                        confirmPid.TryGetInt32(out var confirmedPid) && confirmedPid > 0 &&
                        root.TryGetProperty("window_handle", out var confirmWindow) &&
                        confirmWindow.TryGetInt64(out var confirmedHandle) && confirmedHandle > 0)
                        Dispatcher.UIThread.Post(() => HybridConfirmReleased?.Invoke(
                            confirmedPlayer, confirmedPid, confirmedHandle));
                    else if (kind.GetString() == "login_status" &&
                        root.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.String &&
                        source.GetString() is { } sourceName &&
                        sourceName is "waiting" or "visual" or "fallback_timeout")
                        Dispatcher.UIThread.Post(() => LoginStatusChanged?.Invoke(sourceName));
                    else if (kind.GetString() == "auto_party_control" &&
                        root.TryGetProperty("type", out var partyType) && partyType.ValueKind == JsonValueKind.String &&
                        root.TryGetProperty("slot", out var partySlot) && partySlot.TryGetInt32(out var ackSlot) &&
                        root.TryGetProperty("buttons", out var partyButtons) && partyButtons.TryGetUInt16(out var ackButtons))
                    {
                        var pending = Volatile.Read(ref _pendingAutoParty);
                        if (pending is not null && partyType.GetString() == pending.Type &&
                            ackSlot == pending.Slot && ackButtons == pending.Buttons)
                            pending.Completion.TrySetResult();
                    }
                    else if (kind.GetString() == "hybrid_pairing_complete" && IsHybridMode)
                        Volatile.Read(ref _pendingHybridPairingComplete)?.TrySetResult();
                    else if (kind.GetString() == "auto_party_expired")
                    {
                        Volatile.Read(ref _pendingAutoParty)?.Completion.TrySetException(
                            new InvalidOperationException("Auto Party controller ownership expired."));
                        Dispatcher.UIThread.Post(() => AutoPartyExpired?.Invoke());
                    }
                }
                catch (Exception error) when (error is JsonException or InvalidOperationException)
                {
                    // An unrecognized diagnostic line is not a pairing event.
                }
            }
        }
        catch (Exception error) { failure = error.Message; }
        finally
        {
            Volatile.Read(ref _pendingAutoParty)?.Completion.TrySetException(
                new InvalidOperationException("Virtual controller bridge exited."));
            Volatile.Read(ref _pendingHybridPairingComplete)?.TrySetException(
                new InvalidOperationException("Keyboard bridge exited before pairing completion."));
            if (!_stopping)
            {
                Dispatcher.UIThread.Post(() => Ended?.Invoke(failure ?? "Virtual controller bridge exited."));
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stopping = true;
        await StopProcessAsync(_process);
        await _readEvents;
        await _readError;
        _process.Dispose();
    }

    private static async Task StopProcessAsync(Process process)
    {
        if (process.HasExited) return;
        try { process.StandardInput.Close(); } catch (InvalidOperationException) { }
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (TimeoutException)
        {
            if (!process.HasExited) process.Kill();
            await process.WaitForExitAsync();
        }
    }

    public static string? FindExecutable()
    {
        const string file = "PartyPilot.Bridge.exe";
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "bridge", file),
            Path.Combine(AppContext.BaseDirectory, "..", "bridge", file),
            Path.Combine(Environment.CurrentDirectory, "release", "bridge", file)
        };
        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
    }
}
