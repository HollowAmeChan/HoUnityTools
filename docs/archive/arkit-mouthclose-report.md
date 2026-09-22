> **已归档（2026-09-23）。** 一次性调查报告：`mouthClose` 这个语义在各家标准里怎么定义、
> 各家有没有在用。**结论已经体现在生成器与中间层的行为里**，报告本身只作证据留存。
> 现状见 [面捕工作流](../FACE_TRACKING_WORKFLOW.md) 与 [面捕中间层处理](../FACE_TRACKING_MIDDLE_LAYER.md)。

# ARKit `mouthClose` 调查报告

> 调查范围：Apple 官方规范 / OpenXR 与 Meta 标准 / VRCFaceTracking 内核与各模块源码 / Pico 4 Pro 链路 / Adjerry91 的 VRCFaceTracking-Templates 仓库 / MeowFace / Warudo / VRChat / 社区实践
> 所有结论均标注证据强度与验证边界。**未能验证的项集中列在第 12 节，请勿当作事实使用。**

---

## 0. 直接回答你的三个问题

| 你的问题 | 结论 |
|---|---|
| `mouthClose` 会不会被使用？ | **会。** 它是 Apple 官方 52 个形状之一，VRCFaceTracking 把它定义为一等公民 `MouthClosed` 并自动导出为 VRChat 参数 `v2/MouthClosed`；Jerry's 官方模板当前版本里 `FT/v2/MouthClosed` 是**已声明的网络同步参数**，控制器里有专门由它驱动的混合树。 |
| 它怎么被使用？ | **它不是 `jawOpen` 的补集，而是与 `jawOpen` 正交的"唇闭合"轴。** Apple 明确图示 `jawOpen=1` 与 `mouthClose=1` 可以同时成立。VRCFT 文档定义它 "Closes mouth (in relation to JawOpen)"，并且 VRCFT 内核有一条**默认开启**的 corrector 强制 `MouthClosed = min(MouthClosed, JawOpen)`。 |
| "用来跟 jawOpen 配合做咀嚼"这个说法成立吗？ | **机制上成立，但社区出处找不到。** 机制有 Apple 官方定义 + VRCFT 源码 + Haï 建模文档三重背书；但**在公开可检索的中/英/日社区资料中，找不到任何一条"用 mouthClose+jawOpen 做咀嚼/咬合"的记载**（这是明确的负面结果，见第 9 节）。 |
| MeowFace 为什么不输出？ | **因为它压根没有这个通道。** MeowFace 是 **Android** 应用（不是 iPhone），其 blendshape 集合只有 49 项，**枚举里就没有 `mouthClose`**；而且 VRCFT 的 MeowFace 模块用 key 白名单解析，即使 App 发了也会被丢弃。属**代码结构决定的必然结果**，不是 bug。 |
| 只能在 VRC / Warudo 状态机里搓吗？ | **对 MeowFace 路径是的；但对 iPhone ARKit 路径和 Pico 路径不是**——它们有原生 `mouthClose`/`MouthClose` 并直传到 `MouthClosed`。 |

---

## 1. Apple 官方规范：`mouthClose` 的定义

抓取方式：Apple 人类可读页是 JS 渲染的，抓不到正文；改用官方文档 JSON 端点成功。
- 页面：https://developer.apple.com/documentation/arkit/arfaceanchor/blendshapelocation/mouthclose
- JSON：https://developer.apple.com/tutorials/data/documentation/arkit/arfaceanchor/blendshapelocation/mouthclose.json

**Abstract（逐字，其中 "independent of jaw position" 在原页面为斜体强调）：**

> The coefficient describing closure of the lips *independent of jaw position*.

**Discussion（逐字）：**

> This coefficient describes a closing of the lips without relation to the position of the jaw (the jawOpen coefficient), so some values of the mouthClose coefficient can produce unrealistic facial expressions unless other coefficients are also set to realistic values.
>
> The figure below shows a face geometry (see ARSCNFaceGeometry) in three states:
> 1. A neutral face (all BlendShapeLocation coefficient values at 0.0, **including both jawOpen and mouthClose**)
> 2. Setting **only the jawOpen** coefficient to 1.0, while keeping all other coefficient values (**including mouthClose**) at 0.0
> 3. Setting **both the jawOpen and mouthClose** coefficients to 1.0, while keeping all other coefficient values at 0.0

**这三态就是本报告的核心。** 第 3 态 = "下巴张开 + 嘴唇闭合"，也就是咀嚼/咬牙/抿住嘴的几何姿态。Apple 用官方插图明确宣告：**这两个系数不是互斥的，也不是互补的（没有任何 `mouthClose = 1 - jawOpen` 这类公式）。**

对照 `jawOpen` 的官方文档（https://developer.apple.com/tutorials/data/documentation/arkit/arfaceanchor/blendshapelocation/jawopen.json），其 Discussion 只描述 0.0 与 1.0 两态、其余全 0，**完全没有**提到 `mouthClose` 的互补/互斥关系。可见两者在 Apple 的设计里是独立维度。

其它元数据：`ARBlendShapeLocationMouthClose`；`introducedAt: iOS 11.0 / iPadOS 11.0`；取值范围 `0.0 (neutral) 到 1.0 (maximum movement)`；`ARFaceGeometry.init(blendShapes:)` 与 `ARSCNFaceGeometry.update(from:)` 把整本字典当输入整体消费，Apple **没有**提供专门驱动 `mouthClose` 的逻辑。

### 1.1 Apple 自己的 sample 里没用它（值得注意）

Apple 官方 sample "Tracking and visualizing faces" 的 blendshape 演示（机器人头）逐字原文：

> the BlendShapeCharacter class performs this calculation, mapping the eyeBlinkLeft and eyeBlinkRight parameters to one axis of the SCNNode.scale factor of the robot's eyes, and the jawOpen parameter to offset the position of the robot's jaw.

**代码清单里只出现 `.eyeBlinkLeft / .eyeBlinkRight / .jawOpen`，没有 `mouthClose`。** Apple 也从未在任何文档里给出"组合使用建议"，只有上面那句"某些取值会产生不真实表情"的警告。

> 证据强度：**高**（官方文档 JSON 原文）。
> 限制：Apple Tech Talk 601 的 transcript 只取到被截断的前段，未见 `mouthClose`；官方 sample zip 未解压核验。

---

## 2. `mouthClose` 在 ARKit 形状清单中的位置

来源：https://developer.apple.com/tutorials/data/documentation/arkit/arfaceanchor/blendshapelocation.json（`topicSections` 即 Apple 的权威分组，逐项计数）

- Left Eye **7**：eyeBlinkLeft, eyeLookDownLeft, eyeLookInLeft, eyeLookOutLeft, eyeLookUpLeft, eyeSquintLeft, eyeWideLeft
- Right Eye **7**：同上镜像
- **Mouth and Jaw 共 27 个**，Apple 的排列顺序为：
  `jawForward, jawLeft, jawRight, jawOpen,` **`mouthClose`**`, mouthFunnel, mouthPucker, mouthLeft, mouthRight, mouthSmileLeft, mouthSmileRight, mouthFrownLeft, mouthFrownRight, mouthDimpleLeft, mouthDimpleRight, mouthStretchLeft, mouthStretchRight, mouthRollLower, mouthRollUpper, mouthShrugLower, mouthShrugUpper, mouthPressLeft, mouthPressRight, mouthLowerDownLeft, mouthLowerDownRight, mouthUpperUpLeft, mouthUpperUpRight`
  → **`mouthClose` 紧跟在 `jawOpen` 之后**，是同一家族里位置最靠前的一对。
- Eyebrows/Cheeks/Nose **10**；Tongue **1**（tongueOut）；另有 `init(rawValue:)`
- **合计 7+7+27+10+1 = 52**

**两个容易踩的坑：**
1. Apple **从不使用数字 "52"**，官方只说 "more than 50 unique BlendShapeLocation coefficients"。"AR52 / PerfectSync" 是社区叫法。
2. ARKit **不存在**无后缀的 `mouthSmile` / `mouthFrown` / `mouthDimple` / `mouthStretch`（只有 Left/Right 成对）；`mouthShrug` 只有 Upper/Lower（无左右）；`mouthLowerDown` / `mouthUpperUp` 也只在 Left/Right 形式存在。

---

## 3. 跨厂商对照：这个语义在别的标准里叫什么

### 3.1 OpenXR / Meta（FB）—— 叫 `LIPS_TOWARD`

- `XR_FB_face_tracking2`（OpenXR 扩展，v2）：规范源文逐字 `This extension defines 70 blend shapes for tracking facial expressions.`
- `XR_FB_face_tracking`（v1，Quest Pro / Movement SDK 用的那一套）：`This extension defines 63 blend shapes ...`（v2 比 v1 多的 7 个是舌头）
- **两代都在 index 50 有 `LIPS_TOWARD`。** 已用独立开源 OpenXR 绑定交叉核实：
  - https://raw.githubusercontent.com/korejan/libalxr-sharp/main/FBExpression.cs → `Lips_Toward = 50`，v2 `Max = 70`、v1 `Max = 63`
  - https://docs.rs/openxr-sys/latest/openxr_sys/struct.FaceExpressionFB.html 、`.../FaceExpression2FB.html` → 均含 `LIPS_TOWARD`
- 语义（取自 Khronos 官方规范源 asciidoc `fb_face_tracking2.adoc`）：
  > `XR_FACE_EXPRESSION2_LIPS_TOWARD_FB` forces contact between top and bottom lips to keep the mouth closed **regardless of the position of the jaw**.

**这与 Apple 的 "closure of the lips independent of jaw position" 几乎逐字对应。**
最强的独立佐证是 VRCFaceTracking 的官方映射表（可直接读到源文件）：

```js
// docs/tutorial-avatars/tutorial-avatars-extras/compatibility/meta-movement.mdx
['JawOpen', 'JAW_DROP'],
['MouthClosed', 'LIPS_TOWARD'],
```

```js
// docs/tutorial-avatars/tutorial-avatars-extras/compatibility/arkit.mdx
['JawOpen', 'jawOpen'],
['MouthClosed', 'mouthClose'],
```

> ⚠️ **重要更正（推翻我在调查初期的一个假设）**："Pico/Quest 系跟随 FB 集合所以没有唇闭合" 是**错的**。FB 集合里有唇闭合形状，只是名字叫 `LIPS_TOWARD` 而不叫 `mouthClose`。
> ⚠️ 验证边界：上面那句英文定义来自 Khronos 规范源文件，但另一路子调查**未能独立复现该句**（`registry.khronos.org` 返回 403 Cloudflare 拦截、`developers.meta.com` / `developer.oculus.com` 解析到非公网 IP 被拒、精确短语检索零命中）。**可以确证的是**：`LIPS_TOWARD` 在 v1/v2 中都存在且 index=50，且 VRCFT 官方把 `MouthClosed` 映射自它。

### 3.2 SRanipal（VIVE）—— 叫 `Mouth_Ape_Shape`，语义**相反**

VRCFT 官方映射页 `vive-sranipal.mdx` 逐字：

```js
['JawOpen', 'Jaw_Open'],
['MouthClosed', <>Mouth_Ape_Shape¹<br/><code>Negates Jaw_Open</code></>],
```

> **`Mouth_Ape_Shape`** — This SRanipal expression controls Unified's MouthClosed expression. To get the intended tracking on this SRanipal shape, MouthClosed must **negate JawOpen** in the animation proportional to the amount MouthClosed is active. Alternatively, this blendshape can be turned into MouthClosed by negating JawOpen in the blendshape itself.

**这条极其有意思**：SRanipal 根本没有独立的唇闭合形状，它只有一个 `Mouth_Ape_Shape`——而 "Ape Shape"（猿嘴）正是 **`jawOpen` + `mouthClose` 同时拉满**那个姿态。

也就是说：**你要做的"咀嚼姿态"，在日本/韩国面捕圈有个正式名字叫 Mouth Ape Shape。** VRCFT 文档定义 `MouthClosed` 为 "a direct inverse of 'Mouth Ape Shape', which is a combination of both Jaw Open and Mouth Closed"。

### 3.3 FACS —— 唇闭合与下颌下降本就是两个维度

CMU 镜像的 Ekman & Friesen FACS 表（https://www.cs.cmu.edu/~face/facs.htm）逐字：

- **AU24 Lip Pressor**（Orbicularis oris）
- **AU25 Lips part**（Depressor labii inferioris / relaxation of Mentalis / Orbicularis oris）
- **AU26 Jaw Drop**（Masseter, relaxed Temporalis and internal Pterygoid）
- 另有 AU23 Lip Tightener、AU22 Lip Funneler、AU28 Lip Suck

FACS 把"唇分离"与"下颌下降"当作两个独立维度编码，这与 Apple 的建模一致。
> ⚠️ **`mouthClose≈AU24/25`、`jawOpen≈AU26` 的对应关系是我的分析性推断，Apple 和 Khronos 文档里都没有明文。** Apple 的 `mouthClose` 页面 JSON 中**完全不含** "FACS"/"Action Unit" 字样。另外 Apple 在 Tech Talk 601 里把 blendshape 描述为 "weighted parameters representing over 50 specific **muscle movements** of the detected face"。

---

## 4. VRCFaceTracking 内核：它是"一等公民"，但内核不帮你填

### 4.1 枚举定义

文件：`VRCFaceTracking.Core/Params/Expressions/UnifiedExpressions.cs`

```csharp
#region Jaw Exclusive Expressions

JawOpen,          // Opens the jawbone...
JawRight,         // ...
JawLeft,          // ...
JawForward,       // ...
JawBackward,      // ...
JawClench,        // Specific jaw muscles that forces the jaw closed...
JawMandibleRaise, // Raises mandible (jawbone).

MouthClosed,      // Closes the mouth relative to JawOpen. Basis on the complex tightening action of the orbicularis oris muscle.

#endregion
```

### 4.2 自动导出为 VRChat 参数 `v2/MouthClosed`

文件：`VRCFaceTracking.Core/Params/Expressions/UnifiedExpressionsParameters.cs`

```csharp
private static IEnumerable<EParam> GetAllBaseExpressions() =>
    ((UnifiedExpressions[])Enum.GetValues(typeof(UnifiedExpressions))).ToList().Select(shape =>
       new EParam("v2/" + shape.ToString(), exp => exp.Shapes[(int)shape].Weight, 0.0f));
```

### 4.3 **内核不做任何补全**

- `UnifiedTrackingData.Shapes` 是 `UnifiedExpressionShape[]`（长度 `Max+1`），默认 `Weight = 0f`；**内核每帧不清零、也没有 fallback 赋值**。模块不写 → 永远 0。
- `UnifiedTrackingMutator.Enabled` 默认为 `false`，且只执行用户手配的 mutation，**没有内置的 MouthClosed 推导**。
- `UnifiedSimplifier.ExpressionMap` / `UnifiedCombinedShapes` 里**没有任何一条会推导 `MouthClosed`**。

**结论：`v2/MouthClosed` 的值 100% 来自模块。** 所以"必须在状态机层搓"这个判断，对没有提供该形状的数据源是完全正确的。

### 4.4 ⚠️ 默认开启的钳制：`MouthClosed = min(MouthClosed, JawOpen)`

文件：`VRCFaceTracking.Core/Params/Data/Mutation/Correctors.cs`

```csharp
public override string Name => "Unified Correctors";
public override string Description => "Processes data to conform to Unified Expressions.";

[MutationProperty("MouthClosed/JawOpen Clamp", true)]   // ← 默认 true
public bool mouthClosedFix = true;
...
if (mouthClosedFix)
{
    data.Shapes[(int)UnifiedExpressions.MouthClosed].Weight =
        Math.Min(
            data.Shapes[(int)UnifiedExpressions.MouthClosed].Weight,
            data.Shapes[(int)UnifiedExpressions.JawOpen].Weight
        );
}
```

来历：[PR #221](https://github.com/benaclejames/VRCFaceTracking/pull/221)，由 VRCFT 作者 benaclejames 提交，正文写明 **"Requested by @Adjerry91"** —— 就是 Jerry's Templates 的作者。

> ⚠️ PR 描述说 "MouthClosed can never be a **lower** value than JawOpen"（≥），但合并后的代码是 `Math.Min`（≤）。**方向相反，以代码为准**，PR 文字应为笔误。
> **对你的咀嚼方案的直接影响**：这个 clamp 只阻止 `MouthClosed > JawOpen`。做咀嚼时两者都拉满（≈1）是**可以通过**的；但如果你想让"下颌先不动、嘴唇先咬合"，那一瞬间 `JawOpen≈0` 会把 `MouthClosed` 一起压成 0。要完全自由控制，需要去 VRCFT 的 Mutation/Corrector 设置里**关掉 `MouthClosed/JawOpen Clamp`**。

### 4.5 官方文档的覆盖缺口（一条有新闻价值的发现）

VRCFT 的 `compatibility/` 目录**只有 3 个逐形状映射页**：

| 文件 | 内容 |
|---|---|
| `overview.mdx` | 四列总表：Unified × ARKit × SRanipal × Meta Movement |
| `arkit.mdx` | ARKit（iOS） |
| `meta-movement.mdx` | Meta Movement（Quest Pro） |
| `vive-sranipal.mdx` | VIVE SRanipal |

**不存在** Pico 专页、HP Omnicept 专页、Vive Streaming / Virtual Desktop / MeowFace / iFacialMocap 的任何映射页。
→ 所以"某个具体设备到底给不给 `MouthClosed`"，**在官方文档层面完全无从判断**，只能读模块源码。MeowFace 恒 0 这件事就整个落在文档盲区里。官方也从**未**对 `MouthClosed` 给出任何"某些硬件不支持"的警告。

---

## 5. 各数据源是否真的提供 `MouthClosed`（逐模块源码审计）

| 数据源 | 提供了吗 | 机制 / 源码原文 |
|---|---|---|
| **ARKit / iPhone**：iFacialMocap / FaceMotion3D | ✅ 原生直传 | `UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.MouthClosed].Weight = server.FaceData.BlendValue("mouthClose");` |
| **ARKit / iPhone**：Unreal LiveLink | ✅ 原生直传 | `unifiedExpressions[(int)UnifiedExpressions.MouthClosed].Weight = trackingData.lowerface.MouthClose;`（`LiveLinkNames` 第 19 项就是 `MouthClose`） |
| **Pico 4 Pro / Enterprise** | ✅ 代码路径存在（真机数据未验证） | `unifiedShape[(int)UnifiedExpressions.MouthClosed].Weight = this.scaler!.UnifiedExpressionShapeScale(pxrShape[(int)BlendShapeIndex.MouthClose], UnifiedExpressions.MouthClosed);` |
| **Meta Quest Pro / Quest Link (OpenXR)** | ⚠️ **合成值** | `unifiedExpressions[(int)UnifiedExpressions.MouthClosed].Weight = Math.Max(0, Math.Min(weights[(int)ExpressionFB.LIPS_TOWARD], weights[(int)ExpressionFB.JAW_DROP]));` ← 被 `JAW_DROP` 门控 |
| **ALVR**（Meta 通道） | ✅ | `w[MouthClosed] = p[LipsToward];` |
| **ALVR**（HTC/SRanipal 通道） | ✅ | `w[JawOpen] = p[JawOpen] + p[MouthApeShape];` … `w[MouthClosed] = p[MouthApeShape];` |
| **ALVR**（Pico 通道） | ✅ | `w[MouthClosed] = p[MouthClose];` |
| **官方 SRanipal 模块** | ✅（源形状是反的） | `Shapes[JawOpen].Weight = blend_shape_weight[JawOpen] + blend_shape_weight[MouthApeShape];` … `Shapes[MouthClosed].Weight = blend_shape_weight[MouthApeShape];` |
| **MeowFace（Android）** | ❌ **从不写入，恒 0** | 见第 6 节 |
| **Steam Frame（Valve）** | N/A | `Supported => (true, false)`，纯眼动，不写面部 |
| Virtual Desktop / Vive Streaming / SteamLink(LinkFT) | ❓ 未验证 | 闭源或映射在 18KB 文件里未逐行核验 |

**核心结论：在本次审计覆盖的所有开源面部模块中，没有一个模块"收到唇闭合形状却丢弃"——唯一的例外是 MeowFace。**

---

## 6. MeowFace 专项（直接解释你的观察）

### 6.1 先纠正一个前提：MeowFace 是 **Android** 应用

VRCFT 官方文档标题即 **"Android MeowFace"**，路径 `/docs/hardware/desktop/android/meowface`，原文：

> An Android phone (or realistically any Android-running device with a camera) can generate **ARKit-_like_** face tracking data for VRCFT using the MeowFace app by Suvidriel.

Google Play 包名 `com.suvidriel.meowface`。
👉 **iPhone 侧对应的工具是 iFacialMocap / FaceMotion3D / Unreal LiveLink Face，而这三个都真给 `mouthClose`。** 你如果是在安卓机上测的，那结论完全一致。

### 6.2 硬证据一：VRCFT 的 MeowFace 模块枚举里没有 `mouthClose`

文件：`regzo2/VRCFaceTracking-MeowFace` → `MeowDataStructure.cs`

```csharp
public enum MeowShapeType
{
    jawOpen, eyeLookOutRight, eyeLookDownRight, noseSneerLeft, eyeLookOutLeft, noseSneerRight,
    mouthLeft, headRight, eyeLookUpLeft, eyeLookUpRight, headUp, mouthRollLower, cheekPuff,
    browOuterUpRight, eyeLookInRight, mouthUpperUpLeft, browInnerUpRight, headRollRight,
    eyeLookInLeft, jawLeft, browInnerUpLeft, mouthUpperUpRight, mouthRight, browDownRight,
    headDown, eyeWideRight, browDownLeft, mouthShrugUpper, mouthRollUpper, eyeWideLeft,
    browOuterUpLeft, tongueOut, eyeSquintLeft, jawRight, mouthLowerDownRight, mouthLowerDownLeft,
    eyeLookDownLeft, eyeSquintRight, mouthFrownLeft, mouthFrownRight, mouthSmileRight,
    headRollLeft, eyeBlinkLeft, mouthPucker, eyeBlinkRight, mouthSmileLeft, mouthFunnel,
    headLeft, browInnerUp
}
```

共 **49 项，没有 `mouthClose`**。而且缺的不止它——还有 `jawForward`、`cheekSquintLeft/Right`、`mouthShrugLower`、`mouthDimple*`、`mouthPress*`、`mouthStretch*`、`mouthTightener*`。**MeowFace 的模型本来就是一张残缺的 ARKit 子集表。**

并且 `UpdateExpressions()` 里**从不出现** `UnifiedExpressions.MouthClosed`（全文只有 JawOpen / JawLeft / JawRight 三个 jaw 赋值）。

### 6.3 硬证据二：就算发了也会被丢弃

文件：`MeowJsonConverter.cs`

```csharp
foreach (MeowNamedShape namedShape in namedShapes)
{
    // Try to convert the string to an enum
    if (Enum.TryParse(namedShape.k, true, out MeowShapeType shapeType))
    {
        blendShapes[(int)shapeType].v = namedShape.v;
    }
}
```

MeowFace 发的是 `[{"k":"...","v":...}, ...]` 命名列表；**解析不到 `MeowShapeType` 的 key 直接忽略**。

### 6.4 硬证据三：两个独立作者的实现互相印证

第三方实现 `Jeka8833/MeowFaceVRCFTInterface`（2025-12 仍在更新）独立枚举的 MeowFace 参数名表 **48 项 + 1 项被注释，同样没有 `mouthClose`**；其 `JawMapper` 只映射三个：

```csharp
meowFaceParam.TrySetToVrcftShape(UnifiedTracking.Data.Shapes, UnifiedExpressions.JawRight, MeowFaceParam.JawRight);
meowFaceParam.TrySetToVrcftShape(UnifiedTracking.Data.Shapes, UnifiedExpressions.JawLeft,  MeowFaceParam.JawLeft);
meowFaceParam.TrySetToVrcftShape(UnifiedTracking.Data.Shapes, UnifiedExpressions.JawOpen,  MeowFaceParam.JawOpen);
```

两个互不相关的作者、相隔两年，独立枚举出同一结论。

### 6.5 结论与边界

- **确定无疑**：VRCFT + MeowFace 组合下 `v2/MouthClosed` **必然恒为 0**（代码结构决定）。
- **强推定但非铁证**：MeowFace 应用本体不发 `mouthClose`。理由：App 闭源，且 `suvidriel.itch.io/meowface`、APKPure、Google Play 页面三次抓取全部失败，拿不到本体自述的形状清单。
- **未找到**：任何"MeowFace 的 mouthClose 恒为 0"的公开一手 bug 报告（GitHub issue / Reddit / 论坛均无）。**请勿把它写成"社区已知问题"。**
- 顺带查到该模块一个真实历史 bug：[issue #12](https://github.com/regzo2/VRCFaceTracking-MeowFace/issues/12) 报告数值整体错位（"the eyebrows move the jaw to the right / sticking tongue out closes left eye"），根因是旧版按**数组下标**映射；[PR #18](https://github.com/regzo2/VRCFaceTracking-MeowFace/pull/18)（2025-10-23）才改成按 key 映射。**而 registry 上 `MeowFaceTrackingModule` 仍是 `Version 1.3 / LastUpdated 2023-04-20`**——即注册表安装版落后于仓库修复版。你如果是从注册表装的，可能吃的是老版本。

---

## 7. Pico 4 Pro / Enterprise 专项

### 7.1 关键更正：Pico 的 72 维集合是 **ARKit 命名**，不是 FB 集合

`PicoConnectors/Pxr.cs`：

```csharp
public static class Pxr { public const int BLEND_SHAPE_NUMS = 72; }

public enum BlendShapeIndex
{
    EyeLookDown_L = 0, NoseSneer_L = 1, EyeLookIn_L = 2, BrowInnerUp = 3, BrowDown_R = 4,
    MouthClose = 5,                 // ← 唇闭合在这里
    MouthLowerDown_R = 6, JawOpen = 7, MouthUpperUp_R = 8, MouthShrugUpper = 9,
    MouthFunnel = 10, ... MouthShrugLower = 17, MouthRollLower = 18,
    MouthSmile_L = 19, MouthPress_L = 20, MouthSmile_R = 21, MouthPress_R = 22,
    MouthDimple_R = 23, MouthLeft = 24, JawForward = 25, ...
    MouthDimple_L = 49, MouthLowerDown_L = 50, TongueOut = 51,
    PP = 52, CH = 53, o = 54, ..., sil = 71      // 后 20 项是 viseme
}
```

- **index 0–51（52 项）= ARKit 的 52 个形状名**（顺序是 Pico 自己的，不是 Apple 文档顺序）
- **index 52–71（20 项）= viseme**
- 同一张表在 `alvr-org/VRCFT-ALVR` 的 `PicoFaceTracking.cs`（`enum FacePico`）里独立出现，互相印证

→ **所以 Pico 提供的是 ARKit 风格的 `MouthClose`，而不是 FB 的 `LIPS_TOWARD`**；这也是为什么模块能 1:1 直传。`BlendShapeIndex.MouthClose = 5` 与 FB 的 `Lips_Toward = 50` 是**两个不同的集合、不同的索引**。

### 7.2 模块与使用条件

- 模块：**Pico4SAFTExtTrackingModule**（[源码仓库](https://github.com/regzo2/PicoStreamingAssistantFTUDP/tree/vrcfacetracking-module)），从 [VRCFT Module Registry](https://docs.vrcft.io/docs/vrcft-software/vrcft) 安装。
- VRCFT 官方 Pico 文档要点（https://docs.vrcft.io/docs/hardware/vr/pico/pico4pe）：
  - 头显固件至少 **v5.9.0**
  - Pico 设置 **LAB** 页里同时开启 **Eye Tracking** 和 **Lip Tracking**
  - 装 **Pico Connect**（PC 端）
  - **改隐藏配置**：`C:\Users\{用户名}\AppData\Roaming\PICO Connect\setting.json` 里把 `faceTrackingTransferProtocol` 改成 `2`、`faceTrackingMode` 改成 `1`（legacy protocol + Image-driven）。文档明说这是临时方案，因为模块不支持更新的协议；改模式还能避免"有麦克风输入时面捕停止工作"。
  - 已知缺陷：模块**总是会初始化**，且关闭 VRCFT 时不结束自己的更新线程，可能需要强制结束进程。
- VRCFT 兼容性表给 Pico 4 Pro/Enterprise 的评分：Lower Face Expressibility **7/10**、Face Tracking Quality **4/10**（Quest Pro 是 8/10 与 7/10，ARKit 是 8/10 与 8/10）。

### 7.3 ⚠️ 仍未验证的关键点

**代码路径存在 ≠ 真机有数据。** 我没有找到任何一手证据表明 Pico 硬件真的在 index 5 输出有意义的非零值。相关线索：
- registry 条目 `LastUpdated: 2023-05-05`，`UsageInstructions` 写明 "REQUIRES THE LATEST (currently unreleased) STREAMING ASSISTANT"
- 没有找到 Pico 官方文档标注哪些 blendshape 未被硬件支持/恒为 0
- `Pxr.cs` 是模块作者的二手转写，**未找到 Pico 官方 Unity SDK / 官方文档里那张 72 项表的原始定义**
- UDP 包是 72 个 float 全量无条件传输、还是带有效性掩码，未确认

👉 **这正好是你上手实测要回答的问题**（见第 11 节验证清单）。

---

## 8. Jerry's VRCFaceTracking-Templates 专项（你指定的仓库）

仓库：https://github.com/Adjerry91/VRCFaceTracking-Templates
用法：VRCFury / Modular Avatar 预制体，把 VRCFT 的 OSC 数据接到 avatar 的 blendshape 上。

### 8.1 资产层面：`mouthClose` 有专用动画

`Packages/adjerry91.vrcft.templates/Animations/Face Blendshapes/ARkit/` 下有：

| 文件 | GUID | 内容 |
|---|---|---|
| `Mouth_Closed.anim` | `ed25882832d93ff4a87464606589ced2` | `blendShape.mouthClose = 100`（作用在 `Body` 网格） |
| `Mouth_Closed 0.anim` | `dd4e8df4016be6244bb6f3cf99e3afea` | 该形状的 0 值版本 |
| `Jaw_Open.anim` | `5d8a2d25b34143445a7f45e6a380b371` | `blendShape.jawOpen = 100` |
| `Jaw_Open 0.anim` | `51e34eb3922895b4dab16b6d4ce33790` | `blendShape.jawOpen = 0` |
| `Jaw_Open_0_Mouth_Closed_0.anim` | `49e46bbabbaedc6418ce4a434c7d241d` | 双曲线 |
| `Jaw_Open_1_Mouth_Closed_0.anim` | `8bba9ccc32a27d548975921731a4f09f` | 双曲线 |
| `Jaw_Open_1_Mouth_Closed_1.anim` | `5b59f0bc557e882458166e6266e55e37` | 双曲线（jawOpen=100 且 mouthClose=100） |

`Jaw_Open_1_Mouth_Closed_1.anim` 的实际内容（已完整读取）：

```yaml
m_Name: Jaw_Open_1_Mouth_Closed_1
m_FloatCurves:
  - attribute: blendShape.jawOpen     value: 100   path: Body
  - attribute: blendShape.mouthClose  value: 100   path: Body
```

**这三个组合 clip 是教科书式的二维混合树四角** `(0,0) (1,0) (1,1)`——正是 Apple 文档第 3 态的直接实现。

### 8.2 时间线：CHANGELOG 里有一条完整的"做了又回退"轨迹

来自 `Packages/adjerry91.vrcft.templates/CHANGELOG.md`：

- `[5.0.5] 2023-10-06` — "Add MouthClosed limit to LipSuckUpper, CheekSuckLeft and CheckSuckRight"
- `[5.2.0] 2024-01-27` — "Modify logic for MouthOpen and Closed"
- `[5.2.1] 2024-02-10` — "Add limits to MouthClosed. **MouthClosed should never be larger value than JawOpen.**"
- `[5.3.2] 2024-02-22` — "**Change logic on JawOpen and MouthClosed; converted to 2D blend**" ＋ "Add LipSuckLower/Upper MouthClosed limit" ＋ "Limit MouthRaiserUpper from MouthClosed" ＋ "Limit Brow Sad emulation from MouthClosed" ＋ "Remove MouthUpperUp Left&Right Limit with MouthClosed"
- `[5.3.3] 2024-02-26` — "**Revert Jaw Open and Mouth Closed logic as it breaks SRanipal tracking headsets.**"
- `[7.0.0] 2025-10-08` — "Add FaceTrackingEmulation toggle in settings menu (Default On). Turning emulation toggle off will disable any emulation of other face tracking blend shapes with other face tracking parameters." ＋ "Add FaceTrackingLimits toggle"

`5.2.1` 那句限制后来被**推到了 VRCFT 内核**（第 4.4 节的 `Correctors.cs`，由 Adjerry91 要求加入），所以它在模板和内核两层都生效。

### 8.3 控制器实测：二维混合树**已被回退成一维**

我把当前 main 的 `Animators/ARkit Blendshapes/FX - Face Tracking - ARKit Blendshapes.controller` 抓下来做了实际 grep。实测发现：

**(a) 存在一条活的 1D 限幅树，参数就是 MouthClosed：**

```yaml
m_Name: Limit Jaw Open (MouthClosed)
m_Childs:
  - m_Motion: {guid: 5d8a2d25b34143445a7f45e6a380b371}   # = Jaw_Open.anim (jaw 全开)
    m_Threshold: 0
  - m_Motion: {guid: 51e34eb3922895b4dab16b6d4ce33790}   # = Jaw_Open 0.anim (jaw 关闭)
    m_Threshold: 4
m_BlendParameter: OSCm/Proxy/FT/v2/MouthClosed
m_MinThreshold: 0
m_MaxThreshold: 4
m_BlendType: 0        # 0 = Simple 1D
```

→ **MouthClosed 上升会强制把 jaw 拉回闭合**（即用唇闭合量去"限制"下颌张开幅度）。这是这条参数在模板里最主要的活用途。同类限幅树还有 `Limit Mouth Frown Left (MouthX-Left)`、`Limit Mouth Lower Down (Lip Funnel)`、`Tongue Out Limit (LipFunnel)`。

**(b) 名为 `Jaw Open Blend` 的树是 1D，但残留着 2D 的字段：**

```yaml
m_Name: Jaw Open Blend
m_Childs:
  - m_Motion: {guid: 51e34eb3922895b4dab16b6d4ce33790}   # Jaw_Open 0.anim
    m_Threshold: 0
    m_Position: {x: 0, y: 0}
  - m_Motion: {guid: 5d8a2d25b34143445a7f45e6a380b371}   # Jaw_Open.anim
    m_Threshold: 1
    m_Position: {x: 1, y: 0.5}                            # ← 二维坐标残留
m_BlendParameter: OSCm/Proxy/FT/v2/JawOpen
m_BlendParameterY: OSCm/Proxy/FT/v2/MouthClosed           # ← 二维 Y 轴参数残留
m_BlendType: 0                                            # ← 但类型是 1D
```

**这就是 `5.3.3` 那次 revert 留下的化石**：树被从二维改回一维（`m_BlendType: 0`），但 `m_BlendParameterY` 和二维坐标没被清掉。而且这条树的**两个子节点是 `Jaw_Open 0.anim` / `Jaw_Open.anim`，不是那三个 `Jaw_Open_x_Mouth_Closed_x` 组合 clip**——也就是说**活的 JawOpen 混合里已经没有 MouthClosed 这个轴了**。

**(c) 我验证了 `m_BlendType` 的枚举含义**（这决定了 (b) 的判定）：在该控制器已取回的部分里，`m_BlendType` 只出现 0、1、4 三个值；其中值为 **1** 的两条树是真正的二维树——两条眼动树 `OSCm/Proxy/FT/v2/EyeRightX × EyeY` 与 `EyeLeftX × EyeY`，子节点坐标形如 `(0.7, 0)`、`(0, 0.7)`、`(0, -0.7)`：
- `0` = Simple 1D（如 `Limit Jaw Open (MouthClosed)`，两子节点坐标均为 `(0,0)`）
- `1` = 2D
- `4` = Direct（如 `Binary_FT/v2/JawX_Negative`）

→ **`Jaw Open Blend` 是 1D 而非 2D，判据可靠。**

**(d) 参数是正式声明的。** `Animators/ARkit Blendshapes/Parameters - Face Tracking - ARkit Blendshapes.asset` 里逐字有：

```yaml
- name: FT/v2/JawOpen
  valueType: 1        # Float
  saved: 0
  defaultValue: 0
  networkSynced: 1
- name: FT/v2/MouthClosed
  valueType: 1        # Float
  saved: 0
  defaultValue: 0
  networkSynced: 1
```

注意 `FT/v2/MouthClosed` **没有** 1/2/4 那种二进制解码变体（其他很多参数都有 `X1/X2/X4`），和 `FT/v2/JawOpen` 一样是普通的网络同步 float。控制器里另有一处 `OSCm_Input` 树直接以 `FT/v2/MouthClosed` 为混合参数，以及三处以 `OSCm/Proxy/FT/v2/MouthClosed`（代理参数）为混合参数。

README 也明确：**"All parameters are exposed on the template prefab. Do NOT use FT/v2/ parameters as these are the raw data coming from OSC... Please use OSCm/Proxy/FT/v2 parameters when using in custom animations."** —— 你要自己搓动画时，用 `OSCm/Proxy/FT/v2/MouthClosed`，不要用 `FT/v2/MouthClosed`。

### 8.4 ⚠️ 本节最重要的验证边界

**控制器原始大小 441,805 字节，但抓取工具在 100,377 字节处截断（只拿到约 23%）。**

因此：
- 我**能**确证：(a)(b)(c)(d) 全部成立（它们都落在已取回部分）。
- 我**不能**确证：那三个 `Jaw_Open_x_Mouth_Closed_x` 组合 clip 是否在控制器的**剩余 77%** 里被某个状态/树引用。我在已取回部分 grep 这三个 GUID **零命中**，但**这不足以判定它们是孤儿资源**——请不要把这条当成结论。
- 从旁证看，它们**很可能是 5.3.2 那次二维混合的遗留资产**（因为 `Jaw Open Blend` 这个天然该放它们的树上放的是普通 clip，且类型已回退为 1D）。

---

## 9. 社区实践：真被用吗？"咀嚼"这个说法有出处吗？

### 9.1 唯一一条带量化数据的实测

[QtMeshEditor PR #1067](https://github.com/fernandotonon/QtMeshEditor/pull/1067)，小节标题就叫 **"The mouthClose fix"**：

> Reported from a real take: **`mouthClose` alone pulls the lower lip to the nose, while `jawOpen 1.0 + mouthClose 1.0` looks right.**
> That is the shape behaving correctly. **`mouthClose` is an ARKit counter-shape — meaningful only as a partial cancellation of `jawOpen`.**

附顶点级测量：

| | `mouthClose` | `jawOpen` |
|---|---|---|
| verts up | **12,903** | 123 |
| verts down | 328 | **18,195** |
| max displacement | **+3.81** | −3.54 |
| cosine with the other | **−0.46** | |

并指出 NVIDIA 的角色模型用了**相反约定**（`mouthClose` 下移 16,364 个顶点、cosine **+0.41**），因此按名做了 0.5 倍缩放修正，修正后 "peak `mouthClose` 0.176 → 0.085, **frames where it exceeded twice `jawOpen` 9 → 0**"。

> **这是唯一一条量化证明"`mouthClose` 不能单独驱动、必须与 `jawOpen` 成对使用"的社区证据。** 强度中高（单一开发者，但有具体数字、前后对比、可复核）。

### 9.2 建模工具官方文档的背书

Haï 的 FaceTra Shape Creator 有 [Mouth Closed 专页](https://docs.hai-vr.dev/docs/products/facetra-shape-creator/shapes/mouth-closed)：

> you know that **Mouth Closed is one of the unusual shapes, because as a technicality, it morphs vertices relatively to the Jaw Open shape.**
> **The preview you see in the editor scene is not the MouthClosed blendshape on its own.** The scene shows the MouthClosed blendshape while the JawOpen shape is playing, and the JawOpen Jaw Puller is active.
> The same face pulling deformation of the Jaw Open shape is already visibly applied in the editor, and the blendshapes defined inside the Jaw Open shape is already cancelled.
> You will need to assemble blendshapes that represents the mouth being closed.

→ **主流 VRChat 建模工具把它当作"必须真做"的形状，且明确说它的正确形态与 Blender 里看到的单独形状不同。**

### 9.3 硬件厂商的已知实现缺陷（唯一一条"某设备 MouthClosed 不对"的报告）

VRCFT 的 [Vive Focus 3 / XR Elite 文档](https://docs.vrcft.io/docs/hardware/vr/vive/focus3_xre)：

> Even now, Vive's implementation of VRCFT's functionality is buggy, slow, and **handles some parameters (notably MouthClosed) completely incorrectly.**

同页 Troubleshooting：症状 "My avatar's lower lip is up too high/clipping even when I have a neutral facial expression IRL"，原因 "You're using the Vive Streamer's built in output"，解法是改用 VRCFT 而不是 Vive Streamer 自带输出。
> 这是**厂商软件实现**的错误，不是追踪器输出恒 0。强度：弱-中（VRCFT 单方面陈述，未见 HTC 回应）。

### 9.4 ❌ 明确的负面结果：找不到"咀嚼"用例

调查方用了中/英/日多轮检索：

`"mouthClose" "jawOpen" chewing`、`fake chewing animation avatar jawOpen mouthClose`、`VRChat chewing expression blendshape`、`ARKit blendshape lips sealed jaw open bite expression`、`咀嚼 シェイプキー`、`口を閉じたまま顎を開く シェイプキー ARKit` 等。

**全部未命中。** 结论：
- ❌ 没有任何公开帖子/文档/issue 说"我用 mouthClose + jawOpen 做咀嚼/咬人/磨牙/咬紧牙关"。
- ❌ Reddit 上关于 `mouthClose` 的讨论：**零命中**（VRChat 论坛、Steam 讨论区同样未命中）。
- ❌ VS seeFace / Live2D / iFacialMocap / Virtual Motion Capture 官方文档中均未找到 `mouthClose` 的说明。
- ❌ 没有官方文档点名 `mouthClose` 是"可以留空/自动填充"的形状。

社区对这对形状的讨论**全部停留在"嘴唇闭合 + 下巴张开"这一层**，没有延伸到咀嚼这个具体用例。

> **这是负面结果，不等于证伪。** 真实用例很可能存在于 Discord 或视频教程（YouTube/Bilibili）中，而文本检索覆盖不到。但你引用"经验帖说用来做咀嚼"时，**目前找不到可引用的出处**——你等于是在自己开荒。
> 可能的解释（**属推测**）：(a) 该用法确实罕见；(b) 做咀嚼的人直接用下颚骨旋转 + 自定义嘴形，不走 ARKit 面捕形状。

### 9.5 建模/工具侧其它值得知道的点

- **VRM 标准里没有 `mouthClose` 的等价物**（硬性结论）。VRM 1.0 的 Expression preset 只有 Blink / BlinkLeft / BlinkRight / LookUp / LookDown / LookLeft / LookRight / **Aa / Ih / Ou / Ee / Oh** / Happy / Angry / Sad / Relaxed / Surprised / Neutral。ARKit 52 形状必须以 **custom expression** 挂上去。
- **Unified Expressions 有 `MouthClosed`，且是正牌 Base Shape**（描述 "Closes mouth (in relation to JawOpen)."）。其参考图文件名特别叫 `avatar_ref_mouth_closed_explain.png`——只有它带 `_explain` 后缀，说明官方为它单独画了解释图。
- **PerfectSync 的 52 个 clip 清单里 `MouthClose` 在列**（VMagicMirror 官方文档逐条列出，位于 `MouthShrugLower` 与 `MouthSmileLeft` 之间）。官方允许"太细微的形状可以跳过、留空"，但这是**对 52 个形状的通用许可，并未点名 `mouthClose`**。
- **自动生成工具会主动跳过它**：Kx VRC ARKit BlendShape Generator 的 issue #66 逐字（日文）："**生成結果が破綻するので作らせたくない**: MMDの「あ」から導いた `mouthClose` や `cheekPuff` のように、**マッピングは成立するが動きが不自然になる名前がある**。"（因为生成结果会崩所以不想让它生成：像从 MMD 的「あ」推导出的 mouthClose 和 cheekPuff，映射虽然成立但动作会不自然。）该工具另有「口の打ち消し」(Mouth Cancellation) 功能，**默认烘焙目标就是 `jawOpen`**，并建议"焼き込み先は必要最小限（多くの場合は `jawOpen` のみ）に絞ってください"。
- **中文社区的真实踩坑**：「mouthClose 和 MouthClosed 真的难绷，之前 debug 半天没找到原因，结果是因为少打了一个 d」（VRCD 文档库）。名字只差一个字母和大小写，是真实痛点。
- **VRChat 官方 Selfie Expression 用同构手法**：官方人员确认用"负值 AA viseme"抵消下巴张开以分离唇形与张口（[Canny 报告](https://vrchat.canny.io/bug-reports/p/selfie-expression-causes-some-avatar-jaws-to-close-too-much)）。⚠️ **这条讲的是 viseme 系统，不是 `mouthClose`**，仅作模式类比。

---

## 10. Warudo 与 VRChat 侧

### 10.1 Warudo：`mouthClose` 在官方 ARKit 命名表里，但官方文档从不主动提它

- [3D Primer / ARKit 章节](https://docs.warudo.app/docs/tutorials/3d-primer#arkit) 的 "ARKit-compatible (Perfect Sync) models" 清单里**明确列出 `mouthClose`**（camelCase）。原文规则：
  > When using facial capture, Warudo will use the official naming conventions to identify the blendshapes on your character model. ... If you are using a model with **ARKit** blendshapes, please ensure that the naming conventions follow the ARKit conventions below. ... **Note that uppercase and lowercase letters must match the conventions.**
- Warudo 按名字 **1:1 匹配模型 blendshape**，所以输入侧有 `mouthClose` 就会直接驱动模型的 `mouthClose`。
- [Face Tracking 文档](https://docs.warudo.app/docs/mocap/face-tracking) 提供 **ARKit / MikuMikuDance / VRM** 三套命名映射，且每个 blendshape 可单独调 threshold / sensitivity（input range 与 output range 双区间映射，"To disable a blendshape, set the top right value to 0"）。
- **⚠️ 但那页 FAQ 里有一条很说明问题的细节**：
  > My model's mouth is slightly open even when my mouth is closed. → This is usually caused by applying ARKit tracking on a non-ARKit-compatible model... increase the threshold ... of the ARKit mouth blendshapes **`jawOpen`, `mouthFunnel`, `mouthPucker`**.
  → **官方在讲"闭嘴闭不紧"时点名的是 `jawOpen`/`mouthFunnel`/`mouthPucker`，`mouthClose` 一次都没提。** 同页全文也搜不到 "chewing" 这个词。

### 10.2 VRChat 本身没有 `mouthClose`

官方内建 animator 参数表只有：`IsLocal`、`PreviewMode`、`Viseme`、`Voice`、`GestureLeft/Right`、`GestureLeftWeight/GestureRightWeight`、`AngularY`、`VelocityX/Y/Z`、`VelocityMagnitude`、`Upright`、`Grounded`、`Seated`、`AFK`、`TrackingType`、`VRMode`、`MuteSelf`、`InStation`、`Earmuffs`、`IsOnFriendsList`、`AvatarVersion`、`IsAnimatorEnabled`，加 scaling 组；FX 层默认别名是 `VRCFaceBlendH` / `VRCFaceBlendV`。

**没有任何 ARKit blendshape，也没有 `mouthClose`。** 所有面捕参数都是 avatar 自定义的 Custom Parameter，由 VRCFT 等第三方工具通过 OSC 驱动。官方文档原文：

> You can set up your avatar for OSC, allowing users and third-party tools to control parameters. For example, **VRCFaceTracking** uses face and eye tracking hardware to control an avatar's facial expression parameters.

- VRChat 的 float 参数是 8-bit，精度 **1/127**（做大幅度咀嚼动作时这个量化够用，做细微抿唇时会有台阶感）。
- 附带发现：VRCFT 导出的参数清单里除了 `v2/MouthClosed` 还有 **`v2/MouthClosedNegative`**（在 [issue #237 的评论](https://github.com/benaclejames/VRCFaceTracking/issues/237#issuecomment-2453545916) 里由 contributor 用 `GetParamNames()` 导出、维护者确认）。强度：中。

---

## 11. 对你的实际方案的意义 + 实测验证清单

### 11.1 "咀嚼"这件事的物理结论

- 咀嚼/咬合的几何 = **`jawOpen` 高 + `mouthClose` 高**（Apple 官方第 3 态；SRanipal 里叫 `Mouth_Ape_Shape`；建模上对应 Haï 说的"JawOpen 播放中 + JawOpen 上拉被抵消"）。
- **不能只拉 `mouthClose`**：单独驱动会把下唇拉到鼻子（有顶点级测量支持）。
- **两个轴是独立的**：不是 `1 - jawOpen`。
- **默认有个 clamp**：`MouthClosed = min(MouthClosed, JawOpen)`（VRCFT 内核，默认开）。做"两者都拉满"的咀嚼能过；做"下颌不动先咬合"会被压掉。要完全自由控制需关掉它。

### 11.2 三条可选路线

| 路线 | 做法 | 代价 |
|---|---|---|
| **A. 换数据源（推荐，最真）** | 换成 iPhone ARKit 路径（iFacialMocap / FaceMotion3D / Unreal LiveLink Face）或 Pico，它们**原生提供** `mouthClose` 并直传到 `v2/MouthClosed` | 需要对应硬件；Pico 真机是否有数据待你实测 |
| **B. 状态机/手势搓（你就想做的那条）** | VRChat：`mouthClose` 是 avatar 上的 blendshape，直接用 Expression Menu / 手势动画驱动它，**完全绕开 VRCFT**。Warudo：模型必须有 ARKit 命名的 `mouthClose` blendshape（大小写精确），然后用触发器/手动参数驱动 | 非实时追踪，只能做"手动咀嚼"演出 |
| **C. 用别的参数合成** | 只能在**你自己写模块或在 VRCFT mutation 里加规则**时做；内核不会替你合成 | 需要写代码 |

### 11.3 ⚠️ 路线 B 的一个陷阱

如果你在 avatar 上装了 Jerry's 模板，那么 `FT/v2/MouthClosed` 那条链**同时在动**，而且它包含 `Limit Jaw Open (MouthClosed)`——**MouthClosed 上升会把下巴拉回闭合**。所以如果你一边手动拉 `mouthClose` 一边让它被追踪的 `jawOpen` 驱动，两者会互相打架。

规避方式（择一）：
- 咀嚼时把面捕状态关掉（模板里有 `FacialExpressionsDisabled` 与 `LipTrackingActive` 参数可用来切状态）；
- 或者不装那条限幅逻辑。

> 另外提醒：模板 README 明确要求**不要把模板的 animator 抠出来嵌入**（"Do NOT embed (copying out the animators) templates without VRCFury or modifying animators in products"），会用坏后续更新。

### 11.4 你去戴 VR / 开 Warudo 时的验证清单

按这个顺序做，能一刀切开"设备没数据"还是"软件没转发"：

1. **Pico 侧先按 VRCFT 文档改配置**：`PICO Connect\setting.json` → `faceTrackingTransferProtocol: 2`、`faceTrackingMode: 1`；头显 LAB 页里 Eye Tracking + Lip Tracking 都开。
2. **看 VRCFT 有没有在发这个地址**：开 VRCFT 的 debug / OSC 监视，查 `v2/MouthClosed`：
   - **地址存在但恒为 0** → 设备层/模块层没数据
   - **地址压根不出现** → 模块没实现
   - **有非零值在动** → 恭喜，Pico 真机确实给数据（这会直接填掉本报告第 7.3 节的空白）
3. **做一个针对性动作**：闭紧嘴唇、同时把下巴往下张（就是"含着东西说话"那个动作）。观察 `v2/MouthClosed` 是否上升。**这是唯一能证明 Pico `MouthClose` (index 5) 不是死通道的实验。**
4. **Warudo 里**：查它的 ARKit 映射列表里 `mouthClose` 这一项有没有输入在动（有但不动 = 上游没给；压根没这一项 = Warudo 没映射——但按官方文档它是在的）。
5. **别忘核对大小写**：模型上是 `mouthClose`（camelCase，与 ARKit 一致）；VRCFT 参数是 `MouthClosed`（多一个 d、PascalCase）。**差一个字母就会静默失效**，这是社区里真实踩过的坑。
6. **想要完全自由控制时**：去 VRCFT 的 Mutation/Corrector 设置里关掉 `MouthClosed/JawOpen Clamp`。

---

## 12. 验证边界与未决问题（请勿当作事实）

| # | 事项 | 状态 |
|---|---|---|
| 1 | **模板控制器只取回 100,377 / 441,805 字节（约 23%）** | ⚠️ 因此"三个 `Jaw_Open_x_Mouth_Closed_x` 组合 clip 是否仍被引用"**未验证**。已取回部分 grep 零命中，但不足以下结论。 |
| 2 | **Pico 真机是否真的在 `BlendShapeIndex.MouthClose`(5) 输出非零值** | ❓ 未验证。代码路径存在 ≠ 数据有内容。未找到 Pico 官方标注哪些 shapes 不被支持；`Pxr.cs` 那张 72 项表**未找到 Pico 官方一手定义**；UDP 包是全量传输还是带有效性掩码未确认。 |
| 3 | **MeowFace 应用本体是否发送 `mouthClose`** | ❓ App 闭源，`suvidriel.itch.io/meowface` / APKPure / Google Play 三次抓取均失败。**但已确证**：即使发送，VRCFT 也会用 `Enum.TryParse` 白名单丢弃 → `v2/MouthClosed` 恒 0。 |
| 4 | **`LIPS_TOWARD` 那句英文定义的二次复核** | ⚠️ 取自 Khronos 官方规范源 asciidoc；另一路调查**未能独立复现**（registry.khronos.org 403 Cloudflare、developers.meta.com 解析非公网 IP、精确短语检索零命中）。**enum 存在性与 index=50 已由 libalxr-sharp 独立核实**，且 VRCFT 官方映射表 `MouthClosed ← LIPS_TOWARD` 可直接读到源文件。 |
| 5 | Vive Streaming 模块 / Virtual Desktop / SteamLink(LinkFT) 是否写 `MouthClosed` | ❓ 闭源，或映射在 18KB 的 `FaceData.cs` 里未逐行核验 |
| 6 | ALVR 的 HTC / Pico 通道细节 | ⚠️ 表格里的结论取自源码片段，未逐行核验整个文件 |
| 7 | Quest Pro 模块 `min(LIPS_TOWARD, JAW_DROP)` 的**保真度** | ❓ 代码逻辑如此，但"抿唇 + 下颌闭合"时会把 `MouthClosed` 压成 0；与 Meta 原始 `LIPS_TOWARD` 绝对值**不等价**，还原度未实测 |
| 8 | "mouthClose + jawOpen 做咀嚼"的真实用例 | ❌ 公开可检索范围内**未发现**（可能是 Discord / 视频教程覆盖不到），**不等于不存在** |
| 9 | Apple Tech Talk 601 transcript 全文 / 官方 sample zip 内部源码 | ❓ 前者只取到截断前段，后者未解压 |
| 10 | `hinzka.hatenablog.com`（PerfectSync 原始形状清单）与 `note.com`（日本社区最集中的面捕文章） | ❌ 本次环境 fetch 全部失败，日本社区原始描述未纳入 |
| 11 | Google Sheets "Face Tracking Shapes Conversion" | ❌ 域名解析为内网 IP，无法抓取 |
| 12 | VRCFT Discord / VRChat 面捕群 | ❌ 不可公开检索，完全未覆盖 |
| 13 | `v2/MouthClosedNegative` 的确切生成规则 | ⚠️ 仅从 issue 评论得知存在，未读代码确认生成条件 |

---

## 13. 核心参考链接

**Apple**
- https://developer.apple.com/documentation/arkit/arfaceanchor/blendshapelocation/mouthclose
- https://developer.apple.com/tutorials/data/documentation/arkit/arfaceanchor/blendshapelocation/mouthclose.json ←（正文原文从这里取）
- https://developer.apple.com/tutorials/data/documentation/arkit/arfaceanchor/blendshapelocation.json ←（52 个形状分组）
- https://developer.apple.com/documentation/arkit/tracking-and-visualizing-faces

**标准**
- https://raw.githubusercontent.com/KhronosGroup/OpenXR-Docs/main/specification/sources/chapters/extensions/fb/fb_face_tracking2.adoc
- https://raw.githubusercontent.com/KhronosGroup/OpenXR-Docs/main/specification/sources/chapters/extensions/fb/fb_face_tracking.adoc
- https://raw.githubusercontent.com/korejan/libalxr-sharp/main/FBExpression.cs ←（`Lips_Toward = 50`，v1/v2 双枚举）
- https://www.cs.cmu.edu/~face/facs.htm ←（FACS AU24/25/26）

**VRCFaceTracking**
- https://raw.githubusercontent.com/benaclejames/VRCFaceTracking/master/VRCFaceTracking.Core/Params/Expressions/UnifiedExpressions.cs
- https://raw.githubusercontent.com/benaclejames/VRCFaceTracking/master/VRCFaceTracking.Core/Params/Expressions/UnifiedExpressionsParameters.cs
- https://github.com/benaclejames/VRCFaceTracking/blob/master/VRCFaceTracking.Core/Params/Data/Mutation/Correctors.cs ←（默认 clamp）
- https://github.com/benaclejames/VRCFaceTracking/pull/221
- https://docs.vrcft.io/docs/tutorial-avatars/tutorial-avatars-extras/parameters
- https://docs.vrcft.io/docs/tutorial-avatars/tutorial-avatars-extras/unified-blendshapes
- https://raw.githubusercontent.com/VRCFaceTracking/docs/master/docs/tutorial-avatars/tutorial-avatars-extras/unified-explanations/jaw/_jaw_mouth_closed.mdx ←（官方专页）
- https://raw.githubusercontent.com/VRCFaceTracking/docs/master/docs/tutorial-avatars/tutorial-avatars-extras/compatibility/meta-movement.mdx
- https://raw.githubusercontent.com/VRCFaceTracking/docs/master/docs/tutorial-avatars/tutorial-avatars-extras/compatibility/vive-sranipal.mdx
- https://docs.vrcft.io/docs/hardware/vr/pico/pico4pe
- https://docs.vrcft.io/docs/hardware/desktop/android/meowface
- https://docs.vrcft.io/docs/hardware/vr/vive/focus3_xre
- https://registry.vrcft.io/modules

**模块源码**
- Pico：https://raw.githubusercontent.com/regzo2/PicoStreamingAssistantFTUDP/master/PicoStreamingAssistantFTUDP/PicoStreamingAssistantFTUDP/PicoConnectors/Pxr.cs
- Pico（映射）：https://raw.githubusercontent.com/regzo2/PicoStreamingAssistantFTUDP/master/PicoStreamingAssistantFTUDP/PicoStreamingAssistantFTUDP/Pico4SAFTExtTrackingModule.cs
- MeowFace：https://raw.githubusercontent.com/regzo2/VRCFaceTracking-MeowFace/master/MeowFaceExtTrackingInterface/MeowDataStructure.cs
- MeowFace（第三方）：https://raw.githubusercontent.com/Jeka8833/MeowFaceVRCFTInterface/master/MeowFaceVRCFTInterface/MeowFace/MeowFaceParam.cs
- Quest Pro：https://raw.githubusercontent.com/regzo2/VRCFaceTracking-QuestProOpenXR/master/VRCFT%20-%20Quest%20OpenXR/QuestOpenXRTrackingModule.cs
- ALVR：https://raw.githubusercontent.com/alvr-org/VRCFT-ALVR/main/ALVRModule/PicoFaceTracking.cs ／ `FbFaceTracking.cs`
- iFacialMocap：https://raw.githubusercontent.com/VRCFaceTracking/VRC_iFacialMocap/main/VRC_iFacialMocap/src/iFacialMocapTrackingInterface.cs
- Unreal LiveLink：https://raw.githubusercontent.com/VRCFaceTracking/LiveLinkTrackingModule/main/VRCFT-LiveLink/LiveLinkExtTrackingInterface.cs

**模板仓库（本次实测）**
- https://github.com/Adjerry91/VRCFaceTracking-Templates
- https://raw.githubusercontent.com/Adjerry91/VRCFaceTracking-Templates/main/Packages/adjerry91.vrcft.templates/Animations/Face%20Blendshapes/ARkit/Jaw_Open_1_Mouth_Closed_1.anim
- https://raw.githubusercontent.com/Adjerry91/VRCFaceTracking-Templates/main/Packages/adjerry91.vrcft.templates/Animations/Face%20Blendshapes/ARkit/Mouth_Closed.anim
- https://raw.githubusercontent.com/Adjerry91/VRCFaceTracking-Templates/main/Packages/adjerry91.vrcft.templates/Animators/ARkit%20Blendshapes/FX%20-%20Face%20Tracking%20-%20ARKit%20Blendshapes.controller
- https://raw.githubusercontent.com/Adjerry91/VRCFaceTracking-Templates/main/Packages/adjerry91.vrcft.templates/Animators/ARkit%20Blendshapes/Parameters%20-%20Face%20Tracking%20-%20ARkit%20Blendshapes.asset
- https://raw.githubusercontent.com/Adjerry91/VRCFaceTracking-Templates/main/Packages/adjerry91.vrcft.templates/CHANGELOG.md

**建模/社区**
- https://docs.hai-vr.dev/docs/products/facetra-shape-creator/shapes/mouth-closed
- https://malaybaku.github.io/VMagicMirror/en/tips/perfect_sync/
- https://vrm.dev/en/univrm1/vrm1_tutorial/expression/
- https://github.com/fernandotonon/QtMeshEditor/pull/1067
- https://github.com/limit7412/VRCARKitBlendShapeGenerator/issues/66
- https://arkit-face-blendshapes.com/ ／ https://github.com/suchipi/arkit-face-blendshapes

**Warudo / VRChat**
- https://docs.warudo.app/docs/mocap/face-tracking
- https://raw.githubusercontent.com/HakuyaLabs/warudo-doc/master/docs/tutorials/3d-primer.md
- https://github.com/vrchat-community/creator-docs/blob/main/Docs/docs/avatars/animator-parameters/index.md
