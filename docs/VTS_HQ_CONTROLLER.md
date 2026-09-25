# 带一层门控的 VTS 原生语义控制器：2D 混合树规划与装配草案

**状态：草案 v3**（v2 起按你的意见改：**按键表情的平行副本会让门控嵌套，这一层嵌套是接受的**；
副本改用 **1D 交叉淡入树**，于是中间层不用写互补权重、复制门也不由我们写。已定/待定见 §11）。
分工：**能表达什么**（42 树族 × 轴 × 条件）见 [VTS 全量契约表](VTS_HIGH_QUALITY_FACE_CONTRACT.md)；
**轴的公式从哪来**见 [参数空间](VTS_FACE_PARAMETER_SPACES.md)；**当前装配机制**（控制器是搬来的作品、装配只填片段）见
[控制器结构](FACE_TRACKING_CONTROLLER_STRUCTURE.md) §3；**引擎能/不能算什么**见 [混合树能力边界](BLEND_TREE_LIMITS.md) §2 与 [踩过的坑 · 混合树](pitfalls/BLEND_TREE_TRAPS.md)。

**这一版要回答的只有一件事：哪些语义进哪张 2D 表、门控摆在哪、权重谁算。** 动画内容后做。

## 1. 起点：土豆那份配置已经把 VB 语义算出来了，只是没地方去

`D:\Unity_Project\BREAK_URP\Assets\Hollow\土豆\FT\ho-iPhoneVTS.hoface.json`（67 输入行 / 94 输出行）
里**不是直通的那 42 行**正好就是 VB `AdvancedARKit_V3.0` 那套语义 —— 也就是说**VB 已有的轴，我们不用重新造，直接合进来**：

```text
MouthOpen  = (jawOpen − mouthClose) − .2×(mouthRollUpper+mouthRollLower) + .2×mouthFunnel
MouthSmile = (2 − (frownL+frownR+pucker) + (smileL+smileR+(dimpleL+dimpleR)/2)) / 4
MouthFunnel = mouthFunnel − .2×jawOpen            MouthPressLipOpen = lipRaise/1.8 − roll
MouthPucker = 2×(dimpleL+dimpleR) − mouthPucker   MouthX = (mouthLeft−mouthRight) + (smileL−smileR)
MouthShrug = (shrugUpper+shrugLower+pressR+pressL)/4
EyeOpenL/R = .5 − .8×eyeBlink + .8×eyeWide        Brows / BrowLeftY / BrowRightY / BrowInnerUp
JawOpen / CheekPuff / TongueOut / FaceAngleXYZ / FacePositionXYZ / EyeLeftXY / EyeRightXY
Ho/Drive/Lid/{Left,Right}/{BlinkWide,Squint} = eyeBlink − eyeWide / eyeSquint
```

⚠️ **但控制器只认 57 个名字**（`Gate/Eye`、`Gate/Lip`、4 根 `Ho/Drive/Lid/*` + 51 个**裸 ARKit 名**）。
那 20+ 行 VB 语义**名字不在控制器参数表里** ⇒ 写入那一步按控制器的口逐个查字典，查不到就**静默忽略**
（面板「参数输出」栏把它们标成"不在控制器里"）。**这一版的活就是把这条链接上。**

> 📌 现状要核对的：现控制器 `Ho/Drive/Lid/Left/BlinkWide` 的 `m_DefaultFloat = 1` 而 Right 是 `0`
> （左右不一致；WD 开时权重不足 1 的部分混的是**启动值**，启动值不同 ⇒ 同一份动画左右结果不同）。
> 新控制器把默认值统一写清。

## 2. 哪些语义可以构成 2D 混合树（本稿的核心）

三档：**① 数据已经就绪**（VB 行已经在跑，直接合）/ **② 需要一根 HQ 新轴**（公式在契约表 D，中间层加一行）/
**③ 先做一维**（契约 §4 明确"先保留独立轴"的那些）。**④ 不做 2D** 的另列。

### ① 立刻能做（轴已经在配置里了）

| 树名 | X 轴 | Y 轴 | 条件轴 | 备注 |
| --- | --- | --- | --- | --- |
| **`MouthCore`** | `Form` = 2×`MouthSmile` − 1 | `Open` = `MouthOpen` | `Funnel`, `Press` | 最重要的那张表；`MouthSmile` 静息 0.5 ⇒ 必须映射成 −1…1 的中性 0 |
| **`LidL` / `LidR`** | `BlinkWide` = `eyeBlink − eyeWide`（±1，0 中性） | `Squint` | `EyeSmile`（②） | 现控制器已经在跑（6 个采样点）；坐标别和 VRCFT 的 0…1/0.75 混用 |
| **`GazeL` / `GazeR`** | `GazeX` = `EyeLeft_x`/`EyeRight_x` | `GazeY` | — | **每侧独立**，不从合并量还原；独立左右眼是另一种契约 |
| **`BrowCoreL` / `BrowCoreR`** | `BrowY` = `BrowLeftY`/`BrowRightY` | `BrowInnerUp` | — | VB 的 BrowY 里已经掺了 `(mouthRight−mouthLeft)/8`（说话时眉会动一点，是 VB 的联动） |
| **`CheekSquint`** | `CheekSquintL` | `CheekSquintR` | `Form`, `Lid` | 直通即可 |
| **`NoseSneer`** | `NoseSneerL` | `NoseSneerR` | 上唇抬起 | 直通即可 |
| **`MouthWidth`** | `MouthPucker`（−1…1 双向） | `MouthX` | `Open`, `Form` | 对 `MouthCore` 的嘴宽/偏嘴修正 |
| **`MouthTongue`** | `HQTongueLeft`（新，先同跟 `tongueOut`） | `HQTongueRight`（新） | `Jaw`, `Open`, `Pucker`, `Funnel`, `Press` | **分左右**：模型上舌头本来就是左右两组键；V3 去掉了 `1−tongueOut` 抑制 ⇒ 更容易同时有值 |
| **`CheekPuff`** | `HQCheekPuffLeft`（新，先同跟 `cheekPuff`） | `HQCheekPuffRight`（新） | `Open`, `Seal`, `Form`, `Pucker` | **分左右**鼓腮；嘴型从"轴"降成**条件** |

### ② 加一根 HQ 行就能做（公式都在契约表 D，机械可抄）

| 树名 | X 轴 | Y 轴 | 新行 |
| --- | --- | --- | --- |
| `MouthJaw` | `JawOpen` | `HQJawForward` = `jawForward` | 条件 `HQJawX` = `jawRight − jawLeft` |
| `MouthShrugSplit` | `HQUpperLipShrug` = `mouthShrugUpper` | `HQLowerLipShrug` = `mouthShrugLower` | — |
| `MouthCorner` | `HQSmileFrownL` = clamp(`smileL−frownL`) | `HQSmileFrownR` | — |
| `MouthUpperRaise` | `HQUpperLipRaiseL` = `mouthUpperUp_L` | `…Right` | 条件 `Open`/`Press` |
| `MouthLowerDrop` | `HQLowerLipDropL` = `mouthLowerDown_L` | `…Right` | 同上 |
| `LidL`/`LidR` 的笑眼条件 | — | — | `HQEyeSmileL/R` = `mouthSmile_L/R`（**现在缺这两行**，见 §9；本版笑眼改走副本，见 §4） |
| `CheekPuffTongue`（极端组合残差） | `HQTongueLeft` | `HQTongueRight` | 条件 `HQCheekPuffLeft/Right`、`Jaw` |

### ③ 先一维（**别急着配 2D**）：`MouthX`、`MouthShrug`、`MouthPucker`、`HQJawX`、`HQJawForward`、`CheekPuff`（单侧那根）、`TongueOut`
契约 §4 的理由：这些是**双向轴或独立自由度**，先独立；只有"组合后的造型明显不成立"时才升级成条件修正。
（分侧那两根 `HQCheekPuffL/R`、`HQTongueL/R` 例外 —— 它们在 `CheekPuff`/`MouthTongue` 里就是主轴，见 §2①。）

### ④ 不做 2D：`FaceAngle*` / `BodyAngle*` / `FacePosition*` / `BodyPosition*`（3D 宿主走**姿态链**，不画面部矩阵）、
`FaceFound` / `Hotkey` / `Timestamp`（控制数据，不是美术维度）、音素（**类别权重**，没有自然距离，不是连续轴）、
`HandPos` / `HandFinger` / `CtrlStick`（演出通道）。

## 3. 门控：区域门 × 表情副本门（**嵌套，已确认可接受**）

### 3.1 两层是什么

| 层 | 参数 | 是什么 | 谁写 |
| --- | --- | --- | --- |
| **第 1 层：区域门** | `Ho/Drive/Gate/{Mouth, EyeLeft, EyeRight, Brow, Cheek}` | 整块算不算数（默认 1） | 中间层**常量行**（`expression` 留空 + `defaultValue = 1`） |
| **第 2 层：表情副本** | `Ho/Drive/Gate/Expr/<表情名>`（`Smile`、`Angry`、`Wink`…） | 这张表走"普通版"还是"按键表情版" | **本版不接**（§3.3）：默认 `0`，**中间层不写它** |

```
Ho/00 Drive Tree (Direct)
└─ Mouth            ← 权重 = Ho/Drive/Gate/Mouth            （第 1 层）
     ├─ MouthCoreSwitch（Simple1D，blendParameter = Ho/Drive/Gate/Expr/Smile）
     │    ├─ 阈值 0 → MouthCore（Direct：4 张条件切片，权重 = Slice/MouthCore/Funnel0Press0 …，和恒 1）
     │    └─ 阈值 1 → MouthCoreExpr（Direct：同名切片，**同轴、不同姿势**）
     ├─ MouthJaw      ← 权重 = 1（不参与表情副本：按键表情不该改下颌）
     └─ MouthWidth      ← 权重 = 1
```

**副本用「1D 交叉淡入树」实现，不用"两个互补权重的兄弟节点"** —— 这是本节最要紧的一条：

* 1D 树（`Simple1D`，两个子节点分别钉在阈值 `0` / `1`）**由引擎自己补齐权重**（段内线性插值），
  不需要谁去写 `1 − g` ⇒ 中间层不用参与，区域内的权重和天然恒为 1。
* 门参数**默认 `0` = 普通版**；而**我们不写这个参数** ⇒ 任何外部来源（VTS API、键盘、别的脚本）
  都能自己驱动它，**不会被我们每帧覆盖**。这就是"门只是一行参数、谁写都行"的落地方式。
* 参考实现里那个 `EyeSyncMix` 就是同一个形状（1D 树 + OFF/ON 两版子树），见
  [控制器结构](FACE_TRACKING_CONTROLLER_STRUCTURE.md) §1.4 与 [混合树能力边界](BLEND_TREE_LIMITS.md) §2。

⚠️ **为什么平行副本只能做成"树内权重交叉淡入"，不能做成 Animator 状态切换**：
影子台是 **`Update(0)` 静态采样**（[控制器结构](FACE_TRACKING_CONTROLLER_STRUCTURE.md) §3、[设计](FACE_TRACKING_DESIGN.md) §4），
而状态过渡是**按时间推进**的 —— `dt = 0` 时过渡永远走不完。按键表情只有"参数驱动的权重"这一条路。

### 3.2 嵌套的后果（认下来，但要知道代价在哪）

* 有效门控 = `Gate/Mouth × (1D 树在 g 处算出的权重) × Slice/…`，**乘积只活在树里**：
  Hub 里读到的是**各个因子**，不是乘积。下游要"这块实际贡献多少"就自己乘 ——
  **真要让乘积本身也可见**，就在中间层多写一行 `Ho/Drive/Eff/<族>/<切片>`（乘法由中间层做，不是从树里读回来）。
* 让**每个区域内部的总权重恒为 1**（切片权重和 = 1，副本与主版由 1D 树互补）：
  这样就不会有"权重和不足 1 ⇒ 混进启动值"那份意外（[踩过的坑 · 混合树](pitfalls/BLEND_TREE_TRAPS.md) §4）。
  区域门调到 1 时这块完全由树决定；把区域门调小 = **让这块回到启动姿势**，这是区域门唯一的语义。
* **一张表一个副本、一个门**（第一版）：门名就是表情名（`Gate/Expr/Smile`），**同一个门可以同时驱动好几张表的副本**
  （`Smile` 同时改嘴、眼睑、眉）。同一张表要**两个**表情版本时，再把这棵 1D 树升级成多阈值。

### 3.3 表情开关的来源：**本版不接、也不测**（你定）

结构与命名先定死，来源后接 —— 树只认 `Ho/Drive/Gate/Expr/<名>`，参数默认 `0` 且**不由中间层写**。
真要接的时候，候选与一条实测事实在这里备查：

⚠️ 协议里的 `Hotkey` **实测恒为 −1**：安卓与 iPhone 两台都是（"按 VTS app 内的屏幕热键才给 1–8"，
而那三个快捷键不输出）—— 见 [参数实机验证表](PARAMETER_DEVICE_VERIFICATION.md) §0.2 与 [HO 参数规范](PARAMETER_HO.md) §1。
所以"按键"要另选来源：① VTS 公开 API（`ExpressionActivation` / `HotkeyTrigger`）适配器；
② 本地按键（Unity 调试面板开关 / Warudo 官方键盘节点写同一个参数）；③ VB 若把表情输出成线。

## 4. 平行副本给谁做（**已定**）

| 做副本 | 表情门（建议名） | 为什么 | 不做副本 |
| --- | --- | --- | --- |
| **`MouthCore` 外嘴** | `Gate/Expr/Smile` | 按键表情最常改的就是嘴（笑/怒/吐舌的整套嘴形） | `MouthJaw` 下颌、`MouthWidth` 嘴宽：表情不改下颌与嘴宽，保持原样 |
| **`LidL` / `LidR` 眼睑** | `Gate/Expr/Smile`（可与嘴共用） | 笑眼/眯眼/闭眼的表情（`Wink` 再单独加一个门） | `GazeL`/`GazeR` 注视：按键表情一般不动眼球（要动就再开一张） |
| **`BrowCoreL` / `BrowCoreR` 眉** | `Gate/Expr/Angry` | 怒/悲/惊讶全靠眉 | `CheekSquint` 颊、`NoseSneer` 鼻：先不做 |
| ~~M14 舌~~ | — | 第一版不做（要吐舌表情时再加） | — |

**一个门可以驱动多张表的副本**（`Smile` 同时改嘴、眼睑、眉 ⇒ 三棵 1D 树共用同一个 blendParameter）。
第一版就一个表情门先跑通；**门名与表情的对应关系是作者约定**，代码不参与（`HoFaceNaming` 不认识它）。

副本的**键集合与轴与原表完全一致**（这是"平行副本"的定义：同轴不同姿势）。装配器只按**槽位名**填片段 ⇒
副本的槽位用**同族带后缀**的命名：`M01E__Form__Open__A3X{i}Y{j}`（`E` = expression 副本）。

⚠️ **笑眼这一条因此换了个做法**：契约里 `LidL`/`LidR` 的第三维是 `HQEyeSmile`（条件切片）；本版把它改成**平行副本**
（`Smile` 门一开，眼睑整套换成笑眼版姿势）。理由：按键表情本来就是"整套换一套姿势"，
而条件切片要求作者为每个条件单独摆一遍采样点 —— 两者做的事一样，副本更好维护。`HQEyeSmile` 仍然留在契约里，
将来要做"不按键、由 `mouthSmile` 自动联动的笑眼"时再加。

## 5. 键集合的分工规则（比树形更重要）

1. **每棵树拥有互不重叠的键集合**（写进契约表：族 → 键列表）。Direct 是**加法**、不归一化（`0.6+0.8 → 140`）：
   键不重叠时"基础 + 修正"才是叠加；重叠时值会相加。
2. **语义会重叠的必须并进同一张 2D 表**，靠摆采样点表达（例：眼睑"闭眼 × 眯眼"的多个组合姿势）。
   判据一句话：**两棵树如果会写同一个键，就得合并，或者在中间层先选一格。**
3. **基础 / 条件覆盖 / 修正**三类槽按 [契约表 §F](VTS_HIGH_QUALITY_FACE_CONTRACT.md) 的 fallback：
   修正槽没填 = **零残差**（不影响基础）；条件覆盖槽没填 = **复用同一份基础片段**（不是留空 Motion）。
   装配器只做"同名片段替换 + 报缺项"，**不会替你补洞**。
4. 修正树写的是**残差**：做完单侧上唇姿势要减掉 `MouthCore` 已经表达的部分再放进 `MouthUpperRaise`，不能全量再加一遍。

## 6. 第一版落地清单（tranche 1）

| 树名 | 2D 轴 | 条件 | 槽位前缀 | 建议采样 | 副本 |
| --- | --- | --- | --- | --- | --- |
| `MouthCore` 外嘴核心 | Form × Open | Funnel × Press（先只做 1 张，其余复用基础片段） | `MouthCore__Form__Open__A3X{i}Y{j}` | 3×3 | ✅ 副本 `MouthCoreExpr`（门 `Smile`） |
| `LidL` / `LidR` 眼睑 | BlinkWide × Squint | ~~EyeSmile~~（改走副本，见 §4） | `LidL__BlinkWide__Squint__A3X{i}Y{j}`（沿用现名） | 3×2（现有 6 点） | ✅ 副本 `LidLExpr`/`LidRExpr`（门 `Smile`） |
| `GazeL` / `GazeR` 注视 | InOut × UpDown | — | `GazeL__InOut__UpDown__A3X{i}Y{j}` | 3×3（或十字 5 点） | — |
| `BrowCoreL` / `BrowCoreR` 眉 | Height × InnerUp（**按 VB 的 BrowLeftY/BrowRightY**） | — | `BrowCoreL__Height__InnerUp__A2X{i}Y{j}` | 2×2 | ✅ 副本 `BrowCoreLExpr`/`BrowCoreRExpr`（门 `Angry`） |
| `MouthJaw` 下颌 | Jaw × Forward | JawSide | `MouthJaw__Jaw__Forward__A3X{i}Y0` | 3×1 | — |
| `MouthWidth` 嘴宽/偏嘴 | Pucker × LeftRight | — | `MouthWidth__Pucker__LeftRight__A3X{i}Y{j}` | 3×3 | — |
| `CheekSquint` 双颊 | CheekL × CheekR | — | `CheekSquint__CheekL__CheekR__A2X{i}Y{j}` | 2×2 | — |
| `CheekPuff` 鼓腮（**分左右**） | PuffL × PuffR | Open, Seal, Form, Pucker | `CheekPuff__PuffL__PuffR__A2X{i}Y{j}` | 2×2 | — |
| `MouthTongue` 舌（**分左右**） | TongueL × TongueR | Jaw, Open, Pucker, Funnel, Press | `MouthTongue__TongueL__TongueR__A2X{i}Y{j}` | 2×2 | — |
| `NoseSneer` 鼻翼 | SneerL × SneerR | — | `NoseSneer__SneerL__SneerR__A2X{i}Y{j}` | 2×2 | — |

**先不做**（契约里都留着，加的时候不动已经做好的树）：`MouthSeal`、`MouthShrugSplit`、`MouthCorner`、
`MouthUpperRaise`、`MouthLowerDrop`、`MouthLipRoll`、`MouthLipPress`、`MouthStretch`、`MouthDimple`、
`MouthRawRound`、`MouthJawSide`、`MouthShrugBase`、`CheekPuffTongue`、`LidGazeL/R`、`LidMouthL/R`、`LidBoth`、
`BrowEyeL/R`、`BrowCenter`、`HeadAim`/`HeadPos`/`BodyAim`/`BodyPos`、`AudioPhoneme`、`HandPos`/`HandFinger`/`CtrlStick`。

## 7. 参数契约（控制器认这些名字）

三段式 `Ho/Drive/<部位>/<轴>`（ASCII、`HoFaceNaming` 的根不变）：

```text
Mouth:  Form(-1..1, 0中性) Open(0..1) Funnel(0..1) Press(-1..1) Jaw(0..1) Pucker(-1..1) X(-1..1) Shrug(0..1)
        TongueL(0..1) TongueR(0..1)
Lid:    Left|Right / BlinkWide(-1..1, 0中性) Squint(0..1) EyeSmile(0..1)
Gaze:   Left|Right / X(-1..1) Y(-1..1)
Brow:   Left|Right / Y(0..1) InnerUp(0..1)
Cheek:  Left|Right / Squint(0..1) Puff(0..1)      Nose: Left|Right / Sneer(0..1)
Gate:   Mouth EyeLeft EyeRight Brow Cheek（区域门，默认 1）  Gate/Expr/<表情名>（副本门，默认 0、我们不写）
Slice:  Ho/Drive/Slice/<树名>/<切片>（中间层算的分区权重；副本那条 1D 轴不需要权重行）
```

名字的完整规则（树名怎么拼、轴段词用哪些、副本/区域/槽位怎么命名）见 **[面捕命名权威](FACE_TRACKING_NAMING.md)** ——
本文只用名字，不再自己发明。

⚠️ **不借 VB/VTS 的原名当参数名**：配置里那个 `MouthSmile` 是**VB 公式的输出**，不是 VTS 自己那个 `MouthSmile`；
同名同义的误会会长期留在表里（[参数标准表](PARAMETER_STANDARDS.md) 的分工正是"外部怎么定 vs 我们选什么"）。

## 8. 采样点与槽位（动画后做，但命名现在定死）

* 槽位 = **一个多键姿势片段**，命名 `<树名>__<X段词>__<Y段词>__A<n>X<i>Y<j>`（现场例子：`LidL__BlinkWide__Squint__A3X0Y2`）；
  段词与 1D/副本的写法见[命名权威](FACE_TRACKING_NAMING.md) §5。
* ⚠️ **工具缺口**：现有「形态键动画」工具（`Editor/AnimationTools/HoBlendShapeClipBuilder.cs`）出的是
  "**一键一片段**、值恒 100" —— 那是给 Direct 每键叶子用的，**不是 2D 采样点的姿势**。
  填 2D 表需要"**姿势烘焙**"：把调试面板里调好的滑条姿势（会话 `SetPreview` 那套）存成一个以槽位命名的多键片段。
  **动画你后做，但这条工具得排在它前面。**
* 采样允许**稀疏、非方阵**，多个采样点可以共用同一份片段（闭眼时不同 Squint 可以是同一个闭眼姿势）。
* 非方阵的插值语义由 2D Freeform Cartesian 定，**先摆点看结果**，不要假设。

## 9. 配置改造：**发货那份 profile 已经改好了**（2026-09-27）

包里的 `Editor/FaceTracking/Profiles/ho-iPhoneVTS.hoface.json` 从 67 输入 / 90 输出变成 **67 / 127**：
**90 行出口原样保留**，**另加 37 行控制器轴**（逐行清单见 [HO 参数规范](PARAMETER_HO.md) §3.7）。
**改造 = 追加，不是重命名** —— 三条查出来的硬约束决定了这个形状：

| # | 约束（都有代码/文档判据） | 后果 |
| --- | --- | --- |
| 1 | **输出行之间不能互相引用**：求值器只查输入行（`HoFaceChain.EvaluateOutputs` → `inputIndex` / 原始线名） | 切片权重行**把轴公式内联展开**；要收短得先改那条链 |
| 2 | **G1–G3 那 90 行是下游契约**：VTS 生态、Hub 消费者、离线台架（`.research/profile-json-test` 的 §3 表格核对）都按名字守它们 | 轴行**新增**而不是把出口名改掉；`§0` 的"出口行总数 90"也保持不变 |
| 3 | **4 根 `Ho/Drive/Lid/*` 早就不是代码内置的了**（`HoFaceMiddleware` 里那段注入已删） | 它们**必须写在 profile 里** —— 发货那份以前**缺这 4 行**（只有土豆那份 rig 副本有），现在补齐 |

细节与坑（都写进了 §3.7 的备注）：

* `Form` = **2×`MouthSmile` − 1**、`Brow/*/Y` = **2×VB `Brow*Y` − 1**：VB 那两条的静息是 0.5，
  轴要"0 = 中性"就得重映射（两条都用宽曲线，别用 0..1 的默认曲线，否则静息负半边被夹掉）。
* **区域门是常量行**（`expression` 留空 + `defaultValue = 1`）：**常量行不过曲线**（`HoFaceAnimationSession.cs:283`），
  所以写 1 就是 1。
* **`Gate/Expr/*` 一行都不写**：它属于驱动"按键"的那一方（Unity 面板 / Warudo 键盘节点 / VTS API 适配器）；
  控制器里默认 `0`。我们每帧写它 = 把它锁死。
* **分侧轴先两侧同跟单侧原值**（`TongueL`/`TongueR` ← `tongueOut`；`Cheek/*/Puff` ← `cheekPuff`），
  有分侧来源时直接驱动、树不动。
* **文件头 `notes` 那段"没写 VB 自造输出"是过期的**（那些行后来加了）—— 顺手在 §3.7 里说清，改 profile 时一并改写。
* 土豆那份 **rig 副本**（`BREAK_URP/.../ho-iPhoneVTS.hoface.json`，94 行）与发货那份**已经漂了**：
  它多 4 行显式眼睑轴（现在发货那份也有了），没有 33 行新轴 —— **把它换成发货那份**即可对齐（沙箱里那份同理）。

判据（面板「参数输出」栏）：**要进树的轴一个都不许落在"不在控制器里"** —— 骨架没搭好之前，这 37 行会全部
显示"不在控制器里"，那正是"还没接上"的可视化。其余出口行保持 output-only。

## 10. 手搭清单（在混合树编辑器里照这个建；动画留空）

**第 0 步 · 参数（全部 Float）**：**一共 39 个** = 下面这 **37 个由发货 profile 每帧写**
（逐行的表达式与曲线见 [HO 参数规范](PARAMETER_HO.md) §3.7）+ **2 个表情门**（profile **不写**，
留给按键来源）。不写全的名字以后查[命名权威](FACE_TRACKING_NAMING.md) §6。

| 组 | 参数 | 默认值 |
| --- | --- | --- |
| 区域门（5） | `Ho/Drive/Gate/Mouth` · `EyeLeft` · `EyeRight` · `Brow` · `Cheek` | **1** |
| 表情门（2，profile 不写） | `Ho/Drive/Gate/Expr/Smile` · `Ho/Drive/Gate/Expr/Angry` | **0** |
| 轴（28） | `Mouth/Form` `Open` `Funnel` `Press` `Jaw` `Forward` `Pucker` `X` `TongueL` `TongueR`；`Lid/Left\|Right/BlinkWide` `Squint`；`Gaze/Left\|Right/X` `Y`；`Brow/Left\|Right/Y` `InnerUp`；`Cheek/Left\|Right/Squint` `Puff`；`Nose/Left\|Right/Sneer`（前缀都是 `Ho/Drive/`） | 0（`BlinkWide` 也是 0） |
| 切片权重（4） | `Ho/Drive/Slice/MouthCore/{Funnel0Press0, Funnel1Press0, Funnel0Press1, Funnel1Press1}` | 0 |

**第 1 步 · 层与状态**：一层（现控制器是 `Ho/00 Drive`）+ 一个状态；**Write Defaults 开**。

**第 2 步 · 根 Direct** `Ho/00 Drive Tree`：5 个子节点 = 5 个区域子树，`directBlendParameter` = 对应区域门。

**第 3 步 · 区域子树**（Direct，子节点见下）：`MouthRegion`（门 `Mouth`）、`EyeLeftRegion`（`EyeLeft`）、
`EyeRightRegion`（`EyeRight`）、`BrowRegion`（`Brow`）、`CheekRegion`（`Cheek`）。

**第 4 步 · 表（tranche 1）**：区域子树里的每个子节点要么是一张 2D 表、要么是一棵 1D 副本树。

| 表 | 类型 | blendParameter X / Y | 子节点 | 槽位名 |
| --- | --- | --- | --- | --- |
| `MouthCore` | FreeformCartesian2D | `Ho/Drive/Mouth/Form` / `…/Open` | 9（3×3，先摆 1 行也行） | `MouthCore__Form__Open__A3X{i}Y{j}` |
| `MouthJaw` | FreeformCartesian2D | `…/Mouth/Jaw` / `…/Mouth/Forward` | 3 | `MouthJaw__Jaw__Forward__A3X{i}Y0` |
| `MouthWidth` | FreeformCartesian2D | `…/Mouth/Pucker` / `…/Mouth/X` | 9 | `MouthWidth__Pucker__LeftRight__A3X{i}Y{j}` |
| `MouthTongue` | FreeformCartesian2D | `…/Mouth/TongueL` / `…/Mouth/TongueR` | 4 | `MouthTongue__TongueL__TongueR__A2X{i}Y{j}` |
| `LidL` / `LidR` | FreeformCartesian2D | `…/Lid/<Left\|Right>/BlinkWide` / `…/Squint` | 6 | `LidL__BlinkWide__Squint__A3X{i}Y{j}`（现成） |
| `GazeL` / `GazeR` | FreeformCartesian2D | `…/Gaze/<Left\|Right>/X` / `…/Y` | 9 | `GazeL__InOut__UpDown__A3X{i}Y{j}` |
| `BrowCoreL` / `BrowCoreR` | FreeformCartesian2D | `…/Brow/<Left\|Right>/Y` / `…/InnerUp` | 4 | `BrowCoreL__Height__InnerUp__A2X{i}Y{j}` |
| `CheekSquint` | FreeformCartesian2D | `…/Cheek/Left/Squint` / `…/Right/Squint` | 4 | `CheekSquint__CheekL__CheekR__A2X{i}Y{j}` |
| `CheekPuff` | FreeformCartesian2D | `…/Cheek/Left/Puff` / `…/Right/Puff` | 4 | `CheekPuff__PuffL__PuffR__A2X{i}Y{j}` |
| `NoseSneer` | FreeformCartesian2D | `…/Nose/Left/Sneer` / `…/Right/Sneer` | 4 | `NoseSneer__SneerL__SneerR__A2X{i}Y{j}` |
| `MouthCoreSwitch` | **Simple1D**（`blendParameter` = `Gate/Expr/Smile`，阈值 0 / 1） | — | 2（0→`MouthCore`、1→`MouthCoreExpr`） | — |
| `LidLSwitch` / `LidRSwitch` | Simple1D（`Gate/Expr/Smile`） | — | 2（`LidL`/`LidLExpr`、`LidR`/`LidRExpr`） | — |
| `BrowCoreLSwitch` / `BrowCoreRSwitch` | Simple1D（`Gate/Expr/Angry`） | — | 2（`BrowCoreL`/`BrowCoreLExpr`…） | — |

**第 5 步 · 占位**：每张表的子节点**先可以是空 Motion**（槽位"缺"能在「详情」栏逐条列出来）；
填的时候按 §8 的"姿势烘焙"出片段，文件名 = 槽位名。

⚠️ 手搭时最容易忘的三条：**Direct 的每个子节点都要挂一个参数**（不挂就不参与混合）；
**同一个键不要被两张表写**（Direct 是加法，会相加）；**区域门保持 `1`**（调小 = 让这块回到启动姿势）。

## 11. 装配与验收

1. 控制器编辑页**就地装配**（`HoUnityTools/面捕/控制器编辑`）：控制器是你那份、动画文件夹是槽位数据、调试对象是场景角色；
   装配只填片段 + 重绑形态键曲线，**不碰层与参数**。
2. **硬约束**（装配不拦，开始驱动才拒）：只允许形态键曲线、无 `StateMachineBehaviour`、
   **Direct 树所在状态必须 WD 开**（关掉会逐帧发散：`98.98 → 246.28 → 1059.33`）。
3. **这一版"做完"的判据**：① §7 的参数名在控制器参数表里齐全、区域门默认值统一为 `1`、
   表情门默认 `0` 且**配置里没有写它的行**；② 门只有 §3 那两层（区域 + 表情副本的 1D 树）；
   ③ 配置里要进树的行**全部命中**控制器参数；④ 「详情」栏能列出槽位清单（"缺哪些动画"是可见的清单）。
4. **看不到的部分**：没有动画时树里全是空 Motion / 复用片段，**插值行为要等第一张表填上才看得到** ——
   这一版不谎称验收过"动起来对不对"。1D 副本树可以在**参数层**先验（手动把门推 0→1，看权重交叉淡入）。
5. 门：包侧 `compile-check-pkg.ps1` + `ensure-bom.ps1 -Fix`；涉及 mod 的过 mod `compile-check`；两边分别提交。

## 12. 已定 / 待定

**已定（2026-09-27）**：

1. **平行副本**给 **`MouthCore` 外嘴 + `LidL`/`LidR` 眼睑 + `BrowCoreL`/`BrowCoreR` 眉**（`MouthTongue` 以后再说）；副本用 **1D 交叉淡入树**（§3.1）。
2. **参数名**用 `Ho/Drive/<部位>/<轴>`（不借 VB/VTS 原名）。
3. **眉毛**做，按 VB 现成的 `BrowLeftY/BrowRightY` × `BrowInnerUp`。
4. **表情来源本版不接、也不测**：门参数建好、默认 `0`、中间层不写它（§3.3）。
5. **骨架由你手搭**，我出清单（§10）；不写生成器。
6. **鼓腮与吐舌分左右**，各出一对 HQ 轴（`HQCheekPuffLeft/Right`、`HQTongueLeft/Right`，契约 HQ 39 → 43），
   树名/轴段词按 [命名权威](FACE_TRACKING_NAMING.md)（树族不再用 `M01` 那套编号）。

**待定**：

1. **表情门的名字**（已用 `Smile` / `Angry` 两个）与第一批到底要几个表情 —— 只影响命名，不影响结构。
2. **采样点密度**（§6/§10 的建议值 3×3 / 3×2 / 2×2）—— 你填动画时定，本文只是起点建议。
3. **表情开关来源**先接哪个（VTS API 适配器 / 本地按键 / 等 VB 输出）？**手机 `Hotkey` 那条已经废了**（实测恒 −1）。

下一步：照 §10 手搭骨架（空 Motion + 槽位占位）→ 按 §9 改土豆那份配置 → 跑"名字全命中"的验收（§11）。
