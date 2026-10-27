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
            if (!initial.Success || !initial.SprintCatchUpPriority || !initial.MirrorAttackDirection)
                throw new Exception("Target follow input defaults were not restored.");
            if (!store.Save(false, false).Success)
                throw new Exception("Follow input selection was not saved.");
            var restored = new FollowInputPreferenceStore(path).Load();
            if (!restored.Success || restored.SprintCatchUpPriority || restored.MirrorAttackDirection)
                throw new Exception("Follow input selection did not survive restart.");
            File.WriteAllText(path, "{\"SprintCatchUpPriority\":false}");
            if (store.Load().Success)
                throw new Exception("An incomplete follow input file was accepted.");
            File.WriteAllText(path,
                "{\"SprintCatchUpPriority\":false,\"MirrorAttackDirection\":\"false\"}");
            if (store.Load().Success)
                throw new Exception("A follow input string was accepted as a boolean.");
            if (!store.Save(true, false).Success ||
                new FollowInputPreferenceStore(path).Load() is not
                    { Success: true, SprintCatchUpPriority: true, MirrorAttackDirection: false })
                throw new Exception("Invalid follow input settings could not be repaired.");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
