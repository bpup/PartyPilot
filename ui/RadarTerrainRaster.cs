namespace PartyPilot.Desktop;

// Draws the full observed packed grid around the current raw player grid
// coordinate. This does not establish the candidate's game-world calibration.
public static class RadarTerrainRaster
{
    // 0.6.17 defaults: map 42%, border 40%, background 15%.
    // The target's terrain bitmap scales base alphas 224 and 38 by these
    // settings; Avalonia's Premul bitmap needs the corresponding BGRA bytes.
    private const byte BorderAlpha = 90;
    private const byte BackgroundAlpha = 38;
    private static readonly (int X, int Y)[] CardinalNeighbors =
        [(-1, 0), (1, 0), (0, -1), (0, 1)];

    public static byte[] Render(RadarTerrainCapture capture, RadarFrame frame,
        int width, int height, double pixelsPerGridCell)
    {
        if (!capture.Matches(frame) || width is < 1 or > 4096 || height is < 1 or > 4096 ||
            (long)width * height > 4_194_304 || !double.IsFinite(pixelsPerGridCell) ||
            pixelsPerGridCell <= 0)
            throw new ArgumentException("Radar terrain needs a matching frame and bounded projection.");
        var pixels = new byte[width * height * 4];
        var boundaryByCell = new Dictionary<long, bool>();
        var origin = RadarProjection.UnprojectGrid(frame.GridX!.Value, frame.GridY!.Value,
            0.5, 0.5, width / 2d, height / 2d, pixelsPerGridCell);
        var right = RadarProjection.UnprojectGrid(frame.GridX.Value, frame.GridY!.Value,
            1.5, 0.5, width / 2d, height / 2d, pixelsPerGridCell);
        var down = RadarProjection.UnprojectGrid(frame.GridX.Value, frame.GridY.Value,
            0.5, 1.5, width / 2d, height / 2d, pixelsPerGridCell);
        var rightX = right.X - origin.X;
        var rightY = right.Y - origin.Y;
        var downX = down.X - origin.X;
        var downY = down.Y - origin.Y;
        for (var y = 0; y < height; y++)
        {
            boundaryByCell.Clear();
            var rowX = origin.X + y * downX;
            var rowY = origin.Y + y * downY;
            for (var x = 0; x < width; x++)
            {
                var rawX = rowX + x * rightX;
                var rawY = rowY + x * rightY;
                if (!double.IsFinite(rawX) || !double.IsFinite(rawY) ||
                    rawX < 0 || rawY < 0 || rawX >= capture.Width || rawY >= capture.Height)
                    continue;
                var gridX = (int)Math.Floor(rawX);
                var gridY = (int)Math.Floor(rawY);
                if (!capture.Renderable(gridX, gridY)) continue;
                var offset = (y * width + x) * 4;
                var key = ((long)gridY << 32) | (uint)gridX;
                if (!boundaryByCell.TryGetValue(key, out var boundary))
                {
                    boundary = IsBoundary(capture, gridX, gridY);
                    boundaryByCell.Add(key, boundary);
                }
                if (boundary)
                {
                    pixels[offset] = 70;
                    pixels[offset + 1] = 74;
                    pixels[offset + 2] = 74;
                    pixels[offset + 3] = BorderAlpha;
                }
                else
                {
                    pixels[offset] = 9;
                    pixels[offset + 1] = 7;
                    pixels[offset + 2] = 5;
                    pixels[offset + 3] = BackgroundAlpha;
                }
            }
        }
        return pixels;
    }

    private static bool IsBoundary(RadarTerrainCapture capture, int x, int y)
    {
        foreach (var (offsetX, offsetY) in CardinalNeighbors)
        {
            var neighborX = x + offsetX;
            var neighborY = y + offsetY;
            if (neighborX < 0 || neighborX >= capture.Width ||
                neighborY < 0 || neighborY >= capture.Height ||
                !capture.Renderable(neighborX, neighborY)) return true;
        }
        return false;
    }
}
