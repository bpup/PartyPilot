using System.Text;

namespace PartyPilot.Sensor;

// Resolves one named component through the entity's name/index table and
// verifies that the resolved component still belongs to the same entity.
internal static class RawEntityComponentReader
{
    private const long MaxPointer = 0x7FFF_FFFF_FFFF;

    internal static long Find(long entity, string componentName,
        Func<long, int, byte[]?> read)
    {
        if (!Plausible(entity) || string.IsNullOrEmpty(componentName) ||
            componentName.Length > 64 || componentName.Any(c =>
                !((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))) ||
            !TryPointer(read, entity + 8, out var details) ||
            !TryPointer(read, details + 40, out var lookup) ||
            !TryVector(read, lookup + 40, out var namesStart, out var nameCount)) return 0;

        var selectedIndex = -1;
        for (var index = 0; index < nameCount; index++)
        {
            var entry = read(namesStart + index * 16L, 16);
            if (entry is not { Length: 16 }) return 0;
            var namePointer = BitConverter.ToInt64(entry, 0);
            var componentIndex = BitConverter.ToInt32(entry, 8);
            if (!Plausible(namePointer) || componentIndex is < 0 or >= 256) continue;
            var nameBytes = read(namePointer, 64);
            if (nameBytes is not { Length: 64 }) continue;
            var length = Array.IndexOf(nameBytes, (byte)0);
            if (length < 1 || length > 64 ||
                nameBytes.AsSpan(0, length).IndexOfAnyInRange((byte)0, (byte)31) >= 0 ||
                nameBytes.AsSpan(0, length).IndexOfAnyInRange((byte)127, (byte)255) >= 0)
                continue;
            if (Encoding.ASCII.GetString(nameBytes, 0, length) != componentName) continue;
            selectedIndex = componentIndex;
            break;
        }
        if (selectedIndex < 0 || !TryComponentList(read, entity + 16,
                out var componentsStart, out var componentCount) ||
            selectedIndex >= componentCount ||
            !TryPointer(read, componentsStart + selectedIndex * 8L, out var component) ||
            !TryPointer(read, component + 8, out var owner) || owner != entity) return 0;
        return component;
    }

    private static bool TryVector(Func<long, int, byte[]?> read, long address,
        out long first, out int count)
    {
        first = 0;
        count = 0;
        var header = read(address, 24);
        if (header is not { Length: 24 }) return false;
        var start = BitConverter.ToInt64(header, 0);
        var last = BitConverter.ToInt64(header, 8);
        var capacity = BitConverter.ToInt64(header, 16);
        if (!Plausible(start) || !Plausible(last) || !Plausible(capacity) ||
            last <= start || capacity < last || (last - start) % 16 != 0) return false;
        var length = (last - start) / 16;
        if (length is < 1 or > 256) return false;
        first = start;
        count = (int)length;
        return true;
    }

    private static bool TryComponentList(Func<long, int, byte[]?> read, long address,
        out long first, out int count)
    {
        first = 0;
        count = 0;
        var header = read(address, 16);
        if (header is not { Length: 16 }) return false;
        var start = BitConverter.ToInt64(header, 0);
        var last = BitConverter.ToInt64(header, 8);
        if (!Plausible(start) || !Plausible(last) || last <= start ||
            (last - start) % 8 != 0) return false;
        var length = (last - start) / 8;
        if (length is < 1 or > 256) return false;
        first = start;
        count = (int)length;
        return true;
    }

    private static bool TryPointer(Func<long, int, byte[]?> read, long address,
        out long value)
    {
        value = 0;
        var bytes = read(address, 8);
        if (bytes is not { Length: 8 }) return false;
        value = BitConverter.ToInt64(bytes);
        return Plausible(value);
    }

    private static bool Plausible(long pointer) => pointer >= 0x10000 && pointer < MaxPointer;
}
