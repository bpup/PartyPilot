namespace PartyPilot.Bridge;

internal readonly record struct CompositeClient3Options(int Pid, long WindowHandle,
    string GameExecutable, bool IncludeP6)
{
    internal int[] PrivateSlots => IncludeP6 ? [5, 6] : [5];

    internal bool MatchesThirdGamePath(IReadOnlyList<string> gamePaths)
    {
        if (gamePaths.Count != 3 || !Path.IsPathFullyQualified(gamePaths[2])) return false;
        try
        {
            return string.Equals(Path.GetFullPath(gamePaths[2]), Path.GetFullPath(GameExecutable),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    internal bool MatchesPairingTarget(PairingControl control) =>
        (control.Slot == 5 || IncludeP6 && control.Slot == 6) &&
        control.Pid == Pid && control.WindowHandle == WindowHandle;

    internal static bool TryParse(string[] args, out CompositeClient3Options? options)
    {
        options = null;
        var names = new[] { "--client3-pid", "--client3-window-handle",
            "--client3-game-exe", "--client3-players" };
        if (!args.Any(names.Contains)) return true;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index++)
        {
            if (!names.Contains(args[index])) continue;
            if (index + 1 >= args.Length || !values.TryAdd(args[index], args[++index]))
                return false;
        }
        if (values.Count != names.Length ||
            !int.TryParse(values["--client3-pid"], out var pid) || pid <= 0 ||
            !long.TryParse(values["--client3-window-handle"], out var window) || window <= 0 ||
            !Path.IsPathFullyQualified(values["--client3-game-exe"]) ||
            !Client3RuntimeAttach.IsKnownGameExecutable(values["--client3-game-exe"]) ||
            values["--client3-players"] is not ("5" or "5,6")) return false;
        options = new CompositeClient3Options(pid, window, values["--client3-game-exe"],
            values["--client3-players"] == "5,6");
        return true;
    }
}
