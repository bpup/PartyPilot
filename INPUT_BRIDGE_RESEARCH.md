# 0.6.17 输入桥接核对

## 事实：提供版本的静态证据

- `Poe2Coop.Services/ControllerSlotBridgeService.cs` 把 P5 和 P6 单独路由到第三个游戏进程。它在该进程恢复执行前加载专用运行时，也存在运行后附加路径；随后等待进程内的 XInput 钩子就绪。
- 两个角色共用一个按进程 ID 命名的 76 字节共享内存映射。头部标识版本、状态和已连接槽位；每个角色有独立的控制器状态及序号。P5 可单独连接，P6 只与 P5 一起连接。
- `Poe2Coop.Services/MultiInstanceLaunchService.cs` 对同一独立版安装目录的额外客户端使用另一专用运行时，在暂停创建的进程中完成准备后恢复执行。
- PartyPilot 目前通过 ViGEm 创建最多四个系统级虚拟手柄。它没有进程内 XInput 路由，也没有 P5/P6 对应的运行时。

## 推断与边界

- 仅把 PartyPilot 的 `--slots` 上限改成六个，不能复现第三个进程里的 P5/P6：普通 [XInput](https://learn.microsoft.com/en-us/windows/win32/api/xinput/nf-xinput-xinputgetstate) 只接受 0–3 号系统槽位，而提供版本使用进程内的独立路径。此结论基于上述静态结构和当前桥接实现，尚无游戏内运行对照。
- 同目录多客户端也不能通过放宽路径预检来证明可用；提供版本的额外实例依赖其专用运行时。目前独立实现应继续要求不同安装目录。

## 待确认

- 进程内 XInput 钩子对目标游戏版本的实际覆盖、时序与反作弊兼容性。
- P5/P6 的控制器报告如何与游戏内两个角色完成配对，以及焦点切换时的输入所有权。
- 独立开源实现所需的进程内组件和 Windows/PoE2 实机验收。当前没有可用的 Windows 测试环境。
