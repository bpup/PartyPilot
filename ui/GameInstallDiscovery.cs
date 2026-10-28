namespace PartyPilot.Desktop;

internal static class GameInstallDiscovery
{
    private static readonly string[] Executables =
    [
        "PathOfExile.exe", "PathOfExile_x64.exe",
        "PathOfExileSteam.exe", "PathOfExile_x64Steam.exe",
        "PathOfExileEGS.exe", "PathOfExile_x64EGS.exe"
    ];

    internal static IReadOnlyList<string> FindExecutables(string directory)
    {
        if (!Directory.Exists(directory)) return [];
        return Executables.Select(name => Path.Combine(directory, name))
            .Where(File.Exists).ToArray();
    }
}
