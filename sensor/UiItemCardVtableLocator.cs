using System.Buffers.Binary;
using System.Reflection.PortableExecutable;

namespace PartyPilot.Sensor;

// The signature identifies the item-card constructor in the supplied build.
// A missing or ambiguous match produces no address; callers must fail closed.
internal static class UiItemCardVtableLocator
{
    private static readonly int[] Constructor = (
        "48 8D 05 ?? ?? ?? ?? 48 89 06 48 8D 05 ?? ?? ?? ?? 48 89 86 28 02 00 00 " +
        "48 8D 05 ?? ?? ?? ?? 48 89 86 30 02 00 00 48 8D 05 ?? ?? ?? ?? " +
        "48 89 86 C8 02 00 00 48 89 BE D0 02 00 00 48 89 BE D8 02 00 00 " +
        "48 89 BE E0 02 00 00 40 88 BE E8 02 00 00 48 89 9E F0 02 00 00 " +
        "4C 89 B6 F8 02 00 00")
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(value => value == "??" ? -1 : Convert.ToInt32(value, 16)).ToArray();

    internal static long Resolve(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            using var pe = new PEReader(file);
            var header = pe.PEHeaders.PEHeader;
            if (header == null) return 0;
            var sections = pe.PEHeaders.SectionHeaders;
            var candidates = new HashSet<long>();
            foreach (var section in sections.Where(IsExecutable))
            {
                var bytes = pe.GetSectionData(section.VirtualAddress).GetContent().ToArray();
                foreach (var index in FindPattern(bytes))
                {
                    var rva = (long)section.VirtualAddress + index + 7 +
                        BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(index + 3, 4));
                    var dataSection = sections.FirstOrDefault(candidate =>
                        !IsExecutable(candidate) && rva >= candidate.VirtualAddress &&
                        rva + 24 <= (long)candidate.VirtualAddress + candidate.VirtualSize);
                    if (dataSection.VirtualSize == 0) continue;
                    var table = pe.GetSectionData((int)rva).GetContent(0, 24).ToArray();
                    if (table.Length != 24) continue;
                    var valid = true;
                    for (var entry = 0; entry < 3; entry++)
                    {
                        var absolute = BinaryPrimitives.ReadUInt64LittleEndian(table.AsSpan(entry * 8, 8));
                        if (absolute < (ulong)header.ImageBase ||
                            !sections.Any(candidate => IsExecutable(candidate) &&
                                absolute - (ulong)header.ImageBase >= (ulong)candidate.VirtualAddress &&
                                absolute - (ulong)header.ImageBase <
                                    (ulong)((long)candidate.VirtualAddress + candidate.VirtualSize)))
                        {
                            valid = false;
                            break;
                        }
                    }
                    if (valid) candidates.Add(rva);
                }
            }
            return candidates.Count == 1 ? candidates.Single() : 0;
        }
        catch
        {
            return 0;
        }
    }

    internal static IEnumerable<int> FindPattern(byte[] bytes)
    {
        for (var index = 0; index <= bytes.Length - Constructor.Length; index++)
        {
            var matches = true;
            for (var part = 0; part < Constructor.Length; part++)
            {
                if (Constructor[part] >= 0 && bytes[index + part] != Constructor[part])
                {
                    matches = false;
                    break;
                }
            }
            if (matches) yield return index;
        }
    }

    private static bool IsExecutable(SectionHeader section) =>
        (section.SectionCharacteristics & SectionCharacteristics.MemExecute) != 0;
}
