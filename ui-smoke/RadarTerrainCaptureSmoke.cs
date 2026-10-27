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
                DataFile = "capture.packed",
                GeneratedRoomStatus = "candidate_rooms_observed",
                GeneratedRoomAreaAddress = "0xABCDEF",
                GeneratedRoomAnchor = "ingame_data_address",
                GeneratedRooms = new[]
                {
                    new { Path = "Metadata/Terrain/Gallows/Act1/1_6/Rooms/boss_1.arm",
                        Category = "boss", Label = "Boss arena", GridX = 11.5, GridY = 11.5,
                        Left = 0, Top = 0, Right = 1, Bottom = 1 }
                },
                TerrainPlacementStatus = "candidate_placements_observed",
                TerrainPlacementAreaAddress = "0xABCDEF",
                TerrainPlacementAnchor = "ingame_data_address",
                TerrainPlacements = new[]
                {
                    new { Path = "Metadata/Terrain/Gallows/Act3/Objects/MachinariumGeneratorButton",
                        Category = "quest", Label = "Soul Core Generator",
                        GridX = 11.5, GridY = 11.5, TileX = 0, TileY = 0 }
                },
                QuestTileStatus = "candidate_tiles_observed",
                QuestTileAreaId = "G1_8",
                QuestTileAreaAddress = "0xABCDEF",
                QuestTileAnchor = "ingame_data_address",
                QuestTiles = new[]
                {
                    new { Path = "Metadata/Terrain/Woods/GraveyardDungeons/Feature/MausoleumBoss_ArenaFloor.tdt",
                        Category = "campaign", Label = "MausoleumBoss_ArenaFloor",
                        GridX = 11.5, GridY = 11.5, TileX = 0, TileY = 0,
                        RotationX = (int?)0, RotationY = (int?)0, RotationSwap = (bool?)false,
                        AdjustmentX = 0, AdjustmentY = 0 }
                }
            });
            File.WriteAllText(metadataPath, Metadata("01"));
            if (!RadarTerrainCapture.TryLoad(metadataPath, out var capture) || capture is null ||
                capture.ProcessId != 123 || capture.AreaHash != 42 ||
                capture.Width != 128 || capture.Height != 64 ||
                capture.Rooms.Count != 3 ||
                capture.Rooms[0] is not { Category: "boss", Label: "Boss arena" } ||
                capture.Rooms[1] is not { Category: "quest", Label: "Soul Core Generator" } ||
                capture.Rooms[2] is not { Category: "campaign", Label: "MausoleumBoss_ArenaFloor" } ||
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
                matching with { GridX = 1, GridY = 0 }, 64, 64, 8);
            if (projected[(32 * 64 + 26) * 4 + 3] != 0 ||
                projected[(27 * 64 + 32) * 4 + 3] != 224 ||
                projected[(63 * 64) * 4 + 3] != 0)
                throw new Exception($"Radar terrain preview lost blocked, walkable, or outside-map pixels: " +
                    $"blocked={projected[(32 * 64 + 26) * 4 + 3]}, " +
                    $"walkable={projected[(27 * 64 + 32) * 4 + 3]}, " +
                    $"outside={projected[(63 * 64) * 4 + 3]}.");
            var interior = RadarTerrainRaster.Render(capture, matching, 1, 1, 1);
            if (interior[0] != 9 || interior[1] != 7 ||
                interior[2] != 5 || interior[3] != 38)
                throw new Exception("Radar terrain lost its faint walkable fill.");
            var diagonal = RadarTerrainRaster.Render(capture,
                matching with { GridX = 1, GridY = 1 }, 1, 1, 1);
            if (diagonal[3] != 38)
                throw new Exception("A diagonal blocked cell incorrectly became a wall edge.");
            var autoDirectory = Path.Combine(directory, "auto");
            Directory.CreateDirectory(autoDirectory);
            var catalog = new RadarTerrainCatalog(autoDirectory);
            var scanAt = DateTime.UtcNow;
            if (catalog.FindMatching(matching, scanAt) is not null)
                throw new Exception("Radar found terrain before the sensor captured it.");
            File.Copy(dataPath, Path.Combine(autoDirectory, "capture.packed"));
            File.Copy(metadataPath, Path.Combine(autoDirectory, "terrain-123-test.json"));
            if (catalog.FindMatching(matching, scanAt.AddSeconds(1)) is not null ||
                catalog.FindMatching(matching, scanAt.AddSeconds(3)) is not { } discovered ||
                !discovered.Matches(matching) ||
                catalog.FindMatching(matching with { AreaHash = 43 }, scanAt.AddSeconds(4)) is not null)
                throw new Exception("Radar did not discover a new matching capture or reject a new area.");
            var terrainWindow = new RadarOverlayWindow(new LaunchedClient(123, 1, "C:/Game.exe"),
                2, new RadarVisibility(), renderOnly: true);
            terrainWindow.Show();
            terrainWindow.Apply(matching, discovered);
            terrainWindow.UpdateLayout();
            if (!terrainWindow.TerrainVisible ||
                terrainWindow.GetLogicalDescendants().OfType<Image>().Count() != 1 ||
                !terrainWindow.GetLogicalDescendants().OfType<TextBlock>()
                    .Any(label => label.Text == "Boss arena") ||
                !terrainWindow.GetLogicalDescendants().OfType<TextBlock>()
                    .Any(label => label.Text == "Soul Core Generator") ||
                !terrainWindow.GetLogicalDescendants().OfType<TextBlock>()
                    .Any(label => label.Text == "MausoleumBoss_ArenaFloor") ||
                terrainWindow.CaptureRenderedFrame() is null)
                throw new Exception("Matching terrain was not drawn behind radar markers.");
            terrainWindow.Apply(matching with { AreaHash = 43 }, null);
            if (terrainWindow.TerrainVisible ||
                terrainWindow.GetLogicalDescendants().OfType<Image>().Any())
                throw new Exception("Radar drew candidate terrain for a different area.");
            terrainWindow.Close();
            var autoMetadataPath = Path.Combine(autoDirectory, "terrain-123-test.json");
            File.WriteAllText(autoMetadataPath, Metadata("01").Replace(
                "\"Label\":\"Soul Core Generator\"", "\"Label\":\"Unknown\""));
            File.SetLastWriteTimeUtc(autoMetadataPath, DateTime.UtcNow.AddSeconds(2));
            if (catalog.FindMatching(matching, scanAt.AddSeconds(6)) is not null)
                throw new Exception("Radar retained cached room markers after metadata changed.");
            File.WriteAllText(metadataPath, Metadata("01").Replace("\"Category\":\"boss\"",
                "\"Category\":\"quest\""));
            if (RadarTerrainCapture.TryLoad(metadataPath, out _))
                throw new Exception("Radar accepted a generated-room category that disagrees with its path.");
            File.WriteAllText(metadataPath, Metadata("01").Replace("\"GridX\":11.5",
                "\"GridX\":12.5"));
            if (RadarTerrainCapture.TryLoad(metadataPath, out _))
                throw new Exception("Radar accepted a generated-room coordinate outside its tile center.");
            File.WriteAllText(metadataPath, Metadata("01").Replace("\"Label\":\"Soul Core Generator\"",
                "\"Label\":\"Unknown\""));
            if (RadarTerrainCapture.TryLoad(metadataPath, out _))
                throw new Exception("Radar accepted a terrain placement label that disagrees with its path.");
            File.WriteAllText(metadataPath, Metadata("01").Replace("\"QuestTileAreaId\":\"G1_8\"",
                "\"QuestTileAreaId\":\"G1_9\""));
            if (RadarTerrainCapture.TryLoad(metadataPath, out _))
                throw new Exception("Radar accepted a tile landmark from another area.");
            File.WriteAllText(metadataPath, Metadata("01").Replace("\"RotationX\":0",
                "\"RotationX\":1"));
            if (RadarTerrainCapture.TryLoad(metadataPath, out _))
                throw new Exception("Radar accepted a campaign label with the wrong tile rotation.");
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
