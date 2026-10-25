# Ho CombineArmature

在独立的衣服 FBX 与本体骨架之间装配骨骼。组件与处理器分别位于独立的 Runtime / Editor 程序集，只依赖 Unity；不依赖 VRC SDK、MA、NDMF、URP 或 Input System。

## 摆放与使用

```text
Character                         ← 角色根，包含相关动画和组件引用
├─ Animator                       ← 若需要动画，挂在角色根
├─ Rig                            ← targetArmature
│  └─ Hips → Chest ...
├─ Body
└─ Outfits
   └─ Outfit_A                    ← 网格/换装开关根，保持层级
      ├─ Outfits_A                ← 挂 HoCombineArmature
      │  └─ Hips
      │     ├─ Chest ...
      │     └─ Skirt → Tip ...
      └─ Mesh                     ← SkinnedMeshRenderer
```

1. 将本体与衣服导入并调整衣服的可见外形。匹配骨骼的位置、旋转和缩放可以不同，工具会保留绑定偏移。衣服 FBX 自带的 Animator 应删除、禁用或移到骨架子树外，避免衣服骨架被第二个 Animator 独立驱动。
2. 在衣服骨架根添加 `HoUnityTools/Ho CombineArmature`（C# 类型 `HoCombineArmature`），将本体对应骨架根拖入“本体骨架根”。根对象不要求同名。
3. 指定完整“角色根”，或留空自动取共同祖先及其最近的 Animator。此范围必须包含本体、衣服网格和所有引用衣服骨骼的组件。
4. 按需设置衣服骨骼名称的前缀/后缀；子节点按对应父节点下的名称精确匹配，不做全局搜名字或 Humanoid 猜测。骨骼偏移会保留，但不会自动重塑衣服网格或重画蒙皮权重。
5. 点击“检查骨骼匹配与引用”。进入 Play 自动处理场景副本；退出 Play 恢复编辑时结构。需要导出 Prefab/AssetBundle 时，点击“生成已装配的角色副本”，再保存或导出生成的角色。

添加第三套衣服只需重复上述步骤，让每套衣服直接指向本体。禁止嵌套、互相依赖或指向另一套衣服；所有装配计划在第一次修改前统一检查。

## 两种模式

### 直接合并（默认）

- 重映射蒙皮 `bones` 与 `rootBone`，保留数组顺序、原 Mesh、权重、bindposes 与 BlendShape；不合并网格或材质。
- 同姿势的对应骨骼直接复用本体骨骼，删除可折叠的重复子骨骼。
- 不同姿势的对应骨骼保留为偏移骨骼，挂到本体对应骨骼下的绑定参考帧中，保留原来的世界姿势和局部 TRS。蒙皮及组件引用继续指向该偏移骨骼，因此无需修改或克隆 Mesh.bindposes。参考帧只包含静态 Transform，不增加逐帧脚本。
- 非均匀缩放与旋转组合形成的剪切不能放进单个 Transform 时，用捕获的局部 TRS 链表示参考帧。源骨架根在无需偏移时保留原位；需要偏移时也可以移动到本体对应根下，身份保持不变。
- 将衣服独有分支接到对应本体骨骼下。仅分支根加 `衣服骨架名__原名` 的命名空间，分支内部结构保持不变；碰撞时追加数字。
- 重映射角色范围内组件的序列化 Transform / GameObject 引用，包括数组、约束来源和 Renderer.probeAnchor。源骨架根本身保留身份，普通组件对它的引用不改；蒙皮根引用按骨骼映射处理。
- 骨架子树之外的网格、衣服根、开关节点保持原父级与名字。

**衣服骨架子树是可重组区域。** 匹配的共享骨骼上仅允许 Transform 和根上的 HoCombineArmature；网格、Animator、开关动画等放在外面。专用分支上可以有骨骼附属组件，但不能有 Renderer/Animator。

本工具不包含 MA 的动画路径重写和 Active 状态传播系统。检测到已绑定 AnimatorController / Legacy Animation 中有轨道指向衣服骨架子树，会拒绝处理；即使是专用骨骼轨道，也需要先移出这种使用方式。未挂到 Animator 的孤立动画资产、Timeline、自定义字符串路径、外部 ScriptableObject 内部数据、运行时实例化的对象引用不在自动修复范围内。

关闭 Outfit_A 可以隐藏其子网格，但合并后移入本体的专用骨骼和物理组件不会自动随 Outfit_A 一起停用。若需要暂停其物理模拟，应在换装配置中显式控制对应组件。不要用衣服骨架根本身作为开关。

不再要求合并前匹配骨骼世界矩阵一致。零缩放导致目标坐标系无法求逆、NaN/无穷变换、歧义重名等仍会报错。角色根外的已加载场景组件若引用衣服骨架，也会报错，避免删除后产生悬空引用。

### 保留层级 / 原生位置旋转约束

- 每个匹配节点生成一个 ParentConstraint，复制位置和旋转。
- 保留源骨架、专用分支、蒙皮引用以及所有原有路径。
- 捕获当前的位置/旋转偏移后跟随，不要求初始姿势重合；不混入手工 LateUpdate 姿态复制。
- **不复制骨骼缩放。** 原生 ParentConstraint 的平移偏移按源朝向、世界长度保存；涉及运行时缩放变化时优先使用直接合并。绑定的 AnimatorController 中发现本体骨架缩放轨道时会拒绝约束模式；脚本等运行期缩放需要作者自行保证不使用该模式。
- 本体由 Animator 驱动时走 Unity 原生约束求值；若目标还被 LateUpdate、IK、物理或其他约束系统后续改写，需要在实际管线验证求值顺序。本工具不承诺跨所有物理插件都没有延迟。
- 保留完整重复骨架并增加约束，会增加对象与求值成本；通常优先直接合并。

## 处理时机与导出接口

`HoCombineArmature` 是配置组件，没有运行时 Awake/LateUpdate，不负责播放器里动态加载衣服。

- **Play / 场景构建**：Editor 的 IProcessSceneWithReport 处理正在加载/构建的场景。普通场景重载开启时自动生效；关闭 Scene Reload 会在进入 Play 前阻止自动装配，避免破坏原场景。仅关闭 Domain Reload 可以使用。
- Play 的场景处理回调不保证先于所有自定义 Awake。依赖装配后骨骼的初始化应放在 Start，或直接使用预烘焙 Prefab；运行中 Instantiate 的源 Prefab 不会因为有配置组件就自动装配。
- **Prefab / AssetBundle / 其他构建器**：必须处理导出副本，不要直接处理源资产。任意自定义导出器都可以在保存临时 Prefab 前调用：

```csharp
using Hollow.HoUnityTools.Editor.Armatures;

// 这里的 characterCopy 必须是调用方负责丢弃或导出的完整角色副本。
HoCombineArmatureProcessor.ProcessHierarchy(characterCopy);

// 或从源角色生成处理后的场景副本；失败时自动销毁失败副本。
GameObject baked = HoCombineArmatureProcessor.BakeCopy(sourceCharacter);
```

处理包含未激活的衣服。禁用的配置组件不装配，但会从导出副本移除。`processAutomatically=false` 仅关闭 Play/场景自动处理，显式烘焙与导出 API 仍处理该组件。成功后移除配置组件，重复调用不会重复合并或叠加约束。

**Warudo FastBuild** 已在临时 Character.prefab 的脚本重绑之前执行处理，配置组件不参与 Mod 源码收集；面捕 rig 的独立 AssetBundle 导出也在临时副本上执行。其他 Warudo 导出入口需要使用上述 API 或先手动烘焙。自动检查不会递归修改动画/网格等源资产。

**VRC** 可以继续在 VRC 工程使用 MA，在其他工程用本组件。不要在同一衣服骨架上同时挂 MA Merge Armature 和 HoCombineArmature。当前组件没有注册 VRC Avatar 上传回调，也没有实现 VRC 白名单接口；如要单独用它上传，应先烘焙并导出不带配置组件的副本，或后续增加隔离的 VRC 构建适配层。约束模式使用 Unity 原生约束，不在本包内引用 VRC 约束程序集。

## 验证

`Tests~/CombineArmature/Run.ps1` 创建独占隔离项目，只复制两个新程序集和探针，不安装 VRC/MA/NDMF 等包。用例覆盖蒙皮 BakeMesh 顶点一致性、驱动骨骼运动、序列化引用、Renderer.probeAnchor、多套衣服的独立分支、关闭的衣服、错误原子拒绝、动画保护、Prefab 实例与副本保存/重载、真实 Play 自动处理、动画后的原生约束求值和退出 Play 恢复。

```powershell
& '.\Tests~\CombineArmature\Run.ps1'
& '.\Tests~\CombineArmature\Run.ps1' -Unity 'D:\Unity\Unity 2021.3.45f2\Editor\Unity.exe' -EditorMode
```

项目目录必须是新的；重复运行使用新的 `-Project`。成功结果同时要求 Unity exit 0 与 `combine-armature-result.txt` 中的 PASS 标记。

`-EditorMode` 使用普通编辑器启动并自动运行测试、退出，适用于某些旧编辑器批处理许可启动异常的环境；不修改许可证或工程外的编辑器配置。

2026-10-24 验证：Unity 6000.3.15f1（批处理）和 2021.3.45f2（普通编辑器自动执行）各通过 47 项检查；两版均实际进入/退出 Play，后一个约束测试关闭 Domain Reload、保持 Scene Reload。整包 Runtime / Editor 另通过离线编译。Warudo FastBuild 的处理入口已接入，并验证 LoadPrefabContents → 处理 → 保存/重载这段公共路径；尚未对用户角色执行完整 UMod 打包与 Warudo 运行验收。

2026-10-25 更新：组件显示名统一为 `Ho CombineArmature`，C# 类型为 `HoCombineArmature`，保留原 MonoScript GUID 并声明旧类型迁移。两版 Unity 各通过 58 项检查，增加不同绑定姿势、原局部坐标系、剪切/镜像缩放、偏移约束和旧类型序列化迁移验证。

### WTD 两套衣服的配置实例

在 `WTD.unity` 的 `WTD-BUILD` 角色上，主骨架是 `Armature-Swimsuit`，巫女服骨架是 `Parts/巫女服/WTD-miko/Armature-Miko`：

| 项目 | 设置 |
| --- | --- |
| 组件挂载处 | `WTD-BUILD/Parts/巫女服/WTD-miko/Armature-Miko` |
| 本体骨架根 | `WTD-BUILD/Armature-Swimsuit` |
| 角色根 | `WTD-BUILD`，或留空自动推断 |
| 装配方式 | 直接合并 |
| 骨骼前后缀 | 留空 |

泳装网格已使用主骨架，无需再加一个反向合并组件。不要把组件挂在包含 Animator 和网格的 `WTD-miko` 模型根上，也不要把“角色根”填成两套骨架中的某一套。

本机 Prefab 副本验证匹配 150 根骨骼、18 个专用分支，保留 8 根 `MCH_` 偏移骨骼。8 个巫女服网格的父级不变，合并前后 BakeMesh 的最大顶点差异约为 `4.59e-5` Unity 单位；原场景与 FBX 不执行破坏性合并。

设计来源与行业比较：[调查记录](../research/COMBINE_ARMATURE.md)。
