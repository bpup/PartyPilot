# 自动拾取取证与实现边界

## 事实：所提供 0.6.17 的静态实现

已提取程序集的 `Poe2Coop.Services/ActiveLootCard.cs` 记录选中物品指针、基础物品 ID、名称、是否可作为目标、距离、接收时间、帧序号和玩家编号。`AutoPickupRouting.cs` 依玩家编号挑选卡片，并在菜单、个人背包或背包自动化期间阻止拾取。`ShellViewModel.cs` 的拾取链路还检查物品目录与分配、背包容量、角色生命状态，并要求卡片足够新鲜；其优先拾取链路另将卡片与可见掉落目标及实体身份精确对应。`AutoLootGate.cs` 在满足其输入安全标记后按 80 毫秒脉冲、180 毫秒重试间隔决定发送；这些数字是静态代码值，未在游戏中测得实际效果。原版内存探针通过读取当前物品卡片得到这些信息，并按本地玩家槽位分组。

## 独立版当前证据

传感器的 `RadarEntities` 只给出当前可见世界掉落的实体 ID、路径、位置以及可读到的名称、堆叠数和稀有度。它没有选中卡片、分配归属、可拾取状态、背包容量、菜单状态或可靠的本地 P1/P2 映射，不能据此发送拾取按键。

新增默认关闭的 `DiagnoseItemCards`：按所提供版本静态代码确认的构造签名查找游戏模块中的物品卡片类型，再只读遍历当前可见 UI 树，报告每个附着卡片的对象、物品和 owner 指针，统一标记 `unassigned_ui_card`。缺少唯一类型签名、UI 根节点、可见父子关系或稳定的卡片内容时，不报告卡片。合成内存测试覆盖隐藏、脱离、回收、越界和读取过程中变化的节点；**没有用户当前 PoE2 游戏程序或 Windows 运行环境，类型签名和 UI 树布局尚未实机验证**。同进程精确指针关联仅在 owner 与唯一可见玩家地址相等、item 与唯一可见 `WorldItem` 内部物品地址相等时，分别报告玩家实体和掉落实体候选，重复或缺失即保持空值。目标版 `LootMetadata.BaseId` 明确从物品元数据路径取最后一段，独立版据此对已关联的内部物品路径附上 `BaseIdCandidate`；它没有目录分配授权。同时开启 `DiagnoseLocalSlotCandidates` 后，只有新鲜且同一游戏状态/数据地址下的有序向量候选与 owner 精确相等，才附加 `unverified_vector_order_candidate` 的 1/2 槽位候选。目标版直接读取区域实体列表以验证向量，独立版仍依赖 ExileCore2 可见实体及未核实的基址与偏移。**上述候选不证明可靠 P1/P2、区域实例、掉落分配或可拾取**；这些字段不连接离线拾取状态机。

默认关闭的 `DiagnoseRadarEntities` 下有 `GroundHover` 只读字段。它取自 ExileCore2 的 `ItemsOnGroundLabelElement.ItemOnHover`，最多附带该实体对应的内部物品路径和名称；`GroundHoverStatus` 分别报告未观察到、已观察到或读取失败。接收端仅接受 `Evidence=ui_hover_only`，不会把该字段接入手柄命令。鼠标悬停与手柄当前物品卡片之间的对应关系**待 Windows/PoE2 实测**；即使两者偶尔指向同一物品，也不能由此推断拾取许可。

新增 `internal/autoloot` 独立状态机与 `PartyPilot-LootReplay.exe -replay <JSONL 文件>` 离线回放程序。输入必须明确给出当前卡片、帧序号、玩家、卡片时间，以及目录认可、容量、背包限制、菜单、个人背包、背包自动化和生存条件。卡片超过 350 毫秒、属于其他本地玩家、不可作为目标或任一条件不满足时不会产生拾取请求。有效卡片首次触发 80 毫秒请求；同一物品的新帧至少间隔 180 毫秒才重试，卡片消失或安全条件关闭立即取消当前请求。这些是离线决策输出，**没有接入实时传感器或虚拟手柄**。回放输入由操作者提供，不能证明任何字段已在游戏里被读取。

回放示例单行（`pulse_active` 仅是输出，不会按键）：

```json
{"now":"2026-10-26T00:00:00Z","player":1,"frame_sequence":1,"card":{"selected_item":"0xABC","base_id":"CurrencyExalted","player":1,"targetable":true,"received_at":"2026-10-26T00:00:00Z"},"conditions":{"catalog_approved":true,"capacity_allows":true,"inventory_limit_allows":true,"menu_clear":true,"own_inventory_closed":true,"inventory_automation_free":true,"alive":true}}
```

新增 `internal/autoloot/catalog.go` 和 `PartyPilot-CatalogReplay.exe -replay <JSONL 文件>`：操作者须逐条提供目录定义、玩家分配、物品卡片及可选掉落实体身份。离线解析按目标版的物品指针与基础 ID 对应、接收时间差不超过 2 秒、稀有度/类别匹配及明确分配筛选；优先选择指定稀有度的条目，再选精确基础 ID 条目。输出固定标记 `explicit_assignment_only`。**目标版的经济规则可覆盖普通分配，这里尚未实现；目录输出不接入实时拾取，也不是完整拾取许可。**

示例目录回放单行：

```json
{"player":1,"card":{"selected_item":"0xABC","base_id":"CurrencyExalted","player":1,"targetable":true,"received_at":"2026-10-26T00:00:00Z"},"definitions":[{"key":"CurrencyExalted","runtime_base_ids":["CurrencyExalted"],"item_class":"Currency","required_rarity":"","matches_all_of_item_class":false,"assignment":"P1"}]}
```

## 待确认与后续实现

1. 在真实 PoE2 中核对 ExileCore2 悬停字段的实体类型、更新频率和与目标版手柄卡片的关系。
2. 在真实 Windows/PoE2 核对物品卡片类型签名与 UI 树读取；验证有序本地玩家、owner、区域实例和掉落实体身份的对应关系后，再确定哪些卡片可进入拾取状态机。
3. 取得菜单、背包、分配归属、容量和生命状态的实时来源，逐项验证后才能接入拾取决策与输入桥接。

当前状态：**只读悬停与原始卡片诊断、离线拾取决策可编译，自动拾取输入未实现，游戏集成待验证**。
