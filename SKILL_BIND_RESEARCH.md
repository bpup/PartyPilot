# 0.6.17 技能绑定读取取证

## 已确认的静态证据

- 所提供版本的 `ProbeSession.ReadPartySkillBars` 先要求可靠的玩家顺序，再按区域缓存和每两秒扫描读取技能栏；服务器链路失败时才尝试 Actor 技能栏。来源：所提供安装包反编译的 `ProbeSession.cs` 中 `ReadPartySkillBars` 与 `ReadServerSkillBar`。
- 服务器链路需要玩家数据指针与角色实体一一对应，并在读取技能栏前检查背包向量。绑定表读取长度为 148 字节，其中从第 13 个四字节字段起有 24 个槽位。来源：同文件 `TryAddStrictInventoryChainCandidate`、`ReadBindSlotTable`、`DecodeBindSlotTable`。
- 槽位按键映射中，仅每组前七个按钮被目标版标为确认；其余方向键映射只是建议。来源：`Poe2Coop.Services/LivePartySkillDiscovery.cs` 的 `ControllerInputForBindSlot` 和 `BindSlotButtonConfirmed`。

## 独立实现

`sensor/SkillBindTable.cs` 可对已采集的 148 字节表做只读解析，保留原始八位十六进制标识，识别技能石、固有动作和武器赋予技能。技能石名称只从显式提供的面板槽位证据关联；未知名称保持空值。无效长度、任一槽位缺少高位标记、玩家 owner 指针与预期角色不匹配时，整表拒绝。解析成功返回 `decoded_unverified_source`，因为传入地址相等本身不能证明采样文件来自当前游戏和当前区域。

传感器启用现有 `DiagnoseLocalSlotCandidates` 后，每两秒尝试一次只读来源扫描。扫描要求 `IngameState + 656` 与当前区域地址一致，按目标版的有界区域指针范围查找直接或嵌套的玩家数据向量，核对一至两名角色的 owner 一一对应关系、各角色的 12×5 主背包结构及本地角色的绑定表，并在读取结束时复核区域、入口、玩家向量、owner、背包及绑定表。重复入口指向同一组玩家数据会合并；多条不同的有效数据链路会返回 `ambiguous_candidates`，不输出技能。区域、角色或顺序变化时传感器丢弃缓存结果。输出字段为 `SkillBindSource`、`SkillBindSourceStatus` 和 `SkillBindSourceSeenUtc`。这里使用的 `+72` 玩家向量、`+768` owner、`+800` 背包向量和 `+30792` 绑定表偏移，来自安装目录中的 `NoddPilot.Offsets.dll` 静态属性及所提供版本的读取流程；未证明当前游戏版本仍采用它们。

`release/skillbind/PartyPilot.SkillBind.exe` 提供采样文件检查：

```text
PartyPilot.SkillBind.exe bind-table.bin 0x<已观察 owner 地址> 0x<预期角色地址>
```

输入文件必须恰好 148 字节；两个地址参数只是额外一致性检查，不能替代采集时的来源验证。输出是 JSON；成功退出码 0，拒绝为 1，用法错误为 2。此工具不读取进程内存，也不发送游戏输入。

## 待确认

- 当前运行的 PoE2 版本使用哪个偏移配置；仅凭 0.6.17 静态默认值不能确定。
- ExileCore2 的本地角色、当前区域、玩家数据 owner 和技能石面板能否在真实 Windows 进程中形成唯一且稳定的链路。
- 解析出的按键是否与游戏内该角色当前装备、技能和控制设置一致。尚无 Windows/PoE2 运行记录。

因此，技能栏发现目前是 **implementation-aligned / integration-pending**；自动战斗仍未接入。
