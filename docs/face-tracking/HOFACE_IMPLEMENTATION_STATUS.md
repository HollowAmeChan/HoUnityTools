# HoFace 实施记录

更新日期：2026-11-03。目标模式已启动，WPF + .NET 10 已由用户确认。

## 核心里程碑

已实现：

- `Tools~/HoFaceStudio/` 中的 .NET 工程与共享源码链接，不另复制桌面求值逻辑。
- 无 UnityEngine 引用的 `netstandard2.0` 核心；保留 Unity 现有字段类型与旧调用入口。
- 新 value v1 格式入口、旧 JSON 兼容读取、显式时钟与会话独立随机状态。
- Pose、1D、Cartesian 2D、Direct 的初版 IR、稳定引用校验与最终权重范围。
- 明确标记为诊断用途的嘴部样例；WTD 当前 ARKit 生产资产已迁移至 IR v2。

已验证：

- 便携核心与真实 Unity 的生产规则 / 曲线对照：**24,625 项检查通过**。
- 原有镜像与校准真实 Unity 回归：**2,766 项检查通过**。
- 独立核心检查覆盖格式、顺序、时钟、随机、曲线、延迟、IR 边界与合成；最新数量以运行输出为准。
- WPF 桌面工程已能以 Release 配置构建，应用启动探针保持进程运行并正常退出。
- VTS iPhone UDP 接收与制作会话已接入：预览模式执行外部 value + IR，裸值模式只发布原始帧，不重复求值。
- IR 节点列表与曲线/1D/2D 画布已接入 WPF；最终权重 UDP 预览协议与 Blender 4.5.8 薄适配器原型已通过回环/注册测试。
- Warudo Mod 侧不增加作者调试桥接：Warudo 只作为最终消费端，角色组件内部执行 value + IR。桌面会话保留原始帧事件边界，未来若需要仅接 Unity 编辑器适配器。
- Unity 编辑器专用 `HoFaceStudioRawInput` 已接入共享 `hoface.raw` v1 解析，接收后调用现有角色参数接收器；Blender 继续只接收最终形态键权重。
- `HoVtsIrController` 已接入角色运行时：直接读取 value 输出、求值 IR 并写形态键，不依赖 Animator；独立 Unity 工程验证通过。
- IR v2 只保存逻辑形态键输出名与范围；Unity 组件和 Blender HoFace 各自维护显式网格对象列表，同名键写到所有列表内网格，缺键跳过，清单外对象不查找、不写入。预览 v2 不携带物体绑定。
- WTD 的 52 个逻辑输出与 6 个网格引用已保存；隔离 Unity 工程验证实际模型的 208 个匹配绑定，1,036 组输入、215,488 个权重与迁移基线一致，生产 prefab 副本中的 6 个 renderer 引用及接收器/IR 引用均成功加载。组件网格列表回归 16 项，Blender 两个薄适配器回归 14 项。
- HoFaceStudio 本地 AI 控制通道已接入：状态、截图、打开/保存、撤销/重做、选区、VTS、模式和预览命令已通过命名管道验证；录制/回放/冻结/诊断接口列为下一阶段。
- Unity 旧 JSON/面捕调试窗口已从菜单入口撤下，旧全局调试宿主不再自动 tick；接收器 Inspector 改为启动 HoFaceStudio 并显示 Play 模式 raw 状态。
- 已删除旧 JSON 编辑器、旧面捕测试窗口、组件调试视图、旧 Unity 调试会话和旧 trace 记录器；控制器迁移工具与运行时稳定组件保留。
- 旧影子 AnimationSession、全局 DebugSettings/Host、输入 Hub、旧 Unity VTS 适配器和控制器编辑窗口已物理删除；HoVtsController 运行组件仅作为迁移兼容保留。
- Blender HoTools 侧已新增正式 **HoFace** 模块：只接收 `hoface.preview` 最终权重，维护驱动物体列表、连接状态、形态键清空和显式预检查开关；旧混合矩阵面板入口已退役，提交为 `8553a0f3`。
- 核心编译无警告；本机已有 .NET 10 SDK 与 WindowsDesktop 运行环境，暂未安装新环境。

包级验证说明：原全量 Editor 扫描因既有 MMD 编辑器的 `Volume` 类型引用列表缺失而失败；给本机临时响应文件补上工程已存在的 `Unity.RenderPipelines.Core.Runtime.dll` 后，完整扫描可编译。未修改该 MMD 代码或将此问题归因于本次核心提取。真实 Unity 隔离工程则直接编译并执行了相关面捕源码。

## 尚未完成

- WPF 曲线 / 控制点编辑器已建立首版：曲线绘制、点/切线拖动、双击加点、缩放平移、value/IR 文档打开/保存、撤销/重做和外部修改检测；仍缺真实 VTS 与 IR 节点选择联动的制作验收。
- 录制回放，以及 Unity 编辑器适配器的连接、断开和所有权验收。
- Blender / Unity 薄预览适配及其写入所有权验证。
- Unity 预览适配及写入所有权验证。
- WTD IR v2 在实际 Warudo Character Mod 中的构建与客户端验收。

现有稳定实现继续保留。核心里程碑通过不代表以上集成已完成；后续按独立提交更新本记录。
