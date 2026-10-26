using PartyPilot.Desktop;

internal static class ControllerLaunchPreflightSmoke
{
    internal static async Task RunAsync()
    {
        if (OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "partypilot-startup-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var executable = Path.Combine(root, "fake-bridge");
            var arguments = Path.Combine(root, "arguments");
            await File.WriteAllTextAsync(executable,
                "#!/bin/sh\nprintf '%s\\n' \"$@\" > '" + arguments + "'\n" +
                "echo '{\"startup_preflight_ready\":true,\"isolation_matches\":true,\"physical_source_readable\":true}'\n");
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var result = await ControllerLaunchPreflight.CheckAsync(executable, "HID\\DEVICE",
                ["/game/PathOfExile.exe", "/second/PathOfExile.exe"], 2, false);
            var actual = await File.ReadAllLinesAsync(arguments);
            if (!result.Ready || !actual.SequenceEqual(new[]
                {
                    "--check-startup", "--device", "HID\\DEVICE", "--physical-index", "2",
                    "--game-exe", "/game/PathOfExile.exe", "--game-exe", "/second/PathOfExile.exe"
                }))
                throw new Exception("Desktop did not bind the selected physical input to every game executable before launch.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
