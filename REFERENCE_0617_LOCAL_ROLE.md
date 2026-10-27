# 0.6.17 本地角色静态证据

本记录只用于独立实现的输入、位置来源核对，不包含授权模块、解锁逻辑或原软件源码。

## 样本

从本机 `MultiPilotPoE2/current` 目录读取；2026-10-27 用 ICSharpCode.Decompiler 9.1.0.7988 静态检查以下方法：

| 模块 | SHA-256 | 静态发现 |
| --- | --- | --- |
| `NoddPilot.Offsets.dll` | `36f6b3db7a3349c4dbbd852ce1081c4d8b6bffe6eb2321f89cd99265d51ba2e3` | `AreaEntityListReader.TryResolveLocalPlayer` 从区域实例中的本地角色结构取得角色向量，再读取向量首项；失败后尝试两个本地角色直接指针位置，并用角色路径筛选。 |
| `NoddPilot.Runtime.dll` | `374de91fd6c6320889031499e0aa0df066ef3ccc8f73842898bf7116e89c63cc` | `PlayerIdentityProbe.Read` 通过区域实例与 `PlayerVitalsReader.ResolvePlayer` 读取单个角色身份。 |
| `MultiPilot.Core.dll` | `26dd6d9ac32c97087661e1d74aac27c16a932a01f3243696a32d1661ea006a7e` | `QuadFollowGraph.Resolve` 接收四个跟随目标并在 1–4 角色内处理目标与环路。仅此类是四角色图，不能据此推定整个软件的角色上限。 |

## 对当前实现的影响

**事实：** 目标模块存在本地角色向量入口，但上述已检查的方法只消费首项，未提供“向量第二项必然对应客户端内 P2”的运行证据。`sensor/LocalSlotCandidateScanner.cs` 可诊断两个候选地址，`sensor/FastOrderedCandidateFrame.cs` 在显式实验开关下才输出双角色控制帧。

**待确认：** 在真实 0.6.17 对应游戏版本中，逐客户端核对向量首项、第二项与画面 P1/P2 的身份及位置；随后核对三客户端 P1–P6 映射、区域切换时的稳定性和后台输入。静态方法名、偏移默认值与离线模拟均不能替代这一步。
