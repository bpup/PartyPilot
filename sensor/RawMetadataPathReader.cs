using System.Text;

namespace PartyPilot.Sensor;

// Read-only candidate for the supplied build's entity details / std::wstring
// chain. The active game offset profile still requires live verification.
internal static class RawMetadataPathReader
{
    internal static string? Read(long entity, Func<long, int, byte[]?> read)
    {
        if (!Pointer(entity)) return null;
        var detailBytes = read(entity + 8, 8);
        if (detailBytes is not { Length: 8 }) return null;
        var details = BitConverter.ToInt64(detailBytes);
        if (!Pointer(details)) return null;
        var header = read(details + 8, 32);
        if (header is not { Length: 32 }) return null;
        var length = BitConverter.ToInt64(header, 16);
        var capacity = BitConverter.ToInt64(header, 24);
        if (length is < 5 or > 255 || capacity < length || capacity > 4096) return null;
        byte[]? bytes;
        if (capacity <= 7) bytes = header[..((int)length * 2)];
        else
        {
            var address = BitConverter.ToInt64(header, 0);
            if (!Pointer(address)) return null;
            bytes = read(address, (int)length * 2);
        }
        if (bytes == null || bytes.Length != length * 2) return null;
        var path = Encoding.Unicode.GetString(bytes).Replace('\\', '/');
        return path.StartsWith("Metadata/", StringComparison.OrdinalIgnoreCase) &&
            path.Length == length && !path.Any(c => c < ' ' || c > '~') ? path : null;
    }

    private static bool Pointer(long address) => address >= 0x10000 && address < 0x7FFF_FFFF_FFFF;
}
