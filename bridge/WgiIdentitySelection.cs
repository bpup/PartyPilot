namespace PartyPilot.Bridge;

internal static class WgiIdentitySelection
{
    // The initial claim must be unique. After virtual pads appear, other devices
    // may share the VID/PID, so retain only the exact WGI identity claimed first.
    internal static int SelectClaimedIndex(IReadOnlyList<string?> candidates, string claimedId)
    {
        if (string.IsNullOrWhiteSpace(claimedId)) return -1;
        var selected = -1;
        for (var index = 0; index < candidates.Count; index++)
        {
            if (!string.Equals(candidates[index], claimedId, StringComparison.Ordinal)) continue;
            if (selected >= 0) return -1;
            selected = index;
        }
        return selected;
    }
}
