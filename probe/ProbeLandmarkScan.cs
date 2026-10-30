using PartyPilot.Sensor;

namespace PartyPilot.Probe;

// Scan optional map anchors on a separate read-only handle so a large tile
// vector cannot delay the motion frames sent by the probe's watch loop.
internal static class ProbeLandmarkScan
{
    internal static Task<bool> Start(int processId, AnchorCandidate anchor,
        long gameStateAddress, KnownGameLayout gameLayout, StateCandidate state,
        uint areaHash, PackedTerrainProbe.Observation observation,
        PackedTerrainProbe.PackedSource terrainSource,
        PackedTerrainProbe.AreaLayout? terrainLayout, float gridX, float gridY,
        string dataFile,
        CancellationToken cancellationToken) => Task.Run(async () =>
    {
        try
        {
            using var memory = Win32Memory.Open(processId);
            byte[]? Read(long address, int length)
            {
                if (cancellationToken.IsCancellationRequested ||
                    !Win32Memory.Plausible(address) || length is < 1 or > 64 * 1024)
                    return null;
                var bytes = new byte[length];
                return memory.TryRead(address, bytes) ? bytes : null;
            }
            bool AreaStillCurrent() => !cancellationToken.IsCancellationRequested &&
                AnchorCandidateScanner.TryReadCurrentGameState(anchor, memory.TryRead,
                    out var currentGameState) && currentGameState == gameStateAddress &&
                StateCandidateValidator.TryRead(gameStateAddress, gameLayout,
                    memory.TryRead, out var current) &&
                current?.IngameStateAddress == state.IngameStateAddress &&
                current.AreaInstanceAddress == state.AreaInstanceAddress &&
                AreaHashCandidate.ReadStable(state.AreaInstanceAddress, memory.TryRead) ==
                    areaHash;

            // The source probe waits for zone placements to settle before its
            // first scan. Keep that wait off the position sampling thread.
            await Task.Delay(TimeSpan.FromMilliseconds(1500), cancellationToken);
            GeneratedRoomProbe.Result? rooms = null;
            TerrainPlacementProbe.Result? placements = null;
            QuestTileProbe.Result? tiles = null;
            var currentLayout = terrainLayout;
            var areaHashText = areaHash.ToString(System.Globalization.CultureInfo.InvariantCulture);
            while (AreaStillCurrent())
            {
                if (currentLayout is null)
                {
                    var refreshed = PackedTerrainProbe.Scan(state.AreaInstanceAddress,
                        gridX, gridY, Read);
                    if (refreshed.Source == terrainSource)
                        currentLayout = refreshed.Layout;
                }
                if (!RoomsComplete(rooms))
                    rooms = GeneratedRoomProbe.Scan(state.AreaInstanceAddress,
                        observation.Width, observation.Height, Read);
                if (!AreaStillCurrent()) return false;
                if (currentLayout is { } layout)
                {
                    if (!PlacementsComplete(placements))
                        placements = TerrainPlacementProbe.Scan(layout,
                            observation.Width, observation.Height, Read);
                    if (!AreaStillCurrent()) return false;
                    if (!TilesComplete(tiles))
                    {
                        var areaId = RawAreaIdReader.Read(state.IngameStateAddress, Read);
                        tiles = QuestTileProbe.Scan(layout, observation.Width,
                            observation.Height, areaId, Read);
                    }
                }
                if (!AreaStillCurrent()) return false;
                var update = PackedTerrainCapture.UpdateAnchors(dataFile, processId,
                    areaHashText, observation.AreaAddress, observation.Anchor,
                    rooms, placements, tiles, AreaStillCurrent);
                if (update is not ("markers_updated" or "already_captured"))
                {
                    Console.Error.WriteLine($"landmark_scan pid={processId} status={update}");
                    return false;
                }
                if (RoomsComplete(rooms) &&
                    currentLayout is not null && PlacementsComplete(placements) &&
                    TilesComplete(tiles))
                    return true;
                await Task.Delay(TimeSpan.FromMilliseconds(1500), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error)
        {
            Console.Error.WriteLine($"landmark_scan pid={processId} failed={error.GetType().Name}");
        }
        return false;
    });

    private static bool RoomsComplete(GeneratedRoomProbe.Result? result) =>
        result is { Status: "candidate_rooms_observed", AnchorsTruncated: false } or
            { Status: "no_room_paths", AnchorsTruncated: false };

    private static bool PlacementsComplete(TerrainPlacementProbe.Result? result) =>
        result is { Status: "candidate_placements_observed" or "no_matches",
            Truncated: false };

    private static bool TilesComplete(QuestTileProbe.Result? result) =>
        result is { Status: "candidate_tiles_observed" or "no_matches" or
            "area_not_supported", Truncated: false };
}
