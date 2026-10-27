namespace PartyPilot.Desktop;

// Draws the full observed packed grid around the current raw player grid
// coordinate. This does not establish the candidate's game-world calibration.
public static class RadarTerrainRaster
{
    private static readonly (int X, int Y)[] CardinalNeighbors =
        [(-1, 0), (1, 0), (0, -1), (0, 1)];

    public static byte[] Render(RadarTerrainCapture capture, RadarFrame frame,
        int width, int height, double pixelsPerGridCell,
        RadarAppearanceSettings? appearance = null)
    {
        if (!capture.Matches(frame) || width is < 1 or > 4096 || height is < 1 or > 4096 ||
            (long)width * height > 4_194_304 || !double.IsFinite(pixelsPerGridCell) ||
            pixelsPerGridCell <= 0)
            throw new ArgumentException("Radar terrain needs a matching frame and bounded projection.");
        var pixels = new byte[width * height * 4];
        var style = appearance ?? RadarAppearanceSettings.Default;
        if (style.MapOpacity is < 2 or > 100 || style.BorderOpacity is < 0 or > 100 ||
            style.BackgroundOpacity is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(appearance));
        var borderAlpha = style.BorderAlpha;
        var backgroundAlpha = style.BackgroundAlpha;
        var borderBlue = Premul(198, borderAlpha);
        var borderGreenRed = Premul(210, borderAlpha);
        var backgroundBlue = Premul(58, backgroundAlpha);
        var backgroundGreen = Premul(46, backgroundAlpha);
        var backgroundRed = Premul(34, backgroundAlpha);
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
                    pixels[offset] = borderBlue;
                    pixels[offset + 1] = borderGreenRed;
                    pixels[offset + 2] = borderGreenRed;
                    pixels[offset + 3] = borderAlpha;
                }
                else
                {
                    pixels[offset] = backgroundBlue;
                    pixels[offset + 1] = backgroundGreen;
                    pixels[offset + 2] = backgroundRed;
                    pixels[offset + 3] = backgroundAlpha;
                }
            }
        }
        return pixels;
    }

    private static byte Premul(int colour, int alpha) =>
        (byte)Math.Clamp((int)Math.Round(colour * alpha / 255d), 0, 255);

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
