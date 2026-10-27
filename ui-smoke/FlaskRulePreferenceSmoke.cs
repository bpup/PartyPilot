using PartyPilot.Desktop;

internal static class FlaskRulePreferenceSmoke
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "partypilot-flask-rules-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FlaskRulePreferenceStore(directory);
            var original = store.Load(2);
            if (!original.Success || original.RuntimeFile is not null ||
                original.Players[0] != FlaskRulePreferenceStore.DefaultFor(1) ||
                original.Players[1] != FlaskRulePreferenceStore.DefaultFor(2))
                throw new Exception("New flask rules did not use the target reset profiles.");
            var selected = original.Players.ToArray();
            selected[1] = selected[1] with { LifeTrigger = 60, LifeCooldownMs = 500 };
            var saved = store.Save(2, selected);
            if (!saved.Success || saved.RuntimeFile is null ||
                store.Load(2).Players[1] != selected[1])
                throw new Exception("Per-player flask rules did not survive reload.");
            if (store.Save(2, [selected[0]]).Success ||
                store.Save(2, [selected[0], selected[0]]).Success)
                throw new Exception("Partial or duplicate player rules were accepted.");
            File.WriteAllText(saved.RuntimeFile, "{\"players\":[{\"player\":1}]}");
            if (store.Load(2).Success)
                throw new Exception("Incomplete player rules were silently defaulted.");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
