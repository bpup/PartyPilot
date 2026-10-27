using System.Text;

namespace PartyPilot.Sensor;

// Candidate 0.6.17 chain: in-game state +656 -> world-area holder +152 -> row +0.
// An absent or malformed ID is unknown; the area name is never substituted.
internal static class RawAreaIdReader
{
    internal static string? Read(long state, Func<long, int, byte[]?> read)
    {
        if (!Pointer(state)) return null;
        var holderBytes = read(state + 656, 8);
        if (holderBytes is not { Length: 8 }) return null;
        var holder = BitConverter.ToInt64(holderBytes);
        if (!Pointer(holder)) return null;
        var rowBytes = read(holder + 152, 8);
        if (rowBytes is not { Length: 8 }) return null;
        var row = BitConverter.ToInt64(rowBytes);
        if (!Pointer(row)) return null;
        var stringBytes = read(row, 8);
        if (stringBytes is not { Length: 8 }) return null;
        var address = BitConverter.ToInt64(stringBytes);
        if (!Pointer(address) || (address & 1) != 0) return null;
        var bytes = read(address, 200);
        if (bytes is not { Length: 200 }) return null;
        var end = -1;
        for (var i = 0; i < bytes.Length - 1; i += 2)
            if (bytes[i] == 0 && bytes[i + 1] == 0) { end = i; break; }
        if (end is < 2 or > 80) return null;
        var id = Encoding.Unicode.GetString(bytes, 0, end);
        return id.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? id : null;
    }

    private static bool Pointer(long address) => address >= 0x10000 && address < 0x7FFF_FFFF_FFFF;
}
