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
        var four = PartyTopologies.ForPlayerCount(4).Single(item => item.Id == "4-balanced");
        var fiveThird = PartyTopologies.ForPlayerCount(5).Single(item => item.Id == "5-third-solo");
        var fiveMain = PartyTopologies.ForPlayerCount(5).Single(item => item.Id == "5-main-solo");
        var six = PartyTopologies.ForPlayerCount(6).Single(item => item.Id == "6-balanced");
        if (!LiveTerrainSelection.TryCreate(four, ["", ""], false, out var fourNone, out _) ||
            fourNone.Count != 0 ||
            !LiveTerrainSelection.TryCreate(fiveThird, ["", "", ""], false, out var fiveThirdNone, out _) ||
            fiveThirdNone.Count != 0 ||
            !LiveTerrainSelection.TryCreate(fiveMain, ["", "", ""], false, out var fiveMainNone, out _) ||
            fiveMainNone.Count != 0 ||
            !LiveTerrainSelection.TryCreate(six, ["", "", ""], false, out var sixNone, out _) ||
            sixNone.Count != 0 ||
            LiveTerrainSelection.TryCreate(four, ["", ""], true, out _, out _) ||
            LiveTerrainSelection.TryCreate(fiveThird, ["", "", ""], true, out _, out _) ||
            LiveTerrainSelection.TryCreate(fiveMain, ["/candidate.json", "", ""], false, out _, out _) ||
            LiveTerrainSelection.TryCreate(six, ["/candidate.json", "", ""], false, out _, out _))
            throw new Exception("Ordered two-player clients accepted incomplete terrain or unsupported collision settings.");
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
            if (!LiveTerrainSelection.TryCreate(four, [first, second], false,
                    out var fourBound, out _) ||
                fourBound.Count != 4 || fourBound[1] != first || fourBound[2] != first ||
                fourBound[3] != second || fourBound[4] != second ||
                !LiveTerrainSelection.TryCreate(fiveThird, [first, second, third], false,
                    out var fiveThirdBound, out _) ||
                fiveThirdBound.Count != 5 || fiveThirdBound[1] != first ||
                fiveThirdBound[2] != first || fiveThirdBound[3] != second ||
                fiveThirdBound[4] != second || fiveThirdBound[5] != third ||
                !LiveTerrainSelection.TryCreate(fiveMain, [first, second, third], false,
                    out var fiveMainBound, out _) ||
                fiveMainBound.Count != 5 || fiveMainBound[1] != first ||
                fiveMainBound[2] != second || fiveMainBound[3] != second ||
                fiveMainBound[4] != third || fiveMainBound[5] != third ||
                !LiveTerrainSelection.TryCreate(six, [first, second, third], false,
                    out var sixBound, out _) ||
                sixBound.Count != 6 || sixBound[1] != first || sixBound[2] != first ||
                sixBound[3] != second || sixBound[4] != second ||
                sixBound[5] != third || sixBound[6] != third ||
                LiveTerrainSelection.TryCreate(four, [first, first], false, out _, out _) ||
                LiveTerrainSelection.TryCreate(six, [first, second, ""], false, out _, out _) ||
                LiveTerrainSelection.TryCreate(six, [first, second, third], true, out _, out _))
                throw new Exception("Ordered client terrain bindings did not map one file per client.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
