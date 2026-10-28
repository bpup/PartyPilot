using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using PartyPilot.Probe;

if (args is ["--self-test"])
{
    SelfTest.Run();
    Console.WriteLine("scanner self-test passed");
    return;
}

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("独立探针只能在 Windows 上连接游戏；此电脑仅能运行扫描器自测。");
    Environment.ExitCode = 2;
    return;
}

var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "PathOfExile", "PathOfExile_x64", "PathOfExileSteam", "PathOfExile_x64Steam",
    "PathOfExileEGS", "PathOfExile_x64EGS"
};
var processes = Process.GetProcesses().Where(process => names.Contains(process.ProcessName))
    .OrderBy(process => process.Id).ToArray();
if (processes.Length == 0)
{
    Console.WriteLine(JsonSerializer.Serialize(new { status = "game_not_running" }));
    return;
}

foreach (var process in processes)
{
    try
    {
        using (process)
        {
            var module = process.MainModule ?? throw new InvalidOperationException("main module unavailable");
            var path = module.FileName;
            using var file = File.OpenRead(path);
            var hash = Convert.ToHexString(SHA256.HashData(file));
            var recognizedBuild = KnownGameLayout.ForSha256(hash) is not null;
            using var memory = Win32Memory.Open(process.Id);
            var candidates = AnchorCandidateScanner.Find(module.BaseAddress.ToInt64(),
                module.ModuleMemorySize, memory.TryRead);
            var selection = candidates.Count == 1
                ? LayoutCandidateResolver.Resolve(candidates[0].GameStateAddress, hash,
                    memory.TryRead)
                : new LayoutCandidateSelection(null, null, 0);
            var layout = selection.Layout;
            var state = selection.State;
            var validated = layout is not null && state is not null;
            var roles = validated
                ? RoleCandidateDiagnostic.Capture(candidates[0].GameStateAddress, layout!,
                    memory.TryRead) : null;
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                pid = process.Id,
                status = roles?.Status == "role_position_candidates_unverified"
                    ? (recognizedBuild ? "role_position_candidates_unverified" :
                        "role_position_candidates_unverified_unknown_build") :
                    validated ? (recognizedBuild ? "area_candidate_only" :
                        "area_candidate_unknown_build") :
                    candidates.Count == 0 ? "anchor_missing" :
                    candidates.Count > 1 ? "anchor_ambiguous" :
                    selection.ValidLayoutCount > 1 ? "layout_ambiguous" :
                    recognizedBuild ? "state_unverified" : "unknown_build_unverified",
                exeSha256 = hash,
                recognizedBuild,
                layout = layout?.Name,
                validLayoutCount = selection.ValidLayoutCount,
                candidateCount = candidates.Count,
                // Addresses are diagnostic only. No movement or radar frames are emitted.
                gameStateAddress = validated ? $"0x{candidates[0].GameStateAddress:X}" : null,
                areaInstanceAddress = validated ? $"0x{state!.AreaInstanceAddress:X}" : null,
                baseAreaLevel = validated ? state!.BaseAreaLevel : (int?)null,
                monsterLevel = validated ? state!.MonsterLevel : (int?)null,
                roleCandidateStatus = roles?.Status,
                roleCandidateHeaders = roles?.MatchingHeaders,
                roleCandidates = roles?.Positions
            }));
        }
    }
    catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or
                                  System.IO.IOException or UnauthorizedAccessException)
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            pid = process.Id, status = "attach_failed", error = error.Message
        }));
    }
}
