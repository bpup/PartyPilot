using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using PartyPilot.Probe;

if (args is ["--self-test"])
{
    SelfTest.Run();
    Console.WriteLine("scanner self-test passed");
    return;
}

if (args is not ([] or ["--watch"] or ["--watch", "--experimental-ordered-udp"]))
{
    Console.Error.WriteLine("Usage: PartyPilot.Probe [--watch [--experimental-ordered-udp]|--self-test]");
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
var watch = args is ["--watch"];
var emitOrdered = args is ["--watch", "--experimental-ordered-udp"];
watch |= emitOrdered;
using var orderedUdp = emitOrdered ? new UdpClient() : null;
var orderedEndpoint = new IPEndPoint(IPAddress.Loopback, 47617);
var radarEndpoint = new IPEndPoint(IPAddress.Loopback, 47618);
var sessions = new Dictionary<int, DiagnosticSession>();
var reportedEmpty = false;
void Discover()
{
    foreach (var process in Process.GetProcesses())
    {
        try
        {
            if (!names.Contains(process.ProcessName) || sessions.ContainsKey(process.Id))
            {
                process.Dispose();
                continue;
            }
            try
            {
                sessions.Add(process.Id, DiagnosticSession.Open(process));
                reportedEmpty = false;
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
        catch (Exception error) when (error is InvalidOperationException or Win32Exception)
        {
            process.Dispose(); // Process exited or became inaccessible.
        }
    }
    if (sessions.Count == 0 && !reportedEmpty)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { status = "game_not_running" }));
        reportedEmpty = true;
    }
}

try
{
    Discover();
    if (!watch && sessions.Count == 0) return;
    using var cancellation = new CancellationTokenSource();
    if (watch) Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };
    var lastDiscovery = DateTime.UtcNow;
    var diagnosticClock = Stopwatch.StartNew();
    var nextDiagnosticAt = TimeSpan.Zero;
    do
    {
        var captureFull = !emitOrdered || diagnosticClock.Elapsed >= nextDiagnosticAt;
        foreach (var (pid, session) in sessions.OrderBy(entry => entry.Key).ToArray())
        {
            try
            {
                if (captureFull)
                {
                    Console.WriteLine(JsonSerializer.Serialize(session.Capture()));
                    if (orderedUdp is not null && session.LastOrderedFrame is { } packet)
                        await orderedUdp.SendAsync(packet, orderedEndpoint);
                    if (orderedUdp is not null && session.LastRadarFrame is { } radar)
                        await orderedUdp.SendAsync(radar, radarEndpoint);
                    if (orderedUdp is not null && session.LastRadarMotionFrame is { } radarMotion)
                        await orderedUdp.SendAsync(radarMotion, radarEndpoint);
                }
                else if (orderedUdp is not null && session.CaptureMotion() is { } motion)
                {
                    await orderedUdp.SendAsync(motion, orderedEndpoint);
                    if (session.LastRadarMotionFrame is { } radarMotion)
                        await orderedUdp.SendAsync(radarMotion, radarEndpoint);
                }
                if (session.HasExited)
                {
                    sessions.Remove(pid);
                    session.Dispose();
                }
            }
            catch (Exception error) when (error is InvalidOperationException or Win32Exception or
                                          IOException or UnauthorizedAccessException)
            {
                sessions.Remove(pid);
                session.Dispose();
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    pid, status = "capture_failed", error = error.Message
                }));
            }
        }
        if (captureFull) nextDiagnosticAt = diagnosticClock.Elapsed.Add(TimeSpan.FromMilliseconds(100));
        if (!watch || cancellation.IsCancellationRequested) break;
        if (DateTime.UtcNow - lastDiscovery >= TimeSpan.FromSeconds(2))
        {
            Discover();
            lastDiscovery = DateTime.UtcNow;
        }
        try { await Task.Delay(emitOrdered ? 20 : 100, cancellation.Token); }
        catch (OperationCanceledException) { break; }
    } while (!cancellation.IsCancellationRequested);
}
finally
{
    foreach (var session in sessions.Values) session.Dispose();
}
