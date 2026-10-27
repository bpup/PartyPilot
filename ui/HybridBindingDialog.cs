using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PartyPilot.Common;

namespace PartyPilot.Desktop;

internal sealed class HybridBindingDialog : Window
{
    private sealed record CustomRow(StackPanel Container, TextBox Label, TextBox Description,
        ComboBox Key, ComboBox Action);
    private readonly ComboBox[] _choices = new ComboBox[HybridBindingConfig.Definitions.Length];
    private readonly StackPanel _customPanel = new() { Spacing = 10 };
    private readonly List<CustomRow> _customRows = [];
    private readonly TextBlock _status = new() { Foreground = Brushes.Orange, TextWrapping = TextWrapping.Wrap };

    internal HybridBindingDialog(HybridBindingConfig current)
    {
        Title = "键鼠按键映射";
        Width = 550;
        Height = 690;
        MinWidth = 460;
        MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var rows = new StackPanel { Spacing = 8, Margin = new Thickness(18) };
        rows.Children.Add(new TextBlock
        {
            Text = "按目标版 0.6.17 的角色聚焦、移动和虚拟手柄动作设置。基础按键不能重复；附加动作可共用按键。运行桥接前保存。",
            TextWrapping = TextWrapping.Wrap
        });
        for (var index = 0; index < _choices.Length; index++)
        {
            if (index is 0 or 6) rows.Children.Add(new TextBlock
            {
                Text = index == 0 ? "角色聚焦" : "移动与动作",
                FontWeight = FontWeight.Bold,
                Margin = new Thickness(0, 10, 0, 0)
            });
            var definition = HybridBindingConfig.Definitions[index];
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,190") };
            var label = new TextBlock { Text = definition.Label, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 0);
            row.Children.Add(label);
            var choice = new ComboBox
            {
                ItemsSource = AutoPartyKeyBinding.Options,
                SelectedItem = AutoPartyKeyBinding.Options.FirstOrDefault(option =>
                    AutoPartyKeyBinding.VirtualKey(option) == current.KeyAt(index)) ?? "None",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            _choices[index] = choice;
            Grid.SetColumn(choice, 1);
            row.Children.Add(choice);
            rows.Children.Add(row);
        }
        rows.Children.Add(new TextBlock
        {
            Text = "附加动作键",
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 12, 0, 0)
        });
        rows.Children.Add(new TextBlock
        {
            Text = "同一按键可同时触发多个虚拟手柄动作；菜单中禁用动作尚待游戏菜单状态验证。",
            TextWrapping = TextWrapping.Wrap
        });
        rows.Children.Add(_customPanel);
        foreach (var action in current.CustomActions) AddCustomRow(action);
        var add = new Button { Content = "添加附加动作", HorizontalAlignment = HorizontalAlignment.Left };
        add.Click += (_, _) => AddCustomRow(new HybridCustomAction("New binding", "", 0, 6));
        rows.Children.Add(add);
        rows.Children.Add(_status);
        var save = new Button { Content = "保存映射", HorizontalAlignment = HorizontalAlignment.Right };
        save.Click += (_, _) => Save();
        rows.Children.Add(save);
        Content = new ScrollViewer { Content = rows };
    }

    private void AddCustomRow(HybridCustomAction action)
    {
        var container = new StackPanel { Spacing = 5 };
        var label = new TextBox { Watermark = "名称", Text = action.Label };
        var description = new TextBox { Watermark = "说明（可留空）", Text = action.Description };
        var key = new ComboBox
        {
            ItemsSource = AutoPartyKeyBinding.Options,
            SelectedItem = AutoPartyKeyBinding.Options.FirstOrDefault(option =>
                AutoPartyKeyBinding.VirtualKey(option) == action.Key) ?? "None",
            MinWidth = 155
        };
        var target = new ComboBox
        {
            ItemsSource = HybridBindingConfig.Definitions.Skip(6).Select(item => item.Label).ToArray(),
            SelectedIndex = action.ActionIndex - 6,
            MinWidth = 155
        };
        var controls = new WrapPanel { ItemSpacing = 8 };
        controls.Children.Add(key);
        controls.Children.Add(target);
        var remove = new Button { Content = "删除" };
        controls.Children.Add(remove);
        container.Children.Add(label);
        container.Children.Add(description);
        container.Children.Add(controls);
        var row = new CustomRow(container, label, description, key, target);
        remove.Click += (_, _) =>
        {
            _customRows.Remove(row);
            _customPanel.Children.Remove(container);
        };
        _customRows.Add(row);
        _customPanel.Children.Add(container);
    }

    private void Save()
    {
        var keys = _choices.Select(choice => AutoPartyKeyBinding.VirtualKey(
            choice.SelectedItem as string ?? "None")).ToArray();
        var custom = _customRows.Select(row => new HybridCustomAction(
            row.Label.Text ?? "", row.Description.Text ?? "",
            AutoPartyKeyBinding.VirtualKey(row.Key.SelectedItem as string ?? "None"),
            row.Action.SelectedIndex + 6)).ToArray();
        if (!HybridBindingConfig.TryCreate(keys, custom, out var config) || config is null)
        {
            var duplicate = keys.Where(key => key != 0).GroupBy(key => key)
                .FirstOrDefault(group => group.Count() > 1);
            _status.Text = duplicate is null ? "包含无法识别的按键或附加动作。" :
                $"按键 {AutoPartyKeyBinding.Options.First(option => AutoPartyKeyBinding.VirtualKey(option) == duplicate.Key)} 已分配给多个动作。";
            return;
        }
        Close(config);
    }
}
