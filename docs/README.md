# HoUnityTools 文档

这里记录跨 Blender、Unity 和 Warudo 的工具约定，以及已经验证过的构建流程。

## 文档

- [面捕混合树入门与 ARKit 模板使用](FACE_TRACKING_TEMPLATE_GUIDE.md)：解释参数、混合树、动画绑定及 Jerry 模板的使用流程。
- [编辑器面捕调试组件设计](FACE_TRACKING_DEBUGGER_DESIGN.md)：iFacialMocap 直连、全局面板、角色组件、输出门控和 LookAt 协作。P0–P2 已实现并在独立 Unity 工程跑通自动化验收；真机与眼球骨骼验收未做。
- [Warudo FastBuild 设计与验证](WARUDO_FAST_BUILD.md)：FastBuild 的流程、SDK 约束、依赖处理和排错方法。
- [摆锤约束设计与验证](PENDULUM_CONSTRAINT.md)：摆锤约束的模型、输出绑定、水瓶液面预设与验证结论。
- [旧 Hotools 源码迁移说明](MIGRATION.md)：从旧 Hotools 工程迁移到当前 Unity 包的记录。

文档中的 Warudo 结论以 Warudo Mod Tool 0.14.4.8 和 Unity 2021.3.45f2 的实际构建结果为准。SDK 或 Unity 版本变化后，应重新检查构建日志和生成的 `.warudo` 内容。
