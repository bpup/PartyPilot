namespace PartyPilot.Desktop;

public readonly record struct PrivacyPixelRect(int X, int Y, int Width, int Height);

public static class PrivacyNameplateDetector
{
    private static readonly byte[] RedCandidates = BuildRedCandidates();

    public static IReadOnlyList<PrivacyPixelRect> Locate(byte[] pixels, int width, int height)
    {
        if (width <= 0 || height <= 0 || pixels.Length < (long)width * height * 4) return [];
        var left = Math.Clamp((int)Math.Round(width * 0.18), 0, width - 1);
        var right = Math.Clamp((int)Math.Round(width * 0.99), left + 1, width);
        var top = Math.Clamp((int)Math.Round(height * 0.05), 0, height - 1);
        var bottom = Math.Clamp((int)Math.Round(height * 0.9), top + 1, height);
        var rows = new List<int>();
        for (var y = top; y < bottom; y++)
        {
            var hits = 0;
            for (var x = left; x < right; x++)
                if (IsNamePixel(pixels, (y * width + x) * 4)) hits++;
            if (hits >= 8) rows.Add(y);
        }
        var candidates = new List<(PrivacyPixelRect Bounds, int Score)>();
        for (var row = 0; row < rows.Count;)
        {
            var first = rows[row];
            var last = first;
            row++;
            while (row < rows.Count && rows[row] - last <= 3) last = rows[row++];
            var textHeight = last - first + 1;
            if (textHeight is < 9 or > 32) continue;
            var columns = new int[right - left];
            for (var y = first; y <= last; y++)
                for (var x = left; x < right; x++)
                    if (IsNamePixel(pixels, (y * width + x) * 4)) columns[x - left]++;
            for (var column = 0; column < columns.Length;)
            {
                while (column < columns.Length && columns[column] == 0) column++;
                if (column == columns.Length) break;
                var start = column;
                var end = column;
                column++;
                while (column < columns.Length)
                {
                    if (columns[column] > 0) end = column;
                    else if (column - end > 14) break;
                    column++;
                }
                var textWidth = end - start + 1;
                if (textWidth is < 32 or > 360) continue;
                var score = 0;
                var maxRowHits = 0;
                for (var y = first; y <= last; y++)
                {
                    var rowHits = 0;
                    for (var x = left + start; x <= left + end; x++)
                        if (IsNamePixel(pixels, (y * width + x) * 4)) rowHits++;
                    score += rowHits;
                    maxRowHits = Math.Max(maxRowHits, rowHits);
                }
                if (score < 60 || maxRowHits < 8) continue;
                var x0 = Math.Max(0, left + start - 8);
                var y0 = Math.Max(0, first - 4);
                candidates.Add((new PrivacyPixelRect(x0, y0,
                    Math.Min(width - x0, textWidth + 16),
                    Math.Min(height - y0, textHeight + 8)), score));
            }
        }
        return candidates.OrderByDescending(candidate => candidate.Score).Take(4)
            .OrderBy(candidate => candidate.Bounds.Y).Select(candidate => candidate.Bounds).ToArray();
    }

    public static PrivacyMaskBox Expand(int client, PrivacyPixelRect detected, int width, int height)
    {
        var scale = Math.Clamp(height / 1080d, 0.4, 2.5);
        var maskWidth = Math.Clamp(Math.Max((int)Math.Round(170 * scale),
            detected.Width + (int)Math.Round(12 * scale)), 1, width);
        var maskHeight = Math.Clamp(Math.Max((int)Math.Round(37 * scale),
            detected.Height + (int)Math.Round(8 * scale)), 1, height);
        var x = Math.Clamp((int)Math.Round(detected.X + detected.Width / 2d - maskWidth / 2d),
            0, width - maskWidth);
        var y = Math.Clamp((int)Math.Round(detected.Y + detected.Height / 2d - maskHeight / 2d),
            0, height - maskHeight);
        return new PrivacyMaskBox(client, x / (double)width, y / (double)height,
            maskWidth / (double)width, maskHeight / (double)height);
    }

    public static IReadOnlyList<PrivacyPixelRect> SelectTwo(IReadOnlyList<PrivacyPixelRect> candidates,
        int clientHeight)
    {
        if (candidates.Count == 0) return [];
        var first = candidates[0];
        var minimum = Math.Max(42, (int)Math.Round(clientHeight * 0.04));
        var second = candidates.Skip(1).OrderByDescending(candidate => DistanceSquared(first, candidate))
            .FirstOrDefault(candidate => DistanceSquared(first, candidate) >= minimum * minimum);
        return second.Width > 0 && second.Height > 0 ? [first, second] : [first];
    }

    private static double DistanceSquared(PrivacyPixelRect a, PrivacyPixelRect b)
    {
        var dx = a.X + a.Width / 2d - b.X - b.Width / 2d;
        var dy = a.Y + a.Height / 2d - b.Y - b.Height / 2d;
        return dx * dx + dy * dy;
    }

    private static bool IsNamePixel(byte[] pixels, int index)
    {
        var blue = pixels[index];
        var green = pixels[index + 1];
        var red = pixels[index + 2];
        var mask = RedCandidates[red];
        return mask != 0 &&
            (((mask & 1) != 0 && Near(green, blue, 144, 124)) ||
             ((mask & 2) != 0 && Near(green, blue, 105, 90)) ||
             ((mask & 4) != 0 && Near(green, blue, 135, 116)) ||
             ((mask & 8) != 0 && Near(green, blue, 116, 100)) ||
             ((mask & 16) != 0 && Near(green, blue, 126, 109)) ||
             ((mask & 32) != 0 && Near(green, blue, 111, 95)));
    }

    private static bool Near(int green, int blue, int targetGreen, int targetBlue) =>
        Math.Abs(green - targetGreen) <= 3 && Math.Abs(blue - targetBlue) <= 3;

    private static byte[] BuildRedCandidates()
    {
        var candidates = new byte[256];
        var reds = new[] { 169, 124, 159, 137, 149, 131 };
        for (var color = 0; color < reds.Length; color++)
            for (var red = reds[color] - 3; red <= reds[color] + 3; red++)
                candidates[red] |= (byte)(1 << color);
        return candidates;
    }
}
