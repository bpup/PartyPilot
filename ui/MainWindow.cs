using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PartyPilot.Common;

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
    private readonly TextBox[] _attachPids = new TextBox[3];
    private readonly TextBlock[] _clientLabels = new TextBlock[3];
    private readonly StackPanel[] _clientRows = new StackPanel[3];
    private readonly AccountProfileStore _accountProfiles = new();
    private readonly StackPanel[] _accountRows = new StackPanel[3];
    private readonly TextBlock[] _accountResults = new TextBlock[3];
    private readonly bool[] _accountPresent = new bool[3];
    private readonly Button[] _accountSaveButtons = new Button[3];
    private readonly Button[] _accountDeleteButtons = new Button[3];
    private readonly LoginCalibrationStore _loginCalibrations = new();
    private readonly Button[] _calibrateLogin = new Button[3];
    private readonly TextBlock[] _calibrationResults = new TextBlock[3];
    private readonly AutoLoginPreferenceStore _autoLoginPreferences = new();
    private readonly CheckBox[] _autoLoginChoices = new CheckBox[3];
    private readonly TextBlock[] _autoLoginResults = new TextBlock[3];
    private readonly bool[] _autoLoginPreferenceReady = new bool[3];
    private readonly LaunchedClient?[] _preparedAutoLoginWindows = new LaunchedClient?[3];
    private readonly LaunchedClient?[] _manualAutoLoginWindows = new LaunchedClient?[3];
    private readonly Button _check = new();
    private readonly Button _launch = new();
    private readonly Button _attach = new();
    private readonly Button _cancelAutoLogin = new();
    private readonly Button _probeRoster = new();
    private readonly TextBlock _rosterStatus = new();
    private readonly StackPanel[] _inviteRows = new StackPanel[6];
    private readonly TextBox[] _inviteNames = new TextBox[6];
    private readonly ComboBox _inviteTarget = new();
    private readonly Button _sendInvite = new();
    private readonly TextBlock _inviteStatus = new();
    private readonly Button _runAutoParty = new();
    private readonly Button _stopAutoParty = new();
    private readonly ComboBox _autoPartyKey = new();
    private readonly Button _saveAutoPartyKey = new();
    private readonly TextBlock _autoPartyKeyStatus = new();
    private readonly GameShortcutPreferenceStore _autoPartyKeyPreferences =
        new("F8", "auto-party-key.json");
    private string _autoPartyKeyValue = "F8";
    private readonly ComboBox _followPauseKey = new();
    private readonly Button _saveFollowPauseKey = new();
    private readonly TextBlock _followPauseKeyStatus = new();
    private readonly GameShortcutPreferenceStore _followPauseKeyPreferences =
        new("F7", "follow-pause-key.json");
    private string _followPauseKeyValue = "F7";
    private readonly Button _watchPartyLog = new();
    private readonly TextBlock _autoPartyStatus = new();
    private readonly TextBlock _status = new();
    private readonly ComboBox _inputMode = new();
    private readonly Button _hybridBindingsButton = new();
    private readonly TextBlock _hybridBindingsStatus = new();
    private readonly HybridBindingPreferenceStore _hybridBindingPreferences = new();
    private HybridBindingConfig _hybridBindings = HybridBindingConfig.Default;
    private readonly ComboBox _physicalIndex = new();
    private readonly TextBox _physicalDevice = new();
    private readonly CheckBox _isolationConfirmed = new();
    private readonly CheckBox _rightStickFollowerOverride = new();
    private readonly CheckBox _autoFlask = new();
    private readonly CheckBox _menuGuard = new();
    private readonly TextBlock _menuGuardStatus = new();
    private readonly MenuGuardPreferenceStore _menuGuardPreferences = new();
    private bool _menuGuardPreferenceReady;
    private readonly FollowerMirrorPreferenceStore _followerMirrorPreferences = new();
    private FollowerMirrorConfig _followerMirror = FollowerMirrorConfig.Default;
    private bool _followerMirrorPreferenceReady;
    private readonly Button _followerMirrorButton = new();
    private readonly TextBlock _followerMirrorStatus = new();
    private readonly CheckBox _lastFocusedLeads = new();
    private readonly TextBlock _leadershipPreferenceStatus = new();
    private readonly LeadershipPreferenceStore _leadershipPreferences = new();
    private bool _leadershipPreferenceReady;
    private readonly Button _prepareBridge = new();
    private readonly Button _armPairing = new();
    private readonly Button _confirmRole = new();
    private readonly Button _stopBridge = new();
    private readonly Button _endSession = new();
    private readonly TextBlock _pairingStatus = new();
    private readonly TextBox _followStop = new();
    private readonly TextBox _followCatchup = new();
    private readonly TextBox _followMaxAge = new();
    private readonly TextBox _followLogAge = new();
    private readonly TextBox _followLeaderName = new();
    private readonly ComboBox[] _followTargets = new ComboBox[6];
    private readonly TextBox[] _followPlayerStops = new TextBox[6];
    private readonly FollowStopPreferenceStore _followStopPreferences;
    private double?[] _configuredFollowStops = new double?[6];
    private bool _followStopPreferenceReady;
    private bool _updatingFollowStopFields;
    private readonly TextBlock _followStopPreferenceStatus = new();
    private readonly StackPanel[] _followTargetRows = new StackPanel[6];
    private readonly FollowTargetPreferenceStore _followTargetPreferences;
    private int[] _configuredFollowTargets = new int[6];
    private bool _followTargetPreferenceReady;
    private readonly bool[] _followTargetsRecovered = new bool[6];
    private bool _updatingFollowTargetChoices;
    private readonly TextBlock _followTargetPreferenceStatus = new();
    private readonly Button _startFollow = new();
    private readonly Button _pauseFollow = new();
    private readonly Button _stopFollow = new();
    private readonly TextBlock _followStatus = new();
    private readonly TextBox _radarScale = new();
    private readonly TextBox _radarHotkeyText = new();
    private readonly TextBlock _radarHotkeyStatus = new();
    private readonly RadarHotkeyPreferenceStore _radarHotkeyPreferences = new();
    private RadarHotkeyBinding _radarHotkey;
    private bool _capturingRadarHotkey;
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
    private HybridPairingGuide? _hybridPairing;
    private IReadOnlyList<LaunchedClient> _launchedClients = [];
    private IReadOnlyList<SessionGameClientIdentity> _sessionGameIdentities = [];
    private bool _pairingHasStarted;
    private bool _armRequested;
    private bool _pairingSettling;
    private bool _pairingSettled;
    private bool _followStopping;
    private bool _radarStopping;
    private bool _fpsBusy;
    private bool _rosterBusy;
    private bool _inviteBusy;
    private CancellationTokenSource? _inviteCancellation;
    private CancellationTokenSource? _autoPartyCancellation;
    private CancellationTokenSource? _autoLoginFillCancellation;
    private PartyLeaveLogMonitor? _partyLogMonitor;
    private bool _autoPartyRunning;
    private GameKeyboardShortcutHook? _gameHotkeys;
    private HybridKeyEventPublisher? _hybridPublisher;
    private bool _restoringAutoPartyKey;
    private bool _restoringFollowPauseKey;
    private bool _closing;
    private string _loginStatus = "waiting";
    private bool _busy;
    private int _hybridFocusedPlayer = 1;
    private int _hybridFocusTransition;
    private int _hybridFocusGeneration;
    private readonly HashSet<int> _hybridFocusKeysDown = [];
    private bool HybridSelected => _inputMode.SelectedIndex == 1;

    public MainWindow() : this(new FollowTargetPreferenceStore()) { }

    internal MainWindow(FollowTargetPreferenceStore followTargetPreferences,
        FollowStopPreferenceStore? followStopPreferences = null)
    {
        _followTargetPreferences = followTargetPreferences;
        _followStopPreferences = followStopPreferences ?? new FollowStopPreferenceStore();
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
        content.Children.Add(BuildAccountStep());
        content.Children.Add(BuildLaunchStep());
        content.Children.Add(BuildAutoPartyStep());
        content.Children.Add(BuildFollowStep());
        content.Children.Add(BuildRadarStep());
        content.Children.Add(BuildPrivacyStep());
        content.Children.Add(BuildFpsStep());
        content.Children.Add(BuildResourceStep());
        content.Children.Add(BuildStatusCard());

        shell.Children.Add(new ScrollViewer { Content = content });
        Content = shell;
        var hybridPreference = _hybridBindingPreferences.Load();
        _hybridBindings = hybridPreference.Bindings;
        _hybridBindingsStatus.Text = hybridPreference.Success
            ? "键鼠按键映射已加载；更改后将在下一次桥接会话生效。"
            : "键鼠映射文件无效或无法读取；当前使用目标版默认按键，请重新保存。";
        var keyPreference = _autoPartyKeyPreferences.Load();
        _autoPartyKeyValue = keyPreference.Success ? keyPreference.Key : "None";
        _autoPartyKey.SelectedItem = _autoPartyKeyValue;
        _autoPartyKeyStatus.Text = keyPreference.Success
            ? $"自动组队快捷键：{_autoPartyKeyValue}；仅在已绑定游戏窗口前台生效。"
            : "快捷键设置文件无效或无法读取；请选择按键重新保存。";
        _autoPartyKey.SelectionChanged += (_, _) => SaveAutoPartyKeySelection();
        var followKeyPreference = _followPauseKeyPreferences.Load();
        _followPauseKeyValue = followKeyPreference.Success ? followKeyPreference.Key : "None";
        if (_followPauseKeyValue != "None" && _followPauseKeyValue == _autoPartyKeyValue)
        {
            _followPauseKeyValue = "None";
            _followPauseKeyStatus.Text = "暂停键与自动组队键冲突；已禁用暂停快捷键，请重新选择并保存。";
        }
        else _followPauseKeyStatus.Text = followKeyPreference.Success
            ? $"暂停跟随快捷键：{_followPauseKeyValue}；仅在已绑定游戏窗口前台生效。"
            : "暂停键设置文件无效或无法读取；请选择按键重新保存。";
        _followPauseKey.SelectedItem = _followPauseKeyValue;
        _followPauseKey.SelectionChanged += (_, _) => SaveFollowPauseKeySelection();
        var radarPreference = _radarHotkeyPreferences.Load();
        _radarHotkey = radarPreference.Binding;
        _radarHotkeyText.Text = _radarHotkey.DisplayText;
        _radarHotkeyStatus.Text = !radarPreference.Success
            ? "雷达快捷键设置无效；当前使用 Tab，请重新保存。"
            : RadarKeyConflicts(_radarHotkey)
                ? "雷达快捷键与自动组队或暂停跟随冲突；请重新设置。"
                : $"雷达显隐快捷键：{_radarHotkey.DisplayText}；仅在已绑定游戏窗口前台生效。";
        _playerCount.SelectionChanged += (_, _) => UpdateTopologyOptions();
        _topologyVariant.SelectionChanged += (_, _) => UpdateClientRows();
        _applyLayout.IsCheckedChanged += (_, _) => UpdateLayoutControls();
        _isolationConfirmed.IsCheckedChanged += (_, _) => UpdateBridgeControls();
        _physicalIndex.SelectionChanged += (_, _) => UpdateBridgeControls();
        _physicalDevice.TextChanged += (_, _) => UpdateBridgeControls();
        Opened += (_, _) =>
        {
            if (!OperatingSystem.IsWindows()) return;
            _gameHotkeys ??= new GameKeyboardShortcutHook(
                () => _launchedClients.Any(PoeChatInviteService.IsExactForeground),
                () => Dispatcher.UIThread.Post(() =>
                {
                    if (_closing) return;
                    if (_autoPartyRunning) _autoPartyCancellation?.Cancel();
                    else if (_runAutoParty.IsEnabled) _ = RunAutoPartyAsync();
                }),
                () => Dispatcher.UIThread.Post(() =>
                {
                    if (!_closing) ToggleFollowPause();
                }));
            _gameHotkeys.SetKeys(AutoPartyKeyBinding.VirtualKey(_autoPartyKeyValue),
                AutoPartyKeyBinding.VirtualKey(_followPauseKeyValue));
            if (!_gameHotkeys.Start())
            {
                _autoPartyKeyStatus.Text = "键鼠快捷键监听未能启用；仍可使用自动组队按钮。";
                _followPauseKeyStatus.Text = "键鼠快捷键监听未能启用；仍可使用暂停跟随按钮。";
            }
            UpdateBridgeControls();
        };
        Closed += async (_, _) =>
        {
            NamePrivacy.Changed -= UpdateNamePrivacy;
            _closing = true;
            _gameHotkeys?.Dispose();
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
        var panel = Step("1", "CHOOSE INPUT · 选择输入", "选择实体手柄，或让 P1 使用原生键鼠并为其他角色创建虚拟手柄。");
        _inputMode.ItemsSource = new[] { "Controller · 实体手柄", "Keyboard and mouse · 键鼠" };
        _inputMode.SelectedIndex = 0;
        _inputMode.MinWidth = 260;
        _inputMode.HorizontalAlignment = HorizontalAlignment.Left;
        _inputMode.SelectionChanged += (_, _) =>
        {
            _pairingStatus.Text = HybridSelected
                ? "键鼠模式要求客户端 1 仅有 P1。启动游戏前会先建立虚拟手柄；绑定现有窗口后也可手动准备。"
                : "实体手柄模式需要确认 HidHide 隔离与输入设备。";
            UpdateBridgeControls();
        };
        panel.Children.Add(_inputMode);
        _hybridBindingsButton.Content = "设置键鼠按键映射";
        _hybridBindingsButton.HorizontalAlignment = HorizontalAlignment.Left;
        _hybridBindingsButton.Click += async (_, _) => await EditHybridBindingsAsync();
        panel.Children.Add(_hybridBindingsButton);
        _hybridBindingsStatus.Foreground = MutedBrush;
        _hybridBindingsStatus.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_hybridBindingsStatus);
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
        _autoFlask.Content = "随实时跟随启用自动药剂（双/三客户端各一名角色；生命/魔力须绑定手柄十字键左/右）";
        panel.Children.Add(_autoFlask);
        var menuPreference = _menuGuardPreferences.Load();
        _menuGuardPreferenceReady = menuPreference.Success;
        _menuGuard.Content = "启用菜单保护";
        _menuGuard.IsThreeState = true;
        _menuGuard.IsChecked = menuPreference.Success ? menuPreference.Enabled : null;
        _menuGuardStatus.Text = menuPreference.Success
            ? "目标版默认开启；更改后在下次准备桥接时生效。"
            : "菜单保护设置无效；请重新选择并保存。";
        _menuGuardStatus.Foreground = MutedBrush;
        _menuGuardStatus.TextWrapping = TextWrapping.Wrap;
        _menuGuard.IsCheckedChanged += (_, _) =>
        {
            if (_menuGuard.IsChecked is not bool enabled)
            {
                _menuGuardPreferenceReady = false;
                _menuGuardStatus.Text = "请选择菜单保护状态。";
            }
            else
            {
                _menuGuardPreferenceReady = _menuGuardPreferences.Save(enabled);
                _menuGuardStatus.Text = _menuGuardPreferenceReady
                    ? "菜单保护设置已保存；下次准备桥接时生效。"
                    : "菜单保护设置保存失败；重新保存前不能准备桥接。";
            }
            UpdateBridgeControls();
        };
        panel.Children.Add(_menuGuard);
        panel.Children.Add(_menuGuardStatus);
        var followerMirror = _followerMirrorPreferences.Load();
        _followerMirrorPreferenceReady = followerMirror.Success;
        _followerMirror = followerMirror.Config;
        _followerMirrorButton.Content = "设置 P1–P6 跟随按键";
        _followerMirrorButton.Click += async (_, _) => await EditFollowerMirrorAsync();
        panel.Children.Add(_followerMirrorButton);
        _followerMirrorStatus.Text = followerMirror.Success
            ? "目标版默认镜像全部动作；可为各角色单独选择。"
            : "跟随按键设置无效；请重新选择并保存。";
        _followerMirrorStatus.Foreground = MutedBrush;
        panel.Children.Add(_followerMirrorStatus);
        var leadership = _leadershipPreferences.Load();
        _leadershipPreferenceReady = leadership.Success;
        _lastFocusedLeads.Content = "最近手动聚焦的角色成为 MAIN";
        _lastFocusedLeads.IsThreeState = true;
        _lastFocusedLeads.IsChecked = leadership.Success ? leadership.LastFocusedPlayerLeads : null;
        _leadershipPreferenceStatus.Text = leadership.Message;
        _leadershipPreferenceStatus.Foreground = MutedBrush;
        _leadershipPreferenceStatus.TextWrapping = TextWrapping.Wrap;
        _lastFocusedLeads.IsCheckedChanged += (_, _) =>
        {
            if (_lastFocusedLeads.IsChecked is not bool enabled)
            {
                _leadershipPreferenceReady = false;
                _leadershipPreferenceStatus.Text = "请选择 MAIN 规则。";
            }
            else
            {
                var saved = _leadershipPreferences.Save(enabled);
                _leadershipPreferenceReady = saved.Success;
                _leadershipPreferenceStatus.Text = saved.Message;
            }
            UpdateBridgeControls();
        };
        panel.Children.Add(_lastFocusedLeads);
        panel.Children.Add(_leadershipPreferenceStatus);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 12, LineSpacing = 8 };
        _prepareBridge.Content = "准备虚拟手柄";
        _prepareBridge.Click += async (_, _) => await PrepareBridgeAsync();
        _armPairing.Content = "开始检测当前登录画面";
        _armPairing.Click += async (_, _) => await ArmPairingAsync();
        _stopBridge.Content = "停止手柄桥接";
        _stopBridge.Click += async (_, _) => await StopBridgeAsync();
        _endSession.Content = "结束会话并关闭游戏";
        _endSession.Click += async (_, _) => await EndSessionAsync();
        _confirmRole.Content = "已在游戏内确认此角色";
        _confirmRole.Click += async (_, _) => await ConfirmRoleAsync();
        actions.Children.Add(_prepareBridge);
        actions.Children.Add(_armPairing);
        actions.Children.Add(_stopBridge);
        actions.Children.Add(_endSession);
        actions.Children.Add(_confirmRole);
        panel.Children.Add(actions);
        _pairingStatus.Text = "选择输入方式、人数与布局后检查桥接支持情况。";
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
            row.Children.Add(Label("绑定已运行窗口的进程号（同目录多客户端时必填）", 12, MutedBrush));
            var attachPid = new TextBox { Watermark = "进程号，仅用于绑定已运行窗口" };
            attachPid.TextChanged += (_, _) => ClearLaunchedClients();
            row.Children.Add(attachPid);
            _paths[index] = path;
            _uris[index] = uri;
            _attachPids[index] = attachPid;
            _clientLabels[index] = clientLabel;
            _clientRows[index] = row;
            panel.Children.Add(row);
        }
        return Card(panel);
    }

    private Border BuildAccountStep()
    {
        var panel = Step("2A", "ACCOUNTS · 账号资料",
            "按客户端保存独立 PoE 账号；密码只写入当前 Windows 用户保护的本地资料。每端一名角色时可启用启动后自动填入；须先确认邮箱和密码字段。");
        for (var index = 0; index < 3; index++)
        {
            var client = index + 1;
            var row = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
            row.Children.Add(Label($"客户端 {client} · 独立账号", 13, Brushes.White, FontWeight.SemiBold));
            var email = new TextBox { Watermark = "PoE 账号邮箱", Width = 250,
                HorizontalAlignment = HorizontalAlignment.Left };
            var password = new TextBox { Watermark = "密码", PasswordChar = '•', Width = 250,
                HorizontalAlignment = HorizontalAlignment.Left };
            row.Children.Add(email);
            row.Children.Add(password);
            row.Children.Add(Label("修改账号时密码可留空，沿用本客户端已保存的密码。", 12, MutedBrush));
            var actions = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 10 };
            var save = new Button { Content = "保存账号资料", IsEnabled = OperatingSystem.IsWindows() };
            var remove = new Button { Content = "删除账号资料", IsEnabled = OperatingSystem.IsWindows() };
            save.Click += (_, _) =>
            {
                var result = _accountProfiles.Save(client, email.Text, password.Text);
                if (result.Success) password.Text = string.Empty;
                if (result.Success)
                {
                    _accountPresent[client - 1] = true;
                    _preparedAutoLoginWindows[client - 1] = null;
                    _manualAutoLoginWindows[client - 1] = null;
                }
                _accountResults[client - 1].Text = result.Success
                    ? _accountProfiles.Describe(client) : result.Message;
                UpdateAutoLoginControls();
                UpdateBridgeControls();
            };
            remove.Click += (_, _) =>
            {
                var result = _accountProfiles.Delete(client);
                if (result.Success)
                {
                    _accountPresent[client - 1] = false;
                    _preparedAutoLoginWindows[client - 1] = null;
                    _manualAutoLoginWindows[client - 1] = null;
                    email.Text = string.Empty;
                    password.Text = string.Empty;
                    _autoLoginChoices[client - 1].IsChecked = false;
                }
                _accountResults[client - 1].Text = result.Message;
                UpdateAutoLoginControls();
                UpdateBridgeControls();
            };
            actions.Children.Add(save);
            actions.Children.Add(remove);
            _accountSaveButtons[index] = save;
            _accountDeleteButtons[index] = remove;
            row.Children.Add(actions);
            var status = Label(_accountProfiles.Describe(client), 12, MutedBrush);
            _accountPresent[index] = _accountProfiles.Load(client) is not null;
            row.Children.Add(status);
            _accountResults[index] = status;
            var calibrationActions = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 10 };
            var calibrate = new Button { Content = "标记登录字段", IsEnabled = false };
            calibrate.Click += async (_, _) => await CalibrateLoginAsync(client);
            var clearMark = new Button { Content = "清除字段标记", IsEnabled = OperatingSystem.IsWindows() };
            clearMark.Click += (_, _) =>
            {
                var deleted = _loginCalibrations.Delete(client);
                if (deleted)
                {
                    _preparedAutoLoginWindows[client - 1] = null;
                    _manualAutoLoginWindows[client - 1] = null;
                }
                _calibrationResults[client - 1].Text = deleted
                    ? "登录字段标记已清除。" : "登录字段标记无法清除。";
                UpdateBridgeControls();
            };
            calibrationActions.Children.Add(calibrate);
            calibrationActions.Children.Add(clearMark);
            row.Children.Add(calibrationActions);
            var calibrationStatus = Label(_loginCalibrations.Load(client).Message, 12, MutedBrush);
            row.Children.Add(calibrationStatus);
            _calibrateLogin[index] = calibrate;
            _calibrationResults[index] = calibrationStatus;
            var loginPreference = _autoLoginPreferences.Load(client);
            _autoLoginPreferenceReady[index] = loginPreference.Success;
            var automatic = new CheckBox
            {
                Content = "此独立客户端启动后自动填入账号，配对前复核",
                IsThreeState = true,
                IsChecked = loginPreference.Success ? loginPreference.Enabled : null
            };
            var automaticStatus = Label(loginPreference.Message, 12, MutedBrush);
            automatic.IsCheckedChanged += (_, _) =>
            {
                if (automatic.IsChecked is not bool enabled)
                {
                    _autoLoginPreferenceReady[client - 1] = false;
                    automaticStatus.Text = "请选择自动填入或手动登录。";
                }
                else
                {
                    var saved = _autoLoginPreferences.Save(client, enabled);
                    _autoLoginPreferenceReady[client - 1] = saved.Success;
                    automaticStatus.Text = saved.Message;
                    if (saved.Success)
                    {
                        _preparedAutoLoginWindows[client - 1] = null;
                        _manualAutoLoginWindows[client - 1] = null;
                    }
                }
                UpdateBridgeControls();
            };
            row.Children.Add(automatic);
            row.Children.Add(Label("填入时会临时使用并清空系统剪贴板；登录提交仍由后续配对 A 键控制。", 12, MutedBrush));
            row.Children.Add(automaticStatus);
            _autoLoginChoices[index] = automatic;
            _autoLoginResults[index] = automaticStatus;
            _accountRows[index] = row;
            panel.Children.Add(row);
        }
        return Card(panel);
    }

    private Border BuildLaunchStep()
    {
        var panel = Step("3", "PREPARE & START · 准备并开始", "检查配置后可启动或绑定游戏窗口；已启用的独立客户端会尝试填入账号。此步骤不提交登录或执行角色配对。");
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
        _cancelAutoLogin.Content = "取消账号准备";
        _cancelAutoLogin.IsEnabled = false;
        _cancelAutoLogin.Click += (_, _) => _autoLoginFillCancellation?.Cancel();
        actions.Children.Add(_check);
        actions.Children.Add(_launch);
        actions.Children.Add(_attach);
        actions.Children.Add(_cancelAutoLogin);
        panel.Children.Add(actions);
        _probeRoster.Content = "检查 Current Party 人数";
        _probeRoster.Click += async (_, _) => await ProbeRosterAsync();
        panel.Children.Add(_probeRoster);
        _rosterStatus.Text = "绑定窗口后，可在游戏内打开 Current Party 面板再检查；结果仅是画面候选行数。";
        _rosterStatus.Foreground = MutedBrush;
        _rosterStatus.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_rosterStatus);
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

    private Border BuildAutoPartyStep()
    {
        var panel = Step("3A", "AUTO PARTY · 组队邀请", "按角色填写游戏中的准确名称。已绑定游戏窗口前台时按所选快捷键可运行或取消自动组队。自动恢复向其他客户端发出全新邀请，按目标版菜单顺序接受，并检查 Current Party 画面候选人数。Windows 游戏内效果待验证。");
        panel.Children.Add(Label("自动组队快捷键（目标版默认 F8；None 关闭）", 12, MutedBrush));
        _autoPartyKey.ItemsSource = AutoPartyKeyBinding.Options;
        _autoPartyKey.Width = 190;
        _autoPartyKey.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(_autoPartyKey);
        _saveAutoPartyKey.Content = "保存快捷键";
        _saveAutoPartyKey.Click += (_, _) => SaveAutoPartyKeySelection(force: true);
        panel.Children.Add(_saveAutoPartyKey);
        _autoPartyKeyStatus.Foreground = MutedBrush;
        _autoPartyKeyStatus.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_autoPartyKeyStatus);
        for (var player = 1; player <= 6; player++)
        {
            var row = new StackPanel { Spacing = 4 };
            row.Children.Add(Label($"P{player} 角色名", 12, MutedBrush));
            var name = new TextBox { Watermark = "游戏内准确角色名" };
            name.TextChanged += (_, _) => UpdateAutoPartyControls();
            row.Children.Add(name);
            _inviteRows[player - 1] = row;
            _inviteNames[player - 1] = name;
            panel.Children.Add(row);
        }
        _inviteTarget.Width = 190;
        _inviteTarget.HorizontalAlignment = HorizontalAlignment.Left;
        _inviteTarget.SelectionChanged += (_, _) => UpdateAutoPartyControls();
        panel.Children.Add(_inviteTarget);
        _sendInvite.Content = "发送一次邀请";
        _sendInvite.Click += async (_, _) => await SendOneInviteAsync();
        _runAutoParty.Content = "运行自动组队";
        _runAutoParty.Click += async (_, _) => await RunAutoPartyAsync();
        _stopAutoParty.Content = "取消自动组队";
        _stopAutoParty.Click += (_, _) => _autoPartyCancellation?.Cancel();
        _watchPartyLog.Content = "开始监听离队日志";
        _watchPartyLog.Click += (_, _) => TogglePartyLogMonitor();
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 12, LineSpacing = 8 };
        actions.Children.Add(_sendInvite);
        actions.Children.Add(_runAutoParty);
        actions.Children.Add(_stopAutoParty);
        actions.Children.Add(_watchPartyLog);
        panel.Children.Add(actions);
        _inviteStatus.Text = "需要已绑定窗口、完成配对和每个客户端至少一个准确角色名。";
        _inviteStatus.Foreground = MutedBrush;
        _inviteStatus.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_inviteStatus);
        _autoPartyStatus.Text = "自动组队未运行；截图候选人数不能证明角色身份。";
        _autoPartyStatus.Foreground = MutedBrush;
        _autoPartyStatus.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_autoPartyStatus);
        return Card(panel);
    }

    private Border BuildFollowStep()
    {
        var panel = Step("4", "FOLLOW · 跟随", "实时输入支持双客户端 2P 或三个客户端 3P，均须每端一名角色；请先完成配对，并在每个客户端启用只读位置传感器。距离与时效需要按实际游戏校准。");
        var fields = new (TextBox Box, string Label)[]
        {
            (_followStop, "停止距离"), (_followCatchup, "追赶距离"),
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
        panel.Children.Add(Label("初始 MAIN 角色名（可选，用于识别离队）", 12, MutedBrush));
        _followLeaderName.Watermark = "角色名";
        _followLeaderName.Width = 190;
        _followLeaderName.HorizontalAlignment = HorizontalAlignment.Left;
        _followLeaderName.TextChanged += (_, _) => UpdateFollowControls();
        panel.Children.Add(_followLeaderName);
        for (var index = 0; index < _followTargets.Length; index++)
        {
            var row = new StackPanel { Spacing = 4, IsVisible = false };
            row.Children.Add(Label($"P{index + 1} 跟随目标", 12, MutedBrush));
            var choice = new ComboBox { Name = $"follow-target-{index + 1}", Width = 190,
                HorizontalAlignment = HorizontalAlignment.Left,
                ItemsSource = new[] { FollowTargetChoices.CurrentMain }, SelectedIndex = 0 };
            _followTargets[index] = choice;
            _followTargetRows[index] = row;
            var player = index + 1;
            choice.SelectionChanged += (_, _) => SaveFollowTargetChoice(player);
            row.Children.Add(choice);
            row.Children.Add(Label($"P{player} 停止距离（不填沿用上方值）", 12, MutedBrush));
            var playerStop = new TextBox { Name = $"follow-stop-{player}", Width = 190,
                HorizontalAlignment = HorizontalAlignment.Left, Watermark = "0–200" };
            playerStop.TextChanged += (_, _) => SaveFollowStopChoice(player);
            _followPlayerStops[index] = playerStop;
            row.Children.Add(playerStop);
            panel.Children.Add(row);
        }
        var loadedTargets = _followTargetPreferences.Load();
        _configuredFollowTargets = loadedTargets.Targets;
        _followTargetPreferenceReady = loadedTargets.Success;
        _followTargetPreferenceStatus.Text = loadedTargets.Message;
        _followTargetPreferenceStatus.Foreground = MutedBrush;
        _followTargetPreferenceStatus.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_followTargetPreferenceStatus);
        var loadedStops = _followStopPreferences.Load();
        _configuredFollowStops = loadedStops.Stops;
        _followStopPreferenceReady = loadedStops.Success;
        _followStopPreferenceStatus.Text = loadedStops.Message;
        _followStopPreferenceStatus.Foreground = MutedBrush;
        _followStopPreferenceStatus.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_followStopPreferenceStatus);
        panel.Children.Add(Label("暂停/恢复跟随快捷键（目标版默认 F7；None 关闭）", 12, MutedBrush));
        _followPauseKey.ItemsSource = AutoPartyKeyBinding.Options;
        _followPauseKey.Width = 190;
        _followPauseKey.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(_followPauseKey);
        _saveFollowPauseKey.Content = "保存暂停快捷键";
        _saveFollowPauseKey.Click += (_, _) => SaveFollowPauseKeySelection(force: true);
        panel.Children.Add(_saveFollowPauseKey);
        _followPauseKeyStatus.Foreground = MutedBrush;
        _followPauseKeyStatus.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_followPauseKeyStatus);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 12 };
        _startFollow.Content = "开始跟随";
        _startFollow.Click += async (_, _) => await StartFollowAsync();
        _stopFollow.Content = "停止跟随";
        _stopFollow.Click += async (_, _) => await StopFollowAsync();
        actions.Children.Add(_startFollow);
        _pauseFollow.Content = "暂停跟随";
        _pauseFollow.Click += (_, _) => ToggleFollowPause();
        actions.Children.Add(_pauseFollow);
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
        panel.Children.Add(Label("雷达显隐快捷键（如 Tab、Ctrl+R、Shift+F9）", 12, MutedBrush));
        var hotkeyRow = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 12 };
        _radarHotkeyText.Width = 190;
        _radarHotkeyText.Watermark = "Tab";
        _radarHotkeyText.KeyDown += CaptureRadarHotkey;
        hotkeyRow.Children.Add(_radarHotkeyText);
        var captureHotkey = new Button { Content = "录入快捷键" };
        captureHotkey.Click += (_, _) =>
        {
            _capturingRadarHotkey = true;
            _radarHotkeyStatus.Text = "请按快捷键；只按 Ctrl、Alt 或 Shift 会继续等待，Esc 取消。";
            _radarHotkeyText.Focus();
        };
        hotkeyRow.Children.Add(captureHotkey);
        var saveHotkey = new Button { Content = "保存雷达快捷键" };
        saveHotkey.Click += (_, _) => SaveRadarHotkey();
        hotkeyRow.Children.Add(saveHotkey);
        panel.Children.Add(hotkeyRow);
        _radarHotkeyStatus.Foreground = MutedBrush;
        _radarHotkeyStatus.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_radarHotkeyStatus);
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
            StopPartyLogMonitor();
            _inviteCancellation?.Cancel();
            _autoPartyCancellation?.Cancel();
            _launchedClients = [];
            _sessionGameIdentities = [];
            Array.Clear(_preparedAutoLoginWindows);
            Array.Clear(_manualAutoLoginWindows);
            _rosterStatus.Text = "绑定窗口后，可在游戏内打开 Current Party 面板再检查；结果仅是画面候选行数。";
            _ = StopRadarAsync();
            StopPrivacy();
            _ = StopAllResourcesAsync();
        }
        _selectedTopology = selection;
        if (topologyChanged && HybridSelected && _bridge is null && selection is not null)
            _pairingStatus.Text = selection.PlayerCount == 1
                ? "单人模式由 P1 原生键鼠操作；无需准备虚拟手柄。"
                : HybridTopologySupport.CanBridge(selection)
                    ? "客户端 1 仅有 P1；启动游戏前先建立键鼠虚拟手柄，绑定现有窗口后也可手动准备。"
                    : "目标版键鼠模式要求客户端 1 仅有 P1；此布局的键鼠桥接尚未接入。";
        if (_bridge is null && topologyChanged) _autoFlask.IsChecked = selection?.Id is "2-split" or "3-three-singles";
        var count = _selectedTopology?.ClientCount ?? 0;
        _assignment.Text = _selectedTopology is null
            ? "选择人数后显示角色与客户端分布。"
            : $"{_selectedTopology.PlayerCount} 人 · {count} 个客户端 · {_selectedTopology.Summary}";
        for (var index = 0; index < _clientRows.Length; index++)
        {
            _clientRows[index].IsVisible = index < count;
            _accountRows[index].IsVisible = index < count;
            _fpsRows[index].IsVisible = index < count;
            _resourceRows[index].IsVisible = index < count;
            if (index < count)
            {
                var players = _selectedTopology!.Clients[index];
                _clientLabels[index].Text = $"客户端 {index + 1} · {string.Join('/', players.Select(player => $"P{player}"))} · 游戏程序";
            }
        }
        UpdateLoginCalibrationControls();
        UpdateAutoLoginControls();
        for (var player = 0; player < _inviteRows.Length; player++)
            _inviteRows[player].IsVisible = player < (_selectedTopology?.PlayerCount ?? 0);
        if (topologyChanged)
        {
            _inviteTarget.ItemsSource = Enumerable.Range(2, Math.Max(0, count - 1))
                .Select(client => $"客户端 {client}").ToArray();
            _inviteTarget.SelectedIndex = count > 1 ? 0 : -1;
        }
        _check.IsEnabled = !_busy && !_pairingHasStarted && _bridge is null && count > 0;
        _launch.IsEnabled = _check.IsEnabled;
        _attach.IsEnabled = _check.IsEnabled;
        UpdateFollowTargetChoices();
        UpdateRosterControls();
        UpdateAutoPartyControls();
        UpdateBridgeControls();
        UpdateRadarControls();
        UpdatePrivacyControls();
        UpdateFpsControls();
        UpdateResourceControls();
    }

    private void UpdateFollowTargetChoices()
    {
        var supported = _selectedTopology?.Id is "2-split" or "3-three-singles";
        if (!_followTargetPreferenceReady) Array.Clear(_followTargetsRecovered);
        _updatingFollowTargetChoices = true;
        try
        {
            for (var index = 0; index < _followTargets.Length; index++)
            {
                if (_followTargets[index] is null) continue;
                var player = index + 1;
                var options = supported ? FollowTargetChoices.For(_selectedTopology!, player) : [];
                _followTargetRows[index].IsVisible = options.Length > 0;
                _followTargets[index].ItemsSource = options;
                _followTargets[index].SelectedItem = _followTargetPreferenceReady && options.Length > 0
                    ? FollowTargetChoices.Normalize(_selectedTopology!, player, _configuredFollowTargets[index])
                    : null;
            }
        }
        finally
        {
            _updatingFollowTargetChoices = false;
        }
        UpdateFollowStopFields();
        UpdateFollowControls();
    }

    private void UpdateFollowStopFields()
    {
        _updatingFollowStopFields = true;
        try
        {
            for (var index = 0; index < _followPlayerStops.Length; index++)
            {
                var slot = _selectedTopology?.Slots.FirstOrDefault(item => item.PartyPlayer == index + 1);
                _followPlayerStops[index].Text = slot is null ? string.Empty :
                    _configuredFollowStops[slot.RuntimePad - 1]?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty;
            }
        }
        finally
        {
            _updatingFollowStopFields = false;
        }
    }

    private void SaveFollowStopChoice(int player)
    {
        if (_updatingFollowStopFields || _selectedTopology is null) return;
        var slot = _selectedTopology.Slots.FirstOrDefault(item => item.PartyPlayer == player);
        if (slot is null || _selectedTopology.Id is not ("2-split" or "3-three-singles")) return;
        if (!FollowPlayerStop.TryRead(_followPlayerStops[player - 1].Text, out var value))
        {
            _followStopPreferenceStatus.Text = "停止距离必须为 0–200；当前输入未保存。";
            UpdateFollowControls();
            return;
        }
        var next = (double?[])_configuredFollowStops.Clone();
        next[slot.RuntimePad - 1] = value;
        _configuredFollowStops = next;
        var saved = _followStopPreferences.Save(next);
        _followStopPreferenceReady = saved.Success;
        _followStopPreferenceStatus.Text = saved.Message;
        UpdateFollowControls();
    }

    private void SaveFollowTargetChoice(int player)
    {
        if (_updatingFollowTargetChoices || _selectedTopology is null ||
            _selectedTopology.Id is not ("2-split" or "3-three-singles")) return;
        if (!FollowTargetChoices.TryRead(_selectedTopology, player,
                _followTargets[player - 1].SelectedItem as string, out var target)) return;
        var next = (int[])_configuredFollowTargets.Clone();
        next[player - 1] = target;
        _configuredFollowTargets = next;
        _followTargetsRecovered[player - 1] = true;
        if (!_followTargetPreferenceReady && _selectedTopology.Slots.Any(slot =>
                !_followTargetsRecovered[slot.PartyPlayer - 1]))
        {
            _followTargetPreferenceStatus.Text = "请为当前布局的每个角色明确选择跟随目标。";
            UpdateFollowControls();
            return;
        }
        var saved = _followTargetPreferences.Save(next);
        _followTargetPreferenceReady = saved.Success;
        _followTargetPreferenceStatus.Text = saved.Message;
        UpdateFollowControls();
    }

    private void ClearLaunchedClients()
    {
        StopPartyLogMonitor();
        _inviteCancellation?.Cancel();
        _autoPartyCancellation?.Cancel();
        _launchedClients = [];
        _sessionGameIdentities = [];
        Array.Clear(_preparedAutoLoginWindows);
        Array.Clear(_manualAutoLoginWindows);
        UpdateLoginCalibrationControls();
        UpdateAutoLoginControls();
        _rosterStatus.Text = "窗口绑定已清除；请重新绑定后检查 Current Party。";
        UpdateRosterControls();
        UpdateAutoPartyControls();
        _ = StopRadarAsync();
        StopPrivacy();
        _ = StopAllResourcesAsync();
        if (_pairing is not null) ShowCurrentPairingStep();
        UpdateBridgeControls();
        UpdateRadarControls();
        UpdatePrivacyControls();
        UpdateResourceControls();
    }

    private void UpdateLoginCalibrationControls()
    {
        for (var index = 0; index < _calibrateLogin.Length; index++)
            if (_calibrateLogin[index] is not null)
                _calibrateLogin[index].IsEnabled = OperatingSystem.IsWindows() && !_busy &&
                    !_armRequested && !_pairingHasStarted &&
                    index < _launchedClients.Count && index < (_selectedTopology?.ClientCount ?? 0);
    }

    private void UpdateAutoLoginControls()
    {
        _cancelAutoLogin.IsEnabled = _autoLoginFillCancellation is not null;
        for (var index = 0; index < _autoLoginChoices.Length; index++)
            if (_autoLoginChoices[index] is not null)
            {
                _accountSaveButtons[index].IsEnabled = OperatingSystem.IsWindows() &&
                    !_armRequested && !_pairingHasStarted && !_busy &&
                    _autoLoginFillCancellation is null;
                _accountDeleteButtons[index].IsEnabled = _accountSaveButtons[index].IsEnabled;
                _autoLoginChoices[index].IsEnabled = OperatingSystem.IsWindows() &&
                    !_armRequested && !_pairingHasStarted && !_busy &&
                    _selectedTopology is { } topology && index < topology.ClientCount &&
                    topology.Clients[index].Count == 1 && _accountPresent[index];
            }
    }

    private async Task CalibrateLoginAsync(int clientNumber)
    {
        if (!OperatingSystem.IsWindows() || _selectedTopology is null ||
            clientNumber > _launchedClients.Count ||
            !_calibrateLogin[clientNumber - 1].IsEnabled) return;
        var bound = _launchedClients[clientNumber - 1];
        var topology = _selectedTopology;
        _calibrateLogin[clientNumber - 1].IsEnabled = false;
        _calibrationResults[clientNumber - 1].Text = "正在读取已绑定客户端的登录画面…";
        try
        {
            var frame = await Task.Run(() => LoginCalibrationCapture.TryCapture(bound));
            if (frame is null || !ReferenceEquals(_selectedTopology, topology) ||
                clientNumber > _launchedClients.Count || _launchedClients[clientNumber - 1] != bound)
            {
                _calibrationResults[clientNumber - 1].Text = "窗口身份或画面无法核对；没有保存标记。";
                return;
            }
            var dialog = new LoginCalibrationDialog(clientNumber, frame, topology.Id);
            var calibration = await dialog.ShowDialog<LoginCalibration?>(this);
            if (calibration is null)
            {
                _calibrationResults[clientNumber - 1].Text = "没有保存字段标记；可手动登录。";
                return;
            }
            if (!ReferenceEquals(_selectedTopology, topology) ||
                clientNumber > _launchedClients.Count || _launchedClients[clientNumber - 1] != bound ||
                !LoginCalibrationCapture.MatchesCurrentViewport(bound, calibration, topology.Id))
            {
                _calibrationResults[clientNumber - 1].Text = "窗口身份或布局已改变；没有保存字段标记。";
                return;
            }
            var saved = _loginCalibrations.Save(clientNumber, calibration);
            if (saved.Success)
            {
                _preparedAutoLoginWindows[clientNumber - 1] = null;
                _manualAutoLoginWindows[clientNumber - 1] = null;
            }
            _calibrationResults[clientNumber - 1].Text = saved.Message;
            UpdateBridgeControls();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _calibrationResults[clientNumber - 1].Text = "登录画面标记未完成；没有保存字段标记。";
        }
        finally
        {
            UpdateLoginCalibrationControls();
        }
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
        if (RadarKeyConflicts(_radarHotkey))
        {
            _radarHotkeyStatus.Text = "雷达快捷键与自动组队或暂停跟随冲突；请重新设置。";
            return;
        }
        try
        {
            var session = RadarSession.Start(_launchedClients, scale, _radarHotkey);
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
        UpdateRosterControls();
        UpdateAutoPartyControls();
        var slots = _selectedTopology?.Slots.Where(slot => slot.Client != 3)
            .Select(slot => slot.RuntimePad).ToArray() ?? [];
        var useWgi = slots.Length == 4;
        var privateReady = _selectedTopology?.ClientCount != 3 ||
            PrivateClient3Plan.TryCreate(_selectedTopology, _launchedClients, out _);
        var supported = slots.All(slot => slot is >= 1 and <= 4) &&
            (slots.Length is >= 1 and <= 3 || useWgi) && privateReady;
        var hybridSupported = _selectedTopology is not null &&
            HybridTopologySupport.CanBridge(_selectedTopology) &&
            _launchedClients.Count == _selectedTopology.ClientCount;
        var gamePathsReady = _selectedTopology is not null &&
            Enumerable.Range(0, _selectedTopology.ClientCount)
                .All(index => !string.IsNullOrWhiteSpace(_paths[index]?.Text));
        _prepareBridge.Content = HybridSelected && _selectedTopology?.PlayerCount == 1
            ? "单人原生键鼠无需桥接" : HybridSelected ? "准备键鼠虚拟手柄" : "准备虚拟手柄";
        _prepareBridge.IsEnabled = !_busy && _bridge is null && gamePathsReady &&
            _menuGuardPreferenceReady && _menuGuard.IsChecked is bool &&
            _followerMirrorPreferenceReady &&
            (HybridSelected ? hybridSupported && _gameHotkeys?.IsStarted == true :
                supported && _leadershipPreferenceReady && _lastFocusedLeads.IsChecked is bool &&
                (useWgi || _physicalIndex.SelectedIndex is >= 0 and <= 3) &&
                _isolationConfirmed.IsChecked == true && !string.IsNullOrWhiteSpace(_physicalDevice.Text));
        _stopBridge.IsEnabled = _bridge is not null;
        _endSession.IsEnabled = OperatingSystem.IsWindows() && !_busy &&
            (_bridge is not null || _launchedClients.Count > 0);
        _followerMirrorButton.IsEnabled = _bridge is null && !_busy;
        _armPairing.IsEnabled = !HybridSelected && _bridge is not null && _pairing?.Current is not null &&
            _launchedClients.Count == _selectedTopology?.ClientCount &&
            _pairing.Armed == false && _pairing.AReleased == false && !_armRequested && !_busy &&
            (_selectedTopology?.Clients[_pairing.Current.Client - 1].Count != 1 ||
             !_accountPresent[_pairing.Current.Client - 1] ||
             _autoLoginPreferenceReady[_pairing.Current.Client - 1]);
        _armPairing.Content = _pairing?.Current is { } next &&
            _selectedTopology?.Clients[next.Client - 1].Count == 1 &&
            _accountPresent[next.Client - 1] &&
            _autoLoginChoices[next.Client - 1].IsChecked == true
                ? _manualAutoLoginWindows[next.Client - 1] is { } manual &&
                  next.Client <= _launchedClients.Count && manual == _launchedClients[next.Client - 1]
                    ? "手动登录后检测当前画面"
                    : _preparedAutoLoginWindows[next.Client - 1] is { } prepared &&
                  next.Client <= _launchedClients.Count && prepared == _launchedClients[next.Client - 1]
                    ? "核验已填账号并检测登录画面"
                    : "填入账号并检测登录画面"
                : "开始检测当前登录画面";
        _confirmRole.Content = HybridSelected && _hybridPairing?.Phase == HybridPairingPhase.AwaitNativeMain
            ? "P1 已进入游戏" : "已在游戏内确认此角色";
        _confirmRole.IsEnabled = HybridSelected
            ? _bridge?.IsHybridMode == true && (_hybridPairing?.Phase is
                HybridPairingPhase.AwaitNativeMain or HybridPairingPhase.AwaitUserConfirmation)
            : _bridge is not null && _pairing?.AReleased == true;
        _playerCount.IsEnabled = _bridge is null && !_busy;
        _topologyVariant.IsEnabled = _bridge is null && !_busy;
        _inputMode.IsEnabled = _bridge is null && !_busy;
        _physicalIndex.IsEnabled = !HybridSelected && _bridge is null && !_busy && !useWgi;
        _hybridBindingsButton.IsEnabled = _bridge is null && !_busy;
        _physicalDevice.IsEnabled = !HybridSelected && _bridge is null && !_busy;
        _isolationConfirmed.IsEnabled = !HybridSelected && _bridge is null && !_busy;
        _rightStickFollowerOverride.IsEnabled = !HybridSelected && _bridge is null && !_busy;
        _lastFocusedLeads.IsEnabled = !HybridSelected && _bridge is null && !_busy;
        _autoFlask.IsEnabled = !HybridSelected && _bridge is null && !_busy &&
            (_selectedTopology?.Id is "2-split" or "3-three-singles");
        _menuGuard.IsEnabled = _bridge is null && !_busy;
        _check.IsEnabled = !_busy && !_pairingHasStarted && _bridge is null && _selectedTopology is not null;
        _launch.IsEnabled = _check.IsEnabled && (!HybridSelected || _selectedTopology is not null &&
            (_selectedTopology.PlayerCount == 1 && HybridTopologySupport.HasNativeP1(_selectedTopology) ||
             HybridTopologySupport.CanBridge(_selectedTopology)));
        _attach.IsEnabled = _launch.IsEnabled;
        UpdateAutoLoginControls();
        UpdateLoginCalibrationControls();
        for (var index = 0; index < _paths.Length; index++)
        {
            if (_paths[index] is not null) _paths[index].IsEnabled = _bridge is null && !_pairingHasStarted && !_busy;
            if (_uris[index] is not null) _uris[index].IsEnabled = _bridge is null && !_pairingHasStarted && !_busy;
            if (_attachPids[index] is not null) _attachPids[index].IsEnabled = _bridge is null && !_pairingHasStarted && !_busy;
        }
        if (_bridge is null && _selectedTopology is not null && !supported)
        {
            _pairingStatus.Text = "此布局需要先启动或绑定三个游戏窗口，并核对第三客户端进程身份；当前尚未开始配对。";
        }
        UpdateFollowControls();
    }

    private void UpdateRosterControls()
    {
        _probeRoster.IsEnabled = OperatingSystem.IsWindows() && !_busy && !_rosterBusy &&
            _selectedTopology is not null && _launchedClients.Count == _selectedTopology.ClientCount;
    }

    private void SaveAutoPartyKeySelection(bool force = false)
    {
        if (_restoringAutoPartyKey || _autoPartyKey.SelectedItem is not string selected ||
            selected == _autoPartyKeyValue && !force) return;
        if (selected != "None" && (selected == _followPauseKeyValue ||
            AutoPartyKeyBinding.VirtualKey(selected) == _radarHotkey.VirtualKey))
        {
            _autoPartyKeyStatus.Text = "该按键已用于暂停跟随或雷达；请选择不同按键。";
            _restoringAutoPartyKey = true;
            try { _autoPartyKey.SelectedItem = _autoPartyKeyValue; }
            finally { _restoringAutoPartyKey = false; }
            return;
        }
        if (!_autoPartyKeyPreferences.Save(selected))
        {
            _autoPartyKeyStatus.Text = "快捷键保存失败；已恢复上一个按键。";
            _restoringAutoPartyKey = true;
            try { _autoPartyKey.SelectedItem = _autoPartyKeyValue; }
            finally { _restoringAutoPartyKey = false; }
            return;
        }
        _autoPartyKeyValue = selected;
        ApplyGameShortcutKeys();
        _autoPartyKeyStatus.Text = _bridge?.IsHybridMode == true &&
            _hybridBindings.ContainsKey(AutoPartyKeyBinding.VirtualKey(selected))
            ? "自动组队快捷键与键鼠操作冲突，本次会话已禁用；仍可使用按钮。"
            : OperatingSystem.IsWindows() &&
            _gameHotkeys is { IsStarted: false }
            ? "快捷键已保存，但键鼠监听未能启用；仍可使用自动组队按钮。"
            : selected == "None"
            ? "自动组队快捷键已关闭。"
            : $"自动组队快捷键：{selected}；仅在已绑定游戏窗口前台生效。";
    }

    private void SaveFollowPauseKeySelection(bool force = false)
    {
        if (_restoringFollowPauseKey || _followPauseKey.SelectedItem is not string selected ||
            selected == _followPauseKeyValue && !force) return;
        if (selected != "None" && (selected == _autoPartyKeyValue ||
            AutoPartyKeyBinding.VirtualKey(selected) == _radarHotkey.VirtualKey))
        {
            _followPauseKeyStatus.Text = "该按键已用于自动组队或雷达；请选择不同按键。";
            _restoringFollowPauseKey = true;
            try { _followPauseKey.SelectedItem = _followPauseKeyValue; }
            finally { _restoringFollowPauseKey = false; }
            return;
        }
        if (!_followPauseKeyPreferences.Save(selected))
        {
            _followPauseKeyStatus.Text = "暂停快捷键保存失败；已恢复上一个按键。";
            _restoringFollowPauseKey = true;
            try { _followPauseKey.SelectedItem = _followPauseKeyValue; }
            finally { _restoringFollowPauseKey = false; }
            return;
        }
        _followPauseKeyValue = selected;
        ApplyGameShortcutKeys();
        _followPauseKeyStatus.Text = _bridge?.IsHybridMode == true &&
            _hybridBindings.ContainsKey(AutoPartyKeyBinding.VirtualKey(selected))
            ? "暂停键与键鼠操作冲突，本次会话已禁用；仍可使用按钮。"
            : OperatingSystem.IsWindows() &&
            _gameHotkeys is { IsStarted: false }
            ? "暂停键已保存，但键鼠监听未能启用；仍可使用暂停跟随按钮。"
            : selected == "None"
                ? "暂停跟随快捷键已关闭。"
                : $"暂停跟随快捷键：{selected}；仅在已绑定游戏窗口前台生效。";
    }

    private void ApplyGameShortcutKeys()
    {
        var autoPartyKey = AutoPartyKeyBinding.VirtualKey(_autoPartyKeyValue);
        var followPauseKey = AutoPartyKeyBinding.VirtualKey(_followPauseKeyValue);
        if (_bridge?.IsHybridMode == true)
        {
            if (_hybridBindings.ContainsKey(autoPartyKey)) autoPartyKey = 0;
            if (_hybridBindings.ContainsKey(followPauseKey)) followPauseKey = 0;
        }
        _gameHotkeys?.SetKeys(autoPartyKey, followPauseKey);
    }

    private async Task EditHybridBindingsAsync()
    {
        if (_bridge is not null || _busy) return;
        var selected = await new HybridBindingDialog(_hybridBindings)
            .ShowDialog<HybridBindingConfig?>(this);
        if (selected is null) return;
        if (!_hybridBindingPreferences.Save(selected))
        {
            _hybridBindingsStatus.Text = "键鼠映射保存失败；仍使用原有按键。";
            return;
        }
        _hybridBindings = selected;
        _gameHotkeys?.SetHybridBindings(selected);
        _hybridBindingsStatus.Text = "键鼠按键映射已保存；下一次准备桥接时生效。";
        ApplyGameShortcutKeys();
    }

    private async Task EditFollowerMirrorAsync()
    {
        if (_bridge is not null || _busy) return;
        var selected = await new FollowerMirrorDialog(_followerMirror, _selectedTopology?.PlayerCount ?? 2)
            .ShowDialog<FollowerMirrorConfig?>(this);
        if (selected is null) return;
        _followerMirrorPreferenceReady = _followerMirrorPreferences.Save(selected);
        if (_followerMirrorPreferenceReady) _followerMirror = selected;
        _followerMirrorStatus.Text = _followerMirrorPreferenceReady
            ? "跟随按键已保存；下次准备桥接时生效。"
            : "跟随按键保存失败；重新保存前不能准备桥接。";
        UpdateBridgeControls();
    }

    private bool RadarKeyConflicts(RadarHotkeyBinding binding) =>
        binding.VirtualKey == AutoPartyKeyBinding.VirtualKey(_autoPartyKeyValue) ||
        binding.VirtualKey == AutoPartyKeyBinding.VirtualKey(_followPauseKeyValue);

    private void SaveRadarHotkey()
    {
        if (!RadarHotkeyBinding.TryParse(_radarHotkeyText.Text, out var binding))
        {
            _radarHotkeyStatus.Text = "雷达快捷键无效；请使用键盘按键，可加 Ctrl、Alt 或 Shift。";
            return;
        }
        if (RadarKeyConflicts(binding))
        {
            _radarHotkeyStatus.Text = "雷达快捷键与自动组队或暂停跟随冲突；请选不同按键。";
            return;
        }
        if (!_radarHotkeyPreferences.Save(binding))
        {
            _radarHotkeyStatus.Text = "雷达快捷键保存失败，继续使用原设置。";
            return;
        }
        _radarHotkey = binding;
        _radarHotkeyText.Text = binding.DisplayText;
        _radar?.SetHotkey(binding);
        _radarHotkeyStatus.Text = $"雷达显隐快捷键：{binding.DisplayText}；仅在已绑定游戏窗口前台生效。";
    }

    private void CaptureRadarHotkey(object? sender, KeyEventArgs e)
    {
        if (!_capturingRadarHotkey) return;
        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            _capturingRadarHotkey = false;
            _radarHotkeyText.Text = _radarHotkey.DisplayText;
            _radarHotkeyStatus.Text = "已取消录入，原有雷达快捷键保持生效。";
            return;
        }
        if (e.Key is Key.None or Key.LWin or Key.RWin or Key.LeftShift or Key.RightShift or
            Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.System)
            return;
        var prefix = string.Join('+', new[]
        {
            e.KeyModifiers.HasFlag(KeyModifiers.Control) ? "Ctrl" : null,
            e.KeyModifiers.HasFlag(KeyModifiers.Alt) ? "Alt" : null,
            e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? "Shift" : null
        }.Where(value => value != null));
        var input = prefix.Length == 0 ? e.Key.ToString() : prefix + "+" + e.Key;
        if (!RadarHotkeyBinding.TryParse(input, out var binding))
        {
            _radarHotkeyStatus.Text = "该按键不支持；请使用字母、数字、功能键、方向键或常见键盘按键。";
            return;
        }
        _capturingRadarHotkey = false;
        _radarHotkeyText.Text = binding.DisplayText;
        SaveRadarHotkey();
    }

    private void UpdateAutoPartyControls()
    {
        var namesReady = _selectedTopology is not null && AutoPartyInviteNames.TryCreate(
            _selectedTopology, _inviteNames.Take(_selectedTopology.PlayerCount)
                .Select(box => box?.Text).ToArray(), out _);
        var ready = OperatingSystem.IsWindows() && !_busy && !_inviteBusy && !_autoPartyRunning &&
            _bridge is not null && _pairingSettled &&
            (!_bridge.IsHybridMode || _hybridPairing?.Phase == HybridPairingPhase.Complete) &&
            _selectedTopology is { ClientCount: > 1 } &&
            _launchedClients.Count == _selectedTopology.ClientCount &&
            _inviteTarget.SelectedIndex is >= 0 && namesReady;
        _sendInvite.IsEnabled = ready;
        _runAutoParty.IsEnabled = ready;
        _stopAutoParty.IsEnabled = _autoPartyRunning;
        _watchPartyLog.IsEnabled = _partyLogMonitor is not null || ready;
        _watchPartyLog.Content = _partyLogMonitor is null ? "开始监听离队日志" : "停止监听离队日志";
    }

    private void TogglePartyLogMonitor()
    {
        if (_partyLogMonitor is not null) { StopPartyLogMonitor(); return; }
        if (!_runAutoParty.IsEnabled) return;
        try
        {
            var monitor = new PartyLeaveLogMonitor(_launchedClients);
            monitor.Signal += signal => OnPartyLeaveSignal(monitor, signal);
            monitor.Error += error => OnPartyLogError(monitor, error);
            _partyLogMonitor = monitor;
            _autoPartyStatus.Text = "正在监听各客户端的新离队日志行；历史日志不触发恢复。";
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _autoPartyStatus.Text = $"无法开始离队日志监听：{error.Message}";
        }
        UpdateAutoPartyControls();
    }

    private void StopPartyLogMonitor()
    {
        var monitor = _partyLogMonitor;
        _partyLogMonitor = null;
        if (monitor is null) return;
        monitor.Dispose();
        if (!_closing) _autoPartyStatus.Text = "离队日志监听已停止。";
        UpdateAutoPartyControls();
    }

    private void OnPartyLogError(PartyLeaveLogMonitor source, string error) =>
        Dispatcher.UIThread.Post(() => { if (ReferenceEquals(_partyLogMonitor, source)) _autoPartyStatus.Text = error; });

    private void OnPartyLeaveSignal(PartyLeaveLogMonitor source, PartyLeaveSignal signal) => Dispatcher.UIThread.Post(async () =>
    {
        if (!ReferenceEquals(_partyLogMonitor, source) || _autoPartyRunning || !_runAutoParty.IsEnabled ||
            _selectedTopology is null || signal.Client > _selectedTopology.ClientCount) return;
        var target = PartyLeaveRouting.ResolveTarget(signal, _selectedTopology,
            _inviteNames.Take(_selectedTopology.PlayerCount).Select(box => box.Text).ToArray());
        if (target < 0) return;
        _autoPartyStatus.Text = target == 0
            ? "Client.txt 检测到队伍解散，开始全队恢复。"
            : $"Client.txt 检测到客户端 {target} 离队，开始定向恢复。";
        await RunAutoPartyAsync(target);
    });

    private async Task RunAutoPartyAsync(int requestedTargetClient = 0)
    {
        if (!_runAutoParty.IsEnabled || _selectedTopology is null || _bridge is null ||
            !AutoPartyInviteNames.TryCreate(_selectedTopology,
                _inviteNames.Take(_selectedTopology.PlayerCount).Select(box => box.Text).ToArray(), out var names) ||
            names is null) return;
        var bridge = _bridge;
        var topology = _selectedTopology;
        var clients = _launchedClients.ToArray();
        using var cancellation = new CancellationTokenSource();
        _autoPartyCancellation = cancellation;
        _autoPartyRunning = true;
        UpdateAutoPartyControls();
        var expired = false;
        void OnExpired() { expired = true; cancellation.Cancel(); }
        bridge.AutoPartyExpired += OnExpired;
        try
        {
            await StopFollowAsync();
            cancellation.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_bridge, bridge)) throw new OperationCanceledException();
            var recovery = new AutoPartyRecoverySession(bridge, topology,
                clients, names, status => _autoPartyStatus.Text = status);
            _autoPartyStatus.Text = "自动组队恢复已开始；正在等待桥接确认。";
            var observed = await recovery.RunAsync(cancellation.Token, requestedTargetClient);
            _autoPartyStatus.Text = observed
                ? requestedTargetClient == 0
                    ? "各受邀客户端画面候选人数达到所选布局；角色身份及游戏内组队结果仍待核对。"
                    : $"客户端 {requestedTargetClient} 画面候选人数达到所选布局；其他客户端及角色身份仍待核对。"
                : $"客户端 {recovery.FailedClient} 的自动组队尝试结束，画面候选人数未达到所选布局。";
        }
        catch (OperationCanceledException)
        {
            _autoPartyStatus.Text = expired
                ? "桥接的自动组队输入独占已超时，流程停止。"
                : "自动组队已取消，菜单输入已停止。";
        }
        catch (Exception error)
        {
            _autoPartyStatus.Text = $"自动组队已停止：{error.Message}";
        }
        finally
        {
            bridge.AutoPartyExpired -= OnExpired;
            if (ReferenceEquals(_autoPartyCancellation, cancellation)) _autoPartyCancellation = null;
            _autoPartyRunning = false;
            UpdateAutoPartyControls();
        }
    }

    private async Task SendOneInviteAsync()
    {
        if (!_sendInvite.IsEnabled || _selectedTopology is null ||
            !AutoPartyInviteNames.TryCreate(_selectedTopology,
                _inviteNames.Take(_selectedTopology.PlayerCount).Select(box => box.Text).ToArray(), out var names) ||
            names is null) return;
        var targetClient = _inviteTarget.SelectedIndex + 2;
        var inviter = _launchedClients[0];
        using var cancellation = new CancellationTokenSource();
        _inviteCancellation = cancellation;
        _inviteBusy = true;
        UpdateAutoPartyControls();
        _inviteStatus.Text = $"正在从客户端 1 向客户端 {targetClient} 发送一次邀请…";
        try
        {
            var result = await PoeChatInviteService.SendInviteAsync(
                inviter, names.ForClient(targetClient), cancellation.Token);
            _inviteStatus.Text = result.Ok
                ? $"客户端 1 已尝试发送邀请。请在客户端 {targetClient} 手动接受；尚未核验游戏内组队结果。"
                : $"未完成邀请：{result.Detail}";
        }
        catch (OperationCanceledException)
        {
            _inviteStatus.Text = "邀请操作已取消。";
        }
        finally
        {
            if (ReferenceEquals(_inviteCancellation, cancellation)) _inviteCancellation = null;
            _inviteBusy = false;
            UpdateAutoPartyControls();
        }
    }

    private async Task ProbeRosterAsync()
    {
        if (!_probeRoster.IsEnabled) return;
        var clients = _launchedClients.ToArray();
        _rosterBusy = true;
        UpdateRosterControls();
        _rosterStatus.Text = "正在读取已绑定游戏窗口的 Current Party 画面…";
        try
        {
            var results = await Task.Run(() => clients.Select(client =>
            {
                var captured = PartyRosterProbe.TryCount(client, out var candidates);
                return (captured, candidates);
            }).ToArray());
            if (!clients.SequenceEqual(_launchedClients)) return;
            _rosterStatus.Text = string.Join("；", results.Select((result, index) =>
                result.captured
                    ? $"客户端 {index + 1}：{result.candidates} 条候选生命条"
                    : $"客户端 {index + 1}：窗口无效或画面无法读取")) +
                "。0 条也可能是面板未打开；此检查不证明角色身份或组队成功。";
        }
        catch (Exception error)
        {
            _rosterStatus.Text = $"Current Party 画面检查失败：{error.Message}";
        }
        finally
        {
            _rosterBusy = false;
            UpdateRosterControls();
        }
    }

    private void UpdateFollowControls()
    {
        foreach (var choice in _followTargets)
            if (choice is not null) choice.IsEnabled = _follow is null && !_busy;
        foreach (var stop in _followPlayerStops)
            if (stop is not null) stop.IsEnabled = _follow is null && !_busy;
        var valid = LiveFollowPlan.TryCreate(_followStop.Text, _followCatchup.Text,
            _followMaxAge.Text, _followLogAge.Text, _followLeaderName.Text, out _);
        if (_selectedTopology?.Id is "2-split" or "3-three-singles")
            valid &= _selectedTopology.Slots.All(slot =>
                FollowPlayerStop.TryRead(_followPlayerStops[slot.PartyPlayer - 1].Text, out _));
        _startFollow.IsEnabled = OperatingSystem.IsWindows() && !_busy && !_followStopping && _follow is null &&
            _followTargetPreferenceReady && _followStopPreferenceReady && _bridge is not null && _pairingSettled &&
            (!_bridge.IsHybridMode || _hybridPairing?.Phase == HybridPairingPhase.Complete &&
             Volatile.Read(ref _hybridFocusedPlayer) == 1 && Volatile.Read(ref _hybridFocusTransition) == 0) &&
            (_selectedTopology?.Id is "2-split" or "3-three-singles") &&
            _launchedClients.Count == _selectedTopology.ClientCount && valid;
        _stopFollow.IsEnabled = _follow is not null;
        _pauseFollow.IsEnabled = _follow is not null && !_followStopping;
        _pauseFollow.Content = _follow?.Paused == true ? "恢复跟随" : "暂停跟随";
    }

    private async Task PrepareBridgeAsync(Client3PreResumeSession? pendingPrivate = null)
    {
        if ((_busy && pendingPrivate is null) || _bridge is not null ||
            _selectedTopology is null || !OperatingSystem.IsWindows())
        {
            _pairingStatus.Text = "手柄桥接需要 Windows、已选布局与空闲桥接会话。";
            return;
        }
        if (!_menuGuardPreferenceReady || _menuGuard.IsChecked is not bool ||
            !_followerMirrorPreferenceReady)
        {
            _pairingStatus.Text = "先确认并保存菜单保护及跟随按键设置，再准备桥接。";
            return;
        }
        if (HybridSelected)
        {
            await PrepareHybridBridgeAsync(adoptExistingPrivate: pendingPrivate is not null);
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
        var wasBusy = _busy;
        _busy = true;
        UpdateBridgeControls();
        _pairingStatus.Text = "正在检查 HidHide 隔离、实体手柄与虚拟手柄编号…";
        try
        {
            var gameExecutables = Enumerable.Range(0, _selectedTopology.ClientCount)
                .Select(index => _paths[index].Text?.Trim() ?? "").ToArray();
            var session = await BridgeSession.StartAsync(executable, _selectedTopology.Slots, _physicalIndex.SelectedIndex,
                _physicalDevice.Text.Trim(), gameExecutables, _rightStickFollowerOverride.IsChecked == true,
                _selectedTopology.Id is "2-split" or "3-three-singles" && _autoFlask.IsChecked == true,
                useWgi, privatePlan, lastFocusedLeads: _lastFocusedLeads.IsChecked == true,
                menuGuardEnabled: _menuGuard.IsChecked == true, followerMirror: _followerMirror,
                adoptExistingPrivate: pendingPrivate is not null);
            ActivateBridge(session, _selectedTopology);
        }
        catch (Exception error)
        {
            _pairingStatus.Text = $"手柄桥接未就绪：{error.Message}";
        }
        finally
        {
            _busy = wasBusy;
            UpdateBridgeControls();
        }
    }

    private async Task PrepareHybridBridgeAsync(BridgeSession? prelaunchBridge = null,
        bool adoptExistingPrivate = false)
    {
        var topology = _selectedTopology;
        var clients = _launchedClients.ToArray();
        if (topology is null || !HybridTopologySupport.CanBridge(topology) ||
            clients.Length != topology.ClientCount || _gameHotkeys?.IsStarted != true ||
            !_menuGuardPreferenceReady || _menuGuard.IsChecked is not bool ||
            !_followerMirrorPreferenceReady)
        {
            _pairingStatus.Text = "键鼠模式需要已绑定的游戏窗口与可用的键鼠监听；当前尚未就绪。";
            return;
        }
        if (_hybridBindings.KeyAt(0) == 0 || _hybridBindings.KeyAt(11) == 0 ||
            Enumerable.Range(1, topology.PlayerCount - 1).Any(index => _hybridBindings.KeyAt(index) == 0))
        {
            _pairingStatus.Text = "当前布局配对需要 P1、每位跟随者的聚焦键以及 A 确认键；请先在键鼠映射中分配。";
            return;
        }
        var executable = BridgeSession.FindExecutable();
        if (executable is null)
        {
            _pairingStatus.Text = "找不到 PartyPilot.Bridge.exe；请使用完整发布目录。";
            return;
        }
        var wasBusy = _busy;
        _busy = true;
        UpdateBridgeControls();
        _pairingStatus.Text = "正在核对键鼠模式的游戏窗口和虚拟手柄…";
        BridgeSession? session = null;
        try
        {
            session = prelaunchBridge ?? await BridgeSession.StartHybridAsync(executable,
                topology.Slots, clients, _hybridBindings,
                menuGuardEnabled: _menuGuard.IsChecked == true, followerMirror: _followerMirror,
                adoptExistingPrivate: adoptExistingPrivate);
            if (prelaunchBridge is not null)
                await prelaunchBridge.BindHybridAsync(topology.Slots, clients, _hybridBindings,
                    _menuGuard.IsChecked == true, _followerMirror, adoptExistingPrivate);
            Volatile.Write(ref _hybridFocusedPlayer, 1);
            Volatile.Write(ref _hybridFocusTransition, 0);
            _hybridFocusKeysDown.Clear();
            _bridge = session;
            _pairing = null;
            _hybridPairing = new HybridPairingGuide(topology);
            _pairingSettled = false;
            session.HybridFocusChanged += HybridFocusChanged;
            session.HybridConfirmReleased += HybridConfirmReleased;
            session.Ended += BridgeEnded;
            _hybridPublisher = new HybridKeyEventPublisher(session.SendHybridKeyAsync, error =>
                Dispatcher.UIThread.Post(async () =>
                {
                    if (!ReferenceEquals(_bridge, session)) return;
                    await StopBridgeAsync();
                    _pairingStatus.Text = $"键鼠输入通道已停止：{error.Message}";
                }));
            ApplyGameShortcutKeys();
            _gameHotkeys.SetHybridBindings(_hybridBindings);
            if (_hybridBindings.ContainsKey(AutoPartyKeyBinding.VirtualKey(_autoPartyKeyValue)))
                _autoPartyKeyStatus.Text = "自动组队快捷键与键鼠操作冲突，本次会话已禁用；仍可使用按钮。";
            if (_hybridBindings.ContainsKey(AutoPartyKeyBinding.VirtualKey(_followPauseKeyValue)))
                _followPauseKeyStatus.Text = "暂停键与键鼠操作冲突，本次会话已禁用；仍可使用按钮。";
            _gameHotkeys.SetHybridHandler(HandleHybridKey);
            session.StartReading();
            ShowHybridPairingStep();
            UpdateClientRows();
        }
        catch (Exception error)
        {
            if (session is not null && ReferenceEquals(_bridge, session))
                await StopBridgeAsync();
            else if (session is not null)
                await session.DisposeAsync();
            _pairingStatus.Text = $"键鼠桥接未就绪：{error.Message}";
        }
        finally
        {
            _busy = wasBusy;
            UpdateBridgeControls();
        }
    }

    private bool HandleHybridKey(int key, bool down, bool gameForeground)
    {
        var session = _bridge;
        var topology = _selectedTopology;
        if (session?.IsHybridMode != true || topology is null ||
            !_hybridBindings.ContainsKey(key)) return false;
        if (Volatile.Read(ref _autoPartyRunning))
        {
            if (_hybridPublisher?.TryPublish(key, down) != true)
                Dispatcher.UIThread.Post(async () =>
                {
                    if (!ReferenceEquals(_bridge, session)) return;
                    await StopBridgeAsync();
                    _pairingStatus.Text = "自动组队期间键鼠输入队列已满，桥接已停止。";
                });
            return gameForeground;
        }
        var focusPlayer = _hybridBindings.FocusForKey(key);
        if (focusPlayer > topology.PlayerCount) return false;
        if (down && !gameForeground) return false;
        var pairing = _hybridPairing;
        if (pairing is not null && pairing.Phase != HybridPairingPhase.Complete)
        {
            if (focusPlayer > 1 &&
                (pairing.Phase == HybridPairingPhase.AwaitNativeMain ||
                 focusPlayer != pairing.CurrentPlayer)) return gameForeground;
            if (focusPlayer == 0 && Volatile.Read(ref _hybridFocusedPlayer) > 1 &&
                pairing.Phase == HybridPairingPhase.AwaitFollowerFocus) return gameForeground;
        }
        if (focusPlayer != 0 && !down) _hybridFocusKeysDown.Remove(key);
        if (down && focusPlayer != 0 && _hybridFocusKeysDown.Add(key))
        {
            Volatile.Write(ref _hybridFocusedPlayer, focusPlayer);
            Volatile.Write(ref _hybridFocusTransition, 1);
            var generation = Interlocked.Increment(ref _hybridFocusGeneration);
            _ = CheckHybridFocusTimeoutAsync(session, focusPlayer, generation);
        }
        if (_hybridPublisher?.TryPublish(key, down) != true)
        {
            Dispatcher.UIThread.Post(async () =>
            {
                if (!ReferenceEquals(_bridge, session)) return;
                await StopBridgeAsync();
                _pairingStatus.Text = "键鼠输入队列已满，桥接已停止并释放虚拟手柄。";
            });
            return gameForeground;
        }
        return gameForeground && (focusPlayer != 0 ||
            Volatile.Read(ref _hybridFocusTransition) != 0 ||
            Volatile.Read(ref _hybridFocusedPlayer) > 1);
    }

    private async Task CheckHybridFocusTimeoutAsync(BridgeSession session, int player, int generation)
    {
        await Task.Delay(TimeSpan.FromSeconds(2));
        if (generation != Volatile.Read(ref _hybridFocusGeneration) ||
            Volatile.Read(ref _hybridFocusTransition) == 0) return;
        Dispatcher.UIThread.Post(async () =>
        {
            if (!ReferenceEquals(_bridge, session) ||
                generation != Volatile.Read(ref _hybridFocusGeneration) ||
                Volatile.Read(ref _hybridFocusTransition) == 0) return;
            await StopBridgeAsync();
            _pairingStatus.Text = $"P{player} 焦点切换未获得桥接确认，键鼠输入已停止。";
        });
    }

    private async void HybridFocusChanged(int player, int pid, long handle)
    {
        var session = _bridge;
        if (session?.IsHybridMode != true) return;
        var topology = _selectedTopology;
        var assignment = topology?.Slots.FirstOrDefault(slot => slot.PartyPlayer == player);
        var client = assignment is null || assignment.Client > _launchedClients.Count
            ? null : _launchedClients[assignment.Client - 1];
        if (client is null ||
            client.Pid != pid || client.WindowHandle != handle)
        {
            await StopBridgeAsync();
            _pairingStatus.Text = "键鼠焦点与绑定窗口不一致，桥接已停止。";
            return;
        }
        if (!PoeChatInviteService.IsExactForeground(client) &&
            !await PoeChatInviteService.FocusExactWindowAsync(client, CancellationToken.None))
        {
            await StopBridgeAsync();
            _pairingStatus.Text = $"无法切到 P{player} 所在游戏窗口，键鼠桥接已停止。";
            return;
        }
        if (!ReferenceEquals(_bridge, session)) return;
        Volatile.Write(ref _hybridFocusedPlayer, player);
        Volatile.Write(ref _hybridFocusTransition, 0);
        Interlocked.Increment(ref _hybridFocusGeneration);
        if (_hybridPairing?.ObserveFocus(player) == true) ShowHybridPairingStep();
        else if (_hybridPairing?.Phase == HybridPairingPhase.Complete)
            _pairingStatus.Text = player == 1
                ? "已返回 P1 游戏窗口；键鼠配对由用户确认，可准备实时跟随。"
                : $"P{player} 游戏窗口已在前台；按 {HybridKeyLabel(0)} 返回 P1 后可启动实时跟随。";
        UpdateFollowControls();
    }

    private async void HybridConfirmReleased(int player, int pid, long handle)
    {
        var session = _bridge;
        if (session?.IsHybridMode != true) return;
        var assignment = _selectedTopology?.Slots.FirstOrDefault(slot => slot.PartyPlayer == player);
        var client = assignment is null || assignment.Client > _launchedClients.Count
            ? null : _launchedClients[assignment.Client - 1];
        if (client is null || client.Pid != pid || client.WindowHandle != handle)
        {
            await StopBridgeAsync();
            _pairingStatus.Text = "键鼠确认键来自非当前绑定窗口，桥接已停止。";
            return;
        }
        if (_hybridPairing?.ObserveConfirmRelease(player) == true) ShowHybridPairingStep();
    }

    private void ShowHybridPairingStep()
    {
        var guide = _hybridPairing;
        if (guide is null) return;
        _pairingStatus.Text = guide.Phase switch
        {
            HybridPairingPhase.AwaitNativeMain =>
                "P1 使用鼠标登录和选角；进入游戏后点击“P1 已进入游戏”。此步由用户确认，尚无游戏内身份验证。",
            HybridPairingPhase.AwaitFollowerFocus =>
                $"在游戏窗口按 {HybridKeyLabel(guide.CurrentPlayer - 1)}，切换到 P{guide.CurrentPlayer} 的客户端。",
            HybridPairingPhase.AwaitConfirmRelease =>
                $"P{guide.CurrentPlayer} 窗口已前台；使用 {HybridKeyLabel(6)}/{HybridKeyLabel(7)}/{HybridKeyLabel(8)}/{HybridKeyLabel(9)} 选角，按 {HybridKeyLabel(11)} 发送虚拟手柄 A。",
            HybridPairingPhase.AwaitUserConfirmation =>
                $"已观察到 P{guide.CurrentPlayer} 的虚拟 A 键松开；请确认该角色确已进入游戏，再点击“已在游戏内确认此角色”。",
            _ => $"所有角色已由用户逐个确认。按 {HybridKeyLabel(0)} 返回 P1；仍须在游戏中核对虚拟手柄隔离与实际组队。"
        };
        UpdateBridgeControls();
    }

    private string HybridKeyLabel(int index) => AutoPartyKeyBinding.Options.FirstOrDefault(option =>
        AutoPartyKeyBinding.VirtualKey(option) == _hybridBindings.KeyAt(index)) ?? "未绑定";

    private void ActivateBridge(BridgeSession session, PartyTopology topology)
    {
        session.AReleased += BridgeAReleased;
        session.PairingArmed += BridgePairingArmed;
        session.PairingCompleted += BridgePairingCompleted;
        session.LoginStatusChanged += BridgeLoginStatusChanged;
        session.Ended += BridgeEnded;
        _bridge = session;
        _pairing = new PairingGuide(topology);
        session.StartReading();
        ShowCurrentPairingStep();
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
        var bridge = _bridge;
        var topology = _selectedTopology!;
        _armRequested = true;
        _loginStatus = "waiting";
        UpdateBridgeControls();
        try
        {
            if (topology.Clients[current.Client - 1].Count == 1 &&
                _accountPresent[current.Client - 1] &&
                _autoLoginChoices[current.Client - 1].IsChecked == true &&
                _manualAutoLoginWindows[current.Client - 1] != client)
            {
                var credential = _accountProfiles.Load(current.Client);
                var stored = _loginCalibrations.Load(current.Client);
                if (credential is null || !stored.Success || stored.Calibration is not { } calibration ||
                    !LoginCalibrationCapture.MatchesCurrentViewport(client, calibration, topology.Id))
                {
                    _pairingStatus.Text = "自动填入需要已保存账号、已确认字段标记及匹配的游戏窗口布局；未发送登录输入。可重新标记或改为手动登录。";
                    _autoLoginResults[current.Client - 1].Text = _pairingStatus.Text;
                    _armRequested = false;
                    UpdateBridgeControls();
                    return;
                }
                using var cancellation = new CancellationTokenSource();
                _autoLoginFillCancellation = cancellation;
                UpdateAutoLoginControls();
                _pairingStatus.Text = $"正在核对客户端 {current.Client} 登录画面并填入已保存账号…";
                AutoLoginFillResult filled;
                try
                {
                    var alreadyPrepared = _preparedAutoLoginWindows[current.Client - 1] == client;
                    var service = new AutoLoginFillService(new WindowsLoginInputPort(client),
                        captureAttempts: alreadyPrepared ? 45 : 195);
                    filled = alreadyPrepared
                        ? await service.VerifyPreparedAsync(calibration, cancellation.Token)
                        : await service.FillAsync(credential, calibration, cancellation.Token);
                }
                finally
                {
                    if (ReferenceEquals(_autoLoginFillCancellation, cancellation))
                        _autoLoginFillCancellation = null;
                    UpdateAutoLoginControls();
                }
                if (!filled.Success || !ReferenceEquals(_bridge, bridge) ||
                    !ReferenceEquals(_selectedTopology, topology) ||
                    current.Client > _launchedClients.Count ||
                    _launchedClients[current.Client - 1] != client)
                {
                    _preparedAutoLoginWindows[current.Client - 1] = null;
                    _pairingStatus.Text = filled.Message;
                    _autoLoginResults[current.Client - 1].Text = filled.Message;
                    _armRequested = false;
                    UpdateBridgeControls();
                    return;
                }
                _preparedAutoLoginWindows[current.Client - 1] = client;
                _autoLoginResults[current.Client - 1].Text = filled.Message;
            }
            _pairingStatus.Text = "已发送当前角色的配对准备请求；等待桥接确认。";
            await bridge.ArmPairingAsync(current.RuntimePad, client);
        }
        catch (OperationCanceledException)
        {
            _armRequested = false;
            _pairingStatus.Text = "自动填入已取消；没有提交登录。";
            UpdateBridgeControls();
        }
        catch (IOException error)
        {
            _armRequested = false;
            _pairingStatus.Text = error.Message;
            UpdateBridgeControls();
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
        UpdateAutoPartyControls();
    }

    private async void BridgeEnded(string reason)
    {
        var hybrid = _bridge?.IsHybridMode == true;
        await StopBridgeAsync();
        _pairingStatus.Text = $"{(hybrid ? "键鼠" : "手柄")}桥接已停止：{reason}";
    }

    private async Task ConfirmRoleAsync()
    {
        if (_bridge?.IsHybridMode == true)
        {
            await ConfirmHybridRoleAsync();
            return;
        }
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

    private async Task ConfirmHybridRoleAsync()
    {
        var guide = _hybridPairing;
        if (_bridge?.IsHybridMode != true || guide is null) return;
        var advanced = guide.Phase switch
        {
            HybridPairingPhase.AwaitNativeMain => guide.ConfirmNativeMain(),
            HybridPairingPhase.AwaitUserConfirmation => guide.ConfirmFollowerJoined(),
            _ => false
        };
        if (!advanced) return;
        _pairingHasStarted = true;
        if (guide.Phase == HybridPairingPhase.Complete)
        {
            _busy = true;
            UpdateBridgeControls();
            try { await _bridge.EnableHybridMainFollowMirrorAsync(); }
            catch (Exception error)
            {
                await StopBridgeAsync();
                _pairingStatus.Text = $"键鼠配对完成信号未被桥接确认：{error.Message}";
                return;
            }
            finally
            {
                _busy = false;
                UpdateBridgeControls();
                UpdateFollowControls();
            }
        }
        _pairingSettled = guide.Phase == HybridPairingPhase.Complete;
        ShowHybridPairingStep();
        UpdateClientRows();
        UpdateFollowControls();
        UpdateAutoPartyControls();
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
                        : $"请打开客户端 {current.Client} 的登录/角色画面，再点“{_armPairing.Content}”。";
    }

    private async Task StopBridgeAsync()
    {
        var wasHybrid = _bridge?.IsHybridMode == true;
        _gameHotkeys?.SetHybridHandler(null);
        var publisher = _hybridPublisher;
        _hybridPublisher = null;
        if (publisher is not null) await publisher.DisposeAsync();
        if (wasHybrid)
            _autoPartyKeyStatus.Text = $"自动组队快捷键：{_autoPartyKeyValue}；仅在已绑定游戏窗口前台生效。";
        if (wasHybrid)
            _followPauseKeyStatus.Text = $"暂停跟随快捷键：{_followPauseKeyValue}；仅在已绑定游戏窗口前台生效。";
        Volatile.Write(ref _hybridFocusedPlayer, 1);
        Volatile.Write(ref _hybridFocusTransition, 0);
        _hybridFocusKeysDown.Clear();
        Interlocked.Increment(ref _hybridFocusGeneration);
        _autoLoginFillCancellation?.Cancel();
        StopPartyLogMonitor();
        _inviteCancellation?.Cancel();
        _autoPartyCancellation?.Cancel();
        await StopFollowAsync();
        var session = _bridge;
        _bridge = null;
        ApplyGameShortcutKeys();
        _pairing = null;
        _hybridPairing = null;
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
        session.HybridFocusChanged -= HybridFocusChanged;
        session.HybridConfirmReleased -= HybridConfirmReleased;
        session.Ended -= BridgeEnded;
        await session.DisposeAsync();
        _pairingStatus.Text = wasHybrid
            ? "键鼠桥接已停止，虚拟手柄已释放。"
            : "手柄桥接已停止，虚拟手柄已释放。";
    }

    private async Task EndSessionAsync()
    {
        if (!OperatingSystem.IsWindows() || _busy ||
            (_bridge is null && _launchedClients.Count == 0)) return;
        _busy = true;
        UpdateClientRows();
        var boundCount = _launchedClients.Count;
        var identities = _sessionGameIdentities.ToArray();
        try
        {
            _status.Text = "正在停止输入并结束游戏会话…";
            await StopBridgeAsync();
            await StopRadarAsync();
            StopPrivacy();
            await StopAllResourcesAsync();
            var results = await SessionGameClientShutdown.CloseAsync(identities);
            var unverified = boundCount - identities.Length;
            _status.Text = $"会话已停止；{results.Count(result => result.Closed)} 个游戏进程已关闭" +
                (results.Any(result => result.Forced)
                    ? $"（其中 {results.Count(result => result.Forced)} 个在正常关闭超时后终止）" : "") +
                (unverified > 0 ? $"，{unverified} 个进程身份未核实、未尝试关闭" : "") +
                (results.Any(result => !result.Closed)
                    ? "；未关闭：" + string.Join("；", results.Where(result => !result.Closed)
                        .Select(result => $"PID {result.Pid} {result.Detail}")) : "") + "。";
            _launchedClients = [];
            _sessionGameIdentities = [];
            Array.Clear(_preparedAutoLoginWindows);
            Array.Clear(_manualAutoLoginWindows);
            _rosterStatus.Text = "会话已结束；再次检查 Current Party 前需重新绑定游戏窗口。";
        }
        catch (Exception error)
        {
            _status.Text = $"结束会话未完成：{error.Message}";
        }
        finally
        {
            _busy = false;
            UpdateClientRows();
        }
    }

    private async Task StartFollowAsync()
    {
        if (_bridge is null || !_pairingSettled || _selectedTopology is null || _follow is not null || _followStopping ||
            _bridge.IsHybridMode && (_hybridPairing?.Phase != HybridPairingPhase.Complete ||
                Volatile.Read(ref _hybridFocusedPlayer) != 1 || Volatile.Read(ref _hybridFocusTransition) != 0) ||
            !_followTargetPreferenceReady || !_followStopPreferenceReady ||
            !LiveFollowPlan.TryCreate(_followStop.Text, _followCatchup.Text,
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
            var followTargets = new Dictionary<int, int>();
            var playerStops = new Dictionary<int, double>();
            foreach (var slot in _selectedTopology.Slots)
            {
                if (!FollowTargetChoices.TryRead(_selectedTopology, slot.PartyPlayer,
                        _followTargets[slot.PartyPlayer - 1].SelectedItem as string, out var target))
                    throw new InvalidOperationException($"P{slot.PartyPlayer} 的跟随目标不属于当前布局。");
                if (target != 0) followTargets[slot.PartyPlayer] = target;
                if (!FollowPlayerStop.TryRead(_followPlayerStops[slot.PartyPlayer - 1].Text,
                        out var stop))
                    throw new InvalidOperationException($"P{slot.PartyPlayer} 的停止距离必须在 0–200 之间。");
                if (stop is double value) playerStops[slot.PartyPlayer] = value;
            }
            IReadOnlyDictionary<int, string>? memberNames = null;
            if (AutoPartyInviteNames.TryCreate(_selectedTopology,
                    _inviteNames.Take(_selectedTopology.PlayerCount).Select(box => box.Text).ToArray(),
                    out var inviteNames) && inviteNames is not null)
            {
                var confirmed = _selectedTopology.Slots.Where(slot => slot.PartyPlayer != 1)
                    .ToDictionary(slot => slot.PartyPlayer,
                        slot => inviteNames.ForClient(slot.Client));
                if (plan.LeaderName.Length == 0) confirmed[1] = inviteNames.ForClient(1);
                memberNames = confirmed;
            }
            var session = await LiveFollowSession.StartAsync(executable, _selectedTopology,
                _launchedClients, _bridge, plan, memberNames, followTargets, playerStops);
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
        if (frame.Reason == "follow paused")
        {
            _followStatus.Text = "跟随已暂停；所有自动方向已回中，恢复后仍需新鲜位置与区域证据。";
            return;
        }
        if (_selectedTopology?.Id == "3-three-singles" &&
            frame.Reason is not ("party disbanded" or "local client left party" or "followed member left party"))
        {
            var states = string.Join(" · ", frame.PadCommands.Select(command =>
                $"P{command.Player} {command.Status switch
                {
                    "following" => "跟随",
                    "catching_up" => "追赶",
                    "holding" => "停靠",
                    _ when command.Reason == "dead follower" => "等待（角色死亡）",
                    _ when command.Reason == "missing position" => "等待（缺少位置）",
                    _ when command.Reason == "different or unknown instance" => "等待（区域未核对）",
                    _ when command.Reason == "stale position" => "等待（位置过期）",
                    _ => "等待"
                }}"));
            _followStatus.Text = $"MAIN P{frame.FocusedPlayer} · {states}；游戏内效果待验证。";
            return;
        }
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

    private void ToggleFollowPause()
    {
        if (_follow is not { } session || _followStopping)
        {
            _followStatus.Text = "实时跟随尚未运行；请先完成配对并开始跟随。";
            return;
        }
        try
        {
            session.SetPaused(!session.Paused);
            _followStatus.Text = session.Paused
                ? "已请求暂停跟随；等待实时决策确认自动方向回中。"
                : "已请求恢复跟随；仍需新鲜位置与区域证据。";
            UpdateFollowControls();
        }
        catch (InvalidOperationException error)
        {
            _followStatus.Text = $"跟随状态未切换：{error.Message}";
        }
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
        if (HybridSelected && _selectedTopology is not null &&
            !HybridTopologySupport.CanBridge(_selectedTopology) &&
            !(_selectedTopology.PlayerCount == 1 && HybridTopologySupport.HasNativeP1(_selectedTopology)))
        {
            _status.Text = "目标版键鼠模式要求客户端 1 只有 P1；当前独立版暂不支持此布局的键鼠桥接。";
            return;
        }
        var reusePreparedBridge = action == ClientAction.Start && _bridge is not null &&
            !_pairingHasStarted && _launchedClients.Count == 0 &&
            _selectedTopology?.ClientCount is 1 or 2;
        if (_busy || _pairingHasStarted || (_bridge is not null && !reusePreparedBridge))
        {
            _status.Text = "请先停止手柄桥接，再重新检查、启动或绑定游戏窗口。";
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
        var client3Name = _selectedTopology.ClientCount == 3
            ? Path.GetFileNameWithoutExtension(_paths[2].Text?.Trim()) : "";
        var prepareClient3BeforeResume = action == ClientAction.Start &&
            _selectedTopology.ClientCount == 3 && string.IsNullOrWhiteSpace(_uris[2].Text) &&
            (string.Equals(client3Name, "PathOfExile", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(client3Name, "PathOfExile_x64", StringComparison.OrdinalIgnoreCase));
        if (prepareClient3BeforeResume)
        {
            startInfo.RedirectStandardInput = true;
            startInfo.ArgumentList.Add("-pre-resume-client3");
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
            if (action == ClientAction.Attach && !string.IsNullOrWhiteSpace(_attachPids[index].Text))
            {
                var rawPid = _attachPids[index].Text!.Trim();
                if (!int.TryParse(rawPid, out var pid) || pid <= 0)
                {
                    _status.Text = $"客户端 {index + 1} 的进程号必须是正整数。";
                    return;
                }
                startInfo.ArgumentList.Add("-pid");
                startInfo.ArgumentList.Add($"{index + 1}={pid}");
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
        UpdateClientRows();
        _status.Text = action switch
        {
            ClientAction.Start => "正在检查控制器隔离和实体输入…",
            ClientAction.Attach => "正在识别已运行游戏窗口…",
            _ => "正在检查配置…"
        };
        BridgeSession? prelaunchBridge = null;
        Client3PreResumeSession? pendingPrivate = null;
        try
        {
            if (action == ClientAction.Start && HybridSelected)
            {
                if (!_menuGuardPreferenceReady || _menuGuard.IsChecked is not bool ||
                    !_followerMirrorPreferenceReady)
                {
                    _status.Text = "先确认并保存菜单保护及跟随按键设置；游戏尚未启动。";
                    return;
                }
                if (!HybridTopologySupport.CanBridge(_selectedTopology) ||
                    _hybridBindings.KeyAt(0) == 0 || _hybridBindings.KeyAt(11) == 0 ||
                    Enumerable.Range(1, _selectedTopology.PlayerCount - 1)
                        .Any(index => _hybridBindings.KeyAt(index) == 0))
                {
                    _status.Text = "键鼠布局或配对按键尚未就绪；游戏尚未启动。";
                    return;
                }
                var bridgeExecutable = BridgeSession.FindExecutable();
                if (bridgeExecutable is null)
                {
                    _status.Text = "找不到 PartyPilot.Bridge.exe，无法在游戏启动前准备虚拟手柄。";
                    return;
                }
                var gameExecutables = Enumerable.Range(0, _selectedTopology.ClientCount)
                    .Select(index => _paths[index].Text?.Trim() ?? "").ToArray();
                _status.Text = "正在创建键鼠虚拟手柄，游戏尚未启动…";
                prelaunchBridge = await BridgeSession.StartHybridPrelaunchAsync(bridgeExecutable,
                    _selectedTopology.Slots, gameExecutables, _hybridBindings,
                    _menuGuard.IsChecked == true, _followerMirror);
                _status.Text = "键鼠虚拟手柄已就绪；正在启动游戏窗口…";
            }
            else if (action == ClientAction.Start)
            {
                if (!_menuGuardPreferenceReady || _menuGuard.IsChecked is not bool ||
                    !_followerMirrorPreferenceReady)
                {
                    _status.Text = "先确认并保存菜单保护及跟随按键设置；游戏尚未启动。";
                    return;
                }
                if (_isolationConfirmed.IsChecked != true || string.IsNullOrWhiteSpace(_physicalDevice.Text))
                {
                    _status.Text = "启动前请确认实体手柄隔离并填写所选设备 ID；游戏尚未启动。";
                    return;
                }
                var bridgeExecutable = BridgeSession.FindExecutable();
                if (bridgeExecutable is null)
                {
                    _status.Text = "找不到 PartyPilot.Bridge.exe，无法完成启动前检查；游戏尚未启动。";
                    return;
                }
                var useWgi = _selectedTopology.Slots.Count(slot => slot.Client != 3) == 4;
                var gameExecutables = Enumerable.Range(0, _selectedTopology.ClientCount)
                    .Select(index => _paths[index].Text?.Trim() ?? string.Empty).ToArray();
                var preflight = await ControllerLaunchPreflight.CheckAsync(bridgeExecutable,
                    _physicalDevice.Text.Trim(), gameExecutables,
                    useWgi ? null : _physicalIndex.SelectedIndex, useWgi);
                if (!preflight.Ready)
                {
                    _status.Text = preflight.Message;
                    return;
                }
                if (_selectedTopology.ClientCount <= 2)
                {
                    if (!_leadershipPreferenceReady || _lastFocusedLeads.IsChecked is not bool)
                    {
                        _status.Text = "先确认 MAIN 切换设置，再准备虚拟手柄；游戏尚未启动。";
                        return;
                    }
                    if (reusePreparedBridge && _bridge is { IsRunning: true } prepared)
                        prelaunchBridge = prepared;
                    else
                    {
                        if (_bridge is not null) await StopBridgeAsync();
                        _status.Text = "正在连接虚拟手柄并核对槽位，游戏尚未启动…";
                        var session = await BridgeSession.StartAsync(bridgeExecutable,
                            _selectedTopology.Slots, _physicalIndex.SelectedIndex,
                            _physicalDevice.Text.Trim(), gameExecutables,
                            _rightStickFollowerOverride.IsChecked == true,
                            _selectedTopology.Id is "2-split" or "3-three-singles" && _autoFlask.IsChecked == true,
                            useWgi, lastFocusedLeads: _lastFocusedLeads.IsChecked == true,
                            menuGuardEnabled: _menuGuard.IsChecked == true, followerMirror: _followerMirror);
                        ActivateBridge(session, _selectedTopology);
                        prelaunchBridge = session;
                    }
                    _status.Text = "虚拟手柄已就绪；正在启动并等待游戏窗口…";
                }
                else
                    _status.Text = "启动前只读检查通过；正在等待游戏窗口…";
            }
            if (action != ClientAction.Check)
            {
                _fpsGamesClosed.IsChecked = false;
                await StopRadarAsync();
                StopPrivacy();
                await StopAllResourcesAsync();
                _launchedClients = [];
                _sessionGameIdentities = [];
                Array.Clear(_preparedAutoLoginWindows);
                Array.Clear(_manualAutoLoginWindows);
                _rosterStatus.Text = "正在重新识别游戏窗口；完成绑定后可检查 Current Party。";
            }
            if (prelaunchBridge is not null && !prelaunchBridge.IsRunning)
                throw new InvalidOperationException("虚拟手柄在游戏启动前中断；游戏尚未启动。");
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("启动器未能启动");
            var error = process.StandardError.ReadToEndAsync();
            Task<string> output;
            if (prepareClient3BeforeResume)
            {
                try
                {
                    var notice = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromMinutes(3));
                    var expectedGame = _paths[2].Text?.Trim() ?? "";
                    if (!Client3PreResumeSession.TryReadLauncherNotice(notice, expectedGame, out var pid))
                        throw new InvalidOperationException("启动器没有确认第三客户端的暂停进程身份。");
                    var privateBridge = BridgeSession.FindExecutable() ??
                        throw new InvalidOperationException("找不到第三客户端私有手柄桥接程序。");
                    var includeP6 = _selectedTopology.Slots.Count(slot => slot.Client == 3) == 2;
                    pendingPrivate = await Client3PreResumeSession.StartAsync(
                        privateBridge, pid, expectedGame, includeP6);
                    await process.StandardInput.WriteLineAsync($"resume {pid}");
                    await process.StandardInput.FlushAsync();
                    await pendingPrivate.ConfirmResumeAsync();
                    output = process.StandardOutput.ReadToEndAsync();
                }
                catch (Exception preparationError)
                {
                    try { process.StandardInput.Close(); } catch (InvalidOperationException) { }
                    // The launcher owns cleanup of a still-suspended game process.
                    // Closing stdin makes its pre-resume callback reject the launch.
                    try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(50)); }
                    catch (TimeoutException) { }
                    if (process.HasExited)
                    {
                        var detail = (await process.StandardOutput.ReadToEndAsync()).Trim();
                        var diagnostic = (await error).Trim();
                        if (detail.Length > 500) detail = detail[..500];
                        if (diagnostic.Length > 500) diagnostic = diagnostic[..500];
                        throw new InvalidOperationException(
                            $"{preparationError.Message} {detail} {diagnostic}".Trim(), preparationError);
                    }
                    throw;
                }
            }
            else output = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            await Task.WhenAll(output, error);
            if (action != ClientAction.Check && process.ExitCode == 0)
            {
                try
                {
                    _launchedClients = LaunchedClients.ParseSuccessfulLaunch(output.Result, _selectedTopology.ClientCount);
                    _sessionGameIdentities = SessionGameClientShutdown.Capture(_launchedClients);
                    if (_pairing is not null) ShowCurrentPairingStep();
                }
                catch (Exception bindingError)
                {
                    if (prelaunchBridge is not null && ReferenceEquals(_bridge, prelaunchBridge))
                        await StopBridgeAsync();
                    else if (prelaunchBridge?.IsRunning == true)
                        await prelaunchBridge.DisposeAsync();
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
            if (prelaunchBridge is not null && (process.ExitCode != 0 ||
                (!prelaunchBridge.IsHybridMode && !ReferenceEquals(_bridge, prelaunchBridge)) ||
                !prelaunchBridge.IsRunning))
            {
                if (ReferenceEquals(_bridge, prelaunchBridge)) await StopBridgeAsync();
                else if (prelaunchBridge.IsRunning) await prelaunchBridge.DisposeAsync();
                _status.Text = process.ExitCode == 0
                    ? "游戏窗口已识别，但启动前手柄桥接中断；请先重新准备桥接再配对。"
                    : message + Environment.NewLine + "虚拟手柄已释放。";
                return;
            }
            if (action != ClientAction.Check && process.ExitCode == 0 &&
                _launchedClients.Count == _selectedTopology.ClientCount)
            {
                if (prelaunchBridge?.IsHybridMode == true)
                {
                    await PrepareHybridBridgeAsync(prelaunchBridge,
                        adoptExistingPrivate: pendingPrivate is not null);
                    if (!ReferenceEquals(_bridge, prelaunchBridge))
                    {
                        if (prelaunchBridge.IsRunning) await prelaunchBridge.DisposeAsync();
                        _status.Text = "游戏窗口已识别，但键鼠桥接绑定失败：" + _pairingStatus.Text;
                        return;
                    }
                }
                else if (pendingPrivate is not null)
                {
                    await PrepareBridgeAsync(pendingPrivate);
                    if (_bridge is null)
                        throw new InvalidOperationException("第三客户端私有手柄未能接入主桥接：" +
                            _pairingStatus.Text);
                }
                if (pendingPrivate is not null)
                {
                    await pendingPrivate.HandoffAsync();
                    await pendingPrivate.DisposeAsync();
                    pendingPrivate = null;
                }
                var preparation = await PrepareAutoLoginsAfterBindingAsync(_selectedTopology,
                    _launchedClients.ToArray());
                if (!string.IsNullOrEmpty(preparation))
                    _status.Text = message + Environment.NewLine + preparation;
            }
        }
        catch (Exception exception)
        {
            if (prelaunchBridge is not null && ReferenceEquals(_bridge, prelaunchBridge))
                await StopBridgeAsync();
            else if (prelaunchBridge?.IsRunning == true)
                await prelaunchBridge.DisposeAsync();
            else if (pendingPrivate is not null && _bridge is not null)
                await StopBridgeAsync();
            _status.Text = $"启动或绑定失败：{exception.Message}";
        }
        finally
        {
            try
            {
                if (pendingPrivate is not null) await pendingPrivate.DisposeAsync();
            }
            finally
            {
                _busy = false;
                UpdateClientRows();
            }
        }
    }

    private async Task<string?> PrepareAutoLoginsAfterBindingAsync(PartyTopology topology,
        IReadOnlyList<LaunchedClient> clients)
    {
        using var cancellation = new CancellationTokenSource();
        _autoLoginFillCancellation = cancellation;
        UpdateAutoLoginControls();
        try
        {
            var credentials = new AccountCredential?[topology.ClientCount];
            for (var index = 0; index < credentials.Length; index++)
            {
                if (topology.Clients[index].Count != 1 ||
                    _autoLoginChoices[index].IsChecked != true) continue;
                if (!_autoLoginPreferenceReady[index])
                    return $"客户端 {index + 1} 自动填入设置无效；请重新选择，未发送登录输入。";
                credentials[index] = _accountProfiles.Load(index + 1);
                if (credentials[index] is null)
                    return $"客户端 {index + 1} 没有可读取的受保护账号资料；未发送登录输入。";
            }
            if (credentials.OfType<AccountCredential>()
                .GroupBy(credential => credential.Email, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1))
                return "多个独立客户端保存了同一账号邮箱；已停止所有自动填入。";
            var configurations = new AutoLoginStartupConfiguration[topology.ClientCount];
            var manualCount = 0;
            for (var index = 0; index < configurations.Length; index++)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (topology.Clients[index].Count != 1 ||
                    _autoLoginChoices[index].IsChecked != true)
                {
                    configurations[index] = new(false, null, null);
                    continue;
                }
                var credential = credentials[index]!;
                var calibration = _loginCalibrations.Load(index + 1).Calibration;
                if (calibration is null)
                {
                    _status.Text = $"等待客户端 {index + 1} 登录画面，以便首次标记字段…";
                    var frame = await new LoginMarkCaptureService(
                        new WindowsLoginInputPort(clients[index])).CaptureAsync(cancellation.Token);
                    if (frame is null)
                    {
                        _manualAutoLoginWindows[index] = clients[index];
                        _autoLoginResults[index].Text = "未能核对登录画面；此窗口本次改为手动登录。";
                        manualCount++;
                        configurations[index] = new(false, null, null);
                        continue;
                    }
                    var dialog = new LoginCalibrationDialog(index + 1, frame, topology.Id);
                    calibration = await dialog.ShowDialog<LoginCalibration?>(this);
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (calibration is null)
                    {
                        _manualAutoLoginWindows[index] = clients[index];
                        _autoLoginResults[index].Text = "已选择此窗口本次手动登录；没有保存字段标记。";
                        manualCount++;
                        configurations[index] = new(false, null, null);
                        continue;
                    }
                    if (_closing || !ReferenceEquals(_selectedTopology, topology) ||
                        index >= _launchedClients.Count ||
                        _launchedClients[index] != clients[index] ||
                        !LoginCalibrationCapture.MatchesCurrentViewport(
                            clients[index], calibration, topology.Id))
                        return "标记期间窗口身份或布局发生变化；没有保存字段标记。";
                    var saved = _loginCalibrations.Save(index + 1, calibration);
                    if (!saved.Success) return saved.Message;
                    _calibrationResults[index].Text = saved.Message;
                }
                configurations[index] = new(true, credential, calibration);
            }
            if (!AutoLoginStartupPlan.TryCreate(topology, clients, configurations,
                    out var items, out var reason)) return reason;
            foreach (var item in items)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (_closing || !ReferenceEquals(_selectedTopology, topology) ||
                    item.Client > _launchedClients.Count ||
                    _launchedClients[item.Client - 1] != item.Window)
                    return "游戏窗口绑定已变化；自动填入停止。";
                _status.Text = $"等待客户端 {item.Client} 登录画面并核对账号字段…";
                var result = await new AutoLoginFillService(new WindowsLoginInputPort(item.Window))
                    .FillAsync(item.Credential, item.Calibration, cancellation.Token);
                _autoLoginResults[item.Client - 1].Text = result.Message;
                if (!result.Success) return $"客户端 {item.Client}：{result.Message}";
                if (!ReferenceEquals(_selectedTopology, topology) ||
                    item.Client > _launchedClients.Count ||
                    _launchedClients[item.Client - 1] != item.Window)
                    return "游戏窗口绑定已变化；自动填入停止。";
                _preparedAutoLoginWindows[item.Client - 1] = item.Window;
                await Task.Delay(900, cancellation.Token);
            }
            if (items.Count == 0 && manualCount == 0) return null;
            return $"已为 {items.Count} 个独立客户端填入账号；{manualCount} 个窗口本次手动登录。配对时将重新核验已填入的登录画面。";
        }
        catch (OperationCanceledException)
        {
            return "自动填入已取消；未提交登录。";
        }
        catch (IOException error)
        {
            return error.Message;
        }
        finally
        {
            if (ReferenceEquals(_autoLoginFillCancellation, cancellation))
                _autoLoginFillCancellation = null;
            UpdateAutoLoginControls();
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
