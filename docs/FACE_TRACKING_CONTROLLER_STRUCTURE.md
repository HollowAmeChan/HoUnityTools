# 面捕控制器结构：Jerry 模板 vs 我们生成的

日期：2026-09-22（§5/§6 于 2026-09-23 按现状重写）。本文是**源码实测 + 一次判别性实验**的记录：
参考实现怎么搭的、我们自己的生成物长什么样、以及「Direct 树 + Write Defaults 关闭会发散」这条判别性实验。
数据来自 `.research/VRCFaceTracking-Templates` 与 `D:\UnityVrcVCC_Projects\VrcMaster`（本地检出，gitignore）
与 `.research/UnityFaceValidation` 的批处理用例。

> 怎么用 / 中间层做什么 / 什么能进树：见 [面捕工作流](FACE_TRACKING_WORKFLOW.md)、
> [面捕中间层处理](FACE_TRACKING_MIDDLE_LAYER.md)、[混合树的能力边界](BLEND_TREE_LIMITS.md)。

## 1. 参考实现：Jerry 的 ARKit 控制器长什么样

`FX - Face Tracking - ARKit Blendshapes.controller`（15823 行 YAML）的实测统计：

| 项目 | 数值 |
| --- | --- |
| 图层 | **3**（全部 Override、无 AvatarMask） |
| 状态机 | 3，其中 layer 0 的状态机**是空的**（只是一个占位/命名空间） |
| 状态 | 10 |
| **混合树** | **412**（362 Simple1D + 36 Direct + 12 FreeformCartesian2D + 2 SimpleDirectional2D） |
| MonoBehaviour | 10（`VRC Avatar Parameter Driver` 与 `VRC Tracking Control`） |
| 参数 | 206 |
| 每个状态的 Write Defaults | **全部 = 1（开）** |

### 1.1 三层各干什么

| 层 | 名字 | 作用 |
| --- | --- | --- |
| 0 | `FX - Face Tracking - ARKit Blendshapes` | 空状态机。真正的驱动在 layer 2 |
| 1 | `Tracking_State` | **门控层**：7 个状态全都播放 `_Do_Nothing.anim`，靠 `VRC Avatar Parameter Driver` 写 `State/*`、`FacialExpressionsDisabled` 等开关。Eye/Face/Lip Tracking 的启用、Visemes 开关都在这里 |
| 2 | `Face_Tracking` | **驱动层**：`FT Blendshape Driver (EDIT THIS)` 一个状态，里面是**一整棵 Direct 混合树** |

### 1.2 驱动层：一棵 Direct 树管住整张脸

根节点是一棵 **Direct 混合树，22 个子节点**。每个子节点代表一个面部区域，**用它的参数直接当子权重**做门控：

| direct 参数 | 用途 | 出现次数（全控制器） |
| --- | --- | --- |
| `OSCm/BlendSet` | 整脸淡入淡出 | 85 |
| `LipTrackingActive` | 嘴部追踪可用 | 46 |
| `EyeTrackingActive` | 眼部追踪可用 | 12 |
| `FT/DirectBlend` | 用户总开关 | 9 |
| `FaceTrackingEmulation` | 无设备时模拟 | 1 |

每个区域子节点再往下是**每张脸一块的混合树**：

- **多数是 Simple1D**：`OSCm/Proxy/FT/v2/<Shape>` 从 0 插值到 100 的姿势，两个子片段（`X 0.anim` / `X.anim`）。
- **需要二维空间的地方用 FreeformCartesian2D**：
  - **眼睑**：`(EyeLidRight, EyeSquintRight)` → 5 个**预先做好的姿势**：`Blink / Neutral / Wide / Squint / Open_Squint`。也就是说"半闭 + 眯眼"不是两条曲线相加，而是作者摆好的中间姿势。
  - **眼球**：`(EyeLeftX, EyeY)` → `Look_Neutral / In / Out / Up / Down`，每只眼一棵。
  - **眉内端**：`(BrowExpressionLeft, BrowExpressionRight)`，左右联动。
- **有耦合的地方不是 1:1 映射**：`SmileFrown` 同时驱动 `Mouth_Smile`、`Cheek_Squint`、`Mouth_Dimple`；`JawOpen` 是 `(JawOpen, MouthClosed)` 的二维树；`Brow_Down` 也被 `MouthRaiserLower` / `NoseSneer` 拉。
- **全局限制**：若干子树外面套着 `FaceTrackingLimits`。

### 1.3 参数不是直通的：三层参数链

```
FT/v2/*            109 个    ← VRCFT 发过来的原始值
  ↓ OSCmooth 预制件（31 个 OSCm/Smooth/* + OSCm/Sensitivity/* + OSCm/Remote/*）
OSCm/Proxy/FT/v2/*  31 个    ← 混合树实际读的是这一层
```

`sensitivity`（每个部位一条灵敏度曲线）、`smoothing`（本地/远端分别设）都在这一段完成，**不在控制器里**。

### 1.4 眨眼的"叠加"：EyeSync

`FT/EyeSync`（0/1）在 Shared.controller 里选择两种接法：

- **OFF** → `Proxy EyeLids (EyeSync OFF)`：左右眼睑各用各的值。
- **ON** → `Proxy EyeLid`：一棵 4 子节点 Direct 树，**左眼的值也去驱动右眼、右眼的值也去驱动左眼**，交叉权重是 `FT/EyeSyncMix`（默认 0.5）。

效果是"两眼要眨一起眨、要半闭一起半闭"，避免一只眼半闭时显得不协调。眼球注视方向也有一份同样的 `EyeSync In/Out`。

## 2. 我们原来的生成器：一层一个形态键（已废弃）

`HoFaceAnimationAssets.Generate` 曾经生成 **每个形态键一个独立 Override 图层**，每层一棵 Simple1D 树、两个片段（0 / 100）。一个 52 键的模型 ≈ **52 个图层 + 104 个片段**。

代码里的理由是：

> Independent Override layers avoid Direct-tree normalization and cross-channel attenuation.
> （独立 Override 图层避免 Direct 树的归一化与通道间削弱）

**这条理由是错的 —— 实测否掉了。**（第 5 节记录了替代实现与结果。）

## 3. 判别性实验：Direct 树到底会不会削弱

用例在 `Tests~/FaceTrackingValidation.cs`。同一个模型、同一个 Direct 树（1 图层、52 个子节点、每个子节点用自己的 ARKit 参数当权重），只改一个变量：

| 条件 | jawOpen（参数 0.6） | mouthSmileLeft（参数 0.8） | 结论 |
| --- | --- | --- | --- |
| **Direct + Write Defaults 开** | **60.0** | **80.0** | 精确等于 `参数 × 100`。**不归一化、不互相削弱** |
| Direct + Write Defaults 关（干净物体） | 0 | 0 | 归零 |
| Direct + Write Defaults 关（场景里有基础动画在写同一个键） | 98.98 → 246.28 → 1059.33 | 131.97 → 531.97 | **发散**：逐帧拿"当前值"当基准反复混合 |

`98.976` 精确等于递推 `out ← 0.6×100 + 0.4×out` 的第五项，验证了机制：**Direct 树里权重和不足 1 的那部分 `(1 − Σw)` 会与"当前值"混合；写默认值关闭时这个"当前值"永远不会被复位，于是每帧往上爬，最终发散。**

所以：

- **Direct 树本身没有问题**，Unity 官方文档也明说 Direct 就是用来 map 参数到子权重、并点名"可用于混合表情的形态键"（[Direct blending](https://docs.unity3d.com/6/Documentation/Manual/BlendTree-DirectBlending.html)）。
- **不可用的组合是「Direct 树 + Write Defaults Off」。** 而 Jerry 的控制器**所有状态都是 WD 开** —— 这才是两边真正的分岔点。

## 4. 那我们的 52 个图层怎么评价

**结论：不是"不合理"，但它是为一个已经被放弃的设计付的代价。**

- 当初选多层，表面理由是"避免 Direct 归一化"——**这条已被实验否定**。
- 真正的约束是：我们的管线**强制 Write Defaults 关闭**（`HoFaceAnimationAssets.Compile` 会拒绝 WD On）。在那条约束下 Direct 树确实不能用，`Simple1D + 独立 Override 层`是当时唯一可行的绕法。
- 但那条约束来自**早期"接管角色 Animator、把身体层和面部层叠进同一个 PlayableGraph"的设计**。那套设计已经被实测否掉，改成了**影子求值**（见设计文档 7.1）。

**关键推论：影子是隔离的，没有任何别的写入者。** WD On 会把控制器里出现过的属性每帧复位到默认值 —— 在共享 Animator 的旧设计里这会踩掉身体动画，但在只跑面部控制器的影子台上**完全无害**。也就是说：

> **"必须 WD Off" 现在是一条过期约束。**

放开它，就能换成 Jerry 那种结构，图层数从 52 降到 **1**。

## 5. 我们现在的生成物（2026-09-23 更新）

`HoFaceAnimationAssets.Generate` 产出**一个** `.controller`：

```
Ho/00 Drive (Direct)                                   ← 驱动层（「应用改动」整段重写）
  ├ 眼睑左  FreeformCartesian2D(Ho/LidLeft.X, Ho/LidLeft.Y)   权重 = Ho/Gate/Eye
  │     五格：睁大(-1,0)=wide100 / 中性(0,0)=空 / 闭(1,0)=blink100
  │           眯(0,1)=blink90+squint100 / 睁大+眯(-1,1)=wide100+squint100
  ├ 眼睑右  同上
  ├ 眼 (Direct, 权重 = Ho/Gate/Eye)    眼球 8 + 眉 5（一键一叶子直通）
  └ 唇 (Direct, 权重 = Ho/Gate/Lip)    嘴 22 + 颊 3 + 鼻 2 + 舌 1
Ho/99 (EDIT THIS)                                       ← 空层 + 空片段，永不重写
```

| 项 | 现状 |
| --- | --- |
| 图层 | **2**（驱动层 + 扩展点） |
| 驱动层根树 | Direct，**4 个子节点**（两棵眼睑 2D 树 + 眼/唇两棵区域子树） |
| 直通叶子 | **46**（眼 13 = 眼球 8 + 眉 5；唇 33） |
| 参数 | 52 个 `ARKit/<键名>` + 2 个 `Ho/Gate/*` + 4 个 `Ho/Lid*.X/.Y` = **58** |
| 门控参数默认值 | **1（开）** —— 单独打开这个资产时不是一片死脸 |
| Write Defaults | **必须开**（Direct 树的前提；`Direct + WD Off` 会被 `Compile` 直接拒绝） |

**演化路径**（每一步都是被实测推着走的）：

1. 52 个独立 Override 层 → **一棵 Direct 树平铺 52 个键** —— §3 的实验推翻了"Direct 会归一化削弱"。
2. 平铺 → **两棵区域子树**（眼 / 唇，各挂一个门控参数）—— **分组是为了让"这块算不算数"变成参数**，
   可以被任何东西驱动（会话、用户自己的层、以后的菜单），而不是只能重新生成控制器。
3. 眼睑从"三个直通叶子" → **FreeformCartesian2D 五格姿势** —— 治 `blink × squint` 叠加。
   这正是 §1.2 里参考实现的做法（姿势表数值也是照它实测抄的，见
   [混合树的能力边界](BLEND_TREE_LIMITS.md) §6）。

**幂等**：「应用改动」只重写 `Ho/00 Drive`，复用同名片段，并销毁上一版的区域子树、眼睑 2D 树、
以及已经被 2D 姿势取代的那六个直通片段（`eyeBlink*` / `eyeWide*` / `eyeSquint*`）。

**边界仍在**：只允许形态键曲线；无 Behaviour、无同步图层、无事件/对象曲线（`Compile` 会拦）。

### 5.1 每个生成物**为什么存在**（初始化到底在干什么）

> **关键一句**：**初始化 = 程序化地产出一大片「状态 + 驱动映射」**（片段 + 混合树 + 门控参数），
> 不是手搓几个参数。所以以后要加新状态，就是**加一条生成配置**（做成面板上的预设开关），
> 而不是让用户自己去编树。面板上它单开一栏（初始化）。

| 生成物 | 目的 |
| --- | --- |
| `Ho/00 Drive`（Direct 根树） | 参数 → 姿势的入口。Direct 的类型让**子节点的权重就是参数值本身**，不需要额外搬运 |
| **两棵区域子树**（眼 / 唇）+ `Ho/Gate/*` | 让"这块驱动**算不算数**"变成一个**参数** —— 于是它可以被任何东西驱动（我们的会话、用户自己的层、以后的菜单），而不是只能靠重新生成控制器来切 |
| **两棵眼睑 2D 树**（每只眼 6 格姿势） | **让会重叠的语义进同一棵树**：`blink / wide / squint` 不再是三个加数，而是**查表的坐标**。这就是"过眨眼"的结构解法（§1.2 参考实现的做法） |
| 每个姿势片段 | 作者摆的那一格：写 100（简单方向）或**一组组合值**（如 `眯 = blink 90 + squint 100`）。片段只是**数据**，不带时间轴 |
| 46 个"一键一叶子"直通片段 | 最简单的一维映射：参数 0→1，键 0→100。**凡是不会重叠的通道都用这个** |
| 四根眼睑轴参数 `Ho/Lid*.X/.Y` | **参数算术**：`开合 = blink − wide`、`眯眼 = squint`。树算不出新参数，只能消费 —— 所以这一步必须在外面（[§3 不能表达什么](BLEND_TREE_LIMITS.md)） |
| `Ho/99 (EDIT THIS)` 空层 + 空片段 | 用户的扩展点，**任何生成动作都不碰它** |

**为什么不是"几条曲线"**：如果只生成曲线（每个键一条 0→100），那"又眨又眯"就必然是
**两个形变相加**（实测加和到 200，见 [混合树的能力边界](BLEND_TREE_LIMITS.md) §1）。
生成树的目的是**把会重叠的语义变成"选一格"而不是"加两项"**。

## 6. 参考实现里我们抄了 / 还没抄的

| 参考实现的东西 | 我们 | 落在哪 |
| --- | --- | --- |
| **二维区域**（眼睑 `开合 × 眯眼` 五格姿势） | ✅ 抄了 | 生成器（§5） |
| **区域门控**（`EyeTrackingActive` / `LipTrackingActive`） | ✅ 抄了 | `Ho/Gate/*` 参数 + 两棵区域子树 |
| **参数预处理链**（平滑 / 灵敏度 / 上下限） | ✅ 抄了，但**在树外面** | 中间层（[面捕中间层处理](FACE_TRACKING_MIDDLE_LAYER.md)） |
| **EyeSync 式左右交叉混合** | ✅ 抄了语义，**实现放在 C#** | `HoFaceEyeSync`（VRCFT 也在 C#：`Correctors.BlendOpposingParams`） |
| **耦合**：`SmileFrown` 同时驱动嘴角 / 脸颊 / 酒窝；`JawOpen` 是 `(JawOpen, MouthClosed)` 的二维空间 | ❌ 没抄 | —— |
| **限制 / 修正**：若干子树外面套 `FaceTrackingLimits` | ❌ 没抄 | 中间层欠账 |
| **眼球**：SimpleDirectional2D + 四方向（可选四斜向） | ❌ 现在是直通叶子 | —— |

后三条都是同一个处方的更多例子：**会重叠的语义进同一棵树 / 参数算术留在外面**
（判据见 [混合树的能力边界](BLEND_TREE_LIMITS.md)）。

## 7. 复现

```powershell
# 控制器结构（层 / 状态 / 树骨架 / 参数驱动 / 参数表）
python .research/inspect_arkit_controller.py     # → .research/arkit-controller-report.txt
python .research/inspect_shared_controller.py    # → .research/shared-controller-report.txt

# 判别性实验（Direct 树 × Write Defaults）
# 把 Tests~/FaceTrackingValidation.cs 放进一次性 Unity 工程的 Assets/Editor，
# 跑 HoFaceTrackingValidation.RunBatch，看 HO_WDON / HO_WDOFF 两行
```

踩过的解析坑：Unity YAML 里混合树的子节点字段是 `m_Childs`（不是 `m_Children`）、类型字段是 `m_BlendType`（不是 `m_Type`）；文档 id 在 `--- !u!206 &-123` 里，`&` 后面直接换行，不能用 `split(' ')` 截。
