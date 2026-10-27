using PartyPilot.Desktop;

internal static class FollowInputPreferenceSmoke
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(),
            "partypilot-follow-input-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "follow-input.json");
        try
        {
            var store = new FollowInputPreferenceStore(path);
            var initial = store.Load();
            if (!initial.Success || initial.Options != FollowInputOptions.TargetDefaults)
                throw new Exception("Observed follow input defaults were not restored.");
            var chosen = FollowInputOptions.TargetDefaults with
            {
                SprintCatchUp = false,
                SprintCatchUpPriority = false,
                MirrorAttackDirection = false,
                AggressiveBlinkCatchUp = true,
                AggressiveSprintRetryDelayMs = 900,
                CloseRangeMirror = true,
                CloseRangeMirrorDurationMs = 650
            };
            if (!store.Save(chosen).Success || new FollowInputPreferenceStore(path).Load() is not
                { Success: true, Options: var restored } || restored != chosen)
                throw new Exception("Follow input selection did not survive restart.");
            File.WriteAllText(path,
                "{\"SprintCatchUpPriority\":false,\"MirrorAttackDirection\":false}");
            var legacy = store.Load();
            if (!legacy.Success || legacy.Options != FollowInputOptions.TargetDefaults with
                { SprintCatchUpPriority = false, MirrorAttackDirection = false } ||
                File.ReadAllText(path).Contains("\"SprintCatchUp\":true", StringComparison.Ordinal) == false)
                throw new Exception("The previous two-field preference was not migrated safely.");
            File.WriteAllText(path, "{\"SprintCatchUpPriority\":false}");
            if (store.Load().Success)
                throw new Exception("An incomplete follow input file was accepted.");
            File.WriteAllText(path,
                "{\"SprintCatchUpPriority\":false,\"MirrorAttackDirection\":\"false\"}");
            if (store.Load().Success)
                throw new Exception("A follow input string was accepted as a boolean.");
            File.WriteAllText(path,
                "{\"SprintCatchUp\":true,\"SprintCatchUpPriority\":false," +
                "\"MirrorAttackDirection\":false,\"ClassicSprintCatchUp\":false," +
                "\"AggressiveBlinkCatchUp\":false,\"AggressiveSprintRetryDelayMs\":\"800\"," +
                "\"CloseRangeMirror\":false,\"CloseRangeMirrorDurationMs\":400}");
            if (store.Load().Success)
                throw new Exception("A retry interval string was accepted as a number.");
            if (store.Save(chosen with { AggressiveSprintRetryDelayMs = 499 }).Success ||
                store.Save(chosen with { ClassicSprintCatchUp = true }).Success)
                throw new Exception("Invalid follow input settings were saved.");
            if (!store.Save(chosen).Success || store.Load() is not
                { Success: true, Options: var repaired } || repaired != chosen)
                throw new Exception("Invalid follow input settings could not be repaired.");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
