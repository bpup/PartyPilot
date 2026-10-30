using PartyPilot.Desktop;

internal static class LiveMemberNameSelectionSmoke
{
    internal static void Run()
    {
        var four = PartyTopologies.ForPlayerCount(4).Single(item => item.Id == "4-balanced");
        var names = LiveMemberNameSelection.TryCreate(four, ["Leader", "Second", "Third", "Fourth"], "");
        if (names is null || names.Count != 4 || names[1] != "Leader" ||
            names[2] != "Second" || names[3] != "Third" || names[4] != "Fourth" ||
            LiveMemberNameSelection.TryCreate(four, ["Leader", "", "Third", "Fourth"], "") is not null ||
            LiveMemberNameSelection.TryCreate(four, ["Leader", "Leader", "Third", "Fourth"], "") is not null)
            throw new Exception("Dual-local follow copied one client invite name to two roles.");
        var overrideNames = LiveMemberNameSelection.TryCreate(four,
            ["", "Second", "Third", "Fourth"], "Leader");
        if (overrideNames is null || overrideNames.Count != 3 ||
            overrideNames.ContainsKey(1) || overrideNames[2] != "Second")
            throw new Exception("Explicit leader name was overwritten by an invite field.");
        var six = PartyTopologies.ForPlayerCount(6).Single(item => item.Id == "6-balanced");
        if (LiveMemberNameSelection.TryCreate(six,
                ["Leader", "Second", "Third", "Fourth", "Fifth", ""], "") is not null)
            throw new Exception("A six-role follow used incomplete names for departure detection.");
    }
}
