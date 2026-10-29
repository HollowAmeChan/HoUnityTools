# 面捕

> 当前使用入口见 [VTS 角色组件使用](../guides/VTS_CHARACTER_COMPONENTS.md)，架构边界见 [VTS 角色组件架构](VTS_COMPONENT_ARCHITECTURE.md)。旧的 Warudo 参数处理节点、HoFace 控制求解节点和 HoFT AssetBundle 仅在归档中保留。

面捕主线是 **VTS / iPhone 原始输入 → 中间层 → 面部控制器与语义输出 → Unity / Warudo**。新实现先看上面的组件架构；旧流水线、控制器与参数文档用于维护和迁移。

## 规范与工具

| 想查什么 | 文档或工具 |
| --- | --- |
| **新规划**：角色组件中枢、控制器、LookAt、Unity 调试和迁移边界 | [VTS 角色组件架构](VTS_COMPONENT_ARCHITECTURE.md) |
| 整条链的分工、输入输出和资产归属 | [组件架构](VTS_COMPONENT_ARCHITECTURE.md) · [WTD 记录](WTD_COMPONENT_MIGRATION.md) |
| 控制器结构、绑定属性和角色配置 | [角色组件使用](../guides/VTS_CHARACTER_COMPONENTS.md) |
| 原始线名、姿态字段、标定和设备输入 | [角色组件使用](../guides/VTS_CHARACTER_COMPONENTS.md) |
| 旧动态语义参数 Hub 设计 | [归档记录](../archive/FACE_TRACKING_DYNAMIC_PARAMETERS.md) |
| 生成、隔离、检查、录样和统计 | [FaceTracking 作者工具](../../Tools~/FaceTracking/README.md) |
| 机器可读的命名与参数对应 | [词表](../VTS_HIGH_QUALITY_FACE_CATALOG.json) |
| 包内配置与控制器如何复制使用 | [Profiles](../../Editor/FaceTracking/Profiles/README.md) · [Controllers](../../Editor/FaceTracking/Controllers/README.md) |
| Warudo 角色打包（HoFT 独立导出已废弃） | [FastBuild](../guides/WARUDO_FAST_BUILD.md) |

历史规范保留在 `docs/archive/`，本页提供当前组件路线的入口。

## 修改与验证

当前操作从 [VTS 角色组件使用](../guides/VTS_CHARACTER_COMPONENTS.md) 开始：角色持有规则和控制器，Unity 调试只选择对象，Warudo 侧保留网络接收、标定及角色输入。旧文档中的独立控制器 Bundle 路线仅供历史参考。

1. 调整单个区域时，先在隔离控制器与小配置中验证。
2. 使用录样、统计和检查工具判断结果；参数数量和刻度以生成器、检查器及实际资产为准。
3. 联合验证应建立在已验证的部分上；最终合并到用户维护的生产资产由用户手动完成。
4. 面捕实现横跨包与 Warudo Mod 时，两端都要同步并验证。同步规则见 [仓库与提交](../pitfalls/REPO_AND_GIT.md)。

具体命令集中在 [作者工具说明](../../Tools~/FaceTracking/README.md)，本页不复制命令和参数表。

## 排错与历史

| 情况 | 去哪里 |
| --- | --- |
| 数据到了但脸不动、配置换了不生效 | [面捕流水线排错](../pitfalls/FACE_TRACKING_PIPELINE.md) |
| Hub / 动态参数的实现取舍 | [动态参数排错记录](../pitfalls/FACE_TRACKING_DYNAMIC_PARAMETERS.md) |
| 混合树权重或状态触发不符合预期 | [混合树排错](../pitfalls/BLEND_TREE_TRAPS.md) · [轴与状态](../AXES.md) |
| Warudo 产物 Missing Script、安全校验或节点取证 | [打包与工具链](../pitfalls/BUILD_AND_TOOLING.md) · [Warudo 取证](../pitfalls/WARUDO_INSPECTION.md) |
| 早期方案、设备调查与阶段验收记录 | [历史索引](../archive/README.md) |
| 原始决策及旧小节编号 | [决策日志](../DECISIONS.md) |

此前总入口里记录的逐轴验收进度与待办，保存在 [旧入口快照](../archive/DOCS_INDEX_BEFORE_2026-10-24.md)。它主要记录 2026-09-29 的状态，本轮整理没有重新验收各轴，也没有据此更改公式或角色资产。

返回 [文档总入口](../README.md)。
