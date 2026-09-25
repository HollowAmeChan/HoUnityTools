# 控制器的输出范围与实测能力：姿态、语义、物理阶段

日期：2026-09-25；**2026-09-26 ~ 27 按"组件值全部走 Hub"与"非 float 类不做"修订过**。
本文是**"我们到底写哪些东西、凭什么这么定"的正本**；动态参数那条通道的形状见
[动态参数（语义输出）](FACE_TRACKING_DYNAMIC_PARAMETERS.md)。
**本轮做的是独立 Unity 实验**：没有动另一工程正在定型的 Warudo 节点，**也没有实现 §6 ~ §9 那些扩展**
（那几节是提案，各自标了 ⚠️）。

## 1. 一句话范围

**控制器吐"姿态"（形态键 + 骨骼，曲线绑定）与"语义"（写进 Hub，按名字）；适配器那一层没有了 —— 组件读 Hub。**

| 种类 | 现在怎么办 |
| --- | --- |
| 形态键权重 | 曲线绑定，照旧（生成期前缀 `SK/`） |
| Transform / 骨骼 | 曲线绑定，照旧（生成期前缀 `BT/`） |
| 语义 / 动态参数 | 写进 **`HoFaceSemanticHub`**（生成期前缀 `P/`），按名字写、按名字读 |
| ❌ Bool / 离散开关（部件 active、Renderer enabled） | **不做**。控制器要开关就走 Hub 槽，由**作者自己的组件**读 |
| ❌ 组件 float/int 档位 | **不做**（同上，走 Hub） |
| ❌ Vector / Quaternion / Color 组件字段 | **不做**（同上） |
| ❌ 材质属性（MPB） | **不做** —— 控制器不写材质 |
| ❌ Object 引用（换材质 / 贴图） | **不做**（本来也不能插值，只能离散） |

**为什么那几类不做**：它们的**能力上限取决于我们能不能把值读回来**；读回来要么反射（UMod 禁）、
要么需要一个能被动画的已知类型 —— 成本远高于收益，而"下游要读"这件事语义槽已经覆盖了。
当年按"七种属性 + 一组注册组件适配器 + 批量应用节点"设计的经过，见
[踩过的坑 · 动态参数](pitfalls/FACE_TRACKING_DYNAMIC_PARAMETERS.md) §7。

⚠️ **收窄的代价（必须承认）**：Hub 的槽是 **float**。
所以"离散开关 / 枚举档位 / 向量 / 颜色 / 对象引用"这类**非 float 语义**经过 Hub 时要自己定编码
（例如 0/1 当假/真、三个槽当一个向量、或者干脆不做）。这不是疏漏，
是为了"一个中控组件 + 按名字读"这个统一形态付的价。
§2 的实测证明 Unity 自己能动画这些类型，所以**将来要放开时不必重测**。

## 2. 实测：Unity 到底能动画哪些东西

环境：**Unity 2021.3.45f2**（与 Warudo Mod 工程一致）和 **Unity 6000.3.15f1**（本包调试环境）。
各自独立的一次性工程，普通 Unity 组件，不加载 Warudo、Animancer 或 VRC SDK。
除版本行外，两份 59 行结果逐行一致；数值/时序检查通过。

| 能力 | 实验 | 结果与实际意义 |
| --- | --- | --- |
| **曲线写 Animator 参数** | 输入 P 混合两个叶子，叶子把 Derived 分别写 0/1 | P=.75 时 `GetFloat(Derived)=.75`，`IsParameterControlledByCurve=True` |
| **参数驱动下一 Layer** | Layer0 写 Derived，Layer1 用 Derived 控制另一个树 | P 从 .25 变 .75，第一次求值下游仍 25，第二次才 75；本实验为**一轮求值延迟** |
| **脚本 float 字段** | strength 端点 0/10 | P=.75 得到 7.5 |
| **脚本 bool/int** | 端点 0/1、0/10 | P=.25 时 bool 为 true、int 为 3；P=.75 时 int 为 8；这里使用 FloatCurve 绑定 |
| **Vector/Color 字段** | vector.x 端点 0/2、tint.r 端点 0/1 | P=.75 得到 1.5 与 .75 |
| **部件/Renderer 开关** | `GameObject.m_IsActive`、`Renderer.m_Enabled` | P=0 关，P=.1 已开；本实验不是以 .5 作为阈值 |
| **材质 float/color** | `_ProbeFloat` 端点 0/4、`_Color.r` 端点 0/1 | P=.75 时 Renderer 级 MPB 读到 3、.75；sharedMaterial 仍是原值 0 |
| **材质对象替换** | object-reference curve 在 t=.5 切材质 | t=0 是 MaterialA，t=.6 是 MaterialB |
| **手动求值已禁用 Animator** | 独立 rig 保持 active，Animator.enabled=false | `Animator.Update` 在两个版本仍能求值；这不证明禁用整个 rig 也能求值 |
| **时间推进** | 0→1 的一秒动画 | 连续 `Update(0)` 不前进；两次 `Update(.1)` 得到 .2 |
| **显式同帧阶段** | A 求值→GetFloat→B.SetFloat→B 求值 | 输入 .25/.75/.1，当次下游分别 25/75/10，没有帧间等待 |

上述是**编辑器内 batchmode/nographics 实验**，验证引擎求值与可读属性，不是 Warudo 进程里的全链路验收，
也不包含最终画面质量、所有 Shader、所有 Rig 类型和所有 Layer 组合。
"一轮求值延迟"是本实验的具体结果，不应扩大成所有 Controller/Playables 组合的时序保证。

⚠️ **这些实测现在只用得上前两行 + 时间那两行**（形态键、骨骼、显式阶段）；
其余几行（脚本字段 / 开关 / 材质 / 对象引用）**留作能力证据**：**将来要放开时不用重测**，
但**不属于我们的写入范围**（§1）。

材料：`.research/controller-capabilities/Unity2021`、`Unity6`；结果各在 `capability-results.txt`。
复核脚本 `verify_results.py` 及 `verification.json` 在上一级。实验工程和生成资产均在 gitignore 目录中。

### 2.1 一条被纠正的旧结论

旧文档写过"**Unity 原生支持动画曲线驱动 Animator float 参数，是 VRChat 专属**" —— **这是错的**。
本轮用普通 `.anim` + `AnimatorController`，绑定 `path="" / type=Animator / propertyName="Derived"`
直接验证通过，两个 Unity 版本都通过。
[Unity 参数曲线说明](https://docs.unity3d.com/2021.3/Documentation/Manual/AnimationCurvesOnImportedClips.html)
（这不表示一个 Animator 的多个 Layer 会在同一次求值里像数据流节点一样按顺序相互供值 —— 那有 §2 表里的"一轮延迟"。）

## 3. Warudo / Animancer 的约束

用户提醒运行中角色可能被禁用 Animator，这个限制必须覆盖。我们不接管、也不强制启用角色本体的 Animator。
本仓库之前的实机记录实际是 `Animator.enabled=True`、`runtimeAnimatorController=null`，
由 Warudo/Animancer 负责角色动画；因此"Animancer 存在"不等于"Animator 组件一定禁用"。
Animancer 仍利用 Animator 组件，其官方也支持以 Playable 等方式使用控制器。
[Animancer 组件](https://kybernetik.com.au/animancer/docs/manual/playing/inspector/)、
[控制器支持](https://kybernetik.com.au/animancer/docs/manual/animator-controllers/)

**稳定前提是：角色的动画调度归 Warudo；我们在独立求值 rig 上运行自己的控制器。**
最后通过宿主的应用节点把采样结果写入真实角色。这同时兼容本体 Animator 为 enabled、disabled
或没有 Controller 的情况；仍需正确处理与宿主输出的执行顺序和所有权。

当前 Warudo 实现已采用独立 rig。2026-09-25 读取的源码：
`D:/Unity_Project/BreakWarudo/Assets/HoWarudoModTests/Mods-Ho/HoFaceTracking/Core/HoFaceController.cs`。
它加载 bundle 内原配 rig，在 `Solve` 中设置参数、`Update(0)`、读取形状及骨骼；头/根位置另由保留参数装配。
所以"输出通道窄"和"时间不主动推进"都属于当前实现选择。

## 4. 三种"内部语义"要区别

| 用户希望消费的值 | 是否已经存在 | 正确的接口 |
| --- | --- | --- |
| 混合树的输入轴，如 Lid、Smile、Funnel | 它本来就是 Animator 参数 | 已经可直接读/转发，不必再让一层动画"生成一次同名值" |
| 根据姿势混合产生的新量，如 JellyStrength、JellyDrive | 需要作者定义输出 | 叶子把它写进 **Hub**（`P/JellyStrength`），其它模块读 Hub |
| 果冻/摆锤求解后的量 | 需要速度、位移、历史状态及 dt | 由物理求解器积分，再作为下游阶段输入或最终属性输出 |

混合树内部的节点权重/中间姿势不自动变成公开语义信号。要供给其他组件，作者应明确添加导出参数或属性绑定。
导出参数与上游输入应有不同所有权：**一个 Hub 槽只允许一个写入者**（控制器 / Unity 侧调试会话 / 手动，三选一）。
`In/*`、`Out/*`、`Physics/*` 这类前缀只是可选命名约定；**不要每帧又从上游覆盖被曲线驱动的输出**。
`HoFaceController` 注释里"GetFloat 只能读我们写进去的输入"这句已经不准：它还可以读曲线驱动参数。

## 5. ⚠️ 物理阶段：怎样让果冻眼跟语义解耦（设计，未实现）

**控制器完全可以给组件输出开关、强度、目标值、频率、阻尼。** 组件只消费数值，不需要认识 Smile、ARKit、VTS 等上游名字。

```text
Lid / Smile 等语义
  → 控制器：JellyDrive、JellyEnabled、JellyStrength、Frequency、Damping
  → 写进 Hub 的槽（P/JellyDrive …）        ← 不是"写果冻组件的字段"
  → 果冻组件在 Step 里【读 Hub】           ← 它只认 Hub 里那几个名字
  → 果冻专用形态键/Transform 结果
```

这样果冻组件**不需要我们给它做适配器**，也不需要认识任何上游名字 —— 它只认 Hub 的名字。
但"动画输出物理参数"与"物理积分在动画层中完成"仍是两件事：循环摆动动画可以制作视觉摇晃；
响应实时输入、具有惯性和回弹的弹簧仍需要有状态的求解器。

本项目已有 `HoFaceJelly.Step` 阻尼弹簧实现；当前 `HoSpringConstraint.ReadInput()` 只读最终形态键，
取最大值并归一化。要实现用户希望的直接语义驱动，需为它增加**读 Hub 的数值输入模式**，
或用一个独立适配器喂给同一求解核心，保留原来的"读形态键"用法。
**目前没有这个外部数值入口**，不能仅靠把 float 参数命名相同便宣称已接通。

若物理结果还要经过一个姿势矩阵：

```text
阶段A：控制器生成控制量（写 Hub）
  → 物理阶段：只积分一次，生成 Physics/JellyX、Physics/JellyY（也写 Hub）
  → 阶段B：控制器/姿势图把物理量转换为最终造型
  → 应用结果
```

这可以封装在一个"角色动画求值"预设和求解 node 内部，Unity 预览也用同一调度；
用户不必把每根弹簧、每个值都拉成 Warudo 蓝图线。
**阶段 ≠ Animator Layer。** 可以使用两个明确的 Controller 实例 / 求值阶段；
不要靠同一个 Controller 反复 `Update(0)` 直到看起来收敛 ——
后者可能重复处理转换、回调、事件或反馈，且链深变化会影响需要几次求值。
无闭环的 Hub 写入后可以在本帧读取（**Hub 就是内存里的一个列表，读它没有求值代价**）；
需要反馈时必须明示上一帧缓冲或有向无环的多阶段安排。

## 6. ⚠️ 求解 node 建议输出什么（设计，未实现）

保留现有五个 Tracking 兼容出口用于接官方节点，新增一个**完整的求值帧**；
旧出口是同一帧结果的投影，不各自重复求值。概念结构（设计，不是已实现的 C# 类型）：

```text
AnimationFrame
  frameId / deltaTime / valid / controllerInstanceId
  blendShapes            名字或 bindingId → 权重      ← 曲线绑定，照旧
  bonePoses              骨骼 → 旋转/位置/缩放       ← 曲线绑定，照旧
  signals                语义槽（名字）→ float       ← 写进 HoFaceSemanticHub
  properties[]           其它已绑定属性               ← 范围已收窄（见 §1，实际为空）
  events[]               可选的声明式事件及序号
```

**`properties` 那一格现在是空的**：能装进去的那几类（显隐 / 组件档位 / 向量颜色 / 材质 / 对象引用）
§1 全部不做。换句话说：**Hub 就是那张"其它属性"的替身** ——
用户要别的组件跟着动，就在 Hub 上开一格、让那个组件自己读，**我们不替它写**。

缺失绑定表示"本帧未声明该输出 / 不拥有它"，不能和"显式输出 0/false"混同；
切控制器、停用、换装时需要释放并按规则恢复或交还所有权。
对 Hub 来说这条落在**写入者**身上：一个槽的写入者没了（控制器停了 / 换了），
那批槽要按"中性值"或"交还上一个写入者"的规则收场 —— **Hub 自己不猜**。

Events 与 Trigger 属于一次性命令，不能作为每帧 Bool 重复执行。
任意 AnimationEvent / StateMachineBehaviour 带来的副作用无法仅靠采样一份属性快照完整移植。
建议首期禁用影子上的任意事件执行，仅支持声明过的事件出口；
不声称支持任意控制器附带的 C# 逻辑、所有 Humanoid IK 或依赖外界物理的行为。

## 7. ⚠️ 应用 node：只要一个（设计，未实现）

```mermaid
flowchart LR
    IN[参数输入 / VB / 我们的处理节点] --> EV[角色动画求值：阶段、物理、统一时钟]
    EV --> FR[一份 AnimationFrame]
    FR --> TR[现有 Tracking 出口 / 官方形状与骨骼应用]
    FR --> HU[写进 HoFaceSemanticHub]
    HU --> RD[作者的组件 / 材质控制器 / 蓝图 自己读 Hub]
```

| node / 职责 | 粒度 |
| --- | --- |
| 现有形态键 / 骨骼 / 头根应用 | 沿用官方兼容口 |
| **写进 Hub** | 一次接整帧：把 `signals` 按名字写进目标角色的 Hub（**一个** node，不是一槽一个） |
| 应用部件状态 / 材质属性 / 组件参数 | **不做**：要开关的组件自己读 Hub（§1） |
| 可选应用普通 Transform | 配件 / 非 Humanoid 局部 TRS，不能混用骨骼"相对 rest 偏移"的口径（**仍在范围内**） |

界面上还可以提供一个"应用角色动画结果"的组合 node，在内部调用这些应用器；
需要和官方逻辑混用的作者再拆开连接。

**物理调度不能隐含在一个名字叫"写组件值"的 node 里。**
无后续阶段的真实组件物理可在明确的 LateUpdate 阶段运行；需要同帧回流到阶段 B 时，
由求解器 / 阶段调度器显式调用一次 Step。两种模式都必须避免组件自动 Update 一次、宿主又 Step 一次。
写权冲突的判据也随之变简单：**Hub 上"一个槽一个写入者"**，
比"谁最后写了那个材质属性"好查得多 —— 这是走 Hub 的一个额外收益。

## 8. ⚠️ 怎样导出绑定而不依赖运行时反射（设计，未实现）

当前 bundle 里带 rig + controller，但运行时不能用 `UnityEditor.AnimationUtility` 枚举绑定；
Warudo 插件沙箱也限制反射。因此应在 Unity 导出时生成**绑定清单 manifest**：

```text
bindingId
targetPath / componentAdapterId / componentSlot
propertyPath 或 shaderPropertyId / materialSlot
valueKind / referenceResourceId
space / absolute-or-offset / default / ownership / releasePolicy
```

manifest 现在只需要装**两类绑定 + 一张语义表**：

| 类别 | 内容 | 运行时怎么用 |
| --- | --- | --- |
| 形态键 | `targetPath` + `blendShape.<名>` | 照旧 |
| 骨骼 / Transform | `targetPath` + `type` + `propertyName` | 照旧 |
| **Hub 动态参数** | **一格一个 `(名字, 值)` 键值对**（名字由**中间层**按输出行的 `parameter` 开出来；中性值 = 静态的 0 或那一行声明的 `defaultValue`） | 按名字写 `hub.SetFloat(名字, 值)`，**不反射** |

⚠️ `componentAdapterId` / `shaderPropertyId` / `materialSlot` / `referenceResourceId` 这几个字段
**现在用不上了**（那是"写其它组件/材质"的方案），留着的唯一理由是将来放开时不必重新设计格式 ——
**但不要现在就为它们写解析代码**。

导出阶段遍历 `GetCurveBindings` 与 `GetObjectReferenceCurveBindings`，对受支持绑定分类；
未知绑定明确报告，不静默漏掉 —— 因为我们的范围只有形态键与 Transform，
碰到材质 / 对象引用那类**直接点名说不支持**（而不是想办法支持它）。
运行时用稳定 bindingId 解析目标和已注册的类型访问器，不开放"任意字符串反射写任意组件"。

**Hub 这条为什么天然不需要适配器**：`HoFaceSemanticHub` 的值是运行期按名字开的键值对列表，
读写只按名字/下标 —— **不碰字段名**。所以它同时满足"不反射"和"类型在两边程序集里都真实存在"两条
（该类型经 `sync-modcore.ps1` 逐字节同步进 mod）。
而且**影子/代理那份上不会跑作者的业务脚本**（Hub 上只有值与写的人当场声明的名字，
接口就在 Hub 自己身上，**不带**任何表），没有 Awake/Update/全局注册那些副作用。
⚠️ 2026-09-26 之后，**控制器资产上不再挂我们自己的任何行为**：动态参数由中间层算完直接写角色 Hub。

## 9. ⚠️ 时钟与预览（设计，未实现）

当前 Warudo `Solve` 和 Unity 调试会话主要手动 `Update(0)`，是静态姿势采样路径。
若影子 Animator 仍由 Unity 自动更新，动画时间也可能由那条隐含时钟推进；
不能仅凭手动 dt=0 就断言当前进程所有动画都冻结。问题是时间与采样顺序没有由这次显式求值完整定义。
要支持有时间的表情动画、状态过渡、播放速度与物理，必须升级为**每帧一次的显式时钟**，并区分：

- 静态参数预览：允许 dt=0 重新采样。
- 正常播放：每个阶段按约定推进 dt 一次；物理也只积分一次。
- 暂停/跳转：控制器和物理按明确的重置/快照策略处理，不把持续播放和拖动预览混在一起。

本轮证明独立 Animator 组件 disabled 时仍可手动求值，可作为避免自动重复推进的候选方案；
rig 需保持 active，并在 Warudo 内进一步验证。
也可以评估手动 PlayableGraph 时钟，但本轮没有以它替换现有求解器，
不因更换调度顺便重写已经跑通的绑定机制。

时钟与 Hub 的关系：Hub 的槽**没有时间语义**（它就是当前值）。
所以"暂停/跳转"时 Hub 要不要跟着回退，是**写入者的责任**（控制器不推进就不写，槽保持上一帧值）——
需要"回退到某帧"的话必须显式快照，不能靠 Hub 自己记住历史。

## 10. 实现顺序（都还没做）

**先加入绑定清单与语义槽（Hub），打通形态键/骨骼 + Hub 写入；
再加入受控的内部语义导出与物理输入；需要物理回流时再支持显式多阶段。**
按顺序打通是为了每步都能在 Unity 和 Warudo 用相同夹具验收。
（Hub 那一格已经落了，见[动态参数](FACE_TRACKING_DYNAMIC_PARAMETERS.md)；其余仍是提案。）
