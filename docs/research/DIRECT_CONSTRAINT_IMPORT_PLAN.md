# 三类常用约束：三后端接线方案

2026-10-25，设计状态，生产代码尚未接线。契约正本位于 HoTools 的 `Exporter/DIRECT_CONSTRAINT_IMPORT_CONTRACT.md`。

## 分类与后端能力分开

Blender COPY_LOCATION、COPY_ROTATION、ARMATURE 拟作为 IR v3 的 `DIRECT_CONSTRAINT`，进入 knownConstraints；无需 Aux 标记或先识别整张三点图。MCH／显式 Aux 关系优先认领，避免与通用条目重复。

未标记 DAMPED_TRACK／LOCKED_TRACK 继续 unknown。未来三点生成工具用持久 Ho 骨属性或明确的生成标签描述组合关系，再为 frame 增加识别器。

known 只代表原始类型语义已认识。计划层另区分 Exact、NativeApproximation、Unsupported、Invalid、Unknown，静音／零影响另标为不执行。不能把空间不支持、缺目标或 Armature 的后端差异统一叫 unknown。

## 后端映射

| 原类型 | Standard | HoAux | VRC |
| --- | --- | --- | --- |
| Copy Location | PositionConstraint | CopyLocation 操作 | VRCPositionConstraint |
| Copy Rotation | RotationConstraint | CopyRotation 操作 | VRCRotationConstraint |
| Armature | 多源 ParentConstraint 刚性映射，检查等价子集 | 独立 Armature 加权矩阵操作 | 多源 VRCParentConstraint 刚性映射，检查等价子集 |

Copy 首批考虑全轴 WORLD→WORLD、无反相、无 tail、Replace／零偏移的明确子集。保留其他参数，尚未实现的设置在计划层解释原因。复制约束不能复用现有始终烘保持偏移的装配逻辑，否则会改变 Replace 的含义。

同一 owner 的多条复制约束保留 stackIndex。Standard／VRC 用阶段 helper 持有上一条结果，仅对已测的等价组合折叠；Ho 在虚拟姿态缓冲区顺序求值。不同 FBX 骨基底需要方向补偿，不直接复制不等价的本地数值。

Armature 在线性混合且 Preserve Volume／Envelope 关闭时，原始矩阵运算是 `Σ a_i M_i inverse(B_i)` 乘以约束前 owner 矩阵；Unity／VRC Parent 主要是刚性位置旋转语义。它们在多源缩放／剪切以及动态 owner 前序状态下不自动等价。NativeApproximation 必须明示；严格等价要求下不满足的条目标 Unsupported。

Ho 后端保留原始仿射运算，在最终写入 Transform 前判断结果是否可由层级表达。不能把任意剪切矩阵简单分解为 TRS 后宣称精确。上一轮 WTD 原型证明的是参考点位置与下游组合，不是完整通用 Armature 骨矩阵。

## 具体修改位置

1. `ConstraintConfig`：补 references.targets 的目标身份／权重、Armature 选项和最终 FBX 绑定上下文。reader 接受 v2/v3，v3 才认 DIRECT_CONSTRAINT。
2. `HoFbxImportProcessingWindow`：扩展关系验证和通用计划项、后端能力预览、栈序与依赖诊断。不能再用 Unknown 表示已知类型的后端不支持。
3. Standard 装配：新增 Position、零偏移 Copy Rotation、顺序 helper 和明确的多父级绑定。精确与目标原生刚性语义分开展示。
4. `HoAuxRig`：新增 CopyLocation、CopyRotation、Armature 操作，保留既有枚举序列化值；解决输入姿态与 RestoreOwnersToBindPose 的边界，避免通用 owner 动画被抹掉或输出逐帧反馈。
5. `HoVrcConstraintBridge`：新增 Position 类型、逐源权重和 offset API；继续集中挂点＋TargetTransform。新 helper 纳入导入标记、安全清理、重导入和 Prefab override。

## WTD 的阶段性结果

新分类应是 9 条 direct known、4 条 unknown：Armature×4、Copy Location×3、Copy Rotation×2，以及 Damped／Locked×4。

这不是整套效果已可完整自动导入。DIR 的 unknown 旋转需要未来 frame 标记解析器，或用户在 Unity 手工补对应 Aim。计划应提示通道依赖缺失，而不是因 9 条能认领就显示完整成功。

同一 owner 存在未知栈屏障时，不能把已知项跨过它重排或折叠。常见独立条目可以先导入；相关未实现上下文须明确报告。

## 验证门槛

Copy 的 0／0.3／0.5／1 influence、初始偏移、动态父级、不同轴基底、两条以上栈序；Armature 的单源／多源、非归一权重、动态 pre-state、缩放／剪切反例；真实 HoFBX 默认姿态／MCH、重导入、换后端、撤销、序列化和安全清理。

VRC 的本机 SDK／运行时尚未测，不能以 Unity 原生结果替代。详细方案与 IR v3 示例见 HoTools 正本。
