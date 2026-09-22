# HoUnityTools 文档

这里记录跨 Blender、Unity 和 Warudo 的工具约定，以及已经验证过的构建流程。

## 文档

- [面捕混合树入门与 ARKit 模板使用](FACE_TRACKING_TEMPLATE_GUIDE.md)：解释参数、混合树、动画绑定及 Jerry 模板的使用流程。
- [编辑器面捕调试组件设计](FACE_TRACKING_DEBUGGER_DESIGN.md)：iFacialMocap 直连、全局面板、角色组件、输出门控和 LookAt 协作。P0–P2 已实现并在独立 Unity 工程跑通自动化验收；真机与眼球骨骼验收未做。
- [面捕控制器结构：Jerry 模板 vs 我们生成的](FACE_TRACKING_CONTROLLER_STRUCTURE.md)：参考实现的三层结构、一棵 Direct 树管整张脸，以及「Direct 树 + 写默认值关闭会发散」的判别性实验。
- [Warudo FastBuild 设计与验证](WARUDO_FAST_BUILD.md)：FastBuild 的流程、SDK 约束、依赖处理和排错方法。
- [摆锤约束设计与验证](PENDULUM_CONSTRAINT.md)：摆锤约束的模型、输出绑定、水瓶液面预设与验证结论。
- [约束面板设计系统](EDITOR_UI_SYSTEM.md)：约束类 Inspector 共用的尺寸栅格、色板与自绘控件库（眨眼面板已按它重画）。
- [眨眼约束设计与验证](BLINK_CONSTRAINT.md)：自动眨眼 + 果冻眼（规则 / 弹簧 / ramp / 合并策略）。
- [注视约束设计与验证](LOOKAT_CONSTRAINT.md)：眼→头颈→脊椎的优先级瀑布、鼠标与目标物体两种驱动。
- [旧 Hotools 源码迁移说明](MIGRATION.md)：从旧 Hotools 工程迁移到当前 Unity 包的记录。

文档中的 Warudo 结论以 Warudo Mod Tool 0.14.4.8 和 Unity 2021.3.45f2 的实际构建结果为准。SDK 或 Unity 版本变化后，应重新检查构建日志和生成的 `.warudo` 内容。
