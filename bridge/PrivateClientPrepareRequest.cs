namespace PartyPilot.Bridge;

internal readonly record struct PrivateClientPrepareRequest(int Client, int Pid,
    string GameExecutable, int[] Slots)
{
    internal bool IncludeSecond => Slots.Length == 2;

    internal static bool TryParse(string[] args, out PrivateClientPrepareRequest request)
    {
        request = default;
        if (args.Length != 9 || args[0] != "--prepare-private-client") return false;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index += 2)
        {
            if (args[index] is not ("--client" or "--pid" or "--game-exe" or "--slots") ||
                !values.TryAdd(args[index], args[index + 1])) return false;
        }
        if (values.Count != 4 || !int.TryParse(values["--client"], out var client) ||
            client is < 1 or > 3 || !int.TryParse(values["--pid"], out var pid) || pid <= 0 ||
            string.IsNullOrWhiteSpace(values["--game-exe"]) ||
            !Client3RuntimeAttach.IsKnownGameExecutable(values["--game-exe"])) return false;
        var first = client * 2 - 1;
        var slots = values["--slots"] == first.ToString()
            ? new[] { first }
            : values["--slots"] == $"{first},{first + 1}"
                ? new[] { first, first + 1 } : [];
        if (slots.Length == 0) return false;
        request = new PrivateClientPrepareRequest(client, pid, values["--game-exe"], slots);
        return true;
    }
}
