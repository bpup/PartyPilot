using PartyPilot.Desktop;

internal static class FollowTargetChoicesSmoke
{
    internal static void Run()
    {
        var three = PartyTopologies.ForPlayerCount(3)
            .Single(topology => topology.Id == "3-three-singles");
        if (!FollowTargetChoices.For(three, 2).SequenceEqual(
                ["当前主控", "P1", "P3"]) ||
            !FollowTargetChoices.TryRead(three, 2, "P3", out var target) || target != 3 ||
            !FollowTargetChoices.TryRead(three, 2, "当前主控", out target) || target != 0 ||
            FollowTargetChoices.TryRead(three, 2, "P2", out _) ||
            FollowTargetChoices.TryRead(three, 2, "P4", out _))
            throw new Exception("Three-client follow choices did not match active other players.");
        var split = PartyTopologies.ForPlayerCount(2)
            .Single(topology => topology.Id == "2-split");
        if (!FollowTargetChoices.For(split, 1).SequenceEqual(["当前主控", "P2"]) ||
            FollowTargetChoices.For(split, 3).Length != 0 ||
            FollowTargetChoices.Normalize(split, 1, 3) != "当前主控" ||
            FollowTargetChoices.Normalize(split, 2, 2) != "当前主控" ||
            FollowTargetChoices.Normalize(three, 2, 3) != "P3")
            throw new Exception("Two-client follow choices included an inactive player.");
        var six = PartyTopologies.ForPlayerCount(6)
            .Single(topology => topology.Id == "6-balanced");
        if (!FollowTargetChoices.For(six, 6).SequenceEqual(
                ["当前主控", "P1", "P2", "P3", "P4", "P5"]) ||
            !FollowTargetChoices.TryRead(six, 6, "P5", out target) || target != 5)
            throw new Exception("Six-player follow choices did not include every other active player.");
    }
}
