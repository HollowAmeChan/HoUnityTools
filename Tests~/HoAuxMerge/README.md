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

普通约束的当前合并数值回归见 `Tests~/ConstraintIRV4/Run-local.ps1`，输入为配套 Blender 合成 v4 fixture。旧 WTD v3 测试入口已删除；当时两组各 2120 次比较及 8160 次测量的结果属于历史证据，不提供旧版本导入能力。
