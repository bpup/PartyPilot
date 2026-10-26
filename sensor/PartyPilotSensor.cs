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
    private static readonly IPEndPoint PrivacyReceiver = new(IPAddress.Loopback, 47619);
    private readonly UdpClient _udp = new();
    private DateTime _lastSentUtc;
    private DateTime _lastSlotScanUtc;
    private long _lastSlotScanStateAddress;
    private long _lastSlotScanDataAddress;
    private long _lastSlotScanLocalAddress;
    private LocalSlotCandidateScanner.Result? _slotCandidates;
    private string? _slotCandidateStatus;
    private DateTime _lastRadarSentUtc;
    private DateTime _lastCardSentUtc;
    private string? _cardModulePath;
    private Task<long>? _cardVtableTask;

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
                    byte[]? ReadCandidate(long address, int size)
                    {
                        try { return data.M.ReadMem(address, size); }
                        catch { return null; } // An unreadable candidate is not an overall scan failure.
                    }
                    _slotCandidates = LocalSlotCandidateScanner.Scan(stateAddress, data.Address, localAddress,
                        paths, ReadCandidate,
                        address => RawCharacterPathReader.Read(address, ReadCandidate));
                    if (_slotCandidates.Status is "single_slot_candidate" or "two_slot_candidate")
                    {
                        var positions = _slotCandidates.OrderedAddresses.Select(address =>
                            long.TryParse(address.AsSpan(2),
                                System.Globalization.NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture, out var entity)
                                ? RawRenderPositionReader.Read(entity, ReadCandidate) : null).ToArray();
                        if (LocalSlotCandidateScanner.Revalidate(_slotCandidates, localAddress,
                                paths, ReadCandidate,
                                address => RawCharacterPathReader.Read(address, ReadCandidate)))
                            _slotCandidates = _slotCandidates with
                            {
                                RawPositions = positions,
                                RawPositionsSeenUtc = now
                            };
                    }
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
        object? groundHover = null;
        string? groundHoverStatus = null;
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
            try
            {
                groundHover = ReadGroundHover();
                groundHoverStatus = groundHover is null ? "none" : "ui_hover_observed";
            }
            catch
            {
                groundHoverStatus = "scan_failed";
            }
        }

        object[]? itemCards = null;
        string? itemCardStatus = null;
        DateTime? itemCardSeenUtc = null;
        if (Settings.DiagnoseItemCards.Value && now - _lastCardSentUtc >= TimeSpan.FromSeconds(1))
        {
            _lastCardSentUtc = now;
            itemCardSeenUtc = now;
            try
            {
                var process = GameController.Window.Process;
                var module = process.MainModule;
                var modulePath = module?.FileName;
                if (module == null || string.IsNullOrWhiteSpace(modulePath))
                    itemCardStatus = "layout_unavailable";
                else
                {
                    if (!string.Equals(_cardModulePath, modulePath, StringComparison.OrdinalIgnoreCase))
                    {
                        _cardModulePath = modulePath;
                        _cardVtableTask = null;
                    }
                    _cardVtableTask ??= Task.Run(() => UiItemCardVtableLocator.Resolve(modulePath));
                    if (!_cardVtableTask.IsCompleted)
                        itemCardStatus = "layout_pending";
                    else if (!_cardVtableTask.IsCompletedSuccessfully || _cardVtableTask.Result == 0)
                        itemCardStatus = "layout_unavailable";
                    else
                    {
                        var result = UiItemCardTreeScanner.Scan(
                            (address, size) =>
                            {
                                try { return GameController.IngameState.Data.M.ReadMem(address, size); }
                                catch { return null; }
                            },
                            module.BaseAddress.ToInt64(), module.ModuleMemorySize,
                            GameController.IngameState.Address, _cardVtableTask.Result);
                        itemCardStatus = result.Cards.Length > 32 ? "too_many_cards" : result.Status;
                        if (itemCardStatus == "ok")
                        {
                            var (players, loot) = ReadItemCardCorrelations();
                            var slotCandidate = Settings.DiagnoseLocalSlotCandidates.Value &&
                                _slotCandidateStatus == "ok" && _slotCandidates != null &&
                                now >= _lastSlotScanUtc &&
                                now - _lastSlotScanUtc <= TimeSpan.FromSeconds(1) &&
                                _lastSlotScanStateAddress == GameController.IngameState.Address &&
                                _lastSlotScanDataAddress == GameController.IngameState.Data.Address &&
                                _lastSlotScanLocalAddress == (player?.Address ?? 0)
                                    ? _slotCandidates : null;
                            if (slotCandidate != null)
                            {
                                var paths = new Dictionary<long, string>();
                                foreach (var entity in GameController.Entities)
                                {
                                    if (entity?.GetComponent<Player>() == null || entity.Address <= 0) continue;
                                    paths.TryAdd(entity.Address, entity.Path ?? "");
                                    if (paths.Count == 256) break;
                                }
                                byte[]? ReadCandidate(long address, int size)
                                {
                                    try { return GameController.IngameState.Data.M.ReadMem(address, size); }
                                    catch { return null; }
                                }
                                if (!LocalSlotCandidateScanner.Revalidate(slotCandidate, player?.Address ?? 0,
                                        paths, ReadCandidate,
                                        address => RawCharacterPathReader.Read(address, ReadCandidate)))
                                    slotCandidate = null;
                            }
                            itemCards = result.Cards.Select(card =>
                            {
                                var match = UiItemCardCorrelator.Resolve(card, players, loot, slotCandidate);
                                return (object)new
                                {
                                    Object = FormatAddress(card.Object),
                                    Item = FormatAddress(card.Item),
                                    Owner = FormatAddress(card.Owner),
                                    Evidence = "unassigned_ui_card",
                                    OwnerMatch = match.Owner,
                                    LootMatch = match.Loot,
                                    SlotCandidate = match.Slot
                                };
                            }).ToArray();
                        }
                    }
                }
            }
            catch
            {
                itemCardStatus = "scan_failed";
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
            GroundHover = groundHover,
            GroundHoverStatus = groundHoverStatus,
            ItemCards = itemCards,
            ItemCardStatus = itemCardStatus,
            ItemCardSeenUtc = itemCardSeenUtc,
            VisiblePlayers = visiblePlayers,
            VisiblePlayersStatus = visiblePlayersStatus
        });
        var bytes = Encoding.UTF8.GetBytes(message);
        _udp.Send(bytes, bytes.Length, Receiver);
        if (radarSeenUtc.HasValue) _udp.Send(bytes, bytes.Length, RadarReceiver);
        var privacyPulse = JsonSerializer.SerializeToUtf8Bytes(new
        {
            ProcessId = GameController.Window.Process.Id,
            SeenUtc = now,
            PrivacyStatus = "render-ok"
        });
        _udp.Send(privacyPulse, privacyPulse.Length, PrivacyReceiver);
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

    private (UiItemCardCorrelator.Player[] Players, UiItemCardCorrelator.Loot[] Loot)
        ReadItemCardCorrelations()
    {
        var players = new List<UiItemCardCorrelator.Player>();
        var loot = new List<UiItemCardCorrelator.Loot>();
        foreach (var entity in GameController.Entities)
        {
            if (entity == null || entity.Id == 0) continue;
            var path = entity.Path;
            if (entity.Address > 0 && path != null && RadarClassifier.IsPlayerPath(path) &&
                entity.GetComponent<Player>() != null)
                players.Add(new(entity.Address, entity.Id));
            if (path == null || !RadarClassifier.IsWorldItemWrapperPath(path)) continue;
            var inner = entity.GetComponent<WorldItem>()?.ItemEntity;
            var itemPath = inner?.Path;
            var name = inner?.GetComponent<Base>()?.Name?.Trim();
            if (inner?.Address > 0 && itemPath?.StartsWith("Metadata/Items/", StringComparison.Ordinal) == true &&
                Encoding.UTF8.GetByteCount(itemPath) <= 256 &&
                !string.IsNullOrWhiteSpace(name) && Encoding.UTF8.GetByteCount(name) <= 96)
                loot.Add(new(inner.Address, entity.Id, itemPath, name));
        }
        return (players.ToArray(), loot.ToArray());
    }

    private object? ReadGroundHover()
    {
        // This is UI hover evidence only. It does not prove a controller card is selected
        // or that any player can pick up this item.
        var hovered = GameController.IngameState.IngameUi.ItemsOnGroundLabelElement?.ItemOnHover;
        if (hovered == null || hovered.Id == 0) return null;
        var path = hovered.Path;
        if (string.IsNullOrWhiteSpace(path) ||
            !path.StartsWith("Metadata/", StringComparison.Ordinal) ||
            Encoding.UTF8.GetByteCount(path) > 256) return null;
        string? itemPath = null;
        string? itemName = null;
        try
        {
            var item = hovered.GetComponent<WorldItem>()?.ItemEntity;
            var candidatePath = item?.Path;
            var candidateName = item?.GetComponent<Base>()?.Name?.Trim();
            if (candidatePath?.StartsWith("Metadata/Items/", StringComparison.OrdinalIgnoreCase) == true &&
                Encoding.UTF8.GetByteCount(candidatePath) <= 256 &&
                !string.IsNullOrWhiteSpace(candidateName) &&
                Encoding.UTF8.GetByteCount(candidateName) <= 96)
            {
                itemPath = candidatePath;
                itemName = candidateName;
            }
        }
        catch
        {
            // Keep the observed hover identity without claiming its inner item resolved.
        }
        return new
        {
            EntityId = hovered.Id,
            Address = FormatAddress(hovered.Address),
            Path = path,
            ItemPath = itemPath,
            Name = itemName,
            Evidence = "ui_hover_only"
        };
    }

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
