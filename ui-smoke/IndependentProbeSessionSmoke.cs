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
                "while True:\n print('capture',flush=True); time.sleep(.01)\n");
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.UserExecute);
            await using (var session = await IndependentProbeSession.StartAsync(executable))
            {
                for (var attempt = 0; attempt < 20 && !File.Exists(arguments); attempt++)
                    await Task.Delay(100);
                if (!File.Exists(arguments) ||
                    File.ReadAllText(arguments) != "--watch --experimental-ordered-udp")
                    throw new Exception("The desktop did not start the independent probe in ordered mode.");
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
