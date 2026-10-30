using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PartyPilot.Desktop;

internal sealed class FlaskCustomDialog : Window
{
    private readonly int _playerCount;
    private readonly List<FlaskCustomRule> _rules;
    private readonly ComboBox _selected = new() { Width = 170 };
    private readonly ComboBox _player = new() { Width = 170 };
    private readonly ComboBox _resource = new() { Width = 170 };
    private readonly ComboBox _comparator = new() { Width = 170 };
    private readonly ComboBox _dependency = new() { Width = 170 };
    private readonly ComboBox _input = new() { Width = 170 };
    private readonly CheckBox _enabled = new() { Content = "启用" };
    private readonly CheckBox _combatOnly = new() { Content = "仅战斗中触发" };
    private readonly TextBox _threshold = Field("custom-threshold");
    private readonly TextBox _cooldown = Field("custom-cooldown");
    private readonly TextBox _priority = Field("custom-priority");
    private readonly TextBox _minDeficit = Field("custom-min-deficit");
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private int _shown = -1;
    private bool _changing;

    internal FlaskCustomDialog(int playerCount, IReadOnlyList<FlaskCustomRule> current)
    {
        if (playerCount is not (2 or 3)) throw new ArgumentOutOfRangeException(nameof(playerCount));
        _playerCount = playerCount;
        var valid = FlaskCustomPreferenceStore.Valid(current, playerCount);
        _rules = valid ? current.ToList() : [FlaskCustomPreferenceStore.DefaultRule()];
        Title = "自定义恢复规则";
        Width = 590;
        Height = 760;
        MinWidth = 480;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var rows = new StackPanel { Spacing = 10, Margin = new Thickness(18) };
        rows.Children.Add(new TextBlock { Text = "CUSTOM RECOVERY RULES", FontWeight = FontWeight.DemiBold });
        rows.Children.Add(new TextBlock { Text = "按列表顺序判断；每条规则有独立冷却。Ward 当前没有可靠的实时读数，涉及 Ward 的规则不会有游戏内验证。",
            TextWrapping = TextWrapping.Wrap });
        var picker = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        _selected.SelectionChanged += (_, _) => ChangeRule();
        picker.Children.Add(_selected);
        var add = new Button { Content = "新增规则" };
        add.Click += (_, _) => AddRule();
        picker.Children.Add(add);
        var remove = new Button { Content = "删除当前" };
        remove.Click += (_, _) => RemoveRule();
        picker.Children.Add(remove);
        rows.Children.Add(picker);
        rows.Children.Add(_enabled);
        _player.ItemsSource = playerCount == 2
            ? new[] { "Off", "P1", "P2", "Both" }
            : new[] { "Off", "P1", "P2", "P3", "Both" };
        _resource.ItemsSource = FlaskCustomPreferenceStore.Resources;
        _comparator.ItemsSource = FlaskCustomPreferenceStore.Comparators;
        _dependency.ItemsSource = FlaskCustomPreferenceStore.Dependencies;
        _input.ItemsSource = FlaskCustomPreferenceStore.Inputs;
        AddChoice(rows, "目标角色", _player);
        AddChoice(rows, "资源", _resource);
        AddChoice(rows, "比较符", _comparator);
        AddNumber(rows, "阈值（0–100%）", _threshold);
        AddNumber(rows, "冷却（0.25–60 秒）", _cooldown);
        AddNumber(rows, "优先级（0–100；目标版按列表顺序判断）", _priority);
        AddNumber(rows, "最低资源缺口（非负整数）", _minDeficit);
        AddChoice(rows, "依赖条件", _dependency);
        rows.Children.Add(_combatOnly);
        AddChoice(rows, "手柄输入", _input);
        rows.Children.Add(_status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消" };
        cancel.Click += (_, _) => Close(null);
        var save = new Button { Content = "保存" };
        save.Click += (_, _) => Save();
        actions.Children.Add(cancel);
        actions.Children.Add(save);
        rows.Children.Add(actions);
        Content = new ScrollViewer { Content = rows };
        Refresh(0);
        if (!valid) _status.Text = "原规则文件无效；当前显示目标版默认规则。保存会覆盖原文件。";
    }

    private static TextBox Field(string name) => new()
    {
        Name = name, Width = 130, HorizontalAlignment = HorizontalAlignment.Left
    };

    private static void AddChoice(StackPanel rows, string label, ComboBox choice)
    {
        rows.Children.Add(new TextBlock { Text = label });
        rows.Children.Add(choice);
    }

    private static void AddNumber(StackPanel rows, string label, TextBox number)
    {
        rows.Children.Add(new TextBlock { Text = label });
        rows.Children.Add(number);
    }

    private void Refresh(int selected)
    {
        _changing = true;
        _selected.ItemsSource = Enumerable.Range(1, _rules.Count).Select(index => $"规则 {index}").ToArray();
        _selected.SelectedIndex = _rules.Count == 0 ? -1 : Math.Clamp(selected, 0, _rules.Count - 1);
        Show(_selected.SelectedIndex);
        _changing = false;
    }

    private void ChangeRule()
    {
        if (_changing || _selected.SelectedIndex == _shown) return;
        var next = _selected.SelectedIndex;
        if (!Capture())
        {
            _changing = true;
            _selected.SelectedIndex = _shown;
            _changing = false;
            return;
        }
        Show(next);
    }

    private void Show(int index)
    {
        _shown = index;
        var hasRule = index >= 0 && index < _rules.Count;
        foreach (var field in new Control[] { _enabled, _player, _resource, _comparator,
                     _threshold, _cooldown, _priority, _minDeficit, _dependency, _combatOnly, _input })
            field.IsEnabled = hasRule;
        if (!hasRule) return;
        var rule = _rules[index];
        _enabled.IsChecked = rule.Enabled;
        _player.SelectedItem = rule.Player;
        _resource.SelectedItem = rule.Resource;
        _comparator.SelectedItem = rule.Comparator;
        _threshold.Text = rule.Threshold.ToString("R", CultureInfo.InvariantCulture);
        _cooldown.Text = rule.CooldownSeconds.ToString("R", CultureInfo.InvariantCulture);
        _priority.Text = rule.Priority.ToString(CultureInfo.InvariantCulture);
        _minDeficit.Text = rule.MinDeficit.ToString(CultureInfo.InvariantCulture);
        _dependency.SelectedItem = rule.Dependency;
        _combatOnly.IsChecked = rule.CombatOnly;
        _input.SelectedItem = rule.Input;
        _status.Text = "";
    }

    private bool Capture()
    {
        if (_shown < 0 || _shown >= _rules.Count) return true;
        if (_player.SelectedItem is not string player || _resource.SelectedItem is not string resource ||
            _comparator.SelectedItem is not string comparator || _dependency.SelectedItem is not string dependency ||
            _input.SelectedItem is not string input ||
            !double.TryParse(_threshold.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold) ||
            !double.TryParse(_cooldown.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var cooldown) ||
            !int.TryParse(_priority.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var priority) ||
            !int.TryParse(_minDeficit.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var minDeficit))
        {
            _status.Text = "请检查当前规则的数值和选项。";
            return false;
        }
        var updated = _rules[_shown] with
        {
            Enabled = _enabled.IsChecked == true, Player = player, Resource = resource,
            Comparator = comparator, Threshold = threshold, CooldownSeconds = cooldown,
            Priority = priority, MinDeficit = minDeficit, Dependency = dependency,
            CombatOnly = _combatOnly.IsChecked == true, Input = input
        };
        var previous = _rules[_shown];
        _rules[_shown] = updated;
        if (!FlaskCustomPreferenceStore.Valid(_rules, _playerCount))
        {
            _rules[_shown] = previous;
            _status.Text = "当前规则超出目标版允许的范围。";
            return false;
        }
        _status.Text = "";
        return true;
    }

    private void AddRule()
    {
        if (!Capture()) return;
        _rules.Add(FlaskCustomPreferenceStore.DefaultRule());
        Refresh(_rules.Count - 1);
    }

    private void RemoveRule()
    {
        if (_shown < 0) return;
        _rules.RemoveAt(_shown);
        Refresh(Math.Min(_shown, _rules.Count - 1));
    }

    private void Save()
    {
        if (!Capture()) return;
        Close(_rules.ToArray());
    }
}
