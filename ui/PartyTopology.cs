namespace PartyPilot.Desktop;

public sealed record PartySlot(int PartyPlayer, int Client, int LocalPlayer, int RuntimePad);

public sealed class PartyTopology
{
    public string Id { get; }
    public string Label { get; }
    public int PlayerCount { get; }
    public int ClientCount => Clients.Count;
    public IReadOnlyList<IReadOnlyList<int>> Clients { get; }
    public IReadOnlyList<PartySlot> Slots { get; }
    public string Summary => string.Join(" · ", Clients.Select((players, index) =>
        $"客户端 {index + 1}: {string.Join('/', players.Select(player => $"P{player}"))}"));
    public override string ToString() => Label;

    internal PartyTopology(string id, string label, int playerCount, params int[][] clients)
    {
        Id = id;
        Label = label;
        PlayerCount = playerCount;
        if (clients.Length is < 1 or > 3 || clients.Any(players => players.Length is < 1 or > 2))
        {
            throw new ArgumentException("A topology needs 1–3 clients with 1–2 players each.");
        }
        var partyPlayers = clients.SelectMany(players => players).ToArray();
        if (!partyPlayers.SequenceEqual(Enumerable.Range(1, playerCount)))
        {
            throw new ArgumentException("Party players must appear once in order from P1.");
        }
        Clients = clients.Select(players => (IReadOnlyList<int>)Array.AsReadOnly(players)).ToArray();
        Slots = clients.SelectMany((players, clientIndex) => players.Select((partyPlayer, localIndex) =>
            new PartySlot(partyPlayer, clientIndex + 1, localIndex + 1, clientIndex * 2 + localIndex + 1))).ToArray();
    }
}

public static class PartyTopologies
{
    private static readonly PartyTopology[] All =
    [
        new("1-single", "单客户端 · P1", 1, [1]),
        new("2-couch", "单客户端 · P1/P2", 2, [1, 2]),
        new("2-split", "双客户端 · P1 · P2", 2, [1], [2]),
        new("3-main-solo", "客户端 1: P1 · 客户端 2: P2/P3", 3, [1], [2, 3]),
        new("3-second-solo", "客户端 1: P1/P2 · 客户端 2: P3", 3, [1, 2], [3]),
        new("3-three-singles", "三个客户端 · P1 · P2 · P3", 3, [1], [2], [3]),
        new("4-balanced", "两个客户端 · P1/P2 · P3/P4", 4, [1, 2], [3, 4]),
        new("5-third-solo", "客户端 1: P1/P2 · 客户端 2: P3/P4 · 客户端 3: P5", 5, [1, 2], [3, 4], [5]),
        new("5-main-solo", "客户端 1: P1 · 客户端 2: P2/P3 · 客户端 3: P4/P5", 5, [1], [2, 3], [4, 5]),
        new("6-balanced", "三个客户端 · P1/P2 · P3/P4 · P5/P6", 6, [1, 2], [3, 4], [5, 6])
    ];

    public static IReadOnlyList<PartyTopology> ForPlayerCount(int count) =>
        All.Where(item => item.PlayerCount == count).ToArray();

    public static PartyTopology DefaultFor(int count) => ForPlayerCount(count).FirstOrDefault() ??
        throw new ArgumentOutOfRangeException(nameof(count), "Choose 1–6 party players.");
}
