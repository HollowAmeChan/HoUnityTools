# HoFaceStudio

这里是计划内嵌于 HoUnityTools 的 Windows 面捕制作控制台工程位置。

**当前已建立可独立编译的 value / IR 核心、诊断样例与验证工程；桌面编辑器、预览传输和角色 IR 接入仍在推进。**

当前实现范围与复现命令见 [核心契约](CORE_CONTRACT.md)，阶段状态见 [实施记录](../../docs/face-tracking/HOFACE_IMPLEMENTATION_STATUS.md)。

AI / 自动化接口见 [AI_AUTOMATION.md](AI_AUTOMATION.md)。它通过本机命名管道提供状态、截图、文档、选区、VTS、预览和保存操作；录制、回放、冻结和诊断命令按后续版本扩展。

Release 构建与启动：

```powershell
dotnet build HoFaceStudio.slnx -c Release
.\src\HoFace.Studio\bin\Release\net10.0-windows\HoFaceStudio.exe
```

打开一对 `.hoface.value.json` 与 `.hoface.ir.json` 后，当前首版支持 value 行筛选、IR 节点列表、曲线/1D/2D 图、曲线点/切线操作、JSON 文本页、保存、撤销/重做和外部文件变更保护。VTS iPhone UDP 接收已接入制作会话；制作预览会执行外部 value + IR，最终权重可通过 UDP 发给 Blender 薄适配器。Warudo Mod 不增加作者调试桥接，正式运行由角色组件内部的 `HoVtsIrController` 执行 value + IR；Unity 编辑器侧可挂 `HoFaceStudioRawInput` 接收 raw 帧，让 Unity 的 value、LookAt 和其他消费者继续运行。

## 文档

- [双文件与两种调试模式](../../docs/face-tracking/HOFACE_VALUE_IR_ARCHITECTURE.md)
- [Windows UI 选型、交互与工程结构](../../docs/face-tracking/HOFACE_STUDIO_TECH_DECISION.md)
- [实施里程碑与验收](../../docs/face-tracking/HOFACE_IMPLEMENTATION_PLAN.md)

## 工程约定

- 推荐 WPF + .NET 10，选型需要用真实交互原型确认。
- `.hoface.value.json` 处理参数，`.hoface.ir.json` 组合静态形态键姿势。
- 默认外部求值并发送最终权重；另有裸值转发至现有角色接收组件的测试模式。
- 共享核心的计划主本位于 `Runtime/FaceTracking/Core/`，桌面项目链接编译，UI 不进入核心。
- Warudo 的角色内嵌模块服务于发布运行和角色链路测试，不作为默认制作调试后端。
- `Tools~` 内的桌面源码不进入 Unity 常规脚本导入；构建输出继续忽略。
- 创建真实 `.csproj` 时需添加本目录专用的 Git 入库例外；当前仓库默认忽略 `.csproj`。

## 同工作区查看两端

打开 [HoFace.authoring.code-workspace](HoFace.authoring.code-workspace)，可同时查看：

1. HoUnityTools：运行时代码主本、桌面工程与文档；
2. `BreakWarudo/Assets/HoWarudoModTests`：网络插件、节点、现行同步清单和验证工具。

相对路径按本机现有 `D:/Unity_Fork/HoUnityTools` 与 `D:/Unity_Project/BreakWarudo` 布局设置。配置不会自动改变当前 Codex 窗口的工作区；其他机器需要调整第二个目录。
