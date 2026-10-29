using System.Diagnostics;
using System.Security.Cryptography;

namespace PartyPilot.Probe;

public sealed class DiagnosticSession : IDisposable
{
    private sealed record MotionCandidate(long ValidatedAtTimestamp, long GameStateAddress,
        KnownGameLayout Layout, StateCandidate State, uint AreaHash,
        RoleCandidateResult Roles);

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
        if (validated && _recognizedBuild)
        {
            LastOrderedFrame = ProbeOrderedFrame.TryEncode(_process.Id, seenUtc,
                areaHashCandidate, state!.AreaInstanceAddress, roles);
            if (LastOrderedFrame is not null && areaHashCandidate is { } hash &&
                roles?.ValidatedVector is not null)
                _motionCandidate = new MotionCandidate(Stopwatch.GetTimestamp(), gameStateAddress,
                    layout!, state!, hash, roles);
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
            vectorHeaderAddress = roles?.VectorHeaderAddress,
            orderedAddresses = roles?.OrderedAddresses,
            roleCandidates = roles?.Positions
        };
    }

    internal byte[]? CaptureMotion()
    {
        var baseline = _motionCandidate;
        if (baseline is null || !_recognizedBuild || _process.HasExited ||
            _anchors.Count != 1 || Stopwatch.GetElapsedTime(baseline.ValidatedAtTimestamp) >
                TimeSpan.FromMilliseconds(250) ||
            !AnchorCandidateScanner.TryReadCurrentGameState(_anchors[0], _memory.TryRead,
                out var gameStateAddress) || gameStateAddress != baseline.GameStateAddress)
            return RejectMotion();
        var roles = RoleCandidateDiagnostic.CaptureFast(gameStateAddress, baseline.Layout,
            baseline.State, baseline.Roles, _memory.TryRead);
        if (roles is null ||
            AreaHashCandidate.ReadStable(baseline.State.AreaInstanceAddress, _memory.TryRead)
                != baseline.AreaHash)
            return RejectMotion();
        return ProbeOrderedFrame.TryEncode(_process.Id, DateTime.UtcNow,
            baseline.AreaHash, baseline.State.AreaInstanceAddress, roles) ?? RejectMotion();
    }

    private byte[]? RejectMotion()
    {
        _motionCandidate = null;
        return null;
    }

    public void Dispose()
    {
        _memory.Dispose();
        _process.Dispose();
    }
}
