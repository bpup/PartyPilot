using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace PartyPilot.Desktop;

internal sealed class FlaskRuleDialog : Window
{
    private readonly int _playerCount;
    private readonly Dictionary<int, FlaskPlayerRule> _editing;
    private readonly ComboBox _player = new();
    private readonly CheckBox _lifeEnabled = new() { Content = "启用生命药剂" };
    private readonly CheckBox _skipRecovering = new() { Content = "恢复中跳过生命药剂" };
    private readonly CheckBox _manaEnabled = new() { Content = "启用魔力药剂" };
    private readonly TextBox _lifeTrigger = Field("life-trigger");
    private readonly TextBox _lifeCooldown = Field("life-cooldown");
    private readonly TextBox _emergency = Field("life-emergency");
    private readonly TextBox _lifeMinMissing = Field("life-min-missing");
    private readonly TextBox _manaTrigger = Field("mana-trigger");
    private readonly TextBox _manaCooldown = Field("mana-cooldown");
    private readonly TextBox _manaMinMissing = Field("mana-min-missing");
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private int _shownPlayer;

    internal FlaskRuleDialog(int playerCount, IReadOnlyList<FlaskPlayerRule> current)
    {
        if (playerCount is not (2 or 3)) throw new ArgumentOutOfRangeException(nameof(playerCount));
        _playerCount = playerCount;
        var currentValid = FlaskRulePreferenceStore.Valid(current, playerCount);
        _editing = (currentValid
            ? current : Enumerable.Range(1, playerCount).Select(FlaskRulePreferenceStore.DefaultFor).ToArray())
            .ToDictionary(item => item.Player);
        Title = "逐角色自动药剂规则";
        Width = 580;
        Height = 720;
        MinWidth = 480;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var rows = new StackPanel { Spacing = 10, Margin = new Thickness(18) };
        rows.Children.Add(new TextBlock { Text = "PLAYER FLASK RULES",
            FontWeight = Avalonia.Media.FontWeight.DemiBold });
        rows.Children.Add(new TextBlock { Text =
            "每位角色单独设置生命与魔力药剂。紧急阈值是独立选项，只有资源同时达到正常触发条件时才会检查。",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var picker = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        picker.Children.Add(new TextBlock { Text = "角色", VerticalAlignment = VerticalAlignment.Center });
        _player.ItemsSource = Enumerable.Range(1, playerCount).ToArray();
        _player.Width = 100;
        _player.SelectionChanged += (_, _) => ChangePlayer();
        picker.Children.Add(_player);
        rows.Children.Add(picker);
        rows.Children.Add(_lifeEnabled);
        AddNumber(rows, "生命触发阈值（1–95%）", _lifeTrigger);
        AddNumber(rows, "生命紧急阈值（1–95%）", _emergency);
        AddNumber(rows, "生命冷却（100–60000 毫秒）", _lifeCooldown);
        AddNumber(rows, "生命最低缺口（0–10000）", _lifeMinMissing);
        rows.Children.Add(_skipRecovering);
        rows.Children.Add(new TextBlock { Text = "当前实时传感器没有可靠的恢复状态；此选项已保存，但实时判断暂时无法据此跳过。",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        rows.Children.Add(_manaEnabled);
        AddNumber(rows, "魔力触发阈值（1–95%）", _manaTrigger);
        AddNumber(rows, "魔力冷却（100–60000 毫秒）", _manaCooldown);
        AddNumber(rows, "魔力最低缺口（0–10000）", _manaMinMissing);
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
        _player.SelectedItem = 1;
        ShowPlayer(1);
        if (!currentValid)
            _status.Text = "原设置文件无效；当前显示目标版重置值。保存会覆盖原文件。";
    }

    private static TextBox Field(string name) => new()
    {
        Name = name, Width = 130, HorizontalAlignment = HorizontalAlignment.Left
    };

    private static void AddNumber(StackPanel panel, string label, TextBox field)
    {
        panel.Children.Add(new TextBlock { Text = label });
        panel.Children.Add(field);
    }

    private void ChangePlayer()
    {
        if (_player.SelectedItem is not int player || player == _shownPlayer) return;
        if (!Capture())
        {
            _player.SelectedItem = _shownPlayer;
            return;
        }
        ShowPlayer(player);
    }

    private void ShowPlayer(int player)
    {
        _shownPlayer = player;
        var rule = _editing[player];
        _lifeEnabled.IsChecked = rule.LifeEnabled;
        _lifeTrigger.Text = rule.LifeTrigger.ToString("R", CultureInfo.InvariantCulture);
        _lifeCooldown.Text = rule.LifeCooldownMs.ToString(CultureInfo.InvariantCulture);
        _emergency.Text = rule.Emergency.ToString("R", CultureInfo.InvariantCulture);
        _lifeMinMissing.Text = rule.LifeMinMissing.ToString(CultureInfo.InvariantCulture);
        _skipRecovering.IsChecked = rule.SkipRecovering;
        _manaEnabled.IsChecked = rule.ManaEnabled;
        _manaTrigger.Text = rule.ManaTrigger.ToString("R", CultureInfo.InvariantCulture);
        _manaCooldown.Text = rule.ManaCooldownMs.ToString(CultureInfo.InvariantCulture);
        _manaMinMissing.Text = rule.ManaMinMissing.ToString(CultureInfo.InvariantCulture);
        _status.Text = "";
    }

    private bool Capture()
    {
        if (_shownPlayer == 0) return true;
        if (!double.TryParse(_lifeTrigger.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var lifeTrigger) ||
            !int.TryParse(_lifeCooldown.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var lifeCooldown) ||
            !double.TryParse(_emergency.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var emergency) ||
            !int.TryParse(_lifeMinMissing.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var lifeMinMissing) ||
            !double.TryParse(_manaTrigger.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var manaTrigger) ||
            !int.TryParse(_manaCooldown.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var manaCooldown) ||
            !int.TryParse(_manaMinMissing.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var manaMinMissing) ||
            !Percent(lifeTrigger) || !Percent(emergency) || !Percent(manaTrigger) ||
            lifeCooldown is < 100 or > 60000 || manaCooldown is < 100 or > 60000 ||
            lifeMinMissing is < 0 or > 10000 || manaMinMissing is < 0 or > 10000)
        {
            _status.Text = "当前角色的药剂数值超出目标版控件范围。";
            return false;
        }
        _editing[_shownPlayer] = new FlaskPlayerRule(_shownPlayer, _lifeEnabled.IsChecked == true,
            lifeTrigger, lifeCooldown, emergency, lifeMinMissing, _skipRecovering.IsChecked == true,
            _manaEnabled.IsChecked == true, manaTrigger, manaCooldown, manaMinMissing);
        _status.Text = "";
        return true;
    }

    private static bool Percent(double value) => double.IsFinite(value) && value is >= 1 and <= 95;

    private void Save()
    {
        if (!Capture()) return;
        var rules = _editing.Values.OrderBy(item => item.Player).ToArray();
        if (!FlaskRulePreferenceStore.Valid(rules, _playerCount))
        {
            _status.Text = "逐角色药剂规则无效。";
            return;
        }
        Close(rules);
    }
}
