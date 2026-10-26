namespace PartyPilot.Sensor;

// Exact pointer joins are diagnostic candidates only. They do not establish
// local player order, loot allocation, targetability or pickup permission.
internal static class UiItemCardCorrelator
{
    internal sealed record Player(long Address, uint EntityId);
    internal sealed record Loot(long InnerItemAddress, uint EntityId, string ItemPath, string Name);
    internal sealed record OwnerMatch(uint EntityId, string Evidence);
    internal sealed record LootMatch(uint EntityId, string ItemPath, string Name, string Evidence);
    internal sealed record SlotCandidate(int LocalSlot, string Evidence);
    internal sealed record Match(OwnerMatch? Owner, LootMatch? Loot, SlotCandidate? Slot);

    internal static Match Resolve(UiItemCardTreeScanner.Card card,
        IReadOnlyList<Player> players, IReadOnlyList<Loot> loot,
        LocalSlotCandidateScanner.Result? orderedCandidate = null)
    {
        var ownerCandidates = players.Where(player => player.Address > 0 &&
            player.EntityId != 0 && player.Address == card.Owner).Take(2).ToArray();
        var lootCandidates = loot.Where(item => item.InnerItemAddress > 0 &&
            item.EntityId != 0 && item.InnerItemAddress == card.Item &&
            item.ItemPath.StartsWith("Metadata/Items/", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(item.Name)).Take(2).ToArray();
        var owner = ownerCandidates.Length == 1
            ? new OwnerMatch(ownerCandidates[0].EntityId, "pointer_equal_visible_player") : null;
        SlotCandidate? slot = null;
        if (owner != null && orderedCandidate != null &&
            (orderedCandidate.Status is "single_slot_candidate" or "two_slot_candidate") &&
            orderedCandidate.OrderedAddresses.Length ==
                (orderedCandidate.Status == "two_slot_candidate" ? 2 : 1) &&
            orderedCandidate.OrderedAddresses.Distinct(StringComparer.OrdinalIgnoreCase).Count() ==
                orderedCandidate.OrderedAddresses.Length)
        {
            var address = $"0x{card.Owner:X}";
            var index = Array.FindIndex(orderedCandidate.OrderedAddresses,
                candidate => string.Equals(candidate, address, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) slot = new(index + 1, "unverified_vector_order_candidate");
        }
        return new Match(
            owner,
            lootCandidates.Length == 1
                ? new LootMatch(lootCandidates[0].EntityId, lootCandidates[0].ItemPath,
                    lootCandidates[0].Name, "pointer_equal_visible_world_item") : null,
            slot);
    }
}
