using PartyPilot.Desktop;

internal static class FlaskSupportPreferenceSmoke
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "partypilot-drinker-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FlaskSupportPreferenceStore(directory);
            if (store.Load(2) is not { Success: true, RuntimeFile: null })
                throw new Exception("New Drinker settings did not start disabled.");
            var binding = FlaskSupportPreferenceStore.Default(2, 1) with
            {
                SupportBuild = true,
                Monitor = new FlaskSupportMonitor(true, 1, "Life AND Mana", 50, "Both", false)
            };
            var saved = store.Save(2, [binding]);
            if (!saved.Success || saved.RuntimeFile is null || !File.Exists(saved.RuntimeFile) ||
                store.Load(2).Bindings.Single() != binding)
                throw new Exception("Drinker settings did not survive reload in the live wire format.");
            if (store.Save(2, [binding, binding]).Success ||
                store.Save(2, [binding with { Monitor = binding.Monitor with { TargetPlayer = 3 } }]).Success)
                throw new Exception("Duplicate or unbound Drinker targets were accepted.");
            var noManaOnly = FlaskSupportPreferenceStore.Default(2, 1) with { BuildHasNoMana = true };
            if (store.Save(2, [noManaOnly]).RuntimeFile is null ||
                store.Load(2).RuntimeFile is null)
                throw new Exception("No-mana Drinker setting did not reach the live process file.");
            saved = store.Save(2, [binding]);
            File.WriteAllText(saved.RuntimeFile!,
                "{\"bindings\":[{\"drinker_player\":2,\"support_build\":true," +
                "\"monitor\":{\"enabled\":true,\"target_player\":1}}]}");
            if (store.Load(2).Success)
                throw new Exception("Incomplete Drinker settings were silently defaulted.");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
