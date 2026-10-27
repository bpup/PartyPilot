using PartyPilot.Desktop;

internal static class SessionGameClientShutdownSmoke
{
    internal static void Run()
    {
        var started = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var game = Path.GetFullPath("PathOfExile_x64Steam.exe");
        var expected = new SessionGameClientIdentity(120, 4,
            "PathOfExile_x64Steam", game, started);
        if (!SessionGameClientShutdown.Matches(expected, expected) ||
            SessionGameClientShutdown.Matches(expected, expected with { Pid = 121 }) ||
            SessionGameClientShutdown.Matches(expected, expected with { SessionId = 5 }) ||
            SessionGameClientShutdown.Matches(expected, expected with
                { ProcessName = "PathOfExile_x64EGS" }) ||
            SessionGameClientShutdown.Matches(expected, expected with
                { Executable = Path.GetFullPath("Other/PathOfExile_x64Steam.exe") }) ||
            SessionGameClientShutdown.Matches(expected, expected with
                { StartedUtc = started.AddSeconds(2) }) ||
            SessionGameClientShutdown.Matches(expected with { ProcessName = "notepad" }, expected))
            throw new Exception("Session shutdown accepted a replaced or unrelated process.");
    }
}
