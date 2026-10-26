# 地形与绕行取证

## 已确认

- 所提供 0.6.17 版本的反编译结果中，`Poe2Coop.Services/NavigationTerrain.cs` 从 `terrain` JSON 读取 `width`、`height`、`stride` 和逐行的 `data`，其中字符串字符为 `1` 或 `0`。`SmartFollowEngine.cs` 使用地形作视线检查、路线规划、路线推进和障碍恢复；其 `AdvanceRoute` 会跳过已到达或当前可直达的路点，`HasClearFollowSegment` 允许受限的起点脱困段。这是安装包的静态证据，并非游戏内运行结果。
- 安装包 `memory-probe/NoddPilot.Offsets.dll` 中的 `NoddPilot.Offsets.Reading.TerrainReader` 将每字节的低四位解为左格、高四位解为右格；调用处 `ProbeSession.cs` 把解码值大于零的格子写为 `1`，其余写为 `0`。读取器还用总 tile 数、行字节数、缓存长度、玩家格坐标和四位值分布筛选候选内存区域。这些是目标版的静态行为，尚未证明独立传感器可找到同一块实时内存。
- 本地 ExileCore2 参考程序集暴露 `IngameData.RawPathfindingData`，类型为 `int[][]`，以及 `Terrain`，类型为 `GameOffsets2.TerrainData`。`TerrainData` 有 `NumCols`、`NumRows`、`BytesPerRow` 等字段。仅凭字段名称和类型，不能确定 `int[][]` 的行列方向、数值含义、层级或与角色坐标的对应关系。
- PartyPilot 现有 `internal/navigation` 是受控回放网格上的离线路由器。实时传感器此前没有地形输出，实时跟随也未调用这个路由器。
- 独立的 `GridFromPacked` 现可在尺寸和坐标参数明确时，把目标版格式的压缩字节转换为受控回放网格；`cmd/partypilot` 的 `packed_terrain` 回放字段接受这组数据。该网格采用第一个格子中心作为原点，并走八邻格、一格净空及视线简化的寻路分支；人工 `terrain` 网格保留原四邻格行为。输入不完整、尺寸不一致、同帧同时提供两种地形时直接报错。压缩地图仍没有接到实时传感器或实时跟随。

## 本次只读诊断

在每个 ExileCore2 实例中启用传感器后，可单独开启默认关闭的 `DiagnoseTerrain`。传感器最多每五秒读取一次 `RawPathfindingData`，只报告行数、最短和最长行、最多 65,536 个单元的原始整数频数；频数表至多保留 32 种值，并标明是否截断。

若压缩地形扫描只找到一个候选，传感器还会在 `%LOCALAPPDATA%\PartyPilot\terrain` 保存一次完整的 `.packed` 原始字节和同名 `.json` 元数据。JSON 记录候选宽高、每行字节数、区域哈希和地址、读取时间、原始 Render 格坐标、传感器世界坐标、局部小块及 SHA-256。文件不是已确认的游戏地形：候选布局、区域实例与世界坐标映射仍须在 Windows 游戏中核对。短读、首尾块复读不一致，或完整文件中的小块与先前观察不一致时，均删除临时文件；这只能发现部分采集期变化。相同进程、区域哈希和候选地址在本次传感器运行中不重复采集。完整采集可能一次读取最多 64 MiB，建议只在诊断时短暂启用。`PackedTerrainCaptureStatus` 回报 `captured_local`、`already_captured` 或失败原因；本机文件不会经 UDP 发送。

同一开关现在还从当前 `IngameState` 的静态区域指针候选、`IngameData.Address` 和本地角色原始 Render 格坐标做只读扫描。候选需通过 tile 尺寸、缓存长度、坐标范围及压缩四位值分布检查；若仅有一个候选，最多回报玩家周围 9×9 格的 `1`/`0` 小块及其左上角、地图宽高、区域地址来源、采样数。多个布局或两个区域地址均成立时报告歧义，任何地址或小块读取失败都不输出地图。`PartyPilot-SensorLog.exe` 的本机诊断流可查看 `TerrainSummary`、`TerrainStatus`、`PackedTerrainProbe`、`PackedTerrainStatus`、`PackedTerrainCaptureStatus` 和 `TerrainSeenUtc`。目标版静态偏移不等于当前游戏内验证；小块也不进入跟随输入。

## 待游戏实测

1. 分别在开阔地、明确障碍两侧、换图后采样，确认原始整数与实际通行性及区域变化的关系。
2. 对照 `PackedTerrainProbe` 的玩家附近小块和游戏可见障碍，核对候选区域、行列方向及 `0`/非零的含义；再用世界坐标与格坐标核对原点、步长、层级和地图实例身份。
3. 只有映射成立后，才把地形转为导航网格并验证路径、视线和失败时回中；不能把频数分布直接当作目标版 `1`/`0` 地形。

受控回放可提供 `packed_terrain`：`area`、`origin_x`、`origin_y`、`cell_size`，以及三种互斥的地形来源：`bytes_per_row`、`rows` 加 base64 `data`；相同尺寸加原始字节 `data_file`；或单独指定 `capture_metadata_file`。最后一种读取诊断目录里的 `.json` 和同名 `.packed`，校验大小、SHA-256 与局部小块后取出尺寸。文件路径相对运行目录。宽度为 `bytes_per_row * 2`；压缩缓存上限 64 MiB，宽高按安装包读取器的静态范围限制为最多 100,000 格。回放器不展开整张阻挡列表，寻路只缓存搜索触及的格子；单次搜索仍受 60,000 次队列取出上限约束。重复引用同一个 `data_file` 时，回放器按该路径缓存文件内容，因此换图应使用新路径。`origin_x`/`origin_y` 是左上第一个格子的中心。目标轨迹模式把队长周围五格作为搜索和视线禁入区域，复用仍可用的路线并跳过已到达或可直达的路点；起点贴近障碍时只允许目标版规则内的短脱困段。避让路径不可达时按目标版 `Obstacle recovery` 绕人规则输出标记方向。该方向未用真实地形或游戏输入验证。调用者必须先确认实例、原点和格子尺寸；示例或合成数据不能据此证明游戏内坐标映射。目标版异步重规划仍未接入独立版。

目前没有 Windows 虚拟机或 PoE2 游戏运行环境，因此上述映射与实时绕行仍为待确认。
