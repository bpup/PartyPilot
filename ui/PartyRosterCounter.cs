namespace PartyPilot.Desktop;

// Port of the supplied build's Current Party life-bar row classifier.
// A count is visual evidence only; it is not proof of member identity.
public static class PartyRosterCounter
{
    public static int CountCandidateRows(ReadOnlySpan<byte> bgra, int width, int height)
    {
        if (width < 300 || height < 240 || bgra.Length < (long)width * height * 4) return 0;
        var left = Math.Clamp((int)Math.Round(width * 0.27), 20, width - 2);
        var right = Math.Clamp((int)Math.Round(width * 0.42), left + 1, width);
        var top = Math.Clamp((int)Math.Round(height * 0.1), 0, height - 1);
        var bottom = Math.Clamp((int)Math.Round(height * 0.86), top + 1, height);
        var minimumRun = Math.Max(18, (int)Math.Round((right - left) * 0.45));
        var rows = new List<int>();
        for (var y = top; y < bottom; y++)
        {
            var longest = 0;
            var run = 0;
            for (var x = left; x < right; x++)
            {
                var index = (y * width + x) * 4;
                var blue = bgra[index];
                var green = bgra[index + 1];
                var red = bgra[index + 2];
                if (red >= 24 && red * 100 >= green * 108 &&
                    red * 100 >= blue * 103 && red - Math.Min(green, blue) >= 4)
                    longest = Math.Max(longest, ++run);
                else
                    run = 0;
            }
            if (longest >= minimumRun) rows.Add(y);
        }
        var bands = new List<(int Top, int Bottom)>();
        foreach (var y in rows)
        {
            if (bands.Count > 0 && y <= bands[^1].Bottom + Math.Max(4, height / 160))
                bands[^1] = (bands[^1].Top, y);
            else
                bands.Add((y, y));
        }
        var maximumHeight = Math.Max(18, height / 16);
        var accepted = bands.Where(band => band.Bottom - band.Top + 1 <= maximumHeight).ToArray();
        var tallThreshold = Math.Max(6, height / 70);
        var tall = accepted.Count(band => band.Bottom - band.Top + 1 >= tallThreshold);
        var shortCount = accepted.Length - tall;
        return Math.Min(6, tall >= 2 && shortCount >= tall ? tall : accepted.Length);
    }
}
