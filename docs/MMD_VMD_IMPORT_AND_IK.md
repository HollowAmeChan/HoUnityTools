# MMD VMD 动作与 Ho IK 约束

## 现在的边界

MMD 动作不是普通 FK 动画。VMD 可能同时包含骨骼帧、形态键帧、镜头帧、灯光帧、自阴影帧以及 `show/IK` 开关帧；动作作者还可能只针对某个模型的骨骼布局制作。MMD 运行顺序包含追加/继承、IK、物理和形态键，不能只把 VMD 骨骼曲线直接套到 Unity Transform 上。

当前 `HoUnityTools` 的 PMX importer 主要服务静态场景。角色 PMX 不作为当前最终运行时目标，因此不要求把角色 PMX 的完整蒙皮、物理和 Humanoid Avatar 流程接进 Ho 管线。动作适配使用 PMX 角色作为元数据参考，真正播放目标可以是已经进入 Unity 的 FBX/GLTF/其他角色骨架。动画导入研究以 UMT 为基线：它具备 PMX 骨骼/形态键、MMD transform order、CCD IK、VMD bone/morph/IK toggle、VMD camera 两节点 rig 和可选物理烘焙。HoAux 继续处理 Unity 侧的 twist、fan、体积保持等辅助形变。

HoUnityTools 现在也提供 `HoVmdAnimation` 与 `.vmd` ScriptedImporter。它先把 VMD 作为独立数据资产导入，保留原始模型名和各类轨道；这一步不猜模型映射、不自动改骨骼，也不和静态 `.pmx` importer 抢扩展名。后续播放器/适配器读取该资产并选择目标骨架。

`HoVmdMotionPlayer` 提供第一版运行时适配：按骨骼名写入 FK 位置/旋转，按 morph 名称写入 `SkinnedMeshRenderer` blend shape，按 `HoIkConstraint.IkName` 应用 show/IK 开关。`HoVmdCameraPlayer` 使用 target + Camera 两节点读取 VMD 镜头的目标位置、距离、旋转、FOV 和透视开关。复杂 MMD transform order、物理烘焙和模型专用重命名仍应接 UMT solver 或后续 Ho 适配层。

`HoVmdBindingProfile` 是 FBX/GLTF 角色的可保存适配资产：用 target path 映射 VMD 骨骼，用 renderer path + blend shape name 映射 morph，用 constraint path 映射动作级 IK。播放器优先使用 Profile，再用同名自动匹配作为起点。Profile 属于角色资产，不属于动作包。

`HoVmdBindingProfileBuilder` 可以从选中的 FBX/GLTF 角色层级生成初始 Profile：优先精确名称，其次规范化名称，再尝试 Humanoid `HumanBodyBones`。生成结果需要人工检查，尤其是 MMD 的 WIK、toe、眼睛和作者专用 morph。

编辑器定格 review 会在写入 FK/morph 和 IK 开关后立即调用 Ho IK；播放模式仍由约束的 LateUpdate 执行。这样后台多角度渲染能检查到实际脚底/膝盖结果。

## HoIkConstraint

`Hollow.HoUnityTools.RigConstraints.HoIkConstraint` 是 Unity 运行时的通用 CCD 约束：

- 一条链支持任意节数；链表从效应器父骨骼向根排列。
- `weight` 可作为 MMD `show/IK` 开关的适配目标；关闭时保留动画 FK 姿态。
- 每节支持最大步进角、局部欧拉角限制和局部轴遮罩。
- 可选 Pole 目标稳定膝盖/手肘平面。
- `LateUpdate` 适合 Animator/VMD 曲线之后求解；`Manual` 适合由 MMD transform manager 或自定义播放控制器在明确顺序中调用。
- `CaptureBindPose` 只记录局部旋转；开启 `restoreBindRotation` 时每次求解前恢复这些旋转。普通 VMD 播放应保持关闭，让动画先写入骨骼。

`.vmd` 资产面板会显示骨骼、morph、镜头、灯光和 show/IK 段数量。实际动作绑定仍需一个目标骨架和名称映射；这让“导入成功”和“动作适配成功”保持可区分。播放器默认按名称自动匹配，出现重名或作者专用骨骼时使用显式绑定列表。

典型配置：

```text
左脚 IK 目标：左足IK
效应器：左足首（或模型的脚掌骨）
链：左足首 → 左ひざ → 左足
Pole：左ひざ辅助目标（可选）

左脚趾 IK：
效应器：左つま先
链：左つま先の親 / 左足首（按模型实际层级）
```

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
