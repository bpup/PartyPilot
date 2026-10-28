using System.Diagnostics;
using System.Security.Cryptography;

namespace PartyPilot.Probe;

public sealed class DiagnosticSession : IDisposable
{
    private readonly Process _process;
    private readonly Win32Memory _memory;
    private readonly string _hash;
    private readonly bool _recognizedBuild;
    private readonly IReadOnlyList<AnchorCandidate> _anchors;
    private long _sequence;

    private DiagnosticSession(Process process, Win32Memory memory, string hash,
        IReadOnlyList<AnchorCandidate> anchors)
    {
        _process = process;
        _memory = memory;
        _hash = hash;
        _recognizedBuild = KnownGameLayout.ForSha256(hash) is not null;
        _anchors = anchors;
    }

    public int ProcessId => _process.Id;
    public bool HasExited => _process.HasExited;

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
            return new DiagnosticSession(process, memory, hash, anchors);
        }
        catch
        {
            memory.Dispose();
            throw;
        }
    }

    public object Capture()
    {
        var sequence = ++_sequence;
        var seenUtc = DateTime.UtcNow;
        if (_process.HasExited)
            return new { pid = _process.Id, sequence, seenUtc, status = "game_exited" };

        var selection = _anchors.Count == 1
            ? LayoutCandidateResolver.Resolve(_anchors[0].GameStateAddress, _hash,
                _memory.TryRead)
            : new LayoutCandidateSelection(null, null, 0);
        var layout = selection.Layout;
        var state = selection.State;
        var validated = layout is not null && state is not null;
        var roles = validated
            ? RoleCandidateDiagnostic.Capture(_anchors[0].GameStateAddress, layout!,
                _memory.TryRead) : null;
        var status = roles?.Status == "role_position_candidates_unverified"
            ? (_recognizedBuild ? "role_position_candidates_unverified" :
                "role_position_candidates_unverified_unknown_build") :
            validated ? (_recognizedBuild ? "area_candidate_only" :
                "area_candidate_unknown_build") :
            _anchors.Count == 0 ? "anchor_missing" :
            _anchors.Count > 1 ? "anchor_ambiguous" :
            selection.ValidLayoutCount > 1 ? "layout_ambiguous" :
            _recognizedBuild ? "state_unverified" : "unknown_build_unverified";
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
            gameStateAddress = validated ? $"0x{_anchors[0].GameStateAddress:X}" : null,
            areaInstanceAddress = validated ? $"0x{state!.AreaInstanceAddress:X}" : null,
            baseAreaLevel = validated ? state!.BaseAreaLevel : (int?)null,
            monsterLevel = validated ? state!.MonsterLevel : (int?)null,
            roleCandidateStatus = roles?.Status,
            roleCandidateHeaders = roles?.MatchingHeaders,
            roleCandidates = roles?.Positions
        };
    }

    public void Dispose()
    {
        _memory.Dispose();
        _process.Dispose();
    }
}
