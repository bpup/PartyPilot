# 自动拾取取证与实现边界

## 事实：所提供 0.6.17 的静态实现

已提取程序集的 `Poe2Coop.Services/ActiveLootCard.cs` 记录选中物品指针、基础物品 ID、名称、是否可作为目标、距离、接收时间、帧序号和玩家编号。`AutoPickupRouting.cs` 依玩家编号挑选卡片，并在菜单、个人背包或背包自动化期间阻止拾取。`ShellViewModel.cs` 的拾取链路还检查物品目录与分配、背包容量、角色生命状态，并要求卡片足够新鲜；其优先拾取链路另将卡片与可见掉落目标及实体身份精确对应。`AutoLootGate.cs` 在满足其输入安全标记后按 80 毫秒脉冲、180 毫秒重试间隔决定发送；这些数字是静态代码值，未在游戏中测得实际效果。原版内存探针通过读取当前物品卡片得到这些信息，并按本地玩家槽位分组。

## 独立版当前证据

传感器的 `RadarEntities` 只给出当前可见世界掉落的实体 ID、路径、位置以及可读到的名称、堆叠数和稀有度。它没有选中卡片、分配归属、可拾取状态、背包容量、菜单状态或可靠的本地 P1/P2 映射，不能据此发送拾取按键。

默认关闭的 `DiagnoseRadarEntities` 下有 `GroundHover` 只读字段。它取自 ExileCore2 的 `ItemsOnGroundLabelElement.ItemOnHover`，最多附带该实体对应的内部物品路径和名称；`GroundHoverStatus` 分别报告未观察到、已观察到或读取失败。接收端仅接受 `Evidence=ui_hover_only`，不会把该字段接入手柄命令。鼠标悬停与手柄当前物品卡片之间的对应关系**待 Windows/PoE2 实测**；即使两者偶尔指向同一物品，也不能由此推断拾取许可。

新增 `internal/autoloot` 独立状态机与 `PartyPilot-LootReplay.exe -replay <JSONL 文件>` 离线回放程序。输入必须明确给出当前卡片、帧序号、玩家、卡片时间，以及目录认可、容量、背包限制、菜单、个人背包、背包自动化和生存条件。卡片超过 350 毫秒、属于其他本地玩家、不可作为目标或任一条件不满足时不会产生拾取请求。有效卡片首次触发 80 毫秒请求；同一物品的新帧至少间隔 180 毫秒才重试，卡片消失或安全条件关闭立即取消当前请求。这些是离线决策输出，**没有接入实时传感器或虚拟手柄**。回放输入由操作者提供，不能证明任何字段已在游戏里被读取。

回放示例单行（`pulse_active` 仅是输出，不会按键）：

```json
{"now":"2026-10-26T00:00:00Z","player":1,"frame_sequence":1,"card":{"selected_item":"0xABC","base_id":"CurrencyExalted","player":1,"targetable":true,"received_at":"2026-10-26T00:00:00Z"},"conditions":{"catalog_approved":true,"capacity_allows":true,"inventory_limit_allows":true,"menu_clear":true,"own_inventory_closed":true,"inventory_automation_free":true,"alive":true}}
```

## 待确认与后续实现

1. 在真实 PoE2 中核对 ExileCore2 悬停字段的实体类型、更新频率和与目标版手柄卡片的关系。
2. 找到可核验的、按游戏进程和本地玩家槽位分离的选中卡片来源，并与区域实例、掉落实体身份关联。
3. 取得菜单、背包、分配归属、容量和生命状态的实时来源，逐项验证后才能接入拾取决策与输入桥接。

当前状态：**只读悬停诊断和离线拾取决策可编译，自动拾取输入未实现，游戏集成待验证**。
