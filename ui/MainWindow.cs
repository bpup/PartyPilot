using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
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
    private readonly Button[] _folderButtons = new Button[3];
    private readonly ComboBox[] _gameChoices = new ComboBox[3];
    private readonly TextBlock[] _gameLocations = new TextBlock[3];
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
    private readonly TextBlock _focusGuide = new();
    private readonly Button _hybridBindingsButton = new();
    private readonly TextBlock _hybridBindingsStatus = new();
    private readonly HybridBindingPreferenceStore _hybridBindingPreferences = new();
    private HybridBindingConfig _hybridBindings = HybridBindingConfig.Default;
    private readonly CheckBox _rightStickFollowerOverride = new();
    private readonly CheckBox _engageDodge = new();
    private readonly ComboBox _engageDodgeBinding = new();
    private readonly TextBlock _engageDodgeStatus = new();
    private readonly RightStickDodgePreferenceStore _engageDodgePreferences = new();
    private bool _engageDodgePreferenceReady;
    private readonly CheckBox _autoFlask = new();
    private readonly Button _flaskRulesButton = new();
    private readonly TextBlock _flaskRulesStatus = new();
    private readonly FlaskRulePreferenceStore _flaskRulePreferences;
    private readonly Button _flaskCustomButton = new();
    private readonly TextBlock _flaskCustomStatus = new();
    private readonly FlaskCustomPreferenceStore _flaskCustomPreferences;
    private readonly Button _flaskSupportButton = new();
    private readonly TextBlock _flaskSupportStatus = new();
    private readonly FlaskSupportPreferenceStore _flaskSupportPreferences;
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
    private readonly TextBox[] _terrainBindingPaths = new TextBox[3];
    private readonly StackPanel[] _terrainBindingRows = new StackPanel[3];
    private readonly CheckBox _classicSprintCatchUp = new();
    private readonly CheckBox _sprintCatchUp = new();
    private readonly CheckBox _sprintCatchUpPriority = new();
    private readonly CheckBox _mirrorAttackDirection = new();
    private readonly FollowInputPreferenceStore _followInputPreferences;
    private readonly TextBlock _followInputPreferenceStatus = new();
    private bool _followInputPreferenceReady;
    private readonly CheckBox _aggressiveBlinkCatchUp = new();
    private readonly TextBox _aggressiveSprintRetryDelay = new();
    private readonly CheckBox _closeRangeMirror = new();
    private readonly TextBox _closeMirrorDuration = new();
    private readonly CheckBox _classicCollisionDodge = new();
    private readonly CheckBox _experimentalWarpClick = new();
    private readonly Button _manualWarpToMain = new();
    private readonly TextBlock _terrainBindingStatus = new();
    private readonly TextBox _followLeaderName = new();
    private readonly ComboBox[] _followTargets = new ComboBox[6];
    private readonly TextBox[] _followPlayerStops = new TextBox[6];
    private readonly FollowStopPreferenceStore _followStopPreferences;
    private double?[] _configuredFollowStops = new double?[6];
    private bool _followStopFreshProfile;
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
    private readonly TextBox _radarMapOpacity = new();
    private readonly TextBox _radarBorderOpacity = new();
    private readonly TextBox _radarBackgroundOpacity = new();
    private readonly TextBlock _radarAppearanceStatus = new();
    private readonly RadarAppearancePreferenceStore _radarAppearancePreferences = new();
    private readonly TextBox[] _radarTerrainPaths = new TextBox[3];
    private readonly CheckBox _radarAutoTerrain = new();
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
    private PairingInGameWitness? _pairingWitness;
    private readonly PairingInGameTracker _pairingInGameTracker = new();
    private readonly HashSet<int> _sensorVerifiedRoles = [];
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
    private bool _oneClickFollowRequested;
    private int _hybridFocusedPlayer = 1;
    private int _hybridFocusTransition;
    private int _hybridFocusGeneration;
    private readonly HashSet<int> _hybridFocusKeysDown = [];
    private readonly StackPanel _pairingPanel = new() { Spacing = 10 };
    private Border? _pairingCard;
    private bool HybridSelected => _inputMode.SelectedIndex == 1;

    public MainWindow() : this(new FollowTargetPreferenceStore()) { }

    internal MainWindow(FollowTargetPreferenceStore followTargetPreferences,
        FollowStopPreferenceStore? followStopPreferences = null,
        FollowInputPreferenceStore? followInputPreferences = null,
        FlaskSupportPreferenceStore? flaskSupportPreferences = null,
        FlaskRulePreferenceStore? flaskRulePreferences = null,
        FlaskCustomPreferenceStore? flaskCustomPreferences = null)
    {
        _followTargetPreferences = followTargetPreferences;
        _followStopPreferences = followStopPreferences ?? new FollowStopPreferenceStore();
        _followInputPreferences = followInputPreferences ?? new FollowInputPreferenceStore();
        _flaskSupportPreferences = flaskSupportPreferences ?? new FlaskSupportPreferenceStore();
        _flaskRulePreferences = flaskRulePreferences ?? new FlaskRulePreferenceStore();
        _flaskCustomPreferences = flaskCustomPreferences ?? new FlaskCustomPreferenceStore();
        Title = "PartyPilot · 多角色跟随";
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
        content.Children.Add(Label("开始使用", 12, AccentBrush, FontWeight.Bold));
        var heading = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 16 };
        heading.Children.Add(Label("连接客户端，设置跟随与地图", 29, Brushes.White, FontWeight.Bold));
        _hideNames.Content = "隐藏姓名";
        _hideNames.Click += (_, _) => NamePrivacy.Enabled = !NamePrivacy.Enabled;
        heading.Children.Add(_hideNames);
        content.Children.Add(heading);
        content.Children.Add(Label("选人数和游戏目录，然后一键启动。", 13, MutedBrush));
        content.Children.Add(BuildInputStep());
        content.Children.Add(BuildClientStep());
        content.Children.Add(OptionalSection("账号自动填入 · 可选", BuildAccountStep()));
        content.Children.Add(BuildLaunchStep());
        content.Children.Add(BuildPairingStep());
        content.Children.Add(OptionalSection("组队邀请 · 可选", BuildAutoPartyStep()));
        content.Children.Add(BuildFollowStep());
        content.Children.Add(BuildRadarStep());
        content.Children.Add(OptionalSection("隐私遮挡 · 可选", BuildPrivacyStep()));
        content.Children.Add(OptionalSection("帧率设置 · 可选", BuildFpsStep()));
        content.Children.Add(OptionalSection("后台资源设置 · 可选", BuildResourceStep()));
        content.Children.Add(BuildStatusCard());

        shell.Children.Add(new ScrollViewer { Content = content });
        Content = shell;
        var followInput = _followInputPreferences.Load();
        var followOptions = followInput.Options;
        _sprintCatchUp.IsChecked = followOptions.SprintCatchUp;
        _sprintCatchUpPriority.IsChecked = followInput.Success ? followOptions.SprintCatchUpPriority : null;
        _mirrorAttackDirection.IsChecked = followInput.Success ? followOptions.MirrorAttackDirection : null;
        _classicSprintCatchUp.IsChecked = followOptions.ClassicSprintCatchUp;
        _aggressiveBlinkCatchUp.IsChecked = followOptions.AggressiveBlinkCatchUp;
        _aggressiveSprintRetryDelay.Text = followOptions.AggressiveSprintRetryDelayMs.ToString(CultureInfo.InvariantCulture);
        _closeRangeMirror.IsChecked = followOptions.CloseRangeMirror;
        _closeMirrorDuration.Text = followOptions.CloseRangeMirrorDurationMs.ToString(CultureInfo.InvariantCulture);
        _followInputPreferenceReady = followInput.Success;
        _followInputPreferenceStatus.Text = followInput.Message;
        _sprintCatchUp.IsCheckedChanged += (_, _) => SaveFollowInputPreference();
        _sprintCatchUpPriority.IsCheckedChanged += (_, _) => SaveFollowInputPreference();
        _mirrorAttackDirection.IsCheckedChanged += (_, _) => SaveFollowInputPreference();
        _classicSprintCatchUp.IsCheckedChanged += (_, _) => SaveFollowInputPreference();
        _aggressiveBlinkCatchUp.IsCheckedChanged += (_, _) => SaveFollowInputPreference();
        _aggressiveSprintRetryDelay.TextChanged += (_, _) => SaveFollowInputPreference();
        _closeRangeMirror.IsCheckedChanged += (_, _) => SaveFollowInputPreference();
        _closeMirrorDuration.TextChanged += (_, _) => SaveFollowInputPreference();
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
        var appearancePreference = _radarAppearancePreferences.Load();
        _radarMapOpacity.Text = appearancePreference.Settings.MapOpacity.ToString(CultureInfo.InvariantCulture);
        _radarBorderOpacity.Text = appearancePreference.Settings.BorderOpacity.ToString(CultureInfo.InvariantCulture);
        _radarBackgroundOpacity.Text = appearancePreference.Settings.BackgroundOpacity.ToString(CultureInfo.InvariantCulture);
        _radarAppearanceStatus.Text = appearancePreference.Success
            ? "地图外观已加载；修改后保存，可立即更新已启动的覆盖层。"
            : "地图外观设置文件无效；当前使用目标版首次启动值，请重新保存。";
        _playerCount.SelectionChanged += (_, _) => UpdateTopologyOptions();
        _topologyVariant.SelectionChanged += (_, _) => UpdateClientRows();
        _applyLayout.IsCheckedChanged += (_, _) => UpdateLayoutControls();
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
        _hideNames.Content = NamePrivacy.Enabled ? "姓名已隐藏" : "隐藏姓名";
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

    private static void CollapseAdvanced(StackPanel panel, int start, string title)
    {
        var content = new StackPanel { Spacing = 10 };
        while (panel.Children.Count > start)
        {
            var child = panel.Children[start];
            panel.Children.RemoveAt(start);
            content.Children.Add(child);
        }
        panel.Children.Add(new Expander { Header = title, IsExpanded = false, Content = content });
    }

    private static Expander OptionalSection(string title, Control content) =>
        new() { Header = title, IsExpanded = false, Content = content };

    private static Border BuildSidebar()
    {
        var navigation = new StackPanel { Width = 194, Spacing = 18, Margin = new Thickness(20, 26) };
        navigation.Children.Add(Label("PartyPilot", 17, AccentBrush, FontWeight.Bold));
        navigation.Children.Add(Label("独立 PoE2 组队工具", 11, MutedBrush));
        navigation.Children.Add(new Border { Height = 1, Background = EdgeBrush, Margin = new Thickness(0, 14) });
        navigation.Children.Add(Label("游戏窗口与角色", 13, Brushes.White, FontWeight.Bold));
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
        var panel = Step("1", "选择输入", "你用手柄，还是键盘鼠标？");
        _inputMode.ItemsSource = new[] { "实体手柄", "键盘与鼠标" };
        _inputMode.SelectedIndex = 0;
        _inputMode.MinWidth = 260;
        _inputMode.HorizontalAlignment = HorizontalAlignment.Left;
        _inputMode.SelectionChanged += (_, _) =>
        {
            _focusGuide.Text = HybridSelected
                ? "键鼠默认操作：F1 返回 P1，F2–F6 切换其他角色；返回 P1 后，其他角色恢复自动跟随。聚焦键可在上方按键映射中修改。"
                : "手柄操作：按下左摇杆 L3 控制 P1，按下右摇杆 R3 控制 P2；LT+L3/R3 切换 P3/P4，LT+方向键上/下切换 P5/P6。单独控制后松开并再按同一聚焦键，即可返回共同控制，让跟随者继续追近；快速连按也生效。切换到其他角色仍需间隔 350 毫秒，P3–P6 组合键除外。";
            _pairingStatus.Text = HybridSelected
                ? "键鼠模式会在启动游戏前准备虚拟手柄；三客户端各两人布局为独立版实验功能。"
                : "实体手柄将在启动时自动检测；请先连接手柄。";
            UpdateBridgeControls();
        };
        panel.Children.Add(_inputMode);
        var advancedStart = panel.Children.Count;
        _focusGuide.Name = "focus-help";
        _focusGuide.Text = "手柄操作：按下左摇杆 L3 控制 P1，按下右摇杆 R3 控制 P2；LT+L3/R3 切换 P3/P4，LT+方向键上/下切换 P5/P6。单独控制后松开并再按同一聚焦键，即可返回共同控制，让跟随者继续追近；快速连按也生效。切换到其他角色仍需间隔 350 毫秒，P3–P6 组合键除外。";
        _focusGuide.Foreground = MutedBrush;
        _focusGuide.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_focusGuide);
        _hybridBindingsButton.Content = "设置键鼠按键映射";
        _hybridBindingsButton.HorizontalAlignment = HorizontalAlignment.Left;
        _hybridBindingsButton.Click += async (_, _) => await EditHybridBindingsAsync();
        panel.Children.Add(_hybridBindingsButton);
        _hybridBindingsStatus.Foreground = MutedBrush;
        _hybridBindingsStatus.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_hybridBindingsStatus);
        panel.Children.Add(Label("连接手柄后，程序会自动寻找并检查输入隔离；无需填写设备编号。", 12, MutedBrush));
        _rightStickFollowerOverride.Content = "右摇杆暂时控制可活动的跟随角色（主角色不再使用右摇杆瞄准）";
        _rightStickFollowerOverride.IsCheckedChanged += (_, _) => UpdateBridgeControls();
        panel.Children.Add(_rightStickFollowerOverride);
        var dodgePreference = _engageDodgePreferences.Load();
        _engageDodgePreferenceReady = dodgePreference.Success;
        _engageDodge.Content = "右摇杆开始控制跟随角色时闪避一次";
        _engageDodge.IsChecked = dodgePreference.Success ? dodgePreference.Enabled : null;
        _engageDodgeBinding.ItemsSource = RightStickDodgePreferenceStore.Bindings;
        _engageDodgeBinding.SelectedItem = dodgePreference.Binding;
        _engageDodgeBinding.Width = 160;
        _engageDodgeBinding.HorizontalAlignment = HorizontalAlignment.Left;
        _engageDodgeStatus.Text = dodgePreference.Success
            ? "目标版默认关闭，按键为 B；更改后下次准备桥接时生效。"
            : "右摇杆闪避设置无效；请重新选择并保存。";
        _engageDodgeStatus.Foreground = MutedBrush;
        void SaveEngageDodge()
        {
            if (_engageDodge.IsChecked is not bool enabled ||
                _engageDodgeBinding.SelectedItem is not string binding)
            {
                _engageDodgePreferenceReady = false;
                _engageDodgeStatus.Text = "请选择右摇杆闪避状态与按键。";
            }
            else
            {
                _engageDodgePreferenceReady = _engageDodgePreferences.Save(enabled, binding);
                _engageDodgeStatus.Text = _engageDodgePreferenceReady
                    ? "右摇杆闪避设置已保存；下次准备桥接时生效。"
                    : "右摇杆闪避设置保存失败；重新保存前不能准备桥接。";
            }
            UpdateBridgeControls();
        }
        _engageDodge.IsCheckedChanged += (_, _) => SaveEngageDodge();
        _engageDodgeBinding.SelectionChanged += (_, _) => SaveEngageDodge();
        panel.Children.Add(_engageDodge);
        panel.Children.Add(_engageDodgeBinding);
        panel.Children.Add(_engageDodgeStatus);
        _autoFlask.Content = "随实时跟随启用自动药剂（双/三客户端各一名角色；生命/魔力须绑定手柄十字键左/右）";
        panel.Children.Add(_autoFlask);
        _flaskRulesButton.Content = "设置逐角色生命/魔力药剂";
        _flaskRulesButton.HorizontalAlignment = HorizontalAlignment.Left;
        _flaskRulesButton.Click += async (_, _) => await EditFlaskRulesAsync();
        panel.Children.Add(_flaskRulesButton);
        _flaskRulesStatus.Foreground = MutedBrush;
        _flaskRulesStatus.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_flaskRulesStatus);
        _flaskCustomButton.Content = "设置自定义恢复规则";
        _flaskCustomButton.HorizontalAlignment = HorizontalAlignment.Left;
        _flaskCustomButton.Click += async (_, _) => await EditFlaskCustomAsync();
        panel.Children.Add(_flaskCustomButton);
        _flaskCustomStatus.Foreground = MutedBrush;
        _flaskCustomStatus.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_flaskCustomStatus);
        _flaskSupportButton.Content = "设置共享药剂 · Drinker";
        _flaskSupportButton.HorizontalAlignment = HorizontalAlignment.Left;
        _flaskSupportButton.Click += async (_, _) => await EditFlaskSupportAsync();
        panel.Children.Add(_flaskSupportButton);
        _flaskSupportStatus.Foreground = MutedBrush;
        _flaskSupportStatus.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_flaskSupportStatus);
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
        panel.Children.Add(Label("默认由 P1 带路，P2–P6 跟随；开启上方选项后，最近手动聚焦的角色会成为主控。", 12, MutedBrush));
        panel.Children.Add(_leadershipPreferenceStatus);
        CollapseAdvanced(panel, advancedStart, "高级设置 · 按键、右摇杆与镜像");
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
        _pairingPanel.Children.Add(actions);
        _pairingStatus.Text = "选择输入方式、人数与布局后检查桥接支持情况。";
        _pairingStatus.Foreground = MutedBrush;
        _pairingStatus.TextWrapping = TextWrapping.Wrap;
        _pairingPanel.Children.Add(_pairingStatus);
        return Card(panel);
    }

    private Border BuildPairingStep()
    {
        var panel = Step("4", "确认游戏角色", "按游戏画面提示逐个确认。");
        panel.Children.Add(_pairingPanel);
        _pairingCard = Card(panel);
        _pairingCard.IsVisible = false;
        return _pairingCard;
    }

    private Border BuildClientStep()
    {
        var panel = Step("2", "选择人数与客户端", "每个游戏客户端最多两名角色。");
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
            var clientIndex = index;
            var row = new StackPanel { Spacing = 7, Margin = new Thickness(0, 8, 0, 0) };
            var clientLabel = Label($"客户端 {index + 1} · 游戏程序", 13, Brushes.White, FontWeight.SemiBold);
            row.Children.Add(clientLabel);
            var browse = new Button { Content = "选择游戏目录", HorizontalAlignment = HorizontalAlignment.Left };
            browse.Click += async (_, _) => await ChooseGameFolderAsync(clientIndex);
            row.Children.Add(browse);
            var location = Label("尚未选择游戏目录", 12, MutedBrush);
            location.TextWrapping = TextWrapping.Wrap;
            row.Children.Add(location);
            var choice = new ComboBox { IsVisible = false, HorizontalAlignment = HorizontalAlignment.Left,
                MinWidth = 270 };
            choice.SelectionChanged += (_, _) =>
            {
                if (choice.SelectedItem is string executable)
                {
                    _paths[clientIndex].Text = executable;
                    location.Text = $"已选择：{Path.GetFileName(executable)}";
                }
            };
            row.Children.Add(choice);
            var path = new TextBox { Watermark = "游戏程序完整路径（高级选项）" };
            path.TextChanged += (_, _) =>
            {
                ClearLaunchedClients();
                location.Text = string.IsNullOrWhiteSpace(path.Text)
                    ? "尚未选择游戏目录"
                    : $"已选择：{Path.GetFileName(path.Text.Trim())}";
            };
            var advanced = new StackPanel { Spacing = 7 };
            advanced.Children.Add(Label("游戏程序路径（仅在目录选择无法使用时修改）", 12, MutedBrush));
            advanced.Children.Add(path);
            advanced.Children.Add(Label("Steam/Epic 启动 URI（可选；须与上方程序对应）", 12, MutedBrush));
            var uri = new TextBox { Watermark = "steam://rungameid/2694490 或 Epic 清单 URI" };
            uri.TextChanged += (_, _) => ClearLaunchedClients();
            advanced.Children.Add(uri);
            advanced.Children.Add(Label("绑定已运行窗口的进程号（同目录多客户端时必填）", 12, MutedBrush));
            var attachPid = new TextBox { Watermark = "进程号，仅用于绑定已运行窗口" };
            attachPid.TextChanged += (_, _) => ClearLaunchedClients();
            advanced.Children.Add(attachPid);
            row.Children.Add(new Expander { Header = "启动与绑定的高级选项", IsExpanded = false,
                Content = advanced });
            _paths[index] = path;
            _folderButtons[index] = browse;
            _gameChoices[index] = choice;
            _gameLocations[index] = location;
            _uris[index] = uri;
            _attachPids[index] = attachPid;
            _clientLabels[index] = clientLabel;
            _clientRows[index] = row;
            panel.Children.Add(row);
        }
        return Card(panel);
    }

    private async Task ChooseGameFolderAsync(int index)
    {
        if (!StorageProvider.CanPickFolder)
        {
            _gameLocations[index].Text = "当前系统无法打开目录选择器。";
            return;
        }
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择流放之路 2 游戏目录",
                AllowMultiple = false
            });
            if (folders.Count == 0) return;
            using var folder = folders[0];
            var directory = folder.TryGetLocalPath();
            if (directory is null)
            {
                _gameLocations[index].Text = "此目录无法作为本机游戏目录，请重新选择。";
                return;
            }
            var executables = GameInstallDiscovery.FindExecutables(directory);
            _paths[index].Text = string.Empty;
            _gameChoices[index].ItemsSource = executables;
            _gameChoices[index].IsVisible = executables.Count > 1;
            _gameChoices[index].SelectedIndex = executables.Count == 1 ? 0 : -1;
            _gameLocations[index].Text = executables.Count switch
            {
                0 => "所选目录没有找到流放之路 2 游戏程序，请选择安装目录。",
                1 => $"已选择：{Path.GetFileName(executables[0])}",
                _ => "发现多个游戏程序，请选择要启动的版本。"
            };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _gameLocations[index].Text = "无法读取所选目录，请检查权限后重试。";
        }
    }

    private Border BuildAccountStep()
    {
        var panel = Step("2A", "账号资料",
            "按客户端保存独立 PoE 账号；密码只写入当前 Windows 用户保护的本地资料。每端一名或两名角色均可在启动后自动填入一次；须先确认邮箱和密码字段。");
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
        var panel = Step("3", "开始", "启动后按画面提示确认角色。");
        _applyLayout.Content = "对游戏窗口应用布局";
        var advanced = new StackPanel { Spacing = 8 };
        advanced.Children.Add(_applyLayout);
        _layoutMode.ItemsSource = new[] { "stacked", "tiled", "multiple" };
        _layoutMode.SelectedIndex = 0;
        _layoutMode.Width = 190;
        _layoutMode.HorizontalAlignment = HorizontalAlignment.Left;
        advanced.Children.Add(_layoutMode);
        _monitors.Watermark = "显示器编号，例如 1,2";
        advanced.Children.Add(_monitors);

        var actions = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 12, LineSpacing = 8, Margin = new Thickness(0, 10, 0, 0) };
        _check.Content = "检查配置";
        _check.Click += async (_, _) => await RunLauncherAsync(ClientAction.Check);
        _launch.Content = "一键启动";
        _launch.Click += async (_, _) => await RunOneClickStartAsync();
        _attach.Content = "绑定已运行窗口";
        _attach.Click += async (_, _) => await RunLauncherAsync(ClientAction.Attach);
        _cancelAutoLogin.Content = "取消账号准备";
        _cancelAutoLogin.IsEnabled = false;
        _cancelAutoLogin.Click += (_, _) => _autoLoginFillCancellation?.Cancel();
        actions.Children.Add(_launch);
        panel.Children.Add(actions);
        advanced.Children.Add(_attach);
        advanced.Children.Add(_cancelAutoLogin);
        advanced.Children.Add(_check);
        _probeRoster.Content = "检查 Current Party 人数";
        _probeRoster.Click += async (_, _) => await ProbeRosterAsync();
        advanced.Children.Add(_probeRoster);
        _rosterStatus.Text = "绑定窗口后，可在游戏内打开 Current Party 面板再检查；结果仅是画面候选行数。";
        _rosterStatus.Foreground = MutedBrush;
        _rosterStatus.TextWrapping = TextWrapping.Wrap;
        advanced.Children.Add(_rosterStatus);
        panel.Children.Add(new Expander { Header = "窗口布局与队伍检查 · 可选", IsExpanded = false,
            Content = advanced });
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
        var panel = Step("3B", "组队邀请", "按角色填写游戏中的准确名称。已绑定游戏窗口前台时按所选快捷键可运行或取消自动组队。自动恢复向其他客户端发出全新邀请，按目标版菜单顺序接受，并检查游戏内队伍画面候选人数。Windows 游戏内效果待验证。");
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
        var panel = Step("4", "自动跟随与追赶", "角色配对完成、各客户端位置传感器已启用后，点击开始跟随。4–6 人布局仍需在游戏中核对角色身份和方向。");
        panel.Children.Add(Label("配对完成后点击“开始跟随”。默认距离与时效可在高级设置中调整。", 12, MutedBrush));
        var advancedStart = panel.Children.Count;
        _followStop.Name = "follow-stop-global";
        _followStop.Text = "9";
        _followCatchup.Name = "follow-catchup-global";
        _followCatchup.Text = "39.13940256045524";
        _followMaxAge.Text = "600ms";
        _followLogAge.Text = "1m";
        panel.Children.Add(Label("时效采用独立版测试预设 600ms / 1m，仍需游戏内校准。", 12, MutedBrush));
        var fields = new (TextBox Box, string Label)[]
        {
            (_followStop, "停止距离（0.6.17 首次启动配置 9）"),
            (_followCatchup, "追赶距离（0.6.17 首次启动配置约 39.14）"),
            (_followMaxAge, "位置有效期（ms/s/m/h）"), (_followLogAge, "区域首次核对时限（ms/s/m/h）")
        };
        foreach (var (box, label) in fields)
        {
            panel.Children.Add(Label(label, 12, MutedBrush));
            box.Watermark = "按需调整";
            box.Width = 190;
            box.HorizontalAlignment = HorizontalAlignment.Left;
            box.TextChanged += (_, _) => UpdateFollowControls();
            panel.Children.Add(box);
        }
        _sprintCatchUp.Content = "追赶时自动冲刺（目标版默认开启）";
        _sprintCatchUp.IsChecked = true;
        _sprintCatchUp.IsCheckedChanged += (_, _) => UpdateFollowControls();
        panel.Children.Add(_sprintCatchUp);
        _sprintCatchUpPriority.Content = "追赶优先，暂停镜像施法按键（0.6.17 首次启动配置开启）";
        _sprintCatchUpPriority.IsChecked = true;
        panel.Children.Add(_sprintCatchUpPriority);
        _mirrorAttackDirection.Content = "攻击时同步主控移动方向（目标版默认开启；实体手柄）";
        _mirrorAttackDirection.IsChecked = true;
        panel.Children.Add(_mirrorAttackDirection);
        _followInputPreferenceStatus.Foreground = MutedBrush;
        _followInputPreferenceStatus.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_followInputPreferenceStatus);
        _classicSprintCatchUp.Content = "启用经典冲刺追赶（0.6.17 首次启动配置开启）";
        _classicSprintCatchUp.IsCheckedChanged += (_, _) =>
        {
            if (_classicSprintCatchUp.IsChecked == true) _aggressiveBlinkCatchUp.IsChecked = false;
            if (_classicSprintCatchUp.IsChecked != true) _classicCollisionDodge.IsChecked = false;
            UpdateFollowControls();
        };
        panel.Children.Add(_classicSprintCatchUp);
        _aggressiveBlinkCatchUp.Content = "激进闪避转冲刺追赶（目标版默认关闭）";
        _aggressiveBlinkCatchUp.IsCheckedChanged += (_, _) =>
        {
            if (_aggressiveBlinkCatchUp.IsChecked == true) _classicSprintCatchUp.IsChecked = false;
            UpdateFollowControls();
        };
        panel.Children.Add(_aggressiveBlinkCatchUp);
        panel.Children.Add(Label("激进冲刺重试间隔（毫秒，500–1500；目标版默认 800）", 12, MutedBrush));
        _aggressiveSprintRetryDelay.Name = "aggressive-sprint-retry-delay";
        _aggressiveSprintRetryDelay.Text = "800";
        _aggressiveSprintRetryDelay.Width = 190;
        _aggressiveSprintRetryDelay.HorizontalAlignment = HorizontalAlignment.Left;
        _aggressiveSprintRetryDelay.TextChanged += (_, _) => UpdateFollowControls();
        panel.Children.Add(_aggressiveSprintRetryDelay);
        _closeRangeMirror.Content = "近距离镜像跟随（实体手柄；目标版默认关闭）";
        _closeRangeMirror.IsCheckedChanged += (_, _) => UpdateFollowControls();
        panel.Children.Add(_closeRangeMirror);
        panel.Children.Add(Label("镜像时长（毫秒，50–5000；目标版默认 400）", 12, MutedBrush));
        _closeMirrorDuration.Name = "close-mirror-duration";
        _closeMirrorDuration.Text = "400";
        _closeMirrorDuration.Width = 190;
        _closeMirrorDuration.HorizontalAlignment = HorizontalAlignment.Left;
        _closeMirrorDuration.TextChanged += (_, _) => UpdateFollowControls();
        panel.Children.Add(_closeMirrorDuration);
        panel.Children.Add(Label("候选地形绑定（可选；每个客户端都须使用已核对的独立文件）", 12, MutedBrush));
        for (var index = 0; index < _terrainBindingPaths.Length; index++)
        {
            var row = new StackPanel { Spacing = 4, IsVisible = false };
            row.Children.Add(Label($"客户端 {index + 1} 地形绑定 JSON 绝对路径", 12, MutedBrush));
            var binding = new TextBox { Name = $"terrain-binding-client-{index + 1}",
                Watermark = "留空表示不使用地形", MinWidth = 310,
                HorizontalAlignment = HorizontalAlignment.Left };
            binding.TextChanged += (_, _) => UpdateFollowControls();
            _terrainBindingPaths[index] = binding;
            _terrainBindingRows[index] = row;
            row.Children.Add(binding);
            panel.Children.Add(row);
        }
        _classicCollisionDodge.Content = "启用经典冲刺地形碰撞闪避（目标版默认关闭）";
        _classicCollisionDodge.IsCheckedChanged += (_, _) => UpdateFollowControls();
        panel.Children.Add(_classicCollisionDodge);
        _terrainBindingStatus.Foreground = MutedBrush;
        _terrainBindingStatus.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_terrainBindingStatus);
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
        _followStopFreshProfile = loadedStops.FreshProfile;
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
        _experimentalWarpClick.Content = "实验：跨区追赶（P1 带路时可靠近同入口；队伍传送；入口交互待验证）";
        _experimentalWarpClick.IsChecked = false;
        panel.Children.Add(_experimentalWarpClick);
        CollapseAdvanced(panel, advancedStart, "高级设置 · 距离、追赶与时效");
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 12 };
        _startFollow.Content = "开始跟随";
        _startFollow.Click += async (_, _) => await StartFollowAsync();
        _stopFollow.Content = "停止跟随";
        _stopFollow.Click += async (_, _) => await StopFollowAsync();
        actions.Children.Add(_startFollow);
        _pauseFollow.Content = "暂停跟随";
        _pauseFollow.Click += (_, _) => ToggleFollowPause();
        actions.Children.Add(_pauseFollow);
        _manualWarpToMain.Content = "实验：手动追到 MAIN / 再按取消";
        _manualWarpToMain.Click += (_, _) => ToggleManualWarpToMain();
        actions.Children.Add(_manualWarpToMain);
        actions.Children.Add(_stopFollow);
        panel.Children.Add(actions);
        var manualWarpGuide = Label("实体手柄：LT + 十字键右，手动追到 MAIN；再按一次取消。仅在实时跟随运行且关闭自动跨区实验选项时生效。", 12, MutedBrush);
        manualWarpGuide.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(manualWarpGuide);
        _followStatus.Text = "等待配对完成；真实游戏中的方向与角色绑定尚未验证。";
        _followStatus.Foreground = MutedBrush;
        _followStatus.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_followStatus);
        return Card(panel);
    }

    private Border BuildRadarStep()
    {
        var panel = Step("5", "Tab 地图覆盖层", "按 Tab 在已绑定游戏窗口上显示可见标记和可选候选地形；请在对应 ExileCore2 实例中启用雷达诊断，绘制地形还需开启地形诊断。无迷雾效果、坐标方向与比例仍需游戏内验证。");
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
        panel.Children.Add(Label("每个地图格对应的屏幕像素（0.6.17 默认 0.66；可调整）", 12, MutedBrush));
        _radarScale.Text = "0.66";
        _radarScale.Watermark = "像素/地图格";
        _radarScale.Width = 190;
        _radarScale.HorizontalAlignment = HorizontalAlignment.Left;
        _radarScale.TextChanged += (_, _) => UpdateRadarControls();
        panel.Children.Add(_radarScale);
        panel.Children.Add(Label("候选地形透明度（目标版首次启动：地图 58%、边线 26%、背景 6%；暂不改变标记）", 12, MutedBrush));
        var appearanceRow = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 12 };
        foreach (var (field, label, initial) in new[]
                 {
                     (_radarMapOpacity, "地图 %", "58"),
                     (_radarBorderOpacity, "边线 %", "26"),
                     (_radarBackgroundOpacity, "背景 %", "6")
                 })
        {
            var group = new StackPanel { Spacing = 4 };
            group.Children.Add(Label(label, 12, MutedBrush));
            field.Text = initial;
            field.Width = 100;
            field.TextChanged += (_, _) =>
            {
                _radarAppearanceStatus.Text = "地图外观已修改，保存后应用到已启动的覆盖层。";
                UpdateRadarControls();
            };
            group.Children.Add(field);
            appearanceRow.Children.Add(group);
        }
        var saveAppearance = new Button { Content = "保存地图外观" };
        saveAppearance.Click += (_, _) => SaveRadarAppearance();
        appearanceRow.Children.Add(saveAppearance);
        panel.Children.Add(appearanceRow);
        _radarAppearanceStatus.Foreground = MutedBrush;
        _radarAppearanceStatus.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_radarAppearanceStatus);
        _radarAutoTerrain.Content = "自动查找当前进程与区域的候选地形（实验性）";
        _radarAutoTerrain.IsChecked = true;
        panel.Children.Add(_radarAutoTerrain);
        panel.Children.Add(Label("候选地形采集文件（可选；需在游戏中核对区域与地图方向）", 12, MutedBrush));
        for (var index = 0; index < _radarTerrainPaths.Length; index++)
        {
            panel.Children.Add(Label($"客户端 {index + 1} 的采集 JSON 绝对路径", 12, MutedBrush));
            var path = new TextBox { Watermark = "留空则只显示标记", MinWidth = 310,
                HorizontalAlignment = HorizontalAlignment.Left };
            _radarTerrainPaths[index] = path;
            panel.Children.Add(path);
        }
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 12 };
        _startRadar.Content = "启动地图覆盖层";
        _startRadar.Click += (_, _) => StartRadar();
        _stopRadar.Content = "关闭地图覆盖层";
        _stopRadar.Click += async (_, _) => await StopRadarAsync();
        actions.Children.Add(_startRadar);
        actions.Children.Add(_stopRadar);
        panel.Children.Add(actions);
        _radarStatus.Text = "等待已绑定的游戏窗口；地图比例仍需游戏内核对。";
        _radarStatus.Foreground = MutedBrush;
        _radarStatus.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_radarStatus);
        return Card(panel);
    }

    private Border BuildPrivacyStep()
    {
        var panel = Step("6", "游戏画面遮挡",
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
        var panel = Step("6", "帧率设置", "按客户端分别预览前台与后台帧率配置。仅修改明确指定的 PoE2 配置文件；应用前需关闭所有游戏窗口。");
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
        var panel = Step("7", "资源设置",
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
        if (topologyChanged)
            _flaskRulesStatus.Text = selection?.Id is "2-split" or "3-three-singles"
                ? _flaskRulePreferences.Load(selection.PlayerCount).Message
                : "当前布局尚不能发送实时逐角色药剂请求。";
        if (topologyChanged)
            _flaskCustomStatus.Text = selection?.Id is "2-split" or "3-three-singles"
                ? _flaskCustomPreferences.Load(selection.PlayerCount).Message
                : "当前布局尚不能发送实时自定义恢复请求。";
        if (topologyChanged)
            _flaskSupportStatus.Text = selection?.Id is "2-split" or "3-three-singles"
                ? _flaskSupportPreferences.Load(selection.PlayerCount).Message
                : "当前布局尚不能发送实时共享药剂请求。";
        if (topologyChanged && HybridSelected && _bridge is null && selection is not null)
            _pairingStatus.Text = selection.PlayerCount == 1
                ? "单人模式由 P1 原生键鼠操作；无需准备虚拟手柄。"
                : HybridTopologySupport.CanBridge(selection)
                    ? "启动游戏前先建立键鼠虚拟手柄；三客户端各两人布局仍需游戏内验证。"
                    : "此布局的键鼠桥接尚未接入。";
        if (_bridge is null && topologyChanged) _autoFlask.IsChecked = selection?.Id is "2-split" or "3-three-singles";
        var count = _selectedTopology?.ClientCount ?? 0;
        if (_pairingCard is not null) _pairingCard.IsVisible = _launchedClients.Count > 0;
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
        var supported = _selectedTopology?.Id is "2-split" or "3-three-singles" or
            "4-balanced" or "5-third-solo" or "5-main-solo" or "6-balanced";
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
        for (var index = 0; index < _terrainBindingRows.Length; index++)
            if (_terrainBindingRows[index] is not null)
                _terrainBindingRows[index].IsVisible = supported &&
                    index < _selectedTopology!.ClientCount;
        UpdateFollowControls();
    }

    private void UpdateFollowStopFields()
    {
        _updatingFollowStopFields = true;
        try
        {
            var shippingStops = _followStopFreshProfile &&
                _selectedTopology?.Id is ("4-balanced" or "5-third-solo" or
                    "5-main-solo" or "6-balanced")
                    ? FollowStopPreferenceStore.CreateShippingDefaults() : null;
            for (var index = 0; index < _followPlayerStops.Length; index++)
            {
                var slot = _selectedTopology?.Slots.FirstOrDefault(item => item.PartyPlayer == index + 1);
                _followPlayerStops[index].Text = slot is null ? string.Empty :
                    (shippingStops?[slot.RuntimePad - 1] ?? _configuredFollowStops[slot.RuntimePad - 1])
                        ?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty;
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
        if (slot is null || _selectedTopology.Id is not ("2-split" or "3-three-singles" or
            "4-balanced" or "5-third-solo" or "5-main-solo" or "6-balanced")) return;
        if (!FollowPlayerStop.TryRead(_followPlayerStops[player - 1].Text, out var value))
        {
            _followStopPreferenceStatus.Text = "停止距离必须为 0–200；当前输入未保存。";
            UpdateFollowControls();
            return;
        }
        var current = _followStopFreshProfile && _selectedTopology.Id is ("4-balanced" or
            "5-third-solo" or "5-main-solo" or "6-balanced")
            ? FollowStopPreferenceStore.CreateShippingDefaults()[slot.RuntimePad - 1]
            : _configuredFollowStops[slot.RuntimePad - 1];
        if (_followStopPreferenceReady && value == current) return;
        var next = _followStopFreshProfile && _selectedTopology.Id is ("4-balanced" or
            "5-third-solo" or "5-main-solo" or "6-balanced")
            ? FollowStopPreferenceStore.CreateShippingDefaults()
            : (double?[])_configuredFollowStops.Clone();
        next[slot.RuntimePad - 1] = value;
        _configuredFollowStops = next;
        var saved = _followStopPreferences.Save(next);
        _followStopPreferenceReady = saved.Success;
        if (saved.Success) _followStopFreshProfile = false;
        _followStopPreferenceStatus.Text = saved.Message;
        UpdateFollowControls();
    }

    private void SaveFollowTargetChoice(int player)
    {
        if (_updatingFollowTargetChoices || _selectedTopology is null ||
            _selectedTopology.Id is not ("2-split" or "3-three-singles" or
                "4-balanced" or "5-third-solo" or "5-main-solo" or "6-balanced")) return;
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
                    _accountPresent[index];
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
            RadarProjection.TryScale(_radarScale.Text, out _) &&
            RadarAppearanceSettings.TryParse(_radarMapOpacity.Text,
                _radarBorderOpacity.Text, _radarBackgroundOpacity.Text, out _);
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
            !RadarProjection.TryScale(_radarScale.Text, out var gridScale) ||
            !RadarAppearanceSettings.TryParse(_radarMapOpacity.Text,
                _radarBorderOpacity.Text, _radarBackgroundOpacity.Text, out var appearance)) return;
        if (RadarKeyConflicts(_radarHotkey))
        {
            _radarHotkeyStatus.Text = "雷达快捷键与自动组队或暂停跟随冲突；请重新设置。";
            return;
        }
        try
        {
            var captures = new Dictionary<int, RadarTerrainCapture>();
            for (var index = 0; index < _launchedClients.Count; index++)
            {
                var path = _radarTerrainPaths[index].Text?.Trim();
                if (string.IsNullOrEmpty(path)) continue;
                if (!RadarTerrainCapture.TryLoad(path, out var capture) || capture is null ||
                    capture.ProcessId != _launchedClients[index].Pid)
                    throw new InvalidDataException($"客户端 {index + 1} 的候选地形文件或进程身份无效。");
                captures.Add(capture.ProcessId, capture);
            }
            var catalog = _radarAutoTerrain.IsChecked == true
                ? new RadarTerrainCatalog(RadarTerrainCatalog.SensorDirectory) : null;
            var worldScale = gridScale / RadarProjection.WorldUnitsPerGridUnit;
            var session = RadarSession.Start(_launchedClients, worldScale, _radarHotkey,
                captures, catalog, appearance);
            session.Ended += RadarEnded;
            session.FrameReceived += RadarFrameReceived;
            session.FrameObserved += RadarFrameObserved;
            _radar = session;
            _radarStatus.Text = captures.Count == 0 && catalog is null
                ? $"地图覆盖层已启动，按 {_radarHotkey.DisplayText} 显示；等待各游戏进程的只读标记。失焦或样本过期会隐藏标记。"
                : $"地图覆盖层已启动，按 {_radarHotkey.DisplayText} 显示；候选地形需匹配进程、区域与角色格坐标，游戏内方向待核对。";
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

    private void RadarFrameReceived(int pid, int count, bool terrainVisible)
    {
        _radarStatus.Text = $"已收到游戏进程 {pid} 的 {count} 个实体标记；" +
            (terrainVisible ? "候选地形已绘制，游戏内地图方向待核对。" :
                "候选地形尚未匹配；仅当前游戏窗口在前台时显示标记。");
    }

    private void RadarFrameObserved(RadarFrame frame) => _follow?.ObserveRadarFrame(frame);

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
            session.FrameObserved -= RadarFrameObserved;
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
                supported && _leadershipPreferenceReady && _lastFocusedLeads.IsChecked is bool);
        _stopBridge.IsEnabled = _bridge is not null;
        _endSession.IsEnabled = OperatingSystem.IsWindows() && !_busy &&
            (_bridge is not null || _launchedClients.Count > 0);
        _followerMirrorButton.IsEnabled = _bridge is null && !_busy;
        _armPairing.IsEnabled = !HybridSelected && _bridge is not null && _pairing?.Current is not null &&
            _launchedClients.Count == _selectedTopology?.ClientCount &&
            _pairing.Armed == false && _pairing.AReleased == false && !_armRequested && !_busy &&
            (_pairing.Current.LocalPlayer != 1 ||
             !_accountPresent[_pairing.Current.Client - 1] ||
             _autoLoginPreferenceReady[_pairing.Current.Client - 1]);
        _armPairing.Content = _pairing?.Current is { } next &&
            next.LocalPlayer == 1 &&
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
        _hybridBindingsButton.IsEnabled = _bridge is null && !_busy;
        _rightStickFollowerOverride.IsEnabled = !HybridSelected && _bridge is null && !_busy;
        _engageDodge.IsEnabled = !HybridSelected && _bridge is null && !_busy &&
            _rightStickFollowerOverride.IsChecked == true;
        _engageDodgeBinding.IsEnabled = _engageDodge.IsEnabled;
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
            if (_folderButtons[index] is not null) _folderButtons[index].IsEnabled = _bridge is null && !_pairingHasStarted && !_busy;
            if (_gameChoices[index] is not null) _gameChoices[index].IsEnabled = _bridge is null && !_pairingHasStarted && !_busy;
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

    private void SaveRadarAppearance()
    {
        if (!RadarAppearanceSettings.TryParse(_radarMapOpacity.Text,
                _radarBorderOpacity.Text, _radarBackgroundOpacity.Text, out var appearance))
        {
            _radarAppearanceStatus.Text = "请输入整数：地图 2–100%，边线与背景 0–100%。";
            return;
        }
        if (!_radarAppearancePreferences.Save(appearance))
        {
            _radarAppearanceStatus.Text = "地图外观保存失败，当前覆盖层未更改。";
            return;
        }
        _radar?.SetAppearance(appearance);
        _radarAppearanceStatus.Text = _radar is null
            ? "地图外观已保存；下次启动覆盖层时生效。"
            : "地图外观已保存并应用到当前覆盖层；游戏内效果仍需核对。";
        UpdateRadarControls();
    }

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

    private void SaveFollowInputPreference()
    {
        if (_sprintCatchUpPriority.IsChecked is not bool priority ||
            _mirrorAttackDirection.IsChecked is not bool mirror)
        {
            _followInputPreferenceReady = false;
            _followInputPreferenceStatus.Text = "请选择追赶优先和攻击方向同步两个选项以保存。";
        }
        else
        {
            if (!int.TryParse(_aggressiveSprintRetryDelay.Text, NumberStyles.None,
                    CultureInfo.InvariantCulture, out var retry) ||
                !int.TryParse(_closeMirrorDuration.Text, NumberStyles.None,
                    CultureInfo.InvariantCulture, out var duration))
            {
                _followInputPreferenceReady = false;
                _followInputPreferenceStatus.Text = "重试间隔和镜像时长须填写有效毫秒数。";
            }
            else
            {
                var options = new FollowInputOptions(_sprintCatchUp.IsChecked == true,
                    priority, mirror, _classicSprintCatchUp.IsChecked == true,
                    _aggressiveBlinkCatchUp.IsChecked == true, retry,
                    _closeRangeMirror.IsChecked == true, duration);
                var saved = _followInputPreferences.Save(options);
                _followInputPreferenceReady = saved.Success;
                _followInputPreferenceStatus.Text = saved.Message;
            }
        }
        UpdateFollowControls();
    }

    private void UpdateFollowControls()
    {
        _flaskRulesButton.IsEnabled = _follow is null && !_busy &&
            _selectedTopology?.Id is ("2-split" or "3-three-singles");
        _flaskCustomButton.IsEnabled = _follow is null && !_busy &&
            _selectedTopology?.Id is ("2-split" or "3-three-singles");
        _flaskSupportButton.IsEnabled = _follow is null && !_busy &&
            _selectedTopology?.Id is ("2-split" or "3-three-singles");
        foreach (var choice in _followTargets)
            if (choice is not null) choice.IsEnabled = _follow is null && !_busy;
        foreach (var stop in _followPlayerStops)
            if (stop is not null) stop.IsEnabled = _follow is null && !_busy;
        foreach (var binding in _terrainBindingPaths)
            if (binding is not null) binding.IsEnabled = _follow is null && !_busy;
        _classicSprintCatchUp.IsEnabled = _follow is null && !_busy &&
            _selectedTopology?.Id is ("2-split" or "3-three-singles" or
                "4-balanced" or "5-third-solo" or "5-main-solo" or "6-balanced");
        _sprintCatchUp.IsEnabled = _classicSprintCatchUp.IsEnabled;
        _sprintCatchUpPriority.IsEnabled = _sprintCatchUp.IsEnabled &&
            _sprintCatchUp.IsChecked == true;
        _mirrorAttackDirection.IsEnabled = _classicSprintCatchUp.IsEnabled &&
            _bridge is { IsHybridMode: false };
        _aggressiveBlinkCatchUp.IsEnabled = _classicSprintCatchUp.IsEnabled;
        _aggressiveSprintRetryDelay.IsEnabled = _aggressiveBlinkCatchUp.IsEnabled &&
            _aggressiveBlinkCatchUp.IsChecked == true;
        _closeRangeMirror.IsEnabled = _follow is null && !_busy &&
            _selectedTopology?.Id is ("2-split" or "3-three-singles") &&
            _bridge is { IsHybridMode: false };
        _closeMirrorDuration.IsEnabled = _closeRangeMirror.IsEnabled && _closeRangeMirror.IsChecked == true;
        _classicCollisionDodge.IsEnabled = _follow is null && !_busy &&
            _selectedTopology?.Id is ("2-split" or "3-three-singles") &&
            _classicSprintCatchUp.IsChecked == true;
        _experimentalWarpClick.IsEnabled = _follow is null && !_busy &&
            _selectedTopology?.Id is ("4-balanced" or "5-third-solo" or "5-main-solo" or "6-balanced") &&
            OperatingSystem.IsWindows();
        var valid = LiveFollowPlan.TryCreate(_followStop.Text, _followCatchup.Text,
            _followMaxAge.Text, _followLogAge.Text, _followLeaderName.Text, out _);
        if (_aggressiveBlinkCatchUp.IsChecked == true)
            valid &= int.TryParse(_aggressiveSprintRetryDelay.Text, NumberStyles.None,
                CultureInfo.InvariantCulture, out var retryDelay) && retryDelay is >= 500 and <= 1500;
        if (_closeRangeMirror.IsEnabled && _closeRangeMirror.IsChecked == true)
            valid &= int.TryParse(_closeMirrorDuration.Text, NumberStyles.None,
                CultureInfo.InvariantCulture, out var duration) && duration is >= 50 and <= 5000;
        if (_selectedTopology?.Id is "2-split" or "3-three-singles" or "4-balanced" or
            "5-third-solo" or "5-main-solo" or "6-balanced")
        {
            valid &= _selectedTopology.Slots.All(slot =>
                FollowPlayerStop.TryRead(_followPlayerStops[slot.PartyPlayer - 1].Text, out _));
            var terrainValid = LiveTerrainSelection.TryCreate(_selectedTopology,
                _terrainBindingPaths.Select(box => box?.Text).ToArray(),
                _classicCollisionDodge.IsEnabled && _classicCollisionDodge.IsChecked == true &&
                _sprintCatchUp.IsChecked == true,
                out var terrain, out var terrainError);
            valid &= terrainValid;
            _terrainBindingStatus.Text = terrainValid
                ? terrain.Count == 0 ? "未使用候选地形；碰撞闪避保持关闭。" :
                    "已选择逐客户端候选地形；程序仍会核对文件和实时实例，游戏内映射未验证。"
                : terrainError;
        }
        else _terrainBindingStatus.Text = "当前布局不支持实时地形绑定。";
        _startFollow.IsEnabled = OperatingSystem.IsWindows() && !_busy && !_followStopping && _follow is null &&
            _followTargetPreferenceReady && _followStopPreferenceReady && _followInputPreferenceReady &&
            _bridge is not null && _pairingSettled &&
            (!_bridge.IsHybridMode || _hybridPairing?.Phase == HybridPairingPhase.Complete &&
             Volatile.Read(ref _hybridFocusedPlayer) == 1 && Volatile.Read(ref _hybridFocusTransition) == 0) &&
            (_selectedTopology?.Id is "2-split" or "3-three-singles" or "4-balanced" or
                "5-third-solo" or "5-main-solo" or "6-balanced") &&
            _launchedClients.Count == _selectedTopology.ClientCount && valid &&
            (_bridge.AutoFlaskEnabled != true ||
             _flaskSupportPreferences.Load(_selectedTopology.PlayerCount).Success &&
             _flaskRulePreferences.Load(_selectedTopology.PlayerCount).Success &&
             _flaskCustomPreferences.Load(_selectedTopology.PlayerCount).Success);
        _stopFollow.IsEnabled = _follow is not null;
        _pauseFollow.IsEnabled = _follow is not null && !_followStopping;
        _pauseFollow.Content = _follow?.Paused == true ? "恢复跟随" : "暂停跟随";
        _manualWarpToMain.IsEnabled = _follow is { Paused: false } && !_followStopping &&
            _experimentalWarpClick.IsChecked != true &&
            _selectedTopology?.Id is ("4-balanced" or "5-third-solo" or
                "5-main-solo" or "6-balanced");
    }

    private async Task PrepareBridgeAsync(Client3PreResumeSession? pendingPrivate = null,
        ControllerAutoSelection? prelaunchController = null)
    {
        if ((_busy && pendingPrivate is null) || _bridge is not null ||
            _selectedTopology is null || !OperatingSystem.IsWindows())
        {
            _pairingStatus.Text = "手柄桥接需要 Windows、已选布局与空闲桥接会话。";
            return;
        }
        if (!_menuGuardPreferenceReady || _menuGuard.IsChecked is not bool ||
            !_followerMirrorPreferenceReady || !HybridSelected && !_engageDodgePreferenceReady)
        {
            _pairingStatus.Text = HybridSelected
                ? "先确认并保存菜单保护及跟随按键设置，再准备桥接。"
                : "先确认并保存菜单保护、跟随按键及右摇杆闪避设置，再准备桥接。";
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
            (slots.Length == 4 && !useWgi))
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
            var selected = prelaunchController ?? await ControllerLaunchPreflight.DiscoverAsync(
                executable, gameExecutables, useWgi);
            if (!selected.Ready || selected.Device is null)
                throw new InvalidOperationException(selected.Message);
            var session = await BridgeSession.StartAsync(executable, _selectedTopology.Slots,
                selected.PhysicalIndex ?? 0, selected.Device, gameExecutables,
                _rightStickFollowerOverride.IsChecked == true,
                _selectedTopology.Id is "2-split" or "3-three-singles" && _autoFlask.IsChecked == true,
                useWgi, privatePlan, lastFocusedLeads: _lastFocusedLeads.IsChecked == true,
                menuGuardEnabled: _menuGuard.IsChecked == true, followerMirror: _followerMirror,
                adoptExistingPrivate: pendingPrivate is not null,
                engageDodgeEnabled: _rightStickFollowerOverride.IsChecked == true &&
                    _engageDodge.IsChecked == true,
                engageDodgeBinding: (string)_engageDodgeBinding.SelectedItem!);
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
                $"在游戏窗口按 {HybridKeyLabel(guide.CurrentPlayer - 1)}，切换到 P{guide.CurrentPlayer}。",
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
        _pairingInGameTracker.Reset();
        _sensorVerifiedRoles.Clear();
        if (topology.Slots.All(slot => topology.Clients[slot.Client - 1].Count == 1))
            _pairingWitness = PairingInGameWitness.TryStart((sample, receivedUtc) =>
                Dispatcher.UIThread.Post(() => ObservePairingInGame(sample, receivedUtc)));
        session.StartReading();
        ShowCurrentPairingStep();
    }

    private void BridgeAReleased(int slot)
    {
        if (_pairing?.ObserveARelease(slot) != true) return;
        _pairingInGameTracker.Reset();
        ShowCurrentPairingStep();
        UpdateBridgeControls();
    }

    private async void ObservePairingInGame(PairingInGameSample sample, DateTime receivedUtc)
    {
        var guide = _pairing;
        var current = guide?.Current;
        var topology = _selectedTopology;
        if (_bridge is null || DateTime.UtcNow - receivedUtc > TimeSpan.FromMilliseconds(500) ||
            guide?.AReleased != true || current is null ||
            topology is null || current.LocalPlayer != 1 ||
            current.Client > topology.ClientCount ||
            topology.Clients[current.Client - 1].Count != 1 ||
            current.Client > _launchedClients.Count) return;
        var client = _launchedClients[current.Client - 1];
        if (!PoeChatInviteService.IsWindowOwned(client) ||
            !_pairingInGameTracker.Observe(sample, client.Pid, guide.AReleasedAtUtc, receivedUtc))
            return;
        _sensorVerifiedRoles.Add(current.PartyPlayer);
        await ConfirmRoleAsync();
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
            if (current.LocalPlayer == 1 &&
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
        _ = ContinueOneClickFollowAsync();
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
                var witness = _pairingWitness;
                _pairingWitness = null;
                if (witness is not null) await witness.DisposeAsync();
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
        if (_pairingSettled) await ContinueOneClickFollowAsync();
    }

    private void ShowCurrentPairingStep()
    {
        var current = _pairing?.Current;
        var verified = _sensorVerifiedRoles.Count == 0 ? "" :
            $"其中 {_sensorVerifiedRoles.Count} 名独立客户端角色有新鲜的本地传感器生命值与位置证据；";
        _pairingStatus.Text = current is null
            ? _pairingSettled
                ? $"已观察到 400 毫秒手柄回中；{verified}软件尚未验证游戏内实际配对。手柄桥接仍在运行。"
                : $"所有角色已完成入场检查；{verified}请松开所有手柄输入，等待 400 毫秒回中。"
            : _pairing!.AReleased
                ? _pairingWitness is not null && _selectedTopology is { } selected &&
                    current.Client <= selected.ClientCount && selected.Clients[current.Client - 1].Count == 1
                    ? $"已观察到 P{current.PartyPlayer} 的 A 键松开。正在等待该客户端的新鲜生命值与位置证据；也可在游戏中确认角色已加入后手动继续。"
                    : $"已观察到 P{current.PartyPlayer} 对应虚拟手柄 A 键松开。请在游戏中选角并确认角色已加入，再继续。"
                : _pairing.Armed
                    ? current.LocalPlayer == 2
                        ? $"客户端 {current.Client} 的第二名角色：等待上一角色 A 键松开至少 2 秒；保持窗口前台、松开输入，提示出现后重新按一次 A 键。"
                        : _loginStatus switch
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
        _oneClickFollowRequested = false;
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
        var pairingWitness = _pairingWitness;
        _pairingWitness = null;
        if (pairingWitness is not null) await pairingWitness.DisposeAsync();
        ApplyGameShortcutKeys();
        _pairing = null;
        _pairingInGameTracker.Reset();
        _sensorVerifiedRoles.Clear();
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

    private async Task EditFlaskRulesAsync()
    {
        if (_selectedTopology?.Id is not ("2-split" or "3-three-singles") ||
            _follow is not null || _busy) return;
        var current = _flaskRulePreferences.Load(_selectedTopology.PlayerCount);
        var dialog = new FlaskRuleDialog(_selectedTopology.PlayerCount, current.Players);
        var edited = await dialog.ShowDialog<FlaskPlayerRule[]?>(this);
        if (edited is null) return;
        var saved = _flaskRulePreferences.Save(_selectedTopology.PlayerCount, edited);
        _flaskRulesStatus.Text = saved.Message;
        UpdateFollowControls();
    }

    private async Task EditFlaskSupportAsync()
    {
        if (_selectedTopology?.Id is not ("2-split" or "3-three-singles") ||
            _follow is not null || _busy) return;
        var current = _flaskSupportPreferences.Load(_selectedTopology.PlayerCount);
        var dialog = new FlaskSupportDialog(_selectedTopology.PlayerCount, current.Bindings);
        var edited = await dialog.ShowDialog<FlaskSupportBinding[]?>(this);
        if (edited is null) return;
        var saved = _flaskSupportPreferences.Save(_selectedTopology.PlayerCount, edited);
        _flaskSupportStatus.Text = saved.Message;
        UpdateFollowControls();
    }

    private async Task EditFlaskCustomAsync()
    {
        if (_selectedTopology?.Id is not ("2-split" or "3-three-singles") ||
            _follow is not null || _busy) return;
        var current = _flaskCustomPreferences.Load(_selectedTopology.PlayerCount);
        var dialog = new FlaskCustomDialog(_selectedTopology.PlayerCount, current.Rules);
        var edited = await dialog.ShowDialog<FlaskCustomRule[]?>(this);
        if (edited is null) return;
        var saved = _flaskCustomPreferences.Save(_selectedTopology.PlayerCount, edited);
        _flaskCustomStatus.Text = saved.Message;
        UpdateFollowControls();
    }

    private async Task StartFollowAsync()
    {
        if (_bridge is null || !_pairingSettled || _selectedTopology is null || _follow is not null || _followStopping ||
            _bridge.IsHybridMode && (_hybridPairing?.Phase != HybridPairingPhase.Complete ||
                Volatile.Read(ref _hybridFocusedPlayer) != 1 || Volatile.Read(ref _hybridFocusTransition) != 0) ||
            !_followTargetPreferenceReady || !_followStopPreferenceReady || !_followInputPreferenceReady ||
            !LiveFollowPlan.TryCreate(_followStop.Text, _followCatchup.Text,
                _followMaxAge.Text, _followLogAge.Text, _followLeaderName.Text, out var plan) || plan is null) return;
        var executable = LiveFollowSession.FindExecutable();
        if (executable is null)
        {
            _followStatus.Text = "找不到 PartyPilot-LiveFollow.exe；请使用完整发布目录。";
            return;
        }
        var enableCloseMirror = _selectedTopology.Id is ("2-split" or "3-three-singles") &&
            !_bridge.IsHybridMode &&
            _closeRangeMirror.IsChecked == true;
        var enableAttackMirror = _selectedTopology.Id is ("2-split" or "3-three-singles") &&
            !_bridge.IsHybridMode && _mirrorAttackDirection.IsChecked == true;
        var enableExperimentalWarp = _selectedTopology.Id is
            ("4-balanced" or "5-third-solo" or "5-main-solo" or "6-balanced") &&
            OperatingSystem.IsWindows() && _experimentalWarpClick.IsChecked == true;
        if (enableCloseMirror && (!int.TryParse(_closeMirrorDuration.Text, NumberStyles.None,
                CultureInfo.InvariantCulture, out var mirrorDuration) || mirrorDuration is < 50 or > 5000))
        {
            _followStatus.Text = "镜像时长必须为 50–5000 毫秒。";
            return;
        }
        var radarStartedForFollow = false;
        if (enableExperimentalWarp && _radar is null)
        {
            StartRadar();
            if (_radar is null)
            {
                _followStatus.Text = $"实验性跨区追赶需要雷达采样：{_radarStatus.Text}";
                return;
            }
            radarStartedForFollow = true;
        }
        _busy = true;
        UpdateFollowControls();
        _followStatus.Text = "正在核对进程、日志与传感器输入…";
        try
        {
            var flaskSupport = _flaskSupportPreferences.Load(_selectedTopology.PlayerCount);
            if (_bridge.AutoFlaskEnabled && !flaskSupport.Success)
                throw new InvalidOperationException(flaskSupport.Message);
            var flaskRules = _flaskRulePreferences.Load(_selectedTopology.PlayerCount);
            if (_bridge.AutoFlaskEnabled && !flaskRules.Success)
                throw new InvalidOperationException(flaskRules.Message);
            var flaskCustom = _bridge.AutoFlaskEnabled
                ? _flaskCustomPreferences.EnsureRuntimeFile(_selectedTopology.PlayerCount)
                : null;
            if (flaskCustom is { Success: false })
                throw new InvalidOperationException(flaskCustom.Message);
            if (!LiveTerrainSelection.TryCreate(_selectedTopology,
                    _terrainBindingPaths.Select(box => box?.Text).ToArray(),
                    _selectedTopology.Id is ("2-split" or "3-three-singles") &&
                    _classicCollisionDodge.IsChecked == true && _sprintCatchUp.IsChecked == true,
                    out var terrain, out var terrainError))
                throw new InvalidOperationException(terrainError);
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
            var memberNames = LiveMemberNameSelection.TryCreate(_selectedTopology,
                _inviteNames.Take(_selectedTopology.PlayerCount).Select(box => box.Text).ToArray(),
                plan.LeaderName);
            var session = await LiveFollowSession.StartAsync(executable, _selectedTopology,
                _launchedClients, _bridge, plan, memberNames, followTargets, playerStops,
                terrain, _selectedTopology.Id is ("2-split" or "3-three-singles") &&
                    _classicCollisionDodge.IsChecked == true,
                _classicSprintCatchUp.IsChecked == true,
                sprintCatchUp: _sprintCatchUp.IsChecked == true,
                sprintCatchUpPriority: _sprintCatchUpPriority.IsChecked == true,
                mirrorAttackDirection: enableAttackMirror,
                aggressiveBlinkCatchUp: _aggressiveBlinkCatchUp.IsChecked == true,
                aggressiveSprintRetryDelay: _aggressiveBlinkCatchUp.IsChecked == true
                    ? int.Parse(_aggressiveSprintRetryDelay.Text!, CultureInfo.InvariantCulture) : 800,
                relayMainInput: !_bridge.IsHybridMode,
                closeRangeMirror: enableCloseMirror,
                closeMirrorDuration: enableCloseMirror
                    ? int.Parse(_closeMirrorDuration.Text!, CultureInfo.InvariantCulture) : 400,
                followerMirror: _followerMirror,
                flaskSupportFile: _bridge.AutoFlaskEnabled ? flaskSupport.RuntimeFile : null,
                flaskRulesFile: _bridge.AutoFlaskEnabled ? flaskRules.RuntimeFile : null,
                flaskCustomFile: flaskCustom?.RuntimeFile,
                experimentalWarpClick: enableExperimentalWarp);
            session.StatusChanged += FollowStatusChanged;
            session.WarpStatusChanged += FollowWarpStatusChanged;
            session.Ended += FollowEnded;
            _follow = session;
            session.StartReading();
            FollowStatusChanged(session.Current);
        }
        catch (Exception error)
        {
            if (radarStartedForFollow && _follow is null)
                await StopRadarAsync();
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
        if (frame.Handoffs.Count > 0)
        {
            var requests = string.Join(" · ", frame.Handoffs.Select(item =>
                $"客户端 {item.DestinationClient} 的 P{item.DriverPlayer} 需传送至 P{item.TargetPlayer}"));
            var warpState = _follow?.ManualWarpActive == true
                ? "正在执行手动追到 MAIN"
                : _experimentalWarpClick.IsChecked != true
                ? "自动传送未开启"
                : _selectedTopology is not null &&
                  (PartyWarpRoutes.FromFrame(frame, _selectedTopology) is not null ||
                   PartyWarpControllerPlan.FromFrame(frame, _selectedTopology) is not null)
                    ? "正在尝试实验性传送"
                    : "当前跨区请求不支持自动传送";
            var anchorState = _follow is not null && frame.Handoffs.Any(item =>
                _follow.MatchTransitionAnchor(item) is not null)
                ? "；雷达发现同路径入口候选，尚未自动靠近或交互"
                : string.Empty;
            _followStatus.Text = $"检测到跨区：{requests}；{warpState}，跨区自动方向已停止{anchorState}。";
            return;
        }
        if (frame.PadCommands.Any(command => command.Status == "approach_blocked"))
        {
            var blocked = string.Join("、", frame.PadCommands.Where(command =>
                command.Status == "approach_blocked").Select(command => $"P{command.Player}"));
            _followStatus.Text = $"{blocked} 入口追赶无进展，自动方向已停止；请手动处理。";
            return;
        }
        if (_selectedTopology?.Id is "3-three-singles" or "4-balanced" or
            "5-third-solo" or "5-main-solo" or "6-balanced" &&
            frame.Reason is not ("party disbanded" or "local client left party" or "followed member left party"))
        {
            var states = string.Join(" · ", frame.PadCommands.Select(command =>
                $"P{command.Player} {command.Status switch
                {
                    "following" => "跟随",
                    "catching_up" => "追赶",
                    "approaching_transition" => "靠近入口（实验性）",
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
            ("holding", "Close-range mirror") => "近距离镜像方向已输出；游戏内移动效果待验证。",
            ("holding", "Mirrored dodge / sprint") => "镜像闪避／冲刺方向已输出；游戏内效果待验证。",
            ("holding", _) => "跟随者已停靠，自动方向已停止。",
            ("waiting", "missing position") => "缺少角色位置样本；请检查独立探针或传感器与进程绑定。自动方向已停止。",
            ("waiting", "different or unknown instance") => "两端尚无可用的同一区域实例证据；自动方向已停止。",
            ("waiting", "stale position") => "角色位置样本已过期；自动方向已停止。",
            ("waiting", "dead follower") => "传感器报告跟随角色生命值为零；自动与右摇杆方向已停止。",
            ("waiting", "party disbanded") => "日志报告队伍解散，本次跟随已锁定停止；重新组队后再启动。",
            ("waiting", "local client left party") => "日志报告本地角色离队，本次跟随已锁定停止；重新组队后再启动。",
            ("waiting", "followed member left party") => "日志报告所跟随角色离队，本次跟随已锁定停止；重新组队后再启动。",
            _ => "等待新鲜位置、区域日志与队伍状态；自动方向已停止。"
        };
    }

    private void FollowWarpStatusChanged(string detail) =>
        _followStatus.Text = $"跨区传送：{detail}";

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

    private void ToggleManualWarpToMain()
    {
        if (_follow is not { } session) return;
        try
        {
            var started = session.ToggleManualWarpToMain();
            _followStatus.Text = started
                ? "已请求手动追到 MAIN；仅处理当前已确认的跨区传送请求。"
                : "已请求取消手动追到 MAIN。";
        }
        catch (InvalidOperationException error)
        {
            _followStatus.Text = $"手动追到 MAIN 未启动：{error.Message}";
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
            session.WarpStatusChanged -= FollowWarpStatusChanged;
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

    private async Task RunOneClickStartAsync()
    {
        var topology = _selectedTopology;
        if (topology is null)
        {
            _status.Text = "先选择人数。";
            return;
        }
        var missing = Enumerable.Range(0, topology.ClientCount)
            .FirstOrDefault(index => string.IsNullOrWhiteSpace(_paths[index].Text), -1);
        if (missing >= 0)
        {
            _status.Text = $"先为客户端 {missing + 1} 选择游戏目录。";
            return;
        }
        var priorClients = _launchedClients;
        await RunLauncherAsync(ClientAction.Start);
        if (ReferenceEquals(_launchedClients, priorClients) ||
            _launchedClients.Count != topology.ClientCount || !ReferenceEquals(_selectedTopology, topology))
        {
            _oneClickFollowRequested = false;
            return;
        }
        if (_bridge is null && topology.PlayerCount > 1)
            await PrepareBridgeAsync();
        if (_bridge is null && topology.PlayerCount > 1)
        {
            _status.Text = $"游戏已打开，输入连接尚未就绪：{_pairingStatus.Text}";
            return;
        }
        _oneClickFollowRequested = topology.PlayerCount > 1;
        _status.Text = _follow is not null ? "游戏已打开，跟随已启动。" :
            topology.PlayerCount == 1 ? "游戏已打开。" :
            "游戏已打开。按画面提示确认角色，完成后自动尝试跟随。";
        if (_pairingSettled) await ContinueOneClickFollowAsync();
    }

    private async Task ContinueOneClickFollowAsync()
    {
        if (!_oneClickFollowRequested || !_pairingSettled) return;
        _oneClickFollowRequested = false;
        UpdateFollowControls();
        if (!_startFollow.IsEnabled)
        {
            _followStatus.Text = "角色已确认；位置数据或跟随设置尚未就绪。";
            return;
        }
        await StartFollowAsync();
    }

    private async Task RunLauncherAsync(ClientAction action)
    {
        if (HybridSelected && _selectedTopology is not null &&
            !HybridTopologySupport.CanBridge(_selectedTopology) &&
            !(_selectedTopology.PlayerCount == 1 && HybridTopologySupport.HasNativeP1(_selectedTopology)))
        {
            _status.Text = "当前布局的键鼠桥接尚未接入。";
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
        ControllerAutoSelection? selectedController = null;
        var startRadarAfterBinding = false;
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
                    !_followerMirrorPreferenceReady || !_engageDodgePreferenceReady)
                {
                    _status.Text = "先确认并保存菜单保护、跟随按键及右摇杆闪避设置；游戏尚未启动。";
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
                var selected = await ControllerLaunchPreflight.DiscoverAsync(bridgeExecutable,
                    gameExecutables, useWgi);
                if (!selected.Ready || selected.Device is null)
                {
                    _status.Text = selected.Message;
                    return;
                }
                selectedController = selected;
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
                            _selectedTopology.Slots, selected.PhysicalIndex ?? 0,
                            selected.Device, gameExecutables,
                            _rightStickFollowerOverride.IsChecked == true,
                            _selectedTopology.Id is "2-split" or "3-three-singles" && _autoFlask.IsChecked == true,
                            useWgi, lastFocusedLeads: _lastFocusedLeads.IsChecked == true,
                            menuGuardEnabled: _menuGuard.IsChecked == true, followerMirror: _followerMirror,
                            engageDodgeEnabled: _rightStickFollowerOverride.IsChecked == true &&
                                _engageDodge.IsChecked == true,
                            engageDodgeBinding: (string)_engageDodgeBinding.SelectedItem!);
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
            var logStartOffsets = action == ClientAction.Start
                ? Enumerable.Range(0, _selectedTopology.ClientCount)
                    .Select(index => LaunchedClients.CaptureLogStartOffset(_paths[index].Text?.Trim()))
                    .ToArray()
                : [];
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
                    var launched = LaunchedClients.ParseSuccessfulLaunch(output.Result,
                        _selectedTopology.ClientCount);
                    _launchedClients = action == ClientAction.Start
                        ? launched.Select((client, index) => client with
                            { LogStartOffset = logStartOffsets[index] }).ToArray()
                        : launched;
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
                    await PrepareBridgeAsync(pendingPrivate, selectedController);
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
                // The supplied 0.6.17 first-run profile enables the radar.
                startRadarAfterBinding = true;
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
                if (startRadarAfterBinding) StartRadar();
            }
        }
    }

    private async Task<string?> PrepareAutoLoginsAfterBindingAsync(PartyTopology topology,
        IReadOnlyList<LaunchedClient> clients)
    {
        string? result;
        try
        {
            result = await PrepareAutoLoginsAfterBindingCoreAsync(topology, clients);
        }
        catch
        {
            await RestoreMainClientForegroundAsync(clients);
            throw;
        }
        if (await RestoreMainClientForegroundAsync(clients)) return result;
        return string.IsNullOrEmpty(result)
            ? "主客户端未能恢复前台，请手动聚焦后再配对。"
            : result + Environment.NewLine + "主客户端未能恢复前台，请手动聚焦后再配对。";
    }

    private static Task<bool> RestoreMainClientForegroundAsync(IReadOnlyList<LaunchedClient> clients) =>
        clients.Count == 0 ? Task.FromResult(false) :
        PoeChatInviteService.IsExactForeground(clients[0]) ? Task.FromResult(true) :
        PoeChatInviteService.FocusExactWindowAsync(clients[0], CancellationToken.None);

    private async Task<string?> PrepareAutoLoginsAfterBindingCoreAsync(PartyTopology topology,
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
                if (_autoLoginChoices[index].IsChecked != true) continue;
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
                if (_autoLoginChoices[index].IsChecked != true)
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
