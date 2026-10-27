namespace PartyPilot.Desktop;

// Draws the full observed packed grid around the current raw player grid
// coordinate. This does not establish the candidate's game-world calibration.
public static class RadarTerrainRaster
{
    private const double AntialiasWidth = 0.18333333333333335;

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
        if (borderAlpha == 0 && backgroundAlpha == 0) return pixels;
        var field = RadarTerrainDistanceField.For(capture);
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
            var rowX = origin.X + y * downX;
            var rowY = origin.Y + y * downY;
            for (var x = 0; x < width; x++)
            {
                var rawX = rowX + x * rightX;
                var rawY = rowY + x * rightY;
                if (!double.IsFinite(rawX) || !double.IsFinite(rawY) ||
                    rawX < 0 || rawY < 0 || rawX >= capture.Width || rawY >= capture.Height)
                    continue;
                var distance = field.Sample(rawX, rawY);
                var coverage = SmoothStep(-AntialiasWidth, AntialiasWidth, distance);
                if (coverage <= 0) continue;
                var edgeMix = borderAlpha == 0 ? 0 :
                    1 - SmoothStep(1.2 - AntialiasWidth, 1.2 + AntialiasWidth, distance);
                var edge = edgeMix * borderAlpha;
                var fill = (1 - edgeMix) * backgroundAlpha;
                var total = edge + fill;
                if (total <= 0) continue;
                var offset = (y * width + x) * 4;
                pixels[offset] = Premul(198 * edge + 58 * fill, coverage);
                pixels[offset + 1] = Premul(210 * edge + 46 * fill, coverage);
                pixels[offset + 2] = Premul(210 * edge + 34 * fill, coverage);
                pixels[offset + 3] = ToByte(coverage * total);
            }
        }
        return pixels;
    }

    private static byte Premul(double weightedColour, double coverage) =>
        ToByte(weightedColour * coverage / 255d);

    private static byte ToByte(double value) =>
        (byte)Math.Clamp((int)Math.Round(value), 0, 255);

    private static double SmoothStep(double low, double high, double value)
    {
        var fraction = Math.Clamp((value - low) / (high - low), 0, 1);
        return fraction * fraction * (3 - 2 * fraction);
    }
}
