# MultiPilot 0.6.17 核心机制核对

日期：2026-10-28。依据本机标记 0.6.17 的安装目录和任务工作区的反编译文本。以下是**静态分析**，不是运行验收，也不是原始 C# 工程。反编译文件留在任务工作区 `work/`，不作为 PartyPilot 产品源码提交。

| 链路 | 静态可确认的连接 | 尚缺的证据 |
| --- | --- | --- |
| 角色位置 | `memory-probe/MultiPilotMemoryProbe.dll` 中 `PlayerSlotVectorResolver.Resolve` 搜索本地 1–2 角色向量；`ProbeSession.CaptureMotion` 读取位置与顺序可靠性。`MultiPilot.Core.dll` 的 `MemoryReaderService` 以期望角色数启动独立运动探针，在 `ApplyMotion` 中要求 `attached`、`playerOrderReliable` 和足量角色，之后发布 `MotionFrame` 及雷达运动数据。 | 当前 PoE2 版本下实际内存布局、角色身份与画面 P1–P6 的对应；换区、重连、死亡等场景。 |
| 跟随和追赶 | `SmartFollowEngine.Compute` 接受运动帧、地形、停靠距离、领队和追赶参数；区域或领队变化时重置路径；`ComputeFresh` 使用领队轨迹、停靠区、路径/视线与冲刺条件产生状态和方向。`PartyFollowSession` 负责临时领队/目标选择，不等于驱动角色的全部链路。 | 帧如何在所有拓扑中分发、控制输出是否被游戏消费、距离阈值的游戏内手感以及跨区域追赶表现。 |
| 多客户端输入 | `AssignedRuntime.LocalClient` 根据 `ClientAssignment.Slots` 建立每客户端运行时；核心与手动输入进入双槽输出仲裁，最终调用 `ControllerSlotBridgeService.Submit`。桥接服务可见 P5/P6 的共享内存样本和停止时的中性输入。 | 桥接模块在目标 Windows/游戏进程中是否成功加载；手柄和键鼠两套实际路由；前后台窗口切换时输入是否正确隔离。 |
| Tab 地图 | `RadarOverlayService.Start` 按游戏进程、热键、运动映射和角色掩码启动雷达进程；`RadarFeedPump.ToggleOverlay` 切换显示，数据处理里有地形缓存、访问区域网格、传送点/出口/任务标记分类和雷达绘制。 | 0.6.17 实际运行时的标记准确率、迷雾边界、任务点来源、分辨率/窗口模式覆盖与 Tab 热键冲突。 |
| 中文 UI/启动 | 安装目录 `Assets/getting-started/quick-start.html` 描述自动发现 Client 1、添加游戏、账号保存、输入选择、READY 逐人配对；反编译工程中可见 `SetupView`、`PlayView`、`PairingOverlayViewModel`、`ShellViewModel` 等视图与状态类。 | 精确的 0.6.17 窗口布局、完整中文文案、样式资源、键位默认值和全部交互状态；部分随包指南混有其他构建版本内容。 |

## 对“1:1”的判定

- **原理层：部分可知。** 托管程序集的上述方法和调用可读，足以指导独立实现的边界与验证点；反编译不能恢复原始注释、工程历史、全部变量含义或未覆盖的运行分支。
- **界面层：尚不可称 1:1。** 有官方页面和本地指南，但缺 0.6.17 每个页面/状态的实机截图与交互记录。
- **功能层：尚不可称 1:1。** 当前没有真实 Windows/PoE2 对照验收。必须逐场景比较 P1–P6 位置、跟随追赶、后台输入、Tab 地图、失败恢复与停止后的输入释放。
- **实现目标：行为复刻。** 对照可观察结果独立实现，并保留静态证据与实测差异；不把反编译文本直接当作开源产品的可构建源码，也不处理付费授权或有效期。

下一步的可执行验收顺序：先做 0.6.17 界面逐页取证，再用相同的 2/4/6 角色场景在 Windows 上对比目标版与 PartyPilot；每项记录输入方式、客户端/角色映射、实际画面、结果和失败日志。没有目标版运行记录的项持续标为待确认。

## ExileCore2 依赖核对

- **事实：** [FollowLad](https://github.com/AlphaCaster/FollowLad) 明确自称 ExileApi2/Core PoE2 插件。[ExileCore2 仓库](https://github.com/exCore2/ExileCore2)说明它是插件框架。
- **事实：** 当前 PartyPilot 的 `BETA_TEST.md` 要求用户手动把 `PartyPilot.Sensor` 放入 ExileCore2 的 `Plugins/Compiled`。这是目前的产品依赖和使用门槛。
- **事实：** MultiPilot 0.6.17 已读依赖包含 `NoddPilot.Memory`、`NoddPilot.Offsets`、`NoddPilot.Runtime`，并有独立的 `memory-probe/MultiPilotMemoryProbe.dll`。在本次读取的依赖清单和相关方法中未见 ExileCore2 引用。
- **待确认：** [ExiledBot 2](https://exiled-bot.net/)、[ExileBot Autofollower](https://exilebot.com/autofollower)、[ExilePal](https://exilepal.com/)的公开页面没有说明是否使用 ExileCore2；不能将“没有写依赖”当作“肯定没有依赖”。MultiPilot 是否曾借鉴该框架也无法从现有静态证据排除。
- **实现影响：** 若要达到目标版的开箱体验，PartyPilot 需要让位置和地图数据链路无需用户手动安装 ExileCore2 插件。现有插件链可作为联调路径，但不能据此宣称与目标版安装和启动体验一致。

## 位置探针的具体工作原理（静态可见部分）

`ProbeSession.CaptureMotion` 首先确认目标进程和当前区域实例可读；随后通过 `AreaEntityListReader.ReadEntities` 取得实体候选，再由 `PlayerSlotVectorResolver.Resolve` 在区域实例附近寻找本地角色向量。解析器只接受 1–2 项、指针结构和容量合理的向量，并核对每项是否指向角色实体；若出现多个相互冲突的有效顺序，返回不可靠。探针在区域变化或约五秒后重新解析；正常帧读取按顺序排列的角色位置，任意角色位置失败便发出 `playerOrderReliable=false`。桌面端 `MemoryReaderService.ApplyMotion` 只有在进程附着、顺序可靠且角色数量足够时才发布运动帧。

这说明目标版的核心位置来源是**随软件运行的独立内存探针**，不是让用户先安装 ExileCore2 插件。它解释了 PartyPilot 当前“一键启动”仍不能做到目标版开箱即用的关键原因。偏移量在游戏更新后的有效性、区域实例与画面角色的对应，还须在 Windows/PoE2 中验证。

## 本轮范围的静态覆盖表

下表的“已读入口”均来自本机标记为 0.6.17 的程序集反编译文本，表示方法和控制流可读，**不表示游戏内已经复现**。

| 用户流程 | 已读入口与确认的原理 | PartyPilot 当前差距 | 状态 |
| --- | --- | --- | --- |
| 客户端发现与启动 | `GameClientTypes` 列出 Steam、Epic、Standalone 与六种已知进程名；`EpicGameClientDiscoveryService` 读 Epic 安装清单。`MultiInstanceLaunchService` 对额外独立客户端检查 `FindigoMultiInstance.dll` 完整性，创建暂停进程、加载运行时后恢复；`ShellViewModel.HomeCanStart` 汇总输入、人数、账号和会话状态。 | 本轮已加入目录选择与“一键启动”入口；Steam/Epic 自动发现、所有启动拓扑与目标版的处理顺序仍未对齐。 | 静态原理已读；启动实测待确认 |
| 账号与登录 | `GameAccountCredentialStore` 用 Windows 当前用户保护保存每客户端邮箱和密码；`GameClientAutoLoginService` 核对窗口、识别登录表单或使用确认标记，填入后离开文本输入状态，配对首个 A 键才继续登录。 | 当前独立版有按客户端加密保存与手动字段标记；目标版的自动识别体验和准确率尚未复现。 | 静态原理已读；输入效果待确认 |
| 手柄配对和切换 | `SetupViewModel` 展示逐客户端 P1–P6 配对顺序；`FocusModeService` 对聚焦按键做按下沿检测、组合键优先、350ms 防抖，同一角色再次触发则返回共同控制。`ControllerSlotBridgeService` 把两个输入样本写入共享映射，并有释放为中性输入的分支。 | PartyPilot 有配对引导和快捷键逻辑，但真实手柄识别、后台隔离和 P5/P6 桥接仍未实测。 | 静态原理已读；游戏输入待确认 |
| 位置和角色顺序 | `ProbeSession.CaptureMotion` + `PlayerSlotVectorResolver` + `MemoryReaderService.ApplyMotion` 形成独立进程读取、顺序校验、失效拒收和运动帧发布链。 | PartyPilot 仍依赖 ExileCore2 插件；双角色位置入口为显式实验功能。 | 静态原理已读；独立探针未实现 |
| 同区跟随和追赶 | `SmartFollowEngine.Compute/ComputeFresh` 按区域与领队重置，结合停止距离、领队轨迹、地形路径和冲刺条件输出停靠、跟随、追赶或手动接管状态。 | 独立版有跟随决策和追赶路径，但目标版阈值、每角色方向和游戏手感未逐场景对照。 | 静态原理已读；行为一致性待确认 |
| 换区与传送 | `SmartFollowTransitionCoordinator` 持有入口/传送门目标、队长与跟随客户端区域证据、接近与交互状态；未确认进入时有失败和释放路径。 | 独立版相关路径仍是实验功能，入口对应与成功换区未实测。 | 静态原理已读；行为一致性待确认 |
| Tab 地图 | `RadarOverlayService.Start` 启动独立雷达进程并传递游戏进程、热键和运动映射；`RadarFeedPump` 有地形缓存、访问区域网格、地图标记分类和渲染，包含任务点、传送点及出口。 | 独立版依赖 ExileCore2 标记与候选地形文件；地图完整性、方向和去迷雾未实测。 | 静态原理已读；数据与画面待确认 |
| 主界面 | `MainWindow` 与 `SetupView`/`PlayView` 是 Avalonia 视图；`ShellViewModel` 有人数、客户端、输入、账号、启动条件和状态绑定。 | PartyPilot 也是 Avalonia，但布局与文案只做了简化，尚无 0.6.17 逐页实机截图对比。 | 静态结构已读；视觉/交互 1:1 待确认 |

**研究结论：** 本轮目标功能的主要静态调用链均已找到，可以明确独立实现应替代哪些来源与流程；不能把“主要链路找到”表述为“全部代码已恢复”。完整 1:1 仍缺目标版与 PartyPilot 在同一 Windows/PoE2 环境下的逐项画面、输入和结果对照。尤其是目标版的独立位置/地图探针、PartyPilot 的手动插件依赖，是最先需要解决的结构性差距。

**用户确认的例外：游戏目录选择。** `ShellViewModel.ChooseConfiguredClientExecutableAsync` 的 0.6.17 静态代码实际调用 Avalonia 的 `OpenFilePickerAsync`，让用户选择 `PathOfExile*.exe`；Epic 客户端则先尝试从安装清单自动发现。用户明确要求 PartyPilot 用“打开目录选择”而非填写完整路径，因此独立版采用文件夹选择后列出发现的游戏程序。此处按用户要求改善交互，不声称与目标版选择器完全一样。

## 独立探针的定位入口（本轮新增核对）

**事实：** `AnchorScanRunner.Run` 从 `NoddPilot.Offsets.dll` 内嵌的 `signatures.json` 读取 `GameStatePattern` 与 `FileRootPattern`，扫描游戏进程模块，把匹配点转换为指针存储地址，再读取 `GameState` 指针。找到它后，优先检查指定的 `IngameState` 子状态，失败时做形状校验搜索；找不到会明确返回未解析。`GameBuildOffsetOverrides.Apply` 先计算游戏 EXE 的 SHA-256；静态代码内明确列有一个原始构建哈希和两个 2026-09-05 PoE2 0.5.5 客户端哈希。未知哈希在目标版会尝试最近的布局并继续做实时校验，不能因此认定当前游戏版本兼容。

**事实：** 本仓库新增 `probe/`，它独立于 ExileCore2，用只读进程权限查找上述已知哈希的游戏进程，并验证 `GameState` 定位候选是否唯一。未知构建、零候选或多候选均不输出可供跟随使用的位置。此程序目前只输出诊断 JSON；它不发布运动帧、不读取角色位置，也不接入桌面的一键启动。

**待确认：** 当前 PoE2 EXE 哈希、签名是否仍匹配、`GameState → IngameState → AreaInstance` 的有效偏移、同窗口角色向量与位置坐标、雷达地形来源和 Windows 下读取权限。本机没有 Windows/PoE2，扫描器自测只能证明字节匹配、跨块读取和无效指针拒收，不能证明游戏内可用。

## 本机文件再核对（2026-10-28）

使用新安装的 `reverse-engineering` skill 的只读 triage 脚本检查本机安装目录中的三个文件，供后续分析确认是否针对同一组字节：

| 文件 | SHA-256 | 静态识别 |
| --- | --- | --- |
| `NoddPilot.Licensing.dll` | `c69fd58faf6cf706f483e77e6f82502a7a7afd2836690cfbbbbf01a5a5a2e554` | .NET 托管程序集，76,800 字节 |
| `MultiPilot.Core.dll` | `26dd6d9ac32c97087661e1d74aac27c16a932a01f3243696a32d1661ea006a7e` | .NET 托管程序集，41,319,936 字节；`.text` 熵较高，单凭熵不能证明采用某种壳 |
| `memory-probe/MultiPilotMemoryProbe.dll` | `2fcc449e258315c13b6a0788ca8acb9de234c8cdf7007fabc5873985d2ab41f6` | .NET 托管可执行文件，1,344,000 字节 |

**授权与偏移的证据边界：** `LicenseSession` 静态代码显示激活响应验签、解包加密偏移密钥及会话心跳；`OffsetCatalog` 同时有 `CreateDefault()`，使用内嵌签名和默认偏移，另有 `ActivateSealed(key)` 切换到解封后的配置。因而“存在服务端密钥”是事实，“所有功能在任何情况下都必须取得该密钥”仍是待确认。未执行或修改授权流程，不能称为已破解。
