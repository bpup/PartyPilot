# 0.6.17 近距离镜像跟随接入状态

## 已核对的目标行为

- `Poe2Coop.Services/UltimateShippingDefaults.cs` 将 `CloseRangeMirror` 设为 `false`；`Poe2Coop.Services.Distributed/FollowFormation.cs` 的显式 Tight group 预设才将它设为 `true`、时长设为 400 ms。
- `Poe2Coop.Services/CloseRangeMirrorController.cs` 在跟随停靠时镜像主手柄左摇杆一小段时间，时长限制为 50–5000 ms；结束后请求 Smart Follow 直接接手。连续跟随是否继续，还取决于队长最近 250 ms 内是否移动。镜像冲刺在双方相距 8 格且按键至少持续 125 ms 后释放。
- `Poe2Coop.ViewModels/ShellViewModel.cs` 先计算 Smart Follow，再调用镜像控制器；镜像结束且原结果仍是停靠时，用连续跟随模式重新计算。菜单、手动接管、拾取优先等状态会阻止镜像。

## 独立版现状与接入条件

- `internal/follow/close_mirror.go` 已实现上述状态机的独立决策部分，并用受控帧检查短时镜像、接手、冲刺释放和输入失效。当前没有调用者，**不能在桌面或游戏中启用**。
- `bridge/Program.cs` 已能在受保护配对完成后接收 `main_input_subscription`，以最多每 50 ms 一帧输出主手柄摇杆、B 键及菜单阻断状态；默认不发送。`ui/BridgeSession.cs`、`ui/LiveFollowSession.cs` 与 `cmd/livefollow/main.go` 尚未订阅或传递这些样本。下游仍须核验来源、样本新鲜度、失焦和菜单状态。
- `bridge/BridgeProtocol.cs` 会把 `holding` 状态的方向强制置零。镜像接入时必须为有效的停靠镜像方向设计明确的桥接表示，并验证松手、超时、角色切换时立即回中。
- `internal/follow/follow.go` 尚未接收镜像结束后的连续跟随请求。接入时需复现目标版 250 ms 队长运动门禁，以及同一帧从镜像切到跟随时不插入空白移动帧。

目前没有 Windows／PoE2 实机，物理摇杆读数、传感器坐标和游戏内镜像效果仍为 `integration-pending`。
