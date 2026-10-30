using PartyPilot.Desktop;

internal static class FlaskCustomPreferenceSmoke
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "partypilot-flask-custom-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FlaskCustomPreferenceStore(directory);
            var initial = store.Load(2);
            if (!initial.Success || initial.RuntimeFile is not null || initial.Rules is not [{
                    Enabled: true, Player: "Both", Resource: "Life", Comparator: "≤", Threshold: 45,
                    CooldownSeconds: 3, Priority: 50, MinDeficit: 300, Dependency: "None",
                    CombatOnly: true, Input: "D-pad Left"
                }])
                throw new Exception("New custom recovery rules did not use the target default rule.");
            var runtime = store.EnsureRuntimeFile(2);
            if (!runtime.Success || runtime.RuntimeFile is null || !File.Exists(runtime.RuntimeFile) ||
                store.Load(2).Rules.Single() != runtime.Rules.Single())
                throw new Exception("Default custom recovery rule did not become a valid runtime file.");
            var extra = FlaskCustomPreferenceStore.DefaultRule() with
                { Player = "P2", Resource = "Energy Shield", Input = "X", MinDeficit = 0 };
            if (!store.Save(2, [runtime.Rules.Single(), extra]).Success ||
                store.Load(2).Rules.Last() != extra)
                throw new Exception("Multiple custom recovery rules did not survive reload in order.");
            if (store.Save(2, [extra, extra]).Success ||
                store.Save(2, [extra with { Threshold = 101 }]).Success ||
                store.Save(2, [extra with { Player = "P3" }]).Success)
                throw new Exception("Invalid custom recovery rules were accepted.");
            File.WriteAllText(runtime.RuntimeFile, "{\"rules\":[{\"id\":\"incomplete\"}]}");
            if (store.Load(2).Success || store.EnsureRuntimeFile(2).Success)
                throw new Exception("Incomplete custom rules were silently defaulted.");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
