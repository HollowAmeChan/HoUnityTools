# VBridger 的输入 / 输出参数格式（一手抽样）

> **已归档（2026-09-23）。** 这是从 `Saves\*.vbridger` 十份自带预设（已解密成 JSON）里**逐行统计**
> 出来的词汇表与规则，用来回答两个问题：**它的输入是什么**、**它发出去的参数是什么格式**。
> 前置的存档格式与字段说明见 [VBridger 的中间层：一手逆向记录](VBRIDGER_MIDDLE_LAYER_RESEARCH.md)；
> 我们自己那套中间层见 [面捕中间层处理](../FACE_TRACKING_MIDDLE_LAYER.md)。
> 逐行原始数据（10 份预设 × 314 行全量表、输入并集表、min/max/smooth 直方图）在
> `.research/vbridger/VOCABULARY.md`（不进仓库），本文只留结论。

## 0. 三句话结论

1. **输出不是一套统一标准**，而是"**目标平台自己的命名**"，而且**一张表只打一个下游**：
   喂 VTubeStudio 用 **VTS 的追踪参数名**（`FaceAngleX`、`EyeOpenLeft`、`MouthOpen`…，固定清单）；
   喂 VMC/VRM 应用用 **ARKit 原名**（`eyeBlinkLeft`…，事实上的可移植面部词汇表）；
   其余是**它自己声明/用户自定义的驼峰名**（`JawOpen`、`MouthX`、`BodyAngle`…）。
   ⚠️ **同名在两套表里可能不是一回事**：`JawOpen` 在一份预设里是 VTS 自定义参数，另一份里是 VMC 的 ARKit 键名
   —— 中间层必须**按下游分表**（这个坑是从它自己的预设里看出来的）。
2. **Live2D 侧有两个命名空间，别混**：VTS 的**追踪参数**是固定的一套（插件可自由写，VBridger 用的就是它）；
   而 `ParamAngleX` / `ParamEyeLOpen` / `ParamMouthOpenY` 是**每个模型自己的参数 ID**，
   官方 API **没有"直接写模型参数"的请求** —— 中间层应发**追踪参数**，让 VTS 自己的映射 / auto-setup 去对模型。
3. **输入远不止 ARKit 52**：实测 10 份预设里出现 **104 个变量** —— ARKit（`_L/_R` 拼写）、头/眼姿态
   （`headRotX/Y/Z`、`headPosX/Y/Z`、`eyeLeftY`…）、**15 个 viseme**（`viseme_AA…` 连续值 + `viseme_AA_abs…`
   绝对值两条线）、`volume`，以及追踪健康位 `faceFound`。
   ⚠️ **但它自己认的名字只有 100 个（`SceneData.shapekeys`）**，多出来的 15 个里
   **`HipsX`/`SpineX`/`ChestX`/`UpperChestX` 全是不存在的变量** —— `VMC-Face-Head` 那条
   `Head = −(NeckX+UpperChestX+ChestX+SpineX+HipsX) + headRotX*2` 因此**表达式校验直接失败**
   （源码里未定义变量抛 `ESUnknownExpressionException`、输入框标红、该行不再求值），
   **整条 Head 行是死的**。（`NeckX/Y/Z` 能成立，靠的是"vector 行在 LateUpdate 里把自己发布的
   `名字X/Y/Z` 注册成全局变量"这个机制；`Hips/Spine/Chest/UpperChest` 在任何地方都没有。）
   → 教训：**未定义变量必须报错，不能静默当 0**（我们自己的求值器是"未知变量 = 0"，
   代价就是这种错永远看不见；至少要给"表达式中引用了不存在的输入"一条面板告警）。


## 1. 输入词汇表（表达式里能写什么）

| 组 | 名字（示例） | 值域 | 谁给 |
| --- | --- | --- | --- |
| **ARKit 52** | `eyeBlink_L/R`、`eyeWide_L/R`、`eyeSquint_L/R`、`eyeLookUp/Down/In/Out_L/R`、`jawOpen`、`mouthClose`、`mouthSmile_L/R`、`mouthFrown_L/R`、`mouthPucker`、`mouthFunnel`、`mouthRollUpper/Lower`、`mouthShrugUpper/Lower`、`mouthPress_L/R`、`mouthDimple_L/R`、`mouthStretch_L/R`、`mouthUpperUp_L/R`、`mouthLowerDown_L/R`、`mouthLeft/Right`、`cheekPuff`、`cheekSquint_L/R`、`browOuterUp_L/R`、`browDown_L/R`、`browInnerUp`、`noseSneer_L/R`、`tongueOut` | 0..1（`mouthLeft/Right` 方向语义） | 手机 App（iFacialMocap 的 `_L/_R` 拼写） |
| **头的姿态/位置** | `headRotX/Y/Z`（度）、`headPosX/Y/Z` | 度 / 米级 | 手机 App |
| **眼的原始角度** | `eyeLeftY`、`eyeLeftZ`、`eyeRightY`、`eyeRightZ`（VTS 直连时用） | 度/raw | VTS 追踪源 |
| **向量行自己发布的分量** | `NeckX/Y/Z`、`HeadX/Y/Z` 之类 —— **只有存在同名 vector 行时才成立** | 同该行 | `LateUpdate` 把 `名字X/Y/Z` 注册成全局变量（L7145-L7173） |
| ~~全身骨链~~ | ~~`HipsX/Y/Z`、`SpineX/Y/Z`、`ChestX/Y/Z`、`UpperChestX/Y/Z`~~ | — | ❌ **不存在**：源码里搜不到这些标识符，`VMC-Face-Head` 的 `Head` 行因此无效（见 §0.3） |
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

> 上面 104 个里，**89 个是这张表里的**，另外 15 个是"表里没有的"：`NeckX/Y/Z`（靠 vector 行发布）
> 与 `Hips/Spine/Chest/UpperChest` 的 X/Y/Z **12 个纯属不存在**（见 §0.3）。
> 反过来，这张表里有 **11 个从来没有被任何预设引用**：`BlendShapes`、`jawForward`、
> `noseSneer_L/R`、`Joints`、`eyeLeftX`、`eyeRightX`、`Sound Input`、`viseme_SIL`、`Other`、`faceFound`
> ——（`viseme_SIL` 只以 `_abs` 形式被用）。**声明与实际使用是两回事**，看这张表时要小心。

其它追踪源**不新增变量，而是改名进这套规范名**（同文件里的三张表）：

| 表 | 条数 | 拼写 | 来源 |
| --- | --- | --- | --- |
| `shapekeys`（规范） | 100 | `eyeBlink_L` / `eyeBlink_R` | iFacialMocap（`_L/_R`） |
| `faceMotionKeys` | **66** | `eyeBlinkLeft` / `eyeBlinkRight` | FaceMotion3D（ARKit 拼写） |
| `vtsKeys` | **66** | `EyeBlinkLeft` / `EyeBlinkRight`（首字母大写） | VTubeStudio 的 BlendShapes 追踪 |
| `mouthKeys` | 27 | 27 个嘴部键 | 音频口型/嘴部模块用（会乘 Mouth Multiplier） |

> 两张改名表**只覆盖前 66 项**（到 `headRotZ` 为止）——`volume`、`viseme_*`、`Sound Input`、
> `faceFound` 这些**只能按原拼写进**，因为音视频源本来就只有一套名字。

**哪个来源用哪张表（这决定"我们直连 iFacialMocap 该发什么名字"）**：

| 来源 | 用不用改名表 | 条件 | 依据 |
| --- | --- | --- | --- |
| **iFacialMocap / 默认 UDP 协议** | ❌ **不改名** | 线名必须**逐字等于 `shapekeys` 里的一员**，否则整条 `continue` 丢掉 | L12161-L12164 |
| FaceMotion3D | ✅ `faceMotionKeys` | 仅在 `FaceMotionActive` 时；先剥掉 `Foo.` 前缀再按**下标**换名 | L12148-L12159 |
| VTubeStudio 的 BlendShapes | ✅ `vtsKeys` | 按**下标**对齐，查不到再用原名在 `shapekeys` 里找一次 | L12019-L12031 |
| NVidia AR | ❌ | 它自己的表就是 `_L/_R` 拼写，还会把 `cheekPuff_L/R` 合并成 `cheekPuff = max(L,R)` | L11705-L11709 |

**线上单位（重要）**：iFacialMocap 那条路的融合键是 **0..100**，源码里先 `/100f` 才进标定与曲线
（L12177）；我们自己的接收端是同一个做法（`IFacialMocapPacket` 里也 `/100f`）。
头姿是**度**，头位置是**厘米级**（不是米）。

> **这条设计值得抄**：*一套规范名 + 每个数据源一张改名表*。表达式与预设只认规范名，
> 换设备只换表 —— 而不是让每条规则都去写"如果来源是 X 就用另一个拼写"。

`viseme_*` 那 15 个名字（`SIL PP FF TH DD KK CH SS NN RR AA EE IH OH OU`）不是随手起的：
它是**微软 SAPI / JALI 那一套 viseme 枚举**，在 VTuber 工具链里被广泛沿用。
`blendshapeCalibration` 是同文件里的 `List<float>`（**62 个 0**），就是校准按钮抓的那份"静止归零"。

### 1.2 一个值从设备到表达式之间被加工了几道（这是"额外的东西"）

原始值**不是**直接进表达式的，中间有四道（都可证）：

| # | 加工 | 说明 | 依据 |
| --- | --- | --- | --- |
| 1 | **静息值标定** | `MapValue(x,0,1,calibration[i])`：把"静止时读到的那点噪声"压回 0 —— `Lerp(0,1,InverseLerp(offset,1,x))`，offset 为 0 时原样通过 | L11542-L11549 |
| 2 | **每输入曲线** | 上一步的结果再过一条**逐输入**的 AnimationCurve（`InputCurves*.vbsettings`，**68 条**：52 ARKit + `volume` + 15 viseme）——默认**全是恒等线**，是给用户手调"某一路太灵敏"用的 | L11907 / L12053 / L12177 |
| 3 | **Mouth Multiplier** | 27 个嘴部输入（`mouthKeys`）额外乘一个全局系数（UI 0.1–1.5，默认 1.0） | L11189-L11192 |
| 4 | 写进 solver 的全局变量 | 之后表达式才看得到 | — |

外加两个"非面捕但表达式里能读"的东西：`volume`（音频）与 `faceFound`（丢脸时置 0；
它是在 `ExpressionSolver.globalConstants` 里被直接改的，L11185、L11996）。

**表达式语言**（`AK.ExpressionSolver`，MIT，2015 Antti Kuukka，被编进 DLL）：

- 内建 `sin cos tan asin acos atan atan2 sqrt abs sign floor ceil min max sinh cosh tanh exp log log10 round rand clamp approx pow strlen`，
  常量 `e`/`pi`，字符串字面量 `'...'`（**单引号里的表达式是懒求值**），运算符 `+ - * / ^ % < > <= >= == != && ||`。
- **VBridger 自己加了四个**（L5630-5633）：`stabil(var,dif)`（迟滞防抖）、`time(inc,max)`（锯齿计时）、
  `if(cond,then,else)`、`lerp(a,b,t)`。
- ⚠️ **未定义变量 = 硬错误**（不是 0）：`undefinedVariablePolicy` 默认 `Error` 且从不改（L34159），
  写了不存在的名字 → 抛异常 → 该行 `valid=false`、输入框标红、**永远不出值**（L6697-L6713）。
  我们的求值器选的是"未知 = 0、永不抛"，代价见 §0.3 的教训。

## 2. 输出词汇表（它往外发什么）

10 份预设合计 **100 个不同的输出名**。按目标分四族：

### 2.1 VTS 追踪参数（喂 VTubeStudio，固定的一套）

应用里有一张 `SceneData.defaultOutput`（反编译 L8023+，**24 个名字**）—— 这正是它认的
**VTS 追踪参数白名单**，也是"发送给 VTS"的默认集合：

`FacePositionX/Y/Z`、`FaceAngleX/Y/Z`、`MouthSmile`、`MouthOpen`、`Brows`、`TongueOut`、
`EyeOpenLeft/Right`、`EyeLeftX/Y`、`EyeRightX/Y`、`CheekPuff`、`BrowLeftY/RightY`、`MouthX`、
`VoiceVolumePlusMouthOpen`、`VoiceFrequency`、`VoiceVolume`、`VoiceFrequencyPlusMouthSmile`。

（`EyeLeftX/Y` 在这里出现，但十份预设里只写 `EyeRightX/Y` —— VTS 侧两眼共用一个注视目标。）

**但预设里发出去的名字**远比这 24 个多，要分成三类看（314 行输出统计：**119 行 VTS 追踪名 /
127 行 ARKit 名 / 4 行人类骨骼名 / 0 行 `Param*` / 64 行自定义**）：

| 类别 | 名字 | 说明 |
| --- | --- | --- |
| **① 官方 VTS 追踪参数**（插件可自由写） | `FaceAngleX/Y/Z`、`FacePositionX/Y/Z`、`MouthOpen`、`MouthSmile`、`MouthX`、`Brows`、`BrowLeftY/RightY`、`EyeOpenLeft/Right`、`EyeLeftX/Y`、`EyeRightX/Y`、`TongueOut`、`CheekPuff`、`FaceAngry`、`Voice*`（+ 20 个手部参数） | 名字与语义由 VTS 定；官方清单见 [VTS wiki](https://github.com/DenchiSoft/VTubeStudio/wiki/VTS-Model-Settings)；⚠️ **`CheekPuff`/`FaceAngry` 只有 iOS、`TongueOut` 只有 iOS/Android** |
| **② 它自己声明的自定义参数**（预设里大量用） | `BodyAngleX/Y/Z`、`BodyPositionX/Y/Z`、`BodyAngle`、`BodyPosition`、`JawOpen`、`MouthPucker`、`MouthFunnel`、`MouthShrug`、`MouthPressLipOpen`、`BrowInnerUp`、`Eye_Squint_L/R`、`EyeSquintLeft/Right` | 官方追踪清单里**没有**这些名字，靠 `ParameterCreationRequest` 自动登记（名字要唯一、字母数字、4–32 字符，`min/max/default` 只是"新建映射时的默认范围"、不是钳制） |
| **③ 模型参数 `Param*`** | **一个都没有** | `ParamAngleX` / `ParamEyeLOpen` 是**每个模型自己的 ID**，而且**官方 API 没有直接写模型参数的请求** —— 所以 VBridger 一律发追踪参数，让 VTS 的映射/auto-setup 去对模型 |

预设里实际用到的名字与范围：

| 名字 | 范围（VBridger 声明） | 用途 |
| --- | --- | --- |
| `FaceAngleX/Y/Z`（或 vector 版 `FaceAngle`） | ±30…±50（度） | 头部朝向 |
| `FacePositionX/Y/Z`（或 `FacePosition`） | X ±15 / Y ±5 / Z ±10 | 头部位置 |
| `BodyAngleX/Y/Z`（或 `BodyAngle`）〔自定义〕 | ±30…±40 | 身体朝向（**带 100ms 延迟**，让身体慢跟头） |
| `BodyPositionX/Y/Z`〔自定义〕 | 同 FacePosition | 身体位置 |
| `EyeOpenLeft` / `EyeOpenRight` | 0..1，**0.5 = 中性** | 睁眼度（0 = 闭） |
| `EyeRightX` / `EyeRightY` | ±0.6…±1 | 眼球方向（VTS 里两眼共用一个目标，所以只发一套） |
| `MouthOpen` | 0..1 | 张嘴（= 下颌 − 唇闭合 + 漏斗修正） |
| `MouthSmile` | −1..1 | 笑/哭 |
| `MouthX` | −1..1 | 嘴左右 |
| `MouthPucker` / `MouthFunnel`〔自定义〕 | −1..1 / 0..1 | 嘟嘴 / 漏斗 |
| `MouthPressLipOpen` / `MouthShrug`〔自定义〕 | ±1.3 / 0..1 | 抿嘴张开 / 嘴耸 |
| `JawOpen` / `CheekPuff` / `TongueOut` | 0..1 | 直通（`JawOpen` 是自定义名，`CheekPuff`/`TongueOut` 是官方名） |
| `Brows` / `BrowLeftY` / `BrowRightY` | 0..1，**默认 0.5** | 眉（`BrowInnerUp` 是自定义） |
| `VoiceVolumePlusMouthOpen` / `VoiceFrequencyPlusMouthSmile` | 0..1 | VTS 的"音频驱动"口型（VBridger 用**嘴型**冒充） |
| `Eye_Squint_L/R`、`EyeSquintLeft/Right`〔自定义〕 | 0..1 | 眯眼（自定义名，不同预设拼写不同） |

> ⚠️ 上表的"范围"是**VBridger 自己声明的**（写在预设的 `min`/`max` 里）。官方只对语音类明示 `0..1`，
> 其余追踪参数的权威 min/max 要运行时用 `InputParameterListRequest` 现取。


### 2.2 ARKit 原名（喂 VMC / VRM 应用）

两份 VMC 预设的输出**全是 ARKit-52 的 camelCase 名**（逐行实测）：

| 预设 | 行数 | ARKit 行 | 唯一名 | 其它 |
| --- | --- | --- | --- | --- |
| `VBridger_VMC_FaceOnly` | 50 | 50 | **49** | — |
| `VBridger_VMC-Face-Head` | 54 | 50 | **49** | 4 行向量 `Head` / `Neck` / `LeftEye` / `RightEye` |

**都不是完整 52 个**：`jawForward`、`noseSneerLeft`、`noseSneerRight` 缺失，而 **`jawLeft` 写了两遍**
（两行完全一样，应该是 `jawRight` 的笔误）—— 下游若按"必须有 52 个"校验会直接失败。

**镜像反接也不一致**（这才是"中间层要能改数据"的典型理由）：

| 预设 | 反接的键 |
| --- | --- |
| `VMC_FaceOnly` | `eyeBlinkLeft ← eyeBlink_R`、`eyeBlinkRight ← eyeBlink_L`、`browOuterUpLeft ← browOuterUp_R`、`browOuterUpRight ← browOuterUp_L` |
| `VMC-Face-Head` | 只有 `browOuterUpLeft ← browOuterUp_R`、`browOuterUpRight ← browOuterUp_L`（`eyeBlinkLeft = eyeBlink_L` **没反**） |

同一家出的两份预设，同一批键的左右接法都不一样 —— 说明**左右约定是"看模型"的，不是标准的**。

为什么 ARKit 名字会成为事实标准：VMC 协议本身**不规定任何混合键名表**（`/VMC/Ext/Blend/Val`
只带一个字符串 + 一个 float，收方按自己模型里的键去对），是 VRM/ARKit 生态把它当成了通用词汇。
三个相关但**不同**的东西别混：

| 名字 | 是什么 | 谁定 |
| --- | --- | --- |
| **ARKit 52**（`eyeBlinkLeft`/`jawOpen`/`mouthSmileLeft`…） | Apple `ARFaceAnchor.BlendShapeLocation` 的 52 个键，**camelCase** | Apple；VMC/VRM 生态事实采用 |
| **VTS `VTSARKitBlendshape`** | VTS 侧的另一种 input 类型，**它自己映射成 52 个 ARKit 语义**（用 `Left` 拼写，如 `EyeBlinkLeft`） | VTS |
| **VRCFT "Unified Expressions"** | 另一套**更大**的前脸标准（含 `EyeLook*`/`Jaw*`/`Lip*` 数十个），不是 ARKit 52 | VRCFT |

⚠️ 结论：**"ARKit 52 直通"这句话在我们这里要按下游分别建表**，而且**要能纠左右** —— 不能一张表打天下。

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
| `VBridger_VMC-Face-Head` | 54 | VMC | ARKit 50 行 + 4 行向量；但 **`Head` 那行引用不存在的变量、是死行**（§0.3），且 `jawLeft` 重复 |
| `VBridger_VMC_FaceOnly` | 50 | VMC | 只发脸（仍然含镜像反接） |
| `VBridger_VisemesARKit` | 34 | VTS | **口型优先**：有声走 viseme、静音回 ARKit（用 `viseme_SIL_abs` 当开关） |
| `VBridger_PNGTuber` | 10 | VTS/自定义 | 2D 立绘：`Visemes = Σ(序号×viseme_abs)` 用来**选帧**（⚠️ 这行实测有错，见下表） |

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
| **选帧** | `Visemes = Σ(序号_i · viseme_i_abs)`（PNGTuber，`min=0 max=12`）—— ⚠️ 实测这行**自己就是错的**：`viseme_SS_abs * 67`（应为 `* 6`）、`viseme_DD` 与 `viseme_KK` 都乘 `8`。**"把枚举塞进连续参数"这种做法天生易错**；我们要的是"一个参数一行/一分支" |

## 4. 取值与单位约定（做下游映射时照这个）

- **眼**：0..1，**0.5 = 中性**，0 = 闭、1 = 睁大（VTS 约定）。ARKit 侧是"闭/睁大各一个键"，所以要合成一根轴。
- **嘴**：`MouthSmile`/`MouthX`/`MouthPucker` 是 **−1..1**（0 = 中性）；`MouthOpen`/`JawOpen`/`Funnel` 是 0..1。
- **眉**：0..1，**默认 0.5**。
- **头/身体角度**：**度**，`FaceAngle ±30…±50`、`BodyAngle ±30…±40`；头姿缩放系数 0.66~1.5，身体带 100ms 延迟。
- **位置**：X ±15 / Y ±5 / Z ±10（自定义单位，VTS 侧不是米）。
- **修饰符**：`smooth` 是 **0..1 的 EMA 系数**（0 = 不滤），实测 0 ~ 0.77；`delay` 是**毫秒**（只有身体用 100）；
  `stepDetails2` 只出现在 `Stepped`/`PNGTuber`。

## 5. 下游标准（别人吃什么）

上面 §2 讲的是 **VBridger 自己发什么**。这一节讲**下游真的认什么** —— 两边并不重合，这才是"我至今
不知道 l2d 那边吃的输入有什么标准"的答案：**Live2D 侧吃的不是模型参数，是 VTS 的追踪参数**。

### 5.1 结论表

| 下游 | 名字形态 | 例子 | 范围 | 谁定义 | 跨模型稳定性 |
| --- | --- | --- | --- | --- | --- |
| **VTS 追踪参数**（插件能写的） | PascalCase，无前缀 | `FaceAngleX`、`MouthOpen`、`EyeOpenLeft`、`EyeRightX`、`Brows`、`TongueOut`、`VoiceA`、`HandLeftFinger_2_Index` | 协议接受 `−1e6..1e6`；各参数 min/max/default 是**"新建映射时的默认上下限"**，官方只对语音类明示 `0..1`，其余用 `InputParameterListRequest` 现取 | **VTubeStudio**（官方 Wiki），插件只能读不能增删清单 | **高**，但**平台相关**：`CheekPuff`/`FaceAngry` 仅 iOS，`TongueOut` 仅 iOS/Android |
| **VTS 模型参数**（`Param*`） | `Param` + PascalCase，**每个模型自己起** | `ParamAngleX`、`ParamEyeLOpen`、`ParamMouthOpenY`、`ParamMouthForm`、`ParamBodyAngleX`、`ParamBreath` | 逐模型自定义；Cubism 标准表只是**惯例**（`ParamAngle*` `±30`、`ParamEye*Open` `0/1/1`、`ParamMouthOpenY` `0..1`、`ParamMouthForm` `−1..1`、`ParamBodyAngle*` `±10`） | **模型作者**（Cubism Editor）；Live2D 官方只给 Standard Parameter List 约定，非强制 | **低**。⚠️ **官方 API 没有直接写模型参数的请求** —— 只能写追踪参数，让用户在 VTS 里映射；「参数名遵守 Cubism 标准表」的价值是 **VTS auto-setup 一键映射** |
| **VMC / VRM**（Blend） | 就是**接收模型自己的 blend 名**；面捕场景的事实标准 = **ARKit 52 的 camelCase** | `eyeBlinkLeft`、`jawOpen`、`mouthSmileLeft`；最小公共集是 VRM0 预设名 `A/I/U/E/O`、`Blink_L/R` | VMC 协议**不给范围**；VRM 1.0 规定 Expression `[0-1]` **并要求实现 clamp**；VRM0 绑定权重惯例 `[0,1]` | **VRM spec**（名字语义 + `[0,1]`）+ **Apple ARKit**（52 名）；接收应用自己决定映射（Warudo 提供 ARKit/MMD/VRM 三选一） | **中**：名字稳定，但**模型必须真做了这些 blend**；大小写敏感；VRM0 预设名与 VRM1 不同名，要按 spec 的映射表转 |
| **VMC**（Bone） | `UnityEngine.HumanBodyBones` 的**类型名** | `Head`、`Neck`、`LeftEye`、`RightEye`、`Hips`、`Spine` | 位置 = 米（局部），旋转 = 四元数 | **Unity / VRM humanoid 骨骼定义** | **高**（名字固定），但**骨骼是否存在**看模型（眼骨、指骨可选） |
| **VRM 1.0 Expression** | 小写预设名，自定义放 `expressions.custom` | `happy`、`angry`、`sad`、`relaxed`、`surprised`、`aa/ih/ou/ee/oh`、`blink`、`blinkLeft/Right`、`lookUp/Down/Left/Right`、`neutral` | **`[0-1]`，规范要求 clamp**（整个生态里唯一被规范写死的值域）；`isBinary` 阈值 0.5 | **VRM Consortium** | **最高**（规范级），但**只有 17 个预设**，做不了面捕细节 |
| **Unity Animator 参数**（我们自己的链） | 任意字符串，大小写敏感，类型只有 Float/Int/Bool/Trigger | 随项目自定 | Float 无内置范围 | **我们自己** | **零** —— 正因为自由，**建议直接采用 VTS 追踪参数名或 ARKit-52 名**，省掉一张映射表 |

引用：[VTS Model Settings](https://github.com/DenchiSoft/VTubeStudio/wiki/VTS-Model-Settings)、
[VTS README（custom parameters / 注入规则）](https://github.com/DenchiSoft/VTubeStudio#adding-new-tracking-parameters-custom-parameters)、
[Cubism 标准参数表](https://docs.live2d.com/en/cubism-editor-manual/standard-parameter-list/)、
[VMC Protocol spec](https://protocol.vmc.info/english)、
[VRM 0.0 spec](https://github.com/vrm-c/vrm-specification/blob/master/specification/0.0/README.md)、
[VRMC_vrm-1.0 expressions](https://github.com/vrm-c/vrm-specification/blob/master/specification/VRMC_vrm-1.0/expressions.md)、
[Apple `ARFaceAnchor.BlendShapeLocation`](https://developer.apple.com/documentation/arkit/arfaceanchor/blendshapelocation)；
逐条实测与 UNVERIFIED 清单见 `.research/vbridger/DOWNSTREAM_STANDARDS.md`。

### 5.2 注册自定义参数（写 VTS 非清单名时）

```jsonc
{ "messageType": "ParameterCreationRequest",
  "data": { "parameterName": "MyNewParamName", "explanation": "…",
            "min": -50, "max": 50, "defaultValue": 10 } }
```

- `parameterName`：**唯一、仅字母数字、无空格、长度 4–32**（所以 `Eye_Squint_L` 这种下划线名
  根本注册不了，`HandLeftFinger_2_Index` 能存在是因为它是 VTS 内置的）。
- `min/max/defaultValue`：`±1e6` 内，**不是值的硬上下限**，只是"新建映射时默认填的范围"。
- 配额：全局 300 个、单插件 100 个；重名（别人建的）失败，自己重复创建成功并能**改 min/max/default**。
- 存 `StreamingAssets/Config/custom_parameters.json`；**吊销 token 会删掉该插件的全部自定义参数**。
- 注入约束：值必须是 float 且 `−1e6..1e6`；`id` 不存在直接报错；**每个参数至少每秒重发一次**，
  否则 VTS 视为丢失并回落；`mode` 缺省 = 覆盖（同参数同时只能一个插件写），`"add"` = 叠加。

### 5.3 三条落地建议

1. **输出表按下游分表**，不要一张扁平表。VBridger 自己就踩了这个坑：
   `JawOpen` 在 VTS 预设里是自定义追踪参数、在 VMC 预设里是 blend 名，"同名两种身份"。
2. **每行带 `(name, min, max, default)` 元数据**：写 VTS 时拿去 `ParameterCreationRequest`，
   写 VMC 时它只是钳位/曲线定义域（VBridger 就是这么干的，源码可证）。
3. **先探测再写**：VTS 先 `InputParameterListRequest`（区分 `defaultParameters` / `customParameters`）
   → 缺的注册 → 每帧注入；VMC 侧**没有探测机制**，只能靠模型约定 + 骨名白名单（HumanBodyBones）校验。

### 5.4 未确认（不要当结论用）

- VTS 各默认追踪参数的**完整** min/max/default：官方 README 的示例自称 incomplete，只能运行时取。
- 能否用 `InjectParameterDataRequest` 直写 `ParamAngleX`：官方文档只承认写追踪参数 → **不依赖**。
- VMC 的 `/VMC/Ext/Blend/Val` 值域是否被协议限定 `0..1`：spec 只写 float，`0..1` 来自 VRM 规范。
- OBSKUR 的 blend 命名要求：没找到官方说明。

## 6. 对我们自己的意义

1. **"参数格式"不是我们发明的**：要对接 VTS 就照 §2.1 的名字与范围；对接 VMC/VRM 就照 §2.2 的 ARKit 原名
   （下游侧的标准见 §5）。这两套名字就是"输出参数"该长什么样的**事实标准**
   （加上 VRM 1.0 的 expression 名与 Cubism 的 `Param*` 约定）。
   更关键的一条：**Live2D 那边根本没有"输入参数标准"这回事** —— 我们能写的是 VTS 的追踪参数，
   模型参数 `Param*` 是逐模型的、官方 API 不给直写；所以别去猜 L2D 吃什么，交给 VTS 的映射。
2. **我们的 profile 已经能表达上面全部规则**：加权和、减法、门控、`clamp`、`lerp`、`if`、
   按行曲线与有序修饰符 —— §3 的十种模式都是"一条表达式 + 一条曲线"。
3. **值得抄的两条设计**：① 同一语义给"连续值 + `_abs` 绝对值"两条输入线（做门控用）；
   ② 给每个输出声明 `min/max/default`（对接 VTS 的参数声明要报这些，我们的曲线定义域+范围已经够用）。
4. **不建议抄的**：`FaceAngle`/`BodyAngle` 那种一行写 X/Y/Z 的 vector 模式（我们要的是"一个参数一行"）、
   以及把音频口型冒充成 `Voice*` 参数的做法（那是给下游不支持音频时的权宜）。
