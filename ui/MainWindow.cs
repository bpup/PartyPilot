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
    private readonly Button _hideNames = new();
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
    private readonly TextBox _physicalDevice = new();
    private readonly CheckBox _isolationConfirmed = new();
    private readonly CheckBox _rightStickFollowerOverride = new();
    private readonly CheckBox _autoFlask = new();
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
    private readonly TextBox _radarScale = new();
    private readonly Button _startRadar = new();
    private readonly Button _stopRadar = new();
    private readonly TextBlock _radarStatus = new();
    private readonly TextBox _privacyBoxes = new();
    private readonly CheckBox _privacyAutoNames = new();
    private readonly Button _startPrivacy = new();
    private readonly Button _stopPrivacy = new();
    private readonly TextBlock _privacyStatus = new();
    private readonly StackPanel[] _fpsRows = new StackPanel[3];
    private readonly TextBox[] _fpsPaths = new TextBox[3];
    private readonly TextBox[] _fpsForeground = new TextBox[3];
    private readonly TextBox[] _fpsBackground = new TextBox[3];
    private readonly Button[] _fpsPreview = new Button[3];
    private readonly Button[] _fpsApply = new Button[3];
    private readonly TextBlock[] _fpsResults = new TextBlock[3];
    private readonly CheckBox _fpsGamesClosed = new();
    private readonly StackPanel[] _resourceRows = new StackPanel[3];
    private readonly TextBox[] _resourceCpu = new TextBox[3];
    private readonly TextBox[] _resourceAffinity = new TextBox[3];
    private readonly TextBox[] _resourceRamWarning = new TextBox[3];
    private readonly TextBox[] _resourceRamLimit = new TextBox[3];
    private readonly Button[] _resourcePreview = new Button[3];
    private readonly Button[] _resourceStart = new Button[3];
    private readonly Button[] _resourceStop = new Button[3];
    private readonly TextBlock[] _resourceResults = new TextBlock[3];
    private readonly ResourcePolicySession?[] _resourceSessions = new ResourcePolicySession?[3];
    private readonly bool[] _resourceBusy = new bool[3];
    private PartyTopology? _selectedTopology;
    private BridgeSession? _bridge;
    private LiveFollowSession? _follow;
    private RadarSession? _radar;
    private PrivacyMaskSession? _privacy;
    private PairingGuide? _pairing;
    private IReadOnlyList<LaunchedClient> _launchedClients = [];
    private bool _pairingHasStarted;
    private bool _armRequested;
    private bool _pairingSettling;
    private bool _pairingSettled;
    private bool _followStopping;
    private bool _radarStopping;
    private bool _fpsBusy;
    private bool _closing;
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
        var heading = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 16 };
        heading.Children.Add(Label("Connect. Choose. Start playing.", 29, Brushes.White, FontWeight.Bold));
        _hideNames.Content = "Hide names";
        _hideNames.Click += (_, _) => NamePrivacy.Enabled = !NamePrivacy.Enabled;
        heading.Children.Add(_hideNames);
        content.Children.Add(heading);
        content.Children.Add(Label("独立版工作区 · 游戏窗口启动与有限布局的手柄配对引导", 13, MutedBrush));
        content.Children.Add(BuildInputStep());
        content.Children.Add(BuildClientStep());
        content.Children.Add(BuildLaunchStep());
        content.Children.Add(BuildFollowStep());
        content.Children.Add(BuildRadarStep());
        content.Children.Add(BuildPrivacyStep());
        content.Children.Add(BuildFpsStep());
        content.Children.Add(BuildResourceStep());
        content.Children.Add(BuildStatusCard());

        shell.Children.Add(new ScrollViewer { Content = content });
        Content = shell;
        _playerCount.SelectionChanged += (_, _) => UpdateTopologyOptions();
        _topologyVariant.SelectionChanged += (_, _) => UpdateClientRows();
        _applyLayout.IsCheckedChanged += (_, _) => UpdateLayoutControls();
        _isolationConfirmed.IsCheckedChanged += (_, _) => UpdateBridgeControls();
        _physicalIndex.SelectionChanged += (_, _) => UpdateBridgeControls();
        _physicalDevice.TextChanged += (_, _) => UpdateBridgeControls();
        Closed += async (_, _) =>
        {
            NamePrivacy.Changed -= UpdateNamePrivacy;
            _closing = true;
            await StopAllResourcesAsync();
            await StopRadarAsync();
            StopPrivacy();
            await StopBridgeAsync();
        };
        NamePrivacy.Changed += UpdateNamePrivacy;
        UpdateNamePrivacy();
        UpdateClientRows();
        UpdateLayoutControls();
    }

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));

    private void UpdateNamePrivacy()
    {
        _hideNames.Content = NamePrivacy.Enabled ? "Names hidden" : "Hide names";
        _followLeaderName.PasswordChar = NamePrivacy.Enabled ? '•' : '\0';
    }

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
        var panel = Step("1", "CHOOSE INPUT · 选择输入", "四人双客户端布局由 Windows Gaming Input 读取唯一匹配的实体手柄；其他可配对布局使用所选 XInput 编号。需先完成实体手柄对游戏的隔离。");
        _physicalIndex.PlaceholderText = "实体手柄 XInput 编号";
        _physicalIndex.ItemsSource = Enumerable.Range(0, 4).Select(index => $"XInput {index}").ToArray();
        _physicalIndex.Width = 190;
        _physicalIndex.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(_physicalIndex);
        _physicalDevice.Watermark = "HidHide 中已隐藏的实体手柄设备 ID";
        panel.Children.Add(_physicalDevice);
        panel.Children.Add(Label("从 HidHide 设备列表复制当前实体手柄的设备 ID；程序会读取隔离配置，但设备与 XInput 编号的对应仍需实机核对。", 12, MutedBrush));
        _isolationConfirmed.Content = "我已确认实体手柄对游戏隐藏，仅由桥接读取";
        panel.Children.Add(_isolationConfirmed);
        _rightStickFollowerOverride.Content = "右摇杆暂时控制可活动的跟随角色（主角色不再使用右摇杆瞄准）";
        panel.Children.Add(_rightStickFollowerOverride);
        _autoFlask.Content = "随实时跟随启用自动药剂（仅双客户端各一名角色；生命/魔力须绑定手柄十字键左/右）";
        panel.Children.Add(_autoFlask);
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

    private Border BuildRadarStep()
    {
        var panel = Step("5", "RADAR · 雷达", "在已绑定游戏窗口上显示只读实体标记；请在对应 ExileCore2 实例中启用传感器的雷达诊断。坐标方向与比例仍需游戏内校准。");
        panel.Children.Add(Label("每个游戏坐标单位对应的屏幕像素（需明确填写）", 12, MutedBrush));
        _radarScale.Watermark = "像素/坐标单位";
        _radarScale.Width = 190;
        _radarScale.HorizontalAlignment = HorizontalAlignment.Left;
        _radarScale.TextChanged += (_, _) => UpdateRadarControls();
        panel.Children.Add(_radarScale);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 12 };
        _startRadar.Content = "显示雷达";
        _startRadar.Click += (_, _) => StartRadar();
        _stopRadar.Content = "关闭雷达";
        _stopRadar.Click += async (_, _) => await StopRadarAsync();
        actions.Children.Add(_startRadar);
        actions.Children.Add(_stopRadar);
        panel.Children.Add(actions);
        _radarStatus.Text = "等待已绑定的游戏窗口与明确的比例。";
        _radarStatus.Foreground = MutedBrush;
        _radarStatus.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_radarStatus);
        return Card(panel);
    }

    private Border BuildPrivacyStep()
    {
        var panel = Step("6", "PRIVACY · 游戏画面遮挡",
            "在已绑定游戏窗口的指定区域显示模糊遮挡；窗口画面无法捕获时保留黑色遮挡。仅当前游戏窗口在前台时显示，暂不自动识别姓名牌。");
        panel.Children.Add(Label("每行：客户端编号, 左边距%, 上边距%, 宽%, 高%；最多 16 行，例如 1,10,5,20,6。", 12, MutedBrush));
        _privacyAutoNames.Content = "自动识别并遮挡游戏画面中的姓名牌（需启用各客户端传感器）";
        _privacyAutoNames.IsCheckedChanged += (_, _) => UpdatePrivacyControls();
        panel.Children.Add(_privacyAutoNames);
        _privacyBoxes.AcceptsReturn = true;
        _privacyBoxes.MinHeight = 76;
        _privacyBoxes.Watermark = "1,10,5,20,6";
        _privacyBoxes.TextChanged += (_, _) => UpdatePrivacyControls();
        panel.Children.Add(_privacyBoxes);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 12 };
        _startPrivacy.Content = "显示遮挡";
        _startPrivacy.Click += (_, _) => StartPrivacy();
        _stopPrivacy.Content = "关闭遮挡";
        _stopPrivacy.Click += (_, _) => StopPrivacy();
        actions.Children.Add(_startPrivacy);
        actions.Children.Add(_stopPrivacy);
        panel.Children.Add(actions);
        _privacyStatus.Text = "等待已绑定游戏窗口，以及手动区域或已启用的自动姓名牌选项。";
        _privacyStatus.Foreground = MutedBrush;
        _privacyStatus.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_privacyStatus);
        return Card(panel);
    }

    private Border BuildFpsStep()
    {
        var panel = Step("6", "PERFORMANCE · FPS", "按客户端分别预览前台与后台帧率配置。仅修改明确指定的 PoE2 配置文件；应用前需关闭所有游戏窗口。");
        _fpsGamesClosed.Content = "应用前已关闭所有 PoE2 游戏客户端";
        _fpsGamesClosed.IsCheckedChanged += (_, _) => UpdateFpsControls();
        panel.Children.Add(_fpsGamesClosed);
        for (var index = 0; index < 3; index++)
        {
            var client = index;
            var row = new StackPanel { Spacing = 7, Margin = new Thickness(0, 8, 0, 0) };
            row.Children.Add(Label($"客户端 {client + 1} · FPS 配置", 13, Brushes.White, FontWeight.SemiBold));
            row.Children.Add(Label("该客户端的 poe2_production_Config.ini 完整路径", 12, MutedBrush));
            var path = new TextBox { Watermark = "配置文件绝对路径" };
            path.TextChanged += (_, _) => UpdateFpsControls();
            row.Children.Add(path);
            var modes = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 12 };
            var foreground = new TextBox { Text = "keep", Width = 180, Watermark = "前台：keep / unlimited / 帧数" };
            var background = new TextBox { Text = "keep", Width = 180, Watermark = "后台：keep / unlimited / 帧数" };
            foreground.TextChanged += (_, _) => UpdateFpsControls();
            background.TextChanged += (_, _) => UpdateFpsControls();
            var foregroundField = new StackPanel { Spacing = 4 };
            foregroundField.Children.Add(Label("前台 FPS", 12, MutedBrush));
            foregroundField.Children.Add(foreground);
            var backgroundField = new StackPanel { Spacing = 4 };
            backgroundField.Children.Add(Label("后台 FPS", 12, MutedBrush));
            backgroundField.Children.Add(background);
            modes.Children.Add(foregroundField);
            modes.Children.Add(backgroundField);
            row.Children.Add(modes);
            row.Children.Add(Label("前台 / 后台：keep 保持原值；unlimited 或 0 不限帧；正整数为目标帧数。", 12, MutedBrush));
            var actions = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 12 };
            var preview = new Button { Content = "预览变更" };
            var apply = new Button { Content = "备份并应用" };
            preview.Click += async (_, _) => await RunFpsAsync(client, false);
            apply.Click += async (_, _) => await RunFpsAsync(client, true);
            actions.Children.Add(preview);
            actions.Children.Add(apply);
            row.Children.Add(actions);
            var result = Label("尚未预览。", 12, MutedBrush);
            row.Children.Add(result);
            _fpsRows[index] = row;
            _fpsPaths[index] = path;
            _fpsForeground[index] = foreground;
            _fpsBackground[index] = background;
            _fpsPreview[index] = preview;
            _fpsApply[index] = apply;
            _fpsResults[index] = result;
            panel.Children.Add(row);
        }
        return Card(panel);
    }

    private Border BuildResourceStep()
    {
        var panel = Step("7", "PERFORMANCE · RESOURCES",
            "按已绑定客户端预览并显式启用后台资源策略；切回前台时恢复。0 表示不设置该项。");
        for (var index = 0; index < 3; index++)
        {
            var client = index;
            var row = new StackPanel { Spacing = 7, Margin = new Thickness(0, 8, 0, 0) };
            row.Children.Add(Label($"客户端 {index + 1} · 后台资源", 13, Brushes.White, FontWeight.SemiBold));
            var fields = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 12 };
            TextBox Field(string title, string watermark, int width)
            {
                var box = new TextBox { Text = "0", Width = width, Watermark = watermark };
                box.TextChanged += (_, _) => UpdateResourceControls();
                var container = new StackPanel { Spacing = 4 };
                container.Children.Add(Label(title, 12, MutedBrush));
                container.Children.Add(box);
                fields.Children.Add(container);
                return box;
            }
            _resourceCpu[index] = Field("后台 CPU %", "0–100", 110);
            _resourceAffinity[index] = Field("处理器掩码", "0 或 0x3", 130);
            _resourceRamWarning[index] = Field("RAM 提醒 MB", "0", 130);
            _resourceRamLimit[index] = Field("后台 RAM 上限 MB", "0", 155);
            row.Children.Add(fields);
            var actions = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 12 };
            var preview = new Button { Content = "预览策略" };
            var start = new Button { Content = "启用后台策略" };
            var stop = new Button { Content = "停止并恢复" };
            preview.Click += async (_, _) => await RunResourceAsync(client, false);
            start.Click += async (_, _) => await RunResourceAsync(client, true);
            stop.Click += async (_, _) => await StopResourceAsync(client);
            actions.Children.Add(preview);
            actions.Children.Add(start);
            actions.Children.Add(stop);
            row.Children.Add(actions);
            var result = Label("等待已绑定游戏进程和明确的策略。", 12, MutedBrush);
            row.Children.Add(result);
            _resourceRows[index] = row;
            _resourcePreview[index] = preview;
            _resourceStart[index] = start;
            _resourceStop[index] = stop;
            _resourceResults[index] = result;
            panel.Children.Add(row);
        }
        return Card(panel);
    }

    private void UpdateClientRows()
    {
        var selection = _topologyVariant.SelectedItem as PartyTopology;
        var topologyChanged = !ReferenceEquals(_selectedTopology, selection);
        if (topologyChanged)
        {
            _launchedClients = [];
            _ = StopRadarAsync();
            StopPrivacy();
            _ = StopAllResourcesAsync();
        }
        _selectedTopology = selection;
        if (_bridge is null && topologyChanged) _autoFlask.IsChecked = selection?.Id == "2-split";
        var count = _selectedTopology?.ClientCount ?? 0;
        _assignment.Text = _selectedTopology is null
            ? "选择人数后显示角色与客户端分布。"
            : $"{_selectedTopology.PlayerCount} 人 · {count} 个客户端 · {_selectedTopology.Summary}";
        for (var index = 0; index < _clientRows.Length; index++)
        {
            _clientRows[index].IsVisible = index < count;
            _fpsRows[index].IsVisible = index < count;
            _resourceRows[index].IsVisible = index < count;
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
        UpdateRadarControls();
        UpdatePrivacyControls();
        UpdateFpsControls();
        UpdateResourceControls();
    }

    private void ClearLaunchedClients()
    {
        _launchedClients = [];
        _ = StopRadarAsync();
        StopPrivacy();
        _ = StopAllResourcesAsync();
        if (_pairing is not null) ShowCurrentPairingStep();
        UpdateBridgeControls();
        UpdateRadarControls();
        UpdatePrivacyControls();
        UpdateResourceControls();
    }

    private void UpdateRadarControls()
    {
        var ready = _selectedTopology is not null &&
            _launchedClients.Count == _selectedTopology.ClientCount &&
            RadarProjection.TryScale(_radarScale.Text, out _);
        _startRadar.IsEnabled = OperatingSystem.IsWindows() && !_busy && !_radarStopping &&
            _radar is null && ready;
        _stopRadar.IsEnabled = _radar is not null;
    }

    private void UpdatePrivacyControls()
    {
        var parsed = string.IsNullOrWhiteSpace(_privacyBoxes.Text) ||
            PrivacyMaskPlan.TryParse(_privacyBoxes.Text, _selectedTopology?.ClientCount ?? 0, out _);
        var ready = _selectedTopology is not null &&
            _launchedClients.Count == _selectedTopology.ClientCount &&
            parsed && (_privacyAutoNames.IsChecked == true ||
                !string.IsNullOrWhiteSpace(_privacyBoxes.Text));
        _startPrivacy.IsEnabled = OperatingSystem.IsWindows() && !_busy && _privacy is null && ready;
        _stopPrivacy.IsEnabled = _privacy is not null;
        _privacyBoxes.IsEnabled = _privacy is null;
        _privacyAutoNames.IsEnabled = _privacy is null;
    }

    private void StartPrivacy()
    {
        if (_privacy is not null || _busy || !OperatingSystem.IsWindows() ||
            _selectedTopology is null || _launchedClients.Count != _selectedTopology.ClientCount) return;
        IReadOnlyList<PrivacyMaskBox> boxes = [];
        if (!string.IsNullOrWhiteSpace(_privacyBoxes.Text) &&
            !PrivacyMaskPlan.TryParse(_privacyBoxes.Text, _selectedTopology.ClientCount, out boxes)) return;
        var autoNames = _privacyAutoNames.IsChecked == true;
        if (boxes.Count == 0 && !autoNames) return;
        try
        {
            _privacy = PrivacyMaskSession.Start(_launchedClients, boxes, autoNames);
            _privacy.Ended += PrivacyEnded;
            _privacyStatus.Text = $"已启用 {boxes.Count} 个手动区域" +
                (autoNames ? "和自动姓名牌检测" : "") + "；截图失败时保留黑色遮挡，失焦时隐藏。";
        }
        catch (Exception error) { _privacyStatus.Text = $"遮挡未能打开：{error.Message}"; }
        UpdatePrivacyControls();
    }

    private void PrivacyEnded(string reason)
    {
        StopPrivacy();
        _privacyStatus.Text = $"遮挡已停止：{reason}";
    }

    private void StopPrivacy()
    {
        if (_privacy is null) return;
        _privacy.Ended -= PrivacyEnded;
        _privacy.Dispose();
        _privacy = null;
        _privacyStatus.Text = "遮挡已关闭。";
        UpdatePrivacyControls();
    }

    private void UpdateFpsControls()
    {
        for (var index = 0; index < _fpsRows.Length; index++)
        {
            if (_fpsRows[index] is null) continue;
            var ready = FpsConfigPlan.TryCreate(_fpsPaths[index].Text,
                _fpsForeground[index].Text, _fpsBackground[index].Text, out _);
            _fpsPreview[index].IsEnabled = OperatingSystem.IsWindows() && !_busy && !_fpsBusy &&
                _fpsRows[index].IsVisible && ready;
            _fpsApply[index].IsEnabled = _fpsPreview[index].IsEnabled && _fpsGamesClosed.IsChecked == true;
        }
    }

    private void UpdateResourceControls()
    {
        for (var index = 0; index < _resourceRows.Length; index++)
        {
            if (_resourceRows[index] is null) continue;
            var bound = _selectedTopology is not null &&
                _launchedClients.Count == _selectedTopology.ClientCount &&
                index < _launchedClients.Count;
            var ready = bound && ResourcePolicyPlan.TryCreate(_launchedClients[index].Pid,
                _resourceCpu[index].Text, _resourceAffinity[index].Text,
                _resourceRamWarning[index].Text, _resourceRamLimit[index].Text, out _);
            var idle = !_resourceBusy[index] && _resourceSessions[index] is null;
            _resourcePreview[index].IsEnabled = OperatingSystem.IsWindows() && !_busy &&
                !_closing && _resourceRows[index].IsVisible && idle && ready;
            _resourceStart[index].IsEnabled = _resourcePreview[index].IsEnabled;
            _resourceStop[index].IsEnabled = _resourceSessions[index] is not null && !_resourceBusy[index];
        }
    }

    private async Task RunResourceAsync(int index, bool apply)
    {
        if (!OperatingSystem.IsWindows() || _busy || _closing || index < 0 || index >= 3 ||
            _resourceBusy[index] || _resourceSessions[index] is not null ||
            _selectedTopology is null || _launchedClients.Count != _selectedTopology.ClientCount ||
            index >= _launchedClients.Count ||
            !ResourcePolicyPlan.TryCreate(_launchedClients[index].Pid,
                _resourceCpu[index].Text, _resourceAffinity[index].Text,
                _resourceRamWarning[index].Text, _resourceRamLimit[index].Text, out var plan) ||
            plan is null) return;
        var tool = FindTool("PartyPilot-ResourceWatch.exe");
        if (tool is null)
        {
            _resourceResults[index].Text = "找不到 PartyPilot-ResourceWatch.exe；请使用完整预览包。";
            return;
        }
        _resourceBusy[index] = true;
        UpdateResourceControls();
        _resourceResults[index].Text = apply ? "正在预检并启动后台资源管理…" : "正在预览后台资源策略…";
        try
        {
            await ResourcePolicyTool.PreviewAsync(tool, plan);
            if (_closing) throw new InvalidOperationException("窗口已关闭。");
            if (apply)
            {
                if (_launchedClients.Count <= index || _launchedClients[index].Pid != plan.Pid)
                    throw new InvalidOperationException("游戏进程绑定已变化。");
                var session = await ResourcePolicySession.StartAsync(tool, plan);
                if (_closing || _launchedClients.Count <= index || _launchedClients[index].Pid != plan.Pid)
                {
                    await session.DisposeAsync();
                    throw new InvalidOperationException("游戏进程绑定已变化。");
                }
                _resourceSessions[index] = session;
                _resourceResults[index].Text = $"已启动 PID {plan.Pid} 的后台资源管理；实际限制与恢复待 Windows 游戏实测。";
                _ = ObserveResourceEndAsync(index, session);
            }
            else
            {
                _resourceResults[index].Text = $"预览通过：PID {plan.Pid}；CPU {plan.CpuPercent}%、掩码 0x{plan.AffinityMask:X}、RAM 提醒 {plan.RamWarningMb} MB、后台上限 {plan.RamLimitMb} MB。未应用。";
            }
        }
        catch (Exception exception)
        {
            _resourceResults[index].Text = $"资源策略未完成：{exception.Message}";
        }
        finally
        {
            _resourceBusy[index] = false;
            UpdateResourceControls();
        }
    }

    private async Task ObserveResourceEndAsync(int index, ResourcePolicySession session)
    {
        var (code, detail) = await session.Completion;
        if (!ReferenceEquals(_resourceSessions[index], session)) return;
        _resourceSessions[index] = null;
        _resourceResults[index].Text = code == 0
            ? $"PID {session.Pid} 的资源管理已结束；恢复流程已返回。"
            : $"PID {session.Pid} 的资源管理异常结束（{code}）：{detail ?? "无诊断信息"}";
        UpdateResourceControls();
        try { await session.DisposeAsync(); }
        catch (Exception exception)
        {
            _resourceResults[index].Text += $" 关闭结果待核对：{exception.Message}";
        }
    }

    private async Task StopResourceAsync(int index)
    {
        if (index < 0 || index >= 3 || _resourceBusy[index]) return;
        var session = _resourceSessions[index];
        if (session is null) return;
        _resourceBusy[index] = true;
        _resourceSessions[index] = null;
        UpdateResourceControls();
        try
        {
            await session.DisposeAsync();
            _resourceResults[index].Text = "资源管理进程已停止并完成恢复流程；Windows 实际限制仍待实测。";
        }
        catch (Exception exception)
        {
            _resourceResults[index].Text = $"资源管理停止状态待核对：{exception.Message}";
        }
        finally
        {
            _resourceBusy[index] = false;
            UpdateResourceControls();
        }
    }

    private async Task StopAllResourcesAsync()
    {
        for (var index = 0; index < 3; index++) await StopResourceAsync(index);
    }

    private async Task RunFpsAsync(int index, bool apply)
    {
        if (!OperatingSystem.IsWindows() || _busy || _fpsBusy ||
            index < 0 || index >= _fpsRows.Length || !_fpsRows[index].IsVisible ||
            !FpsConfigPlan.TryCreate(_fpsPaths[index].Text,
                _fpsForeground[index].Text, _fpsBackground[index].Text, out var plan) || plan is null) return;
        if (apply && (_fpsGamesClosed.IsChecked != true || KnownGameIsRunning()))
        {
            _fpsResults[index].Text = "请先关闭所有 PoE2 游戏客户端，再应用配置。";
            return;
        }
        if (apply)
        {
            for (var other = 0; other < _fpsRows.Length; other++)
            {
                if (other == index || !_fpsRows[other].IsVisible ||
                    !FpsConfigPlan.TryCreate(_fpsPaths[other].Text,
                        _fpsForeground[other].Text, _fpsBackground[other].Text, out var otherPlan) ||
                    otherPlan is null || !plan.ConflictsWith(otherPlan)) continue;
                _fpsResults[index].Text = $"客户端 {index + 1} 与 {other + 1} 指向同一配置文件，却填写了不同的帧率模式。";
                return;
            }
        }
        var tool = FindTool("PartyPilot-FpsConfig.exe");
        if (tool is null)
        {
            _fpsResults[index].Text = "找不到 PartyPilot-FpsConfig.exe；请使用完整预览包。";
            return;
        }
        _fpsBusy = true;
        UpdateFpsControls();
        _fpsResults[index].Text = apply ? "正在备份并应用 FPS 配置…" : "正在预览 FPS 配置…";
        try
        {
            var result = await FpsConfigTool.RunAsync(tool, plan, apply);
            _fpsResults[index].Text = result.ChangedKeys.Count == 0
                ? "所选前台和后台 FPS 配置已保持原值或无需修改。"
                : result.Applied
                    ? $"已修改 {string.Join("、", result.ChangedKeys)}；原文件备份：{result.Backup}"
                    : $"预览：将修改 {string.Join("、", result.ChangedKeys)}；尚未写入文件。";
        }
        catch (Exception exception)
        {
            _fpsResults[index].Text = $"FPS 配置未完成：{exception.Message}";
        }
        finally
        {
            _fpsBusy = false;
            UpdateFpsControls();
        }
    }

    private static bool KnownGameIsRunning()
    {
        try
        {
            foreach (var name in new[] { "PathOfExile", "PathOfExile_x64", "PathOfExileSteam",
                "PathOfExile_x64Steam", "PathOfExileEGS", "PathOfExile_x64EGS" })
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    using (process)
                    {
                        if (!process.HasExited) return true;
                    }
                }
            }
            return false;
        }
        catch
        {
            // If process inspection fails, do not claim the game is closed.
            return true;
        }
    }

    private void StartRadar()
    {
        if (_radar is not null || _busy || _radarStopping || !OperatingSystem.IsWindows() ||
            _selectedTopology is null || _launchedClients.Count != _selectedTopology.ClientCount ||
            !RadarProjection.TryScale(_radarScale.Text, out var scale)) return;
        try
        {
            var session = RadarSession.Start(_launchedClients, scale);
            session.Ended += RadarEnded;
            session.FrameReceived += RadarFrameReceived;
            _radar = session;
            _radarStatus.Text = "雷达已打开；等待各游戏进程的只读标记。失焦或样本过期会隐藏标记。";
        }
        catch (Exception exception)
        {
            _radarStatus.Text = $"雷达未能打开：{exception.Message}";
        }
        UpdateRadarControls();
    }

    private async void RadarEnded(string reason)
    {
        await StopRadarAsync();
        _radarStatus.Text = $"雷达已停止：{reason}";
    }

    private void RadarFrameReceived(int pid, int count)
    {
        _radarStatus.Text = $"已收到游戏进程 {pid} 的 {count} 个可见实体标记；仅当前游戏窗口在前台时显示。";
    }

    private async Task StopRadarAsync()
    {
        if (_radarStopping || _radar is null) return;
        _radarStopping = true;
        var session = _radar;
        _radar = null;
        UpdateRadarControls();
        try
        {
            session.Ended -= RadarEnded;
            session.FrameReceived -= RadarFrameReceived;
            await session.DisposeAsync();
            _radarStatus.Text = "雷达已关闭。";
        }
        catch (Exception exception)
        {
            _radarStatus.Text = $"雷达关闭时发生错误：{exception.Message}";
        }
        finally
        {
            _radarStopping = false;
            UpdateRadarControls();
        }
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
        var slots = _selectedTopology?.Slots.Where(slot => slot.Client != 3)
            .Select(slot => slot.RuntimePad).ToArray() ?? [];
        var useWgi = slots.Length == 4;
        var privateReady = _selectedTopology?.ClientCount != 3 ||
            PrivateClient3Plan.TryCreate(_selectedTopology, _launchedClients, out _);
        var supported = slots.All(slot => slot is >= 1 and <= 4) &&
            (slots.Length is >= 1 and <= 3 || useWgi) && privateReady;
        var gamePathsReady = _selectedTopology is not null &&
            Enumerable.Range(0, _selectedTopology.ClientCount)
                .All(index => !string.IsNullOrWhiteSpace(_paths[index]?.Text));
        _prepareBridge.IsEnabled = !_busy && _bridge is null && supported &&
            (useWgi || _physicalIndex.SelectedIndex is >= 0 and <= 3) &&
            _isolationConfirmed.IsChecked == true &&
            !string.IsNullOrWhiteSpace(_physicalDevice.Text) && gamePathsReady;
        _stopBridge.IsEnabled = _bridge is not null;
        _armPairing.IsEnabled = _bridge is not null && _pairing?.Current is not null &&
            _launchedClients.Count == _selectedTopology?.ClientCount &&
            _pairing.Armed == false && _pairing.AReleased == false && !_armRequested && !_busy;
        _confirmRole.IsEnabled = _bridge is not null && _pairing?.AReleased == true;
        _playerCount.IsEnabled = _bridge is null && !_busy;
        _topologyVariant.IsEnabled = _bridge is null && !_busy;
        _physicalIndex.IsEnabled = _bridge is null && !_busy && !useWgi;
        _physicalDevice.IsEnabled = _bridge is null && !_busy;
        _isolationConfirmed.IsEnabled = _bridge is null && !_busy;
        _rightStickFollowerOverride.IsEnabled = _bridge is null && !_busy;
        _autoFlask.IsEnabled = _bridge is null && !_busy && _selectedTopology?.Id == "2-split";
        for (var index = 0; index < _paths.Length; index++)
        {
            if (_paths[index] is not null) _paths[index].IsEnabled = _bridge is null && !_pairingHasStarted && !_busy;
            if (_uris[index] is not null) _uris[index].IsEnabled = _bridge is null && !_pairingHasStarted && !_busy;
        }
        if (_bridge is null && _selectedTopology is not null && !supported)
        {
            _pairingStatus.Text = "此布局需要先启动或绑定三个游戏窗口，并核对第三客户端进程身份；当前尚未开始配对。";
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
        var slots = _selectedTopology.Slots.Where(slot => slot.Client != 3)
            .Select(slot => slot.RuntimePad).ToArray();
        var useWgi = slots.Length == 4;
        PrivateClient3Plan? privatePlan = null;
        if (_selectedTopology.ClientCount == 3 &&
            !PrivateClient3Plan.TryCreate(_selectedTopology, _launchedClients, out privatePlan))
        {
            _pairingStatus.Text = "请先启动或绑定三个游戏窗口，核对第三客户端的进程与窗口身份。";
            return;
        }
        if (slots.Length is < 1 or > 4 || slots.Any(slot => slot is < 1 or > 4) ||
            (slots.Length == 4 && !useWgi) ||
            (!useWgi && _physicalIndex.SelectedIndex is < 0 or > 3) ||
            _isolationConfirmed.IsChecked != true ||
            string.IsNullOrWhiteSpace(_physicalDevice.Text))
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
        _pairingStatus.Text = "正在检查 HidHide 隔离、实体手柄与虚拟手柄编号…";
        try
        {
            var gameExecutables = Enumerable.Range(0, _selectedTopology.ClientCount)
                .Select(index => _paths[index].Text?.Trim() ?? "").ToArray();
            var session = await BridgeSession.StartAsync(executable, _selectedTopology.Slots, _physicalIndex.SelectedIndex,
                _physicalDevice.Text.Trim(), gameExecutables, _rightStickFollowerOverride.IsChecked == true,
                _selectedTopology.Id == "2-split" && _autoFlask.IsChecked == true,
                useWgi, privatePlan);
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
            ("waiting", "dead follower") => "传感器报告跟随角色生命值为零；自动与右摇杆方向已停止。",
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

        if (action != ClientAction.Check)
        {
            _fpsGamesClosed.IsChecked = false;
            await StopRadarAsync();
            StopPrivacy();
            await StopAllResourcesAsync();
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
        return FindTool("PartyPilot-ClientLaunch.exe");
    }

    private static string? FindTool(string file)
    {
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
