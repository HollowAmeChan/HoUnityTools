# 参数实机验证表（设备实测：哪些参数真的在动）

> 与 [`PARAMETER_STANDARDS.md`](PARAMETER_STANDARDS.md) 分工：那份是**协议规定**有哪些参数（官方出处），
> 本文是**这台设备实测**下来，那些线名里**哪几条真的在动、哪几条恒 0**。
> 协议里有、设备也发、读数却永远不变的，只有这张表能告诉你。

## 0. 怎么读

* **wire** —— 设备**原样**的线名。参考预设（`ho-debug-*.hoface.json`）**不改名、不改量纲、什么都不改**，
  所以这里的名字就是线上的名字，也是出口字典的名字。名字怎么进来的原样怎么出去。
* **status** —— 判据只看 **range = max − min**：
  一条 97% 时间为 0、但到过 0.028 的线是**带信号**的，用均值判会把它误杀。

  | status | 判据 | 含义 |
  |---|---|---|
  | `MOVES` | range ≥ 0.05 | 明确在动 |
  | `WEAK` | 0.005 ≤ range < 0.05 | 动过，但幅度小 —— **要更长的捕捉才能定** |
  | `CONSTANT-NONZERO` | range < 0.005 且 \|mean\| ≥ 0.001 | 恒定非零（常量，或噪声底） |
  | `NEVER-MOVES` | range < 0.005 且 \|mean\| < 0.001 | 实测从来没动过 |

* **near-zero %** —— 该线读数接近 0 的帧占比。高 + range 小 = 基本没工作。
* ⚠️ **丢脸的帧不算样本**：手机丢追时照样发那 15 个标量、只是不发 `BlendShapes`（本帧键 65 → 15）。
  工具只统计**整帧齐全**的帧，否则每条形状线都会被算成"永远 0"。

### 0.1 最终结论（安卓 VTS，223 帧）

65 条实测线里：

| 分档 | 条数 | 是谁 |
|---|---|---|
| **能用** | **60** | range ≥ 0.05，动得起来 |
| **恒定（预期）** | **2** | `FaceFound` = 1、`Hotkey` = −1 |
| **不给值** | **3** | `EyeLeft_z`、`EyeRight_z`、`mouthShrugUpper` |

**三条"不给值"的性质不一样，别混：**

* **`EyeLeft_z` / `EyeRight_z`** —— **223 帧、100%、range 0**，从头到尾一个数都没给过。
  （VTS 协议里眼是 `Vector3`，z 分量本来就没语义。**对照**：苹果那台是会填的，见 §2。）
* **`mouthShrugUpper`** —— **通道在、但基本不给值**：223 帧 range 只有 **0.0394**、常态 0.005；
  那些 `0.015` / `0.044` 的读数是**噪声底**，不是表情。实测确认（人肉）：圆唇（"o 嘴"）
  **不会**让它动 —— 圆唇在这台设备上主要走 `mouthPucker`（峰值 **0.42**）与
  `mouthFunnel`（**0.23**）；上唇上耸/撇嘴也上不去。⇒ **按"不动"算**。

⚠️ 对比：`mouthLeft` / `mouthRight` **是能给值的**，只是**要用力拉**（见 §1.1）——
它们和上面这三条不是一回事。

#### ⚠️ `head*` 那 6 条**不存在**（订正）

早先的记录里写过"安卓发这 6 条、苹果不发"，**那是错的**。查原始段（`── 原始线名 …`）：

* **安卓 65 条、苹果 67 条，两台都没有** `headUp` / `headDown` / `headLeft` /
  `headRight` / `headRollLeft` / `headRollRight`。
* 之前之所以在日志里看得到这几个名字，是因为**参考预设里曾经列了这几行** ——
  输入行没数据 ⇒ 出口恒 0，于是出口段里能看到 `headUp = 0.000`。
  **那是配置的残留，不是设备发的东西。**
* 头姿就用 `Rotation_x/y/z`、头位用 `Position_x/y/z`（都是标量行）。

### 0.2 逐键语义与触发情况（两台并集）

语义一列**照抄 PARAMETER_STANDARDS.md §3.1 的 Unity 官方描述**（不另写）；
"触发情况"是实测总结出来的**怎么才会让它动**（空格子＝普通表情照常做就有）。
ange 是两台的实测范围（**NEVER-MOVES** / **WEAK** 只是"这批采样里没见到它动"，见下）。

<!-- BEGIN GENERATED SEMANTICS -->
| `browDownLeft` | 左眉外端下压 |  | `browDown_L` 0.610 | `BrowDownLeft` 0.657 |
| `browDownRight` | 右眉外端下压 |  | `browDown_R` 0.610 | `BrowDownRight` 0.657 |
| `browInnerUp` | 双眉内端上抬 |  | `browInnerUp_L` 0.053 + `browInnerUp_R` 0.053 | `BrowInnerUp` 0.296 |
| `browOuterUpLeft` | 左眉外端上抬 | 要明显挑眉（90%+ 帧贴地） | `browOuterUp_L` 0.320 | `BrowOuterUpLeft` 0.110 |
| `browOuterUpRight` | 右眉外端上抬 | 要明显挑眉（90%+ 帧贴地） | `browOuterUp_R` 0.203 | `BrowOuterUpRight` 0.104 |
| `cheekPuff` | 双颊向外鼓 |  | `cheekPuff` 0.109 | `CheekPuff` 0.514 |
| `eyeBlinkLeft` | 左眼睑闭合 |  | `eyeBlink_L` 0.999 + `EyeBlinkLeft` 0.804 | `EyeBlinkLeft` 0.513 |
| `eyeBlinkRight` | 右眼睑闭合 |  | `eyeBlink_R` 0.999 + `EyeBlinkRight` 0.801 | `EyeBlinkRight` 0.511 |
| `eyeLeft_x` | 左眼水平转角（度） |  | `EyeLeft_x` 27.730 | `EyeLeft_x` 30.923 |
| `eyeLeft_y` | 左眼垂直转角（度） |  | `EyeLeft_y` 50.390 | `EyeLeft_y` 46.834 |
| `eyeLeft_z` | 左眼深度分量 | **安卓不填**（恒 0）；苹果会填 | `EyeLeft_z` 0.000 **NEVER-MOVES** | `EyeLeft_z` 5.871 |
| `eyeLookDownLeft` | 左眼向下看 |  | `eyeLookDown_L` 0.347 | `EyeLookDownLeft` 0.559 |
| `eyeLookDownRight` | 右眼向下看 |  | `eyeLookDown_R` 0.346 | `EyeLookDownRight` 0.556 |
| `eyeLookInLeft` | 左眼向**右**看（向鼻侧） |  | `eyeLookIn_L` 0.840 | `EyeLookInLeft` 0.503 |
| `eyeLookInRight` | 右眼向**左**看（向鼻侧） |  | `eyeLookIn_R` 0.872 | `EyeLookInRight` 0.911 |
| `eyeLookOutLeft` | 左眼向**左**看（向颞侧） |  | `eyeLookOut_L` 0.869 | `EyeLookOutLeft` 0.822 |
| `eyeLookOutRight` | 右眼向**右**看（向颞侧） | 要明显外看（96% 帧贴地） | `eyeLookOut_R` 0.823 | `EyeLookOutRight` 0.380 |
| `eyeLookUpLeft` | 左眼向上看 | 要明显抬眼看（90%+ 帧贴地） | `eyeLookUp_L` 0.596 | `EyeLookUpLeft` 0.332 |
| `eyeLookUpRight` | 右眼向上看 | 要明显抬眼看（90%+ 帧贴地） | `eyeLookUp_R` 0.596 | `EyeLookUpRight` 0.331 |
| `eyeRight_x` | 右眼水平转角（度） |  | `EyeRight_x` 27.728 | `EyeRight_x` 30.647 |
| `eyeRight_y` | 右眼垂直转角（度） |  | `EyeRight_y` 49.944 | `EyeRight_y` 45.552 |
| `eyeRight_z` | 右眼深度分量 | **安卓不填**（恒 0）；苹果会填 | `EyeRight_z` 0.000 **NEVER-MOVES** | `EyeRight_z` 6.111 |
| `eyeSquintLeft` | 左眼周围收缩（眯） |  | `eyeSquint_L` 0.788 | `EyeSquintLeft` 0.322 |
| `eyeSquintRight` | 右眼周围收缩（眯） |  | `eyeSquint_R` 0.828 | `EyeSquintRight` 0.322 |
| `eyeWideLeft` | 左眼上下眼睑张开 | 要明显睁大眼（65–75% 帧贴地） | `eyeWide_L` 0.123 | `EyeWideLeft` 0.328 |
| `eyeWideRight` | 右眼上下眼睑张开 | 要明显睁大眼（65–75% 帧贴地） | `eyeWide_R` 0.117 | `EyeWideRight` 0.279 |
| `faceFound` | 有脸 1 / 丢脸 0 |  | `FaceFound` 0.000 **CONSTANT-NONZERO** | `FaceFound` 0.000 **CONSTANT-NONZERO** |
| `headDown` | 头向下（**只有安卓发**） | **只有安卓多给这一份**（旋转系的 0..1 归一化、左右分边）；**不是**位移，也**不能**从 `Rotation_*` 换算出来 —— 见 §0.3 | `headDown` 0.081 | — **苹果不发** |
| `headLeft` | 头向左（**只有安卓发**） | **只有安卓多给这一份**（旋转系的 0..1 归一化、左右分边）；**不是**位移，也**不能**从 `Rotation_*` 换算出来 —— 见 §0.3 | `headLeft` 0.319 | — **苹果不发** |
| `headRight` | 头向右（**只有安卓发**） | **只有安卓多给这一份**（旋转系的 0..1 归一化、左右分边）；**不是**位移，也**不能**从 `Rotation_*` 换算出来 —— 见 §0.3 | `headRight` 0.707 | — **苹果不发** |
| `headRollLeft` | 头左倾（**只有安卓发**） | **只有安卓多给这一份**（旋转系的 0..1 归一化、左右分边）；**不是**位移，也**不能**从 `Rotation_*` 换算出来 —— 见 §0.3 | `headRollLeft` 0.158 | — **苹果不发** |
| `headRollRight` | 头右倾（**只有安卓发**） | **只有安卓多给这一份**（旋转系的 0..1 归一化、左右分边）；**不是**位移，也**不能**从 `Rotation_*` 换算出来 —— 见 §0.3 | `headRollRight` 0.177 | — **苹果不发** |
| `headUp` | 头向上（**只有安卓发**） | **只有安卓多给这一份**（旋转系的 0..1 归一化、左右分边）；**不是**位移，也**不能**从 `Rotation_*` 换算出来 —— 见 §0.3 | `headUp` 0.395 | — **苹果不发** |
| `hotkey` | 最后按下的屏幕热键编号（1–8；−1 ＝ 没按过） | 按 VTS app 内的屏幕热键才给 1–8；实测两台都恒 −1 | `Hotkey` 0.000 **CONSTANT-NONZERO** | `Hotkey` 0.000 **CONSTANT-NONZERO** |
| `jawLeft` | 下颌向左 | **要张嘴**（闭嘴时几乎不给值） | `jawLeft` 0.386 | `JawLeft` 0.344 |
| `jawOpen` | 下颌张开 | **要张嘴**（闭嘴时几乎不给值） | `jawOpen` 0.702 | `JawOpen` 0.803 |
| `jawRight` | 下颌向右 | **要张嘴**（闭嘴时几乎不给值） | `jawRight` 0.216 | `JawRight` 0.600 |
| `mouthFrownLeft` | 左嘴角向下 | 要明显撇嘴（70–90% 帧贴地） | `mouthFrown_L` 0.820 | `MouthFrownLeft` 0.230 |
| `mouthFrownRight` | 右嘴角向下 | 要明显撇嘴（70–90% 帧贴地） | `mouthFrown_R` 0.797 | `MouthFrownRight` 0.224 |
| `mouthFunnel` | 双唇收成"O 形张开" | 圆唇（"o 嘴"；`mouthPucker` 峰值最高） | `mouthFunnel` 0.666 | `MouthFunnel` 0.329 |
| `mouthLeft` | 双唇整体向左 | **要用力拉**（轻拉常年 0） | `mouthLeft` 0.866 | `MouthLeft` 0.965 |
| `mouthLowerDownLeft` | 左侧下唇向下 |  | `mouthLowerDown_L` 0.211 | `MouthLowerDownLeft` 0.724 |
| `mouthLowerDownRight` | 右侧下唇向下 |  | `mouthLowerDown_R` 0.211 | `MouthLowerDownRight` 0.704 |
| `mouthPucker` | 双唇收拢压紧（嘟嘴） | 圆唇（"o 嘴"；`mouthPucker` 峰值最高） | `mouthPucker` 0.948 | `MouthPucker` 0.885 |
| `mouthRight` | 双唇整体向右 | **要用力拉**（轻拉常年 0） | `mouthRight` 0.197 | `MouthRight` 0.956 |
| `mouthRollLower` | 下唇向内卷 |  | `mouthRollLower` 0.165 | `MouthRollLower` 0.488 |
| `mouthRollUpper` | 上唇向内卷 |  | `mouthRollUpper` 0.159 | `MouthRollUpper` 0.243 |
| `mouthShrugUpper` | 上唇向外（耸） | 安卓：**基本不动**（range 0.039，噪声底）；苹果：正常 | `mouthShrugUpper` 0.039 **WEAK** | `MouthShrugUpper` 0.556 |
| `mouthSmileLeft` | 左嘴角向上 |  | `mouthSmile_L` 0.911 | `MouthSmileLeft` 0.739 |
| `mouthSmileRight` | 右嘴角向上 |  | `mouthSmile_R` 0.842 | `MouthSmileRight` 0.741 |
| `mouthUpperUpLeft` | 左侧上唇向上 |  | `mouthUpperUp_L` 0.163 | `MouthUpperUpLeft` 0.384 |
| `mouthUpperUpRight` | 右侧上唇向上 |  | `mouthUpperUp_R` 0.168 | `MouthUpperUpRight` 0.386 |
| `noseSneerLeft` | 左侧鼻翼上提 |  | `noseSneer_L` 0.076 | `NoseSneerLeft` 0.414 |
| `noseSneerRight` | 右侧鼻翼上提 |  | `noseSneer_R` 0.076 | `NoseSneerRight` 0.369 |
| `position_x` | 头位 X（单位未标定） |  | `Position_x` 36.710 | `Position_x` 15.760 |
| `position_y` | 头位 Y（单位未标定） |  | `Position_y` 14.004 | `Position_y` 5.949 |
| `position_z` | 头位 Z（单位未标定） |  | `Position_z` 15.578 | `Position_z` 4.753 |
| `rotation_x` | 头姿 X（度） |  | `Rotation_x` 102.529 | `Rotation_x` 39.119 |
| `rotation_y` | 头姿 Y（度） |  | `Rotation_y` 67.435 | `Rotation_y` 48.029 |
| `rotation_z` | 头姿 Z（度） |  | `Rotation_z` 33.430 | `Rotation_z` 26.411 |
| `timestamp` | UNIX 毫秒时间戳（float 装不下，只够看大概） |  | `Timestamp` 3000000.000 | `Timestamp` 1000000.000 |
| `tongueOut` | 伸舌 | **要明显伸到位**（95%+ 帧贴地，但能到 1.0） | `tongueOut` 0.330 | `TongueOut` 1.000 |
| `cheekSquintLeft` | 左眼下方/周围颊部上抬 |  | — **安卓不发** | `CheekSquintLeft` 0.338 |
| `cheekSquintRight` | 右眼下方/周围颊部上抬 |  | — **安卓不发** | `CheekSquintRight` 0.315 |
| `jawForward` | 下颌前伸 | **要张嘴**（闭嘴时几乎不给值） | — **安卓不发** | `JawForward` 0.420 |
| `mouthClose` | **双唇闭合**（与下颌无关，独立于 jawOpen） |  | — **安卓不发** | `MouthClose` 0.130 |
| `mouthDimpleLeft` | 左嘴角向后拉（酒窝） |  | — **安卓不发** | `MouthDimpleLeft` 0.382 |
| `mouthDimpleRight` | 右嘴角向后拉 |  | — **安卓不发** | `MouthDimpleRight` 0.367 |
| `mouthPressLeft` | 左侧下唇向上压 |  | — **安卓不发** | `MouthPressLeft` 0.414 |
| `mouthPressRight` | 右侧下唇向上压 |  | — **安卓不发** | `MouthPressRight` 0.421 |
| `mouthShrugLower` | 下唇向外（耸） |  | — **安卓不发** | `MouthShrugLower` 0.638 |
| `mouthStretchLeft` | 左嘴角向左 |  | — **安卓不发** | `MouthStretchLeft` 0.713 |
| `mouthStretchRight` | 右嘴角向右（⚠️ Unity 官方英文描述写成 "left corner"，是官方笔误） |  | — **安卓不发** | `MouthStretchRight` 0.815 |
<!-- END GENERATED SEMANTICS -->

⚠️ **两条判据的坑**（同一个坑踩了三次，别再踩）：
JawRight 在 114 帧时 range 只有 0.043（判 WEAK），到 120 帧变成 **0.600**；
MouthRight 同样 0.020 → **0.480**；TongueOut 更早还被判过"不给值"，实际能到 **1.000**。
⇒ **看到 WEAK / NEVER-MOVES 要连着"我做了那个动作没有、有没有配对做、采了多少帧"一起看。**
### 0.3 `head*` 那 6 条到底是什么（安卓独有）

用户的问题：**它是"位移"还是"旋转"？** 用安卓自己的 223 帧数据判：

**① 跟旋转走，不跟位移走** —— 单变量回归的 R²（取值域 0..1 的 `head*` 当因变量）：

| head 线 | 最强旋转项 | R² | 最强位移项 | R² |
|---|---|---|---|---|
| `headUp` | `Rotation_y` | **0.601** | `Position_y` | 0.218 |
| `headLeft` | `Rotation_x` | **0.512** | `Position_x` | **0.003** |
| `headRight` | `Rotation_x` | **0.645** | `Position_z` | 0.054 |
| `headRollLeft` | `Rotation_z` | **0.767** | `Position_x` | 0.037 |
| `headRollRight` | `Rotation_z` | **0.486** | `Position_y` | 0.012 |
| `headDown` | `Rotation_y` | 0.059 | `Position_z` | 0.001 |

最硬的是 `headLeft` vs `Position_x`：**R² = 0.003**（基本零），而 vs `Rotation_x` 是 0.512。
六条里五条"旋转"都比"位移"好一个量级。⇒ **是旋转族。**

（`headUp` 控制住旋转后 `Position_y` 还残留 −0.42~−0.51 —— 那不是位移，是**低头/抬头时
脸相对相机原点会前后移**的几何耦合，加上两个通道各自的延迟/噪声。）

**② 但"是旋转"不等于"能从 `Rotation_*` 换算出来"** —— 拟合不上：

```
headLeft  ≈ clamp01(a·( Rotation_x - d))  最佳 a=0.08  d=5 → 平均误差 0.119
headRight ≈ clamp01(a·(-Rotation_x - d))  最佳 a=0.10  d=5 → 平均误差 0.058
headUp    ≈ clamp01(a·( Rotation_y - d))  最佳 a=0.005 d=5 → 平均误差 0.253
headDown  ≈ ...                                          → 平均误差 0.866  ← 满量程 1.0
headRollLeft / headRollRight                             → 0.047 / 0.112
```

`headDown` 的误差 0.87 等于完全没用。而且**量纲根本不同**：`head*` 是 **0..1**、
`Rotation_*` 是**度**（±70 这种量级）。

**③ 能力上没有差别**（这一条要说清，我先前表述错过一次）
`Rotation_x/y/z`（旋转）与 `Position_x/y/z`（位移）**两台都发**，所以**表达头姿/头位两边都够**。
`head*` 只是**安卓多给的一份**：旋转系的 0..1 归一化、按左右/上下/倾拆成六条单边通道。
⇒ 苹果要头姿就用 `Rotation_x/y/z` 的**原始度数**；要 `head*` 那种 0..1 形式，就**自己在中间层写表达式**，
而标定系数**只能拿那台设备自己采的数据定**（跨设备抄会错，理由就是上面那张表）。
---

## 1. 安卓 VTS（`androidVTS`）

**怎么采的**：Warudo 里连 VTS 安卓手机（"3rd Party PC Clients"），参数层挂
`ho-debug-androidVTS.hoface.json`（纯直通参考预设）。节点在"新鲜 + 有脸"时自动 dump，
两份值（原始线名 / 出口参数）**取自同一次求值**，逐帧对齐。

**采到多少**：**223 帧**（人肉把每条都试过一轮之后）。最终分档见 **§0.1**。

⚠️ 下面全表里的 `headUp` / `headDown` / `headLeft` / `headRight` / `headRollLeft` /
`headRollRight` **是安卓实测真的收到的线**（原始段 65 条里就有它们）—— 和 §0.1 说的
"苹果那 6 条 `head*` 不存在"**不矛盾**：安卓发、苹果不发，两台本来就不是一套线名。

**值直通**：同一次求值的两份快照里，可比的对 10522 条、**differs 148**，而那 148 条只有两类：

* **`Timestamp`** —— 绝对值 ~1.79e12 被 `-180..180` 曲线**夹到 180**。这个数超过 float 精度
  （约 7 位有效数字），本来就只能看大概，**不参与任何换算**。
* **`EyeBlinkLeft`** —— 差 0.002 到 0.083，**且只在眨眼上升沿差得多**（变化越快差越大）。
  两份快照在同一次求值里，但隔了极小的读取窗口；`eyeBlink` 是变化最快的一条（range 0.80），
  所以只有它看得出这个时间差。**这是取数时机，不是变换。**

### 1.1 关键发现：**每条通道有各自的"激活力道"**

31 帧那一版把好几条判成 `NEVER-MOVES`，**那是采样太少 + 动作不够用力**，不是设备不发。
实测（人肉确认）：

* **`mouthLeft` / `mouthRight`**：**通道是通的**，但**必须用力拉才给值** ——
  轻轻拉时连着 12 份 dump 全是 0.000；用力拉之后 `mouthLeft` 到 **0.866**、`mouthRight` 到 **0.197**。
* **左右拉嘴这件事，设备主要报在 `jawLeft` / `jawRight` 上**（轻轻动就有 0.02–0.19），
  而不是 `mouthLeft` / `mouthRight` —— VTS 那套名字里这是两条不同的通道。
* **`mouthFrown_L/R`**：31 帧时 0.0006（判 `NEVER-MOVES`），用力之后 range **0.82**。

#### ⚠️ 另一类：**要配合别的动作才触发**（苹果实测）

苹果的 `JawRight` 一度被判成 `WEAK`（120 帧里 84% 的时间为 0），可它的 range 其实有
**0.600** —— 不是信号弱，是**触发条件**：

* **`JawRight` 要"张嘴 + 下颌往右偏"才明显**；闭嘴状态下几乎不给值。
  （数据也吻合：它贴地的那些帧，`JawOpen` 也基本是 0。）
* 所以下颌系（`JawOpen` / `JawLeft` / `JawRight` / `JawForward`）要**张着嘴做**。

⇒ **"哪几条在动"这句结论依赖你有没有做那个动作、以及有没有配对做。**
看下面那张表时要连着这一节一起看。

### 1.2 站得住的结论

* **`EyeLeft_z` / `EyeRight_z` 恒为 0**（223 帧、100%、range 0）—— 眼球那个分量**不发值**。
  （VTS 协议里眼是 `Vector3`，z 分量本来就没语义。**对照**：苹果那台会填，见 §2.2。）
* **`headDown`**：31 帧时是 0，223 帧里有 **range 0.081**（99% 的帧仍为 0）—— 能给值但极少。
* **`Hotkey` 恒为 −1**（协议里 −1 = 没按过，见 `PARAMETER_STANDARDS.md` §1.8）—— 符合预期。
* **`FaceFound` 恒为 1** —— 这是**筛选的后果**（只统计有脸的帧），不是设备行为。
* **`Timestamp` 前 3 位之外不变**（`1790349000000` → `1790352000000`，只有"十万"位在动）——
  超过 float 精度，**别拿它当时间用**。
* **`noseSneer_L` / `noseSneer_R` 常态 0.005**（range 0.076 但 90%+ 帧贴地）—— 噪声底。
  `browInnerUp_L/R` 常态 0.0176 ± 0.005。
* **两套拼写同时在发，只有一套是活值**：`EyeBlinkLeft`（range 0.80）是真眨眼，
  `eyeBlink_L`（range 1.00）也在动但小一个量级。选哪套是个真实的选择。
* `headLeft` / `headRight` / `headRollLeft` / `headRollRight` **四个都在动**（range 0.16–0.71）—— 注意那 6 个 `head*` **只有安卓发**，苹果没有（§0.1）。

### 1.3 全表（工具生成，不要手改）
<!-- BEGIN GENERATED STATS androidVTS -->
| wire | n | mean | std | min | max | range | near-zero % | status |
|---|---|---|---|---|---|---|---|---|
| `Hotkey` | 223 | -1.0000 | 0.0000 | -1.0000 | -1.0000 | 0.0000 | 0.0 | CONSTANT-NONZERO |
| `FaceFound` | 223 | 1.0000 | 0.0000 | 1.0000 | 1.0000 | 0.0000 | 0.0 | CONSTANT-NONZERO |
| `Timestamp` | 223 | 1790350757847.5300 | 1071869.2792 | 1790349000000.0000 | 1790352000000.0000 | 3000000.0000 | 0.0 | MOVES |
| `Rotation_x` | 223 | 1.2882 | 8.4287 | -70.6621 | 31.8669 | 102.5290 | 0.0 | MOVES |
| `Rotation_y` | 223 | -23.4792 | 13.0249 | -45.7712 | 21.6642 | 67.4354 | 0.0 | MOVES |
| `EyeLeft_y` | 223 | 0.3895 | 4.0150 | -25.5026 | 24.8870 | 50.3896 | 0.0 | MOVES |
| `EyeRight_y` | 223 | 0.2099 | 3.9766 | -24.7315 | 25.2126 | 49.9441 | 0.0 | MOVES |
| `Position_x` | 223 | 0.1360 | 6.0321 | -20.0000 | 16.7104 | 36.7104 | 0.0 | MOVES |
| `Rotation_z` | 223 | -1.1417 | 4.1237 | -15.7821 | 17.6481 | 33.4302 | 0.0 | MOVES |
| `EyeLeft_x` | 223 | -0.7075 | 2.4819 | -17.4413 | 10.2887 | 27.7300 | 0.4 | MOVES |
| `EyeRight_x` | 223 | -0.7067 | 2.4806 | -17.4359 | 10.2918 | 27.7277 | 0.0 | MOVES |
| `Position_z` | 223 | -3.8719 | 2.7803 | -15.0000 | 0.5779 | 15.5779 | 0.0 | MOVES |
| `Position_y` | 223 | -12.5034 | 3.6494 | -20.0000 | -5.9964 | 14.0036 | 0.0 | MOVES |
| `eyeBlink_R` | 223 | 0.0689 | 0.1272 | 0.0009 | 1.0000 | 0.9991 | 0.0 | MOVES |
| `eyeBlink_L` | 223 | 0.0670 | 0.1234 | 0.0009 | 1.0000 | 0.9991 | 0.0 | MOVES |
| `mouthPucker` | 223 | 0.1154 | 0.1412 | 0.0078 | 0.9557 | 0.9479 | 0.0 | MOVES |
| `mouthSmile_L` | 223 | 0.1527 | 0.0974 | 0.0370 | 0.9480 | 0.9110 | 0.0 | MOVES |
| `eyeLookIn_R` | 223 | 0.0679 | 0.1080 | 0.0080 | 0.8801 | 0.8721 | 0.0 | MOVES |
| `eyeLookOut_L` | 223 | 0.0706 | 0.1090 | 0.0093 | 0.8778 | 0.8685 | 0.0 | MOVES |
| `mouthLeft` | 223 | 0.0079 | 0.0669 | 0.0000 | 0.8656 | 0.8656 | 83.9 | MOVES |
| `mouthSmile_R` | 223 | 0.1639 | 0.1261 | 0.0542 | 0.8959 | 0.8417 | 0.0 | MOVES |
| `eyeLookIn_L` | 223 | 0.0576 | 0.0762 | 0.0250 | 0.8647 | 0.8397 | 0.0 | MOVES |
| `eyeSquint_R` | 223 | 0.2880 | 0.2149 | 0.0190 | 0.8473 | 0.8283 | 0.0 | MOVES |
| `eyeLookOut_R` | 223 | 0.0609 | 0.0754 | 0.0149 | 0.8380 | 0.8231 | 0.0 | MOVES |
| `mouthFrown_L` | 223 | 0.0043 | 0.0549 | 0.0000 | 0.8196 | 0.8196 | 91.0 | MOVES |
| `EyeBlinkLeft` | 223 | 0.3884 | 0.2280 | 0.1958 | 1.0000 | 0.8042 | 0.0 | MOVES |
| `EyeBlinkRight` | 223 | 0.3927 | 0.2276 | 0.1995 | 1.0000 | 0.8005 | 0.0 | MOVES |
| `mouthFrown_R` | 223 | 0.0042 | 0.0534 | 0.0000 | 0.7974 | 0.7974 | 91.0 | MOVES |
| `eyeSquint_L` | 223 | 0.2814 | 0.2051 | 0.0190 | 0.8066 | 0.7876 | 0.0 | MOVES |
| `headRight` | 223 | 0.0176 | 0.0594 | 0.0000 | 0.7066 | 0.7066 | 59.2 | MOVES |
| `jawOpen` | 223 | 0.0784 | 0.1320 | 0.0000 | 0.7023 | 0.7023 | 20.2 | MOVES |
| `mouthFunnel` | 223 | 0.0513 | 0.0900 | 0.0094 | 0.6754 | 0.6660 | 0.0 | MOVES |
| `browDown_R` | 223 | 0.0971 | 0.1283 | 0.0093 | 0.6193 | 0.6100 | 0.0 | MOVES |
| `browDown_L` | 223 | 0.0978 | 0.1278 | 0.0094 | 0.6193 | 0.6099 | 0.0 | MOVES |
| `eyeLookUp_L` | 223 | 0.0759 | 0.0717 | 0.0211 | 0.6168 | 0.5957 | 0.0 | MOVES |
| `eyeLookUp_R` | 223 | 0.0759 | 0.0717 | 0.0212 | 0.6168 | 0.5956 | 0.0 | MOVES |
| `headUp` | 223 | 0.2401 | 0.0843 | 0.0000 | 0.3946 | 0.3946 | 1.3 | MOVES |
| `jawLeft` | 223 | 0.0331 | 0.0374 | 0.0088 | 0.3947 | 0.3859 | 0.0 | MOVES |
| `eyeLookDown_L` | 223 | 0.0523 | 0.0327 | 0.0269 | 0.3739 | 0.3470 | 0.0 | MOVES |
| `eyeLookDown_R` | 223 | 0.0523 | 0.0327 | 0.0280 | 0.3740 | 0.3460 | 0.0 | MOVES |
| `tongueOut` | 223 | 0.0350 | 0.0486 | 0.0071 | 0.3374 | 0.3303 | 0.0 | MOVES |
| `browOuterUp_L` | 223 | 0.0410 | 0.0361 | 0.0191 | 0.3392 | 0.3201 | 0.0 | MOVES |
| `headLeft` | 223 | 0.0282 | 0.0514 | 0.0000 | 0.3187 | 0.3187 | 41.7 | MOVES |
| `jawRight` | 223 | 0.0195 | 0.0230 | 0.0026 | 0.2190 | 0.2164 | 0.0 | MOVES |
| `mouthLowerDown_L` | 223 | 0.0466 | 0.0342 | 0.0128 | 0.2234 | 0.2106 | 0.0 | MOVES |
| `mouthLowerDown_R` | 223 | 0.0471 | 0.0342 | 0.0130 | 0.2235 | 0.2105 | 0.0 | MOVES |
| `browOuterUp_R` | 223 | 0.0202 | 0.0338 | 0.0000 | 0.2027 | 0.2027 | 43.9 | MOVES |
| `mouthRight` | 223 | 0.0014 | 0.0135 | 0.0000 | 0.1973 | 0.1973 | 94.6 | MOVES |
| `headRollRight` | 223 | 0.0086 | 0.0206 | 0.0000 | 0.1765 | 0.1765 | 58.3 | MOVES |
| `mouthUpperUp_R` | 223 | 0.0027 | 0.0142 | 0.0000 | 0.1684 | 0.1684 | 72.6 | MOVES |
| `mouthRollLower` | 223 | 0.0209 | 0.0168 | 0.0061 | 0.1706 | 0.1645 | 0.0 | MOVES |
| `mouthUpperUp_L` | 223 | 0.0027 | 0.0140 | 0.0000 | 0.1630 | 0.1630 | 73.1 | MOVES |
| `mouthRollUpper` | 223 | 0.0244 | 0.0176 | 0.0078 | 0.1664 | 0.1586 | 0.0 | MOVES |
| `headRollLeft` | 223 | 0.0196 | 0.0307 | 0.0000 | 0.1578 | 0.1578 | 42.6 | MOVES |
| `eyeWide_L` | 223 | 0.0269 | 0.0132 | 0.0074 | 0.1306 | 0.1232 | 0.0 | MOVES |
| `eyeWide_R` | 223 | 0.0256 | 0.0109 | 0.0114 | 0.1281 | 0.1167 | 0.0 | MOVES |
| `cheekPuff` | 223 | 0.0117 | 0.0125 | 0.0065 | 0.1151 | 0.1086 | 0.0 | MOVES |
| `headDown` | 223 | 0.0006 | 0.0058 | 0.0000 | 0.0814 | 0.0814 | 98.7 | MOVES |
| `noseSneer_R` | 223 | 0.0049 | 0.0092 | 0.0014 | 0.0776 | 0.0762 | 0.0 | MOVES |
| `noseSneer_L` | 223 | 0.0049 | 0.0092 | 0.0014 | 0.0776 | 0.0762 | 0.0 | MOVES |
| `browInnerUp_R` | 223 | 0.0178 | 0.0040 | 0.0085 | 0.0618 | 0.0533 | 0.0 | MOVES |
| `browInnerUp_L` | 223 | 0.0177 | 0.0040 | 0.0085 | 0.0616 | 0.0531 | 0.0 | MOVES |
| `EyeRight_z` | 223 | 0.0000 | 0.0000 | 0.0000 | 0.0000 | 0.0000 | 100.0 | NEVER-MOVES |
| `EyeLeft_z` | 223 | 0.0000 | 0.0000 | 0.0000 | 0.0000 | 0.0000 | 100.0 | NEVER-MOVES |
| `mouthShrugUpper` | 223 | 0.0059 | 0.0032 | 0.0049 | 0.0443 | 0.0394 | 0.0 | WEAK |
<!-- END GENERATED STATS androidVTS -->
---

## 2. iPhone VTS（`iphoneVTS`）

**怎么采的**：和 §1 一样，参数层挂 `ho-debug-iphoneVTS.hoface.json`。
**这份预设现在是 67 / 67**（原先列了 73 行、含 6 行死行，已按 §2.1 的实测裁掉）。

**采到多少**：**120 帧可用**（223 帧是掉脸帧 —— 这段捕捉里抬手/低头比较多）。

**结论**：

| status | 条数 |
|---|---|
| `MOVES` | **64** |
| `WEAK` | **0** |
| `CONSTANT-NONZERO` | 3（`FaceFound` = 1、`Hotkey` = −1、`Timestamp`） |
| `NEVER-MOVES` | **0** |

**值直通**：7464 对可比、differs **66**，而那 66 条**全部来自第一份 dump**
（第一个采样点没有"前一份"可比），内容就是 `Timestamp` 那一类。**后面每一帧都相等。**

### 2.1 那份预设已从 73 行裁到 **67 行**

**原始段实测 67 条**（52 形状 + 13 标量 + `Hotkey` + `Timestamp`），
**没有** `headUp/Down/Left/Right/RollLeft/RollRight`（见 §0.1 那条订正：那 6 条两台都不存在，
是配置里的残留行、输入行永远没数据 ⇒ 出口恒 0）。
⇒ 已按实测重新生成，**现在是 67 / 67**，那 6 行死行删掉了。

### 2.2 和安卓的差异：不是"数值不同"，是"**哪些线有信号**不同"

> ⚠️ **判据的局限，先说清**：下面这张表里的 `WEAK` / `NEVER-MOVES` **只是"我这批采样里没见到它动"**，
> 不等于"它不会动"。实测就吃过两次：
> · `JawRight` 在 **114 帧**时 range 只有 0.043（判 `WEAK`），到 **120 帧**变成 **0.600**；
> · `MouthRight` 同样从 0.020 变成 **0.480**；
> · `TongueOut` 更早还被判过"不给值"，实际 range 能到 **1.000**。
> ⇒ **看 `WEAK` / `NEVER-MOVES` 时要连着"我做了那个动作没有、采了多少帧"一起看。**

**① 眼球 z 分量：苹果会填，安卓不填**

| | 苹果 | 安卓 |
|---|---|---|
| `EyeLeft_z` | mean **0.217** · range **3.66**（**在动**） | mean 0 · range 0（**恒 0**） |
| `EyeRight_z` | mean **1.092** · range **3.90**（**在动**） | mean 0 · range 0（**恒 0**） |

⇒ 安卓那两条"不给值"**不是协议没这个分量，是安卓那台不填**。

**② 安卓"根本不发"的那 12 条，苹果全部有信号，有几个还很强**

| wire | 苹果 range |
|---|---|
| `MouthStretchLeft` / `Right` | **0.713** / 0.695 |
| `MouthShrugLower` | **0.638** |
| `MouthLowerDownLeft` / `Right` | 0.587 / 0.605 |
| `JawForward` | 0.276 |
| `MouthPressLeft` / `Right` | 0.414 / 0.421 |
| `MouthDimpleLeft` / `Right` | 0.380 / 0.365 |
| `CheekSquintLeft` / `Right` | 0.338 / 0.315 |
| `MouthClose` | 0.093 |

苹果这边 `MOVES` 里有 **61** 条，比安卓多出来的正是这些。**这就是"一份配置通吃两台会静默失效"的实证**：
那 12 行在安卓上永远没数据，安卓那 6 条 `Eye*z`/`mouthShrugUpper` 之外也没法跟苹果共用一份。

**③ 方向差得远**：苹果 `Rotation_y` mean **+23.9**、`EyeLeft_x` mean **+12.0**；
安卓 `Rotation_y` mean **−23.5**、`EyeLeft_x` mean **−0.71**。**符号和零点都不一样**，
所以连"标定"都不能跨设备抄。

**④ `TongueOut` 是动的 —— 只是要"伸到位"**

早先一版把苹果的 `TongueOut` 记成"不给值"，**是错的**（那批 108 帧里没采到那个动作）。
用户实测确认它一直能动能，补采之后：

| | 帧数 | mean | range | near-zero | 最大 |
|---|---|---|---|---|---|
| 苹果 | 114 | 0.0439 | **1.000** | 94.7% | **1.000** |
| 安卓 | 223 | 0.0350 | **0.330** | — | 0.337 |

⇒ **两台都发这条、都能到很大**，但都**大部分时间贴地**（要明显伸舌头才起）。
这类"高阈值 + 高饱和"的线，**必须专门做那个动作才会出现在采样里** ——
自动 dump 每秒一份、一轮 6 份，一个短动作很容易整段跳过。


### 2.3 全表（67 条，工具生成）

<!-- BEGIN GENERATED STATS iphoneVTS -->
| wire | n | mean | std | min | max | range | near-zero % | status |
|---|---|---|---|---|---|---|---|---|
| `Hotkey` | 203 | -1.0000 | 0.0000 | -1.0000 | -1.0000 | 0.0000 | 0.0 | CONSTANT-NONZERO |
| `FaceFound` | 203 | 1.0000 | 0.0000 | 1.0000 | 1.0000 | 0.0000 | 0.0 | CONSTANT-NONZERO |
| `Timestamp` | 203 | 1790353364532.0200 | 482146.3522 | 1790353000000.0000 | 1790354000000.0000 | 1000000.0000 | 0.0 | MOVES |
| `Rotation_y` | 203 | 19.8148 | 10.8402 | -12.2160 | 35.8134 | 48.0294 | 0.0 | MOVES |
| `EyeLeft_y` | 203 | 0.7222 | 3.9319 | -18.0250 | 28.8086 | 46.8336 | 0.0 | MOVES |
| `EyeRight_y` | 203 | 5.3375 | 3.8938 | -13.6361 | 31.9154 | 45.5515 | 0.0 | MOVES |
| `Rotation_x` | 203 | -2.2798 | 4.2309 | -18.8103 | 20.3082 | 39.1185 | 0.0 | MOVES |
| `EyeLeft_x` | 203 | 12.5472 | 5.8890 | -9.2143 | 21.7084 | 30.9227 | 0.0 | MOVES |
| `EyeRight_x` | 203 | 12.4473 | 5.8416 | -9.1777 | 21.4694 | 30.6471 | 0.0 | MOVES |
| `Rotation_z` | 203 | 1.7782 | 3.4447 | -13.7145 | 12.6969 | 26.4114 | 0.0 | MOVES |
| `Position_x` | 203 | 2.5690 | 2.6849 | -5.1365 | 10.6234 | 15.7599 | 0.0 | MOVES |
| `EyeRight_z` | 203 | 1.0344 | 0.7973 | -3.1222 | 2.9883 | 6.1105 | 0.5 | MOVES |
| `Position_y` | 203 | -3.6394 | 1.2222 | -6.4799 | -0.5312 | 5.9487 | 0.0 | MOVES |
| `EyeLeft_z` | 203 | 0.1189 | 0.6240 | -4.0640 | 1.8065 | 5.8705 | 0.0 | MOVES |
| `Position_z` | 203 | -3.1264 | 0.6606 | -5.6101 | -0.8569 | 4.7532 | 0.0 | MOVES |
| `TongueOut` | 203 | 0.0246 | 0.1550 | 0.0000 | 1.0000 | 1.0000 | 97.0 | MOVES |
| `MouthLeft` | 203 | 0.0259 | 0.1431 | 0.0000 | 0.9645 | 0.9645 | 58.1 | MOVES |
| `MouthRight` | 203 | 0.0233 | 0.1117 | 0.0000 | 0.9555 | 0.9555 | 47.8 | MOVES |
| `EyeLookInRight` | 203 | 0.1545 | 0.0936 | 0.0000 | 0.9106 | 0.9106 | 3.4 | MOVES |
| `MouthPucker` | 203 | 0.1992 | 0.1208 | 0.0429 | 0.9279 | 0.8850 | 0.0 | MOVES |
| `EyeLookOutLeft` | 203 | 0.0374 | 0.0860 | 0.0000 | 0.8218 | 0.8218 | 36.0 | MOVES |
| `MouthStretchRight` | 203 | 0.1292 | 0.1754 | 0.0315 | 0.8460 | 0.8145 | 0.0 | MOVES |
| `JawOpen` | 203 | 0.0833 | 0.1629 | 0.0016 | 0.8046 | 0.8030 | 0.0 | MOVES |
| `MouthSmileRight` | 203 | 0.0739 | 0.1162 | 0.0000 | 0.7409 | 0.7409 | 18.7 | MOVES |
| `MouthSmileLeft` | 203 | 0.0618 | 0.1183 | 0.0000 | 0.7387 | 0.7387 | 30.5 | MOVES |
| `MouthLowerDownLeft` | 203 | 0.0838 | 0.1447 | 0.0045 | 0.7288 | 0.7243 | 0.0 | MOVES |
| `MouthStretchLeft` | 203 | 0.1313 | 0.1488 | 0.0327 | 0.7460 | 0.7133 | 0.0 | MOVES |
| `MouthLowerDownRight` | 203 | 0.0890 | 0.1586 | 0.0018 | 0.7059 | 0.7041 | 0.0 | MOVES |
| `BrowDownRight` | 203 | 0.1802 | 0.1278 | 0.0000 | 0.6567 | 0.6567 | 6.4 | MOVES |
| `BrowDownLeft` | 203 | 0.1822 | 0.1287 | 0.0000 | 0.6567 | 0.6567 | 6.4 | MOVES |
| `MouthShrugLower` | 203 | 0.2427 | 0.1172 | 0.0433 | 0.6817 | 0.6384 | 0.0 | MOVES |
| `JawRight` | 203 | 0.0189 | 0.0986 | 0.0000 | 0.6002 | 0.6002 | 89.7 | MOVES |
| `EyeLookDownLeft` | 203 | 0.3297 | 0.1346 | 0.0000 | 0.5590 | 0.5590 | 6.9 | MOVES |
| `EyeLookDownRight` | 203 | 0.3284 | 0.1341 | 0.0000 | 0.5561 | 0.5561 | 6.9 | MOVES |
| `MouthShrugUpper` | 203 | 0.1835 | 0.0917 | 0.0431 | 0.5988 | 0.5557 | 0.0 | MOVES |
| `CheekPuff` | 203 | 0.0572 | 0.0837 | 0.0034 | 0.5172 | 0.5138 | 0.0 | MOVES |
| `EyeBlinkLeft` | 203 | 0.1068 | 0.0812 | 0.0000 | 0.5133 | 0.5133 | 14.3 | MOVES |
| `EyeBlinkRight` | 203 | 0.1059 | 0.0799 | 0.0000 | 0.5113 | 0.5113 | 14.3 | MOVES |
| `EyeLookInLeft` | 203 | 0.0171 | 0.0603 | 0.0000 | 0.5025 | 0.5025 | 65.0 | MOVES |
| `MouthRollLower` | 203 | 0.0587 | 0.0513 | 0.0114 | 0.4995 | 0.4881 | 0.0 | MOVES |
| `MouthPressRight` | 203 | 0.1268 | 0.0713 | 0.0375 | 0.4580 | 0.4205 | 0.0 | MOVES |
| `JawForward` | 203 | 0.0376 | 0.0662 | 0.0007 | 0.4208 | 0.4201 | 0.0 | MOVES |
| `NoseSneerLeft` | 203 | 0.1694 | 0.0689 | 0.0886 | 0.5030 | 0.4144 | 0.0 | MOVES |
| `MouthPressLeft` | 203 | 0.1205 | 0.0693 | 0.0339 | 0.4475 | 0.4136 | 0.0 | MOVES |
| `MouthUpperUpRight` | 203 | 0.0646 | 0.0845 | 0.0173 | 0.4032 | 0.3859 | 0.0 | MOVES |
| `MouthUpperUpLeft` | 203 | 0.0615 | 0.0760 | 0.0183 | 0.4023 | 0.3840 | 0.0 | MOVES |
| `MouthDimpleLeft` | 203 | 0.0571 | 0.0487 | 0.0096 | 0.3915 | 0.3819 | 0.0 | MOVES |
| `EyeLookOutRight` | 203 | 0.0056 | 0.0398 | 0.0000 | 0.3795 | 0.3795 | 96.6 | MOVES |
| `NoseSneerRight` | 203 | 0.1628 | 0.0628 | 0.0845 | 0.4535 | 0.3690 | 0.0 | MOVES |
| `MouthDimpleRight` | 203 | 0.0577 | 0.0486 | 0.0096 | 0.3764 | 0.3668 | 0.0 | MOVES |
| `JawLeft` | 203 | 0.0193 | 0.0266 | 0.0000 | 0.3436 | 0.3436 | 11.3 | MOVES |
| `CheekSquintLeft` | 203 | 0.0798 | 0.0575 | 0.0267 | 0.3643 | 0.3376 | 0.0 | MOVES |
| `EyeLookUpLeft` | 203 | 0.0087 | 0.0389 | 0.0000 | 0.3320 | 0.3320 | 93.6 | MOVES |
| `EyeLookUpRight` | 203 | 0.0086 | 0.0388 | 0.0000 | 0.3314 | 0.3314 | 93.6 | MOVES |
| `MouthFunnel` | 203 | 0.0379 | 0.0462 | 0.0004 | 0.3298 | 0.3294 | 0.5 | MOVES |
| `EyeWideLeft` | 203 | 0.0249 | 0.0580 | 0.0000 | 0.3281 | 0.3281 | 74.4 | MOVES |
| `EyeSquintLeft` | 203 | 0.0604 | 0.0493 | 0.0147 | 0.3366 | 0.3219 | 0.0 | MOVES |
| `EyeSquintRight` | 203 | 0.0603 | 0.0490 | 0.0147 | 0.3365 | 0.3218 | 0.0 | MOVES |
| `CheekSquintRight` | 203 | 0.0724 | 0.0406 | 0.0231 | 0.3380 | 0.3149 | 0.0 | MOVES |
| `BrowInnerUp` | 203 | 0.0672 | 0.0473 | 0.0225 | 0.3188 | 0.2963 | 0.0 | MOVES |
| `EyeWideRight` | 203 | 0.0241 | 0.0562 | 0.0000 | 0.2793 | 0.2793 | 75.4 | MOVES |
| `MouthRollUpper` | 203 | 0.0173 | 0.0180 | 0.0079 | 0.2505 | 0.2426 | 0.0 | MOVES |
| `MouthFrownLeft` | 203 | 0.0126 | 0.0328 | 0.0000 | 0.2300 | 0.2300 | 70.4 | MOVES |
| `MouthFrownRight` | 203 | 0.0111 | 0.0398 | 0.0000 | 0.2237 | 0.2237 | 81.8 | MOVES |
| `MouthClose` | 203 | 0.0315 | 0.0225 | 0.0062 | 0.1357 | 0.1295 | 0.0 | MOVES |
| `BrowOuterUpLeft` | 203 | 0.0049 | 0.0199 | 0.0000 | 0.1098 | 0.1098 | 93.6 | MOVES |
| `BrowOuterUpRight` | 203 | 0.0040 | 0.0165 | 0.0000 | 0.1036 | 0.1036 | 93.6 | MOVES |
<!-- END GENERATED STATS iphoneVTS -->

---

## 3. 两台设备对比（安卓 VTS ↔ 苹果 VTS）

> 这一节把"两台到底差在哪"收在一处。§2.2 是同一个结论在苹果语境下的展开，两边内容一致。

### 3.1 线名集合：**同一条通道，两台经常叫不同名字**

| | 安卓 | 苹果 |
|---|---|---|
| 实测条数 | **65** | **67** |
| 形状的拼写 | `_L/_R` 后缀 + camelCase（`browDown_L`） | **纯 PascalCase**（`BrowDownLeft`） |
| 标量 | `Rotation_x` / `Position_y` / `EyeLeft_x` / `FaceFound` / `Hotkey` / `Timestamp` | **同名** |
| `head*` 那 6 条 | **有**（`headLeft` 等） | **没有** |

**归并之后**（把 `_L/_R` 折成 `Left/Right`、大小写无关）：**52 组同义通道**，另有：

* **只有安卓有**（9 组）：6 个 `head*` + `browInnerUp` 拆成 `browInnerUp_L` / `_R` 两条（苹果是一条 `BrowInnerUp`）
* **只有苹果有**（12 组）：`cheekSquintLeft/Right`、`jawForward`、`mouthClose`、
  `mouthDimpleLeft/Right`、`mouthPressLeft/Right`、`mouthShrugLower`、`mouthStretchLeft/Right`

### 3.2 数值：**同一条通道，两台的量级与零点也不同**

| 通道 | 安卓 range | 苹果 range | 备注 |
|---|---|---|---|
| `tongueOut` | 0.330 | **1.000** | 苹果能到满 |
| `mouthStretchLeft` / `Right` | — **不发** | 0.713 / 0.815 | |
| `mouthShrugLower` | — **不发** | 0.638 | |
| `jawRight`（张嘴做） | 0.216 | **0.600** | |
| `cheekSquintLeft` / `Right` | — **不发** | 0.338 / 0.315 | |
| `eyeLeft_z` / `eyeRight_z` | **0.000（恒 0）** | **5.871 / 6.111** | 安卓不填这个分量 |
| `mouthShrugUpper` | **0.039（噪声底）** | 0.556 | 安卓基本不动 |

**方向/零点**：苹果 `rotation_y` mean **+19.8**、`eyeLeft_x` mean **+12.5**；
安卓 `rotation_y` mean **−23.5**、`eyeLeft_x` mean **−0.71**。
⇒ **符号和零点都不一样，连"标定"都不能跨设备抄。**

### 3.3 结论：**必须一台设备一份配置**

* 名字不同 ⇒ 用错那份配置时，那一行**永远没数据、且不报错**（静默失效）；
* 名字相同但**某台不发**的（12 组）⇒ 那一格在那台上恒 0；
* 名字相同、都发的，**量级与零点也可能差很远** ⇒ 曲线/换算不能共用。

⚠️ 所以包里是**两份参考预设**（`ho-debug-androidVTS` / `ho-debug-iphoneVTS`），
各自**只列自己做实测发过的线**。往中间层加"真实转化"时，**两边各写一套**。
## 4. 怎么刷新这些表

```powershell
# 一条命令刷新**本文件里全部三张生成表**（§0.2 语义表 + §1.3/§2.3 两张统计表）
& D:\Unity_Fork\HoUnityTools\.warudo-mod-research\.tools\gen-verif-tables.ps1

# 只想看/只要一份设备的统计表时：
& D:\Unity_Fork\HoUnityTools\.warudo-mod-research\.tools\analyze-dump-stats.ps1 `
    -Device androidVTS -Markdown <输出文件>
```

⚠️ 手工改生成表会在下次重跑时被覆盖 —— 要改就改数据源：
数字来自 `Player.log`、ARKit 语义来自 `PARAMETER_STANDARDS.md` §3.1、
"触发情况"与标量语义来自 `.warudo-mod-research/.tools/verification-meanings.txt`。

输出里同时会给一行 `identity : ... comparable with the same frame raw | equal N | differs M` ——
那是"值直通"的核对。它只比**同一次求值里两边都有**的键（出口字典里还可能有设备线名之外的键），
并把前 12 条不同的**逐条打出来**（否则"differs M"完全看不出原因）。
`differs` 不为 0 时要分清是**变换**还是**取数时机**——§1 里那 149 条就是
`Timestamp`（float 精度）与 `EyeBlinkLeft`（上升沿的时间差）两类，都不是变换。

**参考预设**（`ho-debug-*.hoface.json`）就是这些表的 `wire` 列 ——
纯直通、零改名、两层宽曲线，一份设备一份，**别互相套用**。
