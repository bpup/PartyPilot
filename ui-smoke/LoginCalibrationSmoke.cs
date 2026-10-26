using PartyPilot.Desktop;

internal static class LoginCalibrationSmoke
{
    internal static void Run()
    {
        if (!LoginCalibration.TryCreate(0.42, 0.61, 0.43, 0.69,
                1685, 1263, "three-client", out var calibration) || calibration is null ||
            !calibration.Matches(1600, 1200, "three-client") ||
            calibration.Matches(1600, 900, "three-client") ||
            calibration.Matches(1685, 1263, "other-layout") ||
            LoginCalibration.TryCreate(0.42, 0.61, 0.43, 0.62,
                1685, 1263, "three-client", out _) ||
            LoginCalibration.TryCreate(0, 0.61, 0.43, 0.69,
                1685, 1263, "three-client", out _))
            throw new Exception("Login marks accepted unsafe points or a changed viewport.");

        var directory = Path.Combine(Path.GetTempPath(), "partypilot-login-mark-smoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LoginCalibrationStore(directory);
            if (store.Load(1) is not { Success: true, Calibration: null } ||
                !store.Save(1, calibration).Success || store.Load(1).Calibration != calibration)
                throw new Exception("Confirmed login marks did not survive a local roundtrip.");
            File.WriteAllText(Path.Combine(directory, "client-1.json"), "{}");
            if (store.Load(1).Success || !store.Delete(1) ||
                store.Load(1) is not { Success: true, Calibration: null })
                throw new Exception("Invalid or cleared login marks were treated as confirmed.");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
