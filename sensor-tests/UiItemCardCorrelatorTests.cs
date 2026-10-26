using PartyPilot.Sensor;

internal static class UiItemCardCorrelatorTests
{
    internal static void Run()
    {
        var card = new UiItemCardTreeScanner.Card(0x60000, 0x80000, 0x90000);
        var players = new[] { new UiItemCardCorrelator.Player(0x90000, 11) };
        var loot = new[] { new UiItemCardCorrelator.Loot(0x80000, 23,
            "Metadata/Items/Currency/CurrencyExalted", "Exalted Orb") };
        var match = UiItemCardCorrelator.Resolve(card, players, loot);
        if (match.Owner?.EntityId != 11 || match.Loot?.EntityId != 23 ||
            match.Loot.ItemPath != "Metadata/Items/Currency/CurrencyExalted" ||
            match.Loot.BaseIdCandidate != "CurrencyExalted")
            throw new Exception("Exact pointer observations did not join.");
        if (UiItemCardCorrelator.Resolve(card,
                [.. players, new UiItemCardCorrelator.Player(0x90000, 12)], loot).Owner != null ||
            UiItemCardCorrelator.Resolve(card, players,
                [.. loot, new UiItemCardCorrelator.Loot(0x80000, 24,
                    "Metadata/Items/Currency/CurrencyExalted", "Exalted Orb")]).Loot != null)
            throw new Exception("Ambiguous pointer observations were accepted.");
        var absent = UiItemCardCorrelator.Resolve(card,
            [new UiItemCardCorrelator.Player(0x90001, 11)],
            [new UiItemCardCorrelator.Loot(0x80001, 23,
                "Metadata/Items/Currency/CurrencyExalted", "Exalted Orb")]);
        if (absent.Owner != null || absent.Loot != null)
            throw new Exception("Near pointers were treated as exact matches.");
        if (UiItemCardCorrelator.Resolve(card, players,
                [new UiItemCardCorrelator.Loot(0x80000, 23, "Metadata/Monsters/Beast", "Beast")]).Loot != null)
            throw new Exception("A non-item path was joined to the card.");
        if (LootBaseIdentity.FromItemPath("Metadata/Items/Gems/SkillGemUncutQuest1") !=
                "SkillGemUncutQuest1" ||
            LootBaseIdentity.FromItemPath("Metadata\\Items\\Currency\\CurrencyExalted/") !=
                "CurrencyExalted" ||
            LootBaseIdentity.FromItemPath("Metadata/Items/") != null ||
            LootBaseIdentity.FromItemPath("Metadata/Monsters/Beast") != null)
            throw new Exception("Item base ID candidate did not follow the verified path rule.");
        var ordered = new LocalSlotCandidateScanner.Result("two_slot_candidate", "test anchor",
            "unverified static offsets", ["0xA0000", "0x90000"], 1, "0x100000", true,
            ["0x100000"]);
        if (UiItemCardCorrelator.Resolve(card, players, loot, ordered).Slot is not
            { LocalSlot: 2, Evidence: "unverified_vector_order_candidate" })
            throw new Exception("Exact owner did not join the unverified second-slot candidate.");
        if (UiItemCardCorrelator.Resolve(card, players, loot,
                ordered with { Status = "conflicting_candidates" }).Slot != null ||
            UiItemCardCorrelator.Resolve(card,
                [new UiItemCardCorrelator.Player(0x90001, 11)], loot, ordered).Slot != null ||
            UiItemCardCorrelator.Resolve(card, players, loot,
                ordered with { OrderedAddresses = ["0x90000", "0x90000"] }).Slot != null)
            throw new Exception("Conflicted, unobserved or duplicate candidate order was accepted.");
        Console.WriteLine("UI item-card pointer correlation checks passed.");
    }
}
