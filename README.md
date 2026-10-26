# PartyPilot

独立开发的 PoE2 多角色跟随软件工作区。当前交付是 **跟随决策内核与回放工具**，尚不能控制游戏。它与 `bpup/poe2-scripts` 没有代码或构建依赖。

## 当前可运行功能

- 读取逐帧 JSONL 位置数据，输出等待、停靠、跟随和追赶命令。
- 丢失坐标、区域实例不一致、数据过期或坐标无效时停止输出移动方向。
- 停靠与恢复使用不同距离，减少边界附近的反复启停。

运行：

```sh
go test ./...
go run ./cmd/partypilot -replay testdata/example.jsonl -stop 2 -resume 3 -catchup 10 -max-age 750ms
```

在 macOS 上交叉编译 Windows 程序：

```sh
GOOS=windows GOARCH=amd64 go build -o PartyPilot.exe ./cmd/partypilot
```

## 功能参照与证据

用户指定的 MultiPilot 0.6.17 安装目录位于 `/Users/liuchang/Desktop/project/MultiPilotPoE2/current`。其随附快速指南描述了 1–6 人、最多三个游戏窗口、虚拟手柄、跟随、Smart Focus 等功能；这些是参照清单，不能证明本项目已实现。

当前内核只根据两个角色的已知坐标计算方向。示例中的距离数字仅用于回放测试，不代表 0.6.17 的参数。它没有读取 PoE2 内存、识别窗口、自动组队、战斗、拾取、药剂或 UI。坐标 `Area` 必须是实例标识；仅有地图名不足以证明两个角色位于同一实例。

`bridge/PartyPilot.Bridge.csproj` 是另一个可执行程序：它使用公开的 ViGEm.NET API 创建一个虚拟 Xbox 360 手柄，从标准输入读取内核输出的 JSON 命令，将方向写入左摇杆；命令停止超过 500 毫秒会松开摇杆。它不会将手柄绑定到指定 PoE2 窗口，也没有验证游戏如何映射世界坐标到摇杆轴，因此目前只能用于受控桥接测试。运行需要 Windows 与已安装的 ViGEmBus。项目没有分发驱动；[ViGEm 项目已停止维护](https://docs.nefarius.at/projects/ViGEm/End-of-Life/)。

已生成的 Windows 文件位于 `release/PartyPilot-Replay.exe` 和 `release/bridge/PartyPilot.Bridge.exe`。后者需要明确传入 `--enable-controller`，并从标准输入持续接收 JSON 命令。两个程序已经交叉编译，**尚未在 Windows 或 PoE2 中运行验证**；用户当前没有可用的虚拟机。

`sensor/` 是基于公开 ExileCore2 接口的只读诊断插件。它默认关闭，启用后以 10Hz 向本机 `127.0.0.1:47617` 发送进程 ID、时间、区域名称和玩家三维坐标。`release/PartyPilot-SensorLog.exe` 可接收并显示这些数据。区域名称不是实例 ID，因此采集数据目前**不会进入自动跟随控制**。插件已经针对当前 ExileCore2 程序集编译，接收程序已用模拟数据验证；真实游戏数据仍待测试。编译插件需设置 `EXILECORE2_DIR` 为 ExileCore2 安装目录，本项目不分发它的二进制文件。

状态：**implementation-aligned / integration-pending**。
