# HoUnityTools 文档

**唯一入口。** 现在在跑的那套方案只有四份权威文档 + 命名权威；其余都在 `pitfalls/`（踩坑）与 `archive/`（过程记录）。
（2026-09-29 重组：原 20 篇顶层文档 → 4 篇权威 + 归档；审查与判定见 [REORG_PLAN_2026-09-29.md](REORG_PLAN_2026-09-29.md)。）

## 现在在跑什么

面捕主线：**VTS / iPhone 裸输入 → 中间层（profile 里的输入行 / 表达式 / 曲线）→ 控制器（混合树）→ 角色与 Warudo**。
角色预制件上**零组件**：调试状态落在 `Assets/HoFaceDebugSettings.json`，三个页都在菜单 `HoUnityTools/面捕/` 下（调试面板 / 控制器编辑 / 配置文件）。

| 想看什么 | 去哪 |
| --- | --- |
| **整条路怎么走**：层间分工、数据流、每一步的权威文件与工具 | [PIPELINE.md](PIPELINE.md) |
| **控制器长什么样**：树型 / 区域门 / 2D 表 / Direct 张量积表 / 变体开关 / 槽位命名 / 装配与检查流程 / **混合树实测边界** | [CONTROLLER.md](CONTROLLER.md) |
| **每根轴的现状**：表达式原文 / 曲线 / 修饰符 / 谁消费 / 实测定论与刻度 | [AXES.md](AXES.md) |
| **参数**：三套名字（VTS 线名 / ARKit 名 / 我们的规范名）怎么对应、设备逐键实测、哪些我们不用 | [PARAMETERS.md](PARAMETERS.md) |
| **名字怎么起**（写任何树名/槽位名之前先查它） | [FACE_TRACKING_NAMING.md](FACE_TRACKING_NAMING.md) · [机器可读目录](VTS_HIGH_QUALITY_FACE_CATALOG.json) · `Runtime/FaceTracking/HoFaceTrackingChannels.cs` 的 `Names`（52 条规范名） |
| **工具怎么跑**（生成 / 接线 / 检查 / 录样速查速统速对 / 资产速查） | [`Tools~/FaceTracking/README.md`](../Tools~/FaceTracking/README.md) |
| **动态参数（语义输出 → Warudo Hub）** | [FACE_TRACKING_DYNAMIC_PARAMETERS.md](FACE_TRACKING_DYNAMIC_PARAMETERS.md) |
| **Warudo 那一边**（mod / 节点 / 硬约束 / 跨仓规矩） | [FACE_TRACKING_WARUDO_ROUTE.md](FACE_TRACKING_WARUDO_ROUTE.md) |
| **实测是怎么来的** | [measurements/README.md](measurements/README.md) |

## 其它工具（与面捕无关）

都在 [完善的功能/](完善的功能/)：Warudo FastBuild · 摆锤约束 · 跟随约束 · 约束面板设计系统 · 动画剪辑直通预览 · 眨眼约束（含果冻眼） · 注视约束。

## 踩过的坑

[pitfalls/](pitfalls/) —— 按"看到什么症状"来找（**症状 → 原因 → 怎么办**）：面捕流水线、动态参数五次改设计、混合树、形态键输出、
Unity YAML 与转储、Animator IK 与更新时机、鼠标输入、液体 shader、Unity 资产、编辑器 UI 与 Playable API、批处理验证、
Warudo 打包、从蓝图里取证、文档编码、仓库与提交。
**活文档只留结论；"当初怎么被咬的"都收在这里。**

## 归档（过程记录，不是现状）

[archive/](archive/) —— 结论都已抽进上面的活文档；这里留的是**"当初凭什么这么判断"的证据**，以及被推翻的方案。
决策日志（按日期，只增不改）在 [DECISIONS.md](DECISIONS.md)；原 `VTS_HQ_CONTROLLER.md`（279 KB 的决策日志 + 现状混合体）已拆成
[AXES.md](AXES.md)（现状）与 [DECISIONS.md](DECISIONS.md)（历史），原文件保留为存根（仓库里有 48 处引用指向它）。

## 写文档的约定

**这里的 `.md` 都是"带 BOM 的 UTF-8"。** 无 BOM 的 UTF-8 在被设成 ANSI/GBK 默认的查看器里会整篇乱码（`编辑器` → `缂栬緫鍣?`），而 BOM 的作用就是让这类查看器认出 UTF-8。

要注意：**用脚本或文本工具改写 `.md` 时很容易把 BOM 丢掉**（改写 = 重写整个文件）。丢掉的症状和"文件坏了"一模一样，但文件本身永远是好的。判据：读前三个字节是不是 `EF BB BF`；不是就补回去，**只补这 3 个字节，不要重新编码正文** —— 重新编码才是真会把文件弄坏的操作。详见 [文档与编码](pitfalls/DOCS_ENCODING.md)。
