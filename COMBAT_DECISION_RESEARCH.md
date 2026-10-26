# 0.6.17 战斗规划回放阶段

## 来源与实现范围

所提供安装包的 `CombatDirectorDefaults.cs` 定义了默认关闭的配置、P1–P6 六个角色的职责与场景连招，以及 18 条连招；`CombatDirectorSafetySettings.cs` 和 `CombatDirectorSafetyGate.cs` 定义了旧数据、菜单、加载、死亡和安全区阻断；`CombatDirectorBehaviourResolver.cs` 规定紧急、Boss、Rare、普通怪群的优先级和回退顺序；`CombatDirectorRanges.cs` 给出默认 60 米范围和普通怪群至少三只的门槛。

独立版的 `internal/combat` 以自己的数据结构记录了六个默认角色和 18 条连招，并实现**只读的默认配置子集**。回放输入必须明确提供区域、时间、数据年龄、安全区、菜单/聊天/背包/加载状态、角色存活与加载状态，以及怪物的稀有度、生命百分比、相对主号距离和逐角色距离。缺失或无效的数据会拒绝该帧。默认配置保持关闭；只有显式启用规划时，才输出 `planned_only` 连招编号。安全阻断会输出对应原因，没有活怪则等待。

目标版的 `CombatDirectorDefaults.cs` 为各角色配置不同目标策略，`CombatDirectorTargetCoordinator.cs` 按威胁、距离、主号目标或多 Rare 分配目标，并保留 Normal/Magic 400 毫秒、Rare 800 毫秒、Unique 1200 毫秒的短期占用。本回放现可在连续 JSONL 帧上输出 `targetId`，并按这些默认策略保留与释放目标。目标分配只读，不代表技能条件或游戏中的选怪动作已验证。

`CombatDirectorEncounterManager.cs` 的默认设置以 60 米和三只怪为普通怪群进入条件，单只 Rare/Unique 也可触发；进入确认 150 毫秒，退出半径 75 米，结束确认 800 毫秒。回放现在保留遭遇 ID 与 `approaching`、`engaged`、`active`、`winding-down`、`complete` 阶段，怪物离开时不立即视为新遭遇；处于结束阶段时不输出新连招。场景标签来自当前遭遇中的怪物，不会由视野外的 Boss 改写当前怪群计划。`encounter` 字段及时间行为仅在按时间顺序提供的合成帧上验证。

```sh
PartyPilot-CombatReplay.exe -replay testdata/combat-example.jsonl -enable-planning
```

该示例是合成怪群，不代表游戏采集数据。命令仅输出规划 JSON，不向手柄桥接发送任何消息。遭遇回放尚未包含质心、角色队形标签和完整的目标版版本号变更规则；解析器中的角色与主号距离、角色允许攻击的稀有度、目标生命门槛、持续战斗时扩大的退出范围、目标锁定等判断也尚未进入本次规划。角色技能槽位、武器切换、目标的游戏内操作、连招计时、重复/冷却、跨客户端协调和手动接管仍未接入实时执行。当前传感器有候选技能绑定、有限的可见雷达实体和怪物组件原始生命诊断，但只有未实机验证的怪物稀有度与实体存活观察值，虽加入未经游戏验证的跨帧存活候选，仍缺可用于战斗的存活确认、完整玩家顺序与战斗 UI 状态，不能构成目标版要求的可靠战斗世界快照；因此规划层仍为 **implementation-aligned / integration-pending**。

传感器现可在默认关闭的雷达诊断中附带读取加载、背包、仓库、聊天、菜单和左右面板的原始可见性。每个读数可为 `null`，表示该属性未能读取；`observed` 仅表示采集流程完成，不等于目标版的 `AnyMenuOpen` 等安全状态。这些字段尚未在游戏中核验，也未接入规划或手柄控制，不能解除上述 `integration-pending` 状态。
