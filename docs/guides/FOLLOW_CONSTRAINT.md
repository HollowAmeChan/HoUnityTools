# 跟随约束

`HoFollowConstraint` 仅在运行时求解。添加组件后指定 `Target`，进入 Play Mode 即可观察结果。组件启用时以当前 Transform 的本地位姿为基准。已有场景和 Prefab 中保存的旧版初始位姿仍可读取。

## 跟随参数

- `Position Follow` 和 `Rotation Follow` 分别控制从基准位姿到目标位姿的混合比例。
- `Response` 控制追随速度。位置用 `SmoothDamp`，旋转用指数插值。`Overshoot` 增强位置惯性，并加快旋转追随。
- `Lock X/Y/Z` 控制各位置轴向基准回锁的比例。`Keep Horizon` 保留基准的俯仰与滚转，只取目标的偏航。轴向以 `Space` 为准。
- `Position Offset` 沿目标本地轴偏移，随目标缩放。`Rotation Offset` 在求解结果上叠加欧拉角。

## 坐标系

| Space | 解算方式 | 用途 |
| --- | --- | --- |
| **Local**（默认） | 在父级本地空间跟随。没有父级时使用世界空间。 | 跟随点和目标共用会移动或旋转的父级时，父级刚性运动直接传递。 |
| **World** | 在世界空间跟随。 | 父级运动也需要有拖尾时使用。 |

`ResetState()` 会重置运行中的阻尼状态。`SnapToTarget()` 直接写入当前设置算出的目标位姿。

## 更新时机

`Update` 是默认选项。供 Animation Rigging 等同帧消费者读取时，在 **Script Execution Order** 里把目标驱动排在 Follow 之前，把读取 Follow 位姿的脚本排在 Follow 之后。例如目标驱动更早运行时，可把 `HoFollowConstraint` 设为 `-100`，早于默认顺序的 `RigBuilder.Update()`。细节见 [Animator IK 与更新时机](../pitfalls/UNITY_IK_AND_TIMING.md)。

`LateUpdate` 适合需要读取当帧动画结果的跟随点。旧资产的 `FixedUpdate` 和 `Manual` 枚举值会按 `LateUpdate` 执行。

## 旧版设置

Follow 已移除编辑态求解、初始位姿工具、速度上限、形状限位、额外旋转模式和调试绘制。若要让旧组件使用当前 Transform 作为基准，可在 Unity 的 Debug Inspector 里清除 `Has Initial Transform`。

Floating 固定在 `LateUpdate`，用正弦和 Perlin 噪声驱动位置与旋转。位置的两个效果共用 `Oscillation Space`。旧版的静态偏移可直接设置在基准 Transform 上。缩放、曲线波形和独立噪声空间已移除。
