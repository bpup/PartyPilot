namespace PartyPilot.Desktop;

// M4–M16 follow the target version's XBUTTON high-word translation. Availability
// beyond M5 depends on whether the mouse driver emits those button numbers.
internal static class AutoPartyKeyBinding
{
    internal static readonly IReadOnlyList<string> Options = BuildOptions();

    private static IReadOnlyList<string> BuildOptions()
    {
        var options = new List<string> { "None" };
        options.AddRange(Enumerable.Range('A', 26).Select(value => ((char)value).ToString()));
        options.AddRange(Enumerable.Range(0, 10).Select(value => value.ToString()));
        options.AddRange(["Up Arrow", "Down Arrow", "Left Arrow", "Right Arrow", "Insert",
            "Delete", "Home", "End", "Page Up", "Page Down"]);
        options.AddRange(Enumerable.Range(0, 10).Select(value => $"Numpad {value}"));
        options.AddRange(["Numpad +", "Numpad -", "Numpad *", "Numpad /", "Numpad .",
            "Minus", "Plus", "[", "]", ";", "'", "\\", "/", ",", ".", "`",
            "Enter", "Backspace", "Space", "Tab", "Caps Lock", "Scroll Lock", "Pause",
            "Left Shift", "Right Shift", "Left Ctrl", "Right Ctrl", "Left Alt", "Right Alt"]);
        options.AddRange(Enumerable.Range(1, 24).Select(value => $"F{value}"));
        options.AddRange(Enumerable.Range(2, 15).Select(value => $"M{value}"));
        return options;
    }

    internal static string? Normalize(string? value) =>
        Options.FirstOrDefault(option => option.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase));

    internal static int VirtualKey(string value)
    {
        if (value == "None") return 0;
        if (value.StartsWith('M') && int.TryParse(value.AsSpan(1), out var mouseButton) &&
            mouseButton is >= 2 and <= 16)
            return 65536 + mouseButton;
        if (value.Length == 1 && char.IsAsciiLetterOrDigit(value[0]))
            return char.ToUpperInvariant(value[0]);
        if (value.StartsWith('F') && int.TryParse(value.AsSpan(1), out var function) &&
            function is >= 1 and <= 24) return 111 + function;
        if (value.StartsWith("Numpad ", StringComparison.Ordinal) && value.Length == 8 &&
            char.IsAsciiDigit(value[7])) return 96 + value[7] - '0';
        return value switch
        {
            "Up Arrow" => 38, "Down Arrow" => 40, "Left Arrow" => 37, "Right Arrow" => 39,
            "Insert" => 45, "Delete" => 46, "Home" => 36, "End" => 35,
            "Page Up" => 33, "Page Down" => 34,
            "Numpad *" => 106, "Numpad +" => 107, "Numpad -" => 109,
            "Numpad ." => 110, "Numpad /" => 111,
            "Minus" => 189, "Plus" => 187, "[" => 219, "]" => 221,
            ";" => 186, "'" => 222, "\\" => 220, "/" => 191,
            "," => 188, "." => 190, "`" => 192,
            "Enter" => 13, "Backspace" => 8, "Space" => 32, "Tab" => 9,
            "Caps Lock" => 20, "Scroll Lock" => 145, "Pause" => 19,
            "Left Shift" => 160, "Right Shift" => 161, "Left Ctrl" => 162,
            "Right Ctrl" => 163, "Left Alt" => 164, "Right Alt" => 165,
            _ => 0
        };
    }
}
