# 面捕输入实测记录（真值）

这里放**设备真实发过来的 payload**。为什么单独存一份、而且当成一等公民：

* 配置必须按"设备实际发的键"生成 —— 照命名规则推断会拼出设备根本不发的行，而**那一行不报错、只是永远没数据**。
  安卓那份就栽在这上面：照 iFacialMocap 的 `_L/_R` 规则拼出 `browInnerUp`，设备发的却是 `browInnerUp_L/R`。
* 键名是**契约**，值只是采样。值每帧都在变，所以下面那些数字的用途是"量纲/范围长什么样"，
  **不是拿去当阈值**。
* 配置生成器与守门用例都读这里的 `.keys` 文件（`docs/measurements/<device>.keys`），
  所以"文档"和"生成来源"是同一份东西，不会各写一遍。

怎么取的：Warudo 里「HoFaceVTS接收器」跑起来，把调试输出（`原始值` 那份 `线名 = 值` 的表）整段粘出来。
设备侧是 VTS 的第三方 PC 连接（手机 VTS App → 本机 UDP `49985`）。

---

## 1. 安卓手机上的 VTS（`androidVTS`）

**两次独立 dump，键名完全一致**（65 键）。`.keys` 清单：`androidVTS.keys`。

### 方言（这是它的**实际**形状，不是规则推的）

| 类别 | 实际拼写 | 备注 |
|---|---|---|
| 形状主体 | 小驼峰 + `_L/_R`：`jawOpen` / `mouthSmile_L` / `eyeBlink_L` | — |
| **内眉 / 外眉** | `browInnerUp_L` / `browInnerUp_R`、`browOuterUp_L` / `browOuterUp_R` | ⚠️ **带后缀**，而规范名 `browInnerUp` 只有一个 ⇒ 左右并到它 |
| 不成对通道 | `jawLeft` / `jawRight` / `mouthLeft` / `mouthRight` | **不带后缀**（规范名也不带） |
| 眨眼 | **两种都发**：`eyeBlink_L/R` **和** `EyeBlinkLeft/Right` | 同一条通道；"最早只有头系+眨眼在动"就是因为只有这对有数据 |
| 标量（16） | VTS 固定名：`Rotation_x` / `Position_x` / `EyeLeft_x` / `FaceFound` / `Hotkey` / `Timestamp` | 与 iPhone **同名同量纲** |
| **另一条通道（6）** | `headUp` / `headDown` / `headLeft` / `headRight` / `headRollLeft` / `headRollRight` | ⚠️ 与 `Rotation_*` **不是一回事**，语义未确认 ⇒ **本次不映射** |

### 一次完整 dump（2026-09-25，第二次；值仅作量纲参考）

```
EyeBlinkLeft = 0.233      EyeBlinkRight = 0.239
EyeLeft_x = 0.006   EyeLeft_y = -0.046   EyeLeft_z = 0.000      <- 度
EyeRight_x = 0.005  EyeRight_y = -0.026  EyeRight_z = 0.000
FaceFound = 1.000   Hotkey = -1.000
Position_x = -1.925  Position_y = -14.499  Position_z = -3.567  <- 单位未标定
Rotation_x = 4.240   Rotation_y = -29.975  Rotation_z = -0.167   <- 度
Timestamp = 1790345000000.000
browDown_L = 0.024          browDown_R = 0.024
browInnerUp_L = 0.017       browInnerUp_R = 0.018
browOuterUp_L = 0.030       browOuterUp_R = 0.000
cheekPuff = 0.008
eyeBlink_L = 0.002          eyeBlink_R = 0.002
eyeLookDown_L = 0.043       eyeLookDown_R = 0.043
eyeLookIn_L = 0.040         eyeLookIn_R = 0.037
eyeLookOut_L = 0.038        eyeLookOut_R = 0.038
eyeLookUp_L = 0.042         eyeLookUp_R = 0.043
eyeSquint_L = 0.107         eyeSquint_R = 0.107
eyeWide_L = 0.024           eyeWide_R = 0.023
headDown = 0.000   headLeft = 0.042   headRight = 0.000
headRollLeft = 0.002  headRollRight = 0.000  headUp = 0.258
jawLeft = 0.014   jawOpen = 0.043   jawRight = 0.009
mouthFrown_L = 0.000        mouthFrown_R = 0.000
mouthFunnel = 0.420
mouthLeft = 0.000
mouthLowerDown_L = 0.033    mouthLowerDown_R = 0.033
mouthPucker = 0.328
mouthRight = 0.000
mouthRollLower = 0.010      mouthRollUpper = 0.012
mouthShrugUpper = 0.005
mouthSmile_L = 0.086        mouthSmile_R = 0.082
mouthUpperUp_L = 0.000      mouthUpperUp_R = 0.000
noseSneer_L = 0.003         noseSneer_R = 0.003
tongueOut = 0.120
```

### ⚠️ 这台设备**不发**的 12 个规范名（所以那 12 格在安卓上永远没有源、恒 0）

```
jawForward              mouthClose
mouthDimpleLeft         mouthDimpleRight
mouthStretchLeft        mouthStretchRight
mouthShrugLower
mouthPressLeft          mouthPressRight
cheekSquintLeft         cheekSquintRight
```

**这不是配置写漏了，是设备就不发。** 要那几个形状就换 iPhone 测（它 52 个齐全）。
第一版配置曾给这 12 个各写了一行 —— 那些行**不报错、只是永远拿不到数据**，属于纯噪音，现在已经没有它们。

### 第一次 dump（同日，另一次）记下的形状值（量纲参考）

```
jawOpen = 0.069   mouthSmile_L = 0.164   eyeBlink_L = 0.166   cheekPuff = 0.003
eyeSquint_L = 0.116   eyeLookDown_L = 0.044   noseSneer_L = 0.008   tongueOut = 0.000
```

⇒ **形状值本来就是 0..1**，所以形状行**不乘 0.01**（内置默认表里那套 `* 0.01` 是给 iFacialMocap **App** 的 0..100 写的）。

---

## 2. iPhone 上的 VTS（`iphoneVTS`）

**一次完整 dump，67 键。** `.keys` 清单：`iphoneVTS.keys`。

### 方言

| 类别 | 实际拼写 |
|---|---|
| 形状（**52 个，一个不缺、0 个对不上**） | **干净的 VTS PascalCase**：`JawOpen` / `MouthSmileLeft` / `BrowInnerUp`（内眉**只有一条线**） |
| 标量（16） | 与安卓**同名同量纲** |

⇒ 这台是**纯 VTS 方言**，没有安卓那套混合/特例。核对方式见
`.warudo-mod-research/.tools/check-device-keys.ps1`（拿键清单比规范名，报"对不上"与"没发"）。

### 一次完整 dump（2026-09-25；值仅作量纲参考）

```
BrowDownLeft = 0.186     BrowDownRight = 0.186
BrowInnerUp = 0.082
BrowOuterUpLeft = 0.000  BrowOuterUpRight = 0.000
CheekPuff = 0.965
CheekSquintLeft = 0.080  CheekSquintRight = 0.080
EyeBlinkLeft = 0.233     EyeBlinkRight = 0.233
EyeLeft_x = 17.567  EyeLeft_y = -1.415  EyeLeft_z = -0.386    <- 度（**与安卓同一个坐标系**）
EyeRight_x = 17.518 EyeRight_y = 3.482  EyeRight_z = 0.948
EyeLookDownLeft = 0.452  EyeLookDownRight = 0.452
EyeLookInLeft = 0.039    EyeLookInRight = 0.096
EyeLookOutLeft = 0.000   EyeLookOutRight = 0.000
EyeLookUpLeft = 0.000    EyeLookUpRight = 0.000
EyeSquintLeft = 0.018    EyeSquintRight = 0.018
EyeWideLeft = 0.000      EyeWideRight = 0.000
FaceFound = 1.000  Hotkey = -1.000
JawForward = 0.018  JawLeft = 0.035  JawOpen = 0.032  JawRight = 0.000
MouthClose = 0.031
MouthDimpleLeft = 0.037  MouthDimpleRight = 0.039
MouthFrownLeft = 0.001   MouthFrownRight = 0.000
MouthFunnel = 0.029
MouthLeft = 0.027
MouthLowerDownLeft = 0.044  MouthLowerDownRight = 0.045
MouthPressLeft = 0.338   MouthPressRight = 0.330
MouthPucker = 0.432
MouthRight = 0.000
MouthRollLower = 0.158   MouthRollUpper = 0.027
MouthShrugLower = 0.328  MouthShrugUpper = 0.244
MouthSmileLeft = 0.000   MouthSmileRight = 0.029
MouthStretchLeft = 0.093 MouthStretchRight = 0.081
MouthUpperUpLeft = 0.070 MouthUpperUpRight = 0.066
NoseSneerLeft = 0.251    NoseSneerRight = 0.247
Position_x = -1.171  Position_y = -2.650  Position_z = -3.079   <- 单位未标定
Rotation_x = -4.089  Rotation_y = 9.569   Rotation_z = 3.129    <- 度
Timestamp = 1790345000000.000
TongueOut = 0.000
```

### 两条值得注意的读数（**不是**配置问题）

* `MouthSmileLeft = 0.000` 而 `MouthSmileRight = 0.029` —— 左右不对称，但值本身很小，
  可能只是那一刻的表情/设备噪声。**别拿单帧下结论。**
* `CheekPuff = 0.965` 在这帧接近满值 —— 值得回头看一眼是不是常态（若常态接近 1，多半是设备侧的问题，
  不是我们的映射）。

---

## 3. 两台设备对照

| | 安卓 VTS | iPhone VTS |
|---|---|---|
| 键数 | 65 | 67 |
| 形状拼写 | 小驼峰 + `_L/_R`（眉带后缀、眨眼两套） | 纯 VTS PascalCase |
| 形状覆盖 | **44 / 52**（缺 12，见 §1） | **52 / 52** |
| 内眉 | 左右两条线（并到一个规范名） | 一条线 |
| `headUp/headDown/…` 那 6 个 | **有** | 没有 |
| 标量 16 项 | `Rotation_*` / `Position_*` / `Eye*` / `FaceFound` / `Hotkey` / `Timestamp` —— **两台同名同量纲** | 同左 |

**两台都还没标定**：`Rotation_*` 的**符号与轴序**、`Position_*` 的**单位**、`Eye*_x/y/z` 的**用法**
（是欧拉角还是方向向量）都只是"原样通过"。调试阶段要看的是"有没有流过去"，
标定是后面的事（做法：在对应那份配置的副本上加 `curve` 或 `modifiers`）。

---

## 4. 相关文件

* 配置：`Editor/FaceTracking/Profiles/ho-debug-androidVTS.hoface.json` / `ho-debug-iphoneVTS.hoface.json`
* 生成器：`.warudo-mod-research/.tools/gen-device-profile.ps1`（**读这里的 `.keys`**，不推断名字）
* 核对工具：`.warudo-mod-research/.tools/check-device-keys.ps1`（一份键清单 → 报"对不上/没发"）
* 守门用例：`.research/profile-json-test`（真值清单 ↔ 原始 dump 逐名核对、配置覆盖、单方言、输出齐全）
* 协议出处：<https://github.com/DenchiSoft/VTubeStudioBlendShapesUDPReceiverTest>
