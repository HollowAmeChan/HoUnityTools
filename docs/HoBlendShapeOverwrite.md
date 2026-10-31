# HoBlendShapeOverwrite

随宿主物体启停的临时 BlendShape 覆写。组件菜单：`HoUnityTools / Constraints / Ho Blend Shape Overwrite`。

## 配置

- **Mesh 对象列表**：拖入一个或多个 `SkinnedMeshRenderer`；引用可以跨物体，不必放在服装节点下。
- **覆盖规则**：每项只有 BS 名称和值（Unity 权重 0–100）。规则应用于列表中所有包含该键的网格。
- **禁用时恢复**：默认“指定值”，恢复值默认 0；也可选择“启用前的值”，分别记住每个网格每个键的值。

例如，把组件挂在 `巫女服` 根节点，Mesh 列表填身体网格，添加 `HIDE_Elbow = 100`，恢复设为指定值 0。
运行时启用巫女服会写入 100；关闭巫女服、父节点或组件，以及删除组件，都会释放已写入的键。
泳装不需要额外组件。切换为泳装时必须关闭巫女服根节点；仅关闭衣服 Renderer 不代表关闭整个服装。

## 运行与预览

- `OnEnable` 立即应用，`LateUpdate`（执行顺序 31000）持续维持，`OnDisable` 立即恢复；不需要常驻 Animator。
- 只改当前 Renderer 的权重，不改 Mesh 资产、动画片段或未列出的 BS。
- 编辑模式默认不写。可在场景实例或 Prefab Mode 的 Inspector 点“开始编辑预览”，再切换服装观察。
- 编辑预览状态不序列化；常规场景/Prefab Mode 保存、资产保存、Undo/Redo、脚本重载、进入播放前会停止并恢复。保存后如需继续预览，重新点按钮。
- 离线动画采样器若在 LateUpdate 之后采样，可在采样结束调用 `EvaluateOverwrite()`。

## 边界

- 一个网格没有某规则的键时直接跳过；列表中所有网格都没有该键时给出警告。
- 优先精确匹配键名，否则沿用 HoShapeKeyResolver 的名称容错（大小写、前缀、Blender 数字后缀）。
- 重复 Mesh 引用或命中同一实际键的重复规则只采用第一条。
- 两个组件同时控制同一键时，第一个已取得控制权的组件优先；另一个告警并等待，前者释放后在下一次求值接管。建议换装时先关旧服装，再开新服装。
- 已被 HoFaceOutputOwnership 保留的面捕输出会跳过。不要让其他动画/脚本同时写同一个服装修正键；LateUpdate 不能保证压过比它更晚执行的外部写者。
- 固定恢复 0 是释放时的一次写回；禁用后组件不再持续强制 0，其他系统之后仍可以写这个键。
- 通用自定义 MonoBehaviour 需要运行环境允许执行；这不是将自定义脚本烘焙为 Avatar 动画的工具。

## 验证

`Tests~/HoBlendShapeOverwriteValidation.cs` 提供独立 Unity 工程的 Edit/Play 生命周期验证。
覆盖多个 Mesh/规则、重复引用、服装/父节点/组件开关、删除、独立原值恢复、替换 Mesh、冲突释放、丢失 Renderer、编辑保存清场和真实 LateUpdate。
