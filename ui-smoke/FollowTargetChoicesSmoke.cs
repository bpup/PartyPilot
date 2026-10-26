using PartyPilot.Desktop;

internal static class FollowTargetChoicesSmoke
{
    internal static void Run()
    {
        var three = PartyTopologies.ForPlayerCount(3)
            .Single(topology => topology.Id == "3-three-singles");
        if (!FollowTargetChoices.For(three, 2).SequenceEqual(
                ["Current MAIN", "P1", "P3"]) ||
            !FollowTargetChoices.TryRead(three, 2, "P3", out var target) || target != 3 ||
            !FollowTargetChoices.TryRead(three, 2, "Current MAIN", out target) || target != 0 ||
            FollowTargetChoices.TryRead(three, 2, "P2", out _) ||
            FollowTargetChoices.TryRead(three, 2, "P4", out _))
            throw new Exception("Three-client follow choices did not match active other players.");
        var split = PartyTopologies.ForPlayerCount(2)
            .Single(topology => topology.Id == "2-split");
        if (!FollowTargetChoices.For(split, 1).SequenceEqual(["Current MAIN", "P2"]) ||
            FollowTargetChoices.For(split, 3).Length != 0)
            throw new Exception("Two-client follow choices included an inactive player.");
    }
}
