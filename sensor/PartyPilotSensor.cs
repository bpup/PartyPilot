using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ExileCore2;
using ExileCore2.PoEMemory.Components;

namespace PartyPilot.Sensor;

public sealed class PartyPilotSensor : BaseSettingsPlugin<SensorSettings>
{
    private static readonly IPEndPoint Receiver = new(IPAddress.Loopback, 47617);
    private static readonly IPEndPoint RadarReceiver = new(IPAddress.Loopback, 47618);
    private readonly UdpClient _udp = new();
    private DateTime _lastSentUtc;
    private DateTime _lastSlotScanUtc;
    private long _lastSlotScanStateAddress;
    private long _lastSlotScanDataAddress;
    private long _lastSlotScanLocalAddress;
    private LocalSlotCandidateScanner.Result? _slotCandidates;
    private string? _slotCandidateStatus;
    private DateTime _lastRadarSentUtc;

    public override void Render()
    {
        if (!Settings.Enable.Value) return;
        var now = DateTime.UtcNow;
        if (now - _lastSentUtc < TimeSpan.FromMilliseconds(100)) return;
        _lastSentUtc = now;

        var player = GameController.Player;
        var render = player?.GetComponent<Render>();
        if (render == null) return;
        var vitals = player?.GetComponent<Life>();
        object[]? visiblePlayers = null;
        string? visiblePlayersStatus = null;
        string? ingameDataAddress = null;
        string? observedLocalAddress = null;
        if (Settings.DiagnoseVisiblePlayers.Value)
        {
            try
            {
                visiblePlayers = ReadVisiblePlayers(player?.Id);
                ingameDataAddress = FormatAddress(GameController.IngameState.Data.Address);
                observedLocalAddress = FormatAddress(player?.Address ?? 0);
                visiblePlayersStatus = "ok";
            }
            catch
            {
                // A diagnostic scan must not interrupt the primary local-player sample.
                visiblePlayersStatus = "scan_failed";
            }
        }

        if (Settings.DiagnoseLocalSlotCandidates.Value)
        {
            try
            {
                var data = GameController.IngameState.Data;
                var stateAddress = GameController.IngameState.Address;
                var localAddress = player?.Address ?? 0;
                ingameDataAddress ??= FormatAddress(data.Address);
                observedLocalAddress ??= FormatAddress(localAddress);
                if (_slotCandidates == null || stateAddress != _lastSlotScanStateAddress ||
                    data.Address != _lastSlotScanDataAddress ||
                    localAddress != _lastSlotScanLocalAddress || now - _lastSlotScanUtc >= TimeSpan.FromSeconds(1))
                {
                    var paths = new Dictionary<long, string>();
                    foreach (var entity in GameController.Entities)
                    {
                        if (entity?.GetComponent<Player>() == null || entity.Address <= 0) continue;
                        paths.TryAdd(entity.Address, entity.Path ?? "");
                        if (paths.Count == 256) break;
                    }
                    _slotCandidates = LocalSlotCandidateScanner.Scan(stateAddress, data.Address, localAddress, paths,
                        (address, size) =>
                        {
                            try { return data.M.ReadMem(address, size); }
                            catch { return null; } // An unreadable candidate is not an overall scan failure.
                        });
                    _lastSlotScanStateAddress = stateAddress;
                    _lastSlotScanDataAddress = data.Address;
                    _lastSlotScanLocalAddress = localAddress;
                    _lastSlotScanUtc = now;
                }
                _slotCandidateStatus = "ok";
            }
            catch
            {
                _slotCandidates = null;
                _slotCandidateStatus = "scan_failed";
            }
        }
        else
        {
            _slotCandidates = null;
            _slotCandidateStatus = null;
        }

        object[]? radarEntities = null;
        string? radarStatus = null;
        DateTime? radarSeenUtc = null;
        if (Settings.DiagnoseRadarEntities.Value && now - _lastRadarSentUtc >= TimeSpan.FromMilliseconds(500))
        {
            _lastRadarSentUtc = now;
            radarSeenUtc = now;
            try
            {
                radarEntities = ReadRadarEntities(player!.Id, render.Pos.X, render.Pos.Y);
                radarStatus = "ok";
            }
            catch
            {
                radarStatus = "scan_failed";
            }
        }

        var position = render.Pos;
        // AreaName is diagnostic only. It does not identify a specific instance.
        var message = JsonSerializer.Serialize(new
        {
            ProcessId = GameController.Window.Process.Id,
            SeenUtc = now,
            AreaName = GameController.Area.CurrentArea?.Name,
            AreaHash = GameController.IngameState.Data.CurrentAreaHash,
            X = position.X,
            Y = position.Y,
            Z = position.Z,
            Life = vitals?.CurHP,
            LifeMax = vitals?.MaxHP,
            Mana = vitals?.CurMana,
            ManaMax = vitals?.MaxMana,
            EnergyShield = vitals?.CurES,
            EnergyShieldMax = vitals?.MaxES,
            IngameDataAddress = ingameDataAddress,
            ObservedLocalAddress = observedLocalAddress,
            LocalSlotCandidates = Settings.DiagnoseLocalSlotCandidates.Value ? _slotCandidates : null,
            LocalSlotCandidateStatus = Settings.DiagnoseLocalSlotCandidates.Value ? _slotCandidateStatus : null,
            RadarEntities = radarEntities,
            RadarStatus = radarStatus,
            RadarSeenUtc = radarSeenUtc,
            VisiblePlayers = visiblePlayers,
            VisiblePlayersStatus = visiblePlayersStatus
        });
        var bytes = Encoding.UTF8.GetBytes(message);
        _udp.Send(bytes, bytes.Length, Receiver);
        if (radarSeenUtc.HasValue) _udp.Send(bytes, bytes.Length, RadarReceiver);
    }

    private object[] ReadVisiblePlayers(uint? observedLocalId)
    {
        var players = new List<object>();
        var seen = new HashSet<uint>();
        foreach (var entity in GameController.Entities)
        {
            var component = entity?.GetComponent<Player>();
            if (component == null || string.IsNullOrWhiteSpace(component.PlayerName)) continue;
            if (Encoding.UTF8.GetByteCount(component.PlayerName) > 64 ||
                Encoding.UTF8.GetByteCount(entity!.Path ?? "") > 256) continue;
            var position = entity!.Pos;
            if (entity.Id == 0 || !seen.Add(entity.Id) || !float.IsFinite(position.X) ||
                !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
                continue;
            players.Add(new
            {
                EntityId = entity.Id,
                Address = FormatAddress(entity.Address),
                Path = entity.Path,
                Name = component.PlayerName.Trim(),
                X = position.X,
                Y = position.Y,
                Z = position.Z,
                IsObservedLocal = observedLocalId.HasValue && entity.Id == observedLocalId.Value
            });
            if (players.Count == 16) break;
        }
        return players.ToArray();
    }

    private static string? FormatAddress(long address) => address > 0 ? $"0x{address:X}" : null;

    private object[] ReadRadarEntities(uint observedLocalId, float localX, float localY)
    {
        var seen = new HashSet<uint>();
        var candidates = new List<(object Value, bool Navigation, double Distance)>();
        foreach (var entity in GameController.Entities)
        {
            if (entity == null || entity.Id == 0 || entity.Id == observedLocalId || !seen.Add(entity.Id))
                continue;
            var path = entity.Path;
            if (path == null || Encoding.UTF8.GetByteCount(path) > 256) continue;
            var playerComponent = entity.GetComponent<Player>();
            var playerName = playerComponent?.PlayerName?.Trim();
            var isPlayer = RadarClassifier.IsPlayerPath(path) &&
                !string.IsNullOrWhiteSpace(playerName) && Encoding.UTF8.GetByteCount(playerName) <= 64;
            string? itemPath = null;
            string? lootName = null;
            int? lootStack = null;
            string? lootRarity = null;
            if (RadarClassifier.IsWorldItemWrapperPath(path))
            {
                try
                {
                    var item = entity.GetComponent<WorldItem>()?.ItemEntity;
                    var candidatePath = item?.Path;
                    var candidateName = item?.GetComponent<Base>()?.Name?.Trim();
                    if (candidatePath?.StartsWith("Metadata/Items/", StringComparison.OrdinalIgnoreCase) == true &&
                        Encoding.UTF8.GetByteCount(candidatePath) <= 256 &&
                        !string.IsNullOrWhiteSpace(candidateName) && Encoding.UTF8.GetByteCount(candidateName) <= 96)
                    {
                        itemPath = candidatePath;
                        lootName = candidateName;
                        try { lootStack = RadarLootMetadata.Stack(item?.GetComponent<Stack>()?.Size); }
                        catch { /* Stack remains unknown when its component is unreadable. */ }
                        try { lootRarity = RadarLootMetadata.Rarity(item?.GetComponent<Mods>()?.ItemRarity.ToString()); }
                        catch { /* Rarity remains unknown when its component is unreadable. */ }
                    }
                }
                catch
                {
                    // An unreadable wrapper must not become an asserted loot marker.
                }
            }
            var category = lootName != null ? "loot" : isPlayer ? "player" : RadarClassifier.Category(path);
            if (category == null) continue;
            var pos = entity.Pos;
            if (!float.IsFinite(pos.X) || !float.IsFinite(pos.Y) || !float.IsFinite(pos.Z)) continue;
            var dx = (double)pos.X - localX;
            var dy = (double)pos.Y - localY;
            candidates.Add((new
            {
                EntityId = entity.Id,
                Category = category,
                Path = path,
                Label = lootName ?? (isPlayer ? playerName : null),
                ItemPath = itemPath,
                Stack = lootStack,
                Rarity = lootRarity,
                X = pos.X,
                Y = pos.Y,
                Z = pos.Z
            }, RadarClassifier.IsNavigation(category), dx * dx + dy * dy));
        }
        return RadarPacketBudget.Select(candidates.OrderByDescending(item => item.Navigation)
            .ThenBy(item => item.Distance).Select(item => item.Value));
    }
}
