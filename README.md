# PartyPilot

技能绑定表的 0.6.17 静态读取证据、传感器只读候选扫描及 Windows 采样诊断入口见[技能绑定取证](SKILL_BIND_RESEARCH.md)。当前无法从真实游戏取得并验证该表，自动战斗仍未接入。

桥接现有默认关闭的[战斗短按通道](COMBAT_INPUT_RESEARCH.md)，支持受保护配对后的独占短按与回中；桌面和战斗决策尚未连接该通道。

另有默认关闭的[战斗规划回放](COMBAT_DECISION_RESEARCH.md)，记录 0.6.17 六角色与 18 条默认连招，使用显式合成世界数据输出只读计划，尚不驱动游戏。

独立开发的 PoE2 多角色跟随软件工作区。当前交付包含 **跟随决策内核、实时诊断链路、回放工具与初步桌面入口**；Windows 和 PoE2 游戏内控制尚未验收。它与 `bpup/poe2-scripts` 没有代码或构建依赖。

`release/desktop/PartyPilot.Desktop.exe` 是 Windows x64 自包含桌面程序，按所提供版本主页的“选择输入 → 选择人数 → 准备并开始”顺序组织。它提供 1–6 人的 [角色与客户端布局选项](TOPOLOGY_RESEARCH.md)，依所选布局显示 1–3 个客户端程序路径；2P、3P、5P 的初始布局与所提供版本的设置相符。客户端路径、Steam/Epic 启动 URI、窗口布局预检、显式启动和已运行窗口绑定接到 `PartyPilot-ClientLaunch.exe`。最多三个公共虚拟手柄的布局沿用所选实体 XInput 编号；需要四个公共虚拟手柄的布局从 Windows Gaming Input 读取与设备 VID/PID 唯一匹配的实体手柄，再创建四个公共虚拟手柄。公共手柄路径均须提供 HidHide 设备实例 ID 与游戏程序路径，由桥接只读检查隔离配置；两客户端布局可先准备桥接再启动或绑定窗口，三客户端布局须先取得三个游戏进程与窗口身份，再准备组合桥接。随后由用户逐角色启动登录画面检测。桥接要求对应窗口位置与尺寸稳定、确实位于前台，才转发受保护的 A 键短按；失焦或窗口变化会回中或中断短按。游戏内角色加入仍由用户确认，最后等待手柄回中。HidHide 配置检查和唯一 VID/PID 匹配不能证明设备与 HidHide 实例 ID 相同或游戏实际看不到实体手柄；登录画面仅以窗口下方中央区域的颜色特征判断，连续两帧命中后放行，20 秒未命中则进入有标记的超时回退，尚未在真实游戏画面验证。第三客户端布局已接入按进程身份绑定的组合桥接与配对引导，但未在 Windows/PoE2 验证输入和游戏内加入。双客户端 2P 和三个客户端 3P 的每端一名角色布局可在配对确认后启动实时跟随决策；战斗技能尚未接入界面；雷达仅有待实机验证的可见实体覆盖层，不能把“启动游戏窗口”或“人工确认配对”视为“开始完整组队”。人数必须主动选择，启动按钮在选择前保持禁用；macOS 可编译和预览界面，但实际游戏启动只在 Windows 开放。

```sh
dotnet build ui/PartyPilot.Desktop.csproj -c Release
dotnet run --project ui-smoke/PartyPilot.Desktop.Smoke.csproj -c Release -- /tmp/PartyPilot-ui-preview.png
dotnet publish ui/PartyPilot.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o release/desktop
```

桌面项目使用 [Avalonia 11.3.22](https://www.nuget.org/packages/Avalonia.Desktop/11.3.22)。无界面测试已创建窗口并渲染预览；当前 Mac 锁屏导致本机原生窗口渲染初始化失败，Windows 桌面和 PoE2 运行仍待验证。

绑定 Windows 游戏窗口后，桌面入口提供手动的 `Current Party` 画面检查。它核对窗口进程身份，从客户端截图中按所提供 0.6.17 版本的生命条行规则计算每个窗口的候选数量。计数器已通过目标版本四种合成窗口布局的离线检查；截图和真实游戏面板仍待 Windows/PoE2 验证。零条可能表示面板未打开，任何计数都不能证明成员身份或自动组队成功。

自动组队恢复状态机按所提供版本的 `Missing → SendingInvite → AcceptingInvite → Verifying → RetryWait/Stable/Failed`、最多 10 次全新邀请及 250/400/650/900 毫秒重试间隔工作。桌面入口可填写 P1–P6 的准确角色名；完成窗口绑定和配对引导后，可以手动发送一次邀请，也可以运行/取消自动组队恢复。自动恢复发送 `/invite`、用受邀方虚拟手柄执行目标版的菜单按键顺序，再检查 `Current Party` 画面候选人数。三客户端手动恢复现同时运行两个受邀客户端的重试流程，各自从客户端 1 的稳定基线计算候选人数；聊天邀请、短按和截图会短时串行占用游戏焦点，等待和核验流程可交错。桥接在上一短按到期时释放旧槽位后才接受新槽位短按。默认开启“最近手动聚焦角色成为 MAIN”：启用时退出单人聚焦仍记住最近手动角色，并优先选择其稳定客户端作为邀请方；关闭时 MAIN 固定为 P1。该 MAIN 不稳定时再选当前焦点所在的稳定客户端，最后取编号最小者。桌面允许关闭此规则并在本地保存选择；桥接运行期间不可更改。目标版死亡状态和分布式运行时队长判定仍未接入。并行调度、焦点记忆和桥接脉冲交接已通过离线测试，真实游戏窗口时序仍待 Windows/PoE2 验证。

桌面入口现在可显式开始监听各已绑定游戏程序所在目录的独立 `logs/Client.txt`。已存在的日志从当前文件末尾开始，不重放旧离队事件；尚未出现的日志会在创建后从开头读取。仅把以换行结束的目标版离队、被踢或队伍解散文本送入恢复入口；具名离队按所填准确角色名定位客户端，本地离队按日志来源定位，解散则恢复所有客户端。多个客户端共用同一日志路径时监听不会启动。检测到日志截短后会从新文件开头续读；可识别 UTF-8 和带 BOM 的 UTF-16 日志。离线测试覆盖历史行、半行、新建日志、截短和两种编码；真实日志文件与游戏进程、角色的对应关系以及 Windows/PoE2 自动触发效果仍待验证。

发送前检查每个客户端至少一个角色名、控制字符与命令分隔符，以及邀请方进程所属窗口和前台状态。桥接仅在受保护配对完成后接受自动组队专用菜单按键，限定目标版使用的 Start、十字键左、A、B 和对应按压时长。独占期间隔离普通跟随与实体手柄输入；停止、异常输入或 10 秒无续期时回中。聊天邀请、虚拟手柄菜单操作、截图和游戏内成员身份均未在 Windows/PoE2 实测；即使候选行数达到目标值，也不能据此宣称真实组队成功。

目标版静态默认的 F8 自动组队运行/取消快捷键现接入桌面入口：仅已绑定游戏窗口确为前台时拦截，按住重复键不会重复触发；关闭桌面时卸载键盘钩子。无界面逻辑检查和 Windows x64 发布编译通过，尚未在 Windows 测试实际键盘事件与游戏窗口焦点。

## 当前可运行功能

- 读取逐帧 JSONL 位置数据，输出等待、停靠、跟随和追赶命令。追赶状态现按所提供 0.6.17 版本的出厂 `ClassicSprintCatchUp` 配置保留至停止距离，避免距离短暂低于追赶阈值时立即退出；这仍只是离线决策规则，游戏中的冲刺、地形路径和移动效果未验证。
- 丢失坐标、区域实例不一致、数据过期或坐标无效时停止输出移动方向。
- 停靠与恢复使用不同距离，减少边界附近的反复启停。
- 多角色状态机支持明确的角色/客户端名单：手动接管队员时暂停该队员的自动跟随，切回主角色后恢复。

运行：

```sh
go test ./...
go run ./cmd/partypilot -replay testdata/example.jsonl -stop 2 -resume 3 -catchup 10 -max-age 750ms
go run ./cmd/partypilot -replay testdata/trail-turn-example.jsonl -stop 1 -resume 2 -catchup 30 -max-age 750ms -trail-spacing 1 -trail-max-step 5
go run ./cmd/partypilot -replay testdata/terrain-wall-example.jsonl -stop 1 -resume 2 -catchup 10 -max-age 750ms -require-terrain
go run ./cmd/partyreplay -replay testdata/party-example.jsonl -roster 1:1,2:2 -leader 1 -stop 2 -resume 3 -catchup 10 -max-age 750ms
go run ./cmd/partyreplay -replay testdata/party-example.jsonl -roster 1:1,2:2 -leader 1 -last-focused-leads -stop 2 -resume 3 -catchup 10 -max-age 750ms
go run ./cmd/partyreplay -replay testdata/party-targets-example.jsonl -roster 1:1,2:2,3:2 -leader 1 -follow-targets 2:3 -stop 2 -resume 3 -catchup 10 -max-age 750ms
go run ./cmd/partyreplay -replay testdata/party-example.jsonl -roster 1:1,2:2 -leader 1 -stop 2 -resume 3 -catchup 10 -max-age 750ms | go run ./cmd/bridgefeed -slot 2
go run ./cmd/partyreplay -replay testdata/party-example.jsonl -roster 1:1,2:2 -leader 1 -stop 2 -resume 3 -catchup 10 -max-age 750ms | go run ./cmd/bridgefeed -all
go run ./cmd/partyreplay -replay testdata/party-example.jsonl -roster 1:1,2:2 -leader 1 -stop 2 -resume 3 -catchup 10 -max-age 750ms | go run ./cmd/bridgefeed -slot 2 | dotnet run --project bridge/protocol-tests/ProtocolTests.csproj -c Release -- --stdin
```

在 macOS 上交叉编译 Windows 程序：

```sh
GOOS=windows GOARCH=amd64 go build -o PartyPilot.exe ./cmd/partypilot
```

## 功能参照与证据

新增 `release/PartyPilot-LootReplay.exe -replay <JSONL 文件>`，可用明确提供的物品卡片和安全条件离线检查拾取决策。传感器另有默认关闭的 `DiagnoseItemCards`，只读观察当前 UI 树的原始卡片与 owner 指针，尚不能核验玩家归属或拾取资格。这两部分不会发送手柄输入，实际自动拾取仍未实现，详见[自动拾取取证](AUTO_PICKUP_RESEARCH.md)。

用户指定的 MultiPilot 0.6.17 安装目录位于 `/Users/liuchang/Desktop/project/MultiPilotPoE2/current`。其随附快速指南描述了 1–6 人、最多三个游戏窗口、虚拟手柄、跟随、Smart Focus 等功能；这些是参照清单，不能证明本项目已实现。

回放内核只根据最多六个角色的已知坐标计算方向。示例中的距离数字仅用于回放测试，不代表 0.6.17 的参数。独立窗口布局工具已提供 Windows 目标代码；整体仍没有独立的 PoE2 内存读取器、经游戏验证的自动组队、战斗技能、拾取或完整图形设置界面；药剂按键新增有限实时路径，尚未在游戏内验证。坐标 `Area` 必须是实例标识；仅有地图名不足以证明两个角色位于同一实例。实时传感器现在明确标记 `CoordinateSpace=world`，跟随引擎依据 0.6.17 分布式核心中的 10.869565217391305 世界单位/网格单位比例，将世界坐标换算为网格坐标后计算距离；旧版无单位标记的样本不能驱动跟随。这个静态换算尚需在 Windows/PoE2 实测核对，不能据此认定目标版默认距离已校准。

可选的 `-trail-spacing` 与 `-trail-max-step` 会让跟随者依次走向已观测的队长轨迹点，而非直接指向队长当前位置。两项必须同时显式设置，且最大步长要大于采样间距。若队长单次观测位移超过上限，轨迹跟随会保持等待，直到明确重置；单人回放帧可传 `"reset":true`，组队回放帧可传 `"reset_follow":true`。重置前应确认新的位置与实例数据可靠。轨迹点本身不能证明道路可通；不提供经验证的地形时，轨迹模式无法绕开未知障碍。回放里的这两个数值仅用于检查拐弯与跳变，不是 MultiPilot 0.6.17 参数。

受控回放帧还可提供 `terrain` 网格：明确的实例标识、原点、格子尺寸、宽高和阻挡格。普通人工网格仍采用四邻格寻路；目标版压缩格式解出的网格标记为 `target_navigation`，改用八邻格搜索、一格净空、斜向防穿角、附近可走格搜索及视线简化。目标轨迹模式在搜索和视线检查中避开队长周围五格，复用并推进仍然可通行的路线；起点贴近障碍时仅允许目标版规则内的短距离脱困段。单人和队伍回放器都可加 `-target-trail -async-target-path` 启用有界后台规划；队伍回放可用 `-pace` 留出帧间规划时间。规划未完成时输出零方向的 `Planning route`，完成后才使用路线，失败时按目标版绕人恢复规则输出 `Obstacle recovery`。这些分支仍未经过真实地形和游戏输入验证。目标格式的 `origin_x`、`origin_y` 表示第一个格子的中心；普通人工网格的原点仍表示左上边界。地图与角色实例不符时停止输出方向；普通网格目标不可达时也停止。使用 `-require-terrain` 可从第一帧开始要求有效地图；若已收到过地图，后续缺失也会等待，直到明确重置。示例网格是人工构造的，**当前没有从 PoE2 取得或验证地形数据**，也没有验证游戏世界坐标与网格坐标的对应关系；此功能只是独立路径决策原型。

受控回放可显式加 `-target-trail -classic-collision-dodge -require-terrain` 检查目标版的可选经典冲刺碰撞判断。它按前方中线及两条侧向视线判定障碍，并把结果交给已有冲刺重试状态机；该选项在所提供版本的出厂设置中为关闭，PartyPilot 也保持关闭。完整地图已经可作为受控回放和显式绑定的实时输入，但尚无游戏验证，因此回放不能代表游戏内的完整地形路径或冲刺效果。

`bridge/PartyPilot.Bridge.csproj` 是另一个可执行程序：它使用公开的 ViGEm.NET API，按 `--slots 1,2` 等明确配置创建 P1–P4 范围内的 1–4 个虚拟 Xbox 360 手柄，从标准输入读取带 `slot` 的 JSON 命令，将方向写入对应左摇杆；该槽位命令停止超过 500 毫秒会松开摇杆。实体 XInput 编号模式最多创建三个公共虚拟手柄；`--physical-wgi` 模式用 [Windows Gaming Input 的 RawGameController](https://learn.microsoft.com/en-us/uwp/api/windows.gaming.input.rawgamecontroller) 读取准备阶段唯一匹配的 VID/PID，可为四人双客户端布局创建四个公共虚拟手柄。准备时同型号设备不唯一会拒绝；创建虚拟手柄后则按已锁定的 [NonRoamableId](https://learn.microsoft.com/en-us/uwp/api/windows.gaming.input.rawgamecontroller.nonroamableid) 寻找原实体设备，其他同 VID/PID 设备不会使会话误判为丢失。启动实体手柄模式前，桥接通过 [HidHide 官方只读控制接口](https://docs.nefarius.at/projects/HidHide/API-Documentation/)检查隐藏功能已开启、应用列表未反转、明确指定的实体设备在隐藏列表中、桥接程序在允许列表中且指定的游戏程序不在允许列表中；任一检查失败便不创建虚拟手柄。桌面入口要求填入 HidHide 中的设备实例 ID 与游戏程序路径，并读取桥接的检查完成信号。此项仅核对配置，**不能证明设备 ID 对应所选 XInput 编号或 WGI 设备、设备栈已重新枚举，或游戏实际看不到实体手柄**。桌面入口使用 `--pairing-protected`：初始不转发实体输入，收到带目标进程 ID 和窗口句柄的 `arm_pairing` 后，核对窗口归属、前台状态和几何稳定性，每 200 毫秒采样目标窗口下方中央画面，连续两帧匹配登录控件颜色特征或进入已标记的 20 秒超时回退后，要求窗口位置及尺寸稳定 750 毫秒、前台稳定 750 毫秒、输入回中 250 毫秒，再为当前角色发送虚拟 A 键短按；角色选择后通过 `finish_pairing` 进入 400 毫秒回中阶段。`a_release` 指虚拟短按已松开，**不代表游戏内配对成功**。未受保护的命令行镜像模式仍按实体手柄事件报告 A 键松开。未知槽位或无效命令会使所有手柄回中。窗口核对尚未在 Windows 或 PoE2 验证；游戏世界坐标到摇杆轴的映射也未验证。运行需要 Windows 与已安装的 ViGEmBus。项目没有分发驱动；[ViGEm 项目已停止维护](https://docs.nefarius.at/projects/ViGEm/End-of-Life/)。

同一程序还可在 Windows 上以 `--probe-xinput 0 --samples 100` **只读**采样指定物理手柄槽位。实验性镜像模式需要显式传入 `--enable-controller --slots 1,2 --physical-index 0 --main-slot 1 --physical-hidden-confirmed --device-instance "HID\\..." --game-exe "C:\\PoE2\\PathOfExile.exe"`；需要多客户端时为每个游戏程序重复 `--game-exe`。可先运行 `PartyPilot.Bridge.exe --check-hidhide --device "HID\\..." --game-exe "C:\\PoE2\\PathOfExile.exe"`，只读取上述配置并输出各检查项；此诊断不会启动虚拟手柄。该模式至多容纳三个虚拟 XInput 槽位。它把所选实体手柄的按钮、扳机和双摇杆写到焦点虚拟手柄；L3/R3 的按下沿切换 P1/P2，这两个切换键不会转发；其他虚拟手柄继续接收自动化方向。启动前要求所选实体 XInput 槽位已连接；虚拟手柄槽位未知、重复或与实体槽位冲突时退出。运行中持续检查虚拟槽位，发生变化或实体手柄断连时让所有手柄回中并退出，重新检查身份与隔离后才能重启。`--physical-hidden-confirmed` 是操作者的附加确认；程序现已读取 HidHide 配置，但尚未设置或在 Windows 运行核验它。XInput 槽位本身不能证明物理设备身份，其他实体设备占据原槽位的风险仍待解决，因此此模式尚未在 Windows 或游戏中验收。结构布局按 Microsoft 的 [XINPUT_STATE](https://learn.microsoft.com/en-us/windows/win32/api/xinput/ns-xinput-xinput_state) 与 [XINPUT_GAMEPAD](https://learn.microsoft.com/en-us/windows/win32/api/xinput/ns-xinput-xinput_gamepad) 定义，并在 macOS 检查结构大小。

桌面输入区新增可选“右摇杆控制跟随角色”，命令行可加 `--right-stick-follower-override`。开启时，主角色的右摇杆瞄准输出归零；右摇杆任一轴绝对值超过 8000，才暂时覆盖有资格队员的左摇杆方向。双客户端实时链路只在队员位置/区域证据有效、决策为停靠/跟随/追赶且传感器报告生命值大于零时，随决策帧给出手动接管资格；等待、死亡、生命值缺失或命令断流都不会保留手动方向。2P 单客户端布局可在配对完成后直接用右摇杆移动 P2，无需跟随帧；配对期间保持回中，松开摇杆后回到已有自动方向或回中。桌面仅对这一布局向桥接传入 `--direct-right-stick-slot 2`，该模式尚无同客户端 P2 生死来源或自动跟随。默认 L3/R3 可单独聚焦 P1/P2，再按一次返回共同控制，单人聚焦时其他手柄回中；聚焦有 350 毫秒防连按。有第三个虚拟手柄的受支持布局可用 LT+L3 聚焦 P3；桌面显式传入角色到系统手柄的映射，双客户端 P2 的 R3 会落到系统 P3。已用本地协议和模拟进程验证，真实右摇杆轴、角色生死状态、焦点及游戏内效果仍待 Windows/PoE2 实测。

桥接现按目标版的手柄菜单键或 B 键变化跟踪菜单保护：菜单打开后、以及关闭后的 650 毫秒等待期，暂停未聚焦手柄的自动方向；聚焦手柄仍接受实体输入以便操作菜单。焦点切换时，已按住的按钮不会误记到另一角色。桥接会向桌面发送 `menu_guard` 状态事件；目前桌面只接收该事件，尚未据此启动药剂。该信号只能反映经过桥接的手柄输入，无法发现鼠标、键盘或其他来源打开的菜单，也未在游戏内验证，因此不能单独作为自动药剂的安全放行依据。

已生成的 Windows 文件位于 `release/`。手柄桥接需要明确传入 `--enable-controller --slots 2`，并从标准输入持续接收 JSON 命令。桥接已交叉编译，协议解析检查已在 macOS 运行，**尚未在 Windows 或 PoE2 中运行验证**；用户当前没有可用的虚拟机。

`sensor/` 是基于公开 ExileCore2 接口的只读诊断插件。它默认关闭，启用后以 10Hz 向本机 `127.0.0.1:47617` 发送进程 ID、时间、区域名称、区域哈希和玩家三维坐标。`release/PartyPilot-SensorLog.exe` 可接收并显示这些数据。另有默认关闭的 `DiagnoseVisiblePlayers` 选项，打开后在同一诊断流中列出最多 16 个可见玩家实体的名字、实体 ID、位置、地址和路径，并标明与当前本地角色 ID 相同的实体；还会报告 ExileCore2 的游戏数据对象和当前角色地址。这些地址是诊断证据，不会自动绑定 P2 或驱动同窗口自动跟随，原因见[同窗口角色身份核对](SAME_CLIENT_RESEARCH.md)。区域名称不是实例 ID；区域哈希能否唯一标识两客户端的同一实例也**待游戏实测**。预览包包含本项目的 `PartyPilot.Sensor.dll` 与同次构建的 `PartyPilot.Sensor.deps.json`；本地 ExileCore2 参考目录中的已编译插件使用 `Plugins/Compiled/<插件名>/` 结构，用户实际安装目录及加载结果仍需在 Windows 上核对。插件默认关闭，必须在对应 ExileCore2 实例中确认加载并启用；双客户端跟随要求两个游戏 PID 都收到传感器样本。插件已针对当前参考程序集编译，接收程序已用模拟数据验证；真实游戏数据仍待测试。重新编译需设置 `EXILECORE2_DIR` 为 ExileCore2 安装目录，本项目不分发 ExileCore2 本身的二进制文件。`scripts/package_preview.py` 会核验并打包上述传感器文件和 Windows 预览程序。

该可见实体诊断现在还带各角色的生命、魔力和能量护盾读数；有角色路径和地址但暂时无名字的实体也会保留。实时内核另有可测试的同进程双角色有序帧入口；实时命令行可显式选用 `-experimental-ordered-input` 与每名角色的 `-role-bind ROLE:CLIENT:LOCAL:PID`，按客户端提供一份 `-log`/`-exe`。此入口分开接收 `ordered_client` 快速位置与 `ordered_vitals` 慢速资源报文；现有候选扫描不会生成可靠帧，桌面仍只开放每客户端一名角色的实时跟随。四人以上布局的真实自动跟随仍待游戏数据源和 Windows/PoE2 验证。

有序位置报文可只包含世界坐标；资源读数可另由慢速报文提供，若出现则须成对有效。新鲜生命值可延续到后续位置帧，用于死亡判断和手动接管资格；过期、换区或顺序失效后会撤销。显式开启自动药剂时，慢速报文只有在菜单状态明确可操作，且与新鲜位置、当前区域日志和非零区域哈希一致时才可产生按角色分配的请求；本机合成链路已输出 P2 生命药剂请求。没有可信的双角色实机生产端，桌面未接入双角色药剂输入。

实时内核还可把同一游戏进程的单份 `Client.txt` 区域生成及离队记录作用到其两名本地角色；该路径已用合成日志行验证，双角色实时命令行与游戏内行为仍未接入或验收。

开启 `DiagnoseLocalSlotCandidates` 时，诊断现会按所提供版本的公开默认偏移尝试读取候选角色的原始三维坐标，放在 `LocalSlotCandidates.RawPositions` 中；读不到的位置为 `null`。同一诊断还给出当前角色的 ExileCore2 坐标到原始世界/网格 XY 的两种数值距离，不自动判断哪种坐标系正确。这不是经游戏验证的第二角色位置，也不会进入自动跟随或输入，具体边界见[同窗口角色身份核对](SAME_CLIENT_RESEARCH.md)。

可将 `PartyPilot-SensorLog.exe` 的输出保存为 JSONL，再运行 `PartyPilot-SlotAudit.exe -input sensorlog.jsonl` 查看每个进程的双槽完整帧、坐标差距、顺序变化、换区与每槽运动次数。该报告只帮助实机取证，不会启用自动控制。
同一帧还按地址逐槽对照当前可见的 ExileCore2 玩家实体，`VisiblePositionComparisons` 中不可见的槽位为 `null`。这帮助检查第二角色的坐标关系；未在游戏中采样验证，也不改变跟随准入。

传感器在读到本地角色 `Render` 组件时，还会向 `127.0.0.1:47619` 发送不含角色名的隐私渲染样本。桌面端的实验性“自动姓名牌遮挡”需要这个按进程核对的新鲜样本，并对该窗口截图寻找目标版色板中的姓名样式像素。它不等同于目标版的有序双角色位置证据；Windows/PoE2 的识别与叠层效果仍需实测。

需要同窗口槽位取证时，还可单独开启默认关闭的 `DiagnoseLocalSlotCandidates`。它每秒从 ExileCore2 的游戏数据地址，以及“游戏状态地址 + 目标版公开静态默认偏移 656”所指向的地址分别只读寻找本地角色向量，并报告这两种基址是否相等及候选来自哪一个。偏移和基址关系均未实机验证，输出仅为候选，不能用于自动绑定 P2。合成向量检查可用 `DOTNET_ROLL_FORWARD=Major dotnet run --project sensor-tests/PartyPilot.Sensor.Tests.csproj -c Release`；本机只有 .NET 9，因此测试运行时需允许从目标 .NET 8 向上滚动。详见[同窗口角色身份核对](SAME_CLIENT_RESEARCH.md)。

找到候选后，传感器会在每次 10Hz 发送前后重验槽位向量并重新读取候选位置；本次采样中顺序或路径改变会撤销候选。该新鲜度只说明采样链路没有复用上秒的位置，不证明默认内存偏移或角色身份在 PoE2 中正确。

同一帧的进程、游戏状态地址、区域数据地址、区域哈希或当前角色地址发生变化时，传感器会丢弃整帧，避免把旧位置附到新区域。此检查仍未经过真实游戏场景验证。

传感器还提供默认关闭的 `DiagnoseRadarEntities`，每 500 毫秒发送最多 96 个当前可见实体的路径类别与坐标，包含经 `Player` 组件确认的可见角色、少数明确的玩法世界对象，以及已解析内部物品路径和 `Base.Name` 的当前可见掉落，作为独立雷达的数据起点。掉落还可携带经范围校验的堆叠数与稀有度，读取失败时为空；目前覆盖层只显示名称。可见角色不等于已确认本机槽位。桌面入口可在已绑定的 1–3 个游戏窗口上显示实验性标记层：须明确填写像素/坐标单位，在对应 ExileCore2 实例中启用该诊断；独立 UDP 端口为 `127.0.0.1:47618`，与实时跟随互不争用。程序仅接受对应进程且两秒内的新鲜标记，失焦或数据过期隐藏，Tab 可切换显示。Windows 穿透点击和窗口定位代码尚未实机验证；掉落不判断分配、价值或可拾取状态；它没有休眠实体、完整掉落标签或地形路线；详见[雷达数据与目标版差距](RADAR_RESEARCH.md)。

主页的 `Hide names` 开关按所提供 0.6.17 的静态姓名隐私规则遮挡应用内当前显示的角色名：队长名输入框显示圆点，雷达玩家标签显示 `hidden`；实际姓名仍供跟随/离队识别使用，物品名称不变。它尚不遮挡游戏客户端自身的姓名牌或社交界面；详见[姓名隐私差距](PRIVACY_RESEARCH.md)。

`release/PartyPilot-PartyMonitor.exe` 可用 `-leader-pid`、`-follower-pid` 和 `-max-age` 监视两个进程的只读位置流。它区分数据缺失、过期、区域不一致及“同名同哈希候选”；候选**不等于已证实处于同一实例**，因此监视器不输出任何手柄命令。模拟双进程 UDP 数据已验证这些状态切换。

`release/PartyPilot-RosterWatch.exe` 使用 `-roster 1:进程ID,2:进程ID,... -leader 1 -max-age 750ms` 对 1–6 个明确配置的游戏进程做只读监视。它按角色槽位输出队长是否缺失/过期，以及每个队员的缺失、过期、区域不一致或同区域候选状态；拒绝重复槽位、重复进程 ID 和乱序旧样本。该程序已用本机模拟 UDP 报文验证三角色状态变化，仍未接到真实 PoE2。区域名称与哈希相等仍只是候选，不会进入自动控制。

`release/PartyPilot-AreaLogWatch.exe` 通过重复的 `-log 槽位=Client.txt路径` 参数只读监视 2–6 份明确指定、互不相同的客户端日志。例如 `-log "1=C:\\PoE2-A\\logs\\Client.txt" -log "2=C:\\PoE2-B\\logs\\Client.txt" -leader 1 -max-age 30s`。也可用 `-exe 槽位=游戏程序路径`，从该程序所在目录推导 `logs/Client.txt`；每个槽位只能指定一种来源。默认读取每份日志末尾最多 512 KiB 的完整历史行，历史吻合仅显示 `historical_candidate`；用 `-prime-history=false` 可跳过历史。随后只接收新写入的完整 `Generating level … area "…" with seed …` 行；两端区域 ID 与 seed 相同且都未过期时输出 `candidate_same_instance`，不同则输出 `different_area`。日志缺失、共用同一文件或文件读取错误会退出。`[SCENE]` 名称不足以区分实例，因此不参与这一匹配。当前仅用合成日志验证了行为，尚无真实 `Client.txt` 样本，也未核实每个日志文件与游戏进程、角色槽位的对应关系。候选结果不会进入自动控制。

`release/PartyPilot-WindowLayout.exe` 参照所提供版本的布局选项，计算并可在 Windows 上安排 1–3 个已启动的游戏窗口：`stacked` 叠放、`tiled` 单屏分屏、`multiple` 多屏分配。`-list` 列出当前屏幕及工作区域；`-monitors 1,1` 按列出的屏幕编号为每个客户端选屏幕，`-pids` 按相同顺序指定游戏进程。默认仅显示计划，明确加 `-apply` 才请求移动窗口。例如：

```sh
PartyPilot-WindowLayout.exe -list
PartyPilot-WindowLayout.exe -pids 1234,5678 -monitors 1,1 -mode stacked -size small -align center
PartyPilot-WindowLayout.exe -pids 1234,5678 -monitors 1,1 -mode stacked -size small -align center -apply
go run ./cmd/windowlayout -displays-file testdata/layout-displays.json -monitors 1,2 -mode multiple
```

Windows 版只接受所提供版本识别的游戏进程名，并在应用前复核窗口属于相应进程。屏幕位置、窗口位置和布局计算已用人工屏幕数据验证；窗口发现与移动仅通过 Windows 目标编译，尚未在 Windows/PoE2 运行。`-size full -apply` 会请求无边框全屏叠放，并在本地配置目录保存原窗口样式；随后对同一游戏进程应用普通布局时会恢复原样式。恢复时同时核对进程 ID、窗口句柄和进程创建时间，避免旧记录作用于新进程。样式切换仅通过 Windows 目标编译，尚未在 Windows/PoE2 实测；此布局工具不负责启动游戏或账号流程。

`release/PartyPilot-ClientLaunch.exe` 补上直接从游戏程序启动到等待窗口的基本流程。重复 `-exe` 按客户端顺序指定 1–3 个游戏程序；启动前检查文件及目标进程名；同目录独立版额外实例需要 `release/native/PartyPilot.MultiInstance.dll`，Steam/Epic 客户端仍需不同安装目录。程序逐个启动客户端，按新进程 ID 最多等待每个窗口 60 秒；可用 `-apply-layout -monitors 1,2 -mode multiple` 在全部窗口出现后安排位置，也可用 `-mode stacked -size full -monitors 1,1 -apply-layout` 请求无边框全屏叠放。`-attach` 不启动游戏；默认要求每个指定程序恰好对应一个已运行进程及可用游戏窗口。若多个客户端共用同一安装目录，命令行绑定时须为每个槽位提供不同的 `-pid 槽位=进程号`；程序会核对进程实际路径与窗口归属。桌面绑定界面可按客户端填写进程号；普通启动和配置检查会忽略该字段。`-check` 只做路径与布局预检，不启动或绑定游戏；`-attach` 不与 `-check` 或 `-uri` 合用，`-pid` 只用于 `-attach`。在 macOS 上可配合 `-displays-file testdata/layout-displays.json` 验证人工屏幕数据。已启动客户端若后续步骤失败，会在输出中保留其进程 ID，不会静默关闭游戏。

商店客户端可按槽位增加 `-uri 槽位=启动URI`，同时仍须用相应 `-exe` 指定预期游戏程序。Steam 只接受所提供版本使用的 PoE2 游戏 URI；Epic URI 应从该安装的 Epic 清单取得。工具在发送 URI 前记录同名进程，随后只把新出现、程序路径与 `-exe` 一致且具有游戏窗口的唯一进程归入该槽位；多个匹配进程会报错。URI 启动无法从商店协议直接取得游戏进程 ID，因此若同时有其他方式启动同一程序，进程归属仍需人工核对。

```sh
PartyPilot-ClientLaunch.exe -exe "C:\\PoE2-A\\PathOfExile_x64.exe" -exe "D:\\PoE2-B\\PathOfExile_x64.exe" -apply-layout -monitors 1,2 -mode multiple
PartyPilot-ClientLaunch.exe -exe "C:\\PoE2-A\\PathOfExile_x64.exe" -exe "D:\\PoE2-B\\PathOfExile_x64.exe" -attach
PartyPilot-ClientLaunch.exe -exe "C:\\PoE2\\PathOfExile_x64.exe" -exe "C:\\PoE2\\PathOfExile_x64.exe" -attach -pid 1=1234 -pid 2=5678
PartyPilot-ClientLaunch.exe -exe "C:\\Steam\\PathOfExile_x64Steam.exe" -uri 1=steam://rungameid/2694490 -check
```

同一独立版安装目录的额外实例现通过独立实现的 `PartyPilot.MultiInstance.dll` 在暂停启动阶段隔离具名互斥体；启动器等待运行时就绪后才恢复游戏进程。这条路径已完成静态检查、编译和参数测试，尚未在 Windows/PoE2 实机验证。桌面点击“启动游戏窗口”时，先调用桥接程序的 `--check-startup` 只读检查：核对所选设备的 HidHide 隔离涵盖所有游戏程序，再检查所选 XInput 编号或唯一 WGI 设备是否可读；不通过时不会启动游戏。只读检查本身不创建虚拟手柄；在 1–2 个客户端的布局中，桌面随后会先启动桥接、核对虚拟手柄槽位，收到就绪回执后才启动游戏，启动器失败时释放桥接。若用户已手动准备桥接且尚未绑定窗口，会复用仍在运行的会话。三客户端布局的私有手柄运行时已有两阶段就绪协议，但仍待 Windows/PoE2 验证。独立原生私有手柄运行时和桥接端已区分“映射加载”和“XInput 钩子就绪”，并提供暂停进程的接入入口；同目录额外实例启动器已有暂停进程交接；三客户端独立版布局现由桌面在第三客户端主线程恢复前启动短暂的私有手柄桥接，确认共享映射已加载后才允许启动器恢复进程，再等待 XInput 钩子就绪。正式组合桥接仍在游戏窗口绑定后接管 P5/P6；此交接和同目录启动均未在 Windows/PoE2 验证。XInput 编号与所选 HidHide 设备是否为同一实体设备仍需实机核对；目标版的完整启动前保护和恢复仍未实现。启动/绑定后的独立客户端账号准备已接入，但目标版完整的启动阶段恢复与 Windows/PoE2 实机验收仍未完成。桌面已加入绑定窗口截图上的邮箱与密码字段标记、逐项确认、最多三次重试及本地保存。对每端一名角色的独立客户端，可显式开启启动/绑定后的自动填入：先检查所有启用客户端的账号、字段标记和邮箱唯一性，再逐一等待登录画面；精确窗口前台、稳定视口及连续两帧登录按钮颜色特征均通过后，才向所绑定窗口填入邮箱和密码。操作后清除临时剪贴板并释放文本焦点，配对前再核验已准备的窗口；登录提交仍交给后续受保护配对步骤。账号准备可以取消；首次没有字段标记时，会等待已绑定窗口的稳定登录画面并打开截图标记界面，逐项确认后保存。用户可选择当前窗口本次手动登录，其他已启用的独立客户端仍可继续准备。标记只在原客户端进程窗口仍有效、当前布局与宽高比核对通过时保存；标记界面只通过本机合成画面构建检查，未在 Windows/PoE2 实测。桌面按客户端提供独立账号资料保存/删除：密码在 Windows 上用当前用户 DPAPI 加密后写入 `%APPDATA%/PartyPilot/AccountProfiles/`，保存成功会清空密码输入框，状态只显示遮挡邮箱。macOS 上只用合成保护器检查了存储流程；真实 Windows DPAPI 往返、目标登录画面与游戏内登录均待 Windows/PoE2 验证。不同客户端的邮箱不能重复；编辑已保存账号时可留空密码沿用原密码。单纯保存账号或登录字段标记不会触发填入或提交；只有显式启用选项后，启动或绑定窗口才会尝试填入；若当时未准备成功，点击当前角色的配对准备按钮仍可重新尝试。自动填入的 Windows 原生输入、目标游戏登录画面识别和游戏内效果均未实机验证。启动 URI、已运行窗口绑定、进程归属、窗口等待及布局应用只通过参数测试和 Windows 目标编译，尚未在 Windows/PoE2 运行验证。

`release/PartyPilot-ResourceWatch.exe` 提供独立的前后台进程资源策略工具。根据所提供版本的静态实现，它按明确的 1–3 个游戏进程 ID 监视前台窗口；在后台可设置 CPU 硬上限、处理器亲和性或进程内存硬限制，回到前台时请求恢复。RAM 告警只观察工作集，不施加内存限制。默认输出配置预览，只有加 `-apply` 才开始管理进程；按 Ctrl+C 退出时也会请求恢复。程序会核对进程程序名、保持进程句柄，并在进程结束时停止操作。示例中的数值只用于说明参数，不代表 0.6.17 的默认配置：

```sh
PartyPilot-ResourceWatch.exe -pids 1234,5678 -cpu 25 -affinity 0x3 -ram-warning 4096 -ram-limit 8192
PartyPilot-ResourceWatch.exe -pids 1234,5678 -cpu 25 -affinity 0x3 -ram-warning 4096 -ram-limit 8192 -apply
```

桌面入口现按已绑定的每个游戏客户端提供独立的 CPU、处理器掩码、RAM 提醒和后台内存上限字段，可先预览，再显式启用或停止。它仅向启动器已核对进程与窗口的 PID 启动管理进程；切换布局、重新启动游戏或关闭桌面时发送停止指令。桌面使用 `-control-stdin`，控制通道关闭也会触发工具的恢复流程。命令行直接使用时仍可按 Ctrl+C 停止。该链路已通过策略测试、本机工具预览、模拟管理进程停止通道和 Windows 目标编译；尚未在 Windows 或 PoE2 上执行。Job Object 分配、现有作业嵌套、前后台切换和限制恢复都需要 Windows 实机验证。

`release/PartyPilot-FpsConfig.exe` 为明确指定的 PoE2 配置文件分别设置前台和后台帧率。两项都必须显式填写：`keep` 保留对应原值，`unlimited` 或 `0` 只关闭该项限制，正整数开启限制并写入该帧率。默认只预览将变动的 `DISPLAY` 键；关闭所有游戏客户端后加 `-apply` 才备份原文件并替换。它只改 `framerate_limit_enabled`、`framerate_limit`、`background_framerate_limit_enabled`、`background_framerate_limit` 中所需的键，不触碰输入模式或其他节。支持 UTF-8、带 BOM 的 UTF-8 与带 BOM 的 UTF-16；重复 `DISPLAY` 节或目标键会拒绝修改。示例路径和帧数仅用于说明，不代表目标版默认值：

```sh
PartyPilot-FpsConfig.exe -config "C:\Users\Me\Documents\My Games\Path of Exile 2\poe2_production_Config.ini" -foreground keep -background 30
PartyPilot-FpsConfig.exe -config "C:\Users\Me\Documents\My Games\Path of Exile 2\poe2_production_Config.ini" -foreground keep -background 30 -apply
```

这对应所提供 0.6.17 静态实现中的两组 FPS 配置键及 `KeepCurrent`、`Unlimited`、指定帧数的语义。桌面入口现有按客户端分开的 FPS 配置卡片：每个客户端需明确填写配置文件路径，可先预览，再勾选已关闭全部游戏客户端后应用；程序还会只读检查已知 PoE2 进程是否仍在运行。同一路径若被不同客户端填写为不同策略，会拒绝应用。桌面到本机工具的预览、应用和备份已在临时配置文件上贯通；尚未在 Windows/PoE2 的真实配置上应用或验证游戏内帧率。

`release/PartyPilot-FlaskReplay.exe` 提供自动药剂的**离线决策回放**。传感器现在额外只读发送当前及最大生命、魔力、能量护盾；这些字段已按本地 ExileCore2 程序集的 `Life` 组件元数据编译验证，但尚未在游戏中核对数值含义。回放工具采用提供版本静态代码中的 P1–P6 生命/魔力“重置规则”，要求每帧显式给出 `menu_open` 与 `recovering`，只输出建议触发，不发送按键。它覆盖普通阈值、紧急阈值、冷却、恢复后的重新触发和显式持续使用规则，还可在离线样本中演示能量护盾替代/合并生命、Ward 暂缓生命药剂及 Ward 破裂触发。已加入静态代码中的空瓶判定：触发后等待至少 900 毫秒，以资源恢复 1.5 个百分点为有效结果；连续三次未生效后不再输出该药剂，资源比停用时回升 3 个百分点后重新启用。该判断仅在持续收到相应资源读数时运行；离线回放本身不发送按键，也不能替代游戏内空瓶确认。当前传感器没有可靠的 Ward 来源；共享药剂和自定义动作仍未实现。示例：

```sh
PartyPilot-FlaskReplay.exe -replay testdata/flask-example.jsonl
PartyPilot-FlaskReplay.exe -replay testdata/flask-defence-example.jsonl
PartyPilot-FlaskReplay.exe -replay testdata/flask-combined-example.jsonl -combined-defence
```

实时药剂可在桌面选中“双客户端、每端一名角色”或“三客户端、每端一名角色”布局、自动药剂选项保持开启（目标版静态默认设置为开启，可在配对前关闭），并完成配对与实时跟随启动后工作。每个客户端的只读传感器需提供已绑定进程的新鲜生命、魔力和最大值；角色死亡、读数缺失/过期或菜单保护中不产生请求。双客户端的 P1/P2 分别映射到系统虚拟手柄 P1/P3；三客户端的 P3 映射到第三进程内的私有 P5。桥接按目标版默认绑定发送生命十字键左、魔力十字键右，并在 120 毫秒后自动松开；菜单保护、配对未完成、实体手柄断连会拒绝或中断短按。当前恢复状态按目标版实时调用固定传入 false，Ward 无可靠实时来源；鼠标/键盘打开菜单无法由手柄保护识别。已完成 Go 决策测试、本地模拟传感器输出以及桌面到 P5 桥接的模拟进程传递，尚未在 Windows/PoE2 验证传感器数值、私有手柄、绑定或实际喝药效果。P6 的命令和桥接路由已有协议支持，但尚无对应的可信双角色实时生命数据。

`release/PartyPilot-LiveFollow.exe` 把现有传感器 UDP 位置流与每客户端独立日志接入跟随内核，输出与 `BridgeFeed` 兼容的逐帧决策。它要求 `-roster 1:进程ID,2:进程ID`、每槽位 `-log` 或 `-exe`、队长和显式距离/时效参数；当前仅支持 2–3 个游戏进程且每进程一名角色。在 Windows 上，启动与运行期间会只读查询各进程的程序路径，要求指定日志与该程序目录下的 `logs/Client.txt` 为同一文件；校验失败时输出等待命令并退出。macOS 上只运行合成数据测试，无法执行该 Windows 进程校验。启动前的日志记录不会让实时决策开始移动；所有已绑定客户端日志都必须在启动后写入相同区域 ID 与 seed，且各自再收到新的位置样本。进程、日志、位置或区域证据缺失、过期或不一致时输出等待命令。跨客户端的目标与跟随者样本若源 `TickMs` 或本机接收时间相差超过 100 毫秒，只让该跟随者等待。传感器的 `TickMs` 取自采集位置后独立读取的 Unix 毫秒时间，接收时间由实时跟随程序收到 UDP 报文时记录；缺任一项的旧样本不能驱动跟随。此双时间门禁依据所提供版本的静态代码实现，采集与接收时序仍待 Windows/PoE2 核对。可选的 `-terrain-binding 槽位=绑定.json` 把完整候选压缩地图送入实时规划；启用地形模式时须为所有参与客户端分别绑定采集文件，并核对进程、区域哈希、日志 ID/seed、坐标映射及跨客户端地图布局。缺少任一绑定或比对不通过时保持等待；两端还须各出现三个不同源时间的位置帧才开始规划。绑定文件格式与未验证边界见[地形与绕行取证](TERRAIN_RESEARCH.md)。识别到队伍解散或任一已配置客户端本地离队时，跟随停止并锁定到本次进程结束；指定 `-leader-name` 后，被跟随角色离队也会触发同样的停止。其他成员离队不会触发该规则。示例中的距离是受控测试值，不是目标软件参数：

```sh
PartyPilot-LiveFollow.exe -roster 1:1234,2:5678 -leader 1 -leader-name Leader -member-name 2=Second -exe "1=C:\\PoE2-A\\PathOfExile.exe" -exe "2=C:\\PoE2-B\\PathOfExile.exe" -stop 2 -catchup 10 -max-age 750ms -log-max-age 30m
```

桌面入口现可在双客户端 2P 或三个客户端 3P、均每端一名角色的布局完成手柄配对后启动此实时决策程序。这两种布局现按目标版的活跃角色列表提供每名角色的“Current MAIN”或其他活跃角色跟随目标，启动时经 `-follow-target 角色=目标` 传给实时引擎；运行时逐名校验跟随者与其实际目标的区域实例身份，其他角色所在区域不代替这项校验；P1–P6 的选择保存在本机用户设置中；损坏的设置文件会阻止跟随启动，需为当前布局的每名角色重新明确选择。最近手动聚焦 P2 后，P2 保持 MAIN，P1 的方向送到系统虚拟手柄槽位 P1；再次手动聚焦 P1 后，P2 的方向送到槽位 P3。切换跟随槽位时先撤销旧槽位的自动方向；可随时停止，或在桥接退出时撤销自动方向。停止、追赶距离与位置/日志有效期必须由操作者明确填写；实时轨迹模式的停靠释放间隔按目标版固定为 3 网格单位；当前可对活跃角色另填 0–200 的单独停止距离，经 `-stop-player 角色=距离` 应用到该角色，留空则沿用明确填写的通用停止距离；目标版默认距离虽已在源码中确认，但本项目传感器的距离单位尚未在游戏里校准，暂不自动套用；单独距离按系统手柄槽位 P1–P6 保存到本机用户设置；损坏记录会阻止跟随启动，修改当前角色的距离可清空旧记录并重新保存；可选填初始队长角色名以启用已有的被跟随成员离队停机规则。若组队邀请区已填写并校验所有角色的准确名称，实时跟随会把当前布局的名称传给离队检测；P2 或 P3 成为 MAIN 后，日志中的同名离队会锁定停止本次跟随。未填写或未校验名称时，不会按猜测的名字停机。界面区分缺少位置、区域实例证据不足、位置过期以及队伍解散/离队的锁定停机，不借用测试示例作为默认配置。无界面检查还用本机模拟子进程验证了“等待 → 跟随 → 退出后回中”、药剂请求、P2 已成为 MAIN 时的启动，以及运行中 P1 → P2 → P1 切换，以及 3P 三客户端从 P1 切到 P3 时，旧槽位回中和各跟随槽位接收指令；这未执行真实 Windows 手柄或游戏输入。Windows 目标编译已通过，真实传感器、区域身份、角色绑定和移动方向仍待 Windows/PoE2 验证。

实时跟随现按 0.6.17 可观察的轨迹采样规则（相邻点至少 1.5 网格单位、最多 1200 点、队长大位移或突发速度跳变时重置）取落后停止距离的目标点，再将网格方向换算为等距视角手柄方向。贴近轨迹目标而停靠后，会记住该目标；目标移动至少 1.5 网格单位时重新判断是否继续停靠。主角色大位移重置轨迹时也清除该记忆。没有地形数据时，若队员到轨迹目标的直线经过主角色附近，还会按目标版静态几何规则绕开主角色。受控转弯、跳变、停靠目标移动与绕人样本已验证；目标版的地形寻路、障碍绕行和游戏输入结果仍未在独立版实时链路中复现或实测。

目标版出厂配置开启 Classic Sprint Catch-up。独立实时轨迹跟随现按所提供控制器的时间和速度阈值给追赶帧加 `sprint_down`，桌面将其送到桥接 B 键；未检测到加速时最多尝试三次，随后回退步行。停靠、等待、手动聚焦和命令过期会释放自动 B 键。目标版的地形碰撞重试需要经验证的实时地形，目前没有输入该信号；真实游戏的冲刺和躲闪按键效果尚未实测。

该链路已用合成 UDP 与日志验证“等待 → 追赶 → 样本过期等待”、“追赶 → 被跟随成员离队后持续等待”和“追赶 → 日志消失后等待并退出”，并交叉编译 Windows 程序。进程路径读取采用 Microsoft 文档中的 [OpenProcess](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-openprocess) 与 [QueryFullProcessImageNameW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-queryfullprocessimagenamew)。离队文案及角色名仍缺真实 `Client.txt` 样本验证；没有真实 Windows/PoE2 运行证据，也没有验证 ExileCore2 的坐标系与虚拟摇杆方向映射，因此输出仍属于 **implementation-aligned / integration-pending**。

`PartyReplay` 读取受控数据中的明确实例标识，计算各队员命令。显式启用 `-last-focused-leads` 后，手动聚焦的角色成为跟随目标，原 P1 会转为自动跟随；退出独占视图时保持最近手动聚焦的 MAIN，明确手动聚焦 P1 后才恢复 P1 为 MAIN。未启用该选项时仍按固定队长计算。`-follow-targets 2:3` 指定 P2 跟随 P3；未指定的队员跟随当前 MAIN。`BridgeFeed -slot 2` 选择一个队员，`BridgeFeed -all` 输出所有队员命令及焦点切换事件。手动接管或等待会松开相应自动化摇杆。队伍角色编号与系统手柄槽位不同时，须逐一传入 `-pad-map 角色:槽位`；例如双客户端 2P 使用 `BridgeFeed -all -pad-map 1:1 -pad-map 2:3`，缺失或重复映射会拒绝输出。此映射现可在明确提供全部角色到运行槽位的对应关系后，把受控决策帧转发到 P5/P6；公共系统手柄仍限 P1–P4，P5/P6 走第三客户端独立共享内存和 Windows 原生 DLL。桌面组合路径已连入桥接主循环与受保护配对引导；实时传感器尚不能为同一游戏进程中的两个角色分别提供经验证的位置，因此未产生 P5/P6 的实时自动跟随决策，也未在 Windows/PoE2 实测，详见[第三客户端输入状态](NATIVE_PRIVATE_SLOTS.md)。上述双客户端命令已在 macOS 用模拟数据贯通到 C# 桥接协议解析器。Windows 上可以另传 `-pace 100ms`，再把命令输出接到明确槽位的 `PartyPilot.Bridge.exe` 做受控手柄测试；**这条 Windows/游戏输入管道尚未运行验证**。真实传感器尚不能提供经验证的实例标识，所以当前的只读监视器不会自动连接这条控制管道。

多角色回放现按所提供 0.6.17 版本的 `ResolveFollowTargets` 静态规则，在每帧决策前解析跟随图：未设置或无效的运行时目标回到当前 MAIN，循环路径上的角色也回到当前 MAIN；其他可到达 MAIN 的链保持原目标。六角色合成帧覆盖循环消解和聚焦后更换 MAIN。命令行现接受 `角色:0` 表示“当前 MAIN”；当目标指向自己、当前布局未激活的角色或无效编号时，也会按目标版设置入口收敛到“当前 MAIN”。目标列表之外的跟随者仍会被拒绝。

状态：**implementation-aligned / integration-pending**。

地形绕行仍缺游戏数据映射。传感器默认关闭的 `DiagnoseTerrain` 输出原始 `RawPathfindingData` 的有界统计，并只读扫描目标版压缩地形候选；成功时回报玩家附近最多 9×9 格的小块，并将候选压缩字节及尺寸、区域标识、原始格坐标、世界坐标、局部小块和 SHA-256 元数据保存到 Windows `%LOCALAPPDATA%\PartyPilot\terrain`。同一进程、当前区域身份和候选地址只采集一次，区域哈希或区域地址变化后允许重新采集；短读、首尾块复读不一致、小块对不上或采集期间区域身份改变时丢弃文件。此采集是诊断候选，不会自动用于实时移动。受控回放支持每字节双格的 `packed_terrain` 输入；实时跟随新增显式绑定入口，在调用者核实并填写实例与坐标映射后才读取候选地图。证据与待验证步骤见[地形与绕行取证](TERRAIN_RESEARCH.md)。

大型受控地图可把原始压缩字节保存为文件，在 `packed_terrain` 中指定 `data_file` 及尺寸；也可指定诊断导出的 `capture_metadata_file`，由回放器校验对应 `.packed` 的大小、SHA-256 和局部小块，再读取尺寸。这三种数据来源互斥，且仍须明确提供 `area`、`origin_x`、`origin_y` 和 `cell_size`；元数据不证明地图实例或坐标映射。回放器直接读取压缩格子并按需搜索，不再受旧的 256×256 展开网格上限限制。重新序列化后的网格若缺原始字节会拒绝作为有效地图，避免把障碍静默变成空地。
