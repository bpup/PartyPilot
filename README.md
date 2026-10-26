# PartyPilot

独立开发的 PoE2 多角色跟随软件工作区。当前交付是 **跟随决策内核与回放工具**，尚不能控制游戏。它与 `bpup/poe2-scripts` 没有代码或构建依赖。

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

当前内核只根据最多六个角色的已知坐标计算方向。示例中的距离数字仅用于回放测试，不代表 0.6.17 的参数。它没有直接读取 PoE2 内存、识别窗口、自动组队、战斗、拾取、药剂或 UI。坐标 `Area` 必须是实例标识；仅有地图名不足以证明两个角色位于同一实例。

可选的 `-trail-spacing` 与 `-trail-max-step` 会让跟随者依次走向已观测的队长轨迹点，而非直接指向队长当前位置。两项必须同时显式设置，且最大步长要大于采样间距。若队长单次观测位移超过上限，轨迹跟随会保持等待，直到明确重置；单人回放帧可传 `"reset":true`，组队回放帧可传 `"reset_follow":true`。重置前应确认新的位置与实例数据可靠。轨迹点本身不能证明道路可通；不提供经验证的地形时，轨迹模式无法绕开未知障碍。回放里的这两个数值仅用于检查拐弯与跳变，不是 MultiPilot 0.6.17 参数。

受控回放帧还可提供 `terrain` 网格：明确的实例标识、原点、格子尺寸、宽高和阻挡格。网格寻路仅走上下左右相邻格，不穿过阻挡格；地图与角色实例不符、起终点超出地图、目标不可达时停止输出方向。使用 `-require-terrain` 可从第一帧开始要求有效地图；若已收到过地图，后续缺失也会等待，直到明确重置。示例网格是人工构造的，**当前没有从 PoE2 取得或验证地形数据**，也没有验证游戏世界坐标与网格坐标的对应关系；此功能只是独立路径决策原型。

`bridge/PartyPilot.Bridge.csproj` 是另一个可执行程序：它使用公开的 ViGEm.NET API，按 `--slots 1,2` 等明确配置创建 1–4 个虚拟 Xbox 360 手柄，从标准输入读取带 `slot` 的 JSON 命令，将方向写入对应左摇杆；该槽位命令停止超过 500 毫秒会松开摇杆。未知槽位或无效命令会使所有手柄回中。它不会将手柄绑定到指定 PoE2 窗口，也没有验证游戏如何映射世界坐标到摇杆轴，因此目前只能用于受控桥接测试。运行需要 Windows 与已安装的 ViGEmBus。项目没有分发驱动；[ViGEm 项目已停止维护](https://docs.nefarius.at/projects/ViGEm/End-of-Life/)。

同一程序还可在 Windows 上以 `--probe-xinput 0 --samples 100` **只读**采样指定物理手柄槽位。实验性镜像模式需要显式传入 `--enable-controller --slots 1,2 --physical-index 0 --main-slot 1 --physical-hidden-confirmed`，该模式至多容纳三个虚拟 XInput 槽位。它把所选实体手柄的按钮、扳机和双摇杆写到焦点虚拟手柄；L3/R3 的按下沿切换 P1/P2，这两个切换键不会转发；其他虚拟手柄继续接收自动化方向。启动前要求所选实体 XInput 槽位已连接；虚拟手柄槽位未知、重复或与实体槽位冲突时退出。运行中持续检查虚拟槽位，发生变化或实体手柄断连时让所有手柄回中并退出，重新检查身份与隔离后才能重启。`--physical-hidden-confirmed` 是操作者确认游戏无法直接读取实体手柄，本项目尚未实现 HidHide 配置或核验。XInput 槽位本身不能证明物理设备身份，其他实体设备占据原槽位的风险仍待解决，因此此模式尚未在 Windows 或游戏中验收。结构布局按 Microsoft 的 [XINPUT_STATE](https://learn.microsoft.com/en-us/windows/win32/api/xinput/ns-xinput-xinput_state) 与 [XINPUT_GAMEPAD](https://learn.microsoft.com/en-us/windows/win32/api/xinput/ns-xinput-xinput_gamepad) 定义，并在 macOS 检查结构大小。

已生成的 Windows 文件位于 `release/`。手柄桥接需要明确传入 `--enable-controller --slots 2`，并从标准输入持续接收 JSON 命令。桥接已交叉编译，协议解析检查已在 macOS 运行，**尚未在 Windows 或 PoE2 中运行验证**；用户当前没有可用的虚拟机。

`sensor/` 是基于公开 ExileCore2 接口的只读诊断插件。它默认关闭，启用后以 10Hz 向本机 `127.0.0.1:47617` 发送进程 ID、时间、区域名称、区域哈希和玩家三维坐标。`release/PartyPilot-SensorLog.exe` 可接收并显示这些数据。区域名称不是实例 ID；区域哈希能否唯一标识两客户端的同一实例也**待游戏实测**，因此采集数据目前不会进入自动跟随控制。插件已经针对当前 ExileCore2 程序集编译，接收程序已用模拟数据验证；真实游戏数据仍待测试。编译插件需设置 `EXILECORE2_DIR` 为 ExileCore2 安装目录，本项目不分发它的二进制文件。

`release/PartyPilot-PartyMonitor.exe` 可用 `-leader-pid`、`-follower-pid` 和 `-max-age` 监视两个进程的只读位置流。它区分数据缺失、过期、区域不一致及“同名同哈希候选”；候选**不等于已证实处于同一实例**，因此监视器不输出任何手柄命令。模拟双进程 UDP 数据已验证这些状态切换。

`release/PartyPilot-RosterWatch.exe` 使用 `-roster 1:进程ID,2:进程ID,... -leader 1 -max-age 750ms` 对 1–6 个明确配置的游戏进程做只读监视。它按角色槽位输出队长是否缺失/过期，以及每个队员的缺失、过期、区域不一致或同区域候选状态；拒绝重复槽位、重复进程 ID 和乱序旧样本。该程序已用本机模拟 UDP 报文验证三角色状态变化，仍未接到真实 PoE2。区域名称与哈希相等仍只是候选，不会进入自动控制。

`release/PartyPilot-AreaLogWatch.exe` 通过重复的 `-log 槽位=Client.txt路径` 参数只读监视 2–6 份明确指定、互不相同的客户端日志。例如 `-log "1=C:\\PoE2-A\\logs\\Client.txt" -log "2=C:\\PoE2-B\\logs\\Client.txt" -leader 1 -max-age 30s`。也可用 `-exe 槽位=游戏程序路径`，从该程序所在目录推导 `logs/Client.txt`；每个槽位只能指定一种来源。默认读取每份日志末尾最多 512 KiB 的完整历史行，历史吻合仅显示 `historical_candidate`；用 `-prime-history=false` 可跳过历史。随后只接收新写入的完整 `Generating level … area "…" with seed …` 行；两端区域 ID 与 seed 相同且都未过期时输出 `candidate_same_instance`，不同则输出 `different_area`。日志缺失、共用同一文件或文件读取错误会退出。`[SCENE]` 名称不足以区分实例，因此不参与这一匹配。当前仅用合成日志验证了行为，尚无真实 `Client.txt` 样本，也未核实每个日志文件与游戏进程、角色槽位的对应关系。候选结果不会进入自动控制。

`PartyReplay` 读取受控数据中的明确实例标识，计算各队员命令。显式启用 `-last-focused-leads` 后，手动聚焦的角色成为跟随目标，原 P1 会转为自动跟随；返回 P1 焦点时恢复原队长。未启用该选项时仍按固定队长计算。`-follow-targets 2:3` 指定 P2 跟随 P3；未指定的队员跟随当前 MAIN。`BridgeFeed -slot 2` 选择一个队员，`BridgeFeed -all` 输出所有队员命令及焦点切换事件。手动接管或等待会松开相应自动化摇杆。上述管道已在 macOS 用模拟数据贯通到 C# 桥接协议解析器。Windows 上可以另传 `-pace 100ms`，再把 `BridgeFeed -all` 的输出接到 `PartyPilot.Bridge.exe --enable-controller --slots 1,2 --physical-index 0 --main-slot 1 --physical-hidden-confirmed` 做受控手柄测试；**这条 Windows/游戏输入管道尚未运行验证**。真实传感器尚不能提供经验证的实例标识，所以当前的只读监视器不会自动连接这条控制管道。

状态：**implementation-aligned / integration-pending**。
