# 同窗口双角色位置与身份核对

## 事实：所提供 0.6.17 的静态实现

`memory-probe/MultiPilotMemoryProbe.dll` 中的 `PlayerSlotVectorResolver` 从目标进程的本地玩家向量取得有序实体指针，并核对指针、角色实体路径及完整的 1–2 槽位序列。`Poe2Coop.Services/MemoryReaderService.cs` 只在 `playerOrderReliable` 成立时发布用于控制的有序玩家位置；单纯枚举到两个玩家实体或看见角色名字，不等于确认了 P1/P2 本地槽位。

## 独立版目前可采集的证据

`PartyPilot.Sensor` 已有 `GameController.Player` 当前角色位置。新增默认关闭的 `DiagnoseVisiblePlayers` 只读选项后，传感器还可报告最多 16 个带 `Player` 组件、实体 ID、名字和位置的可见实体，并标明哪个实体 ID 与 `GameController.Player` 相同。诊断扫描失败会报告 `scan_failed`，不会中断主角色位置发送。`PartyPilot-SensorLog` 能显示该字段；自动跟随决策不会把这些可见实体当作已配对的第二角色。

该选项现在还输出 ExileCore2 公开的 `IngameState.Data.Address`、`GameController.Player.Address`，以及各可见玩家实体的 `Address`/`Path`。地址以十六进制字符串传输，避免 64 位整数在其他 JSON 工具中丢失精度；接收端会拒绝格式错误或与已标记当前角色不一致的地址。地址只用于同窗口实机取证，未被解释为本地槽位，未进入自动跟随决策。当前没有证据证明 ExileCore2 的 `IngameState.Data.Address` 与目标探针的 `areaInstance` 是同一基址。

另一个默认关闭的 `DiagnoseLocalSlotCandidates` 选项每秒只读扫描一次：以 ExileCore2 的上述数据地址为实验基址，在所提供版本静态公开默认结构偏移 1408/2552、向量偏移 72 的 ±128 字节内寻找 1–2 项指针向量。它只接纳指向已观察到的角色实体、包含当前角色且顺序无冲突的候选；结果明确标为 `single_slot_candidate` / `two_slot_candidate`，而不是可靠 P1/P2。偏移来自静态默认值，目标运行时配置可能不同；实验基址与目标 `areaInstance` 的关系也未经实机证明。合成内存测试覆盖有序双角色、冲突向量、未观察实体、非角色路径及过长向量。该诊断不会改变跟随或手柄输入。

进一步反查目标 `NoddPilot.Offsets.Reading.AreaEntityListReader.TryResolveAreaInstance`，可见其先在游戏状态对象加上 `InGameStateAreaInstanceOffset` 读取指针；该偏移的公开静态默认值是 656。诊断现在也从 ExileCore2 的 `IngameState.Address + 656` 只读取得第二个实验区域基址，与 `IngameState.Data.Address` 分别扫描、去重并比较。输出包含状态指针解析出的区域地址、两种基址是否相等、实际产生候选的区域地址；两种基址若给出相互冲突的角色顺序，结果为 `conflicting_candidates`。目标运行时可能启用了不同偏移，当前没有 PoE2 实机证据证明两种基址任何一个等于目标的真实 `areaInstance`；即便候选相同，也不会自动绑定 P2。

原始物品卡片诊断在两项开关同时开启时，可把 owner 与上面的**新鲜有序候选**精确比对，并仅输出 `unverified_vector_order_candidate` 的本地 1/2 槽位编号。还要求 owner 同时等于唯一可见玩家实体地址；关闭候选扫描、状态地址变化、候选冲突或地址不符时不输出。所提供版本的 `PlayerSlotVectorResolver` 会从区域实体列表逐一读取角色路径，并记录向量头地址供后续重验；独立版现在也记录所选候选向量头，物品卡片关联前重读向量头、顺序和角色路径，变化时丢弃缓存候选。这里仍使用 ExileCore2 当前可见实体及静态默认的原始路径读取，尚未取得等价的完整区域实体列表，故候选不能升级为可靠槽位或驱动输入。

## 待确认

- Windows/PoE2 实机上，同一窗口的两个本地角色是否都出现在 ExileCore2 可见实体集合中，以及名字、实体 ID 和位置是否稳定。
- 是否存在独立、可验证的 P2 本地槽位来源；在此之前不能把其他可见玩家按列表顺序或名字直接绑定 P2，也不能启用同窗口 P2 自动跟随。
- 在 Windows/PoE2 实机上对比上述地址与本地角色向量的基址、槽位及重载变化；静态程序集仅证明属性可读取，不证明两者语义相同。
- 当前编译和合成消息检查只能证明诊断链路结构有效，不能证明 ExileCore2 已在游戏中加载或实际返回了两个角色。
