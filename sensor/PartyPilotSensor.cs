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
    private static readonly IPEndPoint FastCandidateReceiver = new(IPAddress.Loopback, 47620);
    private static readonly IPEndPoint PairingReceiver = new(IPAddress.Loopback, 47621);
    private readonly UdpClient _udp = new();
    private DateTime _lastSentUtc;
    private DateTime _lastFastCandidateSentUtc;
    private DateTime _lastSlotScanUtc;
    private long _lastSlotScanStateAddress;
    private long _lastSlotScanDataAddress;
    private long _lastSlotScanLocalAddress;
    private LocalSlotCandidateScanner.Result? _slotCandidates;
    private string? _slotCandidateStatus;
    private DateTime _lastSkillBindScanUtc;
    private long _skillBindStateAddress;
    private long _skillBindAreaAddress;
    private long _skillBindLocalAddress;
    private string? _skillBindOrderKey;
    private SkillBindSourceScanner.Result? _skillBindSource;
    private string? _skillBindSourceStatus;
    private DateTime _lastRadarSentUtc;
    private DateTime _lastDormantMapScanUtc;
    private object? _dormantMapObservation;
    private readonly DormantNavigationSurvey _dormantSurvey = new();
    private long _dormantMapStateAddress;
    private long _dormantMapDataAddress;
    private long _dormantMapLocalAddress;
    private uint _dormantMapAreaHash;
    private readonly RadarMonsterTracker _monsterTracker = new();
    private uint? _monsterTrackerAreaHash;
    private DateTime _lastCardSentUtc;
    private DateTime _lastTerrainSentUtc;
    private readonly HashSet<string> _capturedTerrainKeys = new();
    private readonly Dictionary<string, string> _capturedTerrainFiles = new();
    private string? _terrainCaptureEpoch;
    private string? _generatedRoomProbeKey;
    private GeneratedRoomProbe.Result? _generatedRoomProbeCached;
    private string? _placementProbeKey;
    private TerrainPlacementProbe.Result? _placementProbeCached;
    private string? _questTileProbeKey;
    private QuestTileProbe.Result? _questTileProbeCached;
    private string? _cardModulePath;
    private Task<long>? _cardVtableTask;

    public override void Render()
    {
        if (!Settings.Enable.Value)
        {
            _monsterTracker.Reset();
            _monsterTrackerAreaHash = null;
            _dormantMapObservation = null;
            _lastDormantMapScanUtc = default;
            _dormantSurvey.Reset();
            return;
        }
        var now = DateTime.UtcNow;
        if (Settings.DiagnoseLocalSlotCandidates.Value &&
            (Settings.DiagnoseFastOrderedMotion.Value || Settings.ExperimentalOrderedFollow.Value) &&
            now - _lastFastCandidateSentUtc >= TimeSpan.FromMilliseconds(20))
        {
            _lastFastCandidateSentUtc = now;
            EmitFastOrderedCandidate(now);
        }
        if (now - _lastSentUtc < TimeSpan.FromMilliseconds(100)) return;
        _lastSentUtc = now;

        var player = GameController.Player;
        var render = player?.GetComponent<Render>();
        if (render == null) return;
        long frameStateAddress;
        long frameDataAddress;
        long frameLocalAddress;
        uint frameAreaHash;
        int frameProcessId;
        try
        {
            frameStateAddress = GameController.IngameState.Address;
            frameDataAddress = GameController.IngameState.Data.Address;
            frameLocalAddress = player?.Address ?? 0;
            frameAreaHash = GameController.IngameState.Data.CurrentAreaHash;
            frameProcessId = GameController.Window.Process.Id;
        }
        catch { return; }
        bool SameAreaAndPlayer()
        {
            try
            {
                return GameController.IngameState.Address == frameStateAddress &&
                    GameController.IngameState.Data.Address == frameDataAddress &&
                    GameController.IngameState.Data.CurrentAreaHash == frameAreaHash &&
                    (GameController.Player?.Address ?? 0) == frameLocalAddress &&
                    GameController.Window.Process.Id == frameProcessId;
            }
            catch { return false; }
        }
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
                var paths = new Dictionary<long, string>();
                var visiblePositions = new Dictionary<long, (float X, float Y, float Z)>();
                foreach (var entity in GameController.Entities)
                {
                    if (entity?.GetComponent<Player>() == null || entity.Address <= 0) continue;
                    paths.TryAdd(entity.Address, entity.Path ?? "");
                    var pos = entity.Pos;
                    if (float.IsFinite(pos.X) && float.IsFinite(pos.Y) && float.IsFinite(pos.Z))
                        visiblePositions.TryAdd(entity.Address, (pos.X, pos.Y, pos.Z));
                    if (paths.Count == 256) break;
                }
                byte[]? ReadCandidate(long address, int size)
                {
                    try { return data.M.ReadMem(address, size); }
                    catch { return null; }
                }
                string? ReadMissingPath(long address) =>
                    RawCharacterPathReader.Read(address, ReadCandidate);
                if (_slotCandidates == null || stateAddress != _lastSlotScanStateAddress ||
                    data.Address != _lastSlotScanDataAddress ||
                    localAddress != _lastSlotScanLocalAddress || now - _lastSlotScanUtc >= TimeSpan.FromSeconds(1))
                {
                    _slotCandidates = LocalSlotCandidateScanner.Scan(stateAddress, data.Address, localAddress,
                        paths, ReadCandidate, ReadMissingPath);
                    _lastSlotScanStateAddress = stateAddress;
                    _lastSlotScanDataAddress = data.Address;
                    _lastSlotScanLocalAddress = localAddress;
                    _lastSlotScanUtc = now;
                }
                if (_slotCandidates is { Status: "single_slot_candidate" or "two_slot_candidate" } candidate)
                    _slotCandidates = LocalSlotMotionSampler.Capture(candidate, localAddress,
                        paths, visiblePositions, ReadCandidate, ReadMissingPath, SameAreaAndPlayer,
                        render.Pos.X, render.Pos.Y, render.Pos.Z, now,
                        ingameStateAddress: stateAddress);
                _slotCandidateStatus = _slotCandidates == null ? "scan_failed" : "ok";
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
            _skillBindSource = null;
            _skillBindSourceStatus = null;
        }

        if (Settings.DiagnoseLocalSlotCandidates.Value)
        {
            try
            {
                var currentState = GameController.IngameState.Address;
                var currentArea = GameController.IngameState.Data.Address;
                var currentLocal = player?.Address ?? 0;
                var currentOrder = _slotCandidates is { Status: "single_slot_candidate" or "two_slot_candidate" }
                    ? string.Join(',', _slotCandidates.OrderedAddresses) : null;
                if (_skillBindStateAddress != currentState || _skillBindAreaAddress != currentArea ||
                    _skillBindLocalAddress != currentLocal || _skillBindOrderKey != currentOrder)
                {
                    _skillBindSource = null;
                    _skillBindSourceStatus = null;
                    _lastSkillBindScanUtc = default;
                    _skillBindStateAddress = currentState;
                    _skillBindAreaAddress = currentArea;
                    _skillBindLocalAddress = currentLocal;
                    _skillBindOrderKey = currentOrder;
                }
            }
            catch
            {
                _skillBindSource = null;
                _skillBindSourceStatus = "scan_failed";
            }
        }

        if (Settings.DiagnoseLocalSlotCandidates.Value &&
            now - _lastSkillBindScanUtc >= TimeSpan.FromSeconds(2))
        {
            _lastSkillBindScanUtc = now;
            _skillBindSource = null;
            _skillBindSourceStatus = "ordered_players_unverified";
            try
            {
                var data = GameController.IngameState.Data;
                var localAddress = player?.Address ?? 0;
                if (_slotCandidates is { Status: "single_slot_candidate" or "two_slot_candidate" } candidate)
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
                        catch { return null; }
                    }
                    if (LocalSlotCandidateScanner.Revalidate(candidate, localAddress, paths,
                            ReadCandidate, address => RawCharacterPathReader.Read(address, ReadCandidate),
                            GameController.IngameState.Address))
                    {
                        var players = new List<long>();
                        foreach (var address in candidate.OrderedAddresses)
                        {
                            if (!address.StartsWith("0x", StringComparison.Ordinal) ||
                                !long.TryParse(address.AsSpan(2),
                                    System.Globalization.NumberStyles.HexNumber,
                                    System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                            {
                                players.Clear();
                                break;
                            }
                            players.Add(parsed);
                        }
                        if (players.Count == candidate.OrderedAddresses.Length)
                        {
                            _skillBindSource = SkillBindSourceScanner.Scan(
                                GameController.IngameState.Address, data.Address, localAddress,
                                players, ReadCandidate);
                            _skillBindSourceStatus = _skillBindSource.Status;
                        }
                    }
                }
            }
            catch
            {
                _skillBindSource = null;
                _skillBindSourceStatus = "scan_failed";
            }
        }

        object[]? radarEntities = null;
        string? radarStatus = null;
        DateTime? radarSeenUtc = null;
        string? radarStateAreaAddress = null;
        float? radarGridX = null;
        float? radarGridY = null;
        object? uiObservation = null;
        string? uiObservationStatus = null;
        DateTime? uiObservationSeenUtc = null;
        object? groundHover = null;
        string? groundHoverStatus = null;
        if (!Settings.DiagnoseDormantAreaMap.Value)
        {
            _dormantMapObservation = null;
            _lastDormantMapScanUtc = default;
            _dormantSurvey.Reset();
        }
        else if (_dormantMapStateAddress != frameStateAddress ||
                 _dormantMapDataAddress != frameDataAddress ||
                 _dormantMapLocalAddress != frameLocalAddress ||
                 _dormantMapAreaHash != frameAreaHash)
        {
            _dormantMapObservation = null;
            _lastDormantMapScanUtc = default;
            _dormantSurvey.Reset();
        }
        if (Settings.DiagnoseDormantAreaMap.Value &&
            now - _lastDormantMapScanUtc >= TimeSpan.FromSeconds(2))
        {
            _lastDormantMapScanUtc = now;
            _dormantMapStateAddress = frameStateAddress;
            _dormantMapDataAddress = frameDataAddress;
            _dormantMapLocalAddress = frameLocalAddress;
            _dormantMapAreaHash = frameAreaHash;
            try
            {
                byte[]? ReadMap(long address, int size)
                {
                    try { return GameController.IngameState.Data.M.ReadMem(address, size); }
                    catch { return null; }
                }
                var map = AreaEntityMapLocator.Locate(frameStateAddress,
                    player!.Id, frameLocalAddress, ReadMap);
                if (!SameAreaAndPlayer())
                {
                    _dormantSurvey.Reset();
                    _dormantMapObservation = new { Status = "game_identity_changed" };
                }
                else
                {
                    var samples = map.Sleeping.Take(16).Select(entry =>
                    {
                        string? path = null;
                        RawRenderPositionReader.Position? position = null;
                        try
                        {
                            path = RawMetadataPathReader.Read(entry.Address, ReadMap);
                            position = RawRenderPositionReader.Read(entry.Address, ReadMap);
                        }
                        catch { /* Keep this diagnostic sample without asserting its contents. */ }
                        return new
                        {
                            EntityId = entry.Id,
                            Address = FormatAddress(entry.Address),
                            Path = path,
                            Position = position
                        };
                    }).ToArray();
                    var navigation = map.Status == "candidate_unverified_build"
                        ? _dormantSurvey.Capture(
                            $"{frameProcessId}:{frameStateAddress:X}:{frameDataAddress:X}:" +
                            $"{frameLocalAddress:X}:{frameAreaHash}:{map.AreaAddress}:{map.EntityListOffset}",
                            map.Sleeping,
                            address => RawMetadataPathReader.Read(address, ReadMap),
                            address => RawRenderPositionReader.Read(address, ReadMap),
                            render.Pos.X, render.Pos.Y)
                        : null;
                    if (navigation == null) _dormantSurvey.Reset();
                    _dormantMapObservation = new
                    {
                        SeenUtc = now,
                        map.Status,
                        map.AreaAddress,
                        map.EntityListOffset,
                        map.AwakeCount,
                        SleepingCount = map.Sleeping.Length,
                        Samples = samples,
                        LocalPosition = RawRenderPositionReader.Read(frameLocalAddress, ReadMap),
                        NavigationCandidates = navigation?.NavigationCandidates ?? [],
                        NavigationBatchScanned = navigation?.BatchScanned ?? 0,
                        NavigationTotalEntities = navigation?.TotalEntities ?? 0,
                        NavigationSweepCompleted = navigation?.SweepCompleted ?? false,
                        NavigationRememberedCount = navigation?.RememberedCount ?? 0,
                        NavigationOutputTruncated = navigation?.OutputTruncated ?? false
                    };
                    if (!SameAreaAndPlayer())
                    {
                        _dormantSurvey.Reset();
                        _dormantMapObservation = new { Status = "game_identity_changed" };
                    }
                }
            }
            catch
            {
                _dormantSurvey.Reset();
                _dormantMapObservation = new { Status = "scan_failed" };
            }
        }
        if (!Settings.DiagnoseRadarEntities.Value && _monsterTrackerAreaHash.HasValue)
        {
            _monsterTracker.Reset();
            _monsterTrackerAreaHash = null;
        }
        if (Settings.DiagnoseRadarEntities.Value && now - _lastRadarSentUtc >= TimeSpan.FromMilliseconds(500))
        {
            _lastRadarSentUtc = now;
            radarSeenUtc = now;
            try
            {
                var areaHash = GameController.IngameState.Data.CurrentAreaHash;
                if (_monsterTrackerAreaHash != areaHash)
                {
                    _monsterTracker.Reset();
                    _monsterTrackerAreaHash = areaHash;
                }
                radarEntities = ReadRadarEntities(player!.Id, render.Pos.X, render.Pos.Y);
                if (Settings.DiagnoseTerrain.Value || Settings.DiagnoseDormantAreaMap.Value)
                {
                    try
                    {
                        byte[]? ReadRadarTerrain(long address, int size)
                        {
                            try { return GameController.IngameState.Data.M.ReadMem(address, size); }
                            catch { return null; }
                        }
                        var raw = Settings.DiagnoseTerrain.Value
                            ? RawRenderPositionReader.Read(frameLocalAddress, ReadRadarTerrain) : null;
                        if (raw is not null)
                        {
                            radarGridX = raw.GridX;
                            radarGridY = raw.GridY;
                        }
                        var pointer = ReadRadarTerrain(frameStateAddress + 656, 8);
                        if (pointer is { Length: 8 })
                            radarStateAreaAddress = FormatAddress(BitConverter.ToInt64(pointer));
                    }
                    catch
                    {
                        radarGridX = null;
                        radarGridY = null;
                        radarStateAreaAddress = null;
                    }
                }
                radarStatus = "ok";
            }
            catch
            {
                _monsterTracker.Reset();
                _monsterTrackerAreaHash = null;
                radarStatus = "scan_failed";
            }
            uiObservationSeenUtc = now;
            try
            {
                uiObservation = ReadUiObservation();
                uiObservationStatus = "observed";
            }
            catch
            {
                uiObservationStatus = "scan_failed";
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
                                        address => RawCharacterPathReader.Read(address, ReadCandidate),
                                        GameController.IngameState.Address))
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

        TerrainDiagnostic.Summary? terrainSummary = null;
        string? terrainStatus = null;
        DateTime? terrainSeenUtc = null;
        PackedTerrainProbe.Observation? packedTerrainProbe = null;
        string? packedTerrainStatus = null;
        string? packedTerrainCaptureStatus = null;
        GeneratedRoomProbe.Result? generatedRoomProbe = null;
        TerrainPlacementProbe.Result? placementProbe = null;
        QuestTileProbe.Result? questTileProbe = null;
        if (Settings.DiagnoseTerrain.Value)
        {
            try
            {
                var epoch = $"{GameController.IngameState.Data.CurrentAreaHash}:" +
                    $"{GameController.IngameState.Address:X}:{GameController.IngameState.Data.Address:X}";
                if (_terrainCaptureEpoch != epoch)
                {
                    _capturedTerrainKeys.Clear();
                    _capturedTerrainFiles.Clear();
                    _generatedRoomProbeKey = null;
                    _generatedRoomProbeCached = null;
                    _placementProbeKey = null;
                    _placementProbeCached = null;
                    _questTileProbeKey = null;
                    _questTileProbeCached = null;
                    _terrainCaptureEpoch = epoch;
                }
            }
            catch
            {
                _capturedTerrainKeys.Clear();
                _capturedTerrainFiles.Clear();
                _generatedRoomProbeKey = null;
                _generatedRoomProbeCached = null;
                _placementProbeKey = null;
                _placementProbeCached = null;
                _questTileProbeKey = null;
                _questTileProbeCached = null;
                _terrainCaptureEpoch = null;
            }
        }
        if (Settings.DiagnoseTerrain.Value && now - _lastTerrainSentUtc >= TimeSpan.FromSeconds(5))
        {
            _lastTerrainSentUtc = now;
            terrainSeenUtc = now;
            try
            {
                terrainSummary = TerrainDiagnostic.Summarize(GameController.IngameState.Data.RawPathfindingData);
                terrainStatus = terrainSummary == null ? "unavailable" :
                    terrainSummary.SampledCells == 0 ? "empty" : "raw_values_observed";
            }
            catch
            {
                terrainStatus = "scan_failed";
            }
            try
            {
                byte[]? ReadTerrain(long address, int size)
                {
                    try { return GameController.IngameState.Data.M.ReadMem(address, size); }
                    catch { return null; }
                }
                var stateAddress = GameController.IngameState.Address;
                var areaPointer = ReadTerrain(stateAddress + 656, 8);
                var area = areaPointer is { Length: 8 } ? BitConverter.ToInt64(areaPointer) : 0;
                var positionCandidate = RawRenderPositionReader.Read(player!.Address, ReadTerrain);
                var result = PackedTerrainProbe.ScanAnchors([
                    new PackedTerrainProbe.AreaCandidate("state_pointer_static_656", area),
                    new PackedTerrainProbe.AreaCandidate("ingame_data_address",
                        GameController.IngameState.Data.Address)
                ],
                    positionCandidate?.GridX ?? float.NaN,
                    positionCandidate?.GridY ?? float.NaN, ReadTerrain);
                packedTerrainProbe = result.Candidate;
                packedTerrainStatus = result.Status;
                if (result.Candidate is { } observation && result.Source is { } source)
                {
                    var areaHash = $"{GameController.IngameState.Data.CurrentAreaHash}";
                    var processId = GameController.Window.Process.Id;
                    var captureKey = $"{processId}:{areaHash}:{observation.AreaAddress}:{source.Start}:{source.BytesPerRow}:{source.Rows}";
                    bool AreaStillCurrent()
                    {
                        if ($"{GameController.IngameState.Data.CurrentAreaHash}" != areaHash ||
                            GameController.IngameState.Address != stateAddress)
                            return false;
                        long currentAddress;
                        if (observation.Anchor == "state_pointer_static_656")
                        {
                            var pointer = ReadTerrain(stateAddress + 656, 8);
                            currentAddress = pointer is { Length: 8 } ? BitConverter.ToInt64(pointer) : 0;
                        }
                        else currentAddress = GameController.IngameState.Data.Address;
                        return currentAddress > 0 &&
                            $"0x{currentAddress:X}".Equals(observation.AreaAddress,
                                StringComparison.OrdinalIgnoreCase);
                    }
                    if (AreaStillCurrent() && long.TryParse(observation.AreaAddress.AsSpan(2),
                            System.Globalization.NumberStyles.HexNumber,
                            System.Globalization.CultureInfo.InvariantCulture, out var roomArea))
                    {
                        var roomResult = _generatedRoomProbeKey == captureKey &&
                            _generatedRoomProbeCached is { } cachedRooms
                            ? cachedRooms : GeneratedRoomProbe.Scan(roomArea, observation.Width,
                                observation.Height, ReadTerrain);
                        if (AreaStillCurrent())
                        {
                            generatedRoomProbe = roomResult;
                            if (roomResult is { Status: "candidate_rooms_observed", AnchorsTruncated: false })
                            {
                                _generatedRoomProbeKey = captureKey;
                                _generatedRoomProbeCached = roomResult;
                            }
                        }
                    }
                    if (AreaStillCurrent() && result.Layout is { } layout)
                    {
                        var placementResult = _placementProbeKey == captureKey &&
                            _placementProbeCached is { } cachedPlacements
                            ? cachedPlacements : TerrainPlacementProbe.Scan(layout,
                                observation.Width, observation.Height, ReadTerrain);
                        if (AreaStillCurrent())
                        {
                            placementProbe = placementResult;
                            if (placementResult is { Status: "candidate_placements_observed", Truncated: false })
                            {
                                _placementProbeKey = captureKey;
                                _placementProbeCached = placementResult;
                            }
                        }
                        var areaId = RawAreaIdReader.Read(stateAddress, ReadTerrain);
                        var tileKey = $"{captureKey}:{areaId}";
                        var tileResult = _questTileProbeKey == tileKey &&
                            _questTileProbeCached is { } cachedTiles
                            ? cachedTiles : QuestTileProbe.Scan(layout,
                                observation.Width, observation.Height, areaId, ReadTerrain);
                        if (AreaStillCurrent())
                        {
                            questTileProbe = tileResult;
                            if (tileResult is { Status: "candidate_tiles_observed" or "no_matches",
                                    Truncated: false })
                            {
                                _questTileProbeKey = tileKey;
                                _questTileProbeCached = tileResult;
                            }
                        }
                    }
                    if (_capturedTerrainKeys.Contains(captureKey))
                    {
                        packedTerrainCaptureStatus = _capturedTerrainFiles.TryGetValue(
                            captureKey, out var existingFile)
                            ? PackedTerrainCapture.UpdateAnchors(existingFile, processId,
                                areaHash, observation.AreaAddress, observation.Anchor,
                                generatedRoomProbe, placementProbe, questTileProbe, AreaStillCurrent)
                            : "already_captured";
                    }
                    else
                    {
                        var directory = Path.Combine(Environment.GetFolderPath(
                            Environment.SpecialFolder.LocalApplicationData), "PartyPilot", "terrain");
                        var capture = PackedTerrainCapture.Save(source, observation,
                            positionCandidate!, render.Pos.X, render.Pos.Y, ReadTerrain,
                            AreaStillCurrent, directory,
                            processId, areaHash, observation.AreaAddress, observation.Anchor, now,
                            generatedRoomProbe, placementProbe, questTileProbe);
                        packedTerrainCaptureStatus = capture.Status;
                        if (capture.Status == "captured_local" && capture.DataFile != null)
                        {
                            _capturedTerrainKeys.Add(captureKey);
                            _capturedTerrainFiles[captureKey] = capture.DataFile;
                        }
                    }
                }
            }
            catch
            {
                packedTerrainStatus = "scan_failed";
            }
        }

        if (!SameAreaAndPlayer())
        {
            _slotCandidates = null;
            _slotCandidateStatus = "scan_failed";
            return;
        }
        object? areaSafety = null;
        string? observedAreaName = null;
        var areaSafetyStatus = "unavailable";
        try
        {
            var area = GameController.Area.CurrentArea;
            if (area != null)
            {
                if (area.Hash == frameAreaHash)
                {
                    observedAreaName = area.Name;
                    areaSafety = new
                    {
                        Hash = area.Hash,
                        Town = area.IsTown,
                        Hideout = area.IsHideout,
                        Peaceful = area.IsPeaceful
                    };
                    areaSafetyStatus = "observed";
                }
                else areaSafetyStatus = "area_mismatch";
            }
        }
        catch { areaSafetyStatus = "unavailable"; }
        if (!SameAreaAndPlayer()) return;
        var position = render.Pos;
        var sourceTickMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // AreaName is diagnostic only. It does not identify a specific instance.
        var message = JsonSerializer.Serialize(new
        {
            ProcessId = GameController.Window.Process.Id,
            SeenUtc = now,
            TickMs = sourceTickMs,
            AreaName = observedAreaName,
            AreaHash = frameAreaHash,
            RadarAreaAddress = radarSeenUtc.HasValue ? FormatAddress(frameDataAddress) : null,
            RadarStateAreaAddress = radarStateAreaAddress,
            RadarGridX = radarGridX,
            RadarGridY = radarGridY,
            AreaSafety = areaSafety,
            AreaSafetyStatus = areaSafetyStatus,
            AreaSafetySeenUtc = now,
            CoordinateSpace = "world",
            X = position.X,
            Y = position.Y,
            Z = position.Z,
            Life = vitals?.CurHP,
            LifeMax = vitals?.MaxHP,
            LifeReservedFlat = vitals?.ReservedFlatHP,
            LifeReservedPercent = vitals?.ReservedPercentHP,
            Mana = vitals?.CurMana,
            ManaMax = vitals?.MaxMana,
            ManaReservedFlat = vitals?.ReservedFlatMana,
            ManaReservedPercent = vitals?.ReservedPercentMana,
            EnergyShield = vitals?.CurES,
            EnergyShieldMax = vitals?.MaxES,
            IngameDataAddress = ingameDataAddress,
            ObservedLocalAddress = observedLocalAddress,
            LocalSlotCandidates = Settings.DiagnoseLocalSlotCandidates.Value ? _slotCandidates : null,
            LocalSlotCandidateStatus = Settings.DiagnoseLocalSlotCandidates.Value ? _slotCandidateStatus : null,
            SkillBindSource = Settings.DiagnoseLocalSlotCandidates.Value ? _skillBindSource : null,
            SkillBindSourceStatus = Settings.DiagnoseLocalSlotCandidates.Value ? _skillBindSourceStatus : null,
            SkillBindSourceSeenUtc = Settings.DiagnoseLocalSlotCandidates.Value &&
                _skillBindSource != null ? _lastSkillBindScanUtc : (DateTime?)null,
            RadarEntities = radarEntities,
            DormantAreaMap = Settings.DiagnoseDormantAreaMap.Value ? _dormantMapObservation : null,
            RadarStatus = radarStatus,
            RadarSeenUtc = radarSeenUtc,
            UiObservation = uiObservation,
            UiObservationStatus = uiObservationStatus,
            UiObservationSeenUtc = uiObservationSeenUtc,
            GroundHover = groundHover,
            GroundHoverStatus = groundHoverStatus,
            ItemCards = itemCards,
            ItemCardStatus = itemCardStatus,
            ItemCardSeenUtc = itemCardSeenUtc,
            TerrainSummary = terrainSummary,
            TerrainStatus = terrainStatus,
            TerrainSeenUtc = terrainSeenUtc,
            PackedTerrainProbe = packedTerrainProbe,
            PackedTerrainStatus = packedTerrainStatus,
            PackedTerrainCaptureStatus = packedTerrainCaptureStatus,
            GeneratedRoomProbe = generatedRoomProbe is null ? null : new
            {
                generatedRoomProbe.Status,
                generatedRoomProbe.Graphs,
                generatedRoomProbe.RoomsChecked,
                generatedRoomProbe.ValidPaths,
                generatedRoomProbe.SamplesTruncated,
                SamplesOmitted = Math.Max(0, generatedRoomProbe.Samples.Length - 4),
                Samples = generatedRoomProbe.Samples.Take(4),
                generatedRoomProbe.MatchedAnchors,
                generatedRoomProbe.AnchorsTruncated,
                AnchorsOmitted = Math.Max(0, generatedRoomProbe.Anchors.Length - 8),
                Anchors = generatedRoomProbe.Anchors.Take(8)
            },
            TerrainPlacementProbe = placementProbe is null ? null : new
            {
                placementProbe.Status,
                placementProbe.TilesChecked,
                placementProbe.ReferencesChecked,
                placementProbe.ValidPaths,
                placementProbe.MatchedAnchors,
                placementProbe.Truncated
            },
            QuestTileProbe = questTileProbe is null ? null : new
            {
                questTileProbe.Status,
                questTileProbe.AreaId,
                questTileProbe.TilesChecked,
                questTileProbe.NamedTiles,
                questTileProbe.MatchedTiles,
                questTileProbe.Truncated
            },
            VisiblePlayers = visiblePlayers,
            VisiblePlayersStatus = visiblePlayersStatus
        });
        var bytes = Encoding.UTF8.GetBytes(message);
        _udp.Send(bytes, bytes.Length, Receiver);
        if (frameDataAddress > 0 && frameLocalAddress > 0 && frameAreaHash != 0 &&
            vitals is not null && vitals.MaxHP > 0 && vitals.CurHP > 0 &&
            float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z))
        {
            var pairing = JsonSerializer.SerializeToUtf8Bytes(new
            {
                Kind = "pairing_ingame",
                ProcessId = frameProcessId,
                SeenUtc = now,
                AreaHash = frameAreaHash,
                IngameDataAddress = frameDataAddress,
                PlayerAddress = frameLocalAddress,
                X = position.X,
                Y = position.Y,
                Z = position.Z,
                Life = vitals.CurHP,
                LifeMax = vitals.MaxHP
            });
            _udp.Send(pairing, pairing.Length, PairingReceiver);
        }
        if (radarSeenUtc.HasValue) _udp.Send(bytes, bytes.Length, RadarReceiver);
        var privacyPulse = JsonSerializer.SerializeToUtf8Bytes(new
        {
            ProcessId = GameController.Window.Process.Id,
            SeenUtc = now,
            PrivacyStatus = "render-ok"
        });
        _udp.Send(privacyPulse, privacyPulse.Length, PrivacyReceiver);
    }

    private void EmitFastOrderedCandidate(DateTime seenUtc)
    {
        var processId = 0;
        uint areaHash = 0;
        try
        {
            var player = GameController.Player;
            var render = player?.GetComponent<Render>();
            if (player is null || render == null) return;
            var data = GameController.IngameState.Data;
            var stateAddress = GameController.IngameState.Address;
            var areaAddress = data.Address;
            var localAddress = player.Address;
            areaHash = data.CurrentAreaHash;
            processId = GameController.Window.Process.Id;
            if (localAddress <= 0 || processId <= 0) return;
            bool SameIdentity()
            {
                try
                {
                    return GameController.IngameState.Address == stateAddress &&
                        GameController.IngameState.Data.Address == areaAddress &&
                        GameController.IngameState.Data.CurrentAreaHash == areaHash &&
                        (GameController.Player?.Address ?? 0) == localAddress &&
                        GameController.Window.Process.Id == processId;
                }
                catch { return false; }
            }
            byte[]? ReadCandidate(long address, int size)
            {
                try { return data.M.ReadMem(address, size); }
                catch { return null; }
            }
            LocalSlotCandidateScanner.Result? captured = null;
            if (_slotCandidates is { Status: "single_slot_candidate" or "two_slot_candidate" } candidate &&
                _lastSlotScanStateAddress == stateAddress &&
                _lastSlotScanDataAddress == areaAddress &&
                _lastSlotScanLocalAddress == localAddress &&
                seenUtc - _lastSlotScanUtc <= TimeSpan.FromSeconds(1))
            {
                var localPath = player.Path;
                var paths = new Dictionary<long, string>();
                if (!string.IsNullOrWhiteSpace(localPath)) paths[localAddress] = localPath;
                captured = LocalSlotMotionSampler.Capture(candidate, localAddress, paths,
                    new Dictionary<long, (float X, float Y, float Z)>(),
                    ReadCandidate, address => RawCharacterPathReader.Read(address, ReadCandidate),
                    SameIdentity, render.Pos.X, render.Pos.Y, render.Pos.Z, seenUtc,
                    ingameStateAddress: stateAddress);
                // A failed revalidation must revoke a previously accepted ordered frame.
            }
            if (!SameIdentity())
            {
                EmitFastOrderedInvalidation(processId, areaHash, seenUtc);
                return;
            }
            var bytes = FastOrderedCandidateFrame.Encode(processId, areaHash,
                GameController.Area.CurrentArea?.Name, captured, seenUtc,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if (Settings.DiagnoseFastOrderedMotion.Value)
                _udp.Send(bytes, bytes.Length, FastCandidateReceiver);
            if (Settings.ExperimentalOrderedFollow.Value)
            {
                var control = FastOrderedCandidateFrame.TryEncodeExperimentalControl(
                    processId, areaHash, GameController.Area.CurrentArea?.Name, captured,
                    seenUtc, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) ?? bytes;
                _udp.Send(control, control.Length, Receiver);
            }
        }
        catch
        {
            // Once a PID is known, revoke its prior ordered positions instead
            // of leaving the last movement active until sample expiry.
            EmitFastOrderedInvalidation(processId, areaHash, seenUtc);
        }
    }

    private void EmitFastOrderedInvalidation(int processId, uint areaHash, DateTime seenUtc)
    {
        if (processId <= 0 || seenUtc == default || !Settings.ExperimentalOrderedFollow.Value) return;
        try
        {
            var bytes = FastOrderedCandidateFrame.Encode(processId, areaHash, null,
                null, seenUtc, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            _udp.Send(bytes, bytes.Length, Receiver);
        }
        catch
        {
            // Failure reporting must not interrupt ExileCore's render callback.
        }
    }

    private object[] ReadVisiblePlayers(uint? observedLocalId)
    {
        var players = new List<object>();
        var seen = new HashSet<uint>();
        foreach (var entity in GameController.Entities)
        {
            var component = entity?.GetComponent<Player>();
            if (component == null || entity == null ||
                !RadarClassifier.IsPlayerPath(entity.Path ?? "")) continue;
            if (Encoding.UTF8.GetByteCount(component.PlayerName ?? "") > 64 ||
                Encoding.UTF8.GetByteCount(entity!.Path ?? "") > 256) continue;
            var position = entity!.Pos;
            if (entity.Id == 0 || !seen.Add(entity.Id) || !float.IsFinite(position.X) ||
                !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
                continue;
            var life = entity.GetComponent<Life>();
            players.Add(new
            {
                EntityId = entity.Id,
                Address = FormatAddress(entity.Address),
                Path = entity.Path,
                Name = component.PlayerName?.Trim() ?? "",
                X = position.X,
                Y = position.Y,
                Z = position.Z,
                IsObservedLocal = observedLocalId.HasValue && entity.Id == observedLocalId.Value,
                Life = life?.CurHP,
                LifeMax = life?.MaxHP,
                Mana = life?.CurMana,
                ManaMax = life?.MaxMana,
                EnergyShield = life?.CurES,
                EnergyShieldMax = life?.MaxES
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
        _monsterTracker.BeginFrame();
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
            bool? monsterComponentPresent = null;
            int? monsterLifeCurrent = null;
            int? monsterLifeMaximum = null;
            string? monsterRarity = null;
            bool? entityIsAlive = null;
            bool? entityIsHostile = null;
            bool? trackedAliveCandidate = null;
            if (category == "monster" || category == "quest" &&
                path.StartsWith("Metadata/Monsters/", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (entity.GetComponent<Monster>() != null)
                    {
                        monsterComponentPresent = true;
                        monsterRarity = RadarMonsterMetadata.Rarity((int)entity.Rarity);
                        entityIsAlive = entity.IsAlive;
                        entityIsHostile = entity.IsHostile;
                        var life = entity.GetComponent<Life>();
                        var values = RadarMonsterVitals.Read(life?.CurHP, life?.MaxHP);
                        if (values.HasValue)
                        {
                            monsterLifeCurrent = values.Value.Current;
                            monsterLifeMaximum = values.Value.Maximum;
                        }
                        if (entity.Address > 0 && entityIsAlive.HasValue)
                            trackedAliveCandidate = _monsterTracker.Observe(entity.Address, entity.Id, path,
                                entityIsAlive.Value, entity.IsValid,
                                monsterLifeCurrent ?? 0, monsterLifeMaximum ?? 0);
                    }
                }
                catch
                {
                    // Keep the path-only marker when a component cannot be read.
                }
            }
            var pos = entity.Pos;
            if (!float.IsFinite(pos.X) || !float.IsFinite(pos.Y) || !float.IsFinite(pos.Z)) continue;
            var dx = (double)pos.X - localX;
            var dy = (double)pos.Y - localY;
            candidates.Add((new
            {
                EntityId = entity.Id,
                Category = category,
                Path = path,
                Label = lootName ?? (isPlayer ? playerName : category == "quest" ? RadarClassifier.QuestLabel(path) :
                    category == "boss" ? RadarClassifier.BossLabel(path) : null),
                ItemPath = itemPath,
                Stack = lootStack,
                Rarity = lootRarity,
                MonsterComponentPresent = monsterComponentPresent,
                MonsterLifeCurrent = monsterLifeCurrent,
                MonsterLifeMaximum = monsterLifeMaximum,
                MonsterRarity = monsterRarity,
                EntityIsAlive = entityIsAlive,
                EntityIsHostile = entityIsHostile,
                TrackedAliveCandidate = trackedAliveCandidate,
                X = pos.X,
                Y = pos.Y,
                Z = pos.Z
            }, RadarClassifier.IsNavigation(category), dx * dx + dy * dy));
        }
        _monsterTracker.EndFrame();
        return RadarPacketBudget.Select(candidates.OrderByDescending(item => item.Navigation)
            .ThenBy(item => item.Distance).Select(item => item.Value));
    }

    private object ReadUiObservation()
    {
        var ui = GameController.IngameState.IngameUi;
        return new
        {
            LoadingObserved = TryReadUiBool(() => GameController.IsLoading),
            InventoryPanelVisible = TryReadUiBool(() => ui.InventoryPanel?.IsVisible),
            StashElementVisible = TryReadUiBool(() => ui.StashElement?.IsVisible),
            ChatBoxVisible = TryReadUiBool(() => ui.ChatBox?.IsVisible),
            ControllerGeneralMenuVisible = TryReadUiBool(() => ui.ControllerGeneralMenu?.IsVisible),
            SettingsPanelVisible = TryReadUiBool(() => ui.SettingsPanel?.IsVisible),
            OpenLeftPanelVisible = TryReadUiBool(() => ui.OpenLeftPanel?.IsVisible),
            OpenRightPanelVisible = TryReadUiBool(() => ui.OpenRightPanel?.IsVisible)
        };
    }

    private static bool? TryReadUiBool(Func<bool?> read)
    {
        try { return read(); }
        catch { return null; }
    }
}
