# 注视约束（眼睛 + 头部看向目标）设计

`HoUnityTools/Constraints/Ho Look At Constraint`。让角色的**眼睛形态键**和**头部骨骼**尽量朝向一个目标，两种目标模式：

- **跟随物体**：看向一个 `Transform`（可带偏移、可指定"看得更远一点"的前瞻）。
- **跟随鼠标**：看向鼠标位置（角度映射 / 世界点 / 射线命中三种取法）。

## 设计约束（本版定稿的方向）

- **不猜头骨的局部轴**。头部的旋转在**参考系**（默认角色根，可选 head 的父级或世界）里算好，再以**世界空间叠加**到头骨上。头骨的局部轴约定（Blender 导出的骨骼 Y 朝上、roll 随手画）完全不影响结果，也不需要用户去配 ±X/±Y/±Z。
- **不假设 humanoid**。本项目角色预制体里 `Animator.m_Avatar = 0`，不能依赖 Unity 的 muscle 空间；humanoid muscle 方案只作为后续可选项。
- **每帧读"动画之后"的姿势再叠加增量**，不缓存基准姿势：动画换姿势/切动作我们自然跟随，不累积漂移，组件一关姿势就回到动画。
- **形态键只写不读**（眼睛那四个方向键是**输出**）。这次不读眨眼/凝视键：凝视键是我们要写的东西，读它只会自反馈。
- **与眨眼约束解耦**：两者写不同的键（眼睑 vs 凝视），各自维护绑定与基准快照；共用的形态键写入机制抽出来复用（见「与眨眼约束的复用」）。
- **输入系统**：本项目 `activeInputHandler = 1`（**只启用 Input System 包**），旧的 `UnityEngine.Input` 会直接抛异常。鼠标模式走 `Mouse.current`，并用 `#if ENABLE_INPUT_SYSTEM / ENABLE_LEGACY_INPUT_MANAGER` 兼容 Warudo 等运行时，另给一个"输入来源"选项手动指定。

## 适用范围与非目标

适用范围：

- 人形/兽形角色，有头骨（必填）与可选颈骨；眼睛可以是形态键（VRM/ARKit/PICO/VIVE/MMD 等各家命名都支持）。
- 站立、说话、走路的角色：头部的增量叠加在动画之上，所以和待机/动作动画共存。
- 观众视角类场景（角色看向镜头/鼠标）：鼠标模式。

非目标：

- 不做全身 IK、不做眼球骨骼的旋转（只做形态键；如果角色用眼球骨骼而不是形态键，那是另一套，先不做）。
- 不做情绪/表情推断（"看向感兴趣的东西"不在这里）。
- 不做相机控制（不动相机，只动角色）。
- 不做左右眼分别看不同目标。

## 术语

| 术语 | 含义 |
| --- | --- |
| **参考系** | 分解目标方向用的坐标系。默认角色根（`root.forward` = 前，`root.up` = 上），可改成 head 的父级或世界 |
| **目标点** | 物体模式 = 目标位置 + 偏移；鼠标模式 = 鼠标算出来的世界点或直接的角度 |
| **总角度** | 目标相对参考系前方的偏航（yaw）与俯仰（pitch）误差 |
| **头部承担** | 总角度里分给头部（含颈骨）的部分 |
| **眼睛残余** | 总角度减掉头部承担后剩下的部分，由眼睛形态键表达 |
| **满值角度** | 形态键 = 100 时对应的眼睛偏转角度（各家模型不同，默认 25°） |

## 数据流

```
物体模式: target.position (+offset)  ┐
鼠标模式: 角度映射 / 世界点 / 射线   ┘─→ 目标点（或直接角度）
                                          │
                     参考系（角色根）分解 yaw / pitch
                                          │
                     死区 → 椭圆限位 → 头部/眼睛分工（头部承担比例）
                        │                             │
              头部：世界空间叠加旋转            眼睛：残余角 → 四向形态键
              （含可选颈骨分摊、平滑）          （左右族 / 内外族，互斥取一）
                        │                             │
                        └────── 写回（head.rotation / SetBlendShapeWeight）──────┘
```

## 组件与文件结构

| 文件 | 职责 |
| --- | --- |
| `Runtime/Constraints/HoLookAtConstraint.cs` | 组件本体：目标解算、分工、写头、写形态键 |
| `Runtime/Constraints/HoLookAtData.cs` | 枚举与可序列化数据：目标模式、鼠标取法、眼睛条目 |
| `Runtime/Constraints/HoLookAtSolver.cs` | 纯数学：目标方向 → yaw/pitch → 分工；角度限位与平滑 |
| `Runtime/Constraints/HoMousePointer.cs` | 鼠标/指针采样：Input System 优先，旧 Input 兜底，三种取法 |
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

### 眼睛输出（每条 = 一个"轴 + 键"）

眼睛的四个方向是**互斥取一**的：水平取 `左` 或 `右`（取绝对值大的那个方向），垂直取 `上` 或 `下`。所以一条条目的结构是：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `axis` | `YawPositive / YawNegative / PitchPositive / PitchNegative` | 这条对应哪个方向 |
| `mapping` | `HoShapeKeyTarget` | 共享映射结构：网格范围、键名、混合模式、增益、偏移、输出范围、钳制、权重、ramp 预设 + 强度（+ 自定义曲线/attack/release） |

- 「左右族」和「内外族」靠**键名**区分，不靠语义推断：预设会把 `LookRight`（或 `EYES_LOOK_RIGHT_L`）填进 `YawPositive`；内外族则填 `eyeLookOutLeft` 之类，并在预设里按眼别处理符号（见下）。
- ⚠️ **内外族的符号**：`In/Out` 是相对眼球的。左眼 `In` = 往鼻侧 = 往右看；左眼 `Out` = 往左看。右眼镜像。所以内外族**必须左右眼各一条**（ARKit/PICO 本来也只有左右眼键），且 `YawPositive` 在左眼上对应 `In`、在右眼上对应 `Out`。预设按眼别填好，手改时面板会给提示。

### 头部输出

| 字段 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `headBone` | `Transform` | 空 | 头骨（必填，否则只做眼睛） |
| `neckBone` | `Transform` | 空 | 可选颈骨 |
| `neckShare` | `0..1` | 0.35 | 头部承担里再分给颈骨的比例（0 = 全给头） |
| `reference` | `Transform` | 空 = 角色根 | 参考系；空且无根则用世界 |
| `yawLimit` | `float` | 70° | 头部最大偏航 |
| `pitchLimitUp` | `float` | 40° | 头部最大抬头 |
| `pitchLimitDown` | `float` | 30° | 头部最大低头 |
| `headShare` | `0..1` | 0.7 | 超过死区之后头部承担的比例 |
| `deadZone` | `float` | 8° | 死区：这么小的角度只用眼睛 |
| `headSmoothing` | `float` | 0.06 s | 头部角度的一阶平滑 |
| `headMaxSpeed` | `float` | 360°/s | 头部角速度上限（防瞬移） |
| `headJelly` | `bool` + `f/ζ` | 关 | 可选：头部角度过弹簧（复用果冻求解器，带轻微超调） |
| `headWeight` | `0..1` | 1 | 头部总权重（0 = 只做眼睛） |

### 眼睛参数

| 字段 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `eyeLimit` | `Vector2` | (25°, 20°) | 眼睛满值对应的水平/垂直角度（**每个模型不同，必须可调**） |
| `eyeWeight` | `0..1` | 1 | 眼睛总权重 |
| `eyeSmoothing` | `float` | 0.04 s | 眼睛角度的一阶平滑（比头部快） |
| `eyeEllipseClamp` | `bool` | true | 把 (h, v) 按椭圆夹取，避免斜向看时"过转" |
| `eyeGazeScale` | `float` | 1 | 形态键满值的总缩放（各家 100 对应的角度不同时的兜底） |

## 每帧流程与时序契约（重点）

### 帧内顺序

```
输入 / Update
    鼠标采样、目标物体被别的脚本移动（用户脚本、Timeline、物理）
动画更新
    Animator（含 Animation Rigging 的 rig 求值）写骨骼姿势 ← 这一步决定"基准姿势"
PreLateUpdate · ScriptRunBehaviourLateUpdate
    ★ 我们：读动画之后的 head 姿势 → 算总角度 → 分工 → 叠加写 head（世界空间）→ 写眼睛形态键
PreLateUpdate · afterLateUpdate（MC2 的 PlayerLoop 槽）
    布料/头发读骨头（能看到已经转好的头）、渲染准备
PostLateUpdate
    渲染
```

### 为什么默认 `LateUpdate`

| 选项 | 结果 |
| --- | --- |
| `Update` | 会被同帧的动画覆盖（动画在 Update 之后写姿势）；而且读到的 head 姿势还是上一帧的，算出来的角度差一帧 |
| **`LateUpdate`（默认）** | 晚于动画 → 我们的增量不会被覆盖；早于 MC2/渲染 → 头发、耳朵、挂在头下的东西都能看到 |
| `FixedUpdate` | 与渲染帧不同步，头部会抖 |

### 关键不变量

1. **每帧读当前（动画后）的姿势，只写增量**：不保存"初始姿势"当基准。否则动画换姿势时会打架/漂移。
2. **先算后写**：先把总角度、头部承担、眼睛残余都算完，再写 head，最后写形态键。眼睛的残余角不依赖 head 写完之后的世界姿势，所以顺序只是为了可读性；但如果参考系本身是被我们改过的骨骼（例如把参考系设成颈骨），就必须严格先算后写。
3. **写 head 用世界旋转叠加**（`head.rotation = delta * head.rotation`），不要动 `localRotation` 的欧拉分解 —— 那样会踩到骨骼轴向。
4. **形态键"叠加 + 外部基准检测"**：和眨眼约束同一套（读回值若等于我们上次写的值，就沿用上一次确认的外部基准）。这样动画/面捕写的凝视键不会被我们每帧累加。

### 和别的系统共存

| 共存对象 | 规则 |
| --- | --- |
| **Animation Rigging** 已经在驱动 head/neck | **不要两边都写**：rig 在我们之前求值，我们会覆盖它。二选一 —— 把我们的 `headWeight` 设 0（只做眼睛），或者把 rig 里 head/neck 约束的权重设 0 |
| **Animator IK**（`OnAnimatorIK`） | 它在动画更新里跑，我们的 LateUpdate 晚于它 → 我们会覆盖。要让它负责头部就把 `headWeight` 设 0 |
| **MC2 布料**（头发、耳朵、尾巴） | MC2 在 LateUpdate 之后读骨头，能看到我们的头部旋转。头部转得快时如果布料抖，先查 MC2 的 `移动速度制限/回転速度制限`（1 m/s、360°/s 的配置在上一轮已经踩过一次） |
| 别的 LateUpdate 脚本也在写 head | 显式设 Script Execution Order：`目标驱动脚本 < 我们 < 消费者`；我们的组件建议保持默认 0 |
| 眨眼约束 | 写的是不同键（眼睑 vs 凝视），互不干扰；两者都用"叠加 + 外部基准检测"，同一个键被两边写也能相加 |
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
| 眼睛 | 6 | 满值角度、权重、平滑、椭圆夹紧、总缩放、（每条目共用 ramp/增益/范围等 11 项） |
| 眼睛条目 | 4 条 × 12 | 四向各一条（含共享映射结构的字段） |
| 头部 | 14 | 头骨/颈骨/参考系、限位、承担比例、死区、平滑、角速度、果冻、权重、丢失行为、瞬移阈值 |
| 全局 | 5 | 更新时机、编辑模式求值、锁定/暂停、调试绘制、写入阈值 |

合计约 **36 个全局 + 4 条眼睛条目**；日常只动三处：头骨、眼睛满值角度、（鼠标模式的）灵敏度。

## 面板结构

```
Ho 注视约束
[ 预设 ▾ ] [ 只看眼睛 | 头眼并用 | 鼠标模式 ] [ 清空 ]
▸ 目标                              跟随物体 / 鼠标·角度映射
    模式 [跟随物体 ▾]  目标 [HeadTarget ▾]  偏移 [0,0,0]
    (鼠标模式) 取法 [角度映射 ▾] 灵敏度 [30°/20°] 死区 0.05  相机 [Main Camera ▾]
▾ 眼睛                              左右族 / 满值 25°×20°
    权重 1.0   平滑 0.04 s   椭圆夹紧 [✓]   总缩放 1.0
    Yaw+  [LookRight ▾ (VRM)]   ramp[直通 ▾] 强度 1.0   细节 ▾
    Yaw−  [LookLeft ▾ (VRM)]    …
    Pitch+[LookUp ▾ (VRM)]      …
    Pitch−[LookDown ▾ (VRM)]    …
▸ 头部                              头骨 Head / 偏航 ±70° 俯仰 +40/−30°
    参考系 [角色根 ▾]  颈骨 [Neck ▾] 颈部分摊 0.35
    死区 8°  头部承担 0.7  平滑 0.06 s  角速度 360°/s  果冻 [ ]
    (展开) 丢失行为 / 回正延迟 / 瞬移阈值
▸ 调试
    总角度 (yaw/pitch) / 头部承担 / 眼睛残余 / 四个键的输出值
    [手动目标 ✓] 拖动虚拟目标调参   [重置]
```

- **预设**：`双眼四向`（VRM 的 LookUp/Down/Left/Right）、`左右眼四向·左右族`（Meta/SRanipal 命名）、`左右眼四向·内外族`（ARKit/PICO 的 In/Out，按眼别自动处理符号）、`只看眼睛`（headWeight = 0）、`鼠标模式`（切到鼠标 + 角度映射 + 常用灵敏度）。
- **键名格**复用眨眼约束的 `HoKeyNameDropdown`（内置表 + 网格上实际存在的键 + 缺失标黄）。
- **调试**：Gizmo 画参考系前/上、目标点、总角度扇形、头部承担与眼睛残余的分界（两个不同颜色的扇形），一眼能看出"是头没转够还是眼睛没吃饱"；读数给出每个方向最终写入的键值。
- **手动目标**：编辑器和播放模式都能用一个虚拟目标（场景里的一个点或滑杆）驱动，不用真鼠标也能调参。

## 调试工作流

| 现象 | 先看什么 | 常见原因 |
| --- | --- | --- |
| 头完全不动 | 调试区总角度、`headWeight`、头骨是否填了 | 头骨没填 / `headWeight = 0` / 目标就在前方（在死区内） |
| 头动了但眼睛不动 | 四个方向的输出值、缺失键 | 键名不对（网格上没有）或满值角度太大（角度换算后只有几个单位） |
| 眼睛方向反了 | 内外族符号 | 用了 `In/Out` 键但按左右族配的（面板会提示）；或左右眼条目填反 |
| 斜着看时"过转" | `eyeEllipseClamp` | 关掉了椭圆夹紧，h 和 v 同时接近满值 |
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
2. **眼睛部分**：目标解算 → yaw/pitch → 四向键输出；四套预设可用。验收：鼠标左右移动时 `LookLeft/LookRight` 互斥切换、`LookUp/LookDown` 不受影响；斜向时椭圆夹紧生效。
3. **头部部分**：世界空间叠加 + 限位 + 死区 + 分工 + 平滑；参考系可切。验收：动画播放中头部仍能额外转向目标，且不累积漂移；关掉组件姿势立刻回动画。
4. **鼠标模式三取法 + 输入系统兼容**。验收：Input System 项目里不报错；旧 Input 分支在 `#if` 下能编译。
5. **调试视图 + 文档回填**：把实测的角度分工手感（死区/承担比例默认值）写回本文档。

## 待确认

1. **头部默认参数**：死区 8°、头部承担 0.7、偏航 ±70°、俯仰 +40/−30° —— 这组是"看起来自然"的起点，要不要更保守（比如死区 10°、承担 0.6）？
2. **眼睛满值角度**默认给 25°×20°，不同模型差异很大；要不要在调试区加一个"标定"按钮（把某个方向慢慢推到 100，记录对应的实际角度）？
3. **颈骨分摊**：只支持"颈骨 + 头骨"两段够吗？还是要支持任意骨骼链（neck1/neck2/head 各给比例）？
4. **鼠标角度映射的坐标**：用"屏幕比例 → 角度"（跟相机无关，适合观众视角），还是"相对角色朝向的偏移"（转身时鼠标也要跟着动）？我倾向前者 + 一个"忽略屏幕左右相反"的开关。
5. **是否需要"眨眼时暂停眼睛注视"**（闭眼时盯着鼠标会看到眼睑在动，有人不喜欢）？
6. **形态键 vs 眼球骨骼**：如果以后要用眼球骨骼，现在的数据结构（四向键）要不要预留一个 `Bone` 目标类型？
7. **参考系**：默认角色根（`root.forward/up`）；如果角色根带缩放/倾斜（飞行姿态），要不要额外给"用世界 up"的选项？
