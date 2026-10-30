namespace PartyPilot.Sensor;

// Reads the bounded icon identifier candidates from the supplied build's
// MinimapIcon component. An identifier is a hint, not a quest-state signal.
internal static class RawMinimapIconReader
{
    private static readonly int[] NameOffsets = [0, 8, 16, 24, 32];

    internal static string? Read(long entity, Func<long, int, byte[]?> read)
    {
        var component = RawEntityComponentReader.Find(entity, "MinimapIcon", read);
        if (component == 0 || !TryPointer(component + 32, read, out var data)) return null;
        foreach (var offset in NameOffsets)
        {
            if (TryPointer(data + offset, read, out var nameAddress) &&
                ReadName(nameAddress, read) is { } indirect) return indirect;
            if (ReadName(data + offset, read) is { } inline) return inline;
        }
        return null;
    }

    private static string? ReadName(long address, Func<long, int, byte[]?> read)
    {
        // The original reader asks for up to 96 bytes. Shorter reads allow a
        // bounded name at the end of a readable memory region to be inspected.
        foreach (var size in new[] { 96, 64, 32, 16 })
        {
            var bytes = read(address, size);
            if (bytes is not { Length: > 1 } || bytes.Length != size) continue;
            Span<char> chars = stackalloc char[48];
            var count = 0;
            for (var index = 0; index + 1 < bytes.Length; index += 2)
            {
                var value = (char)(bytes[index] | bytes[index + 1] << 8);
                if (value == '\0') break;
                if (count == chars.Length) return null;
                chars[count++] = value;
            }
            if (count is < 3 or > 48 || !IsAsciiLetter(chars[0])) return null;
            for (var index = 0; index < count; index++)
                if (!IsAsciiLetter(chars[index]) && !char.IsAsciiDigit(chars[index]) &&
                    chars[index] != '_') return null;
            return new string(chars[..count]);
        }
        return null;
    }

    private static bool TryPointer(long address, Func<long, int, byte[]?> read,
        out long pointer)
    {
        pointer = 0;
        var bytes = read(address, 8);
        if (bytes is not { Length: 8 }) return false;
        pointer = BitConverter.ToInt64(bytes);
        return pointer >= 0x10000 && pointer < 0x7FFF_FFFF_FFFF;
    }

    private static bool IsAsciiLetter(char value) =>
        (value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z');
}
