using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PartyPilot.Desktop;

public sealed class MainWindow : Window
{
    private enum ClientAction { Check, Start, Attach }
    private static readonly IBrush CanvasBrush = Brush("#10151E");
    private static readonly IBrush CardBrush = Brush("#192332");
    private static readonly IBrush EdgeBrush = Brush("#344152");
    private static readonly IBrush MutedBrush = Brush("#9FAFC2");
    private static readonly IBrush AccentBrush = Brush("#D7A65B");

    private readonly ComboBox _playerCount = new();
    private readonly ComboBox _topologyVariant = new();
    private readonly TextBlock _assignment = new();
    private readonly ComboBox _layoutMode = new();
    private readonly CheckBox _applyLayout = new();
    private readonly TextBox _monitors = new();
    private readonly TextBox[] _paths = new TextBox[3];
    private readonly TextBox[] _uris = new TextBox[3];
    private readonly TextBlock[] _clientLabels = new TextBlock[3];
    private readonly StackPanel[] _clientRows = new StackPanel[3];
    private readonly Button _check = new();
    private readonly Button _launch = new();
    private readonly Button _attach = new();
    private readonly TextBlock _status = new();
    private readonly ComboBox _physicalIndex = new();
    private readonly CheckBox _isolationConfirmed = new();
    private readonly Button _prepareBridge = new();
    private readonly Button _armPairing = new();
    private readonly Button _confirmRole = new();
    private readonly Button _stopBridge = new();
    private readonly TextBlock _pairingStatus = new();
    private readonly TextBox _followStop = new();
    private readonly TextBox _followResume = new();
    private readonly TextBox _followCatchup = new();
    private readonly TextBox _followMaxAge = new();
    private readonly TextBox _followLogAge = new();
    private readonly TextBox _followLeaderName = new();
    private readonly Button _startFollow = new();
    private readonly Button _stopFollow = new();
    private readonly TextBlock _followStatus = new();
    private PartyTopology? _selectedTopology;
    private BridgeSession? _bridge;
    private LiveFollowSession? _follow;
    private PairingGuide? _pairing;
    private IReadOnlyList<LaunchedClient> _launchedClients = [];
    private bool _pairingHasStarted;
    private bool _armRequested;
    private bool _pairingSettling;
    private bool _pairingSettled;
    private bool _followStopping;
    private string _loginStatus = "waiting";
    private bool _busy;

    public MainWindow()
    {
        Title = "PartyPilot · Play";
        Width = 1120;
        Height = 800;
        MinWidth = 760;
        MinHeight = 600;
        Background = CanvasBrush;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var shell = new DockPanel();
        var sidebar = BuildSidebar();
        DockPanel.SetDock(sidebar, Dock.Left);
        shell.Children.Add(sidebar);

        var content = new StackPanel { Spacing = 18, Margin = new Thickness(34, 28, 34, 36) };
        content.Children.Add(Label("PLAY", 12, AccentBrush, FontWeight.Bold));
        content.Children.Add(Label("Connect. Choose. Start playing.", 29, Brushes.White, FontWeight.Bold));
        content.Children.Add(Label("独立版工作区 · 游戏窗口启动与有限布局的手柄配对引导", 13, MutedBrush));
        content.Children.Add(BuildInputStep());
        content.Children.Add(BuildClientStep());
        content.Children.Add(BuildLaunchStep());
        content.Children.Add(BuildFollowStep());
        content.Children.Add(BuildStatusCard());

        shell.Children.Add(new ScrollViewer { Content = content });
        Content = shell;
        _playerCount.SelectionChanged += (_, _) => UpdateTopologyOptions();
        _topologyVariant.SelectionChanged += (_, _) => UpdateClientRows();
        _applyLayout.IsCheckedChanged += (_, _) => UpdateLayoutControls();
        _isolationConfirmed.IsCheckedChanged += (_, _) => UpdateBridgeControls();
        _physicalIndex.SelectionChanged += (_, _) => UpdateBridgeControls();
        Closed += async (_, _) => await StopBridgeAsync();
        UpdateClientRows();
        UpdateLayoutControls();
    }

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));

    private static TextBlock Label(string text, double size, IBrush brush, FontWeight weight = default) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight == default ? FontWeight.Normal : weight,
        Foreground = brush,
        TextWrapping = TextWrapping.Wrap
    };

    private static Border Card(Control child) => new()
    {
        Background = CardBrush,
        BorderBrush = EdgeBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(22),
        Child = child
    };

    private static StackPanel Step(string number, string heading, string description)
    {
        var panel = new StackPanel { Spacing = 10 };
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        line.Children.Add(Label(number, 23, AccentBrush, FontWeight.Bold));
        line.Children.Add(Label(heading, 18, Brushes.White, FontWeight.Bold));
        panel.Children.Add(line);
        panel.Children.Add(Label(description, 13, MutedBrush));
        return panel;
    }

    private static Border BuildSidebar()
    {
        var navigation = new StackPanel { Width = 194, Spacing = 18, Margin = new Thickness(20, 26) };
        navigation.Children.Add(Label("PARTYPILOT", 17, AccentBrush, FontWeight.Bold));
        navigation.Children.Add(Label("独立 PoE2 组队工具", 11, MutedBrush));
        navigation.Children.Add(new Border { Height = 1, Background = EdgeBrush, Margin = new Thickness(0, 14) });
        navigation.Children.Add(Label("PLAY  ·  游戏窗口", 13, Brushes.White, FontWeight.Bold));
        navigation.Children.Add(Label("自动跟随 · 集成待验证", 12, MutedBrush));
        navigation.Children.Add(Label("战斗与拾取 · 未接入", 12, MutedBrush));
        navigation.Children.Add(Label("设置与支持 · 未接入", 12, MutedBrush));
        return new Border
        {
            Width = 230,
            Background = Brush("#151D29"),
            BorderBrush = EdgeBrush,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = navigation
        };
    }

    private Border BuildInputStep()
    {
        var panel = Step("1", "CHOOSE INPUT · 选择输入", "以下仅适用于最多三个系统虚拟手柄的布局；需先在 Windows 上完成实体手柄对游戏的隔离。");
        _physicalIndex.PlaceholderText = "实体手柄 XInput 编号";
        _physicalIndex.ItemsSource = Enumerable.Range(0, 4).Select(index => $"XInput {index}").ToArray();
        _physicalIndex.Width = 190;
        _physicalIndex.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(_physicalIndex);
        _isolationConfirmed.Content = "我已确认实体手柄对游戏隐藏，仅由桥接读取";
        panel.Children.Add(_isolationConfirmed);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 12, LineSpacing = 8 };
        _prepareBridge.Content = "准备虚拟手柄";
        _prepareBridge.Click += async (_, _) => await PrepareBridgeAsync();
        _armPairing.Content = "开始检测当前登录画面";
        _armPairing.Click += async (_, _) => await ArmPairingAsync();
        _stopBridge.Content = "停止手柄桥接";
        _stopBridge.Click += async (_, _) => await StopBridgeAsync();
        _confirmRole.Content = "已在游戏内确认此角色";
        _confirmRole.Click += async (_, _) => await ConfirmRoleAsync();
        actions.Children.Add(_prepareBridge);
        actions.Children.Add(_armPairing);
        actions.Children.Add(_stopBridge);
        actions.Children.Add(_confirmRole);
        panel.Children.Add(actions);
        _pairingStatus.Text = "选择人数、布局和实体手柄后可检查桥接支持情况。";
        _pairingStatus.Foreground = MutedBrush;
        _pairingStatus.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_pairingStatus);
        return Card(panel);
    }

    private Border BuildClientStep()
    {
        var panel = Step("2", "HOW MANY CHARACTERS? · 选择人数", "按目标版本的布局列出 1–6 人及其所需客户端；仅部分布局支持上方的手柄配对引导。");
        _playerCount.PlaceholderText = "选择 1–6 人";
        _playerCount.ItemsSource = Enumerable.Range(1, 6).Select(count => $"{count} 人").ToArray();
        _playerCount.Width = 190;
        _playerCount.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(_playerCount);
        _topologyVariant.PlaceholderText = "选择角色分布";
        _topologyVariant.MinWidth = 380;
        _topologyVariant.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(_topologyVariant);
        _assignment.Text = "选择人数后显示角色与客户端分布。";
        _assignment.Foreground = MutedBrush;
        _assignment.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_assignment);

        for (var index = 0; index < 3; index++)
        {
            var row = new StackPanel { Spacing = 7, Margin = new Thickness(0, 8, 0, 0) };
            var clientLabel = Label($"客户端 {index + 1} · 游戏程序", 13, Brushes.White, FontWeight.SemiBold);
            row.Children.Add(clientLabel);
            var path = new TextBox { Watermark = "PathOfExile_x64.exe 完整路径" };
            path.TextChanged += (_, _) => ClearLaunchedClients();
            row.Children.Add(path);
            row.Children.Add(Label("Steam/Epic 启动 URI（可选；须与上方程序对应）", 12, MutedBrush));
            var uri = new TextBox { Watermark = "steam://rungameid/2694490 或 Epic 清单 URI" };
            uri.TextChanged += (_, _) => ClearLaunchedClients();
            row.Children.Add(uri);
            _paths[index] = path;
            _uris[index] = uri;
            _clientLabels[index] = clientLabel;
            _clientRows[index] = row;
            panel.Children.Add(row);
        }
        return Card(panel);
    }

    private Border BuildLaunchStep()
    {
        var panel = Step("3", "PREPARE & START · 准备并开始", "检查配置后，可启动游戏窗口或绑定已运行窗口。此步骤不执行账号登录或角色配对。");
        _applyLayout.Content = "对游戏窗口应用布局";
        panel.Children.Add(_applyLayout);
        _layoutMode.ItemsSource = new[] { "stacked", "tiled", "multiple" };
        _layoutMode.SelectedIndex = 0;
        _layoutMode.Width = 190;
        _layoutMode.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(_layoutMode);
        _monitors.Watermark = "显示器编号，例如 1,2";
        panel.Children.Add(_monitors);

        var actions = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 12, LineSpacing = 8, Margin = new Thickness(0, 10, 0, 0) };
        _check.Content = "检查配置";
        _check.Click += async (_, _) => await RunLauncherAsync(ClientAction.Check);
        _launch.Content = "启动游戏窗口";
        _launch.Click += async (_, _) => await RunLauncherAsync(ClientAction.Start);
        _attach.Content = "绑定已运行窗口";
        _attach.Click += async (_, _) => await RunLauncherAsync(ClientAction.Attach);
        actions.Children.Add(_check);
        actions.Children.Add(_launch);
        actions.Children.Add(_attach);
        panel.Children.Add(actions);
        return Card(panel);
    }

    private Border BuildStatusCard()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Label("执行结果", 15, Brushes.White, FontWeight.SemiBold));
        _status.Text = "尚未检查配置。";
        _status.Foreground = MutedBrush;
        _status.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_status);
        return Card(panel);
    }

    private Border BuildFollowStep()
    {
        var panel = Step("4", "FOLLOW · 跟随", "当前实时输入仅支持双客户端各一名角色；请先完成配对，并在两个客户端启用只读位置传感器。距离与时效需要按实际游戏校准。");
        var fields = new (TextBox Box, string Label)[]
        {
            (_followStop, "停止距离"), (_followResume, "恢复距离"), (_followCatchup, "追赶距离"),
            (_followMaxAge, "位置有效期（ms/s/m/h）"), (_followLogAge, "区域日志有效期（ms/s/m/h）")
        };
        foreach (var (box, label) in fields)
        {
            panel.Children.Add(Label(label, 12, MutedBrush));
            box.Watermark = "明确填写";
            box.Width = 190;
            box.HorizontalAlignment = HorizontalAlignment.Left;
            box.TextChanged += (_, _) => UpdateFollowControls();
            panel.Children.Add(box);
        }
        panel.Children.Add(Label("队长角色名（可选，用于识别离队）", 12, MutedBrush));
        _followLeaderName.Watermark = "角色名";
        _followLeaderName.Width = 190;
        _followLeaderName.HorizontalAlignment = HorizontalAlignment.Left;
        _followLeaderName.TextChanged += (_, _) => UpdateFollowControls();
        panel.Children.Add(_followLeaderName);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 12 };
        _startFollow.Content = "开始跟随";
        _startFollow.Click += async (_, _) => await StartFollowAsync();
        _stopFollow.Content = "停止跟随";
        _stopFollow.Click += async (_, _) => await StopFollowAsync();
        actions.Children.Add(_startFollow);
        actions.Children.Add(_stopFollow);
        panel.Children.Add(actions);
        _followStatus.Text = "等待配对完成；真实游戏中的方向与角色绑定尚未验证。";
        _followStatus.Foreground = MutedBrush;
        _followStatus.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_followStatus);
        return Card(panel);
    }

    private void UpdateClientRows()
    {
        var selection = _topologyVariant.SelectedItem as PartyTopology;
        if (!ReferenceEquals(_selectedTopology, selection)) _launchedClients = [];
        _selectedTopology = selection;
        var count = _selectedTopology?.ClientCount ?? 0;
        _assignment.Text = _selectedTopology is null
            ? "选择人数后显示角色与客户端分布。"
            : $"{_selectedTopology.PlayerCount} 人 · {count} 个客户端 · {_selectedTopology.Summary}";
        for (var index = 0; index < _clientRows.Length; index++)
        {
            _clientRows[index].IsVisible = index < count;
            if (index < count)
            {
                var players = _selectedTopology!.Clients[index];
                _clientLabels[index].Text = $"客户端 {index + 1} · {string.Join('/', players.Select(player => $"P{player}"))} · 游戏程序";
            }
        }
        _check.IsEnabled = !_busy && !_pairingHasStarted && count > 0;
        _launch.IsEnabled = !_busy && !_pairingHasStarted && count > 0;
        _attach.IsEnabled = !_busy && !_pairingHasStarted && count > 0;
        UpdateBridgeControls();
    }

    private void ClearLaunchedClients()
    {
        _launchedClients = [];
        if (_pairing is not null) ShowCurrentPairingStep();
        UpdateBridgeControls();
    }

    private void UpdateTopologyOptions()
    {
        var playerCount = _playerCount.SelectedIndex + 1;
        var options = PartyTopologies.ForPlayerCount(playerCount);
        _topologyVariant.ItemsSource = options;
        _topologyVariant.IsVisible = options.Count > 1;
        _topologyVariant.SelectedIndex = options.Count > 0 ? 0 : -1;
        UpdateClientRows();
    }

    private void UpdateLayoutControls()
    {
        var enabled = _applyLayout.IsChecked == true;
        _layoutMode.IsEnabled = enabled;
        _monitors.IsEnabled = enabled;
    }

    private void UpdateBridgeControls()
    {
        var slots = _selectedTopology?.Slots.Select(slot => slot.RuntimePad).ToArray() ?? [];
        var supported = slots.Length is >= 1 and <= 3 && slots.All(slot => slot is >= 1 and <= 4);
        _prepareBridge.IsEnabled = !_busy && _bridge is null && supported &&
            _physicalIndex.SelectedIndex is >= 0 and <= 3 && _isolationConfirmed.IsChecked == true;
        _stopBridge.IsEnabled = _bridge is not null;
        _armPairing.IsEnabled = _bridge is not null && _pairing?.Current is not null &&
            _launchedClients.Count == _selectedTopology?.ClientCount &&
            _pairing.Armed == false && _pairing.AReleased == false && !_armRequested && !_busy;
        _confirmRole.IsEnabled = _bridge is not null && _pairing?.AReleased == true;
        _playerCount.IsEnabled = _bridge is null && !_busy;
        _topologyVariant.IsEnabled = _bridge is null && !_busy;
        for (var index = 0; index < _paths.Length; index++)
        {
            if (_paths[index] is not null) _paths[index].IsEnabled = !_pairingHasStarted && !_busy;
            if (_uris[index] is not null) _uris[index].IsEnabled = !_pairingHasStarted && !_busy;
        }
        if (_bridge is null && _selectedTopology is not null && !supported)
        {
            _pairingStatus.Text = "此布局需要四个以上系统手柄或第三客户端进程内 P5/P6 输入；当前桥接不支持，未开始配对。";
        }
        UpdateFollowControls();
    }

    private void UpdateFollowControls()
    {
        var valid = LiveFollowPlan.TryCreate(_followStop.Text, _followResume.Text, _followCatchup.Text,
            _followMaxAge.Text, _followLogAge.Text, _followLeaderName.Text, out _);
        _startFollow.IsEnabled = OperatingSystem.IsWindows() && !_busy && !_followStopping && _follow is null &&
            _bridge is not null && _pairingSettled && _selectedTopology?.Id == "2-split" &&
            _launchedClients.Count == 2 && valid;
        _stopFollow.IsEnabled = _follow is not null;
    }

    private async Task PrepareBridgeAsync()
    {
        if (_busy || _bridge is not null || _selectedTopology is null || !OperatingSystem.IsWindows())
        {
            _pairingStatus.Text = "手柄桥接需要 Windows、已选布局与空闲桥接会话。";
            return;
        }
        var slots = _selectedTopology.Slots.Select(slot => slot.RuntimePad).ToArray();
        if (slots.Length is < 1 or > 3 || slots.Any(slot => slot is < 1 or > 4) ||
            _physicalIndex.SelectedIndex is < 0 or > 3 || _isolationConfirmed.IsChecked != true)
        {
            _pairingStatus.Text = "当前布局或实体手柄隔离条件不满足桥接要求。";
            return;
        }
        var executable = BridgeSession.FindExecutable();
        if (executable is null)
        {
            _pairingStatus.Text = "找不到 PartyPilot.Bridge.exe；请使用完整发布目录。";
            return;
        }
        _busy = true;
        UpdateBridgeControls();
        _pairingStatus.Text = "正在检查实体手柄与虚拟手柄编号…";
        try
        {
            var session = await BridgeSession.StartAsync(executable, slots, _physicalIndex.SelectedIndex);
            session.AReleased += BridgeAReleased;
            session.PairingArmed += BridgePairingArmed;
            session.PairingCompleted += BridgePairingCompleted;
            session.LoginStatusChanged += BridgeLoginStatusChanged;
            session.Ended += BridgeEnded;
            _bridge = session;
            _pairing = new PairingGuide(_selectedTopology);
            session.StartReading();
            ShowCurrentPairingStep();
        }
        catch (Exception error)
        {
            _pairingStatus.Text = $"手柄桥接未就绪：{error.Message}";
        }
        finally
        {
            _busy = false;
            UpdateBridgeControls();
        }
    }

    private void BridgeAReleased(int slot)
    {
        if (_pairing?.ObserveARelease(slot) != true) return;
        ShowCurrentPairingStep();
        UpdateBridgeControls();
    }

    private async Task ArmPairingAsync()
    {
        var current = _pairing?.Current;
        if (_bridge is null || current is null || _armRequested || _pairing!.Armed || _pairing.AReleased ||
            _launchedClients.Count != _selectedTopology?.ClientCount) return;
        var client = _launchedClients[current.Client - 1];
        _armRequested = true;
        _loginStatus = "waiting";
        UpdateBridgeControls();
        _pairingStatus.Text = "已发送当前角色的配对准备请求；等待桥接确认。";
        try
        {
            await _bridge.ArmPairingAsync(current.RuntimePad, client);
        }
        catch (Exception error)
        {
            await StopBridgeAsync();
            _pairingStatus.Text = $"无法准备配对：{error.Message}";
        }
    }

    private async void BridgePairingArmed(int slot, int pid, long windowHandle)
    {
        _armRequested = false;
        var current = _pairing?.Current;
        var expected = current is not null && _launchedClients.Count >= current.Client
            ? _launchedClients[current.Client - 1] : null;
        if (expected is null ||
            expected.Pid != pid || expected.WindowHandle != windowHandle ||
            _pairing?.Arm(slot) != true)
        {
            await StopBridgeAsync();
            _pairingStatus.Text = "桥接报告了非当前角色的配对槽位，已停止。";
            return;
        }
        _pairingHasStarted = true;
        ShowCurrentPairingStep();
        UpdateClientRows();
    }

    private void BridgeLoginStatusChanged(string source)
    {
        _loginStatus = source;
        ShowCurrentPairingStep();
    }

    private void BridgePairingCompleted()
    {
        if (_pairing?.IsComplete != true || !_pairingSettling) return;
        _pairingSettling = false;
        _pairingSettled = true;
        ShowCurrentPairingStep();
        UpdateFollowControls();
    }

    private async void BridgeEnded(string reason)
    {
        await StopBridgeAsync();
        _pairingStatus.Text = $"手柄桥接已停止：{reason}";
    }

    private async Task ConfirmRoleAsync()
    {
        if (_bridge is null || _pairing?.AReleased != true) return;
        try
        {
            var next = _pairing.ConfirmInGame();
            _loginStatus = "waiting";
            if (next is null)
            {
                _pairingSettling = true;
                await _bridge.FinishPairingAsync();
            }
            ShowCurrentPairingStep();
        }
        catch (Exception error)
        {
            await StopBridgeAsync();
            _pairingStatus.Text = $"无法切换下一角色：{error.Message}";
        }
        UpdateBridgeControls();
    }

    private void ShowCurrentPairingStep()
    {
        var current = _pairing?.Current;
        _pairingStatus.Text = current is null
            ? _pairingSettled
                ? "已观察到 400 毫秒手柄回中，且所有角色步骤由用户确认；软件尚未验证游戏内实际配对。手柄桥接仍在运行。"
                : "所有角色步骤已由用户确认；请松开所有手柄输入，等待 400 毫秒回中。"
            : _pairing!.AReleased
                ? $"已观察到 P{current.PartyPlayer} 对应虚拟手柄 A 键松开。请在游戏中选角并确认角色已加入，再继续。"
                : _pairing.Armed
                    ? _loginStatus switch
                    {
                        "visual" => $"客户端 {current.Client} 登录控件画面特征已连续命中。保持窗口前台并松开输入，稳定后按一次 A 键。",
                        "fallback_timeout" => $"客户端 {current.Client} 等待 20 秒仍未识别登录控件，已进入超时回退。请自行核对当前确是登录/选角画面，保持前台并松开输入，稳定后按一次 A 键。",
                        _ => $"正在检测客户端 {current.Client} 的登录控件画面；保持窗口前台，暂勿按 A 键。"
                    }
                    : _launchedClients.Count != _selectedTopology?.ClientCount
                        ? "请先在下方启动或绑定所有游戏窗口，取得窗口身份后再检测当前登录画面。"
                        : $"请打开客户端 {current.Client} 的登录/角色画面，再点“开始检测当前登录画面”。";
    }

    private async Task StopBridgeAsync()
    {
        await StopFollowAsync();
        var session = _bridge;
        _bridge = null;
        _pairing = null;
        _pairingHasStarted = false;
        _armRequested = false;
        _pairingSettling = false;
        _pairingSettled = false;
        _loginStatus = "waiting";
        UpdateFollowControls();
        UpdateClientRows();
        if (session is null) return;
        session.AReleased -= BridgeAReleased;
        session.PairingArmed -= BridgePairingArmed;
        session.PairingCompleted -= BridgePairingCompleted;
        session.LoginStatusChanged -= BridgeLoginStatusChanged;
        session.Ended -= BridgeEnded;
        await session.DisposeAsync();
        _pairingStatus.Text = "手柄桥接已停止，虚拟手柄已释放。";
    }

    private async Task StartFollowAsync()
    {
        if (_bridge is null || !_pairingSettled || _selectedTopology is null || _follow is not null || _followStopping ||
            !LiveFollowPlan.TryCreate(_followStop.Text, _followResume.Text, _followCatchup.Text,
                _followMaxAge.Text, _followLogAge.Text, _followLeaderName.Text, out var plan) || plan is null) return;
        var executable = LiveFollowSession.FindExecutable();
        if (executable is null)
        {
            _followStatus.Text = "找不到 PartyPilot-LiveFollow.exe；请使用完整发布目录。";
            return;
        }
        _busy = true;
        UpdateFollowControls();
        _followStatus.Text = "正在核对进程、日志与传感器输入…";
        try
        {
            var session = await LiveFollowSession.StartAsync(executable, _selectedTopology,
                _launchedClients, _bridge, plan);
            session.StatusChanged += FollowStatusChanged;
            session.Ended += FollowEnded;
            _follow = session;
            session.StartReading();
            FollowStatusChanged(session.Current);
        }
        catch (Exception error)
        {
            _followStatus.Text = $"跟随未启动：{error.Message}";
        }
        finally
        {
            _busy = false;
            UpdateFollowControls();
        }
    }

    private void FollowStatusChanged(FollowBridgeFrame frame)
    {
        _followStatus.Text = (frame.Status, frame.Reason) switch
        {
            ("following", _) => "已输出跟随方向；游戏内移动效果待验证。",
            ("catching_up", _) => "已输出追赶方向；游戏内移动效果待验证。",
            ("holding", _) => "跟随者已停靠，自动方向已停止。",
            ("waiting", "missing position") => "缺少角色位置样本；请检查两端传感器与进程绑定。自动方向已停止。",
            ("waiting", "different or unknown instance") => "两端尚无可用的同一区域实例证据；自动方向已停止。",
            ("waiting", "stale position") => "角色位置样本已过期；自动方向已停止。",
            ("waiting", "party disbanded") => "日志报告队伍解散，本次跟随已锁定停止；重新组队后再启动。",
            ("waiting", "local client left party") => "日志报告本地角色离队，本次跟随已锁定停止；重新组队后再启动。",
            ("waiting", "followed member left party") => "日志报告所跟随角色离队，本次跟随已锁定停止；重新组队后再启动。",
            _ => "等待新鲜位置、区域日志与队伍状态；自动方向已停止。"
        };
    }

    private async void FollowEnded(string reason)
    {
        await StopFollowAsync();
        _followStatus.Text = string.IsNullOrWhiteSpace(reason)
            ? "跟随进程已结束，自动方向已停止。"
            : $"跟随进程已结束，自动方向已停止：{reason}";
    }

    private async Task StopFollowAsync()
    {
        if (_followStopping) return;
        var session = _follow;
        if (session is null) return;
        _followStopping = true;
        _follow = null;
        UpdateFollowControls();
        try
        {
            session.StatusChanged -= FollowStatusChanged;
            session.Ended -= FollowEnded;
            await session.DisposeAsync();
            _followStatus.Text = "跟随已停止，自动输入已撤销。";
        }
        finally
        {
            _followStopping = false;
            UpdateFollowControls();
        }
    }

    private async Task RunLauncherAsync(ClientAction action)
    {
        if (_busy || _pairingHasStarted)
        {
            return;
        }
        if (!OperatingSystem.IsWindows())
        {
            _status.Text = "桌面界面可在 macOS 预览；游戏窗口启动、绑定和布局操作需要 Windows。";
            return;
        }
        var tool = FindLauncher();
        if (tool is null)
        {
            _status.Text = "找不到 PartyPilot-ClientLaunch.exe。请把桌面程序与 release 目录一起使用。";
            return;
        }
        var startInfo = new ProcessStartInfo(tool)
        {
            WorkingDirectory = Path.GetDirectoryName(tool)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (_selectedTopology is null)
        {
            _status.Text = "请先选择人数与角色分布。";
            return;
        }
        for (var index = 0; index < _selectedTopology.ClientCount; index++)
        {
            startInfo.ArgumentList.Add("-exe");
            startInfo.ArgumentList.Add(_paths[index].Text?.Trim() ?? string.Empty);
            var uri = _uris[index].Text?.Trim();
            if (action != ClientAction.Attach && !string.IsNullOrEmpty(uri))
            {
                startInfo.ArgumentList.Add("-uri");
                startInfo.ArgumentList.Add($"{index + 1}={uri}");
            }
        }
        if (_applyLayout.IsChecked == true)
        {
            startInfo.ArgumentList.Add("-apply-layout");
            startInfo.ArgumentList.Add("-mode");
            startInfo.ArgumentList.Add(_layoutMode.SelectedItem?.ToString() ?? "stacked");
            startInfo.ArgumentList.Add("-monitors");
            startInfo.ArgumentList.Add(_monitors.Text?.Trim() ?? string.Empty);
        }
        if (action == ClientAction.Check)
        {
            startInfo.ArgumentList.Add("-check");
        }
        else if (action == ClientAction.Attach)
        {
            startInfo.ArgumentList.Add("-attach");
        }

        _busy = true;
        if (action != ClientAction.Check) _launchedClients = [];
        UpdateClientRows();
        _status.Text = action switch
        {
            ClientAction.Start => "正在等待游戏窗口…",
            ClientAction.Attach => "正在识别已运行游戏窗口…",
            _ => "正在检查配置…"
        };
        try
        {
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("启动器未能启动");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await Task.WhenAll(output, error);
            if (action != ClientAction.Check && process.ExitCode == 0)
            {
                try
                {
                    _launchedClients = LaunchedClients.ParseSuccessfulLaunch(output.Result, _selectedTopology.ClientCount);
                    if (_pairing is not null) ShowCurrentPairingStep();
                }
                catch (Exception bindingError)
                {
                    _status.Text = action == ClientAction.Start
                        ? $"游戏可能已启动，但无法核对其窗口身份：{bindingError.Message}"
                        : $"已运行窗口的身份信息无效：{bindingError.Message}";
                    return;
                }
            }
            var message = new StringBuilder();
            message.Append(process.ExitCode == 0 ? "成功" : $"失败（退出码 {process.ExitCode}）");
            if (!string.IsNullOrWhiteSpace(output.Result)) message.AppendLine().Append(output.Result.Trim());
            if (!string.IsNullOrWhiteSpace(error.Result)) message.AppendLine().Append(error.Result.Trim());
            _status.Text = message.ToString();
        }
        catch (Exception exception)
        {
            _status.Text = $"启动器调用失败：{exception.Message}";
        }
        finally
        {
            _busy = false;
            UpdateClientRows();
        }
    }

    private static string? FindLauncher()
    {
        var file = "PartyPilot-ClientLaunch.exe";
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, file),
            Path.Combine(AppContext.BaseDirectory, "release", file),
            Path.Combine(Environment.CurrentDirectory, "release", file),
            Path.Combine(AppContext.BaseDirectory, "..", file)
        };
        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
    }
}
