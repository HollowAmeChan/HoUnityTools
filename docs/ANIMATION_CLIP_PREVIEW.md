# 动画剪辑直通预览（HoAnimationPreviewer）

填一条 `AnimationClip` 就能在场景里播，**不需要任何 AnimatorController**。
支持倍速、拖动滑条逐帧、暂停、单帧步进。运行时组件 + 自定义 Inspector。

## 它解决的是哪一步

Unity 里 Humanoid clip **存的是肌肉空间**，只有 Avatar 能把它重定向到骨骼，所以 Humanoid 动画
必须经 Animator 求值 —— 而 Animator 又要求先有一个控制器资产。于是"只想看一眼这条 clip 长什么样"
这件事被卡在中间：要么先建一个控制器，要么进 Timeline。

**这个需求不用自己发明。** Unity 自己的 Animation 窗口和 Inspector 里的 clip 预览都不走
`AnimationMode.SampleAnimationClip`，而是**在内存里临时造一个 `AnimatorController`**，
再用 `Animator.Play(0, 0, normalizedTime)` + `Animator.Update(0)` 手动定位时间
（`Editor/Mono/Inspector/AnimationClipEditor.cs`）。`clip.SampleAnimation()` 只在 `clip.legacy == true`
时才用。本组件走的是同一个求值路径，只是把"造控制器"这一步换成了 `PlayableGraph`。

## 机制

```
AnimationClipPlayable ──► AnimationPlayableOutput ──► 目标物体上的 Animator（带人形 Avatar）
```

- **图是 `DirectorUpdateMode.Manual`**：时间由组件推进，不依赖 Animator 的状态机。
  这是"暂停时骨架一定停住"的原因 —— 没有第二个人在推时间。
- **播放头（秒）是权威值**：`clipPlayable.SetTime(playhead)` + `graph.Evaluate(0f)`。
  倍速只作用在"我们推进播放头"这一步上，所以任意倍速都不改变定位语义。
- **进入预览时会把 Animator 上的控制器引用置空**，退出时原样还原。
  留着旧控制器的话状态机会和预览抢骨架。Avatar 与剔除模式同理，改动前先记原值。
- **Humanoid 的 Avatar**：优先用目标自己的；目标上没有合法人形 Avatar 时，
  去 clip 所在的资产里借一个（重定向规则与 Animator 一致）。
- **姿势基准快照（换 clip 清残留）**：图只写"这条 clip 里有的通道"，所以换 clip 时
  上一条写过、这一条没写的通道会留在旧值上（形态键不回零、没被新 clip 驱动的骨骼停在旧姿势、
  **`m_IsActive` 曲线开的物体还开着**）。因此进入预览时快照一次，**每次重建和退出预览时都还原**：
  整副骨架的本地 TRS、**每个物体的激活状态**、所有形态键权重。
  手动触发用 `ResetPose()`。根物体的激活状态刻意不还原 —— 关掉它等于把组件和预览一起弄没。
- **根运动默认开启。** 注意：不带控制器时 `Animator.applyRootMotion` 的语义与常规状态机不同
  —— 图是直接把通道写进骨骼的，所以"开启 = 根骨骼的位移原样作用到骨架上"，能看到真实位移量；
  想原地看循环动作就关掉。这一条**尚未在实机验证**，见下面的验证探针。

## 用法

1. 把 `HoAnimationPreviewer` 挂到模型上（`AddComponentMenu` 路径：`HoUnityTools/Ho Animation Clip Previewer`）。
   Humanoid 模型上要有一个 `Animator` 且 **Avatar 的 Animation Type = Humanoid**。
2. 面板里把 `AnimationClip` 拖进「剪辑」—— 这时候点播放键就直接出姿势，不用先点「预览」。
3. 走带上五个图标键：播放/暂停（同一个键，按状态换图标）、回到开头、上一帧、下一帧、到末尾；
   右边整条是时间轴，**在槽内任何位置按下即跳过去并开始拖动**。
4. 其余设置全在默认折叠的「设置」里，摘要行不展开也能看到倍速 / 循环 / 根运动 / 采样率。

面板设计稿：`.design/animation-preview-panel.html`（浏览器直接打开，含空态与展开态两个变体，
以及令牌、手感、图标生成方式的说明）。视觉令牌在 `HoAnimationPreviewTheme`，
走带控件在 `HoAnimationPreviewTimeline` —— 与约束面板同一套色值，改面板时不要另写颜色和宽度。

### 改这个面板时必须守的三条（都踩过）

**1. 每一格都要显式定宽，布局自己算矩形。** 走带行**不交给 GUILayout 分配宽度**，
原因和 `docs/EDITOR_UI_SYSTEM.md` 里写的同一条：GUILayout 按剩余宽度百分比分空间，
没有 `GUILayout.Width` 的按钮会被压成几个像素的小点（表现是"图标变成一排小点，
时间轴吃掉整行"）。走带里每个图标都是 `new Rect(x, y, size, size)` 手算，
只有时间轴吃剩下的全部。

**2. 不要在 `GUIStyle` 里做内边距。** 卡片的 `padding` 会让内容被 GUILayout 重新分配，
同样会把图标压扁；而且 `EditorGUILayout.BeginVertical` 返回的是**内边距之内**的矩形，
拿它当背景就会画错位置。走带是先用 `EditorGUILayout.GetControlRect` 拿到整行矩形自己画背景，
再用 `Inset` 手动收边距。

**3. 自定义 Inspector 里别把方法叫 `DrawHeader`。** 那会遮蔽 `Editor.DrawHeader()`
（CS0108），Unity 默认的组件标题栏会被顶掉、tooltip 也挂到错的地方。这里叫 `DrawTitle()`。

面板上没写进 Inspector 的部分：`FrameCount` / `CurrentFrame` / `CurrentTime` / `NormalizedTime`
是一套完整的时间和帧换算，`Play()` / `Pause()` / `TogglePlay()` / `SetFrame()` / `SetTime()` /
`SetNormalizedTime()` / `StepFrames()` / `StepOneFrame()` / `AdvanceBy()` 都在组件上，
运行时脚本可以直接调。

## 时间的两套驱动

编辑器里没有游戏循环，播放模式的 `Update` 不会跑，所以有两个推进源：

| 环境 | 谁推播放头 |
| --- | --- |
| 播放模式 | 组件自己的 `Update()` |
| 编辑器（非播放） | Inspector 的 `EditorApplication.update` 回调，按真实经过时间喂 `AdvanceBy()` |

两边共用同一个 `AdvanceBy()`，并靠 `Application.isPlaying` 互斥，**不会变成两倍速**。

## 已知限制（都是设计取舍，不是 bug）

- **不能在预制体资产上预览。** Prefab 资产与 Prefab Mode 里都建不了运行时播放图，
  组件会提前拒绝并说明原因（`IsPrefabAssetContext`）。把组件放到场景实例上再预览。
- **预览期间不能用这个 Animator 跑别的逻辑。** 控制器引用被置空是必要的；同物体上依赖
  状态机/参数的脚本在预览期间会停摆。
- **编辑器里不自动接管。** 组件是 `[ExecuteAlways]`，但 `OnEnable` 在非播放模式下直接返回，
  所以"选中物体"不会改场景里 Animator 的引用、不会弄脏场景。编辑器里的预览由面板的
  「预览」按钮显式发起（或脚本调 `Play()`/`SetTime()`）。播放模式里才按 `启用即播` 自动开始。
- **退出预览会把骨架还原成进入预览前的姿势**（含形态键与物体激活状态）。想留住某一帧的姿势，就别禁用组件。
- **求值发生在 Update 阶段。** 图是在组件的 `Update` 里求值的，所以挂 `LateUpdate` 的
  布料/IK 之类会读到**本帧的姿势**，而挂 `Update` 且执行顺序早于本组件的脚本读到的是上一帧的。
  对"看一眼 clip"没有影响，但要知道这个先后。
- **仅单条 clip。** 过渡、混合树、多状态不在范围内。

## 为什么不能拿 `graph.IsValid()` 当"可以推时间"的判据

`PlayableGraph.IsValid()` 只看图自己的版本号。**图存在而 playable 句柄已经失效**时它照样
返回 true，于是下一步的 `clipPlayable.SetTime(...)` 会抛：

```
ArgumentNullException: Value cannot be null.
Parameter name: The Playable is null.
```

判据必须是 `AnimationClipPlayable.IsValid()`（对 `default` 句柄返回 false）。组件现在：

- 建图后立刻验 `clipPlayable.IsValid()`，不成立就**撤销接管并说明原因**，不留"自称在预览"的空壳；
- 所有会推时间的入口（`Play` / `SetTime` / `AdvanceBy` / `Update` / `EvaluateNow`）都以
  `IsPlayableReady` 为闸门，句柄失效时**收干净状态而不是抛异常**；
- 失败原因写进 `LastError`，同一个错误只往 Console 写一次，并在面板上以红色提示框显示 ——
  不会变成每帧刷屏。

## 另外三条踩过的坑

**1. 绝不能在 `OnValidate` 里重建。** `OnValidate` 期间 Unity 禁止 `SendMessage`，而重建会
`SetActive`、销毁图。场景里只要有一个带 `OnBecameVisible`/`OnBecameInvisible` 的物体
（蒙皮网格几乎都有），就会刷屏：

```
SendMessage cannot be called during Awake, CheckConsistency, or OnValidate (mesh_X: OnBecameInvisible)
```

所以面板上直接改 clip / Animator 触发的重建走 `RequestRebuild()`：
`OnValidate` 里只置一个待办标记，落到下一次编辑器 `update`（`delayCall` + 面板的 update 回调）
才真做。**没有这一步，只要选中物体改一次字段就会刷屏。**

**2. 一个 Animator 只能有一个预览器。** 两个组件挂在同一棵骨架上时，后建立的图会把先建立的
姿势覆盖掉 —— 表现是两个面板互相打架，极难排查。组件维护一张静态的 Animator 归属表，
第二个接管者会被拒绝并说明是谁占着。切场景/重编译时静态表由 Unity 的 domain reload 清掉。

**3. 预览链路被外力破坏要主动收摊。** 有些 clip 的 `m_IsActive` 曲线会关掉物体；
如果它关掉的正是 Animator 自己所在的物体（或父级），图就再也驱动不了任何东西，
而组件本身还是"启用"状态 —— 静默失效最难查。组件每帧体检**整个祖先链**的启用状态，
一旦断了就结束预览并把原因写进 `LastError`。

## 验证

代码已经过编译验证：用工程生成的 csproj 直接编 `HoUnityTools.Runtime` 与 `HoUnityTools.Editor`，
两边 0 error（探针文件也一起编过）。`Tests~/AnimationClipPreviewValidation.cs` 是一份一次性探针：

1. 找一个 Humanoid 来源（`Assets/ProbeHumanoid.fbx`，或用 `HumanoidPath` 指到你自己的模型）。
2. 逐帧采集整副骨架的世界位置 + 本地旋转，把本组件与 Unity 自己的预览路径
   （内存控制器 + `Animator.Play`/`Update`）各采一遍，报告最大位置误差（门槛 1mm）
   与最大旋转夹角（门槛 0.1°）。
3. **换 clip 的残留**：用两条不同 clip 交替切换，检查切走瞬间的形态键有没有回到基准、
   切回来是否落在同一点。
4. 其余：暂停稳定性、单帧步进、倍速线性、循环折返、非循环停在片尾、禁用后引用与形态键还原、
   编辑器里不自动接管。

**还没验证的两件事**（需要真机跑一次探针）：Humanoid 骨骼的逐帧一致性，
以及根运动开关在"无控制器 + 图直写骨骼"下的实际行为。

跑法（把文件放进一次性验证工程的 `Assets/Editor`，工程根目录要有 `.ho-face-validation` 标记）：

```powershell
& "C:\Program Files\Unity\Hub\Editor\<版本>\Editor\Unity.exe" -batchmode -nographics `
  -projectPath <验证工程> -executeMethod HoAnimationPreviewValidation.RunBatch -logFile -
```

结果落在工程根的 `preview_result.txt`，首行是 `HO_PREVIEW_ALL_PASSED (n)` 或 `HO_PREVIEW_FAILED (m/n)`。

**注意**：Unity 的授权客户端有全局互斥量，**已经开着一个 Unity 编辑器时跑不了 batchmode**
（症状：`Failed to acquire global mutex Unity-LicenseClient-<用户>`）。跑之前先关掉编辑器。

## 参考实现（开源）

| 项目 | 许可 | 与本组件的关系 |
| --- | --- | --- |
| [Baste-RainGames/AnimationPlayer](https://github.com/Baste-RainGames/AnimationPlayer) | MIT | `Editor/AnimationStatePreviewer.cs` 是最完整的 `PlayableGraph + DirectorUpdateMode.Manual + Evaluate()` 参考实现（它预览的是自己建的层/状态，不是任意 clip） |
| [Unity-Technologies/SimpleAnimation](https://github.com/Unity-Technologies/SimpleAnimation) | MIT（已归档） | 官方 `PlayableGraph` 驱动 Animator 的骨架 |
| [ScreenPocket 的 Qiita 文章](https://qiita.com/ScreenPocket/items/000ea4ba53a744621524) | 未标注 | `PreviewRenderUtility` + `AnimationClipPlayable` 的窗口版（本组件不用浮动窗口，直接作用在场景物体上） |

Unity 自己的 `AnimationClipEditor` / `TransitionPreview` / `AvatarPreview` 是最权威的技术来源，
但 `UnityCsReference` 是 **Unity Reference Only License**，只可读，不可抄代码。

**不要用 `AnimationMode.SampleAnimationClip` 做这件事**：它在 Humanoid 上是有记录的偏离路径
（Unity issue tracker 有一条同名条目，但那是迁移前的 FogBugz 问题，正文已失传）；而且它对同一物体
在同一编辑器 tick 内反复采样会卡在默认 A-pose。
