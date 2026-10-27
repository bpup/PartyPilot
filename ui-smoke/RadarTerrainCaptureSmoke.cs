using System.Security.Cryptography;
using System.Text.Json;
using PartyPilot.Desktop;

internal static class RadarTerrainCaptureSmoke
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "partypilot-radar-terrain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var dataPath = Path.Combine(directory, "capture.packed");
            var metadataPath = Path.Combine(directory, "capture.json");
            var data = Enumerable.Repeat((byte)0x11, 4096).ToArray();
            data[0] = 0x10;
            File.WriteAllBytes(dataPath, data);
            string Metadata(string patch) => JsonSerializer.Serialize(new
            {
                Status = "candidate_capture_only",
                ProcessId = 123,
                AreaHash = "42",
                AreaAddress = "0xABCDEF",
                Anchor = "ingame_data_address",
                PackedStart = "0x100000",
                RawGridX = 3.0,
                RawGridY = 3.0,
                SeenUtc = DateTime.UtcNow,
                Width = 128,
                Height = 64,
                BytesPerRow = 64,
                PackedBytes = 4096,
                PatchX = 0,
                PatchY = 0,
                PatchRows = new[] { patch },
                Sha256 = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(),
                DataFile = "capture.packed"
            });
            File.WriteAllText(metadataPath, Metadata("01"));
            if (!RadarTerrainCapture.TryLoad(metadataPath, out var capture) || capture is null ||
                capture.ProcessId != 123 || capture.AreaHash != 42 ||
                capture.Width != 128 || capture.Height != 64 ||
                capture.Walkable(0, 0) || !capture.Walkable(1, 0))
                throw new Exception("Radar rejected a valid bounded terrain capture or decoded nibble order incorrectly.");
            File.WriteAllText(metadataPath, Metadata("10"));
            if (RadarTerrainCapture.TryLoad(metadataPath, out _))
                throw new Exception("Radar accepted terrain whose observed patch disagrees with packed cells.");
            File.WriteAllText(metadataPath, Metadata("01"));
            data[1] = 0;
            File.WriteAllBytes(dataPath, data);
            if (RadarTerrainCapture.TryLoad(metadataPath, out _))
                throw new Exception("Radar accepted terrain with a changed packed file.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
