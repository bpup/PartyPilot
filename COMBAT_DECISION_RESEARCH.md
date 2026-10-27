# 0.6.17 战斗规划回放阶段

## 来源与实现范围

所提供安装包的 `CombatDirectorDefaults.cs` 定义了默认关闭的配置、P1–P6 六个角色的职责与场景连招，以及 18 条连招；`CombatDirectorSafetySettings.cs` 和 `CombatDirectorSafetyGate.cs` 定义了旧数据、菜单、加载、死亡和安全区阻断；`CombatDirectorBehaviourResolver.cs` 规定紧急、Boss、Rare、普通怪群的优先级和回退顺序；`CombatDirectorRanges.cs` 给出默认 60 米范围和普通怪群至少三只的门槛。

独立版的 `internal/combat` 以自己的数据结构记录了六个默认角色和 18 条连招，并实现**只读的默认配置子集**。回放输入必须明确提供区域、时间、数据年龄、安全区、菜单/聊天/背包/加载状态、角色存活与加载状态，以及怪物的稀有度、生命百分比、相对主号距离和逐角色距离。缺失或无效的数据会拒绝该帧。默认配置保持关闭；只有显式启用规划时，才输出 `planned_only` 连招编号。安全阻断会输出对应原因，没有活怪则等待。

目标版 `CombatDirectorEngine.Evaluate` 在没有遭遇时先返回 `In town` 或 `Waiting: no enemies in range`，在遭遇结束中先返回等待结束，再进入全局安全门禁。独立回放现沿用此顺序：`waiting_no_encounter` 的 `reason` 保留目标版文案；`winding-down` 和 `complete` 不被菜单或旧数据阻断覆盖。存在可规划遭遇时仍按目标版安全门禁阻断。

回放现还要求每名已报告角色显式提供 `activeCombo`。它对应目标版 `CombatDirectorEngine.Evaluate` 传给行为解析器的 `activePlayers` 集合成员资格，由回放提供者负责记录；遭遇处于 `active` 阶段不能代替该证据。角色已有连招运行时，默认 60 米选怪半径按目标版公式扩大到 81 米，普通怪群的最低数量由三只降为一只；此规则仅改变只读计划，不驱动游戏输入。

每名已报告角色还须显式提供 `distanceFromMainM`。目标版 `CombatDirectorThreatClassifier` 在任一角色距离 MAIN 严格大于 15 米时给遭遇增加 `FormationBroken` 标签；独立回放用 `formation-broken` 表示这一只读标签。示例距离是合成输入，不代表传感器已能可靠获取六名角色的相对位置。

目标版的 `CombatDirectorDefaults.cs` 为各角色配置不同目标策略，`CombatDirectorTargetCoordinator.cs` 按威胁、距离、主号目标或多 Rare 分配目标，并保留 Normal/Magic 400 毫秒、Rare 800 毫秒、Unique 1200 毫秒的短期占用。本回放现可在连续 JSONL 帧上输出 `targetId`，并按这些默认策略保留与释放目标。目标分配只读，不代表技能条件或游戏中的选怪动作已验证。

行为解析器的紧急分支在已有遭遇时不要求该角色附近还有合格敌人；独立回放现按此规则保留紧急防御/恢复连招。合成测试覆盖角色距所有怪物超过普通选怪半径但仍有遭遇的情形；这不改变“没有遭遇时等待”的规则。

`CombatDirectorPacingScheduler.cs` 按默认 Natural 反应区间 110–230 毫秒、Natural 队伍间隔 45–110 毫秒、角色专属偏移区间，以及遭遇 ID 和角色 ID 的稳定哈希计算启动偏移。回放现在给 `planned_only` 候选输出 `scheduledOffsetMs`，候选被阻断时不占排序位置；该值尚未用于执行计时或按键。

`CombatDirectorEncounterManager.cs` 的默认设置以 60 米和三只怪为普通怪群进入条件，单只 Rare/Unique 也可触发；进入确认 150 毫秒，退出半径 75 米，结束确认 800 毫秒。回放现在保留遭遇 ID 与 `approaching`、`engaged`、`active`、`winding-down`、`complete` 阶段，怪物离开时不立即视为新遭遇；处于结束阶段时不输出新连招。场景标签来自当前遭遇中的怪物，不会由视野外的 Boss 改写当前怪群计划。`encounter` 字段及时间行为仅在按时间顺序提供的合成帧上验证。

回放现在还输出遭遇 `version`：初始管理器版本为 1，标签首次改变会再递增；后续阶段、怪物 ID 集合、稀有度计数或主要威胁改变时递增一次，标签集合改变时再递增一次。这与目标版管理器和引擎的两个更新点对应；尚未将版本用于技能执行协调。

`CombatDirectorEncounterRuntime` 还保存当前遭遇怪物的二维质心及开始、更新、进入战斗、收尾和完成时间。回放现要求每只怪物显式提供 `position.x/y`，对当帧遭遇集合求质心；集合为空时质心为 `(0, 0)`。这只是合成回放的空间数据，尚未证明游戏坐标、单位或六角色相对位置可由当前传感器可靠取得。

回放现还输出当前遭遇怪物 ID 集合、Normal/Magic/Rare/Unique 数量、最近敌人、最近 Rare、最近 Unique 的距离，以及主要威胁 ID。主要威胁按目标版的 Unique → Rare → Magic → Normal 优先级，同级按距离选择；无怪物时 ID 列表为空、四类数量为零，距离和威胁 ID 用 `null` 表示。目标版内部的最近敌人距离在空集合时为正无穷；`null` 是独立 JSON 输出对“无敌人”的显式表示，不用于实时战斗判断。

```sh
PartyPilot-CombatReplay.exe -replay testdata/combat-example.jsonl -enable-planning
PartyPilot-CombatReplay.exe -feed-replay testdata/combat-feed-example.jsonl -enable-planning
```

第二种模式参照目标版 `CombatDirectorWorldSnapshotAdapter`，从显式提供的客户端有序帧组装 P1–P6 快照。它要求每个预期客户端在场、声明角色顺序可靠、区域语义相符、各本地槽位连续且无重复，并要求主客户端明确提供怪物扫描、全部界面安全字段、主控安全区与紧急状态；每名角色的连招活动状态也须显式提供。主客户端的存活怪物会按各角色坐标计算距离，采集时间用于计算数据年龄，交给规划安全门禁判断。缺字段或冲突不会被补成“安全”；`testdata/combat-feed-example.jsonl` 是合成帧，现有传感器**不会**发布这种可靠有序战斗帧。

两个示例都是合成怪群，不代表游戏采集数据。命令仅输出规划 JSON，不向手柄桥接发送任何消息。解析器中的角色与主号距离、角色允许攻击的稀有度、目标生命门槛、目标锁定等判断也尚未进入本次规划。角色技能槽位、武器切换、目标的游戏内操作、连招计时、重复/冷却、跨客户端协调和手动接管仍未接入实时执行。当前传感器有候选技能绑定、有限的可见雷达实体和怪物组件原始生命诊断，但只有未实机验证的怪物稀有度与实体存活观察值，虽加入未经游戏验证的跨帧存活候选，仍缺可用于战斗的存活确认、完整玩家顺序与战斗 UI 状态，不能构成目标版要求的可靠战斗世界快照；因此规划层仍为 **implementation-aligned / integration-pending**。

传感器现可在默认关闭的雷达诊断中附带读取加载、背包、仓库、聊天、菜单和左右面板的原始可见性。每个读数可为 `null`，表示该属性未能读取；`observed` 仅表示采集流程完成，不等于目标版的 `AnyMenuOpen` 等安全状态。这些字段尚未在游戏中核验，也未接入规划或手柄控制，不能解除上述 `integration-pending` 状态。
