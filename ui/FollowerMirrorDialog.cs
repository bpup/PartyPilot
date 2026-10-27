using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using PartyPilot.Common;

namespace PartyPilot.Desktop;

internal sealed class FollowerMirrorDialog : Window
{
    private static readonly string[] Labels =
        ["A", "B", "X", "Y", "LB", "RB", "LT", "RT",
            "D-pad Up", "D-pad Down", "D-pad Left", "D-pad Right"];
    private readonly ComboBox _player = new();
    private readonly ToggleSwitch _enabled = new();
    private readonly ToggleButton[] _buttons = new ToggleButton[12];
    private readonly TextBlock _title = new();
    private FollowerMirrorConfig _editing;
    private int _shownPlayer = 1;

    internal FollowerMirrorDialog(FollowerMirrorConfig current, int playerCount)
    {
        if (playerCount is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(playerCount));
        _editing = current;
        Title = "跟随角色按键镜像";
        Width = 530;
        Height = 350;
        MinWidth = 450;
        MinHeight = 310;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var rows = new StackPanel { Spacing = 14, Margin = new Thickness(18) };
        rows.Children.Add(new TextBlock { Text = "FOLLOWER BUTTONS", FontWeight = FontWeight.DemiBold });
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,180,Auto"), ColumnSpacing = 10 };
        _title.FontWeight = FontWeight.DemiBold;
        _title.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(_title);
        _player.ItemsSource = Enumerable.Range(1, playerCount).ToArray();
        _player.HorizontalAlignment = HorizontalAlignment.Stretch;
        _player.SelectionChanged += (_, _) => ChangePlayer();
        Grid.SetColumn(_player, 1);
        header.Children.Add(_player);
        _enabled.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_enabled, 2);
        header.Children.Add(_enabled);
        rows.Children.Add(header);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8 };
        for (var bit = 0; bit < _buttons.Length; bit++)
        {
            var button = new ToggleButton { Content = Labels[bit], MinWidth = 65 };
            _buttons[bit] = button;
            actions.Children.Add(button);
        }
        rows.Children.Add(actions);
        var save = new Button { Content = "保存", HorizontalAlignment = HorizontalAlignment.Right };
        save.Click += (_, _) =>
        {
            Capture();
            Close(_editing);
        };
        rows.Children.Add(save);
        Content = rows;
        _player.SelectedItem = 1;
        ShowPlayer(1);
    }

    private void ChangePlayer()
    {
        if (_player.SelectedItem is not int player || player == _shownPlayer) return;
        Capture();
        ShowPlayer(player);
    }

    private void Capture()
    {
        var mask = _enabled.IsChecked == true ? 1 : 0;
        for (var bit = 0; bit < _buttons.Length; bit++)
            if (_buttons[bit].IsChecked == true) mask |= 1 << (bit + 1);
        _editing = _editing.WithPlayer(_shownPlayer, mask);
    }

    private void ShowPlayer(int player)
    {
        _shownPlayer = player;
        _title.Text = $"Player {player} follower buttons";
        var mask = _editing.ForPlayer(player);
        _enabled.IsChecked = (mask & 1) != 0;
        for (var bit = 0; bit < _buttons.Length; bit++)
            _buttons[bit].IsChecked = (mask & (1 << (bit + 1))) != 0;
    }
}
