# HoFaceStudio（暂名）

这里是计划内嵌于 HoUnityTools 的 Windows 面捕制作控制台工程位置。

**当前已建立可独立编译的 value / IR 核心、诊断样例与验证工程；桌面编辑器、预览传输和角色 IR 接入仍在推进。**

当前实现范围与复现命令见 [核心契约](CORE_CONTRACT.md)，阶段状态见 [实施记录](../../docs/face-tracking/HOFACE_IMPLEMENTATION_STATUS.md)。

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
