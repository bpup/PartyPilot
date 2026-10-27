namespace PartyPilot.Desktop;

// Draws the full observed packed grid around the current raw player grid
// coordinate. This does not establish the candidate's game-world calibration.
public static class RadarTerrainRaster
{
    public static byte[] Render(RadarTerrainCapture capture, RadarFrame frame,
        int width, int height, double pixelsPerGridCell)
    {
        if (!capture.Matches(frame) || width is < 1 or > 4096 || height is < 1 or > 4096 ||
            (long)width * height > 4_194_304 || !double.IsFinite(pixelsPerGridCell) ||
            pixelsPerGridCell <= 0)
            throw new ArgumentException("Radar terrain needs a matching frame and bounded projection.");
        var pixels = new byte[width * height * 4];
        var columns = new int[width];
        for (var x = 0; x < width; x++)
            columns[x] = (int)Math.Floor(frame.GridX!.Value + (x - width / 2d) / pixelsPerGridCell);
        var boundaryByColumn = new Dictionary<int, bool>();
        var previousGridY = int.MinValue;
        for (var y = 0; y < height; y++)
        {
            var gridY = (int)Math.Floor(frame.GridY!.Value + (y - height / 2d) / pixelsPerGridCell);
            if (gridY != previousGridY)
            {
                boundaryByColumn.Clear();
                previousGridY = gridY;
            }
            if (gridY < 0 || gridY >= capture.Height) continue;
            for (var x = 0; x < width; x++)
            {
                var gridX = columns[x];
                if (gridX < 0 || gridX >= capture.Width) continue;
                var offset = (y * width + x) * 4;
                if (!boundaryByColumn.TryGetValue(gridX, out var boundary))
                {
                    boundary = capture.Walkable(gridX, gridY) &&
                        IsBoundary(capture, gridX, gridY);
                    boundaryByColumn.Add(gridX, boundary);
                }
                if (!boundary)
                    continue;
                // The full area outline is visible; blocked and interior cells remain
                // transparent so they cannot become a dark fog layer. BGRA premultiplied.
                pixels[offset] = 174;
                pixels[offset + 1] = 184;
                pixels[offset + 2] = 184;
                pixels[offset + 3] = 224;
            }
        }
        return pixels;
    }

    private static bool IsBoundary(RadarTerrainCapture capture, int x, int y)
    {
        for (var offsetY = -1; offsetY <= 1; offsetY++)
        for (var offsetX = -1; offsetX <= 1; offsetX++)
        {
            if (offsetX == 0 && offsetY == 0) continue;
            var neighborX = x + offsetX;
            var neighborY = y + offsetY;
            if (neighborX < 0 || neighborX >= capture.Width ||
                neighborY < 0 || neighborY >= capture.Height ||
                !capture.Walkable(neighborX, neighborY)) return true;
        }
        return false;
    }
}
