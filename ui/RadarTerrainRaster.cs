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
        for (var y = 0; y < height; y++)
        {
            var gridY = (int)Math.Floor(frame.GridY!.Value + (y - height / 2d) / pixelsPerGridCell);
            if (gridY < 0 || gridY >= capture.Height) continue;
            for (var x = 0; x < width; x++)
            {
                var gridX = columns[x];
                if (gridX < 0 || gridX >= capture.Width) continue;
                var offset = (y * width + x) * 4;
                if (capture.Walkable(gridX, gridY))
                {
                    // BGRA, premultiplied alpha.
                    pixels[offset] = 34;
                    pixels[offset + 1] = 43;
                    pixels[offset + 2] = 15;
                    pixels[offset + 3] = 72;
                }
                else
                {
                    pixels[offset] = 17;
                    pixels[offset + 1] = 19;
                    pixels[offset + 2] = 12;
                    pixels[offset + 3] = 108;
                }
            }
        }
        return pixels;
    }
}
