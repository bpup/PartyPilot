namespace PartyPilot.Sensor;

// The supplied 0.6.17 TargetableOffsets.Default places MeetsQuestState at 86.
// A raw byte is only a candidate until the current game build is checked.
internal static class TargetableQuestStateProbe
{
    private const int CandidateOffset = 86;

    internal static bool? Read(long componentAddress, Func<long, int, byte[]?> read)
    {
        if (componentAddress < 0x10000 || componentAddress >= 0x7FFF_FFFF_FFFF)
            return null;
        var bytes = read(componentAddress + CandidateOffset, 1);
        if (bytes is not { Length: 1 }) return null;
        return bytes[0] switch
        {
            0 => false,
            1 => true,
            _ => null
        };
    }
}
