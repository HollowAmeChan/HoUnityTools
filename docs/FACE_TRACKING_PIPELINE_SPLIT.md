# 面捕的层间分工：参数生产层 vs 驱动层

日期：2026-09-22。本文回答三个问题：**OSC 中间层是不是"参数生产层"**、**果冻眼该放哪**、**这套分工怎么同时喂给 VRC 和 Warudo**。

证据来源：本地解压的 5 个商业面捕包（见第 5 节），共 31 个控制器，全部逐个解析。解析脚本与报告在 `.research/`（gitignore）：
`inspect_ft_controllers.py`、`inspect_layer.py`、`ft-controllers-overview.txt`、`ft-detail-*.txt`、`lay-*.txt`。

## 1. 结论先说

**是。而且"控制器 + 混合树做动画、中间层做参数"这套分工，在这 31 个控制器里没有一个例外。**

真正的分界不是"层多还是层少"，而是**层按功能分段，还是按形态键逐条分段**：

| | 层的作用 | 数量 |
| --- | --- | --- |
| 参考实现 | **功能分段**：门控 / 平滑 / 二值化 / 果冻 / 修正 | FT 相关 **5~8 层** |
| 我们旧实现 | **逐键分段**：一个形态键一层 | 52 层 |

我们改成「1 层 + 一棵 Direct 树」之后，正好等于参考实现里的**驱动层**那一层。**缺的不是层数，是中间那几段功能层。**

## 2. 参考实现的标准骨架（以 kipfel 的 ARKit 模板为例，5 层）

| 层 | 名字 | 干什么 | 状态 |
| --- | --- | --- | --- |
| 0 | `FX - Face Tracking - ARKit Blendshapes` | **空状态机**，只是个命名空间 | 无 |
| 1 | `Tracking_State` | **门控**：眼/唇/表情/viseme 的开关，靠 `VRC Avatar Parameter Driver` 写 `State/*` | 全播 `_Do_Nothing`，WD=1 |
| 2 | `Face_Tracking_Blendshape_Driver` | **驱动**：**一棵 Direct 树，33 个子节点**。每个部位一个子节点，gate 用 `EyeTrackingActive` / `LipTrackingActive` | 1 个状态，WD=1 |
| 3 | `Face_Tracking_OSCmooth_Binary_Gen` | **参数生产（二值）**：Direct 树 22 子节点，每参数一个，输出 0/1 | 1 个状态，WD=1 |
| 4 | `Face_Tracking_OSCmooth_Smoothing_Gen` | **参数生产（平滑）**：2 个状态 `OSCmooth_Local` / `OSCmooth_Net`，各是一棵 **Direct 树 30 子节点**，每参数一个 | WD=1 |

三个关键细节：

1. **驱动层只做"参数 → 姿势"**，没有任何时间性逻辑。它就是一棵 Direct 树，Input 是 `OSCm/Proxy/FT/v2/*`。
2. **平滑层和驱动层在同一个控制器里，是两个图层**，不是外部程序。JS 里 `OSCm/BlendSet` 是它们的总闸（`_OSCmooth_*` 里每个子节点都挂这个 direct 参数）。
3. **参数是三元组**：`OSCm/Local/*`（原始）→ `OSCm/Smooth/*` → `OSCm/Proxy/*`（喂给驱动树）。`OSCm/Remote/*` 是网络侧另一套，用于远端平滑。

## 3. 果冻眼放哪：参考实现的两种做法

### 3.1 作为独立的"参数生产层"（Shinano v1.32）

Shinano 的控制器 8 层，FT 相关的排布：

```
[0] Eye Tracking State          ← 门控
[1] Lip Tracking State          ← 门控
[2] FacialTracking              ← 驱动（一棵大 Direct 树）
[3] JellyEye                    ← 果冻眼，独立一层
[4] QPro_Toggle
[5] AFK-FT Control
[6] _OSCmooth_Binary_Gen        ← 参数生产
[7] _OSCmooth_Smoothing_Gen     ← 参数生产
```

参数表里有 **`Blink_JellyEye`（Float）**，`JellyEye` 层的状态机是：

```
JellyIdle   （默认，WD=1）
Step 1      （WD=1）
Step 2      （WD=1）→ 播放 JellyEye.anim
```

也就是说：**果冻眼是"每帧推进、带历史"的一层**，和 OSCmooth 是同一类东西（produces a parameter / a pose over time），只是它自己一个层。

### 3.2 作为"修正层"（Mafuyu v1.04a）

Mafuyu 25 层里 FT 相关的是：

```
[19] EyeTrackingState
[20] FacialTrackingState
[21] FacialTracking
[22] MouthDefaultCorrection     ← 果冻/修正
[23] _OSCmooth_Binary_Gen
[24] _OSCmooth_Smoothing_Gen
```

`MouthDefaultCorrection` 的状态机是 `Idle`（默认）→ `MouthDefault`（播放 `MouthDefault.anim`）。说明书写得很清楚：

> 顔の基本表情シェイプキーが口元の動きに合わせて基本に戻れるように設定する補正用レイヤーがあります。
> （有一个修正层，让脸部基本表情形态键随着嘴部动作回到基准）

**这正是"嘴一动，基础表情键回默认"** —— 也就是我们设计文档里"基础动画和面捕抢同一个键"那个问题的工业解法：**用一层专门负责把基础表情拉回去**，而不是让面捕去猜。

### 3.3 所以你的判断是对的

> 「这是不是直接就是可以放果冻眼参数生产的层啊，然后混合树里面直接做果冻眼，这样做会比我们自己搓一个参数靠谱的多对吧」

**对。** 果冻/弹簧是个**时间滤波器**，它需要历史状态。放在 animator 里做的三个好处，正好是我们 C# 手搓的短板：

1. **时序天然正确** —— 它和姿势求值在同一次动画求值里，不存在"我算完了动画又覆盖掉"的竞态。
2. **可看可调** —— 在 Animator 窗口里能直接看到、能单独禁用、能调参数，不需要重新编译。
3. **可搬运** —— 同一层可以用同一套逻辑构建成 Warudo 蓝图节点（见第 4 节）。

代价是：**果冻的归属必须唯一**。既然参考实现把它做在控制器里，那我们 `HoBlinkConstraint` 里的果冻就应该**改成"只生产参数"，或者干脆退役**，不能两边都写眼睛键。这条要单独定。

## 4. VRC 与 Warudo 的对应：四段流水线

你描述的 Warudo 蓝图（裸参 → 参数平滑 → switch → 设到 SkinnedMeshRenderer）和 VRC 那边是**同一条流水线**，只是宿主不同：

| 阶段 | 语义 | VRC 里的形态 | Warudo 蓝图的形态 |
| --- | --- | --- | --- |
| ① 输入 | 裸参数（ARKit / UE / SRanipal） | `FT/v2/*`（206 个参数） | 裸参节点 |
| ② 参数生产 | 平滑 / 灵敏度 / 二值化 / 上下限 / **果冻** | `_OSCmooth_*_Gen` 图层 + `<Param> Root` 树 | 参数平滑节点 / 自定义节点 |
| ③ 门控 | 眼 / 唇 / 表情 / 远端 分组开关 | `Tracking_State`、`*TrackingState` 图层 | switch 切换节点 |
| ④ 输出 | 参数 → 姿势 → 形态键 | Direct 树 → 片段曲线 | 设到 SkinnedMeshRenderer |

**结论：中间层应该按这四个阶段设计，而不是绑定到某一种宿主。** 这样：

- 出 VRC：②③④ 编译成图层 + Direct 树（就是现在这些商业包的样子）。
- 出 Warudo 蓝图：②③④ 编译成节点 —— 你们很快要加的蓝图构建工具正好接得上。
- 出我们自己（Ho 渲管线 / 纯 Unity 调试）：②③④ 就是我们现在这套影子求值 + 生成器。

我们现在的实现只覆盖了 ①③④（输入、分组门控、Direct 树），**②完全空缺** —— 这就是"我们缺的不是层数，是中间那段"的具体所指。

## 5. 证据：解压出来的 5 个商业包

解压位置：`E:\BaiduSyncdisk\大师\_VRC\_面捕\_extracted\`（6540 个文件 / 78.9 MB）。

| 包 | 性质 | 控制器 | 层 | 状态 | 混合树 |
| --- | --- | --- | --- | --- | --- |
| `kipfel_FT_1.04` | Adjerry91 模板 + Breakout 拆分 | 22 个 | 4~7 | 4~12 | 3221（SRanipal）/ 386（ARKit） |
| `Mafuyu_FTsetting(v1.04a)` | 整只角色手工调 | `FxLayer_Mafuyu_FT` | 25（FT 占 6） | 101 | 414 |
| `Manuka_FTsetting(V1.03)` | 同上 | `MANUKA_FX_FT` | 34（FT 占 ~7） | 111 | 424 |
| `Shinano_FTsetting_v1.32` | 同上，最复杂 | `FX_FT_added 2` | 8（全是 FT） | 78 | **626**（其中 101 棵 Direct） |
| `Kipfel_ear_motion` | 附带耳朵动作 | — | — | — | — |

三族共同的形状：

- **层按功能分段，不按形态键分段。**
- **WD 全部为 1（写默认值开）** —— 与说明书的「WD(WriteDefault) Onを基準に製作されました」一致，也印证了判别性实验的结论（Direct 树必须配 WD 开）。
- 混合树全部是「一棵 Direct 根树 + 每部位一棵子树」，Direct 树承担**门控**（`EyeTrackingActive` / `LipTrackingActive` / `OSCm/BlendSet`）。
- 参数链固定是 `Local → Smooth → Proxy` 三段。
- 都有**独立的修正/果冻层**。

## 6. 对我们下一步的直接影响

1. **补 ②「参数生产」这一段。** 最小可用版：把「平滑 + 灵敏度」做成可选的生成层，果冻单独一层。要不要对齐 OSCmooth 的 `Local/Smooth/Proxy` 三段命名，需要定（对齐好处是能在 VRC 侧直接复用别人的成品配置）。
2. **果冻的归属只能有一个。** 要么控制器侧生产、我们 `HoBlinkConstraint` 只读不写；要么反过来。两边都写必然打架。
3. **门控颗粒度对齐参考实现**：眼 / 唇 / 表情 三组，而不是我们现在的 5 组区域。参考实现是 `EyeTrackingActive` + `LipTrackingActive` 就分完了。
4. **中间层要按阶段抽象**，别写死成"生成图层"。因为 Warudo 蓝图那条路要用同一份中间表示。
