# 功能指南

这里按功能整理使用方法、实现约定和验证边界。各页可能包含带日期的演进记录，阅读时区分当前说明和历史实验。

## 角色装配与导出

| 功能 | 说明 |
| --- | --- |
| [FBX 描边顶点色烘焙](FBX_OUTLINE_VERTEX_COLORS.md) | 默认关闭，按 FBX 烘焙 lilToon RGBA 方向 / 厚度；无需 GD |
| [Rig Constraint IR v3](RIG_CONSTRAINT_IR_V3.md) | 常用复制／Armature、独立 Aim 语义与三后端能力边界 |
| [Ho CombineArmature](COMBINE_ARMATURE.md) | 独立衣服骨架装配；直接合并或保留层级的位置/旋转约束；不依赖 MA、NDMF 或 VRC SDK |
| [Warudo FastBuild](WARUDO_FAST_BUILD.md) | 角色 Mod、脚本暂存与产物复核；面捕控制器随角色打包 |

## 角色约束

| 功能 | 说明 |
| --- | --- |
| [外置 Transform 约束](TRANSFORM_CONSTRAINTS.md) | 显式 Target + 多 Sources 的 Position／Rotation／Scale／Parent／Aim，支持偏移捕获与手动添加 |
| [跟随约束](FOLLOW_CONSTRAINT.md) | 锚点坐标系、位置/旋转跟随和更新时机 |
| [摆锤约束](PENDULUM_CONSTRAINT.md) | 摆锤与液面参数、材质契约和验证记录 |
| [眨眼约束](BLINK_CONSTRAINT.md) | 自动眨眼、果冻眼及形态键输出的合并方式 |
| [注视约束](LOOKAT_CONSTRAINT.md) | 目标选择、头颈/眼睛跟随、鼠标输入和形态键输出 |

约束互相覆盖或与物理抢骨骼时，另查 [Animator IK 与更新时机](../pitfalls/UNITY_IK_AND_TIMING.md)；形态键清场和重复注册问题见 [形态键输出](../pitfalls/SHAPE_KEY_OUTPUT.md)。

## 动画与录制

| 功能 | 说明 |
| --- | --- |
| [动画剪辑直通预览](ANIMATION_CLIP_PREVIEW.md) | 不依赖角色控制器的剪辑预览及 Playable 求值边界 |
| [VMD 播放与绑定](VMD_PLAYBACK.md) | 中文 VMD Inspector、Bone Bindings、WTD 主骨架与表情缺项检查 |
| [播放速度面板](PLAY_SPEED_PANEL.md) | 播放速度调试与速度条 |
| [快速渲染面板](QUICK_CAPTURE_PANEL.md) | 游戏视图/相机截图、透明背景和离线录制 |

## 编辑器与界面

| 功能 | 说明 |
| --- | --- |
| [BIRP Scene MSAA](BIRP_SCENE_MSAA.md) | Built-in 管线的 Scene 视图多重采样开关 |
| [编辑器 UI 设计约定](EDITOR_UI_SYSTEM.md) | 约束面板的栅格、令牌和自绘控件 |

面捕工具单独见 [面捕入口](../face-tracking/README.md)；返回 [文档总入口](../README.md)。
