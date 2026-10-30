using System.Diagnostics;
using PartyPilot.Bridge;

internal static class RemoteAttachSmoke
{
    internal static async Task Run(string targetExecutable, string runtimeDll)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("Remote attach smoke requires Windows x64.");
        targetExecutable = Path.GetFullPath(targetExecutable);
        runtimeDll = Path.GetFullPath(runtimeDll);
        if (!File.Exists(targetExecutable) || !File.Exists(runtimeDll))
            throw new FileNotFoundException("Remote attach smoke binaries are missing.");
        var nativeDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "native"));
        Directory.CreateDirectory(nativeDir);
        File.Copy(runtimeDll, Path.Combine(nativeDir, "PartyPilot.PrivateSlots.dll"), true);

        using var target = Process.Start(new ProcessStartInfo(targetExecutable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Could not start remote attach target.");
        try
        {
            var first = await ReadLine(target, TimeSpan.FromSeconds(10));
            if (!first.StartsWith("WINDOW ", StringComparison.Ordinal) ||
                !long.TryParse(first.AsSpan(7), out var handle) || handle <= 0)
                throw new InvalidOperationException($"Remote target did not expose its window: {first}");
            using (var attached = await Client3RuntimeAttach.AttachAsync(
                target.Id, handle, targetExecutable, includeP6: true))
            {
                if (!attached.IsReady || !attached.Submit(
                    new PadReport(0x1000, 0, 0, 0, 0, 0, 0),
                    new PadReport(0x0020, 0, 0, 0, 0, 0, 0)))
                    throw new InvalidOperationException("Remote P5/P6 publisher was not ready.");
                await ExpectBoth(target, 0, 0x1000, 0, 0x0020);
                attached.NeutralAll();
                await ExpectBoth(target, 0, 0, 0, 0);
            }
            await ExpectBoth(target, 1167, 0, 1167, 0);
            for (var attempt = 0; attempt < 4; attempt++)
            {
                using (var reattached = await Client3RuntimeAttach.AttachAsync(
                    target.Id, handle, targetExecutable, includeP6: true))
                {
                    if (!reattached.IsReady || !reattached.Submit(
                        new PadReport(0x2000, 0, 0, 0, 0, 0, 0),
                        new PadReport(0x0040, 0, 0, 0, 0, 0, 0)))
                        throw new InvalidOperationException($"Remote P5/P6 reattach {attempt + 1} was not ready.");
                    await ExpectBoth(target, 0, 0x2000, 0, 0x0040);
                }
                await ExpectBoth(target, 1167, 0, 1167, 0);
            }
            Console.WriteLine("Remote Client 3 attach, P5/P6 input, neutral, stop and four reattaches passed.");
        }
        finally
        {
            if (!target.HasExited) target.Kill(entireProcessTree: true);
            await target.WaitForExitAsync();
        }
    }

    private static async Task ExpectBoth(Process target, int status0, int buttons0,
        int status1, int buttons1)
    {
        var expected0 = $"PAD 0 {status0} {buttons0}";
        var expected1 = $"PAD 1 {status1} {buttons1}";
        var seen0 = false;
        var seen1 = false;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!seen0 || !seen1)
        {
            var line = await target.StandardOutput.ReadLineAsync(timeout.Token);
            if (line is null) throw new InvalidOperationException("Remote target exited before " + expected0 + " / " + expected1);
            if (line == expected0) seen0 = true;
            if (line == expected1) seen1 = true;
        }
    }

    private static async Task<string> ReadLine(Process target, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        return await target.StandardOutput.ReadLineAsync(cancellation.Token)
            ?? throw new InvalidOperationException("Remote target exited before opening a window.");
    }
}
