namespace PartyPilot.Desktop;

public static class NamePrivacy
{
    private static bool _enabled;

    public static bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            Changed?.Invoke();
        }
    }

    public static event Action? Changed;

    public static string Mask(string? name) => !_enabled
        ? name ?? string.Empty
        : string.IsNullOrWhiteSpace(name) ? string.Empty : "hidden";

    public static string MaskIdentity(int slot, string? name)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(name)) return name ?? string.Empty;
        var value = name.Trim();
        return value.Equals($"P{slot}", StringComparison.OrdinalIgnoreCase) ||
               value.Equals($"Player {slot}", StringComparison.OrdinalIgnoreCase)
            ? value : "hidden";
    }
}
