# VMD 动作播放与骨骼绑定

组件入口：**Add Component → HoUnityTools → MMD → VMD Motion Player**。导入检查和绑定工具入口统一在 **HoUnityTools → MMD → 角色绑定与表情检查**，或直接选中 `.vmd` 使用中文导入面板。

## WTD 的配置顺序

1. 把动作 `.vmd` 拖入播放器的 **VMD 动画**。镜头 VMD 使用 `HoUnityTools/MMD/VMD Camera Player`：把组件添加到 Camera，再将镜头动画拖入 **镜头 VMD**。
2. **角色根** 拖入 `WTD-BUILD`，**骨骼搜索根** 指定 `Armature-Swimsuit`。不要把运行时合入的 `Armature-Miko` 当作主骨架。
3. 指定或更换 VMD、角色根、骨骼搜索根时，播放器会扫描当前动作实际带关键帧的骨骼、表情与 IK 开关轨道并尝试匹配。手动重扫可点击 **扫描 VMD 轨道并匹配角色**。
4. 骨骼匹配顺序是 IK 控制器目标、Humanoid Avatar 对应骨、搜索根内唯一精确名或规范化名称。表情按唯一精确/规范化名称匹配。无法判断或重名项会留空供人工指定。
5. 展开 **待处理** 修正空白项；完整映射分别收在 **骨骼轨道映射**、**表情轨道映射**、**VMD IK 开关目标** 折叠项。所有映射保存在当前播放器组件，不生成共享配置文件。
6. 点击 **检查角色表情依赖**，处理缺失或冲突。进入 Play 模式，用 **播放 / 暂停 / 从头播放** 测试。帧率默认 30，按动作作者说明设置。

这些生成和检查按钮不会自动完成 FBX 骨轴重定向。首次适配要检查姿势、脚底、膝盖、模型比例和中心轨道，不能仅凭“有映射”判定播放正确。

## Bone Bindings 填什么

每项是 **VMD 源骨骼名 → Unity 目标 Transform**。扫描器从当前 VMD 的实际轨道生成项目；自动匹配失败或需要覆盖时，再拖入主骨架对应 Transform。名称不需要相同。

| VMD 源名 | WTD 目标 |
| --- | --- |
| `左足` / `右足` | `Armature-Swimsuit/hips/upper_leg.L` / `.R` |
| `左ひざ` / `右ひざ` | 对应的 `lower_leg.L` / `.R` |
| `左足首` / `右足首` | 对应的 `foot.L` / `.R` |
| `左つま先` / `右つま先` | 对应的 `toes.L` / `.R` |
| `左足ＩＫ` / `右足ＩＫ` | 左右脚 IK 的 **Target 控制空物体** |
| `左つま先ＩＫ` / `右つま先ＩＫ` | 左右 toe IK 的 **Target 控制空物体** |

匹配优先级为：组件内已有手动映射、IK Target、Humanoid 骨映射、唯一精确名/规范化名称。扫描一次建立候选后，Inspector 重绘不会反复遍历角色层级。

`センター`、`グルーブ`、`腰`、`下半身` 可能是不同的控制层。不要把这些轨道全部写到 hips，会产生覆盖；无法可靠推断的中心层会留空。WIK/twist、眼睛或手指的专用控制同样需要按角色结构核对。

**VMD IK 开关目标** 列出 show/IK 轨道来源名，可把每条开关映射到对象。对象上有 `HoIkConstraint` 时，开关控制其 `SolveEnabled`；否则按 VMD 状态激活/停用该 GameObject。普通对象的开关目标不能是播放器自身或它的祖先，否则停用对象会连播放器一起停止。

## 表情检查和灰色预览

旧界面的检查按钮被放进 Unity 的 **Imported Object** 只读预览，所以整体是灰色的。现在统计、角色选择、生成配置与检查按钮位于正式的中文 VMD 导入 Inspector。只有尚未填写目标角色时，相应操作按钮才禁用。

可以直接拖入场景角色或 prefab；无需同时选中 VMD 和角色，也无需锁 Inspector。检查只读角色，JSON 存在 `Library/HoMmdStages/MorphAudit/`，面板也会列出缺失和冲突。规范化命中由播放器组件内的表情映射使用，无需保存到角色配置文件。

验证入口：`Tests~/MmdMotion/VmdAuthoringValidation.cs`。在安装该包的 Unity 工程内临时复制到 Editor 目录，调用 `VmdAuthoringValidation.Run()`；结果写到 `Library/HoMmdStages/vmd-authoring-validation.json`，临时测试资产自动删除。
