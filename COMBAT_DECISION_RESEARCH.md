# 0.6.17 战斗规划回放阶段

## 来源与实现范围

所提供安装包的 `CombatDirectorDefaults.cs` 定义了默认关闭的配置、P1–P6 六个角色的职责与场景连招，以及 18 条连招；`CombatDirectorSafetySettings.cs` 和 `CombatDirectorSafetyGate.cs` 定义了旧数据、菜单、加载、死亡和安全区阻断；`CombatDirectorBehaviourResolver.cs` 规定紧急、Boss、Rare、普通怪群的优先级和回退顺序；`CombatDirectorRanges.cs` 给出默认 60 米范围和普通怪群至少三只的门槛。

独立版的 `internal/combat` 以自己的数据结构记录了六个默认角色和 18 条连招，并实现**只读的默认配置子集**。回放输入必须明确提供区域、时间、数据年龄、安全区、菜单/聊天/背包/加载状态、角色存活与加载状态，以及怪物的稀有度、生命百分比和逐角色距离。缺失或无效的数据会拒绝该帧。默认配置保持关闭；只有显式启用规划时，才输出 `planned_only` 连招编号。安全阻断会输出对应原因，没有活怪则等待。

```sh
PartyPilot-CombatReplay.exe -replay testdata/combat-example.jsonl -enable-planning
```

该示例是合成怪群，不代表游戏采集数据。命令仅输出规划 JSON，不向手柄桥接发送任何消息。目标版解析器还有角色与主号距离、怪物与主号距离、角色允许攻击的稀有度、目标生命门槛、持续战斗时扩大的退出范围、目标锁定和危险怪群等判断；这些尚未进入本次规划。角色技能槽位、武器切换、目标选择、连招计时、重复/冷却、跨客户端协调和手动接管也未接入实时执行。当前传感器只有候选技能绑定与有限雷达实体，不能构成目标版要求的可靠战斗世界快照；因此规划层仍为 **implementation-aligned / integration-pending**。
