namespace PartyPilot.Probe;

public sealed record KnownGameLayout(string Name, int IngameStateSlot,
    int AreaInstanceSlot, int BaseAreaLevelOffset, int MonsterLevelOffset)
{
    public static KnownGameLayout? ForSha256(string hash) => hash.ToUpperInvariant() switch
    {
        // Hashes and offsets are observed in the supplied 0.6.17 files. A
        // matching hash identifies a static profile, not a live game result.
        "D6BAC03A460177DCB1E9C621B6D3D0F432C94E32BE1CB9BB58DC1F23EC0934C3"
            => new("supplied original build", 136, 656, 192, 196),
        "BB8627EB41FEBE3F2BE0AA19ED28E5891D5EBD002EEE4F819DB569EA91203DC2"
            => new("supplied 0.5.5 profile", 144, 656, 192, 188),
        "457B5B09A4F1CBBEB2AAC0958138350BF96947B39A1E639F6ADCC195576636EF"
            => new("supplied 0.5.5 profile", 144, 656, 192, 188),
        _ => null
    };
}

public sealed record StateCandidate(long IngameStateAddress, long AreaInstanceAddress,
    int BaseAreaLevel, int MonsterLevel);

public static class StateCandidateValidator
{
    public static bool TryRead(long gameStateAddress, KnownGameLayout layout,
        Func<long, byte[], bool> read, out StateCandidate? candidate)
    {
        candidate = null;
        if (!Win32Memory.Plausible(gameStateAddress) ||
            !TryPointer(gameStateAddress + layout.IngameStateSlot, read, out var ingameState) ||
            !TryPointer(ingameState + layout.AreaInstanceSlot, read, out var areaInstance) ||
            !TryInt32(areaInstance + layout.BaseAreaLevelOffset, read, out var baseLevel) ||
            !TryInt32(areaInstance + layout.MonsterLevelOffset, read, out var monsterLevel) ||
            baseLevel is < 0 or > 100 || monsterLevel is < 1 or > 100 ||
            !TryPointer(gameStateAddress + layout.IngameStateSlot, read, out var stateAgain) ||
            !TryPointer(ingameState + layout.AreaInstanceSlot, read, out var areaAgain) ||
            stateAgain != ingameState || areaAgain != areaInstance)
            return false;
        candidate = new StateCandidate(ingameState, areaInstance, baseLevel, monsterLevel);
        return true;
    }

    private static bool TryPointer(long address, Func<long, byte[], bool> read, out long pointer)
    {
        pointer = 0;
        var bytes = new byte[8];
        if (!read(address, bytes)) return false;
        pointer = BitConverter.ToInt64(bytes);
        return Win32Memory.Plausible(pointer);
    }

    private static bool TryInt32(long address, Func<long, byte[], bool> read, out int value)
    {
        value = 0;
        var bytes = new byte[4];
        if (!read(address, bytes)) return false;
        value = BitConverter.ToInt32(bytes);
        return true;
    }
}
