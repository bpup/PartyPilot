using PartyPilot.Desktop;

internal static class FollowPlayerStopSmoke
{
    internal static void Run()
    {
        if (!FollowPlayerStop.TryRead("", out var absent) || absent is not null ||
            !FollowPlayerStop.TryRead("37.25", out var chosen) || chosen != 37.25 ||
            FollowPlayerStop.TryRead("201", out _) ||
            FollowPlayerStop.TryRead("NaN", out _) ||
            FollowPlayerStop.TryRead("-1", out _))
            throw new Exception("Individual stop distance validation changed.");
    }
}
