# 骨架装配调查（2026-10-24）

## Modular Avatar

使用方法是把 `Merge Armature` 挂在衣服骨架根，将 `Merge Target` 指到本体的对应根。Setup Outfit 会寻找衣服骨架并添加相关组件。源根本身映射到指定目标；后代根据对应父节点的直接子节点名字匹配，支持移除前后缀。

主分支的 `ModularAvatarMergeArmature.MapBone` / `FindCorrespondingBone` / `GetBonesMapping` 展示了这种父级相对匹配；并不是把所有同名骨骼放进全局字典后直接替换。

`MergeArmatureHook` 的流程比简单删重复骨骼复杂：处理嵌套装配依赖，递归移动源节点，区分附加组件和物理链，登记候选合并骨骼，修正 Transform 动画及 Active 动画。它还保留对原对象身份与开关语义的处理；后续骨骼数据库和引用处理负责完成替换与裁剪。不能把 MA 的保证概括为“只改 bones 数组”，也不能把其中一个递归函数直接拿出来当成完整功能。

我们采用独立实现：提供父级相对匹配、绑定偏移保留、蒙皮/组件引用重映射、专用分支接入；不复制 MA 的 NDMF 动画服务、PhysBone 特殊逻辑或 Active 传播功能。由“禁止动画绑定衣服骨架子树”的作者约定缩小支持范围，遇到已知不支持的输入直接拒绝。

来源：

- [组件摆放与合并行为](https://modular-avatar.nadena.dev/docs/reference/merge-armature)
- [Setup Outfit 教程](https://modular-avatar.nadena.dev/docs/tutorials/clothing)
- [配置组件及骨骼匹配实现](https://github.com/bdunderscore/modular-avatar/blob/main/Runtime/ModularAvatarMergeArmature.cs)
- [构建时递归装配实现](https://github.com/bdunderscore/modular-avatar/blob/main/Editor/MergeArmatureHook.cs)
- [NDMF ObjectPathRemapper](https://ndmf.nadena.dev/api/nadena.dev.ndmf.animator.ObjectPathRemapper.html)

## 游戏中的模块化角色

Epic 官方将常见实现分为三类：Leader Pose 让各部件复用主部件姿态；Copy Pose 让各部件保留自己的骨架/动画求值并复制对应骨骼；Skeletal Mesh Merge 合并渲染部件。三者的初始化、动画、渲染和物理成本不同，不能把“合骨架”与“合网格减少 draw call”混为一谈。Copy Pose 明确需要保证姿态源先更新，否则可能读到上一帧。

对本工具，直接改写 Unity SkinnedMeshRenderer 的骨骼引用在概念上接近共用姿态数据；原生约束模式保留独立骨架，更接近姿态跟随。这里只是设计类比，不宣称与 Unreal 的实现或性能完全相同，也不套用其 Morph Target 限制。

Unity 的 Mesh.bindposes 与 SkinnedMeshRenderer.bones 按同一索引对应。因此重新装配必须保留数组索引，而不能拿目标骨架遍历结果覆盖 bones 数组。第一版曾要求对应骨骼矩阵一致；2026-10-25 已移除这个不必要的限制，改用偏移骨骼和静态绑定参考帧，在保留原权重与 bindposes 的同时支持不同姿势。

## 本机 MA 核对与偏移保留（2026-10-25）

核对了本机 `D:/UnityVrcVCC_Projects/VrcWantToDie/Packages/nadena.dev.modular-avatar` 的 1.11.7 版本。`Editor/MergeArmatureHook.cs` 的递归合并使用 `SetParent(..., true)` 保留世界姿势；`Editor/MeshRetargeter.cs` 的 `RetargetBones` 按 `目标骨骼.worldToLocalMatrix × 原骨骼.localToWorldMatrix × 原bindpose` 修正绑定矩阵。MA 因此不要求同名骨骼的姿势完全相同。FAQ 也说明手工调整衣服骨骼后，合并会保留这些调整：[官方 FAQ](https://modular-avatar.nadena.dev/docs/faq)。

Ho CombineArmature 选择不生成新网格资产：同姿势节点直接折叠；不同姿势节点保留原骨骼，通过参考帧挂到对应本体骨骼下。参考帧满足 `目标当前世界变换 × 目标绑定世界变换逆 × 源原父级绑定世界变换`，原骨骼继续使用自己的局部 TRS。若参考帧包含剪切，则将捕获的 TRS 路径分解为若干 Transform 表达，不做有损的单节点分解。

这是绑定框架的保留，不是衣服网格自动适配到任意角色：外形、权重和专有物理逻辑仍取决于资产本身。该实现不逐段复制 MA 源码，也不引入 MA/NDMF/VRC 程序集。

## 本轮约束验证的限制

初版曾尝试 ParentConstraint + ScaleConstraint 全姿态跟随。在 Unity 6000.3.15f1 的隔离测试中，角色共同根缩放 1.3、目标 Chest 同时播放位置/旋转/均匀缩放动画时，出现了约 0.00875 的世界缩放向量误差。单独烘焙后再进入 Play 也能观察到，尚未定位根因，不能据此概括所有 Unity ScaleConstraint 的行为。

本版不发布这一缩放后端，而是明确提供 ParentConstraint 的位置/旋转跟随；本体骨架缩放动画会被检查器拒绝。直接合并没有姿态复制阶段，测试仍保留目标骨骼动态缩放场景。不同模式使用独立的角色和控制器测试资产，避免连续 Play 的 Write Defaults 或原生控制器缓存干扰测试输入。

来源：

- [Epic：Working with Modular Characters](https://dev.epicgames.com/documentation/en-us/unreal-engine/working-with-modular-characters-in-unreal-engine)
- [Unity：Mesh.bindposes](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Mesh-bindposes.html)
- [Unity：ParentConstraint](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Animations.ParentConstraint.html)
- [Unity：ScaleConstraint](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Animations.ScaleConstraint.html)
- [Unity：场景处理回调](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityeditor/build/iprocessscenewithreport/onprocessscene)
