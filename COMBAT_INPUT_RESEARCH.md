# 0.6.17 战斗输入桥接阶段

## 静态依据

- 所提供版本的 `CombatDirectorInputExecutor` 为每名角色取得、路由并释放独占输入；失去输入或取消后释放租用。来源：安装包反编译的 `Poe2Coop.Services/CombatDirectorInputExecutor.cs`。
- 默认角色配置把施法时暂停移动设为开启，按键按压时长为 85 毫秒；配置验证允许 45–160 毫秒。来源：`CombatDirectorDefaults.cs` 与 `CombatDirectorProfileValidator.cs`。
- 目标版支持单键、LT+组合键及触发器输入。来源：`Poe2Coop.Models/AutoLootButtonBinding.cs`。独立桥接中的 L3/R3 已用于人工聚焦，暂不作为战斗短按输入。
- 目标版的 `CombatDirectorComboRuntime` 对带按钮的 LT 组合键先输出 45 毫秒 LT，再输出完整组合键，并把按键保持时间延长 45 毫秒；有存活目标和角色位置时，根据二者位置差将单位方向乘以 32767 写入右摇杆。
- 目标版在安全区、数据过期、加载、菜单、聊天或角色死亡等条件下停止决策。来源：`CombatDirectorSafetyGate.cs`。独立版还没有对应的可靠完整世界快照，不能自动判定这些条件。
- 目标版按主客户端区域 ID 和显示名中的城镇/藏身处等标识识别安全区，Delirium 与 Simulacrum 标识优先视作战斗区域。来源：`CombatDirectorSafeZoneRules.cs` 与 `CombatDirectorViewModel.ObserveClientArea`。独立版合成回放会拒绝与此规则矛盾的 `safeZone: false`。

## 当前实现

`PartyPilot.Bridge.exe` 新增默认关闭的 `--enable-combat`。它只在实体手柄模式和受保护配对均启用时允许战斗控制消息。协议包含 `combat_begin`、`combat_pulse`、`combat_end`，每条消息指定已分配的虚拟手柄槽位；短按消息还要求明确的输入名称和 45–160 毫秒按压时长。桥接只接受目标版按钮集合中不会与当前人工聚焦控制冲突的单键、LT 和 LT+组合键，拒绝摇杆位移及多键伪造短按。

租用仅对非人工聚焦的槽位生效，十秒无续期即过期；一次只允许一个短按，按压结束后恢复已有的跟随方向。按目标版默认配置，短按期间暂停该槽位自动移动。人工聚焦切换、菜单保护、实体手柄断连、自动组队接管、显式结束或桥接停机都会释放战斗短按并回中。战斗租用期间同槽位的药剂短按会被拒绝，避免延后叠加。

协议现在可同时携带 `right_x` 和 `right_y`（有一个就必须有另一个），只接受 16 位有符号摇杆值。带按钮的 `LT+` 短按会先输出 45 毫秒 LT 与瞄准方向，再输出按钮加 LT 与瞄准方向，完整按钮保持请求的 45–160 毫秒。只读战斗回放在角色位置和存活目标位置都明确存在时计算瞄准方向；缺少任一证据时不产生瞄准值。

传感器新增带区域哈希的原始 `IsTown`、`IsHideout`、`IsPeaceful` 观察；只有区域对象哈希与当前游戏帧哈希一致时才上报三个布尔值，否则明确标为 `area_mismatch` 或 `unavailable`。这些值还未在 Windows/PoE2 实测，也没有覆盖完整的逐角色、菜单、技能绑定与目标权限，因此不能单独启用战斗输入。

桌面 `BridgeSession` 现可显式传入 `combatEnabled: true`，并核对桥接的 `combat_enabled` 就绪回执；命令发送后等待匹配的控制回执，桥接退出则终止等待。本机模拟进程覆盖了开始租用、`LT+X` 短按、结束租用、默认关闭及非法绑定拒绝。真实桌面入口仍保持默认关闭，也没有可靠的目标选择、技能配置、战斗调度或经游戏实测的技能绑定。`SkillBindSource` 仍是静态偏移产生的候选，不能自动授权 `combat_pulse`。因此战斗功能为 **implementation-aligned / integration-pending**。
