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

## 2. 42 个树名（权威表）

`X × Y` 是二维主轴（1D 的写「轴（1D）」），条件轴不规定采样数。

| 树名 | 部位 | X | Y | 条件轴 | 是什么 |
| --- | --- | --- | --- | --- | --- |
| `MouthCore` | Mouth | Form | Open | Funnel, Press | 外嘴核心：嘴唇轮廓/口孔 |
| `MouthJaw` | Mouth | Jaw | Forward | JawSide | 下颌/口内（与 `MouthCore` 划清键） |
| `MouthSeal` | Mouth | Seal | Jaw | Open, Form, Funnel | 闭唇时保留下颌运动 |
| `MouthWidth` | Mouth | Pucker | LeftRight | Open, Form | 嘴宽/偏嘴修正 |
| `MouthShrugSplit` | Mouth | ShrugUp | ShrugDown | Open, Pucker | 上下耸唇细分 |
| `MouthCorner` | Mouth | CornerL | CornerR | Open, Funnel, Press | 左右嘴角（不对称） |
| `MouthUpperRaise` | Mouth | UpperL | UpperR | Open, Press | 上唇左右展开/露齿 |
| `MouthLowerDrop` | Mouth | LowerL | LowerR | Open, Press | 下唇左右展开/露齿 |
| `MouthLipRoll` | Mouth | RollUp | RollDown | Open, Jaw | 上下卷唇 |
| `MouthLipPress` | Mouth | PressL | PressR | Open, Pucker | 左右压唇 |
| `MouthStretch` | Mouth | StretchL | StretchR | Open, Form | 左右横向拉伸 |
| `MouthDimple` | Mouth | DimpleL | DimpleR | Open, Form | 酒窝/嘴角收紧 |
| `MouthRawRound` | Mouth | Funnel | Pucker | Open, Form, Press | 原始圆口×嘟嘴（V3 合并掉的残差） |
| `MouthTongue` | Mouth | TongueL | TongueR | Jaw, Open, Pucker, Funnel, Press | 舌（**分左右**）＋唇/齿接触 |
| `MouthJawSide` | Mouth | JawSide | LeftRight | Jaw, Open | 下颌偏移与偏嘴 |
| `MouthShrugBase` | Mouth | Shrug（1D） | — | Open, Pucker | 合并耸唇基础（`MouthShrugSplit` 做细分） |
| `LidL` / `LidR` | Lid | BlinkWide | Squint | EyeSmile | 眼睑完整姿势（笑眼是第三维） |
| `LidGazeL` / `LidGazeR` | Lid | BlinkWide | UpDown | GazeX, Squint, EyeSmile | 眼睑随视线/极端视线残差 |
| `LidMouthL` / `LidMouthR` | Lid | LeftRight | BlinkWide | EyeSmile | 偏嘴带动眼周 |
| `LidBoth` | Lid | LidL | LidR | SquintL, SquintR | 双眼非对称残差（同步算法在中间层） |
| `GazeL` / `GazeR` | Gaze | InOut | UpDown | 无 | 眼球注视（**每侧独立**） |
| `BrowCoreL` / `BrowCoreR` | Brow | Down | OuterUp | InnerUp | 该侧眉核心（压眉/外眉抬起/内眉抬起） |
| `BrowEyeL` / `BrowEyeR` | Brow | Height | BlinkWide | InnerUp, Squint | 眉眼接触（眉压＋睁大） |
| `BrowCenter` | Brow | ExpressionL | ExpressionR | 无 | 中央眉/额头协同 |
| `CheekSquint` | Cheek | CheekL | CheekR | Form, LidL, LidR | 双颊收紧 |
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
| 副本树 | `<树名>Expr`（例：`MouthCoreExpr`、`LidLExpr`、`BrowCoreLExpr`） |
| 1D 开关容器 | `<树名>Switch`，`blendParameter` = 表情门 |
| 表情门 | `Ho/Drive/Gate/Expr/<表情名>`（`Smile` / `Angry` / `Wink`…）；**默认 0，中间层不写它** |

一个门可以同时驱动多张表的副本（`Smile` 同时改嘴、眼睑、眉 ⇒ 三棵 1D 树共用一个参数）。
第一版给 `MouthCore`、`LidL`/`LidR`、`BrowCoreL`/`BrowCoreR` 做副本（已在骨架里），见 [控制器：进度与轴口](VTS_HQ_CONTROLLER.md) §2.1。

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
`Editor/FaceTracking/Profiles/ho-iPhoneVTS.hoface.json` 里 39 行 `Ho/Drive/*`（逐行清单见
[HO 参数规范](PARAMETER_HO.md) §3.7）。

## 5. 槽位名（= 片段名）

```text
二维： <树名>__<X语义>__<Y语义>__A<n>X<i>Y<j>       例：LidL__BlinkWide__Squint__A3X2Y0
一维： <树名>__<轴语义>__A<n>X<i>                例：MouthShrugBase__Shrug__A3X1
副本： 把 <树名> 换成 <树名>Expr                   例：MouthCoreExpr__Form__Open__A3X0Y0
```

* `<X语义>`/`<Y语义>` 用 §2 表里的轴段词（`Form`/`Open`/`BlinkWide`/`UpDown`…），**不变形**。
* `A<n>` = **每轴刻度数（方阵）**；`X<i>Y<j>` 从 0 起、**左下为原点、永不出现负号**，可小数（`X0.5`）。
* **方阵不必铺满**：X 走三档、Y 只用 `Y0`/`Y2` 是合法的（眼睑那 6 个槽位就是这么摆的）。
  用方阵而不是"3×2"是为了让"中轴在哪"不需要一条规则（[控制器结构](FACE_TRACKING_CONTROLLER_STRUCTURE.md) §5.1 有完整论证）。
* 一个槽位 = **一个多键姿势片段**（不是"一键一片段"）；未填的槽有明确的 fallback（[契约表 §F](VTS_HIGH_QUALITY_FACE_CONTRACT.md)）。

## 6. 轴段词（槽位名里那两段从哪来）

| 轴段词 | 轴参数 | 正端 / 负端 | 说明 |
| --- | --- | --- | --- |
| `Form` | `Mouth/Form` | +1 笑 / −1 垂嘴角 | VB `MouthSmile` 静息 0.5 ⇒ **必须重映射成 0 中性** |
| `Open` | `Mouth/Open` | +1 开 / 0 闭 | VB `MouthOpen` |
| `Funnel` | `Mouth/Funnel` | +1 漏斗 | VB `MouthFunnel` |
| `Press` | `Mouth/Press` | +1 展/露齿 / −1 压/卷 | VB `MouthPressLipOpen`（双向） |
| `Jaw` | `Mouth/Jaw` | +1 张口 | ARKit `jawOpen`；`Forward` = `jawForward` |
| `Pucker` | `Mouth/Pucker` | +1 展 / −1 收 | VB `MouthPucker`（dimple−pucker 合成，双向） |
| `LeftRight` | `Mouth/X` | +1 偏左 / −1 偏右 | `mouthLeft − mouthRight + smileL − smileR`；**旧词典写作 `RightLeft` 是猜的，与公式相反** |
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
  —— **39 行 `Ho/Drive/*` 轴已经加进 profile 了**（逐行公式见 [参数规范 §3.7](PARAMETER_HO.md) 与
  [控制器：进度与轴口](VTS_HQ_CONTROLLER.md) §3），出口那 90 行按原样保留。

## 9. 已经用这套名字落地的树（对照 §2 的 42 家族）

| 已落地 | 说明 |
| --- | --- |
| `MouthCore`（+ `MouthCoreExpr`）· `MouthJaw` · `MouthWidth` · `MouthTongue` · `LidL`/`LidR`（+ `Expr`）· `GazeL`/`GazeR` · `BrowCoreL`/`BrowCoreR`（+ `Expr`）· `CheekSquint` · `CheekPuff` · `NoseSneer` | tranche 1：13 张主版 + 5 张副本 = 18 张表 |
| **`MouthCorner`**（2026-09-27 加） | 轴 = 本文 §2 那一行的 `CornerL` × `CornerR`（= 合同表 D 的 `HQSmileFrownLeft/Right`）。**立它的理由**：`Mouth/Form` 的负侧同时被"嘴角下弯"和"噘嘴"驱动（实测噘嘴 −0.5、噘嘴+苦脸 −0.7），一根轴两件事 ⇒ 把**嘴角**单独拆出来做残差表，噘嘴留在 `MouthWidth`，`Form` 的表达式与出口行都不动。见[控制器：进度与轴口](VTS_HQ_CONTROLLER.md) §3.4 |

其余家族（`MouthSeal`、`MouthShrugSplit`、`MouthUpperRaise`、`MouthLowerDrop`、`LidGaze*`、`BrowCenter`、
`HeadAim`/`BodyAim`、`AudioPhoneme`、`Hand*`、`CtrlStick`…）仍是契约里预留、骨架里还没有。
