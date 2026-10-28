using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using PartyPilot.Probe;

if (args is ["--self-test"])
{
    SelfTest.Run();
    Console.WriteLine("scanner self-test passed");
    return;
}

if (args is not ([] or ["--watch"]))
{
    Console.Error.WriteLine("Usage: PartyPilot.Probe [--watch|--self-test]");
    Environment.ExitCode = 2;
    return;
}
if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("独立探针只能在 Windows 上连接游戏；此电脑仅能运行扫描器自测。");
    Environment.ExitCode = 2;
    return;
}

var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "PathOfExile", "PathOfExile_x64", "PathOfExileSteam", "PathOfExile_x64Steam",
    "PathOfExileEGS", "PathOfExile_x64EGS"
};
var processes = new List<Process>();
foreach (var process in Process.GetProcesses())
{
    try
    {
        if (names.Contains(process.ProcessName)) processes.Add(process);
        else process.Dispose();
    }
    catch (InvalidOperationException)
    {
        process.Dispose(); // Process exited during enumeration.
    }
}
processes.Sort((left, right) => left.Id.CompareTo(right.Id));
if (processes.Count == 0)
{
    Console.WriteLine(JsonSerializer.Serialize(new { status = "game_not_running" }));
    return;
}

var sessions = new List<DiagnosticSession>();
try
{
    foreach (var process in processes)
    {
        try
        {
            sessions.Add(DiagnosticSession.Open(process));
        }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception or
                                      IOException or UnauthorizedAccessException)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                pid = process.Id, status = "attach_failed", error = error.Message
            }));
            process.Dispose();
        }
    }

    var watch = args is ["--watch"];
    using var cancellation = new CancellationTokenSource();
    if (watch) Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };
    var exited = new HashSet<int>();
    do
    {
        foreach (var session in sessions)
        {
            if (exited.Contains(session.ProcessId)) continue;
            try
            {
                Console.WriteLine(JsonSerializer.Serialize(session.Capture()));
                if (session.HasExited) exited.Add(session.ProcessId);
            }
            catch (Exception error) when (error is InvalidOperationException or Win32Exception or
                                          IOException or UnauthorizedAccessException)
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    pid = session.ProcessId, status = "capture_failed", error = error.Message
                }));
            }
        }
        if (!watch || cancellation.IsCancellationRequested || exited.Count == sessions.Count) break;
        try { await Task.Delay(100, cancellation.Token); }
        catch (OperationCanceledException) { break; }
    } while (!cancellation.IsCancellationRequested);
}
finally
{
    foreach (var session in sessions) session.Dispose();
}
