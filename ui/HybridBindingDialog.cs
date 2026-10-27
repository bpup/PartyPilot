using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PartyPilot.Common;

namespace PartyPilot.Desktop;

internal sealed class HybridBindingDialog : Window
{
    private readonly ComboBox[] _choices = new ComboBox[HybridBindingConfig.Definitions.Length];
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
            Text = "按目标版 0.6.17 的角色聚焦、移动和虚拟手柄动作设置。已使用的按键不能重复；运行桥接前保存。",
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
        rows.Children.Add(_status);
        var save = new Button { Content = "保存映射", HorizontalAlignment = HorizontalAlignment.Right };
        save.Click += (_, _) => Save();
        rows.Children.Add(save);
        Content = new ScrollViewer { Content = rows };
    }

    private void Save()
    {
        var keys = _choices.Select(choice => AutoPartyKeyBinding.VirtualKey(
            choice.SelectedItem as string ?? "None")).ToArray();
        if (!HybridBindingConfig.TryCreate(keys, out var config) || config is null)
        {
            var duplicate = keys.Where(key => key != 0).GroupBy(key => key)
                .FirstOrDefault(group => group.Count() > 1);
            _status.Text = duplicate is null ? "包含无法识别的按键。" :
                $"按键 {AutoPartyKeyBinding.Options.First(option => AutoPartyKeyBinding.VirtualKey(option) == duplicate.Key)} 已分配给多个动作。";
            return;
        }
        Close(config);
    }
}
