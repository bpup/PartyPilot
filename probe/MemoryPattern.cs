namespace PartyPilot.Probe;

// Pure scanner: no game or native dependency, so overlapping reads can be
// checked against synthetic memory before any Windows integration.
public sealed class MemoryPattern
{
    private readonly byte[] _bytes;
    private readonly bool[] _known;

    public int Length => _bytes.Length;

    public MemoryPattern(string pattern, string mask)
    {
        var parts = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Length != mask.Length)
            throw new ArgumentException("Pattern and mask lengths differ.");
        _bytes = new byte[parts.Length];
        _known = new bool[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (mask[i] is not ('x' or '?')) throw new ArgumentException("Invalid pattern mask.");
            _bytes[i] = Convert.ToByte(parts[i], 16);
            _known[i] = mask[i] == 'x';
        }
    }

    public IReadOnlyList<int> Find(ReadOnlySpan<byte> data)
    {
        var matches = new List<int>();
        for (var start = 0; start <= data.Length - _bytes.Length; start++)
        {
            var matched = true;
            for (var i = 0; i < _bytes.Length; i++)
            {
                if (!_known[i] || data[start + i] == _bytes[i]) continue;
                matched = false;
                break;
            }
            if (matched) matches.Add(start);
        }
        return matches;
    }

    public static bool TryResolveRipPointer(long matchAddress, ReadOnlySpan<byte> window,
        int displacementOffset, out long pointerStorage)
    {
        pointerStorage = 0;
        if (displacementOffset < 1 || displacementOffset > window.Length - 4) return false;
        // An explicit RIP-relative operand takes precedence. The fallback
        // searches a short instruction window when a signature points nearby.
        if ((window[displacementOffset - 1] & 0xC7) == 0x05)
        {
            pointerStorage = matchAddress + displacementOffset + 4 +
                             BitConverter.ToInt32(window.Slice(displacementOffset, 4));
            return pointerStorage > 0;
        }
        var lower = Math.Max(0, displacementOffset - 12);
        var upper = Math.Min(window.Length - 7, displacementOffset + 4);
        for (var i = lower; i <= upper; i++)
        {
            if (window[i] is not (0x48 or 0x4C) || window[i + 1] is not (0x8B or 0x8D or 0x89) ||
                window[i + 2] is not (0x05 or 0x0D)) continue;
            pointerStorage = matchAddress + i + 7 + BitConverter.ToInt32(window.Slice(i + 3, 4));
            return pointerStorage > 0;
        }
        return false;
    }
}
