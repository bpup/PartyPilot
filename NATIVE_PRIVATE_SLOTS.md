# 第三客户端 P5/P6 进程内输入

## 已实现

- `bridge/PrivateSlotPublisher.cs` 为明确的进程 ID 创建 76 字节输入映射及独立的 20 字节索引配置映射。P5 和 P6 报告各有独立的包号、写入序号、按键、扳机、摇杆和采样时间；消费者尚未确认 `Ready` 时，发布器拒绝输入。配置映射由 `bridge/PrivateSlotConfig.cs` 编码，P5/P6 索引必须显式提供且位于不同的 0–3 号，仅 P5 时只提供一个索引。
- `bridge/Client3FocusRouting.cs` 按目标版可读取的静态焦点规则组合 P5/P6：实体手柄只属于当前聚焦角色，明确的辅助按键与扳机仍能到达后台角色，辅助报告的摇杆轴不覆盖手动输入。`bridge/Client3InputPublisher.cs` 把组合结果写入独立双槽映射；未就绪、目标进程退出时拒绝发送，停止时回中。匿名映射测试已覆盖 P5 聚焦、后台辅助和双槽回中。此模块尚未接入桥接主循环。
- `native/private-slot-runtime/private_slots.c` 独立解析报文：拒绝错误标识、版本、槽位、停止状态和写入中的报告；报告超过现有桥接的 500 毫秒命令时效后返回回中状态。
- `native/private-slot-runtime/runtime.c` 是独立的 Windows x64 DLL。加载到目标进程后，它等待本进程的输入映射、索引配置映射和已加载的 XInput 1.4/1.3/9.1.0 模块，并尝试拦截 `XInputGetState`、`XInputGetCapabilities` 和 `XInputSetState`。配置缺失时不就绪，配置无效或冲突时写入失败状态；不再要求游戏进程继承环境变量。索引的真实值**待 Windows/PoE2 实测**。
- 原生钩子使用 vendored [MinHook v1.3.4](https://github.com/TsudaKageyu/minhook/tree/v1.3.4)，对应许可证在 `native/private-slot-runtime/vendor/minhook/LICENSE.txt`。这份 DLL 使用 PartyPilot 自己的共享内存标识和协议版本，不连接目标软件的私有运行时。

## 构建和验证

macOS 本机协议测试：

```sh
clang -std=c11 -Wall -Wextra -Werror native/private-slot-runtime/private_slots.c native/private-slot-runtime/protocol_test.c -o /tmp/partypilot-private-slots-test
/tmp/partypilot-private-slots-test
```

Windows x64 交叉编译需要官方 `llvm-mingw` macOS 工具包，将 `LLVM_MINGW_ROOT` 指向解压目录：

```sh
LLVM_MINGW_ROOT=/absolute/path/to/llvm-mingw sh native/private-slot-runtime/build.sh
```

构建产物是 `release/native/PartyPilot.PrivateSlots.dll` 与 `PartyPilot.PrivateSlots.Smoke.exe`。自检程序需在 Windows 上与 DLL 放在同一目录运行；它创建与游戏无关的同进程输入及配置映射，显式把测试索引设为 P5=2、P6=0，加载 DLL，并检查 XInput 报告、过期回中和停止断连。测试索引不代表 PoE2 的实际顺序。

## 待确认

本机已完成协议测试与 Windows 目标编译，**没有运行 Windows 自检程序，也没有在 PoE2 中加载 DLL**。桌面入口和桥接还没有把 DLL 加载到经确认的第三客户端进程，P5/P6 无法实际控制游戏。游戏使用的 XInput 模块、是否接受进程内钩子、本地索引顺序、配对和焦点输入所有权都需要 Windows/PoE2 实测。
