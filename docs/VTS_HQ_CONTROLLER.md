# VTS 原生语义控制器：现在落到哪 · 每格叶子的语义 · 每根轴的口

**这份文档只记三件事**，不再当计划书：

| 节 | 回答什么 | 什么时候看 |
| --- | --- | --- |
| [§1 当前进度](#1-当前进度) | 什么已经落地、什么还是空的 | 想知道"能不能开跑" |
| [§2 树与叶子](#2-现在的树每格叶子是什么语义) | 29 棵树长什么样、**每一格代表什么姿势** | **实机测灵敏度** |
| [§3 轴的口](#3-每根轴中间层输出行公式修饰符) | 每根轴由哪条中间层输出行喂、公式与曲线是什么、修饰符现状 | **调稳定性 / 调手感**、调中间层 |
| [§4 下一步](#4-下一步) | 按依赖顺序还剩什么 | 接着干 |

设计**怎么推出来**的（为什么分这些表、门为什么嵌套两层、副本为什么用 1D 树）不再写在这里：结论已经落在控制器资产
与 §2/§3 的表里，推导过程看 git 历史。能表达什么看[契约表](VTS_HIGH_QUALITY_FACE_CONTRACT.md) §E，
名字怎么拼看[命名权威](FACE_TRACKING_NAMING.md)，引擎能/不能算什么看[混合树踩坑](pitfalls/BLEND_TREE_TRAPS.md)。
**最后更新：2026-09-27。**

---

## 1. 当前进度

| # | 东西 | 状态 | 落在哪 / 判据 |
| --- | --- | --- | --- |
| 1 | **控制器资产** | ✅ 已生成、Unity 已导入 | `BREAK_URP/Assets/Hollow/土豆/FT/PTP_CTR_Face_VTS.controller`；`.research/check-controller.ps1` 报 **0 问题**（40 参数 / 29 树 / 1 层 1 状态 / WD 开）；导入日志 32 对象全解析、无报错 |
| 2 | **参数表 40 个** | ✅ | 5 区域门 = `1`、`Ho/Drive/W/One` = `1`、2 个表情门 = `0`、28 根轴 = `0`、4 条切片权重 = `0` |
| 3 | **树形 29 棵** | ✅ | 根 `Ho/00 Drive Tree`（Direct）→ 5 个区域 Direct → 18 张表（13 主版 + 5 副本）+ 5 棵 1D 副本树 |
| 4 | **槽位 104 个** | ⬜ **全是空 Motion** | 100% 未摆姿势 ⇒ **造型一条都还看不到**；但**轴值现在就能测**（§2.4）。**刻度已按实测收过一次圈**：`MouthCore` 的 Y（`Open`）= `0 / 0.4 / 0.75`（2026-09-27，见 §2.2 与 §2.3.1） |
| 5 | **中间层 profile** | ✅ 三份完全一致（SHA256 相同） | 包内 `Editor/FaceTracking/Profiles/ho-iPhoneVTS.hoface.json` = BREAK_URP rig 副本 = Warudo 沙箱副本；**67 输入 / 127 输出**（90 出口 + 37 轴行）；`Mouth/Open` 已带**死区曲线**（§3.1） |
| 6 | **轴的修饰符** | 🟡 **28 根轴已挂 `smooth`**（2026-09-27，照 VB 同族口径；见 §3.2） | 区域门与 4 条切片**故意不挂**；曲线：`Mouth/Open` 带 ±0.03 死区，其余仍是恒等（只是放宽范围防夹断） |
| 7 | **两层门** | ✅ 结构在 | 区域门 = 中间层**常量行**（写 `1`）；`Gate/Expr/*` **一行都没写**（留给按键来源，谁写谁锁死） |
| 8 | **切片权重（Funnel × Press 4 条）** | 🟡 profile 里算了，**树里没接** | 骨架里没有切片表；等条件姿势到位再加同级表（§4） |
| 9 | **1D 树的孩子是子树** | ✅ **已实测**（2026-09-27） | 1D 的孩子是子树时**线性交叉淡入**；**四层嵌套（Direct → Direct → 1D → 2D）照旧**。数字见 §2.3.1 / [能力边界](BLEND_TREE_LIMITS.md) §10 —— 兜底形状不需要了 |
| 10 | **姿势烘焙工具** | ⬜ 没有 | 现有「形态键动画」工具（`HoBlendShapeClipBuilder.cs`）出的是"一键一片段、值恒 100"，**填不了 2D 采样点** |
| 11 | **表情门来源** | ⬜ 未定 | 手机协议 `Hotkey` 实测恒 −1（两台设备）；候选 = VTS API 适配器 / 本地按键（面板覆盖或 Warudo 键盘节点）/ VB 输出成线 |
| 12 | **Warudo 侧** | ⬜ bundle 还是 09-25 那份 | `hoface-controller-test.bundle` 里**不含**这 40 个口 ⇒ Warudo 侧现在拿不到轴；profile 沙箱那份已经是最新 |
| 13 | **调试观察面** | ✅ | 面板「参数输出」每行四个覆盖按钮（不覆盖 / −1 / 0 / 1）+ 「清空覆盖（N）」；「预览混合树」把影子台显示到 Hierarchy |

**改形状 / 核对的入口**（改设计时走这里，**别手改资产**）：

* 生成：菜单 `HoUnityTools/面捕/生成控制器骨架（VTS 原生语义）`（`Editor/FaceTracking/HoFaceControllerSkeletonBuilder.cs`）
  或文本生成器 `.research/make-vts-controller.ps1`。
* 核对：`.research/check-controller.ps1 -Path <那份>.controller` —— 参数名 / 类型 / 默认值 / 层状态与 WD /
  树形与轴 / Direct 子节点有没有挂参数，一次核完。**名字敲错一个字符 = 那条值被静默丢掉**（面板只会说"不在控制器里"），
  这一步别省。它靠文本解析，替代不了 Unity 自己报的错，也不检查槽位内容。
* 包侧门：`.research/compile-check-pkg.ps1`（+ 改到 `.ps1` 时 `ensure-bom.ps1 -Fix`）。

---

## 2. 现在的树：每格叶子是什么语义

### 2.1 结构（29 棵 = 1 根 + 5 区域 + 18 表 + 5 个 1D 开关）

```text
Ho/00 Drive Tree                Direct   子节点权重 = Ho/Drive/Gate/{Mouth, EyeLeft, EyeRight, Brow, Cheek}
├─ MouthRegion                  Direct   子节点权重 = Ho/Drive/W/One
│   ├─ MouthCoreSwitch          Simple1D  blendParameter = Ho/Drive/Gate/Expr/Smile   （0 → 主版，1 → 副本）
│   │   ├─ MouthCore            FreeformCartesian2D  9 格
│   │   └─ MouthCoreExpr        FreeformCartesian2D  9 格
│   ├─ MouthJaw                 FreeformCartesian2D  3 格
│   ├─ MouthWidth               FreeformCartesian2D  9 格
│   └─ MouthTongue              FreeformCartesian2D  4 格
├─ EyeLeftRegion                Direct   → LidLSwitch（LidL / LidLExpr 6+6）+ GazeL（9）
├─ EyeRightRegion               Direct   → LidRSwitch（LidR / LidRExpr 6+6）+ GazeR（9）
├─ BrowRegion                   Direct   → BrowCoreLSwitch（BrowCoreL / Expr 4+4）+ BrowCoreRSwitch（BrowCoreR / Expr 4+4）
└─ CheekRegion                  Direct   → CheekSquint（4）+ CheekPuff（4）+ NoseSneer（4）
```

⚠️ **Direct 的每个子节点都必须挂一个参数**（不挂就不参与混合）—— 所以"这一格永远全量生效"也要一个参数：
**`Ho/Drive/W/One`（默认 1，没人写它）**。区域子树里的表与 1D 开关全部挂它；只有 5 个区域子树挂区域门。

⚠️ **Direct 是加法、不归一化**：兄弟表**写同一个键就会相加**。所以叶子姿势的键分工必须守住 §2.2 的
「分工」列 —— 最容易撞的是 `MouthCore`（唇）与 `MouthJaw`（下颌/下巴）。

### 2.2 表与叶子（一格 = 那个坐标下的一整套姿势）

| 树 | X 轴（刻度） | Y 轴（刻度） | 格 | 一格代表什么 | 分工（该写哪些键） | 槽位名模板 |
| --- | --- | --- | ---: | --- | --- | --- |
| `MouthCore` | `Mouth/Form` −1 撇/垂嘴角 · 0 中性 · +1 笑 | `Mouth/Open` **0 闭 · 0.4 半开 · 0.75 张满**（**按实测定**：手机张满只到 0.75、半张 0.4） | 9 | 外嘴基础：**唇的轮廓与口孔** | 只写唇/口孔，**不写下颌与下巴** | `MouthCore__Form__Open__A3X<i>Y<j>` |
| `MouthCoreExpr` | 同 `MouthCore` | 同 `MouthCore` | 9 | `Gate/Expr/Smile`=1 时的外嘴：同轴、整套换成表情版（夸张的笑/怒嘴形） | 同上（副本是平行姿势，不是叠加） | `MouthCoreExpr__Form__Open__A3X<i>Y<j>` |
| `MouthJaw` | `Mouth/Jaw` 0 · 0.5 · 1 | `Mouth/Forward` 只有 0（Y 只用 `Y0`） | 3 | 下颌/口内：**下颌骨与下巴**随开度 | 只写颌/下巴；不前伸 | `MouthJaw__Jaw__Forward__A3X<i>Y0` |
| `MouthWidth` | `Mouth/Pucker` −1 收 · 0 · +1 展 | `Mouth/X` −1 偏右 · 0 · +1 偏左 | 9 | 对 `MouthCore` 的**嘴宽 / 偏嘴修正**（写残差） | **不重复写完整外嘴** | `MouthWidth__Pucker__LeftRight__A3X<i>Y<j>` |
| `MouthTongue` | `Mouth/TongueL` 0 / 1 | `Mouth/TongueR` 0 / 1 | 4 | 舌（分左右）：舌头两组键 | 舌相关键 | `MouthTongue__TongueL__TongueR__A2X<i>Y<j>` |
| `LidL` | `Lid/Left/BlinkWide` −1 睁大 · 0 中性 · +1 闭 | `Lid/Left/Squint` 0 不眯 · 1 眯满 | 6 | 左眼睑完整姿势（闭/睁大 × 眯） | 左眼睑相关键 | `LidL__BlinkWide__Squint__A3X<i>Y<j>` |
| `LidLExpr` | 同 `LidL` | 同 `LidL` | 6 | 笑眼版（`Gate/Expr/Smile`） | 同上 | `LidLExpr__BlinkWide__Squint__A3X<i>Y<j>` |
| `GazeL` | `Gaze/Left/X` −1 · 0 · +1 | `Gaze/Left/Y` −1 · 0 · +1 | 9 | 左眼球注视：每格 = 眼球朝那个方向 | 只写眼球骨骼，**不写眼睑** | `GazeL__InOut__UpDown__A3X<i>Y<j>` |
| `LidR` / `LidRExpr` | 同 `LidL`（右） | 同 `LidL`（右） | 6 / 6 | 右眼睑（主版 / 笑眼版） | 右眼睑相关键 | `LidR__BlinkWide__Squint__A3X<i>Y<j>` |
| `GazeR` | `Gaze/Right/X` −1 · 0 · +1 | `Gaze/Right/Y` −1 · 0 · +1 | 9 | 右眼球注视 | 同上（右） | `GazeR__InOut__UpDown__A3X<i>Y<j>` |
| `BrowCoreL` | `Brow/Left/Y` −1 压眉 · +1 抬眉 | `Brow/Left/InnerUp` 0 / 1 | 4 | 左眉姿势：压/抬 × 内眉抬起 | 左眉相关键 | `BrowCoreL__Height__InnerUp__A2X<i>Y<j>` |
| `BrowCoreLExpr` | 同 `BrowCoreL` | 同 `BrowCoreL` | 4 | 怒/悲那一版（`Gate/Expr/Angry`） | 同上 | `BrowCoreLExpr__Height__InnerUp__A2X<i>Y<j>` |
| `BrowCoreR` / `BrowCoreRExpr` | 同（右） | 同（右） | 4 / 4 | 右眉（主版 / 表情版） | 右眉相关键 | `BrowCoreR__Height__InnerUp__A2X<i>Y<j>` |
| `CheekSquint` | `Cheek/Left/Squint` 0 / 1 | `Cheek/Right/Squint` 0 / 1 | 4 | 双颊上提（眯眼笑时的颊），分侧 | 颊相关键（**与眼睑表分工**） | `CheekSquint__CheekL__CheekR__A2X<i>Y<j>` |
| `CheekPuff` | `Cheek/Left/Puff` 0 / 1 | `Cheek/Right/Puff` 0 / 1 | 4 | 鼓腮（分左右） | 颊/口腔相关键 | `CheekPuff__PuffL__PuffR__A2X<i>Y<j>` |
| `NoseSneer` | `Nose/Left/Sneer` 0 / 1 | `Nose/Right/Sneer` 0 / 1 | 4 | 鼻翼上提 / 皱鼻，分侧 | 鼻/鼻唇相关键 | `NoseSneer__SneerL__SneerR__A2X<i>Y<j>` |

* 槽位名 = `<树名>__<X段词>__<Y段词>__A<n>X<i>Y<j>`（`n` = X 轴刻度数，`i`/`j` 从 **0** 起）。段词与副本后缀的规则见[命名权威](FACE_TRACKING_NAMING.md) §5。
* **没摆的格不用补洞**：基础表没摆 = 复用同一份低维基础姿势；修正表没摆 = **零残差**。所以"最少起步"先摆中性格加两端就能开跑（契约 §F）。
* ⚠️ **`LidL`/`LidR` 的 Y 只有 0 / 1 两档**（半眯 `Squint=0.5` 没有格，由插值给出）；`MouthJaw` 的 Y 只有 `Y0`（不前伸）。这是"稀疏、非方阵"允许的用法，不是漏了。
* ⚠️ **`GazeL/GazeR` 的 X 方向**：我们原样直通 `EyeLeft_x`/`EyeRight_x`，没有翻符号，所以 **`X0`(−1) 到底是"内"还是"外"要靠实测定**（§2.4 第一项就是它）。

### 2.3 灵敏度现在能测到什么程度

* **能测**：每根轴**实际会走到多少**（手机做动作 / 说话 → 看轴值）。这是"曲线与修饰符该怎么调"的唯一依据。
* **不能测**：造型好不好看 —— 104 格全是空 Motion，插值出来的还是启动姿势。插值行为要等第一张表真填上才看得到。

#### 2.3.1 轴超出槽位范围会怎样（**2026-09-27 实测**）

我们的 13 张表都是**摆满的矩形网格**（3×3 / 3×2 / 2×2 / 3×1）。这种表的实测行为：

| 轴的位置 | Unity 的行为 |
| --- | --- |
| 在网格内部 | ⚠️ **不是分段线性**：网格线与对称中点上看着像线性，**非对称点实测与手算差最多 ~6%**（(0.2,0.3) 得 129.93 而手算 121.3）。Unity 的 2D 混合是**距离权重**，手册不给公式、五种候选公式都拟合不上 |
| 超出网格（两侧都测了） | **投影到圈边、再用那条边的两个端点混合** ⇒ 落点**永远在"最外围孩子围成的圈"上**（x=−3 与 x=−1 同值、x=3 与 x=1 同值）；**把整列/整行/单角往里挪之后，照样落在位移后的圈上** |
| **缺一角**（奇数阵少一个角，我们可能这么稀疏摆） | 圈被切掉一块 ⇒ 圈外那一侧落在**缺口那条新边**的两个邻居中间（(1,1) → **151.5 = 201/102 各半**，不是原来那个角）；中心权重沿对角线 0.55 → **0**，**不会压回原点** |
| **退化摆法**（三点表 / 点压在别的点构成的线上） | **圈内外跳变**：(0,0.5)=166.67 而 (−0.5,0.5)=100 ⇒ **别让一个孩子落在别的孩子构成的边/线上** |

⇒ **一句话**：**输出永远在"最外围孩子围成的圈"里面，永不外推** —— 圈内距离权重、圈外投影到圈边。
所以"轴超了"最坏是**停在圈上**，不会画出畸形造型。但四件事得记着（详见
[能力边界](BLEND_TREE_LIMITS.md) §10 / §10.1 与[踩坑](pitfalls/BLEND_TREE_TRAPS.md) §6）：

1. **分辨率白扔**：`Mouth/Open` 实测只到 0.75，而槽位摆到 1 ⇒ 最后 1/4 的插值空间永远用不到
   （把槽位挪到实测范围可以救，且挪完照样安全落在圈上 —— 见下面的"两条路"）。
2. **树与 Hub 会不一致**：落点只发生在树内部，Hub / 面板读到的还是超出的原值（"Hub 在涨、姿势不动"）。
3. **"0.4 = 半格"不成立**：格内的插值形状由引擎的距离权重决定，**不能手算**（网格线上是精确的）。
   要精确的死区 / 半格 / 量程，只有中间层曲线能给你（那是你自己算的）。
4. **圈的形状决定"停在哪儿"**：摆满矩形 ⇒ 出界停在矩形边界那一格；缺角 ⇒ 停在缺口那条新边上。两者都安全，姿势不同。

**⚠️ Inspector 里两个别碰的地方**（详见[能力边界](BLEND_TREE_LIMITS.md) §11）：

* **`Compute Positions` 按钮绝对不要点**：它按每个孩子的**根运动速度**算 Pos，而我们的姿势片段没有位移
  ⇒ 一点下去**所有孩子都会被算到 (0,0)**，整棵树塌掉。
* **`Normalize Blend Values` 复选框别勾**：1D/2D 上它是空操作，但**在 Direct 上它真的会归一化** ——
  我们的区域权重和是 4（4 个孩子 × `W/One`），勾上就被除成 0.25，整个区域摊薄。

**所以"量程/放大"有两条路**（都能安全落在圈上，选哪条看你要不要 Hub 里也是那个数）：

| 路 | 怎么做 | 代价 |
| --- | --- | --- |
| **A · 挪槽位** ✅ **2026-09-27 已落地** | `Mouth/Open` 的 Y 刻度从 `0 / .5 / 1` 改成 **`0 / .4 / .75`**（改的是两个生成器的刻度表 + 重新生成资产；`.research/check-controller.ps1` 现在会**核对刻度**） | Hub / 面板 / Warudo 读到的**仍是原始值**（0.75 = 满在那棵树里，别的消费者不知道）；且插值形状本就不可手算，挪完"半格"不在正中 |
| **B · 中间层曲线** 🟡 只用来做死区 | 曲线的横轴是表达式的值、纵轴是写出去的值（`HoFaceCurve.Transfer` 范围外按端点算） | 轴上写出去的就是曲线给出的数 ⇒ **Hub / Warudo 一起受益**；但曲线在 profile 里（三份副本要一起更新）。**量程没有走 B**：`0.4 → 0.5`、`0.75 → 1` 那次映射改成了挪刻度，所以轴值仍是原始口径 |

**实际采用的是 A + "曲线只管死区"**（2026-09-27）：`Mouth/Open` 的响应曲线现在是
`|v| ≤ 0.03 → 0`、`0.03…0.06` 斜坡（斜率 2）、之外恒等 —— 死区只有曲线能做精确（树里两行同姿势的平台会被远处的孩子按距离掺进来）。

**挪到 `0/.4/.75` 之后，0.9 / 1.0 会读到什么（已实测）**：

| 轴值 | 0.70 | 0.75 | 0.8 / 0.9 / 1.0 / 1.2 |
| --- | --- | --- | --- |
| 树里得到的姿势（以 `Open` 为 Y、`Form`=0 那列为例，槽值代号 `100i+j`） | 1.857（还在插值） | **2.0 = 顶行** | **与 0.75 逐位相同**（全是 2.0） |

* **0.9 与 1.0 就是"张满"那一格**，不多不少 —— 这正是"超出安全钳回"，也是"放大"：原来 0.75 只走到 3/4 的姿势高度，现在直接满。
* **钳制是逐轴的**：`Open` 饱和后 `Form` 照常插值（顶行上 x=−1/0/0.4/0.5/0.75/0.9/1/1.5 → 2/102/142/152/177/192/202/202）；
  以后把 `Form` 的刻度也往里挪，两轴同时出界会**钳到位移后的那个角**。
* **代价**：`0.75…1.0` 那 25% 的轴范围**没有任何额外表现**。若以后想要"张到极限再挺一点"，那是**加一行**（不是挪点）。
* 顺带一条好消息：**正好落在网格线上的插值是精确的分段线性**（顶行 x=0.4 → 142 = 100×1.4+2），
  只有**两行之间**才会偏（y=0.70、x=0.5 → 151.78 vs 手算 151.86）。

> **死区那一件事只有曲线能做精确**：树里"两行同姿势"的平台也会被远处的孩子按距离权重掺进来，不是平的。

### 2.4 怎么测（一次把 28 根轴测完）

1. 面板「对象」段：控制器 = `PTP_CTR_Face_VTS`、配置文件 = `ho-iPhoneVTS.hoface.json`、调试对象 = 角色 → 连接 → 开始驱动（或「自动驱动」默认开，按 Play 即跑）。
2. 打开「预览混合树」（默认开）→ Hierarchy 里选中 `Ho Face Shadow` → **Animator 窗口的 Parameters**：这 40 个口就是控制器真实看到的值，逐根看范围最直接。
   （另一条路：面板「参数输出」栏 —— 那里是中间层写出去的值，同一份；每行右边的覆盖按钮可以**把某根轴钉成 −1 / 0 / 1** 手动试。）
3. 做动作并记**实际到达的极值**：例如"说一句话，`Mouth/Open` 只到 0.35"、"闭嘴时 `Lid/Left/BlinkWide` 到 0.8 不到 1"、"`Cheek/Left/Squint` 几乎不动"。
4. 把结果填回 **§3 每张表的「实测范围」列**（现在都是"待测"）—— 那一列 + §3.2 的修饰符说明就是"怎么把它调稳"的输入。
5. 顺手验两条方向性：`GazeL/R` 的 ±1 哪边是内（§2.2 末），`Mouth/X` 的 +1 是不是"偏左"（表达式是 `mouthLeft − mouthRight` + 笑差）。

---

## 3. 每根轴：中间层输出行、公式、修饰符

**轴参数名 = profile 输出行的 `parameter` 名**（同名，都在 `Ho/Drive/…` 下）。中间层每帧按名字写进控制器；
控制器里没有的名字会被静默跳过，所以**面板「参数输出」栏里这 37 行一个都不该显示"不在控制器里"** —— 那是最快的接线自检。

### 3.1 逐根轴的口（37 行里的 28 根轴 + 5 个区域门 + 4 条切片）

> 「实测范围」留空 = 等 §2.4 的实机结果回填；调曲线/修饰符前先有这一列。

**嘴（10）**

| 轴参数 | profile 表达式（原文） | 曲线 | 修饰符 | 语义 / 值域 | 实测范围 |
| --- | --- | --- | --- | --- | --- |
| `Ho/Drive/Mouth/Form` | `((2 - (mouthFrownLeft + mouthFrownRight + mouthPucker) + (mouthSmileRight + mouthSmileLeft + ((mouthDimpleLeft + mouthDimpleRight) / 2))) / 2) - 1` | 恒等 −1…2 | smooth 0.009 s | = 2×`MouthSmile` − 1，展开是 **`[(smileL+smileR) + (dimpleL+dimpleR)/2 − (frownL+frownR) − pucker] / 2`** ⇒ 自然范围 **±1.5**。**0 = 静息、+1 = 笑满；负侧由"嘴角下弯 + 噘嘴"驱动（没有眉/眼）** ⇒ 它是"嘴部情绪轴"，**不是纯 sad**（见 §3.4） | +1 笑满、0 静息、**−0.5…−0.7（噘嘴 / 噘嘴+苦脸）** |
| `Ho/Drive/Mouth/Open` | `(jawOpen - mouthClose) - ((mouthRollUpper + mouthRollLower) * .2) + (mouthFunnel * .2)` | **±0.03 死区 + 0.03..0.06 斜坡，之外恒等**（`MouthCore` 的 Y 刻度已缩到 0/.4/.75，量程归一在树里） | smooth 0.009 s | 0 闭 … 1 张满；**负侧也有值**（抿嘴比闭还"负"）。**实测：张满 0.75、半张 0.4、抿满 −0.16…−0.2、\|x\|<0.03 是"不张也不抿"的死区** | 0 … **0.75**（半张 0.4；抿嘴 −0.2…−0.16） |
| `Ho/Drive/Mouth/Funnel` | `mouthFunnel - (jawOpen * .2)` | 恒等 −1…2 | smooth 0.007 s | 0 普通 … 1 漏斗形；张嘴时被扣一点 | 待测 |
| `Ho/Drive/Mouth/Press` | `((mouthUpperUpRight + mouthUpperUpLeft + mouthLowerDownRight + mouthLowerDownLeft) / 1.8) - (mouthRollLower + mouthRollUpper)` | 恒等 −2…2 | smooth 0.007 s | **双向**：−1 卷/压唇 … +1 展唇露齿 | 待测 |
| `Ho/Drive/Mouth/Jaw` | `jawOpen` | 恒等 0…1 | smooth 0.009 s | 0 闭 … 1 张满；`MouthJaw` 表的主轴 | 待测 |
| `Ho/Drive/Mouth/Forward` | `jawForward` | 恒等 0…1 | smooth 0.01 s | 下颌前伸；**现在树里只用了 0** | 待测 |
| `Ho/Drive/Mouth/Pucker` | `((mouthDimpleRight + mouthDimpleLeft) * 2) - mouthPucker` | 恒等 −2…2 | smooth 0.007 s | **双向**：−1 噘嘴 … +1 展宽 | 待测 |
| `Ho/Drive/Mouth/X` | `(mouthLeft - mouthRight) + (mouthSmileLeft - mouthSmileRight)` | 恒等 −2…2 | smooth 0.007 s | **双向**：+1 偏左（按表达式）… −1 偏右 | 待测 |
| `Ho/Drive/Mouth/TongueL` | `tongueOut` | 恒等 0…1 | smooth 0.007 s | 舌左；**现在两侧同跟单侧原值** | 待测 |
| `Ho/Drive/Mouth/TongueR` | `tongueOut` | 恒等 0…1 | smooth 0.007 s | 舌右；同上 | 待测 |

**眼睑 / 注视（8）**

| 轴参数 | profile 表达式 | 曲线 | 修饰符 | 语义 / 值域 | 实测范围 |
| --- | --- | --- | --- | --- | --- |
| `Ho/Drive/Lid/Left/BlinkWide` | `eyeBlinkLeft - eyeWideLeft` | 恒等 −1…1 | smooth 0.007 s | −1 睁大 · **0 中性** · +1 闭 | 待测 |
| `Ho/Drive/Lid/Left/Squint` | `eyeSquintLeft` | 恒等 0…1 | smooth 0.007 s | 0 不眯 … 1 眯满 | 待测 |
| `Ho/Drive/Lid/Right/BlinkWide` | `eyeBlinkRight - eyeWideRight` | 恒等 −1…1 | smooth 0.007 s | 同上（右） | 待测 |
| `Ho/Drive/Lid/Right/Squint` | `eyeSquintRight` | 恒等 0…1 | smooth 0.007 s | 同上（右） | 待测 |
| `Ho/Drive/Gaze/Left/X` | `EyeLeft_x` | 恒等 −1…1 | smooth 0.007 s | 左眼水平；手机自己发的标量，**不重算** | 待测 |
| `Ho/Drive/Gaze/Left/Y` | `EyeLeft_y` | 恒等 −1…1 | smooth 0.007 s | 左眼垂直 | 待测 |
| `Ho/Drive/Gaze/Right/X` | `EyeRight_x` | 恒等 −1…1 | smooth 0.007 s | 右眼水平 | 待测 |
| `Ho/Drive/Gaze/Right/Y` | `EyeRight_y` | 恒等 −1…1 | smooth 0.007 s | 右眼垂直 | 待测 |

**眉（4）**

| 轴参数 | profile 表达式 | 曲线 | 修饰符 | 语义 / 值域 | 实测范围 |
| --- | --- | --- | --- | --- | --- |
| `Ho/Drive/Brow/Left/Y` | `2 * ((browOuterUpLeft - browDownLeft) + ((mouthRight - mouthLeft) / 8))` | 恒等 −2…2 | smooth 0.018 s | **0 = 静息**，−1 压眉 · +1 抬眉；VB 那条掺了偏嘴联动（说话时眉会动一点） | 待测 |
| `Ho/Drive/Brow/Left/InnerUp` | `browInnerUp` | 恒等 0…1 | smooth 0.015 s | 内眉抬起 | 待测 |
| `Ho/Drive/Brow/Right/Y` | `2 * ((browOuterUpRight - browDownRight) + ((mouthLeft - mouthRight) / 8))` | 恒等 −2…2 | smooth 0.018 s | 同上（右） | 待测 |
| `Ho/Drive/Brow/Right/InnerUp` | `browInnerUp` | 恒等 0…1 | smooth 0.015 s | 同上（两侧同跟一根） | 待测 |

**颊 / 鼻（6）**

| 轴参数 | profile 表达式 | 曲线 | 修饰符 | 语义 / 值域 | 实测范围 |
| --- | --- | --- | --- | --- | --- |
| `Ho/Drive/Cheek/Left/Squint` | `cheekSquintLeft` | 恒等 0…1 | smooth 0.009 s | 左颊上提 | 待测 |
| `Ho/Drive/Cheek/Right/Squint` | `cheekSquintRight` | 恒等 0…1 | smooth 0.009 s | 右颊上提 | 待测 |
| `Ho/Drive/Cheek/Left/Puff` | `cheekPuff` | 恒等 0…1 | smooth 0.01 s | 左鼓腮；**分侧是预留的**，现在两侧同跟 | 待测 |
| `Ho/Drive/Cheek/Right/Puff` | `cheekPuff` | 恒等 0…1 | smooth 0.01 s | 右鼓腮；同上 | 待测 |
| `Ho/Drive/Nose/Left/Sneer` | `noseSneerLeft` | 恒等 0…1 | smooth 0.009 s | 左鼻翼上提 | 待测 |
| `Ho/Drive/Nose/Right/Sneer` | `noseSneerRight` | 恒等 0…1 | smooth 0.009 s | 右鼻翼上提 | 待测 |

**区域门（5）与切片权重（4）** —— 这两组不是"手感轴"，调它们是改结构：

| 参数 | 表达式 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `Ho/Drive/Gate/{Mouth, EyeLeft, EyeRight, Brow, Cheek}` | **空**（常量行） | `1` | 区域门：调小 = 让这块回到启动姿势。**常量行不过曲线**（`HoFaceAnimationSession.cs:316`：走 `defaultValue`，不调 `Transform`），写 1 就是 1；但**修饰符照走**（想让门慢慢推上去可以给它加 Smooth） |
| `Ho/Drive/Slice/MouthCore/{Funnel0Press0, Funnel1Press0, Funnel0Press1, Funnel1Press1}` | `(1−f)(1−p)` / `f(1−p)` / `(1−f)p` / `fp`（f、p 都是**内联**的 `Funnel` / `Press` 公式） | `0` | Funnel × Press 双线性切片，**四条和恒为 1**。树里**还没接**（§4） |

`Ho/Drive/Gate/Expr/*`（`Smile` / `Angry`）**故意一行都不写**：它属于驱动"按键"的那一方。

### 3.2 稳定化：修饰符（**2026-09-27 第一版已铺**）

**现状**：28 根轴全部挂了 `smooth`（区域门与切片行**故意不挂** —— 见下），取值**照 VB 自己那套逐行口径**平移到轴上：

| 轴 | `smooth` | 依据（VB 出口行里的同族时间常数） |
| --- | --- | --- |
| `Mouth/Open` | 0.009 | = VB `MouthOpen` |
| `Mouth/Form` | 0.009 | 嘴部复合（`MouthSmile` 0.007 / `MouthOpen` 0.009），取该部位上界 |
| `Mouth/Jaw` | 0.009 | 与 `MouthOpen` 同一物理运动 |
| `Mouth/Funnel` · `Mouth/Press` · `Mouth/Pucker` · `Mouth/X` | 0.007 | = VB `MouthFunnel` / `MouthPressLipOpen` / `MouthPucker` / `MouthX` |
| `Mouth/TongueL` · `TongueR` | 0.007 | 舌是小而快的动作，取最小档 |
| `Mouth/Forward` | 0.010 | 没有对应行；下颌平移，比 `Jaw` 慢一档 |
| `Lid/*/BlinkWide` · `Lid/*/Squint` | 0.007 | = VB `EyeOpenLeft/Right` —— **再大就会糊掉眨眼** |
| `Gaze/*/X` · `Gaze/*/Y` | 0.007 | = VB `EyeLeftX/Y`、`EyeRightX/Y` —— **再大眼球会拖** |
| `Brow/*/Y` | 0.018 | = VB `BrowLeftY` / `BrowRightY` |
| `Brow/*/InnerUp` | 0.015 | = VB `BrowInnerUp` |
| `Cheek/*/Squint` · `Nose/*/Sneer` | 0.009 | 肌肉类，与 VB `Brows` 同档 |
| `Cheek/*/Puff` | 0.010 | 鼓腮是慢动作 |

**为什么门与切片不挂**：

* **区域门**是常量行 —— 挂 `smooth` 只会在开场把 1 从 0 爬上来（没意义）。真要"慢慢推上去"再加。
* **4 条切片权重**还没接进树（§4）。接的时候**四条必须用同一个时间常数**（线性滤波下"和恒为 1"才守得住）。

**调的时候的参考量级**（90 行出口里那 23 条，VB 自己的做法：**每路一个手感**）：

| 量级 | 谁在用 |
| --- | --- |
| 0.007 s | 眼（`EyeLeftX/Y`、`EyeRightX/Y`、`EyeOpenLeft/Right`）、`MouthSmile`、`MouthX`、`MouthOpen`、`MouthFunnel`、`MouthPucker`、`MouthShrug`、`MouthPressLipOpen` |
| 0.009 – 0.011 s | `Brows` 0.009、`FacePositionZ` 0.011 |
| 0.010 / 0.015 / 0.018 s | `FacePositionX/Y` 0.010、`BrowInnerUp` 0.015、`BrowLeftY`/`BrowRightY` 0.018 |
| 0.042 s | `FaceAngleX/Y/Z`（头姿最重，抖动最明显） |

**写法**（每一行一个修饰符列表，按顺序叠加）：

```json
"modifiers": [ { "kind": "smooth", "seconds": 0.009, "steps": [] } ]
```

* `kind`：`smooth`（时间常数，`seconds`）/ `steps`（维持：`steps[]` = `{trigger, target, hold, threshold}`，按 trigger 升序）/ `delay`（枚举上写着"未实现"，但**两侧不一致**：Unity 侧真的按每行 FIFO 延迟了、mod 侧会跳过它 —— 见[分工现状核对](FACE_PIPELINE_STATUS_2026_09_26.md) §3.2。要两端一致就先别用 `delay`）。
* 编辑入口：「配置文件」窗口（`HoUnityTools/面捕/配置文件`）选中那一行，右边就是表达式 / 曲线 / 修饰符。
  调试面板的「参数输出」栏是**只读**的观察面（但它会把曲线点数与修饰符链摊出来，用来核对）。
* ⚠️ **出口行带平滑 ≠ 轴被平滑**：两批行各自独立求值（轴行不引用出口行），那 23 条 `smooth` 只作用在出口上 ——
  所以 §3.2 顶上那份取值是**在轴上又挂了一遍**（照同族数值抄，不是共享同一个滤波器）。
* ⚠️ **短促事件别用大平滑**：眨眼（`Lid/*/BlinkWide`）、眼球扫视（`Gaze/*`）如果嫌抖，先想"是不是该用 `steps` 维持"，
  加大 `smooth` 会把它们糊成慢动作。

**调的时候必须知道的四条**：

1. **曲线是夹的、不外推**（`HoFaceCurve.Transfer`，`HoFaceMiddleware.cs:15`：范围之外按端点算）：想留住负半边或超过 1 的轴，必须用宽曲线 ——
   现在 `Form`/`Open`/`Funnel`/`Pucker`/`X`/`Press`/`Brow*Y` 都是 `±2` 就是为了这个；改成默认 `0…1` 会把负半边**压平**，
   表情会突然变钝。`Lid/*/BlinkWide` 是 `−1…1`（天然含负侧）。
2. **区域门是常量行，不过曲线**：调它的曲线没有效果，要改就改 `defaultValue`（修饰符仍然生效）。
3. ⚠️ **改 `Funnel` / `Press` 的公式或修饰符时，4 条切片行的内联副本要跟着改**：输出行之间**不能互相引用**
   （求值器只查输入行），所以切片行是把两条公式**抄了一遍**内联算的。只改轴不改切片 = 切片权重与轴不同步 ——
   这类漂移在动画里表现为"条件姿势的权重和轴对不上"，很难查。**要么两处一起改，要么先让切片行引用轴（需要改求值链）**。
4. `Gate/Expr/*` 中间层不写 = 谁都能写它（VTS API / 键盘 / 别的脚本），我们不锁死它。

### 3.3 想改手感时，改的是哪一层（三层分工）

一个"手感不对"的需求，先判断它属于哪一类 —— 三类**只能**在各自的层解决：

| 你想改的 | 改哪 | 为什么不能改另一层 |
| --- | --- | --- |
| **"轴实际只到 0.75，可槽位摆到 1"、"±0.03 要当 0"、"抿嘴只有 −0.2 太弱"** —— 也就是**轴的口径**（同一个脸状态应该给出什么数） | **中间层 · 响应曲线**（必要时表达式）。曲线的横轴是表达式的值、纵轴是写出去的值，范围外按端点算 ⇒ 天然能"压死区 + 归一量程 + 放大负侧" | **树改不了**：树的槽位坐标只是**常量标签**，它不产生值。把槽位从 0/0.5/1 挪到 0/0.4/0.75 只是"换个地方贴标签"，Hub / 面板 / Warudo 读到的仍是 0.75=满 —— **每个消费者都得自己再归一一次**（轴是公共契约） |
| **"−1 那一格应该是噘嘴还是垂嘴角"、"眯眼那张要换姿势"** —— 也就是**某个轴值该长什么样** | **树 · 槽位里的姿势内容**（片段），坐标不用动 | 中间层管不到这里：它只知道轴的值，不知道模型上有哪些键、怎么摆 |
| **"这个姿势该出现在刻度 0.4 还是 0.5"** —— 也就是**采样点位置** | **树 · 槽位坐标**（唯一该动坐标的时候） | 这是艺术决定；但**不要**用它来代替量程归一（上一行） |
| **"抖"、"跳"、"跟手慢"** | **中间层 · 修饰符**（`smooth` / `steps`），偶尔是曲线斜率 | **树没有时间概念**（静态插值机）：抖动必须在写参数那一层治。短促事件（抿嘴那种）用 `steps` 维持比加大平滑更合适 —— 大平滑会把动作糊掉 |

⚠️ 曲线做"平台段"（死区）时**切线要给 0**：`inT`/`outT` 是 Unity 关键帧的 in/out tangent，
两个等值关键点若沿用斜率 1 的切线，Unity 会按平滑切线插值、在平台两端**过冲**（实测过一次：
同样的键序，切线配错时 `v(−0.01)` 会跑出 **+0.0089** 而不是 0）。正确配法见 §3.1 里 `Mouth/Open` 的键。

### 3.4 `Mouth/Form` 到底是"悲伤"吗（**2026-09-27 待拍**）

**它现在是什么**：`2 × VB MouthSmile − 1`，展开成

```text
Form = [ (mouthSmileLeft + mouthSmileRight) + (mouthDimpleLeft + mouthDimpleRight)/2
         − (mouthFrownLeft + mouthFrownRight) − mouthPucker ] / 2        理论范围 ±1.5
```

⇒ 语义上是**"嘴部情绪轴（嘴角上翘 ↔ 下垂）"**，但**不等于"悲伤"**：

| 事实 | 含义 |
| --- | --- |
| 负侧有**两类**来源 | ① `mouthFrown*` 嘴角下弯（真·苦/不悦）② `mouthPucker` 噘嘴（**非情绪**，是嘴形） |
| 公式里**没有眉、没有眼** | "悲伤"那套还包括内眉抬起（`browInnerUp`）、压眉（`browDown`）、上睑下垂 —— 那三样在别的轴上（`Brow/*/InnerUp`、`Brow/*/Y`、`Lid/*`），**不在这根轴里** |
| 实测：**噘嘴 = −0.5**、**噘嘴+苦脸 = −0.7**、纯苦脸（公式上限）= −1.0 | 负侧**七成来自噘嘴**。你那次 −0.7 是"做苦脸时嘴角被带动下弯"（反推 frown ≈ 0.2×2），**不是公式读了眉** |
| 正侧实测到 **+1**（笑满），负侧只到 **−0.7** | 与 `Open`（0…0.75）同类：**实测范围比理论窄、而且左右不对称** |

**为什么非得决定**：`MouthCore` 的 X 轴就是它 —— **X0 那三格摆什么姿势，取决于"−1 代表什么"**：

| 选项 | X0 摆什么 | 后果 |
| --- | --- | --- |
| **A · 认下 VB 语义** | 一格里同时照顾"苦嘴 + 噘嘴"的中间姿势（幅度得小） | 公式与出口行都不动；但**噘嘴永远带一点苦相**（两件事共用一个刻度），也做不出明显的苦相 |
| **B · 把 pucker 拆出去** | X0 = 纯"嘴角下垂 / 不笑"，可以摆明显的苦相 | 要**改表达式**（去掉 `− pucker`）；噘嘴交给 `MouthWidth`（它的 X 轴 `Mouth/Pucker` 负侧本来就是噘嘴）。⚠️ 出口行 `MouthSmile` 是另一行、下游 VTS 生态按它读 —— **轴与出口在这里有意分叉**（要写进 notes）。⚠️ 拆掉之后负侧只剩 frown，实测大概只到 **−0.2…−0.5** ⇒ X 刻度多半也要跟着收（像 `Open` 那样改成 `−0.5 / 0 / +1`） |
| **C · 再加一张表** | `MouthCore` 的 X0 保持中性 | 用契约里已经预留的 `MouthCorner`（左右嘴角 笑/苦 修正表）专做嘴角 —— 语义最干净，但结构变化最大（多一张表 + 4~9 个槽位） |

**倾向 B**（一根轴一件事），`MouthCorner` 留到真需要"左右不对称嘴角"时再上。**等你拍。**

---

## 4. 下一步

按依赖顺序（✅ = 已完成；后面标了谁做）：

1. 🟡 **实机测 28 根轴的灵敏度**（§2.4）—— *进行中*。已回填：`Mouth/Open`、`Mouth/Form`（§3.1）；其余 26 根仍是「待测」。
2. 🟡 **按实测结果给 37 行加修饰符 / 修曲线**（§3.2 / §3.3）—— *你定数值*。已完成 / 待定：
   · ✅ `Mouth/Open`：**死区曲线已加**（`|v| ≤ 0.03 → 0`、`0.03…0.06` 斜坡、之外恒等）+ **树里刻度收成 `0/.4/.75`**（两个生成器都改了；`check-controller.ps1` 现在核对刻度）。
   · ✅ **28 根轴的 `smooth` 已铺第一版**（照 VB 同族口径：眼/注视/眨眼 0.007、嘴 0.007–0.010、眉 0.015/0.018、颊鼻 0.009/0.010）—— **等你实机试手感**，尤其看三处：眨眼会不会糊（0.007 已经是最小档）、眼球跟不跟手、`Cheek/*` 够不够快。
   · ⬜ `Mouth/Form` 负侧：**语义还没定**（它现在 = 2×VB `MouthSmile` − 1，负侧同时被"下弯嘴角"和"噘嘴"驱动）—— 见 §3.1。
3. **姿势烘焙工具**（*我做*）：把调试面板里调好的滑条姿势（会话 `SetPreview` 那套）存成**以槽位命名的多键片段**，
   文件名 = §2.2 的槽位名。现有 `HoBlendShapeClipBuilder` 只能一键一片段，填不了采样点。**排在动画前面。**
4. ✅ **1D 树 + 子树嵌套已实测**（2026-09-27，见 §2.3.1 与[能力边界](BLEND_TREE_LIMITS.md) §10）：
   1D 的两个孩子是子树时**线性交叉淡入**（g=0/.25/.5/.75/1 → 0/25/50/75/100），
   **四层嵌套（Direct → Direct → 1D → 2D）照旧求值**。兜底形状（Direct 兄弟 + 中间层补权重）**不需要**了。
   （同一轮还跑通了 **Unity 侧生成器**：`HoFaceControllerSkeletonBuilder.Build()` 在 batchmode 里建出来的是
   40 参数 / 29 树，`MouthCore` 的 9 格坐标 = `0/.4/.75`，与文本生成器**逐位一致**。）
5. **切片接线**（*等条件姿势*）：`MouthCoreFunnel` / `MouthCorePress` / `FunnelPress` 三张同级表 + `MouthCore` 自己挂
   `Slice/MouthCore/Funnel0Press0`。**没做的切片必须复用基础片段**，否则权重和不足 1 会掺进启动姿势。
   摆切片时**别让点落在别的点构成的边/线上**（那会圈内外跳变，见 §2.3.1）。
6. **表情门来源选一个**（§1 第 11 项）：① VTS 公开 API（`ExpressionActivation` / `HotkeyTrigger`）适配器；
   ② 本地按键（面板覆盖 / Warudo 官方键盘节点写同一个参数）；③ VB 若把表情输出成线。
7. **Warudo 侧补上**（*我*）：按新控制器重建 `hoface-controller-test.bundle`（现在那份 09-25 的不含这 40 个口），
   profile 沙箱副本已经是最新，不用再拷。
8. **接线自检**（随时可跑）：面板「参数输出」栏里这 37 行**一个都不该显示"不在控制器里"**；槽位"缺哪些动画"能在
   「详情」栏列出来；`.research/check-controller.ps1` 退出码 0（现在还会核**刻度**）。
