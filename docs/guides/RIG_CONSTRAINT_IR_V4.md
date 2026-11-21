# Rig Constraint IR v4

普通辅助骨与 HoFBX 约束导入使用三层关联数据：`sources` 保存 Blender 原始事实，`semantics` 引用源记录表达明确行为，`implementationBindings` 引用版本化 Unity 实现契约。源字段和未来扩展字段经无损 JSON DOM 保留；固定 `ConstraintConfig` 只作为现有装配器的临时执行视图，不替代源文档。

普通 Twist 是 Copy Rotation＋Stretch To，Aim 是 Damped Track＋Locked Track，Fan 和 MCH 使用相同的语义组通路。大型 `BoneTools/hoAux` 作者系统及其 Source IR、Driver 不在此导入范围。

## Blender 作者入口与兼容

普通 copyR 应从 Ho 基础辅助骨的 copyR 入口创建，生成器在 owner 骨的 HoTools 属性登记 `COPY_R` 语义组；底层语义复用 `COPY_ROTATION`。v4 不自动认领未登记的手工 Copy Rotation，它仍保存为 source，并显示 UnknownSemantic。其他已登记组合按成员类型、顺序和引用验证；损坏组不按相似名称或当前位置自动修复。

作者态 source 在导出预处理前采集，最终 FBX 的目标重映射、父级、静置矩阵与骨轴由 `bindings` overlay 保存。作者约束保留原目标；预处理生成的 MCH Child Of 明确记录 `EXPORT_GENERATED`，其最终目标仍是主骨，不能被再次重映射成 MCH 自引用。

v2/v3 仍由原读取流程处理，既有 unknown 不重新解释。v4 经同一个加载入口按选中后端投影为 schema 3 执行视图；界面和导入报告显示源版本 4。单 owner 多个组合语义无法由现有一骨一个 Aux 的兼容 DTO 表达，明确拒绝；源文档仍完整保留。

## 三后端与策略

| 后端 | 当前行为 |
| --- | --- |
| HoAux | 使用现有集中求值器；Copy、Armature、Aim 与 Parent／Twist／Fan 保持原执行边界，新增严格参数域的 Local Copy Rotation |
| Standard | Unity 原生组件及阶段 helper；非半权重旋转插值和 Armature 刚性 Parent 映射可能近似 |
| Vrc | 反射装配 SDK 原生组件；现有 shim 证据证明 API／序列化路径，不证明真实 SDK／客户端数值等价 |

`EXACT_ONLY` 不自动退化成近似。导出显式选择 `ALLOW_APPROXIMATE` 才可使用注册的近似实现；普通旧 Twist／Fan 的 `ALLOW_LEGACY` 表示明确采用既有行为契约。可用性、保真声明和验证证据分别显示。PLANNED、缺版本或未知实现 ID 不可执行；LOCKED 方案不适用时失败，不临时换实现。

全部类型目录和实现进度由 Blender 权威 `RigConstraints/registry/registry.v1.json` 生成，见 HoTools 的 `RigConstraints/docs/COVERAGE.md`。不在本仓库手工维护第二张能力表。

## Local Copy Rotation 精确范围

HoAux 的 `LOCAL → LOCAL` 和 `LOCAL_OWNER_ORIENT → LOCAL` 是不同契约。空间箭头表示 Target→Owner。当前要求 XYZ 全开、REPLACE、无反相；owner 无独立动画且以单位 poseBasis 为输入。父级和来源可动态运动。

v4 必须在作者与最终 bindingContext 中显式提供 owner／target 的 `inheritRotation=true` 和 `inheritScale=FULL`。作者 owner poseBasis、父身份与父静置参考必须保持；来源作者静置对应到明确的最终 target（可为 MCH）时也必须保持。比较包含骨架 objectMatrix，允许纯骨架坐标基底转换；不把 `fixObjectTransform` 的基底变换误认为作者静置变化。

应用姿态或 owner MCH 预处理改变局部参考而未携带补偿时，显示 Unsupported。`exportOptions.appliedConstraints=true` 时约束已经固化，禁止重复播放。剪切、退化输入或不可表达 TRS 保持现有诊断。旧 v2/v3 不通过上述新准入规则重新解释。

## 预览、保护与报告

HoFBX 窗口的候选读取、预览和应用使用 `HoConstraintIrAssetLoader`。每次生成新执行视图，避免旧装配代码的可变 DTO 污染下次预览。校验 source 指纹、registry 字节摘要、实现版本、引用、组合重复执行和参数域后才允许清理已有导入。

UnknownSemantic 与“语义已知但当前后端 Unsupported”分开显示；后者仍作为栈屏障保留。只有不支持／Object owner 的快照不能清掉上次装配。显式空快照、全静音／零影响度快照可以清理先前的受管导入，保留手工约束。装配失败使用原撤销事务回滚。

预览显示选中实现 ID／版本、fidelity、verification 和诊断。成功结果持久写入 `ProjectSettings/HoUnityTools/ConstraintIRImportReports.asset`：包含完整源 JSON、输入指纹、registry 摘要、最终选中契约和诊断。报告通过 editor ScriptableSingleton 保存，与场景／Prefab 和运行组件无引用，不带入 Warudo 或播放器构建；报告变更参与 Undo。为持久定位对象，应先保存场景。

## Registry 分发与验证

Unity 分发副本位于 `Editor/HoFbxImport/ConstraintIR/Registry/registry.v1.json`，必须与 Blender 权威文件逐字节相同（包含换行）。本地 `.gitattributes` 固定 LF。

```powershell
./Tests~/ConstraintIRV4/SyncRegistry.ps1 -Authority 'ABSOLUTE_HOTOOLS/RigConstraints/registry/registry.v1.json'
./Tests~/ConstraintIRV4/SyncRegistry.ps1 -Authority 'ABSOLUTE_HOTOOLS/RigConstraints/registry/registry.v1.json' -Check
./Tests~/ConstraintIRV4/RunReader.ps1 -Registry './Editor/HoFbxImport/ConstraintIR/Registry/registry.v1.json'
```

更细的 C# API、测试与运行参数见 [reader 验证说明](../../Tests~/ConstraintIRV4/READER.md)。`RunImport.ps1` 创建隔离 Unity 项目验证真实窗口应用、三后端结构、重导入、回滚、报告、Prefab、MCH 和空快照；`Run-local.ps1` 比较 Blender 实际采样的 Raw／MCH 数值。VRC shim 结果始终不能代替真实 SDK／客户端验证。
