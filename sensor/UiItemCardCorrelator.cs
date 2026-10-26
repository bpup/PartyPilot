namespace PartyPilot.Sensor;

// Exact pointer joins are diagnostic candidates only. They do not establish
// local player order, loot allocation, targetability or pickup permission.
internal static class UiItemCardCorrelator
{
    internal sealed record Player(long Address, uint EntityId);
    internal sealed record Loot(long InnerItemAddress, uint EntityId, string ItemPath, string Name);
    internal sealed record OwnerMatch(uint EntityId, string Evidence);
    internal sealed record LootMatch(uint EntityId, string ItemPath, string Name, string Evidence);
    internal sealed record Match(OwnerMatch? Owner, LootMatch? Loot);

    internal static Match Resolve(UiItemCardTreeScanner.Card card,
        IReadOnlyList<Player> players, IReadOnlyList<Loot> loot)
    {
        var ownerCandidates = players.Where(player => player.Address > 0 &&
            player.EntityId != 0 && player.Address == card.Owner).Take(2).ToArray();
        var lootCandidates = loot.Where(item => item.InnerItemAddress > 0 &&
            item.EntityId != 0 && item.InnerItemAddress == card.Item &&
            item.ItemPath.StartsWith("Metadata/Items/", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(item.Name)).Take(2).ToArray();
        return new Match(
            ownerCandidates.Length == 1
                ? new OwnerMatch(ownerCandidates[0].EntityId, "pointer_equal_visible_player") : null,
            lootCandidates.Length == 1
                ? new LootMatch(lootCandidates[0].EntityId, lootCandidates[0].ItemPath,
                    lootCandidates[0].Name, "pointer_equal_visible_world_item") : null);
    }
}
