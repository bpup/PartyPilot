# 同窗口双角色位置与身份核对

## 事实：所提供 0.6.17 的静态实现

`memory-probe/MultiPilotMemoryProbe.dll` 中的 `PlayerSlotVectorResolver` 从目标进程的本地玩家向量取得有序实体指针，并核对指针、角色实体路径及完整的 1–2 槽位序列。`Poe2Coop.Services/MemoryReaderService.cs` 只在 `playerOrderReliable` 成立时发布用于控制的有序玩家位置；单纯枚举到两个玩家实体或看见角色名字，不等于确认了 P1/P2 本地槽位。

## 独立版目前可采集的证据

可见实体诊断现还读取各实体公开 `Life` 组件中的当前/最大生命、魔力和能量护盾；角色名暂时缺失但地址及角色路径明确的实体也会保留。接收端要求每组资源当前值和最大值成对出现且为非负有限数。这些资源仍属于“可见实体”，不是经本地槽位验证的 P2 状态；不能直接触发同窗口药剂或跟随。

`PartyPilot.Sensor` 已有 `GameController.Player` 当前角色位置。新增默认关闭的 `DiagnoseVisiblePlayers` 只读选项后，传感器还可报告最多 16 个带 `Player` 组件、实体 ID、名字和位置的可见实体，并标明哪个实体 ID 与 `GameController.Player` 相同。诊断扫描失败会报告 `scan_failed`，不会中断主角色位置发送。`PartyPilot-SensorLog` 能显示该字段；自动跟随决策不会把这些可见实体当作已配对的第二角色。

该选项现在还输出 ExileCore2 公开的 `IngameState.Data.Address`、`GameController.Player.Address`，以及各可见玩家实体的 `Address`/`Path`。地址以十六进制字符串传输，避免 64 位整数在其他 JSON 工具中丢失精度；接收端会拒绝格式错误或与已标记当前角色不一致的地址。地址只用于同窗口实机取证，未被解释为本地槽位，未进入自动跟随决策。当前没有证据证明 ExileCore2 的 `IngameState.Data.Address` 与目标探针的 `areaInstance` 是同一基址。

另一个默认关闭的 `DiagnoseLocalSlotCandidates` 选项每秒只读扫描一次：以 ExileCore2 的上述数据地址为实验基址，在所提供版本静态公开默认结构偏移 1408/2552、向量偏移 72 的 ±128 字节内寻找 1–2 项指针向量。它只接纳指向已观察到的角色实体、包含当前角色且顺序无冲突的候选；结果明确标为 `single_slot_candidate` / `two_slot_candidate`，而不是可靠 P1/P2。偏移来自静态默认值，目标运行时配置可能不同；实验基址与目标 `areaInstance` 的关系也未经实机证明。合成内存测试覆盖有序双角色、冲突向量、未观察实体、非角色路径及过长向量。该诊断不会改变跟随或手柄输入。

进一步反查目标 `NoddPilot.Offsets.Reading.AreaEntityListReader.TryResolveAreaInstance`，可见其先在游戏状态对象加上 `InGameStateAreaInstanceOffset` 读取指针；该偏移的公开静态默认值是 656。诊断现在也从 ExileCore2 的 `IngameState.Address + 656` 只读取得第二个实验区域基址，与 `IngameState.Data.Address` 分别扫描、去重并比较。输出包含状态指针解析出的区域地址、两种基址是否相等、实际产生候选的区域地址；两种基址若给出相互冲突的角色顺序，结果为 `conflicting_candidates`。目标运行时可能启用了不同偏移，当前没有 PoE2 实机证据证明两种基址任何一个等于目标的真实 `areaInstance`；即便候选相同，也不会自动绑定 P2。

原始物品卡片诊断在两项开关同时开启时，可把 owner 与上面的**新鲜有序候选**精确比对，并仅输出 `unverified_vector_order_candidate` 的本地 1/2 槽位编号。还要求 owner 同时等于唯一可见玩家实体地址；关闭候选扫描、状态地址变化、候选冲突或地址不符时不输出。所提供版本的 `PlayerSlotVectorResolver` 会从区域实体列表逐一读取角色路径，并记录向量头地址供后续重验；独立版现在也记录所选候选向量头，物品卡片关联前重读向量头、顺序和角色路径，变化时丢弃缓存候选。这里仍使用 ExileCore2 当前可见实体及静态默认的原始路径读取，尚未取得等价的完整区域实体列表，故候选不能升级为可靠槽位或驱动输入。

静态反编译所提供的 `NoddPilot.Offsets.dll` 可见：`MonsterEntityReader.TryReadWorldPosition` 先按名称解析实体的 `Render` 组件，再从该组件读取三个 float；`EntityMapOffsets.Default` 的位置偏移为 312，世界坐标除以 10.869565 得到网格坐标。独立版现仅在已找到 1–2 个有序角色候选时，按这套公开默认偏移尝试只读组件链，并在读完后重验槽位向量；输出 `RawPositions`、`RawPositionsSeenUtc` 与 `RenderComponentAddress` 作为最多缓存一秒的候选诊断。合成内存检查覆盖组件归属与无效坐标。目标版可加载不同的运行时偏移；目前没有实机数据证明这些地址或坐标正确，也没有证据证明它们与 ExileCore2 的当前角色坐标语义一致。`RawPositions` 不参与实时跟随、角色绑定或手柄输入。

ExileCore2 参考程序集的 `Render.Pos` 暴露三维向量，但反编译代码不足以确认它与上面原始“世界/网格”值的单位关系。诊断现对**同一已观察本地实体**同时计算 `Render.Pos` XY 到原始世界 XY、网格 XY 的欧氏距离，输出 `ObservedLocalPositionComparison`；不设置“匹配”阈值，也不从较小的距离自动选择坐标系。实机采样时可连同 `RawPositionsSeenUtc` 观察两种距离的分布，再决定是否存在可用的坐标对应关系。

同一次候选扫描现在还按实体地址将每个有序候选与 ExileCore2 当前可见的 `Player` 实体对照，输出等长的 `VisiblePositionComparisons`。不可见或原始位置不可读的槽位为 `null`；有值时分别记录公开坐标到原始世界/网格坐标的 XY 距离。接收端校验数组长度和距离计算。此项用于实机时逐槽检查坐标一致性，不能单凭“两个值接近”证明第二个实体属于本地双人槽位。

实时引擎现增加显式的“角色→客户端→本地槽位→进程”绑定与完整有序双角色帧入口。双槽帧必须声明可靠顺序来源、同时包含本地 1/2 的世界坐标及生命当前/最大值，并通过时间顺序校验；当前帧若失去顺序或缺少角色，会撤销该进程所有角色的旧位置，旧的无效帧不能撤销更新的数据。合成测试已让一个进程两角色、两个进程四角色及三个进程六角色分别产出跟随决策。现有传感器的 `LocalSlotCandidates` 仍只读且不生成该可靠帧；桌面实时入口保持仅每客户端一名角色。因此这只是多角色实时数据的接收内核，仍需 Windows/PoE2 证明源数据和完整链路。

## 待确认

- Windows/PoE2 实机上，同一窗口的两个本地角色是否都出现在 ExileCore2 可见实体集合中，以及名字、实体 ID 和位置是否稳定。
- 是否存在独立、可验证的 P2 本地槽位来源；在此之前不能把其他可见玩家按列表顺序或名字直接绑定 P2，也不能启用同窗口 P2 自动跟随。
- 在 Windows/PoE2 实机上对比上述地址与本地角色向量的基址、槽位及重载变化；静态程序集仅证明属性可读取，不证明两者语义相同。
- 当前编译和合成消息检查只能证明诊断链路结构有效，不能证明 ExileCore2 已在游戏中加载或实际返回了两个角色。
