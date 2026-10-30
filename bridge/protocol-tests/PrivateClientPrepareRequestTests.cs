using PartyPilot.Bridge;

internal static class PrivateClientPrepareRequestTests
{
    internal static void Run()
    {
        foreach (var client in Enumerable.Range(1, 3))
        {
            var first = client * 2 - 1;
            foreach (var slots in new[] { first.ToString(), $"{first},{first + 1}" })
            {
                var args = Arguments(client.ToString(), "123", slots);
                if (!PrivateClientPrepareRequest.TryParse(args, out var request) ||
                    request.Client != client || request.Pid != 123 ||
                    request.Slots.Length != (slots.Contains(',') ? 2 : 1) ||
                    request.Slots[0] != first || request.IncludeSecond != slots.Contains(','))
                    throw new Exception("Valid per-client private preparation was rejected.");
            }
        }
        foreach (var args in new[]
        {
            Arguments("0", "123", "1"), Arguments("4", "123", "7"),
            Arguments("2", "0", "3"), Arguments("2", "123", "4"),
            Arguments("2", "123", "3,3"), Arguments("2", "123", "3,5"),
            Arguments("2", "123", "3,4,5"),
            Arguments("2", "123", "3").Concat(["--pid", "124"]).ToArray()
        })
            if (PrivateClientPrepareRequest.TryParse(args, out _))
                throw new Exception("Invalid per-client private preparation was accepted.");
    }

    private static string[] Arguments(string client, string pid, string slots) =>
    ["--prepare-private-client", "--client", client, "--pid", pid,
        "--game-exe", @"C:\PoE2\PathOfExile_x64.exe", "--slots", slots];
}
