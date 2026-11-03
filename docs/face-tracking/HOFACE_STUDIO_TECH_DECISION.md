# HoFaceStudio：Windows 桌面 UI 技术决策

调研日期：2026-11-03。WPF + .NET 10 已由用户确认并开始实施。首版已用自绘 WPF 控件验证曲线编辑、点/切线操作与文档框架；交互性能与第三方 UI 依赖仍需真实制作原型验证，实际完成范围见 [实施记录](HOFACE_IMPLEMENTATION_STATUS.md)。

本决策服务于 [双文件架构](HOFACE_VALUE_IR_ARCHITECTURE.md)。软件暂名 HoFaceStudio，工程放在 HoUnityTools 的 `Tools~/HoFaceStudio/`。

## 1. 首要需求

- Windows 本机可运行，能直接访问工程里的两份 JSON。
- 长期维护大量编辑交互：曲线、二维自由点、阈值、权重、表格、参数选择与错误定位。
- 与 Unity / Warudo 角色代码共用 C# 求值核心；不要再引入一份 Python 或 TypeScript 求值器。
- UI 与求值、网络、录样解耦。桌面依赖不能进入 UMod 编译。
- 默认在外部求值并发送最终权重；另有向现有角色接收器转发裸值的模式。
- 软件源代码与核心在同一仓库，Warudo 插件作为同工作区可见的另一个根目录。

当前没有跨平台、网页部署、账号服务、插件市场或自更新需求。

## 2. 方案比较

| 方案 | 可视化与交互能力 | 与 C# 核心的关系 | 当前代价与判断 |
| --- | --- | --- | --- |
| **WPF + .NET 10** | 自定义绘制、Canvas、矢量几何、数据绑定；有成熟文本编辑和节点交互库 | 同进程直接引用核心 | **推荐**。Windows 需求明确，工程与调试链最少 |
| Avalonia + C# | 自定义控件通过 DrawingContext 绘制，也可走 Skia；跨平台 | 同进程直接引用核心 | 可行备选。当前不利用跨平台价值；需要验证特定编辑控件与输入交互 |
| React + Tauri | Web 图形、表单和编辑器生态丰富，可用 SVG/Canvas 做交互 | 为继续共享 C# 核心，通常需要 .NET sidecar / IPC | 需要维护前端、壳和核心进程。只有 Web UI 的收益经原型证明明显时再选 |
| PySide6 / Qt | Qt Widgets 和图形视图适合工具编辑器 | 需要额外桥接 C# 或单独核心进程 | 可行但增加语言与发布链；不因 Blender 使用 Python 就把求值逻辑搬成 Python |

WPF 的推荐理由是 Windows 专用工具和 C# 复用的组合，不代表其自定义编辑器已经免费得到。曲线切线、坐标操作、撤销与数据一致性都仍需专门实现。

Avalonia 核心为 MIT，但部分扩展产品有单独许可。Qt for Python 社区版本涉及 LGPLv3 / GPLv3 条款。第三方依赖必须逐项确认范围，不能将“免费使用”当成没有分发义务。

## 3. 推荐的技术组合

| 部分 | 建议 |
| --- | --- |
| 桌面宿主 | WPF，目标 `net10.0-windows`，C# |
| 核心 | 源码主本位于 `Runtime/FaceTracking/Core/`；外部工程链接编译同一批源码 |
| 核心独立构建 | 先以 `netstandard2.0` 为候选契约，结合 Unity 2021.3 和真实 UMod 编译验证 |
| 编辑器数据模型 | 桌面独立 ViewModel、选择状态、撤销命令；不把 WPF 类型塞进运行时模型 |
| 曲线和二维点位 | 专用 WPF 绘制控件，绘制点数据由共享数学提供 |
| JSON 文本页 | 优先评估 AvalonEdit；它负责文本编辑，规则校验仍由我们的解析与诊断完成 |
| 树 / 图交互 | 首版以层级树、属性面板和局部二维图为主；确需连线画布时评估 Nodify |
| 分区布局 | 首版 Grid / GridSplitter；不提前引入完整停靠框架 |
| 发布 | 先提供 `win-x64` self-contained 文件夹，内含 EXE；构建产物置于忽略目录 |

.NET 10 为 LTS，官方列出的支持期到 2028-11-14；.NET 8 的支持期到 2026-11-10。新桌面工程不以即将到期的 .NET 8 作为长期基线。当前本机已有 .NET 10 SDK，但实际 SDK 与包版本在创建工程时再固定。

核心面向较低 API 契约，桌面面向新 .NET，两者不矛盾。`netstandard2.0` 标签也不保证 UMod 安全校验自动通过，仍需检查实际 API 与打包结果。

### 为什么不把一切放进节点编辑器

用户主要需要快速挑参数、调曲线、拖混合点和修改姿势。完整任意连线编辑器会增加选择、布局和导航成本。

IR 可以有树状组合，但日常操作先聚焦所选节点：

- 左侧区域 / 节点列表；
- 中间为所选曲线或 1D / 2D 混合图；
- 右侧为参数绑定、精确数值与姿势内容；
- 底部为采样、回放与即时结果。

Nodify 只作为确实需要图编辑时的候选控件，不把它的模型格式变成 IR 格式。AvalonEdit、Nodify 的桌面依赖不得进入共享核心。

## 4. 关键可视化交互

| 工作 | 控件与反馈 | 数据边界 |
| --- | --- | --- |
| value 规则编辑 | 可筛选参数表、表达式栏、前后阶段读数、曲线图 | 行顺序与同名输出语义保留 |
| 曲线编辑 | 点和切线拖动、精确数值、缩放平移、当前输入标记 | 范围按参数单位；不强制 0–1 |
| 1D 混合 | 阈值轴、可拖动姿势点、当前参数游标 | 点位置是作品数据，游标是调试状态 |
| 2D 混合 | 自由散点、姿势标签、当前输入与权重反馈 | 不强制规则网格，不把绘制三角网误当实际混合算法 |
| Direct 组合 | 子项列表、权重参数、贡献观察 | 归一化与叠加规则明确 |
| 姿势编辑 | 可筛选目标权重表、隔离、归零与恢复 | 一个姿势可含多个形态键 |
| JSON 文本 | 文本编辑、搜索、诊断定位与结构视图切换 | 两种视图操作同一文档，不各存一份配置 |
| 调试与回放 | 输入源、时间轴、冻结原始样本、锁定输出、临时覆盖 | 不把临时操作静默保存为作品 |

拖拽期间实时预览，松手后形成一次撤销操作。精确数值输入、键盘微调、复制粘贴、删除、重做与中文命名属于首版交互。

文件保存时检查外部修改，先验证再发布运行快照。解析失败时保留可识别的最后有效配置并显示状态，不能让 UI 显示新内容而运行端偷偷继续旧配置却不提示。

## 5. 线程与性能设计原则

- 网络、核心求值、文件操作不阻塞 WPF UI 线程。
- 核心按明确时间推进；画面刷新只读取已发布的帧快照，不在 Paint / Render 回调里求值。
- 实时预览允许只显示最新结果；录样另存完整需要的时间序列，不能为了 UI 平滑伪造样本。
- 曲线 / 网格的静态图形缓存，拖动只更新必要内容。大量采样点不各创建一个重型可视控件。
- 参数历史显示有窗口和容量上限；表格使用合理的虚拟化。
- 文本解析和文件监听合并重复事件，文件观察器回调不直接覆盖未保存编辑。

本次没有性能实测。未来原型需记录实际参数/点位数量、硬件、刷新率、耗时和内存，再定正式预算。

## 6. 选型确认前的原型验收

创建正式应用时先做一块实际编辑画布，验收：

1. 曲线点与切线、二维点的拾取、拖动、缩放和平移；
2. 拖拽的一次撤销、数值输入与选中联动；
3. 中文 IME、长名称、不同 DPI 和窗口跨屏；
4. 文件被 AI 修改后的变更检测与冲突提示；
5. 持续 VTS 输入下编辑响应正常，求值和绘制次数可观测；
6. 两种调试模式明显可区分，对同一目标无重复写入。

如果 WPF 原型在具体交互或维护成本上不合格，再以同一数据与操作样本比较 Avalonia。不能只凭静态截图决定框架。

## 7. 工程与工作区安排

建议目录：

```text
HoUnityTools/
├─ Runtime/FaceTracking/Core/       计划提取的纯 C# 源码主本
├─ Runtime/FaceTracking/            Unity / 角色薄适配
├─ Tools~/HoFaceStudio/
│  ├─ README.md
│  ├─ HoFace.authoring.code-workspace
│  ├─ src/HoFace.Core/              独立构建项目，链接源码主本
│  ├─ src/HoFace.Studio/            WPF 编辑器与制作会话
│  ├─ src/HoFace.Transport/         网络、记录与预览消息
│  └─ tests/                       桌面与核心测试
└─ docs/face-tracking/              架构、决策与实施记录

BreakWarudo/Assets/HoWarudoModTests/ 插件独立仓库，作为工作区第二根目录
```

除本次 README 和工作区配置外，上述新代码目录尚未创建。`Tools~` 使桌面项目不进入 Unity 常规脚本导入；共享核心在 Runtime 下正常参与角色代码构建，不新增专门的子 asmdef。

桌面核心项目用显式 `Compile Include` 链接主本，不复制代码。UI、网络和文件 IO 项目只能向核心依赖，核心不能反向依赖它们。

当前网络插件只同步 `HoJson.cs`、`HoVtsPacket.cs` 等清单内协议源码；规则求值属于角色模块。确需跨仓库副本时使用已入库的清单和检查工具，不用 junction 或本地临时脚本作为唯一机制。

当前 `.gitignore` 忽略全部 `.csproj` / `.sln`。创建真实桌面项目时必须添加仅针对 `Tools~/HoFaceStudio/` 的入库例外，不能强行 add 后就忘记规则。`bin`、`obj`、publish、录样和用户设置继续忽略。

随附工作区配置使用相对于本机现有目录结构的路径，不会自动修改 Codex 当前窗口配置。其他机器目录不一致时需调整第二根目录。

## 8. 调研来源

以下为官方文档或项目自己的仓库，核查日期 2026-11-03。具体依赖版本与完整许可清单在落地时锁定。

- [WPF 概览](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)
- [WPF 绘制与呈现](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/wpf-graphics-rendering-overview)
- [.NET 支持周期](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- [AvalonEdit：WPF 文本编辑器，MIT](https://github.com/icsharpcode/AvalonEdit)
- [Nodify：WPF 节点交互控件，MIT](https://github.com/miroiu/nodify)
- [Avalonia 自定义绘制](https://docs.avaloniaui.net/docs/guides/custom-controls/draw-with-a-custom-control)
- [Avalonia 许可说明](https://docs.avaloniaui.net/faq/licensing)
- [Tauri sidecar](https://v2.tauri.app/develop/sidecar/)
- [Tauri Windows 安装与 WebView2](https://v2.tauri.app/distribute/windows-installer/)
- [Qt for Python 许可](https://doc.qt.io/qtforpython-6/licenses.html)
