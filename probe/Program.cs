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

var knownHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    // Executable SHA-256 values explicitly recognized by the supplied probe.
    "D6BAC03A460177DCB1E9C621B6D3D0F432C94E32BE1CB9BB58DC1F23EC0934C3",
    "BB8627EB41FEBE3F2BE0AA19ED28E5891D5EBD002EEE4F819DB569EA91203DC2",
    "457B5B09A4F1CBBEB2AAC0958138350BF96947B39A1E639F6ADCC195576636EF"
};

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
            if (!knownHashes.Contains(hash))
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
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                pid = process.Id,
                status = candidates.Count == 1 ? "anchor_candidate_only" :
                    candidates.Count == 0 ? "anchor_missing" : "anchor_ambiguous",
                exeSha256 = hash,
                candidateCount = candidates.Count,
                // Addresses are diagnostic only. No movement or radar frames are emitted.
                gameStateAddress = candidates.Count == 1 ? $"0x{candidates[0].GameStateAddress:X}" : null
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
