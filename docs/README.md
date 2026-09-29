# HoUnityTools 文档

**唯一入口。** 现在在跑的那套方案只有四份权威文档 + 命名权威；其余都在 `pitfalls/`（踩坑）与 `archive/`（过程记录）。
（2026-09-29 重组：原 20 篇顶层文档 → 4 篇权威 + 归档；审查与判定见 [REORG_PLAN_2026-09-29.md](archive/REORG_PLAN_2026-09-29.md)。）

## 现在的状态（唯一有效口径）

* ⚠️ **旧计划、旧记录、旧小节里的数字与约定一律无效，永远以当前为准**：同一件事有两个版本 ⇒ **以晚的为准**；
  **数量（参数 / 树 / 槽位 / 行数 / 轴数）不在文档里定义**（随精调一直在变，要看就现跑 `Tools~/FaceTracking/` 里的工具）。
* **验收基准 = 隔离控制器，但它是临时工作台**：要调的轴 / 区先剪成单区控制器（rig 的 `FT/Diagnostics/`），验过再合并回大控制器；
  **验完的那批连同小 profile 用完即删**（09-29 用户已删 `DIAG_MouthCore_Aligned*` 那批）⇒ 权威只剩两处：
  **生成器 / 检查器里的那组值**（`Tools~/FaceTracking/`）与 **rig 里手调的资产**。
  联合件要**拼**在已验过的隔离件上（`isolate-trees.py --base`：骨架＝权威件、同 ID 冲突以权威件为准），别重剪 —— 否则源资产里的旧值会悄悄顶掉验过的值。
  诊断件按**分层命名** `DIAG_L<n>_<名字>`：**L 越大越靠上（合成得越多）**，L1 = 从生产件直接剪 / 改出来的一块，往上逐层合成；
  ⚠️ **没有「L0 / 总控制器」** —— 生产件**就是最高的那一层**，合成到它由**用户手动复制**，脚本只写 rig 的 `Diagnostics/`。
  「谁合谁」只在 `Tools~/FaceTracking/controller-parts.json` 里写一次 ⇒ `compose-controller.py` 一条命令重生成（09-29 起）。
  ⇒ **大控制器与那份大 profile 是「等待被剪枝」的东西**，里面的旧刻度、旧约定**不构成依据**。
* **验收进度**：**嘴的情绪–Open 轴已验收**、**鼻子已过关**（09-29 用户定：顶刻度 1 + 输出规则补 0.05 去噪台阶）；
  **眼 / 眉 / 颊 / 下巴 / 舌 / 风格形态还没验收** —— 用户正在逐轴精调。**状态点永远由用户在 Animator 里手调**（中间层只回答「这根线现在是多少」）。
* **统一形态**（2026-09-29 用户定）：**眼 / 眉 / 嘴全部统一成「sad–smile 轴 × Open 轴」**；
  不再有各自独立的一套切换（猫嘴那类形态是**由东西驱动**的，不是独立的一套）。
* **已定的具体口径**（用户逐条答复，别再来回问）：
  * `MouthCore` 刻度 = **生成器 / 检查器里那组值**（隔离件已删；历史见 `git` 与 [archive/](archive/)）；
  * `MouthShift` **就是 4 格 T 形**；`Mouth/Jaw`、`Mouth/Y`、`Lid/Form` 的两版 ⇒ **以晚的为准**；
  * **眉 Form 还没开始做**、**猫嘴开关还没隔离验收** ⇒ 暂时以晚的为准；
  * **下巴左右那两格**：新的优先；
  * `Gate/Expr/*`：**新的为准**（新模式没有独立切换，统一成 sad–smile × Open）；
  * **风格形态之间先不做互相覆盖**（09-29 用户定）：只留各形态自己的门，避免测试一开始就耦合。
  * ⭐ **规则三「关块」+ 块序 = 归属序**（09-29 用户定，第一条落在 V嘴）：「V嘴 还需要关掉 mouthcore（包括猫嘴变体）」⇒ 控制器里那一整块**单独一条门**
    （契约行 `Ho/Drive/Gate/MouthCore`，默认 1），**行写在主动关它的那个形态的块里**（`Ho/Style/MouthCoreGate = 1 - out("Ho/Style/InvertedV")`；再在猫嘴那条契约行**下面**压一条同名的 `Ho/Drive/Style/CatMouth = out(同名) * (1 - 倒V)` 当第二道保险）。
    ⚠️ **一条规则由"谁主动产生它"拥有** ⇒ 块序按归属排（`Cheek` → `CatMouth` → 噘嘴）：**想整块删掉某个功能时，它的规则是挨在一起的**。见 [AXES.md §8](AXES.md)。
    ⚠️ 关块的粒度 = **控制器里那个孩子**（`MouthCoreRollSwitch` 一个）；**别复用形态门** —— 那会把整嘴平移与嘴宽一起关掉 ✗。
  * ⭐ **style 状态的触发逻辑有标准写法**（09-29 用户实测定，倒V 上验证「效果极其好」）：判据做**斜坡** → `平滑 T` 去抖 → `维持` 硬切；去抖挂**判定行**，换算 `持续 ≈ 2.1203 × T` ⇒ [AXES.md §8](AXES.md)。**新增 style 状态照它抄。**
    ✅ 已按同一形状改过的：**倒V**（进 0.3 / 退 0.1）、**猫嘴**（09-29 拆成 `Read` / `CornerGate` / 判定 / 开关 / 发布 五行 ＋ 进 0.2 / 退 0.4，语义与旧的一行式逐字相同）。**鼓嘴还没拆**（判定仍是一行）—— 下次动它时照抄。

## 现在在跑什么

面捕主线：**VTS / iPhone 裸输入 → 中间层（profile 里的输入行 / 表达式 / 曲线）→ 控制器（混合树）→ 角色与 Warudo**。
角色预制件上**零组件**：调试状态落在 `Assets/HoFaceDebugSettings.json`，三个页都在菜单 `HoUnityTools/面捕/` 下（调试面板 / 控制器编辑 / 配置文件）。

| 想看什么 | 去哪 |
| --- | --- |
| **整条路怎么走**：层间分工、数据流、每一步的权威文件与工具 | [PIPELINE.md](PIPELINE.md) |
| **控制器长什么样**：树型 / 区域门 / 2D 表 / Direct 张量积表 / 变体开关 / 槽位命名 / 装配与检查流程 / **混合树实测边界** | [CONTROLLER.md](CONTROLLER.md) |
| **每根轴的现状**：表达式原文 / 曲线 / 修饰符 / 谁消费 / 实测定论与刻度 | [AXES.md](AXES.md) |
| **参数**：三套名字（VTS 线名 / ARKit 名 / 我们的规范名）怎么对应、设备逐键实测、哪些我们不用 | [PARAMETERS.md](PARAMETERS.md) |
| **名字怎么起**（写任何树名/槽位名之前先查它） | [CONTROLLER.md](CONTROLLER.md) §3（命名形状）· [机器可读目录](VTS_HIGH_QUALITY_FACE_CATALOG.json) · `Runtime/FaceTracking/HoFaceTrackingChannels.cs` 的 `Names`（52 条规范名） |
| **工具怎么跑**（生成 / 接线 / 检查 / 录样速查速统速对 / 资产速查） | [`Tools~/FaceTracking/README.md`](../Tools~/FaceTracking/README.md) |
| **动态参数（语义输出 → Warudo Hub）** | [FACE_TRACKING_DYNAMIC_PARAMETERS.md](FACE_TRACKING_DYNAMIC_PARAMETERS.md)（活的，没被合并） |
| **Warudo 那一边**（mod / 节点 / 硬约束 / 跨仓规矩） | [PIPELINE.md](PIPELINE.md) §7 |
| **实测是怎么来的** | [measurements/README.md](measurements/README.md) |

## 调试分析流程

**在隔离里调 → 录样 → 查 / 统 / 对 → 分析出落点 → 把工具对齐到资产 → 检查器打分 → 合并回去。**

每一步的命令与规矩写在 **[`Tools~/FaceTracking/README.md`](../Tools~/FaceTracking/README.md) A 节**（**唯一出处**，这里不重复）。
口径（旧数字无效 / 以晚的为准 / 数量不写死）见本页最上面。

## 其它工具（与面捕无关）

都在 [完善的功能/](完善的功能/)：Warudo FastBuild · 摆锤约束 · 跟随约束 · 约束面板设计系统 · 动画剪辑直通预览 · 眨眼约束（含果冻眼） · 注视约束 · 播放速度面板（速度条）。

## 踩过的坑

[pitfalls/](pitfalls/) —— 按"看到什么症状"来找（**症状 → 原因 → 怎么办**）：面捕流水线、动态参数五次改设计、混合树、形态键输出、
Unity YAML 与转储、Animator IK 与更新时机、鼠标输入、液体 shader、Unity 资产、编辑器 UI 与 Playable API、批处理验证、
Warudo 打包、从蓝图里取证、文档编码、仓库与提交。
**活文档只留结论；"当初怎么被咬的"都收在这里。**

## 归档（过程记录，不是现状）

[archive/](archive/) —— 结论都已抽进上面的活文档；这里留的是**"当初凭什么这么判断"的证据**，以及被推翻的方案。
决策日志（按日期，只增不改）在 [DECISIONS.md](DECISIONS.md)；原 `VTS_HQ_CONTROLLER.md`（279 KB 的决策日志 + 现状混合体）已拆成
[AXES.md](AXES.md)（现状）与 [DECISIONS.md](DECISIONS.md)（历史），原文件保留为存根（仓库里有 48 处引用指向它）。

## 待办（不在文档区，等你说不在调 Unity 时改）

`.cs` 注释里有 **7 处**指向 docs 的路径已经过期（**都是注释，不影响运行**；改 `.cs` 会触发你 Unity 重编译，所以留着）。
按「内容现在在哪」改，别指回 archive：

| 注释里写的 | 该改成 |
| --- | --- |
| `docs/FACE_TRACKING_NAMING.md` | `docs/CONTROLLER.md`（§3 命名形状） |
| `docs/FACE_TRACKING_CONTROLLER_STRUCTURE.md` | `docs/CONTROLLER.md` |
| `docs/BLEND_TREE_LIMITS.md` | `docs/CONTROLLER.md`（§8 混合树实测边界） |
| `docs/FACE_TRACKING_MIDDLE_LAYER.md` | `docs/PIPELINE.md`（§3 中间层） |
| `docs/EDITOR_UI_SYSTEM.md` · `docs/LOOKAT_CONSTRAINT.md` · `docs/PENDULUM_CONSTRAINT.md` | `docs/完善的功能/` 下同名文件 |

另外 `HoFaceControllerSkeletonBuilder.cs` 里还有两处旧值要一起改：① 类注释写着「43 个参数 + 27 棵树 + 89 个空槽位」——
按口径**不该在文档/注释里写死数量**，那句删掉或改成「跑 `check-controller.ps1` 看当前值」；② `NoseUpTicks = { 0f, 0.7f }` 要改成 `{ 0f, 1f }`（2026-09-29 用户把鼻子顶刻度定成 1，生成器与检查器都已对齐）。

## 写文档的约定

**这里的 `.md` 都是"带 BOM 的 UTF-8"。** 无 BOM 的 UTF-8 在被设成 ANSI/GBK 默认的查看器里会整篇乱码（`编辑器` → `缂栬緫鍣?`），而 BOM 的作用就是让这类查看器认出 UTF-8。

要注意：**用脚本或文本工具改写 `.md` 时很容易把 BOM 丢掉**（改写 = 重写整个文件）。丢掉的症状和"文件坏了"一模一样，但文件本身永远是好的。判据：读前三个字节是不是 `EF BB BF`；不是就补回去，**只补这 3 个字节，不要重新编码正文** —— 重新编码才是真会把文件弄坏的操作。详见 [文档与编码](pitfalls/DOCS_ENCODING.md)。
