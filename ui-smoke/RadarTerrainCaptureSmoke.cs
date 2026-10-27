using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
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
            var matching = new RadarFrame(123, DateTime.UtcNow, 42, "0xABCDEF", 30, 30, [])
                { GridX = 3, GridY = 3 };
            if (!capture.Matches(matching) || capture.Matches(matching with { AreaHash = 43 }) ||
                capture.Matches(matching with { GridX = 128 }) ||
                capture.Matches(matching with { AreaAddress = "0xABCDE0" }) ||
                capture.Matches(matching with { SeenUtc = capture.SeenUtc.AddSeconds(-1) }))
                throw new Exception("Radar terrain accepted a different area, stale frame, or invalid grid coordinate.");
            var projected = RadarTerrainRaster.Render(capture,
                matching with { GridX = 1, GridY = 0 }, 4, 2, 1);
            if (projected[(1 * 4 + 1) * 4 + 3] != 108 ||
                projected[(1 * 4 + 2) * 4 + 3] != 72 ||
                projected[(1 * 4 + 0) * 4 + 3] != 0)
                throw new Exception("Radar terrain preview lost blocked, walkable, or outside-map pixels.");
            var terrainWindow = new RadarOverlayWindow(new LaunchedClient(123, 1, "C:/Game.exe"),
                2, new RadarVisibility(), capture, renderOnly: true);
            terrainWindow.Show();
            terrainWindow.Apply(matching);
            terrainWindow.UpdateLayout();
            if (!terrainWindow.TerrainVisible ||
                terrainWindow.GetLogicalDescendants().OfType<Image>().Count() != 1 ||
                terrainWindow.CaptureRenderedFrame() is null)
                throw new Exception("Matching terrain was not drawn behind radar markers.");
            terrainWindow.Apply(matching with { AreaHash = 43 });
            if (terrainWindow.TerrainVisible ||
                terrainWindow.GetLogicalDescendants().OfType<Image>().Any())
                throw new Exception("Radar drew candidate terrain for a different area.");
            terrainWindow.Close();
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
