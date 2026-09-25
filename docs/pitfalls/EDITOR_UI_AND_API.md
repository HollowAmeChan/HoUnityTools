# 编辑器 UI 与 Playable API

正本：[约束面板设计系统](../EDITOR_UI_SYSTEM.md)（令牌与控件）、[动画剪辑直通预览](../ANIMATION_CLIP_PREVIEW.md)（§9–§11 那几条来自
`Runtime/AnimationTools/HoAnimationPreviewer.cs`）。

## 1. GUILayout 按"剩余宽度的百分比"分配空间

症状：**同一列在不同行宽度不同**，面板看着乱、控件左右不对齐；一个 `0.35` 占半行；
横条/滑杆铺满整行。根因就一句：`GUILayout` 按剩余宽度百分比分配，**label 默认吃掉面板 40%**。

所以这套面板不用自动布局排参数行 —— **每一行自己算矩形**（`HoConstraintEditorControls` 就是干这个的）。
层次也要自己定：标题 / 参数 / 读数 / 说明分成不同字号与灰度，否则全是同一号灰字。

## 2. 字段上不要加 `[Header(...)]`

自定义面板已经画了彩色分区条，而 `PropertyField` 会把 `[Header]` **再画一遍** ——
看起来就是标题重复两遍（曾经踩过）。

## 3. 自绘贴图：`HideFlags.HideAndDontSave`

运行时生成的 4×4（九宫格描边）与 64×1（分区头渐变）贴图带 `HideFlags.HideAndDontSave`：
**不落盘、不进包**，免得把编辑器生成物打进构建。

## 4. `PlayableGraph.IsValid()` 会在"图还在、手柄已失效"时返回 true

症状：`ArgumentNullException: The Playable is null.` —— 你以为 `IsValid()` 已经守住了。
原因：图存在而 playable 句柄已经失效时它照样返回 true。
怎么办：别只信 `IsValid()`，手柄自己也要判（组件里另有 `IsPlayableReady` 一类的就绪判定）。

## 5. 预览时要把 Animator 的 controller 置空

进入预览会把 Animator 上的控制器引用置空 —— **留着旧控制器的话状态机会和预览抢骨架**。
配套的两条：
- 换 clip 时，**上一条写过、这一条没写的通道会留在旧值上**（形态键不回零）。
- 不带控制器时 `applyRootMotion` 的语义与常规状态机不同（这一条尚未实机验证）。

## 6. 不能在预制体资产上预览

目录里的 `.prefab` 资产不是场景实例，预览会改到资产本身 ——
组件用 `IsPrefabAssetContext` 挡住这种情况。

## 7. 不要用 `AnimationMode.SampleAnimationClip` 做连续预览

它对同一物体在同一编辑器 tick 内反复采样会**卡在默认 A-pose**。预览自己驱动 `PlayableGraph`
（`DirectorUpdateMode.Manual` + `AdvanceBy`）就是绕开它。

## 8. batchmode 与已打开的编辑器互斥

Unity 的授权客户端有**全局互斥量**：本机已经开着一个 Unity 编辑器时，跑不了 `-batchmode`（拿不到授权就走不通）。
跑批处理前先确认编辑器关了，或换一台机器 / 另一个许可。

## 9. 在 `OnValidate` 里重建对象 ⇒ 刷屏

症状（一改面板字段就刷）：

```
SendMessage cannot be called during Awake, CheckConsistency, or OnValidate (mesh_X: OnBecameInvisible)
```

原因：`OnValidate` 期间 Unity **禁止 `SendMessage`**，而"重建"会 `SetActive` / 销毁图，于是场景里任何带
`OnBecameVisible`/`OnBecameInvisible` 的物体（蒙皮网格几乎都有）都会炸一次。
怎么办：**`OnValidate` 里只置待办标记**，真重建落到下一次编辑器 update（`delayCall` + 面板的 update 回调）。
预览器那边叫 `RequestRebuild()`。

## 10. 一个 Animator 只能有一个接管者

症状：两个同类组件挂在同一棵骨架上时，后建立的图把先建立的姿势**覆盖**掉 —— 两个面板互相打架，极难排查。
原因：两边都认为自己拥有那个 Animator。
怎么办：组件维护一张**静态归属表**（Animator → 接管者），第二个接管者被拒绝并点名是谁占着。
静态表由 Unity 的 domain reload 自然清掉。

## 11. 预览链路被外力破坏时要主动收摊

症状：**静默失效** —— 组件还是"启用"状态，但图再也驱动不了任何东西。
原因：有些 clip 的 `m_IsActive` 曲线会把物体关掉；如果关掉的正是 Animator 自己所在的物体（或它的父级），
图就废了。怎么办：每帧体检**整个祖先链**的启用状态，断了就结束预览并把原因写进 `LastError`。

## 12. 自定义 Inspector：`DrawHeader` 会被遮蔽、`GUIStyle.padding` 会挤压内容

- 方法名**不能叫 `DrawHeader`**：会遮蔽 `Editor.DrawHeader()`（CS0108），Unity 默认的组件标题栏被顶掉、
  tooltip 也挂到错的地方。预览器那边叫 `DrawTitle()`。
- **不要在 `GUIStyle` 里做内边距**：卡片的 `padding` 会让内容被 GUILayout 重新分配（表现同样是图标被压扁）；
  而且 `EditorGUILayout.BeginVertical` 返回的是**内边距之内**的矩形，拿它当背景就会画错位置 ——
  先用 `GetControlRect` 拿整行矩形自己画背景，再用 `Inset` 手动收边距。
