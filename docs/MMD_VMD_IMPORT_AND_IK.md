# MMD VMD 动作与 Ho IK 约束

## 现在的边界

MMD 动作不是普通 FK 动画。VMD 可能同时包含骨骼帧、形态键帧、镜头帧、灯光帧、自阴影帧以及 `show/IK` 开关帧；动作作者还可能只针对某个模型的骨骼布局制作。MMD 运行顺序包含追加/继承、IK、物理和形态键，不能只把 VMD 骨骼曲线直接套到 Unity Transform 上。

当前 `HoUnityTools` 的 PMX importer 主要服务静态场景。角色 PMX 不作为当前最终运行时目标，因此不要求把角色 PMX 的完整蒙皮、物理和 Humanoid Avatar 流程接进 Ho 管线。动作适配使用 PMX 角色作为元数据参考，真正播放目标可以是已经进入 Unity 的 FBX/GLTF/其他角色骨架。动画导入研究以 UMT 为基线：它具备 PMX 骨骼/形态键、MMD transform order、CCD IK、VMD bone/morph/IK toggle、VMD camera 两节点 rig 和可选物理烘焙。HoAux 继续处理 Unity 侧的 twist、fan、体积保持等辅助形变。

HoUnityTools 现在也提供 `HoVmdAnimation` 与 `.vmd` ScriptedImporter。它先把 VMD 作为独立数据资产导入，保留原始模型名和各类轨道；这一步不猜模型映射、不自动改骨骼，也不和静态 `.pmx` importer 抢扩展名。后续播放器/适配器读取该资产并选择目标骨架。

`HoVmdMotionPlayer` 提供第一版运行时适配：按骨骼名写入 FK 位置/旋转，按 morph 名称写入 `SkinnedMeshRenderer` blend shape，按 `HoIkConstraint.IkName` 应用 show/IK 开关。`HoVmdCameraPlayer` 使用 target + Camera 两节点读取 VMD 镜头的目标位置、距离、旋转、FOV 和透视开关。复杂 MMD transform order、物理烘焙和模型专用重命名仍应接 UMT solver 或后续 Ho 适配层。

`HoVmdMotionPlayer` 会把当前 VMD 实际出现的骨骼、表情与 IK 开关轨道映射保存在播放器组件实例中，不要求每个动作生成外部配置资产。旧的 `HoVmdBindingProfile` 仍可供既有资产读取，但不是常规播放前置条件。

指定 VMD、角色根或骨骼搜索根时，播放器会扫描实际有关键帧的轨道；也可点 **扫描 VMD 轨道并匹配角色** 重扫。匹配优先使用对应的 IK Target，然后是 Humanoid Avatar 骨骼，最后匹配搜索根内唯一的精确名或规范化名称。多骨架角色可指定主骨架。生成结果保存在播放器组件中，仍需人工检查 MMD 中心、Groove、WIK、toe、眼睛和作者专用 morph。中心轨道不会自动合并到 hips。

组件统一从 **Add Component → HoUnityTools → MMD** 添加：`VMD Motion Player`、`VMD Camera Player`。旧 Profile 资产 API 为已有项目保留，普通动作映射直接储存在播放器组件中。IK 约束使用 **Add Component → HoUnityTools → Constraints → Ho IK Constraint**，与同组的 `Ho Follow Constraint`、`Ho Look At Constraint` 等组件保持命名一致；CCD 是内部求解算法。

### VMD 摄像机播放约定

VMD 摄像机不是普通 Transform 曲线：每个关键帧保存轨道中心、带符号距离、弧度 Euler 旋转、FOV、透视开关和 24 字节相机专用 Bezier 曲线。`VMD Camera Player` 是可独立挂载的组件，添加入口为 **HoUnityTools → MMD → VMD Camera Player**；把它挂在带 `Camera` 的物体上，指定 `.vmd` 动画并在 Play 模式播放即可。目标 Camera 自动取所在物体的 Camera；轨道中心在内部计算，无需另建空物体。可选的 `cameraTarget` 仅用于把轨道中心显示在场景层级中以便调试。

相邻帧通常是作者有意制作的跳切；组件默认不会在帧间距小于等于 1 时滑动插值，可通过 `snapAdjacentFrames` 关闭这个策略。负距离保留其符号，正交关键帧会根据距离和 FOV 推导 `orthographicSize`，避免沿用上一个投影模式的旧缩放。`mmdToUnityScale` 默认 `0.08`，需要与角色/场景导入比例一致。

摄像机 VMD 可以独立于角色 VMD 播放：把 `.vmd` 导入为 `HoVmdAnimation` 并填入 `镜头 VMD`。组件自动使用所在物体的 Camera，Play 模式下根据帧率、循环和自动播放设置驱动镜头。静帧或 CLI review 可直接调用 `ApplyAtFrame`。作者动作若另带 `camera.vmd`，应把镜头作为同一动作实例的独立资产记录，不把它误当作角色骨骼动画。

### VMD morph 依赖审查

`HoVmdMorphAudit` 会把 `HoVmdAnimation.morphFrames` 中出现过的形态键名称，与目标角色层级下所有 `SkinnedMeshRenderer.sharedMesh` 的 BlendShape 对照。每个需求按以下顺序处理：当前播放器的显式表情映射、旧版 `HoVmdBindingProfile.morphs` 映射、精确名称、规范化名称；同名跨多个 Renderer 或失效映射会记为冲突，找不到的名称会记为缺失。结果包括组件映射、精确命中、规范化候选、旧配置命中、缺失与冲突名称，可作为角色适配检查结果。

选中 `.vmd` 后，在上方中文导入面板的 **VMD 轨道与表情检查** 中拖入 **目标角色根**，可选填 **主骨架**，点击 **检查所选角色的表情依赖**。检查工具放在 `HoVmdScriptedImporterEditor` 中；下方 Imported Object 是 Unity 的只读导入预览，不承载可操作按钮。也可从 **HoUnityTools/MMD/VMD 轨道与表情检查** 打开相同工具。结果写到 `Library/HoMmdStages/MorphAudit/`，同时在面板列出缺失/冲突名称。播放器映射在 VMD Motion Player 组件内单独确认，无需生成角色配置资产。检查过程只读角色。

### 播放器轨道映射

`HoVmdMotionPlayer` Inspector 只在动画或角色搜索根变化、或手动扫描时遍历角色层级；普通界面重绘使用已保存的扫描结果。待处理的骨骼、表情和 IK 开关映射留在组件实例中。具体步骤与 WTD 示例见 [VMD 播放与绑定](guides/VMD_PLAYBACK.md)。

骨骼映射优先级为：组件内手动映射 → VMD IK 控制器的 Target → Humanoid Avatar → 搜索根内唯一精确名/规范化名称。表情和 IK 开关同样有组件内目标映射。映射只解决轨道到对象的对应，不会自动完成骨轴、比例、中心父级和追加/继承的重定向。

编辑器定格 review 会在写入 FK/morph 和 IK 开关后立即调用 Ho IK；播放模式仍由约束的 LateUpdate 执行。这样后台多角度渲染能检查到实际脚底/膝盖结果。

## HoIkConstraint

`Hollow.HoUnityTools.RigConstraints.HoIkConstraint` 是 Unity 运行时的 IK 约束，结合双节铰链解析解与通用 CCD：

- 一条链支持任意节数；链表从效应器父骨骼向根排列。
- `weight` 可作为 MMD `show/IK` 开关的适配目标；关闭时保留动画 FK 姿态。
- 每节支持最大步进角、局部欧拉角限制和局部轴遮罩。
- 可选 Pole 目标稳定膝盖/手肘平面；至少两节链才有弯曲平面，单节 toe IK 不执行 Pole 修正。
- `LateUpdate` 适合 Animator/VMD 曲线之后求解；`Manual` 适合由 MMD transform manager 或自定义播放控制器在明确顺序中调用。
- `CaptureBindPose` 只记录局部旋转；开启 `restoreBindRotation` 时每次求解前恢复这些旋转。普通 VMD 播放应保持关闭，让动画先写入骨骼。

`.vmd` 资产面板会显示骨骼、morph、镜头、灯光和 show/IK 段数量。实际动作绑定仍需一个目标骨架和名称映射；这让“导入成功”和“动作适配成功”保持可区分。播放器默认按名称自动匹配，出现重名或作者专用骨骼时使用显式绑定列表。

典型配置：

```text
左脚 IK 目标：左足IK
效应器：左足首（或模型的脚掌骨）
链：左ひざ → 左足（膝盖 → 大腿，不重复填写效应器）
Pole：左ひざ辅助目标（可选）

左脚趾 IK：
效应器：左つま先
链：左つま先の親 / 左足首（按模型实际层级）
```

Target 必须位于被解算骨骼的层级之外；否则骨骼运动也会移动目标，形成反馈。组件会拒绝这类配置以及重复链节点。控制空物体可以留在角色的约束分组，用 `HoParentConstraint` 在 IK 之前跟随所需参考系，不需要改原始骨架层级。

常见的双节脚链若有单轴膝盖、Pole、自由旋转的根以及正的等比缩放，会使用解析两骨解：保持初始膝盖弯曲分支，并遵守膝盖的显式角度限制，不再让 CCD 在膝盖铰链的正反两个角度解间跳变。不可达的控制点保留原位，脚部沿目标方向停在可达边界，不改变骨长；控制点与脚部之间有连线并不自动代表错误。

Inspector 的 **Pole Max Step Angle**（默认 `15°`）控制后续求解每次向 Pole 扭转的最大角度；首次求解直接对齐 Pole。目标经过 Pole 轴、弯曲平面没有唯一解时，沿用并平行搬运上一帧的腿平面，再逐步转向新方向。历史平面保存在根的父级旋转空间，Animator 每帧重置 FK 时也能连续求解；角色整体转向不会被误认为 Pole 翻转。显式调用 `CaptureBindPose` 会重新记录参考分支并清除这份连续性状态，不应每帧调用。更长链、带根部轴锁/限位或不满足解析条件的配置继续使用 CCD；Pole 修正只绕“根 → 当前效应器”轴旋转根节点，目标误差变大时撤销修正。

回归入口为 `Tests~/MmdMotion/IkPoleValidation.cs` 的 `IkPoleValidation.Run()`。它在独立预览场景中测试双节链、单轴膝盖、正/负膝盖角度限制、半权重、旋转/等比缩放、穿越 Pole 轴、不可达目标与 WTD 主骨架的双腿/脚趾。WTD 另有后向/外向网格及完整伸展范围轨迹，连续输入和每帧重置 FK 分别测试，并检查大腿/膝盖角度突变；不可达部分检查的是与最近可达点的误差，而不是要求脚部突破腿长。结果写入项目 `Library/HoMmdStages/ik-pole-validation.json`，同时检查目标漂移、骨长、有限数值、原场景脏状态和 Prefab 文件不变。运行时需将测试脚本临时放入工程的 Editor 目录；`Tests~` 不参与包的正常编译。

兰若的动作包有 README 要求关闭左/右足 IK 与 toe IK 的情况。这不是导入错误：动作可能已经把最终脚部姿态烘进 FK 帧，继续求解会二次修改。导入器需要把这类要求写成动作实例设置，而不是永久改模型骨骼。

## 适配顺序

1. 读取 PMX 骨骼和形态键名称，建立原始名称到 Unity Transform/BlendShape 的映射。
2. 读取 VMD header、bone/morph/camera/light/self-shadow/showIK 段，并生成动作报告。
3. 先写骨骼 FK/位置旋转与形态键，再按动作实例设置决定是否启用每个 IK 控制器。
4. 需要实时 MMD 行为时按 transform order → append/inherit → IK → physics → morph 的顺序求解；需要稳定渲染时可以 bake IK/physics 到 FK AnimationClip。
5. 镜头使用独立 target + camera 两节点 rig，将 VMD distance、target position、rotation、FOV 和 perspective 开关转换到 Unity 相机。
6. 用主镜头、左右观察镜头和至少一个动作时间点检查脚底、膝盖、脚趾、手腕、脸部 morph 与镜头裁切。

角色 PMX 的完整蒙皮/物理验证不属于当前交付范围。下一步验证使用 Unity 已有的 FBX/GLTF 角色：VMD 资产解析、显式骨骼映射、morph 映射、IK 开关和相机轨道分别验证，再按动作 README 调整适配参数。

## Sour 式模型（参考数据，不是最终角色导入目标）

`E:/BaiduSyncdisk/大师/_完整角色/Sour暄/Sour式初音ミクVer.1.02_by_Sour暄_194b7aa2b8fd2f9d6f0221d8767ad951.zip` 是卜逻desu 和 Taitohimea 动作适配的参考源。它只用于核对动作作者依赖、足 IK/toe IK 链、WIK 控制器和 morph 命名；真正动作目标可换成 FBX/GLTF 角色，再通过显式映射适配。不能把作者名或动作名当作骨骼标准。

## 当前动作库证据

整理脚本 `Tools~/MmdStages/prepare-motion-library.py` 将原包保留在 E 盘，并写出作者分组、解压包、VMD 类型和 README 摘录到 `E:/BaiduSyncdisk/大师/_完整动作_整理/`。截至首次扫描：7 个作者分支、128 个压缩包、18 个直接 VMD、666 个 VMD，发现 1 组 SHA256 重复包。兰若/甜茶包大量带独立镜头与表情 VMD；卜逻desu 包含 Sour 动作、Sour 镜头以及 PV/BGM 组合包；动作包中的模型和作者规约必须跟随动作实例记录。

解析器对 SweetTea 的部分旧 ZIP 会收到空文件名警告，但 bsdtar 仍成功提取可读文件；这些 warning 写入 `index.json`，不把它们伪装成无警告包。
