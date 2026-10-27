using PartyPilot.Desktop;

internal static class LiveTerrainSelectionSmoke
{
    internal static void Run()
    {
        var two = PartyTopologies.ForPlayerCount(2).Single(item => item.Id == "2-split");
        if (!LiveTerrainSelection.TryCreate(two, ["", ""], false, out var none, out _) ||
            none.Count != 0 ||
            LiveTerrainSelection.TryCreate(two, ["", ""], true, out _, out _) ||
            LiveTerrainSelection.TryCreate(two, ["/missing.json", ""], false, out _, out _))
            throw new Exception("Optional terrain mode accepted a partial or unbound collision request.");
        var directory = Path.Combine(Path.GetTempPath(),
            "partypilot-live-terrain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var first = Path.Combine(directory, "client-1.json");
            var second = Path.Combine(directory, "client-2.json");
            File.WriteAllText(first, "{}");
            File.WriteAllText(second, "{}");
            if (!LiveTerrainSelection.TryCreate(two, [first, second], true,
                    out var bound, out _) || bound[1] != first || bound[2] != second ||
                LiveTerrainSelection.TryCreate(two, [first, first], true, out _, out _) ||
                LiveTerrainSelection.TryCreate(two, [first, Path.Combine(directory, "absent.json")],
                    true, out _, out _))
                throw new Exception("Live terrain paths were not bound to distinct current clients.");
            var three = PartyTopologies.ForPlayerCount(3)
                .Single(item => item.Id == "3-three-singles");
            var third = Path.Combine(directory, "client-3.json");
            File.WriteAllText(third, "{}");
            if (!LiveTerrainSelection.TryCreate(three, [first, second, third], true,
                    out var threeBound, out _) || threeBound[3] != third ||
                LiveTerrainSelection.TryCreate(three, [first, second, ""], true, out _, out _))
                throw new Exception("Three-client terrain mode accepted an incomplete role map.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
