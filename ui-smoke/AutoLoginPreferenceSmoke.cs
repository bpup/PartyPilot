using PartyPilot.Desktop;

internal static class AutoLoginPreferenceSmoke
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "partypilot-auto-login-smoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new AutoLoginPreferenceStore(directory);
            if (store.Load(2) is not { Success: true, Enabled: false } ||
                !store.Save(2, true).Success ||
                store.Load(2) is not { Success: true, Enabled: true } ||
                store.Load(1) is not { Success: true, Enabled: false })
                throw new Exception("Per-client automatic login default or saved choice changed.");
            File.WriteAllText(Path.Combine(directory, "client-2.json"), "{}");
            if (store.Load(2).Success || !store.Save(2, false).Success ||
                store.Load(2) is not { Success: true, Enabled: false })
                throw new Exception("Invalid automatic login choice was silently enabled.");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
