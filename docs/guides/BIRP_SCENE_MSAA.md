# BIRP Scene MSAA —— Scene 视图的多重采样开关

面向旧版 Unity 的 Built-in 工程。**Unity 6 不注册本功能的按钮、Overlay、菜单或渲染回调。**

* **菜单**：`HoUnityTools/BIRP Scene MSAA`（勾选状态就是开关状态）
* **Scene 视图**：顶部工具栏上的 `MSAA` 按钮
  （Unity 2021.3 / 2022.3 默认显示在顶部，无需手动拖动）
* **Overlay 列表名称**：`BIRP Scene MSAA`。主动隐藏后可以从 Scene 视图的 Overlay 列表重新勾选。

## 为什么需要它

Scene 视图渲染到它自己的 RenderTexture，那个 RT 的多重采样是**照抄
`QualitySettings.antiAliasing`** 的 —— 而 Quality 是**项目级**设置。
为了在 Scene 里看清锯齿去改 Quality，会连 Game 视图和构建一起改掉，代价太大。

这个开关只动 Scene 视图那个 RT，**一根手指都不碰 `QualitySettings`**。

## 原理

Scene 视图每帧渲染前，直接改它相机 `targetTexture.antiAliasing`：

```csharp
target.Release();
target.antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing);   // 开
target.Create();
```

关键是 **Release → 改 → Create**：`RenderTexture` 创建之后 `antiAliasing` 就定死了，改不动。

只有 RT 换了（换视图 / 改分辨率）才需要重设 —— 每帧都 Release/Create 会把编辑器卡死，
所以每个 SceneView 记了一个 `prevRT` 做比对。关掉时把它改回 `1`（= 不开多重采样）并忘掉这个 RT。

`QualitySettings.antiAliasing` 为 0 表示"不开"，而 RT 只认 1/2/4/8，
所以夹了一下 `Mathf.Max(1, ...)` —— 效果一样，但不会塞个非法值进去。

## ⚠️ 只在 Built-in 管线下有效

SRP（URP / HDRP）自己管 Scene 视图的渲染目标，硬改会打架 ——
所以 `GraphicsSettings.currentRenderPipeline != null` 时，按钮和菜单项一律**置灰**。

**这也是它叫 BIRP 而不是 SceneMSAA 的原因**：它天生就是给 Warudo / VRChat 这类
基本都用 Built-in 的工程用的。

## 状态存在哪

`EditorPrefs`，键 `com.hollow.hounitytools.birp-scene-msaa`
（**每用户**，不进工程、不进版本管理）。

和 Editor Toolbox 放在工程设置里的做法不同 —— 这是个纯视图偏好，没必要跟着工程走。

## 从哪来

对应 `jp.lilxyzw.editortoolbox` 的 `SceneToolbar`（Scene 视图工具栏的 MSAA 开关）——
把它挪进本包之后，那个包就可以整个删掉了。采样逻辑等价，状态和工具栏集成由本包管理：

1. 状态存 `EditorPrefs`，不是工程设置资产；
2. Unity **2022.1 起**使用 `OverlayAttribute.defaultDockZone` / `defaultLayout`，
   默认设为顶部横向工具栏；
3. Unity **2021.3** 在 Unity 恢复 Overlay 布局后，兼容初始化显示状态和顶部停靠。
   通过 Unity 自身的 `RestoreOverlay` 管理布局，不直接插入或移动 UI 元素。

## 2021 / 2022 按钮缺失或位置错误的原因

旧实现把默认停靠参数错误地限制在 `UNITY_6000_0_OR_NEWER`，但 2022.1 已支持这些参数。
两个旧版本的后果不同：

| 版本 | 旧实现的实际状态 | 修复 |
| --- | --- | --- |
| 2021.3 | 没有保存记录的 Overlay 被恢复为 `displayed=false`、`Layout.Panel`，不创建 MSAA 按钮。该恢复路径不读取 Attribute 的 `defaultDisplay=true` | 在恢复布局完成后设置显示状态和顶部横向工具栏 |
| 2022.3 | Attribute 缺少停靠参数，使用默认的右侧列和 `Layout.Panel` | 使用 2022.1 起可用的默认停靠参数 |

所以此前“2021.3 会先出现浮层，拖一次即可”的说明不成立。

已保存的旧默认布局也会迁移一次。迁移标记保存在对应 SceneView 的 Overlay 布局数据中；
之后手动隐藏、移到面板或改为浮动都继续由 Unity 保存，脚本重载不会把它强行拉回顶部。
这个修复只处理 BIRP Scene MSAA 的工具栏集成；URP 使用 HoURP 的扩展能力。

## 验证

回归入口：[`Tests~/BirpSceneMSAA/Run.ps1`](../../Tests~/BirpSceneMSAA/Run.ps1)。

2026-11-20 验证结果：

| Unity 版本 | 结果 |
| --- | --- |
| 2021.3.18f1 | 使用该编辑器自带 Roslyn 和程序集编译，警告视为错误，通过 |
| 2021.3.45f2 | 真实 SceneView 的默认位置、旧布局迁移、开关同步、布局保存及域重载检查通过 |
| 2022.3.22f1 / 2022.3.40f1 | 同上，通过 |
| 6000.0.30f1 / 6000.3.15f1 | 不注册该功能，在已有旧布局的工程中重新编译并完成域重载检查，通过 |

在独立 BIRP 工程中实例化两个真实 SceneView，检查顶部位置、按钮实例、旧默认布局迁移、
开关同步、内置工具栏保留、隐藏/浮动布局保存，以及真实脚本域重载后的恢复。
测试使用独立的偏好键，不修改用户正在使用的 MSAA 状态；同时断言项目抗锯齿设置不变。

Unity 6 的检查反向断言：两个 SceneView 都没有这个 Overlay，程序集里也没有按钮、菜单和渲染回调类型，
并在脚本域重载后再次检查，确保不留下入口。

这些检查以 `-batchmode -nographics` 运行，验证工具栏和布局行为，不验证最终渲染的抗锯齿效果。
