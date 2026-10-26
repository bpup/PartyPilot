namespace PartyPilot.Desktop;

internal static class AutoPartyInviterSelection
{
    // The supplied 0.6.17 default remembers the last manually focused player as MAIN.
    internal static int DefaultLeaderPlayer(PartyTopology topology, int lastManualFocusedPad,
        bool lastFocusedLeads) =>
        lastFocusedLeads
            ? topology.Slots.FirstOrDefault(slot => slot.RuntimePad == lastManualFocusedPad)?.PartyPlayer ?? 1
            : 1;

    // The supplied build checks its runtime leader first, then the focused
    // player, then the lowest stable client.
    internal static int Select(PartyTopology topology, IReadOnlySet<int> stableClients,
        int leaderPlayer, int focusedPad)
    {
        if (stableClients.Count == 0 || stableClients.Any(client => client < 1 || client > topology.ClientCount))
            throw new ArgumentException("At least one active stable client is required.");
        var leaderClient = topology.Slots.FirstOrDefault(slot => slot.PartyPlayer == leaderPlayer)?.Client ?? 0;
        if (stableClients.Contains(leaderClient)) return leaderClient;
        var focusClient = topology.Slots.FirstOrDefault(slot => slot.RuntimePad == focusedPad)?.Client ?? 0;
        return stableClients.Contains(focusClient) ? focusClient : stableClients.Min();
    }
}
