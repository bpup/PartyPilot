using System.Diagnostics;
using System.Collections.Concurrent;
using System.Text.Json;
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
    private readonly SemaphoreSlim _inputGate = new(1, 1);
    private readonly SemaphoreSlim _partyGate = new(1, 1);
    private AutoPartyPending? _pendingAutoParty;
    private int _lastFocusedSlot;
    private int _lastManualFocusedSlot;
    private bool _stopping;

    public bool AutoFlaskEnabled { get; }
    internal bool LastFocusedLeadsEnabled { get; }
    internal int LastFocusedSlot => Volatile.Read(ref _lastFocusedSlot);
    internal int LastManualFocusedSlot => Volatile.Read(ref _lastManualFocusedSlot);

    public event Action<int>? AReleased;
    public event Action<int, int, long>? PairingArmed;
    public event Action? PairingCompleted;
    public event Action<string>? LoginStatusChanged;
    public event Action<int, string>? MenuGuardChanged;
    public event Action<string>? Ended;
    public event Action? AutoPartyExpired;
    public event Action<int>? ManualFocusChanged;

    private sealed record AutoPartyPending(string Type, int Slot, ushort Buttons,
        TaskCompletionSource Completion);

    private BridgeSession(Process process, Task<string> readError, bool autoFlaskEnabled,
        bool lastFocusedLeads,
        int[] slots, int[] allSlots)
    {
        _process = process;
        _readError = readError;
        AutoFlaskEnabled = autoFlaskEnabled;
        LastFocusedLeadsEnabled = lastFocusedLeads;
        _slots = slots.ToHashSet();
        _allSlots = allSlots.ToHashSet();
        _lastFocusedSlot = slots[0];
        _lastManualFocusedSlot = slots[0];
    }

    public static async Task<BridgeSession> StartAsync(string executable, IReadOnlyList<PartySlot> partySlots, int physicalIndex,
        string deviceInstance, IReadOnlyList<string> gameExecutables, bool rightStickFollowerOverride,
        bool autoFlaskEnabled = false, bool useWgi = false, PrivateClient3Plan? privatePlan = null,
        bool lastFocusedLeads = false)
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
                lastFocusedLeads))
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

    internal static bool IsExpectedReady(string line, int[] slots, string focusPadMap,
        bool rightStickFollowerOverride, int directRightStickSlot, bool autoFlaskEnabled, bool useWgi,
        PrivateClient3Plan? privatePlan, bool lastFocusedLeads = false)
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

    internal async Task SendAutomationAsync(FollowBridgeFrame frame)
    {
        if (_stopping || _process.HasExited)
            throw new InvalidOperationException("Virtual controller bridge is no longer running.");
        await WriteCommandAsync(frame.Command);
    }

    internal string GetMenuGuardState(int slot) => _menuStates.TryGetValue(slot, out var state) ? state : "clear";

    internal async Task SendFlaskAsync(int slot, string resource)
    {
        if (!AutoFlaskEnabled || !_slots.Contains(slot) || resource is not ("life" or "mana") ||
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
        if (!_allSlots.Contains(slot) || !ValidAutoPartyPulse(buttons, holdMilliseconds))
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
                        _slots.Contains(menuSlotValue) &&
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
                        Dispatcher.UIThread.Post(() => ManualFocusChanged?.Invoke(manualPad));
                    }
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
