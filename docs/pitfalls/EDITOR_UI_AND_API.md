# 编辑器 UI 与 Playable API

正本：[约束面板设计系统](../EDITOR_UI_SYSTEM.md)（令牌与控件）；Playable 那几条来自动画剪辑直通预览组件
（`Runtime/AnimationTools/HoAnimationClipPreviewer.cs`，同一个工作区里还在完善中）。

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
