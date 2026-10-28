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
            var layout = KnownGameLayout.ForSha256(hash);
            if (layout is null)
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    pid = process.Id, status = "unknown_game_build", exeSha256 = hash
                }));
                continue;
            }
            using var memory = Win32Memory.Open(process.Id);
            var candidates = AnchorCandidateScanner.Find(module.BaseAddress.ToInt64(),
                module.ModuleMemorySize, memory.TryRead);
            StateCandidate? state = null;
            var validated = candidates.Count == 1 &&
                StateCandidateValidator.TryRead(candidates[0].GameStateAddress, layout,
                    memory.TryRead, out state);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                pid = process.Id,
                status = validated ? "area_candidate_only" :
                    candidates.Count == 0 ? "anchor_missing" :
                    candidates.Count > 1 ? "anchor_ambiguous" : "state_unverified",
                exeSha256 = hash,
                layout = layout.Name,
                candidateCount = candidates.Count,
                // Addresses are diagnostic only. No movement or radar frames are emitted.
                gameStateAddress = validated ? $"0x{candidates[0].GameStateAddress:X}" : null,
                areaInstanceAddress = validated ? $"0x{state!.AreaInstanceAddress:X}" : null,
                baseAreaLevel = validated ? state!.BaseAreaLevel : (int?)null,
                monsterLevel = validated ? state!.MonsterLevel : (int?)null
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
