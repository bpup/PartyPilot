using PartyPilot.Desktop;

internal static class IndependentProbeSessionSmoke
{
    internal static async Task RunAsync()
    {
        if (OperatingSystem.IsWindows()) return; // Fixture uses a Unix shebang.
        var directory = Path.Combine(Path.GetTempPath(),
            "partypilot-probe-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "fake-probe");
        var arguments = Path.Combine(directory, "arguments.txt");
        try
        {
            File.WriteAllText(executable, "#!/usr/bin/env python3\n" +
                "import sys,time\n" +
                $"open({System.Text.Json.JsonSerializer.Serialize(arguments)},'w').write(' '.join(sys.argv[1:]))\n" +
                "print('{\"pid\":123,\"status\":\"unknown_build_unverified\",\"recognizedBuild\":false}',flush=True)\n" +
                "print('{\"pid\":124,\"status\":\"attach_failed\"}',flush=True)\n" +
                "time.sleep(.5)\n" +
                "print('{\"pid\":\"bad\",\"recognizedBuild\":true}',flush=True)\n" +
                "print('{\"pid\":123,\"recognizedBuild\":true}',flush=True)\n" +
                "print('{\"pid\":124,\"recognizedBuild\":true}',flush=True)\n" +
                "while True:\n print('capture',flush=True); time.sleep(.01)\n");
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.UserExecute);
            await using (var session = await IndependentProbeSession.StartAsync(executable))
            {
                var changes = 0;
                session.DiagnosticsChanged += () => Interlocked.Increment(ref changes);
                for (var attempt = 0; attempt < 20 && !File.Exists(arguments); attempt++)
                    await Task.Delay(100);
                if (!File.Exists(arguments) ||
                    File.ReadAllText(arguments) != "--watch --experimental-ordered-udp")
                    throw new Exception("The desktop did not start the independent probe in ordered mode.");
                for (var attempt = 0; attempt < 20 && !session.HasUnknownBuild(123); attempt++)
                    await Task.Delay(100);
                if (!session.IsRunning || !session.HasUnknownBuild(123) ||
                    session.HasUnknownBuild(124) || !session.HasReadFailure(124))
                    throw new Exception("Unknown game build was not distinguished from a running probe.");
                for (var attempt = 0; attempt < 20 && session.HasUnknownBuild(123); attempt++)
                    await Task.Delay(100);
                if (session.HasUnknownBuild(123) || session.HasReadFailure(124) || changes == 0)
                    throw new Exception("Probe build changes did not refresh diagnostics.");
                var shared = await IndependentProbeSession.StartAsync(executable);
                if (!ReferenceEquals(session, shared))
                    throw new Exception("Radar and follow opened duplicate probe processes.");
                await session.DisposeAsync();
                if (!shared.IsRunning)
                    throw new Exception("Releasing one probe owner stopped the other owner.");
                await shared.DisposeAsync();
            }
            File.WriteAllText(executable, "#!/usr/bin/env python3\n" +
                "import sys\nprint('probe rejected startup',file=sys.stderr,flush=True)\nsys.exit(3)\n");
            try
            {
                await IndependentProbeSession.StartAsync(executable);
                throw new Exception("A failed probe was accepted as running.");
            }
            catch (InvalidOperationException error) when (
                error.Message.Contains("probe rejected startup", StringComparison.Ordinal)) { }
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }
}
