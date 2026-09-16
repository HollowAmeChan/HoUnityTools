# 跟随约束的坐标系规则

`HoFollowConstraint` 的位姿解算**全部发生在「锚点坐标系」里**：

- 锚点本身就是父级本地常量（`anchorLocalPosition` / `anchorLocalRotation`），所以坐标系就是父级。
- 没有父级时坐标系就是世界，代码退化成与旧版逐位一致的世界空间路径。
- 世界只在**读写边界**出现：读目标位姿、写回 Transform。

一句话：约束对「锚点 ↔ 目标」做的任何比较、混合、阻尼，都发生在同一个坐标系里；父级的平移/旋转/缩放不出现在任何状态量里。

## 为什么要这样

旧版把 `currentPosition` / `currentRotation` / `velocity` 存成**绝对世界量**，于是低通算的是

```
P⁻¹ · Lowpass_world(desired_world)      旧版
Lowpass_frame(P⁻¹ · desired_world)      现在
```

`P` 是父级世界变换。**低通和换坐标系不可交换**：父级一动，两者就相差一个 `v_parent × smoothTime`。

关键点：旧版的瞬时部分是好的 —— 锚点随父级刚性运动，目标和锚点一起动时 `Lerp(anchor, desired, follow)` 也一起动。坏的只有**时间积分**那一层（`SmoothDamp` / `Slerp` / 速度夹取），它把父级的刚性运动当成"目标动了"来滤，再被 `SetPositionAndRotation` 写回世界位姿，滞后就变成了**相对父级的滑移**。这也解释了为什么把「位置跟随」调到 1 也没用：跟满只是让期望位姿更贴目标，滞后照旧。

`response = 4` 时 `smoothTime = 1/response = 0.25 s`，Unity 的 `SmoothDamp` 是 `ω = 2/T` 的临界阻尼二阶系统，跟踪匀速目标的稳态误差 = `v·T`。

## 实测（Unity 2021.3.18f1 的 `Vector3.SmoothDamp`，dt = 1/60）

场景：跟点与目标固定在父级本地空间的同一点（理想情况应完全刚性），父级匀速平移。

| 参数 | 旧版运动中最大相对偏移 | 停下 1 s 后 |
| --- | --- | --- |
| 父级 4 m/s | **0.969 m**（理论 `v·T` = 1.000 m） | 0.002 m |
| 父级 4 m/s，超调 0.15 | **0.834 m**（超调的前馈恰好抵消一部分） | 0.000 m |
| 父级 10 m/s，最大速度 1 | **27.166 m** | 26.200 m（**被永久甩掉**） |
| 同上，坐标系状态（现在） | **0.000000 m** | 0.000000 m |

旋转同理：`UpdateRotation` 的 `t = 1 - exp(-response·dt)` 是一阶低通，父级匀速自转 `ω` 时的稳态相对角度误差 = `ω/response`（`ω = 90°/s`、`response = 4` → **22.5°**）。

⚠️ `maxVelocity` / `maxAngularVelocity` 旧版夹的是**世界速度**。父级速度一旦超过上限，跟点永远追不上，偏移**无界增长**（上表 27 m）—— 这不是滞后，是甩飞。夹取搬到坐标系里之后，它才回到本意：限制软跟随的**修正**速度。

## 症状 → 原因

| 症状 | 旧版原因 | 现在 |
| --- | --- | --- |
| 编辑模式拖父级 / 动画驱动父级，跟点滞后回弹 | 世界状态 + `SetPositionAndRotation` | 刚性跟随，`localPosition` 每帧不变 |
| 父级匀速平移时恒定滑出，停下回弹 | 世界空间 `SmoothDamp` | 相对偏移恒为 0 |
| 父级自转时相对角度误差；开「保持水平」时期望位姿自己会漂 | 欧拉分量替换只在固定坐标系里才有意义，世界欧拉混合不协变 | 欧拉运算在坐标系里，协变 |
| `maxVelocity` 一开就被永久甩飞 | 夹的是世界速度 | 夹相对速度 |
| 父级一起步就往前冲 | `overshoot` 前馈用被父级速度污染的 `velocity` | 前馈用相对速度 |
| 父级缩放动画时相对位置不刚性 | 世界位姿每帧 `InverseTransformPoint` 反算本地量 | 直接写本地量 |

## 逐特性映射

| 功能 | 旧版（世界） | 现在（锚点坐标系） |
| --- | --- | --- |
| 锚点 | 本地常量 → 每帧抬到世界 | 本地位姿就是坐标系量 |
| 轴锁定 `lockX/Y/Z` | 世界轴分量朝锚点世界坐标 `Lerp` | 坐标系轴分量（锁的是坐标系轴） |
| 旋转过滤 / 旋转锁定 | 世界欧拉分量 | 坐标系欧拉分量 |
| `rotationMode.World` | `target.rotation` | `Inverse(parent.rotation) * target.rotation` |
| `rotationMode.Local` | `parent.rotation * target.localRotation` | `target.localRotation`（世界结果相同，去掉无意义往返） |
| `rotationMode.Target` | 世界量 `target.rotation * targetAnchorRotation` | 捕获的是坐标系内的相对量 |
| `offsetMode.Local` | `target.TransformVector(offset)` 加在世界位置 | 世界位移经 `InverseTransformVector` 进入坐标系，写回时再乘回来 → 世界结果逐字不变 |
| `offsetMode.World` | 直接加世界向量 | 每帧换算，父级自转时会被软跟随（见取舍） |
| `limit` | 世界轴形状 + 世界中心 | 形状与中心都在坐标系里（球体等价，盒体/圆柱跟着坐标系转） |
| 写回 | `SetPositionAndRotation` | 有坐标系时 `SetLocalPositionAndRotation`，否则世界位姿 |
| `Velocity` / `AngularVelocity` | 世界量 | **相对量**（世界速度还包含坐标系自身运动） |
| Gizmo / `CurrentPosition` 等公开属性 | 世界 | 仍按世界给出（读取时换算） |

## 坐标系模式

| 模式 | 含义 | 什么时候用 |
| --- | --- | --- |
| **本地**（默认） | 状态在锚点坐标系（父级本地空间，无父级时等同世界） | 跟点与目标共用父级、父级会被动画/抓取/缓动驱动 |
| **世界** | 旧行为：状态在世界空间，父级运动会进入阻尼 | 场景根的拖尾相机、故意要世界空间滞后感的场合 |

新字段带初始化器，老场景反序列化时会拿到「本地」；真正需要旧行为的实例在「跟随」区手动切回「世界」，Inspector 会给出警告提示。

父级或模式在运行中变化时，状态会按旧坐标系换算到新坐标系（世界位姿不变），相对速度归零，不会突然跳一下。

## 验证流程

1. 父级 4 m/s 匀速平移 2 s：Debug 区「相对偏移 / 相对角度误差」应恒为 0（旧版约 1 m 并回弹）。
2. 父级 90°/s 匀速自转：相对角度误差应恒为 0（旧版约 22°）。
3. `最大速度 = 1`、父级 10 m/s：跟点应刚性跟随（旧版被永久甩掉）。
4. 手在父级里运动：仍然有由「响应」决定的滞后 —— 这是功能，不是 bug。
5. 编辑模式拖父级：刚性跟随，无橡皮筋。
6. 世界模式 + 无父级：与旧版逐位一致。

## 已知取舍

- **`offsetMode = World`**：偏移保持世界方向，父级自转时期望位姿在坐标系里确实会变，因此会被软跟随。要完全刚性请用「本地」。
- **`limit` 盒体/圆柱**：跟着锚点坐标系转（世界轴对齐形状在父级自转下会与"相对空间"冲突）；球体与朝向无关，两种理解等价。尺寸按坐标系单位，父级缩放会一并作用。
- **`rotationMode.Local`** 在**世界模式**下沿用旧的世界结果（`parent.rotation * target.localRotation`），以免切换模式时改变老场景表现。
- **`Velocity` / `AngularVelocity`** 语义变了：现在是相对坐标系的量。需要世界速度时用 `CurrentPosition` 差分。
