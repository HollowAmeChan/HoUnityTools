# FBX 描边顶点色烘焙

在 Unity 导入 FBX 时，把几何描边方向和厚度修正写入 Mesh 的 RGBA 顶点色，供 lilToon 的顶点色描边模式使用。默认关闭，每份 FBX 单独保存开关。烘焙不依赖 Ho GeometryData、Ho 渲染管线或 Blender。

## 使用

1. 在 Project 中选中 FBX，在原生 Inspector 顶部勾选「烘焙 lilToon 描边顶点色」。无需打开 HoFBX 导入面板。
2. 开启后会显示「连接重合顶点」，默认开启，以跨 UV / 硬法线接缝生成连续方向。需要保留分离部件时关闭此项。
3. 点击 Inspector 原有的 `Apply` 保存并重新导入；`Revert` 可放弃未应用的开关改动。支持多选 FBX，其他工具的 userData 分别保留。
4. Unity 原生的 Model / Rig / Animation / Materials 页签继续照常使用，无需相邻 JSON 或目标骨架。
5. 在 lilToon 材质描边设置中，把「顶点颜色」设为 RGBA 的方向 / 宽度修正模式，即 `_OutlineVertexR2Width = 2`。材质由使用者选择，本工具不修改共享材质。

此后重导入该 FBX 会自动重新烘焙。关闭勾选并再次点击 `Apply`，会重新导入并恢复源 FBX 的原始顶点色。源 FBX 文件本身不变；开关存于其 `.meta` 的 importer userData，需与 FBX 一起纳入版本管理。

Inspector 扩展使用 Unity 的 `Editor.finishedDefaultHeaderGUI` 事件追加少量控件，沿用原生 ModelImporterEditor 及其 Apply / Revert，不替换 FBX Inspector。

## 数据与边界

- 使用 GD 的 `HoGeometryDataOutlineCorrectionBuilder` / HoTools `SOLIDIFY_RAW2SMOOTH` 算法：按边的两面角累计方向、Laplacian 判断曲率符号、角点加权求厚度因子。
- RGB 为切线空间方向编码 `direction * 0.5 + 0.5`；A 保留 GD 的 0.5 中性编码，由 lilToon 现有 RGBA 路径直接乘描边宽度。平面 A 为 0.5，和 GD 模式相同；按需要调整材质描边宽度。
- 清除导入的原顶点色通道，再写入全新的 Float32 RGBA 修正数据；不混合旧颜色，导出前无需手工删除顶点色。
- 保留位置、索引、UV、法线、蒙皮与形态键；对几乎平行于法线、退化或长度异常的切线进行正交修复，保证 lilToon 的 TBN 解码与烘焙一致。有效切线保持原值。
- 仅当位置、蒙皮权重与所有形态键帧的位置变化一致时，才连接重合顶点；不会把变形不同的接缝强行连在一起。
- 模型必须带法线与切线；在 Model Importer 中导入或计算它们，不要设为 None。仅支持三角形 / 四边形拓扑。无法烘焙时写入中性修正值 RGB=(0.5,0.5,1)、A=1 并警告，避免 Shader 误把原色作为修正方向。
- Read/Write 可以关闭；烘焙在编辑器导入阶段完成，无需运行时组件或额外 GPU buffer。
- 多个 FBX 的开关互相独立；其他工具的 importer userData 保持原样。
- 后处理版本已升为 2，启用烘焙的 FBX 会重新生成缓存；关闭功能并重新导入仍可恢复源文件的顶点色与切线。

## 验证

2026-10-27：Unity 2022.3.22f1 的 Built-in 独立工程通过 66 项检查，包含真实宇宙猫 FBX、退化切线帧修复、Float32 覆盖、失败时中性回退、Read/Write 关闭、原生 Apply/Revert，以及关闭后原色与切线恢复。
同一宇宙猫模型的“带原顶点色／无顶点色”导出对照中，主体、底部、眼睛和耳朵的解码方向在浮点误差内一致；原色引起的顶点拆分不再放大主体上的 TBN 接缝。

自动验证入口：[Tests~/FbxOutline/Run.ps1](../../Tests~/FbxOutline/Run.ps1)。脚本建立独立 Built-in 管线工程，编译完整 FBX 窗口及相关工具，按参数复制 GD 算法作为测试参照；验证工程没有安装 URP、lilToon 或 Ho 渲染包。

```powershell
& 'Tests~/FbxOutline/Run.ps1' -Fixture 'D:/Fixtures/Character.fbx'
```

`-Fixture` 可选，用于额外验证真实角色；合成 FBX 由测试生成。`-GdBuilder` 默认指向同级 `lilToon-URP-Extensions` 仓库中的 GD builder，可显式指定其他路径。`-Unity` 可指定编辑器，`-Project` 必须为新目录。

2026-10-26：Unity 6000.3.15f1 的 Built-in 独立工程通过 58 项检查，包括平面 / 立方体解析结果、GD 输出一致性、蒙皮 / 多帧形态键接缝、默认关闭、设置持久化、Read/Write 关闭时实际烘焙、缺切线时保留原顶点色、其他 userData 保留，以及原生 Inspector 的 Apply / Revert、多选、关闭后原顶点色恢复。额外角色 fixture 为 FT_ARkit_Debug.fbx。完整 FBX 窗口及相关工具针对 Unity 2021.3.18f1 的 API 编译通过；该版本的导入运行测试因本机许可证未激活而未执行。尚未做角色描边的视觉验收。
