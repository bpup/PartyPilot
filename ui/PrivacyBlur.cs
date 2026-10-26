namespace PartyPilot.Desktop;

public sealed record PrivacyBlurFrame(int Width, int Height, byte[] Pixels);

public static class PrivacyBlur
{
    public const double DefaultStrength = 55;

    public static int Radius(double strength, int clientHeight)
    {
        var fraction = (Math.Clamp(strength, 2, 100) - 2) / 98;
        var scale = Math.Clamp(clientHeight / 1080d, 0.65, 2);
        return Math.Max(1, (int)Math.Round((1 + Math.Pow(fraction, 1.45) * 11) * scale));
    }

    // The source is a top-down BGRA client-area frame. A box is clipped to that client.
    public static PrivacyBlurFrame? Build(byte[] source, int clientWidth, int clientHeight,
        PrivacyMaskBox box, double strength = DefaultStrength)
    {
        if (clientWidth <= 0 || clientHeight <= 0 ||
            source.Length < (long)clientWidth * clientHeight * 4) return null;
        var x0 = Math.Clamp((int)Math.Round(box.X * clientWidth), 0, clientWidth);
        var y0 = Math.Clamp((int)Math.Round(box.Y * clientHeight), 0, clientHeight);
        var x1 = Math.Clamp(x0 + (int)Math.Round(box.Width * clientWidth), 0, clientWidth);
        var y1 = Math.Clamp(y0 + (int)Math.Round(box.Height * clientHeight), 0, clientHeight);
        var width = x1 - x0;
        var height = y1 - y0;
        if (width <= 0 || height <= 0) return null;
        var radius = Math.Clamp(Radius(strength, clientHeight), 1, Math.Max(1, Math.Min(width, height) / 3));
        var horizontal = new byte[checked(width * height * 4)];
        var blurred = new byte[horizontal.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var left = Math.Max(0, x - radius);
                var right = Math.Min(width - 1, x + radius);
                long b = 0, g = 0, r = 0;
                for (var k = left; k <= right; k++)
                {
                    var index = ((y0 + y) * clientWidth + x0 + k) * 4;
                    b += source[index]; g += source[index + 1]; r += source[index + 2];
                }
                var target = (y * width + x) * 4;
                var count = right - left + 1;
                horizontal[target] = (byte)(b / count);
                horizontal[target + 1] = (byte)(g / count);
                horizontal[target + 2] = (byte)(r / count);
                horizontal[target + 3] = 255;
            }
        }
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                var top = Math.Max(0, y - radius);
                var bottom = Math.Min(height - 1, y + radius);
                long b = 0, g = 0, r = 0;
                for (var k = top; k <= bottom; k++)
                {
                    var index = (k * width + x) * 4;
                    b += horizontal[index]; g += horizontal[index + 1]; r += horizontal[index + 2];
                }
                var target = (y * width + x) * 4;
                var count = bottom - top + 1;
                blurred[target] = (byte)(b / count);
                blurred[target + 1] = (byte)(g / count);
                blurred[target + 2] = (byte)(r / count);
                blurred[target + 3] = 255;
            }
        }
        return new PrivacyBlurFrame(width, height, blurred);
    }
}
