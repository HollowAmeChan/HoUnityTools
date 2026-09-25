# HoUnityTools 文档

这里记录跨 Blender、Unity 和 Warudo 的工具约定，以及已经验证过的构建流程。

## 面捕

**入口全在菜单 `HoUnityTools/面捕/` 下面那三个页：调试面板 / 控制器编辑 / 配置文件。**
角色预制件上**零组件** —— 调试状态落在 `Assets/HoFaceDebugSettings.json`，每帧由菜单宿主推。

- [**面捕工作流**](FACE_TRACKING_WORKFLOW.md)：怎么用 —— 三个页各管什么、首次接线、什么会写盘、排查清单（症状 → 先看哪）。**§2.1 是方向：终点是"动画 = 状态"，现在这套 ARKit 映射是过渡件。**
- [**参数实机验证表**](PARAMETER_DEVICE_VERIFICATION.md)：**设备实测哪些参数真的在动** —— 逐键的**语义 + 触发情况**（`§0.2`，语义抄官方、触发是实测总结）、两台各自的 `MOVES` / `WEAK` / `CONSTANT-NONZERO` / `NEVER-MOVES` 与统计量、以及**安卓 ↔ 苹果对比**（`§3`：线名集合差 22 组、量级与零点也差）。表由 `.warudo-mod-research/.tools/analyze-dump-stats.ps1` 从 `Player.log` 的自动 dump 生成。**协议里有、设备也发、读数却永远不变的，只有这张表能告诉你。**
- [**面捕中间层处理**](FACE_TRACKING_MIDDLE_LAYER.md)：**值是怎么被加工的** —— 输入行（线名 → 规范名）与输出行（规范名 → 参数）、表达式语言、曲线、有序修饰符、配置文件格式与它的精确语义。
- [**混合树的能力边界**](BLEND_TREE_LIMITS.md)：静态姿势树的代数边界与完整Animator的区别；已更正“不能写Animator参数”的旧结论，原生参数曲线在两个Unity版本实测可用。
- [**控制器完整输出与物理阶段设计**](ANIMATOR_OUTPUT_PIPELINE_DESIGN.md)：Animator参数回写、跨Layer求值时序、组件/显隐/材质MPB/引用切换的双版本实测，以及Warudo属性帧、绑定清单和果冻物理调度方案。**已按"组件值全部走 Hub"修订**：控制器只吐姿态（形态键+骨骼）与语义（Hub），材质/显隐/对象引用不再是我们写的目标。
- [**面捕设计：已验证的机制层**](FACE_TRACKING_DESIGN.md)：影子台（`shadow.Update(0f)` 一次同步求值）、键的占用表、为什么不用 PlayableGraph、唯一时钟与线程、播放模式切换时的收摊与接回、验收现状（116 条全绿 + 怎么重跑）。
- [**面捕控制器结构**](FACE_TRACKING_CONTROLLER_STRUCTURE.md)：控制器是**作品** —— 参考实现的三层结构、一棵 Direct 树管整张脸、装配模型（填动画 + 重绑形态键曲线）、命名约定与判别性实验。
- [**VTS → VB → 高质量混合树全量契约（当前设计入口）**](VTS_HIGH_QUALITY_FACE_CONTRACT.md)：98个公开VTS固定参数、52原始形变、34个原装VB V3输出、39个拟定HQ扩展和42个高级树族；含全量矩阵、条件轴及选择性填动画的fallback规则，不限定格数。[机器可读目录](VTS_HIGH_QUALITY_FACE_CATALOG.json)。
- [**VTS / VB 参数空间与轴来源**](VTS_FACE_PARAMETER_SPACES.md)：哪些参数组成2D、哪些是高维空间的条件切片、哪些应先保留1D；逐轴给出来源公式，不规定采样格数。
- [**VTS / VB 创作者路线调查**](VTS_CREATOR_WORKFLOW_RESEARCH.md)：十份预设差异、麦克风契约、官方示例实际映射与教程证据；其中动画预算仅为演算示例，采样由作者决定。
- [**面捕在 Warudo 的路线**](FACE_TRACKING_WARUDO_ROUTE.md)：那一边的产物划分（**2 mod / 5 节点**）、为什么要走它的 **Tracking 层**、Warudo 的硬约束（无 asmdef / 无 ScriptableObject / 无 DLL / 无反射 / 无 System.IO）。
- [**参数标准表**](PARAMETER_STANDARDS.md)：**下游到底认哪些名字**的权威依据（逐行表格 + 官方 URL + 未验证标记）—— VTS 追踪参数/语音/手部/控制器、VTS API 与注入规则、自定义参数、Cubism 标准参数与参数组、ARKit 52、iFacialMocap 线协议、VMC 协议地址与 HumanBodyBones、VRM 0.x/1.0、VRCFT Unified Expressions（附录）。**写任何参数名之前先查它。**

## 其他工具

- [Warudo FastBuild 设计与验证](WARUDO_FAST_BUILD.md)：FastBuild 的流程、SDK 约束、依赖处理和排错方法。
- [摆锤约束设计与验证](PENDULUM_CONSTRAINT.md)：摆锤约束的模型、输出绑定、水瓶液面预设与验证结论。
- [跟随约束坐标系规则](FOLLOW_CONSTRAINT.md)：跟随约束的坐标系与父子规则。
- [约束面板设计系统](EDITOR_UI_SYSTEM.md)：约束类 Inspector 共用的尺寸栅格、色板与自绘控件库（眨眼面板已按它重画）。
- [动画剪辑直通预览](ANIMATION_CLIP_PREVIEW.md)：填一条 clip 就能播、不需要 AnimatorController 的组件（倍速 / 拖帧 / 暂停）；含机制、已知限制与验证探针。
- [眨眼约束设计与验证](BLINK_CONSTRAINT.md)：自动眨眼 + 果冻眼（规则 / 弹簧 / ramp / 合并策略）。
- [注视约束设计与验证](LOOKAT_CONSTRAINT.md)：眼→头颈→脊椎的优先级瀑布、鼠标与目标物体两种驱动。

## 踩过的坑（按"看到什么症状"来找）

- [**踩过的坑**](pitfalls/README.md)：这一文件夹放**具体咬过我们一口的东西**（症状 → 原因 → 怎么办）：
  **面捕流水线**（包到了但脸不动 / 实时输入整体不动 / JsonUtility 丢字段）、混合树、形态键输出、
  Unity YAML 与转储、Animator IK 与更新时机、鼠标输入、液体 shader、Unity 资产、编辑器 UI 与 Playable API、
  批处理验证、Warudo 打包、**从蓝图里取证**、文档编码、仓库与提交。
  活文档只留结论；**"当初怎么被咬的"都收在这里**。

## 已归档（过程记录，不是现状）

结论都已经抽进上面的活文档；下面这些留着是**"当初凭什么这么判断"的证据**。

- [旧 Hotools 源码迁移说明](archive/MIGRATION.md)：迁移日期、两条源→目标路径与三个命名空间，以及旧 `Hotools/` 目录已删除。
- [面捕的层间分工：参数生产层 vs 驱动层](archive/FACE_TRACKING_PIPELINE_SPLIT.md)：1100 行的论证日志（含大量「作废/撤销」标记）。仍独有的：§17 参考实现两棵大树的拆法、§21.3 VRCFT `Correctors` 的三条修正。
- [面捕混合树入门与 Jerry ARKit 模板使用](archive/FACE_TRACKING_TEMPLATE_GUIDE.md)：讲 Jerry 现成模板的用法；我们改成装配模型、又改成"控制器编辑就地装配"之后，作业流程部分不再适用，但"模板里长什么样"仍是对照材料。
- [面捕的 OSC / VRCFT 后端：调查保留](archive/FACE_TRACKING_OSC_BACKEND_RESEARCH.md)：未来后端的调查（Av3Emulator 能力清单、VRCFT 链路、`forceRelevant`、OSCQuery 与发现过滤）。**首期没接 VRCFT**，留着是为了将来别重查一遍。
- [ARKit `mouthClose` 调查报告](archive/arkit-mouthclose-report.md)：一次性调查，结论已体现在中间层行为里。
- [VBridger 的中间层：一手逆向记录](archive/VBRIDGER_MIDDLE_LAYER_RESEARCH.md)：同类最成熟产品的存档格式（16 字符循环 XOR）、一行输出的全部字段、表达式语言与函数表、UI 词汇、输入曲线与校准的分层，以及它给我们的八条启示。**我们中间层的形状就是照它定的。**
- [VBridger 的输入 / 输出参数格式](archive/VBRIDGER_IO_VOCABULARY.md)：十份自带预设逐行统计出来的词汇表 —— **输入**（104 个规范名 + 每个数据源一张改名表：iFacialMocap `_L/_R`／FaceMotion3D `Left`／VTS `Left` 首字母大写；15 个 OVR viseme 的连续 + `_abs` 两条线；音频、头姿、全身骨链、`faceFound`）与**输出**（喂 VTS 就用它的追踪参数白名单 24 个、喂 VMC/VRM 就用 ARKit 原名、其余是自定义驼峰名），以及十种反复出现的映射公式与取值约定。**要对接下游时照这张表。**

文档中的 Warudo 结论以 Warudo Mod Tool 0.14.4.8 和 Unity 2021.3.45f2 的实际构建结果为准。SDK 或 Unity 版本变化后，应重新检查构建日志和生成的 `.warudo` 内容。

## 写文档的约定

**这里的 `.md` 都是"带 BOM 的 UTF-8"。** 无 BOM 的 UTF-8 在被设成 ANSI/GBK 默认的查看器里会整篇乱码（`编辑器` → `缂栬緫鍣?`），而 BOM 的作用就是让这类查看器认出 UTF-8。

要注意：**用脚本或文本工具改写 `.md` 时很容易把 BOM 丢掉**（改写 = 重写整个文件）。丢掉的症状和"文件坏了"一模一样，但文件本身永远是好的。判据：读前三个字节是不是 `EF BB BF`；不是就补回去，**只补这 3 个字节，不要重新编码正文** —— 重新编码才是真会把文件弄坏的操作。详见 [文档与编码](pitfalls/DOCS_ENCODING.md)。
