using System.Text.Json;

namespace PartyPilot.Desktop;

// The supplied 0.6.17 radar accepts a keyboard key with Ctrl/Alt/Shift.
internal readonly record struct RadarHotkeyBinding(string DisplayText, int VirtualKey,
    bool Control, bool Alt, bool Shift)
{
    private static readonly IReadOnlyDictionary<string, (string Name, int Key)> Named =
        new Dictionary<string, (string, int)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Tab"] = ("Tab", 9), ["Back"] = ("Backspace", 8),
            ["Backspace"] = ("Backspace", 8), ["Return"] = ("Enter", 13),
            ["Enter"] = ("Enter", 13), ["Escape"] = ("Escape", 27),
            ["Pause"] = ("Pause", 19), ["CapsLock"] = ("Caps Lock", 20),
            ["Capital"] = ("Caps Lock", 20),
            ["Space"] = ("Space", 32), ["PageUp"] = ("Page Up", 33),
            ["Prior"] = ("Page Up", 33), ["PageDown"] = ("Page Down", 34),
            ["Next"] = ("Page Down", 34), ["End"] = ("End", 35),
            ["Home"] = ("Home", 36), ["LeftArrow"] = ("Left Arrow", 37),
            ["UpArrow"] = ("Up Arrow", 38), ["RightArrow"] = ("Right Arrow", 39),
            ["DownArrow"] = ("Down Arrow", 40), ["Left"] = ("Left Arrow", 37),
            ["Up"] = ("Up Arrow", 38), ["Right"] = ("Right Arrow", 39),
            ["Down"] = ("Down Arrow", 40), ["PrintScreen"] = ("Print Screen", 44),
            ["Print"] = ("Print Screen", 44), ["Snapshot"] = ("Print Screen", 44),
            ["Insert"] = ("Insert", 45), ["Delete"] = ("Delete", 46),
            ["Numpad*"] = ("Numpad *", 106), ["Numpad+"] = ("Numpad +", 107),
            ["Numpad-"] = ("Numpad -", 109), ["Numpad/"] = ("Numpad /", 111),
            ["Numpad."] = ("Numpad .", 110), ["NumLock"] = ("Num Lock", 144),
            ["Multiply"] = ("Numpad *", 106), ["Add"] = ("Numpad +", 107),
            ["Subtract"] = ("Numpad -", 109), ["Divide"] = ("Numpad /", 111),
            ["Decimal"] = ("Numpad .", 110),
            ["ScrollLock"] = ("Scroll Lock", 145), ["Scroll"] = ("Scroll Lock", 145),
            [";"] = (";", 186), ["OemSemicolon"] = (";", 186), ["Oem1"] = (";", 186),
            ["="] = ("=", 187), [","] = (",", 188), ["-"] = ("-", 189),
            ["OemPlus"] = ("=", 187), ["OemComma"] = (",", 188),
            ["OemMinus"] = ("-", 189),
            ["."] = (".", 190), ["/"] = ("/", 191), ["`"] = ("`", 192),
            ["OemPeriod"] = (".", 190), ["OemQuestion"] = ("/", 191),
            ["Oem2"] = ("/", 191), ["OemTilde"] = ("`", 192),
            ["Oem3"] = ("`", 192),
            ["["] = ("[", 219), ["\\"] = ("\\", 220), ["]"] = ("]", 221),
            ["OemOpenBrackets"] = ("[", 219), ["Oem4"] = ("[", 219),
            ["OemPipe"] = ("\\", 220), ["Oem5"] = ("\\", 220),
            ["OemBackslash"] = ("\\", 220), ["Oem102"] = ("\\", 220),
            ["OemCloseBrackets"] = ("]", 221), ["Oem6"] = ("]", 221),
            ["'"] = ("'", 222), ["OemQuotes"] = ("'", 222), ["Oem7"] = ("'", 222)
        };

    internal static bool TryParse(string? value, out RadarHotkeyBinding binding)
    {
        binding = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parseValue = value.Trim();
        if (parseValue.EndsWith("Numpad +", StringComparison.OrdinalIgnoreCase))
            parseValue = parseValue[..^"Numpad +".Length] + "Add";
        var parts = parseValue.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        var ctrl = false; var alt = false; var shift = false;
        string? keyName = null;
        foreach (var part in parts)
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Control", StringComparison.OrdinalIgnoreCase)) ctrl = true;
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) alt = true;
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) shift = true;
            else if (keyName == null) keyName = part;
            else return false;
        }
        if (keyName == null || !TryKey(keyName, out var key)) return false;
        var display = string.Join('+', new[] { ctrl ? "Ctrl" : null, alt ? "Alt" : null,
            shift ? "Shift" : null, key.Name }.Where(part => part != null));
        binding = new RadarHotkeyBinding(display, key.Key, ctrl, alt, shift);
        return true;
    }

    private static bool TryKey(string name, out (string Name, int Key) key)
    {
        key = default;
        if (name.Length == 1 && char.IsAsciiLetterOrDigit(name[0]))
        {
            var letter = char.ToUpperInvariant(name[0]);
            key = (letter.ToString(), letter);
            return true;
        }
        if (name.Length == 2 && (name[0] is 'D' or 'd') && char.IsAsciiDigit(name[1]))
        {
            key = (name[1].ToString(), name[1]);
            return true;
        }
        if (name.StartsWith("F", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(name.AsSpan(1), out var function) &&
            function is >= 1 and <= 24)
        {
            key = ($"F{function}", 111 + function);
            return true;
        }
        var numpadNumber = name.StartsWith("Numpad ", StringComparison.OrdinalIgnoreCase)
            ? name.AsSpan(7) : name.StartsWith("Numpad", StringComparison.OrdinalIgnoreCase)
                ? name.AsSpan(6) : ReadOnlySpan<char>.Empty;
        if (!numpadNumber.IsEmpty && int.TryParse(numpadNumber, out var digit) && digit is >= 0 and <= 9)
        {
            key = ($"Numpad {digit}", 96 + digit);
            return true;
        }
        return Named.TryGetValue(name.Replace(" ", ""), out key);
    }

    internal bool IsDown(Func<int, bool> pressed) => pressed(VirtualKey) &&
        pressed(0x11) == Control && pressed(0x12) == Alt && pressed(0x10) == Shift;
}

internal sealed class RadarHotkeyPreferenceStore
{
    private readonly string _path;

    internal RadarHotkeyPreferenceStore(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PartyPilot", "radar-hotkey.json");

    internal (bool Success, RadarHotkeyBinding Binding) Load()
    {
        RadarHotkeyBinding.TryParse("Tab", out var fallback);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(_path));
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("Key", out var value) ||
                value.ValueKind != JsonValueKind.String ||
                !RadarHotkeyBinding.TryParse(value.GetString(), out var binding))
                return (false, fallback);
            return (true, binding);
        }
        catch (FileNotFoundException) { return (true, fallback); }
        catch (DirectoryNotFoundException) { return (true, fallback); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { return (false, fallback); }
    }

    internal bool Save(RadarHotkeyBinding binding)
    {
        if (!RadarHotkeyBinding.TryParse(binding.DisplayText, out var verified) || verified != binding)
            return false;
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(new { Key = binding.DisplayText }));
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
