# VTS → VB高质量加工 → 高级混合树：全量契约表

日期：2026-09-25。**本表是当前设计入口。目标为完整的高质量模板，允许大量树；降配通过选择性填写动画完成，轴、条件和树族保持稳定。**
不规定9格、25格或任何固定叶子数。矩阵可以非方阵、按条件切片、共享姿势、采用不同采样密度。

交付范围：① VTS公开固定追踪参数逐名清单；② 手机原始ARKit逐名清单；③ 本机VB V3全部展开输出；④ 保留细节的高质量扩展输出；⑤ 高级树族、轴、条件、输出职责与缺省规则。
表格中源软件行为与我们的设计明确分开。**这是设计与调查产物，尚未生成控制器、动画、可加载profile，也没有改运行时。**

## 0. 阅读规则与完整性口径

| 标记/层 | 含义 |
| --- | --- |
| **VTS固定追踪参数** | 官方三个页面明确列出的98个名字：33面部/鼠标/音频，26手部，39控制器；表A逐名列出 |
| **手机原始ARKit** | 52路原始观测，另有头眼向量、健康/热键信息；与桌面追踪参数不是一个接口，表B |
| **VB原装V3** | 本机预设26个存档行，向量展开后34个标量；表C是实际字段/公式，不虚构新增能力 |
| **HQ扩展** | 为完整高质量模板提出的39个输出，需作者在VB Editor或我们的中间层实现；表D不是原装V3参数清单 |
| **树族** | 表E的42项设计单元，内部可包含许多条件切片/子树；不是42个Unity节点或42份动画 |
| **可扩展集合** | VTS插件自定义参数与每个模型的Live2D参数ID可变化，名字集合无法静态穷举；应运行时枚举 |

98是**2026-09-25核对的公开列表规模**，不是声称当前所有VTS版本/插件永远只能输出这些参数。
`InputParameterListRequest`用于枚举实际默认/自定义追踪参数；`Live2DParameterListRequest`用于枚举当前模型参数。
官方API对自定义参数有全局300、每插件100的容量限制；命名空间仍由创作者扩展。[VTS API](https://github.com/DenchiSoft/VTubeStudio#requesting-list-of-available-tracking-parameters)

**链路约定：** 原始观测/音频 → 校准与供值选择 → 稳定的造型语义轴 → 高级树 → 形态键/姿态输出。
音频、鼠标、手柄可以给既有语义轴供值；它们不会绕过树直接修改模型。本项目的现行手机接收接口还没有音频适配器，不能把设计中的音频接入当作已实现。

## A. VTS公开固定追踪参数：逐名全表

### A1. 面部、鼠标、音频：33项

以下平台列来自官方表。“运行时查询”表示本次引用的官方资料未给出该项的完整权威范围；不用VB声明范围或模型师映射范围替代。
音频10项的0…1由音频文档明示。[VTS Model Settings](https://github.com/DenchiSoft/VTubeStudio/wiki/VTS-Model-Settings)、[Lipsync](https://github.com/DenchiSoft/VTubeStudio/wiki/Lipsync)

<!-- AUTO:VTS_FACE -->

| 参数（逐名） | 语义 | 范围 | 高级模板接点 | iOS/Android/普通/NVIDIA/MediaPipe |
| --- | --- | --- | --- | --- |
| FacePositionX | 头部横移 | 运行时查询 | H02 | ✔️/✔️/✔️/✔️/✔️ |
| FacePositionY | 头部升降 | 运行时查询 | H02 | ✔️/✔️/✔️/✔️/✔️ |
| FacePositionZ | 头部远近 | 运行时查询 | H02条件 | ✔️/✔️/✔️/✔️/✔️ |
| FaceAngleX | 左右转头 | 运行时查询 | H01 | ✔️/✔️/✔️/✔️/✔️ |
| FaceAngleY | 抬低头 | 运行时查询 | H01 | ✔️/✔️/✔️/✔️/✔️ |
| FaceAngleZ | 歪头 | 运行时查询 | H01条件/姿态 | ✔️/✔️/✔️/✔️/✔️ |
| MouthSmile | 综合嘴型 | 运行时查询 | 校准为Form → M01等 | ✔️/✔️/✔️/✔️/✔️ |
| MouthOpen | 嘴唇开口 | 0..1 | Open → M01等 | ✔️/✔️/✔️/✔️/✔️ |
| Brows | 双眉合并 | 运行时查询 | 眉代理来源；高档优先左右信息 | ✔️/✔️/✔️/✔️/✔️ |
| MousePositionX | 鼠标/触摸横向 | 运行时查询 | Gaze/头姿/作者演出轴的替代来源 | ✔️/✔️/✔️/✔️/✔️ |
| MousePositionY | 鼠标/触摸纵向 | 运行时查询 | 同上，与X成2D | ✔️/✔️/✔️/✔️/✔️ |
| TongueOut | 伸舌 | 运行时查询 | M14/C03 | ✔️/✔️/❌/❌/❌ |
| EyeOpenLeft | 左眼睁开 | 运行时查询 | LidLeft → E01L等 | ✔️/✔️/✔️/✔️/✔️ |
| EyeOpenRight | 右眼睁开 | 运行时查询 | LidRight → E01R等 | ✔️/✔️/✔️/✔️/✔️ |
| EyeLeftX | 左眼水平 | 运行时查询 | E02L/E03L的替代来源 | ✔️/✔️/✔️/✔️/✔️ |
| EyeLeftY | 左眼垂直 | 运行时查询 | E02L/E03L的替代来源 | ✔️/✔️/✔️/✔️/✔️ |
| EyeRightX | 右眼水平 | 运行时查询 | E02R/E03R的替代来源 | ✔️/✔️/✔️/✔️/✔️ |
| EyeRightY | 右眼垂直 | 运行时查询 | E02R/E03R的替代来源 | ✔️/✔️/✔️/✔️/✔️ |
| CheekPuff | 鼓腮 | 运行时查询 | C02/C03 | ✔️/❌/❌/❌/❌ |
| BrowLeftY | 左眉高 | 运行时查询 | B02L；可降级代理B01L | ✔️/✔️/✔️/✔️/✔️ |
| BrowRightY | 右眉高 | 运行时查询 | B02R；可降级代理B01R | ✔️/✔️/✔️/✔️/✔️ |
| VoiceFrequency | 合成语音嘴型 | 0..1 | Form的音频来源，非Hz | ✔️/✔️/✔️/✔️/✔️ |
| VoiceVolume | 音量 | 0..1 | Open/Jaw/HQAudioStrength的音频来源 | ✔️/✔️/✔️/✔️/✔️ |
| VoiceVolumePlusMouthOpen | 音量辅助开合 | 0..1 | Open替代来源，不与原Open重复相加 | ✔️/✔️/✔️/✔️/✔️ |
| VoiceFrequencyPlusMouthSmile | 语音辅助嘴型 | 0..1 | Form替代来源，不与原Form重复相加 | ✔️/✔️/✔️/✔️/✔️ |
| VoiceA | A元音权重 | 0..1 | 音频→既有嘴轴；或A01音素分支权重 | ✔️/✔️/✔️/✔️/✔️ |
| VoiceI | I元音权重 | 0..1 | 音频→既有嘴轴；或A01音素分支权重 | ✔️/✔️/✔️/✔️/✔️ |
| VoiceU | U元音权重 | 0..1 | 音频→既有嘴轴；或A01音素分支权重 | ✔️/✔️/✔️/✔️/✔️ |
| VoiceE | E元音权重 | 0..1 | 音频→既有嘴轴；或A01音素分支权重 | ✔️/✔️/✔️/✔️/✔️ |
| VoiceO | O元音权重 | 0..1 | 音频→既有嘴轴；或A01音素分支权重 | ✔️/✔️/✔️/✔️/✔️ |
| VoiceSilence | 静音量 | 0..1 | 音频/视觉供值比例；不作为美术坐标 | ✔️/✔️/✔️/✔️/✔️ |
| MouthX | 偏嘴 | 运行时查询 | M04/M15/E04L/E04R | ✔️/✔️/❌/✔️/✔️ |
| FaceAngry | 实验性生气量 | 运行时查询 | 保留可绑定；不用它替代可靠的眉/唇观测 | ✔️/❌/❌/❌/❌ |

<!-- END:VTS_FACE -->

`EyeOpen*`包含睁眼程度；VTS这个固定表没有单列EyeSquint或EyeSmile。高级方案从原始观测/VB扩展补齐。
`MouthSmile`是综合嘴型/微笑量；它不等于独立左右Smile与Frown四路信息。
`VoiceFrequency`是音素合成的嘴型量，不是Hz；`Voice…Plus…`是已有开合/嘴型的替代供值源，不应把已经混合的结果再加一次。

### A2. 手部：26项

来源为桌面MediaPipe手部跟踪。位置轴按官方约定：X向外+10、向内−10；Y向上+10、向下−10；Z靠近+10、远离−10；左右“向内”的世界方向不同。[Hand Tracking](https://github.com/DenchiSoft/VTubeStudio/wiki/Hand-Tracking)

<!-- AUTO:VTS_HAND -->

| 参数（逐名） | 语义 | 范围 | 高级模板接点 |
| --- | --- | --- | --- |
| HandLeftFound | 追踪可用性 | 0/1 | 控制来源可用性 |
| HandRightFound | 追踪可用性 | 0/1 | 控制来源可用性 |
| BothHandsFound | 追踪可用性 | 0/1 | 控制来源可用性 |
| HandDistance | 双手距离 | 未公开 | 作者自定义演出轴 |
| HandLeftPositionX | 左手位置X | 见轴定义 | P01 |
| HandLeftPositionY | 左手位置Y | 见轴定义 | P01 |
| HandLeftPositionZ | 左手位置Z | 见轴定义 | P01 |
| HandRightPositionX | 右手位置X | 见轴定义 | P01 |
| HandRightPositionY | 右手位置Y | 见轴定义 | P01 |
| HandRightPositionZ | 右手位置Z | 见轴定义 | P01 |
| HandLeftAngleX | 左手角度X | ±180 | P01条件 |
| HandLeftAngleZ | 左手角度Z | ±180 | P01条件 |
| HandRightAngleX | 右手角度X | ±180 | P01条件 |
| HandRightAngleZ | 右手角度Z | ±180 | P01条件 |
| HandLeftOpen | 左手整体张开 | 0..1 | P02整体手形来源 |
| HandRightOpen | 右手整体张开 | 0..1 | P02整体手形来源 |
| HandLeftFinger_1_Thumb | 左拇指 | 0..1 | P02对应手指 |
| HandLeftFinger_2_Index | 左食指 | 0..1 | P02对应手指 |
| HandLeftFinger_3_Middle | 左中指 | 0..1 | P02对应手指 |
| HandLeftFinger_4_Ring | 左无名指 | 0..1 | P02对应手指 |
| HandLeftFinger_5_Pinky | 左小指 | 0..1 | P02对应手指 |
| HandRightFinger_1_Thumb | 右拇指 | 0..1 | P02对应手指 |
| HandRightFinger_2_Index | 右食指 | 0..1 | P02对应手指 |
| HandRightFinger_3_Middle | 右中指 | 0..1 | P02对应手指 |
| HandRightFinger_4_Ring | 右无名指 | 0..1 | P02对应手指 |
| HandRightFinger_5_Pinky | 右小指 | 0..1 | P02对应手指 |

<!-- END:VTS_HAND -->

这些是完整输入目录的一部分。它们可作为面部演出参数的供值源，也可驱动手部空间；不会因为纳入目录就自动获得面部输出所有权。

### A3. 控制器：39项

摇杆/方向键形成XY空间；扳机是连续量；按钮是状态；手指位置是估计的类别。
触摸与姿态扩展依赖相应控制器。类别编号不默认具有可插值的空间距离。[Controller Input](https://github.com/DenchiSoft/VTubeStudio/wiki/Controller-Input)

<!-- AUTO:VTS_CONTROLLER -->

| 参数（逐名） | 范围 | 模板接点 |
| --- | --- | --- |
| ControllerStickLeftX | -1 to 1 | P03 / 作者指定的演出参数来源 |
| ControllerStickLeftY | -1 to 1 | P03 / 作者指定的演出参数来源 |
| ControllerStickRightX | -1 to 1 | P03 / 作者指定的演出参数来源 |
| ControllerStickRightY | -1 to 1 | P03 / 作者指定的演出参数来源 |
| ControllerStickPressLeft | 0 or 1 | P03 / 作者指定的演出参数来源 |
| ControllerStickPressRight | 0 or 1 | P03 / 作者指定的演出参数来源 |
| ControllerDPadX | -1, 0, or 1 | P03 / 作者指定的演出参数来源 |
| ControllerDPadY | -1, 0, or 1 | P03 / 作者指定的演出参数来源 |
| ControllerTriangle | 0 or 1 | P03 / 作者指定的演出参数来源 |
| ControllerCross | 0 or 1 | P03 / 作者指定的演出参数来源 |
| ControllerSquare | 0 or 1 | P03 / 作者指定的演出参数来源 |
| ControllerCircle | 0 or 1 | P03 / 作者指定的演出参数来源 |
| ControllerShoulderLeft | 0 or 1 | P03 / 作者指定的演出参数来源 |
| ControllerShoulderRight | 0 or 1 | P03 / 作者指定的演出参数来源 |
| ControllerTriggerLeft | 0.0 to 1.0 | P03 / 作者指定的演出参数来源 |
| ControllerTriggerRight | 0.0 to 1.0 | P03 / 作者指定的演出参数来源 |
| ControllerOptionLeft | 0 or 1 | P03 / 作者指定的演出参数来源 |
| ControllerOptionRight | 0 or 1 | P03 / 作者指定的演出参数来源 |
| ControllerHome | 0 or 1 | P03 / 作者指定的演出参数来源 |
| ControllerTouchPadPress | 0 or 1 | P03 / 作者指定的演出参数来源 |
| ControllerButtonsAnyLeft | 0 or 1 | P03 / 作者指定的演出参数来源 |
| ControllerButtonsAnyRight | 0 or 1 | P03 / 作者指定的演出参数来源 |
| ControllerButtonCountLeft | 0+ | P03 / 作者指定的演出参数来源 |
| ControllerButtonCountRight | 0+ | P03 / 作者指定的演出参数来源 |
| ControllerLean | -1 to 1 | P03 / 作者指定的演出参数来源 |
| ControllerThumbPosLeft | 0–3 | P03 / 作者指定的演出参数来源 |
| ControllerThumbPosRight | 0–3 | P03 / 作者指定的演出参数来源 |
| ControllerFingerPosLeft | 0–2 | P03 / 作者指定的演出参数来源 |
| ControllerFingerPosRight | 0–2 | P03 / 作者指定的演出参数来源 |
| ControllerTouchPadTouchCount | 0–2 | P03 / 作者指定的演出参数来源 |
| ControllerTouchPadFingerLeftActive | 0 or 1 | P03 / 作者指定的演出参数来源 |
| ControllerTouchPadFingerRightActive | 0 or 1 | P03 / 作者指定的演出参数来源 |
| ControllerTouchPadFingerLeftX | 0.0 to 1.0 | P03 / 作者指定的演出参数来源 |
| ControllerTouchPadFingerLeftY | 0.0 to 1.0 | P03 / 作者指定的演出参数来源 |
| ControllerTouchPadFingerRightX | 0.0 to 1.0 | P03 / 作者指定的演出参数来源 |
| ControllerTouchPadFingerRightY | 0.0 to 1.0 | P03 / 作者指定的演出参数来源 |
| ControllerOrientationX | -60.0 to 60.0 | P03 / 作者指定的演出参数来源 |
| ControllerOrientationY | -60.0 to 60.0 | P03 / 作者指定的演出参数来源 |
| ControllerOrientationZ | -60.0 to 60.0 | P03 / 作者指定的演出参数来源 |

<!-- END:VTS_CONTROLLER -->

### A4. 不属于上述98个固定追踪名字的输出/控制数据

| 集合 | 名字/结构 | 契约处理 |
| --- | --- | --- |
| 手机状态 | `FaceFound`、`Hotkey`、`Timestamp` | 来源有效性、触发与时间；不占用美术矩阵维度 |
| 手机头眼向量 | `Rotation.{x,y,z}`、`Position.{x,y,z}`、`EyeLeft.{x,y,z}`、`EyeRight.{x,y,z}` | 接收器保留原值，输入配置处理坐标/单位；不要凭VB公式猜手机向量单位 |
| 手机形变 | `BlendShapes[{k,v}]`，52路见表B | 不是桌面VTS的MouthSmile等加工结果 |
| VB运行期健康输出 | `VBridgerFaceFound` | 旧本机研究发现的附加健康参数；不在34项V3预设行统计内 |
| 插件自定义 | 动态名字＋值/范围/默认/作者 | 用实际API列表扩展目录；不存在通用完整名字表 |
| 模型参数 | 每模型自己的`Param*`或任意ID | VTS映射后的模型自由度，API可读；不能用一份全局名字表替代 |
| 自动眨眼/呼吸、动画、表情、物理 | 模型配置/资产/数值提供者 | 不凭功能名编造一个VTS固定输入参数；在明确的造型轴或演出输出处接入 |

手机协议来源：[官方接收示例](https://github.com/DenchiSoft/VTubeStudioBlendshapeUDPReceiverTest)。
VTS模型参数的值提供者优先级见[官方交互说明](https://github.com/DenchiSoft/VTubeStudio/wiki/Interaction-between-Animations,-Tracking,-Physics,-etc.)；复刻完整演出时需要另外实现这些规则。

## B. VTS手机原始ARKit：52项及加工去向

全部按0…1形变系数处理；“VTS手机线名”和“VB公式变量”分别列出，避免大小写/`_L`风格混用。
一项映射到多个加工输出很正常。**每一路都有去向，但若最后只发VB原装34项，部分独立细节仍会被合并掉；表D用于保留它们。**
来自已验证手机接收命名表、既有Apple语义调查以及本轮VB预设公式；这张表机械检查了52路均被加工方案引用。

<!-- AUTO:RAW -->

| ARKit规范名 | VTS手机线名 | VB公式变量 | 加工后去向 |
| --- | --- | --- | --- |
| eyeBlinkLeft | EyeBlinkLeft | eyeBlink_L | EyeOpenLeft |
| eyeLookDownLeft | EyeLookDownLeft | eyeLookDown_L | EyeRightY, HQGazeLeftY |
| eyeLookInLeft | EyeLookInLeft | eyeLookIn_L | EyeRightX, HQGazeLeftX |
| eyeLookOutLeft | EyeLookOutLeft | eyeLookOut_L | EyeRightX, HQGazeLeftX |
| eyeLookUpLeft | EyeLookUpLeft | eyeLookUp_L | EyeRightY, HQGazeLeftY |
| eyeSquintLeft | EyeSquintLeft | eyeSquint_L | EyeSquintLeft |
| eyeWideLeft | EyeWideLeft | eyeWide_L | EyeOpenLeft |
| eyeBlinkRight | EyeBlinkRight | eyeBlink_R | EyeOpenRight |
| eyeLookDownRight | EyeLookDownRight | eyeLookDown_R | HQGazeRightY |
| eyeLookInRight | EyeLookInRight | eyeLookIn_R | HQGazeRightX |
| eyeLookOutRight | EyeLookOutRight | eyeLookOut_R | HQGazeRightX |
| eyeLookUpRight | EyeLookUpRight | eyeLookUp_R | HQGazeRightY |
| eyeSquintRight | EyeSquintRight | eyeSquint_R | EyeSquintRight |
| eyeWideRight | EyeWideRight | eyeWide_R | EyeOpenRight |
| jawForward | JawForward | jawForward | HQJawForward |
| jawLeft | JawLeft | jawLeft | HQJawX |
| jawRight | JawRight | jawRight | HQJawX |
| jawOpen | JawOpen | jawOpen | JawOpen, MouthOpen, VoiceVolumePlusMouthOpen, MouthFunnel |
| mouthClose | MouthClose | mouthClose | MouthOpen, VoiceVolumePlusMouthOpen, HQMouthSeal |
| mouthFunnel | MouthFunnel | mouthFunnel | MouthOpen, VoiceVolumePlusMouthOpen, MouthFunnel, HQLipFunnel |
| mouthPucker | MouthPucker | mouthPucker | MouthSmile, VoiceFrequencyPlusMouthSmile, MouthPucker, HQLipPucker |
| mouthLeft | MouthLeft | mouthLeft | MouthX, BrowLeftY, BrowRightY |
| mouthRight | MouthRight | mouthRight | MouthX, BrowLeftY, BrowRightY |
| mouthSmileLeft | MouthSmileLeft | mouthSmile_L | MouthSmile, VoiceFrequencyPlusMouthSmile, MouthX, HQEyeSmileLeft, HQSmileFrownLeft, HQEmotion |
| mouthSmileRight | MouthSmileRight | mouthSmile_R | MouthSmile, VoiceFrequencyPlusMouthSmile, MouthX, HQEyeSmileRight, HQSmileFrownRight, HQEmotion |
| mouthFrownLeft | MouthFrownLeft | mouthFrown_L | MouthSmile, VoiceFrequencyPlusMouthSmile, HQSmileFrownLeft, HQEmotion |
| mouthFrownRight | MouthFrownRight | mouthFrown_R | MouthSmile, VoiceFrequencyPlusMouthSmile, HQSmileFrownRight, HQEmotion |
| mouthDimpleLeft | MouthDimpleLeft | mouthDimple_L | MouthSmile, VoiceFrequencyPlusMouthSmile, MouthPucker, HQMouthDimpleLeft |
| mouthDimpleRight | MouthDimpleRight | mouthDimple_R | MouthSmile, VoiceFrequencyPlusMouthSmile, MouthPucker, HQMouthDimpleRight |
| mouthStretchLeft | MouthStretchLeft | mouthStretch_L | HQLipStretchLeft |
| mouthStretchRight | MouthStretchRight | mouthStretch_R | HQLipStretchRight |
| mouthRollLower | MouthRollLower | mouthRollLower | MouthOpen, VoiceVolumePlusMouthOpen, MouthPressLipOpen, HQLowerLipRoll |
| mouthRollUpper | MouthRollUpper | mouthRollUpper | MouthOpen, VoiceVolumePlusMouthOpen, MouthPressLipOpen, HQUpperLipRoll |
| mouthShrugLower | MouthShrugLower | mouthShrugLower | MouthShrug, HQLowerLipShrug |
| mouthShrugUpper | MouthShrugUpper | mouthShrugUpper | MouthShrug, HQUpperLipShrug |
| mouthPressLeft | MouthPressLeft | mouthPress_L | MouthShrug, HQLipPressLeft |
| mouthPressRight | MouthPressRight | mouthPress_R | MouthShrug, HQLipPressRight |
| mouthLowerDownLeft | MouthLowerDownLeft | mouthLowerDown_L | MouthPressLipOpen, HQLowerLipDropLeft |
| mouthLowerDownRight | MouthLowerDownRight | mouthLowerDown_R | MouthPressLipOpen, HQLowerLipDropRight |
| mouthUpperUpLeft | MouthUpperUpLeft | mouthUpperUp_L | MouthPressLipOpen, HQUpperLipRaiseLeft |
| mouthUpperUpRight | MouthUpperUpRight | mouthUpperUp_R | MouthPressLipOpen, HQUpperLipRaiseRight |
| browDownLeft | BrowDownLeft | browDown_L | Brows, BrowLeftY, HQBrowDownLeft, HQBrowExpressionLeft |
| browDownRight | BrowDownRight | browDown_R | Brows, BrowRightY, HQBrowDownRight, HQBrowExpressionRight |
| browInnerUp | BrowInnerUp | browInnerUp | BrowInnerUp, HQBrowExpressionLeft, HQBrowExpressionRight |
| browOuterUpLeft | BrowOuterUpLeft | browOuterUp_L | EyeRightY, Brows, BrowLeftY, HQBrowOuterUpLeft, HQBrowExpressionLeft |
| browOuterUpRight | BrowOuterUpRight | browOuterUp_R | Brows, BrowRightY, HQBrowOuterUpRight, HQBrowExpressionRight |
| cheekPuff | CheekPuff | cheekPuff | CheekPuff |
| cheekSquintLeft | CheekSquintLeft | cheekSquint_L | HQCheekSquintLeft |
| cheekSquintRight | CheekSquintRight | cheekSquint_R | HQCheekSquintRight |
| noseSneerLeft | NoseSneerLeft | noseSneer_L | HQNoseSneerLeft |
| noseSneerRight | NoseSneerRight | noseSneer_R | HQNoseSneerRight |
| tongueOut | TongueOut | tongueOut | TongueOut |

<!-- END:RAW -->

ARKit只有共享的`browInnerUp`、共享的`cheekPuff`以及一个`tongueOut`：
高级模板可以为左右内眉或舌造型准备不同动画，但**不能把同一个观测复制两份就说成新增了独立左右内眉、独立左右鼓腮或舌尖方向的传感能力**。
舌左右/卷舌/独立鼓腮等进一步定制可通过作者参数扩展，需要额外追踪源或手动/事件输入。

## C. VB原装 AdvancedARKit V3：34项展开输出

原件：`.research/vbridger/decrypted/VBridger_AdvancedARKit_V3.0.vbridger.txt`。
这里的min/max是**存档声明**，default是**存档默认**，公式是**输出曲线/平滑之前**的表达式；它们不是统一的模型中性定义。
完整曲线、平滑、源行号另保存在配套JSON，供后续配置迁移。

<!-- AUTO:VB -->

| VB V3输出（展开后逐名） | 声明范围 | 存档默认 | 表达式（曲线前） | 高级模板接点 |
| --- | --- | --- | --- | --- |
| FaceAngleX | -30…30 | 0 | - headRotY * .66 | H01 |
| FaceAngleY | -30…30 | 0 | ( - headRotX * .66)  | H01 |
| FaceAngleZ | -30…30 | 0 | headRotZ * .66 | H01条件/姿态 |
| BodyAngleX | -30…30 | 0 | - headRotY * .66 | H03 |
| BodyAngleY | -30…30 | 0 | ( - headRotX * .66)  | H03 |
| BodyAngleZ | -30…30 | 0 | headRotZ * .66 | H03条件 |
| FacePositionX | -15…15 | 0 | headPosX * - 1 | H02 |
| FacePositionY | -15…15 | 0 | headPosY | H02 |
| FacePositionZ | -15…15 | 0 | headPosZ | H02条件 |
| BodyPositionX | -15…15 | 0 | headPosX * - 1 | H04 |
| BodyPositionY | -15…15 | 0 | headPosY | H04 |
| BodyPositionZ | -15…15 | 0 | headPosZ | H04条件 |
| EyeRightX | -1…1 | 0 | (eyeLookIn_L - .1) - eyeLookOut_L | 共享注视兼容来源 → 两侧E02/E03；高档用独立HQGaze |
| EyeRightY | -1…1 | 0 | (eyeLookUp_L - eyeLookDown_L) + (browOuterUp_L * .15) | 共享注视兼容来源 → 两侧E02/E03；高档用独立HQGaze |
| EyeOpenLeft | 0…1 | 0 | .5 + ((eyeBlink_L * - .8) + (eyeWide_L * .8)) | LidLeft → E01L等 |
| EyeOpenRight | 0…1 | 0 | .5 + ((eyeBlink_R * - .8) + (eyeWide_R * .8)) | LidRight → E01R等 |
| EyeSquintLeft | 0…1 | 0 | eyeSquint_L | E01L/E03L/B02L |
| EyeSquintRight | 0…1 | 0 | eyeSquint_R | E01R/E03R/B02R |
| JawOpen | 0…1 | 0 | jawOpen | M02/M03/M14等 |
| MouthOpen | 0…1 | 0 | ((jawOpen - mouthClose) - ((mouthRollUpper + mouthRollLower) * .2) + (mouthFunnel * .2)) | Open → M01等 |
| VoiceVolumePlusMouthOpen | 0…1 | 0 | ((jawOpen - mouthClose) - ((mouthRollUpper + mouthRollLower) * .2) + (mouthFunnel * .2)) | Open替代来源，不与原Open重复相加 |
| MouthSmile | -1…1 | 0 | (2 - ((mouthFrown_L + mouthFrown_R + mouthPucker) / 1) + ((mouthSmile_R + mouthSmile_L + ((mouthDimple_L + mouthDimple_R) / 2)) / 1)) / 4 | 校准为Form → M01等 |
| VoiceFrequencyPlusMouthSmile | 0…1 | 0.5 | (2 - ((mouthFrown_L + mouthFrown_R + mouthPucker) / 1) + ((mouthSmile_R + mouthSmile_L + ((mouthDimple_L + mouthDimple_R) / 2)) / 1)) / 4 | Form替代来源，不与原Form重复相加 |
| MouthFunnel | 0…1 | 0 | mouthFunnel - (jawOpen * .2) | Funnel → M01/M13等 |
| MouthPressLipOpen | -1.3…1.3 | 0 | (((mouthUpperUp_R + mouthUpperUp_L + mouthLowerDown_R + mouthLowerDown_L) / 1.8) - (mouthRollLower + mouthRollUpper)) | Press → M01/M07/M08等 |
| MouthPucker | -1…1 | 0 | (((mouthDimple_R + mouthDimple_L) * 2) - mouthPucker) | M04/C02等 |
| MouthX | -1…1 | 0 | ((mouthLeft - mouthRight) + (mouthSmile_L - mouthSmile_R))  | M04/M15/E04L/E04R |
| CheekPuff | 0…1 | 0 | cheekPuff | C02/C03 |
| TongueOut | 0…1 | 0 | tongueOut | M14/C03 |
| MouthShrug | 0…1 | 0 | ((mouthShrugUpper + mouthShrugLower + mouthPress_R + mouthPress_L) / 4) | M16基础，M05细分 |
| Brows | 0…1 | 0.5 | .5 + (browOuterUp_R + browOuterUp_L - browDown_L - browDown_R) / 4 | 眉代理来源；高档优先左右信息 |
| BrowLeftY | 0…1 | 0 | .5 + (browOuterUp_L - browDown_L) + ((mouthRight - mouthLeft) / 8) | B02L；可降级代理B01L |
| BrowRightY | 0…1 | 0 | .5 + (browOuterUp_R - browDown_R) + ((mouthLeft - mouthRight) / 8) | B02R；可降级代理B01R |
| BrowInnerUp | 0…1 | 0 | browInnerUp | B01L/B01R/B02L/B02R条件 |

<!-- END:VB -->

几个落实到模板的要点：

- 原装V3的MouthSmile公式静息为约0.5，不能未经校准直接作为以0为中性的Form。
- `MouthPressLipOpen`具有双向含义；`MouthPucker`是dimple与原始pucker合成的双向量。
- 原装V3只输出一组EyeRightX/Y，且公式读左侧眼部观测；完整高级档采用表D保留的独立双眼数据。
- `VoiceVolumePlusMouthOpen` / `VoiceFrequencyPlusMouthSmile`这两行在原装V3中没有引用音量或音素。不能因为名字带Voice便认定它已经混入音频。
- FaceAngle/BodyAngle和FacePosition/BodyPosition可来自相同头部观测，差别含曲线与平滑。身体这些轴不是独立身体传感器的证据。
- V2与V3眯眼参数别名不同；所有别名在供值适配处解决，模板语义不随之变名。

## D. 高质量扩展输出：完整保留局部自由度

以下39项是**建议在VB Editor或我们的中间层产生的输出契约**，统一用`HQ`前缀标识，避免冒充V3原装参数。
公式以VB内部已校准输入为变量；移入我们配置时换成对应规范名。若表达式需要复用中间结果，应按现有中间层支持程度展开公式，不假定已支持任意行间引用。
所有名称均检查为4–32位字母数字，适合VTS自定义参数命名规则。

<!-- AUTO:HQ -->

| 建议扩展输出名 | 语义 | 表达式/生产规则 | 建议范围 | 树族 | 来源性质 |
| --- | --- | --- | --- | --- | --- |
| HQGazeLeftX | 左眼水平注视 | eyeLookIn_L - eyeLookOut_L | -1..1 | E02L,E03L | 以演员自身右方为正；模型镜像在校准映射处理 |
| HQGazeLeftY | 左眼垂直注视 | eyeLookUp_L - eyeLookDown_L | -1..1 | E02L,E03L | 直接保留原始自由度 |
| HQEyeSmileLeft | 左笑眼造型代理 | mouthSmile_L | 0..1 | E01L,E03L,E04L | 从嘴角笑映射；是作者联动，不是独立眼部笑观测 |
| HQSmileFrownLeft | 左嘴角欣/悲造型轴 | clamp(mouthSmile_L - mouthFrown_L, -1, 1) | -1..1 | M06 | 这是造型轴，不是心理情绪识别 |
| HQUpperLipRaiseLeft | 左上唇展开 | mouthUpperUp_L | 0..1 | M07 | 直接保留原始自由度 |
| HQLowerLipDropLeft | 左下唇下拉 | mouthLowerDown_L | 0..1 | M08 | 直接保留原始自由度 |
| HQLipPressLeft | 左压唇 | mouthPress_L | 0..1 | M10 | 直接保留原始自由度 |
| HQLipStretchLeft | 左横拉嘴角 | mouthStretch_L | 0..1 | M11 | 直接保留原始自由度 |
| HQMouthDimpleLeft | 左酒窝/嘴角收紧 | mouthDimple_L | 0..1 | M12 | 直接保留原始自由度 |
| HQCheekSquintLeft | 左颊收紧 | cheekSquint_L | 0..1 | C01 | 直接保留原始自由度 |
| HQNoseSneerLeft | 左鼻翼抬起 | noseSneer_L | 0..1 | N01 | 直接保留原始自由度 |
| HQBrowDownLeft | 左压眉 | browDown_L | 0..1 | B01L | 直接保留原始自由度 |
| HQBrowOuterUpLeft | 左外眉抬起 | browOuterUp_L | 0..1 | B01L | 直接保留原始自由度 |
| HQBrowExpressionLeft | 左眉情绪造型 | clamp((browInnerUp + browOuterUp_L) * .5, 0, 1) - browDown_L | -1..1 | B03 | 借鉴VRCFT轴形状的ARKit适配；不是逐值等同VRCFT |
| HQGazeRightX | 右眼水平注视 | eyeLookOut_R - eyeLookIn_R | -1..1 | E02R,E03R | 以演员自身右方为正；模型镜像在校准映射处理 |
| HQGazeRightY | 右眼垂直注视 | eyeLookUp_R - eyeLookDown_R | -1..1 | E02R,E03R | 直接保留原始自由度 |
| HQEyeSmileRight | 右笑眼造型代理 | mouthSmile_R | 0..1 | E01R,E03R,E04R | 从嘴角笑映射；是作者联动，不是独立眼部笑观测 |
| HQSmileFrownRight | 右嘴角欣/悲造型轴 | clamp(mouthSmile_R - mouthFrown_R, -1, 1) | -1..1 | M06 | 这是造型轴，不是心理情绪识别 |
| HQUpperLipRaiseRight | 右上唇展开 | mouthUpperUp_R | 0..1 | M07 | 直接保留原始自由度 |
| HQLowerLipDropRight | 右下唇下拉 | mouthLowerDown_R | 0..1 | M08 | 直接保留原始自由度 |
| HQLipPressRight | 右压唇 | mouthPress_R | 0..1 | M10 | 直接保留原始自由度 |
| HQLipStretchRight | 右横拉嘴角 | mouthStretch_R | 0..1 | M11 | 直接保留原始自由度 |
| HQMouthDimpleRight | 右酒窝/嘴角收紧 | mouthDimple_R | 0..1 | M12 | 直接保留原始自由度 |
| HQCheekSquintRight | 右颊收紧 | cheekSquint_R | 0..1 | C01 | 直接保留原始自由度 |
| HQNoseSneerRight | 右鼻翼抬起 | noseSneer_R | 0..1 | N01 | 直接保留原始自由度 |
| HQBrowDownRight | 右压眉 | browDown_R | 0..1 | B01R | 直接保留原始自由度 |
| HQBrowOuterUpRight | 右外眉抬起 | browOuterUp_R | 0..1 | B01R | 直接保留原始自由度 |
| HQBrowExpressionRight | 右眉情绪造型 | clamp((browInnerUp + browOuterUp_R) * .5, 0, 1) - browDown_R | -1..1 | B03 | 借鉴VRCFT轴形状的ARKit适配；不是逐值等同VRCFT |
| HQUpperLipRoll | 上卷唇 | mouthRollUpper | 0..1 | M09 | 直接保留原始自由度 |
| HQLowerLipRoll | 下卷唇 | mouthRollLower | 0..1 | M09 | 直接保留原始自由度 |
| HQUpperLipShrug | 上耸唇 | mouthShrugUpper | 0..1 | M05 | 直接保留原始自由度 |
| HQLowerLipShrug | 下耸唇 | mouthShrugLower | 0..1 | M05 | 直接保留原始自由度 |
| HQJawForward | 下颌前伸 | jawForward | 0..1 | M02 | 直接保留原始自由度 |
| HQMouthSeal | 独立唇闭合 | mouthClose | 0..1 | M03,C02 | 直接保留原始自由度 |
| HQLipFunnel | 原始漏斗形变 | mouthFunnel | 0..1 | M13 | 直接保留原始自由度 |
| HQLipPucker | 原始嘟嘴形变 | mouthPucker | 0..1 | M13 | 直接保留原始自由度 |
| HQJawX | 下颌侧移 | jawRight - jawLeft | -1..1 | M02,M15 | 直接保留原始自由度 |
| HQEmotion | 音素库的欣/悲造型代理 | clamp((mouthSmile_L + mouthSmile_R - mouthFrown_L - mouthFrown_R) / 2, -1, 1) | -1..1 | A01 | 保留给语音嘴的表情条件，不是心理情绪识别 |
| HQAudioStrength | 音素库的发音幅度 | audio amplitude after calibration | 0..1 | A01 | 音频适配层输出；不引用未接入的原始音量作为假数据 |

<!-- END:HQ -->

HQBrowExpression是借鉴参考轴语义的**ARKit适配公式**，不是把VRCFT统一层数值逐值搬来。
HQEyeSmile与HQEmotion是造型代理，其来源性质已标明；不伪装成新的独立传感量。
**上述轴可以始终保留在高级模板里；某模型不制作对应动画，不要求删除这些参数或改成低档控制器。**

## E. 全量高级混合树空间表

### E1. 表内语义轴的统一解释

| 树内名字 | 供值契约 | 坐标约定 |
| --- | --- | --- |
| `Form` | MouthSmile经模型校准映射 | 建议−1…1，中性0；具体映射由作者profile定义 |
| `Open` | MouthOpen经校准 | 建议0…1，嘴唇闭到开 |
| `Funnel` | MouthFunnel经校准 | 建议0…1，普通到漏斗形 |
| `Press` | MouthPressLipOpen经校准 | 建议−1…1，卷/压唇到唇展开，中性0 |
| `LidLeft/Right` | EyeOpenLeft/Right经校准；也可由原始Blink/Wide合成 | 表中采用0闭、0.5中性、1睁大作为作者约定；沿用当前BlinkWide坐标也可，但整树坐标一起转换 |
| `HQ*` | 表D | 使用各行建议范围；先做每演员/设备校准 |
| `JawOpen/MouthX/MouthPucker/MouthShrug/Brow*`等 | 表C或等价提供者经校准 | 范围、中性、方向显式固定到作者profile，不能只按存档default猜 |
| `phoneme_category` | VTS五元音或作者音素集合 | 类别分支/权重，不是一根0…N距离轴 |
| `HandPosition* / Stick* / FingerCurl` | 表A中对应左右手、控制器、手指参数 | 表中的族用通用名描述，实例化时展开为具体侧别/手指，不是新VTS内置名 |

表内“条件轴”表示：同一张XY表在不同条件下可以有不同姿势。它们与XY一起组成更高维的姿势空间。
每一组轴的**采样点和非方阵布局均留给作者**；表里不给默认九宫格，也不把条件数量换算成强制动画数量。

### E2. 42个树族

<!-- AUTO:TREES -->

| 树族ID | 部位 | 二维主轴/1D轴 | 条件轴 | 输出职责 | 未填动画语义 | 依据 |
| --- | --- | --- | --- | --- | --- | --- |
| M01 | 外嘴核心 | Form × Open | Funnel, Press | 基础姿势；嘴唇轮廓/口孔 | 复用低维基础姿势/中性，不放空Motion | 创作者证实该耦合；此处Unity组织为设计 |
| M02 | 下颌/口内 | JawOpen × HQJawForward | HQJawX | 基础姿势；下颌、牙/口内，明确与M01绑定分工 | 复用低维基础姿势/中性，不放空Motion | 本次高级模板设计，非VB原装树 |
| M03 | 唇闭合与下颌 | HQMouthSeal × JawOpen | Open, Form, Funnel | 条件修正；闭唇时保留下颌运动 | 零修正；保留基础输出 | 本次高级模板设计，非VB原装树 |
| M04 | 横向嘴型 | MouthPucker × MouthX | Open, Form | 对M01的嘴宽/偏嘴修正；不重复写完整外嘴 | 零修正；保留基础输出 | 本次高级模板设计，非VB原装树 |
| M05 | 上下耸唇 | HQUpperLipShrug × HQLowerLipShrug | Open, MouthPucker | 对合并MouthShrug基础的细分修正 | 零修正；保留基础输出 | 本次高级模板设计，非VB原装树 |
| M06 | 左右嘴角表情 | HQSmileFrownLeft × HQSmileFrownRight | Open, Funnel, Press | 不对称/局部嘴角修正；先扣除M01已表达部分 | 零修正；保留基础输出 | 本次高级模板设计，非VB原装树 |
| M07 | 上唇左右展开 | HQUpperLipRaiseLeft × HQUpperLipRaiseRight | Open, Press | 独立上唇/露齿修正 | 零修正；保留基础输出 | 本次高级模板设计，非VB原装树 |
| M08 | 下唇左右展开 | HQLowerLipDropLeft × HQLowerLipDropRight | Open, Press | 独立下唇/露齿修正 | 零修正；保留基础输出 | 本次高级模板设计，非VB原装树 |
| M09 | 上下卷唇 | HQUpperLipRoll × HQLowerLipRoll | Open, JawOpen | 对合并Press的卷唇细分修正 | 零修正；保留基础输出 | 本次高级模板设计，非VB原装树 |
| M10 | 左右压唇 | HQLipPressLeft × HQLipPressRight | Open, MouthPucker | 局部压唇修正 | 零修正；保留基础输出 | 本次高级模板设计，非VB原装树 |
| M11 | 左右横向拉伸 | HQLipStretchLeft × HQLipStretchRight | Open, Form | 拉伸修正，保留区别于Smile的形变 | 零修正；保留基础输出 | 本次高级模板设计，非VB原装树 |
| M12 | 左右酒窝/嘴角收紧 | HQMouthDimpleLeft × HQMouthDimpleRight | Open, Form | 酒窝/嘴角细节修正，扣除Form/Pucker已覆盖部分 | 零修正；保留基础输出 | 本次高级模板设计，非VB原装树 |
| M13 | 原始圆口与嘟嘴 | HQLipFunnel × HQLipPucker | Open, Form, Press | 恢复V3整形/合并丢失的圆口组合残差 | 零修正；保留基础输出 | 本次高级模板设计，非VB原装树 |
| M14 | 伸舌与口内 | TongueOut × JawOpen | Open, MouthPucker, Funnel, Press | 舌基础＋必要的唇/齿接触修正 | 复用低维基础姿势/中性，不放空Motion | 本次高级模板设计，非VB原装树 |
| M15 | 下颌偏移与偏嘴 | HQJawX × MouthX | JawOpen, Open | 下颌/嘴角错位修正 | 零修正；保留基础输出 | 本次高级模板设计，非VB原装树 |
| M16 | 合并耸唇基础 | MouthShrug | Open, MouthPucker | 给V3兼容来源的合并耸唇基础；M05做细分残差 | 复用低维基础姿势/中性，不放空Motion | 本次高级模板设计，非VB原装树 |
| E01L | 左眼睑 | LidLeft × EyeSquintLeft | HQEyeSmileLeft | 核心眼睑完整姿势；笑眼是第三维 | 复用低维基础姿势/中性，不放空Motion | 眼睑2D本地资产已证实；笑眼条件为高级扩展设计 |
| E02L | 左眼球注视 | HQGazeLeftX × HQGazeLeftY | 无 | 眼球注视基础 | 复用低维基础姿势/中性，不放空Motion | 2D本地资产已证实 |
| E03L | 左眼睑随视线 | LidLeft × HQGazeLeftY | HQGazeLeftX, EyeSquintLeft, HQEyeSmileLeft | 眼睑跟随/极端视线残差；不再次写完整眼球基础 | 零修正；保留基础输出 | 官方教程有联动章节；具体切片设计为本次提出 |
| E04L | 左眼睑随偏嘴 | MouthX × LidLeft | HQEyeSmileLeft | 偏嘴带动眼周修正 | 零修正；保留基础输出 | 官方教程有联动章节；具体切片设计为本次提出 |
| B01L | 左眉核心 | HQBrowDownLeft × HQBrowOuterUpLeft | BrowInnerUp | 眉毛基础；完整保留压眉/外眉抬起/内眉抬起 | 复用低维基础姿势/中性，不放空Motion | 本次高级模板设计，非VB原装树 |
| B02L | 左眉眼接触 | BrowLeftY × LidLeft | BrowInnerUp, EyeSquintLeft | 眉压/睁大重叠修正 | 零修正；保留基础输出 | 官方教程有联动章节；此处为设计 |
| E01R | 右眼睑 | LidRight × EyeSquintRight | HQEyeSmileRight | 核心眼睑完整姿势；笑眼是第三维 | 复用低维基础姿势/中性，不放空Motion | 眼睑2D本地资产已证实；笑眼条件为高级扩展设计 |
| E02R | 右眼球注视 | HQGazeRightX × HQGazeRightY | 无 | 眼球注视基础 | 复用低维基础姿势/中性，不放空Motion | 2D本地资产已证实 |
| E03R | 右眼睑随视线 | LidRight × HQGazeRightY | HQGazeRightX, EyeSquintRight, HQEyeSmileRight | 眼睑跟随/极端视线残差；不再次写完整眼球基础 | 零修正；保留基础输出 | 官方教程有联动章节；具体切片设计为本次提出 |
| E04R | 右眼睑随偏嘴 | MouthX × LidRight | HQEyeSmileRight | 偏嘴带动眼周修正 | 零修正；保留基础输出 | 官方教程有联动章节；具体切片设计为本次提出 |
| B01R | 右眉核心 | HQBrowDownRight × HQBrowOuterUpRight | BrowInnerUp | 眉毛基础；完整保留压眉/外眉抬起/内眉抬起 | 复用低维基础姿势/中性，不放空Motion | 本次高级模板设计，非VB原装树 |
| B02R | 右眉眼接触 | BrowRightY × LidRight | BrowInnerUp, EyeSquintRight | 眉压/睁大重叠修正 | 零修正；保留基础输出 | 官方教程有联动章节；此处为设计 |
| E05 | 双眼非对称协调 | LidLeft × LidRight | EyeSquintLeft, EyeSquintRight | 仅鼻根/眼周非对称残差；同步算法在中间层 | 零修正；保留基础输出 | 本次高级模板设计，非VB原装树 |
| B03 | 左右眉中央协同 | HQBrowExpressionLeft × HQBrowExpressionRight | 无 | 中央眉/额头修正；不与B01重复整眉基础 | 零修正；保留基础输出 | 本地Jerry有该2D；作为残差的分工为本次设计 |
| C01 | 双颊收紧 | HQCheekSquintLeft × HQCheekSquintRight | Form, LidLeft, LidRight | 脸颊肌肉基础/修正；与眼睑绑定分工 | 复用低维基础姿势/中性，不放空Motion | 本次高级模板设计，非VB原装树 |
| C02 | 鼓腮嘴型 | CheekPuff × MouthPucker | Open, HQMouthSeal, Form | 鼓腮基础＋闭口/嘴型接触修正 | 复用低维基础姿势/中性，不放空Motion | 本次高级模板设计，非VB原装树 |
| C03 | 鼓腮与伸舌 | CheekPuff × TongueOut | JawOpen | 极端组合残差 | 零修正；保留基础输出 | 本次高级模板设计，非VB原装树 |
| N01 | 鼻翼/鼻唇 | HQNoseSneerLeft × HQNoseSneerRight | HQUpperLipRaiseLeft, HQUpperLipRaiseRight, Form | 鼻翼基础＋鼻唇接触修正 | 复用低维基础姿势/中性，不放空Motion | 本次高级模板设计，非VB原装树 |
| H01 | 头部朝向 | FaceAngleX × FaceAngleY | FaceAngleZ | 姿态输出；需要绘制透视造型时再采姿势表 | 复用低维基础姿势/中性，不放空Motion | VB有数据；3D宿主通常走姿态链 |
| H02 | 头部位置 | FacePositionX × FacePositionY | FacePositionZ | 姿态输出/透视造型 | 复用低维基础姿势/中性，不放空Motion | 本次高级模板设计，非VB原装树 |
| H03 | 身体朝向 | BodyAngleX × BodyAngleY | BodyAngleZ | 姿态输出/造型，VB可从头姿推导 | 复用低维基础姿势/中性，不放空Motion | 本次高级模板设计，非VB原装树 |
| H04 | 身体位置 | BodyPositionX × BodyPositionY | BodyPositionZ | 姿态输出/造型，非独立身体观测 | 复用低维基础姿势/中性，不放空Motion | 本次高级模板设计，非VB原装树 |
| A01 | 显式音素嘴库 | HQAudioStrength × HQEmotion | phoneme_category | 每个音素一张幅度×表情二维表；音素类别不插成数字轴 | 复用低维基础姿势/中性，不放空Motion | VTS显式元音能力＋本次情绪化库设计 |
| P01 | 手位置 | HandPositionX × HandPositionY | HandPositionZ, HandAngleX, HandAngleZ | 左右各自展开，送手部姿态或造型空间 | 复用低维基础姿势/中性，不放空Motion | VTS手部公开参数；模板组织为设计 |
| P02 | 手指姿态 | FingerCurl | hand_side, finger_category | 每指独立，或手形组合；类别不作连续轴 | 复用低维基础姿势/中性，不放空Motion | 本次高级模板设计，非VB原装树 |
| P03 | 控制器造型 | StickX × StickY | Trigger, ButtonState | 外设驱动演出/手部；不占用面部形态键输出 | 复用低维基础姿势/中性，不放空Motion | 本次高级模板设计，非VB原装树 |

<!-- END:TREES -->

### E3. 总体结构与输出所有权

```text
高级面部模板
  嘴部基础
    M01 外嘴：Form×Open | Funnel,Press
    M02 下颌/口内：JawOpen×Forward | JawX
    M14 舌、M16 合并耸唇基础
  嘴部细分与组合修正
    M03…M13、M15（包含M04嘴宽/偏嘴修正）
  左右眼
    E01 核心眼睑 | EyeSmile
    E02 眼球XY
    E03 眼睑随视线、E04 偏嘴眼周、E05 双眼非对称修正
  眉、颊、鼻
    B01 基础；B02/B03 协同修正
    C01/C02/N01 基础与接触修正；C03 极端组合
  音频兼容能力
    默认：供给上面同一批嘴部语义轴
    显式音素库：A01仍位于同一嘴部求值/输出职责内
  姿态与外设能力
    H01…H04、P01…P03 → 姿态/演出，按宿主实现映射
```

这里的“基础/修正”是**姿势资产的角色**，不表示一定要做成多个Animator Layer。
可以在同一个受控Direct根下组织，或在上级条件混合中选完整姿势；沿用本项目影子求值、WD On约束。
头身和手部的骨骼/材质类曲线目前不能直接塞进只允许形态键曲线的面部会话；保留这些契约与宿主姿态出口，不宣称已有运行时支持。

**高质量不等于重复叠加同一动作。** M01的Form/Press已经包含笑、唇抬起和roll等信息；M06–M13必须是相对现有基础的残差，或者在明确选择的分支中替代相应基础。
例如作者完成单侧上唇姿势后，应减去当前M01已实现的部分，再把残差放入M07；不能把“完整上唇展开”再全量加一次。
残差在概念上是 `目标姿势 − 当前基础姿势`；落到Unity可用专门修正形态键、受控权重差或完整条件姿势，必须按目标网格与本项目求值约束选择实现。

此外，M02的下颌基础与M01的外嘴必须划清曲线绑定职责。若现有一个jawOpen形态键同时包含嘴唇和下巴，作者需要拆分或提供组合修正；树多不会自动分离网格形变。

## F. 选择性填动画：高级模板怎样下放

**下放是填充程度变化，不是换一套轴与树结构。** 模板应把每个槽标成基础槽、条件覆盖槽或修正槽，并保存fallback引用。

| 槽类型 | 没填时的明确定义 | 填了以后 |
| --- | --- | --- |
| 基础槽 | 指向作者认可的基础/中性片段；缺失则明确报出该部位未完成 | 使用该采样点的完整基础姿势 |
| 条件覆盖槽 | 复用相应XY位置的低维基础结果，保持姿势；不能泛化成全零 | 在该条件下覆盖为精修姿势 |
| 修正槽 | 零残差/无形变贡献 | 加入该条件的差值修正 |
| 完全未制作的扩展树族 | 所有修正为零，或所有条件切片复用同一基础 | 逐项填入后获得更细腻的动作 |
| 某参数来源缺失 | 选已声明的代理供值或中性，并保留“真实/代理/缺失”状态 | 更好的来源接入时仍用同一套树 |

例如：完整模板已经有`Form×Open | Funnel,Press`。低配模型只做了Form×Open时，各Funnel/Press切片复用同一基础嘴表；之后只替换精修的条件姿势。
独立上/下唇修正没做时，M07–M10为零残差，不破坏M01现有嘴型。

**这条fallback契约是本次设计要求，不是现有装配器已具备的“自动补洞”功能。** 当前装配器只做同名片段替换、保留模板片段和报告缺项；需要模板本身正确布置fallback，或以后显式实现装配规则。
不能把未填槽留为任意null Motion并假定Unity会自动按低维高质量结果降级。

## G. 音频：给已有造型语义供值

用户指出的方向正确：音频辅助/音素转换的主要工作是**接管或混合已有嘴部语义参数**，无需绕过参数层去直接修改模型。

| 模式 | 供给哪些语义 | 如何保留高质量模板 |
| --- | --- | --- |
| 纯视觉 | 表C/D各轴 | 整套高级树消费同一组语义参数 |
| 音量辅助 | Open、JawOpen、HQAudioStrength | 改轴值；矩阵和动画不变 |
| VTS合成嘴型辅助 | Open/Form；使用对应Plus参数时避免二次混入 | 同上 |
| VB VisemesARKit | OVR音素经公式变成Open/Jaw/Funnel/Press/Pucker等 | 同一高级嘴空间，音素数不是叶子数 |
| 高定音频/视觉融合 | 按轴融合视觉值与音频目标值；表情/不对称轴可继续由视觉提供 | 保留同一套HQ轴；每轴供值策略明确 |
| 显式VoiceA/I/U/E/O兼容 | 五个音素权重，及表D的发音幅度/表情代理 | A01在嘴部内部作为可选完整语音姿势库；仍通过同一最终嘴部输出，不跨层写模型 |

通用供值形式可用 `p_final = (1−a_p)×p_visual + a_p×p_audio`，前提是两边已经是相同语义/值域。
`a_p`逐轴配置；不要求笑容、左右嘴角与发音开合使用相同混合比例。公式属于设计示例，不冒充VB内置统一算法。
显式音素库与参数合成路线应有明确的模式选择/混合，不能同一份音频在两路重复全量施加。

显式库还要拆清**音素选择权重**和**发音幅度**：VTS的VoiceA等数值已经受音量影响，不能再无条件乘一次VoiceVolume。
一种可选适配是先对非负音素权重求和S，按`w_i/S`得到类别混合权重、按校准后的S提供HQAudioStrength；S为0时音频贡献归零。
这样幅度只进入A01矩阵一次。使用原始分析器时也可直接提供独立的类别分布与音量，但两种格式必须由适配器统一；这不是声称VTS内部采用该算法。

VoiceSilence、设备可用性、时间对齐负责供值与回退；不是新的美术坐标。
麦克风关闭/失效归零音频贡献并交还视觉；脸丢失不必同时关闭仍有效的音频。音频与面捕分别记录新鲜度。
VTS自身对Voice*的权重约束不自动适用于我们重新合成的数值；各适配器负责交出满足轴契约的值。

## H. 可核对的交付与证据

- [配套机器可读全表](VTS_HIGH_QUALITY_FACE_CATALOG.json)：98个VTS固定名字、52个原始ARKit、34个VB V3输出及曲线/平滑、39个HQ扩展、42个树族；所有`sample`均未指定。
- `.research/vts-creator-workflow/build_hq_catalog.py`：从当日官方页面快照和本机预设生成表；检查去重、分类计数、52输入无遗漏、扩展命名及树族引用。
- [旧官方样例映射审计](../.research/vts-creator-workflow/official-sample-audit.json)：同一模型的VB/普通VTS两套映射，证明供值能力变化与模型资产可分离；为2022样例，不冒充V3当前模板。
- [原装预设逐字段审计](../.research/vts-creator-workflow/preset-audit.json)：本机十份预设的完整字段与差异。
- [轴关系证据与推导](VTS_FACE_PARAMETER_SPACES.md)：公式、中性、参考血统及已证实/设计配对的区分。
- [创作者四轴外嘴实例](https://www.reddit.com/r/Live2D/comments/vjdbr4/)、[官方2024眉眼章节](https://www.youtube.com/watch?v=pZx_I_Y6kq4)、[官方2024嘴部章节](https://www.youtube.com/watch?v=7ZpOy3_E3Zo)。章节证明相关组合被讨论，**不代表表E整套树结构都是官方成品的逐项复制**。

完整性声明：表A覆盖本次官方固定清单；表B覆盖全部52原始形变；表C覆盖本机V3所有预设输出；表D/E是本次明确定义的完整高级模板范围。
它覆盖这些来源可提供的语义与所列高质量组合，但不会声称穷尽所有模型师能创造的表情、材质特效或任意参数交互。
