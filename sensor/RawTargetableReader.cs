namespace PartyPilot.Sensor;

// A missing or unreadable component is unknown, never a positive quest state.
internal static class RawTargetableReader
{
    internal static bool? MeetsQuestState(long entity, Func<long, int, byte[]?> read)
    {
        var component = RawEntityComponentReader.Find(entity, "Targetable", read);
        return component == 0 ? null : TargetableQuestStateProbe.Read(component, read);
    }
}
