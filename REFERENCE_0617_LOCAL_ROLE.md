# 0.6.17 本地角色静态证据

本记录只用于独立实现的输入、位置来源核对，不包含授权模块、解锁逻辑或原软件源码。

## 样本

从本机 `MultiPilotPoE2/current` 目录读取；2026-10-27 用 ICSharpCode.Decompiler 9.1.0.7988 静态检查以下方法：

| 模块 | SHA-256 | 静态发现 |
| --- | --- | --- |
| `NoddPilot.Offsets.dll` | `36f6b3db7a3349c4dbbd852ce1081c4d8b6bffe6eb2321f89cd99265d51ba2e3` | `AreaEntityListReader.TryResolveLocalPlayer` 从区域实例中的本地角色结构取得角色向量，再读取向量首项；失败后尝试两个本地角色直接指针位置，并用角色路径筛选。 |
| `NoddPilot.Runtime.dll` | `374de91fd6c6320889031499e0aa0df066ef3ccc8f73842898bf7116e89c63cc` | `PlayerIdentityProbe.Read` 通过区域实例与 `PlayerVitalsReader.ResolvePlayer` 读取单个角色身份。 |
| `MultiPilot.Core.dll` | `26dd6d9ac32c97087661e1d74aac27c16a932a01f3243696a32d1661ea006a7e` | `QuadFollowGraph.Resolve` 接收四个跟随目标并在 1–4 角色内处理目标与环路。仅此类是四角色图，不能据此推定整个软件的角色上限。 |
| `memory-probe/MultiPilotMemoryProbe.dll` | `2fcc449e258315c13b6a0788ca8acb9de234c8cdf7007fabc5873985d2ab41f6` | `PlayerSlotVectorResolver.Resolve` 搜索区域实例附近的 1–2 项本地角色向量，核对角色路径与顺序冲突；`ProbeSession.CaptureMotion` 使用该顺序读取每个角色的原始位置，要求预期角色数都可读取才标记 `playerOrderReliable=true`。 |

## 对当前实现的影响

**事实：** `NoddPilot.Runtime.dll` 中上述单角色身份方法只消费向量首项；独立的 `memory-probe/MultiPilotMemoryProbe.dll` 有完整的双槽读取路径。目标版 `MemoryReaderService` 以 `--expected-player-count 1|2 --motion-only --watch 20` 启动探针，只有 `attached=true`、`playerOrderReliable=true` 且角色数量足够时才接收运动帧。`AssignedRuntime.LocalClient` 按 `ClientAssignment.Slots` 持有客户端内角色，并将第一、第二路输入一起提交。独立版 `sensor/LocalSlotCandidateScanner.cs` 已有相近的向量候选扫描，`sensor/FastOrderedCandidateFrame.cs` 仍需显式实验开关才能输出双角色控制帧。

**待确认：** 在真实 0.6.17 对应游戏版本中，逐客户端核对向量首项、第二项与画面 P1/P2 的身份及位置；随后核对三客户端 P1–P6 映射、区域切换时的稳定性和后台输入。目标版的 `playerOrderReliable` 是其探针运行时的判断，静态反编译不能证明独立版的候选基址、坐标和游戏角色实际对应。
