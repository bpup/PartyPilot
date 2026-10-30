## 本版内容

- 提供单个 Windows 安装程序和桌面入口；安装载荷改为自带位置/地图探针，不再要求安装 ExileCore2 插件。
- 接入 2–3 个客户端、每端两角色的进程内输入映射和启动前准备链路；加入离线协议与假进程检查。
- Tab 覆盖层可接收自带探针的候选地点和地形帧；角色坐标、地点分类和区域归属仍需在当前 PoE2 版本中核对。

本版使用发布页所指向的源代码提交。Mac 本地的 Go、桥接协议、传感器诊断、探针自测与桌面无界面检查已通过。Windows 构建、静默安装和卸载检查由本次发布工作流执行；PR 的 Windows 检查记录见 [GitHub Actions](https://github.com/bpup/PartyPilot/actions/runs/41047283124)。

**待实机验证：**PoE2 内的 2–6 人跟随、追赶、蓝牙手柄输入及 Tab 地图位置。本版为实验测试版，不保证可直接用于游戏，也不代表与 MultiPilot 0.6.17 功能一致。

安装后从桌面或开始菜单的 **PartyPilot** 启动；安装目录中的其他 EXE 是内部组件。测试步骤见 [BETA_TEST.md](https://github.com/bpup/PartyPilot/blob/v0.1.0-beta.87/BETA_TEST.md)。
