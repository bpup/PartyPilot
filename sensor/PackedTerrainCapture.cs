using System.Security.Cryptography;
using System.Text.Json;

namespace PartyPilot.Sensor;

// Diagnostic export only. A plausible memory layout is not a verified game map.
internal static class PackedTerrainCapture
{
    private const int ChunkBytes = 64 * 1024;
    private const int MaxBytes = 64 * 1024 * 1024;

    internal sealed record Outcome(string Status, string? DataFile = null);

    internal static Outcome Save(PackedTerrainProbe.PackedSource source,
        Func<long, int, byte[]?> read, string directory, int processId,
        string areaHash, string areaAddress, string anchor, DateTime seenUtc)
    {
        var length = (long)source.BytesPerRow * source.Rows;
        if (source.Start < 0x10000 || source.BytesPerRow <= 0 || source.Rows <= 0 ||
            length is < 4096 or > MaxBytes || processId <= 0)
            return new Outcome("invalid_source");

        string? temporary = null;
        string? dataFile = null;
        string? metadataFile = null;
        try
        {
            Directory.CreateDirectory(directory);
            var name = $"terrain-{processId}-{seenUtc:yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}";
            dataFile = Path.Combine(directory, name + ".packed");
            metadataFile = Path.Combine(directory, name + ".json");
            temporary = dataFile + ".tmp";
            byte[]? first = null;
            byte[]? last = null;
            long lastAddress = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using (var output = new FileStream(temporary, FileMode.CreateNew,
                FileAccess.Write, FileShare.None))
            {
                for (long offset = 0; offset < length; offset += ChunkBytes)
                {
                    var size = (int)Math.Min(ChunkBytes, length - offset);
                    var bytes = read(source.Start + offset, size);
                    if (bytes == null || bytes.Length != size)
                        return new Outcome("read_failed");
                    if (offset == 0) first = bytes;
                    last = bytes;
                    lastAddress = source.Start + offset;
                    output.Write(bytes);
                    hash.AppendData(bytes);
                }
                output.Flush(flushToDisk: true);
            }
            if (first == null || last == null ||
                !Matches(read(source.Start, first.Length), first) ||
                !Matches(read(lastAddress, last.Length), last))
                return new Outcome("changed_during_read");

            var digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            File.Move(temporary, dataFile);
            temporary = null;
            File.WriteAllText(metadataFile, JsonSerializer.Serialize(new
            {
                Status = "candidate_capture_only",
                ProcessId = processId,
                AreaHash = areaHash,
                AreaAddress = areaAddress,
                Anchor = anchor,
                PackedStart = $"0x{source.Start:X}",
                SeenUtc = seenUtc,
                Width = source.BytesPerRow * 2,
                Height = source.Rows,
                BytesPerRow = source.BytesPerRow,
                PackedBytes = length,
                Sha256 = digest,
                DataFile = Path.GetFileName(dataFile)
            }));
            return new Outcome("captured_local", dataFile);
        }
        catch
        {
            if (dataFile != null) TryDelete(dataFile);
            if (metadataFile != null) TryDelete(metadataFile);
            return new Outcome("write_failed");
        }
        finally
        {
            if (temporary != null) TryDelete(temporary);
        }
    }

    private static bool Matches(byte[]? current, byte[] original) =>
        current != null && current.AsSpan().SequenceEqual(original);

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* Preserve the original failure status. */ }
    }
}
