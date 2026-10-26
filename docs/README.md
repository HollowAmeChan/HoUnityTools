# HoUnityTools 文档

按功能查使用说明，按症状查排错记录。面捕的结构、参数和历史记录有单独入口。

## 按任务查找

| 想做什么 | 从这里开始 |
| --- | --- |
| 在 FBX 导入时烘焙 lilToon 描边修正顶点色 | [FBX 描边顶点色烘焙](guides/FBX_OUTLINE_VERTEX_COLORS.md) |
| 给角色安装独立衣服、合并或约束骨架 | [Ho CombineArmature](guides/COMBINE_ARMATURE.md) |
| 使用跟随、摆锤、眨眼、注视约束 | [功能指南：角色约束](guides/README.md#角色约束) |
| 预览动画、调整播放速度、截图或离线录制 | [功能指南：动画与录制](guides/README.md#动画与录制) |
| 把角色或面部控制器导出给 Warudo | [Warudo FastBuild](guides/WARUDO_FAST_BUILD.md) |
| 使用或维护面捕工具 | [面捕入口](face-tracking/README.md) |
| 排查面捕、资产引用、Unity 时序或打包问题 | [排错索引](pitfalls/README.md) |
| 排查 VRChat 组件白名单或播放调参回写 | [VRC 构建组件](pitfalls/VRC_BUILD_COMPONENT_WHITELIST.md) · [播放调参回写](pitfalls/PLAYMODE_VALUE_ROUNDTRIP.md) |
| 修改 Inspector 界面 | [编辑器 UI 设计约定](guides/EDITOR_UI_SYSTEM.md) |
| 查询实现方案的比较与验证边界 | [调查记录](research/README.md) |

FBX 导入工具与通用动画轨道工具目前没有独立的完整使用指南；相关实现分别在 [FBX 导入工具](../Editor/HoFbxImport/) 和 [动画工具](../Editor/AnimationTools/)。

## 文档分工

| 位置 | 内容 |
| --- | --- |
| [guides/](guides/README.md) | 各功能的使用、实现说明、限制和验证结果 |
| [face-tracking/](face-tracking/README.md) | 面捕主题导航，连接流水线、控制器、轴、参数和作者工具 |
| [pitfalls/](pitfalls/README.md) | 已遇到的问题：症状、原因、处理办法 |
| [research/](research/README.md) | 方案比较、源码调查、尚未定位或尚未验证的结论 |
| [archive/](archive/README.md) | 旧方案、阶段进度、验收快照与整理记录 |

面捕规范的固定路径仍保留在本目录：`PIPELINE.md`、`CONTROLLER.md`、`AXES.md`、`PARAMETERS.md`、`FACE_TRACKING_DYNAMIC_PARAMETERS.md` 和机器可读词表。入口见 [面捕文档表](face-tracking/README.md#规范与工具)。

历史决策原文 [DECISIONS.md](DECISIONS.md) 保留原路径和小节编号；[VTS_HQ_CONTROLLER.md](VTS_HQ_CONTROLLER.md) 与 [measurements/README.md](measurements/README.md) 保留为旧引用的导航入口。

## 阅读与维护

- 支持范围和验证结果以各页明确标出的版本、日期及复现入口为准；历史进度不自动成为当前验收结论。
- 同一项规则集中维护在对应主题文档；入口只负责导航，不再重复参数表、公式和逐轴验收日志。
- 移动 Unity 包内文档时同时移动 `.meta`，保留 GUID，并更新引用它的 Markdown 链接。
- `docs/**/*.md` 使用带 BOM 的 UTF-8；编码处理见 [文档与编码](pitfalls/DOCS_ENCODING.md)。
- 提交前运行 [本地链接检查](../Tools~/Documentation/README.md)：`python Tools~/Documentation/check-links.py`。

本轮整理前的入口全文保存在 [2026-10-24 旧入口快照](archive/DOCS_INDEX_BEFORE_2026-10-24.md)，其中的用户约定和旧待办可继续追溯。
