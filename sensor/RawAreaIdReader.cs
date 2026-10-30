using System.Text;

namespace PartyPilot.Sensor;

// The supplied 0.6.17 app contains two game-build layouts. Only an unambiguous
// valid ID is accepted; the area name is never substituted.
internal static class RawAreaIdReader
{
    internal static string? Read(long state, Func<long, int, byte[]?> read)
    {
        if (!Pointer(state)) return null;
        var corrected = ReadId(PointerAt(PointerAt(state + 656, read) + 152, read),
            200, read);
        var worldData = PointerAt(state + 776, read);
        var details = PointerAt(worldData + 152, read);
        var vendor = ReadId(PointerAt(details + 144, read), 80, read);
        return corrected != null && vendor != null && corrected != vendor
            ? null : corrected ?? vendor;
    }

    private static string? ReadId(long row, int length, Func<long, int, byte[]?> read)
    {
        var address = PointerAt(row, read);
        if (!Pointer(address) || (address & 1) != 0) return null;
        var bytes = read(address, length);
        if (bytes == null || bytes.Length != length) return null;
        var end = -1;
        for (var i = 0; i < bytes.Length - 1; i += 2)
            if (bytes[i] == 0 && bytes[i + 1] == 0) { end = i; break; }
        if (end is < 4 or > 80) return null;
        var id = Encoding.Unicode.GetString(bytes, 0, end);
        return id.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? id : null;
    }

    private static long PointerAt(long address, Func<long, int, byte[]?> read)
    {
        if (!Pointer(address)) return 0;
        var bytes = read(address, 8);
        if (bytes is not { Length: 8 }) return 0;
        var value = BitConverter.ToInt64(bytes);
        return Pointer(value) ? value : 0;
    }

    private static bool Pointer(long address) => address >= 0x10000 && address < 0x7FFF_FFFF_FFFF;
}
