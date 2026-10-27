namespace PartyPilot.Sensor;

// Static layout candidate from the supplied build. Read-only diagnostic:
// matching these bytes is not proof of the active game's terrain contract.
internal static class PackedTerrainProbe
{
    private const int HeaderBytes = 16 * 1024;
    private const int MaxPackedBytes = 64 * 1024 * 1024;
    private const int TileCells = 23;
    private const int PatchRadius = 4;

    internal sealed record Observation(int Width, int Height, int BytesPerRow,
        int PatchX, int PatchY, string[] PatchRows, int SampledNibbles,
        int InRangeNibbles)
    {
        public string AreaAddress { get; init; } = "";
        public string Anchor { get; init; } = "";
    }
    internal sealed record PackedSource(long Start, int BytesPerRow, int Rows);
    internal sealed record AreaLayout(long Area, int TerrainOffset, int TilesX, int TilesY);
    internal sealed record Result(string Status, Observation? Candidate)
    {
        public PackedSource? Source { get; init; }
        public AreaLayout? Layout { get; init; }
    }
    internal sealed record AreaCandidate(string Anchor, long Address);

    internal static Result ScanAnchors(IEnumerable<AreaCandidate> anchors,
        float gridX, float gridY, Func<long, int, byte[]?> read)
    {
        Observation? selected = null;
        PackedSource? selectedSource = null;
        AreaLayout? selectedLayout = null;
        var attempted = false;
        var unreadable = false;
        var seen = new HashSet<long>();
        foreach (var anchor in anchors)
        {
            if (anchor.Address == 0 || !seen.Add(anchor.Address)) continue;
            attempted = true;
            var result = Scan(anchor.Address, gridX, gridY, read);
            if (result.Status == "ambiguous_candidates") return result;
            if (result.Status == "header_unreadable") unreadable = true;
            if (result.Candidate == null) continue;
            if (selected != null) return new Result("ambiguous_area_candidates", null);
            selected = result.Candidate with
            {
                AreaAddress = $"0x{anchor.Address:X}", Anchor = anchor.Anchor
            };
            selectedSource = result.Source;
            selectedLayout = result.Layout;
        }
        if (selected != null) return new Result("candidate_patch_observed", selected)
        {
            Source = selectedSource,
            Layout = selectedLayout
        };
        if (!attempted || !float.IsFinite(gridX) || !float.IsFinite(gridY))
            return new Result("missing_anchor_or_grid", null);
        return new Result(unreadable ? "header_unreadable" : "no_candidate", null);
    }

    internal static Result Scan(long area, float gridX, float gridY,
        Func<long, int, byte[]?> read)
    {
        if (area < 0x10000 || area >= 0x7FFF_FFFF_FFFF ||
            !float.IsFinite(gridX) || !float.IsFinite(gridY) ||
            gridX < 0 || gridY < 0) return new Result("missing_anchor_or_grid", null);
        byte[]? header = null;
        for (var size = HeaderBytes; size >= 1024; size /= 2)
        {
            var candidateHeader = read(area, size);
            if (candidateHeader is not { Length: >= 1024 and <= HeaderBytes }) continue;
            header = candidateHeader;
            break;
        }
        if (header is null) return new Result("header_unreadable", null);
        Candidate? selected = null;
        var ambiguous = false;
        for (var offset = 0; offset + 184 + 16 <= header.Length; offset += 8)
        {
            var tilesX = BitConverter.ToInt64(header, offset);
            var tilesY = BitConverter.ToInt64(header, offset + 8);
            if (tilesX is < 4 or > 4096 || tilesY is < 4 or > 4096) continue;
            var vector = offset + 184;
            var start = BitConverter.ToInt64(header, vector);
            var end = BitConverter.ToInt64(header, vector + 8);
            if (start < 0x10000 || end <= start || end >= 0x7FFF_FFFF_FFFF) continue;
            var length = end - start;
            if (length is < 4096 or > MaxPackedBytes) continue;
            for (var relative = 24; relative <= 144; relative += 4)
            {
                if (vector + relative + 4 > header.Length) break;
                var bytesPerRow = BitConverter.ToInt32(header, vector + relative);
                if (bytesPerRow <= 0 || length % bytesPerRow != 0) continue;
                var height = length / bytesPerRow;
                var width = (long)bytesPerRow * 2;
                if (width > 100000 || height > 100000 ||
                    Math.Abs(width - tilesX * TileCells) > TileCells ||
                    Math.Abs(height - tilesY * TileCells) > TileCells ||
                    gridX >= width || gridY >= height) continue;
                var sample = read(start, Math.Min(16 * 1024, (int)length));
                if (sample == null || sample.Length != Math.Min(16 * 1024, (int)length)) continue;
                var (sampledBytes, inRange) = PackedTerrainValueProfile.Sample(sample);
                if (!PackedTerrainValueProfile.IsAccepted(sampledBytes, inRange)) continue;
                var candidate = new Candidate(start, bytesPerRow, (int)height,
                    sampledBytes * 2, inRange, offset - 24, (int)tilesX, (int)tilesY);
                if (selected != null && selected != candidate) ambiguous = true;
                else selected = candidate;
                break;
            }
        }
        if (ambiguous) return new Result("ambiguous_candidates", null);
        if (selected == null) return new Result("no_candidate", null);
        var centerX = (int)gridX;
        var centerY = (int)gridY;
        var left = Math.Max(0, centerX - PatchRadius);
        var right = Math.Min(selected.BytesPerRow * 2 - 1, centerX + PatchRadius);
        var top = Math.Max(0, centerY - PatchRadius);
        var bottom = Math.Min(selected.Rows - 1, centerY + PatchRadius);
        var patchRows = new List<string>();
        for (var y = top; y <= bottom; y++)
        {
            var firstByte = left / 2;
            var byteCount = right / 2 - firstByte + 1;
            var bytes = read(selected.Start + (long)y * selected.BytesPerRow + firstByte, byteCount);
            if (bytes == null || bytes.Length != byteCount)
                return new Result("patch_unreadable", null);
            var row = new char[right - left + 1];
            for (var x = left; x <= right; x++)
            {
                var value = bytes[x / 2 - firstByte];
                var nibble = (x & 1) == 0 ? value & 15 : value >> 4;
                row[x - left] = nibble == 0 ? '0' : '1';
            }
            patchRows.Add(new string(row));
        }
        return new Result("candidate_patch_observed", new Observation(
            selected.BytesPerRow * 2, selected.Rows, selected.BytesPerRow,
            left, top, patchRows.ToArray(), selected.SampledNibbles,
            selected.InRangeNibbles))
        {
            Source = new PackedSource(selected.Start, selected.BytesPerRow, selected.Rows),
            Layout = selected.TerrainOffset >= 0
                ? new AreaLayout(area, selected.TerrainOffset, selected.TilesX, selected.TilesY)
                : null
        };
    }

    private sealed record Candidate(long Start, int BytesPerRow, int Rows,
        int SampledNibbles, int InRangeNibbles,
        int TerrainOffset, int TilesX, int TilesY);
}
