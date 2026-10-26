# 地形与绕行取证

## 已确认

- 所提供 0.6.17 版本的反编译结果中，`Poe2Coop.Services/NavigationTerrain.cs` 从 `terrain` JSON 读取 `width`、`height`、`stride` 和逐行的 `data`，其中字符串字符为 `1` 或 `0`。`SmartFollowEngine.cs` 使用地形作视线检查、路线规划和障碍恢复。这是安装包的静态证据，并非游戏内运行结果。
- 本地 ExileCore2 参考程序集暴露 `IngameData.RawPathfindingData`，类型为 `int[][]`，以及 `Terrain`，类型为 `GameOffsets2.TerrainData`。`TerrainData` 有 `NumCols`、`NumRows`、`BytesPerRow` 等字段。仅凭字段名称和类型，不能确定 `int[][]` 的行列方向、数值含义、层级或与角色坐标的对应关系。
- PartyPilot 现有 `internal/navigation` 是受控回放网格上的离线路由器。实时传感器此前没有地形输出，实时跟随也未调用这个路由器。

## 本次只读诊断

在每个 ExileCore2 实例中启用传感器后，可单独开启默认关闭的 `DiagnoseTerrain`。传感器最多每五秒读取一次 `RawPathfindingData`，只报告行数、最短和最长行、最多 65,536 个单元的原始整数频数；频数表至多保留 32 种值，并标明是否截断。`PartyPilot-SensorLog.exe` 的本机诊断流可查看 `TerrainSummary`、`TerrainStatus` 和 `TerrainSeenUtc`。这一诊断不生成可通行网格，也不进入跟随输入。

## 待游戏实测

1. 分别在开阔地、明确障碍两侧、换图后采样，确认原始整数与实际通行性及区域变化的关系。
2. 用玩家已观察到的世界坐标与网格坐标核对行列方向、原点、步长、层级和地图实例身份。
3. 只有映射成立后，才把地形转为导航网格并验证路径、视线和失败时回中；不能把频数分布直接当作目标版 `1`/`0` 地形。

目前没有 Windows 虚拟机或 PoE2 游戏运行环境，因此上述映射与实时绕行仍为待确认。
