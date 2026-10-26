# PartyPilot

独立开发的 PoE2 多角色跟随软件工作区。当前交付包含 **跟随决策内核、实时诊断链路、回放工具与初步桌面入口**；Windows 和 PoE2 游戏内控制尚未验收。它与 `bpup/poe2-scripts` 没有代码或构建依赖。

`release/desktop/PartyPilot.Desktop.exe` 是 Windows x64 自包含桌面程序，按所提供版本主页的“选择输入 → 选择人数 → 准备并开始”顺序组织。它提供 1–6 人的 [角色与客户端布局选项](TOPOLOGY_RESEARCH.md)，依所选布局显示 1–3 个客户端程序路径；2P、3P、5P 的初始布局与所提供版本的设置相符。客户端路径、Steam/Epic 启动 URI、窗口布局预检、显式启动和已运行窗口绑定接到 `PartyPilot-ClientLaunch.exe`。最多三个 P1–P4 系统虚拟手柄的布局可从桌面启动受保护桥接：须先启动或绑定每个游戏窗口，取得进程 ID 与窗口句柄，用户逐角色启动登录画面检测。桥接要求对应窗口位置与尺寸稳定、确实位于前台，才转发受保护的 A 键短按；失焦或窗口变化会回中或中断短按。游戏内角色加入仍由用户确认，最后等待手柄回中。实体手柄隔离仅由用户勾选确认，程序尚未核验 HidHide 或游戏内结果；登录画面仅以窗口下方中央区域的颜色特征判断，连续两帧命中后放行，20 秒未命中则进入有标记的超时回退，尚未在真实游戏画面验证。需要四个系统手柄或第三客户端 P5/P6 进程内输入的布局会明确禁用桥接。自动跟随、战斗与雷达尚未接入这个界面，不能把“启动游戏窗口”或“人工确认配对”视为“开始完整组队”。人数必须主动选择，启动按钮在选择前保持禁用；macOS 可编译和预览界面，但实际游戏启动只在 Windows 开放。

```sh
dotnet build ui/PartyPilot.Desktop.csproj -c Release
dotnet run --project ui-smoke/PartyPilot.Desktop.Smoke.csproj -c Release -- /tmp/PartyPilot-ui-preview.png
dotnet publish ui/PartyPilot.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o release/desktop
```

桌面项目使用 [Avalonia 11.3.22](https://www.nuget.org/packages/Avalonia.Desktop/11.3.22)。无界面测试已创建窗口并渲染预览；当前 Mac 锁屏导致本机原生窗口渲染初始化失败，Windows 桌面和 PoE2 运行仍待验证。

## 当前可运行功能

- 读取逐帧 JSONL 位置数据，输出等待、停靠、跟随和追赶命令。
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

用户指定的 MultiPilot 0.6.17 安装目录位于 `/Users/liuchang/Desktop/project/MultiPilotPoE2/current`。其随附快速指南描述了 1–6 人、最多三个游戏窗口、虚拟手柄、跟随、Smart Focus 等功能；这些是参照清单，不能证明本项目已实现。

回放内核只根据最多六个角色的已知坐标计算方向。示例中的距离数字仅用于回放测试，不代表 0.6.17 的参数。独立窗口布局工具已提供 Windows 目标代码；整体仍没有直接读取 PoE2 内存、自动组队、战斗、拾取、实际药剂按键或完整图形设置界面。坐标 `Area` 必须是实例标识；仅有地图名不足以证明两个角色位于同一实例。

可选的 `-trail-spacing` 与 `-trail-max-step` 会让跟随者依次走向已观测的队长轨迹点，而非直接指向队长当前位置。两项必须同时显式设置，且最大步长要大于采样间距。若队长单次观测位移超过上限，轨迹跟随会保持等待，直到明确重置；单人回放帧可传 `"reset":true`，组队回放帧可传 `"reset_follow":true`。重置前应确认新的位置与实例数据可靠。轨迹点本身不能证明道路可通；不提供经验证的地形时，轨迹模式无法绕开未知障碍。回放里的这两个数值仅用于检查拐弯与跳变，不是 MultiPilot 0.6.17 参数。

受控回放帧还可提供 `terrain` 网格：明确的实例标识、原点、格子尺寸、宽高和阻挡格。网格寻路仅走上下左右相邻格，不穿过阻挡格；地图与角色实例不符、起终点超出地图、目标不可达时停止输出方向。使用 `-require-terrain` 可从第一帧开始要求有效地图；若已收到过地图，后续缺失也会等待，直到明确重置。示例网格是人工构造的，**当前没有从 PoE2 取得或验证地形数据**，也没有验证游戏世界坐标与网格坐标的对应关系；此功能只是独立路径决策原型。

`bridge/PartyPilot.Bridge.csproj` 是另一个可执行程序：它使用公开的 ViGEm.NET API，按 `--slots 1,2` 等明确配置创建 P1–P4 范围内的 1–4 个虚拟 Xbox 360 手柄，从标准输入读取带 `slot` 的 JSON 命令，将方向写入对应左摇杆；该槽位命令停止超过 500 毫秒会松开摇杆。实体手柄模式最多创建三个系统虚拟手柄。桌面入口使用 `--pairing-protected`：初始不转发实体输入，收到带目标进程 ID 和窗口句柄的 `arm_pairing` 后，核对窗口归属、前台状态和几何稳定性，每 200 毫秒采样目标窗口下方中央画面，连续两帧匹配登录控件颜色特征或进入已标记的 20 秒超时回退后，要求窗口位置及尺寸稳定 750 毫秒、前台稳定 750 毫秒、输入回中 250 毫秒，再为首/第二位本地角色发送 350/24 毫秒的虚拟 A 键短按；角色选择后通过 `finish_pairing` 进入 400 毫秒回中阶段。`a_release` 指虚拟短按已松开，**不代表游戏内配对成功**。未受保护的命令行镜像模式仍按实体手柄事件报告 A 键松开。未知槽位或无效命令会使所有手柄回中。窗口核对尚未在 Windows 或 PoE2 验证；游戏世界坐标到摇杆轴的映射也未验证。运行需要 Windows 与已安装的 ViGEmBus。项目没有分发驱动；[ViGEm 项目已停止维护](https://docs.nefarius.at/projects/ViGEm/End-of-Life/)。

同一程序还可在 Windows 上以 `--probe-xinput 0 --samples 100` **只读**采样指定物理手柄槽位。实验性镜像模式需要显式传入 `--enable-controller --slots 1,2 --physical-index 0 --main-slot 1 --physical-hidden-confirmed`，该模式至多容纳三个虚拟 XInput 槽位。它把所选实体手柄的按钮、扳机和双摇杆写到焦点虚拟手柄；L3/R3 的按下沿切换 P1/P2，这两个切换键不会转发；其他虚拟手柄继续接收自动化方向。启动前要求所选实体 XInput 槽位已连接；虚拟手柄槽位未知、重复或与实体槽位冲突时退出。运行中持续检查虚拟槽位，发生变化或实体手柄断连时让所有手柄回中并退出，重新检查身份与隔离后才能重启。`--physical-hidden-confirmed` 是操作者确认游戏无法直接读取实体手柄，本项目尚未实现 HidHide 配置或核验。XInput 槽位本身不能证明物理设备身份，其他实体设备占据原槽位的风险仍待解决，因此此模式尚未在 Windows 或游戏中验收。结构布局按 Microsoft 的 [XINPUT_STATE](https://learn.microsoft.com/en-us/windows/win32/api/xinput/ns-xinput-xinput_state) 与 [XINPUT_GAMEPAD](https://learn.microsoft.com/en-us/windows/win32/api/xinput/ns-xinput-xinput_gamepad) 定义，并在 macOS 检查结构大小。

已生成的 Windows 文件位于 `release/`。手柄桥接需要明确传入 `--enable-controller --slots 2`，并从标准输入持续接收 JSON 命令。桥接已交叉编译，协议解析检查已在 macOS 运行，**尚未在 Windows 或 PoE2 中运行验证**；用户当前没有可用的虚拟机。

`sensor/` 是基于公开 ExileCore2 接口的只读诊断插件。它默认关闭，启用后以 10Hz 向本机 `127.0.0.1:47617` 发送进程 ID、时间、区域名称、区域哈希和玩家三维坐标。`release/PartyPilot-SensorLog.exe` 可接收并显示这些数据。区域名称不是实例 ID；区域哈希能否唯一标识两客户端的同一实例也**待游戏实测**。预览包包含本项目的 `PartyPilot.Sensor.dll` 与同次构建的 `PartyPilot.Sensor.deps.json`；本地 ExileCore2 参考目录中的已编译插件使用 `Plugins/Compiled/<插件名>/` 结构，用户实际安装目录及加载结果仍需在 Windows 上核对。插件默认关闭，必须在对应 ExileCore2 实例中确认加载并启用；双客户端跟随要求两个游戏 PID 都收到传感器样本。插件已针对当前参考程序集编译，接收程序已用模拟数据验证；真实游戏数据仍待测试。重新编译需设置 `EXILECORE2_DIR` 为 ExileCore2 安装目录，本项目不分发 ExileCore2 本身的二进制文件。`scripts/package_preview.py` 会核验并打包上述传感器文件和 Windows 预览程序。

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

`release/PartyPilot-ClientLaunch.exe` 补上直接从游戏程序启动到等待窗口的基本流程。重复 `-exe` 按客户端顺序指定 1–3 个游戏程序；启动前检查文件、目标进程名及安装目录互不相同。程序逐个启动客户端，按新进程 ID 最多等待每个窗口 60 秒；可用 `-apply-layout -monitors 1,2 -mode multiple` 在全部窗口出现后安排位置，也可用 `-mode stacked -size full -monitors 1,1 -apply-layout` 请求无边框全屏叠放。`-attach` 不启动游戏，而是要求每个指定程序恰好对应一个已运行进程及可用游戏窗口；多个同路径进程会拒绝绑定。`-check` 只做路径与布局预检，不启动或绑定游戏；`-attach` 不与 `-check` 或 `-uri` 合用。在 macOS 上可配合 `-displays-file testdata/layout-displays.json` 验证人工屏幕数据。已启动客户端若后续步骤失败，会在输出中保留其进程 ID，不会静默关闭游戏。

商店客户端可按槽位增加 `-uri 槽位=启动URI`，同时仍须用相应 `-exe` 指定预期游戏程序。Steam 只接受所提供版本使用的 PoE2 游戏 URI；Epic URI 应从该安装的 Epic 清单取得。工具在发送 URI 前记录同名进程，随后只把新出现、程序路径与 `-exe` 一致且具有游戏窗口的唯一进程归入该槽位；多个匹配进程会报错。URI 启动无法从商店协议直接取得游戏进程 ID，因此若同时有其他方式启动同一程序，进程归属仍需人工核对。

```sh
PartyPilot-ClientLaunch.exe -exe "C:\\PoE2-A\\PathOfExile_x64.exe" -exe "D:\\PoE2-B\\PathOfExile_x64.exe" -apply-layout -monitors 1,2 -mode multiple
PartyPilot-ClientLaunch.exe -exe "C:\\PoE2-A\\PathOfExile_x64.exe" -exe "D:\\PoE2-B\\PathOfExile_x64.exe" -attach
PartyPilot-ClientLaunch.exe -exe "C:\\Steam\\PathOfExile_x64Steam.exe" -uri 1=steam://rungameid/2694490 -check
```

相同独立版安装目录的额外实例仍未实现；目标版本使用专门的多实例运行时，不能用重复启动普通程序代替。控制器启动前保护、账号准备和自动登录仍未实现。启动 URI、已运行窗口绑定、进程归属、窗口等待及布局应用只通过参数测试和 Windows 目标编译，尚未在 Windows/PoE2 运行验证。

`release/PartyPilot-ResourceWatch.exe` 提供独立的前后台进程资源策略工具。根据所提供版本的静态实现，它按明确的 1–3 个游戏进程 ID 监视前台窗口；在后台可设置 CPU 硬上限、处理器亲和性或进程内存硬限制，回到前台时请求恢复。RAM 告警只观察工作集，不施加内存限制。默认输出配置预览，只有加 `-apply` 才开始管理进程；按 Ctrl+C 退出时也会请求恢复。程序会核对进程程序名、保持进程句柄，并在进程结束时停止操作。示例中的数值只用于说明参数，不代表 0.6.17 的默认配置：

```sh
PartyPilot-ResourceWatch.exe -pids 1234,5678 -cpu 25 -affinity 0x3 -ram-warning 4096 -ram-limit 8192
PartyPilot-ResourceWatch.exe -pids 1234,5678 -cpu 25 -affinity 0x3 -ram-warning 4096 -ram-limit 8192 -apply
```

该工具已通过策略单元测试、配置预览和 Windows 目标编译；尚未在 Windows 或 PoE2 上执行。Job Object 分配、现有作业嵌套、前后台切换和限制恢复都需要 Windows 实机验证。它目前是独立命令行工具，尚未接入启动器或设置界面。

`release/PartyPilot-FlaskReplay.exe` 提供自动药剂的**离线决策回放**。传感器现在额外只读发送当前及最大生命、魔力、能量护盾；这些字段已按本地 ExileCore2 程序集的 `Life` 组件元数据编译验证，但尚未在游戏中核对数值含义。回放工具采用提供版本静态代码中的 P1–P6 生命/魔力“重置规则”，要求每帧显式给出 `menu_open` 与 `recovering`，只输出建议触发，不发送按键。它覆盖普通阈值、紧急阈值、冷却、恢复后的重新触发和显式持续使用规则，还可在离线样本中演示能量护盾替代/合并生命、Ward 暂缓生命药剂及 Ward 破裂触发。当前传感器没有可靠的 Ward 来源；共享药剂、空瓶确认和自定义动作仍未实现。示例：

```sh
PartyPilot-FlaskReplay.exe -replay testdata/flask-example.jsonl
PartyPilot-FlaskReplay.exe -replay testdata/flask-defence-example.jsonl
PartyPilot-FlaskReplay.exe -replay testdata/flask-combined-example.jsonl -combined-defence
```

`release/PartyPilot-LiveFollow.exe` 把现有传感器 UDP 位置流与每客户端独立日志接入跟随内核，输出与 `BridgeFeed` 兼容的逐帧决策。它要求 `-roster 1:进程ID,2:进程ID`、每槽位 `-log` 或 `-exe`、队长和显式距离/时效参数；当前仅支持 2–3 个游戏进程且每进程一名角色。在 Windows 上，启动与运行期间会只读查询各进程的程序路径，要求指定日志与该程序目录下的 `logs/Client.txt` 为同一文件；校验失败时输出等待命令并退出。macOS 上只运行合成数据测试，无法执行该 Windows 进程校验。启动前的日志记录不会让实时决策开始移动；两份日志都必须在启动后写入相同区域 ID 与 seed，且各自再收到新的位置样本。进程、日志、位置或区域证据缺失、过期或不一致时输出等待命令。识别到队伍解散或任一已配置客户端本地离队时，跟随停止并锁定到本次进程结束；指定 `-leader-name` 后，被跟随角色离队也会触发同样的停止。其他成员离队不会触发该规则。示例中的距离是受控测试值，不是目标软件参数：

```sh
PartyPilot-LiveFollow.exe -roster 1:1234,2:5678 -leader 1 -leader-name Leader -exe "1=C:\\PoE2-A\\PathOfExile.exe" -exe "2=C:\\PoE2-B\\PathOfExile.exe" -stop 2 -resume 3 -catchup 10 -max-age 750ms -log-max-age 30m
```

桌面入口现可在“双客户端、每端一名角色”完成手柄配对后启动此实时决策程序，并把 P2 的方向送到系统虚拟手柄槽位 P3；可随时停止，或在桥接退出时撤销自动方向。停止、恢复、追赶距离与位置/日志有效期必须由操作者明确填写；可选填队长角色名以启用已有的被跟随成员离队停机规则。界面区分缺少位置、区域实例证据不足、位置过期以及队伍解散/离队的锁定停机，不借用测试示例作为默认配置。这个接线只通过无界面检查与 Windows 目标编译，尚未在 Windows/PoE2 验证真实传感器、区域身份、角色绑定或移动方向。

该链路已用合成 UDP 与日志验证“等待 → 追赶 → 样本过期等待”、“追赶 → 被跟随成员离队后持续等待”和“追赶 → 日志消失后等待并退出”，并交叉编译 Windows 程序。进程路径读取采用 Microsoft 文档中的 [OpenProcess](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-openprocess) 与 [QueryFullProcessImageNameW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-queryfullprocessimagenamew)。离队文案及角色名仍缺真实 `Client.txt` 样本验证；没有真实 Windows/PoE2 运行证据，也没有验证 ExileCore2 的坐标系与虚拟摇杆方向映射，因此输出仍属于 **implementation-aligned / integration-pending**。

`PartyReplay` 读取受控数据中的明确实例标识，计算各队员命令。显式启用 `-last-focused-leads` 后，手动聚焦的角色成为跟随目标，原 P1 会转为自动跟随；返回 P1 焦点时恢复原队长。未启用该选项时仍按固定队长计算。`-follow-targets 2:3` 指定 P2 跟随 P3；未指定的队员跟随当前 MAIN。`BridgeFeed -slot 2` 选择一个队员，`BridgeFeed -all` 输出所有队员命令及焦点切换事件。手动接管或等待会松开相应自动化摇杆。队伍角色编号与系统手柄槽位不同时，须逐一传入 `-pad-map 角色:槽位`；例如双客户端 2P 使用 `BridgeFeed -all -pad-map 1:1 -pad-map 2:3`，缺失或重复映射会拒绝输出。此映射目前只支持 P1–P4 系统手柄，P5/P6 的进程内路径仍未实现。上述双客户端命令已在 macOS 用模拟数据贯通到 C# 桥接协议解析器。Windows 上可以另传 `-pace 100ms`，再把命令输出接到明确槽位的 `PartyPilot.Bridge.exe` 做受控手柄测试；**这条 Windows/游戏输入管道尚未运行验证**。真实传感器尚不能提供经验证的实例标识，所以当前的只读监视器不会自动连接这条控制管道。

状态：**implementation-aligned / integration-pending**。
