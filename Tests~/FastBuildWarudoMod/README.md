# FastBuild 组件扫描验证

在已安装 HoUnityTools 的 Unity 验证工程中运行：

1. 使用该工程生成的 Unity `.csproj` 引用表，将 `DependencyScanFixtures.cs` 编译为 `HoFastBuildScanFixtures.dll`，放入工程的临时 Assets 目录。必须保留 DLL 形式，才能覆盖同一 DLL 中多个组件的情况。
2. 将 `DependencyScanValidation.cs` 放入临时 Editor 目录。
3. 调用 `HoFastBuildDependencyScanValidation.Run(prefabAssetPath, absoluteReportPath)`；`prefabAssetPath` 为空时只运行通用夹具。真实 Prefab 用于额外检查全量挂载、MC2 宿主引用、本地源码开关和骨架烘焙源码排除。
4. 报告末尾应为 `PASS`。完成后移除临时 DLL 与 Editor 脚本。

验证会创建并清理 `Assets/HoFastBuildScanValidationTemp/Fixture.prefab`，包含原生组件、重复挂载、禁用子节点和两个同 DLL 组件。真实 Prefab 仅加载读取。测试还检查常用宿主程序集识别，以及 `Assembly-CSharp*` 和未知 DLL 不被误放行。

`StagedPrefabValidation.cs` 另覆盖真实的脚本导入与域重载：先调用 `Begin(prefabAssetPath, reportDirectory)`，待 Unity 编译完成后调用 `Finish(reportDirectory, buildArtifact)`。验证暂存的 `HoAuxRig`、`HoRotationConstraint` 可加载、Prefab 可保存重载、作者组件已消费，且全部本地运行时挂载进入产物期望表。`buildArtifact=true` 时再调用官方 UMod 构建并复核产物，输出保存在报告目录的 `output` 子目录；ExportSettings 与 Standalone Define 会恢复。成功或失败结束时，暂存源码与 Prefab 都自动移至报告目录的 `staged` 子目录保留证据，避免与下一次构建产生重复类型。
