using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace PartyPilot.Desktop;

internal sealed class FlaskSupportDialog : Window
{
    private static readonly string[] WatchOptions =
        ["Life", "Mana", "Energy Shield", "Life AND Mana", "Life OR Mana", "Any: Life / Mana / ES"];
    private static readonly string[] DrinkOptions = ["Life", "Mana", "Both"];
    private readonly int _playerCount;
    private readonly Dictionary<(int Drinker, int Target), FlaskSupportBinding> _editing;
    private readonly ComboBox _drinker = new();
    private readonly CheckBox _supportBuild = new() { Content = "此角色使用 Flask Support（Drinker）构筑" };
    private readonly CheckBox _noMana = new() { Content = "构筑没有魔力" };
    private readonly CheckBox _sustained = new() { Content = "目标资源持续偏低时继续喝药" };
    private readonly StackPanel _rules = new() { Spacing = 12 };
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly List<RuleRow> _shownRows = [];
    private int _shownDrinker;

    private sealed record RuleRow(int Target, CheckBox Enabled, CheckBox QuickTopUp,
        ComboBox Watch, TextBox Percent, ComboBox Flask);

    internal FlaskSupportDialog(int playerCount, IReadOnlyList<FlaskSupportBinding> current)
    {
        if (playerCount is not (2 or 3) || !FlaskSupportPreferenceStore.Valid(current, playerCount))
            throw new ArgumentException("Invalid Drinker settings.");
        _playerCount = playerCount;
        _editing = current.ToDictionary(item => (item.DrinkerPlayer, item.Monitor.TargetPlayer));
        Title = "共享药剂 · Drinker";
        Width = 710;
        Height = 650;
        MinWidth = 600;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var content = new StackPanel { Spacing = 12, Margin = new Thickness(18) };
        content.Children.Add(new TextBlock { Text = "FLASK SUPPORT BUILD · DRINKER",
            FontWeight = Avalonia.Media.FontWeight.DemiBold });
        content.Children.Add(new TextBlock { Text =
            "选择饮用者，再为每位被观察角色设置触发条件。AND 要求生命和魔力都低；OR 满足其一即可。每种药剂保留自己的冷却。",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var picker = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        picker.Children.Add(new TextBlock { Text = "饮用者", VerticalAlignment = VerticalAlignment.Center });
        _drinker.ItemsSource = Enumerable.Range(1, playerCount).ToArray();
        _drinker.Width = 100;
        _drinker.SelectionChanged += (_, _) => ChangeDrinker();
        picker.Children.Add(_drinker);
        content.Children.Add(picker);
        content.Children.Add(_supportBuild);
        content.Children.Add(_noMana);
        content.Children.Add(_sustained);
        content.Children.Add(_rules);
        content.Children.Add(_status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消" };
        cancel.Click += (_, _) => Close(null);
        var save = new Button { Content = "保存" };
        save.Click += (_, _) => Save();
        actions.Children.Add(cancel);
        actions.Children.Add(save);
        content.Children.Add(actions);
        Content = new ScrollViewer { Content = content };
        _drinker.SelectedItem = 1;
        ShowDrinker(1);
    }

    private void ChangeDrinker()
    {
        if (_drinker.SelectedItem is not int player || player == _shownDrinker) return;
        if (!Capture())
        {
            _drinker.SelectedItem = _shownDrinker;
            return;
        }
        ShowDrinker(player);
    }

    private void ShowDrinker(int player)
    {
        _shownDrinker = player;
        _shownRows.Clear();
        _rules.Children.Clear();
        var representative = Enumerable.Range(1, _playerCount).Where(target => target != player)
            .Select(target => _editing.GetValueOrDefault((player, target)) ??
                FlaskSupportPreferenceStore.Default(player, target)).First();
        _supportBuild.IsChecked = representative.SupportBuild;
        _noMana.IsChecked = representative.BuildHasNoMana;
        _sustained.IsChecked = representative.SustainedUse;
        foreach (var target in Enumerable.Range(1, _playerCount).Where(target => target != player))
        {
            var binding = _editing.GetValueOrDefault((player, target)) ??
                FlaskSupportPreferenceStore.Default(player, target);
            var panel = new StackPanel { Spacing = 6 };
            var enabled = new CheckBox { Content = $"监测 P{target}", IsChecked = binding.Monitor.Enabled };
            panel.Children.Add(enabled);
            var quick = new CheckBox { Content = "快速补满：任一资源低于 99%", IsChecked = binding.Monitor.QuickTopUp };
            panel.Children.Add(quick);
            panel.Children.Add(new TextBlock { Text = "监测资源" });
            var watch = new ComboBox { ItemsSource = WatchOptions, SelectedItem = binding.Monitor.MonitorResource,
                Width = 230, HorizontalAlignment = HorizontalAlignment.Left };
            panel.Children.Add(watch);
            panel.Children.Add(new TextBlock { Text = "触发阈值（1–99%）" });
            var percent = new TextBox { Text = binding.Monitor.TriggerPercent.ToString("R", CultureInfo.InvariantCulture),
                Width = 100, HorizontalAlignment = HorizontalAlignment.Left };
            panel.Children.Add(percent);
            quick.IsCheckedChanged += (_, _) =>
            {
                watch.IsEnabled = quick.IsChecked != true;
                percent.IsEnabled = quick.IsChecked != true;
            };
            watch.IsEnabled = !binding.Monitor.QuickTopUp;
            percent.IsEnabled = !binding.Monitor.QuickTopUp;
            panel.Children.Add(new TextBlock { Text = "饮用药剂" });
            var flask = new ComboBox { ItemsSource = DrinkOptions, SelectedItem = binding.Monitor.FlaskResource,
                Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
            panel.Children.Add(flask);
            _shownRows.Add(new RuleRow(target, enabled, quick, watch, percent, flask));
            _rules.Children.Add(panel);
        }
        _status.Text = "";
    }

    private bool Capture()
    {
        if (_shownDrinker == 0) return true;
        var staged = new List<FlaskSupportBinding>();
        foreach (var row in _shownRows)
        {
            if (!double.TryParse(row.Percent.Text, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var percent) || !double.IsFinite(percent) || percent is < 1 or > 99 ||
                row.Watch.SelectedItem is not string watch || row.Flask.SelectedItem is not string flask)
            {
                _status.Text = $"P{row.Target} 的阈值或资源选择无效。";
                return false;
            }
            staged.Add(new FlaskSupportBinding(_shownDrinker, _supportBuild.IsChecked == true,
                _noMana.IsChecked == true, _sustained.IsChecked == true,
                new FlaskSupportMonitor(row.Enabled.IsChecked == true, row.Target, watch, percent,
                    flask, row.QuickTopUp.IsChecked == true)));
        }
        foreach (var item in staged) _editing[(_shownDrinker, item.Monitor.TargetPlayer)] = item;
        _status.Text = "";
        return true;
    }

    private void Save()
    {
        if (!Capture()) return;
        var all = _editing.Values.OrderBy(item => item.DrinkerPlayer)
            .ThenBy(item => item.Monitor.TargetPlayer).ToArray();
        if (!FlaskSupportPreferenceStore.Valid(all, _playerCount))
        {
            _status.Text = "共享药剂规则无效。";
            return;
        }
        Close(all);
    }
}
