namespace PartyPilot.Desktop;

internal static class LiveMemberNameSelection
{
    internal static IReadOnlyDictionary<int, string>? TryCreate(PartyTopology topology,
        IReadOnlyList<string?> playerNames, string leaderName)
    {
        if (playerNames.Count != topology.PlayerCount) return null;
        var selected = new Dictionary<int, string>();
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slot in topology.Slots)
        {
            var name = slot.PartyPlayer == 1 && !string.IsNullOrWhiteSpace(leaderName)
                ? leaderName.Trim()
                : playerNames[slot.PartyPlayer - 1]?.Trim() ?? string.Empty;
            if (!PoeChatInviteService.IsValidInviteName(name) || !unique.Add(name)) return null;
            if (slot.PartyPlayer != 1 || string.IsNullOrWhiteSpace(leaderName))
                selected.Add(slot.PartyPlayer, name);
        }
        return selected;
    }
}
