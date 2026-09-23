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

| | **旧路线**（绕过 Warudo 蓝图） | **新路线**（翻译成它喜欢的东西） |
| --- | --- | --- |
| 谁在算 | 我们的脚本自己算完，direct 写模型 | 我们的脚本跑**状态机**，把结果翻译成"键值 + 骨骼旋转/移动" |
| 和 Warudo 的关系 | 我不理你，你也别管我 | 我说你的语言：真值写键、骨骼走它的覆盖层 |
| 角色 mod | 挂着我们的组件 | **不挂任何组件**，只要键足够多 |
| 调试 | 只能在 Unity 里挂组件试 | Unity 面板全局调试（无组件）＋ 真机同一个核心 |
| 换面捕方案 | 要改角色上的东西 | **只换输入那一环**（mod/节点） |

两条路线的分歧点其实只有一句话：**要不要保留"树"**。新路线保留（树是我们真正值钱的东西：
1D/2D 混合、姿势表、门控、中间层那套经验），代价是 Warudo 自己那层"追踪层"的开关/权重/重置
不再管我们，得我们自己补（见 §4）。

---

## 2. 产物划分（2～3 个 mod）

| # | 产物 | 内容 | 为什么这么切 |
| --- | --- | --- | --- |
| ① | **角色 mod**（常规角色） | Unity Prefab `Character` + humanoid + Animator，**不挂我们的任何组件**，只要求键够多（ARKit 52 起） | 角色越干净越好上传、越能复用；键是唯一的契约 |
| ② | **处理链 mod**（输入 + 中间层 + 树 + 翻译） | 收数据 → 读中间层配置（**构建期写死**）→ 参数 → 自己的状态机求值 → 翻译成键值/骨骼 → 写进角色 | 这是"新路线"的本体；它一个 mod 就能搞定全部处理 |
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

蓝图侧对应节点名也印证这件事：`Set Character **Tracking** BlendShapes` 与 `Set Character BlendShape`
是**两个不同**的节点，另有 `Reset Character Tracking BlendShapes` / `Reset Overridden Character Bones`；
骨骼一律带 **Weight**。

**结论**：Warudo 抱的不是一个 Unity `AnimatorController`，而是
**"每个角色一份追踪条目（键 + 曲线 + 权重）+ 骨骼覆盖（四元数 + 权重）+ 根位置覆盖"**，
由它每帧 apply、可整体重置。它的动画则是 `AnimationClip`（`CharacterAnimation` mod 类型），不是用户的控制器资产。

### 绕过它 = 我们要自己补三件事

1. **总开关 / 重置 / 回中性**：`ResetBlendShapes`、`ResetOverridden...` 管不到我们。
   我们自己要有"面捕开/关 + 重置到中性"（断流等待与回中性我们已经有了，当**全局配置**）。
2. **写回时机**：mod 实体有 `OnPreUpdate / OnUpdate / OnPostUpdate / OnLateUpdate / OnEndOfFrame` 五个阶段，
   必须**在它的动画/IK 之后**写（`OnLateUpdate` 或 `OnEndOfFrame`）—— 具体哪个阶段稳定压得住，**要实测**。
3. **键争用**：用户若同时开着 Warudo 自带面捕/表情，两边都写同一批键，谁后写谁赢。
   我们的 mod 启用时应**先认领**（把用到的键从它那层排除/重置），并明确文档化"同一角色只留一个写者"。

---

## 5. 出口形式（我们的树 → Warudo 喜欢的两种动画）

| 我们的结果 | 翻译成 | 为什么 |
| --- | --- | --- |
| 形态键权重 | **真实键值**：`SkinnedMeshRenderer.SetBlendShapeWeight`（按名字，不用它的 tracking entry） | 真值最直接；不进它的 entry 表就没有额外一层映射与曲线语义 |
| 头/颈/眼球等骨骼 | **`OverrideBoneRotations` + `OverrideBoneRotationWeights`**（必要时 `OverrideRootPosition` / `OverrideBonePositions`） | 直接写 `Transform.localRotation` 会被它的动画/IK 覆盖；它这套覆盖层带权重、能和动画混，是"翻译成它喜欢的形式"的正解 |
| 树本身 | 跑在**我们自己的影子 Animator** 上（不进角色控制器、不求合并） | 角色 mod 保持零组件；我们的树原样复用 |

---

## 6. 已确认与待实测

| 项 | 状态 |
| --- | --- |
| Warudo 里能自己新建 Animator 挂我们的控制器 | **已确认可行**（"我们现有的脚本能上传就已经说明问题了"） |
| `.controller` 资产能随 mod 一起打包 | 官方允许往 mod 文件夹放 Unity 资产（prefabs / materials / textures），且"prefab 用到的脚本要一起放"；**控制器属于同类，值得实测一遍** |
| 写回阶段用 `OnLateUpdate` 还是 `OnEndOfFrame` | **待实测**（哪个阶段能稳定压过它的动画与表情） |
| 接收器单独成第三个 mod 时的接口 | **未决**：跨 mod 不能共享我们的类型；可能只能靠 Warudo 自有类型（Asset）或干脆把接收器并进处理链 mod（即"两个 mod"方案） |
| 键认领的具体做法（怎么把我们用的键从它的层里排除） | **待定**（需要实测它那层的冲突行为） |

---

## 7. 与前面几条决定的关系

这条路线不是孤立的，后面几条都是同一条原则（**"算的地方算干净，用的人自己决定怎么用"**）的推论：

| 已定的决定 | 与本文的关系 |
| --- | --- |
| **删掉区域门控**（`outputRegions` / `Ho/Drive/Gate/*`） | 哪些键算数由使用者的树/参数决定，不由我们注入开关 —— 在 Warudo 里更是必须（我们的开关它不认） |
| **删掉眼睑三模式**（`eyeSync*`） | 同上，归树；代价（左右眨眼键各自能闭双眼的模型）写进踩坑文档 |
| **接收端只交原样、映射与量纲写进中间层配置的输入行** | Warudo 侧的配置是**构建期写死的同一份格式**；两边共用同一套规范名，才可能"换个输入就换一套面捕" |
| **断流等待 / 回中性 = 全局配置**（不进每行） | 表达式是纯函数做不了记忆；在 Warudo 里这两个参数须随 mod 的硬配置一起走 |
| **`HoFaceTracking` 收薄成"只把控制器跑起来"** | 它是 Unity 世界的运行时外壳；Warudo 世界对应的外壳就是处理链 mod |

---

## 8. 下一步（等 Warudo 侧两个实测结果）

1. 实测：mod 里新建 Animator + 打包进去的 `.controller`，`SetFloat` 后形态键是否真的动。
2. 实测：`OnLateUpdate` / `OnEndOfFrame` 哪个阶段写键与骨骼覆盖能压过 Warudo 的动画与表情。
3. 定第三块产物要不要存在（接收器单独成 mod 的接口可行性）。
4. 然后才动代码：`HoFaceTracking` 收薄 → 面板吃掉输入+中间层 → 处理链 mod（同核心外壳）。
