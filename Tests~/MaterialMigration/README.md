# 外部材质迁移复核

`Editor/MaterialMigration` 是集中式 Editor 工具，不是材质 UPM 的共享 UI 或运行时依赖。旧材质包仅作为转换输入存在；生成的新材质分别使用各族公开同步 API。

## 入口

`HoMaterialMigration.Preview(planPath, newAssetsFolder, newExternalReportPath)` 生成独立材质与派生纹理，不改变源资产。它输出 JSON 完整输入快照和相邻 Markdown 差异清单。计划由 HoShader 仓库的 `docs/material-platform/migration/build_preview_plan.py` 生成。

CLI 执行 `Hollow.HoUnityTools.Editor.MaterialMigration.HoMaterialMigration.RunPreview`，显式提供：

```powershell
$env:HO_MATERIAL_MIGRATION_PLAN='<计划 JSON>'
$env:HO_MATERIAL_MIGRATION_PREVIEW_ROOT='Assets/HoMaterialReview/<新批次>'
$env:HO_MATERIAL_MIGRATION_REPORT='<工程外新报告 JSON>'
```

预览后复核具体输入、目标角色与差异。修改计划中的 `reviewed`、`acceptFamilyModelChanges` 或逐条 `acceptedDifferences` 只影响应用决策；改变角色、槽位、来源或其它转换输入则需重新生成预览。接受新模型不能绕过数值／几何／动态输入 blocker。

`Apply(reviewedPlanPath, previewReportPath, newExternalBackupDirectory)` 重新核对源 `.mat/.meta`、Shader 与纹理依赖、加载值、提案及派生资源。它将提案复制进原 Material，保留源 GUID/fileID；新增资源及必要 fork 独立保存。Prefab 按依赖顺序先于场景处理，`retain-anchor` 在继承分流后也会落实明确的原材质覆盖。源场景需保持干净且关闭，导入模型所有者动作仍显式拒绝，不能通过忽略 `model-remap-review` 来批量应用。

CLI `RunApply` 使用上述计划／报告变量及 `HO_MATERIAL_MIGRATION_BACKUP`。每行与每个槽位均须已复核；默认生成的计划都未复核。

`Restore(backupDirectory)` 或 CLI `RunRestore` 使用 `HO_MATERIAL_MIGRATION_BACKUP`。它验证成功事务后的文件哈希，保护后续用户编辑；恢复原始材质、场景、Prefab 和 meta 的字节，并清理确实由事务新建的派生资产、fork 与目录。受控异常会立即回滚，并在外部 journal 记录 `RolledBack`。如进程在 `Prepared/Applied` 中途终止，保留 journal／原始备份做恢复审查；当前 `Restore` 只接受 `Verified` 事务，不宣称自动合并中断后的未知编辑。

## 纹理与材质边界

静态 Coverage/HairBase/HSVG 使用旧主图采样域，保留主图 ST；发光与 SSS 烘焙输出原始 UV0 域。每张派生纹理附带来源／参数／输出身份记录；复制到最终目录后，Recipe 的输出 GUID 与哈希会重绑到最终资产，原始预览记录保存在外部事务快照中。

当前派生图为未压缩线性 RFloat／RGBAFloat `.asset`，用于核对数值和 HDR。它们的存储、运行时格式与后续纹理压缩应在生产应用批次中明确处理；不要将此预览格式视为已完成性能优化。非线性重采样／mip 与原多图运行时过滤的差异写入每份 Recipe。工具不修改源 importer 的 Readable。

当前模型不等于旧 toon 材质：SSS、MatCap/rim/backlight、透明、描边和环境照明都有逐组差异。源 Shader 中已退役的保存项不恢复。动画／脚本／MPB 的静态证明属于原生引用与代码审查，单个 Material 的 bake 不能代替它；语义／Tension 关键字与控制值不一致会阻断。

旧 lilToon 的可见 Forward 会执行主图 HSVG，但其旧 HoSurfaceBuffer 不走这段校色。新材质的 HoSurface/RT 采用与可见表面一致的校色后底色；该 GI 输入变化已由真实 GPU 对照量化，写入 `HSVG_NUMERIC_CONSUMERS` 与相关发光差异，不能把旧 SB 相等当成可见主色正确的标准。

## 隔离复现

创建全新的验证目录：

```powershell
python Tools~/MaterialMigration/prepare-validation.py --output D:/Temp/HoMaterialMigrationValidation --shader-root D:/Unity_Fork/HoShader --legacy-root D:/Unity_Fork/lilToon/Assets/lilToon --pipeline-root D:/Unity_Fork/lilToon-URP-Extensions --urp-root D:/Unity_Fork/HoUrp17.3.0 --config-root D:/Unity_Fork/HoUrpConfig17.0.3
```

工具只快照迁移 Editor 子集，避免把 HoUnityTools 的无关项目工具作为复现前提；它保留实际材质包及当前旧 Shader 输入。先通过 Unity CLI 执行 `HoMaterialMigrationTransactionChecks.Setup`，退出后在新的带图形设备进程执行测试：

- `HoMaterialMigrationTransactionChecks.Run`，指定工程外 `HO_MIGRATION_TEST_OUTPUT`：检查真实保存／重载、原 GUID、Prefab/场景分流、保留 anchor、源图变更拒绝、Recipe 最终身份、后续编辑保护、两种写入后失败以及完整 Assets 字节／目录恢复。
- `HoConverterChecks.Run`，设置 `HO_MIGRATION_CONVERTER_FIXTURE=1`：公开材质实现上的字段映射／状态与来源不变。该测试在快照包的 Editor/Validation 下，与 internal converter 同程序集。
- `HoConverterSurfaceChecks.Run`：同一 Renderer 的旧／新 HoSurfaceBuffer 和旧 Forward GPU 见证，验证颜色声明、HSVG、RGBA 发光及 MainStrength；仅在独立工程运行，具体入口条件见源码。
- `HoMigrationBakerChecks.Run`，设置 `HO_BAKER_FIXTURE=1`、全新 `HO_BAKER_FIXTURE_SUFFIX`：验证不可读压缩 PNG、TGA、HDR、alpha/ST/HSVG/发光/SSS 公式、资源复用及拒绝覆盖。`Edges` 使用 suffix2 的已生成 fixture；详细要求见测试源码。

这些夹具会创建测试资产，只用于独立工程；生产调用仅走上述显式计划入口。
