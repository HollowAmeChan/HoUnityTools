# HO 参数规范（我们的中间层选什么）

> 这份是**我们自己的**目标词表的唯一权威表。跟 [参数标准表](PARAMETER_STANDARDS.md) 的分工：
> 那份记**外部标准怎么定**（VTS / ARKit / VMC / VRM / VRCFT 各自的规定，逐条带官方 URL），
> 这份记**我们选什么**——参数叫什么、什么意思、值域多少、从哪个源算出来、用的是谁家公式。
>
> 词表来源是三样东西的差集与并集，全部实测/实读，不靠命名规则推断：
> ① VTS 官方追踪参数 98 条（`docs/VTS_HIGH_QUALITY_FACE_CATALOG.json` 的 `native` 段）；
> ② VBridger 10 份已解密预设里出现的 92 个出口名（`.research/vbridger/decrypted/`）；
> ③ iPhone VTS 实测发来的 67 条线（[设备验证表 §2](PARAMETER_DEVICE_VERIFICATION.md)）。

日期：2026-09-26

## 0. 一句话与范围

**这份规范只覆盖"我们这条链上算得出来"的参数。**
候选词表一共 **48 个**（= 官方追踪参数里与我们输入有关的 **27** 个 + VB 自造 **16** 个 + 协议层信号 **1** 个 + 算不出来的 **11** 个，去重后按下面的口径切开）：

| 组 | 数量 | 在哪 |
|---|---|---|
| 官方 VTS 追踪参数 | **20** | §3.1 |
| VB 自造参数（要注册） | **16** | §3.2 |
| 协议层信号 `FaceFound` | **1** | §3.3 |
| **可合成小计** | **37** | |
| 当前输入下**算不出来** | **11**（9 行） | §5 |

⚠️ `FaceFound` **不是** VTS 的命名追踪参数——它是注入报文里的一个布尔字段
（`InjectParameterDataRequest.data.faceFound`，与 `parameterValues[]` 平级），
所以它不参与"官方 20"的计数，也不该被当成艺术轴。

**不进这份规范的**（有意排除，不是漏了）：

| 排除项 | 为什么 |
|---|---|
| 手部 26 条、手柄 39 条 | 跟面捕无关，且手机不产生 |
| VMC 骨骼向量 `Head` / `Neck` / `LeftEye` / `RightEye` | 那是 `/VMC/Ext/Bone/Pos` 路径的骨骼，不是参数；我们这条链是"参数进参数出" |
| `BodyAngle` / `BodyPosition` 及分量 | 身体姿态，属于骨骼/朝向那一层。**VB 有，我们暂时不要** |
| 原始 ARKit 52 直通 | 不是"合成出来的参数"，是输入本身。它的契约在 §1 |

---

## 1. 输入契约：设备能发什么

这一节是整份规范的**地基**——所有公式只能吃这里有的东西。

### 1.1 iPhone VTS（当前唯一接的设备）

67 条线，实测（[设备验证表 §2](PARAMETER_DEVICE_VERIFICATION.md) 的生成表，203 帧）：

| 半边 | 条数 | 内容 |
|---|---|---|
| **52 个 ARKit 形态量** | 52 | 干净 PascalCase：`JawOpen` / `MouthSmileLeft` / `EyeBlinkLeft`…**52 个全部到位、0 个对不上** |
| **头姿** | 6 | `Rotation_x/y/z`（度）、`Position_x/y/z`（单位未标定） |
| **眼球** | 6 | `EyeLeft_x/y/z`、`EyeRight_x/y/z`（度） |
| **其它** | 3 | `FaceFound`(0/1)、`Hotkey`、`Timestamp` |

**"VTS 苹果侧能不能表达 ARKit 的全部语义"——能，这是实测的，不是推断。**
iPhone 那 203 帧里 52 个形态量**全部 MOVES**，逐个对应 ARKit 52 名单。

⚠️ **但这是分设备的**：安卓那半边只有 60 个 MOVES，`EyeLeft_z`/`EyeRight_z` 恒 0、
`mouthShrugUpper` 只在噪声底。所以"能不能表达"必须**按设备分别确认**。
⚠️ **`MOVES` ≠ 好触发**：`TongueOut` 97.0% 帧接近 0、`EyeLookOutRight` 96.6%、
`EyeLookUp*` 93.6%、`BrowOuterUp*` 93.6%——它们**能动**，但要**明显做动作**才动。
别看到 `MOVES` 就当它随手可得。

### 1.2 设备**不**提供的东西（决定了 §5 那 11 个为什么算不出来）

| 缺什么 | 影响的参数 |
|---|---|
| 麦克风 / 音频（VTS 手机的 tracking 包不带音频，也没有 OVRLipSync） | `VoiceVolume`、`VoiceFrequency`、`VoiceVolumePlusMouthOpen`、`VoiceFrequencyPlusMouthSmile`、`VoiceA/I/U/E/O`、`VoiceSilence`、`Visemes` |
| 鼠标 / 触摸 | `MousePositionX`、`MousePositionY` |
| 生气脸的专用分类器 | `FaceAngry` |

### 1.3 三个词汇表，一条转换规则

这是整份规范**最容易搞错**的地方：同一个形态量有三套名字在流动，而"哪一层用哪一套"是**固定的**，不是口味问题。

| 层 | 长什么样 | 例子 | 谁定义 |
|---|---|---|---|
| **设备线名** | PascalCase | `JawOpen`、`EyeBlinkLeft`、`MouthSmileLeft` | **VTS 手机协议**（= iOS ARKit 名，官方固定） |
| **规范名**（我们中间层内部） | camelCase | `jawOpen`、`eyeBlinkLeft`、`mouthSmileLeft` | `HoFaceTrackingChannels.Names`（52 条，`Runtime/FaceTracking/`） |
| **出口名**（喂控制器） | 我们选的那套 | `MouthOpen`、`FaceAngleX` | 本规范 §3 |
| *（参考）VB 的内部名* | camelCase + `_L/_R` | `jawOpen`、`eyeBlink_L` | VBridger 的 `SceneData.shapekeys` |

**线名 → 规范名的规则只有一条：首字母小写。** 52 个形状全部适用（这正是 VBridger `vtsKeys`
表与它的内部 `shapekeys` 表之间的关系——两表只差大小写，其余 12 个头/眼项拼写相同）。

在本机 VBridger 源码里，这三套拼写各有一张**按下标对齐**的表，用哪张取决于当前追踪源：

| VB 的表 | 行 | 拼写 | 对应我们这边 |
|---|---|---|---|
| `faceMotionKeys` | L8052 | `BrowDownLeft`（iFacialMocap/MediaPipe 的 `Left/Right` 系） | 我们不接 |
| `vtsKeys` | L8063 | **`BrowDownLeft` / `EyeBlinkLeft` / `JawOpen`** | **= 我们设备发的线名，逐字相同** |
| （无表） | — | `browDown_L` / `eyeBlink_L` / `jawOpen` | = VB 内部名；也是我们的规范名 |

⚠️ **VB 并不"拥有一切方言"**：它拥有的是**三套各自的精确表**，`IndexOf` **逐字命中**，
**没有归一化、没有别名表**。所以"多写几条不同拼写的输入行"不是可选的兼容策略，而是必需——
每多接一种拼写就多一族行。

⚠️ **输入行的左值必须是规范名，右值才是线名**：

    输入行：parameter = 规范名（jawOpen，camelCase）    ← 通道靠这个认
            expression = 设备线名（JawOpen，PascalCase） ← 包里真收到的那个键

因为 `HoFaceAnimationSession` 是拿**输入行的 `parameter`** 去
`HoFaceTrackingChannels.IndexOf(...)` 解析通道的。**写反的后果是静默且全面的**：
一份 `parameter = expression = JawOpen` 的配置**一个通道都解析不到**，中间层什么形状参数都不产出——
不报错、不警告，只是全都不动。

> 这不是假想：`ho-iPhoneVTS.hoface.json` 的第一版就是这么写的，理由听起来还挺有道理——
> "这一份的规范名词表就是设备线名词表"。**错。** 规范名是上游通道表的属性，配置只能去符合它。

⚠️ 只有**非通道**的 15 条（`Rotation_*` / `Position_*` / `EyeLeft_*` / `EyeRight_*` /
`FaceFound` / `Hotkey` / `Timestamp`）可以 `parameter == expression`——它们不是通道，
没有任何东西拿它们去 `IndexOf`，所以没有改名可做也没有改名必要。

> 由此推出一条判据：**一份"纯直通"（`parameter == expression`）的配置能不能真跑起来，
> 完全取决于那台设备的线名是否恰好等于规范名**（忽略大小写）。
> 实测：`ho-debug-iphoneVTS` 覆盖 52/52（PascalCase 恰好只差首字母，能跑）；
> `ho-debug-androidVTS` 只覆盖 14/52（`browDown_L` 那种与 `browDownLeft` 差得远）。
> 所以那两份的定位是**实测记录**，不是"能跑的配置"——见
> [Profiles/README.md](../Editor/FaceTracking/Profiles/README.md)。

---

## 2. 值域约定（写公式前必须知道的四件事）

### 2.1 官方只规定了两个参数的范围

| 参数 | 范围 | 依据 |
|---|---|---|
| `MouthOpen` | `[0, 1]`，0 = 闭 | VTS wiki 原文 |
| `FaceAngleX` | 示例 min `-30` / max `30` | VTS API 文档示例数组 |
| `FacePositionX` | 示例 min `-10` / max `10` | 同上 |
| 其余全部 | **官方未公开** | 官方自己写那张示例数组 "is incomplete"；权威范围只能运行时 `InputParameterListRequest` 现取 |

⚠️ 网上流传的 `MouthSmile -1..1`、`Brows 0..1` 是**社区/VBridger 的约定**，不是官方规范。
我们沿用 VB 的约定时要说清这一点。

### 2.2 0.5 中立位是 **VB 发明的**，不是 VTS 的

VB 有一整族参数用 **0.5 = 中立**：`Brows`、`BrowLeftY`、`BrowRightY`、`EyeOpenLeft/Right`。
静息时公式算出来就是 0.5，闭眼往 0 走、睁大往 1 走。

这在官方文档里**找不到依据**。它是 VB 为了让"闭眼/睁眼"能用一根双向轴表达而定的私有约定。
我们照用时：**曲线的纵轴范围必须比 0..1 宽**（这些量能出 `[0,1]`），
否则默认的 `0..1` 曲线会把合法值**静默夹掉**——症状是"出口恒为 0 或 1 而输入侧有真值"。

### 2.3 三种量纲并存，且没有任何字段标注单位

| 量纲 | 例子 | 范围 |
|---|---|---|
| 表情权重 | `MouthOpen`、`CheekPuff` | `0..1` |
| 双向偏移 | `MouthX`、`EyeX`、`EyeY` | `-1..1` |
| 角度 | `FaceAngle*` | `±30/±40/±90`（**度**） |
| 位移 | `FacePosition*` | `±10/±15/±50`（**单位未标定**） |

### 2.4 双层曲线都要写

管线是 `出口值 = curve_out( expression_out( 入口值 ) )`，而**输入行自己也有曲线**。
想直通的量纲，**输入行与出口行都得给宽曲线**。只给一层 = 另一层用默认 `0..1` 夹掉。

---

## 3. 目标词表总表（36 个艺术轴 + 1 个信号）

`源` 列写的是 §1 输入契约里的东西。`公式来源` 列写的是哪份 VBridger 预设——
**选公式按预设，不按出口名**：VB 的 `EyeRightX` 是用 `eyeLookIn_L`/`eyeLookOut_L` 算的，
`EyeRightY` 还加了 `browOuterUp_L`，**名字根本不描述来源**。

### 3.1 官方 VTS 追踪参数（20）

| # | 参数 | 含义 | 值域 | 源 | 公式来源 |
|---|---|---|---|---|---|
| 1 | `FaceAngleX` | 左右转头 | 度 | `Rotation_*` | `VTS_Compatible` |
| 2 | `FaceAngleY` | 抬低头 | 度 | `Rotation_*` | `VTS_Compatible` |
| 3 | `FaceAngleZ` | 歪头 | 度 | `Rotation_*` | `VTS_Compatible` |
| 4 | `FacePositionX` | 头左右位移 | 未标定 | `Position_*` | `VTS_Compatible` |
| 5 | `FacePositionY` | 头上下位移 | 未标定 | `Position_*` | `VTS_Compatible` |
| 6 | `FacePositionZ` | 头远近位移 | 未标定 | `Position_*` | `VTS_Compatible` |
| 7 | `EyeOpenLeft` | 左眼睁开度（0.5 中立） | `0..1` | ARKit 眼睑 | `AdvancedARKit_V3.0` |
| 8 | `EyeOpenRight` | 右眼睁开度（0.5 中立） | `0..1` | ARKit 眼睑 | `AdvancedARKit_V3.0` |
| 9 | `EyeLeftX` | 左眼水平注视 | 度 | `EyeLeft_x` | **设备原值** |
| 10 | `EyeLeftY` | 左眼垂直注视 | 度 | `EyeLeft_y` | **设备原值** |
| 11 | `EyeRightX` | 右眼水平注视 | 度 | `EyeRight_x` | **设备原值** |
| 12 | `EyeRightY` | 右眼垂直注视 | 度 | `EyeRight_y` | **设备原值** |
| 13 | `Brows` | 双眉共同上下（0.5 中立） | `0..1` | ARKit 眉 | `AdvancedARKit_V3.0` |
| 14 | `BrowLeftY` | 左眉上下（0.5 中立） | `0..1` | ARKit 眉 + 嘴 | `AdvancedARKit_V3.0` |
| 15 | `BrowRightY` | 右眉上下（0.5 中立） | `0..1` | ARKit 眉 + 嘴 | `AdvancedARKit_V3.0` |
| 16 | `MouthOpen` | 嘴唇开口 | `0..1`（官方） | ARKit 嘴/颌 | `AdvancedARKit_V3.0` |
| 17 | `MouthSmile` | 综合嘴型（Form） | `-1..1`（社区） | ARKit 嘴 | `AdvancedARKit_V3.0` |
| 18 | `MouthX` | 嘴左右位移 | `-1..1`（社区） | ARKit 嘴 | `AdvancedARKit_V3.0` |
| 19 | `TongueOut` | 伸舌 | `0..1` | `TongueOut` | 各预设恒等 |
| 20 | `CheekPuff` | 鼓腮 | `0..1` | `CheekPuff` | 各预设恒等 |

⚠️ 第 9–12 条我们**不用 VB 的合成**：VB 的 `EyeRightX` 读的是 `eyeLookIn_L`（**左眼**），
`EyeRightY` 还加了 `browOuterUp_L`。iPhone 本来就在发原始眼球标量，直接引设备值更干净。

### 3.2 VB 自造参数（16）——不在任何官方清单里，**要注册才能用**

这一族是 VB 为了让 Live2D 能表达 VTS 词表**根本没有的概念**而发明的。
喂 VTS 时要走 `ParameterCreationRequest`，**接收端用户必须接受**才生效——这是用它们的代价。

| # | 参数 | 建模的东西（VTS 通用参数**没有**） | 值域 | 源 | 公式来源 |
|---|---|---|---|---|---|
| 22 | `MouthFunnel` | 圆唇 / 漏斗嘴 | `0..1` | `MouthFunnel`、`JawOpen` | `AdvancedARKit_V3.0` |
| 23 | `MouthPucker` | 嘟嘴 | `-1..1` | `MouthDimpleLeft/Right`、`MouthPucker` | `AdvancedARKit_V3.0` |
| 24 | `MouthShrug` | 撇嘴 | `0..1` | `MouthShrugUpper/Lower`、`MouthPressLeft/Right` | `AdvancedARKit_V3.0` |
| 25 | `MouthPressLipOpen` | 压唇 + 唇开 | `-1.3..1.3` | `MouthUpperUp*`、`MouthLowerDown*`、`MouthRoll*` | `AdvancedARKit_V3.0` |
| 26 | `BrowInnerUp` | 内眉上抬 | `0..1` | `BrowInnerUp` | `AdvancedARKit_V3.0` |
| 27 | `Eye_Squint_L` | 左眼眯（含颊部提拉） | `0..1` | `EyeSquintLeft`、`CheekSquintLeft` | `VTS_Compatible` |
| 28 | `Eye_Squint_R` | 右眼眯 | `0..1` | `EyeSquintRight`、`CheekSquintRight` | `VTS_Compatible` |
| 29 | `BrowL` | 左眉单轴（0.5 中立，PNGTuber 系） | `0..1` | `BrowOuterUpLeft`、`BrowDownLeft`、`MouthRight` | `PNGTuber` |
| 30 | `BrowR` | 右眉单轴（0.5 中立） | `0..1` | `BrowOuterUpRight`、`BrowDownRight`、`MouthLeft` | `PNGTuber` |
| 31 | `EyeOpen` | 单眼开合（PNGTuber 用单眼） | `0..1` | `EyeBlinkLeft` | `PNGTuber` |
| 32 | `EyeX` | 双眼注视合成成一根双向轴 | `-1..1` | `EyeLookOut/In*` | `PNGTuber` |
| 33 | `EyeY` | 同上，垂直 | `-1..1` | `EyeLookUp/Down*` | `PNGTuber` |
| 34 | `FaceAngle`（+`X/Y/Z`） | 脸 3 轴**向量**（同时走 Bone/Pos） | `-40..40` | `Rotation_*` | `AdvancedARKit_V2.0` |
| 35 | `FacePosition`（+`X/Y/Z`） | 脸 3 轴位移**向量** | `-15..15` | `Position_*` | `AdvancedARKit_V2.0` |
| 36 | `BodyAngle`（+`X/Y/Z`） | **身体** 3 轴朝向 | `-40..40` | `Rotation_*`、`EyeBlink*` | `AdvancedARKit_V2.0` |
| 37 | `BodyPosition`（+`X/Y/Z`） | **身体** 3 轴位移 | `-15..15` | `Position_*` | `AdvancedARKit_V2.0` |

⚠️ 第 27/28 条 VB 有两种写法：`VTS_Compatible` 用 `(eyeSquint_L+cheekSquint_L)/2`（含颊部），
`AdvancedARKit_V3.0` 只用 `eyeSquint_L`。**这是两个不同的参数**，不是同一件事的两种写法。
我们取前者（信息更多），选哪份要在配置里写明。

### 3.3 协议层信号（1）

| 参数 | 含义 | 值域 | 源 | 公式来源 |
|---|---|---|---|---|
| `FaceFound` | 追踪健康（**不是艺术轴**） | `0/1` | `FaceFound` | **设备原值** |

⚠️ 它**不是** VTS 的命名追踪参数。注入报文里 `faceFound` 是与 `parameterValues[]` 平级的
一个布尔字段（VTS 官方示例原文），不是参数数组里的一个 id。VB 也照这个语义处理：
恒定发一个 `VBridgerFaceFound`（`faceFound?1:0`）。

### 3.4 命名不一致——照抄 VB 的原样，**不改**

同一族里 VB 自己就不统一，这是**实测**，不要"顺手统一"：

* `MouthFunnel`（PascalCase）vs `Eye_Squint_L`（下划线 + `_L`）vs `BrowL`（无分隔）；
* `BrowLeftY`（官方拼法）vs `BrowL`（VB 拼法）；
* `EyeRightX`（官方名）**读左眼数据**（见 §3.1 的警告）。

我们**按用途**微调时要在配置的 `notes` 里写明"改过什么、为什么"。

---

## 4. 公式全表（从本机 VBridger 机械提取，不手抄）

提取器：`.research/vbridger/one_formula_each.ps1`（按预设优先级选一条，输出名 → 公式）。
预设优先级：`VTS_Compatible` > `AdvancedARKit_V3.0` > `AdvancedARKit_V2.0` > `AdvancedARKitSettings` > `PNGTuber`。

变量名一律是 **VB 内部拼写**（`eyeBlink_L`、`mouthSmile_L`，即 `SceneData.shapekeys`）。
映射到我们的输入线名：**我们的规范名就是设备线名**，iPhone 发的是 PascalCase
（`EyeBlinkLeft`）。转换规则是 VB 的 `vtsKeys` 表（52 个形状只差大小写），
对照表在 [参数标准表 §3.2](PARAMETER_STANDARDS.md)、生成映射在 `catalog.raw_arkit[].name`。

### 4.1 头姿 / 位移 / 注视

```
FaceAngleX      = -Rotation_y
FaceAngleY      = -Rotation_x
FaceAngleZ      =  Rotation_z
FacePositionX   = -Position_x
FacePositionY   =  Position_y
FacePositionZ   = -Position_z

FaceAngle   (向量) = -Rotation_y                         ; Y = -((Rotation_x * ((90-abs(Rotation_y))/90)) + (Rotation_z * (Rotation_y/45))) ; Z = ((Rotation_z * ((90-abs(Rotation_y))/90)) - (Rotation_x * (Rotation_y/45)))
FacePosition(向量) = -Position_x                          ; Y =  Position_y                                  ; Z =  Position_z
BodyAngle   (向量) = -Rotation_y * 1.5                    ; Y = (-Rotation_x * 1.5) + ((EyeBlinkLeft + EyeBlinkRight) * -1) ; Z = Rotation_z * 1.5
BodyPosition(向量) = -Position_x                          ; Y =  Position_y * 1                              ; Z =  Position_z * -.5

EyeLeftX   = EyeLeft_x
EyeLeftY   = EyeLeft_y
EyeRightX  = EyeRight_x
EyeRightY  = EyeRight_y
```

⚠️ `FaceAngleX/Y/Z` 与 `FacePositionX/Y/Z` 的**正负号是 VB 的**，属于照抄结构；
它吃的那两根设备轴（`Rotation_*` / `Position_*`）**还没在实机上核过**。
`FaceAngle` 那个向量行的 Y/Z 分量带**交叉项**（`Rotation_x` 与 `Rotation_z` 互相修正，
用 `(90-abs(Rotation_y))/90` 当权重）——这是 VB 为了补偿头部大幅偏转时的欧拉角耦合，
不是随手写的。

### 4.2 眼睑 / 眉

```
EyeOpenLeft    = .5 + (EyeBlinkLeft  * -.8) + (EyeWideLeft  * .8)
EyeOpenRight   = .5 + (EyeBlinkRight * -.8) + (EyeWideRight * .8)

Brows          = .5 + (BrowOuterUpLeft + BrowOuterUpRight - BrowDownLeft - BrowDownRight) / 4
BrowLeftY      = .5 + (BrowOuterUpLeft  - BrowDownLeft)  + ((MouthRight - MouthLeft) / 8)
BrowRightY     = .5 + (BrowOuterUpRight - BrowDownRight) + ((MouthLeft - MouthRight) / 8)

BrowL          = ((BrowOuterUpLeft  - BrowDownLeft  - MouthRight) / 2) + .5
BrowR          = ((BrowOuterUpRight - BrowDownRight - MouthLeft ) / 2) + .5
EyeOpen        = EyeBlinkLeft
Eye_Squint_L   = (EyeSquintLeft  + CheekSquintLeft ) / 2
Eye_Squint_R   = (EyeSquintRight + CheekSquintRight) / 2
BrowInnerUp    = BrowInnerUp
```

⚠️ `Brows` / `BrowLeftY` / `BrowRightY` / `BrowL` / `BrowR` / `EyeOpen*` **静息就是 0.5**
（见 §2.2）。它们的曲线纵轴必须比 `0..1` 宽。
⚠️ `BrowLeftY`/`BrowRightY` **吃嘴部数据**（`MouthLeft/Right` 当偏航补偿项）——
这是"名字不描述来源"的又一个例子，别以为它只看眉。

### 4.3 嘴

```
MouthOpen        = (JawOpen - MouthClose) - ((MouthRollUpper + MouthRollLower) * .2) + (MouthFunnel * .2)
MouthSmile       = (2 - (MouthFrownLeft + MouthFrownRight + MouthPucker) + (MouthSmileRight + MouthSmileLeft + ((MouthDimpleLeft + MouthDimpleRight) / 2))) / 4
MouthX           = ((MouthLeft - MouthRight) + (MouthSmileLeft - MouthSmileRight))
MouthFunnel      = MouthFunnel - (JawOpen * .2)
MouthPucker      = ((MouthDimpleRight + MouthDimpleLeft) * 2) - MouthPucker
MouthShrug       = (MouthShrugUpper + MouthShrugLower + MouthPressRight + MouthPressLeft) / 4
MouthPressLipOpen= ((MouthUpperUpRight + MouthUpperUpLeft + MouthLowerDownRight + MouthLowerDownLeft) / 1.8) - (MouthRollLower + MouthRollUpper)
TongueOut        = TongueOut
CheekPuff        = CheekPuff
```

⚠️ `MouthOpen` 与 `MouthSmile` 都是**把 VTS 没有的维度折进来**的合成式：
`MouthOpen` 折了 `MouthClose`（闭唇）、`MouthRoll*`（卷唇）、`MouthFunnel`（圆唇）；
`MouthSmile` 折了 `MouthFrown*`、`MouthPucker`、`MouthDimple*`。
所以它们是**有损**的——这正是 VB 再单独开 `MouthFunnel`/`MouthPucker` 的原因。
⚠️ `MouthSmile` 静息 = 0.5（分子上 `2 - …`、分母 `/4`）。
⚠️ `MouthPucker` 里 `(dimple*2) - pucker` 的**正负方向与直觉相反**：dimple 大声时值为正。
⚠️ `MouthPressLipOpen` 的除数在四份预设里是 **四个不同的东西**，VB 自己没定下来：
`/1.2`（`AdvancedARKitSettings`）、`/1.8`（V2.0 / V2.0_Stepped / V2.0_PlusVolume / V3.0）、
`/16`（`VTS_Compatible`），而 `VisemesARKit` 那份**根本不是这一族公式**——它整个改由 `viseme_*` 驱动
（`(0 - viseme_PP) + ((viseme_SS*.5 + viseme_KK*.5 + …) * (1 - viseme_PP))`），
跟嘴部形态量无关。**没有权威值**，我们取 `/1.8`（多数派）。

### 4.4 注视合成

```
EyeX = EyeLookOutLeft - EyeLookInLeft
EyeY = EyeLookUpLeft  - EyeLookDownLeft
```

⚠️ 只看**左眼**、且只用了 `_L` 侧。这是 PNGTuber 的"双眼当一只用"。
我们要双眼独立时**不要用它**，直接用 §4.1 的设备眼球标量。

---

## 5. 明确**算不出来**的（9 行 / 11 个参数）

这一列不是"以后再做"，是**当前输入契约下不可能**。要它们必须先扩输入。

| 参数 | 缺什么 | 备注 |
|---|---|---|
| `Visemes` | 音素分类器（OVRLipSync / uLipSync） | VB 把它压成一个 `0..12` 的**音素编号**（`viseme_*_abs` 加权和）。⚠️ VB 那份公式里有错位：`viseme_SS_abs * 67` 与声明的 `max=12` 矛盾，`DD` 与 `KK` 共用 `8`。**要抄也得重排音素表** |
| `VoiceVolume` | 麦克风 | — |
| `VoiceFrequency` | 麦克风 + 元音检测 | — |
| `VoiceVolumePlusMouthOpen` | 麦克风 | VB 用 `MouthOpen` 顶上（无音频时退化成一个重复的 `MouthOpen`） |
| `VoiceFrequencyPlusMouthSmile` | 麦克风 | 同上，退化成重复的 `MouthSmile` |
| `VoiceA/I/U/E/O` | 麦克风 + 元音分类 | 官方保证这五个**永不同时为 1** |
| `VoiceSilence` | 麦克风 | PNGTuber 用它做"无声走面捕"的开关 |
| `MousePositionX/Y` | 鼠标 / 触摸 | 手机没有指针 |
| `FaceAngry` | 生气脸专用分类器 | 官方标 EXPERIMENTAL、不推荐；ARKit 52 里没有对应形态量 |

⚠️ `VoiceVolumePlusMouthOpen` / `VoiceFrequencyPlusMouthSmile` 这两个名字听起来像"音频""辅助"，
但 VB 的公式在有麦克风时是**加权混合**、没麦克风时**就是一个重复的 `MouthOpen`/`MouthSmile`**。
所以它们对我们**没有新增信息**——不要为了让参数表"齐全"而把它们算两遍。

---

## 6. 未标定 / 未验证（**别当结论用**）

| 项 | 状态 |
|---|---|
| `FaceAngle*` / `FacePosition*` 的正负号与轴向 | 照抄 VB 的结构，**未在实机核对**。改法是翻配置里那行的符号 |
| `Position_*` 的单位 | **未标定**，手机原始值直通 |
| `EyeLeft_*` / `EyeRight_*` 的单位与零位 | 实测给的是 `Rotation_*` 同级的小数值（`EyeLeft_x` 约 ±20），**未核对是否就是度** |
| `EyeLeftZ` / `EyeRightZ` 是否可用 | iPhone 能填（范围 5.87/6.11），安卓恒 0。含义未定 |
| 0.5 中立位是否该保留 | VB 的私有约定（§2.2）。用 VTS 官方语义（`Brows` 无 0.5）还是 VB 的，**我们还没定** |
| `MouthPressLipOpen` 的除数 | VB 自己四份预设四个值（`/1.2`、`/1.8`、`/16`，`VisemesARKit` 完全换公式），**没有权威值**；我们取多数派 `/1.8` |
| VB 那 16 个自造参数的**接收端接受率** | 走 `ParameterCreationRequest`，用户必须手动接受才生效。我们不做 VTS 中转时无所谓，做的时候要测 |

---

## 7. 这份规范怎么用

1. **改配置前先查这里**：参数名、值域、源、公式都在这。配置里的 `notes` 只写"这一行跟规范哪条对应"，不重复公式。
2. **加新参数**：先在这份里加一行（含义 / 值域 / 源 / 公式来源 / 可用性），**再**改配置与生成器。
   顺序反了就会出现"配置里有个没人知道什么意思的参数"。
3. **发现新设备或新语义**：先更新 §1 输入契约（实测表在 [设备验证表](PARAMETER_DEVICE_VERIFICATION.md)），
   再回来看 §5 那 11 个里有没有能解锁的。
4. **外部标准的依据**（官方 URL、逐条引用）在 [参数标准表](PARAMETER_STANDARDS.md)；
   这份不重复抄，只引用。

### 相关文件

| 文件 | 记什么 |
|---|---|
| [参数标准表](PARAMETER_STANDARDS.md) | **外部标准**：VTS / Cubism / ARKit / VMC / VRM / iFacialMocap / VRCFT，逐条带官方 URL |
| [设备验证表](PARAMETER_DEVICE_VERIFICATION.md) | **这台设备实测发了什么、动了多少**（生成表，可由脚本刷新） |
| **本文件** | **我们选什么**：目标词表 + 逐参数定义 + 公式来源 |
| [面捕中间层](FACE_TRACKING_MIDDLE_LAYER.md) | 值怎么被加工（两层行、缺键语义、曲线纪律） |
| [VBridger 的输入 / 输出参数格式](archive/VBRIDGER_IO_VOCABULARY.md) | 归档：VB 那 100 个输入变量与 314 行出口的一手记录 |
