# HoUnityTools 文档

这里记录跨 Blender、Unity 和 Warudo 的工具约定，以及已经验证过的构建流程。

## 文档

- [**面捕工作流**](FACE_TRACKING_WORKFLOW.md)：怎么用 —— 六步接线、面板四个分区各回答什么、什么会写盘、排查清单。
- [**面捕中间层处理**](FACE_TRACKING_MIDDLE_LAYER.md)：中间层做什么（增益/死区/平滑/双眼同步/区域门控/轴生产）、顺序、判据，以及**哪些处理是隐式的**。
- [**混合树的能力边界**](BLEND_TREE_LIMITS.md)：它能算的是一台"以参数为变量的多项式机器"（乘积、加权平均都能做），做不到 `min`/`max`/除法/需要记忆的东西，**而且写不了 Animator 参数**。含实测证据与"该放树里还是放外面"的判断流程。
- [面捕控制器结构：Jerry 模板 vs 我们生成的](FACE_TRACKING_CONTROLLER_STRUCTURE.md)：参考实现的三层结构、一棵 Direct 树管整张脸，以及「Direct 树 + 写默认值关闭会发散」的判别性实验。
- [编辑器面捕调试组件设计](FACE_TRACKING_DEBUGGER_DESIGN.md)：机制层（影子台、占用表、为什么不用 PlayableGraph）。**状态与 UI 部分已过期**，看前三份。
- [Warudo FastBuild 设计与验证](WARUDO_FAST_BUILD.md)：FastBuild 的流程、SDK 约束、依赖处理和排错方法。
- [摆锤约束设计与验证](PENDULUM_CONSTRAINT.md)：摆锤约束的模型、输出绑定、水瓶液面预设与验证结论。
- [约束面板设计系统](EDITOR_UI_SYSTEM.md)：约束类 Inspector 共用的尺寸栅格、色板与自绘控件库（眨眼面板已按它重画）。
- [眨眼约束设计与验证](BLINK_CONSTRAINT.md)：自动眨眼 + 果冻眼（规则 / 弹簧 / ramp / 合并策略）。
- [注视约束设计与验证](LOOKAT_CONSTRAINT.md)：眼→头颈→脊椎的优先级瀑布、鼠标与目标物体两种驱动。
- [旧 Hotools 源码迁移说明](MIGRATION.md)：从旧 Hotools 工程迁移到当前 Unity 包的记录。

## 已归档（过程记录，不是现状）

结论都已经抽进上面的活文档；下面这些留着是**"当初凭什么这么判断"的证据**。

- [面捕的层间分工：参数生产层 vs 驱动层](archive/FACE_TRACKING_PIPELINE_SPLIT.md)：1100 行的论证日志（含大量「作废/撤销」标记）。仍独有的：§17 参考实现两棵大树的拆法、§21.3 VRCFT `Correctors` 的三条修正。
- [面捕混合树入门与 Jerry ARKit 模板使用](archive/FACE_TRACKING_TEMPLATE_GUIDE.md)：讲 Jerry 现成模板的用法；我们改成自己生成后，使用说明部分不再适用。
- [ARKit `mouthClose` 调查报告](archive/arkit-mouthclose-report.md)：一次性调查，结论已体现在中间层行为里。

文档中的 Warudo 结论以 Warudo Mod Tool 0.14.4.8 和 Unity 2021.3.45f2 的实际构建结果为准。SDK 或 Unity 版本变化后，应重新检查构建日志和生成的 `.warudo` 内容。
