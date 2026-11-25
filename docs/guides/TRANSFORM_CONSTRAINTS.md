# 外置 Transform 约束

五个独立组件允许把约束放在专门的空物体上，用显式引用驱动别处的 Transform。运行时和 Inspector 位于独立程序集，只依赖 Unity，不要求 VRC SDK、Animation Rigging、RigBuilder、Input System 或 URP。

HoPosition、HoRotation、HoScale、HoParent 和 HoAim 只在 Play Mode 或播放器中写入 Transform。编辑态可以配置、绑定和保存约束。

## 手动添加

1. 建立一个约束宿主，例如角色下的 `Constraints` 空物体。
2. 在 `Add Component > HoUnityTools > Transform Constraints` 添加需要的组件。
3. 在 **被驱动物体** 指定要控制的骨骼或物体。组件挂在被驱动物体上时，可以点 **用当前物体**。
4. 在 **来源物体 / 权重** 添加来源。使用一个来源时保持权重 1 即可。
5. 点 **保留偏移并启用** 保留当前相对姿态，或点 **贴合来源并启用** 直接跟随来源。
6. 进入 Play 查看结果。更新阶段、轴和重新绑定在 **高级设置** 中。

同一个宿主可以添加多个约束，目标可以在角色骨架内部。禁用组件、停用宿主、关闭 **运行时启用** 或把总体权重设为零，都能停止运行时求解。需要恢复姿态时，可在播放中使用 **恢复绑定姿态**。更换目标或已经绑定的来源后需要重新绑定。

## 组件语义

| 组件 | 输出 | 来源与偏移 |
| --- | --- | --- |
| `HoPositionConstraint` | 位置 | 对来源本地偏移点加权；World 空间使用 `source.TransformPoint(offset)` |
| `HoRotationConstraint` | 旋转 | 来源四元数乘每源旋转偏移，再加权 |
| `HoScaleConstraint` | 缩放 | 来源缩放乘每源比例，再加权 |
| `HoParentConstraint` | 位置、旋转 | 每源独立捕获相对偏移；不写 Target 缩放 |
| `HoAimConstraint` | 旋转 | 朝向来源偏移点的加权位置，可设置有符号 Aim / Up 轴和独立 Up Object |

位置、旋转、缩放互不替代：只装 Position 就不会写旋转或缩放。Parent 的位置偏移属于来源坐标系，因此会随来源缩放移动；这不代表 Target 自身继承来源缩放。

两来源旋转使用按权重归一化的 `Quaternion.Slerp`；三来源及以上按 Sources 列表顺序递进 Slerp，换序可能改变结果。Parent 是 TRS 中的位置／旋转跟随，不是 Blender Armature 约束的仿射矩阵混合或精确 LBS。

## 空间、基线和权重

- **World**：读取来源世界变换，求解后转换为 Target 本地变换。Scale 读取 Unity `lossyScale`，沿用 Unity 的有损缩放定义；不会重建剪切矩阵。Target 或父级退化为零缩放时会拒绝世界缩放求解，可使用 Local。
- **Local**：直接读取各来源相对于各自父级的本地 TRS 数值，结果也写入 Target 本地坐标。不同父级之间不会额外转换坐标系。
- **RestPose**：总体权重从捕获的本地基础姿态混合；基础姿态会跟随当前父级移动，反复求值不会逐帧追向来源。
- **Animated**：从本阶段开始时 Target 的本地输入混合。需要 Animator 或其他上游每帧写回该输入，否则上帧约束结果会成为下帧输入，产生反馈。

位置、缩放轴遮罩使用所选空间的轴。部分旋转轴采用 Unity Euler 分解，有 Euler 跳变和万向节锁限制；全轴旋转直接使用四元数。未选轴沿用基础输入。同一个 Target 的后续约束会从本次求值中较早约束已写入的通道继续混合。

Aim 使用世界几何，支持 Scene Up、世界向量、Up Object 位置、Up Object 旋转四种上方向。来源可以有多个，也可以只有一个；不要求三点骨或任何 HoAux 标签。目标重合、Up 平行等退化情况保留当前旋转，并显示错误。

## 外置宿主与更新顺序

求值器根据 Target、Sources、Target 父级和 Aim Up Object 建立依赖；来源位于另一个被驱动对象的子级时，也会等待其祖先约束。宿主的层级位置和注册顺序不决定结果。

同一个被驱动物体可组合位置、旋转和缩放组件。朝向约束同顺序时会读取位置约束更新后的位置。位置、旋转、缩放是三个独立通道。控制相同通道的组件应使用相同更新阶段和不同 **执行顺序**。后执行的组件从前一个结果继续混合，强度为 1 时覆盖相同轴。即使各自只选了不同的轴，也需要指定顺序。相同顺序的重叠通道会报告冲突，只停止冲突组件和依赖它们的求解。独立通道继续更新。

默认在 **LateUpdate** 求值。也可以选择 Update、FixedUpdate 或 Manual。组件默认执行顺序为 30000；每阶段统一求值一次。自动依赖排序仅覆盖同阶段参与求值的这些 Ho 组件。Animator、Unity 原生约束、HoAux、其他 IK 和物理的顺序需要调用方安排；骨骼根跟随与其后的物理不保证只靠默认 LateUpdate 就能正确衔接。

需要明确物理前后顺序时，将相关组件设为 Manual，调用：

```csharp
using Hollow.HoUnityTools.TransformConstraints;

HoTransformConstraintScheduler.Evaluate(constraints);
```

传入同一依赖组的所有组件，才能排序整个链。`component.EvaluateNow()` 仅求值该组件。两个入口都只写运行中的目标。自定义 asmdef 调用时引用 `HoUnityTools.TransformConstraints.Runtime`。

## 与其他约束一起使用

组合时以实际写入的 Transform 或形态键为准。组件挂在同一个宿主上并不会自动共享求解顺序。

| 组件 | 实际输出 | 组合方式 |
| --- | --- | --- |
| 外置位置、旋转、缩放、Parent、Aim | 显式指定的被驱动物体 | 同阶段由外置约束调度器排序。Parent 占用位置和旋转，可与 Scale 一起使用。 |
| Follow、Floating | 各自所在物体的位置和旋转。Floating 只驱动有幅度的通道。 | 跟随或硬附着放在父级，Floating 放子级。这样浮动在跟随结果上叠加。 |
| Pendulum | 绑定里的 Transform 位置或旋转，也可绑定其他输出。 | 检查绑定实际指向的对象。需要在已有位姿上加摆动时，驱动专用子级。 |
| HoAux、Ho IK、LookAt 骨骼模式 | HoAux 规则绑定的骨骼、IK 链、Animator 注视和眼骨。 | HoAux 内部规则由自己的求解器组合。IK 目标和 Pole 可以由约束驱动，写入阶段要早于 IK 读取。骨骼的同一通道交给一个求解器。 |
| Blink、Spring、LookAt 形态键模式、BlendShape 修正或覆盖 | 指定网格上的形态键。 | 与 Transform 约束可并存。多个组件若指向同一网格上的同一个键，各组件分别写回，应把该键归给明确的驱动。 |

外置约束 Inspector 会提示同目标的外置约束冲突。Animator、Unity 内置约束、HoAux、IK 和其他独立组件的输出由各自的系统求解。

## 编辑态绑定与 Prefab

绑定按钮读取当前姿态，只保存组件上的配置，支持撤销。目标、来源、基础姿态和偏移按普通组件序列化，Prefab 实例内的引用会正常重映射。**应用一次** 和 **恢复绑定姿态** 只在播放时可用。可直接保存或导出编辑态的姿态。

## 平台与现有导入器

这些是普通 Unity C# 运行时组件。VRChat 上传仍需要使用其支持的约束组件；本模块没有添加自动转换。Warudo 构建还需随产物提供运行时程序集，本轮没有验证 Warudo 客户端加载。

本模块提供手动创作基础设施；原有 HoFollow、HoLookAt、HoAux 与 IR v4 导入后端保持各自实现。IR v4 的具体导入能力见 [约束导入契约](RIG_CONSTRAINT_IR_V4.md)。

## 验证入口

`Tests~/TransformConstraints/Run.ps1` 创建全新的独立 Unity 工程，只复制本模块、Inspector 和测试探针，不安装 VRC SDK、Animation Rigging、URP 或 Input System。它同时执行编辑器与真实 Play 模式测试，并在工程根目录输出 `results.json`。

测试覆盖五种约束在编辑态的绑定与写入限制、Prefab 序列化及实例引用重映射，以及在 Play Mode 下的偏移捕获、多来源、通道组合、依赖与循环检测、退化输入和各更新阶段。
