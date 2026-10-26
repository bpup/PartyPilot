using System.Diagnostics;
using System.Text.Json;
using Avalonia.Threading;

namespace PartyPilot.Desktop;

internal sealed class BridgeSession : IAsyncDisposable
{
    private readonly Process _process;
    private Task _readEvents = Task.CompletedTask;
    private readonly Task<string> _readError;
    private bool _stopping;

    public event Action<int>? AReleased;
    public event Action<int, int, long>? PairingArmed;
    public event Action? PairingCompleted;
    public event Action<string>? LoginStatusChanged;
    public event Action<string>? Ended;

    private BridgeSession(Process process, Task<string> readError)
    {
        _process = process;
        _readError = readError;
    }

    public static async Task<BridgeSession> StartAsync(string executable, IReadOnlyList<PartySlot> partySlots, int physicalIndex,
        string deviceInstance, IReadOnlyList<string> gameExecutables, bool rightStickFollowerOverride)
    {
        var slots = partySlots.Select(item => item.RuntimePad).ToArray();
        if (slots.Length is < 1 or > 3 || slots.Any(slot => slot is < 1 or > 4) ||
            slots.Distinct().Count() != slots.Length || physicalIndex is < 0 or > 3 ||
            !partySlots.Select(item => item.PartyPlayer).SequenceEqual(Enumerable.Range(1, slots.Length)) ||
            string.IsNullOrWhiteSpace(deviceInstance) || gameExecutables.Count is < 1 or > 3 ||
            gameExecutables.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("The current bridge needs 1–3 P1–P4 pads, a selected HidHide device, and 1–3 game paths.");
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
            "--physical-index", physicalIndex.ToString(), "--main-slot", slots[0].ToString(),
            "--physical-hidden-confirmed", "--pairing-protected", "--device-instance", deviceInstance
        })
        {
            start.ArgumentList.Add(argument);
        }
        foreach (var game in gameExecutables)
        {
            start.ArgumentList.Add("--game-exe");
            start.ArgumentList.Add(game);
        }
        if (rightStickFollowerOverride) start.ArgumentList.Add("--right-stick-follower-override");
        if (directRightStickSlot != 0)
        {
            start.ArgumentList.Add("--direct-right-stick-slot");
            start.ArgumentList.Add(directRightStickSlot.ToString());
        }
        var process = Process.Start(start) ?? throw new InvalidOperationException("Virtual controller bridge did not start.");
        var readError = process.StandardError.ReadToEndAsync();
        try
        {
            var readyLine = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
            if (readyLine is null || !IsExpectedReady(readyLine, slots, focusPadMap,
                rightStickFollowerOverride, directRightStickSlot))
            {
                throw new InvalidOperationException("Virtual controller bridge did not confirm the requested pad slots.");
            }
            return new BridgeSession(process, readError);
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

    private static bool IsExpectedReady(string line, int[] slots, string focusPadMap,
        bool rightStickFollowerOverride, int directRightStickSlot)
    {
        try
        {
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            return root.TryGetProperty("event", out var kind) && kind.GetString() == "ready" &&
                   root.TryGetProperty("physical_mode", out var mode) && mode.GetBoolean() &&
                   root.TryGetProperty("pairing_protected", out var protectedMode) && protectedMode.GetBoolean() &&
                   root.TryGetProperty("hidhide_preflight", out var preflight) && preflight.GetBoolean() &&
                   root.TryGetProperty("right_stick_follower_override", out var rightStick) &&
                   rightStick.GetBoolean() == rightStickFollowerOverride &&
                   root.TryGetProperty("direct_right_stick_slot", out var directSlot) &&
                   directSlot.GetInt32() == directRightStickSlot &&
                   root.TryGetProperty("focus_pad_map", out var focusMap) &&
                   focusMap.GetString() == focusPadMap &&
                   root.TryGetProperty("slots", out var selected) &&
                   selected.EnumerateArray().Select(value => value.GetInt32()).SequenceEqual(slots.Order());
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
        await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
        {
            type = "arm_pairing", slot, pid = client.Pid, window_handle = client.WindowHandle
        }));
        await _process.StandardInput.FlushAsync();
    }

    public async Task FinishPairingAsync()
    {
        if (_stopping || _process.HasExited)
        {
            throw new InvalidOperationException("Virtual controller bridge is no longer running.");
        }
        await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { type = "finish_pairing" }));
        await _process.StandardInput.FlushAsync();
    }

    internal async Task SendAutomationAsync(FollowBridgeFrame frame)
    {
        if (_stopping || _process.HasExited)
            throw new InvalidOperationException("Virtual controller bridge is no longer running.");
        await _process.StandardInput.WriteLineAsync(frame.Command);
        await _process.StandardInput.FlushAsync();
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
                        Dispatcher.UIThread.Post(() => PairingArmed?.Invoke(armedSlotValue, pid, handle));
                    else if (kind.GetString() == "pairing_complete")
                        Dispatcher.UIThread.Post(() => PairingCompleted?.Invoke());
                    else if (kind.GetString() == "login_status" &&
                        root.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.String &&
                        source.GetString() is { } sourceName &&
                        sourceName is "waiting" or "visual" or "fallback_timeout")
                        Dispatcher.UIThread.Post(() => LoginStatusChanged?.Invoke(sourceName));
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
