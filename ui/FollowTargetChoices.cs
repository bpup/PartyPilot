namespace PartyPilot.Desktop;

internal static class FollowTargetChoices
{
    internal const string CurrentMain = "当前主控";

    internal static string[] For(PartyTopology topology, int player)
    {
        var active = topology.Slots.Select(slot => slot.PartyPlayer).ToArray();
        if (!active.Contains(player)) return [];
        return [CurrentMain, .. active.Where(candidate => candidate != player)
            .Select(candidate => $"P{candidate}")];
    }

    internal static bool TryRead(PartyTopology topology, int player, string? choice, out int target)
    {
        target = 0;
        if (!For(topology, player).Contains(choice)) return false;
        if (choice == CurrentMain) return true;
        return int.TryParse(choice![1..], out target);
    }

    internal static string Normalize(PartyTopology topology, int player, int target)
    {
        var choice = $"P{target}";
        return For(topology, player).Contains(choice) ? choice : CurrentMain;
    }
}
