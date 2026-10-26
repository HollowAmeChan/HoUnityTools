# Ho Unity Tools

Ho 渲染体系配套的 Unity 创作工具集，覆盖角色、动画、面捕、录制和 Warudo 工作流，并与 [HoTools Blender 插件](https://github.com/HollowAmeChan/HoTools) 联动。

## 功能

- **FBX 导入处理**：读取相邻配置，辅助装配骨骼显示与约束。
- **FBX 描边烘焙**：直接在 FBX 的原生 Inspector 中开启 lilToon RGBA 描边顶点色烘焙（默认关闭，无需 GD）。[使用说明](docs/guides/FBX_OUTLINE_VERTEX_COLORS.md)
- **动画工具**：生成形态键 / 物体 / 组件开关动画，处理动画轨道，以及不依赖控制器的剪辑预览。
- **角色约束**：跟随、漂浮、摆锤、弹簧、眨眼 / 果冻眼，以及眼睛和头部注视。
- **外置 Transform 约束**：Position、Rotation、Scale、Parent、Aim，显式 Target + 多 Sources，可集中挂在外部对象上；无需 VRC SDK 或 Animation Rigging。[使用说明](docs/guides/TRANSFORM_CONSTRAINTS.md)
- **Ho CombineArmature**：独立衣服骨架装配，支持直接合并或保留层级的原生约束；不依赖 MA / NDMF / VRC SDK。[使用说明](docs/guides/COMBINE_ARMATURE.md)
- **面捕**：实时输入调试、表达式 / 曲线中间层、控制器装配、配置文件编辑与动态语义参数 Hub。
- **快速渲染**：游戏视图截图、离线 MP4 录制与 Scene / Game 相机同步。
- **播放调试**：播放速度面板、PhysBone / Collider 数值改动回写，回写时保留对象引用。
- **Warudo FastBuild**：从 Prefab 构建 Mod、管理导出工作区并复核产物组件。
- **编辑器辅助**：骨骼绘制、Built-in 管线的 Scene MSAA 开关，以及部分作者工具组件的 VRChat 构建清理。

常用面板位于 `HoUnityTools` 菜单，组件位于 `Add Component > HoUnityTools`。详细说明见 [文档入口](docs/README.md)。

## Ho 渲染体系相关仓库

| 仓库 | 用途 |
| --- | --- |
| [HoTools](https://github.com/HollowAmeChan/HoTools) | Blender 角色、模型、贴图、动画与自动化工具 |
| [HoGLTF](https://github.com/HollowAmeChan/HoGLTF) | Blender glTF 导出与 Ho 材质契约桥接 |
| [HoUrp17.3.0](https://github.com/HollowAmeChan/HoUrp17.3.0) | URP 渲染基座 |
| [HoUrpConfig17.0.3](https://github.com/HollowAmeChan/HoUrpConfig17.0.3) | URP Shader 配置 |
| [lilToon](https://github.com/HollowAmeChan/lilToon) | 角色 / NPR 着色器 |
| [lilToon-URP-Extensions](https://github.com/HollowAmeChan/lilToon-URP-Extensions) | 光照、阴影、反射与后处理扩展 |
| [lilToon-UnityGLTF-Extensions](https://github.com/HollowAmeChan/lilToon-UnityGLTF-Extensions) | glTF 材质契约导入 |

## 可选材质合集

- [HoShader](https://github.com/HollowAmeChan/HoShader)：宝石、皮肤、绒毛等独立材质与 Shader 实验合集，按需选用。
