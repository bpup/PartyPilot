using System.Text.Json;
using PartyPilot.Common;

namespace PartyPilot.Desktop;

internal sealed class HybridBindingPreferenceStore
{
    private readonly string _path;

    internal HybridBindingPreferenceStore(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PartyPilot", "hybrid-bindings.json");

    internal (bool Success, HybridBindingConfig Bindings) Load()
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(_path));
            if (json.RootElement.ValueKind != JsonValueKind.Object ||
                !json.RootElement.TryGetProperty("keys", out var keys) ||
                keys.ValueKind != JsonValueKind.String ||
                !HybridBindingConfig.TryParse(keys.GetString(), out var baseBindings) ||
                baseBindings is null || baseBindings.CustomActions.Count != 0)
                return (false, HybridBindingConfig.Default);
            var custom = json.RootElement.TryGetProperty("custom", out var customValue)
                ? customValue.Deserialize<HybridCustomAction[]>() : [];
            if (custom is null || !HybridBindingConfig.TryCreate(
                    Enumerable.Range(0, HybridBindingConfig.Definitions.Length)
                        .Select(baseBindings.KeyAt).ToArray(), custom, out var bindings) || bindings is null)
                return (false, HybridBindingConfig.Default);
            return (true, bindings);
        }
        catch (FileNotFoundException) { return (true, HybridBindingConfig.Default); }
        catch (DirectoryNotFoundException) { return (true, HybridBindingConfig.Default); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { return (false, HybridBindingConfig.Default); }
    }

    internal bool Save(HybridBindingConfig bindings)
    {
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(new
            {
                keys = string.Join(',', Enumerable.Range(0, HybridBindingConfig.Definitions.Length)
                    .Select(bindings.KeyAt)),
                custom = bindings.CustomActions
            }));
            File.Move(temporary, _path, overwrite: true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return false; }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
