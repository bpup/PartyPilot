using PartyPilot.Sensor;

internal static class DormantNavigationSurveyTests
{
    public static void Run()
    {
        var entries = Enumerable.Range(1, 300)
            .Select(index => new AreaEntityMapSnapshotReader.Entry((uint)index,
                0x100000 + index * 8L)).ToArray();
        var paths = new Dictionary<long, string>
        {
            [entries[4].Address] = "Metadata/QuestObjects/FinalLetter",
            [entries[289].Address] = "Metadata/MiscellaneousObjects/Waypoint"
        };
        string? ReadPath(long address) => paths.GetValueOrDefault(address) ?? "Metadata/Effects/Particle";
        RawRenderPositionReader.Position? ReadPosition(long address) =>
            new(100, 200, 0, 9.2f, 18.4f, $"0x{address:X}");
        var survey = new DormantNavigationSurvey();
        var first = survey.Capture("area-one", entries, ReadPath, ReadPosition, 100, 200);
        Require(first.BatchScanned == 256 && !first.SweepCompleted &&
            first.NavigationCandidates.Length == 1 && first.NavigationCandidates[0].EntityId == 5,
            "The first bounded slice missed its known quest candidate.");
        var second = survey.Capture("area-one", entries, ReadPath, ReadPosition, 100, 200);
        Require(second.BatchScanned == 44 && second.SweepCompleted &&
            second.NavigationCandidates.Select(item => item.EntityId).SequenceEqual([5u, 290u]),
            "A navigation point beyond the first 256 entities was never discovered or retained.");
        var withoutQuest = entries.Where(entry => entry.Id != 5).ToArray();
        var removed = survey.Capture("area-one", withoutQuest, ReadPath, ReadPosition, 100, 200);
        Require(removed.NavigationCandidates.Length == 1 && removed.NavigationCandidates[0].EntityId == 290,
            "A candidate removed from the current map stayed visible.");
        paths[entries[289].Address] = "Metadata/Effects/Particle";
        var changed = survey.Capture("area-one", withoutQuest, ReadPath, ReadPosition, 100, 200);
        Require(changed.NavigationCandidates.Length == 0,
            "A remembered candidate survived a changed path on the same entity address.");
        paths[entries[289].Address] = "Metadata/MiscellaneousObjects/Waypoint";
        var switched = survey.Capture("area-two", entries, ReadPath, ReadPosition, 100, 200);
        Require(switched.NavigationCandidates.Length == 1 && switched.NavigationCandidates[0].EntityId == 5,
            "A new area reused the old sweep cursor or retained its old waypoint.");
        survey.Reset();
        Require(survey.Capture("area-two", [], ReadPath, ReadPosition, 100, 200)
                .NavigationCandidates.Length == 0,
            "Reset left a dormant navigation candidate behind.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
