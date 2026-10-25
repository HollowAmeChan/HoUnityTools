# HoAux + Merge 回归

从全新的隔离 Unity 工程对照未合并与已合并的约束图，使用生产 `HoAuxRig` 和 `HoCombineArmatureProcessor`，不写入用户场景、FBX 或 Prefab。

```powershell
& '.\Tests~\HoAuxMerge\Run.ps1' -Project 'D:\Temp\HoAuxMergeValidation01'
```

项目路径必须不存在。测试会进入真实 Play，输出 `merge-results.json`。Unity 6000.3.15f1：600 次位置／旋转对照通过，覆盖 Root 的平移、旋转、统一缩放、Chest 运动、旧 Parent／Local Fan、Armature 点、Copy Location、Aim、重复求值，以及三种骨架布局：

- 约束空间就是源骨架根。
- 约束空间是保留在原处的 FBX 外层，实际合并根是其带变换的子级。
- 不同根绑定姿势，合并保留偏移骨骼；此组对照根运动。

合并副本会保存并重新实例化为 Prefab，再清空所有 Owner／Target／Up／Source／Direct Space 引用，调用生产 OnEnable 的路径恢复，检查绑定数量及后续运动。测试也在捕获之后改动过显示姿态，防止合并意外将显示结果重新捕获为绑定。原绑定姿态、规则和权重应保留。

完整 WTD 配置还可使用 `Tests~/ConstraintIRV3/Run.ps1` 的 `-FullConfig` 与 `-FullModel`；附加 `-FullOutfit` 传入巫女服 FBX，会验证它合入泳装主骨架的流程。两组都会在 Play 场景自动处理时合并，保留真实的 110 条规则并与未合并结果对照。本机验证每组 2120 次比较通过；既有 IR 的 8160 次测量也通过。

2026-10-26 的修复前对照使用旧版处理器运行相同数值用例，暂时跳过旧版必然不通过的路径恢复断言，600 次比较中失败 96 项；例如根运动后 Armature 点的位置差为 0.4362131。修复后失败为零。检查只比较已声明的 Unity 约束语义，不代替 Warudo／VRC 客户端验收。

使用说明和迁移接口见 [Ho CombineArmature](../../docs/guides/COMBINE_ARMATURE.md)。
