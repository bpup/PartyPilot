# 同窗口双角色位置与身份核对

## 事实：所提供 0.6.17 的静态实现

`memory-probe/MultiPilotMemoryProbe.dll` 中的 `PlayerSlotVectorResolver` 从目标进程的本地玩家向量取得有序实体指针，并核对指针、角色实体路径及完整的 1–2 槽位序列。`Poe2Coop.Services/MemoryReaderService.cs` 只在 `playerOrderReliable` 成立时发布用于控制的有序玩家位置；单纯枚举到两个玩家实体或看见角色名字，不等于确认了 P1/P2 本地槽位。

## 独立版目前可采集的证据

`PartyPilot.Sensor` 已有 `GameController.Player` 当前角色位置。新增默认关闭的 `DiagnoseVisiblePlayers` 只读选项后，传感器还可报告最多 16 个带 `Player` 组件、实体 ID、名字和位置的可见实体，并标明哪个实体 ID 与 `GameController.Player` 相同。诊断扫描失败会报告 `scan_failed`，不会中断主角色位置发送。`PartyPilot-SensorLog` 能显示该字段；自动跟随决策不会把这些可见实体当作已配对的第二角色。

该选项现在还输出 ExileCore2 公开的 `IngameState.Data.Address`、`GameController.Player.Address`，以及各可见玩家实体的 `Address`/`Path`。地址以十六进制字符串传输，避免 64 位整数在其他 JSON 工具中丢失精度；接收端会拒绝格式错误或与已标记当前角色不一致的地址。地址只用于同窗口实机取证，未被解释为本地槽位，未进入自动跟随决策。当前没有证据证明 ExileCore2 的 `IngameState.Data.Address` 与目标探针的 `areaInstance` 是同一基址。

## 待确认

- Windows/PoE2 实机上，同一窗口的两个本地角色是否都出现在 ExileCore2 可见实体集合中，以及名字、实体 ID 和位置是否稳定。
- 是否存在独立、可验证的 P2 本地槽位来源；在此之前不能把其他可见玩家按列表顺序或名字直接绑定 P2，也不能启用同窗口 P2 自动跟随。
- 在 Windows/PoE2 实机上对比上述地址与本地角色向量的基址、槽位及重载变化；静态程序集仅证明属性可读取，不证明两者语义相同。
- 当前编译和合成消息检查只能证明诊断链路结构有效，不能证明 ExileCore2 已在游戏中加载或实际返回了两个角色。
