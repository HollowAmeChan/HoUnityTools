# Ho Unity Tools

本 Unity 包联动 [HollowAmeChan/HoTools](https://github.com/HollowAmeChan/HoTools) Blender 插件。


## 面板

| 功能 | 入口 |
| --- | --- |
| 动画处理 | `HoUnityTools/动画处理` |
| FBX 导入处理中控 | `HoUnityTools/HoFBX导入处理` |
| Warudo Prefab 快速构建 | `HoUnityTools/FastBuildWarudoMod` |

选中 FBX 资产后，也可以使用 `Assets/HoUnityTools/HoFBX导入处理` 打开同一个 FBX 面板并自动扫描相邻配置文件。这是面板的上下文快捷入口，不是另一套功能。

在 Project 窗口中右键 Prefab，可以使用 `Assets/HoUnityTools/FastBuildWarudoMod` 打开 FastBuild 面板并把该 Prefab 作为源 Prefab。顶栏入口 `HoUnityTools/FastBuildWarudoMod` 在没有选中 Prefab 时也能打开面板，源 Prefab 可以在面板里手动指定；窗口已经有源 Prefab 时，顶栏入口不会覆盖它。

构建结束后 FastBuild 会直接读取 `.warudo` 产物复核组件是否真的挂上：结果打印到控制台，完整报告写入 `Library/HoFastBuildWarudoMod/last-verification.txt`，面板底部也可以展开查看或重新复核。

面板的“Warudo 工作区”一栏可以直接列出、切换、新建、删除 `ExportSettings` 里的工作区（Export Profile），并显示活动工作区的 Mod 名称、作者、版本、资产目录、导出目录和 SDK 校验结果；需要编辑完整字段时可以一键打开 uMod 官方设置窗口。

> **注意**：安装方式会影响 FastBuild 依赖列表的默认勾选。把本包下载成 GitHub ZIP 解压到 `Packages/`（目录名变成 `HoUnityTools-master`）与用 `file:`／git URL 安装，早期的包目录判断只认后者，会静默地不复制任何脚本源码，产出没有任何运行时程序集、组件全是 Missing Script 的 Mod。现版本已改为按包的实际解析路径判断，三种安装方式一致。详见 [Warudo FastBuild 设计与验证](docs/WARUDO_FAST_BUILD.md#安装方式会改变构建结果重要)。

## 组件

在 Inspector 中使用 `Add Component`，组件路径如下：

| 组件 | Add Component 路径 |
| --- | --- |
| 跟随约束 | `HoUnityTools/Constraints/Ho Follow Constraint` |
| 漂浮约束 | `HoUnityTools/Constraints/Ho Floating Constraint` |
| 摆锤约束 | `HoUnityTools/Constraints/Ho Pendulum Constraint` |
| Scene/Game View 相机同步 | `HoUnityTools/Ho Scene To Game View Sync` |
| 骨骼绘制器 | `HoUnityTools/Ho Bone Renderer` |

FBX 导入处理中控可以根据配置自动添加骨骼绘制器和 Unity 标准约束。`HoImportedConstraintMarker` 是导入器内部标记组件，不应手动添加。

## 文档

- [Warudo FastBuild 设计与验证](docs/WARUDO_FAST_BUILD.md)
- [摆锤约束设计与验证](docs/PENDULUM_CONSTRAINT.md)
- [跟随约束坐标系规则](docs/FOLLOW_CONSTRAINT.md)
- [眨眼约束设计（自动眨眼 + 果冻眼）](docs/BLINK_CONSTRAINT.md)
- [旧 Hotools 源码迁移说明](docs/MIGRATION.md)
