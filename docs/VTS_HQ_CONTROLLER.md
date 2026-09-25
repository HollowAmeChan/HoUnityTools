# 带一层门控的 VTS 原生语义控制器：2D 混合树规划与装配草案

**状态：草案 v2**（v1 的门控结论已按你的意见改：**按键表情的平行副本会让门控嵌套，这一层嵌套是接受的**）。
要你拍板的在 §11。分工：**能表达什么**（42 树族 × 轴 × 条件）见 [VTS 全量契约表](VTS_HIGH_QUALITY_FACE_CONTRACT.md)；
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

| 树族 | X 轴 | Y 轴 | 条件轴 | 备注 |
| --- | --- | --- | --- | --- |
| **M01 外嘴核心** | `Form` = 2×`MouthSmile` − 1 | `Open` = `MouthOpen` | `Funnel`, `Press` | 最重要的那张表；`MouthSmile` 静息 0.5 ⇒ 必须映射成 −1…1 的中性 0 |
| **E01L / E01R 眼睑** | `BlinkWide` = `eyeBlink − eyeWide`（±1，0 中性） | `Squint` | `EyeSmile`（②） | 现控制器已经在跑（6 个采样点）；坐标别和 VRCFT 的 0…1/0.75 混用 |
| **E02L / E02R 注视** | `GazeX` = `EyeLeft_x`/`EyeRight_x` | `GazeY` | — | **每侧独立**，不从合并量还原；独立左右眼是另一种契约 |
| **B01L / B01R 眉** | `BrowY` = `BrowLeftY`/`BrowRightY` | `BrowInnerUp` | — | VB 的 BrowY 里已经掺了 `(mouthRight−mouthLeft)/8`（说话时眉会动一点，是 VB 的联动） |
| **C01 双颊** | `CheekSquintL` | `CheekSquintR` | `Form`, `Lid` | 直通即可 |
| **N01 鼻翼** | `NoseSneerL` | `NoseSneerR` | 上唇抬起 | 直通即可 |
| **M04 横向嘴型** | `MouthPucker`（−1…1 双向） | `MouthX` | `Open`, `Form` | 对 M01 的嘴宽/偏嘴修正 |
| **M14 舌** | `TongueOut` | `JawOpen` | `Open`, `Pucker`, `Funnel`, `Press` | V3 去掉了 `1−tongueOut` 抑制 ⇒ 更容易同时有值，值得配表 |
| **C02 鼓腮** | `CheekPuff` | `MouthPucker` | `Open`, `Seal`, `Form` | 鼓腮 × 嘴型 |

### ② 加一根 HQ 行就能做（公式都在契约表 D，机械可抄）

| 树族 | X 轴 | Y 轴 | 新行 |
| --- | --- | --- | --- |
| M02 下颌/口内 | `JawOpen` | `HQJawForward` = `jawForward` | 条件 `HQJawX` = `jawRight − jawLeft` |
| M05 上下耸唇 | `HQUpperLipShrug` = `mouthShrugUpper` | `HQLowerLipShrug` = `mouthShrugLower` | — |
| M06 左右嘴角 | `HQSmileFrownL` = clamp(`smileL−frownL`) | `HQSmileFrownR` | — |
| M07 上唇左右展开 | `HQUpperLipRaiseL` = `mouthUpperUp_L` | `…Right` | 条件 `Open`/`Press` |
| M08 下唇左右展开 | `HQLowerLipDropL` = `mouthLowerDown_L` | `…Right` | 同上 |
| E01 笑眼条件 | — | — | `HQEyeSmileL/R` = `mouthSmile_L/R`（**现在缺这两行**，见 §9） |

### ③ 先一维（**别急着配 2D**）：`MouthX`、`MouthShrug`、`MouthPucker`、`HQJawX`、`HQJawForward`、`CheekPuff`、`TongueOut`
契约 §4 的理由：这些是**双向轴或独立自由度**，先独立；只有"组合后的造型明显不成立"时才升级成条件修正。

### ④ 不做 2D：`FaceAngle*` / `BodyAngle*` / `FacePosition*` / `BodyPosition*`（3D 宿主走**姿态链**，不画面部矩阵）、
`FaceFound` / `Hotkey` / `Timestamp`（控制数据，不是美术维度）、音素（**类别权重**，没有自然距离，不是连续轴）、
`P01–P03` 手/控制器（演出通道）。

## 3. 门控：区域门 × 表情副本门（**嵌套，已确认可接受**）

### 3.1 两层是什么

| 层 | 参数 | 是什么 | 谁写 |
| --- | --- | --- | --- |
| **第 1 层：区域门** | `Ho/Drive/Gate/{Mouth, EyeLeft, EyeRight, Brow, Cheek}` | 整块算不算数（默认 1） | 中间层**常量行**（`expression` 留空 + `defaultValue = 1`） |
| **第 2 层：表情副本门** | `Ho/Drive/Gate/Expr/<名>`（`Smile`、`Wink`、`Angry`…） | 这张表走"普通版"还是"按键表情版" | 某个外部来源（§3.3） |

```
Ho/00 Drive Tree (Direct)
└─ Mouth            ← 权重 = Ho/Drive/Gate/Mouth            （第 1 层）
     ├─ M01_main    ← 权重 = Slice/M01/ExprOff = 1 − Gate/Expr/Smile
     │    └─ 4 张条件切片（权重 = Slice/M01/F0P0 …，和恒 1）
     ├─ M01_expr    ← 权重 = Slice/M01/ExprOn = Gate/Expr/Smile   （第 2 层，平行副本）
     │    └─ 同名 4 张切片（**同轴、不同姿势**）
     ├─ M02/Jaw     ← 权重 = 1（不参与表情副本：按键表情不该改下颌）
     └─ M04/PX      ← 权重 = 1
```

**为什么平行副本必须做成"树内权重交叉淡入"，不能做成 Animator 状态切换**：
影子台是 **`Update(0)` 静态采样**（[控制器结构](FACE_TRACKING_CONTROLLER_STRUCTURE.md) §3、[设计](FACE_TRACKING_DESIGN.md) §4），
而状态过渡是**按时间推进**的 —— `dt = 0` 时过渡永远走不完。按键表情只有"参数驱动的权重"这一条路。

### 3.2 嵌套的后果（认下来，但要知道代价在哪）

* 有效门控 = `Gate/Mouth × Gate/Expr/Smile × Slice/…`，**乘积只活在树里**：Hub 里读到的是**各个因子**，
  不是乘积。下游要"这块实际贡献多少"就自己乘（三个因子都在 Hub 里）——
  **真要让乘积本身也可见**，就在中间层多写一行 `Ho/Drive/Eff/<族>/<切片>`（乘法由中间层做，不是从树里读回来）。
* 让**每个区域内部的总权重恒为 1**（`ExprOff + ExprOn = 1`、切片权重和 = 1）：
  这样就不会有"权重和不足 1 ⇒ 混进启动值"那份意外（[踩过的坑 · 混合树](pitfalls/BLEND_TREE_TRAPS.md) §4）。
  区域门调到 1 时这块完全由树决定；把区域门调小 = **让这块回到启动姿势**，这是门控唯一的语义。
* 主/副本权重的**互补值由中间层算**（`1 − g` 也是一行），树里不留"1−x"这种看不见的运算。

### 3.3 表情开关的来源：**手机那条现在不能用**（实测）

⚠️ 协议里的 `Hotkey` **实测恒为 −1**：安卓与 iPhone 两台都是（"按 VTS app 内的屏幕热键才给 1–8"，
而那三个快捷键不输出）—— 见 [参数实机验证表](PARAMETER_DEVICE_VERIFICATION.md) §0.2 与 [HO 参数规范](PARAMETER_HO.md) §1。
所以"按键"这个来源要另选，候选（**门只是一行参数，谁写都行** —— 这正是"行侧 gate"的好处）：

1. **VTS 公开 API**（`ExpressionActivation` / `HotkeyTrigger`）：需要一个适配器去连 VTS 的 WS API；
   Unity 侧与 Warudo 侧各要一条（mod 里已经有一半 VTS 服务端的代码可借）。
2. **本地按键**：Unity 调试面板给几个开关先跑通结构；Warudo 侧用官方键盘输入节点写同一个参数。
3. **VB 的表情**（如果 VB 的高级档把它暴露成线）：那是"VB 高质量拓展"的一部分，需要它自己输出一行。

本稿的态度：**结构与命名先定死，来源后接** —— 树只认 `Ho/Drive/Gate/Expr/<名>`。

## 4. 平行副本给谁做（v1 的"重要"要具体化）

| 做副本 | 为什么 | 不做副本 |
| --- | --- | --- |
| **M01 外嘴** | 按键表情最常改的就是嘴（笑/怒/吐舌的整套嘴形） | M02 下颌、M04 嘴宽：表情不改下颌与嘴宽，保持原样 |
| **E01L / E01R 眼睑** | 笑眼/眯眼/闭眼的表情（`Wink`、`Smile`） | E02 注视：按键表情一般不动眼球（要动就让作者再开一张） |
| **B01L / B01R 眉** | 怒/悲/惊讶全靠眉 | C01 颊、N01 鼻：先不做 |
| （可选）**M14 舌** | 吐舌类表情 | — |

副本的**键集合与轴与原表完全一致**（这是"平行副本"的定义：同轴不同姿势）。装配器只按**槽位名**填片段 ⇒
副本的槽位用**同族带后缀**的命名：`M01E__Form__Open__A3X{i}Y{j}`（`E` = expression 副本）。

## 5. 键集合的分工规则（比树形更重要）

1. **每棵树拥有互不重叠的键集合**（写进契约表：族 → 键列表）。Direct 是**加法**、不归一化（`0.6+0.8 → 140`）：
   键不重叠时"基础 + 修正"才是叠加；重叠时值会相加。
2. **语义会重叠的必须并进同一张 2D 表**，靠摆采样点表达（例：眼睑"闭眼 × 眯眼"的多个组合姿势）。
   判据一句话：**两棵树如果会写同一个键，就得合并，或者在中间层先选一格。**
3. **基础 / 条件覆盖 / 修正**三类槽按 [契约表 §F](VTS_HIGH_QUALITY_FACE_CONTRACT.md) 的 fallback：
   修正槽没填 = **零残差**（不影响基础）；条件覆盖槽没填 = **复用同一份基础片段**（不是留空 Motion）。
   装配器只做"同名片段替换 + 报缺项"，**不会替你补洞**。
4. 修正树写的是**残差**：做完单侧上唇姿势要减掉 M01 已经表达的部分再放进 M07，不能全量再加一遍。

## 6. 第一版落地清单（tranche 1）

| 族 | 2D 轴 | 条件 | 槽位前缀 | 建议采样 | 副本 |
| --- | --- | --- | --- | --- | --- |
| M01 外嘴核心 | Form × Open | Funnel × Press（先只做 1 张，其余复用基础片段） | `M01__Form__Open__A3X{i}Y{j}` | 3×3 | ✅ `M01E__…` |
| E01L / E01R 眼睑 | BlinkWide × Squint | EyeSmile | `LidL__BlinkWide__Squint__A3X{i}Y{j}`（沿用现名） | 3×2（现有 6 点） | ✅ `LidLE__…` |
| E02L / E02R 注视 | GazeX × GazeY | — | `E02L__GazeX__GazeY__A3X{i}Y{j}` | 3×3（或十字 5 点） | — |
| B01L / B01R 眉 | BrowY × InnerUp | — | `B01L__BrowY__InnerUp__A2X{i}Y{j}` | 2×2 | ✅ |
| M02 下颌 | Jaw × HQJawForward | HQJawX | `M02__Jaw__Forward__A3X{i}Y0` | 3×1 | — |
| M04 嘴宽/偏嘴 | Pucker × MouthX | — | `M04__Pucker__X__A3X{i}Y{j}` | 3×3 | — |
| C01 双颊 | CheekL × CheekR | — | `C01__CheekL__CheekR__A2X{i}Y{j}` | 2×2 | — |
| N01 鼻翼 | SneerL × SneerR | — | `N01__SneerL__SneerR__A2X{i}Y{j}` | 2×2 | — |

**先不做**：M03/M05–M16 的细分修正、C02/C03、E03/E04/E05、B02/B03、H01–H04、A01、P01–P03。
它们在契约里都留着，加的时候不动已经做好的树。

## 7. 参数契约（控制器认这些名字）

三段式 `Ho/Drive/<部位>/<轴>`（ASCII、`HoFaceNaming` 的根不变）：

```text
Mouth:  Form(-1..1, 0中性) Open(0..1) Funnel(0..1) Press(-1..1) Jaw(0..1) Pucker(-1..1) X(-1..1) Shrug(0..1) Tongue(0..1)
Lid:    Left|Right / BlinkWide(-1..1, 0中性) Squint(0..1) EyeSmile(0..1)
Gaze:   Left|Right / X(-1..1) Y(-1..1)
Brow:   Left|Right / Y(0..1) InnerUp(0..1)
Cheek:  Left|Right/Squint(0..1) Puff(0..1)      Nose: Left|Right/Sneer(0..1)
Gate:   Mouth EyeLeft EyeRight Brow Cheek        Gate/Expr/<名>
Slice:  <族ID>/<切片>（含 ExprOn/ExprOff）
```

⚠️ **不借 VB/VTS 的原名当参数名**：配置里那个 `MouthSmile` 是**VB 公式的输出**，不是 VTS 自己那个 `MouthSmile`；
同名同义的误会会长期留在表里（[参数标准表](PARAMETER_STANDARDS.md) 的分工正是"外部怎么定 vs 我们选什么"）。

## 8. 采样点与槽位（动画后做，但命名现在定死）

* 槽位 = **一个多键姿势片段**，命名 `<族>__<X轴>__<Y轴>__A<n>X<i>Y<j>`（现场例子：`LidL__BlinkWide__Squint__A3X0Y2`）。
* ⚠️ **工具缺口**：现有「形态键动画」工具（`Editor/AnimationTools/HoBlendShapeClipBuilder.cs`）出的是
  "**一键一片段**、值恒 100" —— 那是给 Direct 每键叶子用的，**不是 2D 采样点的姿势**。
  填 2D 表需要"**姿势烘焙**"：把调试面板里调好的滑条姿势（会话 `SetPreview` 那套）存成一个以槽位命名的多键片段。
  **动画你后做，但这条工具得排在它前面。**
* 采样允许**稀疏、非方阵**，多个采样点可以共用同一份片段（闭眼时不同 Squint 可以是同一个闭眼姿势）。
* 非方阵的插值语义由 2D Freeform Cartesian 定，**先摆点看结果**，不要假设。

## 9. 土豆那份配置怎么改（改造清单）

| 动作 | 内容 |
| --- | --- |
| **改名** | 20+ 行 VB 语义的 `parameter` 换成 §7 的轴名（`MouthOpen` → `Ho/Drive/Mouth/Open`、`MouthSmile` → `…/Form`、`MouthPressLipOpen` → `…/Press`…） |
| **补曲线** | `Form` 要 0…1 → **−1…1**（现在那行是直通，中性 0 的树不能用）；`Open`/`Funnel`/`Press` 各自标定端点 |
| **保留** | 4 根 `Ho/Drive/Lid/*`（已经是这套坐标）、52 行裸 ARKit 直通（进 Hub，进不进树由树决定） |
| **新增** | 门控常量行 5 行；`Gate/Expr/<名>` 行（来源见 §3.3）；`HQEyeSmileL/R`（现在**缺**这两行）；tranche 1 还缺的轴行（`HQJawForward`、`HQJawX`、颊/鼻那几根直通行） |
| **切片权重行** | M01 先 4 行（双线性、和恒 1）；每个副本 2 行（`ExprOn`/`ExprOff`） |
| **不改** | `FaceAngle*` / `Body*` / `FacePosition*` / `FaceFound` / `EyeLeftXY`：**走 Hub 给下游**（姿态链、别的脚本），不是面部树的轴 |
| **清过期话** | 文件头 `notes` 里那段"Also NOT written: VBridger's own custom outputs（MouthFunnel/MouthPucker/MouthShrug/Eye_Squint_L/R/MouthPressLipOpen/BrowInnerUp）"**已经不成立了**（这些行后来加上去了），迁移时一并改写 |

判据：**要进树的轴一个都不许落在"不在控制器里"**；其余行保持 output-only 并在 notes 里写清"故意不进树"。

## 10. 装配与验收

1. 控制器编辑页**就地装配**（`HoUnityTools/面捕/控制器编辑`）：控制器是你那份、动画文件夹是槽位数据、调试对象是场景角色；
   装配只填片段 + 重绑形态键曲线，**不碰层与参数**。
2. **硬约束**（装配不拦，开始驱动才拒）：只允许形态键曲线、无 `StateMachineBehaviour`、
   **Direct 树所在状态必须 WD 开**（关掉会逐帧发散：`98.98 → 246.28 → 1059.33`）。
3. **这一版"做完"的判据**：① §7 的参数名在控制器参数表里齐全、门控默认值统一为 1；
   ② 门只有 §3 那两层（区域 + 表情副本）；③ 配置里要进树的行**全部命中**控制器参数；
   ④ 「详情」栏能列出槽位清单（"缺哪些动画"是可见的清单）。
4. **看不到的部分**：没有动画时树里全是空 Motion / 复用片段，**插值行为要等第一张表填上才看得到** ——
   这一版不谎称验收过"动起来对不对"。副本门则可以在**参数层**先验（看着权重交叉淡入）。
5. 门：包侧 `compile-check-pkg.ps1` + `ensure-bom.ps1 -Fix`；涉及 mod 的过 mod `compile-check`；两边分别提交。

## 11. 要你拍板

1. **tranche 1 清单**（§6 那 8 族 + 5 门）要不要增减？特别是**眉毛**先按"单侧 `BrowY` × `InnerUp`"还是先不做？
2. **平行副本给谁**（§4 提的 M01 + E01L/R + B01L/R）？每个副本一个 `Gate/Expr/<名>`，名字叫什么（`Smile`/`Angry`/`Wink`…）？
3. **参数名**用 `Ho/Drive/<部位>/<轴>`（本稿建议）还是沿用 VB/VTS 原名（省一次改名，但同名同义的误会留着）？
4. **表情开关来源**先接哪个（VTS API 适配器 / 本地按键 / 等 VB 输出）？**手机 `Hotkey` 那条已经废了**（实测恒 −1）。

拍完这四条，下一步就是：按 §7/§3 建控制器骨架（空 Motion + 槽位占位）→ 改配置 → 跑"名字全命中"的验收。
