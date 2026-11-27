# Ho Unity Tools

Ho 渲染体系配套的 Unity 创作工具集，覆盖角色、动画、面捕、录制和 Warudo 工作流，并与 [HoTools Blender 插件](https://github.com/HollowAmeChan/HoTools) 联动。

## 功能

- **Ho PMX 场景导入**：原生 PMX Inspector、相对贴图、Ho lilToon 材质、透明分类、Ray 配套材质读取与材质提取。[使用说明](docs/guides/PMX_SCENE_IMPORT.md)
- **FBX 导入处理**：读取相邻配置，辅助装配骨骼显示与约束。
- **FBX 描边烘焙**：直接在 FBX 的原生 Inspector 中开启 lilToon RGBA 描边顶点色烘焙（默认关闭，无需 GD）。[使用说明](docs/guides/FBX_OUTLINE_VERTEX_COLORS.md)
- **动画工具**：生成形态键 / 物体 / 组件开关动画，处理动画轨道，以及不依赖控制器的剪辑预览。
- **角色约束**：跟随、漂浮、摆锤、弹簧、眨眼 / 果冻眼，以及眼睛和头部注视。
- **外置 Transform 约束**：Position、Rotation、Scale、Parent、Aim，显式 Target + 多 Sources，可集中挂在外部对象上；无需 VRC SDK 或 Animation Rigging。[使用说明](docs/guides/TRANSFORM_CONSTRAINTS.md)
- **Ho CombineArmature**：独立衣服骨架装配，支持直接合并或保留层级的原生约束；不依赖 MA / NDMF / VRC SDK。[使用说明](docs/guides/COMBINE_ARMATURE.md)
- **面捕**：角色组件持有 VTS 参数规则和独立控制器，支持实时输入、曲线编辑、临时覆盖、隔离与录样。[使用说明](docs/guides/VTS_CHARACTER_COMPONENTS.md)
- **快速渲染**：游戏视图截图、离线 MP4 录制与 Scene / Game 相机同步。
- **播放调试**：播放速度面板、PhysBone / Collider 数值改动回写，回写时保留对象引用。
- **Warudo FastBuild**：从 Prefab 构建 Mod、管理导出工作区并复核产物组件。
- **编辑器辅助**：骨骼绘制、Built-in 管线的 Scene MSAA 开关，以及部分作者工具组件的 VRChat 构建清理。

常用面板位于 `HoUnityTools` 菜单，组件位于 `Add Component > HoUnityTools`。详细说明见 [文档入口](docs/README.md)。

## Ho 渲染体系相关仓库

| 仓库 | 用途 |
| --- | --- |
| [HoUrp17.3.0](https://github.com/HollowAmeChan/HoUrp17.3.0) | URP 渲染基座 |
| [HoUrpConfig17.0.3](https://github.com/HollowAmeChan/HoUrpConfig17.0.3) | URP Shader 配置 |
| [lilToon-URP-Extensions](https://github.com/HollowAmeChan/lilToon-URP-Extensions) | 光照、阴影、反射与后处理扩展 |
| HoShader | 下一代材质底座，项目正在迁移，仓库目前为私有 |

## 日期版本与安装包

推送默认分支后自动发布日期版本（`YYYYMMDD-HHMMSS`）与 Unity 安装包；在 GitHub Releases 下载 `.tgz`，通过 Package Manager 从 tarball 安装。版本对齐、手动发布与安装说明见 [发布说明](.github/RELEASING.md)。
