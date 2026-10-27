using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PartyPilot.Sensor;

// Diagnostic export only. A plausible memory layout is not a verified game map.
internal static class PackedTerrainCapture
{
    private const int ChunkBytes = 64 * 1024;
    private const int MaxBytes = 64 * 1024 * 1024;

    internal sealed record Outcome(string Status, string? DataFile = null);

    internal static string UpdateAnchors(string dataFile, int processId, string areaHash,
        string areaAddress, string anchor, GeneratedRoomProbe.Result? generatedRooms,
        TerrainPlacementProbe.Result? terrainPlacements, QuestTileProbe.Result? questTiles,
        Func<bool> areaStillCurrent)
    {
        var roomsReady = generatedRooms is { Status: "candidate_rooms_observed",
            AnchorsTruncated: false };
        var placementsReady = terrainPlacements is { Status: "candidate_placements_observed",
            Truncated: false };
        var tilesReady = questTiles is { Status: "candidate_tiles_observed", Truncated: false };
        if (!roomsReady && !placementsReady && !tilesReady) return "already_captured";
        if (!areaStillCurrent()) return "area_changed_before_update";
        string? temporary = null;
        try
        {
            var metadataFile = Path.ChangeExtension(dataFile, ".json");
            var info = new FileInfo(metadataFile);
            if (!info.Exists || info.Length is < 1 or > 65536) return "metadata_unavailable";
            if (JsonNode.Parse(File.ReadAllText(metadataFile)) is not JsonObject root ||
                root["Status"]?.GetValue<string>() != "candidate_capture_only" ||
                root["ProcessId"]?.GetValue<int>() != processId ||
                root["AreaHash"]?.GetValue<string>() != areaHash ||
                root["AreaAddress"]?.GetValue<string>() != areaAddress ||
                root["Anchor"]?.GetValue<string>() != anchor ||
                root["DataFile"]?.GetValue<string>() != Path.GetFileName(dataFile))
                return "metadata_mismatch";
            var changed = false;
            if (roomsReady && root["GeneratedRooms"] is null)
            {
                root["GeneratedRoomStatus"] = generatedRooms!.Status;
                root["GeneratedRoomAreaAddress"] = areaAddress;
                root["GeneratedRoomAnchor"] = anchor;
                root["GeneratedRooms"] = JsonSerializer.SerializeToNode(generatedRooms.Anchors);
                changed = true;
            }
            if (placementsReady && root["TerrainPlacements"] is null)
            {
                root["TerrainPlacementStatus"] = terrainPlacements!.Status;
                root["TerrainPlacementAreaAddress"] = areaAddress;
                root["TerrainPlacementAnchor"] = anchor;
                root["TerrainPlacements"] = JsonSerializer.SerializeToNode(terrainPlacements.Anchors);
                changed = true;
            }
            if (tilesReady && root["QuestTiles"] is null)
            {
                root["QuestTileStatus"] = questTiles!.Status;
                root["QuestTileAreaId"] = questTiles.AreaId;
                root["QuestTileAreaAddress"] = areaAddress;
                root["QuestTileAnchor"] = anchor;
                root["QuestTiles"] = JsonSerializer.SerializeToNode(questTiles.Anchors);
                changed = true;
            }
            if (!changed) return "already_captured";
            temporary = metadataFile + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temporary, root.ToJsonString());
            if (new FileInfo(temporary).Length > 65536) return "metadata_too_large";
            if (!areaStillCurrent()) return "area_changed_during_update";
            File.Move(temporary, metadataFile, overwrite: true);
            temporary = null;
            return "markers_updated";
        }
        catch
        {
            return "marker_update_failed";
        }
        finally
        {
            if (temporary != null) TryDelete(temporary);
        }
    }

    internal static Outcome Save(PackedTerrainProbe.PackedSource source,
        PackedTerrainProbe.Observation observation,
        RawRenderPositionReader.Position position, float observedWorldX, float observedWorldY,
        Func<long, int, byte[]?> read, Func<bool> areaStillCurrent,
        string directory, int processId,
        string areaHash, string areaAddress, string anchor, DateTime seenUtc,
        GeneratedRoomProbe.Result? generatedRooms = null,
        TerrainPlacementProbe.Result? terrainPlacements = null,
        QuestTileProbe.Result? questTiles = null)
    {
        var length = (long)source.BytesPerRow * source.Rows;
        if (source.Start < 0x10000 || source.BytesPerRow <= 0 || source.Rows <= 0 ||
            length is < 4096 or > MaxBytes || processId <= 0 ||
            observation.Width != source.BytesPerRow * 2 || observation.Height != source.Rows ||
            !float.IsFinite(position.GridX) || !float.IsFinite(position.GridY) ||
            !float.IsFinite(observedWorldX) || !float.IsFinite(observedWorldY) ||
            position.GridX < 0 || position.GridY < 0 ||
            position.GridX >= observation.Width || position.GridY >= observation.Height ||
            areaStillCurrent == null)
            return new Outcome("invalid_source");
        if (!areaStillCurrent()) return new Outcome("area_changed_before_read");

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
            if (!MatchesPatch(temporary, observation, source.BytesPerRow))
                return new Outcome("patch_mismatch");
            if (!areaStillCurrent())
                return new Outcome("area_changed_during_read");

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
                RawGridX = position.GridX,
                RawGridY = position.GridY,
                RawWorldX = position.WorldX,
                RawWorldY = position.WorldY,
                ObservedWorldX = observedWorldX,
                ObservedWorldY = observedWorldY,
                PatchX = observation.PatchX,
                PatchY = observation.PatchY,
                PatchRows = observation.PatchRows,
                SeenUtc = seenUtc,
                Width = source.BytesPerRow * 2,
                Height = source.Rows,
                BytesPerRow = source.BytesPerRow,
                PackedBytes = length,
                Sha256 = digest,
                DataFile = Path.GetFileName(dataFile),
                GeneratedRoomStatus = generatedRooms?.Status,
                GeneratedRoomAreaAddress = generatedRooms is null ? null : areaAddress,
                GeneratedRoomAnchor = generatedRooms is null ? null : anchor,
                GeneratedRooms = generatedRooms is { Status: "candidate_rooms_observed", AnchorsTruncated: false }
                    ? generatedRooms.Anchors : null,
                TerrainPlacementStatus = terrainPlacements?.Status,
                TerrainPlacementAreaAddress = terrainPlacements is null ? null : areaAddress,
                TerrainPlacementAnchor = terrainPlacements is null ? null : anchor,
                TerrainPlacements = terrainPlacements is { Status: "candidate_placements_observed", Truncated: false }
                    ? terrainPlacements.Anchors : null,
                QuestTileStatus = questTiles?.Status,
                QuestTileAreaId = questTiles?.AreaId,
                QuestTileAreaAddress = questTiles is null ? null : areaAddress,
                QuestTileAnchor = questTiles is null ? null : anchor,
                QuestTiles = questTiles is { Status: "candidate_tiles_observed", Truncated: false }
                    ? questTiles.Anchors : null
            }));
            if (!areaStillCurrent())
            {
                TryDelete(metadataFile);
                TryDelete(dataFile);
                return new Outcome("area_changed_during_read");
            }
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

    private static bool MatchesPatch(string path, PackedTerrainProbe.Observation patch,
        int bytesPerRow)
    {
        if (patch.PatchX < 0 || patch.PatchY < 0 || patch.PatchRows.Length is < 1 or > 9 ||
            patch.PatchY + patch.PatchRows.Length > patch.Height) return false;
        using var stream = File.OpenRead(path);
        foreach (var (row, rowIndex) in patch.PatchRows.Select((row, index) => (row, index)))
        {
            if (row.Length is < 1 or > 9 || patch.PatchX + row.Length > patch.Width)
                return false;
            for (var x = 0; x < row.Length; x++)
            {
                stream.Position = (long)(patch.PatchY + rowIndex) * bytesPerRow +
                    (patch.PatchX + x) / 2;
                var value = stream.ReadByte();
                if (value < 0) return false;
                var nibble = ((patch.PatchX + x) & 1) == 0 ? value & 15 : value >> 4;
                if (row[x] != (nibble == 0 ? '0' : '1')) return false;
            }
        }
        return true;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* Preserve the original failure status. */ }
    }
}
