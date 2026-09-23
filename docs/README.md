# HoUnityTools 文档

这里记录跨 Blender、Unity 和 Warudo 的工具约定，以及已经验证过的构建流程。

## 文档

- [**面捕工作流**](FACE_TRACKING_WORKFLOW.md)：怎么用 —— 六步接线、面板五个分区各回答什么（栏名不带序号）、什么会写盘、排查清单。
- [**面捕中间层处理**](FACE_TRACKING_MIDDLE_LAYER.md)：中间层做什么（增益/死区/平滑/双眼同步/区域门控/轴生产）、顺序、判据，以及**哪些处理是隐式的**。
- [**混合树的能力边界**](BLEND_TREE_LIMITS.md)：它能算的是一台"以参数为变量的多项式机器"（乘积、加权平均都能做），做不到 `min`/`max`/除法/需要记忆的东西，**而且写不了 Animator 参数**。含实测证据与"该放树里还是放外面"的判断流程。
- [面捕控制器结构：Jerry 模板 vs 我们的做法](FACE_TRACKING_CONTROLLER_STRUCTURE.md)：参考实现的三层结构、一棵 Direct 树管整张脸、**模板（树形）+ 动画文件夹（姿势）+ 驱动对象（写谁）→ 装配**，以及「Direct 树 + 写默认值关闭会发散」的判别性实验。
- [编辑器面捕调试组件设计](FACE_TRACKING_DEBUGGER_DESIGN.md)：机制层（影子台、占用表、为什么不用 PlayableGraph）。**状态与 UI 部分已过期**，看前三份。
- [Warudo FastBuild 设计与验证](WARUDO_FAST_BUILD.md)：FastBuild 的流程、SDK 约束、依赖处理和排错方法。
- [摆锤约束设计与验证](PENDULUM_CONSTRAINT.md)：摆锤约束的模型、输出绑定、水瓶液面预设与验证结论。
- [约束面板设计系统](EDITOR_UI_SYSTEM.md)：约束类 Inspector 共用的尺寸栅格、色板与自绘控件库（眨眼面板已按它重画）。
- [动画剪辑直通预览](ANIMATION_CLIP_PREVIEW.md)：填一条 clip 就能播、不需要 AnimatorController 的组件（倍速 / 拖帧 / 暂停）；含机制、已知限制与验证探针。
- [眨眼约束设计与验证](BLINK_CONSTRAINT.md)：自动眨眼 + 果冻眼（规则 / 弹簧 / ramp / 合并策略）。
- [注视约束设计与验证](LOOKAT_CONSTRAINT.md)：眼→头颈→脊椎的优先级瀑布、鼠标与目标物体两种驱动。

## 已归档（过程记录，不是现状）

结论都已经抽进上面的活文档；下面这些留着是**"当初凭什么这么判断"的证据**。

- [旧 Hotools 源码迁移说明](archive/MIGRATION.md)：迁移日期、两条源→目标路径与三个命名空间，以及旧 `Hotools/` 目录已删除。
- [面捕的层间分工：参数生产层 vs 驱动层](archive/FACE_TRACKING_PIPELINE_SPLIT.md)：1100 行的论证日志（含大量「作废/撤销」标记）。仍独有的：§17 参考实现两棵大树的拆法、§21.3 VRCFT `Correctors` 的三条修正。
- [面捕混合树入门与 Jerry ARKit 模板使用](archive/FACE_TRACKING_TEMPLATE_GUIDE.md)：讲 Jerry 现成模板的用法；我们改成装配模型后，作业流程部分不再适用，但"模板里长什么样"仍是对照材料。
- [面捕的 OSC / VRCFT 后端：调查保留](archive/FACE_TRACKING_OSC_BACKEND_RESEARCH.md)：未来后端的调查（Av3Emulator 能力清单、VRCFT 链路、`forceRelevant`、OSCQuery 与发现过滤）。**首期没接 VRCFT**，留着是为了将来别重查一遍。
- [ARKit `mouthClose` 调查报告](archive/arkit-mouthclose-report.md)：一次性调查，结论已体现在中间层行为里。

文档中的 Warudo 结论以 Warudo Mod Tool 0.14.4.8 和 Unity 2021.3.45f2 的实际构建结果为准。SDK 或 Unity 版本变化后，应重新检查构建日志和生成的 `.warudo` 内容。

## 踩过的坑（按"看到什么症状"来找）

- [**踩过的坑**](pitfalls/README.md)：这一文件夹放**具体咬过我们一口的东西**（症状 → 原因 → 怎么办）：
  Unity YAML 与转储、混合树、形态键输出、Animator IK 与更新时机、鼠标输入、液体 shader、
  Unity 资产、编辑器 UI 与 Playable API、批处理验证、Warudo 打包、文档编码、仓库与提交。
  活文档只留结论；**"当初怎么被咬的"都收在这里**。

## 写文档的约定

**这里的 `.md` 都是"带 BOM 的 UTF-8"。** 无 BOM 的 UTF-8 在被设成 ANSI/GBK 默认的查看器里会整篇乱码（`编辑器` → `缂栬緫鍣?`），而 BOM 的作用就是让这类查看器认出 UTF-8。

要注意：**用脚本或文本工具改写 `.md` 时很容易把 BOM 丢掉**（改写 = 重写整个文件）。丢掉的症状和"文件坏了"一模一样，但文件本身永远是好的。判据：读前三个字节是不是 `EF BB BF`；不是就补回去，**只补这 3 个字节，不要重新编码正文** —— 重新编码才是真会把文件弄坏的操作。
