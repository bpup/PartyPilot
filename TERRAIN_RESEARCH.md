# 地形与绕行取证

## 已确认

- 所提供 0.6.17 版本的反编译结果中，`Poe2Coop.Services/NavigationTerrain.cs` 从 `terrain` JSON 读取 `width`、`height`、`stride` 和逐行的 `data`，其中字符串字符为 `1` 或 `0`。`SmartFollowEngine.cs` 使用地形作视线检查、路线规划和障碍恢复。这是安装包的静态证据，并非游戏内运行结果。
- 安装包 `memory-probe/NoddPilot.Offsets.dll` 中的 `NoddPilot.Offsets.Reading.TerrainReader` 将每字节的低四位解为左格、高四位解为右格；调用处 `ProbeSession.cs` 把解码值大于零的格子写为 `1`，其余写为 `0`。读取器还用总 tile 数、行字节数、缓存长度、玩家格坐标和四位值分布筛选候选内存区域。这些是目标版的静态行为，尚未证明独立传感器可找到同一块实时内存。
- 本地 ExileCore2 参考程序集暴露 `IngameData.RawPathfindingData`，类型为 `int[][]`，以及 `Terrain`，类型为 `GameOffsets2.TerrainData`。`TerrainData` 有 `NumCols`、`NumRows`、`BytesPerRow` 等字段。仅凭字段名称和类型，不能确定 `int[][]` 的行列方向、数值含义、层级或与角色坐标的对应关系。
- PartyPilot 现有 `internal/navigation` 是受控回放网格上的离线路由器。实时传感器此前没有地形输出，实时跟随也未调用这个路由器。
- 独立的 `GridFromPacked` 现可在尺寸和坐标参数明确时，把目标版格式的压缩字节转换为受控回放网格；`cmd/partypilot` 的 `packed_terrain` 回放字段接受这组数据。输入不完整、尺寸不一致、同帧同时提供两种地形时直接报错。它没有接到实时传感器。

## 本次只读诊断

在每个 ExileCore2 实例中启用传感器后，可单独开启默认关闭的 `DiagnoseTerrain`。传感器最多每五秒读取一次 `RawPathfindingData`，只报告行数、最短和最长行、最多 65,536 个单元的原始整数频数；频数表至多保留 32 种值，并标明是否截断。`PartyPilot-SensorLog.exe` 的本机诊断流可查看 `TerrainSummary`、`TerrainStatus` 和 `TerrainSeenUtc`。这一诊断不生成可通行网格，也不进入跟随输入。

## 待游戏实测

1. 分别在开阔地、明确障碍两侧、换图后采样，确认原始整数与实际通行性及区域变化的关系。
2. 用玩家已观察到的世界坐标与网格坐标核对行列方向、原点、步长、层级和地图实例身份。
3. 只有映射成立后，才把地形转为导航网格并验证路径、视线和失败时回中；不能把频数分布直接当作目标版 `1`/`0` 地形。

受控回放可提供 `packed_terrain`：`area`、`origin_x`、`origin_y`、`cell_size`、`bytes_per_row`、`rows` 和 base64 编码的 `data`。解码后的宽度为 `bytes_per_row * 2`，当前路由器最多接受 256×256 格。调用者必须先确认实例、原点和格子尺寸；示例或合成数据不能据此证明游戏内坐标映射。

目前没有 Windows 虚拟机或 PoE2 游戏运行环境，因此上述映射与实时绕行仍为待确认。
