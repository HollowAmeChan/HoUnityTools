# 参数标准表（面捕与形态键的权威依据）

这份文档**只做一件事**：把"下游到底认哪些名字、什么值域、谁定的、跨模型稳不稳"逐行列清楚，
让本仓库所有涉及"写参数 / 写形态键 / 生成动画 / 做映射"的设计都能引到这里，而不是各自凭记忆。

**读法约定**

- 每张表都有**来源**小节，给官方 URL；行内注明官方原话的关键点。
- 官方没给的数值写「**官方未公开**」，绝不填一个看起来合理的数。
- 标 `UNVERIFIED` 的是**没有找到权威来源**的东西；标 `INFERRED` 的是由多处证据推出、但没人明说。
- 版本/抓取时间写在每节末尾（官方文档会变，尤其是 VTS 与 VRM 生态）。

**现状一句话（2026-09-25，代码是唯一真相）**：**输入只剩 VTS 手机**那一条请求式协议（§1.8）；
`iFacialMocap` 已整个删掉（§6 只作**官方协议记录**保留，我们不再接）；
中间层 `*.hoface.json` 是**映射表的唯一来源**（面板把它当必填总闸；**留空 / 没指定 ⇒ 空表，这一层不做事**，
**没有内置默认兜底** —— 与 Warudo 侧口径一致；指定之后两类行都只认它 —— §8 逐条给代码出处）。

**三条铁律**（后面所有内容都是这三条的展开）

1. **"追踪参数"和"模型参数"是两个世界。** VTS 的 `FaceAngleX`/`MouthOpen` 是**追踪参数**（固定清单，插件可写）；
   `ParamAngleX`/`ParamMouthOpenY` 是**每个 Live2D 模型自己的参数 ID**（逐模型、**VTS 插件协议不提供直写请求**）。
   我们永远写前者，让 VTS 的映射去对后者。
2. **VMC / VRM 侧没有"参数名标准"，只有"模型自己的形态键名"。** 协议只规定消息格式；
   名字空间属于收方模型，大小写敏感。面捕场景里事实上的通用词汇是 **Apple ARKit 52**。
3. **Unity Animator 参数名完全是我们自己的。** 正因为自由，**建议直接采用第 1、2 条里的名字**
   （追踪参数名或 ARKit 名）当 Animator 参数名 —— 这样 `SetFloat` 的名字与下游一致，省掉一张映射表。
   **我们的现状**：发货那份 `ho-iPhoneVTS.hoface.json` 的具体出口是 [HO 参数规范](PARAMETER_HO.md) 的 **90 行**
   （G1 **裸规范名** `jawOpen`… + G2 官方 VTS 追踪参数 + VB 自造 + 姿态向量 + `FaceFound`），
   **外加 37 行控制器轴 `Ho/Drive/*`**（喂新控制器，不计入 90；见那份 §3.7）。
   ⚠️ **没有任何"内置默认配置"**（2026-09-26 起：那张 52 个 `ARKit/<规范名>` + 4 根眼睑轴的内置默认表已整个删掉，
   见 [中间层 §6.1](FACE_TRACKING_MIDDLE_LAYER.md)）：
   输出行的 `parameter` **只由使用者自己的配置文件定**，控制器里没有那个名字就跳过（不猜也不补）；
   没指定配置文件 = 空表、这一层不做事。

---

## 1. VTS（Live2D / VTube Studio）

### 1.1 追踪参数全表（官方 "Supported INPUT parameters"）

来源：[VTS Model Settings（wiki 原文）](https://github.com/DenchiSoft/VTubeStudio/wiki/VTS-Model-Settings)。
平台列 ✔️/❌ 完全照抄官方那张表 —— **这是"某个参数在某个追踪源上根本没有数据"的权威依据**。

| 参数名 | 官方解释 | iOS | Android | 普通 webcam | webcam NVIDIA | webcam MediaPipe |
| --- | --- | --- | --- | --- | --- | --- |
| `FacePositionX` | 头左右位置 | ✔️ | ✔️ | ✔️ | ✔️ | ✔️ |
| `FacePositionY` | 头上下位置 | ✔️ | ✔️ | ✔️ | ✔️ | ✔️ |
| `FacePositionZ` | 离摄像机远近 | ✔️ | ✔️ | ✔️ | ✔️ | ✔️ |
| `FaceAngleX` | 头左右转 | ✔️ | ✔️ | ✔️ | ✔️ | ✔️ |
| `FaceAngleY` | 头上下转 | ✔️ | ✔️ | ✔️ | ✔️ | ✔️ |
| `FaceAngleZ` | 头侧倾 | ✔️ | ✔️ | ✔️ | ✔️ | ✔️ |
| `MouthSmile` | 笑的程度 | ✔️ | ✔️ | ✔️ | ✔️ | ✔️ |
| `MouthOpen` | 张嘴程度 | ✔️ | ✔️ | ✔️ | ✔️ | ✔️ |
| `Brows` | 双眉共同上下 | ✔️ | ✔️ | ✔️ | ✔️ | ✔️ |
| `BrowLeftY` | 左眉上下 | ✔️ | ✔️ | ✔️ | ✔️ | ✔️ |
| `BrowRightY` | 右眉上下 | ✔️ | ✔️ | ✔️ | ✔️ | ✔️ |
| `EyeOpenLeft` | 左眼睁开度 | ✔️ | ✔️ | ✔️ | ✔️ | ✔️ |
| `EyeOpenRight` | 右眼睁开度 | ✔️ | ✔️ | ✔️ | ✔️ | ✔️ |
| `EyeLeftX` / `EyeLeftY` | 眼球追踪（左） | ✔️ | ✔️ | ✔️ | ✔️ | ✔️ |
| `EyeRightX` / `EyeRightY` | 眼球追踪（右） | ✔️ | ✔️ | ✔️ | ✔️ | ✔️ |
| `MousePositionX` / `MousePositionY` | 鼠标或手指在设定范围内的位置 | ✔️ | ✔️ | ✔️ | ✔️ | ✔️ |
| `MouthX` | 嘴左右位移 | ✔️ | ✔️ | **❌** | ✔️ | ✔️ |
| `TongueOut` | 吐舌 | ✔️ | ✔️ | **❌** | **❌** | **❌** |
| `CheekPuff` | 鼓腮 | ✔️ | **❌** | **❌** | **❌** | **❌** |
| `FaceAngry` | 识别生气脸（官方标注 EXPERIMENTAL，不推荐） | ✔️ | **❌** | **❌** | **❌** | **❌** |

**值域**：官方只在下面这些地方给了明确范围 ——

| 参数 | 范围 | 依据 |
| --- | --- | --- |
| `MouthOpen` | `[0, 1]`，0 = 闭、1 = 全开 | wiki 原文举例："the input parameter MouthOpen is within the range [0, 1], with 0 being closed and 1 being all the way open" |
| `FaceAngleX` | 示例值 min `-30`、max `30` | API 文档 `InputParameterListResponse` 示例 |
| `FacePositionX` | 示例值 min `-10`、max `10` | 同上 |
| 其余追踪参数 | **官方未公开** | README 明确写这张示例数组 "is incomplete"；**权威范围只能运行时用 `InputParameterListRequest` 现取** |

> ⚠️ `MouthSmile` / `Brows` / `EyeOpen*`（以及上表那些）**官方都没写范围** ——
> 网上流传的 `MouthSmile -1..1`、`Brows 0..1` 是社区/工具的约定（VBridger 预设里就是自己填的），不是官方规范。
> ⚠️ **官方文档自己有一处矛盾**：`MouthX` 那一行的 "Webcam" 列是 ❌，但同一个表的
> "Webcam NVIDIA" 与 "Webcam MediaPipe" 两列都是 ✔️。合理解释是那一列指的是**默认的 OpenSeeFace 追踪器**，
> 但官方没写这句话 —— 所以引用这张表时要说清"哪个 webcam 追踪器"。
> ⚠️ 官方还写了一条容易忽略的规则：**每个输出参数在模型配置里只能被选一次**
> （"Each output parameter can only be chosen once"），即一个 Live2D 参数只能有一个输入源 ——
> 这条直接等价于我们这边"一个参数只能有一个写入者"的设计。

**官方 auto-setup 的硬约束**：它按**标准 Live2D 参数名与值**自动配置；
"如果你改了参数 ID 或范围，auto-setup 就配不出来，只能手动配"。这就是 §2 Cubism 标准表的价值所在。

### 1.2 语音 / 口型参数（Advanced Lipsync）

来源：[VTS Lipsync（wiki 原文）](https://github.com/DenchiSoft/VTubeStudio/wiki/Lipsync)。
实现基于 [uLipSync](https://github.com/hecomi/uLipSync)（旧版 Simple Lipsync 基于 Oculus LipSync，官方已不推荐）。

| 参数 | 范围（官方明示） | 语义 |
| --- | --- | --- |
| `VoiceA` / `VoiceI` / `VoiceU` / `VoiceE` / `VoiceO` | `0..1` | 各元音被检测到的程度 |
| `VoiceSilence` | `0..1` | 检测到"静音"（或音量极低，接近 0）时为 1 |
| `VoiceVolume` / `VoiceVolumePlusMouthOpen` | `0..1` | 麦克风音量（后者 = `MouthOpen + VoiceVolume`） |
| `VoiceFrequency` / `VoiceFrequencyPlusMouthSmile` | `0..1` | 由元音检测合成，供"只有一个嘴型参数"的模型用（后者 = `MouthSmile + VoiceFrequency`） |

官方两条要点：① 这些参数可以接到**任意** Live2D 参数上，不限于嘴（可做均衡器效果）；
② **`VoiceA/I/U/E/O` 永远不会同时为 1**，只在小值区间混合（这是 **VTS 自身输出**的保证；自行接入 uLipSync、混合其他源或改写权重后，应由我们自己约束，不能沿用该保证）。
③ 官方给的嘴部推荐接法：`MouthOpen → ParamMouthOpen`、`MouthSmile → ParamMouthForm`、
`VoiceSilence → ParamSilence`、`VoiceA..O → ParamA..O`，由 `ParamSilence` 决定"有声走元音、无声走面捕"。
（官方这段示例里写的是 `ParamMouthOpen`，而 Cubism 标准表里叫 `ParamMouthOpenY` —— 后者是标准名。）

### 1.3 手部追踪参数

来源：[VTS Hand-Tracking（wiki 原文）](https://github.com/DenchiSoft/VTubeStudio/wiki/Hand-Tracking)。
**仅 Windows/macOS 的 webcam 追踪**（MediaPipe），实验性。

| 参数 | 语义 | 范围（官方明示） |
| --- | --- | --- |
| `HandLeftFound` / `HandRightFound` / `BothHandsFound` | 是否追踪到 | 1 = 找到，0 = 没找到 |
| `HandDistance` | 两手之间距离（都找到时） | 官方未公开 |
| `HandLeftPositionX` | 左手的 X：向外 = `10`，中间 = `0`，向内 = `-10` | `-10..10` |
| `HandLeftPositionY` | 左手的 Y：上 = `10`，中 = `0`，下 = `-10` | `-10..10` |
| `HandLeftPositionZ` | 左手的 Z：靠近摄像机 = `10`，远 = `-10` | `-10..10` |
| `HandRightPositionX/Y/Z` | 右手，向上/外/近的方向定义同上（X 的"外"是向右） | `-10..10` |
| `HandLeftAngleX` / `HandLeftAngleZ` | 左手左右旋转 / 左右倾斜 | `±180` |
| `HandRightAngleX` / `HandRightAngleZ` | 右手同上 | `±180` |
| `HandLeftOpen` / `HandRightOpen` | 手张开程度 | `0..1`（0 = 完全握拳，1 = 完全伸直） |
| `HandLeftFinger_1_Thumb` … `HandLeftFinger_5_Pinky` | 左手指：拇指/食指/中指/无名指/小指 | `0..1` |
| `HandRightFinger_1_Thumb` … `HandRightFinger_5_Pinky` | 右手同上 | `0..1` |

> ⚠️ **手指参数的官方拼写里带下划线 + 数字**（`HandLeftFinger_2_Index`）。
> 这不是笔误：它属于内置清单，所以能带下划线；而**自定义参数禁止下划线**（见 §1.6）。

### 1.4 追踪参数 vs 模型参数（最容易搞混的一节）

| | 追踪参数（tracking） | 模型参数（`Param*`） |
| --- | --- | --- |
| 例子 | `FaceAngleX`、`MouthOpen`、`EyeOpenLeft`、`Brows` | `ParamAngleX`、`ParamEyeLOpen`、`ParamMouthOpenY` |
| 谁定义 | **VTube Studio**（固定清单） | **每个模型的作者**（Cubism Editor 里自己起） |
| 插件能否新增 | 能，走 `ParameterCreationRequest`（自定义参数） | **不能** |
| VTS 插件 API 能否直写 | 能，`InjectParameterDataRequest` | **没有这个请求**（⚠️ 这是"VTS 协议不提供"，不是"Live2D 写不了" —— Live2D 自己的 SDK 当然能直写 `Param*`，那是宿主程序内部的事） |
| 名字是否保证存在 | 是（固定清单） | **不保证**：逐模型、可改名、可改范围 |
| 值域 | 见 §1.1（多数官方未公开） | 逐模型；Cubism 标准表只是**惯例**（§2） |
| 跨模型稳定 | 高（同版本内一致） | **低**（同名不同范围很常见） |

**所以"给 Live2D 喂数据"这件事，真相是**：写**追踪参数** → 用户在 VTS 的
"VTS Parameter Setup" 里把它映射到自己的 `Param*` → （若用户用的是标准名）auto-setup 可以一键配好。
（**我们现在的接收链路不写追踪参数** —— 中间层输出行的内置约定是 ARKit 名 `ARKit/<键>`；
要喂 VTS 就自己在配置文件里加输出行、按 §1.5 的注入规则发，§8 那条"按下游分表"说的是这件事。）
`Live2DParameterListRequest` 能把当前模型的 `Param*`（含各自 min/max/default）读回来，
但**只读不写**。
**"必须用 Cubism 标准名"的真正理由是**：模型作者可以随便改名、改范围、甚至不建这个参数 ——
按标准名建模的模型才能被 auto-setup 和通用工具识别，而不是"API 不允许写别的名字"。

### 1.5 VTS API：用到的消息

来源：[VTS README（官方 API 文档）](https://github.com/DenchiSoft/VTubeStudio)，
以下 JSON 均为官方原文示例（原样引用，未改字段）。

| 消息 | 方向 | 用途 | 关键点 |
| --- | --- | --- | --- |
| `InputParameterListRequest` | → | 问"现在有哪些追踪参数" | 返回 `defaultParameters` + `customParameters`（各含 `name`/`addedBy`/`value`/`min`/`max`/`defaultValue`）。官方警告：**返回数据量大，不要 60+ FPS 地问** |
| `ParameterValueRequest` | → | 问单个参数 | 参数不存在会**返回错误** |
| `Live2DParameterListRequest` | → | 问当前模型的 `Param*` | 无模型时 `modelLoaded=false`、数组为空 |
| `ParameterCreationRequest` | → | 新建自定义追踪参数 | 见 §1.6 |
| `ParameterDeletionRequest` | → | 删自定义参数 | 内置参数删不掉，别人的插件建的也删不掉 |
| `InjectParameterDataRequest` | → | **写值** | 见下面 |

`InjectParameterDataRequest` 官方示例：

```jsonc
{
  "apiName": "VTubeStudioPublicAPI",
  "apiVersion": "1.0",
  "requestID": "SomeID",
  "messageType": "InjectParameterDataRequest",
  "data": {
    "faceFound": false,
    "mode": "set",
    "parameterValues": [
      { "id": "FaceAngleX", "value": 12.31 },
      { "id": "MyNewParamName", "weight": 0.8, "value": 0.7 }
    ]
  }
}
```

**注入规则表（全部为官方原文要点）**

| 规则 | 内容 |
| --- | --- |
| 值类型与范围 | 必须是浮点，`-1000000 .. 1000000`；**越界返回错误** |
| 参数必须存在 | "If one or more of the parameters don't exist, an error payload will be returned." → **先探测/先注册，再写** |
| 存活期 | **每个参数至少每秒重发一次**，否则视为 "lost"，回落到之前控制它的东西（没人控制则回默认值） |
| 覆盖关系 | 只要我们持续发，API 值就覆盖 webcam/iOS/Android 的追踪值 |
| `weight`（可选） | `0..1`，与面捕值**加权混合**；不填 = 1（立刻完全接管）。用途：淡入接管，避免跳变 |
| `mode`（可选） | 缺省/`"set"` = 覆盖（**同一参数同时只能被一个插件 set**，被别人占了会报错）；`"add"` = 叠加（可多插件并发，此时忽略 `weight`） |
| `faceFound`（可选） | `true` 时让 VTS 认为"脸还在"，用来控制"追踪丢失"动画的播放时机 |

### 1.6 自定义参数（自定义追踪参数）

官方原文规则（README "Adding new tracking parameters"）：

| 项目 | 规则 |
| --- | --- |
| 名字 | 官方原文 "unique, alphanumeric (no spaces allowed) and have to be between 4 and 32 characters" |
| 重名 | 被别人插件建过 → **失败**；同插件重复创建 → **成功**，且可借此**覆盖** `min`/`max`/`defaultValue`。→ 官方那两句合起来看，"unique" 的准确语义是**跨插件唯一** |
| 下划线是否合法 | 官方只写 "alphanumeric"，**没有明确说下划线**。`INFERRED`：下划线不属于 alphanumeric，所以像 `Eye_Squint_L` 这种名字应当会被拒（内置的手部参数能带下划线，是因为它们不属于"自定义"） |
| `min`/`max`/`defaultValue` | 浮点 `±1000000`。⚠️ 官方原文："**They're the values that will be used as default lower and upper value when a new parameter mapping is created**" —— **不是值的硬上下限** |
| `explanation` | 可选，**< 256 字符**（按字符还是按字节官方未说明），用户查看参数详情时显示 |
| 配额 | 全局 **300** 个，单插件 **100** 个，超了报错 |
| 持久化 | `StreamingAssets/Config/custom_parameters.json` |
| 吊销 token | **该插件建的 custom 参数全部被删**；已用过它的模型里会显示为红字，插件可随时重新建回来 |

> **这条对我们最有用**：往外发自定义名时，`min/max/default` 是**给 UI 当默认范围用的元数据**，
> 不是钳制。所以将来若对接 VTS，输出行带 `min/max/default` 是有意义的（VTS 要报这三个值），
> 但真正钳制值的是我们自己的曲线定义域。
> ⚠️ **现状**：我们的输出行**现在没有** `min`/`max`/`default` 字段 ——
> `HoFaceOutput` 只有 `parameter` / `expression` / `curve` / `modifiers` / `notes`
> （`Runtime/FaceTracking/HoFaceMiddleware.cs:92`），定义域就是曲线关键点的范围。

### 1.7 还有一类输入：控制器（附录）

来源：[Controller-Input（官方 wiki）](https://github.com/DenchiSoft/VTubeStudio/wiki/Controller-Input)，**桌面 Steam 版**才有。
它同样属于"追踪参数"命名空间，官方给了完整范围，共 **39** 个：
摇杆 `ControllerStickLeftX/Y`、`ControllerStickRightX/Y`（`-1..1`）、`ControllerStickPressLeft/Right`（0/1）、
十字键 `ControllerDPadX/Y`（`-1/0/1`）、面键 `ControllerTriangle/Cross/Square/Circle`（0/1）、
`ControllerShoulderLeft/Right`（0/1）、`ControllerTriggerLeft/Right`（`0..1` 模拟）、
`ControllerOptionLeft/Right`、`ControllerHome`、`ControllerTouchPadPress`、`ControllerButtonsAnyLeft/Right`、
`ControllerButtonCountLeft/Right`（无上界）、`ControllerLean`（`-1..1`）、
`ControllerThumbPosLeft/Right`（0–3）、`ControllerFingerPosLeft/Right`（0–2）、
`ControllerTouchPadTouchCount`（0–2）、`ControllerTouchPadFinger{Left,Right}Active`（0/1）与 `…X/Y`（`0..1`）、
`ControllerOrientationX/Y/Z`（`-60..60`，陀螺仪欧拉角）。
⚠️ 触摸板与陀螺仪**只有 DualShock 4 / DualSense / DualSense Edge** 有（官方原文）。
我们做面捕用不到，列在这里是为了"参数全集"完整 —— 将来若要枚举"VTS 支持哪些输入"，别漏了这 39 个。

**来源与时间**：以上 VTS 内容抓取于 2026-09-24；官方 README 对应提交
`0f46ef44b487fa17c8120db572ebd925924b93a3`（2026-09-02），API 版本串 `"1.0"`。
**官方 wiki 不提供版本号或修订日期**，只能以抓取日期为准。

### 1.8 VTS 手机 "3rd Party PC Clients" 协议（我们现在唯一接的输入）

来源：[VTubeStudioBlendshapeUDPReceiverTest（官方示例仓库）](https://github.com/DenchiSoft/VTubeStudioBlendshapeUDPReceiverTest)，
载荷字段名取自 `Assets/VTubeStudioBlendshapeDataReceiver/VTubeStudioRawTrackingData.cs`。
App 侧开关：VTS 手机版设置第一页底部的 "3rd Party PC Clients"（README 原话：**"Apps like VSeeFace and VBridger use this."**）。

**它是请求式，不是推流**：PC 先往 `手机:21412`（UDP）发一条请求，手机再把数据发回**这个包的源 IP**、
端口用请求里 `ports` 列的那些（`Runtime/FaceTracking/HoVtsPacket.cs:62`）。
官方 README 原文：端口是 "`21412` **or whatever is displayed in the iOS app**"、`ports` 可列 **1–32 个**
（"so you can have multiple apps running on one PC that all receive the data on different ports"）、
数据 "typically at 60 FPS unless there is lag in the iPhone app"。

```json
{"messageType":"iOSTrackingDataRequest","time":5,"sentBy":"HoFaceTracking","ports":[49984]}
```

| 字段 | 约束（官方 README / 示例类型） | 我们的取值 |
| --- | --- | --- |
| `time` | 只允许 **0.5–10 秒**；官方建议 "send one request per second with `time` set to `5`" | `5`，每秒续一次（`Editor/FaceTracking/VtsIphoneReceiver.cs:39`） |
| `sentBy` | 长度 **1–64**，"currently only used for logging" | `HoFaceTracking` |
| `ports` | **至少一个、最多 32 个**；本机 UDP 监听端口 | 默认 `49984`（`VtsIphoneReceiver.cs:36`） |

**每帧载荷**（字段名与注释照抄官方 `VTubeStudioRawTrackingData`；README 原话 "Some fields may be
**added** to this payload in the future" → 未知字段一律跳过）：

| 字段 | 官方类型与注释 | 落到线名空间后叫什么 |
| --- | --- | --- |
| `Timestamp` | `long` —— "Current UNIX millisecond timestamp" | `Timestamp`（⚠️ float 只有约 7 位有效数字，**只够调试看个大概**） |
| `Hotkey` | `int`，**初值 `-1`** —— "Last pressed on-screen hotkey"（README：按键值是 **1–8**） | `Hotkey`（原样交出去）。见下面那段"`Hotkey` 到底怎么触发" |
| `FaceFound` | `bool` —— "Whether or not face has been found" | `FaceFound`（1/0）—— **这是我们选它而不选 iFacialMocap 的那个字段**（§6） |
| `Rotation` / `Position` | `Vector3` —— "Current face rotation" / "Current face position" | `Rotation_x/_y/_z`、`Position_x/_y/_z` |
| `EyeLeft` / `EyeRight` | `Vector3` —— "Left/Right eye rotation" | `EyeLeft_x/…`、`EyeRight_x/…` |
| `BlendShapes` | `List<VTSTrackingDataEntry>`，每项 `{k: string, v: float}` —— "Current iOS blendshapes" | **线名 = `k` 原样**、值 = `v` 原样 |

**形态键线名是 PascalCase**（`EyeBlinkLeft`、`JawOpen`），与 §3.1 的 PascalCase 列、VTS 的
`VTSARKitBlendshape` 枚举一致；**数值是 iOS 的原始 0..1，接收端不换算**（`HoVtsPacket.cs:160`）。
所以「新建配置」的初始输入行里 VTS 那一套是原样直通，只有 iFacialMocap 那一套要 `× 0.01`。
⚠️ **载荷里的 `k` 是字符串、不是枚举**：官方类型里那个
`Dictionary<VTSARKitBlendshape, float> BlendShapeDictionary` 是**收方自己填的**（注释：
"Not sent over network, filled on receiver side"）—— 别以为线上传的是枚举序号。

> ⚠️ **别用 `JsonUtility.FromJson` 解这个载荷**（本机实测，已记在 `HoVtsPacket.cs:8` 与
> `Runtime/FaceTracking/HoJson.cs`）：`BlendShapes` 是 `List<嵌套类>`，`JsonUtility` 会**静默丢掉它** ——
> 12 个头/眼分量全在、**52 个形态键全丢**，解析还"成功"。我们走自己的 `HoJsonReader`。

> ⚠️ **官方示例仓库这个名字有点误导**：它演示的是**同一台 iPhone 上 VTS App 转发 iOS blendshape 数据**
> （VSeeFace 也吃这条，见 §4.5），而不是"手机从 VTS 拿追踪参数"。**它是 PC 客户端收包，不是 PC 客户端读 VTS 的参数**。

#### `Hotkey` 到底怎么触发（2026-09-25 联网复核官方源码）

两个官方来源合起来才能读全这个字段：

1. **载荷定义的官方注释**（`VTubeStudioRawTrackingData.cs`）：
   ```csharp
   /// <summary>
   /// Last pressed on-screen hotkey.
   /// </summary>
   public int Hotkey = -1;
   ```
2. **官方 README 的字段清单**：`Any on-screen hotkey pressed? (int between 1 and 8)`。
3. **官方接收端示例**（`VTubeStudioBlendshapeDataReceiver.cs`）说明了**怎么用**它：
   ```csharp
   if (receivedTrackingData.Hotkey != -1)
   {
       HotkeyReceived?.Invoke(receivedTrackingData.Hotkey);
   }
   ```
   ⇒ 官方自己就是**把 `≠ -1` 当成"有人按了热键"这个事件**，`-1` 是"没有"。

**结论（能确定的）**：
* `-1` = **没有热键值**（类型定义里的初值）；**1–8 = 最后按下的那个屏幕热键的编号**。
* 它是**事件语义**，不是"当前状态"：**没有"松开"这个信号**，只有"最近按过第 N 个"。
  要拿它做事，判据是 `Hotkey != -1`、并自己处理"同一个键连按两下"（值不会变，得配时间/去抖）。
* **它跟"键盘按键"无关** —— 是 **on-screen hotkey**，即 VTube Studio app 里的热键按钮。

**未能确认的**：官方 wiki（`DenchiSoft/VTubeStudio` 的 Home / 各页）**没有**写手机版（iOS/Android）
上屏幕热键具体怎么按、手机版有没有这个界面。所以"手机版能不能触发这条"必须**实测**。
本机安卓实测：**整场 173 帧日志里 `Hotkey` 只出现过 `-1`**（对照：同一份日志里 `FaceFound`
有 0/1 两值、`Rotation_x` 有 2422 种取值）——即**这次捕捉里它一次都没被触发过**，原因未知
（没按 / 手机版没有该界面 / 手机版不填这个字段，三者未区分）。

**来源与时间**：官方示例仓库 `DenchiSoft/VTubeStudioBlendshapeUDPReceiverTest` 的 README
（`raw.githubusercontent.com/.../main/README.md`）与载荷类型定义
（`Assets/VTubeStudioBlendshapeDataReceiver/VTubeStudioRawTrackingData.cs`），
**2026-09-25 联网复核，逐条对上**。`Runtime/FaceTracking/HoVtsPacket.cs:55` 里记的
"`time` ∈ [0.5, 10]、`sentBy` 长度 1–64、`ports` 至少一个"与官方 README 一致。

---

## 2. Cubism 标准参数表（`Param*` 的惯例）

来源：[Live2D Cubism Editor Manual – Standard Parameter List](https://docs.live2d.com/en/cubism-editor-manual/standard-parameter-list/)
（页面标注 **Updated: 08/26/2021**，Cubism Editor 5）。

**官方原则原文**："**The eyes and mouth should be set to 0 when normally closed and 1 when normally open.**"
并补充："If you want to close tightly, open wide, etc., you can add outside the range in 0.1 units."
（要更紧闭/更张开，就按 0.1 单位往外扩。）

### 2.1 标准参数全表

标 `*` 的是官方标注"按需输入"（Parameters marked with * are to be input if necessary）。

| Name | ID | min | default | max | 方向（官方原文） | 备注 |
| --- | --- | --- | --- | --- | --- | --- |
| Angle X | `ParamAngleX` | -30 | 0 | 30 | + 为屏幕右侧 | 想转更多就设 ±45 等 |
| Angle Y | `ParamAngleY` | -30 | 0 | 30 | + 为抬头 | |
| Angle Z | `ParamAngleZ` | -30 | 0 | 30 | + 为屏幕右侧 | |
| Left eye Open/Close | `ParamEyeLOpen` | 0 | **1** | 1 | + 为睁 | 想更睁大就把 max 提到 1.5/2；想紧闭把 min 设 -0.5 |
| Left eye Smiling | `ParamEyeLSmile` | 0 | 0 | 1 | + 为笑眼 | |
| Right eye Open/Close | `ParamEyeROpen` | 0 | **1** | 1 | + 为睁 | 同左 |
| Right eye Smiling | `ParamEyeRSmile` | 0 | 0 | 1 | + 为笑眼 | |
| Eyeball X | `ParamEyeBallX` | -1 | 0 | 1 | + 为看右 | |
| Eyeball Y | `ParamEyeBallY` | -1 | 0 | 1 | + 为看上 | |
| Eyeball scaling `*` | `ParamEyeBallForm` | -1 | 0 | 1 | -1 变小 / 0 标准 / 1 变大 | 只缩小用 -1..0，只放大用 0..1 |
| Left eyebrow Up/Down | `ParamBrowLY` | -1 | 0 | 1 | + 为抬眉 | |
| Right eyebrow Up/Down | `ParamBrowRY` | -1 | 0 | 1 | + 为抬眉 | |
| Left eyebrow Left/Right | `ParamBrowLX` | -1 | 0 | 1 | **- 为眉头靠拢** | 注意对齐方向（左眉对齐左边） |
| Right eyebrow Left/Right | `ParamBrowRX` | -1 | 0 | 1 | **- 为眉头靠拢** | 同上 |
| Left eyebrow Angle | `ParamBrowLAngle` | -1 | 0 | 1 | **- 表愤怒** | 负值时左眉向左下 |
| Right eyebrow Angle | `ParamBrowRAngle` | -1 | 0 | 1 | **- 表愤怒** | 负值时右眉向右下 |
| Left eyebrow Deformation | `ParamBrowLForm` | -1 | 0 | 1 | - 表愤怒 | |
| Right eyebrow Deformation | `ParamBrowRForm` | -1 | 0 | 1 | - 表愤怒 | |
| Mouth Deformation | `ParamMouthForm` | -1 | 0 | 1 | + 笑嘴 / - 怒嘴 | |
| Mouth Open/Close | `ParamMouthOpenY` | 0 | 0 | 1 | + 为张开 | 想更张就把 max 提到 1.5 |
| Cheek | `ParamCheek` | 0 | **As appropriate** | 1 | + 为脸红 | 默认值按角色设定 |
| Body rotation X | `ParamBodyAngleX` | -10 | 0 | 10 | + 为屏幕右侧 | |
| Body rotation Y | `ParamBodyAngleY` | -10 | 0 | 10 | + 为屏幕上 | |
| Body rotation Z | `ParamBodyAngleZ` | -10 | 0 | 10 | + 为屏幕右侧倾斜 | |
| Breath | `ParamBreath` | 0 | 0 | 1 | + 为吸气 | |
| Left arm A `*` | `ParamArmLA` | -30 | 0 | 30 | + 为展开 | 动作大时也用 50/100；动作小用 10 |
| Right arm A `*` | `ParamArmRA` | -30 | 0 | 30 | + 为展开 | |
| Left arm B `*` | `ParamArmLB` | -30 | 0 | 30 | + 为展开 | |
| Right arm B `*` | `ParamArmRB` | -30 | 0 | 30 | + 为展开 | |
| Left hand `*` | `ParamHandL` | -10 | 0 | 10 | + 为变形 | |
| Right hand `*` | `ParamHandR` | -10 | 0 | 10 | + 为变形 | |
| Hair sway Front | `ParamHairFront` | -1 | 0 | 1 | + 为屏幕右侧 | **通常由物理驱动** |
| Hair sway Sideways | `ParamHairSide` | -1 | 0 | 1 | + 为屏幕右侧 | 通常由物理驱动 |
| Hair sway Back | `ParamHairBack` | -1 | 0 | 1 | + 为屏幕右侧 | 通常由物理驱动 |
| Hair sway Fluffy `*` | `ParamHairFluffy` | -1 | 0 | 1 | + 为蓬松外扩 | |
| Shrug `*` | `ParamShoulderY` | -10 | 0 | 10 | + 为耸肩 | |
| Breast physics `*` | `ParamBustX` / `ParamBustY` | -1 / -1 | 0 / 0 | 1 / 1 | + 为向右摆 / + 为向上摆 | |
| Overall Left/right `*` | `ParamBaseX` | -10 | 0 | 10 | + 为屏幕右侧 | |
| Overall Up/down `*` | `ParamBaseY` | -10 | 0 | 10 | + 为屏幕上 | |

**三个陷阱（官方原文摆在那，但很容易漏）**：

- `ParamEyeLOpen` / `ParamEyeROpen` 的 **default 是 1，不是 0** —— 眼睛默认是睁的。
  按"所有参数默认 0"去初始化，会得到**永久闭眼**。
- `ParamCheek` 的 default 官方写 "As appropriate"（按角色），**没有统一默认值**。
- **表里的 min/max 是"建议基准"，不是硬边界**：官方备注明说"想更睁大就把 max 提到 1.5、2"、
  "想转更多就设 ±45"、"想紧闭就把 min 设 -0.5" —— 这些扩出来的值**没有**写进表的 min/max 列。
- 眼与嘴的"闭 = 0、开 = 1"是**原则**（官方原文 "The eyes and mouth should be set to 0 when normally closed
  and 1 when normally open"），不是协议强制的边界。
- `ParamAngleZ` 官方给的方向描述**和 `ParamAngleX` 字面一模一样**（都是"+ 为屏幕右侧"），
  而 Z 轴其实是倾斜 —— 官方 EN/ZH 两版都这样写，**疑似笔误但无法确认**，本表照抄原文。

### 2.2 标准参数组（`ParamGroup*`）

官方给的可选参数组 ID（做 Live2D 侧分类用；我们可以拿它当"这部分属于脸/头/眼/眉/嘴/身体"的分类依据）：

| 组 | ID | 组 | ID |
| --- | --- | --- | --- |
| Face | `ParamGroupFace` | Hands | `ParamGroupHands` |
| Head | `ParamGroupHead` | Left hand | `ParamGroupHandL` |
| Eyes | `ParamGroupEyes` | Right hand | `ParamGroupHandR` |
| Eyeballs | `ParamGroupEyeballs` | Arms | `ParamGroupArms` |
| Eyebrows | `ParamGroupBrows` | Left arm | `ParamGroupArmL` |
| Mouth | `ParamGroupMouth` | Right arm | `ParamGroupArmR` |
| Body | `ParamGroupBody` | Legs | `ParamGroupLegs` |
| Left leg | `ParamGroupLegL` | Right leg | `ParamGroupLegR` |
| Sway | `ParamGroupSway` | Facial expression | `ParamGroupExpression` |
| Hair | `ParamGroupHair` | All | `ParamGroupOverall` |

**来源与时间**：Live2D 官方 Editor Manual（英文版），页面标注 Updated 2021-08-26，抓取于 2026-09-24。

---

## 3. ARKit 52（3D 侧的通用词汇表）

来源：Apple [`ARFaceAnchor.BlendShapeLocation`](https://developer.apple.com/documentation/arkit/arfaceanchor/blendshapelocation)；
下表的中文语义照抄 **Unity 官方枚举文档**（[`ARKitBlendShapeLocation`](https://docs.unity3d.com/Packages/com.unity.xr.arkit@5.1/api/UnityEngine.XR.ARKit.ARKitBlendShapeLocation.html)，
package `com.unity.xr.arkit@5.1.6`），因为它逐条给了官方一句话描述。

**值域（Unity 文档原文）**："A coefficient of zero for the feature represents the neutral position,
while a coefficient of one represents the fully articulated position." → **0 = 中性，1 = 完全做出该动作**。

### 3.1 全表（52 个）

`camelCase` 列是 Apple 的规范拼写（VMC 生态用这一列；也是**我们内部 52 个规范名**，
`Runtime/FaceTracking/HoFaceTrackingChannels.cs:46`），`PascalCase` 列是
Unity 枚举与 VTS 枚举的拼写（也是 **VTS 手机包发来的形态键线名**，§1.8）。
⚠️ **iFacialMocap 的线名是第三套**：它把左右后缀写成 `_L/_R`（`eyeBlink_L`），
而 `jawLeft` / `jawRight` / `mouthLeft` / `mouthRight` 这 4 个**不带后缀、保持 camelCase**
（这条规则原文在 iFacialMocap 官方开发者文档 §6.2；我们**不再接**这个协议，代码里也没有这个换算函数了 ——
以前它是 `HoFaceMiddlewareDefaults.IFacialWire`，那个类 2026-09-26 已删。⚠️ 顺带记住：**安卓版 VTS
发出来的形态键恰好就是这个 `_L/_R` 方言**，见 mod `README.md` §1.4）。

| # | camelCase（Apple / VMC / 我们的规范名） | PascalCase（Unity / VTS / VTS 手机线名） | 中文语义（照抄 Unity 官方描述） |
| --- | --- | --- | --- |
| 1 | `browDownLeft` | `BrowDownLeft` | 左眉外端下压 |
| 2 | `browDownRight` | `BrowDownRight` | 右眉外端下压 |
| 3 | `browInnerUp` | `BrowInnerUp` | 双眉内端上抬 |
| 4 | `browOuterUpLeft` | `BrowOuterUpLeft` | 左眉外端上抬 |
| 5 | `browOuterUpRight` | `BrowOuterUpRight` | 右眉外端上抬 |
| 6 | `cheekPuff` | `CheekPuff` | 双颊向外鼓 |
| 7 | `cheekSquintLeft` | `CheekSquintLeft` | 左眼下方/周围颊部上抬 |
| 8 | `cheekSquintRight` | `CheekSquintRight` | 右眼下方/周围颊部上抬 |
| 9 | `eyeBlinkLeft` | `EyeBlinkLeft` | 左眼睑闭合 |
| 10 | `eyeBlinkRight` | `EyeBlinkRight` | 右眼睑闭合 |
| 11 | `eyeLookDownLeft` | `EyeLookDownLeft` | 左眼向下看 |
| 12 | `eyeLookDownRight` | `EyeLookDownRight` | 右眼向下看 |
| 13 | `eyeLookInLeft` | `EyeLookInLeft` | 左眼向**右**看（向鼻侧） |
| 14 | `eyeLookInRight` | `EyeLookInRight` | 右眼向**左**看（向鼻侧） |
| 15 | `eyeLookOutLeft` | `EyeLookOutLeft` | 左眼向**左**看（向颞侧） |
| 16 | `eyeLookOutRight` | `EyeLookOutRight` | 右眼向**右**看（向颞侧） |
| 17 | `eyeLookUpLeft` | `EyeLookUpLeft` | 左眼向上看 |
| 18 | `eyeLookUpRight` | `EyeLookUpRight` | 右眼向上看 |
| 19 | `eyeSquintLeft` | `EyeSquintLeft` | 左眼周围收缩（眯） |
| 20 | `eyeSquintRight` | `EyeSquintRight` | 右眼周围收缩（眯） |
| 21 | `eyeWideLeft` | `EyeWideLeft` | 左眼上下眼睑张开 |
| 22 | `eyeWideRight` | `EyeWideRight` | 右眼上下眼睑张开 |
| 23 | `jawForward` | `JawForward` | 下颌前伸 |
| 24 | `jawLeft` | `JawLeft` | 下颌向左 |
| 25 | `jawOpen` | `JawOpen` | 下颌张开 |
| 26 | `jawRight` | `JawRight` | 下颌向右 |
| 27 | `mouthClose` | `MouthClose` | **双唇闭合**（与下颌无关，独立于 jawOpen） |
| 28 | `mouthDimpleLeft` | `MouthDimpleLeft` | 左嘴角向后拉（酒窝） |
| 29 | `mouthDimpleRight` | `MouthDimpleRight` | 右嘴角向后拉 |
| 30 | `mouthFrownLeft` | `MouthFrownLeft` | 左嘴角向下 |
| 31 | `mouthFrownRight` | `MouthFrownRight` | 右嘴角向下 |
| 32 | `mouthFunnel` | `MouthFunnel` | 双唇收成"O 形张开" |
| 33 | `mouthLeft` | `MouthLeft` | 双唇整体向左 |
| 34 | `mouthLowerDownLeft` | `MouthLowerDownLeft` | 左侧下唇向下 |
| 35 | `mouthLowerDownRight` | `MouthLowerDownRight` | 右侧下唇向下 |
| 36 | `mouthPressLeft` | `MouthPressLeft` | 左侧下唇向上压 |
| 37 | `mouthPressRight` | `MouthPressRight` | 右侧下唇向上压 |
| 38 | `mouthPucker` | `MouthPucker` | 双唇收拢压紧（嘟嘴） |
| 39 | `mouthRight` | `MouthRight` | 双唇整体向右 |
| 40 | `mouthRollLower` | `MouthRollLower` | 下唇向内卷 |
| 41 | `mouthRollUpper` | `MouthRollUpper` | 上唇向内卷 |
| 42 | `mouthShrugLower` | `MouthShrugLower` | 下唇向外（耸） |
| 43 | `mouthShrugUpper` | `MouthShrugUpper` | 上唇向外（耸） |
| 44 | `mouthSmileLeft` | `MouthSmileLeft` | 左嘴角向上 |
| 45 | `mouthSmileRight` | `MouthSmileRight` | 右嘴角向上 |
| 46 | `mouthStretchLeft` | `MouthStretchLeft` | 左嘴角向左 |
| 47 | `mouthStretchRight` | `MouthStretchRight` | 右嘴角向右（⚠️ Unity 官方英文描述写成 "left corner"，是官方笔误） |
| 48 | `mouthUpperUpLeft` | `MouthUpperUpLeft` | 左侧上唇向上 |
| 49 | `mouthUpperUpRight` | `MouthUpperUpRight` | 右侧上唇向上 |
| 50 | `noseSneerLeft` | `NoseSneerLeft` | 左侧鼻翼上提 |
| 51 | `noseSneerRight` | `NoseSneerRight` | 右侧鼻翼上提 |
| 52 | `tongueOut` | `TongueOut` | 伸舌 |

**分类小计（核对用）**：

| 组 | 个数 | 成员 |
| --- | --- | --- |
| 眼睑/眼周 | 6 | `eyeBlink`×2、`eyeSquint`×2、`eyeWide`×2 |
| 眼动 | 8 | `eyeLookDown/In/Out/Up` ×2 |
| 下颌 | 4 | `jawForward`、`jawLeft`、`jawOpen`、`jawRight` |
| 嘴 | 23 | `mouthClose`、`mouthDimple`×2、`mouthFrown`×2、`mouthFunnel`、`mouthLeft`、`mouthLowerDown`×2、`mouthPress`×2、`mouthPucker`、`mouthRight`、`mouthRollLower`、`mouthRollUpper`、`mouthShrugLower`、`mouthShrugUpper`、`mouthSmile`×2、`mouthStretch`×2、`mouthUpperUp`×2 |
| 颊 | 3 | `cheekPuff`、`cheekSquint`×2 |
| 鼻 | 2 | `noseSneer`×2 |
| 眉 | 5 | `browDown`×2、`browInnerUp`、`browOuterUp`×2 |
| 舌 | 1 | `tongueOut` |
| **合计** | **52** | 6+8+4+23+3+2+5+1 = 52 ✅ |

**Apple 自己的官方分组**（用来核对"我们有没有漏"）：docs JSON API 里分成 5 组 ——
**Left Eye 7** / **Right Eye 7** / **Mouth and Jaw 27** / **Eyebrows, Cheeks, and Nose 10** / **Tongue 1** = 52。
和上表的关系：Left Eye 7 = 眼睑 3 + 眼动 4；Mouth and Jaw 27 = 嘴 23 + 下颌 4；
Eyebrows/Cheeks/Nose 10 = 眉 5 + 颊 3 + 鼻 2。**两组口径合计都是 52**。

### 3.2 拼写来源对照（五条路）

| 来源 | 拼写 | 用在哪 | 依据 |
| --- | --- | --- | --- |
| **Apple** `ARFaceAnchor.BlendShapeLocation` | camelCase（`eyeBlinkLeft`） | iOS 原生 / VMC 生态 / **我们的 52 个规范名** | Apple 文档 |
| **Unity** `ARKitBlendShapeLocation` | PascalCase（`EyeBlinkLeft`） | Unity ARKit 包 | Unity 文档（枚举名逐条给了 Apple 文档链接） |
| **VTS** `VTSARKitBlendshape` | PascalCase，**顺序照抄 Apple** | VTS 的 iOS blendshape UDP 接收 | [VTSARKitBlendshape.cs](https://github.com/DenchiSoft/VTubeStudioBlendshapeUDPReceiverTest/blob/main/Assets/VTubeStudioBlendshapeDataReceiver/VTSARKitBlendshape.cs) 文件头原文："Names and order taken from https://developer.apple.com/..." |
| **VTS 手机**（"3rd Party PC Clients" 的 JSON 包，§1.8） | **PascalCase，与 `VTSARKitBlendshape` 同一套** | VTS 手机直接发给本机（形态键线名就是这一列） | 官方示例仓库 `VTubeStudioBlendshapeUDPReceiverTest` 的类型定义 + 本机实测（`HoVtsPacket.cs`） |
| **iFacialMocap**（§6，**我们不再接**） | camelCase + `_L/_R` 后缀（`eyeBlink_L`；4 个键不带后缀） | 官方线协议 | 官方开发者文档 §6.2（换算函数 `IFacialWire` 随内置默认表 2026-09-26 一起删了，规则本身照官方文档） |
| **VMC / VRM 生态** | camelCase | `/VMC/Ext/Blend/Val` 的 name | VMC 官方 spec 直接链接 Unity 的 ARKit 枚举文档作为"高级面捕"参考 |

> ⚠️ VMC 官方明确：**大小写敏感**（"due to changes in the UniVRM specification, it is Case Sensitive"）。
> 发 camelCase 是对的；宽容实现（含 EVMC4U）会忽略大小写，但"不宽容的实现就不好使"。
> ⚠️ **不要把"顺序"当成同一份表 —— 按索引映射名字一定会错位，必须按名字映射。** 实测证据：
> Apple 官方分组顺序（docs JSON API 抓取）里 "Mouth and Jaw" 的开头是
> `jawForward,` **`jawLeft, jawRight`** `, jawOpen …`；而 VTS 的 `VTSARKitBlendshape.cs`
> （文件头自称 "Names and order taken from developer.apple.com"）写的是
> `JawForward,` **`JawRight, JawLeft`** `, JawOpen` —— **名字一致、顺序不同**。
> （VBridger 的 `vtsKeys` 恰恰是按**索引**对齐的：见
> [VBridger 的输入 / 输出参数格式](archive/VBRIDGER_IO_VOCABULARY.md) §1.1，**别照抄那个做法**。）

### 3.3 已知坑（写死在这，别再踩）

| 坑 | 说明 |
| --- | --- |
| `mouthClose` ≠ 闭嘴的"闭" | 它是"双唇闭合"，**独立于下颌**：`jawOpen` 高 + `mouthClose` 高 = 张着嘴但抿唇。Apple 自己还警告 `jawOpen` 单独拉高会不自然。**不要写成 `mouthClose = 1 − jawOpen`** |
| `eyeLookIn` / `eyeLookOut` 方向 | **按脸自身定义**：`eyeLookInLeft` = 左眼向**脸右侧**看（即向中线）。而 ARKit 预览画面是**镜像**的，所以"画面里向内"与 `eyeLookIn` 不是一回事 —— 调试时最容易把 L/R 判反 |
| `eyeSquint` ≠ `eyeBlink` | `eyeBlink` 是上眼睑闭合，`eyeSquint` 是**眼周**收缩，两者可同时非零 |
| 没有左右后缀的键 | `cheekPuff`、`browInnerUp`、`jawForward/Left/Right/Open`、`mouthClose/Funnel/Pucker/Left/Right/Roll*/Shrug*` 都是单键 —— 别去找 `cheekPuffLeft` |
| `mouthFunnel` vs `mouthPucker` | Funnel = **张开**的圆（漏斗）；Pucker = **闭合**双唇的收拢压缩（嘬嘴） |
| `mouthRollLower/Upper` | 官方语义是"**向口腔内侧**卷"（往牙齿方向），不是向外翻 |
| `mouthShrugLower/Upper` | 官方是 "**outward** movement"（向外），不是"向上耸" |
| `browDown*` / `noseSneer*` | `browDown*` 只指**眉外侧**（"outer portion"）；`noseSneer*` 是"鼻翼**周围**上提" |
| `jawForward` | 是下颌**向前平移**，与 `jawOpen`（张开角度）正交；**具体是哪根轴官方没给**。很多追踪源对它恒发 0，不要拿它当"张嘴" |
| `tongueOut` / `cheekPuff` 的平台支持 | 见 §1.1：只有手机端有（`cheekPuff` 只 iOS），webcam 永远没有数据；`tongueOut` 的 1.0 是"ARKit 能追踪到的最大程度"而非物理极限 |
| **逐键机型门槛表不存在** | Apple 只给了整体要求（iOS 14 / 带 Neural Engine，或 iOS 13 及以下必须 TrueDepth）与 `tongueOut` 的 iOS 12.0；**没有"哪个键需要哪颗芯片"的官方矩阵** → 这类说法一律不要引用 |
| 左右对称性 | 52 个里并非全部左右成对：做"单根轴"时要显式决定用左、右还是平均 |
| `mouthStretchRight` 的官方描述 | **Apple 自己写成 "the left corner"**，Unity 写 "right corner" —— 按对称性判断 Apple 原文是笔误；别把这句抄进注释 |

**来源与时间**：Apple `ARFaceAnchor.BlendShapeLocation`（正文是 JS 渲染的，抓的是它的
docs JSON API `developer.apple.com/tutorials/data/documentation/arkit/arfaceanchor/blendshapelocation.json`）
+ Unity `com.unity.xr.arkit@5.1.6` 枚举文档 + VTS 的枚举源码，抓取于 2026-09-24。

---

## 4. VMC Protocol（3D 侧的通讯协议）

来源：[VMC Protocol specification（官方英文版）](https://protocol.vmc.info/english)（另有[日文版](https://protocol.vmc.info/)，本表以英文版为准）。
协议本体：**OSC over UDP/IP**，UTF-8，常用端口 **39539**（Marionette 收）/ **39540**（Performer 收、Assistant 发），
允许 bundle，发送周期未定义，**收方应丢弃不需要的消息**，未知地址与参数过多应忽略。

**角色术语（官方原文）**：`Marionette` = 收动作并渲染（如 EVMC4U）；`Performer` = 处理动作与 IK 并发送
（如 VMC 本体、VSeeFace）；`Assistant` = 只发部分骨骼与表情（可选）。

### 4.1 地址总表

| 地址 | 参数（按顺序） | 语义 | 版本 |
| --- | --- | --- | --- |
| `/VMC/Ext/OK` | `(int)loaded` | 模型是否加载（0/1） | 基础 |
| `/VMC/Ext/OK` | `+ (int)calibrationState (int)calibrationMode` | 标定状态（0 未标定 / 1 等待 / 2 标定中 / 3 已标定）、标定模式（0 Normal / 1 MR Normal / 2 MR Floor fix） | V2.5 |
| `/VMC/Ext/OK` | `+ (int)trackingStatus` | 追踪状态（1 OK / 0 Bad）。⚠️ 官方注明 **V2.7 在 VirtualMotionCapture 本体里未实现** | V2.7 |
| `/VMC/Ext/T` | `(float)time` | 发送方相对时间，主要用来确认"通不通" | 基础 |
| `/VMC/Ext/Root/Pos` | `(string)name (float)px py pz (float)qx qy qz qw` | 模型根绝对变换（`name` = `"root"`）。官方建议收方**当作本地姿态**处理 | v2.0 |
| `/VMC/Ext/Root/Pos` | `+ (float)sx sy sz (float)ox oy oz` | 追加 MR 合成用的缩放与偏移（可把 avatar 调到真实身高） | v2.1 |
| `/VMC/Ext/Bone/Pos` | `(string)name (float)px py pz (float)qx qy qz qw` | 骨骼局部变换。**`name` = Unity `UnityEngine.HumanBodyBones` 的类型名**；官方原文："All HumanBodyBones will be send. (Include eye bone and finger bones)" | 基础 |
| `/VMC/Ext/Blend/Val` | `(string)name (float)value` | 形态键值。官方原文："BlendShapeProxy value in VRM model." 面捕与口型走这条 | 基础 |
| `/VMC/Ext/Blend/Apply` | 无参 | 一批 blend 发完后发一次，收方此刻才应用 | 基础 |
| `/VMC/Ext/Cam` | `(string)name (float)px py pz (float)qx qy qz qw (float)fov` | 摄像机变换与 FOV | V2.1 |
| `/VMC/Ext/Con` | `(int)active (string)name (int)IsLeft (int)IsTouch (int)IsAxis (float)ax ay az` | 手柄按键/轴（active：1 按下 / 0 抬起 / 2 轴变化） | V2.1 |
| `/VMC/Ext/Key` | `(int)active (string)name (int)keycode` | 键盘输入 | V2.1 |
| `/VMC/Ext/Midi/Note` | `(int)active (int)channel (int)note (float)velocity` | MIDI 音符 | V2.2 |
| `/VMC/Ext/Midi/CC/Val` | `(int)knob (float)value` | MIDI CC 值（⚠️ 同名地址在**两个方向版本不同**：Marionette 方向 V2.2 / Performer 方向 V2.3） | V2.2 / V2.3 |
| `/VMC/Ext/Midi/CC/Bit` | `(int)knob (int)active` | MIDI CC 按钮 | V2.2 |
| `/VMC/Ext/Hmd/Pos`、`/VMC/Ext/Con/Pos`、`/VMC/Ext/Tra/Pos` | `(string)serial (float)px py pz (float)qx qy qz qw` | 设备变换（`serial` = OpenVR 序列号）；`Pos` 是 avatar 尺度，`Pos/Local` 是设备原始尺度 | V2.2 / Local V2.3 |
| `/VMC/Ext/Rcv` | `(int)enable (int)port` | 开关接收（低频率）。V2.7 追加 `(string)IP Address`，但**V2.7 未在 VMC 本体实现** | v2.4 |
| `/VMC/Ext/Light` | `(string)name (float)px py pz (float)qx qy qz qw (float)rgba` | 平行光变换与颜色（低频率） | V2.4（Performer 方向 V2.9） |
| `/VMC/Ext/VRM` | `(string)path (string)title` | 本地 VRM 文件信息（低频率），建议做差异检测。V2.7 追加 `(string)Hash` | V2.4 |
| `/VMC/Ext/Remote` | `(string)service (string)json` | 在线服务 avatar 信息（低频率），如 vroidhub / dmmvrconnect | V3.0 |
| `/VMC/Ext/Opt` | `(string)option` | 通用设置字符串（收方自定义） | V2.4 |
| `/VMC/Ext/Setting/Color` | `(float)r g b a` | 背景色 | V2.4 |
| `/VMC/Ext/Setting/Win` | `(int)IsTopMost IsTransparent WindowClickThrough HideBorder` | 窗口属性（1=true） | V2.4 |
| `/VMC/Ext/Config` | `(string)path` | 已加载的配置文件路径 | V2.5 |
| `/VMC/Ext/Set/Period` | `(int)Status Root Bone BlendShape Camera Devices` | 设置各类消息发送周期，值 = 1/x 帧 | V2.3 |
| `/VMC/Ext/Set/Eye` | `(int)enable (float)px py pz` | 视线目标位置。⚠️ **破坏性变更**：V2.3–V2.7 是**绝对坐标**，**V2.8 起改为头相对坐标** | V2.3 |
| `/VMC/Ext/Set/Req` | 无参 | 请求立即发送一次 | V2.4 |
| `/VMC/Ext/Set/Res` | `(string)Response` | 通用响应字符串 | V2.4 |
| `/VMC/Ext/Set/Calib/Ready` | 无参 | 请求"准备标定" | V2.5 |
| `/VMC/Ext/Set/Calib/Exec` | `(int)mode` | 请求执行标定（0/1/2 同 calibrationMode）。官方提醒 Ready 与 Exec 之间要留足时间 | V2.5 |
| `/VMC/Ext/Set/Config` | `(string)Path` | 请求加载配置文件 | V2.5 |
| `/VMC/Ext/Set/Shortcut` | `(string)shortcut` | 调用 VMC 快捷方式，如 `Functions.FreeCamera` | V3.1 |
| `/VMC/Thru/xxx/xxx` | `(string)arg1 [(float)arg2 \| (int)arg2]` | 厂商自定义透传；**Performer 必须把它从 Assistant 原样透传到 Marionette** | V2.6 |

### 4.2 值语义与范围（协议到底规定了什么）

| 问题 | 结论 | 依据 |
| --- | --- | --- |
| `/VMC/Ext/Blend/Val` 的值域 | **协议不给范围**。spec 只说 "(float)value"、语义是 "VRM BlendShapeProxy value"。所谓 `0..1` 来自 **VRM 表达式规范**（§5.2），不是 VMC 协议 | spec 原文 |
| blend 的名字空间 | **收方模型自己的 BlendShape / Expression 名**，不是全局表；**大小写敏感**（"due to changes in the UniVRM specification, it is Case Sensitive"）。宽容实现（含 EVMC4U）忽略大小写 | spec 原文 |
| 骨骼位置单位 | `p=Position`。**VMC spec 全文没有写单位**；"米"这个结论来自 **VRM 0.0 规范**的 "The unit of distance is meter"（间接归因，不要写成"VMC 规定米"） | spec + VRM 0.x 规范 |
| 骨骼旋转 | `q=Quaternion`（`qx qy qz qw` 顺序） | spec 原文 |
| VRM1 的骨骼姿态 | 官方明确建议发 **未经 ControlRig 归一化的原始骨骼姿态**（`instance.Humanoid.GetBoneTransform`），发归一化姿态"不禁止但不推荐"，且必须做成默认关闭的选项 | spec 原文 |
| 未知地址 / 参数过少或类型不符 | **必须忽略**，不要报错崩溃 | spec 原文 |
| 要不要 clamp 到 `0..1` | **协议没有要求**：官方参考接收端不做任何 clamp。我们发的时候也不要自作主张 clamp，收的时候按下游语义处理 | spec + 官方参考实现 |
| `/VMC/Ext/Set/Enabled` | ⚠️ **官方 spec（V3.1）里没有这个地址**（英文页、日文页、官方参考实现的地址白名单三处都没有）。**不要实现也不要依赖它** | spec 全文 + 参考实现 |
| 高级面捕用哪套名字 | spec 直接把高级面捕指向 hinzka 的 PerfectSync 说明 + **Unity 的 ARKit 枚举文档** → ARKit 52 就是事实词汇表 | spec 原文链接 |

### 4.3 骨名表（`UnityEngine.HumanBodyBones`）

来源：[Unity Scripting API – HumanBodyBones](https://docs.unity3d.com/ScriptReference/HumanBodyBones.html)
（Unity 6.6 / 6000.6 文档，构建于 2026-09-21）。`/VMC/Ext/Bone/Pos` 的 `name` 必须取自这里。

| 骨（枚举名） | 官方一句话 | 脸部相关 |
| --- | --- | --- |
| `Hips` | 胯 | |
| `Spine` / `Chest` / `UpperChest` | 第 1 节脊椎 / 胸 / 上胸 | |
| `Neck` | 颈 | ✅ 常用 |
| `Head` | 头 | ✅ 常用 |
| `LeftEye` / `RightEye` | 左/右眼 | ✅ 常用 |
| `Jaw` | 下颌 | ✅ |
| `LeftUpperLeg` / `RightUpperLeg` | 大腿 | |
| `LeftLowerLeg` / `RightLowerLeg` | 膝 | |
| `LeftFoot` / `RightFoot` | 踝 | |
| `LeftToes` / `RightToes` | 脚趾 | |
| `LeftShoulder` / `RightShoulder` | 肩 | |
| `LeftUpperArm` / `RightUpperArm` | 上臂 | |
| `LeftLowerArm` / `RightLowerArm` | 肘 | |
| `LeftHand` / `RightHand` | 腕 | |
| `LeftThumbProximal/Intermediate/Distal` 等 | 左手指骨：Thumb / Index / Middle / Ring / Little × Proximal / Intermediate / Distal（**共 15 根**） | |
| `RightThumb…` / `RightIndex…` / `RightMiddle…` / `RightRing…` / `RightLittle…` 各 3 节 | 右手指骨（**共 15 根**） | |
| `LastBone` | 枚举结束的哨兵值，**不是骨** | |

**计数**：真实骨骼 **55** 根（Hips…RightLittleDistal）+ `LastBone` 哨兵 = 枚举 56 项。
（⚠️ 枚举的**数值**顺序是文档声明顺序推得的，官方没给数字。）

| 必须显式忽略 `LastBone` | 说明 |
| --- | --- |
| 为什么 | Unity 官方定义 `LastBone` 是 "**Last bone index delimiter**"（分隔符，不是骨）。官方参考接收端 `SampleBonesReceive.cs` **显式跳过**它；只有**日文版** spec 写了"HumanBodyBones 全部会被发送，**包含 LastBone**"（英文版没这句）→ 日文那句是措辞歧义/笔误。**收到 `LastBone` 不要去找骨、不要报错** |

**能否真的发出去取决于模型**：VRM 0.x 里 `neck`、`head`、`hips`、`spine`、`chest`、上肢、腿、手是 **Required**，
而 `left/rightEye`、`jaw`、`upperChest`、`toes`、全部指骨是 **Optional**（§5.1）。
→ 我们发骨名时**必须先确认目标模型真的绑了这根骨**，否则消息是发得出去但没人听。
（注：**Unity 官方没有公开逐骨必需/可选清单**，Manual 只笼统说 humanoid "至少 15 根骨"；
上表的必需/可选用的是 **VRM 规范**的口径。）

### 4.4 VRM0 ↔ VRM1 预设映射（spec 自带）

| VRM0 预设 BlendShape | VRM1 预设 Expression |
| --- | --- |
| `Joy` | `happy` |
| `Angry` | `angry` |
| `Sorrow` | `sad` |
| `Fun` | `relaxed` |
| `A` / `I` / `U` / `E` / `O` | `aa` / `ih` / `ou` / `ee` / `oh` |
| `Blink_L` / `Blink_R` | `blinkLeft` / `blinkRight` |

**spec 的兼容规定（原文要点）**：现有 VMC 应用都用 VRM0；
**用 VRM1 的发送方必须按 VRM0 格式发送**（VRM1 格式只作为可选附加）；
**用 VRM1 的接收方收到 VRM0 名字时要自行转换**。

### 4.5 常见接收端对 blend 名的期望

| 应用 | 期望 | 来源 |
| --- | --- | --- |
| Warudo | 提供 **BlendShape Mapping** 选项（ARKit / MikuMikuDance / VRM，默认自动探测）；ARKit 映射期望小写 camelCase | [Warudo Handbook – Customizing Face Tracking](https://docs.warudo.app/docs/mocap/face-tracking) |
| VNyan | 有 "Blendshape Tracking" 开关；ARKit 追踪要求 avatar **具备 52 个 ARKit blendshape clip** | [VNyanDoc – Tracking Layers](https://github.com/Suvidriel/VNyanDoc/wiki/Tracking-Layers) |
| VSeeFace | 接收 iOS blendshape 数据（VTS 手机 App 直连，端口 21412），官方称 "All iOS blendshapes are supported" | [VTS Wiki – Sending data to VSeeFace](https://github.com/DenchiSoft/VTubeStudio/wiki/Sending-data-to-VSeeFace) |
| OBSKUR | **UNVERIFIED**：没找到官方命名说明 | — |

---

## 5. VRM（3D 模型自己的形态键 / 表情规范）

### 5.1 VRM 0.x（BlendShapePreset）

来源：[vrm-specification 0.0 README](https://github.com/vrm-c/vrm-specification/blob/master/specification/0.0/README.md)。

| 预设名 | 分组 | 说明 |
| --- | --- | --- |
| `Neutral` | 待机 | |
| `A` `I` `U` `E` `O` | 口型 | 官方括号注 `(aa)` `(ih)` `(ou)` `(E)` `(oh)` |
| `Blink` | 眨眼 | |
| `Blink_L` `Blink_R` | 眨眼 | 左右分开 |
| `Fun` `Angry` `Sorrow` `Joy` | 情绪 | |
| `LookUp` `LookDown` `LookLeft` `LookRight` | 视线 | |
| `Unknown` | 哨兵 | 表示"不是预设，用自定义名" |

**唯一 ID 规则（官方伪代码，原样）**：

```
function GetID(preset, name)
{
  if (Preset != BlendShapePreset.Unknown) return preset.ToString().ToUpper();
  else return name.ToUpper();
}
```

→ 预设名的 ID 是**大写预设名**；自定义名的 ID 是**大写后的自定义名**。官方建议自定义名也用大写字母。

> ⚠️ **规范内部自相矛盾（照实记下）**：README 正文的枚举写成 PascalCase
> （`Neutral`、`A`、`Blink_L`…），而**真正的 JSON Schema**
> （`specification/0.0/schema/vrm.blendshape.group.schema.json`，已核对原文）里的 `presetName.enum` 是
> **全小写 18 项**：`unknown`、`neutral`、`a`、`i`、`u`、`e`、`o`、`blink`、`joy`、`angry`、`sorrow`、
> `fun`、`lookup`、`lookdown`、`lookleft`、`lookright`、`blink_l`、`blink_r`。
> 规范**没有说明文件里实际序列化用哪种** → 这一项标 `UNVERIFIED`，读别人的 VRM 时**两种都要能认**。
> 顺带：0.x 的 schema 里也有 `isBinary`（原文 "0 or 1. Do not allow an intermediate value. Value should rounded"）。

**权重范围（这坑必须先说清）**：VRM 0.x 官方 schema
`specification/0.0/schema/vrm.blendshape.bind.schema.json`（已核对原文）写的是

```jsonc
"weight": { "description": "SkinnedMeshRenderer.SetBlendShapeWeight",
            "type": "number", "minimum": 0, "maximum": 100 }
```

即 **文件里是 0–100**（和 Unity 的 `SkinnedMeshRenderer.SetBlendShapeWeight` 一致）；
而 **VRM 1.0 是 0–1**（其文档明确写 "In 0.X [0-100]"）。
**但 UniVRM 的运行时代理 API 是 0–1**（编辑器面板显示 100）→ **迁移时必须做 100 倍换算**，
而且"你在哪一层"决定了你看到的是 0–1 还是 0–100。

### 5.2 VRM 1.0（`VRMC_vrm.expressions`）

来源：[VRMC_vrm-1.0/expressions.md](https://github.com/vrm-c/vrm-specification/blob/master/specification/VRMC_vrm-1.0/expressions.md)。

**值域（官方原文，全生态里唯一被规范写死的）**：
"Value is a number with a value in the range **[0-1]**. The VRM implementation **should clamp** the value
if the application gives a value outside this range."

| 预设名 | 分组 | 对应 VRM0 旧名 | 备注 |
| --- | --- | --- | --- |
| `happy` | Emotion | `Joy` | 原 `joy` |
| `angry` | Emotion | `Angry` | |
| `sad` | Emotion | `Sorrow` | 原 `sorrow` |
| `relaxed` | Emotion | `Fun` | 原 `fun`；官方解释 "Comfortable" |
| `surprised` | Emotion | — | **1.0 新增** |
| `aa` `ih` `ou` `ee` `oh` | Lip Sync（procedural） | `A` `I` `U` `E` `O` | |
| `blink` `blinkLeft` `blinkRight` | Blink（procedural） | `Blink` `Blink_L` `Blink_R` | |
| `lookUp` `lookDown` `lookLeft` `lookRight` | Gaze（procedural） | `LookUp`… | |
| `neutral` | Other | `Neutral` | "left for backwards compatibility" |

**共 18 个键** = 5 情绪 + 5 口型 + 3 眨眼 + 4 视线 = **17 个功能性预设** + 兼容保留的 `neutral`
（官方 JSON schema 里这 18 个都列在 `expressions.preset` 下，且全部可选）。

| 字段 | 规则（官方原文要点） |
| --- | --- |
| `isBinary` | 大于 0.5 变 1.0，否则 0.0 |
| `morphTargetBinds[].weight` | "morph value when applied **[0-1]**。**In 0.X [0-100]**" |
| `overrideMouth` / `overrideBlink` / `overrideLookAt` | 非零时压制 procedural 表情。值：`none` 不做 / `block` 目标权重置 0 / `blend` 线性衰减 |
| override 的作用对象 | `overrideMouth` → `aa/ih/ou/ee/oh`；`overrideBlink` → `blink/blinkLeft/blinkRight`；`overrideLookAt` → `lookUp/Down/Left/Right` |
| isBinary 与 override 的互动 | 覆盖方用 isBinary 时**必须用二值化后的值**去影响别人；被覆盖方若自己也 isBinary，则**只要受到 >0 的影响就完全压制**（防止 0/1 之外的值出现） |
| 自定义表情 | 放 `expressions.custom`，**不得与预设同名** |
| 应用算法 | 所有 MorphTarget 先归 0 → 累加各表达式的 Weight → 应用 |

**对我们的直接后果**：VRM 只有 **17 个功能性预设**（+`neutral`），表达力远不如 ARKit 52。
要发"具体的脸"只能走 `expressions.custom`，或者发 ARKit 名字靠应用自己的映射表（Warudo 就是这么做的）。

### 5.3 我们该往 VRM / VMC 写什么名字

| 场景 | 写什么 | 理由 |
| --- | --- | --- |
| 目标是"面捕生态通用的 3D avatar 应用"（Warudo/VNyan/VSeeFace…） | **ARKit 52 的 camelCase**（`eyeBlinkLeft`、`jawOpen`…） | VMC spec 自己指向 ARKit；接收端普遍提供 ARKit 映射；VNyan 直接要求 52 个 ARKit clip |
| 目标是"严格只认 VRM 预设的表情" | VRM0 预设名（`Joy`/`A`/`Blink_L`…），或 VRM1 名字 | 但只有 17 个，做不了细节；且要按 §4.4 的格式约定 |
| 目标是我们自己的 Unity 控制器 | **任意**（我们自己定），建议沿用上面两套名字 | 免映射表 |

**来源与时间**：VMC spec 与 VRM 规范（master 分支）、Unity HumanBodyBones 文档，抓取于 2026-09-24。

---

## 6. iFacialMocap 线协议（**官方协议记录**：我们不再接，历史与将来参考）

> **状态（2026-09-25）**：**我们不再接 iFacialMocap**。它给不出 `FaceFound`，而那是"丢追回中性"
> 整条机制唯一的开关，所以接收端连着 `IFacialMocapPacket.cs` 一起删了
> （`Editor/FaceTracking/HoFaceInputEnvironment.cs:13`、`Tests~/FaceTrackingValidation.cs:1204`）。
> **本节整节保留为官方协议记录** —— 读别人的文档、看别家工具的日志、将来要再接回来时都用得上。
> 下面凡写"我们"的句子，都是**当年的实现**，不是现状。

来源：**官方开发者文档** [iFacialMocap communication specifications](https://www.ifacialmocap.com/for-developer/)
（另有[日文版](https://www.ifacialmocap.com/for-developer/%E6%97%A5%E6%9C%AC%E8%AA%9E/)、
[韩文版](https://www.ifacialmocap.com/for-developer/korean/)）。这份文档是**官方**的，
所以下面这些是 `CONFIRMED`；社区里流传的"逆向出来的格式"与它一致。

### 6.1 握手与端口

| 模式 | PC 先发什么 | 发到哪 | 数据回到哪 |
| --- | --- | --- | --- |
| UDP（默认） | 字符串 `iFacialMocap_sahuasouryya9218sauhuiayeta91555dy3719` | iOS 端口 `49983` | PC 端口 `49983`，**60 FPS** |
| TCP/IP | 字符串 `iFacialMocap_UDPTCP_…dy3719` | iOS 端口 `49983` | PC 端口 `49986`，每帧以 `___iFacialMocap` 结尾（TCP 会断帧，用这个当分隔符） |
| 停止 TCP | `iFacialMocap_UDPTCPSTOP_…dy3719` | iOS 端口 `49983` | — |
| 录制回放 | `iFacialMocap_bakeRecordedAnimation_start` | iOS 端口 `49983` | PC 端口 `49987`，JSON 数组 + `___iFacialMocap`（60 FPS 录制） |
| 让角色正视前方 | `iFacialMocap_lookForward` | iOS 端口 `49983` | — |

> 官方还提供蓝牙替代方案（[示例代码](https://github.com/emoto-yasushi/iFacialMocap_Facemotion3d_Bluetooth)）
> 与一个"直接给 Unity 用"的脚本（官方说明它**是给 FBX 用的、不是 VRM**）。

### 6.2 每帧的字符串文法（官方原文）

```
BlendShape 名-值 (0 ~ 100) | … | BlendShape 名-值 (0 ~ 100) | … |
  = head # 欧拉角X(度), 欧拉角Y, 欧拉角Z, 位置X, 位置Y, 位置Z |
  rightEye # 欧拉角X, 欧拉角Y, 欧拉角Z |
  leftEye # 欧拉角X, 欧拉角Y, 欧拉角Z |
```

| 字段 | 格式 | 量纲（官方明示） |
| --- | --- | --- |
| 形态键 | `名称-数值`，名称就是 **ARKit 52 的拼写**（`mouthSmile_R`、`eyeBlink_L`…，即 `_L/_R` 后缀） | **0 ~ 100**（不是 0~1！） |
| 头 | `=head#` + **6 个数**：欧拉角 X/Y/Z、位置 X/Y/Z | **度**（官方："Angle-related data is sent in degrees, not radians."）/ 位置单位官方未说明 |
| 右眼 / 左眼 | `rightEye#` / `leftEye#` + 3 个欧拉角 | 度 |
| 分隔符 | 形态键之间与变换块之间用 `\|`；`=` **划分"形态键区"与"变换区"**；`#` 划分变换名与数值；`,` 划分数值分量 | — |
| `___iFacialMocap` | **只属于 TCP 模式**的帧结束标记；**UDP 样例帧里没有它** —— 用 UDP 却强求这个后缀会把每帧都丢掉 | 官方原文 |
| `trackingStatus` | 会作为**普通形态键**出现（官方蓝牙示例里有 `trackingStatus-1`）；**语义官方没说明** | 存在 `CONFIRMED` / 语义 `UNVERIFIED` |

**变换块的顺序不要按位置假设**：官方网页样例里是 `rightEye#…` 在 `leftEye#…` 之前，
而 App 作者发布的官方蓝牙参考实现里规范化输出是 `leftEye#…rightEye#…` 在前 ——
**所以按名字找块，别按第几个块找**。

官方示例帧（原样，注意结尾还有一串 `34903,-1.666…` 的额外字段与一个多余的 `|`）：

```
mouthSmile_R-0|…|mouthLeft-0|=head#-21.488958,-6.038993,-6.6019735,-0.030653415,-0.10287084,-0.6584072|rightEye#6.0297494,2.4403017,0.25649446|leftEye#6.034903,-1.6660284,-0.17520553|34903,-1.6660284,-0.17520553|
```

> ⚠️ **不要拿官方这条样例帧当"标准帧"来写测试**：逐字段核对它有三处不合格式 ——
> ① 开头粘着一段带括号的残缺片段（`mouthSmile_R-0(eyeLookOut_L-0)mouthUpperUp_L-11(eyeWide_R-0)`），
> 而其中 `eyeLookOut_L`、`eyeWide_R` **恰好是整包唯一缺失的两个键**（这一帧只有 **50 个互异 ARKit 键**）；
> ② 有一个孤立字段 `|1|`；③ `=head#` 之后又多出一块与 leftEye 数值重复的第 4 块。
> ① ② 的样子像**两个 UDP 包首尾粘连** —— 结论：**不能假设每帧 52 键齐全**，
> 缺键 ≠ 0（我们接收端的 `Present[]` 语义是对的）。

**`sendDataVersion=v2`（iOS 1.1.8 起）**：握手串后面加 `|sendDataVersion=v2`，
形态键的**分隔符从 `-` 变成 `&`**（`mouthSmile_R&0`）。
官方给的理由："Facemotion3d 会发负的 blendshape 值，用 `-` 当分隔符可能撞车。"
→ **解析器必须同时吃 `-` 与 `&` 两种分隔**（我们现在的实现就是先找 `&` 再找 `-`）。

⚠️ **负值是在源头被压掉的**：App 作者（DevelopW）发布的官方蓝牙参考实现里写明，
**iFacialMocap 模式（`-` 分隔）会把负的 BlendShape 值 clamp 到 0**，只有 Facemotion3d 模式（`&`）保留负值。
→ 如果要用"双向键"的负方向（`jawLeft`/`jawRight`、`mouthLeft`/`mouthRight`、
`eyeLookIn`/`eyeLookOut` 这类成对键），**必须主动发 `|sendDataVersion=v2` 切换**，
否则拿到的永远是 0。（这条来自官方参考实现而非网页文档，标 `INFERRED-官方实现`。
**我们现在不接这条协议，所以这是"将来要接回来时的前置条件"**。）

### 6.3 当年我们这边的实现对照（历史记录；那份代码已删）

> ⚠️ **这张表现在只是历史**：它对照的 `Editor/FaceTracking/IFacialMocapPacket.cs` **已经删掉了**
> （理由见本节开头）。留着它的价值是"官方文法的每一条我们都逐条核过"，以及"重接时要重新满足哪些点"。

| 我们的假设 | 与官方文档是否一致 |
| --- | --- |
| 按 `\|` 切分字段 | ✅ 一致 |
| `=head#` / `head#`、`leftEye#`、`rightEye#` 按**前缀**找块 | ✅ 一致（而且"按名字找"是必须的：两块的前后顺序在官方两份材料里就不同） |
| head 6 个数、眼 3 个数 | ✅ 一致（但**我们把 head 当 6 个平铺数存**，用的人要记得前三个是欧拉角、后三个是位置） |
| 形态键值 `value / 100f` | ✅ 一致（官方就是 0~100） |
| 名称用 `_L/_R` 拼写 | ✅ 一致（ARKit 拼写的 52 键） |
| 分隔符 `&` 或 `-` 都接受（先 `&` 再 `-`） | ✅ 一致（v2 模式用 `&`）。⚠️ **负号与 v1 分隔符同形**这一点在源头就被规避了（v1 不发负值），所以"先找 `&` 再找 `-`"是安全的 |
| 缺键只记 `Present[]=false`，不写 0 | ✅ 与官方样例的实际情况相符（每帧不保证 52 键） |
| 未实现：TCP 模式（`___iFacialMocap` 结尾）、录制回放、蓝牙、`lookForward` | 官方有，当年只做了 UDP 直连（**现在这条协议整个不接了**） |
| 未消费：`trackingStatus` | 当年只会计入 `UnknownCount`；它的语义官方未说明，先不猜 |

> ⚠️ **2026-09-26 更新**：`IFacialWire`（`eyeBlinkLeft` → `eyeBlink_L`）**已经不在代码里了** ——
> 它唯一的作用是生成那张内置默认表的输入行，而**那张表连同生成它的类一起删掉了**
> （仓库里不允许有默认配置，见 [中间层 §6.1](FACE_TRACKING_MIDDLE_LAYER.md)）。
> 也就是说：**解析 iFacialMocap 报文的代码没了，"iFacialMocap 线名 → 规范名"那张内置映射表也没了**；
> 这条规则现在只活在**本文档**（与官方开发者文档 §6.2）里。
> 但它的**现实价值没变**：安卓版 VTS 发出来的形态键就是这套 `_L/_R` 拼写（mod `README.md` §1.4），
> 要接它就**在自己的配置文件里写这些行**（`Profiles/ho-debug-android.hoface.json` 就是这么写的）。
> 要接第三种协议，同样是写自己的输入行。

### 6.4 已知坑

| 坑 | 说明 |
| --- | --- |
| **0~100 不是 0~1** | 忘了换会让所有值大 100 倍（VBridger 在源码里 `/100f`；我们的规矩是**不写死代码**、由输入行写换算 —— 当年内置默认表里那一行就是 `jawOpen * 0.01`，那张表 2026-09-26 已删；真实事故：拿它接安卓 VTS ⇒ 值小 100 倍、还不报错，见 mod `README.md` §1.5） |
| head 那 6 个数的顺序 | 官方是 **欧拉角在前、位置在后**；和我们平时"位置+旋转"的直觉相反 |
| 变换块不要按位置找 | 网页样例是 rightEye 在前，官方蓝牙参考实现是 leftEye 在前 —— **按名字找**（§6.2） |
| **不能假设每帧 52 键齐全** | 官方样例帧只有 50 个互异键，且带粘连/孤立字段（§6.2）→ **缺键 ≠ 0**，把缺键当 0 会让表情间歇抽动 |
| **v1 的负值在源头就没了** | `-` 分隔（默认）时 App 把负值 clamp 到 0；要用负方向必须发 `|sendDataVersion=v2`（§6.2） |
| 60 FPS 固定 | UDP 会丢帧（官方原话 "If it is UDP, frames may be dropped"），所以我们不能假设每帧都收到 |
| `___iFacialMocap` 只属 TCP | UDP 帧没有这个后缀（§6.2） |
| 额外尾部字段 | 官方示例里有 `34903,-1.666…` 这种没有名字前缀的字段，还有 `trackingStatus` 这种没写语义的键；解析要能忽略不认识的字段（当年我们数 `UnknownCount`） |
| 手机端 IP | 需要用户手填 iOS 设备 IP；端口固定 49983 |

**仍未确认**（不要在代码里当已知量用）：head/leftEye/rightEye 的**欧拉角轴序与旋转方向**、
**坐标系手性与原点**、head 后三个位置值的**单位**、`trackingStatus` 的取值语义、
**逐键机型门槛表**（Apple 只有整体要求 + `tongueOut` 的 iOS 12.0，没有"哪个键要哪颗芯片"的官方矩阵）、
UDP 载荷上限、每帧键数是否有任何保障。

---

## 7. VRCFaceTracking 的 Unified Expressions（附录：我们**不实现**，但必须认识它）

**为什么单独一节**：它是最容易被误当成"ARKit 的另一个拼写"的东西 —— 实际上它是**另一套标准**，
名字、拆分粒度、值域编码都不同。我们首期不接 VRCFT（见 `archive/FACE_TRACKING_OSC_BACKEND_RESEARCH.md`），
但读别人的文档、看 avatar 的动画参数时会撞上它。

来源：[VRCFT Unified Expressions](https://docs.vrcft.io/docs/tutorial-avatars/tutorial-avatars-extras/unified-blendshapes)、
[Tracking Standard Comparison Overview](https://docs.vrcft.io/docs/tutorial-avatars/tutorial-avatars-extras/compatibility/overview)、
[Avatar Parameters](https://docs.vrcft.io/docs/tutorial-avatars/tutorial-avatars-extras/parameters/parameters)
（对应仓库 `VRCFaceTracking/docs`，抓取于 2026-09-24）。

### 7.1 它是什么

官方定义：**Unified Expressions 是 VRCFaceTracking 用的开源表情标准**，同时充当"avatar 的表情形态标准"，
自称与 `ARKit`/`PerfectSync`、`SRanipal`、`FACS` 等标准**兼容**（靠 VRCFT 内部的坐标变换去转换），
目标是"一套 avatar 形态在多种面捕设备上表现几乎一致"。
形态分两层：**Base Shapes**（解剖学基础形）+ **Blended Shapes**（把基础形合成更简单的形）。

### 7.2 它有而 ARKit 没有的形态（最容易踩的差异）

| 类别 | Unified 独有的形态 | 说明 |
| --- | --- | --- |
| 眼 | `EyeDilationLeft/Right`、`EyeConstrictLeft/Right` | **瞳孔**放大/收缩，ARKit 完全没有 |
| 眉 | `BrowPinchLeft/Right`、`BrowLowererLeft/Right` | ARKit 把它们合成一个 `browDown`；Unified 拆成"内侧夹紧"与"外侧下拉" |
| 眉 | `BrowInnerUpLeft/Right` | ARKit 只有单个 `browInnerUp`（双眉一起）；Unified 拆左右 |
| 鼻 | `NasalDilationLeft/Right`、`NasalConstrictLeft/Right` | 鼻翼扩张/收缩，ARKit 无 |
| 颊 | `CheekSuckLeft/Right` | 吸颊，ARKit 无 |
| 下颌 | `JawBackward`、`JawClench`、`JawMandibleRaise` | ARKit 只有 `jawForward`；后缩/咬紧/抬颌都没有 |
| 唇 | `LipSuckUpper/Lower/Corner…`、`LipFunnelUpper/Lower…`、`LipPuckerUpper/Lower…` | ARKit 的 `mouthRollUpper/Lower`、`mouthFunnel`、`mouthPucker` **不区分上下唇**，Unified 拆开 |
| 嘴 | `MouthUpperDeepenLeft/Right`、`MouthUpperLeft/Right`、`MouthLowerLeft/Right`、`MouthCornerPull/Slant…`、`MouthRaiserUpper/Lower`、`MouthTightenerLeft/Right` | ARKit 用 `mouthStretch`/`mouthShrug` 等更粗的粒度表达 |
| 舌 | `TongueUp/Down/Left/Right/Roll/BendDown/CurlUp/Squish/Flat/TwistLeft/Right` | ARKit 只有 `tongueOut` 一个 |
| 颈/喉 | `SoftPalateClose`、`ThroatSwallow`、`NeckFlexLeft/Right` | ARKit 无（官方标注需要专门动画设置） |

**注意**：`EyeClosedLeft/Right` 才是 Unified 对 ARKit `eyeBlinkLeft/Right` 的名字 ——
**名字不同**，写错就是"参数不存在"。

### 7.3 它给 avatar 的参数长什么样（`v2/` 前缀）

参数名形如 `v2/<Shape>`，且**允许任意前缀分层**（官方示例 `.../v2/JawOpen`、`...ExamplePrefix/v2/JawOpen`、
`...Example/Nest/v2/JawOpen`）。类型只有 **Float 与 Binary** 两种。
**v1（SRanipal 式、无前缀）与 v2（`v2/` 前缀）是同时发送的两套参数**，
官方注明 v1 由 v2 "directly emulated"（v1 是兼容层）。

> ⚠️ **官方文档与官方源码不一致，以源码为准**（已实测）：文档的 Avatar Parameters 页把眼动列成
> `v2/EyeLeftX` / `EyeLeftY` / `EyeRightX` / `EyeRightY`，但源码
> `VRCFaceTracking.Core/Params/Expressions/UnifiedExpressionsParameters.cs` **只发**
> `v2/Eye` / `v2/EyeLeft` / `v2/EyeRight` —— **全仓库没有 `v2/EyeLeftX` 这个字面量**。
> 同一页还列了 `v2/EyeSquintLeft/Right`，源码里是合并量。**对接前先探 OSCQuery，别照抄文档表格。**

| 参数 | 编码（官方原文 + 源码实测） |
| --- | --- |
| `v2/Eye` / `v2/EyeLeft` / `v2/EyeRight` | 注视向量。源码：`exp.Eye.Combined().Gaze` / `Left.Gaze` / `Right.Gaze`（⚠️ 文档里的 `EyeLeftX` 等名字不存在） |
| `v2/EyeOpenLeft` / `EyeOpenRight` / `EyeOpen` | 睁眼度（原始） |
| `v2/EyeClosedLeft` / `EyeClosedRight` / `EyeClosed` | = `1 − 睁眼度` |
| `v2/EyeLidLeft` / `EyeLidRight` / `EyeLid` | `<0→0.75>` = 睁眼度，**`<0.75→1.0>` = 睁大**。源码公式：`openness * 0.75 + EyeWide * 0.25` —— **0.75 才是"正常睁开"** |
| `v2/EyeWide` | 取左右眼 `EyeWide` 的**较大值** |
| `v2/PupilDilation`、`v2/PupilDiameterLeft/Right`、`v2/PupilDiameter` | `<0→1>`（源码对 `PupilDiameter` 还乘了 `0.1` / `0.05` 做归一） |
| `v2/BrowPinchLeft/Right`、`BrowLowererLeft/Right`、`BrowInnerUpLeft/Right`、`BrowOuterUpLeft/Right` | `<0→1>` |
| `v2/CheekPuffSuckLeft/Right` | `<0→1>` 鼓腮，**`<0→-1>` 吸腮**（一根轴两个语义） |
| `v2/JawOpen`、`v2/MouthClosed` | `<0→1>` |
| `v2/JawX` | `<0→1>` 右 / `<0→-1>` 左（合并轴） |
| `v2/JawZ` | `<0→1>` 前 / `<0→-1>` 后（合并轴） |
| `v2/MouthUpperX`、`v2/MouthLowerX`、`v2/MouthX` | `<0→1>` 右 / `<0→-1>` 左 |
| `v2/TongueX` / `TongueY` / `TongueArchY` / `TongueShape` | 都是"正方向 / 负方向"的合并轴 |
| 其余单个形态（`v2/MouthFrownRight` 等） | `<0→1>` |
| 简化参数（`v2/MouthSmileLeft`、`v2/SmileSad`、`v2/BrowExpression`…） | 由左右/上下**平均或合并**而来，官方单列一组 "Simplified Tracking Parameters" |
| 追踪状态（Bool） | `EyeTrackingActive`、`ExpressionTrackingActive`、`LipTrackingActive` —— **只在加载或状态变化时发一次** |

**规模**：Unified 的形态名共 **150 个** = Base Shapes **101**（含 8 个 `EyeLook*`）+ Blended Shapes **41**；
其中 **56 个在 ARKit 52 里完全没有对应物**（舌/颊/鼻/颈那一大片）。→ **两套名字绝不可以在同一张
映射表里混用**；最容易踩的是把 UE 的 `BrowInnerUpLeft`（单侧）当成 ARKit 的 `browInnerUp`（双眉内端）。

**与我们最相关的对照**：做"眼睑一根轴"时，VBridger 用 `0.5` 当中性，**VRCFT 用 `0.75`**
（源码里就是 `0.75·openness + 0.25·wide`）。**两种约定都真实存在**，所以"中性点"必须显式写进我们的配置，不能硬编码。

> ⚠️ **master 领先正式版**：源码 master 上有一批 v5.0.0 **未发布**的参数（`v2/LipSuckFunnel*`、
> `v2/MouthCornerY`、`v2/MouthTightenerStretch*` 等）。所以"VRCFT 发什么"要以**运行时的参数列表**为准。

### 7.4 边界说明（我们不做 VRCFT 时要注意什么）

1. **不要把 Unified 名当 ARKit 名用**：`EyeClosedLeft` ≠ `eyeBlinkLeft`，`BrowLowererLeft` ≠ `browDownLeft`。
2. **不要把 VRChat 的参数限制当成 Unity Animator 的限制**：同步上限 **256 bits**（是**位数**，不是 256 个参数）、
   `float` 占 8 bits、`bool` 占 1 bit、另有"8192 个自定义参数"这一条（**只出现在现行官方页，未见交叉确认**）
   —— 这些都是 **VRChat** 的规则，**Unity Animator 没有这些限制**。社区流传的"256 个参数"来自一个
   资产条数缺陷报告，不是同步上限。
3. **我们的中间层将来加一套"VRCFT 列"不需要改机制**：本来就是"一行 = 下游名 + 一条表达式"，
   这正是 `docs/FACE_TRACKING_MIDDLE_LAYER.md` 里"按下游分表"的含义。
4. **值得抄的两条**：`0.75` 式"中性点显式化"、**有符号合并轴**（鼓腮/吸腮一根轴），比每个语义拆一根轴省参数。

### 7.5 未能确认

- **官方文档与官方源码在眼动参数上不一致**（§7.3）：文档有 `v2/EyeLeftX` 等，源码只有
  `v2/Eye` / `v2/EyeLeft` / `v2/EyeRight`。**以源码为准，但对接前仍应先探 OSCQuery。**
- **`v2/EyeSquintLeft/Right` 是否真的发出**：文档列了，源码里是合并量 —— 未确认。
- VRChat 的 **8192 个自定义参数**上限只在**现行官方页**出现，旧快照版没有 → 适用范围未交叉确认。
- VRChat 参数名的**字符集**（是否允许空格、非 ASCII）：官方没有条文。
- 对照表里有两处疑似官方笔误（`MouthLowerDownRight` 的 ARKit 列写成 `mouthLowerUpRight`；
  `MouthDimpler*` 应为 `MouthDimple*`）：**以 ARKit 文档的拼写为准**。
- VRCFT 文档自称 "Still under construction"、"Documentation still actively in development"，
  且 master 上有未发布参数 → 形态与参数列表**随时可能变**。
- SRanipal 的形状官方总数、UE 是否有 v3：未找到。

---

## 8. 本仓库现在怎么用上面这些表（逐条给代码出处）

| 我们的设计 | 依据 | 具体引用 |
| --- | --- | --- |
| 中间层**不声明**它读什么、也不声明写什么，只等着被喂 | §1.4 两个命名空间 | 模型参数 `Param*` 逐模型、**VTS 插件协议不提供直写请求**；追踪参数才是可写面 |
| 写入侧的唯一映射表是 **`*.hoface.json` 配置文件**（面板把它当必填总闸） | §1.4 / §1.5 | 它装"输入行（线名 → 规范名）+ 输出行（参数名 = 曲线(表达式)）"。**留空 / 没指定 ⇒ 空表，这一层不做事**（**没有内置默认兜底，也没有任何默认配置可退回** —— 内置默认表 2026-09-26 已整个删掉，「开始驱动」也因此被 gate 住）；**一旦指定，两类行就都只认它的**（漏一条线名那条链静默失效，那一类为空也是空表）（`Editor/FaceTracking/HoFaceDebugSettings.cs` 的 `Inputs()` / `Outputs()`）。**没有"组件替我们决定吃哪些键"这回事了** |
| 输出行名字按**下游分表** | §1.1 / §3.2 | 发货那份 `ho-iPhoneVTS.hoface.json` 就是"一份配置、几套下游名"（G1 裸规范名 `jawOpen` + G2 官方 VTS 追踪参数 + VB 自造 + 姿态向量 + `FaceFound`，共 90 行，见 [HO 参数规范](PARAMETER_HO.md)）；要喂 L2D 或 VMC 就在配置里另加一套 —— 同一个语义在不同下游是**不同名字**（L2D 追踪参数是 `MouthOpen`/`JawOpen` 这类，VMC 是 `jawOpen`） |
| 每个输出参数**只能有一个写入者**（占用表） | §1.1 官方约束 + §1.5 `mode` | VTS 官方："Each output parameter can only be chosen once"；`mode:set` 同参数同时只能一个插件写 |
| 面捕输入用 ARKit 52 名当**规范名**，姿态分量与线名换算全交给输入行 | §3.1 / §3.2 | 52 个规范名 = §3.1 的 camelCase 列（`HoFaceTrackingChannels.cs:46`）；线名三套（VTS 手机 PascalCase / iFacialMocap `_L/_R` / 同名直通）**都在配置的输入行里换算**（以前那份内置默认表 `Inputs()` 是这么写的，2026-09-26 已删；现在是发货配置 `ho-iPhoneVTS.hoface.json` 与自己写的配置） |
| 形态键量纲在**输入行**里换算：iFacialMocap `× 0.01`、VTS 手机原样 | §6.2 / §1.8 | 官方文法写明 iFacialMocap 形态键是 **0~100**；VTS 手机包是 iOS 原始 **0..1**。⚠️ **接收端只交原样，不在 C# 里除法**（`HoVtsPacket.cs:160`） |
| 值进树的量纲：**参数是 0..1 的权重、clip 里写 100** | §3 值域 + §5.2 clamp | ARKit / VRM1 都是 `[0-1]`；**VRM0 文件里是 0–100**（schema `maximum: 100`）；Cubism 眼/嘴"闭 0 开 1"。⚠️ 详见 `pitfalls/SHAPE_KEY_OUTPUT.md` §6（写成百分比会得到 210 这种值） |
| **眼睑轴的"中性点"必须显式写在配置里，不能硬编码** | §7.3 | VBridger 用 `0.5`，VRCFT 用 `0.75`（0.75 才 = 正常睁开）—— 两种约定都真实存在。所以**我们从来不硬编码中性点**：当年那张默认表用的是**双向轴** `BlinkWide = eyeBlink − eyeWide`（+1 闭 / −1 睁大 / 0 中性，轴名由 `HoFaceNaming.LidAxis`，`HoFaceNaming.cs:20`）—— 那张表 2026-09-26 已删，**轴的定义现在写在配置文件里** |
| "睁大 / 闭 / 眯"合成两根轴 | §3.1 `eyeBlink` × `eyeWide` × `eyeSquint` | ARKit 把它们拆成三个独立键，没有现成的"睁眼度"轴；**已删的那份内置默认表**把前两个压成一根双向轴、`squint` 一根单端轴 —— 这仍是推荐形状，但要自己写进配置 |
| 做"张嘴"用 `jawOpen`，做"抿唇"用 `mouthClose`，不混 | §3.3 | `mouthClose` 官方语义是"双唇闭合，独立于下颌" |
| 合成"眼睛 X / Y"轴时必须定符号 | §3.3 `eyeLookIn/Out` | In/Out 是**相对鼻子**的方向，直接相减会把左右弄反 |
| 输出行**不声明** `min/max/default`：曲线关键点的范围就是定义域 | §1.6 | VTS 的 `min/max/default` 是"默认映射范围"而非钳制；现在输出行只有 `parameter`/`expression`/`curve`/`modifiers`/`notes`（`HoFaceMiddleware.cs:92`） |
| 不做"一行写 X/Y/Z"的 vector 输出 | §4.1 `/VMC/Ext/Bone/Pos` + §4.3 | 骨骼是另一个概念（HumanBodyBones 白名单 + 四元数），不该和形态键挤一张表 |
| 发骨名之前先确认模型有这个骨 | §4.3 + §5.1 | VRM 0.x 里眼骨、颚骨、指骨是 **Optional** |
| 明确"我们不实现 VRCFT" | §7 | VRCFT 的 Unified Expressions 是**另一套更大的标准**，与 ARKit 52 不能混用 |
| 控制器里没有那个参数名就**跳过**（不猜也不补） | §1.5 / §1.6 | VTS 写不存在的参数会**直接报错**；同名不同下游也会静默失效。会话只写控制器 `parameters` 里真有的名字（`HoFaceAnimationSession.cs:242`） |
| 只抄自己**拥有**的形态键（占用表） | §1.1 的"一个参数一个写入者" | `HoFaceOutputOwnership` 防 LookAt / 眨眼约束与面捕互相覆盖（`Runtime/FaceTracking/HoFaceOutputOwnership.cs:10`）；同一个键被第二个面捕会话抢会直接报错 |

---

## 9. 未能确认（不要当结论用）

| 事项 | 状态 | 说明 |
| --- | --- | --- |
| VTS 各默认追踪参数的**完整** min/max/default | **官方未公开** | README 的示例数组自己标注 "incomplete"，只有 `FaceAngleX`(`±30`)、`FacePositionX`(`±10`) 两个示例值；权威做法是运行时 `InputParameterListRequest` 现取 |
| 能否用 `InjectParameterDataRequest` 直写 `ParamAngleX` 之类的模型参数 | **UNVERIFIED** | 官方文档只承认写 default/custom **追踪参数**。设计中**不依赖**这个行为 |
| VTS 是否存在 `BodyAngleX` / `BodyRotationX` / `HairFront` / `ArmLeftA` / `EyeSmileLeft` 之类的追踪参数 | 官方清单中**未出现**（CONFIRMED 缺席，但不能排除"清单滞后于 App 版本"） | 这些名字在 **Cubism 模型参数**里存在（`ParamBodyAngleX` 等） |
| VMC `/VMC/Ext/Blend/Val` 的值域是否被协议限定为 `0..1` | **无协议级限定**（CONFIRMED 缺席） | spec 英文/日文全页无范围声明，官方参考接收端也不 clamp；`0..1` 来自 VRM 表达式规范 |
| VTS `weight` 与 `mode:"add"` 的精确混合公式 | **部分确认** | 官方只给语义（`weight` 做加权混合、`add` 忽略 weight），没给公式 |
| OBSKUR 的 blend/命名要求 | **UNVERIFIED** | 未找到官方说明 |
| `HandDistance` 的取值范围 | **官方未公开** | wiki 只给了语义 |
| VTS 手机 "3rd Party PC Clients" 协议（§1.8） | ✅ 已确认（官方示例仓库 + 本机实测） | 请求包字段约束与载荷字段名都可读；**没确认的是** `Rotation`/`Position`/`EyeLeft`/`EyeRight` 的欧拉角轴序与旋转方向、坐标系手性与原点、位置字段单位 —— 全在输入行里交给使用者按设备定（`HoFaceMiddleware.cs:194`） |
| iFacialMocap 线协议 | ✅ 已确认（§6）**但我们现在不接** | 官方开发者文档存在且给出完整文法；只有"**位置字段的单位**"官方未说明。留着当官方协议记录 |
| VRM 0.x 文件里 `presetName` 到底写大写还是小写 | **UNVERIFIED**（规范自相矛盾） | README 枚举 PascalCase 17 项，JSON Schema enum 全小写 18 项（§5.1）；**两种都要能认** |
| VRChat 侧 Avatar Parameter 限制 | **已核实主要数值**（§7.4） | 同步上限 **256 bits**（= 位数不是个数；float 8 bits、bool 1 bit）；**8192 个自定义参数**只在现行官方页出现，未交叉确认。这些是 VRChat 的规则，**不是 Unity Animator 的限制** |
| VRM1 新增的 `thumbMetacarpal` 与 Unity `HumanBodyBones`（只有 ThumbProximal/Intermediate/Distal）的逐节映射 | **UNVERIFIED** | 没找到官方声明；实现手部链路前必须实测 |
| `/VMC/Ext/Set/Enabled` | **官方 spec 中不存在**（CONFIRMED 缺席） | 英文页、日文页、官方参考实现的地址白名单三处都没有 |
| Cubism Editor 4.2 与 5.x 的标准参数表是否完全一致 | **未逐条比对** | 本表用的是 Editor 5 页面（标注 Updated 2021-08-26） |
| `/VMC/Ext/Bone/Pos` 能否真的用上某根骨 | 取决于模型（**不是 UNVERIFIED，是"必须实测"**） | VRM 的眼骨/颚骨/指骨是 Optional（§5.1） |
| Apple `ARFaceAnchor.BlendShapeLocation` 页面正文 | 页面可访问但 **JS 渲染抓不到纯文本** | 本表的 52 名与语义取自 **Unity 官方枚举文档**（`com.unity.xr.arkit@5.1.6`，每条都带 Apple 文档链接）+ VTS 的枚举源码，**两者逐名一致** |
