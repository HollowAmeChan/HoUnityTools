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

预览后复核具体输入、目标角色与差异。修改计划中的 `reviewed`、`acceptFamilyModelChanges` 或逐条 `acceptedDifferences` 只影响应用决策；改变角色、槽位、来源或其它转换输入则需重新生成预览。接受新模型不能绕过数值／几何／动态输入 blocker。`row.modelDecisions[]` 具有 `targetFamily/code/rationale`，属于预览输入，不是应用时忽略错误的许可。限定决策包括废弃零宽／Eye 继承壳、原生透明深度／单 Pass／透明壳、零覆盖隐藏件、非 Skin 角色去 SSS、异常 Skin F0 改 .04，以及缺 Main 的发光遮罩改用明确原生采样器。未知 code、空依据或不适用的作用域会拒绝；透明壳还检查源标准 RGB／alpha blend，自定义状态不能被“原生透明”泛化吞掉。

`Apply(reviewedPlanPath, previewReportPath, newExternalBackupDirectory)` 重新核对源 `.mat/.meta`、Shader 与纹理依赖、加载值、提案及派生资源。它将提案复制进原 Material，保留源 GUID/fileID；新增资源及必要 fork 独立保存。Prefab 按依赖顺序先于场景处理，`retain-anchor` 在继承分流后也会落实明确的原材质覆盖。源场景需保持干净且关闭。

共享引用批次显式设置 `resolveReferenceOwners=true`。`HoMigrationReferenceOwners` 从真实 Prefab 源链、覆盖与导入键生成证据，折叠同一所有者下的继承意图；保留每条原始意图和冲突 retain。模型 remap 只有在完整使用范围、typed identifier、原 GUID/localID 和单一目标都能证明时执行。`retainMixedModelAnchors=true` 是另一项明确选择：混合 FBX 默认保留原 GUID 的 anchor，报告逐条列出未完成的模型内分族及 `NotCreated` wrapper，不能称其已完成。`RunReferenceOwners` 使用 PLAN/REPORT 环境变量只读导出这份证据。默认材料批次不解析 owner，也不接受 model-remap-review。

`createModelWrappers=true` 要求上述两个 owner flag 都开启。Preview 冻结 wrapper recipes，按根相对路径／组件 index／槽绑定全部原模型意图和 anchor，保存 mesh／rig／组件结构签名。Apply 新建与原模型相连的 Prefab Variant；不修改原模型/meta，不自动把现有场景换成 wrapper。模型独有的非 anchor 角色也会创建真实 fork。新 prefab/meta/祖先目录纳入同一个 journal；`after-model-wrappers` 故障回滚与跨进程 Restore 均清理它们。原模型默认例外和新 wrapper 的完成数量分别报告。已删除资产的 GUID 缓存不算占用，只拒绝真正存在的 asset/meta。

Apply 在写入前验证完整 owner 证据和所有待重导入／依赖 importer 的 dirty 状态；只备份真正写入的场景／Prefab／材质和 remap `.meta`，上游依赖单独记录哈希。Apply 后核对所有展开意图，并单独输出模型默认例外数量。一个 fork 路径只能对应一个 source/family；owner 模式的一个 source/family 也只能对应一个明确 fork 路径。目标直接使用正在 remap 模型的纹理 subasset 时先拒绝，要求提取纹理；不放宽纹理哈希来掩盖重导入依赖变化。

CLI `RunApply` 使用上述计划／报告变量及 `HO_MATERIAL_MIGRATION_BACKUP`。每行与每个槽位均须已复核；默认生成的计划都未复核。

`Restore(backupDirectory)` 或 CLI `RunRestore` 使用 `HO_MATERIAL_MIGRATION_BACKUP`。它验证成功事务后的文件哈希，保护后续用户编辑；恢复原始材质、场景、Prefab 和 meta 的字节，并清理确实由事务新建的派生资产、fork 与目录。受控异常会立即回滚，并在外部 journal 记录 `RolledBack`。如进程在 `Prepared/Applied` 中途终止，保留 journal／原始备份做恢复审查；当前 `Restore` 只接受 `Verified` 事务，不宣称自动合并中断后的未知编辑。

## 纹理与材质边界

静态 Coverage/HairBase/HSVG 使用旧主图采样域，保留主图 ST；发光与 SSS 烘焙输出原始 UV0 域。每张派生纹理附带来源／参数／输出身份记录；复制到最终目录后，Recipe 的输出 GUID 与哈希会重绑到最终资产，原始预览记录保存在外部事务快照中。

派生图默认使用 Production：按全部 mip 的 GPU 误差及覆盖分类选择 R8／RGBA32／Half／Float，并移除生产图的 CPU Readable 副本；不默认重复 BC7 压缩。显式 `HO_MIGRATION_TEXTURE_FORMAT=ExactPreview` 保留可读 RFloat／RGBAFloat 数值基准。Recipe v2 记录格式、编码、精度和拒绝候选。完整容差及连续采样限制见 [格式策略](../../docs/guides/material-migration-texture-formats.md)。非线性重采样／mip 与原多图运行时过滤的差异仍保留。工具不修改源 importer 的 Readable。

Skin/Eye 的非金属判定可对实际绑定金属图的全部导入 mip 做严格 GPU 恒零证明；标量非零但图 R 恒零时保留非金属家族。近零、读取失败、缺 mip 或任何非零都不放行。隐藏决策还明确修正旧 `_Invisible` 未被 RT 消费的幽灵遮挡，不能声称旧 RT 数值相等。

`OutlineCoverage` 单独烘焙旧 OutlineTex alpha × OutlineColor.a，再按 Outline UV 链接 AlphaMask；输出 raw UV0，目标 CoverageMap identity ST 与 OutlineColor.a=1，不乘主图 alpha。`OutlineColor` 只烘焙旧描边 HSVG RGB，保留旧 OutlineTex ST；alpha 由独立覆盖负责。发光默认仍拒绝缺 Main 的遮罩采样器；明确原生决策才改用 mask 自己的 importer sampler，并且只有可证明白色主表面的 MainStrength 能恒等省略。

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
- `HoMigrationTextureFormatChecks.Run/Reload`：Production 的格式／实际采样误差、HDR、覆盖及跨进程保存。原公式 converter/Baker fixture 自己选择 ExactPreview。
- `HoConverterOutlineChecks.Run`、`HoMigrationTextureAnalysisChecks.Run`、`HoConverterHiddenCoverageChecks.Run`：限定模型决策、严格全 mip 恒零和真实隐藏覆盖。后者读回实际 Forward/Shadow/Depth/Ho/Meta 与空 Renderer 的一致性。

原生 owner 与主事务 fixture 使用独立的 **无旧材质包** 工程。在同一准备命令追加 `--native-owners`，快照完整 HoUnityTools 及真实 PMX importer，仅复制 owner/wrapper tests，不安装 lilToon。执行 `HoMigrationReferenceOwnerChecks.Run` / `HoMigrationOwnerTransactionChecks.Run`；设置全新 `HO_REFERENCE_FIXTURE_SUFFIX` 和工程外 `HO_MIGRATION_TEST_OUTPUT`。事务测试使用很小的同名合成 source shader，只测试事务身份，不是旧 Shader 的渲染兼容测试。不要把它与真实 lilToon 材质测试混在一个宿主；两种准备模式已做隔离。

此原生 profile 还包括 `HoMigrationWrapperChecks.Create/ReloadAndRestore` 与 `HoMigrationWrapperTransactionChecks.Apply/Restore`，后一个 Restore 由独立 Editor 进程执行。两份合成 shader 事务夹具会主动拒绝安装了真实旧材质包的宿主。

这些夹具会创建测试资产，只用于独立工程；生产调用仅走上述显式计划入口。
