using System.Text.Json;

namespace PartyPilot.Desktop;

public sealed record LaunchedClient(int Pid, long WindowHandle, string Executable);

public static class LaunchedClients
{
    // Matches cmd/clientlaunch's single JSON result; a preflight or partial launch is not a binding.
    public static IReadOnlyList<LaunchedClient> ParseSuccessfulLaunch(string output, int expectedCount)
    {
        if (expectedCount is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(expectedCount));
        using var json = JsonDocument.Parse(output);
        var root = json.RootElement;
        var hasError = root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("error", out var error) &&
            (error.ValueKind != JsonValueKind.String || !string.IsNullOrEmpty(error.GetString()));
        if (root.ValueKind != JsonValueKind.Object ||
            hasError ||
            !root.TryGetProperty("checked_executables", out var checkedPaths) ||
            checkedPaths.ValueKind != JsonValueKind.Array || checkedPaths.GetArrayLength() != expectedCount ||
            !root.TryGetProperty("clients", out var clients) ||
            clients.ValueKind != JsonValueKind.Array || clients.GetArrayLength() != expectedCount)
        {
            throw new InvalidOperationException("Launcher did not return a complete client binding.");
        }
        var result = new List<LaunchedClient>(expectedCount);
        for (var index = 0; index < expectedCount; index++)
        {
            var entry = clients[index];
            var expectedPath = checkedPaths[index].GetString();
            if (string.IsNullOrWhiteSpace(expectedPath) ||
                !entry.TryGetProperty("executable", out var executable) ||
                executable.GetString() != expectedPath ||
                !entry.TryGetProperty("pid", out var pidElement) ||
                !pidElement.TryGetInt32(out var pid) || pid <= 0 ||
                !entry.TryGetProperty("window", out var window) ||
                !window.TryGetProperty("pid", out var windowPid) ||
                !windowPid.TryGetInt32(out var owner) || owner != pid ||
                !window.TryGetProperty("handle", out var handleElement) ||
                !handleElement.TryGetInt64(out var handle) || handle <= 0)
            {
                throw new InvalidOperationException($"Launcher did not identify a unique window for client {index + 1}.");
            }
            result.Add(new LaunchedClient(pid, handle, expectedPath));
        }
        if (result.Select(client => client.Pid).Distinct().Count() != expectedCount ||
            result.Select(client => client.WindowHandle).Distinct().Count() != expectedCount)
        {
            throw new InvalidOperationException("Launcher returned duplicate game processes or windows.");
        }
        return result;
    }
}
