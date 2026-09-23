# VBridger 的输入 / 输出参数格式（一手抽样）

> **已归档（2026-09-23）。** 这是从 `Saves\*.vbridger` 十份自带预设（已解密成 JSON）里**逐行统计**
> 出来的词汇表与规则，用来回答两个问题：**它的输入是什么**、**它发出去的参数是什么格式**。
> 前置的存档格式与字段说明见 [VBridger 的中间层：一手逆向记录](VBRIDGER_MIDDLE_LAYER_RESEARCH.md)；
> 我们自己那套中间层见 [面捕中间层处理](../FACE_TRACKING_MIDDLE_LAYER.md)。

## 0. 三句话结论

1. **输出不是一套统一标准**，而是"**目标平台自己的命名**"：喂 VTubeStudio 就用 **VTS 的追踪参数名**
   （`FaceAngleX`、`EyeOpenLeft`、`MouthOpen`…），喂 VMC/VRM 应用就用 **ARKit 原名**（`eyeBlinkLeft`…），
   其余是**给用户自定义的驼峰名**（`JawOpen`、`MouthX`…）。
2. **Live2D 侧没有一个"全局标准"**：VTS 的**追踪参数**是固定的一套（VBridger 用的就是它）；
   而 `ParamAngleX` / `ParamEyeLOpen` / `ParamMouthOpenY` 这些是**每个模型自己的参数 ID**
   （Cubism 只有一套"Cubism 标准参数"的约定），客户端要驱模型参数得走 VTS 的参数注入/自定义参数机制。
3. **输入远不止 ARKit 52**：实测 10 份预设里出现 **104 个变量** —— ARKit（`_L/_R` 拼写）、头/眼姿态
   （`headRotX/Y/Z`、`headPosX/Y/Z`、`eyeLeftY/eyeRightY`…）、**全身骨链**（`ChestX`…`HipsZ`，VMC 输入用）、
   **15 个 viseme**（`viseme_AA…` 连续值 + `viseme_AA_abs…` 绝对值两条线）、`volume`，以及追踪健康位。

## 1. 输入词汇表（表达式里能写什么）

| 组 | 名字（示例） | 值域 | 谁给 |
| --- | --- | --- | --- |
| **ARKit 52** | `eyeBlink_L/R`、`eyeWide_L/R`、`eyeSquint_L/R`、`eyeLookUp/Down/In/Out_L/R`、`jawOpen`、`mouthClose`、`mouthSmile_L/R`、`mouthFrown_L/R`、`mouthPucker`、`mouthFunnel`、`mouthRollUpper/Lower`、`mouthShrugUpper/Lower`、`mouthPress_L/R`、`mouthDimple_L/R`、`mouthStretch_L/R`、`mouthUpperUp_L/R`、`mouthLowerDown_L/R`、`mouthLeft/Right`、`cheekPuff`、`cheekSquint_L/R`、`browOuterUp_L/R`、`browDown_L/R`、`browInnerUp`、`noseSneer_L/R`、`tongueOut` | 0..1（`mouthLeft/Right` 方向语义） | 手机 App（iFacialMocap 的 `_L/_R` 拼写） |
| **头的姿态/位置** | `headRotX/Y/Z`（度）、`headPosX/Y/Z` | 度 / 米级 | 手机 App |
| **眼的原始角度** | `eyeLeftY`、`eyeLeftZ`、`eyeRightY`、`eyeRightZ`（VTS 直连时用） | 度/raw | VTS 追踪源 |
| **全身骨链**（VMC 输入） | `HipsX/Y/Z`、`SpineX/Y/Z`、`ChestX/Y/Z`、`UpperChestX/Y/Z`、`NeckX/Y/Z` | 度 | VMC 输入（`VMC-Face-Head` 预设里把全身链和头拼起来） |
| **viseme 两条线** | `viseme_AA…viseme_SIL`（**连续值**）与 `viseme_*_abs`（**当前最强口的绝对值**） | 0..1 | 音频口型模块 |
| **音频** | `volume` | 0..1 | 音频输入 |
| **追踪健康** | `faceFound`（丢脸） | 0/1 | 追踪侧 |

> 命名习惯值得抄：**同一个语义给两条线** —— `viseme_XX`（连续强度）与 `viseme_XX_abs`
> （"就是它"的绝对值），于是表达式可以用 `_abs` 做**门控**、用连续值做**幅度**（见 §3 的 viseme 规则）。

### 1.1 规范名 + 改名表（它怎么兼容多个追踪源）

应用里有一张**内部规范名**表（`SceneData.shapekeys`，反编译 L8031+，**100 条**）：
`BlendShapes` / `Joints` / `Other` 三个占位 + 52 个 ARKit（**`_L/_R` 拼写**）
+ `eyeLeftX/Y/Z`、`eyeRightX/Y/Z` + `headPosX/Y/Z`、`headRotX/Y/Z` + `Sound Input`、`volume`
+ 15 个 `viseme_*` + 15 个 `viseme_*_abs` + `faceFound`。

> 预设里还会直接用到**全身体链**（`HipsX/Y/Z`、`SpineX/Y/Z`、`ChestX/Y/Z`、`UpperChestX/Y/Z`、`NeckX/Y/Z`）——
> 那是 VMC 输入那条路给的，**不在这张表里**（所以"表达式里用到的变量 104 个"比"声明的 100 个"多）。

其它追踪源**不新增变量，而是改名进这套规范名**（同文件里的三张表）：

| 表 | 拼写 | 来源 |
| --- | --- | --- |
| `shapekeys`（规范） | `eyeBlink_L` / `eyeBlink_R` | iFacialMocap（`_L/_R`） |
| `faceMotionKeys` | `eyeBlinkLeft` / `eyeBlinkRight` | FaceMotion3D（ARKit 拼写） |
| `vtsKeys` | `EyeBlinkLeft` / `EyeBlinkRight`（首字母大写） | VTubeStudio 的 BlendShapes 追踪 |
| `mouthKeys` | 27 个嘴部键 | 音频口型/嘴部模块用 |

> **这条设计值得抄**：*一套规范名 + 每个数据源一张改名表*。表达式与预设只认规范名，
> 换设备只换表 —— 而不是让每条规则都去写"如果来源是 X 就用另一个拼写"。

`viseme_*` 那 15 个名字（`SIL PP FF TH DD KK CH SS NN RR AA EE IH OH OU`）不是随手起的：
它是**微软 SAPI / JALI 那一套 viseme 枚举**，在 VTuber 工具链里被广泛沿用。
`blendshapeCalibration` 是同文件里的 `List<float>`（**62 个 0**），就是校准按钮抓的那份"静止归零"。

## 2. 输出词汇表（它往外发什么）

10 份预设合计 **100 个不同的输出名**。按目标分四族：

### 2.1 VTS 追踪参数（喂 VTubeStudio，固定的一套）

应用里有一张 `SceneData.defaultOutput`（反编译 L8023+，**24 个名字**）—— 这正是它认的
**VTS 追踪参数白名单**，也是"发送给 VTS"的默认集合：

`FacePositionX/Y/Z`、`FaceAngleX/Y/Z`、`MouthSmile`、`MouthOpen`、`Brows`、`TongueOut`、
`EyeOpenLeft/Right`、`EyeLeftX/Y`、`EyeRightX/Y`、`CheekPuff`、`BrowLeftY/RightY`、`MouthX`、
`VoiceVolumePlusMouthOpen`、`VoiceFrequency`、`VoiceVolume`、`VoiceFrequencyPlusMouthSmile`。

（`EyeLeftX/Y` 在这里出现，但十份预设里只写 `EyeRightX/Y` —— VTS 侧两眼共用一个注视目标。）

预设里实际用到的（含预设自己加的自定义名）：

| 名字 | 范围 | 用途 |
| --- | --- | --- |
| `FaceAngleX/Y/Z`（或 vector 版 `FaceAngle`） | ±30…±50（度） | 头部朝向 |
| `FacePositionX/Y/Z`（或 `FacePosition`） | X ±15 / Y ±5 / Z ±10 | 头部位置 |
| `BodyAngleX/Y/Z`（或 `BodyAngle`） | ±30…±40 | 身体朝向（**带 100ms 延迟**，让身体慢跟头） |
| `BodyPositionX/Y/Z` | 同 FacePosition | 身体位置 |
| `EyeOpenLeft` / `EyeOpenRight` | 0..1，**0.5 = 中性** | 睁眼度（0 = 闭） |
| `EyeRightX` / `EyeRightY` | ±0.6…±1 | 眼球方向（VTS 里两眼共用一个目标，所以只发一套） |
| `MouthOpen` | 0..1 | 张嘴（= 下颌 − 唇闭合 + 漏斗修正） |
| `MouthSmile` | −1..1 | 笑/哭 |
| `MouthX` | −1..1 | 嘴左右 |
| `MouthPucker` / `MouthFunnel` | −1..1 / 0..1 | 嘟嘴 / 漏斗 |
| `MouthPressLipOpen` / `MouthShrug` | ±1.3 / 0..1 | 抿嘴张开 / 嘴耸 |
| `JawOpen` / `CheekPuff` / `TongueOut` | 0..1 | 直通 |
| `Brows` / `BrowInnerUp` / `BrowLeftY` / `BrowRightY` | 0..1，**默认 0.5** | 眉 |
| `VoiceVolumePlusMouthOpen` / `VoiceFrequencyPlusMouthSmile` | 0..1 | VTS 的"音频驱动"口型（VBridger 用**嘴型**冒充） |
| `Eye_Squint_L/R`、`EyeSquintLeft/Right` | 0..1 | 眯眼（自定义名，不同预设拼写不同） |

### 2.2 ARKit 原名（喂 VMC / VRM 应用）

`eyeBlinkLeft` … `mouthStretchRight` 共 **51 个**（52 键里少一个），基本一对一，
但有两处**故意反接**：VMC 预设里 `browOuterUpLeft ← browOuterUp_R`、`eyeBlinkLeft ← eyeBlink_R`
—— 因为那份模型的键是镜像的。**这就是"中间层要能改数据"的典型理由。**

### 2.3 vector 行（一行写 X/Y/Z）

`FaceAngle`、`FacePosition`、`BodyAngle`、`BodyPosition`、`LeftEye`、`RightEye`、`Head`、`Neck`
—— vector 行发 VMC 的骨骼/位置，或 VTS 的三个分量参数。

### 2.4 自由驼峰名（给用户自定义）

`JawOpen`、`MouthX`、`MouthPucker`、`MouthFunnel`、`MouthShrug`、`MouthPressLipOpen`、
`BrowLeftY/RightY`、`BrowInnerUp`、`CheekPuff`、`FaceAngle*`… —— 在"高级 ARKit"预设里就是这套名字，
用户自己在下游建同名参数即可。

## 3. 规则长什么样（这才是"中间层"的价值）

十份预设 = 十套规则。按用途分：

| 预设 | 行数 | 目标 | 它回答的问题 |
| --- | --- | --- | --- |
| `AdvancedARKitSettings` | 34 | VTS（声明范围） | 参数的 **min/max/默认** 该报多少（`FaceAngle ±50`、`FacePosition −15..15`…） |
| `AdvancedARKit_V2.0 / PlusVolume / Stepped / V3.0` | 26 ×4 | VTS | 同一套映射的四个版本：**V3 去掉音频、加平滑**；`PlusVolume` 把 `volume` 掺进嘴型；`Stepped` 给翻页动画用 |
| `VBridger_VTS_Compatible` | 28 | VTS | 最保守的一套（兼容 VTS 自己的追踪参数），身体带 100ms 延迟 |
| `VBridger_VMC-Face-Head` | 54 | VMC | ARKit 52 + **头/颈/全身链**（`Head = −(全身链和) + headRot×2`） |
| `VBridger_VMC_FaceOnly` | 50 | VMC | 只发脸（仍然含镜像反接） |
| `VBridger_VisemesARKit` | 34 | VTS | **口型优先**：有声走 viseme、静音回 ARKit（用 `viseme_SIL_abs` 当开关） |
| `VBridger_PNGTuber` | 10 | VTS/自定义 | 2D 立绘：`Visemes = Σ(序号×viseme_abs)` 用来**选帧** |

**反复出现的公式模式**（照抄这几条基本就能做出一套能用的映射）：

| 模式 | 例子 |
| --- | --- |
| **中性 0.5 的 0..1 轴** | `EyeOpenLeft = .5 + (eyeBlink_L*-.8 + eyeWide_L*.8)`；`Brows = .5 + (browOuterUp_L+browOuterUp_R-browDown_L-browDown_R)/4` |
| **正负两族相减再归一** | `MouthSmile = (2 − (frown_L+frown_R+pucker) + (smile_L+smile_R+avg(dimple)))/4` |
| **副语义做减法修正** | `MouthOpen = (jawOpen − mouthClose) − (rollUpper+rollLower)*.2 + funnel*.2` |
| **互斥门控** | `MouthX = (mouthLeft − mouthRight)*(1 − tongueOut)`（吐舌时嘴的左右让位） |
| **一个键反向冒充另一个** | `MouthPucker = (avg(dimple)×2 − pucker)*(1 − tongueOut)` |
| **耦合（眉跟嘴）** | `BrowLeftY = .5 + (browOuterUp_L − browDown_L) + (mouthRight − mouthLeft)/8` |
| **慢跟（身体）** | `BodyAngleX = −headRotY*1.5`，`delay = 100ms` |
| **roll/yaw 补偿** | `FaceAngleZ = headRotZ*((90−|headRotY|)/90) − headRotX*(headRotY/45)` |
| **音频口型：加权和 × 音量 × 非静音** | `JawOpen = (Σ w_i·viseme_i)·volume·(1−viseme_SIL_abs) + jawOpen·viseme_SIL_abs` |
| **选帧** | `Visemes = Σ(序号_i · viseme_i_abs)`（PNGTuber，max=12） |

## 4. 取值与单位约定（做下游映射时照这个）

- **眼**：0..1，**0.5 = 中性**，0 = 闭、1 = 睁大（VTS 约定）。ARKit 侧是"闭/睁大各一个键"，所以要合成一根轴。
- **嘴**：`MouthSmile`/`MouthX`/`MouthPucker` 是 **−1..1**（0 = 中性）；`MouthOpen`/`JawOpen`/`Funnel` 是 0..1。
- **眉**：0..1，**默认 0.5**。
- **头/身体角度**：**度**，`FaceAngle ±30…±50`、`BodyAngle ±30…±40`；头姿缩放系数 0.66~1.5，身体带 100ms 延迟。
- **位置**：X ±15 / Y ±5 / Z ±10（自定义单位，VTS 侧不是米）。
- **修饰符**：`smooth` 是 **0..1 的 EMA 系数**（0 = 不滤），实测 0 ~ 0.77；`delay` 是**毫秒**（只有身体用 100）；
  `stepDetails2` 只出现在 `Stepped`/`PNGTuber`。

## 5. 对我们自己的意义

1. **"参数格式"不是我们发明的**：要对接 VTS 就照 §2.1 的名字与范围；对接 VMC/VRM 就照 §2.2 的 ARKit 原名。
   这两套名字就是"输入参数"该长什么样的**事实标准**（加上 VRM 1.0 的 expression 名与 Cubism 的 `Param*` 约定）。
2. **我们的 profile 已经能表达上面全部规则**：加权和、减法、门控、`clamp`、`lerp`、`if`、
   按行曲线与有序修饰符 —— §3 的十种模式都是"一条表达式 + 一条曲线"。
3. **值得抄的两条设计**：① 同一语义给"连续值 + `_abs` 绝对值"两条输入线（做门控用）；
   ② 给每个输出声明 `min/max/default`（对接 VTS 的参数声明要报这些，我们的曲线定义域+范围已经够用）。
4. **不建议抄的**：`FaceAngle`/`BodyAngle` 那种一行写 X/Y/Z 的 vector 模式（我们要的是"一个参数一行"）、
   以及把音频口型冒充成 `Voice*` 参数的做法（那是给下游不支持音频时的权宜）。
