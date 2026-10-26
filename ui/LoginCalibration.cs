using System.Text.Json;

namespace PartyPilot.Desktop;

internal sealed record LoginCalibration(double UsernameX, double UsernameY, double PasswordX,
    double PasswordY, int Width, int Height, string LayoutKey)
{
    internal static bool TryCreate(double usernameX, double usernameY, double passwordX,
        double passwordY, int width, int height, string? layoutKey, out LoginCalibration? calibration)
    {
        calibration = null;
        if (width < 320 || height < 240 || string.IsNullOrWhiteSpace(layoutKey) ||
            !SafePoint(usernameX, usernameY) || !SafePoint(passwordX, passwordY) ||
            passwordY - usernameY < 0.015) return false;
        calibration = new(usernameX, usernameY, passwordX, passwordY, width, height, layoutKey);
        return true;
    }

    internal bool Matches(int width, int height, string? layoutKey) =>
        TryCreate(UsernameX, UsernameY, PasswordX, PasswordY, Width, Height, LayoutKey, out _) &&
        width >= 320 && height >= 240 && string.Equals(LayoutKey, layoutKey, StringComparison.Ordinal) &&
        Math.Abs((double)width / height / ((double)Width / Height) - 1) <= 0.04;

    private static bool SafePoint(double x, double y) =>
        double.IsFinite(x) && double.IsFinite(y) && x is >= 0.01 and <= 0.99 &&
        y is >= 0.01 and <= 0.99;
}

internal sealed record LoginCalibrationResult(bool Success, LoginCalibration? Calibration, string Message);

internal sealed class LoginCalibrationStore
{
    private readonly string _directory;

    internal LoginCalibrationStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PartyPilot", "LoginCalibration");
    }

    internal LoginCalibrationResult Load(int client)
    {
        var path = PathFor(client);
        try
        {
            var calibration = JsonSerializer.Deserialize<LoginCalibration>(File.ReadAllBytes(path));
            return calibration is not null &&
                   LoginCalibration.TryCreate(calibration.UsernameX, calibration.UsernameY,
                       calibration.PasswordX, calibration.PasswordY, calibration.Width,
                       calibration.Height, calibration.LayoutKey, out _)
                ? new(true, calibration, "登录字段标记已保存；使用前仍须核对当前窗口布局。")
                : new(false, null, "登录字段标记文件无效，请重新标记。");
        }
        catch (FileNotFoundException) { return new(true, null, "尚未标记登录字段。"); }
        catch (DirectoryNotFoundException) { return new(true, null, "尚未标记登录字段。"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(false, null, "登录字段标记无法读取，请重新标记。");
        }
    }

    internal LoginCalibrationResult Save(int client, LoginCalibration calibration)
    {
        var path = PathFor(client);
        if (!LoginCalibration.TryCreate(calibration.UsernameX, calibration.UsernameY,
                calibration.PasswordX, calibration.PasswordY, calibration.Width, calibration.Height,
                calibration.LayoutKey, out _))
            return new(false, null, "登录字段位置或窗口尺寸无效。");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(calibration));
            File.Move(temporary, path, overwrite: true);
            return new(true, calibration, "登录字段位置已保存；自动登录尚未接入。");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new(false, null, "登录字段位置保存失败。");
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal bool Delete(int client)
    {
        try
        {
            File.Delete(PathFor(client));
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    private string PathFor(int client)
    {
        if (client is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(client));
        return Path.Combine(_directory, $"client-{client}.json");
    }
}
