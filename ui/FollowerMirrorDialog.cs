using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using PartyPilot.Common;

namespace PartyPilot.Desktop;

internal sealed class FollowerMirrorDialog : Window
{
    private static readonly string[] Labels =
        ["启用", "A", "B", "X", "Y", "LB", "RB", "LT", "RT", "十字上", "十字下", "十字左", "十字右"];
    private readonly CheckBox[,] _choices = new CheckBox[6, 13];

    internal FollowerMirrorDialog(FollowerMirrorConfig current)
    {
        Title = "跟随角色按键镜像";
        Width = 570;
        Height = 670;
        MinWidth = 460;
        MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var rows = new StackPanel { Spacing = 12, Margin = new Thickness(18) };
        for (var player = 1; player <= 6; player++)
        {
            rows.Children.Add(new TextBlock { Text = $"P{player}", FontWeight = Avalonia.Media.FontWeight.Bold });
            var group = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 12, LineSpacing = 6 };
            for (var bit = 0; bit < Labels.Length; bit++)
            {
                var choice = new CheckBox { Content = Labels[bit], IsChecked =
                    (current.ForPlayer(player) & (1 << bit)) != 0 };
                _choices[player - 1, bit] = choice;
                group.Children.Add(choice);
            }
            rows.Children.Add(group);
        }
        var save = new Button { Content = "保存", HorizontalAlignment = HorizontalAlignment.Right };
        save.Click += (_, _) => Close(ReadConfig());
        rows.Children.Add(save);
        Content = new ScrollViewer { Content = rows };
    }

    private FollowerMirrorConfig ReadConfig()
    {
        var config = FollowerMirrorConfig.Default;
        for (var player = 1; player <= 6; player++)
        {
            var mask = 0;
            for (var bit = 0; bit < 13; bit++)
                if (_choices[player - 1, bit].IsChecked == true) mask |= 1 << bit;
            config = config.WithPlayer(player, mask);
        }
        return config;
    }
}
