# 注视约束（眼睛 + 头部看向目标）设计

`HoUnityTools/Constraints/Ho Look At Constraint`。让角色的**眼睛形态键**和**头部骨骼**尽量朝向一个目标，两种目标模式：

- **跟随物体**：看向一个 `Transform`（可带偏移、可指定"看得更远一点"的前瞻）。
- **跟随鼠标**：看向鼠标位置（角度映射 / 射线命中两种取法）。

## 业内成熟方案调查

先看别人怎么做的，再决定我们抄哪一部分。

| 方案 | 做法与参数 | 我们借鉴什么 |
| --- | --- | --- |
| **Unity 内置 LookAt IK**（humanoid）<br>[`Animator.SetLookAtWeight`](https://docs.unity3d.com/510/Documentation/ScriptReference/Animator.SetLookAtWeight.html) | `weight / bodyWeight / headWeight / eyesWeight / clampWeight` 五个参数。`bodyWeight` 就是"带着身子动"那个旋钮；`clampWeight`：0 不受限、1 完全夹死（等于不能看）、0.5 = 只能在可能范围的一半（180°）内转 | **参数命名与语义**照抄这套，有经验的人一眼就懂；`bodyWeight` 目前在 we 这里 = 颈骨参与度，以后扩到脊柱就是同义扩展 |
| **Animation Rigging · Multi-Aim Constraint**<br>[文档](https://docs.unity3d.com/Packages/com.unity.animation.rigging@1.2/manual/constraints/MultiAimConstraint.html) | Aim Axis（被约束物朝哪个局部轴）+ Up Axis + **World Up Type**（None / Scene Up / Object Up / Object Up Rotation / Vector）稳定 roll；多个 Source 按权重求和（不归一化）；**Constrained Axes + Min/Max Limit** 逐轴限位 | ①"up 参考"的做法正是我们的**参考系 up**；②逐轴 Min/Max 限位 ≈ 我们的偏航/俯仰限位；③多目标按权重求和的设计如果我们以后要"看最近的几个兴趣点"可以直接借 |
| **UniVRM · VRMLookAtHead + VRMLookAtBoneApplyer / BlendShapeApplyer**<br>[总览](https://vrm.dev/univrm/lookat/univrm_lookat/) · [Bone 档](https://vrm.dev/univrm/lookat/lookat_bone/) | 拆成"算方向"与"应用"两层：Head 组件算出目标相对头的 **yaw/pitch**，再由 Applyer 应用；Applyer 分 **Bone / BlendShape / TextureUV** 三种。Bone 档的 **DegreeMapping** 用四条曲线：`VerticalDown / VerticalUp / HorizontalOuter / HorizontalInner`，每条给"**Curve X Range Degree**（角度上限）→ **Curve Y Range Degree**（该角度下眼球的旋转量）"。文档明确写：**横向不是左右，而是内外** | ①我们照这个两层结构：解算层（目标 → yaw/pitch）+ 应用层（形态键 / 眼球骨骼）；②**四条方向曲线**取代我原来"四向 ramp 预设"的写法（更贴业内，也更容易标定）；③"横向用内外"是正统表达，左右族只是它的等价镜像表达 |
| **VTuber/游戏通用做法：脊柱分摊** | 头部承担不完的角度按递减比例分给 chest/upper chest/neck/head（`bodyWeight` 的实现方式就是沿脊柱分摊） | 现阶段**只做头 + 颈**（按你的要求），但内部按"骨骼链 + 每骨比例"建模，以后加 chest 只是加数据不改结构 |

结论：**只支持 humanoid，于是第一行（Unity 内置 LookAt IK）从"参考"升级成"实现方式"** —— 头部与身体的参与直接交给 `OnAnimatorIK` + `SetLookAtPosition/SetLookAtWeight`（肌肉空间、自带脊柱分摊、自带 clamp，不用猜骨骼轴向）；眼睛部分仍由我们按 UniVRM 的四向曲线驱动形态键或眼球骨骼。

## 设计约束（本版定稿的方向）

- **只支持 humanoid**（`Animator` 有 Avatar 且 `isHuman`）。骨骼一律用 `Animator.GetBoneTransform(HumanBodyBones.…)` 取：头 = `Head`、颈以上由 Unity 的 `bodyWeight` 分摊、眼球 = `LeftEye`/`RightEye`。**不做名字猜测、不做非 humanoid 回退**。
- **头部与身体的参与交给 Unity 的 LookAt IK**（[`SetLookAtPosition` / `SetLookAtWeight`](https://docs.unity3d.com/510/Documentation/ScriptReference/Animator.SetLookAtWeight.html)）：`weight / bodyWeight / headWeight / eyesWeight / clampWeight` 直接用官方语义，其中 `bodyWeight` 就是"带着身子动"（沿脊柱分摊）。我们的**死区、分工、限位作用在"传进去的目标方向"上**，而不是作用在最终骨骼旋转上 —— 这样既保留逐轴限位，又让 Unity 负责解算。
- **眼睛部分与 humanoid 解耦**：眼睛的形态键路径只依赖网格上的凝视键，任何 rig 都能用；只有"眼球骨骼"与"头部"依赖 humanoid。不是 humanoid 时组件在面板报错，眼睛形态键照常工作。
- **两段更新时机**（见「每帧流程与时序契约」）：头部在 `OnAnimatorIK` 里交给 Unity；眼睛在 `LateUpdate` 写，因为它必须晚于头部 IK 的结果。
- **眼睛支持两种应用方式**：**形态键**（本项目这种）与**眼球骨骼**（更常见）。两者共用同一套"四向角度曲线"，只是输出单位不同（键值 0..100 / 骨骼角度）。
- **三大块 + 一个共用的目标区**：面板与数据按「① 脊椎跟随 / ② 头颈跟随 / ③ 眼睛跟随」组织，每块有自己的启用与权重（正好对应 Unity 的三个权重），共享的部分（目标、参考系、总权重、Animator 检查）放顶部。这样三个效果可以独立开关、独立调权重、独立读数。
- **眨眼不影响注视**：闭眼期间眼睛仍然照着目标算（形态键/骨骼照写），眨眼约束只管眼睑键，两者不互相打断。
- **一键装配预设**：`GetBoneTransform` 拿 head/eye + 在各网格上挑实际存在的凝视键 + 判断左右族/内外族，点一下就能用（你确认过预设保持这个形态即可）。
- **为"以后被状态机/图驱动"预留接口**：公开 `Weight` / 三块权重乘子 / `SetTarget` / `SetTargetPoint` / `SetMode`（见「以后的事」一节）。本版不接状态机、不做烘焙，但设计上保持可烘焙（输出只依赖目标与参数、只写可动画属性、姿势只在固定时机改）。
- **输入系统**：本项目 `activeInputHandler = 1`（**只启用 Input System 包**），旧的 `UnityEngine.Input` 会直接抛异常。鼠标模式走 `Mouse.current`，并用 `#if ENABLE_INPUT_SYSTEM / ENABLE_LEGACY_INPUT_MANAGER` 兼容 Warudo 等运行时，另给一个"输入来源"选项手动指定。

## 适用范围与非目标

适用范围：

- **Unity humanoid 角色**（`Animator` 有 Avatar 且 `isHuman`，动画图层勾了 **IK Pass**）。骨骼一律用 `HumanBodyBones` 取，不需要命名规范。
- 眼睛可以是**形态键**（VRM/ARKit/PICO/VIVE/MMD 等各家命名都支持）或**眼球骨骼**（`LeftEye`/`RightEye`），两者可以同时开。
- 站立、说话、走路的角色：头部走 Unity 的 IK，天然叠加在动画之上，所以和待机/动作动画共存。
- 观众视角类场景（角色看向镜头/鼠标）：鼠标模式。

非目标：

- 不做全身 IK、不做"手/上半身跟着目标转"（`bodyWeight` 只让脊柱跟着转，手臂不会跟随）。
- 不做情绪/表情推断（"看向感兴趣的东西"不在这里）。
- 不做相机控制（不动相机，只动角色）。
- 不做左右眼分别看不同目标。
- 不做非 humanoid rig（没有 Avatar 的模型，头部不生效；眼睛形态键部分仍然可用）。

## 术语

| 术语 | 含义 |
| --- | --- |
| **参考系** | 我们算 yaw/pitch 用的坐标系。默认角色根（`root.forward` = 前，`root.up` = 上），可改成 Animator 物体或世界。只影响我们的解算与限位，不影响 Unity 的 IK |
| **目标点** | 物体模式 = 目标位置 + 偏移；鼠标模式 = 鼠标算出来的世界点或直接的角度 |
| **总角度** | 目标相对参考系前方的偏航（yaw）与俯仰（pitch）误差 |
| **头部承担** | 总角度里交给 Unity IK 的部分（会按 `bodyWeight` 沿脊柱分摊） |
| **眼睛残余** | 总角度减掉头部承担后剩下的部分，交给眼睛 |
| **方向曲线** | 四条（水平内/外、垂直上/下）：横轴 = 归一化角度、纵轴 = 输出比例。**角度上限**与**满值输出**是两个独立的量（UniVRM 的 DegreeMapping 就是这么分的） |

## 数据流

```
物体模式: target.position (+offset)  ┐
鼠标模式: 角度映射 / 射线      ┘─→ 目标点（或直接角度）
                                          │
                     参考系（角色根）分解 yaw / pitch
                                          │
                     死区 → 分工 → 限位夹到合法方向
                        │                             │
        头部方向 → OnAnimatorIK 交给 Unity        眼睛残余 → 四条方向曲线
        SetLookAtPosition / SetLookAtWeight       （水平 内/外、垂直 上/下）
        （Unity 按 bodyWeight 沿脊柱分摊）                │
                        │                   ┌─────────┴─────────┐
                        │            形态键（0..100）      眼球骨骼（世界叠加）
                        └────── 写回（Unity IK / SetBlendShapeWeight / bone rotation）──┘
```

## 组件与文件结构

| 文件 | 职责 |
| --- | --- |
| `Runtime/Constraints/HoLookAtConstraint.cs` | 组件本体：目标解算、分工、写头、写形态键 |
| `Runtime/Constraints/HoLookAtData.cs` | 枚举与可序列化数据：目标模式、鼠标取法、眼睛条目 |
| `Runtime/Constraints/HoLookAtSolver.cs` | 纯数学：目标方向 → yaw/pitch → 分工；角度限位与平滑 |
| `Runtime/Constraints/HoMousePointer.cs` | 鼠标/指针采样：Input System 优先，旧 Input 兜底，两种取法 |
| `Editor/Constraints/HoLookAtPresetActions.cs` | 一键装配：检查 humanoid、按语义挑网格上存在的凝视键、判断左右族/内外族、收集网格、填相机 |
| `Runtime/Constraints/HoShapeKeyTarget.cs` | **从眨眼约束抽出的共享映射结构**（键、范围、增益、ramp 预设…） |
| `Runtime/Constraints/HoShapeKeyWriter.cs` | **抽出的共享写入器**：绑定表、外部基准快照、合并、阈值写 |
| `Editor/Constraints/HoLookAtConstraintEditor.cs` | 面板：看哪里 / 角色 / 三块 / 丢失 / 高级 / 调试（全部带中文 Tooltip） |
| `Editor/Constraints/HoLookAtGizmos.cs` | Scene 视图可视化：参考系、限位框、目标方向、头/眼分工，带角度标签 |
| `Editor/Constraints/HoLookAtPresetActions.cs` | 预设：双眼四向/左右眼四向（左右族、内外族）、"只看眼睛"、"头眼并用" |

## 数据模型

### 目标来源

| 字段 | 类型 | 默认 | 面板位置 | 说明 |
| --- | --- | --- | --- | --- |
| `targetMode` | `Transform / Mouse` | Transform | 看哪里 | 跟随物体 / 跟随鼠标 |
| `target` | `Transform` | — | 看哪里 | 物体模式的目标 |
| `targetOffset` | `Vector3` | 0 | 看哪里 | 物体模式的位置偏移（在目标自身空间） |
| `mouseCamera` | `Camera` | 空 | 看哪里 | 用来做屏幕→世界的换算。**只用手动指定的那台，运行时不再自动猜**（自动猜过一次，猜错的表现是"左右是反的"）；空的时候面板给按钮「填入场景里的相机」并警告一次 |
| `mouseSensitivity` | `Vector2` | (30°, 20°) | 看哪里 | 角度映射模式下鼠标走满屏幕对应多少度 |
| `mouseDeadZone` | `float` | 0.05 | 看哪里 | 鼠标中心死区（屏幕比例），避免鼠标微动带着眼睛抖 |
| `mouseHoldOffscreen` | `bool` | true | 看哪里 | 鼠标移出窗口/无输入时保持最后方向，而不是回中立 |
| `mouseSampleMode` | `AngleMap / Raycast` | AngleMap | 高级 | 鼠标取法（见「鼠标模式」）。**v2 去掉了 `WorldPoint`**：它的"投到水平面"与"固定距离"两种花活，实际用起来和 `Raycast` 的兜底重复 |
| `mouseDistance` | `float` | 3 m | 高级 | 射线模式的兜底距离：射线什么都没打中时取射线上这个距离的点 |
| `mouseRaycastMask` | `LayerMask` | 全 | 高级 | 射线模式命中的层 |

> `inputSource`（Auto / InputSystem / LegacyInput）**已删除**：包已经硬依赖 Input System，采样固定走 Input System，旧 `Input` 只在宿主（Warudo 之类）里作为兜底存在。

### 眼睛输出（两个应用层 + 四条方向曲线）

照 UniVRM 的分层：**解算层**算出目标相对参考系的 yaw/pitch，**应用层**把角度落到眼睛上。应用层两种，可以同时开：

**A. 形态键**（一条通道 = 一个方向，左右眼各一个键）

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `channel` | `Inner / Outer / LookLeft / LookRight / Up / Down` | 六条通道（`HoLookAtEyeChannel`）。横向两族必须分开：`In/Out` 相对眼球、`LookLeft/LookRight` 相对头 |
| `leftEye` / `rightEye` | `HoLookAtEyeKey` | **只有三个字段**：`enabled` / `keyName` / `gain` |
| `enabled` | bool | 这条通道是否参与 |

- **v2 的关键简化**：通道不再复用 `HoShapeKeyTarget`。v1 每条通道左右眼各挂一个 16 字段的完整映射（网格范围、混合模式、偏移、输出范围、钳制、权重、ramp 预设 + 强度、曲线…），四条通道就是 128 个序列化字段，面板上全是看不懂的旋钮，而且实际用到的只有"键名"和"要不要压一点增益"。
- 现在通道只存 `keyName` + `gain`；运行期由 `HoShapeKeyTarget.CreateRuntime(键名, 增益)` 现造一个瞬发（Direct）目标交给共享写入器。眼睛的平滑统一由 `eyeSmoothing` 负责，通道不再各配一套 ramp —— **一套平滑参数比六套 ramp 好懂也好调**。
- 中间的角度→输出映射仍然在四条方向曲线上（见下），所以"标定"能力一点没少。

**B. 眼球骨骼**

| 字段 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `eyeBoneWeight` | `0..1` | 0（关） | humanoid 的 `LeftEye`/`RightEye` 用"世界空间叠加旋转"驱动（不猜轴向）；**0 = 完全不动骨骼** |

- v2 把 `driveEyeBones`（开关）与 `eyeBoneUseCurve`（是否过曲线）合并进 `eyeBoneWeight`：`0` 就是关，非 0 就按四条曲线成形 —— 三个旋钮表达一件事，现在就一个数。
- 骨骼和形态键可以同时用：有些模型既有凝视形态键又有眼球骨骼，两边一起给会更有神；只想要一种就把另一边设 0。

**四条方向曲线（两种应用层共用）** —— 这就是 UniVRM 的 DegreeMapping：

| 曲线 | 横轴（Curve X Range Degree） | 纵轴（Curve Y Range Degree） |
| --- | --- | --- |
| `horizontalInner` | 目标角度上限（默认 30°） | 该角度下的满值输出：形态键 = 0..100，骨骼 = 0..30° |
| `horizontalOuter` | 同上（内外可以给不同上限，很多模型外侧能动得更多） | 同上 |
| `verticalUp` | 默认 20° | 同上 |
| `verticalDown` | 默认 25° | 同上 |

- 曲线的横轴是**归一化后的 |角度| / 上限**，纵轴是 0..1 的输出比例 —— 所以"角度上限"和"满值输出"是分开的两个数，模型差异（有的 100 对应 15°、有的对应 35°）靠这两处标定，而不是靠一个笼统的"强度"。
- **斜向的椭圆夹紧不再是开关**（v1 的 `eyeEllipseClamp`）：永远开着。它的作用是斜着看时不让 h 和 v 同时吃满（"过转"），没有哪种模型是希望它关掉的。
- ⚠️ **内外族的符号**：`In/Out` 是相对眼球的。左眼 `In` = 往鼻侧 = 往右看；左眼 `Out` = 往左看，右眼镜像。所以用内外族时**左右眼各填一组键**（ARKit/PICO 本来也只有左右眼键），预设按眼别把 `HorizontalInner`/`HorizontalOuter` 落到正确的键和符号上。
- **左右族是等价表达**：VRM 的 `LookLeft/LookRight`、Meta 的 `EYES_LOOK_LEFT_L` 这类"相对头"的命名，填进 `LookLeft/LookRight`（= v1 的 `HorizontalOuter`/`HorizontalInner`）即可，语义完全对得上。面板上有「左右族 / 内外族」两个按钮，会读网格上的键自动填。

### 三大块（UI 与数据都按这个分）

这个约束本质上是**三个可以独立开关、独立调权重的效果叠在一起**，所以面板与数据模型都按三块组织 —— 正好一一对应 Unity 的三个权重：

| 块 | 对应 | 权重 | 这一块自己的细节 |
| --- | --- | --- | --- |
| **① 脊椎跟随** | Unity `bodyWeight`（沿脊柱分摊） | 启用 + 身体强度 | 只在超过某角度才参与（`spineMinAngle`，高级） |
| **② 头颈跟随** | Unity `headWeight` | 启用 + 头部强度 | 起始死区、头部承担、左右/上下限位 |
| **③ 眼睛跟随** | 我们的四条方向曲线 | 启用 + 眼球强度 | 四条曲线与角度上限、六条通道的键名与增益、眼球骨骼权重 |

这样"多个效果混合"时一眼能看出谁在出力：调试区按三块分别给读数（总角度、每块的承担量、是否吃满），谁关掉、谁调权重互不影响。共享的部分（目标来源、参考系、总强度、Animator 检查）放在顶部的「看哪里 / 角色」两区。

### ① 脊椎跟随 / ② 头颈跟随

参数直接用 Unity 内置 LookAt IK 的语义，加上我们自己算"目标方向"时用的死区、分工与限位：

| 字段 | 类型 | 默认 | 面板位置 | 说明 |
| --- | --- | --- | --- | --- |
| `spineEnabled` | bool | true | ① | 是否让身体参与 |
| `bodyWeight` | `0..1` | 0.3 | ① | 面板叫「身体强度」。**= Unity 的 `bodyWeight`**：身体参与度（Unity 沿脊柱分摊）—— 这就是"带着身子动" |
| `spineMinAngle` | `float` | 0° | 高级 | 只在总角度超过它之后才开始分摊（0 = 一直参与） |
| `headEnabled` | bool | true | ② | 是否转头/颈 |
| `headWeight` | `0..1` | 1 | ② | 面板叫「头部强度」。**= Unity 的 `headWeight`**：头部参与度（0 = 只做眼睛） |
| `headLimitYaw` | `float` | 70° | ② | 面板叫「左右限位」：头部最大偏航（作用在传进去的目标方向上） |
| `headLimitPitch` | `float` | 40° | ② | 面板叫「上下限位」：抬头/低头各这么多，**上下对称** |
| `headShare` | `0..1` | 0.7 | ② | 面板叫「头部承担」：超过死区之后头部承担的比例（剩下的给眼睛） |
| `deadZone` | `float` | 8° | ② | 面板叫「起始死区」：这么小的角度只用眼睛 |
| `aimSmoothing` | `float` | 0.06 s | 高级 | 目标方向的一阶平滑（不是骨骼旋转平滑） |
| `aimMaxSpeed` | `float` | 360°/s | 高级 | 目标方向的角速度上限（防瞬移） |
| `clampWeight` | — | — | — | **已删除**：Unity 的 `clampWeight` 现在固定传 0。理由：限位已经由我们自己的 `headLimitYaw/Pitch` 按角度做掉了，再让 Unity 二次夹取会让"面板读出的头部角度"和实际转出来的对不上；调试 Gizmo 画的就是真实角度，所以这里必须只留一个夹取者 |

- **不再需要"颈骨"字段**：颈部参与由 Unity 的 `bodyWeight` 沿脊柱分摊，比我们自己分两段更自然。
- **`pitchLimitUp/Down` 合并成 `headLimitPitch`**：上下不对称在真实头部是有意义的（抬头比低头难），但用起来要同时理解两个数；现在一个数 + Gizmo 里一眼能看到框，比两个数字好懂。真要不对称时改成 head 骨骼叠加是后续（本版不做）。
- **后续（本版不做）**：多目标（Multi-Aim 式的权重求和）。

### ③ 眼睛参数

| 字段 | 类型 | 默认 | 面板位置 | 说明 |
| --- | --- | --- | --- | --- |
| `eyesEnabled` | bool | true | ③ | 是否做眼动 |
| `renderers` | `List<Renderer>` | 空 | ③ | 哪些网格上有凝视形态键（「收集子级网格」按钮） |
| `eyeWeight` | `0..1` | 1 | ③ | 面板叫「眼球强度」 |
| `eyeSmoothing` | `float` | 0.04 s | ③ | 眼睛角度的一阶平滑（比头部快）。**通道级的 ramp 已经删掉**，平滑只在这里 |
| `eyeAngleLimit` | `Vector4` | (30°, 30°, 20°, 25°) | ③ | 四条曲线横轴对应的角度上限（内/外/上/下） |
| `horizontalInner/Outer`、`verticalUp/Down` | `AnimationCurve` × 4 | 线性 | ③ | 四条方向曲线（见上） |
| `eyeEntries` | `List<HoLookAtEyeEntry>` | 空 | ③ | 六条通道（键名 + 增益） |
| `eyeBoneWeight` | `0..1` | 0 | 高级 | 眼球骨骼权重，0 = 不动骨骼 |
| `mergeMode` | `Saturate / SoftClip / Normalize` | Saturate | ③ | 合并方式：写形态键时求和超过 100 怎么处理，见下节 |
| `eyesWeight`（给 Unity 的） | — | — | — | **已删除**：眼球一律由我们的四条曲线驱动。留一个"绕过曲线交给 Unity"的旋钮只会让人怀疑自己该用哪个 |

### 眼睛键写满 100 的两条路

注视的眼睛通道**每条通道写不同的键**，所以通道之间不会互相叠加；能写满 100 的只有两种情况：

| 情况 | 表现 | 对策 |
| --- | --- | --- |
| **角度吃满上限**（正常） | 条形拉满、括号里的键值 100，且 `总角度 ≥ 该通道的角度上限` | 这是"眼睛已经转到头"。嫌太容易满就把四个角度上限调大（贴合模型真实可动范围），或把「眼球强度」压到 0.8~0.9 |
| **眨眼/高光/表情也写同一个键**（打架） | 键值 100 但总角度还没到上限；调试区饱和清单里标「被削」 | 换「合并方式」为软饱和或按比例分配，让两路都有份 |

`mergeMode` 的作用与眨眼那边完全一样（共用同一个写入器，见 `docs/BLINK_CONSTRAINT.md` 的「形态键为什么老是写死 100」）：只动我们这一路，`sum = 0` 时键值恒等于外部基准，不会改写动画/面捕写下的值。

### 关掉眼睛块 / 关掉组件时的清场

眼动键同样不能停在最后一次的值上（不然眼睛会僵在一个方向）：

| 时机 | 行为 |
| --- | --- |
| 关掉「③ 眼睛跟随」 | 眼睛残余按 0 算，通道输出 0，键回到基准（重建时写入器会继承"我们写之前的基准"，所以不会把上一次的输出冻成基准） |
| 组件被禁用 / `ResetState()` | `writer.RestoreWritten()` 把写过的眼动键**硬写回基准** |
| 换网格、改键名、删通道（重建后那个键不再有人写） | 重建收尾时硬写回基准（不看写入阈值） |

实现细节与眨眼共用：`HoShapeKeyWriter` 跨重建记住"上一轮写过哪些键、基准是多少"，重建后仍在写的继承基准、不再写的硬写清场。




## 每帧流程与时序契约（重点）

因为头部交给 Unity 的 LookAt IK，**时机被切成两段**：

```
输入 / Update
    鼠标采样、目标物体被别的脚本移动（用户脚本、Timeline、物理）
动画更新
    Animator 求值（含 Animation Rigging 的 rig）写骨骼姿势
    ★ 我们（OnAnimatorIK，第一段）：
        读 head 世界姿势 → 算总角度 → 死区/限位夹到合法方向 → 分工
        → Animator.SetLookAtPosition(头部承担方向上的点) + SetLookAtWeight(...)
        → 缓存本帧的"眼睛残余角"
    Animator 应用 IK（肌肉空间，按 bodyWeight 沿脊柱分摊、按 headWeight 转头、按 clampWeight 夹取）
PreLateUpdate · ScriptRunBehaviourLateUpdate
    ★ 我们（LateUpdate，第二段）：用缓存的残余角写眼睛
        —— 形态键（SetBlendShapeWeight）与/或 眼球骨骼（世界空间叠加旋转）
PreLateUpdate · afterLateUpdate（MC2 的 PlayerLoop 槽）
    布料/头发读骨头（能看到已经转好的头与眼球）、渲染准备
PostLateUpdate
    渲染
```

**为什么必须两段**：眼球骨骼的世界朝向依赖头部的最终姿势。如果我们在 `OnAnimatorIK` 里写眼球，Unity 随后应用头部 IK 会把眼球一起转走，残余角就对不上了。所以眼球放到 `LateUpdate`（晚于头部 IK 结果）；形态键顺带在同一处写，时序一致、只写一次。

### 前置条件（面板会检查并报错）

| 条件 | 说明 |
| --- | --- |
| `Animator` 有 Avatar 且 `isHuman` | 否则**头部不生效**（面板报错），眼睛形态键部分照常工作 |
| 动画图层的 **IK Pass** 勾上 | 不勾的话 `OnAnimatorIK` 根本不会被调用 —— 这是"头一动不动"最常见的原因。位置：**Animator 窗口 → Layers → 图层行右侧齿轮 ⚙ → IK Pass**（Unity 6 里选中图层后 Inspector 也会显示这个勾）。有多个图层时，每个想参与 IK 的图层都要勾 |
| 组件与 Animator 的**物体关系** | ⚠️ Unity 只把 `OnAnimatorIK` 发给**Animator 所在的那个 GameObject**（和 `OnAnimatorMove` 一样）。约束挂在子物体（约束层 / 骨骼）上是常态，所以组件会在播放时自动往 Animator 物体上挂一个 `HoLookAtIkRelay` 转发器；编辑模式不会加组件（避免标脏场景），那时要么把组件放在 Animator 物体上，要么手动挂一个转发器 |
| `Animator.Culling Mode` | 若是 `Cull Update Transforms` / `Cull Completely`，角色离屏时动画与 IK 都不更新 —— 离屏测试时头不动是正常的 |
| `Animator` 与组件在同一物体上，或显式指定 Animator | 空时按 自己 → 父级 → 子级 找 |

### 选项与代价

| 方案 | 结果 |
| --- | --- |
| **`OnAnimatorIK` + 内置 IK（本版采用）** | 在动画管线内部完成，肌肉空间正确、自带脊柱分摊与 clamp、不会被同帧动画覆盖；需要 Avatar + IK Pass |
| 自己写 head 的 `LateUpdate`（世界叠加） | 不依赖 humanoid / IK Pass，但要自己实现分摊，且会和 Animation Rigging 抢同一根骨头（本版不做） |
| `Update` / `FixedUpdate` 写骨骼 | 会被动画覆盖 / 与渲染帧不同步，都不考虑 |

### 关键不变量

1. **我们只改"目标方向"，不直接改头部骨骼旋转**：Unity 的 IK 天然是"在动画之上叠加"，所以不存在基准姿势缓存、也不会累积漂移；组件一关，头自然回到动画。
2. **限位作用在方向、不作用在旋转**：先按偏航/俯仰限位把目标方向夹到合法锥体内，再把点交给 `SetLookAtPosition` —— 这样既保留了逐轴限位，又不和 Unity 的 `clampWeight` 打架（两者可以同时用，`clampWeight` 作为最后一道保险）。
3. **平滑作用在方向上**：`aimSmoothing` / `aimMaxSpeed` 平滑的是我们算出的目标方向（一阶 + 角速度上限），不是骨骼旋转。
4. **眼球晚于头部**：见上面的两段时机。
5. **形态键"叠加 + 外部基准检测"**：和眨眼约束同一套（读回值若等于我们上次写的值，就沿用上一次确认的外部基准）。这样动画/面捕写的凝视键不会被我们每帧累加。
6. **`deadZone` 与分工先于限位**：先判死区（小角度只给眼睛），再做分工（`headShare`），最后把头部那部分夹到限位内；剩下的全部给眼睛，再由眼睛自己的上限与曲线决定实际输出。

### 和别的系统共存

| 共存对象 | 规则 |
| --- | --- |
| **Animation Rigging** 已经在驱动 head/neck | **不要两边都写**：rig 在动画图里先求值、我们的 IK 后应用，我们会盖掉 rig 的头部。二选一 —— 把我们的 `headWeight` 设 0（只做眼睛），或者把 rig 里 head/neck 约束的权重设 0 |
| **Animator IK**（`OnAnimatorIK`） | 我们就在这条管线里：`SetLookAtPosition` / `SetLookAtWeight` 由 Unity 应用。面板会检查**图层是否勾了 IK Pass**（不勾则 `OnAnimatorIK` 根本不被调用）与 **Avatar 是否 humanoid**，不满足时直接报错 |
| **MC2 布料**（头发、耳朵、尾巴） | MC2 在 LateUpdate 之后读骨头，能看到已经转好的头与眼球。头部转得快时如果布料抖，先查 MC2 的 `移动速度制限/回転速度制限`（1 m/s、360°/s 的配置在上一轮已经踩过一次） |
| 别的 LateUpdate 脚本也在写 head | 头部由 Unity 的 IK 在本组件回调里应用，LateUpdate 里再写 head 的脚本会覆盖 IK 结果 —— 要么别写，要么显式设执行顺序并接受覆盖 |
| 眨眼约束 | 写的是不同键（眼睑 vs 凝视），互不干扰；**眨眼期间注视继续**（闭眼时凝视键照写，睁眼那一刻眼睛已经在正确方向上，看起来更自然） |
| 编辑模式 | 默认不写（避免把场景标脏）：用 Gizmo + 虚拟目标做预览 |

### 目标丢失/切换

- 物体模式目标为 null、或被禁用：按 `lostBehavior`（`Hold 最后角度 / Return 回中立 / 停用`），默认 `Return`，带 `returnDelay`（默认 0.4 s）和 `returnSpeed`。
- 鼠标模式移出窗口：`mouseOffscreenBehavior`（默认 `Hold`）。
- 目标瞬移（Teleport/切场景）：`maxSpeed` 限制 + 平滑，避免头"啪"一下扭过去；可选 `teleportAngleThreshold`（超过就瞬移跟过去，不慢慢转，例如 120°）。

## 鼠标模式

| 取法 | 做法 | 什么时候用 |
| --- | --- | --- |
| `AngleMap`（默认） | 鼠标屏幕位置 → 归一化 (−1..1) → 乘 `mouseSensitivity` 得到 yaw/pitch 偏移（中心死区可调） | 观众视角、VRChat 式"看向鼠标"，最可控，不依赖相机距离 |
| `Raycast` | 相机 → 鼠标射线打到 `mouseRaycastMask` 的物体；**没打中时取射线上 `mouseDistance` 处的点** | 有场景几何、想让角色盯着墙上的东西 |

> v1 的 `WorldPoint`（固定距离 / 投到水平面）已删除：固定距离那半就是 `Raycast` 的兜底，投平面那半实际没人用。

⚠️ **`AngleMap` 的坐标系**（`mouseSpace`）：

- **屏幕相对（默认，观众视角）**：鼠标在屏幕中心 → 角色**看向观察者**（大致等于看向镜头）；鼠标在右上 → 角色看向**观察者的右上方**。实现在观察者坐标系里构造方向 `(tan(yaw), tan(pitch), −1)`（−Z 朝向观察者、+X 观察者右、+Y 上）再交给正常分解流程。
  - ⚠️ 基准方向**不能用相机的 `forward`**：那是射进屏幕里的方向，而角色是面对相机的，用它等于让角色"看向自己背后" —— 平转角会一路顶到 ±150° 以上，头部被限位卡住、眼睛吃满。这是第一版踩过的坑。
- **角色相对**：鼠标右 = 看向**角色**右侧，相当于把鼠标当成角色自己的注视摇杆；正面机位下看起来是"反的"。

`Raycast` 是物理目标点，不存在这个歧义。

**参考系（`reference`）**：空时默认取 **Animator 所在物体**（角色根）的朝向 —— 那才是角色的面向。退回到组件自己的 transform 往往是骨骼/空物体，轴向随机，会让"总角度"读数与限位全部失准（看起来像"平转 150° 以上"）。

- **输入读取**：`#if ENABLE_INPUT_SYSTEM` 用 `Mouse.current.position.ReadValue()` / `Pointer.current`；`#if ENABLE_LEGACY_INPUT_MANAGER` 用 `Input.mousePosition`。本项目只开了 Input System，所以旧分支只是兼容 Warudo 之类的宿主。v2 删掉了面板上的「输入来源」选择（包已硬依赖 Input System，采样固定走 Auto：有 Input System 就用它，否则退回旧 Input）。
- **相机必须手动指定**（`mouseCamera`）：运行时**不自动猜相机**（`Camera.main` 依赖 MainCamera 标签，很多测试场景没有；按"像素面积最大"猜也会挑错）。空的时候只警告一次，角度映射退回角色相对坐标系。面板上没填时会显示一个「填入场景里的相机」按钮（编辑器里挑第一个渲染到屏幕的启用相机填进去，仍然是你显式点的那一下）；「一键装配」也会顺手填。
- 鼠标屏幕坐标换算用 `mouseCamera`，支持 `Screen.width/height` 与相机的 `pixelRect`（多相机/画中画时不至于错位）。

## 参数总表（控制参数预算）

v2 的账（面板上"一眼能看到"的 vs 折进「高级」的）：

| 分组 | 面板可见 | 高级里 | 说明 |
| --- | --- | --- | --- |
| 看哪里 | 4~6 | 3 | 模式、目标+偏移 / 相机+灵敏度+死区+离屏保持；高级：取法、距离、射线层 |
| 角色 | 1（总强度） | 3 | 只留「总强度」+ 两行自动检测的信息（Animator / 参考系）；高级：Animator 覆盖、参考系覆盖、编辑模式求值 |
| ① 脊椎 | 2 | 1 | 启用、身体强度；高级：脊椎起始角 |
| ② 头颈 | 6 | 2 | 启用、头部强度、起始死区、头部承担、左右限位、上下限位；高级：方向平滑、最大角速度 |
| ③ 眼睛 | 5 + 4 曲线 + 6 通道 | 1 | 启用、网格、眼球强度、角度上限、四条曲线；高级：眼球骨骼权重 |
| 丢失与瞬移 | 1 | 3 | 丢失行为；高级：回正延迟/速度、瞬移阈值 |
| 调试 | 1 | 1 | 场景 Gizmo 开关；高级：写入阈值 |
| **眼睛通道（每只眼）** | **2** | — | `keyName` + `gain`（v1 是 16 项：网格范围/混合模式/偏移/输出范围/钳制/权重/ramp 预设+强度/曲线…） |

合计 **约 30 个序列化字段 + 6 条通道 × 2 只眼 × 2 项**（v1 是 40+ 字段 + 通道 192 项）。日常只动四处：**一键装配**、`bodyWeight`/`headWeight`、四条曲线的角度上限、（鼠标模式的）灵敏度。

### v2 砍掉的字段与理由

| 砍掉 | 理由 |
| --- | --- |
| `clampWeight` | 限位由我们自己的 `headLimitYaw/Pitch` 按角度做；Unity 再夹一次会让面板读数与实际不一致。固定传 0，**只留一个夹取者** |
| `pitchLimitUp` / `pitchLimitDown` | 合并成 `headLimitPitch`（对称）。上下不对称的收益远小于"两个数要一起理解"的成本 |
| `eyesWeightToUnity` | 眼球一律走四条曲线。留一个"绕过曲线交给 Unity"的旋钮只会让人怀疑该用哪个 |
| `eyeEllipseClamp` | 椭圆夹紧永远开：没有哪种模型希望斜看时 h/v 同时吃满 |
| `driveEyeBones` / `eyeBoneUseCurve` | 合并进 `eyeBoneWeight`（0 = 不动骨骼，非 0 = 过曲线）。三个旋钮表达一件事 |
| `mouseSampleMode = WorldPoint`、`projectToPlane`、`planeHeight` | 与 `Raycast` 的兜底距离重复；`Raycast` 没打中时就用固定距离的点，一个兜底数就够 |
| `inputSource` | 包已硬依赖 Input System；旧 `Input` 只在宿主里兜底，不需要用户选 |
| 通道里的 `HoShapeKeyTarget`（16 项 × 12） | 实际只用得到键名和增益。运行期用 `CreateRuntime(键名, 增益)` 现造瞬发目标，平滑交给 `eyeSmoothing` 一套管 |

## 面板结构

```
Ho 注视约束
[ 一键装配 ] [ 清空通道 ]
⚠ 播放时的检查（humanoid / IK Pass / 转发器）显示在这里
▸ 看哪里（目标）                    跟随物体 / 鼠标·角度
    目标来源 [跟随物体 ▾]
    目标物体 [HeadTarget ▾]   偏移 [0,0,0]
    (鼠标) 相机 [Main Camera ▾]  灵敏度 [30,20]  鼠标死区 0.05  离屏保持 [✓]
▾ 角色（接口）                      强度 1
    总强度 1.0
    Animator：HIRO（humanoid ✓）         ← 只读
    参考系：HIRO（自动）                  ← 只读
    [ 只看眼睛 ] [ 头眼并用 ]
▸ ① 脊椎跟随                        body 0.3
    [✓] 启用   身体强度 0.3
▾ ② 头颈跟随                        head 1
    [✓] 启用   头部强度 1.0   起始死区 8°   头部承担 0.7
    左右限位 70°   上下限位 40°
▾ ③ 眼睛跟随                        开
    [✓] 启用   目标网格 [▾]  [收集子级网格] [重新解析键]
    眼球强度 1.0   四个角度上限 内/外/上/下 (30,30,20,25)
    四条方向曲线：水平内 / 水平外 / 看上 / 看下
    通道 → 键名（左右眼各一个）         [ 左右族 ] [ 内外族 ]
      ▾ 水平内 In    [启用] [✕]
          左眼 [eyeLookInLeft  ▾] 增益 1.0
          右眼 [eyeLookInRight ▾] 增益 1.0
      …（六条）
▸ 丢失与瞬移                        回中立
▸ 高级                              少动
    跟随手感 / 丢失之后 / 眼球骨骼 / 鼠标细节 / 组件 / 写入
▾ 调试                              有目标
    场景 Gizmo [✓]
    左右（yaw，正 = 角色右侧）  ▮▮▮▮│▮▮▮  总 32.4° 限 70°
    上下（pitch，正 = 抬头）    ▮▮│▮▮▮▮  总 12.0° 限 40°
    六个通道条形（写入比例 + 左右眼实际键值）
```

- **每个字段都带中文 Tooltip**：鼠标悬停即可看到"它是干什么的、大概填多少"。面板上的名字用大白话（身体强度 / 头部承担 / 起始死区），文档里保留 Unity 的原始字段名，两边能对上。
- **只显示当前模式用得上的**：跟随物体时看不到鼠标那几项；角度映射时看不到射线层与距离。
- **一键装配**：自动找 Animator、把参考系清空（= 自动用角色根）、收集网格、按模型上实际存在的凝视键判断左右族还是内外族、填相机；找不到的项在面板里列出来让用户手填。
- **族别按钮**（`左右族` / `内外族`）放在通道表头，按下去就按网格上的键重填六条通道。
- **键名格**复用眨眼约束的 `HoKeyNameDropdown`（内置表 + 网格上实际存在的键 + 缺失标黄 + 规范名提示）。

## 可视化调试

"纯数值不直观"是上一版最直接的抱怨，所以 v2 把调试做成**两处可视化**，共用同一份数据（`HoLookAtConstraint.GetDebug()` 返回 `HoLookAtDebug`，面板条形与 Gizmo 都读它）：

**面板条形读数**（`调试` 区，播放或编辑模式求值时实时刷新）

- 左右（yaw）/ 上下（pitch）各一条**以正前方为中心的刻度条**，左端右端就是限位边界：
  黄标 = 总角度（目标在哪），青条 = 头部承担到哪，紫标 = 头 + 眼睛 = 实际目光落在哪。
  三者分开画，一眼能看出"是头没转够、还是眼睛没吃饱、还是目标已经超出限位"。
- 六条通道各一条**比例条**：条形 = 这一帧真正写出去的比例（和写形态键用的是同一套"角度 → 归一化 → 过曲线"算法），括号里是左右眼实际写出的形态键值。

**Scene 视图 Gizmo**（选中组件即可见，可在面板上关掉；画在编辑器程序集里所以能带文字标签）

| 画的东西 | 颜色 | 看什么 |
| --- | --- | --- |
| 参考系前方 / 上方 | 绿 / 蓝 | 所有角度都相对它；参考系填错（比如填了骨骼）在这里一眼可见 |
| 头部限位框 | 白线框 | 左右 ±`headLimitYaw`、上下 ±`headLimitPitch` 投到 1 米处的椭圆框；目标出框就是"头转不过去、全靠眼睛" |
| 总角度方向 + 目标点 | 黄（**出框变红**） | 目标在哪；变红即超出限位，同时画一条虚线连回限位方向 |
| 头部承担方向 | 青 | 头部实际转到哪，带 "头 xx° / xx°" 标签 |
| 实际目光方向 | 紫 | 头 + 眼睛残余，带 "眼残余 xx° / xx°" 标签 |

- 无目标 / 丢失时目标点画成灰色圆球，方向线仍按当前解算结果画，方便确认回正行为。
- 目标点**超出限位**时用红色：飞线、贴身近战这些"角色其实看不过来"的场景，不需要读数字就能看出来。


## 调试工作流

| 现象 | 先看什么 | 常见原因 |
| --- | --- | --- |
| 头完全不动 | 面板顶部的 Animator 检查三项 | 不是 humanoid / 图层没勾 **IK Pass** / Avatar 为空 —— 这三条是"头一动不动"的头号原因 |
| 头动了但眼睛不动 | 通道条形 + 缺失键 | 键名不对（网格上没有，键名格会标黄）；或角度没超过曲线横轴上限（输出被曲线压小了） |
| 眼睛动得太猛 / 幅度不对 | 曲线横轴上限与纵轴满值 | 角度上限填小了（比如 100 键值实际只对应 15°，却按 30° 标定）；这正是 UniVRM 把两个量分开的原因 |
| 眼睛方向反了 | 通道条形里哪条在涨 | 用了 `In/Out` 键却按左右族配（或反之）；看「水平内 / 水平外」哪条在亮就知道族别有没有选错 |
| 左右和鼠标反了 | Gizmo 的黄色方向线 | 相机填错（不是观众视角那台）；或相机没填（退回角色相对坐标系） |
| 身体跟着转得太多 | `bodyWeight` | 它沿脊柱分摊，0.3~0.5 比较自然；1 会整个人转过来 |
| 头和 Animation Rigging 抢 | rig 里是否也有 head/neck 约束 | 两边都写 → 二选一（见共存表） |
| 目标明明在身边却总在看别处 | Gizmo 里参考系的前方箭头 | 参考系填成了骨骼（轴向随机）—— 清空它，让组件自动用角色根 |
| 眼球骨骼转起来像"翻白眼" | 眼球骨骼的 pivot 是否在眼球中心 | 我们走世界叠加旋转，轴向不影响；但骨骼 pivot 偏了就会绕错点转 |
| 头在抖 / 一顿一顿 | 目标是否被动画或物理推着走、`aimSmoothing` | 目标抖动（加平滑或换角速度上限）；或 MC2 布料在和头部抢骨头 |
| 眼动键总是 100，眼睛像"定住"了 | 调试区条形 + 饱和清单 | 总角度已经超过该通道的角度上限（正常吃满）→ 调大角度上限或压眼球强度；若角度还没到上限就已经 100，说明有眨眼/高光/表情在写同一个键 → 换「合并方式」 |
| 头和动画打架 | 动画是否也驱动 head（或 Animation Rigging） | 两边都写 → 按「和别的系统共存」二选一 |
| 眨眼约束失效了 | 眨眼输出键 | 注视约束不该写眼睑键；检查预设有没有误填 |
| 切换动作时头"弹"一下 | 是否缓存了基准姿势 | 违反"每帧读动画后姿势"的不变量 |

## 与眨眼约束的复用（先重构再新增）

现在的眨眼约束里已经有一整套"形态键绑定 + 基准快照 + 合并 + 阈值写"的机制。注视约束要用同一套，所以第一步先抽公共件（**只移字段、不改字段名**，`[Serializable]` 类的类型名不进 YAML，所以改名不丢数据）：

1. `HoShapeKeyTarget`：从 `HoBlinkTarget` 原样搬字段（`meshScope/meshIndex/keyName/side/blendMode/weight/gain/offset/outputMin/outputMax/clampToRange/rampPreset/rampIntensity/rampCurve/rampAttack/rampRelease`）。`side` 对注视约束无意义，但留着不碍事。
2. `HoShapeKeyWriter`：绑定表、外部基准快照、目标求值与合并、阈值写。眨眼约束改用它（行为不变），注视约束直接复用。
3. `HoBlinkTarget` 保留为 `HoShapeKeyTarget` 的别名（`using HoBlinkTarget = HoShapeKeyTarget;`）以免编辑器代码大改。

顺带把上一轮踩到的两个坑固化进写入器：**外部基准检测**（不要把自身写入当基准）和**写回值夹到 0..100**。

## 以后的事：被状态机/图驱动（本版就做接口）

我们的效果作为**运行时**节点，由 Animator 状态、Warudo Blueprint/节点图或 Timeline 曲线控制它的**权重与目标**。接口本身几乎零代码成本，**本版就留**；不预留的话以后要动序列化结构：

- `Weight`（外部乘子，与面板上的 `weight` 相乘）以及三块的 `SpineWeight / HeadWeight / EyeWeight` 外部乘子
- `SetTarget(Transform)` / `SetTargetPoint(Vector3)` / `SetMode(...)` / `SetEnabled(bool)`
- 跨帧状态只有平滑器（与果冻弹簧），没有别的历史 —— 外部随时切目标都不需要复位

Warudo 侧的对应物是 **Blueprint / 节点图**（Ports & Triggers）与脚本 API，天然适合"某个事件 → 让角色看向某物"。

**烘焙成剪辑：不做，也不再列入规划。** 结论是这条路径对本项目几乎不需要考虑（交互式的鼠标模式本来就烘不了，物体模式也没有"省运行时"的迫切需求）。下面三条"可烘焙性"约束仍然成立，但它们本来就是这个设计自然满足的性质，不额外花成本：

1. 每帧输出只依赖"目标点 + 参数"，不依赖历史（平滑器/弹簧除外）。
2. 只写标准可动画属性：骨骼旋转（由 Unity IK 写）与 `SkinnedMeshRenderer` 形态键；不碰材质、不碰非骨骼 Transform。
3. 姿势只在固定时机改：头部在 `OnAnimatorIK`、眼睛在 `LateUpdate`。

## 性能与资源约束

- 每帧只有一次目标解算、一次分工、最多 4 次形态键写（阈值写）、1~2 次骨骼旋转写；无分配、无字典查找（绑定在构建期解析）。
- 鼠标采样每帧一次，`Camera.ScreenPointToRay` 只在需要世界点时调用。
- Gizmo 只在选中时绘制，读数字符串只在面板可见时生成。

## 里程碑与验收

1. **抽公共件**（`HoShapeKeyTarget` + `HoShapeKeyWriter`）：眨眼约束改用它，行为与数值不变（用同一套调试读数回归）。
2. **三块骨架 + 面板 + 外部接口**：①/②/③ 三块的启用与权重、目标区、Animator 三项检查，以及 `Weight` / `SetTarget` / `SetTargetPoint` / `SetMode` / `SetEnabled` 这组公开接口（本版就留）。此时可以先只让眼睛块工作。
3. **② 头颈 + ① 脊椎 · humanoid IK 路线**：`OnAnimatorIK` 里算方向、限位、分工并调用 `SetLookAtPosition/SetLookAtWeight`。验收：`bodyWeight` 从 0 调到 0.6 时上半身参与可见；目标移到身后时头部按限位停住而眼睛吃满；关掉组件姿势立刻回动画。
4. **③ 眼睛 · 形态键**：四条方向曲线 → 内/外/上/下四向键输出；左右族与内外族两套预设。验收：左右移动时"内/外"互斥切换、垂直不受影响；把某条曲线横轴上限 30°→15°，输出幅值随之翻倍。
5. **③ 眼睛 · 眼球骨骼 + 一键装配**：`GetBoneTransform(LeftEye/RightEye)` + 世界空间叠加 + 可选过曲线。验收：眼球骨骼自身轴向任意旋转 90° 后看向方向不变；新模型点一次"一键装配"即可用。
6. **鼠标模式 + 输入系统兼容 + 外部接口**（`Weight`/`SetTarget`/`SetMode`）。验收：Input System 项目里不报错；旧 Input 分支在 `#if` 下能编译；外部脚本能在运行时切目标而不跳变。
7. **调试视图 + 文档回填**（v2 完成）：面板条形 + Scene Gizmo 两处可视化，全部字段带中文 Tooltip；把实测手感（死区/承担比例/曲线形状/`bodyWeight`）写回本文档。

## 待确认

1. **v2 简化后的主观感受**：面板是不是"看着就懂"了？还有哪些字段你看了不知道干嘛 / 从来没动过 —— 下一轮继续砍。
2. **头部默认参数**：`bodyWeight` 0.3、`headWeight` 1、死区 8°、头部承担 0.7、左右限位 70°、上下限位 40° —— 这组是"看起来自然"的起点，要不要更保守（比如死区 10°、承担 0.6）？
3. **四条曲线的角度上限默认值**：内/外/上/下 = 30°/30°/20°/25°。要不要在调试区加一个**标定**按钮（拖动虚拟目标把某个方向推到形态键满值，记录当时的实际角度并回填上限）？
4. **鼠标角度映射的坐标**：用"屏幕比例 → 角度"（跟相机无关，适合观众视角），还是"相对角色朝向的偏移"（转身时鼠标也要跟着动）？倾向前者，`mouseAngleSpace` 的「角色相对」已经作为开关提供。

已定：三块划分（① 脊椎 / ② 头颈 / ③ 眼睛）够用；外部接口本版就留；烘焙不做；humanoid Avatar 由使用侧保证（用成熟模型测试，面板只做检查与报错，不做非 humanoid 回退）。

## 后续规划（本版不做）

- **状态机/图驱动组件**：一个 `StateMachineBehaviour`（按状态设权重/目标）与 Warudo Blueprint 节点；前置是上面那组公开接口。
- **多目标**：借 Multi-Aim 的"多 Source 权重求和"支持"看最近的几个兴趣点"。
- **TextureUV 应用层**：UniVRM 的第三种眼睛驱动方式（贴图 UV 偏移），遇到既不支持形态键也不支持眼球骨骼的模型再补。
- **手/上半身联动**：真正的"整个人转向"（`bodyWeight = 1` 只让脊柱跟着转，手臂不会跟随）。

