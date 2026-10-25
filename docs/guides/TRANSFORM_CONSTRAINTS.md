# 外置 Transform 约束

五个独立组件允许把约束放在专门的空物体上，用显式引用驱动别处的 Transform。运行时和 Inspector 位于独立程序集，只依赖 Unity，不要求 VRC SDK、Animation Rigging、RigBuilder、Input System 或 URP。

## 手动添加

1. 建立一个约束宿主，例如角色下的 `Constraints` 空物体。
2. 在 `Add Component > HoUnityTools > Transform Constraints` 添加需要的组件。
3. 把被驱动骨骼或物体填入 **Target · 被驱动对象**。宿主不会自动充当 Target。
4. 在 **Sources** 添加来源 Transform 和权重。正权重自动归一化；零权重来源不参与求解。
5. 点 **保持编辑姿态并启用**，捕获基础姿态与每源偏移；或者点 **清零偏移并启用**，捕获基础姿态并使用无偏移的来源结果。
6. 进入 Play；需要编辑时查看结果，可以打开 **编辑预览（临时）**。

同一个宿主可以添加多个约束，Target 可以在角色骨架内部。禁用组件、停用宿主、关闭“启用约束”或把总体权重设为零都会停止运行时写入；停止写入不会自动恢复运行时骨骼姿态。需要恢复时使用“恢复绑定姿态”。更换 Target 或已经捕获的来源后需要重新捕获。

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

同一个 Target 的多个写者按 **Order** 从小到大执行；相同 Order 的 Position 和 Aim 可以组合。相同 Order 对同一通道重复写入会报错，即使各自只选了不同的轴，也要设置不同 Order。依赖循环、无效上游及失败的求值会阻止下游继续读取旧结果，独立的依赖组仍可运行。

默认在 **LateUpdate** 求值。也可以选择 Update、FixedUpdate 或 Manual。组件默认执行顺序为 30000；每阶段统一求值一次。自动依赖排序仅覆盖同阶段参与求值的这些 Ho 组件。Animator、Unity 原生约束、HoAux、其他 IK 和物理的顺序需要调用方安排；骨骼根跟随与其后的物理不保证只靠默认 LateUpdate 就能正确衔接。

需要明确物理前后顺序时，将相关组件设为 Manual，调用：

```csharp
using Hollow.HoUnityTools.TransformConstraints;

HoTransformConstraintScheduler.Evaluate(constraints);
```

传入同一依赖组的所有组件，才能排序整个链。`component.EvaluateNow()` 仅求值该组件。自定义 asmdef 调用时引用 `HoUnityTools.TransformConstraints.Runtime`。

## 编辑预览与 Prefab

编辑预览只写宿主与 Target 在同一场景中的非持久对象，保存被写通道的原始本地姿态。关闭预览、禁用组件、保存场景、保存 Prefab Stage、进入 Play、脚本重载或退出 Editor 时恢复。未被约束写入的通道保留编辑结果；Prefab 资产本身不参与预览。

预览期间不要手动编辑被接管的 Target 通道；先关闭预览。自定义脚本要复制、导出、通过 `PrefabUtility.SaveAsPrefabAsset` 创建资产或自行序列化对象时，应先调用 `HoTransformConstraintPreview.RestoreAll()`，再读取作者姿态，避免把临时预览结果存入产物。该 API 位于 `HoUnityTools.TransformConstraints.Editor`。

配置、Target、Sources、基础姿态和偏移按普通组件序列化，Prefab 实例内的引用正常重映射。两个捕获按钮支持组件 Undo；“求值一次”和“恢复绑定姿态”支持 Target Undo。“编辑预览”是临时显示，关闭即恢复，不会自动烘焙到绑定姿态。

## 平台与现有导入器

这些是普通 Unity C# 运行时组件。VRChat 上传仍需要使用其支持的约束组件；本模块没有添加自动转换。Warudo 构建还需随产物提供运行时程序集，本轮没有验证 Warudo 客户端加载。

本模块提供手动创作基础设施；原有 HoFollow、HoLookAt、HoAux 与 IR v3 导入后端保持各自实现。IR v3 的具体导入能力见 [约束导入契约](RIG_CONSTRAINT_IR_V3.md)。

## 验证入口

`Tests~/TransformConstraints/Run.ps1` 创建全新的独立 Unity 工程，只复制本模块、Inspector 和测试探针，不安装 VRC SDK、Animation Rigging、URP 或 Input System。它同时执行编辑器与真实 Play 模式测试，并在工程根目录输出 `results.json`。

2026-10-26：Unity 6000.3.15f1 通过独立工程验证，覆盖外置宿主、多源归一化、偏移捕获、通道保留、基线稳定性、移动父级、非均匀父级世界缩放、24 组 Aim 轴组合、退化输入、反序依赖、Up Object、循环与上游失败、同目标 Position + Aim、多个写者顺序、编辑预览恢复、显式求值 Undo／Redo、场景保存、Prefab 引用重映射，以及实际 Update／LateUpdate／FixedUpdate／Manual 求值。另用本机 Unity 2021.3.18f1 的 UnityEngine／UnityEditor 引用程序集通过两个生产程序集的 API 编译检查；该项不代表在 2021 Editor 中运行过 Play 测试。
