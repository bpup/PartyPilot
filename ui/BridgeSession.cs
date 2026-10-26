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
    public event Action<string>? Ended;

    private BridgeSession(Process process, Task<string> readError)
    {
        _process = process;
        _readError = readError;
    }

    public static async Task<BridgeSession> StartAsync(string executable, int[] slots, int physicalIndex)
    {
        if (slots.Length is < 1 or > 3 || slots.Any(slot => slot is < 1 or > 4) ||
            slots.Distinct().Count() != slots.Length || physicalIndex is < 0 or > 3)
        {
            throw new ArgumentException("The current physical controller bridge supports 1–3 global pads P1–P4.");
        }
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
            "--physical-index", physicalIndex.ToString(), "--main-slot", slots[0].ToString(),
            "--physical-hidden-confirmed"
        })
        {
            start.ArgumentList.Add(argument);
        }
        var process = Process.Start(start) ?? throw new InvalidOperationException("Virtual controller bridge did not start.");
        var readError = process.StandardError.ReadToEndAsync();
        try
        {
            var readyLine = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
            if (readyLine is null || !IsExpectedReady(readyLine, slots))
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

    private static bool IsExpectedReady(string line, int[] slots)
    {
        try
        {
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            return root.TryGetProperty("event", out var kind) && kind.GetString() == "ready" &&
                   root.TryGetProperty("physical_mode", out var mode) && mode.GetBoolean() &&
                   root.TryGetProperty("slots", out var selected) &&
                   selected.EnumerateArray().Select(value => value.GetInt32()).SequenceEqual(slots.Order());
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    public async Task FocusAsync(int slot)
    {
        if (_stopping || _process.HasExited)
        {
            throw new InvalidOperationException("Virtual controller bridge is no longer running.");
        }
        await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { type = "focus", slot }));
        await _process.StandardInput.FlushAsync();
    }

    private async Task ReadEventsAsync()
    {
        try
        {
            string? line;
            while ((line = await _process.StandardOutput.ReadLineAsync()) is not null)
            {
                try
                {
                    using var json = JsonDocument.Parse(line);
                    var root = json.RootElement;
                    if (root.TryGetProperty("event", out var kind) && kind.GetString() == "a_release" &&
                        root.TryGetProperty("slot", out var value) && value.TryGetInt32(out var slot))
                    {
                        Dispatcher.UIThread.Post(() => AReleased?.Invoke(slot));
                    }
                }
                catch (JsonException)
                {
                    // An unrecognized diagnostic line is not a pairing event.
                }
            }
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException) { }
        finally
        {
            if (!_stopping)
            {
                Dispatcher.UIThread.Post(() => Ended?.Invoke("Virtual controller bridge exited."));
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
