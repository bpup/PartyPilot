using PartyPilot.Desktop;

internal static class PartyRosterCounterSmoke
{
    public static void Run()
    {
        Check(804, 764, [145, 235, 325, 415, 505, 595], 170, 390, 9, 6);
        Check(804, 764, [145, 235, 325, 415], 170, 390, 9, 4);
        Check(400, 300, [42, 82, 122, 162, 202], 110, 166, 8, 5);

        var wide = new byte[1566 * 859 * 4];
        foreach (var top in new[] { 217, 279, 343, 406 })
        {
            Paint(wide, 1566, top, 390, 632, 9);
            Paint(wide, 1566, top + 30, 480, 620, 20);
        }
        if (PartyRosterCounter.CountCandidateRows(wide, 1566, 859) != 4)
            throw new Exception("Wide Current Party cards were counted twice.");
        if (PartyRosterCounter.CountCandidateRows(wide, 299, 859) != 0 ||
            PartyRosterCounter.CountCandidateRows(wide.AsSpan(0, 100), 1566, 859) != 0)
            throw new Exception("Invalid roster frame was accepted.");
    }

    private static void Check(int width, int height, int[] rows, int left,
        int right, int barHeight, int expected)
    {
        var pixels = new byte[width * height * 4];
        foreach (var row in rows) Paint(pixels, width, row, left, right, barHeight);
        if (PartyRosterCounter.CountCandidateRows(pixels, width, height) != expected)
            throw new Exception($"Current Party row count changed for {width}x{height}.");
    }

    private static void Paint(byte[] pixels, int width, int top, int left, int right, int height)
    {
        for (var y = top; y < top + height; y++)
        {
            for (var x = left; x <= right; x++)
            {
                var index = (y * width + x) * 4;
                pixels[index] = 18;
                pixels[index + 1] = 22;
                pixels[index + 2] = 94;
                pixels[index + 3] = 255;
            }
        }
    }
}
