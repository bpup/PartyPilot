using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using PartyPilot.Sensor;

namespace PartyPilot.Probe;

public sealed class DiagnosticSession : IDisposable
{
    private sealed record MotionCandidate(long ValidatedAtTimestamp, long GameStateAddress,
        KnownGameLayout Layout, StateCandidate State, uint AreaHash,
        RoleCandidateResult Roles);
    private sealed record LandmarkContext(AnchorCandidate Anchor, long GameStateAddress,
        KnownGameLayout GameLayout, StateCandidate State, uint AreaHash,
        PackedTerrainProbe.Observation Observation,
        PackedTerrainProbe.PackedSource TerrainSource,
        PackedTerrainProbe.AreaLayout? TerrainLayout, float GridX, float GridY,
        string DataFile);

    private readonly Process _process;
    private readonly Win32Memory _memory;
    private readonly string _hash;
    private readonly bool _recognizedBuild;
    private readonly long _moduleBase;
    private readonly int _moduleSize;
    private IReadOnlyList<AnchorCandidate> _anchors;
    private DateTime _nextAnchorScanUtc;
    private long _sequence;
    private MotionCandidate? _motionCandidate;
    private long _entityMapArea;
    private uint? _entityMapHash;
    private string? _entityMapLocal;
    private int? _entityMapOffset;
    private DateTime _nextEntityMapScanUtc;
    private long _completeRadarArea;
    private uint? _completeRadarHash;
    private string? _completeRadarLocal;
    private long _terrainArea;
    private uint? _terrainHash;
    private DateTime _nextTerrainScanUtc;
    private string? _terrainDataFile;
    private LandmarkContext? _landmarkContext;
    private Task<bool>? _landmarkScanTask;
    private CancellationTokenSource? _landmarkScanCancellation;
    private DateTime _nextLandmarkScanUtc;
    private bool _landmarksComplete;

    private DiagnosticSession(Process process, Win32Memory memory, string hash,
        long moduleBase, int moduleSize, IReadOnlyList<AnchorCandidate> anchors)
    {
        _process = process;
        _memory = memory;
        _hash = hash;
        _recognizedBuild = KnownGameLayout.ForSha256(hash) is not null;
        _moduleBase = moduleBase;
        _moduleSize = moduleSize;
        _anchors = anchors;
        _nextAnchorScanUtc = DateTime.UtcNow.AddSeconds(5);
    }

    public int ProcessId => _process.Id;
    public bool HasExited => _process.HasExited;
    internal byte[]? LastOrderedFrame { get; private set; }
    internal byte[]? LastRadarFrame { get; private set; }
    internal byte[]? LastRadarMotionFrame { get; private set; }

    public static DiagnosticSession Open(Process process)
    {
        var module = process.MainModule ?? throw new InvalidOperationException("main module unavailable");
        using var file = File.OpenRead(module.FileName);
        var hash = Convert.ToHexString(SHA256.HashData(file));
        var memory = Win32Memory.Open(process.Id);
        try
        {
            var anchors = AnchorCandidateScanner.Find(module.BaseAddress.ToInt64(),
                module.ModuleMemorySize, memory.TryRead);
            return new DiagnosticSession(process, memory, hash,
                module.BaseAddress.ToInt64(), module.ModuleMemorySize, anchors);
        }
        catch
        {
            memory.Dispose();
            throw;
        }
    }

    public object Capture()
    {
        LastOrderedFrame = null;
        LastRadarFrame = null;
        LastRadarMotionFrame = null;
        _motionCandidate = null;
        var sequence = ++_sequence;
        var seenUtc = DateTime.UtcNow;
        if (_process.HasExited)
            return new { pid = _process.Id, sequence, seenUtc, status = "game_exited" };

        if (_anchors.Count != 1 && seenUtc >= _nextAnchorScanUtc)
        {
            _anchors = AnchorCandidateScanner.Find(_moduleBase, _moduleSize,
                _memory.TryRead);
            _nextAnchorScanUtc = seenUtc.AddSeconds(5);
        }

        long gameStateAddress = 0;
        var currentStateReadable = _anchors.Count == 1 &&
            AnchorCandidateScanner.TryReadCurrentGameState(_anchors[0], _memory.TryRead,
                out gameStateAddress);
        var selection = currentStateReadable
            ? LayoutCandidateResolver.Resolve(gameStateAddress, _hash, _memory.TryRead)
            : new LayoutCandidateSelection(null, null, 0);
        var layout = selection.Layout;
        var state = selection.State;
        var validated = layout is not null && state is not null;
        var roles = validated
            ? RoleCandidateDiagnostic.Capture(gameStateAddress, layout!,
                _memory.TryRead) : null;
        if (validated &&
            (!AnchorCandidateScanner.TryReadCurrentGameState(_anchors[0],
                _memory.TryRead, out var stateAfterAddress) ||
             stateAfterAddress != gameStateAddress ||
             !StateCandidateValidator.TryRead(gameStateAddress, layout!,
                 _memory.TryRead, out var stateAfter) ||
             stateAfter?.IngameStateAddress != state!.IngameStateAddress ||
             stateAfter.AreaInstanceAddress != state.AreaInstanceAddress))
        {
            validated = false;
            roles = null;
        }
        var areaHashCandidate = validated
            ? AreaHashCandidate.ReadStable(state!.AreaInstanceAddress, _memory.TryRead) : null;
        if (validated && areaHashCandidate is not null &&
            (!AnchorCandidateScanner.TryReadCurrentGameState(_anchors[0],
                _memory.TryRead, out var stateAfterHashAddress) ||
             stateAfterHashAddress != gameStateAddress ||
             !StateCandidateValidator.TryRead(gameStateAddress, layout!,
                 _memory.TryRead, out var stateAfterHash) ||
             stateAfterHash?.AreaInstanceAddress != state!.AreaInstanceAddress ||
             stateAfterHash.IngameStateAddress != state.IngameStateAddress))
        {
            validated = false;
            roles = null;
            areaHashCandidate = null;
        }
        var status = roles?.Status == "role_position_candidates_unverified"
            ? (_recognizedBuild ? "role_position_candidates_unverified" :
                "role_position_candidates_unverified_unknown_build") :
            validated ? (_recognizedBuild ? "area_candidate_only" :
                "area_candidate_unknown_build") :
            _anchors.Count == 0 ? "anchor_missing" :
            _anchors.Count > 1 ? "anchor_ambiguous" :
            selection.ValidLayoutCount > 1 ? "layout_ambiguous" :
            _recognizedBuild ? "state_unverified" : "unknown_build_unverified";
        // A unique layout plus a revalidated local-player vector is stronger
        // evidence than the executable hash alone. Keep the unknown-build
        // diagnostic visible while allowing this opt-in candidate transport.
        if (validated)
        {
            LastOrderedFrame = ProbeOrderedFrame.TryEncode(_process.Id, seenUtc,
                areaHashCandidate, state!.AreaInstanceAddress, roles, _recognizedBuild);
            if (LastOrderedFrame is not null && areaHashCandidate is { } hash &&
                roles?.ValidatedVector is not null)
            {
                _motionCandidate = new MotionCandidate(Stopwatch.GetTimestamp(), gameStateAddress,
                    layout!, state!, hash, roles);
                LastRadarMotionFrame = ProbeRadarPacket.TryEncodeMotion(_process.Id,
                    seenUtc, hash, state.AreaInstanceAddress, roles.Positions[0]);
            }
        }
        ProbeEntityMapScanner.Result? entityMap = null;
        if (!validated || areaHashCandidate is null or 0 ||
            roles is not { Status: "role_position_candidates_unverified", Positions.Length: >= 1 })
        {
            _entityMapArea = 0;
            _entityMapHash = null;
            _entityMapLocal = null;
            _entityMapOffset = null;
        }
        else if (_entityMapArea != state!.AreaInstanceAddress ||
                 _entityMapHash != areaHashCandidate ||
                 _entityMapLocal != roles.Positions[0].EntityAddress ||
                 seenUtc >= _nextEntityMapScanUtc)
        {
            if (_entityMapArea != state.AreaInstanceAddress ||
                _entityMapHash != areaHashCandidate ||
                _entityMapLocal != roles.Positions[0].EntityAddress)
                _entityMapOffset = null;
            _entityMapArea = state.AreaInstanceAddress;
            _entityMapHash = areaHashCandidate;
            _entityMapLocal = roles.Positions[0].EntityAddress;
            _nextEntityMapScanUtc = seenUtc.AddSeconds(1);
            var local = roles.Positions[0].EntityAddress;
            if (local.StartsWith("0x", StringComparison.Ordinal) &&
                long.TryParse(local.AsSpan(2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out var localAddress))
            {
                var candidate = ProbeEntityMapScanner.Scan(state.AreaInstanceAddress,
                    localAddress, ReadBytes, _entityMapOffset);
                if (StateCandidateValidator.TryRead(gameStateAddress, layout!,
                        _memory.TryRead, out var stateAfterMap) &&
                    stateAfterMap?.AreaInstanceAddress == state.AreaInstanceAddress &&
                    stateAfterMap.IngameStateAddress == state.IngameStateAddress &&
                    AreaHashCandidate.ReadStable(state.AreaInstanceAddress, _memory.TryRead) ==
                        areaHashCandidate)
                {
                    entityMap = candidate;
                    _entityMapOffset = candidate.Status == "candidate_unverified_build"
                        ? candidate.EntityListOffset : null;
                    if (candidate.Status == "candidate_unverified_build" &&
                        RoleCandidateDiagnostic.CaptureFast(gameStateAddress, layout!, state,
                            roles, _memory.TryRead) is { Positions.Length: >= 1 } freshRoles &&
                        AreaHashCandidate.ReadStable(state.AreaInstanceAddress,
                            _memory.TryRead) == areaHashCandidate)
                    {
                        LastRadarFrame = ProbeRadarPacket.TryEncode(_process.Id, DateTime.UtcNow,
                            areaHashCandidate.Value, state.AreaInstanceAddress,
                            freshRoles.Positions[0], candidate, ReadBytes);
                        if (LastRadarFrame is not null)
                        {
                            _completeRadarArea = state.AreaInstanceAddress;
                            _completeRadarHash = areaHashCandidate;
                            _completeRadarLocal = freshRoles.Positions[0].EntityAddress;
                        }
                    }
                }
                else _nextEntityMapScanUtc = seenUtc;
            }
        }
        string? terrainScanStatus = null;
        string? terrainCaptureStatus = null;
        if (!validated || areaHashCandidate is null or 0 ||
            roles is not { Status: "role_position_candidates_unverified", Positions.Length: >= 1 })
        {
            ResetLandmarkScan();
            _terrainArea = 0;
            _terrainHash = null;
            _terrainDataFile = null;
        }
        else
        {
            if (_terrainArea != state!.AreaInstanceAddress || _terrainHash != areaHashCandidate)
            {
                ResetLandmarkScan();
                _terrainArea = state.AreaInstanceAddress;
                _terrainHash = areaHashCandidate;
                _terrainDataFile = null;
                _nextTerrainScanUtc = DateTime.MinValue;
            }
            if (_terrainDataFile is { } previous &&
                (!File.Exists(previous) || !File.Exists(Path.ChangeExtension(previous, ".json"))))
            {
                ResetLandmarkScan();
                _terrainDataFile = null;
            }
            if (_terrainDataFile is null && seenUtc >= _nextTerrainScanUtc)
            {
                _nextTerrainScanUtc = seenUtc.AddSeconds(5);
                var local = roles.Positions[0].EntityAddress;
                if (local.StartsWith("0x", StringComparison.Ordinal) &&
                    long.TryParse(local.AsSpan(2), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out var localAddress) &&
                    RawRenderPositionReader.Read(localAddress, ReadBytes) is { } position)
                {
                    bool AreaStillCurrent() => !_process.HasExited && _anchors.Count == 1 &&
                        AnchorCandidateScanner.TryReadCurrentGameState(_anchors[0],
                            _memory.TryRead, out var currentState) && currentState == gameStateAddress &&
                        StateCandidateValidator.TryRead(gameStateAddress, layout!,
                            _memory.TryRead, out var current) &&
                        current?.AreaInstanceAddress == state.AreaInstanceAddress &&
                        current.IngameStateAddress == state.IngameStateAddress &&
                        AreaHashCandidate.ReadStable(state.AreaInstanceAddress, _memory.TryRead) ==
                            areaHashCandidate;
                    var terrain = PackedTerrainProbe.ScanAnchors([
                        new PackedTerrainProbe.AreaCandidate("state_pointer_static_656",
                            state.AreaInstanceAddress)
                    ], position.GridX, position.GridY, ReadBytes);
                    terrainScanStatus = terrain.Status;
                    if (terrain.Candidate is { } observation && terrain.Source is { } source &&
                        AreaStillCurrent())
                    {
                        var directory = Path.Combine(Environment.GetFolderPath(
                            Environment.SpecialFolder.LocalApplicationData), "PartyPilot", "terrain");
                        var capture = PackedTerrainCapture.Save(source, observation, position,
                            null, null, ReadBytes, AreaStillCurrent, directory, _process.Id,
                            areaHashCandidate.Value.ToString(CultureInfo.InvariantCulture),
                            observation.AreaAddress, observation.Anchor, DateTime.UtcNow);
                        terrainCaptureStatus = capture.Status;
                        if (capture.Status == "captured_local" && capture.DataFile is { } file)
                        {
                            _terrainDataFile = file;
                            _landmarkContext = new LandmarkContext(_anchors[0],
                                gameStateAddress, layout!, state, areaHashCandidate.Value,
                                observation, source, terrain.Layout, position.GridX,
                                position.GridY, file);
                            _nextEntityMapScanUtc = DateTime.MinValue;
                        }
                    }
                }
            }
            if (_landmarkScanTask is { IsCompleted: true } completed)
            {
                try { _landmarksComplete = completed.GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { _landmarksComplete = false; }
                _landmarkScanTask = null;
                _landmarkScanCancellation?.Dispose();
                _landmarkScanCancellation = null;
                _nextLandmarkScanUtc = seenUtc.AddSeconds(5);
            }
            if (!_landmarksComplete && _landmarkScanTask is null &&
                _landmarkContext is { } landmark &&
                landmark.DataFile == _terrainDataFile &&
                seenUtc >= _nextLandmarkScanUtc)
            {
                _landmarkScanCancellation = new CancellationTokenSource();
                _landmarkScanTask = ProbeLandmarkScan.Start(_process.Id,
                    landmark.Anchor, landmark.GameStateAddress, landmark.GameLayout,
                    landmark.State, landmark.AreaHash, landmark.Observation,
                    landmark.TerrainSource, landmark.TerrainLayout, landmark.GridX,
                    landmark.GridY, landmark.DataFile,
                    _landmarkScanCancellation.Token);
            }
        }
        if (LastRadarFrame is null && _terrainDataFile is { } terrainFile &&
            File.Exists(terrainFile) && validated && areaHashCandidate is > 0 &&
            roles is { Status: "role_position_candidates_unverified", Positions.Length: >= 1 } &&
            (_completeRadarArea != state!.AreaInstanceAddress ||
             _completeRadarHash != areaHashCandidate ||
             _completeRadarLocal != roles.Positions[0].EntityAddress) &&
            _terrainArea == state!.AreaInstanceAddress && _terrainHash == areaHashCandidate &&
            _anchors.Count == 1 &&
            AnchorCandidateScanner.TryReadCurrentGameState(_anchors[0],
                _memory.TryRead, out var currentGameState) &&
            currentGameState == gameStateAddress &&
            RoleCandidateDiagnostic.CaptureFast(gameStateAddress, layout!, state,
                roles, _memory.TryRead) is
                { Status: "role_position_candidates_unverified", Positions.Length: >= 1 } freshTerrainRoles &&
            AreaHashCandidate.ReadStable(state.AreaInstanceAddress, _memory.TryRead) ==
                areaHashCandidate)
        {
            LastRadarFrame = ProbeRadarPacket.TryEncodeTerrainOnly(_process.Id,
                DateTime.UtcNow, areaHashCandidate.Value, state.AreaInstanceAddress,
                freshTerrainRoles.Positions[0]);
        }
        return new
        {
            pid = _process.Id,
            sequence,
            seenUtc,
            status,
            exeSha256 = _hash,
            recognizedBuild = _recognizedBuild,
            layout = layout?.Name,
            validLayoutCount = selection.ValidLayoutCount,
            candidateCount = _anchors.Count,
            // Addresses and positions are diagnostics only. No control frame is emitted.
            gameStateAddress = validated ? $"0x{gameStateAddress:X}" : null,
            areaInstanceAddress = validated ? $"0x{state!.AreaInstanceAddress:X}" : null,
            areaHashCandidate,
            baseAreaLevel = validated ? state!.BaseAreaLevel : (int?)null,
            monsterLevel = validated ? state!.MonsterLevel : (int?)null,
            roleCandidateStatus = roles?.Status,
            roleCandidateHeaders = roles?.MatchingHeaders,
            entityMapStatus = entityMap?.Status,
            sleepingEntityMapStatus = entityMap?.SleepingStatus,
            entityMapOffset = entityMap?.EntityListOffset,
            awakeEntityCount = entityMap?.Awake.Length,
            sleepingEntityCount = entityMap?.Sleeping.Length,
            radarCandidatePacket = LastRadarFrame is not null,
            terrainScanStatus,
            terrainCaptureStatus,
            vectorHeaderAddress = roles?.VectorHeaderAddress,
            orderedAddresses = roles?.OrderedAddresses,
            roleCandidates = roles?.Positions
        };
    }

    internal byte[]? CaptureMotion()
    {
        var baseline = _motionCandidate;
        if (baseline is null || _process.HasExited ||
            _anchors.Count != 1 || Stopwatch.GetElapsedTime(baseline.ValidatedAtTimestamp) >
                TimeSpan.FromMilliseconds(250) ||
            !AnchorCandidateScanner.TryReadCurrentGameState(_anchors[0], _memory.TryRead,
                out var gameStateAddress) || gameStateAddress != baseline.GameStateAddress)
            return RejectMotion();
        var roles = RoleCandidateDiagnostic.CaptureFast(gameStateAddress, baseline.Layout,
            baseline.State, baseline.Roles, _memory.TryRead);
        if (roles is null ||
            AreaHashCandidate.ReadStable(baseline.State.AreaInstanceAddress, _memory.TryRead)
                != baseline.AreaHash ||
            !AnchorCandidateScanner.TryReadCurrentGameState(_anchors[0], _memory.TryRead,
                out var stateAfterAddress) || stateAfterAddress != baseline.GameStateAddress ||
            Stopwatch.GetElapsedTime(baseline.ValidatedAtTimestamp) >
                TimeSpan.FromMilliseconds(250))
            return RejectMotion();
        var seenUtc = DateTime.UtcNow;
        var ordered = ProbeOrderedFrame.TryEncode(_process.Id, seenUtc,
            baseline.AreaHash, baseline.State.AreaInstanceAddress, roles, _recognizedBuild);
        if (ordered is null) return RejectMotion();
        LastRadarMotionFrame = ProbeRadarPacket.TryEncodeMotion(_process.Id, seenUtc,
            baseline.AreaHash, baseline.State.AreaInstanceAddress, roles.Positions[0]);
        return ordered;
    }

    private byte[]? RejectMotion()
    {
        _motionCandidate = null;
        LastRadarMotionFrame = null;
        return null;
    }

    public void Dispose()
    {
        ResetLandmarkScan();
        _memory.Dispose();
        _process.Dispose();
    }

    private void ResetLandmarkScan()
    {
        _landmarkScanCancellation?.Cancel();
        _landmarkScanCancellation?.Dispose();
        _landmarkScanCancellation = null;
        _landmarkScanTask = null;
        _landmarkContext = null;
        _landmarksComplete = false;
        _nextLandmarkScanUtc = DateTime.MinValue;
    }

    private byte[]? ReadBytes(long address, int length)
    {
        if (!Win32Memory.Plausible(address) || length is < 1 or > 64 * 1024) return null;
        var bytes = new byte[length];
        return _memory.TryRead(address, bytes) ? bytes : null;
    }
}
