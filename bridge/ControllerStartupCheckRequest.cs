namespace PartyPilot.Bridge;

internal sealed record ControllerStartupCheckRequest(string Device, string[] GameExecutables,
    int? XInputIndex, bool UseWgi)
{
    internal static bool TryParse(string[] args, out ControllerStartupCheckRequest? request)
    {
        request = null;
        if (args.Length < 6 || args[0] != "--check-startup") return false;
        string? device = null;
        int? index = null;
        var wgi = false;
        var games = new List<string>();
        for (var at = 1; at < args.Length; at++)
        {
            switch (args[at])
            {
                case "--device" when device is null && at + 1 < args.Length:
                    device = args[++at];
                    break;
                case "--game-exe" when at + 1 < args.Length:
                    games.Add(args[++at]);
                    break;
                case "--physical-index" when index is null && at + 1 < args.Length &&
                    int.TryParse(args[at + 1], out var parsed) && parsed is >= 0 and <= 3:
                    index = parsed;
                    at++;
                    break;
                case "--physical-wgi" when !wgi:
                    wgi = true;
                    break;
                default:
                    return false;
            }
        }
        if (string.IsNullOrWhiteSpace(device) || games.Count is < 1 or > 3 ||
            games.Any(string.IsNullOrWhiteSpace) || wgi == index.HasValue) return false;
        request = new ControllerStartupCheckRequest(device, games.ToArray(), index, wgi);
        return true;
    }
}
