# 快速渲染面板（拍一张 / 离线录一段）

两件事，一个面板：**把当前游戏视图存成图片**，以及**按"多少帧 / 多少秒"离线录一段**。
形态仿 Unity 官方的 Recorder 包（`com.unity.recorder`），但**不依赖它、也没复制它的代码** ——
只借鉴机制，没要它的动画录制 / 音频 / AOV / 在线编码 / 时间轴片段 / `.asset` 配置资产。
定位是"手边的快捷键"，不是"工程化的录制管线"。

## 画面来源：游戏视图 / 指定相机（透明背景）

「高级」最上面一行是**来源**，两个选项：

| 来源 | 拿到什么 | 透明背景 |
| --- | --- | --- |
| **游戏视图**（默认） | 游戏视图的最终合成结果：多相机、后处理、UI 都在里面 | **不可能** —— 见下 |
| **指定相机** | 那一台相机渲进一张带 alpha 的 RT | **可以**（勾「透明背景」） |

### 为什么游戏视图拿不到透明

它走的是 `ScreenCapture.CaptureScreenshotIntoRenderTexture`，抓的是**画到屏幕上的**最终结果。
屏幕（后台缓冲）没有透明通道这回事 —— 相机 clear 成什么就是什么。
所以透明**不可能**从抓屏拿到，只能让相机自己渲进一张带 alpha 的 RT。这是机制问题，不是参数没设对。

### 天空盒：直接算全透明（用户要的语义）

**只设「透明背景」是不够的** —— 那是踩过的坑：天空盒是画在背景**之上**的一层几何
（RenderSettings.skybox 那个材质），相机清屏色再透明也挡不住它，结果就是"背景透明了，天还是实的"。

所以勾上「透明背景」时**会把天空盒整个关掉**，那块直接落到透明清屏色上：

| 借的东西 | 改成 | 为什么 |
| --- | --- | --- |
| `RenderSettings.skybox` | `null` | 天空盒不再被画 ⇒ 那块变透明 |
| `ambientMode` / `ambientLight` | Flat + 中灰 | Skybox 模式失去来源会退化，显式给个中灰免得场景变全黑 |
| `defaultReflectionMode` / `reflectionIntensity` | Custom + `0` | 不清掉的话天空盒会从**反射**里透出来（金属面上那圈亮边） |

**全部在 `finally` 里原样还原**（HoQuickCaptureSkyboxOff）—— 这些是**场景级**设置，
不还原会把整个场景的光照环境改掉，比改一台相机严重得多。

> ⚠️ **已知取舍**：天空盒不只是"画个背景"，它同时是**环境光的来源**。设成 `null` 之后，
> 用 `ambientMode` = Skybox 的工程会临时退化成 Flat + 中灰，所以这一张的间接光与反射
> **和平时不完全一样**。面板上会写这句。
> 想要"照明也完全不受影响"，正路是把天空盒材质换成纯透明（那需要在工程里建材质资源，
> 不是这个面板该擅自做的事）。
>
> 本来就没有天空盒时（`RenderSettings.skybox == null`）这一步整个跳过，也不报警 —— 那种情况结果已经是对的。

### 要透明要同时满足四条（缺一个的症状都是"图是对的、alpha 全是 1"）

1. **来源选「指定相机」**；
2. **勾上「透明背景」** —— 面板会临时把相机改成 `Clear Flags = SolidColor` + 背景 `alpha = 0`，
   拍完**还原**（借了要还）。相机的 `Culling Mask` 也要只留你要的东西，否则背景物件会把 alpha 填满；
3. **存成 `png` 或 `exr`** —— `jpg` 没有 alpha 通道（选了会被当场挡住并提示）；`exr` 的 alpha **永不改写**
   （它的用途就是拿去做合成）；
4. **URP 工程要有 `Allow Post Process Alpha Output`** ——
   后处理会把 alpha 写回 1。这一步在**包外**（你自己的 RP Asset），
   本工程 `Assets/Settings/PC_RPAsset.asset` 的 `m_AllowPostProcessAlphaOutput` 是 **0**，
   所以面板**会在拍透明图时临时打开它**（**只改内存、不写盘**，拍完在 `finally` 里改回去）。

   > ⚠️ **这一条曾经被我删掉过一次，结果立刻出问题，别再删第三次。**
   > 用户说过「不需要做多余的隐式设置，除非是必要的」，我据此把自动打开整个删了、
   > 改成"要透明就自己勾"——**结果背景变成纯色，而且连抗锯齿也没了**。
   > 因为它是"必要的"那一类，不是"多余的"：
   >
   > ```
   > UniversalRenderPipeline.cs:1768
   >   bool allowAlphaOutput = !cameraData.postProcessEnabled
   >       || (cameraData.postProcessEnabled && settings.allowPostProcessAlphaOutput);
   >   cameraData.isAlphaOutputEnabled = cameraData.isAlphaOutputEnabled && allowAlphaOutput;
   > ```
   >
   > 只要后处理参与渲染而开关是关的，URP 就把 `isAlphaOutputEnabled` 置 false
   > ⇒ 最终 blit 不带 `_ENABLE_ALPHA_OUTPUT` ⇒ **alpha 被写成 1**。
   >
   > 而"连 AA 也没了"是**同一个开关的连带效果，不是第二件事**：
   > TAA / SMAA 在 URP 里都属于**后处理**。所以"背景变纯色"恰恰证明
   > **后处理真的跑了** —— 报"纯色 + 没 AA"时别去查两处。
   >
   > 判断标准：一个隐式设置该不该做，看**去掉它功能是否直接不工作**。
   > `Allow Post Process Alpha Output` 属于"不做就不工作"，而且它不写盘、不留痕，做。
   > 反之 **MSAA 采样数不该往 RT 上写**（见下面「抗锯齿」那节）—— 写它反而会盖掉用户特意关掉的 MSAA。

### 渲染管线：直接调 URP，但**不强制依赖**

- 内建管线：camera.Render()。
- URP：camera.Render() **不走 URP**（后处理与透明处理都不参与），要用
  UniversalRenderPipeline.RenderSingleCamera(context, camera)。

**这里是直接调用 URP 函数，不是反射**（用户明确同意：「不弄依赖是因为允许用户不使用我做的变体」——
"能不能调 URP 函数"和"是否强制依赖 URP"是两件事）。做法：

| 位置 | 内容 |
| --- | --- |
| `Editor/HoUnityTools.Editor.asmdef` → `references` | 加上 `Unity.RenderPipelines.Universal.Runtime` |
| 同一个 asmdef → `versionDefines` | `com.unity.render-pipelines.universal` → 定义 `HO_URP_AVAILABLE` |
| 代码 | URP 那几行包在 `#if HO_URP_AVAILABLE` 里 |

于是：**装了 URP 的工程**编到 URP 分支（直接调用，类型安全、能被编译器查到）；
**没装的工程**那段代码根本不参与编译，而 asmdef 里那条引用在 Unity 里只是一个"Missing Reference"提示，
不影响编译 —— 已实测：BreakWarudo（2021.3、内建管线、`HO_URP_AVAILABLE` 未定义）整体 **0 error**。

⚠️ `HO_URP_AVAILABLE` 只保证"装了 URP 包"，**不保证"这个工程在用 URP"**（可以装了却用内建管线），
所以运行时还要看 `GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset`。

#### URP 单相机入口：两个版本两条路

这是**后处理与相机级抗锯齿能不能出来的关键**。入口按版本分：

| 版本 | 入口 | 说明 |
| --- | --- | --- |
| **Unity 6 / 2023.1+** | `RenderPipeline.SubmitRenderRequest(camera, SingleCameraRequest)` | 现代入口，完整走 URP 管线 |
| **2021.3** | `UniversalRenderPipeline.RenderSingleCamera(context, camera)` | 那时 `SubmitRenderRequest` 还不存在 |

- Unity 6 这条是**首选**：`SingleCameraRequest` 的 `destination` 直接指我们的 RT，
  由引擎自己把渲染塞进正常的管线流程，不再需要我们自己拼一个 `ScriptableRenderContext`。
- ⚠️ URP 自己的过期提示里写的是 `UniversalRenderer.SingleCameraRequest`，**那个是写错的**，
  实际类型嵌在 `UniversalRenderPipeline.SingleCameraRequest` 下。
- ⚠️ 2021.3 的 `RenderSingleCamera` 需要一个**由引擎初始化**的 `ScriptableRenderContext`，
  而引擎内部那个拿不到，只能自己 `new` 一个 —— 这在部分 URP 版本上会**静默什么都不画**。
  这个不确定性没法在 2021.3 上消除，所以两条路都一律"渲完验一下"：
  稀疏采样判断整幅是不是单色，空的就退回 `camera.Render()`，并在面板上写明"这张不含 URP 后处理"。
  选择**明确降级 + 说明**，而不是假装成功。

⚠️ **改完 asmdef 后 Unity 必须重新生成 csproj**，否则 `HO_URP_AVAILABLE` 不会出现在
`DefineConstants` 里，URP 分支就是**死代码**，症状是"画面没有后处理"（见「验证」那节）。

### 抗锯齿：不归面板管，跟着相机的设置走

用户明确说过：「**关 MSAA 是有预谋的，我主要依赖 TAA 跟 SMAA**」。
所以这里有一条容易踩反的坑，记下来：

- **不要**往 RT 上写 `RenderTexture.antiAliasing`。URP 取采样数的逻辑是
  （`UniversalRenderPipeline.cs:1446`）：
  ```
  allowMSAA && asset.msaaSampleCount > 1 ? asset.msaaSampleCount : camera.targetTexture.antiAliasing
  ```
  工程把 `m_MSAA` 设成 1 时上半句不成立，于是**落到 RT 上** ——
  我们给 4 就等于把 MSAA 强行开回 4x，**正好盖掉用户特意关掉的东西**。RT 不带采样数，跟随工程设置。
- **TAA / SMAA 不需要我们做任何事**：它们在 URP 里是**相机级**设置
  （`UniversalAdditionalCameraData.antialiasing`，和 `renderPostProcessing` 同一条路），
  只要渲染真的走了 URP 管线，它自己就会应用。
- 所以"没有抗锯齿"和"没有后处理"是**同一个病根**：以前走的是 `camera.Render()`，根本没进 URP。
  修好渲染入口，两个一起好。
- 顺带一条：URP 在**选了基于后处理的抗锯齿模式时会强制关掉 MSAA**
  （`UniversalRenderPipeline.cs:1438-1442` 附近有注释写明），所以 TAA/SMAA 和 MSAA 本来也不该同时开。

### 相机的 targetDisplay

相机渲进 RT 本来跟 `targetDisplay` 无关，但**编辑器里只有 Display.main（index 0）是活的** ——
`Display.displays` 其余项是 `null`，而 `Display` 既不能 `new` 也不能从脚本手动 `Activate`。
所以相机要是挂在 Display 1 上，某些管线下会渲出一张空的。

处理：`targetDisplay != 0` 时**临时挪到主显示**渲，渲完还原。
面板上会留一句"相机挂在 Display N 上，渲染时临时用了主显示"（这条保留 ——
"渲出来和实际那条 display 不完全一样"是用户该知道的）。

### 提示的规矩（用户要求：不要黄字，或者简单一点）

正文里只留三类话，而且都尽量短：

| 留 | 例子 |
| --- | --- |
| **失败** | "指定相机渲出来是空的：确认它启用中、Culling Mask 里有东西……" |
| **结果和你以为的不一样** | "没指定相机，用了 Camera.main。" / "URP 没渲出东西，已退回 camera.Render()（这张不含后处理）。" / "相机挂在 Display N 上，渲染时临时用了主显示。" |
| —— | —— |

**不留**的（都改成了静默）：
· "天空盒已临时关掉" —— 勾了透明本来就是这个预期，不用每次说；
· "已临时打开后处理保留 alpha" —— 同上，正常路径不吭声（**这个功能是必需的，见上面第 4 条**，
  只是它不需要每次都报告）；
· 透明那四条前提的科普 —— 那些在**工具提示**里，不该每次拍完往面板上贴。
（只有**失败**才说话：找不到那个开关、或临时打开失败。）

### 一次删错东西的教训：「必要」和「多余」怎么分

我曾把「后处理保留 alpha」的自动打开判定为"多余的隐式设置"并删掉，理由是
"用户没要求我们动他的资产"。**这个判断是错的**，而且错了两次代价：
删完之后背景变纯色、AA 也没了（两者同源，见上面第 4 条）。

正确的判据是**去掉它功能是否直接不工作**：

| 隐式动作 | 去掉会怎样 | 结论 |
| --- | --- | --- |
| 临时打开 `Allow Post Process Alpha Output` | **透明直接坏掉**（alpha 被写成 1） | **必要**，做（只改内存、拍完还原） |
| 临时关掉天空盒（`RenderSettings` 一组） | **透明直接坏掉**（天空被填满） | **必要**，做（场景级、播放模式才落盘、`finally` 还原） |
| 往 RT 上写 MSAA 采样数 | 会**盖掉**用户特意关掉的 MSAA，反而更糟 | **有害**，不做 |
| 临时把 `targetDisplay` 挪到 0 | 挂在 Display N 上的相机会渲出空图 | **必要**，做（渲完还原，且会说明） |

所以"不要多余的隐式设置"这条原则仍然成立 —— 只是**判据是必要性，不是"动没动用户的东西"**。
真正要避免的是第三种：**做了还让结果变差**的那种。

## 输出格式

| 动作 | 格式（枚举，行内选） | 默认 |
| --- | --- | --- |
| 截帧 | `png` / `jpg` / `exr` | **png** |
| 录制 | `mp4` / `PNG 序列` | **mp4** |

两种都走 Unity **自带**的编码能力，**零依赖**：

- 图片：`Texture2D.EncodeToPNG / EncodeToJPG / EncodeToEXR`。
- 视频：`UnityEditor.Media.MediaEncoder`（Windows 上底层是 Media Foundation），
  H.264 写进 `.mp4`。**不需要 ffmpeg、不需要装任何东西。**

> ⚠️ 关于"自带一份 ffmpeg"：**这条路走不通，所以没走**。
> 一份静态 `ffmpeg.exe` 有 100 MB 以上，**超过 GitHub 单文件 100 MiB 的硬上限，根本提交不进仓库**；
> 换 Git LFS 更糟 —— Unity 官方文档明确不建议把"包的必需资产"放进 LFS
>（"Avoid placing essential package code or assets under LFS tracking"），
> 而且 LFS 配额用尽时只会取回指针文件。
> 而 Unity 自带的编码器已经能出 H.264 MP4，于是这个依赖整个不需要了。

**没有 MOV**：Unity 的编码器只认扩展名，而它能写的容器只有 `.mp4`（H.264/H.265）与
`.webm`（VP8）—— 它写不了 `.mov`（MOV/ProRes 是 Recorder 包自带的一个原生插件，不在引擎里）。
与其放一个必然失败的选项，不如不给。

## 界面

**刻意做得很小**：两行干完活，其余全在一个默认收起的「高级」里。正文里不写说明文字 ——
所有解释都在**悬浮工具提示**里（鼠标停上去就有）。形态向隔壁播放速度条看齐，
所以**没有**套用约束面板那套栅格 / 卡片 / 分区标题。

```
[截帧]  png  1920x1080   D:/captures/HoCapture        […] [↗]
[录制]  mp4  5.00  [s|帧]                        150 帧
▸ 高级
```

| 行 | 内容 | 提示在哪 |
| --- | --- | --- |
| 第一行 | `截帧` 按钮 · **图片格式** · **当前出图分辨率** · **输出目录**（可直接编辑）· 选目录 · 在文件管理器打开 | 每一个控件上 |
| 第二行 | `录制` 按钮 · **视频格式** · **时长** · 单位（秒 / 帧）· 换算出的帧数 | 同上 |
| 录制中 | 第二行原地换成 `暂停`/`继续` · `停止` · 进度条 · 百分比 · 已录游戏时间 | 同上 |
| 第三行 | `▸ 高级`（**默认收起**，展开状态记在 EditorPrefs） | —— |

「高级」里是：画面来源（游戏视图 / 指定相机 + 相机 + 透明背景）、视频参数（码率 / H.264 档次 / 关键帧间隔）、分辨率模式（跟随视图 / 自定义 + 宽高）、
帧率（24/30/60 快捷钮）、目录结构（每段一个子目录 / 完成后打开 / 顺带改视图）、
文件名前缀、图片质量、缩略图、定位最近产物 / 复制路径、恢复默认。

> 正文里只有两样东西不是提示：**报错**和**录制进度**。那两样是"现在发生了什么"，
> 藏进 tooltip 等于没说。报错还带一个 ✕ 可以手动清掉。

## 入口

| 想要什么 | 去哪 |
| --- | --- |
| **面板**（截图 + 录制） | 菜单 `HoUnityTools/快速渲染` |
| 配置存哪 | EditorPrefs 键 `com.hollow.hounitytools.quickcapture`（**每台机器一份、不进仓库**）；「高级」展开状态存同名前缀的 `.advanced` |
| 产物放哪 | 第一行那个文件夹框，**可以留空**（= 工程根目录下的 `HoQuickCapture`）；相对路径按工程根目录解析 |
| 代码 | `Editor/QuickCapture/`：`HoQuickCaptureWindow`（面板）· `HoQuickCaptureEngine`（状态机 / 落盘 / 收尾）· `HoQuickCaptureMediaEncoder`（MP4 编码）· `HoQuickCaptureGameView`（游戏视图分辨率）· `HoQuickCaptureSettings`（数据与落盘）。`Runtime/QuickCapture/`：`HoQuickCaptureDriver`（帧泵）· `HoQuickCaptureFormats`（运行时枚举）· `HoQuickCaptureNaming`（文件名规则） |

> ⚠️ **帧泵为什么在 `Runtime/` 而不是 `Editor/`**：它是个 MonoBehaviour，而
> **编辑器程序集里的 MonoBehaviour 挂不到物体上** —— `AddComponent` 会直接报
> *"Can't add script behaviour 'HoQuickCaptureDriver' because it is an editor script.
> To attach a script it needs to be outside the 'Editor' folder."*（踩过一次）
> 依赖方向只能是 Editor → Runtime，所以帧泵要用的类型（枚举、文件名规则）也跟着放 Runtime；
> "这段录完了"则反过来由引擎**以回调形式**传进去 —— 帧泵不认识引擎，是引擎认识帧泵。
> 帧泵这侧不引 `UnityEditor`，所以它在播放器程序集里也是干净的（虽然它只在编辑器里会被用到）。

## 30 秒上手

1. **截帧在播放模式与编辑模式下都能用**；**录制只在播放模式下**（原因见下节）。
2. **拍一张**：点「截帧」。存到输出目录，文件名带拍摄时刻（`前缀_2026-02-05_14h30m12s.png`），
   所以连拍几张不会互相覆盖、也不会出现全零帧号那种让人以为是序列的名字。
3. **录一段**：按 **Play**，选格式（默认 `mp4`）、填时长、点「录制」。
   行尾那个「N 帧」就是你会拿到的帧数。录的时候可以**暂停 / 继续**；
   「停止」会收好已经录到的，**不删文件**。
4. 默认每录一段建一个 `take_日期_时间` 子目录，上一段不会被覆盖。帧率在「高级」里。

## 两种模式下的行为

| | 播放模式 | 编辑模式 |
| --- | --- | --- |
| **截帧** | 完整的游戏视图合成结果 | **能用**。同样先试合成结果；拿不到就退回渲主摄像机（那条路没有 UI、没有多相机叠加，面板会写明） |
| **录制** | 帧随时间前进，录的是**会动的画面** | **不让录**：按钮灰掉，提示"先按 Play" |
| 怎么拿到"画完的那一帧" | `MonoBehaviour` + 协程 `yield return new WaitForEndOfFrame()` | **自己催一帧**，见下 |
| 暂停 / 继续 | 有 | 不涉及 |

> **录制为什么故意只在播放模式**：编辑模式里时间不前进，催帧也只是把同一张画面重画一遍，
> 录不出"在动的东西"。曾经做过一个"把同一张按目标帧数写进去"的定格版本，**砍掉了** ——
> 那个设计既没用又费事。与其给一段定格的怪文件，不如直接把按钮挡掉、把原因写在提示里。

### 编辑模式是怎么"催帧"的

编辑模式下**游戏视图平时根本不重画** —— 它只在你动它或场景变化时才画一次。
所以想拍就得主动催（`HoQuickCaptureEditModePump`）：

1. `InternalEditorUtility.RepaintAllViews()` —— 让游戏视图排队重画；
2. `EditorApplication.QueuePlayerLoopUpdate()` —— 让播放循环（含摄像机渲染）真的跑一次；
3. 等**下一个** `EditorApplication.update` tick 再确认到底有没有画上去。

不催就没画面；催完当场抓会抓到上一帧。头几次扑空是正常的，所以会重试（上限 12 次），
并且用一个"整幅是不是单色/全黑"的稀疏采样判断这次到底有没有画上去。

#### ⚠️ 但「抓」这一步**不能**在第 3 步做（踩过的坑）

用户报过：「直接截游戏视图不播放时会直接截到**错误的编辑器区域的绘制**」。
根因是时序。`EditorApplication.update` 这个 tick 的完整顺序是：

    ① 我们 QueuePlayerLoopUpdate() → 播放循环跑一次，游戏视图渲进**后台缓冲**
    ② 编辑器各视图**重画** → IMGUI 把编辑器界面画到**同一块后台缓冲**上
    ③ 才轮到我们的 update 回调

而 `ScreenCapture.CaptureScreenshotIntoRenderTexture` 读的就是**当前后台缓冲**。
所以在第 ③ 步抓，抓到的是**编辑器界面**，不是游戏视图 —— 症状就是"截到了旁边那块编辑器区域"。

**修法：把"抓"挪进渲染回调**，管线一渲完立刻抓，那会儿后台缓冲里还是游戏视图。
两条管线各挂一个（不知道用户用哪条）：

| 管线 | 回调 |
| --- | --- |
| SRP（URP / HDRP） | `RenderPipelineManager.endFrameRendering` |
| 内建 | `Camera.onPostRender` |

这个回调是**同步**的、就在渲完之后，所以 `TryCaptureInRenderHook` 里必须**当场抓完**
——回调一返回，编辑器马上就会把界面画上去。

> ⚠️ `endFrameRendering` 的委托签名**跨版本不一样**：2021.3 是
> `Action<ScriptableRenderContext, Camera[]>`，Unity 6 是
> `Action<ScriptableRenderContext, List<Camera>>`。
> 所以 lambda 要**存进一个字段再挂**，这样既能让编译器推断出当前版本的签名，
> 又能在注销时用同一个实例 `-=` 掉。
> **别写成 `+= (a, b) => ...`** —— 那样每次都是新委托实例，`-=` 摘不掉，
> 每抓一次就永久多一个订阅（泄漏）。

编辑模式下"催帧"仍然要（第 1、2 步），只是第 3 步从"抓"变成了"等回调"；
如果回调一直不来（游戏视图没开着、或管线不上报），重试到上限后
退到"渲一台相机"，并明确告诉用户这张不是游戏视图合成结果。

抓画面本身仍然优先用 `ScreenCapture.CaptureScreenshotIntoRenderTexture` —— 和播放模式**同一个调用**，
两条路拍出来的东西才一致。只有它拿不到画面时才退到相机渲 RT。

> ⚠️ 编辑模式是"尽力而为"：游戏视图最小化 / 被完全挡住时可能催不出画面，
> 那时会重试到上限然后报错说明，不会静默卡住。

## 「不是真实时间」是怎么做到的

这是这个面板最要紧的一条，也是它和"录屏"的根本区别。

录制期间把 **`Time.captureDeltaTime` 设成 `1 / 帧率`**。它的语义（Unity 文档原文）是：

> If this property has a non-zero value then **Time.time increases at an interval of captureDeltaTime
> (scaled by Time.timeScale) regardless of real time and the duration of a frame.**

也就是说：**每画完一帧，游戏时间就前进恰好 `1/帧率` 秒**，跟这一帧实际画了多久完全无关。

| | 真实时间录制（录屏） | 本面板（离线步进） |
| --- | --- | --- |
| 30fps 录 5 秒 | 墙上时间 5 秒，机器跟不上就**丢帧** | 出 **150 帧**，录了 3 分钟还是 30 秒都一样 |
| 机器慢的后果 | 画面一顿一顿 / 少帧 | 只是录得久一点，**序列一帧不少** |
| 逐帧确定性 | 没有 | 有：同样的第 N 帧 = 同样的游戏时间 |

具体后果：`5 秒 @30fps` **一定**是 150 帧。你在面板上看到的「已录」读数走的是**游戏时间**，不是墙上时间。

> `Time.captureDeltaTime` 是 **Unity 2019.2** 引入的（不是新 API），2021.3 与 Unity 6 都有。
> 本面板**不用** `Time.captureFramerate`：它的 getter 会 `Mathf.Round` 成整数，
> 表示不了 23.976 / 29.97 / 59.94。Unity Recorder 自己也不用它（整个包里只把它 `= 0`）。

## 暂停的语义

「暂停」= `Time.timeScale = 0`：**模拟冻结，但画面继续画**。所以暂停时你还能看到当前这一帧，
而不是整个编辑器卡住（那是工具栏那个暂停，两者不一样）。

- 暂停期间**不抓帧**，但帧泵继续每帧转一圈 —— 这样「继续」能在下一帧立刻接上。
- 暂停是**可逆**的：继续之后游戏时间从暂停处接着走，序列里不会多出重复帧，也不会缺帧。
- 退出播放 / 关面板 / 脚本重编译时，暂停状态与 `captureDeltaTime` 都会被还原。

### `Time.timeScale` 必须是 1

`Time.captureDeltaTime` 是**被 `Time.timeScale` 缩放**的（Unity 文档原文：
"Time.time advances at an interval of captureDeltaTime, **scaled by Time.timeScale**"）。
所以"离线步进"的步长要想等于 `1/帧率`，进录制时 `timeScale` 就必须是 1。面板会挡住这两种情况：

| 情况 | 后果 | 面板怎么做 |
| --- | --- | --- |
| 游戏自己的暂停菜单把 `timeScale` 设成 0 | 游戏时间根本不往前走，**一帧都拍不出来** | 准入检查挡住，提示先让它恢复 |
| 慢动作 / 快进把 `timeScale` 设成 0.5 / 2 | 每帧只前进 `timeScale/帧率`，步长对不上 | 准入检查挡住，提示设回 1 |

这条挡的是"**游戏自己**暂停"，和面板的「暂停」是两回事：面板暂停时是自己把原值存下来再置 0，
继续时还回去，全程有账本。

## 分辨率

| 模式 | 出图 | 什么时候用 |
| --- | --- | --- |
| **跟随游戏视图**（默认） | 和游戏视图当前的**渲染**分辨率一致 | 就想"我看到的什么样，出来什么样" |
| **自定义** | 自己填宽高 | 要固定的出图尺寸 |

抓帧用的是 `ScreenCapture.CaptureScreenshotIntoRenderTexture`，拿的是**最终呈现给用户的那一帧**：
多相机合成、后处理、Screen Space–Overlay 的 UI 全都在里面，不是某个相机的裸渲染。
这个调用和 Unity Recorder 的 GameViewInput 用的是同一个 —— 它的输出分辨率就取自你传进去的那张
RenderTexture 的尺寸。

选「自定义」时会多一个「**同时改游戏视图**」开关（默认开）：

- **开**：录制前临时把游戏视图切到你填的尺寸，录完**自动还原**。
  为什么需要它：游戏视图比目标小的话，抓到的画面被放大 → 糊。
- **关**：游戏视图不动，出图会被拉伸。

> **Unity Recorder 在这里是不还原的**（它官方文档明说"录完游戏视图不会自动回到之前的分辨率，
> 要还原请用游戏视图控制栏里的 Aspect 下拉"）。本面板不学那一条：借了东西要还。
>
> **还的是像素尺寸，不是下拉里那个选项**：录制结束后游戏视图会停在"自定义分辨率"模式下、
> 尺寸已经还原成你原来的值。要完全回到原来的下拉选项（比如 `Free Aspect`），
> 在游戏视图左上角那个下拉里点一下即可。这是已知边界，不是没还干净。

## 四条硬规矩（改代码前先读）

1. **"画完的那一帧"只能靠两条路拿到，没有第三条**。
   播放模式：协程 `yield return new WaitForEndOfFrame()` 卡在渲染结束之后 ——
   编辑器的 `OnGUI` 早于渲染、`EditorApplication.update` 拿的是上一帧，两个都不是那个时间点。
   编辑模式：**自己催帧**（`RepaintAllViews` + `QueuePlayerLoopUpdate`，再等下一个 update tick），
   见上面「两种模式下的行为」。
   想加新的抓帧路数，先想清楚"它落在帧的哪个时刻"，别拿 `OnGUI` / 普通 update 凑数。
2. **有借有还，且每条路径都要还**。借出去四样：`Time.captureDeltaTime`、`Time.timeScale`、
   游戏视图分辨率、那个隐藏的帧泵物体。收尾路径有五条：正常录完 / 手动停 / **退出播放** /
   关面板 / **脚本重编译（域重载）**。漏掉"退出播放"这条的症状最典型：
   用户录到一半直接按停止播放，工程被留在 `captureDeltaTime` 非 0 的状态里，
   下次进播放游戏时间就走得莫名其妙。
   （驱动那边 `OnDisable` / `OnDestroy` 还会再还一次，还原是幂等的。）
3. **`Time.captureDeltaTime` 是全局的，一个时钟只能有一个主人**。
   见下面「和 Unity Recorder 打架」。
4. **只有 Game 视图能改渲染分辨率**。设备模拟器（Simulator）视图下
   `SetCustomRenderingResolution` 只打一条日志然后什么都不干 —— 所以写之前先问 `GetViewType()`。

## 和 Unity Recorder 打架（重要）

两个包都要写 `Time.captureDeltaTime`，**同时跑就不对了**。已核实的两种冲突方式
（都在 Recorder 5.1.6 源码里）：

- **Variable 模式**：`Recorder.ResetDeltaTime()` 会把 `Time.captureDeltaTime` **写成 0** ——
  它的 `m_FrameInterval` 只在 Constant 模式下被赋值，Variable 下是 0，
  于是"把填充去掉"那一步直接把时钟清零。发生在**第二次非跳过帧**上。
- **Constant 模式**且帧率与我们不同：它会覆盖成自己的帧率，并且往控制台**报一条红错**
  （错误挂在我们这边，用户很难联想到是这个面板）。

结果都一样：**离线步进没了，录制退化成按真实时间走**。

所以本面板的防线是**每帧对一次步长**：相邻两帧的游戏时间差应该正好是 `1/帧率`，
差出半个帧就当场报错收摊，并说明"最常见的原因是本工程同时开着 Unity Recorder"。
已经录到的帧不受影响 —— 宁可停下来说清楚，也不要给用户一段节奏不对的序列。

> 面板上还写着一句静态提醒（点了「开始录制」就能看到）。**没有**做"自动检测 Recorder 是否正在录"：
> 想过用反射建一个 `RecorderController` 去问 `IsRecording()`，但那样建出来的实例
> 跟真正在录的那个会话没有任何关系（它自己的会话列表是空的），拿不到真实状态；
> 与其留一个永远返回"没在录"的假检测，不如把力气放在**每帧都真的能验出来**的步长校验上。
>
> 反过来说：**别指望用本面板和 Recorder 一起录同一个工程**。要一起用就先把 Recorder 停掉。

## 帧泵可能"卡住"的三种情况（都不是 bug，是机制限制）

抓帧整个建立在 `WaitForEndOfFrame` 上，所以**只要有东西让它等不到，帧泵就会一直等下去**——
而 Unity Recorder 的文档把这个坑写得很清楚：*"If this happens during a recording, the Recorder stays
in a waiting state but the simulation keeps moving forward, and so does the game clock."*

| 情况 | 表现 | 本面板怎么处理 |
| --- | --- | --- |
| **游戏视图被挡住 / 最小化** | 等不到 `WaitForEndOfFrame`；帧泵挂着，**而游戏时钟照走** → 静默丢帧 | **8 秒**没出过任何一帧就报错并**强制收摊**，提示把 Game 视图切到前台。这个阈值只用于报错，所以定得宽：4K + 光追那种一秒一帧的工程不该被误判 |
| **`-batchmode`（CI / 批处理）** | 图形管线根本没初始化，官方文档写明 `WaitForEndOfFrame` 在批处理模式下**不会运行**；Recorder 的 KnownIssues 里也写着"recording never starts" | 准入检查**直接挡住**并说明原因，不让它挂在那儿 |
| **编辑器工具栏那个暂停** | 画面不画了 | 准入检查挡住，提示先取消 |

⚠️ 看门狗必须**强制**收摊（直接 `FinishSession`），不能只调 `Stop()`：
`Stop()` 是"礼貌"路径 —— 置个标志等帧循环下一圈自己看到。
而看门狗要处理的**正是循环卡在 `WaitForEndOfFrame` 上根本回不来的情况**，
那种情况下标志没人读，状态会永远停在"录制中"、`captureDeltaTime` 一直借着、按钮全点不动。
（这条是两个独立审查都点出来的同一个洞，最初实现里确实写错了。）

**结论**：录的时候把 Game 视图放在前台看得见的地方。这不是本面板的缺陷 ——
Unity 官方的 Recorder 有完全一样的限制（它的 `KnownIssues.md` / `RecordingAccumulation.md` 里写着）。

## 上下颠倒这件事（踩了三次，按"这个 RT 怎么来的"分路处理）

**症状**：截帧出来的图和录出来的 MP4 **同时**上下颠倒。

**根因**：`RenderTexture` 与 `Texture2D` 的**坐标原点本来就不同**
（`Graphics.CopyTexture` 的文档原话：*"the coordinate origin of a RenderTexture is in the
lower left corner while the origin of a Texture2D is in the upper left corner"*），
而 `RenderTexture.ReadPixels` + `Texture2D.GetPixels32` 拼出来的数组到底是哪个行序，
**Unity 的文档没有写**。再叠上第三个约定：

| 谁 | 行序 |
| --- | --- |
| `ReadPixels` + `GetPixels32`（读回来） | **文档没写** ← 问题就出在这一格 |
| `Texture2D.SetPixels32` / `MediaEncoder.AddFrame` | 第 0 行 = **底边**（Unity 纹理行序） |
| `EncodeToPNG / EncodeToJPG / EncodeToEXR` | 按**数组顺序**当扫描行写，顶边在前才存得正 |

**所以这件事不靠推理，靠实测**。实测结论（**2026-09-30，Windows / D3D11**）：

> ### **游戏视图抓屏**（`ScreenCapture.CaptureScreenshotIntoRenderTexture`）交回来的原始数据是「第一行 = 图像顶边」。
> ### **相机自己渲出来的 RT 是反的**（第一行 = 图像底边）。

第二条是加了「指定相机 + 透明背景」之后才发现的：同一套翻转逻辑下，**游戏视图那条出图是正的，
相机那条上下颠倒**。所以行序不能当成一个全局结论 —— **它跟这个 RT 是怎么来的有关**。

现在由 `TryBuildFrameFrom(source, sourceIsFlipped, …)` 分路传进去，三条路各自都对：

| 路 | `sourceIsFlipped` | 画面怎么进 RT 的 |
| --- | --- | --- |
| 播放模式帧泵 | `false` | 游戏视图抓屏 |
| 编辑模式帧泵 · 游戏视图 | `false` | 同上（抓不到时"退回渲相机"会传 `true`） |
| 编辑模式帧泵 · 指定相机 | `true` | `camera.Render()` / `UniversalRenderPipeline.RenderSingleCamera` |

判定写成 `rawIsTopDown = !sourceIsFlipped`，于是**每条路仍然各自只翻一次**。

这条一开始按文档那套"底边在前"实现，出图整个上下颠倒；翻过来就正了。
代码里现在**就按这个结论写死**。

（中途临时做过一个「上下翻转」开关用来试 —— 试出来之后就删了，
需求是"别让我选，你就弄对"。开关那版的做法留在 git 历史里，
真要再用可以照抄：**加设置项比一次次改代码猜快得多**。）

⚠️ **换机器 / 换图形后端（D3D12 / Vulkan / OpenGL）时，如果出图又倒了**，
说明这一格因后端而异。那时候**不要再靠猜改代码**（这个坑连着踩过**三次**），
直接照当时那个开关的做法加回一个设置项，让人一键切。

**代码上的规矩**（避免"两边都以为对方会翻"这种错 —— 这个错连犯过两次）：

- `ReadbackPixels` **只负责把原始数据交出来，绝不翻转**。
- `CaptureNow` 里按上面的实测结论**一次性**算出两份，**整个流程只翻一次**：
  - `PixelData` / `GetTopDownPixels()` —— **顶边在前**（就是原始数组本身），给图片编码；
  - `BottomUp` / `GetBottomUpPixels()` —— **底边在前**（复制 + 翻转），给纹理 / 视频。
- `Texture2D` 用 `BottomUp` 那份建，所以**缩略图、图片、视频看到的始终是同一张图**。
- 改这块之前先想清楚"这份数组的下一站是谁"。

## 产物

- **MP4**（默认）：`前缀_2026-02-05_14h30m12s.mp4`，H.264 + `yuv420p`，时间戳按**帧号**算
  （`帧号 / 帧率`），所以时间轴一定是等间距的 —— 不会因为某一帧渲染特别慢而在视频里留个空洞。
  **封口那一步掉了文件就是坏的**：编码器要 `Dispose` 才会把索引写完，少了它文件时长是 0、
  播放器打不开。所以正常录完 / 手动停 / 退出播放三条路径都会走到收尾。
- **PNG 序列**：`前缀_000000.png`、`前缀_000001.png`…… 帧号一律 **6 位补零**，
  所以 `ffmpeg`/AE/达芬奇能直接按顺序吃进去：

  ```
  ffmpeg -framerate 30 -i HoCapture_%06d.png -c:v libx264 -pix_fmt yuv420p out.mp4
  ```

  为什么是 6 位而不是更常见的 4 位：帧数上限是 100 万，4 位一到 10000 帧就会出现
  "`_10000.png` 排在 `_9999.png` 前面"的字典序错乱，而且 `%04d` 只认四位、到 9999 就停。
  6 位在这个上限内**永远不会变宽**。
  （这个格式就是为"自己再拿 ffmpeg 合成"准备的 —— 面板本身不需要 ffmpeg。）
- **单张截图**：`前缀_2026-02-05_14h30m12s.png`（本地时间，`14h30m12s` 是为了避开
  Windows 文件名里不能有冒号）。时间戳一律 `InvariantCulture`，免得跟着系统区域变成别的样子。
- **抓帧失败不留空洞**：某一帧读回失败时**不吃帧号**，循环直接重试那一帧。
  早先的实现会把失败也当成一帧交出去，于是序列里出现 `000000 / 000002 / 000003…`，
  `ffmpeg -i prefix_%06d.png` 到空洞就断，用户拿到一段被截断的视频还不知道为什么。
  失败原因会显示在面板上（并注明"在重试，序列不会断"）。
- **alpha 一律写成不透明**（255）：游戏视图抓下来的东西本来就该是不透明的
  （Unity Recorder 甚至专门做了个 MakeOpaque 着色器保证这一点）。**EXR 例外** ——
  它的用途就是拿去做合成，alpha 是有效数据，改了反而是错的。
- **图片质量**（默认 50）对 PNG 是压缩力度、对 JPG 是有损质量（面板会把方向翻过来，
  两边的"数字越大越好"是反的）。`EncodeToPNG(int quality)` 是 **Unity 2022.1 才有的重载**，
  2021.3 上自动退回无参版本（走反射探测，见 `HoQuickCaptureEngine.SupportsPngQuality`）。
  录长片段想快就调到 0~20 —— 编码在主线程上做，这个值直接决定每帧多停多久。

## 已知边界

- **MP4 要求宽高都是偶数**（H.264 的 `yuv420p` 限制）。面板会在开录前挡住奇数分辨率
  并告诉你怎么改（自动给出相邻的偶数），而不是让它录到一半才炸。
- **MP4 只在编辑器里有**：`MediaEncoder` 属于 `UnityEditor`，播放器构建里没有。
  这个面板本来就整个是编辑器工具，所以不影响；但以后要做"运行时录制"就不能用这条路。
- **MP4 不支持 Linux 编辑器**（Windows / macOS 可以；Unity 自己的 XML 文档写着
  "for macOS and Windows only"）。PNG 序列不受影响。
- **`RenderTexture.active` 是全局状态**，读像素那一步必须还回去（驱动里 `ReadbackPixels` 的 `finally`）。
  不还的话后面所有 `Graphics.Blit` / 相机渲染都会写错地方。
- **游戏视图分辨率有静默钳制**。Unity 内部对渲染目标做过显存启发式限制，超了会打
  `GameView reduced to a reasonable size for this system` 并把尺寸压下来。
  所以填 8K 不保证真出 8K —— 看面板上的出图尺寸读数，或直接看产物。
- **游戏视图的 Scale 滑杆不影响抓帧分辨率**（它只是显示缩放）。真正影响渲染分辨率的只有
  分辨率下拉和「Low Resolution Aspect Ratios」。
- **输出目录如果在 `Assets/` 里**，产物会被 Unity 当成资产导入（一段 150 帧的序列就是 150 张全尺寸
  Texture，导入很慢很占内存）。收尾时会自动 `AssetDatabase.Refresh()`，但**别把序列往 Assets 里放** ——
  默认目录（工程根目录下的 `HoQuickCapture`）刻意在 `Assets` 外面就是为了这个。
- **自定义分辨率没有做显存保护**：填 16384x16384 会在抓帧时一次性要好几 GB，可能分配失败。
  超过 8K 请自己掂量。
- **配置在 EditorPrefs**，换机器 / 换 Unity 账号就是另一套；想清干净就删掉那个键。
- **`.meta` 由 Unity 自己生成**（GUID 以它给的为准），不要手写。
- **TAA 是"时间上"的抗锯齿，单帧/快速移动会有残影**。它的做法是把**连续多帧**抖动过的
  画面累积起来，所以：
  - **机位静止**时最干净（这也是拍静帧的常见用法）；
  - 相机或物体**动得快**时，那一帧可能带着上一帧的残留（鬼影/拖影），
    这是 TAA 的固有行为，不是本面板的 bug；
  - 我们**不重置** TAA 的历史（`TAA` 的 history 在 URP 内部），因为重置反而会让它"没来得及收敛"
    就出图，比现在更糊。录制时是离线逐帧推进的，帧与帧之间是连续的，所以序列整体是自洽的。
- **抗锯齿与 MSAA 无关**（本工程靠 TAA / SMAA）：面板**不提供** MSAA 设置，也不该提供 ——
  见上面「抗锯齿：不归面板管」那节，往 RT 上写采样数会盖掉工程特意关掉的 MSAA。

## 验证

- **已做**：拿两套 Unity 的托管程序集（`2021.3.45f2` 与 `6000.3.15f1`）用 Roslyn 把
  `Editor/` + `Runtime/` 整体编了一遍。两条版本分支都编到了：
  `UNITY_2022_2_OR_NEWER` 下的 `PlayModeWindow` 路，以及 2021.3 的反射路。
  另外单独编了一遍 `QuickCapture(6) + 共享控件(2) + Runtime(42)` 的隔离集：**0 error**
  （这样即便整个工程因为别处的历史问题编不过，也能确定本模块自己是干净的）。
- **已做**：Unity 的资产管线已导入 `Editor/QuickCapture/`（六个 `.meta` 由 Unity 生成），
  编辑器日志里没有 `error CS`。
- **已做**：两轮独立代码审查（一轮常规、一轮对抗式），产出的问题已全部修掉。其中三条是**首次使用
  就会踩**的：
  1. **看门狗混用了两个钟**（心跳取 `Time.realtimeSinceStartup`，判定取
     `EditorApplication.timeSinceStartup`）。前者进播放归零、后者不归零，两个一减就是几千秒，
     于是**每一段录制都会在 8 秒左右被误判成"游戏视图没在渲染"并砍断** ——
     默认的 5 秒 @30fps 在 1080p 下墙上时间本来就要十几秒，所以默认配置必踩。
     现在心跳与判定统一用编辑器时钟。
  2. **看门狗只调 `Stop()` 收不了摊**（见上面「帧泵可能卡住」那节的 ⚠️）。
  3. **单张截图全都写成 `_0000.png` 互相覆盖**：驱动那边一律用帧号命名，
     而单张的帧号恒为 0，`BuildScreenshotFileName` 成了死代码 —— 与面板上
     "文件名带拍摄时刻，不会覆盖上一张"的说法自相矛盾。现在单张走时间戳。
- **未做**：鼠标交互与真机出图/出片没有在编辑器里逐项点过。上机第一遍建议照这个清单走：
  1. 编辑模式下打开面板：面板应该**只有两行 + 一个「高级」**，正文里没有说明文字；
     两个按钮都是"不能点"的，鼠标停上去的提示里写着原因（没在播放 / 编辑器暂停了 / timeScale 不是 1…）。
  2. 第一行右侧的分辨率读数应等于游戏视图当前的渲染分辨率；目录框里填的路径能编辑、能用 `…` 选。
  3. 按 Play → 「截帧」：输出目录里应出现一张 PNG；**再截一张应出现第二个文件**（文件名不同），
     不是覆盖第一张。展开「高级」应能看到缩略图。把格式切 `jpg` / `exr` 各截一张，
     扩展名应跟着变。
  4. **MP4（默认格式，最要紧的一条）**：时长填 `2`、单位 `s`、帧率「高级」里设 30
     （行尾应显示 `60 帧`）；点「录制」。
     - 应生成一个 `.mp4`，**能直接用播放器打开、时长显示 2 秒**。
     - 画面应是**正的**（不是上下颠倒），颜色正常（不是发灰或过暗）。
     - 如果文件打不开、时长 0，说明**封口那一步没走到** —— 那是收尾路径的 bug。
     注意墙上时间**不**等于 2 秒；这一段如果超过 8 秒（1080p 很正常），
     **不该**被任何"没有拍到帧"的报错打断；被打断就是看门狗又坏了。
  5. 切「PNG 序列」录同样的 60 帧：文件名应是 `前缀_000000.png`~`前缀_000059.png`，
     中间没有缺号。
  6. 录制中第二行应变成 `暂停 / 停止 + 进度条 + 百分比 + 已录秒数`；
     点「暂停」画面冻住但还在画，点「继续」帧号接着走，总数仍是 60。
  7. 录到一半点「停止」：**MP4 也要能打开**（时长会短于 2 秒，但不该是 0）。
  8. 录到一半直接**停止播放**（工具栏）：控制台不该有异常；MP4 同样应能打开；
     然后在控制台跑 `Time.captureDeltaTime`，**应该是 0**（还回去了）。
  9. 「高级」里切「自定义」1920x1080 + 勾上「顺带改视图」录一小段：
     录的时候游戏视图应变 1920x1080，录完尺寸自动还原（下拉会停在"自定义分辨率"，见上面的说明）。
  10. 把分辨率设成奇数（比如 1921x1080）起录：应**当场拒绝**并提示改成 1920x1080，
      而不是录到一半才炸。
  11. 把 Game 视图拖到 Scene 视图后面盖住 → 起录：约 8 秒后应报错说"没有拍到任何一帧"，
      而且**状态要真的回到空闲**（按钮重新可点）—— 不能挂在"录制中"上。报错行右边的 ✕ 能清掉它。
  12. 连点两次「录制」（隔一秒以内）：应得到两个不同名的 `take_*` 子目录，不是同一个。
  13. 把「高级」展开着关掉窗口再打开：应该记着展开状态（存 EditorPrefs）。
  16. **URP 分支真的被编到了**（改了 asmdef 之后要确认）：让 Unity 重新生成 csproj，
      然后查 HoUnityTools.Editor.csproj：
      - `<DefineConstants>` 里应该有 `HO_URP_AVAILABLE`；
      - 并且引用了 URP —— 形式是 `<ProjectReference Include="Unity.RenderPipelines.Universal.Runtime.csproj" />`。
        ⚠️ 它**不是** `<HintPath>` 形式（URP 是本地 `file:` 包，走的是工程引用），
        所以别拿"HintPath 里搜不到 Universal"当作没引用的判据。
      - 已实测（BREAK_URP，Unity 6000.3.15f1）：两项都成立，
        且 `UNITY_2023_1_OR_NEWER` 也在 defines 里 —— 也就是说编的是
        `SubmitRenderRequest` 那条新路，不是 2021.3 的 `RenderSingleCamera`。
      没有的话说明 versionDefines 没生效，URP 那条路会一直走内建分支（症状：画面不含后处理）。
  16. **透明背景**（四条逐个验）：
      - 「高级 → 来源」切「指定相机」，拖一台相机进去，勾「透明背景」，格式保持 png，拍一张。
      - 用看图工具确认**天空那一块也是棋盘格**（不只是物件周围的背景）—— 天空盒应已被关掉。
      - 顺带确认场景的 Lighting 设置（Skybox / Ambient / Reflection）**拍完还原了**。
      - 用看图工具确认背景是**棋盘格（透明）**而不是黑/白。本工程还要先把
        Assets/Settings/PC_RPAsset.asset 的 Allow Post Process Alpha Output 勾上。
      - 切 jpg 再拍：应**当场被挡住**并提示 jpg 没有 alpha 通道。
      - 相机的 Clear Flags / Background **拍完要还原**（拍之前在 Inspector 里记下，拍完对照）。
      - 相机留空再拍：应自动用 Camera.main，并在面板上写明"没指定相机"。
  15. 透明那条路在**播放模式**下也试一次（它同样走编辑模式帧泵，不依赖 WaitForEndOfFrame）。
  17. **后处理真的在**（这是"URP 分支被编到"的**下游验收**，最直观）：
      挑一个**后处理效果明显**的场景（Bloom / 暗角 / 色调映射最看得出来），
      用「指定相机」拍一张，跟**游戏视图里同一台相机的画面**对比：
      - 有 Bloom 的场景里，高光应该有辉光；关掉后处理再拍一张，两张应**明显不同**。
      - 如果两张一模一样、且都不含后处理 → 说明还是走了 `camera.Render()` 那条退路。
        先看面板有没有"URP 没渲出东西，已退回 camera.Render()"这句提示，
        再按第 16 条查 `HO_URP_AVAILABLE`。
      - 相机的 `Rendering → Post Processing` **必须自己勾上** —— 这是相机自己的开关，
        面板不动它（不勾的话后处理本来就不该有，这是预期行为，不是 bug）。
  18. **抗锯齿跟着相机设置走**（TAA / SMAA，**不是** MSAA）：
      - 前提：机位**静止**（TAA 需要连续多帧对齐才收敛，一边动一边拍会糊，见「已知边界」）。
      - 拍一张，放大看斜边/细高光：应与游戏视图里的锯齿程度**一致**。
      - ⚠️ **不该**出现"MSAA 被打开"的迹象：本工程 `PC_RPAsset` 的 `m_MSAA` 是 **1（关）**，
        我们**不再往 RT 上写采样数**（曾经写过 4，那等于把 MSAA 强行开回 4x，盖掉用户的设置）。
      - 想确认路径没走错：相机上 TAA 的效果与游戏视图一致即说明进了 URP 管线。

