# 0.6.17 近距离镜像跟随接入状态

## 已核对的目标行为

- `Poe2Coop.Services/UltimateShippingDefaults.cs` 将 `CloseRangeMirror` 设为 `false`；`Poe2Coop.Services.Distributed/FollowFormation.cs` 的显式 Tight group 预设才将它设为 `true`、时长设为 400 ms。
- `Poe2Coop.Services/CloseRangeMirrorController.cs` 在跟随停靠时镜像主手柄左摇杆一小段时间，时长限制为 50–5000 ms；结束后请求 Smart Follow 直接接手。连续跟随是否继续，还取决于队长最近 250 ms 内是否移动。镜像冲刺在双方相距 8 格且按键至少持续 125 ms 后释放。
- `Poe2Coop.ViewModels/ShellViewModel.cs` 先计算 Smart Follow，再调用镜像控制器；镜像结束且原结果仍是停靠时，用连续跟随模式重新计算。菜单、手动接管、拾取优先等状态会阻止镜像。

## 独立版现状与接入条件

- `internal/follow/close_mirror.go` 已实现上述状态机的独立决策部分，并用受控帧检查短时镜像、接手、冲刺释放和输入失效。`internal/live/close_mirror.go` 现可在双客户端 2P、同一已核对实例、主手柄样本新鲜且未被菜单阻断时调用短时镜像；桌面提供实体手柄开关和 50–5000 ms 时长输入，默认关闭。
- `bridge/Program.cs` 已能在受保护配对完成后接收 `main_input_subscription`，以最多每 50 ms 一帧输出主手柄摇杆、B 键及菜单阻断状态；默认不发送。`ui/BridgeSession.cs` 核对回执与有界轴值；`ui/LiveFollowSession.cs` 在上述开关下将最新样本送入 `cmd/livefollow/main.go`，实时引擎按角色与 500 ms 时效校验。模拟子进程检查了桥接→桌面→实时进程传递，Go 单测检查了镜像方向与暂停释放；没有 Windows／游戏实测。
- `bridge/BridgeProtocol.cs` 现仅在带 `mirror_movement=true` 且方向有效的 `holding` 命令中放行短时镜像；普通 `holding` 仍回中。桌面解析和桥接协议测试覆盖了该显式标记。松手、输入超时、暂停由状态机和实时引擎检查；角色切换和实际物理输出仍需 Windows 实测。
- `internal/follow/follow.go` 现可在镜像到期后由 `internal/party/party.go` 同帧重新计算单个跟随者；仅当队长最近 250 ms 内有新鲜移动、距离超过 5 格且路线没有跳跃时，才越过停靠距离继续跟随。受控帧检查了同帧接手、静止与过期时继续停靠。仍缺少受跟随者按键配置约束的 B 键镜像冲刺及其 8 格释放信号。

目前没有 Windows／PoE2 实机，物理摇杆读数、传感器坐标和游戏内镜像效果仍为 `integration-pending`。
