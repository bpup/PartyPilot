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
            match.Loot.ItemPath != "Metadata/Items/Currency/CurrencyExalted")
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
        Console.WriteLine("UI item-card pointer correlation checks passed.");
    }
}
