# HO 参数规范（现状）

> **2026-09-29 重组**：本文由三份文档合并而成，**只保留现在在用的口径** ——
> [`PARAMETER_HO.md`](PARAMETER_HO.md)（我们的参数规范）、
> [`PARAMETER_DEVICE_VERIFICATION.md`](PARAMETER_DEVICE_VERIFICATION.md)（设备实测表）、
> [`PARAMETER_STANDARDS.md`](PARAMETER_STANDARDS.md)（外部标准底表）。
> 三份原文**保留作历史参考**（各自开头已加横幅）。
> **被推翻的结论与研究过程没有带过来**；原文里自相矛盾、无法判定的一律照抄并标「原文如此」，集中在 §10。

**一句话**：输入只有 **VTS 手机协议**一条；52 个 ARKit 形态键各有三个名字（设备线名 / 规范名 / 出口名）；
出口是 **90 行**（G1–G3c）+ 喂控制器的 **40 行 `Ho/Drive/*`**；还有 **9 行**在当前输入契约下算不出来。

---

## 0. 命名权威与三层名字

### 0.1 权威在哪（只有两处）

| 权威 | 管什么 | 位置 |
|---|---|---|
| **通道表** | **52 个规范名的唯一名单与顺序** | `Runtime/FaceTracking/HoFaceTrackingChannels.cs` 的 `Names`（:46） |
| **词表对应关系** | 规范名 ↔ VTS/Apple 线名 ↔ VB/安卓 `_L/_R` 线名 | `docs/VTS_HIGH_QUALITY_FACE_CATALOG.json` 的 `raw_arkit[]`（每项 = `name` / `vts_wire` / `vb_internal` / `range` / `processed_outputs`） |

⚠️ **路径订正**：旧文与口头引用写的是 `Editor/FaceTracking/HoFaceTrackingChannels.cs`，
**实际文件在 `Runtime/FaceTracking/`**（`PARAMETER_HO.md` §1.5、`PARAMETER_STANDARDS.md` §3.1 引的也是这个路径）。
`Editor/FaceTracking/` 下有的是面板 / 会话 / `Profiles/*.hoface.json`。

⚠️ 目录 JSON 里另外三段**不是通道名权威**，别混：
`vbridger_v3[]` = VBridger V3.0 的参考出口行（含 `equation`/`range`/`curve`）、
`hq_extensions[]` = 控制器的 `HQ*` 契约名、`tree_families[]` = 树的分工表。
`native[]` 是 VTS 官方文档化的 **98 个追踪参数**（面/鼠标/音频 33 + 手 26 + 手柄 39），见 §7。

### 0.2 三层名字，一条换算规则

| 层 | 长什么样 | 例子 | 谁定义 |
|---|---|---|---|
| **设备线名**（线上真收到的键） | PascalCase（苹果）/ camelCase + `_L/_R`（安卓） | `JawOpen` / `jawOpen` / `eyeBlink_L` | VTS 手机协议（= iOS ARKit 名与它的安卓方言） |
| **规范名**（我们中间层内部） | camelCase | `jawOpen`、`eyeBlinkLeft` | `HoFaceTrackingChannels.Names`（52 条） |
| **出口名**（喂控制器 / 下游） | 我们选的那套 | `MouthOpen`、`FaceAngleX`、`jawOpen` | §5（本文） |
| *（参考）VB 内部名* | camelCase + `_L/_R` | `jawOpen`、`eyeBlink_L` | VBridger 的 `SceneData.shapekeys`；**与安卓线名同一套** |

**线名 → 规范名的规则只有一条：首字母小写。** 52 个形状全部适用
（`JawOpen` → `jawOpen`、`MouthSmileLeft` → `mouthSmileLeft`）。
**VB 内部名 → 规范名也只有一条：`_L/_R` → `Left/Right`，其余原样**（`eyeBlink_L` → `eyeBlinkLeft`）——
这正是 VB 的 `vtsKeys` 表与它的内部 `shapekeys` 表之间的关系，§5 的公式全按这条读。

**输入行的写法（写反的后果是静默且全面的）**：

```
parameter  = 规范名（jawOpen，camelCase）      ← 通道靠这个认
expression = 设备线名（JawOpen / jawOpen）      ← 包里真收到的那个键
```

`HoFaceAnimationSession` 拿**输入行的 `parameter`** 去 `HoFaceTrackingChannels.IndexOf(...)` 解析通道。
一份 `parameter = expression = JawOpen` 的配置**一个通道都解析不到**，中间层什么都不产出 —— 不报错、不警告。

**只有 15 条非通道**（`Rotation_*` / `Position_*` / `EyeLeft_*` / `EyeRight_*` / `FaceFound` / `Hotkey` / `Timestamp`）
可以 `parameter == expression` —— 它们不是通道，没有任何东西拿它们去 `IndexOf`。

### 0.3 索引表：52 个规范名 + 36 条别名 = **88 条**

`HoFaceTrackingChannels.BuildIndices()` 除规范名外，还给**以 `Left`/`Right` 结尾**的规范名
额外注册 `_L`/`_R` 别名；比较是 `StringComparer.Ordinal`（**逐字**，`JawOpen` ≠ `jawOpen`）。

* **不注册别名的 4 个**：`jawLeft` / `jawRight` / `mouthLeft` / `mouthRight`（它们是**不带后缀**的线名）。
* ⇒ `52 + 36 = 88` 条。
* ⇒ **`browInnerUp` 没有别名**（不以 `Left`/`Right` 结尾）—— 安卓发来的 `browInnerUp_L` / `_R`
  **解析不到 `browInnerUp` 这个通道**，只能当原始线名用（按 `BuildIndices()` 逐字推得）。

实测（走真的 `IndexOf`）：

| 配置 | 输入行 | 解析到的通道 | 为什么 |
|---|---|---|---|
| `ho-iPhoneVTS` | 67 | **52 / 52** ✅ | 52 条形状行的 `parameter` 就是规范名 |
| `ho-debug-iphoneVTS` | 67 | **0 / 52** | 线名是 PascalCase，规范名与别名都是 camelCase |
| `ho-debug-androidVTS` | 65 | **40 / 52** | 小驼峰 + `_L/_R` 命中别名表；剩 12 个它不发 |

⇒ 那两份 `ho-debug-*` 的定位是**实测记录**，不是"能跑的配置"（见 `Editor/FaceTracking/Profiles/README.md`）。
⚠️ **核这类事别用 PowerShell**：它的 `-contains`/`-eq`/`-match` 默认忽略大小写，会把第二行假报成 52/52；
要核就照 C# 的 `Ordinal` + 真 `IndexOf`。

---

## 1. 输入契约：设备能发什么

只有**一条**输入：VTS 手机的 `iOSTrackingDataRequest` 协议
（载荷 = `Timestamp` / `Hotkey` / `FaceFound` / `BlendShapes[]` / `Rotation` / `Position` / `EyeLeft` / `EyeRight`）。

| 设备（`wire` 名） | 实测线数 | 组成 |
|---|---|---|
| **iPhone VTS** | **67** | 52 个 ARKit 形态键 + 12 个头/眼标量 + `FaceFound` / `Hotkey` / `Timestamp` |
| **安卓 VTS** | **65** | 能用 **60** + 恒定 **2**（`FaceFound` = 1、`Hotkey` = −1）+ 不给值 **3**（`EyeLeft_z`、`EyeRight_z`、`mouthShrugUpper`） |

* **形态键线名两套拼写**：苹果 **纯 PascalCase**（`BrowDownLeft`）；安卓 **camelCase + `_L/_R`**（`browDown_L`）。
* **量纲**：形态键是 iOS 原始 `0..1`（接收端**不换算**，`HoVtsPacket.cs:160`）；
  `Rotation_*` / `EyeLeft_*` / `EyeRight_*` 是**度**级小数；`Position_*` **单位未标定**。
* **判据只看 `range = max − min`**（一条 97% 时间为 0、但到过 0.028 的线是带信号的，用均值判会误杀）：

  | status | 判据 | 含义 |
  |---|---|---|
  | `MOVES` | range ≥ 0.05 | 明确在动 |
  | `WEAK` | 0.005 ≤ range < 0.05 | 动过但幅度小 |
  | `CONSTANT-NONZERO` | range < 0.005 且 \|mean\| ≥ 0.001 | 恒定非零 |
  | `NEVER-MOVES` | range < 0.005 且 \|mean\| < 0.001 | 实测从来没动过 |

* ⚠️ **丢脸的帧不算样本**：手机丢追时照样发那 15 个标量、只是不发 `BlendShapes`（整帧键 65 → 15），
  所以只统计**整帧齐全**的帧，否则每条形状线都会被算成"永远 0"。
* ⚠️ **`WEAK` / `NEVER-MOVES` 只表示"这批采样里没见到它动"**，不等于"它不会动"。
  看结论要连着"**我做了那个动作没有、有没有配对做、采了多少帧**"一起看。
* **值直通**：参考预设是纯直通（不改名、不改量纲）。两台实测同名同帧值相等，
  差异只有两类 —— `Timestamp`（绝对值 ~1.79e12 超过 float 精度，见 §3）与 `EyeBlinkLeft` 上升沿
  （差 0.002–0.083，变化越快差越大）：那是**取数时机，不是变换**。

### 1.1 设备**不**提供的东西（决定了 §7）

| 缺什么 | 影响的参数 |
|---|---|
| 麦克风 / 音频（VTS 手机的 tracking 包不带音频，也没有 OVRLipSync） | `Voice*` 一族、`Visemes`、`volume` |
| 鼠标 / 触摸 | `MousePositionX/Y` |
| 生气脸的专用分类器 | `FaceAngry` |

**这是有意的边界，不是暂时没做。** 要接麦克风就是**新增一条输入**（另一个接收端 + 新的线名），
而不是在这一层里塞音频代码 —— 那会毁掉"换个协议只改配置"这条性质。
⚠️ **手机侧开麦克风口型也不会往协议里加字段**：麦克风口型是 VTS 在自己进程里对麦克风做的，跟发什么包无关。

---

## 2. 52 个通道：三个名字 + 设备实测

**语义一列照抄 Unity 官方枚举描述**（`ARKitBlendShapeLocation`，`com.unity.xr.arkit@5.1.6`）。
**range 后括号里是同期 session 均值**（不是静息值）；标了「贴地」的是 near-zero 帧占比高（**要专门做那个动作才会出现**）。

| # | 规范名 | VTS 线名（苹果发的） | `_L/_R` 线名（安卓/VB） | 语义 | 安卓实测 range（均值） | 苹果实测 range（均值） | 触发 / 静息 / 伪影 |
|---|---|---|---|---|---|---|---|
| **眼睑 / 眼周（6）** | | | | | | | |
| 1 | `eyeBlinkLeft` | `EyeBlinkLeft` | `eyeBlink_L` | 左眼睑闭合 | `eyeBlink_L` **0.999**（0.067）· `EyeBlinkLeft` 0.804（0.388） | 0.513（0.107）· 14.3% 贴地 | 安卓**同时发两套拼写**，选哪套是个真实的选择（原文如此） |
| 2 | `eyeBlinkRight` | `EyeBlinkRight` | `eyeBlink_R` | 右眼睑闭合 | `eyeBlink_R` **0.999**（0.069）· `EyeBlinkRight` 0.801（0.393） | 0.511（0.106）· 14.3% 贴地 | 同上 |
| 3 | `eyeSquintLeft` | `EyeSquintLeft` | `eyeSquint_L` | 左眼周围收缩（眯） | 0.788（0.281） | 0.322（0.060） | `eyeSquint` ≠ `eyeBlink`；**伪影**：`eyeBlink` 混着 `3.9·eyeSquint` 的下眼睑泄漏 + 0.06 静止偏置（控制器轴去污口径） |
| 4 | `eyeSquintRight` | `EyeSquintRight` | `eyeSquint_R` | 右眼周围收缩（眯） | 0.828（0.288） | 0.322（0.060） | 同上 |
| 5 | `eyeWideLeft` | `EyeWideLeft` | `eyeWide_L` | 左眼上下眼睑张开 | 0.123（0.027） | 0.328（0.025）· 74.4% 贴地 | **要明显睁大眼**（65–75% 帧贴地） |
| 6 | `eyeWideRight` | `EyeWideRight` | `eyeWide_R` | 右眼上下眼睑张开 | 0.117（0.026） | 0.279（0.024）· 75.4% 贴地 | 同上 |
| **眼动（8）** | | | | | | | |
| 7 | `eyeLookDownLeft` | `EyeLookDownLeft` | `eyeLookDown_L` | 左眼向下看 | 0.347（0.052） | 0.559（0.330） | |
| 8 | `eyeLookDownRight` | `EyeLookDownRight` | `eyeLookDown_R` | 右眼向下看 | 0.346（0.052） | 0.556（0.328） | |
| 9 | `eyeLookInLeft` | `EyeLookInLeft` | `eyeLookIn_L` | 左眼向**右**看（向鼻侧） | 0.840（0.058） | 0.503（0.017）· 65.0% 贴地 | In/Out 是**相对鼻子**的方向；ARKit 预览是镜像的，最容易把 L/R 判反 |
| 10 | `eyeLookInRight` | `EyeLookInRight` | `eyeLookIn_R` | 右眼向**左**看（向鼻侧） | 0.872（0.068） | 0.911（0.155） | 同上 |
| 11 | `eyeLookOutLeft` | `EyeLookOutLeft` | `eyeLookOut_L` | 左眼向**左**看（向颞侧） | 0.869（0.071） | 0.822（0.037）· 36.0% 贴地 | |
| 12 | `eyeLookOutRight` | `EyeLookOutRight` | `eyeLookOut_R` | 右眼向**右**看（向颞侧） | 0.823（0.061） | 0.380（0.006）· **96.6% 贴地** | **要明显外看** |
| 13 | `eyeLookUpLeft` | `EyeLookUpLeft` | `eyeLookUp_L` | 左眼向上看 | 0.596（0.076） | 0.332（0.009）· **93.6% 贴地** | **要明显抬眼看** |
| 14 | `eyeLookUpRight` | `EyeLookUpRight` | `eyeLookUp_R` | 右眼向上看 | 0.596（0.076） | 0.331（0.009）· **93.6% 贴地** | 同上 |
| **下颌（4）** | | | | | | | |
| 15 | `jawForward` | `JawForward` | `jawForward` | 下颌前伸 | **不发** | 0.420（0.038） | **要张嘴**（闭嘴时几乎不给值）；裸线满量程只有 **0.13**（控制器实测） |
| 16 | `jawLeft` | `JawLeft` | `jawLeft` | 下颌向左 | 0.386（0.033） | 0.344（0.019） | **要张嘴**；左右拉嘴设备主要报在这里（轻动就 0.02–0.19），不在 `mouthLeft/Right` |
| 17 | `jawOpen` | `JawOpen` | `jawOpen` | 下颌张开 | 0.702（0.078）· 20.2% 贴地 | 0.803（0.083） | **要张嘴** |
| 18 | `jawRight` | `JawRight` | `jawRight` | 下颌向右 | 0.216（0.020） | 0.600（0.019）· 89.7% 贴地 | **要"张嘴 + 下颌往右偏"才明显**；闭嘴时几乎不给值 |
| **嘴（23）** | | | | | | | |
| 19 | `mouthClose` | `MouthClose` | `mouthClose` | **双唇闭合**（与下颌无关，独立于 `jawOpen`） | **不发** | 0.130（0.032） | ⚠️ **不要写成 `1 − jawOpen`**；`jawOpen` 高 + `mouthClose` 高 = 张着嘴但抿唇 |
| 20 | `mouthFunnel` | `MouthFunnel` | `mouthFunnel` | 双唇收成"O 形张开" | 0.666（0.051） | 0.329（0.038） | 圆唇（"o 嘴"）走这条 + `mouthPucker` |
| 21 | `mouthPucker` | `MouthPucker` | `mouthPucker` | 双唇收拢压紧（嘟嘴） | 0.948（0.115） | 0.885（0.199） | 圆唇**峰值最高**的一条；Funnel = 张开的圆 / Pucker = 闭合的收拢 |
| 22 | `mouthLeft` | `MouthLeft` | `mouthLeft` | 双唇整体向左 | 0.866（0.008）· 83.9% 贴地 | 0.965（0.026）· 58.1% 贴地 | **要用力拉**（轻拉时连着 12 份 dump 全是 0.000） |
| 23 | `mouthRight` | `MouthRight` | `mouthRight` | 双唇整体向右 | 0.197（0.001）· 94.6% 贴地 | 0.956（0.023）· 47.8% 贴地 | **要用力拉** |
| 24 | `mouthSmileLeft` | `MouthSmileLeft` | `mouthSmile_L` | 左嘴角向上 | 0.911（0.153） | 0.739（0.062）· 30.5% 贴地 | |
| 25 | `mouthSmileRight` | `MouthSmileRight` | `mouthSmile_R` | 右嘴角向上 | 0.842（0.164） | 0.741（0.074）· 18.7% 贴地 | |
| 26 | `mouthFrownLeft` | `MouthFrownLeft` | `mouthFrown_L` | 左嘴角向下 | 0.820（0.004）· **91.0% 贴地** | 0.230（0.013）· 70.4% 贴地 | **要明显撇嘴**（70–90% 帧贴地） |
| 27 | `mouthFrownRight` | `MouthFrownRight` | `mouthFrown_R` | 右嘴角向下 | 0.797（0.004）· **91.0% 贴地** | 0.224（0.011）· 81.8% 贴地 | 同上 |
| 28 | `mouthDimpleLeft` | `MouthDimpleLeft` | `mouthDimple_L` | 左嘴角向后拉（酒窝） | **不发** | 0.382（0.057） | 猫嘴态实测 0.78–0.84（控制器轴口径） |
| 29 | `mouthDimpleRight` | `MouthDimpleRight` | `mouthDimple_R` | 右嘴角向后拉 | **不发** | 0.367（0.058） | 同上 |
| 30 | `mouthStretchLeft` | `MouthStretchLeft` | `mouthStretch_L` | 左嘴角向左 | **不发** | 0.713（0.131） | |
| 31 | `mouthStretchRight` | `MouthStretchRight` | `mouthStretch_R` | 右嘴角向右 | **不发** | 0.815（0.129） | ⚠️ Unity 官方英文描述写成 "left corner"，**官方笔误**（Apple 原文也是），别把它抄进注释 |
| 32 | `mouthRollLower` | `MouthRollLower` | `mouthRollLower` | 下唇向内卷 | 0.165（0.021） | 0.488（0.059） | 官方语义是"**向口腔内侧**卷"（往牙齿方向），不是向外翻 |
| 33 | `mouthRollUpper` | `MouthRollUpper` | `mouthRollUpper` | 上唇向内卷 | 0.159（0.024） | 0.243（0.017） | 同上 |
| 34 | `mouthShrugLower` | `MouthShrugLower` | `mouthShrugLower` | 下唇向外（耸） | **不发** | 0.638（0.243） | 官方是 "**outward** movement"（向外），不是"向上耸" |
| 35 | `mouthShrugUpper` | `MouthShrugUpper` | `mouthShrugUpper` | 上唇向外（耸） | **0.039**（0.006）· `WEAK`；**常态 0.005 是噪声底**（min 0.0049 / max 0.0443） | 0.556（0.184） | 安卓**基本不给值**：圆唇（"o 嘴"）**不会**让它动（圆唇主要走 `mouthPucker` 0.42 / `mouthFunnel` 0.23）；0.015 / 0.044 那些读数是噪声底，不是表情 ⇒ **按"不动"算** |
| 36 | `mouthPressLeft` | `MouthPressLeft` | `mouthPress_L` | 左侧下唇向上压 | **不发** | 0.414（0.121） | |
| 37 | `mouthPressRight` | `MouthPressRight` | `mouthPress_R` | 右侧下唇向上压 | **不发** | 0.421（0.127） | |
| 38 | `mouthLowerDownLeft` | `MouthLowerDownLeft` | `mouthLowerDown_L` | 左侧下唇向下 | 0.211（0.047） | 0.724（0.084） | |
| 39 | `mouthLowerDownRight` | `MouthLowerDownRight` | `mouthLowerDown_R` | 右侧下唇向下 | 0.211（0.047） | 0.704（0.089） | |
| 40 | `mouthUpperUpLeft` | `MouthUpperUpLeft` | `mouthUpperUp_L` | 左侧上唇向上 | 0.163（0.003）· 73.1% 贴地 | 0.384（0.062） | |
| 41 | `mouthUpperUpRight` | `MouthUpperUpRight` | `mouthUpperUp_R` | 右侧上唇向上 | 0.168（0.003）· 72.6% 贴地 | 0.386（0.065） | |
| **颊 / 鼻 / 舌（6）** | | | | | | | |
| 42 | `cheekPuff` | `CheekPuff` | `cheekPuff` | 双颊向外鼓 | 0.109（0.012） | 0.514（0.057） | 单边鼓时嘴唇被推过去 0.67~0.87、双鼓 ≈0.03（控制器轴分侧口径） |
| 43 | `cheekSquintLeft` | `CheekSquintLeft` | `cheekSquint_L` | 左眼下方/周围颊部上抬 | **不发** | 0.338（0.080） | |
| 44 | `cheekSquintRight` | `CheekSquintRight` | `cheekSquint_R` | 右眼下方/周围颊部上抬 | **不发** | 0.315（0.072） | |
| 45 | `noseSneerLeft` | `NoseSneerLeft` | `noseSneer_L` | 左侧鼻翼上提 | 0.076（0.005）· **常态 0.005 是噪声底**（90%+ 帧贴地） | 0.414（0.169） | 静息实测 0.085…0.1514（三次静息，控制器轴口径）；真动作（挤眼+鼻上抬）0.70 |
| 46 | `noseSneerRight` | `NoseSneerRight` | `noseSneer_R` | 右侧鼻翼上提 | 0.076（0.005）· 同上 | 0.369（0.163） | 同上 |
| 47 | `tongueOut` | `TongueOut` | `tongueOut` | 伸舌 | 0.330（0.035）· max 0.337 | **1.000**（0.025）· **97.0% 贴地** | **要明显伸到位**（95%+ 帧贴地，但能到 1.0）；`1.0` 是"ARKit 能追到的最大程度"，不是物理极限 |
| **眉（5）** | | | | | | | |
| 48 | `browDownLeft` | `BrowDownLeft` | `browDown_L` | 左眉外端下压 | 0.610（0.098） | 0.657（0.182）· 6.4% 贴地 | 只指**眉外侧**（"outer portion"）；静息实测 0.108…0.2083、真皱眉/皱鼻 0.83（控制器轴口径） |
| 49 | `browDownRight` | `BrowDownRight` | `browDown_R` | 右眉外端下压 | 0.610（0.097） | 0.657（0.180）· 6.4% 贴地 | 同上 |
| 50 | `browInnerUp` | `BrowInnerUp` | `browInnerUp` | 双眉内端上抬 | 安卓拆成 `browInnerUp_L` 0.053（0.018）+ `browInnerUp_R` 0.053（0.018）；**常态 0.0176 ± 0.005** | 0.296（0.067） | 苹果是**一条** `BrowInnerUp`，安卓是**两条** `_L/_R`（那两条解析不到通道，见 §0.3） |
| 51 | `browOuterUpLeft` | `BrowOuterUpLeft` | `browOuterUp_L` | 左眉外端上抬 | 0.320（0.041） | 0.110（0.005）· **93.6% 贴地** | **要明显挑眉** |
| 52 | `browOuterUpRight` | `BrowOuterUpRight` | `browOuterUp_R` | 右眉外端上抬 | 0.203（0.020）· 43.9% 贴地 | 0.104（0.004）· **93.6% 贴地** | 同上 |

**分类小计（核对用）**：眼睑 6 + 眼动 8 + 下颌 4 + 嘴 23 + 颊 3 + 鼻 2 + 舌 1 + 眉 5 = **52** ✅

### 2.1 通道语义的坑（写死在这，别再踩）

| 坑 | 说明 |
|---|---|
| `mouthClose` ≠ 闭眼的"闭" | 它是"双唇闭合"，**独立于下颌**；`jawOpen` 高 + `mouthClose` 高 = 张着嘴但抿唇 |
| `eyeLookIn` / `eyeLookOut` 方向 | **按脸自身定义**（`eyeLookInLeft` = 左眼向脸右侧看，即向中线）；ARKit 预览画面是**镜像**的，调试最容易把 L/R 判反 |
| `eyeSquint` ≠ `eyeBlink` | 前者是**眼周**收缩，两者可同时非零 |
| 没有左右后缀的键 | `cheekPuff`、`browInnerUp`、`jawForward/Left/Right/Open`、`mouthClose/Funnel/Pucker/Left/Right/Roll*/Shrug*` 都是单键 —— 别去找 `cheekPuffLeft` |
| `mouthRollLower/Upper` | "**向口腔内侧**卷"，不是向外翻 |
| `mouthShrugLower/Upper` | 官方是 "**outward**"（向外），不是"向上耸" |
| `browDown*` / `noseSneer*` | `browDown*` 只指**眉外侧**；`noseSneer*` 是"鼻翼**周围**上提" |
| `jawForward` | 官方只写"下颌前伸"，**没有承诺与 `jawOpen` 正交**（全家桶里唯一显式写"独立"的是 `mouthClose`）；实测这台 iPhone **要张嘴才给值**、安卓不发 ⇒ 不要拿它当"张嘴" |
| `tongueOut` / `cheekPuff` 的平台支持 | 只有手机端有（`cheekPuff` **只 iOS**），webcam 永远没有数据 |
| 左右对称性 | 52 个里并非全部左右成对：做"单根轴"时要显式决定用左、右还是平均 |
| 逐键机型门槛表**不存在** | Apple 只给了整体要求（iOS 14 / 带 Neural Engine，或 iOS 13 及以下必须 TrueDepth）与 `tongueOut` 的 iOS 12.0；**没有"哪个键需要哪颗芯片"的官方矩阵** → 这类说法一律不要引用 |

---

## 3. 15 条非通道标量（头姿 / 头位 / 眼球 / 信号）

它们**没有规范名**，`parameter == expression`；单位与实测如下（安卓 223 帧 / 苹果 203 帧，原文如此）：

| 线名 | 含义 | 安卓实测 range（均值 · min…max） | 苹果实测 range（均值 · min…max） | 备注 |
|---|---|---|---|---|
| `Rotation_x` | 头姿 X（度） | 102.529（1.288 · −70.662…31.867） | 39.119（−2.280 · −18.810…20.308） | 头姿就用这三条 |
| `Rotation_y` | 头姿 Y（度） | 67.435（**−23.479**） | 48.029（**+19.815**） | **苹果与安卓的符号/零点都不一样** |
| `Rotation_z` | 头姿 Z（度） | 33.430（−1.142） | 26.411（1.778） | |
| `Position_x` | 头位 X（**单位未标定**） | 36.710（0.136 · −20.000…16.710） | 15.760（2.569） | 手机原始值直通 |
| `Position_y` | 头位 Y（单位未标定） | 14.004（−12.503） | 5.949（−3.639） | |
| `Position_z` | 头位 Z（单位未标定） | 15.578（−3.872） | 4.753（−3.126） | |
| `EyeLeft_x` | 左眼水平转角（度） | 27.730（−0.708） | 30.923（**+12.547**） | 单位与零位**未核对是否就是度** |
| `EyeLeft_y` | 左眼垂直转角（度） | 50.390（0.390） | 46.834（0.722） | |
| `EyeLeft_z` | 左眼深度分量 | **0.000（恒 0）** · `NEVER-MOVES` · 100% 贴地 | 5.871（0.119） | 安卓**一个数都没给过**（223 帧、100%、range 0）；苹果会填 ⇒ 不是协议没这个分量，是安卓不填 |
| `EyeRight_x` | 右眼水平转角（度） | 27.728（−0.707） | 30.647（12.447） | |
| `EyeRight_y` | 右眼垂直转角（度） | 49.944（0.210） | 45.552（5.338） | |
| `EyeRight_z` | 右眼深度分量 | **0.000（恒 0）** · `NEVER-MOVES` · 100% 贴地 | 6.111（1.034） | 同上 |
| `FaceFound` | 有脸 1 / 丢脸 0 | 恒定 **1**（`CONSTANT-NONZERO`） | 恒定 **1** | 恒 1 是**筛选的后果**（只统计有脸的帧），不是设备行为 |
| `Hotkey` | 最后按下的屏幕热键编号（1–8；**−1** ＝ 没按过） | 恒定 **−1**（`CONSTANT-NONZERO`） | 恒定 **−1** | 事件语义（没有"松开"信号）；手机版能不能触发**必须实测** |
| `Timestamp` | UNIX 毫秒时间戳 | range 3000000（`1790349000000` → `1790352000000`） | range 1000000 | **float 只有约 7 位有效数字**：前 3 位之外不变，只够看大概，**不参与任何换算**；参考预设的 `-180..180` 曲线还会把它夹到 180 |

---

## 4. 两台设备的差异：**必须一台设备一份配置**

| 事实 | 数字 |
|---|---|
| 眼球 z 分量 | 安卓 **恒 0**（223 帧、100%）；苹果 `EyeLeft_z` range **5.871** / `EyeRight_z` **6.111**（在动） |
| 安卓**根本不发**的通道 | `cheekSquintLeft/Right`、`jawForward`、`mouthClose`、`mouthDimpleLeft/Right`、`mouthPressLeft/Right`、`mouthShrugLower`、`mouthStretchLeft/Right` —— 逐名点数 **11 个通道**（原文一处写"12 组"、一处写"12 条"并多列了 `mouthLowerDownLeft/Right`，而生成表里安卓明明发了 `mouthLowerDown_L/R`，原文如此，见 §10） |
| 安卓**多**给的 | 6 条 `head*` + `browInnerUp` 拆成 `browInnerUp_L`/`_R`（**6 条 `head*` 的收发有矛盾，见 §10**） |
| 两套拼写同时发 | 安卓把 `eyeBlink_L`/`_R`（range 1.00）与 `EyeBlinkLeft`/`Right`（range 0.80）**都发**，只有一套是活值 |
| 方向 / 零点 | 苹果 `Rotation_y` mean **+19.8**、`EyeLeft_x` mean **+12.5**；安卓 `Rotation_y` mean **−23.5**、`EyeLeft_x` mean **−0.71** ⇒ **符号和零点都不一样，连"标定"都不能跨设备抄** |

**结论**：名字不同 ⇒ 用错那份配置时那一行**永远没数据、且不报错**（静默失效）；
名字相同但某台不发的 ⇒ 那一格在那台上恒 0；名字相同、都发的，**量级与零点也可能差很远** ⇒ 曲线/换算不能共用。
⚠️ 所以包里是**两份参考预设**（`ho-debug-androidVTS` / `ho-debug-iphoneVTS`），各自**只列自己做实测发过的线**；
往中间层加"真实转化"时，**两边各写一套**。

---

## 5. 出口：**两份并行 + 一套额外**（共 90 行）

| 组 | 行数 | 长什么样 | 干什么 |
|---|---|---|---|
| **G1 原始 ARKit 52** | **52** | **裸规范名**：`eyeBlinkLeft`、`jawOpen`… | **无损直通**，曲线恒等 `0..1 → 0..1` |
| **G2 官方 VTS 追踪参数** | **20** | `MouthOpen`、`Brows`、`FaceAngleX`… | **合成**出来的语义轴，喂 VTS 就发这组 |
| **G3a VB 自造参数** | **5** | `MouthFunnel`、`MouthPressLipOpen`… | VTS 词表**没有**的概念，要注册才能用 |
| **G3b 姿态向量** | **12** | `Face/Angle/X`、`Body/Pos/Z`… | 4 组 × XYZ（`Face/`·`Body/` 前缀） |
| **G3c 协议层信号** | **1** | `FaceFound` | 追踪健康（**不是艺术轴**） |
| | **90** | | **出口行总数** |
| **C 控制器轴** | **40** | `Ho/Drive/*` | 喂控制器（§6，**不计入上面 90**） |

⚠️ **G2 是"给 VTS 用的折中"，有损，而且故意有损。** `MouthOpen` 把 `mouthClose`/`mouthRoll*`/`mouthFunnel`
折成一个数、`MouthSmile` 把 `mouthFrown*`/`mouthPucker`/`mouthDimple*` 折成一个数。**要细节就读 G1。**
只出 G2 等于在中间层就把信息扔了。

⚠️ **G1 用裸名，没有前缀**：VBridger 不这么干（它的 VMC 发送循环遍历整个 `outputValues`，原始量就是**裸名**发出去的）；
加了前缀（曾经写成 `ARKit/<规范名>`）就**谁都读不到** —— 控制器只写自己参数表里声明过的名字，52 行白算。

⚠️ **只有姿态向量带命名空间**，因为那两个真的会撞：

| 名字 | 含义 |
|---|---|
| `FaceAngleX` | 官方 VTS 标量：`±90`，不缩放 |
| `Face/Angle/X` | VB 向量分量：`±30`，带 `0.66` 阻尼 |

`Head/*` 是**已删的 `ho-vts-default`** 的保留名，**别再用**。

⚠️ 出口里有 5 对名字只差大小写（`cheekPuff`/`CheekPuff`、`mouthFunnel`/`MouthFunnel`、
`mouthPucker`/`MouthPucker`、`browInnerUp`/`BrowInnerUp`、`tongueOut`/`TongueOut`）——
**这是正常的**：整条链每个字典都是 `StringComparer.Ordinal`（大小写敏感），它们是不同的键、不同的参数。
两套名字并存是故意的（原始裸名 + VB 合成名）。

### 5.1 G1 原始 ARKit 52

就是 §2 那 52 条规范名，一个不多一个不少，`parameter = expression = <规范名>`，曲线恒等。
它的意义是"**你永远拿得到原始值**"：G2/G3 任何一个合成量的口径你不认同时，可以直接拿这 52 条自己算。
**它是这一层的信息上界。**
⚠️ **G1 不给任何修饰符** —— 它的全部价值就是"没被动过的值"。

### 5.2 G2 官方 VTS 追踪参数（20）

`公式来源` 列写的是哪份 VBridger 预设 —— **选公式按预设，不按出口名**：
VB 的 `EyeRightX` 是用 `eyeLookIn_L`/`eyeLookOut_L`（**左眼**！）算的，`EyeRightY` 还加了 `browOuterUp_L`，
**名字根本不描述来源**。

| # | 参数 | 含义 | 值域 | 源 | 公式来源 |
|---|---|---|---|---|---|
| 1 | `FaceAngleX` | 左右转头 | 度 | `Rotation_*` | `VTS_Compatible` |
| 2 | `FaceAngleY` | 抬低头 | 度 | `Rotation_*` | `VTS_Compatible` |
| 3 | `FaceAngleZ` | 歪头 | 度 | `Rotation_*` | `VTS_Compatible` |
| 4 | `FacePositionX` | 头左右位移 | 未标定 | `Position_*` | `VTS_Compatible` |
| 5 | `FacePositionY` | 头上下位移 | 未标定 | `Position_*` | `VTS_Compatible` |
| 6 | `FacePositionZ` | 头远近位移 | 未标定 | `Position_*` | `VTS_Compatible` |
| 7 | `EyeOpenLeft` | 左眼睁开度（**0.5 中立**） | `0..1` | ARKit 眼睑 | `AdvancedARKit_V3.0` |
| 8 | `EyeOpenRight` | 右眼睁开度（0.5 中立） | `0..1` | ARKit 眼睑 | `AdvancedARKit_V3.0` |
| 9 | `EyeLeftX` | 左眼水平注视 | 度 | `EyeLeft_x` | **设备原值** |
| 10 | `EyeLeftY` | 左眼垂直注视 | 度 | `EyeLeft_y` | **设备原值** |
| 11 | `EyeRightX` | 右眼水平注视 | 度 | `EyeRight_x` | **设备原值** |
| 12 | `EyeRightY` | 右眼垂直注视 | 度 | `EyeRight_y` | **设备原值** |
| 13 | `Brows` | 双眉共同上下（0.5 中立） | `0..1` | ARKit 眉 | `AdvancedARKit_V3.0` |
| 14 | `BrowLeftY` | 左眉上下（0.5 中立） | `0..1` | ARKit 眉 **+ 嘴** | `AdvancedARKit_V3.0` |
| 15 | `BrowRightY` | 右眉上下（0.5 中立） | `0..1` | ARKit 眉 **+ 嘴** | `AdvancedARKit_V3.0` |
| 16 | `MouthOpen` | 嘴唇开口 | `0..1`（**官方**） | ARKit 嘴/颌 | `AdvancedARKit_V3.0` |
| 17 | `MouthSmile` | 综合嘴型（Form） | `-1..1`（社区） | ARKit 嘴 | `AdvancedARKit_V3.0` |
| 18 | `MouthX` | 嘴左右位移 | `-1..1`（社区） | ARKit 嘴 | `AdvancedARKit_V3.0` |
| 19 | `TongueOut` | 伸舌 | `0..1` | `TongueOut` | 各预设恒等 |
| 20 | `CheekPuff` | 鼓腮 | `0..1` | `CheekPuff` | 各预设恒等 |

⚠️ 第 9–12 条我们**不用 VB 的合成**：iPhone 本来就在发原始眼球标量，直接引设备值更干净。
⚠️ 第 16/17 条是**有损**合成（见上）。

### 5.3 G3a VB 自造参数（5）—— 不在任何官方清单里，**要注册才能用**

喂 VTS 时要走 `ParameterCreationRequest`，**接收端用户必须接受**才生效 —— 这是用它们的代价。

| # | 参数 | 建模的东西（VTS 通用参数**没有**） | 值域 | 源 |
|---|---|---|---|---|
| 21 | `MouthFunnel` | 圆唇 / 漏斗嘴 | `0..1` | `mouthFunnel`、`jawOpen` |
| 22 | `MouthPucker` | 嘟嘴 | `-1..1` | `mouthDimple*`、`mouthPucker` |
| 23 | `MouthShrug` | 撇嘴 | `0..1` | `mouthShrug*`、`mouthPress*` |
| 24 | `MouthPressLipOpen` | 压唇 + 唇开 | `-1.3..1.3` | `mouthUpperUp*`、`mouthLowerDown*`、`mouthRoll*` |
| 25 | `BrowInnerUp` | 内眉上抬 | `0..1` | `browInnerUp` |

全部取自 `AdvancedARKit_V3.0`。
⚠️ `MouthPressLipOpen` 的除数 VB 自己四份预设四个值（`/1.2`、`/1.8`、`/16`，`VisemesARKit` 干脆换整套公式），
**没有权威值**；我们取多数派 `/1.8`。

### 5.4 G3b 姿态向量（12 = 4 组 × XYZ）

⚠️ **命名空间是必须的**（见 §5 开头：`FaceAngleX` 与 `Face/Angle/X` 是不同的量）。
取 `AdvancedARKit_V3.0` 的**简洁版**（无交叉项、`BodyAngle` 不混 `eyeBlink`）——
混两份预设会让同一份配置里的口径不一致，这比"少一点精度"更糟。

| 组 | 出口参数名 | 含义 | 值域 | 出口公式 |
|---|---|---|---|---|
| 脸旋转 | `Face/Angle/X` | 左右转头 | `±30` | `-Rotation_y * .66` |
| | `Face/Angle/Y` | 抬低头 | `±30` | `-Rotation_x * .66` |
| | `Face/Angle/Z` | 歪头 | `±30` | `Rotation_z * .66` |
| 脸位移 | `Face/Pos/X` | 左右 | 未标定 | `-Position_x` |
| | `Face/Pos/Y` | 上下 | 未标定 | `Position_y` |
| | `Face/Pos/Z` | 远近 | 未标定 | `-Position_z` |
| 身体旋转 | `Body/Angle/X` | 身体左右转 | `±30` | `-Rotation_y * .66` |
| | `Body/Angle/Y` | 身体前后倾 | `±30` | `-Rotation_x * .66` |
| | `Body/Angle/Z` | 身体侧倾 | `±30` | `Rotation_z * .66` |
| 身体位移 | `Body/Pos/X` | 身体左右 | 未标定 | `-Position_x` |
| | `Body/Pos/Y` | 身体上下 | 未标定 | `Position_y` |
| | `Body/Pos/Z` | 身体远近 | 未标定 | `-Position_z` |

⚠️ **脸旋转与身体旋转现在算出的是同一个值**（都只吃 `Rotation_*`）。VB 的 `BodyAngle` 是给
"头部转动带动身体"用的，它靠**下游骨骼权重**去区分，而不是靠公式 ⇒ 这两组不是冗余。
控制器若不做身体骨骼，`Body/*` 可以整组不接。

### 5.5 G3c 协议层信号（1）

| 参数 | 含义 | 值域 | 源 |
|---|---|---|---|
| `FaceFound` | 追踪健康（**不是艺术轴**） | `0/1` | `FaceFound`（**设备原值**） |

⚠️ 它**不是** VTS 的命名追踪参数：注入报文里 `faceFound` 是与 `parameterValues[]` 平级的**布尔字段**。
拼写必须逐字是 `FaceFound`（大小写敏感）。
**谁在读**：只有**控制器**（`HoFaceController.Solve` 把它喂进去，float 口直接写值、bool 口按 `value != 0f`）；
node 接口不暴露它，求解器也不读这一格。用途只有一个：**丢追动画**。

### 5.6 出口公式全表（照抄 VB 原文，变量是我们填的规范名）

§5.2/5.3 的口径写出来就是这样 —— 形状变量是**我们的规范名**
（VB 的原文写 `eyeBlink_L`/`mouthSmile_L`，换算见 §0.2）；`Rotation_*`/`Position_*` 那类**设备线名**没有规范名，原样用：

```
FaceAngleX      = -Rotation_y                 EyeOpenLeft    = .5 + (eyeBlinkLeft  * -.8) + (eyeWideLeft  * .8)
FaceAngleY      = -Rotation_x                 EyeOpenRight   = .5 + (eyeBlinkRight * -.8) + (eyeWideRight * .8)
FaceAngleZ      =  Rotation_z
FacePositionX   = -Position_x                 Brows          = .5 + (browOuterUpLeft + browOuterUpRight - browDownLeft - browDownRight) / 4
FacePositionY   =  Position_y                 BrowLeftY      = .5 + (browOuterUpLeft  - browDownLeft)  + ((mouthRight - mouthLeft) / 8)
FacePositionZ   = -Position_z                 BrowRightY     = .5 + (browOuterUpRight - browDownRight) + ((mouthLeft - mouthRight) / 8)
                                              BrowInnerUp    = browInnerUp
EyeLeftX   = EyeLeft_x     EyeRightX  = EyeRight_x
EyeLeftY   = EyeLeft_y     EyeRightY  = EyeRight_y

MouthOpen         = (jawOpen - mouthClose) - ((mouthRollUpper + mouthRollLower) * .2) + (mouthFunnel * .2)
MouthSmile        = (2 - (mouthFrownLeft + mouthFrownRight + mouthPucker) + (mouthSmileRight + mouthSmileLeft + ((mouthDimpleLeft + mouthDimpleRight) / 2))) / 4
MouthX            = ((mouthLeft - mouthRight) + (mouthSmileLeft - mouthSmileRight))
TongueOut         = tongueOut
CheekPuff         = cheekPuff

MouthFunnel       = mouthFunnel - (jawOpen * .2)
MouthPucker       = ((mouthDimpleRight + mouthDimpleLeft) * 2) - mouthPucker
MouthShrug        = (mouthShrugUpper + mouthShrugLower + mouthPressRight + mouthPressLeft) / 4
MouthPressLipOpen = ((mouthUpperUpRight + mouthUpperUpLeft + mouthLowerDownRight + mouthLowerDownLeft) / 1.8) - (mouthRollLower + mouthRollUpper)

Face/Angle/X = -Rotation_y * .66      Body/Angle/X = -Rotation_y * .66
Face/Angle/Y = -Rotation_x * .66      Body/Angle/Y = -Rotation_x * .66
Face/Angle/Z =  Rotation_z * .66      Body/Angle/Z =  Rotation_z * .66
Face/Pos/X   = -Position_x            Body/Pos/X   = -Position_x
Face/Pos/Y   =  Position_y            Body/Pos/Y   =  Position_y
Face/Pos/Z   = -Position_z            Body/Pos/Z   = -Position_z
```

⚠️ `MouthSmile` **静息 = 0.5**（`2 - …` 除以 `4`）；`MouthPucker` 里 `(dimple*2) - pucker` 的**正负方向与直觉相反**（dimple 大时值为正）。
⚠️ **`BrowLeftY`/`BrowRightY` 吃嘴部数据**（`mouthLeft/Right` 当偏航补偿项）—— "名字不描述来源"的又一个例子。
⚠️ `FaceAngle*` / `FacePosition*` 的**正负号是 VB 的**，照抄结构；它吃的那两根设备轴**还没在实机上核过**（§9）。

### 5.7 命名不一致 —— 照抄 VB 的原样，**不改**

同一族里 VB 自己就不统一（`MouthFunnel` vs `MouthPressLipOpen` vs `FaceFound`；
`BrowLeftY` 读嘴部数据；`EyeRightX` 在 VB 里读**左眼**数据）—— 这是设计现状，不要"顺手统一"。
我们**按用途**微调时要在配置的 `notes` 里写明"改过什么、为什么"。实际偏离只有两处：
`FaceAngle*`/`FacePosition*` 取 `VTS_Compatible` 的官方标量拼法、向量组取 V3.0 的简洁公式。

---

## 6. 控制器轴 `Ho/Drive/*`（40 行，**不计入 90**）

**这一组不是"给下游的出口"，是"喂控制器的口径"**：发货 profile **同时**写两份东西 ——
G1–G3 那些**出口名**（VTS 生态照旧按那些名字读），外加 `Ho/Drive/<部位>/<轴>`，让控制器直接按名取轴。

⚠️ **为什么是"新增"而不是"把出口名改掉"**：G1–G3 的名字是**下游契约**（VTS 生态、Hub 消费者、
离线台架 `Tools~/FaceTracking/profile-json-test` 的核对都按名字守它们）。改名只省一份重复，代价是改一条对外契约 —— 不值。
⚠️ **为什么表达式这么长（都是内联展开）**：**输出行之间不能互相引用**（求值器解析变量时只查**输入行**），
所以"切片权重 = 函数(轴)"只能把轴的公式再抄一遍。
⚠️ **上面这张表是"我们选了什么口径"**；发货 profile 里**逐行的表达式原文、曲线范围、修饰符现状**
（33 根轴已挂 `smooth`；区域门与切片行不挂）在[轴的口](AXES.md)（原 `VTS_HQ_CONTROLLER.md` §3.1 / §3.2 已拆到这里）——
两边同名同义，**改公式时两处一起改**。

| 参数 | 表达式（现状） | 值域 | 实测 / 现状 |
|---|---|---|---|
| `Ho/Drive/Lid/Left/BlinkWide` | `clamp((eyeBlinkLeft - 3.9 * eyeSquintLeft - 0.06) * (1 - clamp(HoExternalEyeSync, 0, 1)) + (max(eyeBlinkLeft - 3.9 * eyeSquintLeft, eyeBlinkRight - 3.9 * eyeSquintRight) - 0.06 - min(eyeWideLeft, eyeWideRight)) * clamp(HoExternalEyeSync, 0, 1), -1, 1)` | −1 睁大 … **0 中性** … +1 闭 | **去污**：实测 `eyeBlink` 混着 `3.9·eyeSquint` 的下眼睑泄漏 + **0.06 静止偏置** |
| `Ho/Drive/Lid/Left/Form` | `clamp(clamp((eyeSquintLeft - 0.05) / 0.05, 0, 1) * clamp(1 - out("Ho/Drive/Lid/Left/BlinkWide") / 0.20, 0, 1) - clamp(-out("Ho/Drive/Mouth/Form"), 0, 1), -1, 1)` | −1 sad … 0 … +1 笑眼 | 正侧门在**去污后的** `BlinkWide` 上 |
| `Ho/Drive/Lid/Right/BlinkWide` | 同左（`…Right`） | −1 … 0 … +1 | 同上 |
| `Ho/Drive/Lid/Right/Form` | 同左（`…Right`） | −1 sad … 0 … +1 笑眼 | 同上 |
| `Ho/Drive/Mouth/Form` | `((mouthSmileRight + mouthSmileLeft + ((mouthDimpleLeft + mouthDimpleRight) / 2)) - 2 * max((mouthFrownLeft + mouthFrownRight) / 2, clamp(((mouthStretchLeft + mouthStretchRight) / 2 - (0.42 * jawOpen + 0.05)) * 1.5, 0, 1))) / 2` | −1 sad … 0 … +1 笑 | = 2×`MouthSmile` − 1（**必须**重映射：VB 静息 0.5）。名字**读作"净笑量"**，`Form` 是保留的（Cubism `ParamMouthForm` / VTS 推荐接法） |
| `Ho/Drive/Mouth/Open` | `(jawOpen - mouthClose) - ((mouthRollUpper + mouthRollLower) * .2) + (mouthFunnel * .2)` | 0 … 1 | **实测张满只到 0.75、半张 0.4、大笑张嘴 ≈0.6**；响应曲线带 **±0.02 死区**（0.02…0.05 是斜坡） |
| `Ho/Drive/Mouth/Funnel` | `mouthFunnel - (jawOpen * .2)` | 0 … 1（负侧也有值） | |
| `Ho/Drive/Mouth/Press` | `((mouthUpperUpRight + mouthUpperUpLeft + mouthLowerDownRight + mouthLowerDownLeft) / 1.8) - (mouthRollLower + mouthRollUpper)` | −1 压/卷 … +1 展/露齿 | 双向 |
| `Ho/Drive/Mouth/Jaw` | `clamp(if((jawOpen - mouthClose) < 0, jawOpen - mouthClose, jawOpen), -1, 1)` | **−1 咬合/压 · 0 静息 · +1 张开** | 下巴的**竖直**轴（双极）。正侧 = `jawOpen` 本身，负侧保留差值（否则闭嘴下颌下拉会被 `mouthClose` 整份抵消）。带 **±0.05 死区**（0.05…0.08 斜坡）。⚠️ **咀嚼就长在这根轴上，振幅 ≤0.05 会被死区整个抹掉**，振幅待测。⚠️ 安卓不发 `mouthClose` ⇒ 那边只剩正侧 |
| `Ho/Drive/Mouth/JawSide` | `jawRight - jawLeft` | −1 偏左 · 0 居中 · +1 偏右 | **实测单侧只到 0.52**。⚠️ 咬颌态相关：咬紧时真动只有 **0.02–0.04**，而"只挤嘴角"的**伪影有 0.05–0.2**；**微张解放咬颌才到 0.52**。带 ±0.05 死区；树里 X 刻度按实测收到 `−0.65 / 0 / +0.65`。⚠️ **当前没有树消费**（下巴表降成 1D 只留上下），照旧发布当出口 |
| `Ho/Drive/Mouth/Forward` | `jawForward` | 0 … 1（⚠️ **裸线满量程只有 0.13**） | **降级成辅助变量/出口，不做树**。实测（只留档）：静息 ≈0 / 噘嘴单独 ≈0.05 / 噘嘴+前顶 = **0.14** |
| `Ho/Drive/Mouth/X` | `clamp((mouthLeft - mouthRight) + (mouthSmileLeft - mouthSmileRight), -1, 1)` | **+1 往左 · 0 静息 · −1 往右** | **整嘴左右平移（有符号）**。判据里**不借 `dimple`**（dimple 归嘴角层 —— 两层不许吃同一个信号）。曲线带 **±0.20 死区**（0.20…0.30 斜坡）。实测：整嘴平移 `mouthLeft/Right` **0.963~0.976** vs 嘴角撇 **0.047~0.136** ⇒ **缝 +0.594** |
| `Ho/Drive/Mouth/Y` | `clamp(mouthPucker * clamp((mouthPucker - 0.45) / 0.02, 0, 1) * (1 - clamp((max(noseSneerLeft, noseSneerRight) - 0.54) / 0.05, 0, 1)) - clamp((jawOpen * clamp((mouthClose - jawOpen + 0.1) / 0.1, 0, 1) - 0.35) / 0.1, 0, 1), -1, 1)` | **+1 上 · 0 静息**（负侧到不了 ⇒ 树里钳到 0） | **整嘴上下平移**（只有 0 / +1 两档）。**+ 上 = 噘嘴判据**（膝 **0.45** + 鼻门 0.54/0.05）；**− 下 = 闭唇张开下颌**（膝 **0.35**/0.10 + **相对唇门** `(mouthClose − jawOpen + 0.10)/0.10`：说话那个瞬态 `jawOpen` 0.65 / `mouthClose` 0.28 ⇒ 0）。114 段老语料：单通道缝 **+0.171**、加鼻门全局最坏 **+0.951**。⚠️ 与"咀嚼"只能靠**幅度**分：均值 0.085 ⇒ 0，但**峰值 0.2~0.37** ⇒ 膝 0.35 + `smooth 0.10 s` |
| `Ho/Drive/Mouth/Pucker` | `(((mouthDimpleRight + mouthDimpleLeft) * 2) - mouthPucker) * (1 - clamp((out("Ho/Style/CatMouth") - 0.25) / 0.15, 0, 1))` | −1 … +1 | 双向。**猫嘴门**膝 **0.25/0.15**（0.15 会误伤"抿嘴嘴宽"，那一档的猫嘴权重只有 0.145）。**猫嘴 `dimple` 0.78~0.84 ⇒ 不加门时这根轴算到 ≈3.1、永远顶满宽档**。⚠️ 当前口径下猫嘴判据经"维持"后是 **0/1** ⇒ 膝写 0.15 还是 0.25 **效果一样**（0 ⇒ 门 1、1 ⇒ 门 0）。⭐ 它是 `MouthWidth`（嘴宽，1D 3 格）的轴：刻度 **−0.93 收嘴不撅（窄）/ −0.09 静态（中）/ +2.0 抿嘴嘴宽（宽）**（9 段补录实测；表达式是 `2×`**和**`（dimpleL + dimpleR）` ⇒ 宽端量纲是 2，抿嘴嘴宽 = 4×0.525 − 0.104 = **1.996**） |
| `Ho/Drive/Mouth/TongueL` | `tongueOut` | 0 … 1 | **舌头那条 1D 表（4 格）的轴（伸出量）** |
| `Ho/Drive/Mouth/TongueR` | `tongueOut` | 0 … 1 | **当前没有树消费**（发布给出口 `HQTongueRight`），留给"歪舌头"的侧向开关 |
| `Ho/Drive/Style/CatMouth` | `out("Ho/Style/CatMouth")` —— **判据住在风格行** `Ho/Style/CatMouth`：`clamp(mouthRollLower × clamp((嘴角方向 − 0.12) / 0.10, 0, 1) × (1 + 3·jawOpen²), 0, 1)`，`嘴角方向 = (酒窝左+右)/2 − (苦左+右)/2` | 0 … 1 | 卷唇开关的输入（驱动 `MouthCoreRollSwitch`，阈值 **0.15 / 0.30**，在两张整嘴表间交叉淡入）。实测：真猫嘴 **0.434~0.460**（过满档）· 最坏非目标（常态笑）**0.075** · 抿嘴/用力说话/说话/咀嚼/张满/噘嘴/挤眼/闭唇咀嚼 = **0**。判据物理量 = **下唇内卷 + 嘴角往后拉**：① 下唇卷 `mouthRollLower`（猫嘴上唇几乎不卷 0.026~0.032）；② **嘴角门**（猫嘴 0.358~0.503 vs 非目标 ≤0.096 —— 抿嘴的下唇卷 0.362~0.396 ≈ 猫嘴、用力说话 0.151~0.211，都被这道门挡住）。膝 **0.12**（用户三档 0.50 / 0.25 / 0.17 ⇒ 膝 0.12 后是 0.40 / 0.10 / 0.044，最坏非目标 0.096 仍在膝下）。⭐ **常开写 `HoExternalCatMouth = 1`**（同形：`HoExternalInvertedV`、`HoExternalCheek`；再配 `HoAutoCatMouth = 0` 就只留常开）。⚠️ 已知缺口：笑会把门顶开（酒窝 0.374、苦 0）⇒「笑 + 下唇卷 ≥0.15」会开始淡入（实测余量 2 倍） |
| `Ho/Drive/Gaze/Left\|Right/X` | `EyeLeft_x` / `EyeRight_x` | −1 … +1 | 手机自己就发左右眼标量，不重算；**哪边是内要实测定** |
| `Ho/Drive/Gaze/Left\|Right/Y` | `EyeLeft_y` / `EyeRight_y` | −1 … +1 | |
| `Ho/Drive/Brow/Left/Y` | `2 * ((browOuterUpLeft - browDownLeft*死区) + ((mouthRight - mouthLeft) / 8))`，死区 = `clamp((browDownLeft − 0.24) / 0.02, 0, 1)` | −1 压眉 … **0 静息** … +1 抬眉 | = 2×VB `BrowLeftY` − 1（VB 静息 0.5，且掺了偏嘴）。**死区只掐 `browDown` 那一项**，偏嘴项照留。实测：三次静息 `browDown*` 就有 **0.108…0.2083**（偏嘴项 ≈0）⇒ 旧公式把中性脸钉在 −0.24…−0.38；真皱眉/皱鼻 **0.83**（⇒ −1.66 饱和）在拐点之上，一点没削 |
| `Ho/Drive/Brow/Right/Y` | 同上（右），死区 = `clamp((browDownRight − 0.24) / 0.02, 0, 1)` | 同上（右） | 偏嘴那一项左右反号 |
| `Ho/Drive/Brow/Left\|Right/InnerUp` | `browInnerUp` | 0 … 1 | |
| `Ho/Drive/Cheek/Left\|Right/Squint` | `cheekSquintLeft` / `cheekSquintRight` | 0 … 1 | ⚠️ **没有树消费**（二次元角色表现不了颊）—— 照旧发布当出口 |
| `Ho/Drive/Cheek/Left\|Right/Puff` | `max(mouthLeft, cheekPuff × (1 − clamp(mouthRight / 0.15, 0, 1))) × clamp((cheekPuff − 0.05) / 0.05, 0, 1) × clamp((max(…) − 0.40) / 0.05, 0, 1)`（右侧镜像） | 0 … 1 | 左右靠 **`mouthLeft/Right`** 分（单边鼓时嘴唇被推过去 **0.67~0.87**、双鼓 **≈0.03**），另有**颊门**（0.05/0.05）与**死区**（0.40/0.05） |
| `Ho/Drive/Nose/Up` | `((noseSneerLeft + noseSneerRight) / 2) * clamp((((noseSneerLeft + noseSneerRight) / 2) − 0.18) / 0.02, 0, 1)` | 0 … 1 | **鼻子上顶**（唯一留下的鼻状态）。**死区与眉同形**：三次静息 `noseSneer*` **0.085…0.1514** ⇒ 中性脸白吃"鼻上顶"格 13…21%；真动作（挤眼+鼻上抬）**0.70** 在拐点之上 ⇒ 表里那个 0.7 档精确命中不变 |
| `Ho/Drive/Gate/{Mouth,Eye,Brow,Nose}` | **空**（常量行）+ `defaultValue = 1` | 0 / 1 | 行侧**区域门**（4 个：左右眼并成一个、颊 → 鼻）；**常量行不过曲线**，但修饰符照走 |
| `Ho/Drive/Slice/MouthCore/{Funnel0Press0,Funnel1Press0,Funnel0Press1,Funnel1Press1}` | `(1−F)(1−P)` / `F(1−P)` / `(1−F)P` / `FP`，F、P 内联 | 0 … 1，**四条和恒为 1** | `MouthCore` 的条件切片权重（Funnel × Press 双线性） |

`Ho/Drive/Gate/Expr/*`（按键表情门）**故意不写**：它属于驱动"按键"的那一方（Unity 面板 / Warudo 键盘节点 /
VTS API 适配器），我们每帧写它就等于把它锁死。控制器里给默认值 `0` 即可。
⚠️ 行数账：原文写 **40 行 = 29 轴 + 4 区域门 + 4 切片权重 + 3 契约行**，按上表逐名点数是 **38**（原文如此，见 §10）。

---

## 7. 哪些参数我们**不用**，以及为什么

### 7.1 VTS 官方文档化的 98 个追踪参数（`catalog.native[]`）

| 处置 | 条数 | 是谁 | 为什么 |
|---|---|---|---|
| **用**（= G2 出口） | **20** | `FaceAngleX/Y/Z`、`FacePositionX/Y/Z`、`MouthSmile`、`MouthOpen`、`Brows`、`BrowLeftY`、`BrowRightY`、`EyeOpenLeft/Right`、`EyeLeftX/Y`、`EyeRightX/Y`、`MouthX`、`TongueOut`、`CheekPuff` | §5.2 |
| **不用** | **26** | 手部追踪参数（`HandLeft/Right*`、`HandDistance`、`BothHandsFound`、手指 `HandLeft/RightFinger_1..5_*`） | 跟面捕无关，且手机不产生 |
| **不用** | **39** | 手柄参数（`ControllerStick*`…`ControllerOrientationX/Y/Z`） | 同上（官方只有桌面 Steam 版有） |
| **不用** | **13** | `VoiceA/I/U/E/O`、`VoiceSilence`、`VoiceVolume`、`VoiceFrequency`、`VoiceVolumePlusMouthOpen`、`VoiceFrequencyPlusMouthSmile`、`MousePositionX/Y`、`FaceAngry` | **输入契约里没有源**（§1.1） |

### 7.2 其他明确排除的（有意排除，不是漏了）

| 排除项 | 一句话理由 |
|---|---|
| 整族语音/音素（`Visemes`、`volume`、`viseme_*`、`viseme_*_abs`） | 要声学/视觉音素识别（VB 用的是它自己进程内的 OVRLipSync + 麦克风），VTS 手机协议不带音频 |
| `VoiceVolumePlusMouthOpen` / `VoiceFrequencyPlusMouthSmile` | 没麦克风时 VB 的公式**就是一个重复的 `MouthOpen`/`MouthSmile`** ⇒ 没有新增信息 |
| `FaceFound` | 它**不是** VTS 的命名追踪参数（报文里与 `parameterValues[]` 平级的布尔字段）；我们按设备原值收，见 §5.5 |
| VB 出口里的 `Eye_Squint_L/R`、`BrowL/R`、`EyeOpen`、`EyeX/Y` | **已有参数换了个拼写或换了个聚合**（再出一遍就是同一个值写两个名字） |
| `EyeX` / `EyeY`（VB 的 PNGTuber 合成注视） | 只看**左眼**、而且有损；控制器要"双眼合并注视"就自己用 G1 的 `eyeLook*Left/Right` 在树里压 |
| VMC 骨骼向量 `Head` / `Neck` / `LeftEye` / `RightEye` | 那是 `/VMC/Ext/Bone/Pos` 的**骨骼**，不是参数；身体姿态我们改用 G3b 的 `Body/*` |
| VMC / VRM / VRCFT 三条下游 | 都不接：VMC 的 blend 名字空间属于收方模型、VRM 只有 17 个功能预设、VRCFT 的 Unified Expressions 是**另一套更大的标准**（`EyeClosedLeft` ≠ `eyeBlinkLeft`，两套名字**绝不可以**混在同一张映射表里） |
| **iFacialMocap** 线协议 | 给不出 `FaceFound`，而那是"丢追回中性"唯一的开关 ⇒ 整条接收端已删；它的 `_L/_R` 拼写规则**活在安卓 VTS 上**（安卓发的就是这套） |
| `Head/*` | 已删的 `ho-vts-default` 的保留名，别再用 |
| "逐键机型门槛表" | **不存在**（Apple 只有整体要求 + `tongueOut` 的 iOS 12.0）⇒ 不要引用 |
| 15 个音素权重 + 15 个硬判 | 与第 1 行同理（`Visemes` 那族） |

**出口侧"算不出来"的 9 行**（§7.1 的 13 个里落在出口名上的那一部分）：
`Visemes`、`VoiceVolume`、`VoiceFrequency`、`VoiceVolumePlusMouthOpen`、`VoiceFrequencyPlusMouthSmile`、
`VoiceA/I/U/E/O`、`VoiceSilence`、`MousePositionX/Y`、`FaceAngry` —— 这一列**不是"以后再做"**，
是**当前输入契约下不可能**；要它们必须先扩输入。

---

## 8. 值域约定与曲线纪律

### 8.1 官方只规定了两个参数的范围

| 参数 | 范围 | 依据 |
|---|---|---|
| `MouthOpen` | `[0, 1]`，0 = 闭、1 = 全开 | VTS wiki 原文 |
| `FaceAngleX` | 示例 min `-30` / max `30` | VTS API 文档示例数组 |
| `FacePositionX` | 示例 min `-10` / max `10` | 同上 |
| 其余全部 | **官方未公开** | 官方自己写那张示例数组 "is incomplete"；权威范围只能运行时 `InputParameterListRequest` 现取 |

⚠️ 网上流传的 `MouthSmile -1..1`、`Brows 0..1` 是**社区/VBridger 的约定**，不是官方规范。
我们沿用 VB 的约定时要说清这一点。

### 8.2 0.5 中立位是 **VB 发明的**，不是 VTS 的

`Brows`、`BrowLeftY`、`BrowRightY`、`EyeOpenLeft/Right` 静息就是 **0.5**（闭眼往 0、睁大往 1）。
官方文档里**找不到依据**。
⇒ 照用时**曲线的纵轴范围必须比 `0..1` 宽**，否则默认的 `0..1` 曲线会把合法值**静默夹掉**
（症状是"出口恒为 0 或 1 而输入侧有真值"）。
⚠️ 另一种约定真实存在：VRCFT 用 **0.75** 当"正常睁开"（`0.75·openness + 0.25·wide`）
⇒ **"中性点"必须显式写进配置，不能硬编码。**

### 8.3 三种量纲并存，且没有任何字段标注单位

| 量纲 | 例子 | 范围 |
|---|---|---|
| 表情权重 | `MouthOpen`、`CheekPuff` | `0..1` |
| 双向偏移 | `MouthX`、`EyeX`、`EyeY` | `-1..1` |
| 角度 | `FaceAngle*` | `±30/±40/±90`（**度**） |
| 位移 | `FacePosition*` | `±10/±15/±50`（**单位未标定**） |

### 8.4 四条纪律

1. **双层曲线都要写**：管线是 `出口值 = curve_out( expression_out( 入口值 ) )`，而**输入行自己也有曲线**。
   想直通的量纲，**输入行与出口行都得给宽曲线**；只给一层 = 另一层用默认 `0..1` 夹掉。
2. **G1 不给修饰符**（§5.1）。
3. **一个参数只能有一个写入者**（VTS 官方"Each output parameter can only be chosen once"的等价物；
   我们这边有 `HoFaceOutputOwnership` 防 LookAt / 眨眼约束与面捕互相覆盖）。
4. **输出行不声明 `min`/`max`/`default`**：曲线关键点的范围就是定义域
   （`HoFaceOutput` 只有 `parameter` / `expression` / `curve` / `modifiers` / `notes`）。
   ⚠️ 双层曲线、缺键语义、修饰符细节见[面捕流水线](PIPELINE.md) §3。

---

## 9. 未标定 / 未验证（**别当结论用**）

| 项 | 状态 |
|---|---|
| `FaceAngle*` / `FacePosition*` 的正负号与轴向 | 照抄 VB 的结构，**未在实机核对**。改法是翻配置里那行的符号 |
| `Position_*` 的单位 | **未标定**，手机原始值直通 |
| `EyeLeft_*` / `EyeRight_*` 的单位与零位 | 实测给的是 `Rotation_*` 同级的小数值（`EyeLeft_x` 约 ±20），**未核对是否就是度** |
| `EyeLeftZ` / `EyeRightZ` 是否可用 | iPhone 能填（range 5.87 / 6.11），安卓恒 0。**含义未定** |
| 0.5 中立位是否该保留 | VB 的私有约定（§8.2）。用 VTS 官方语义还是 VB 的，**我们还没定** |
| `MouthPressLipOpen` 的除数 | VB 自己四份预设四个值（`/1.2`、`/1.8`、`/16`，`VisemesARKit` 完全换公式），**没有权威值**；我们取多数派 `/1.8` |
| VB 自造参数（G3a 5 个 + G3b 12 条）的**接收端接受率** | 走 `ParameterCreationRequest`，用户必须手动接受才生效。我们不做 VTS 中转时无所谓，做的时候要测 |
| `Hotkey` 手机版能不能触发 | 实测两台都恒 −1（协议里 −1 = 没按过）；"没按 / 手机版没界面 / 手机版不填"三者未区分 |
| `Timestamp` | float 精度不够（约 7 位有效数字），**只够看个大概，不参与任何换算** |
| 设备发过的 6 条 `head*`（`headUp/Down/Left/Right/RollLeft/RollRight`） | 收/不收在两处自相矛盾（§10 第 1 条）；**现状处置**：52 个规范名不含它们，头姿/头位用 `Rotation_*` / `Position_*` |
| `WEAK` / `NEVER-MOVES` 的含义 | **只是"这批采样里没见到它动"**，不是"它不会动"（§1） |
| 单边鼓腮 / 咀嚼振幅 / 嘴角撇的缝 | 已有实测数字（§6），但都是**小样本**，要更长的捕捉才能定 |

---

## 10. 附录：原文内部不一致处（**照抄，未改**）

来源三份文档里互相打架、或自身前后不一致的地方；本文按"哪一条可执行"取用，其余原样列出以免下次再踩：

| # | 不一致 | 本文怎么处理 |
|---|---|---|
| 1 | **6 条 `head*`**：`PARAMETER_DEVICE_VERIFICATION.md` §0.1 的「订正」说**两台都没有**（日志里看到的是参考预设的**配置残留**、输入行没数据 ⇒ 出口恒 0）；同文件 §0.2 / §1 与 §1.3 生成表却说**安卓实测真的收到**（`headUp` 0.395、`headDown` 0.081、`headLeft` 0.319、`headRight` 0.707、`headRollLeft` 0.158、`headRollRight` 0.177，且 status = `MOVES`） | 规范名表（52 条）里**没有** `head*` ⇒ 不建通道；头姿/头位一律用 `Rotation_*` / `Position_*`。两说并存，**原文如此** |
| 2 | 苹果采样规模：§2 开头写「**120 帧可用**」，同节 §2.3 生成表 `n = 203`；同文件另处还有 114 / 108 / 173 / 223 帧 | 数字照抄并注明来源节号；**原文如此** |
| 3 | 「只有苹果有」的条数：§3.1 写 **12 组**、§2.2 写 **12 条**（但列了 **13** 个名字，其中 `mouthLowerDownLeft/Right` 安卓明明在发） | 本文按 **11 个通道**列（`cheekSquint L/R`、`jawForward`、`mouthClose`、`mouthDimple L/R`、`mouthPress L/R`、`mouthShrugLower`、`mouthStretch L/R`）；**原文如此** |
| 4 | §2.2 与 §2.3 同一批线的数不一致：`JawForward` **0.276** vs **0.420**；`MouthStretchRight` **0.695** vs **0.815**；`MouthClose` **0.093** vs **0.130**；`MouthLowerDownLeft/Right` **0.587/0.605** vs **0.724/0.704** | §2 取 §2.3 生成表的数（§0.2 与之一致）；**原文如此** |
| 5 | 「只有安卓有」写 **9 组**，逐名点数是 **7 组**（6 条 `head*` + `browInnerUp` 拆两条） | 按逐名点数用；**原文如此** |
| 6 | §5 标题写「**9 行 / 11 个**参数」，逐名点数是 **9 行 / 14 个** | 本文写"9 行"，并列出全部名字；**原文如此** |
| 7 | 控制器轴写「**40 行** = 29 轴 + 4 区域门 + 4 切片权重 + 3 契约行」，按 §3.7 表逐名点数是 **38** | 总数按原文的 40；表按逐名列出；**原文如此** |
| 8 | 命名权威的路径：旧文引 `Editor/FaceTracking/HoFaceTrackingChannels.cs`，实际在 **`Runtime/FaceTracking/`** | 本文引实际路径 |

---

## 11. 相关文件

| 文件 | 记什么 |
|---|---|
| [`PARAMETER_HO.md`](PARAMETER_HO.md) | **历史**：我们的参数规范原文（含被推翻的口径与研究过程） |
| [`PARAMETER_DEVICE_VERIFICATION.md`](PARAMETER_DEVICE_VERIFICATION.md) | **历史**：设备实测原文 + 三张可刷新的生成表（§1.3 安卓 65 条 / §2.3 苹果 67 条 / §0.2 语义表） |
| [`PARAMETER_STANDARDS.md`](PARAMETER_STANDARDS.md) | **历史**：外部标准底表（VTS / Cubism / ARKit / VMC / VRM / iFacialMocap / VRCFT，逐条带官方 URL） |
| **本文** | **现状**：通道三名字对应 + 实测数字 + 出口/控制器口径 + 不用的清单 |
| [`AXES.md`](AXES.md) | **现状**：每根轴的口 —— 表达式原文 / 曲线 / 修饰符 / 谁消费 / 刻度与实测定论（§6 的操作口径） |
| [`CONTROLLER.md`](CONTROLLER.md) / [`DECISIONS.md`](DECISIONS.md) | **现状**：控制器结构 / 决策记录 |
| [`VTS_HQ_CONTROLLER.md`](VTS_HQ_CONTROLLER.md) | **短存根**（原 279 KB 已拆到 `AXES.md` + `CONTROLLER.md`；别删，仓库里有 48 处引用指它） |
| [`PIPELINE.md`](PIPELINE.md) | **现状**：整条流水线（VTS 裸输入 → 中间层 → 控制器 → Warudo / 输出） |
| [`FACE_TRACKING_MIDDLE_LAYER.md`](FACE_TRACKING_MIDDLE_LAYER.md) | **历史**：值怎么被加工（两层行、缺键语义、曲线纪律） |
| `Runtime/FaceTracking/HoFaceTrackingChannels.cs` | **命名权威**：52 个规范名（`Names`）+ 别名表（`BuildIndices`，88 条） |
| `docs/VTS_HIGH_QUALITY_FACE_CATALOG.json` | **命名权威**：`raw_arkit[]` 的 `name` / `vts_wire` / `vb_internal` 三名字对应 |

**实测表怎么刷新**（数字来自 `Player.log`；语义照抄 Unity 官方描述、"触发情况"来自实测总结）：

```powershell
& D:\Unity_Fork\HoUnityTools\.warudo-mod-research\.tools\gen-verif-tables.ps1
```

⚠️ 手工改生成表会在下次重跑时被覆盖 —— 要改就改数据源。
**参考预设**（`ho-debug-*.hoface.json`）就是那些表的 `wire` 列：纯直通、零改名、两层宽曲线，一份设备一份，**别互相套用**。
