namespace PartyPilot.Bridge;

internal readonly record struct Client3AttachRequest(int Pid, long WindowHandle,
    string GameExecutable, bool IncludeP6)
{
    internal static bool TryParse(string[] args, out Client3AttachRequest request)
    {
        request = default;
        if (args.Length != 9 || args[0] != "--attach-private-client3") return false;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index += 2)
        {
            if (args[index] is not ("--pid" or "--window-handle" or "--game-exe" or "--players") ||
                !values.TryAdd(args[index], args[index + 1])) return false;
        }
        if (values.Count != 4 || !int.TryParse(values["--pid"], out var pid) || pid <= 0 ||
            !long.TryParse(values["--window-handle"], out var window) || window <= 0 ||
            string.IsNullOrWhiteSpace(values["--game-exe"]) ||
            values["--players"] is not ("5" or "5,6")) return false;
        request = new Client3AttachRequest(pid, window, values["--game-exe"],
            values["--players"] == "5,6");
        return true;
    }

    internal static bool TryParsePreResume(string[] args, out Client3AttachRequest request)
    {
        request = default;
        if (args.Length != 7 || args[0] != "--prepare-private-client3") return false;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index += 2)
        {
            if (args[index] is not ("--pid" or "--game-exe" or "--players") ||
                !values.TryAdd(args[index], args[index + 1])) return false;
        }
        if (values.Count != 3 || !int.TryParse(values["--pid"], out var pid) || pid <= 0 ||
            string.IsNullOrWhiteSpace(values["--game-exe"]) ||
            values["--players"] is not ("5" or "5,6")) return false;
        request = new Client3AttachRequest(pid, 0, values["--game-exe"],
            values["--players"] == "5,6");
        return true;
    }
}
