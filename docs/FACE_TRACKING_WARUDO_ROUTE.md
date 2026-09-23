# 面捕在 Warudo 的路线（新路线：独立状态机 → 翻译成纯键值与骨骼动画）

> **一句话**：旧路线是"完全绕过 Warudo 蓝图、自己去做动作/表情/状态操作"；
> 新路线是"**独立脚本跑状态机，把它翻译成 Warudo 喜欢的纯键值动画 + 纯骨骼旋转/移动动画**"，
> 用**两到三个互不挂载的 mod** 达成效果。
>
> 新路线的两个好处：① 角色上**只要键够多，就能吃任意方案的面捕** —— 换方案只是换个输入节点/mod；
> ② Unity 内部调试**极其方便，而且不在角色上留任何污染**（角色 mod 干净得可以原样上传）。

本文记录这条路线**为什么成立、约束在哪、产物怎么分**，以及它前面那串决定（门控/眼睑/输入层）的关系。
取证来源：`.warudo-mod-research/`（另一路 Warudo 调研，含官方手册抓取、本机 Warudo 0.15.0 程序集元数据扫描、
`WarudoPluginExamples` 官方插件源码结构）；参数标准见 [参数标准表](PARAMETER_STANDARDS.md)。

---

## 1. 两条路线

| | **旧路线**（绕过 Warudo 蓝图） | **新路线**（说它的语言） |
| --- | --- | --- |
| 谁在算 | 我们的脚本自己算完，直写模型 renderer，谁也管不着 | 我们的脚本跑**状态机**，把结果翻译成"键值 + 骨骼旋转/移动"，**喂进 Warudo 的 Tracking 层** |
| 和 Warudo 的关系 | 我不理你，你也别管我 | 我说你的语言：键走 `Set Character Tracking BlendShapes`、骨骼走 `Override Character Bone Rotation Offsets` |
| 角色 mod | 挂着我们的组件 | **不挂任何组件**，只要键足够多 |
| 调试 | 只能在 Unity 里挂组件试 | Unity 面板全局调试（无组件）＋ 真机同一个核心 |
| 换面捕方案 | 要改角色上的东西 | **只换输入那一环**（mod/节点） |

两条路线的分歧点其实只有一句话：**要不要保留"树"**。新路线保留（树是我们真正值钱的东西：
1D/2D 混合、姿势表、门控、中间层那套经验）；而"喂进它的 Tracking 层"意味着它的**权重混合、整体重置、
丢脸交还**照样管着我们 —— 这正是它给动捕留的正门（见 §4），所以我们既保住树，也不必自己造那套管理。

---

## 2. 产物划分（2～3 个 mod）

| # | 产物 | 内容 | 为什么这么切 |
| --- | --- | --- | --- |
| ① | **角色 mod**（常规角色） | Unity Prefab `Character` + humanoid + Animator，**不挂我们的任何组件**，只要求键够多（ARKit 52 起） | 角色越干净越好上传、越能复用；键是唯一的契约 |
| ② | **处理链 mod**（输入 + 中间层 + 树 + 翻译） | 收数据 → 读中间层配置（**构建期写死**）→ 参数 → 自己的状态机求值 → 翻译成键值/骨骼 → **喂进角色的 Tracking 层** | 这是"新路线"的本体；它一个 mod 就能搞定全部处理 |
| ③ | **接收器 mod**（可选） | 只做"某种面捕方案 → 规范值"（像官方 receiver mod 那样，可注册成一种 tracking 方式） | 让"换方案"变成"换个 mod"；**前提是跨 mod 接口能立起来**（见 §5 未决项） |

**Unity 侧另有两个产物**（不进任何 mod）：

- `HoFaceTracking`：**极薄的运行时外壳** —— 运行时把（装配好的）控制器接到自己的影子上跑起来，
  再把结果写回真实模型。它不含输入、不含中间层、不含配置。
- **面捕调试面板**：**纯编辑器全局状态**（不是组件）。它同时扮演"输入 + 中间层 + 观察者"，
  直接驱动上面那个薄外壳。删掉它，角色上什么都不剩 —— 这就是"debugger"这个名字该有的样子。

---

## 3. Warudo 侧的硬约束（决定了产物怎么切）

来自 `.warudo-mod-research/warudo-mod-build-pipeline-research.md`（官方 mod-sdk / plugin-mod 页原文）：

| 约束 | 对我们意味着什么 |
| --- | --- |
| **不支持 `.asmdef`**（"C# scripts covered by the assembly definitions will not be packaged into the mod"） | 我们包里所有脚本都在 asmdef 下 → **给 Warudo 的 `.cs` 必须是脱离 asmdef 的散文件**（构建时按类型挑出来放进 mod 文件夹） |
| **不支持 ScriptableObject** | 配置只能是**组件的序列化字段**或 **TextAsset**；"工程设置里一份输入环境"这种设计在 Warudo 里不成立 |
| **不支持已编译 DLL**（只能 `.cs` 源码，UMod 用 Roslyn 现场编） | 两个 mod 各自编译成不同程序集 → **同名类型是两个不同的 Type** → **跨 mod 不能 `GetComponent<我们的类型>()`** |
| **不能用反射、`System.IO`、`UnityEditor`** | 运行时路径不能碰文件与反射；接收端用 `System.Net.Sockets` + 线程是允许的（官方 VMC 插件就这么干） |
| 官方插件骨架（VMC） | `Plugin.cs`（`[PluginType]`）+ `Assets/*Asset.cs` + `Behaviors/*Behavior.cs` + `Nodes/`（可选）+ `Localizations/`；VMC 靠注册 `FaceTrackingTemplate` 把自己变成"可选的面捕方式" |

**跨 mod 的唯一可用接口是"两边都引用的类型"**：Unity 原生类型（`Animator` / `Transform` /
`SkinnedMeshRenderer`）与 Warudo 自有类型（`Warudo.Core` 里的 `Character` / `Asset` / `Node`）。
我们自己的类型跨不过去 —— 这是产物划分的硬边界。

---

## 4. Warudo 自己那一层是什么（我们要绕过的对象）

从本机 Warudo 0.15.0 程序集扫描（`.warudo-mod-research/.data/api-scan/api-Warudo.Plugins.Core.txt`）：

| 类别 | 成员 |
| --- | --- |
| 注册"一种追踪方式" | `RegisterCharacterTrackingTemplate` / `UnregisterCharacterTrackingTemplate` / `CreateCharacterTrackingTemplate` / `AutoCompleteCharacterTrackingTemplates` |
| 混合键层 | `BlendShapeEntry` / `IBlendShapeEntry` / `IBlendShapeEntryProvider`；`AddBlendShapeCurve` / `RemoveBlendShapeCurve`；`UpdateBlendShapes` / `ApplyBlendShapeEntry` |
| 重置/开关 | `ResetBlendShapes` / `ResetBlendShapesNextFrame` |
| 骨骼与根 | `OverrideBonePositions` / `OverrideBonePositionWeights` / `OverrideBoneRotations` / `OverrideBoneRotationWeights` / `OverrideRootPosition` |
| VRM 兼容 | `GetUseVRMBlendShapeProxy` / `GetVRMBlendShapeClip` / `VrmBlendShapeClips` |

蓝图侧这两个节点名不是"一个给我们、一个给它们"，而是**两种写语义**：

| 节点 | 语义 | 谁用 |
| --- | --- | --- |
| `Set Character BlendShape` | **直接写那一格**（基础层，设一次） | 脚本化 / 表情式的写入 |
| **`Set Character Tracking BlendShapes`** + `Override Character Bone Rotation Offsets` + `Override Character Root Position` | **动捕输入层**：每帧喂、按 Weight 与动画混合、可整体交还 | **动捕 / 面捕的正门** |

手册那页官方动捕示例的原文就是整条链：

> "for every frame, we want to update the character's blendshapes (**Set Character Tracking BlendShapes**),
> bones (**Override Character Bone Rotation Offsets**), and root position (**Override Character Root Position**)."

配套的 `Reset Character Tracking BlendShapes` / `Reset Overridden Character Bones` 就是"把控制交还"的开关；
骨骼一律带 **Weight**。

**结论**：Warudo 抱的不是一个 Unity `AnimatorController`，而是
**"每个角色一份追踪条目（键 + 曲线 + 权重）+ 骨骼覆盖（四元数 + 权重）+ 根位置覆盖"**，
由它每帧 apply、可整体重置。它的动画则是 `AnimationClip`（`CharacterAnimation` mod 类型），不是用户的控制器资产。

### 我们**全部走 Tracking 层**（不是绕过它）

既然动捕的正门就是 Tracking 层，我们就把出口接在这一层，于是"它自己那套管理"直接归我们享用：

| 机制 | 谁提供 |
| --- | --- |
| 总开关 / 交还与重置 | **它**：丢脸时我们停止写并调 `Reset Character Tracking BlendShapes`（骨骼同理） |
| 与动画/表情的**权重混合** | **它**：`BlendShapeEntry` 与骨骼覆盖都带 Weight |
| 键争用 | **基本消失**：动捕在这层只有一个来源；我们注册成 tracking template 后，用户在面捕下拉里选的就是我们这套，不会两边同时写（**权限这一点仍要实测**） |
| VRM 兼容 | **它**：`GetUseVRMBlendShapeProxy` / `GetVRMBlendShapeClip` |

**还属于我们自己的只剩四件**（都绕不开）：

1. **树跑在哪** —— 我们的树要用我们自己的控制器，所以仍要自己的影子 Animator（Warudo 里自建 Animator 已确认可行）。
2. **丢脸的策略** —— "发中性"还是"停止写并 reset"：产品选择（我们已有断流等待/回中性两个全局参数），机制归它。
3. **输入 + 中间层** —— Unity 侧是面板调试；Warudo 侧是构建期写死的同一份配置。
4. **每帧的翻译** —— 把树的结果按名字喂进 `Set Character Tracking BlendShapes` 与骨骼覆盖（本来就要写，只是目标从 renderer 换成它这一层）。

> ⚠️ **上一版本文写错过一句**：曾把"总开关/重置/权重/键争用"列成"绕过它之后我们必须自己补三件事"。
> 那是**直写真值到 renderer** 那条路（`SetBlendShapeWeight`）的代价，不是这两个节点存在导致的。
> 走 Tracking 层就没有这些负担 —— 记在这里免得后人照错的记。

---

## 5. 出口形式（我们的树 → Warudo 的 Tracking 层）

| 我们的结果 | 翻译成 | 为什么 |
| --- | --- | --- |
| 形态键权重 | **`Set Character Tracking BlendShapes`**（一组 `键名 + 权重`，按名字） | 这是动捕的正门；真值直接进它的追踪层，混合/重置/交还都归它 |
| 头/颈/眼球等骨骼 | **`Override Character Bone Rotation Offsets`**（+ Weight；必要时 `Override Character Root Position` / `Override Bone Positions`） | 直接写 `Transform.localRotation` 会被它的动画/IK 覆盖；"Offsets" 是相对动画的偏移，正好是动捕语义（坐标系待实测） |
| 丢脸 / 交还 | **停写 + `Reset Character Tracking BlendShapes` / `Reset Overridden Character Bones`** | 用它的机制交还控制，而不是自己造一套开关 |
| 树本身 | 跑在**我们自己的影子 Animator** 上（不进角色控制器、不求合并） | 角色 mod 保持零组件；我们的树原样复用 |

---

## 6. 已确认与待实测

| 项 | 状态 |
| --- | --- |
| Warudo 里能自己新建 Animator 挂我们的控制器 | **已确认可行**（"我们现有的脚本能上传就已经说明问题了"） |
| `.controller` 资产能随 mod 一起打包 | 官方允许往 mod 文件夹放 Unity 资产（prefabs / materials / textures），"prefab 用到的脚本要一起放"；控制器属同类，**低风险但值得一测** |
| **普通 mod 能否每帧写它的 Tracking 层**，还是必须注册成 `CharacterTrackingTemplate` 才有这个位置 | **待实测**（决定处理链 mod 要不要注册模板） |
| `Override Character Bone Rotation **Offsets**` 的坐标系与叠加语义 | **待实测**（"偏移" 是相对动画还是绝对；轴序/单位） |
| `ResetBlendShapes` 与 `ResetBlendShapesNextFrame` 的差别与调用时机 | **待实测**（丢脸交还时用哪个） |
| 接收器单独成第三个 mod 时的接口 | **未决**：跨 mod 不能共享我们的类型；可能只能靠 Warudo 自有类型（Asset）或干脆把接收器并进处理链 mod（即"两个 mod"方案） |

---

## 7. 与前面几条决定的关系

这条路线不是孤立的，后面几条都是同一条原则（**"算的地方算干净，用的人自己决定怎么用"**）的推论：

| 已定的决定 | 与本文的关系 |
| --- | --- |
| **删掉区域门控**（`outputRegions` / `Ho/Drive/Gate/*`） | 哪些键算数由使用者的树/参数决定，不由我们注入开关 —— 在 Warudo 里更是必须（我们要喂的是**一层值**，不是一套开关） |
| **删掉眼睑三模式**（`eyeSync*`） | 同上，归树；代价（左右眨眼键各自能闭双眼的模型）写进踩坑文档 |
| **接收端只交原样、映射与量纲写进中间层配置的输入行** | Warudo 侧的配置是**构建期写死的同一份格式**；两边共用同一套规范名，才可能"换个输入就换一套面捕" |
| **断流等待 / 回中性 = 全局配置**（不进每行） | 表达式是纯函数做不了记忆；在 Warudo 里这两个参数须随 mod 的硬配置一起走 |
| **`HoFaceTracking` 收薄成"只把控制器跑起来"** | 它是 Unity 世界的运行时外壳；Warudo 世界对应的外壳就是处理链 mod |

---

## 8. 下一步（等 Warudo 侧实测结果）

1. 实测：**普通 mod 能不能每帧写它的 Tracking 层**（`Set Character Tracking BlendShapes` 那一层），
   还是必须注册成 `CharacterTrackingTemplate` 才有这个位置 —— 这条决定处理链 mod 要不要注册模板。
2. 实测：`Override Character Bone Rotation Offsets` 的坐标系/叠加语义，以及 `ResetBlendShapes` 与
   `ResetBlendShapesNextFrame` 在丢脸交还时该用哪个。
3. （低风险、顺手做）mod 里新建 Animator + 打包进去的 `.controller`，`SetFloat` 后形态键是否真的动。
4. 定第三块产物要不要存在（接收器单独成 mod 的接口可行性）。
5. 然后才动代码：`HoFaceTracking` 收薄 → 面板吃掉输入+中间层 → 处理链 mod（同核心外壳）。
