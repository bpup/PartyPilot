using System.Diagnostics;
using System.Text.Json;

namespace PartyPilot.Desktop;

public sealed record FpsConfigPlan(string ConfigPath, string Foreground, string Background)
{
    public bool ConflictsWith(FpsConfigPlan other) =>
        string.Equals(ConfigPath, other.ConfigPath, StringComparison.OrdinalIgnoreCase) &&
        (Foreground != other.Foreground || Background != other.Background);

    public static bool TryCreate(string? path, string? foreground, string? background,
        out FpsConfigPlan? plan)
    {
        plan = null;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
            !TryMode(foreground, out var front) || !TryMode(background, out var back)) return false;
        try
        {
            plan = new FpsConfigPlan(Path.GetFullPath(path), front, back);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool TryMode(string? text, out string value)
    {
        value = text?.Trim().ToLowerInvariant() ?? "";
        if (value == "0") value = "unlimited";
        if (value is "keep" or "unlimited") return true;
        return int.TryParse(value, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var fps) && fps > 0;
    }
}

public sealed record FpsConfigOutcome(bool Applied, IReadOnlyList<string> ChangedKeys, string? Backup);

public static class FpsConfigTool
{
    public static ProcessStartInfo StartInfo(string executable, FpsConfigPlan plan, bool apply)
    {
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in new[] { "-config", plan.ConfigPath, "-foreground", plan.Foreground,
            "-background", plan.Background }) info.ArgumentList.Add(argument);
        if (apply) info.ArgumentList.Add("-apply");
        return info;
    }

    public static FpsConfigOutcome Parse(string json, bool requestedApply, string expectedPath)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("path", out var pathValue) || pathValue.GetString() != expectedPath ||
            !root.TryGetProperty("applied", out var appliedValue) ||
            appliedValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            !root.TryGetProperty("changed_keys", out var changedValue) ||
            changedValue.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("FPS tool returned an incomplete result.");
        var applied = appliedValue.GetBoolean();
        var keys = new List<string>();
        foreach (var item in changedValue.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not string key ||
                key is not ("DISPLAY.framerate_limit_enabled" or "DISPLAY.framerate_limit" or
                    "DISPLAY.background_framerate_limit_enabled" or "DISPLAY.background_framerate_limit") ||
                keys.Contains(key))
                throw new InvalidDataException("FPS tool returned an unknown or duplicate key.");
            keys.Add(key);
        }
        string? backup = null;
        if (root.TryGetProperty("backup", out var backupValue))
        {
            if (backupValue.ValueKind != JsonValueKind.String) throw new InvalidDataException("Invalid backup path.");
            backup = backupValue.GetString();
        }
        if (applied && (!requestedApply || keys.Count == 0 || string.IsNullOrWhiteSpace(backup)) ||
            !applied && backup != null ||
            requestedApply && keys.Count > 0 && !applied)
            throw new InvalidDataException("FPS tool returned an inconsistent apply result.");
        return new FpsConfigOutcome(applied, keys, backup);
    }

    public static async Task<FpsConfigOutcome> RunAsync(string executable, FpsConfigPlan plan, bool apply)
    {
        using var process = Process.Start(StartInfo(executable, plan, apply)) ??
            throw new InvalidOperationException("FPS tool did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        await Task.WhenAll(output, error).ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error.Result)
                ? $"FPS tool exited with code {process.ExitCode}."
                : error.Result.Trim());
        return Parse(output.Result, apply, plan.ConfigPath);
    }
}
