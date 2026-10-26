using System.Text.Json;

namespace PartyPilot.Sensor;

public static class RadarPacketBudget
{
    public static object[] Select(IEnumerable<object> ranked, int maxMarkers = 96,
        int maxSerializedBytes = 48_000)
    {
        var result = new List<object>();
        var bytes = 0;
        foreach (var candidate in ranked)
        {
            if (result.Count == maxMarkers) break;
            var next = JsonSerializer.SerializeToUtf8Bytes(candidate).Length + 1;
            if (bytes + next > maxSerializedBytes) break;
            result.Add(candidate);
            bytes += next;
        }
        return result.ToArray();
    }
}
