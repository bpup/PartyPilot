using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace PartyPilot.Desktop;

internal sealed class LoginCalibrationDialog : Window
{
    private readonly LoginCalibrationFrame _frame;
    private readonly string _layoutKey;
    private readonly Image _image;
    private readonly TextBlock _instruction;
    private readonly Button _correct;
    private readonly Button _retry;
    private WriteableBitmap? _bitmap;
    private (double X, double Y)? _username;
    private (double X, double Y)? _password;
    private (double X, double Y)? _pending;
    private bool _markPassword;
    private int _usernameAttempts;
    private int _passwordAttempts;
    private string? _feedback;

    internal LoginCalibrationDialog(int client, LoginCalibrationFrame frame, string layoutKey)
    {
        _frame = frame;
        _layoutKey = layoutKey;
        Title = $"客户端 {client} · 标记登录字段";
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var scale = Math.Min(1, Math.Min(960d / frame.Width, 560d / frame.Height));
        var displayWidth = frame.Width * scale;
        var displayHeight = frame.Height * scale;
        Width = displayWidth + 48;
        Height = displayHeight + 190;
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(20) };
        _instruction = new TextBlock { TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(_instruction);
        _bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96),
            PixelFormats.Bgra8888, AlphaFormat.Premul);
        for (var index = 3; index < frame.Pixels.Length; index += 4) frame.Pixels[index] = 255;
        using (var locked = _bitmap.Lock())
        {
            var bytesPerRow = frame.Width * 4;
            for (var row = 0; row < frame.Height; row++)
                Marshal.Copy(frame.Pixels, row * bytesPerRow,
                    locked.Address + row * locked.RowBytes, bytesPerRow);
        }
        _image = new Image
        {
            Source = _bitmap, Width = displayWidth, Height = displayHeight,
            Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Left
        };
        _image.PointerPressed += MarkPoint;
        panel.Children.Add(_image);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        _correct = new Button { Content = "位置正确" };
        _correct.Click += (_, _) => Confirm();
        _retry = new Button { Content = "重试" };
        _retry.Click += (_, _) => Retry();
        var manual = new Button { Content = "手动登录" };
        manual.Click += (_, _) => Close(null);
        actions.Children.Add(_correct);
        actions.Children.Add(_retry);
        actions.Children.Add(manual);
        panel.Children.Add(actions);
        Content = panel;
        Closed += (_, _) => { _image.Source = null; _bitmap?.Dispose(); _bitmap = null; };
        Refresh();
    }

    private void MarkPoint(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (_pending is not null) return;
        var point = eventArgs.GetPosition(_image);
        var x = point.X / _image.Bounds.Width;
        var y = point.Y / _image.Bounds.Height;
        if (!double.IsFinite(x) || !double.IsFinite(y) ||
            x is < 0.01 or > 0.99 || y is < 0.01 or > 0.99)
        {
            if (_markPassword) _passwordAttempts++;
            else _usernameAttempts++;
            if ((_markPassword ? _passwordAttempts : _usernameAttempts) >= 3)
            {
                Close(null);
                return;
            }
            _feedback = "标记需位于游戏画面内侧；请重新点击字段中央。";
            Refresh();
            return;
        }
        _pending = (x, y);
        _feedback = null;
        if (_markPassword) _passwordAttempts++;
        else _usernameAttempts++;
        Refresh();
    }

    private void Confirm()
    {
        if (_pending is not { } point) return;
        if (!_markPassword)
        {
            _username = point;
            _pending = null;
            _markPassword = true;
            Refresh();
            return;
        }
        _password = point;
        if (_username is { } username && _password is { } password &&
            LoginCalibration.TryCreate(username.X, username.Y, password.X, password.Y,
                _frame.Width, _frame.Height, _layoutKey, out var calibration))
        {
            Close(calibration);
            return;
        }
        _pending = null;
        _password = null;
        if (_passwordAttempts >= 3) Close(null);
        else
        {
            _feedback = "密码字段必须位于邮箱字段下方；请重新标记密码字段。";
            Refresh();
        }
    }

    private void Retry()
    {
        if (_pending is null) return;
        _pending = null;
        _feedback = null;
        if ((_markPassword ? _passwordAttempts : _usernameAttempts) >= 3) Close(null);
        else Refresh();
    }

    private void Refresh()
    {
        _correct.IsEnabled = _pending is not null;
        _retry.IsEnabled = _pending is not null;
        _instruction.Text = _feedback ?? (_pending is null
            ? $"请在截图中点击{(_markPassword ? "密码" : "邮箱")}输入框中央。画面只用于标记，不会发送登录输入。"
            : $"请确认标记位于{(_markPassword ? "密码" : "邮箱")}输入框中央；不正确可重试，最多三次。");
    }
}
