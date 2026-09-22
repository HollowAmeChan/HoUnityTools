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

## 7. 三条已定的决策

| 决策 | 内容 |
| --- | --- |
| **HoBlinkConstraint 退役** | 果冻眼改由控制器侧生产。`HoBlinkConstraint` **暂时保留不删**，但不再是眼睛键的写入者。原则上"连约束脚本都可以整体转向控制器图层"是允许的方向。 |
| **中间层命名对齐 OSCmooth** | 参数链用 `Local → Smooth → Proxy` 三段命名，生成层的命名也照它的风格。好处：能直接复用 VRC 生态里别人调好的配置与调参习惯。 |
| **果冻归属控制器侧** | 参考实现的两种做法都看过了（Shinano 的独立 `JellyEye` 层 / Mafuyu 的 `MouthDefaultCorrection` 修正层），选前者：独立层、每帧推进、带历史。 |

**推论（要一起守的）**：既然果冻归控制器侧，**眼睛键的写入者就只能有一个**。`HoFaceOutputOwnership` 那套键级占用要扩展成"哪一段拥有哪个键"，否则果冻层和约束脚本会互相覆盖 —— 这正是参考实现里 `MouthDefaultCorrection` 存在的原因：基础表情的回退由**专门一层**负责，而不是让面捕去猜。

## 8. 傻瓜化：复杂度只能存在于生成物里

担心是对的，而且参考实现早就把这件事解决了。看它实际的安装面：

`Prefabs/Advance/Breakout/ARkit Blendshapes - Eye.prefab` **整个预制件只有一个组件** —— VRCFury 的 `FullController`，内容就两行引用：

```yaml
type: {class: FullController, ns: VF.Model.Feature, asm: VRCFury}
data:
  controllers:
  - controller: .../Breakout/FX - ARkit - Eye.controller     # 一个控制器
  prms:
  - parameters:  .../Breakout/Para - ARkit - Eye.asset        # 一份参数表
```

**层和树是构建期由 VRCFury 合并出来的，作者和用户都不碰。** 傻瓜化靠的是这五条：

| 手法 | 实际做法 |
| --- | --- |
| **按区域拆分安装** | Breakout 预制件：Eye / Eyebrow / Lip / Lip Extras / Tongue / Tongue Steps / Pupil Dilation，用哪个拖哪个 |
| **合并放到构建期** | VRCFury `FullController` 把控制器并进 FX 层，用户看不到合并后的 626 棵树 |
| **参数声明式** | `Para - *.asset` 就是一份 `VRCExpressionParameters` 列表，不是手工接线 |
| **平滑也是声明式的** | VRCFury 自带 `smoothedPrms` 字段；OSCmooth 是"配置 + 生成"。生成物带 `_Gen` 后缀，名字本身就在说"这是生成的，别手改" |
| **设备差异用变体** | `forPico4Pro` / `forQuestPro` / `GXR` / `default` 各一个预制件变体，换变体而不是改图层 |

安装说明也就一句话：「Unity 工具栏 → FT Patch → Start」。

### 对我们的三条硬要求

1. **用户的输入面不许随生成物一起变厚。** 面板上永远是那几个开关（连不连、驱不驱、哪几组、平滑强度、果冻开关），层数由配置推导出来。生成 5 层还是 30 层，用户都不用打开 Animator。
2. **生成物永不手改。** 生成层带前缀分段（`Ho/01 Raw`、`Ho/02 Smooth`、`Ho/03 Gate`、`Ho/04 Drive` 之类），文档和面板上都写死"要改就改配置重新生成"。参考实现用 `_Gen` 后缀表达同一件事。
3. **影子架构是挡住复杂度的那道墙。** 对角色真实模型而言，无论控制器里有多少层，永远只有"N 个形态键被面捕会话接管"这一件事可见。用户不需要理解内部结构就能知道自己在控制什么 —— 这一点是我们比参考实现更有优势的地方，别丢掉。

### Warudo 蓝图同理

蓝图也必须是**生成**的，而且要能折叠：中间表示按**四段**（①输入 ②参数生产 ③门控 ④输出）组织，生成蓝图时每段折成一个节点组/子流程。用户在蓝图里看到的是四个块，不是一个几百节点的网。

## 9. 中间层落地形态（命名对齐后）

按四段 + OSCmooth 命名，目标产物：

```
第 1 段  输入          ARKit/<键>            ← 已有（手机直连写入）
第 2 段  参数生产      OSCm/Local/<键>       ← 原始值中转（对齐 OSCmooth 的 Local）
                      OSCm/Smooth/<键>      ← 平滑后的值
                      OSCm/Proxy/<键>       ← 驱动树实际读的值
                      OSCm/BlendSet         ← 总闸
         （+ 果冻/修正：独立的 Ho/Jelly 段，产出自己的参数）
第 3 段  门控          EyeTrackingActive / LipTrackingActive   ← 收敛成参考实现的两组总闸
第 4 段  输出          ARKit Direct 树 → 形态键          ← 已有（上一轮刚改完）
```

对齐命名的一个具体好处：`OSCm/Proxy/FT/v2/<Shape>` 这套名字在 VRC 生态里是通行约定，我们的生成物能直接套用别人的调参经验，也能让别人看懂。

## 10. 生成物 vs 用户要改的东西

这是"傻瓜化"背后真正难的一处：**控制器是生成的，但用户又要能改它做复杂逻辑** —— 两者天然冲突（重新生成会吃掉用户的手改）。参考实现的解法是**把扩展点显式命名出来**：

| 出处 | 手法 |
| --- | --- |
| Jerry 的 ARKit 模板 | **状态名直接标可改性**：`FT Blendshape Driver (EDIT THIS)` / `FT Local Root (DO NOT EDIT)` / `FT Remote Root (DO NOT EDIT)` |
| Mafuyu v1.04a 说明书 | 让用户**改一个指定片段**：「MouthDefaultState 的动画文件里，加入你想在面部动作时回默认的形态键」 |

所以正确形态不是"生成一个不许碰的控制器"，而是**生成物里留出被命名的扩展点**。

### 两个产物，别混为一谈

「生成」和「只在运行时存在」是两件事。必须分开：

| 产物 | 在哪 | 谁改 | 作用 |
| --- | --- | --- | --- |
| **生成的面部控制器** | 落盘成 `.controller` 资产 | **用户可以改**（这就是"面向用户的最高级东西"） | 用户在这里加自己的混合树逻辑 |
| **预览副本** | 每次会话在内存里重建（`Compile`） | **谁都不改**，永远从上面那个 + 配置重建 | 带拥有权过滤，喂给影子台 |

**只在内存里生成的控制器是没法给用户改的** —— Animator 窗口打不开它，改不了结构，也存不下来。所以"运行时生成"只适用于第二个产物；第一个必须落盘。我们现在的实现其实已经是这个分工，只是没把它讲清楚。

### 由此定的四条

1. **生成物带命名分段，并显式标注可改性**。生成层/状态用 `Ho/... (生成·勿改)`，留给用户的扩展点用 `(EDIT THIS)`，与参考实现同一套表达。
2. **重新生成必须保留用户扩展点**。要么把用户那块引用进新生成物，要么生成到新文件并明确告诉用户"你改的那块还在原文件"。不能静默覆盖手改。这条要写在按钮上，不能只写在文档里。
3. **用户面对的最高级东西是混合树**，这一点确认成立 —— 参考实现 414~626 棵树就是靠手写树完成的耦合与二维空间。但我们的**接受范围仍然是有边界的**：只允许形态键曲线 + 标准状态/树，无 Behaviour、无事件、无对象曲线、无同步图层；超界明确报错，不静默丢弃。
4. **一个要知道的细节**：预览编译只替换片段、不动树结构，所以**用户加的树结构会保留**；但**曲线会按分组与通道选择被过滤** —— 用户手加的键能不能活下来，取决于它落在哪个分组、以及那个分组有没有开。这个行为要在面板上说清楚，否则"我明明加了这个键怎么没反应"会很难查。
