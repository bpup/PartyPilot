using System.Text;

namespace PartyPilot.Sensor;

// Read-only fallback for vector entries missing from ExileCore2's visible
// entity collection. The two +8 offsets come from the supplied 0.6.17
// InventoryChainOffsets.Default; its active runtime profile is not known.
public static class RawCharacterPathReader
{
    private const int DetailsPointerOffset = 8;
    private const int DetailsPathOffset = 8;

    public static string? Read(long entityAddress, Func<long, int, byte[]?> read)
    {
        if (!Plausible(entityAddress)) return null;
        var detailsBytes = read(entityAddress + DetailsPointerOffset, 8);
        if (detailsBytes is not { Length: 8 }) return null;
        var detailsAddress = BitConverter.ToInt64(detailsBytes);
        if (!Plausible(detailsAddress)) return null;
        var textHeader = read(detailsAddress + DetailsPathOffset, 32);
        if (textHeader is not { Length: 32 }) return null;
        var length = BitConverter.ToInt64(textHeader, 16);
        var capacity = BitConverter.ToInt64(textHeader, 24);
        if (length is <= 0 or > 512 || capacity < length || capacity > 4096)
            return null;
        byte[] textBytes;
        if (capacity <= 7)
            textBytes = textHeader[..checked((int)length * 2)];
        else
        {
            var textAddress = BitConverter.ToInt64(textHeader, 0);
            if (!Plausible(textAddress)) return null;
            textBytes = read(textAddress, checked((int)length * 2)) ?? [];
            if (textBytes.Length != length * 2) return null;
        }
        var path = Encoding.Unicode.GetString(textBytes).TrimEnd('\0');
        if (path.Length != length || path.Any(c => c < ' ' || c == 0x7f) ||
            !(path.StartsWith("Metadata/Characters/", StringComparison.OrdinalIgnoreCase) ||
              path.Contains("/Characters/", StringComparison.OrdinalIgnoreCase)))
            return null;
        return path;
    }

    private static bool Plausible(long address) =>
        address >= 0x10000 && address < 0x7FFF_FFFF_FFFF;
}
