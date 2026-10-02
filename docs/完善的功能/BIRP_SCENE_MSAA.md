# BIRP Scene MSAA —— Scene 视图的多重采样开关

* **菜单**：`HoUnityTools/BIRP Scene MSAA`（勾选状态就是开关状态）
* **Scene 视图**：顶部工具栏上的 `MSAA` 按钮
  （Unity 6 会自动落在工具栏；2021.3 上先以浮层出现，手动拖到工具栏一次即可，之后会被记住）

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
把它挪进本包之后，那个包就可以整个删掉了。逻辑等价，两处不同：

1. 状态存 `EditorPrefs`，不是工程设置资产；
2. `Overlay` 的默认停靠参数按版本门控 —— 实测 Editor Toolbox v2.0.2 的写法
   （`defaultDockZone` / `DockZone` / `Layout`）在 Unity **2021.3 上编译不过**，
   所以新版才带停靠参数，老版退化成浮层。

## 验证

用 Unity 自带 Roslyn，对 **2021.3.18f1 / 6000.0.30f1 / 6000.3.15f1** 三个版本各编一次：
三个都是 **0 error 0 warning**，产物里 `HoUnityTools/BIRP Scene MSAA` 与两个 Overlay ID 都在。

⚠️ 没有上机验证：需要确认工具栏上出现 `MSAA` 按钮、勾上以后 Scene 视图锯齿变化、取消后还原。
