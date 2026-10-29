namespace PartyPilot.Probe;

// The +244 offset is the supplied 0.6.17 default AreaInstance current-area
// hash. A matching value is diagnostic only; it cannot establish that two
// separate game processes occupy the same live instance.
public static class AreaHashCandidate
{
    private const int SuppliedDefaultOffset = 244;

    public static uint? Read(long areaInstanceAddress, Func<long, byte[], bool> read)
    {
        if (!Win32Memory.Plausible(areaInstanceAddress)) return null;
        var bytes = new byte[4];
        if (!read(areaInstanceAddress + SuppliedDefaultOffset, bytes)) return null;
        var hash = BitConverter.ToUInt32(bytes);
        return hash == 0 ? null : hash;
    }

    public static uint? ReadStable(long areaInstanceAddress, Func<long, byte[], bool> read)
    {
        var before = Read(areaInstanceAddress, read);
        return before is not null && before == Read(areaInstanceAddress, read)
            ? before : null;
    }
}
