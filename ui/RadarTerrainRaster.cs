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
        var boundaryByCell = new Dictionary<long, bool>();
        for (var y = 0; y < height; y++)
        {
            boundaryByCell.Clear();
            for (var x = 0; x < width; x++)
            {
                var (rawX, rawY) = RadarProjection.UnprojectGrid(frame.GridX!.Value,
                    frame.GridY!.Value, x + 0.5, y + 0.5, width / 2d, height / 2d,
                    pixelsPerGridCell);
                if (!double.IsFinite(rawX) || !double.IsFinite(rawY) ||
                    rawX < 0 || rawY < 0 || rawX >= capture.Width || rawY >= capture.Height)
                    continue;
                var gridX = (int)Math.Floor(rawX);
                var gridY = (int)Math.Floor(rawY);
                var offset = (y * width + x) * 4;
                var key = ((long)gridY << 32) | (uint)gridX;
                if (!boundaryByCell.TryGetValue(key, out var boundary))
                {
                    boundary = capture.Walkable(gridX, gridY) &&
                        IsBoundary(capture, gridX, gridY);
                    boundaryByCell.Add(key, boundary);
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
