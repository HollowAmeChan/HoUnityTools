# 面捕命名权威：树名 / 参数 / 门 / 切片 / 槽位

**这里定名字，别处只引用。** 名字是唯一能把「混合树里的格子」与「去 DCC 做形态键时的那张清单」对上的东西；
散着拼字符串迟早会漂（[控制器结构](FACE_TRACKING_CONTROLLER_STRUCTURE.md) §5 原来那条规矩说的就是这件事）。
2026-09-27 起，**树族不再用编号**（`M01`/`E01L`/`B01L`… 那套只对表内部排序友好，读起来没有任何信息）。

分工：**42 个树名的清单**由本文 §2 与 [契约表 E2](VTS_HIGH_QUALITY_FACE_CONTRACT.md)（机器可读版在
[配套目录](VTS_HIGH_QUALITY_FACE_CATALOG.json) 的 `tree_families`）共同表达；
**轴的公式与来源**在 [契约表 C/D](VTS_HIGH_QUALITY_FACE_CONTRACT.md) 与 [参数空间](VTS_FACE_PARAMETER_SPACES.md)；
**这一版控制器要做哪些树**在 [控制器：进度与轴口](VTS_HQ_CONTROLLER.md)（§2 是已落地的树与叶子语义）。

## 1. 规则

1. **ASCII、CamelCase、不含任何下划线。** 槽位名用 `__`（双下划线）分四段 ⇒ 段内不许出现下划线，
   于是"按 `__` 切"就是完整的解析规则，不需要任何约定。
2. **树名 = `<部位><语义>[L|R]`**，例如 `MouthCore`、`LidGazeL`、`BrowCoreR`。
   部位只有这几个：`Mouth` `Lid` `Gaze` `Brow` `Cheek` `Nose` `Head` `Body` `Hand` `Ctrl` `Audio`。
3. **部位与参数路径第一段同名**：`MouthCore` ↔ `Ho/Drive/Mouth/*`、`LidL` ↔ `Ho/Drive/Lid/Left/*`、
   `BrowCoreL` ↔ `Ho/Drive/Brow/Left/*`。于是"树名 → 参数 → 片段名"三段**机械对得上**，不需要对照词典。
4. **没有第二套编号。** 散文里也写树名（"`MouthCore` 的 Form/Press 已经包含笑…"），
   编号（`M01`）已在契约表与目录里全部替换（2026-09-27）。
5. 派生名字：**平行副本树** = `<树名>Expr`、**它的 1D 开关容器** = `<树名>Switch`、
   **区域子树** = `MouthRegion` / `EyeLeftRegion` / `EyeRightRegion` / `BrowRegion` / `CheekRegion`。
6. **轴名"正端在前"**（老规矩，继续用）：`BlinkWide` = +1 闭 / −1 睁大、`UpDown` = +1 上 / −1 下。
   轴名只表达**观测语义**；模型镜像/方向是**校准映射**的事（写在各行的曲线里），不靠改名解决。
7. **中性约定（2026-09-27 定，作者摆采样点全照它）**：
   * **双向轴**（`Form` `Press` `Pucker` `LeftRight` `BlinkWide` `InOut` `UpDown` `Height`）：**0 = 静息**，两端约 ±1；
   * **单端轴**（`Open` `Funnel` `Jaw` `Forward` `Squint` `InnerUp` `CheekL/R` `PuffL/R` `TongueL/R` `SneerL/R`）：**0 = 静息**、+1 = 满；
   * VB 那些**静息 0.5** 的量（`MouthSmile`、`BrowLeftY`/`BrowRightY`）**进控制器前必须重映射**（`2x − 1`）——
     否则"中性在哪"会一棵树一个说法，而 0..1 的默认曲线还会把负半边**静默夹掉**（[HO 参数规范](PARAMETER_HO.md) §3.7 有那两行的实际写法）。

## 2. 42 个树名（权威表；⚠️ 另有**我们自建**的 `MouthShift`，不在上游契约里）

`X × Y` 是二维主轴（1D 的写「轴（1D）」），条件轴不规定采样数。

| 树名 | 部位 | X | Y | 条件轴 | 是什么 |
| --- | --- | --- | --- | --- | --- |
| `MouthCore` | Mouth | Form | Open | Funnel, Press | 外嘴核心：嘴唇轮廓/口孔 |
| `MouthJaw` | Mouth | —（**1D**，没有 X 轴） | Jaw | JawSide | 下颌/口内（与 `MouthCore` 划清键）。⚠️ 我们实现成 **1D 2 格**（咬合/闭 ↔ 张开；2026-09-28 深夜从 2D 6 格降下来 —— 下巴左右只在"微张/解放咬颌"时才到 0.52，咬紧时 0.02~0.04） |
| `MouthSeal` | Mouth | Seal | Jaw | Open, Form, Funnel | 闭唇时保留下颌运动 |
| **`MouthWidth`** | Mouth | Pucker | —（1D，没有 Y 轴） | Open, Form | 嘴宽。⚠️ 我们的骨架 2026-09-28 当天三版：上午删掉 2D 版（[§5.7.22](VTS_HQ_CONTROLLER.md)）→ 下午换成 `MouthCorner` 3×3（[§5.7.23](VTS_HQ_CONTROLLER.md)）→ **傍晚以 1D 3 格回来**（槽位 `MouthWidth__Pucker__A3X<i>`，刻度 **−0.93 窄 收嘴不撅 / −0.09 中 静态 / +2.0 宽 抿嘴嘴宽**，[§5.7.24](VTS_HQ_CONTROLLER.md)） |
| **`MouthShift`** | Mouth | LeftRight | UpDown | Open, Form | **整嘴平移**（嘴整体左右/上下挪、**唇形不变**）。⚠️ **这个树名是我们自建的**（上游契约表里没有）。实现成 **3×2 = 6 格**的**残差表**（中间格 = 零位移；⚠️ Y **只有 0 / +1 两档**、负侧钳到 0 —— 用户实测「shift 根本不会往下移动，只有 6 个点的状态」）；轴 = `Mouth/X`（`(mouthLeft − mouthRight) + (smileL − smileR)`，**有符号**、曲线 ±0.20 死区）× `Mouth/Y`（0 / +1 = 噘嘴判据）。建它的理由与实测见[控制器 §5.7.23 / §5.7.24](VTS_HQ_CONTROLLER.md) |
| `MouthShrugSplit` | Mouth | ShrugUp | ShrugDown | Open, Pucker | 上下耸唇细分 |
| ~~`MouthCorner`~~（⚠️ **我们的骨架 2026-09-28 傍晚已整棵删掉**） | Mouth | — | — | Open, Funnel, Press | ~~左右嘴角（不对称）~~。它当天走完三版：3×3 半边 `smile−frown` → 下午换轴成 `Mouth/LipPress` × `Mouth/CornerSkew` → **傍晚删掉**（左右那一维交给 `MouthShift`、收缩/舒张交给 1D 的 `MouthWidth`，[§5.7.23 / §5.7.24](VTS_HQ_CONTROLLER.md)；**契约名本身仍保留**）。历史实现细节：`Mouth/LipPress` × `Mouth/CornerSkew`（槽位名 `MouthCorner__LipPress__Skew__A3X<i>Y<j>`）—— 理由：抿嘴与"两边一起外拉"在**嘴角**这一层分不开（`dimple` 0.795 vs 0.577），能分开它们的是**唇**（`press` 0.643 vs 0.318、`shrug` 0.845 vs 0.600），见[控制器 §5.7.23](VTS_HQ_CONTROLLER.md) |
| `MouthUpperRaise` | Mouth | UpperL | UpperR | Open, Press | 上唇左右展开/露齿 |
| `MouthLowerDrop` | Mouth | LowerL | LowerR | Open, Press | 下唇左右展开/露齿 |
| `MouthLipRoll` | Mouth | Roll（1D） | — | Open, Jaw | 见 §3.1：我们**没有把它做成独立表**，而是把 `Mouth/Roll` 当**变体开关**用（`MouthCoreRollSwitch`：静息嘴 ↔ 猫嘴版整嘴，阈值 **0.15 / 0.30**）。轴 = **下唇卷 × 嘴角门**（2026-09-28 换：`clamp(mouthRollLower × clamp((嘴角方向 − 0.12)/0.10, 0, 1), 0, 1)`，`嘴角方向 = (酒窝左+右)/2 − (苦左+右)/2`）—— 旧口径 `√(上×下) × 死区 × 下颌增益 × 噘嘴门` 已被 36 段实测推翻（缝 −0.24）；**按键要强制猫嘴就直接写这个参数**（≥0.30） |
| `MouthLipPress` | Mouth | PressL | PressR | Open, Pucker | 左右压唇 |
| `MouthStretch` | Mouth | StretchL | StretchR | Open, Form | 左右横向拉伸 |
| `MouthDimple` | Mouth | DimpleL | DimpleR | Open, Form | 酒窝/嘴角收紧 |
| `MouthRawRound` | Mouth | Funnel | Pucker | Open, Form, Press | 原始圆口×嘟嘴（V3 合并掉的残差） |
| `MouthTongue` | Mouth | **Tongue（伸出量）** | —（**1D**，没有 Y 轴） | Open, Pucker, Funnel, Press | 舌（**1D 4 格**：默认 + 沿对角线 3 步，张嘴量烘在每段里）＋唇/齿接触 |
| `MouthJawSide` | Mouth | JawSide | LeftRight | Jaw, Open | 下颌偏移与偏嘴 |
| `MouthShrugBase` | Mouth | Shrug（1D） | — | Open, Pucker | 合并耸唇基础（`MouthShrugSplit` 做细分） |
| `LidL` / `LidR` | Lid | BlinkWide | Squint | EyeSmile | 眼睑完整姿势（笑眼是第三维） |
| `LidGazeL` / `LidGazeR` | Lid | BlinkWide | UpDown | GazeX, Squint, EyeSmile | 眼睑随视线/极端视线残差 |
| `LidMouthL` / `LidMouthR` | Lid | LeftRight | BlinkWide | EyeSmile | 偏嘴带动眼周 |
| `LidBoth` | Lid | LidL | LidR | SquintL, SquintR | 双眼非对称残差（同步算法在中间层） |
| `GazeL` / `GazeR` | Gaze | InOut | UpDown | 无 | 眼球注视（**每侧独立**）。⚠️ **我们没建树**（2026-09-27）：朝向交给 Warudo 的 LookAt + IK，4 根轴只作为出口发布 |
| `BrowCoreL` / `BrowCoreR` | Brow | Down | OuterUp | InnerUp | 该侧眉核心（压眉/外眉抬起/内眉抬起） |
| `BrowEyeL` / `BrowEyeR` | Brow | Height | BlinkWide | InnerUp, Squint | 眉眼接触（眉压＋睁大） |
| `BrowCenter` | Brow | ExpressionL | ExpressionR | 无 | 中央眉/额头协同 |
| `CheekSquint` | Cheek | CheekL | CheekR | Form, LidL, LidR | 双颊收紧。⚠️ **我们没建树**（二次元角色表现不了颊） |
| `CheekPuff` | Cheek | PuffL | PuffR | Open, Seal, Form, Pucker | 鼓腮（**分左右**）＋闭口/嘴型接触 |
| `CheekPuffTongue` | Cheek | TongueL | TongueR | PuffL, PuffR, Jaw | 鼓腮×伸舌的极端组合残差 |
| `NoseSneer` | Nose | SneerL | SneerR | UpperL, UpperR, Form | 鼻翼/鼻唇 |
| `HeadAim` | Head | AngleX | AngleY | AngleZ | 头部朝向（姿态输出） |
| `HeadPos` | Head | PosX | PosY | PosZ | 头部位置（姿态输出） |
| `BodyAim` | Body | AngleX | AngleY | AngleZ | 身体朝向（姿态输出） |
| `BodyPos` | Body | PosX | PosY | PosZ | 身体位置（姿态输出） |
| `AudioPhoneme` | Audio | Strength | Emotion | Phoneme | 显式音素嘴库（类别分支，不是连续轴） |
| `HandPos` | Hand | PosX | PosY | PosZ, AngleX, AngleZ | 手位置（左右各自展开） |
| `HandFinger` | Hand | Curl（1D） | — | Side, Finger | 手指姿态（每指独立） |
| `CtrlStick` | Ctrl | StickX | StickY | Trigger, Button | 控制器造型/演出 |

**2026-09-27 的轴改动（顺带记在这里）**：鼓腮与吐舌在模型上本来就是**左右两组键**，而原装 V3 与 ARKit
只给一个单侧值 ⇒ 加了 4 根 HQ 轴 `HQCheekPuffLeft/Right`、`HQTongueLeft/Right`（**先两侧同跟单侧原值**，
有分侧来源时直接驱动、树不动）。于是 `CheekPuff`、`CheekPuffTongue`、`MouthTongue` 三棵树的轴跟着改成
`PuffL × PuffR` / `TongueL × TongueR`。HQ 扩展因此从 39 项变 **43 项**。

## 3. 平行副本（按键表情）

重要树可以带一份**平行副本**：与主版**同轴、同键集合，只有姿势不同**，由一个门交叉淡入（1D 树，阈值 0/1）。

| 东西 | 名字 |
| --- | --- |
| 副本树 | `<树名>Expr`（例：`LidLExpr`、`BrowCoreLExpr`） |
| 1D 开关容器 | `<树名>Switch`，`blendParameter` = 表情门 |
| 表情门 | `Ho/Drive/Gate/Expr/<表情名>`（`Smile` / `Angry` / `Wink`…）；**默认 0，中间层不写它** |

一个门可以同时驱动多张表的副本（`Smile` 同时改眼睑、眉 ⇒ 两棵 1D 树共用一个参数）。
第一版给 `LidL`/`LidR`、`BrowCoreL`/`BrowCoreR` 做副本（已在骨架里），见 [控制器：进度与轴口](VTS_HQ_CONTROLLER.md) §2.1。

⚠️ **嘴没有副本**（2026-09-27 用户定，已删 `MouthCoreExpr` + `MouthCoreSwitch`）：
「按键表情版本身对于嘴张嘴笑没有意义」—— 夸张的笑嘴 = `Form` 更大，轴上本来就够得到；
**只有轴上到不了的"性质"才值得开副本**（笑眼、怒眉）。要"按键强制某个嘴型"，走**增量开关**而不要
直接写轴（例：猫嘴 = 写 `HoExternalCatMouth` = 1 —— 跟倒V 的 `HoExternalInvertedV` 同形，
见 §3.2 与[控制器 §5.7.19](VTS_HQ_CONTROLLER.md)），不需要第二张表。

### 3.1 轴驱动的变体（**另一类**：不是按键表情）

同一种"两个孩子都是整表"的 1D 树，但**由某根轴的值选**、阈值 = 那根轴上的**实测档位**（不是 0/1）。
语义上是**分叉**：那个时刻的姿势一定是**作者画过的两张表之一（或两者的加权）**，而不是几条残差相加。

| 东西 | 名字 | 例 |
| --- | --- | --- |
| 变体树 | `<树名><驱动它的轴段词>` | `MouthCoreRoll`（由**风格轴** `Ho/Drive/Style/CatMouth` 选中的 `MouthCore` 变体 = 猫嘴版整嘴）。⚠️ 树名里的 `Roll` 是**旧轴名**留下的；改它要连带动 8 个槽位名与槽位 GUID，所以 2026-09-28 只改了轴名 |
| 1D 开关容器 | `<变体树名>Switch`，`blendParameter` = 那根轴 | `MouthCoreRollSwitch`（blend = `Ho/Drive/Style/CatMouth`，阈值 0.15 / 0.30） |

⚠️ **变体 vs 副本**：机制一样（1D + 两张整表），区别在**谁选**——副本由**按键表情门**选（0/1），
变体由**某根轴**选（实测档位）。名字上用 `Expr` / `<轴段词>` 区分，一眼能看出是哪一类。
⚠️ 变体表被选中时必须是**完整姿势**（WD 开着，没烘的格会让那几根键回默认）⇒ 作者要为用到的格画整张。
⚠️ 阈值必须显式写死（`m_UseAutomaticThresholds: 0`），否则 Unity 会在 `[0,1]` 上平摊两档、静默错位。

## 4. 参数、门、切片

```text
轴参数    Ho/Drive/<部位>[/<Left|Right>]/<轴>     例：Ho/Drive/Mouth/Form、Ho/Drive/Lid/Left/BlinkWide
区域门    Ho/Drive/Gate/<Mouth|EyeLeft|EyeRight|Brow|Cheek>      默认 1，中间层写常量行
表情门    Ho/Drive/Gate/Expr/<表情名>                            默认 0，中间层不写
恒 1 权重 Ho/Drive/W/One                                         默认 1，没人写；**Direct 的每个子节点都要挂参数**，
                                                                所以"永远全量生效"的那一格也得有个参数
切片权重  Ho/Drive/Slice/<树名>/<切片名>                          例：Ho/Drive/Slice/MouthCore/Funnel0Press0
```

代码只认其中 4 个名字（`HoFaceNaming.cs`：`Ho/Drive` 根、`BlinkWide`、`Squint`、`LidAxis()`），
其余全是**作者约定** + 中间层配置文件里的行名。**这些行现在真的写出来了**：发货那份
`Editor/FaceTracking/Profiles/ho-iPhoneVTS.hoface.json` 里 40 行 `Ho/Drive/*`（逐行清单见
[HO 参数规范](PARAMETER_HO.md) §3.7）。

## 5. 槽位名（= 片段名）

```text
二维： <树名>__<X语义>__<Y语义>__A<n>X<i>Y<j>       例：LidL__BlinkWide__Squint__A3X2Y0
一维： <树名>__<轴语义>__A<n>X<i>                例：MouthShrugBase__Shrug__A3X1
副本： 把 <树名> 换成 <树名>Expr                   例：LidLExpr__BlinkWide__Squint__A3X0Y0
```

* `<X语义>`/`<Y语义>` 用 §2 表里的轴段词（`Form`/`Open`/`BlinkWide`/`UpDown`…），**不变形**。
* `A<n>` = **X 轴刻度数**（⚠️ **允许非方阵**：嘴上就有 4×3 的 `MouthCore` 与 3×2 的 `MouthShift`，分别写 `A4` / `A3`）；`X<i>Y<j>` 从 0 起、**左下为原点、永不出现负号**，可小数（`X0.5`）。
* **方阵不必铺满**：X 走三档、Y 只用 `Y0`/`Y2` 是合法的（眼睑那 6 个槽位就是这么摆的）。
  ⚠️ **2026-09-28 更正：不再是"方阵"** —— 形状由 `A<n>`（X 档数）与规则表里的 `ys`（Y 档数）一起决定；照"方阵+留空"去读会把 3×2 表的 `Y1` 当成"3 行里的中间行"（实际是 2 行的顶行）（[控制器结构](FACE_TRACKING_CONTROLLER_STRUCTURE.md) §5.1 有完整论证）。
* 一个槽位 = **一个多键姿势片段**（不是"一键一片段"）；未填的槽有明确的 fallback（[契约表 §F](VTS_HIGH_QUALITY_FACE_CONTRACT.md)）。

## 6. 轴段词（槽位名里那两段从哪来）

| 轴段词 | 轴参数 | 正端 / 负端 | 说明 |
| --- | --- | --- | --- |
| `Form` | `Mouth/Form` | +1 笑 / −1 sad | **读作"净笑量"**（2026-09-28 起负半轴 = **sad**）：`[(smileL+smileR) + dimple/2 − 2·sad] / 2`，其中 `sad = max(frown均值, 1.5×(stretch均值 − (0.42·jawOpen + 0.05)))`（闭嘴走 `frown`、张嘴走 `stretch` 残差；"噘"不再在这根轴里）。
VB `MouthSmile` 静息 0.5 ⇒ **必须重映射成 0 中性**。⚠️ 见下面那条"名字为什么还叫 `Form`" |
| `Open` | `Mouth/Open` | +1 开 / 0 闭 | VB `MouthOpen` |
| `Funnel` | `Mouth/Funnel` | +1 漏斗 | VB `MouthFunnel` |
| `Press` | `Mouth/Press` | +1 展/露齿 / −1 压/卷 | VB `MouthPressLipOpen`（双向） |
| `Jaw` | `Mouth/Jaw` | +1 张口 | ARKit `jawOpen`；`Forward` = `jawForward` |
| `Pucker` | `Mouth/Pucker` | +1 展 / −1 收 | VB `MouthPucker`（dimple−pucker 合成，双向） |
| `LeftRight` | `Mouth/X` | **+1 往左 / −1 往右** | **整嘴左右平移**（2026-09-28 **上午退役、当天下午随 `MouthShift` 重建**，[控制器 §5.7.22 / §5.7.23](VTS_HQ_CONTROLLER.md)）：`(mouthLeft − mouthRight) + (smileL − smileR)`，曲线 **±0.20 死区**（把"撇嘴"那点位移压成 0）。⚠️ **旧词典写作 `RightLeft` 是猜的，与公式相反**。实测：整嘴平移 `mouthLeft/Right` **0.963~0.976** vs 嘴角撇 **0.047~0.136** ⇒ 缝 **+0.594** |
| `UpDown`（嘴） | `Mouth/Y` | **+1 上 / 0**（⚠️ 负侧到不了 ⇒ 树里钳到 0，只有两档） | **整嘴上下平移**（2026-09-28 下午新建、傍晚收成 **0 / +1 两档**，[§5.7.24](VTS_HQ_CONTROLLER.md)）：+ 上 = 噘嘴判据（膝 0.45 + 鼻门 0.54/0.05）、− 下 = 闭唇张开下颌（膝 **0.35** + **相对唇门**
 `(mouthClose − jawOpen + 0.10)/0.10`）。⚠️ 上侧是"准开关"（静息底噪 0.78 以下没有可用中间档） |
| `Pucker`（嘴宽） | `Mouth/Pucker` | **+ 宽 … − 窄** | **嘴宽**（`MouthWidth` 那棵 **1D 3 格**表的轴，2026-09-28 傍晚重返）= `2×(dimpleL + dimpleR) − pucker`（⚠️ **2×和**）；刻度实测 **−0.93 收嘴不撅（窄）/ −0.09 静态（中）/ +2.0 抿嘴嘴宽（宽）**（宽端量纲是 2）。[§5.7.24](VTS_HQ_CONTROLLER.md) |
| ~~`LipPress`~~ / ~~`Skew`~~ | ~~`Mouth/LipPress`~~ / ~~`Mouth/CornerSkew`~~ | — | ⚠️ **只在 2026-09-28 下午存在了几个小时**（`MouthCorner` 3×3 的两根轴），**傍晚随那棵树一起删掉**（[§5.7.23 / §5.7.24](VTS_HQ_CONTROLLER.md)）—— 段词本身留给以后要用的人 |
| `CornerL` / `CornerR` | `Mouth/CornerL`、`CornerR` | **0…1（是"量"、不钳 0/1）** | **该半嘴角的外拉量**（2026-09-28 下午**重新定义**）= `dimple + stretch − smile − frown`（× 下颌门 `(0.20 − jawOpen)/0.10`）；原来是半边 `smile − frown`（sad 折进 `Mouth/Form` 负半轴后作废）。⚠️ **必须减 `smile`/`frown`**，否则笑把 dimple 顶到 0.33、直接点着嘴角层 |
| `Shrug` / `ShrugUp` / `ShrugDown` | `Mouth/Shrug` | +1 耸 | VB `MouthShrug`（合并）/ 上下拆分 |
| `TongueL` / `TongueR` | `Mouth/TongueL`、`TongueR` | 0…1 | `HQTongueLeft/Right`（先两侧同跟 `tongueOut`） |
| `PuffL` / `PuffR` | `Cheek/PuffL`、`PuffR` | 0…1 | `HQCheekPuffLeft/Right`（先两侧同跟 `cheekPuff`） |
| `BlinkWide` | `Lid/*/BlinkWide` | **+1 闭 / −1 睁大** | `eyeBlink − eyeWide`（代码里唯一认的轴名之一） |
| `Squint` | `Lid/*/Squint` | +1 眯 / 0 不眯 | `eyeSquint`（单端） |
| `InOut` / `UpDown` | `Gaze/*/X`、`Y` | +1 内/+1 上 | `eyeLookIn − eyeLookOut` / `eyeLookUp − eyeLookDown` |
| `Height` | `Brow/*/Y` | −1 压眉 … **0 静息** … +1 抬眉 | = 2×VB `BrowLeftY`/`BrowRightY` − 1（VB 静息 0.5，里面还掺了偏嘴联动） |
| `InnerUp` | `Brow/*/InnerUp` | +1 内眉抬起 | `browInnerUp` |
| `CheekL` / `CheekR`、`SneerL` / `SneerR` | `Cheek/*`、`Nose/*` | 0…1 | 直通（分侧本来就是两个键） |
| `AngleX/Y/Z`、`PosX/Y/Z` | `Head/*`、`Body/*` | — | 姿态链的轴，**不进面部矩阵** |
| `Strength` / `Emotion` | `Audio/*` | — | 音素库的幅度/表情条件 |

### 6.1 名字为什么还叫 `Form`（2026-09-27 用户定：**不改**）

有人（包括用户）提过把它改叫 `Smile` —— 因为**选项 C 之后它确实只剩"笑量"这一件事**
（负侧三族各回各家：苦 → **`MouthCore` 的 Form = −1 列**、噘 → 倒V 形态、卷唇 → `MouthCoreRoll` 变体，
树的 X 只留 `0 静息 / +0.75 常态笑 / +1 大笑`），"多重形态"那层含义已经没了。三家对照如下：

| 血统 | 这根轴叫什么 | 中性 / 量纲 |
| --- | --- | --- |
| **VBridger / VTS 追踪参数** | **`MouthSmile`**（profile 里就是这条公式，0..1） | 静息 **0.5** |
| **Live2D Cubism** | **`ParamMouthForm`**（VTS 官方**推荐接法**就是 `MouthSmile → ParamMouthForm`） | **0**，−1 怒 ↔ +1 笑 |
| **VRCFT Unified** | 全局合并轴 `SmileSad` / `SmileFrown`（单侧 `SmileSadLeft/Right`） | **0** 双向；成熟资产用 **4 姿势 1D**（阈值 −0.8/−0.1/+0.1/+0.8） |

**决定保留 `Form`**，理由三条：

1. **它不是自造词**：Cubism 标准参数就叫 `ParamMouthForm`，而且 VTS 官方把 `MouthSmile` 推荐接进它；
   我们自己的合同（[高质量契约](VTS_HIGH_QUALITY_FACE_CONTRACT.md) E1）也早就把它定义成
   "`MouthSmile` 经模型校准映射，建议 −1…1、中性 0" —— 换名等于把一个有出处的术语换成半个新词。
2. **改叫 `Smile` 会撞字面**：控制器里已有 `Ho/Drive/Gate/Expr/Smile`（**0/1 表情门**，切夸张版姿势）。
   两者路径不同、不会撞参数，但读者会在同一份面板里看到两个 "Smile"（一个连续量、一个开关）。
3. **改叫 `SmileSad`/`SmileFrown` 会过度承诺**：那是 VRCFT 给**双向**轴的名字，
   而我们的树**负列就是苦**（`MouthCore` 的 Form = −1，2026-09-28 傍晚补，[控制器 §5.7.24](VTS_HQ_CONTROLLER.md)）⇒ 名字会指向一处树里到不了的区域。

⚠️ 唯一要记住的是**读法**：在这套东西里 **`Form` = 净笑量**
（`[(smileL+smileR) + dimple/2 − (frownL+frownR) − pucker] / 2`），**仍会出负值**（苦/噘压过笑时），
负值照旧是"正值的抑制量"、照旧发布到 Hub，只是树把它钳到 X0 = 静息。要细节去 G1 拿 `mouthSmile*`/`mouthFrown*`。

## 7. 改名流程（让"权威"真的生效）

1. 改**这里**（本文 §2/§6）→ 2. 改生成器里的 `tree(...)` 那一处（`.research/vts-creator-workflow/build_hq_catalog.py`）
→ 3. 重跑生成器：契约表 AUTO 段与 `VTS_HIGH_QUALITY_FACE_CATALOG.json` 一起更新
→ 4. 跑一遍"旧编号应该搜不到"：`grep -E '\b(M0[0-9]|E0[0-9][LR]?|B0[0-9][LR]?|C0[0-9]|N0[0-9]|H0[0-9]|A01|P0[0-9])\b' docs/*.md`
（只有本文 §1 与契约表顶部那条"2026-09-27 修订"应当命中）
→ 5. 两边提交（包侧过 `compile-check-pkg.ps1` + `ensure-bom.ps1 -Fix`）。

⚠️ **生成器不在版本控制里**（`.research/` 是 scratch）：它是"一次改一处"的便利工具，
**权威本体是本文 + 契约表的 AUTO 段 + `VTS_HIGH_QUALITY_FACE_CATALOG.json`** —— 这三样都在仓库里，
手改也成立，但三处必须同时改。

## 8. 现存例外（还没套上这套名字的地方）

* **土豆那份控制器**（`PTP_CTR_Face_ARKit.controller`）里的树还是老形状：根 Direct `Ho/00 Drive Tree`
  + `LidL`/`LidR`（**名字恰好合规**）+ `EyeRegion`/`LipRegion`（区域名待换成 `EyeLeftRegion`/`MouthRegion`），
  叶子参数是**裸 ARKit 名**（`jawOpen`、`eyeBlinkLeft`…）。新控制器按本文重建，老的那份不动。
* **中间层配置**（`ho-iPhoneVTS.hoface.json`）里的输出行名还是 VB/VTS 原名（`MouthOpen`、`MouthSmile`…）
  —— **40 行 `Ho/Drive/*` 已经加进 profile 了**（29 轴 + 4 个区域门 + 4 条切片 + **3 条形态契约行**（`Gate/MouthStyle`、`Style/InvertedV`、`Style/CatMouth`）；逐行公式见 [参数规范 §3.7](PARAMETER_HO.md) 与

  [控制器：进度与轴口](VTS_HQ_CONTROLLER.md) §3），出口那 90 行按原样保留。

## 9. 已经用这套名字落地的树（对照 §2 的 42 家族）

| 已落地 | 说明 |
| --- | --- |
| `MouthCore` **+ `MouthCoreRoll`（变体）** · `MouthJaw` · `MouthShift` · `MouthWidth` · `MouthTongue` · **`Cheek`（鼓嘴，4 格）** · `LidL`/`LidR`（+ `Expr`）· `BrowCoreL`/`BrowCoreR`（+ `Expr`）· `NoseUp` · **`InvertedV`（倒V，2 格）** | tranche 1：**6 张 2D 主表（`MouthCore` / `MouthShift` / `LidL` / `LidR` / `BrowCoreL` / `BrowCoreR`）+ 1 张 2D 变体表 + 4 张 2D 副本 + 1 张 2D 形态表（`Cheek`）+ 5 张 1D 片段表（`NoseUp` / `InvertedV` / `MouthWidth` / `MouthTongue` / `MouthJaw`）+ 5 个 1D 开关 + 4 个区域 + 根 = 控制器共 27 棵树 / **83 个槽位**（2026-09-28 深夜：舌头 4 格入 1D、下巴 2 格入 1D、整嘴平移挖成 4 格）**（舌头那条表 2026-09-28 深夜定成 **1D 4 格**：默认 + 沿对角线 3 步，见[控制器 §5.7.29](VTS_HQ_CONTROLLER.md)）（⚠️ 2026-09-28 当天三版：删 2D `MouthWidth` ⇒ 92→83、27→26 → 下午重建 `MouthShift` + 嘴角 3×3 ⇒ 回到 92 → **傍晚删嘴角那棵、`MouthWidth` 以 1D 回来 ⇒ 89**，见[控制器 §5.7.22–§5.7.24](VTS_HQ_CONTROLLER.md)；2026-09-27 那一轮：删 `MouthCoreExpr`/`MouthCoreSwitch`（嘴没有按键表情版）、删 `GazeL`/`GazeR`（朝向交给 Warudo 的 LookAt + IK）、删 ARKit 的 `CheekSquint`/`CheekPuff`（二次元表现不了）、`NoseSneer` 收成 `NoseUp` 1D、`MouthJaw` 换轴、加 `MouthCoreRoll`+开关；**`MouthForward` 1D 建了又删** —— 那根线降级成辅助变量/出口，见[控制器 §5.4.1](VTS_HQ_CONTROLLER.md)；当晚再加**两条风格化形态子树** `InvertedV` / `Cheek` + 形态门，见[控制器 §5.6](VTS_HQ_CONTROLLER.md)） |
| ~~**`MouthCorner`**~~（2026-09-27 加；⚠️ **2026-09-28 傍晚已整棵删掉**，[控制器 §5.7.24](VTS_HQ_CONTROLLER.md)——它的两份职责被拆走：左右 ⇒ `MouthShift`、收缩/舒张 ⇒ 1D 的 `MouthWidth`） | 历史轴 = `Mouth/LipPress` × `Mouth/CornerSkew`（更早 = `CornerL` × `CornerR`，即合同表 D 的 `HQSmileFrownLeft/Right`）。
**立它的理由**：`Mouth/Form` 的负侧同时被"嘴角下弯"和"噘嘴"驱动（实测噘嘴 −0.5、噘嘴+苦脸 −0.7），一根轴两件事 ⇒ 把**嘴角**单独拆出来做残差表，噘嘴留在"嘴宽那根轴"（`MouthWidth`，2026-09-28 上午已删、下午重建成 `MouthShift` —— 整嘴平移现在也在那张表里）、`Form` 的表达式与出口行都不动。

见[控制器：进度与轴口](VTS_HQ_CONTROLLER.md) §3.4 |

其余家族（`MouthSeal`、`MouthShrugSplit`、`MouthUpperRaise`、`MouthLowerDrop`、`LidGaze*`、`BrowCenter`、
`HeadAim`/`BodyAim`、`AudioPhoneme`、`Hand*`、`CtrlStick`…）仍是契约里预留、骨架里还没有。
