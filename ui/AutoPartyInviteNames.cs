namespace PartyPilot.Desktop;

internal sealed class AutoPartyInviteNames
{
    private readonly IReadOnlyDictionary<int, string> _clientNames;

    private AutoPartyInviteNames(IReadOnlyDictionary<int, string> clientNames) => _clientNames = clientNames;

    internal string ForClient(int client) => _clientNames[client];

    // The supplied build chooses the first non-empty P1-P6 invite name for each active client.
    internal static bool TryCreate(PartyTopology topology, IReadOnlyList<string?> playerNames,
        out AutoPartyInviteNames? names)
    {
        names = null;
        if (playerNames.Count != topology.PlayerCount) return false;
        var selected = new Dictionary<int, string>();
        for (var client = 1; client <= topology.ClientCount; client++)
        {
            var name = topology.Clients[client - 1]
                .Select(player => playerNames[player - 1]?.Trim() ?? string.Empty)
                .FirstOrDefault(value => value.Length > 0);
            if (!PoeChatInviteService.IsValidInviteName(name)) return false;
            selected.Add(client, name!);
        }
        if (selected.Values.Distinct(StringComparer.Ordinal).Count() != selected.Count) return false;
        names = new AutoPartyInviteNames(selected);
        return true;
    }
}
