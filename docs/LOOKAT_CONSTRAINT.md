# 注视约束（眼睛 + 头部看向目标）设计

`HoUnityTools/Constraints/Ho Look At Constraint`。让角色的**眼睛形态键**和**头部骨骼**尽量朝向一个目标，两种目标模式：

- **跟随物体**：看向一个 `Transform`（可带偏移、可指定"看得更远一点"的前瞻）。
- **跟随鼠标**：看向鼠标位置（角度映射 / 世界点 / 射线命中三种取法）。

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
- **眨眼不影响注视**：闭眼期间眼睛仍然照着目标算（形态键/骨骼照写），眨眼约束只管眼睑键，两者不互相打断。
- **一键装配预设**：`GetBoneTransform` 拿 head/eye + 在各网格上挑实际存在的凝视键 + 判断左右族/内外族，点一下就能用（你确认过预设保持这个形态即可）。
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
鼠标模式: 角度映射 / 世界点 / 射线   ┘─→ 目标点（或直接角度）
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
| `Runtime/Constraints/HoMousePointer.cs` | 鼠标/指针采样：Input System 优先，旧 Input 兜底，三种取法 |
| `Runtime/Constraints/HoLookAtAutoRig.cs` | 一键装配：`GetBoneTransform` 取 Head/LeftEye/RightEye、检查 humanoid + IK Pass、按语义挑网格上存在的凝视键、判断左右族/内外族 |
| `Runtime/Constraints/HoShapeKeyTarget.cs` | **从眨眼约束抽出的共享映射结构**（键、范围、增益、ramp 预设…） |
| `Runtime/Constraints/HoShapeKeyWriter.cs` | **抽出的共享写入器**：绑定表、外部基准快照、合并、阈值写 |
| `Editor/Constraints/HoLookAtConstraintEditor.cs` | 面板：目标、眼睛、头部、调试 |
| `Editor/Constraints/HoLookAtPresetActions.cs` | 预设：双眼四向/左右眼四向（左右族、内外族）、"只看眼睛"、"头眼并用" |

## 数据模型

### 目标来源

| 字段 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `targetMode` | `Transform / Mouse` | Transform | 跟随物体 / 跟随鼠标 |
| `target` | `Transform` | — | 物体模式的目标 |
| `targetOffset` | `Vector3` | 0 | 物体模式的位置偏移（在目标自身空间） |
| `mouseSampleMode` | `AngleMap / WorldPoint / Raycast` | AngleMap | 鼠标取法（见「鼠标模式」） |
| `mouseCamera` | `Camera` | 空 = `Camera.main` | 用来做屏幕→世界的换算 |
| `mouseSensitivity` | `Vector2` | (30°, 20°) | 角度映射模式下鼠标走满屏幕对应多少度 |
| `mouseDeadZone` | `float` | 0 | 鼠标中心死区（屏幕比例） |
| `mouseDistance` | `float` | 3 m | 世界点模式的投影距离 |
| `mousePlaneHeight` | `float` | 0 | 世界点模式可改为投到水平面（角色脚下平面） |
| `mouseRaycastMask` | `LayerMask` | 空 | 射线模式下命中的层 |
| `mouseOffscreenBehavior` | `Hold / Return` | Hold | 鼠标移出窗口/无输入时保持还是回中立 |

### 眼睛输出（两个应用层 + 四条方向曲线）

照 UniVRM 的分层：**解算层**算出目标相对参考系的 yaw/pitch，**应用层**把角度落到眼睛上。应用层两种，可以同时开：

**A. 形态键**（每条 = 一个方向 + 一个键）

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `direction` | `HorizontalInner / HorizontalOuter / VerticalUp / VerticalDown` | 照 UniVRM 的四向命名：**横向用内外**，不是左右 |
| `mapping` | `HoShapeKeyTarget` | 共享映射结构：网格范围、键名、混合模式、增益、偏移、输出范围、钳制、权重、ramp 预设 + 强度 |
| `enabled` | bool | 这条是否参与 |

**B. 眼球骨骼**

| 字段 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `driveEyeBones` | bool | 自动 | 角色有 `LeftEye`/`RightEye`（humanoid）时默认开 |
| `leftEyeBone` / `rightEyeBone` | `Transform` | 自动取 `HumanBodyBones.LeftEye/RightEye` | 眼球骨骼；用"世界空间叠加旋转"驱动（不猜轴向，和头部那条世界叠加同一个思路） |
| `eyeBoneWeight` | `0..1` | 1 | 眼球旋转权重 |
| `eyeBoneUseCurve` | bool | true | 眼球角度也过四向曲线（关掉就是线性） |

**四条方向曲线（两种应用层共用）** —— 这就是 UniVRM 的 DegreeMapping：

| 曲线 | 横轴（Curve X Range Degree） | 纵轴（Curve Y Range Degree） |
| --- | --- | --- |
| `horizontalInner` | 目标角度上限（默认 30°） | 该角度下的满值输出：形态键 = 0..100，骨骼 = 0..30° |
| `horizontalOuter` | 同上（内外可以给不同上限，很多模型外侧能动得更多） | 同上 |
| `verticalUp` | 默认 20° | 同上 |
| `verticalDown` | 默认 25° | 同上 |

- 曲线的横轴是**归一化后的 |角度| / 上限**，纵轴是 0..1 的输出比例 —— 所以"角度上限"和"满值输出"是分开的两个数，模型差异（有的 100 对应 15°、有的对应 35°）靠这两处标定，而不是靠一个笼统的"强度"。
- 默认曲线给线性（`y = x`）；预设会给一组"轻微缓入"的曲线，避免小角度时眼睛就动得很明显。
- ⚠️ **内外族的符号**：`In/Out` 是相对眼球的。左眼 `In` = 往鼻侧 = 往右看；左眼 `Out` = 往左看，右眼镜像。所以用内外族时**左右眼各填一组键**（ARKit/PICO 本来也只有左右眼键），预设按眼别把 `HorizontalInner`/`HorizontalOuter` 落到正确的键和符号上。
- **左右族是等价表达**：VRM 的 `LookLeft/LookRight`、Meta 的 `EYES_LOOK_LEFT_L` 这类"相对头"的命名，填进 `HorizontalOuter`（往左看 = 外侧）/ `HorizontalInner`（往右看 = 内侧）即可，语义完全对得上。预设会自动判断该网格上存在哪一族。

### 头部输出

参数直接用 Unity 内置 LookAt IK 的语义（`weight / bodyWeight / headWeight / eyesWeight / clampWeight`），另外加上我们自己算"目标方向"时用的死区、分工与限位：

| 字段 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `animator` | `Animator` | 空 = 自动找 | 必须是 humanoid（有 Avatar）；空时按同物体 → 父级 → 子级找 |
| `weight` | `0..1` | 1 | **= Unity 的 `weight`**：整个 LookAt 的总权重 |
| `bodyWeight` | `0..1` | 0.3 | **= Unity 的 `bodyWeight`**：身体参与度（Unity 沿脊柱分摊）—— 这就是"带着身子动" |
| `headWeight` | `0..1` | 1 | **= Unity 的 `headWeight`**：头部参与度（0 = 只做眼睛） |
| `clampWeight` | `0..1` | 0.6 | **= Unity 的 `clampWeight`**：0 不受限、1 完全夹死、0.5 = 只能在可能范围（180°）的一半内转。作为限位之外的最后一道保险 |
| `reference` | `Transform` | 空 = 角色根 | 算 yaw/pitch 用的参考系（只影响我们的解算与限位，不影响 Unity 的 IK） |
| `yawLimit` | `float` | 70° | 头部最大偏航（作用在传进去的目标方向上） |
| `pitchLimitUp` | `float` | 40° | 头部最大抬头 |
| `pitchLimitDown` | `float` | 30° | 头部最大低头 |
| `headShare` | `0..1` | 0.7 | 超过死区之后头部承担的比例（剩下的给眼睛） |
| `deadZone` | `float` | 8° | 死区：这么小的角度只用眼睛 |
| `aimSmoothing` | `float` | 0.06 s | 目标方向的一阶平滑（不是骨骼旋转平滑） |
| `aimMaxSpeed` | `float` | 360°/s | 目标方向的角速度上限（防瞬移） |
| `aimJelly` | `bool` + `f/ζ` | 关 | 可选：目标方向过弹簧（复用果冻求解器，带轻微超调） |

- **不再需要"颈骨"字段**：颈部参与由 Unity 的 `bodyWeight` 沿脊柱分摊，比我们自己分两段更自然（也就顺便把"以后要不要支持脊柱链"这个问题解决了）。
- **后续（本版不做）**：多目标（Multi-Aim 式的权重求和）。

### 眼睛参数

| 字段 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `eyesWeight` | `0..1` | 0 | 传给 Unity 的 `eyesWeight`：**默认 0**，因为眼球由我们自己的四条曲线驱动（要标定角度→输出）。若你想让 Unity 顺手把眼球骨骼也带上，可以调大它（但就绕过了曲线） |
| `eyeSmoothing` | `float` | 0.04 s | 眼睛角度的一阶平滑（比头部快） |
| `eyeEllipseClamp` | `bool` | true | 把 (h, v) 按椭圆夹取，避免斜向看时"过转" |
| `horizontalInner/Outer`、`verticalUp/Down` | `AnimationCurve` × 4 | 线性 | 四条方向曲线（见上） |
| `eyeAngleLimit` | `Vector4` | (30°, 30°, 20°, 25°) | 四条曲线横轴对应的角度上限（内/外/上/下） |

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
| 动画图层的 **IK Pass** 勾上 | 不勾的话 `OnAnimatorIK` 根本不会被调用 —— 这是"头一动不动"最常见的原因 |
| `Animator` 与组件在同一物体上，或显式指定 Animator | 默认取同物体 / 父级 / 子级的第一个 Animator |

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
| `WorldPoint` | 从相机沿鼠标射线取固定距离的点（或投到角色脚下的水平面） | 想要"看向房间里的某个位置"的物理感 |
| `Raycast` | 相机 → 鼠标射线打到 `mouseRaycastMask` 的物体 | 有场景几何、想让角色盯着墙上的东西 |

- **输入读取**：`#if ENABLE_INPUT_SYSTEM` 用 `Mouse.current.position.ReadValue()` / `Pointer.current`；`#if ENABLE_LEGACY_INPUT_MANAGER` 用 `Input.mousePosition`。本项目只开了 Input System，所以旧分支只是兼容 Warudo 之类的宿主。面板给「输入来源：自动 / Input System / 旧 Input」的手动覆盖。
- 鼠标屏幕坐标换算用 `mouseCamera`（默认 `Camera.main`，允许指定），支持 `Screen.width/height` 与相机的 `pixelRect`（多相机/画中画时不至于错位）。

## 参数总表（控制参数预算）

| 分组 | 数量 | 说明 |
| --- | --- | --- |
| 目标 | 11 | 模式、物体、偏移、鼠标取法/相机/灵敏度/死区/距离/平面/层/离屏行为 |
| 眼睛 | 8 | `eyesWeight`、平滑、椭圆夹紧、四条方向曲线 + 四个角度上限、眼球骨骼（左/右/权重/是否过曲线） |
| 眼睛形态键条目 | 4 条 × 2 列 | 内/外/上/下各一条，每条**左右眼各一个键**（内外族必须分眼；左右族也允许分眼或同键）；每条含共享映射结构的 16 项 |
| 头部 | 12 | `animator`、`weight`/`bodyWeight`/`headWeight`/`clampWeight`、`reference`、`deadZone`、`headShare`、偏航/俯仰限位、`aimSmoothing`/`aimMaxSpeed`、`aimJelly` |
| 丢失与瞬移 | 5 | `lostBehavior`、`returnDelay`、`returnSpeed`、`teleportAngleThreshold` |
| 全局 | 5 | 更新时机（两段是固定的，这里只留调试相关）、编辑模式求值、锁定/暂停、调试绘制、写入阈值 |

合计约 **36 个全局字段 + 4 条方向条目**。日常只动四处：**一键装配**、`bodyWeight`/`headWeight`、四条曲线的角度上限、（鼠标模式的）灵敏度。

## 面板结构

```
Ho 注视约束
[ 一键装配 ] [ 只看眼睛 | 头眼并用 | 鼠标模式 ] [ 清空 ]
⚠ Animator 检查: [✓ humanoid] [✓ 图层 IK Pass] [✓ Avatar]
▸ 目标                              跟随物体 / 鼠标·角度映射
    模式 [跟随物体 ▾]  目标 [HeadTarget ▾]  偏移 [0,0,0]
    (鼠标模式) 取法 [角度映射 ▾] 灵敏度 [30°/20°] 死区 0.05  相机 [Main Camera ▾]
▾ 眼睛                              形态键 · 内外族 · 满值 30°/20°
    眼睛权重 eyesWeight 0.0   平滑 0.04 s   椭圆夹紧 [✓]
    水平内 [30°] [曲线▾]  [eyeLookInLeft ▾ (ARKit)]  [eyeLookInRight ▾]   细节 ▾
    水平外 [30°] [曲线▾]  [eyeLookOutLeft ▾]          [eyeLookOutRight ▾]  细节 ▾
    垂直上 [20°] [曲线▾]  [eyeLookUpLeft ▾]           [eyeLookUpRight ▾]   细节 ▾
    垂直下 [25°] [曲线▾]  [eyeLookDownLeft ▾]         [eyeLookDownRight ▾] 细节 ▾
    眼球骨骼: 左 [humanoid LeftEye]  右 [humanoid RightEye]  权重 1.0  过曲线 [✓]
▾ 头部                              头 Head（humanoid）/ 偏航 ±70° 俯仰 +40/−30°
    总权重 weight 1.0   身体权重 bodyWeight 0.3   头部权重 headWeight 1.0   clampWeight 0.6
    参考系 [角色根 ▾]  死区 8°  头部承担 0.7
    方向平滑 0.06 s  方向角速度 360°/s  果冻 [ ]
    (展开) 丢失行为 / 回正延迟 / 瞬移阈值
▸ 调试
    总角度 (yaw/pitch) / 头部承担 / 眼睛残余 / 四条曲线输出 / 最终键值
    [手动目标 ✓] 拖动虚拟目标调参   [重置]
```

- **一键装配**：用 `Animator.GetBoneTransform` 取 `Head` / `LeftEye` / `RightEye`（**不需要猜名字**），再在各网格上找该模型实际存在的凝视键、自动判断左右族还是内外族，最后把四条曲线与键名填好；找不到的项在面板里列出来让用户手填。
- **预设**（保持你确认的形态）：`左右族（VRM/Meta/SRanipal）`、`内外族（ARKit/PICO）`、`只看眼睛`（headWeight = 0）、`鼠标模式`、`Unity 内置 IK 同参`（body 0 / head 1 / eyes 0 / clamp 0.5，和 `SetLookAtWeight` 默认值对齐，方便对照）。
- **键名格**复用眨眼约束的 `HoKeyNameDropdown`（内置表 + 网格上实际存在的键 + 缺失标黄）。
- **调试**：Gizmo 画参考系前/上、目标点、总角度扇形、头部承担与眼睛残余的分界（两个不同颜色的扇形），一眼能看出"是头没转够还是眼睛没吃饱"；读数给出每个方向的曲线输出与最终写入值。
- **手动目标**：编辑器和播放模式都能用一个虚拟目标（场景里的一个点或滑杆）驱动，不用真鼠标也能调参。

## 调试工作流

| 现象 | 先看什么 | 常见原因 |
| --- | --- | --- |
| 头完全不动 | 面板顶部的 Animator 检查三项 | 不是 humanoid / 图层没勾 **IK Pass** / Avatar 为空 —— 这三条是"头一动不动"的头号原因 |
| 头动了但眼睛不动 | 四条曲线的输出值、缺失键 | 键名不对（网格上没有）；或角度没超过曲线横轴上限太多（输出被曲线压小了） |
| 眼睛动得太猛 / 幅度不对 | 曲线横轴上限与纵轴满值 | 角度上限填小了（比如 100 键值实际只对应 15°，却按 30° 标定）；这正是 UniVRM 把两个量分开的原因 |
| 眼睛方向反了 | 内外族符号 | 用了 `In/Out` 键却按左右族配（面板会提示）；或左右眼列填反 |
| 身体跟着转得太多 | `bodyWeight` | 它沿脊柱分摊，0.3~0.5 比较自然；1 会整个人转过来 |
| 头和 Animation Rigging 抢 | rig 里是否也有 head/neck 约束 | 两边都写 → 二选一（见共存表） |
| 斜着看时"过转" | `eyeEllipseClamp` | 关掉了椭圆夹紧，h 和 v 同时接近满值 |
| 眼球骨骼转起来像"翻白眼" | 眼球骨骼的 pivot 是否在眼球中心 | 我们走世界叠加旋转，轴向不影响；但骨骼 pivot 偏了就会绕错点转 |
| 头在抖 / 一顿一顿 | 目标是否被动画或物理推着走、`headSmoothing` | 目标抖动（加平滑或换角速度映射）；或 MC2 布料在和头部抢骨头 |
| 头和动画打架 | 动画是否也驱动 head（或 Animation Rigging） | 两边都写 → 按「和别的系统共存」二选一 |
| 眨眼约束失效了 | 眨眼输出键 | 注视约束不该写眼睑键；检查预设有没有误填 |
| 切换动作时头"弹"一下 | 是否缓存了基准姿势 | 违反"每帧读动画后姿势"的不变量 |

## 与眨眼约束的复用（先重构再新增）

现在的眨眼约束里已经有一整套"形态键绑定 + 基准快照 + 合并 + 阈值写"的机制。注视约束要用同一套，所以第一步先抽公共件（**只移字段、不改字段名**，`[Serializable]` 类的类型名不进 YAML，所以改名不丢数据）：

1. `HoShapeKeyTarget`：从 `HoBlinkTarget` 原样搬字段（`meshScope/meshIndex/keyName/side/blendMode/weight/gain/offset/outputMin/outputMax/clampToRange/rampPreset/rampIntensity/rampCurve/rampAttack/rampRelease`）。`side` 对注视约束无意义，但留着不碍事。
2. `HoShapeKeyWriter`：绑定表、外部基准快照、目标求值与合并、阈值写。眨眼约束改用它（行为不变），注视约束直接复用。
3. `HoBlinkTarget` 保留为 `HoShapeKeyTarget` 的别名（`using HoBlinkTarget = HoShapeKeyTarget;`）以免编辑器代码大改。

顺带把上一轮踩到的两个坑固化进写入器：**外部基准检测**（不要把自身写入当基准）和**写回值夹到 0..100**。

## 性能与资源约束

- 每帧只有一次目标解算、一次分工、最多 4 次形态键写（阈值写）、1~2 次骨骼旋转写；无分配、无字典查找（绑定在构建期解析）。
- 鼠标采样每帧一次，`Camera.ScreenPointToRay` 只在需要世界点时调用。
- Gizmo 只在选中时绘制，读数字符串只在面板可见时生成。

## 里程碑与验收

1. **抽公共件**（`HoShapeKeyTarget` + `HoShapeKeyWriter`）：眨眼约束改用它，行为与数值不变（用同一套调试读数回归）。
2. **眼睛 · 形态键**：目标解算 → yaw/pitch → 四条方向曲线 → 内/外/上/下四向键输出；左右族与内外族两套预设可用。验收：鼠标左右移动时"内/外"互斥切换、`垂直上/下` 不受影响；斜向时椭圆夹紧生效；把某条曲线的横轴上限从 30° 改到 15°，输出幅值随之翻倍。
3. **头部 · humanoid IK 路线**：`OnAnimatorIK` 里算方向、限位、分工并调用 `SetLookAtPosition/SetLookAtWeight`；`bodyWeight` 带头身。验收：`bodyWeight` 从 0 调到 0.6 时能看到上半身参与；目标移到身后时头部按限位停住而眼睛吃满；关掉组件姿势立刻回动画。
4. **眼睛 · 眼球骨骼 + 一键装配**：`GetBoneTransform(LeftEye/RightEye)` + 世界空间叠加 + 可选过曲线。验收：眼球骨骼自身轴向任意旋转 90° 后，看向方向不变；新模型上点一次"一键装配"即可用。
5. **鼠标模式三取法 + 输入系统兼容**。验收：Input System 项目里不报错；旧 Input 分支在 `#if` 下能编译。
6. **调试视图 + 文档回填**：把实测的角度分工手感（死区/承担比例/曲线形状/`bodyWeight` 的默认值）写回本文档。

## 后续规划（本版不做）

- **多目标**：借 Multi-Aim 的"多 Source 权重求和"支持"看最近的几个兴趣点"。
- **TextureUV 应用层**：UniVRM 的第三种眼睛驱动方式（贴图 UV 偏移），遇到既不支持形态键也不支持眼球骨骼的模型再补。
- **手/上半身联动**：真正的"整个人转向"（`bodyWeight = 1` 只是让脊柱跟着转，手臂不会跟随）；如果需要，可以再叠一个"朝向目标的身体转向"。

## 待确认

1. **头部默认参数**：`bodyWeight` 0.3、`headWeight` 1、`clampWeight` 0.6、死区 8°、头部承担 0.7、偏航 ±70°、俯仰 +40/−30° —— 这组是"看起来自然"的起点，要不要更保守（比如死区 10°、承担 0.6）？
2. **四条曲线的角度上限默认值**：内/外/上/下 = 30°/30°/20°/25°。要不要在调试区加一个**标定**按钮（拖动虚拟目标把某个方向推到形态键满值，记录当时的实际角度并回填上限）？
3. **鼠标角度映射的坐标**：用"屏幕比例 → 角度"（跟相机无关，适合观众视角），还是"相对角色朝向的偏移"（转身时鼠标也要跟着动）？我倾向前者 + 一个"忽略屏幕左右相反"的开关。
4. **`eyesWeight` 怎么处理**：默认 0（眼球完全由我们的四条曲线管）；要不要在面板上直接暴露成"眼球交给 Unity"的一键切换，方便对照？
5. **参考系**：默认角色根（`root.forward/up`）；如果角色根带缩放/倾斜（飞行姿态），要不要额外给"用世界 up"的选项？
6. ⚠️ **确认你的角色运行时有 humanoid Avatar**：`potato_build.prefab` 里 `Animator.m_Avatar = 0`，如果这是模型的普遍情况，humanoid 路线需要先给这些模型配好 Humanoid Avatar（或者由 Warudo 之类宿主在运行时赋上）。眼睛形态键部分不受影响，但**头部会完全不生效**。
