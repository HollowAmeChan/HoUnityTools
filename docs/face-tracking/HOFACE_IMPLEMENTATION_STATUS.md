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
- 独立核心检查覆盖格式、顺序、时钟、随机、曲线、延迟、IR 边界与合成；2026-11-03 Release 检查 **1048 项通过**，含 raw/preview 互斥、ACK、释放、追踪丢失与回放 Seek/复播。
- Studio 交互检查在 2026-11-03 Release 隔离构建中 **67 项通过**；命名管道 / CLI 端到端脚本另通过 **27 项**，并为 Value 曲线、紧凑布局、Pose、Blend2D、设置区与 JSON 草稿生成后台窗口截图。整套 WPF/.NET 10 Release 构建为 0 警告、0 错误。
- VTS iPhone UDP 接收与制作会话已接入：预览模式执行外部 value + IR，裸值模式只发布原始帧，不重复求值。
- IR 节点列表与曲线/1D/2D 画布已接入 WPF；最终权重 UDP 预览协议与 Blender 4.5.8 薄适配器原型已通过回环/注册测试。
- Warudo Mod 侧不增加作者调试桥接：Warudo 只作为最终消费端，角色组件内部执行 value + IR。作者调试由 HoFaceStudio 处理，Unity 编辑器通过独立 raw 适配器复用现有参数接收器。
- Unity 编辑器专用 `HoFaceStudioRawInput` 已接入共享 `hoface.raw` v1 解析；loopback 收包后按 Unity 本地单调时钟更新角色接收器，并返回逐帧 ACK。raw/preview 模式切换互斥，失追、断连和停止预览会释放该输出端拥有的值。
- `HoVtsIrController` 已接入角色运行时：直接读取 value 输出、求值 IR 并写形态键，不依赖 Animator；向下游消费者提供带绑定版本的最终写入快照和共享输出所有权，读取不会重复求值。
- IR v2 只保存逻辑形态键输出名与范围；Unity 组件和 Blender HoFace 各自维护显式网格对象列表，同名键写到所有列表内网格，缺键跳过，清单外对象不查找、不写入。预览 v2 不携带物体绑定。
- WTD 的 52 个逻辑输出与 6 个网格引用已保存；隔离 Unity 工程验证实际模型的 208 个匹配绑定，1,036 组输入、215,488 个权重与迁移基线一致，生产 prefab 副本中的 6 个 renderer 引用及接收器/IR 引用均成功加载。组件网格列表回归 16 项，Blender 两个薄适配器回归 14 项。
- Unity 2021 隔离工程中的 IR 组件回归通过 **68 项**，覆盖最终权重/写入快照、范围与倍率、所有权冲突和释放；HoFaceStudio raw 与一键角色创建验证也通过，包括 loopback 收包/ACK、回放时间回退、失追释放、停止释放，并预填有形态键的网格清单。
- Unity 2021 旧控制器/参数接收器兼容回归通过 **111 项**；果冻眼编辑器和组件离线回归通过 **1433 项**，最新脚本下的实际 PlayMode 多组件生命周期探针通过 **80,573 项**。
- Blender 4.5.8 后台加载正式 HoFace 适配器并通过 **5 项**形态键回归：清单内写入、清单外隔离、未追踪释放、目标禁用和旧协议拒绝。
- HoFaceStudio 本地 AI 控制通道已有版本化共享契约和 CLI：支持完整读写检查、批处理、文件/标准输入、后台截图、手动求值、临时预览覆盖和清理、录制/回放；冻结 raw/value/weight 仍待实现。
- Unity 旧 JSON/面捕调试窗口已从菜单入口撤下，旧全局调试宿主不再自动 tick；接收器 Inspector 改为启动 HoFaceStudio 并显示 Play 模式 raw 状态。
- 已删除旧 JSON 编辑器、旧面捕测试窗口、组件调试视图、旧 Unity 调试会话和旧 trace 记录器；控制器迁移工具与运行时稳定组件保留。
- 旧影子 AnimationSession、全局 DebugSettings/Host、输入 Hub、旧 Unity VTS 适配器和控制器编辑窗口已物理删除；HoVtsController 运行组件仅作为迁移兼容保留。
- Blender HoTools 侧已新增正式 **HoFace** 模块：只接收 `hoface.preview` 最终权重，维护驱动物体列表、连接状态、形态键清空和显式预检查开关；旧混合矩阵面板入口已退役，提交为 `8553a0f3`。
- 核心编译无警告；本机已有 .NET 10 SDK 与 WindowsDesktop 运行环境，暂未安装新环境。

包级验证说明：原全量 Editor 扫描因既有 MMD 编辑器的 `Volume` 类型引用列表缺失而失败；给本机临时响应文件补上工程已存在的 `Unity.RenderPipelines.Core.Runtime.dll` 后，完整扫描可编译。未修改该 MMD 代码或将此问题归因于本次核心提取。真实 Unity 隔离工程则直接编译并执行了相关面捕源码。

## 尚未完成

- WPF 曲线 / 控制点编辑器、稳定选择、精确值编辑、录制/回放及 AI 命令已有自动化覆盖；仍缺真实 VTS 设备与生产角色配合的完整制作验收。
- Unity raw 适配器的 loopback、逐帧 ACK、追踪丢失、重连时钟和输入释放已在隔离 Unity 工程验证；仍缺用户角色上的 Play 模式 Inspector 实际调试验收。
- Blender 与 Unity 协议/适配器自动验证已通过；仍需用实际模型确认 Blender 形态键写入和完整 VTS→HoFaceStudio→Blender 视觉闭环。
- WTD IR v2 在实际 Warudo Character Mod 中的构建与客户端验收。

现有稳定实现继续保留。核心里程碑通过不代表以上集成已完成；后续按独立提交更新本记录。
