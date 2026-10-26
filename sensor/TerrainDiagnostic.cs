namespace PartyPilot.Sensor;

// Raw pathfinding values have not been mapped to walkability or coordinates.
internal static class TerrainDiagnostic
{
    private const int MaxCells = 65_536;
    private const int MaxValues = 32;

    internal sealed record ValueCount(int Value, int Count);
    internal sealed record Summary(int Rows, int MinColumns, int MaxColumns,
        int SampledCells, bool SampleTruncated, bool ValuesTruncated,
        ValueCount[] Values);

    internal static Summary? Summarize(int[][]? rows)
    {
        if (rows == null || rows.Length == 0) return null;
        var counts = new Dictionary<int, int>();
        var min = int.MaxValue;
        var max = 0;
        var sampled = 0;
        var sampleTruncated = false;
        var valuesTruncated = false;
        foreach (var row in rows)
        {
            var length = row?.Length ?? 0;
            min = Math.Min(min, length);
            max = Math.Max(max, length);
            for (var index = 0; index < length; index++)
            {
                if (sampled == MaxCells)
                {
                    sampleTruncated = true;
                    break;
                }
                sampled++;
                var value = row![index];
                if (counts.TryGetValue(value, out var count)) counts[value] = count + 1;
                else if (counts.Count < MaxValues) counts[value] = 1;
                else valuesTruncated = true;
            }
        }
        return new Summary(rows.Length, min, max, sampled, sampleTruncated,
            valuesTruncated, counts.OrderBy(pair => pair.Key)
                .Select(pair => new ValueCount(pair.Key, pair.Value)).ToArray());
    }
}
