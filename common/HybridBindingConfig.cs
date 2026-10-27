namespace PartyPilot.Common;

internal readonly record struct HybridBindingDefinition(string Id, string Label, int DefaultKey);
internal readonly record struct HybridCustomAction(string Label, string Description,
    int Key, int ActionIndex, bool BlockDuringMenu = false);

internal sealed class HybridBindingConfig
{
    // Order and defaults follow the supplied 0.6.17 HybridKeyboardBinding.Definitions.
    internal static readonly HybridBindingDefinition[] Definitions =
    [
        new("focusP1", "P1 MAIN", 112), new("focusP2", "P2 聚焦", 113),
        new("focusP3", "P3 聚焦", 114), new("focusP4", "P4 聚焦", 115),
        new("focusP5", "P5 聚焦", 116), new("focusP6", "P6 聚焦", 117),
        new("moveUp", "上移", 87), new("moveLeft", "左移", 65),
        new("moveDown", "下移", 83), new("moveRight", "右移", 68),
        new("precision", "精确移动", 164), new("confirm", "A · 交互/确认", 69),
        new("cancel", "B · 返回", 81), new("cancelAlternate", "B · 闪避/冲刺", 32),
        new("buttonX", "X", 49), new("buttonY", "Y", 50),
        new("leftBumper", "LB", 51), new("rightBumper", "RB", 52),
        new("leftTrigger", "LT", 160), new("rightTrigger", "RT", 65538),
        new("dpadUp", "十字键上", 38), new("dpadDown", "十字键下", 40),
        new("dpadLeft", "十字键左", 37), new("dpadRight", "十字键右", 39),
        new("start", "Start · 菜单", 13), new("view", "View · 选择", 8)
    ];

    private readonly int[] _keys;
    private readonly HybridCustomAction[] _customActions;
    private readonly int[] _menuBlockedBaseActions;
    internal static HybridBindingConfig Default { get; } = new(
        Definitions.Select(item => item.DefaultKey).ToArray(), [], [19]);

    private HybridBindingConfig(int[] keys, HybridCustomAction[] customActions,
        int[] menuBlockedBaseActions)
    {
        _keys = keys;
        _customActions = customActions;
        _menuBlockedBaseActions = menuBlockedBaseActions;
    }

    internal int KeyAt(int index) => _keys[index];
    internal IReadOnlyList<HybridCustomAction> CustomActions => _customActions;
    internal IReadOnlyList<int> MenuBlockedBaseActions => _menuBlockedBaseActions;
    internal IReadOnlyList<int> MenuBlockedActions => _menuBlockedBaseActions
        .Concat(_customActions.Where(action => action.BlockDuringMenu)
            .Select(action => action.ActionIndex)).Distinct().ToArray();
    internal int FocusForKey(int key)
    {
        for (var index = 0; index < 6; index++)
            if (Matches(_keys[index], key)) return index + 1;
        return 0;
    }
    internal bool ContainsKey(int key) => _keys.Any(mapped => Matches(mapped, key)) ||
        _customActions.Any(action => Matches(action.Key, key));
    internal static bool Matches(int mapped, int key) => mapped != 0 &&
        (mapped == key || mapped == 160 && key == 16 ||
         mapped == 162 && key == 17 || mapped == 164 && key == 18);
    internal string Encode()
    {
        var baseKeys = string.Join(',', _keys);
        var custom = string.Join(',', _customActions.Select(action =>
            $"{action.Key}:{action.ActionIndex}" + (action.BlockDuringMenu ? ":1" : "")));
        var standardMenu = _menuBlockedBaseActions.SequenceEqual([19]);
        return baseKeys + (custom.Length == 0 && standardMenu ? "" : "|" + custom) +
            (standardMenu ? "" : "|" + string.Join(',', _menuBlockedBaseActions));
    }

    internal static bool TryCreate(IReadOnlyList<int> keys, out HybridBindingConfig? config) =>
        TryCreate(keys, [], out config);

    internal static bool TryCreate(IReadOnlyList<int> keys,
        IReadOnlyList<HybridCustomAction> customActions, out HybridBindingConfig? config)
        => TryCreate(keys, customActions, [19], out config);

    internal static bool TryCreate(IReadOnlyList<int> keys,
        IReadOnlyList<HybridCustomAction> customActions,
        IReadOnlyList<int> menuBlockedBaseActions, out HybridBindingConfig? config)
    {
        config = null;
        if (keys.Count != Definitions.Length || keys.Any(key => !AllowedKey(key)) ||
            customActions.Any(action => !AllowedKey(action.Key) ||
                action.ActionIndex is < 6 or >= 26 || action.Label is null ||
                action.Description is null) ||
            menuBlockedBaseActions.Any(index => index is < 6 or >= 26) ||
            menuBlockedBaseActions.Distinct().Count() != menuBlockedBaseActions.Count) return false;
        var assigned = keys.Where(key => key != 0).ToArray();
        if (assigned.Distinct().Count() != assigned.Length) return false;
        config = new HybridBindingConfig(keys.ToArray(), customActions.ToArray(),
            menuBlockedBaseActions.ToArray());
        return true;
    }

    internal static bool TryParse(string? encoded, out HybridBindingConfig? config)
    {
        config = null;
        if (encoded is null) return false;
        var sections = encoded.Split('|');
        if (sections.Length is < 1 or > 3) return false;
        var parts = sections[0].Split(',');
        if (parts.Length != Definitions.Length) return false;
        var keys = new int[parts.Length];
        for (var index = 0; index < parts.Length; index++)
            if (!int.TryParse(parts[index], out keys[index])) return false;
        var custom = new List<HybridCustomAction>();
        if (sections.Length >= 2 && sections[1].Length != 0)
        {
            foreach (var entry in sections[1].Split(','))
            {
                var pair = entry.Split(':');
                if (pair.Length is < 2 or > 3 || !int.TryParse(pair[0], out var key) ||
                    !int.TryParse(pair[1], out var actionIndex)) return false;
                if (pair.Length == 3 && pair[2] != "1") return false;
                custom.Add(new HybridCustomAction("", "", key, actionIndex,
                    pair.Length == 3));
            }
        }
        if (sections.Length == 2 && sections[1].Length == 0) return false;
        var blocked = new List<int>();
        if (sections.Length == 3 && sections[2].Length != 0)
            foreach (var entry in sections[2].Split(','))
            {
                if (!int.TryParse(entry, out var index)) return false;
                blocked.Add(index);
            }
        return TryCreate(keys, custom, sections.Length == 3 ? blocked : [19], out config);
    }

    private static bool AllowedKey(int key) => key == 0 ||
        key is >= 65 and <= 90 or >= 48 and <= 57 or >= 96 and <= 111 or
            >= 112 and <= 135 or >= 65538 and <= 65552 or
            8 or 9 or 13 or 19 or 20 or 32 or 33 or 34 or 35 or 36 or 37 or 38 or 39 or 40 or
            45 or 46 or 145 or 160 or 161 or 162 or 163 or 164 or 165 or
            186 or 187 or 188 or 189 or 190 or 191 or 192 or 219 or 220 or 221 or 222;
}
