using System.Globalization;
using System.Text;
using System.Text.Json;
using PartyPilot.Sensor;

namespace PartyPilot.Probe;

// Candidate navigation markers from the independently read awake entity table.
// Path matches do not establish quest progress or interactability.
internal static class ProbeRadarPacket
{
    private const uint HighEntityIdStart = 1_073_741_824;
    private const int MaxAwakeReads = 2500;
    private sealed record Marker(uint EntityId, string Category, string Path,
        string? Label, double X, double Y, bool QuestStateCandidate);

    internal static byte[]? TryEncodeMotion(int processId, DateTime seenUtc,
        uint areaHash, long areaAddress, RolePositionCandidate local)
    {
        if (processId <= 0 || seenUtc.Kind != DateTimeKind.Utc || areaHash == 0 ||
            !Win32Memory.Plausible(areaAddress) || local.Slot != 1 ||
            !float.IsFinite(local.WorldX) || !float.IsFinite(local.WorldY) ||
            !float.IsFinite(local.GridX) || !float.IsFinite(local.GridY)) return null;
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            Kind = "radar_motion",
            ProcessId = processId,
            SeenUtc = seenUtc,
            AreaHash = areaHash,
            RadarAreaAddress = (string?)null,
            RadarStateAreaAddress = $"0x{areaAddress:X}",
            X = local.WorldX,
            Y = local.WorldY,
            RadarGridX = local.GridX,
            RadarGridY = local.GridY
        });
    }

    // A confirmed terrain capture may be drawn while the entity table is
    // unavailable. This packet carries no claim that markers were scanned.
    internal static byte[]? TryEncodeTerrainOnly(int processId, DateTime seenUtc,
        uint areaHash, long areaAddress, RolePositionCandidate local)
    {
        if (processId <= 0 || seenUtc.Kind != DateTimeKind.Utc || areaHash == 0 ||
            !Win32Memory.Plausible(areaAddress) || local.Slot != 1 ||
            !float.IsFinite(local.WorldX) || !float.IsFinite(local.WorldY) ||
            !float.IsFinite(local.GridX) || !float.IsFinite(local.GridY)) return null;
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            ProcessId = processId,
            RadarStatus = "ok",
            RadarSeenUtc = seenUtc,
            AreaHash = areaHash,
            RadarAreaAddress = (string?)null,
            RadarStateAreaAddress = $"0x{areaAddress:X}",
            X = local.WorldX,
            Y = local.WorldY,
            RadarGridX = local.GridX,
            RadarGridY = local.GridY,
            RadarEntities = Array.Empty<Marker>(),
            DormantAreaMap = (object?)null
        });
    }

    internal static byte[]? TryEncode(int processId, DateTime seenUtc, uint areaHash,
        long areaAddress, RolePositionCandidate local,
        ProbeEntityMapScanner.Result map, Func<long, int, byte[]?> read) =>
        TryEncode(processId, seenUtc, areaHash, areaAddress, local, map,
            entity => RawMetadataPathReader.Read(entity, read),
            entity => RawRenderPositionReader.Read(entity, read),
            entity => RawTargetableReader.MeetsQuestState(entity, read),
            entity => RawMinimapIconReader.Read(entity, read));

    internal static byte[]? TryEncode(int processId, DateTime seenUtc, uint areaHash,
        long areaAddress, RolePositionCandidate local,
        ProbeEntityMapScanner.Result map, Func<long, string?> readPath,
        Func<long, RawRenderPositionReader.Position?> readPosition,
        Func<long, bool?>? readQuestState = null,
        Func<long, string?>? readIcon = null)
    {
        if (processId <= 0 || seenUtc.Kind != DateTimeKind.Utc || areaHash == 0 ||
            !Win32Memory.Plausible(areaAddress) ||
            map.Status != "candidate_unverified_build" ||
            map.EntityListOffset is null ||
            !float.IsFinite(local.WorldX) || !float.IsFinite(local.WorldY) ||
            !float.IsFinite(local.GridX) || !float.IsFinite(local.GridY) ||
            !local.EntityAddress.StartsWith("0x", StringComparison.Ordinal) ||
            !long.TryParse(local.EntityAddress.AsSpan(2), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out var localAddress) ||
            map.Awake.Count(entry => entry.Address == localAddress) != 1)
            return null;

        var markers = new List<Marker>();
        // 0.6.17 reads the high-ID branch of the awake map separately. Reserve
        // read budget for those entries before scanning ordinary IDs so the
        // bounded packet builder does not discard them solely by sort order.
        foreach (var entry in map.Awake
                     .OrderByDescending(entry => entry.Id >= HighEntityIdStart)
                     .ThenBy(entry => entry.Id).Take(MaxAwakeReads))
        {
            if (entry.Address == localAddress) continue;
            if (ReadMarker(entry, readPath, readPosition, readQuestState, readIcon) is not { } marker) continue;
            markers.Add(marker);
            if (markers.Count == 96) break;
        }
        var dormant = new List<Marker>();
        // Bound read work per process. A later survey can rotate through the
        // remaining sleeping entries without stalling movement sampling.
        foreach (var entry in map.Sleeping.OrderBy(entry => entry.Id).Take(256))
        {
            if (ReadMarker(entry, readPath, readPosition, readQuestState, readIcon) is not { } marker) continue;
            dormant.Add(marker);
            if (dormant.Count == 24) break;
        }
        object? dormantMap = map.Sleeping.Length == 0 ? null : new
        {
            Status = map.Status,
            SeenUtc = seenUtc,
            AreaAddress = $"0x{areaAddress:X}",
            EntityListOffset = map.EntityListOffset.Value,
            AwakeCount = map.Awake.Length,
            SleepingCount = map.Sleeping.Length,
            LocalPosition = new { WorldX = local.WorldX, WorldY = local.WorldY },
            NavigationCandidates = dormant
        };
        var packet = JsonSerializer.SerializeToUtf8Bytes(new
        {
            ProcessId = processId,
            RadarStatus = "ok",
            RadarSeenUtc = seenUtc,
            AreaHash = areaHash,
            RadarAreaAddress = (string?)null,
            RadarStateAreaAddress = $"0x{areaAddress:X}",
            X = local.WorldX,
            Y = local.WorldY,
            RadarGridX = local.GridX,
            RadarGridY = local.GridY,
            RadarEntities = markers,
            DormantAreaMap = dormantMap
        });
        return packet.Length <= 65507 ? packet : null;
    }

    private static Marker? ReadMarker(AreaEntityMapSnapshotReader.Entry entry,
        Func<long, string?> readPath,
        Func<long, RawRenderPositionReader.Position?> readPosition,
        Func<long, bool?>? readQuestState,
        Func<long, string?>? readIcon)
    {
        var path = readPath(entry.Address);
        if (path == null || !path.StartsWith("Metadata/", StringComparison.Ordinal) ||
            Encoding.UTF8.GetByteCount(path) > 256) return null;
        var category = RadarClassifier.Category(path);
        var iconQuestLabel = category is not ("checkpoint" or "waypoint" or "portal" or
            "transition" or "quest" or "boss" or "essence" or "league") &&
            !path.StartsWith("Metadata/Items/", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("Metadata/Effects/", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("Metadata/Projectiles/", StringComparison.OrdinalIgnoreCase)
            ? RadarClassifier.QuestIconLabel(readIcon?.Invoke(entry.Address)) : null;
        if (iconQuestLabel != null) category = "quest";
        var questStateCandidate = false;
        if (category is not ("quest" or "league") &&
            RadarClassifier.CanProbeQuestState(path) &&
            readQuestState?.Invoke(entry.Address) == true)
        {
            category = "quest";
            questStateCandidate = true;
        }
        if (category is not ("checkpoint" or "waypoint" or "portal" or
            "transition" or "quest" or "boss" or "essence")) return null;
        var label = category switch
        {
            "quest" when questStateCandidate => RadarClassifier.QuestStateCandidateLabel(path),
            "quest" => iconQuestLabel ?? RadarClassifier.QuestLabel(path),
            "boss" => RadarClassifier.BossLabel(path),
            _ => null
        };
        if ((category is "quest" or "boss") &&
            (string.IsNullOrWhiteSpace(label) || Encoding.UTF8.GetByteCount(label) > 96))
            return null;
        var position = readPosition(entry.Address);
        if (position == null || !float.IsFinite(position.WorldX) ||
            !float.IsFinite(position.WorldY)) return null;
        return new Marker(entry.Id, category, path, label,
            position.WorldX, position.WorldY, questStateCandidate);
    }
}
