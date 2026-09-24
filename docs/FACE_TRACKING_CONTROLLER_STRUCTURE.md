# 面捕控制器：结构、就地装配与命名约定

日期：2026-09-25（§1 参考实现与 §2 判别性实验的数据来自 2026-09-22/23 的实测，2026-09-25 复核结论未变；
§3 按"控制器编辑页就地装配"的现状重写）。

本文讲三件事，全部是**源码 + 实测**读出来的：
**①** 参考实现（Jerry 的 ARKit 模板）长什么样（§1）；**②** 我们怎么把一份控制器管起来（§2 判别性实验 + §3 就地装配）；
**③** 命名约定（§5，给编树的人）。判别性实验「Direct 树 + Write Defaults 关闭会发散」的机制与完整数字收在
[踩过的坑 · 混合树](pitfalls/BLEND_TREE_TRAPS.md) §1，本文只留结论与判据。

数据来源：`.research/VRCFaceTracking-Templates`、`.research/VRCFaceTracking` 与 `D:\UnityVrcVCC_Projects\VrcMaster`
（本地检出，gitignore），实测用例是 `Tests~/FaceTrackingValidation.cs`。

> 怎么用 / 中间层做什么 / 什么能进树：见 [面捕工作流](FACE_TRACKING_WORKFLOW.md)、
> [面捕中间层处理](FACE_TRACKING_MIDDLE_LAYER.md)、[混合树的能力边界](BLEND_TREE_LIMITS.md)。

## 0. 现在落在哪（2026-09-25）

| 事 | 落点 |
| --- | --- |
| **装配**（填动画 + 重绑网格） | 菜单 **`HoUnityTools/面捕/控制器编辑`** → `Editor/FaceTracking/HoFaceControllerToolWindow.cs:41`；实做在 `Editor/FaceTracking/HoFaceAnimationAssets.cs` |
| **装配后跑起来**（影子台求值） | `Editor/FaceTracking/HoFaceAnimationSession.cs` |
| **调试状态 / 谁 tick** | `Editor/FaceTracking/HoFaceDebugSettings.cs`（落盘 `Assets/HoFaceDebugSettings.json`，`HoFaceDebugSettings.cs:36`）+ `Editor/FaceTracking/HoFaceDebugHost.cs`（`[InitializeOnLoad]`，`EditorApplication.update` 上唯一一处 tick，`HoFaceDebugHost.cs:26-27`、`:85-94`） |
| **参数命名** | `Runtime/FaceTracking/HoFaceNaming.cs` |
| **实测用例** | `Tests~/FaceTrackingValidation.cs` |

- **角色预制件上零组件**：调试状态全在上面那两个文件里（不是组件）。
- 要驱动的网格 = 调试对象下**所有** `SkinnedMeshRenderer`（`HoFaceDebugSettings.Meshes()`，`HoFaceDebugSettings.cs:115-121`；每次调用返回**新表**）。
- 验收：Unity 6000.3.15f1 批处理 `HO_FACE_TESTS_ALL_PASSED`（`Tests~/FaceTrackingValidation.cs:1008`），**114 条断言全绿**（用例里 114 处 `Check(`/`Near(` 调用，定义在 `Tests~/FaceTrackingValidation.cs:1241-1245`）；跑法见 [批处理验证](pitfalls/VALIDATION_LOOP.md)。

## 1. 参考实现：Jerry 的 ARKit 控制器长什么样

`FX - Face Tracking - ARKit Blendshapes.controller`（15823 行 YAML）的实测统计：

| 项目 | 数值 |
| --- | --- |
| 图层 | **3**（全部 Override、无 AvatarMask） |
| 状态机 | 3，其中 layer 0 的状态机**是空的**（只是一个占位/命名空间） |
| 状态 | 10 |
| **混合树** | **412**（362 Simple1D + 36 Direct + 12 FreeformCartesian2D + 2 SimpleDirectional2D） |
| MonoBehaviour | 10（`VRC Avatar Parameter Driver` 与 `VRC Tracking Control`） |
| 参数 | 206 |
| 每个状态的 Write Defaults | **全部 = 1（开）** |

### 1.1 三层各干什么

| 层 | 名字 | 作用 |
| --- | --- | --- |
| 0 | `FX - Face Tracking - ARKit Blendshapes` | 空状态机。真正的驱动在 layer 2 |
| 1 | `Tracking_State` | **门控层**：7 个状态全都播放 `_Do_Nothing.anim`，靠 `VRC Avatar Parameter Driver` 写 `State/*`、`FacialExpressionsDisabled` 等开关。Eye/Face/Lip Tracking 的启用、Visemes 开关都在这里 |
| 2 | `Face_Tracking` | **驱动层**：`FT Blendshape Driver (EDIT THIS)` 一个状态，里面是**一整棵 Direct 混合树** |

> 这三层是**参考实现自己的**。我们的控制器由作者在混合树编辑器里编（§3），
> 而且**不注入门控参数** —— 参考实现那一层门控我们没抄（§4，理由见 §3.2）。

### 1.2 驱动层：一棵 Direct 树管住整张脸

根节点是一棵 **Direct 混合树，22 个子节点**。每个子节点代表一个面部区域，**用它的参数直接当子权重**做门控：

| direct 参数 | 用途 | 出现次数（全控制器） |
| --- | --- | --- |
| `OSCm/BlendSet` | 整脸淡入淡出 | 85 |
| `LipTrackingActive` | 嘴部追踪可用 | 46 |
| `EyeTrackingActive` | 眼部追踪可用 | 12 |
| `FT/DirectBlend` | 用户总开关 | 9 |
| `FaceTrackingEmulation` | 无设备时模拟 | 1 |

每个区域子节点再往下是**每张脸一块的混合树**：

- **多数是 Simple1D**：`OSCm/Proxy/FT/v2/<Shape>` 从 0 插值到 100 的姿势，两个子片段（`X 0.anim` / `X.anim`）。
- **需要二维空间的地方用 FreeformCartesian2D**：
  - **眼睑**：`(EyeLidRight, EyeSquintRight)` → 5 个**预先做好的姿势**：`Blink / Neutral / Wide / Squint / Open_Squint`。也就是说"半闭 + 眯眼"不是两条曲线相加，而是作者摆好的中间姿势。
  - **眼球**：`(EyeLeftX, EyeY)` → `Look_Neutral / In / Out / Up / Down`，每只眼一棵。
  - **眉内端**：`(BrowExpressionLeft, BrowExpressionRight)`，左右联动。
- **有耦合的地方不是 1:1 映射**：`SmileFrown` 同时驱动 `Mouth_Smile`、`Cheek_Squint`、`Mouth_Dimple`；`JawOpen` 是 `(JawOpen, MouthClosed)` 的二维树；`Brow_Down` 也被 `MouthRaiserLower` / `NoseSneer` 拉。
- **全局限制**：若干子树外面套着 `FaceTrackingLimits`。

### 1.3 参数不是直通的：三层参数链

```
FT/v2/*            109 个    ← VRCFT 发过来的原始值
  ↓ OSCmooth 预制件（31 个 OSCm/Smooth/* + OSCm/Sensitivity/* + OSCm/Remote/*）
OSCm/Proxy/FT/v2/*  31 个    ← 混合树实际读的是这一层
```

`sensitivity`（每个部位一条灵敏度曲线）、`smoothing`（本地/远端分别设）都在这一段完成，**不在控制器里**。

### 1.4 眨眼的"叠加"：EyeSync

`FT/EyeSync`（0/1）在 Shared.controller 里选择两种接法：

- **OFF** → `Proxy EyeLids (EyeSync OFF)`：左右眼睑各用各的值。
- **ON** → `Proxy EyeLid`：一棵 4 子节点 Direct 树，**左眼的值也去驱动右眼、右眼的值也去驱动左眼**，交叉权重是 `FT/EyeSyncMix`（默认 0.5）。

效果是"两眼要眨一起眨、要半闭一起半闭"，避免一只眼半闭时显得不协调。眼球注视方向也有一份同样的 `EyeSync In/Out`。

> 这一步是**参考实现**的做法。我们这边曾经有一份 C# 实现（`HoFaceEyeSync`），**2026-09-25 已删**：
> 这类左右整形属于参数生产，现在写在中间层的输入行/输出行里（§3.2、§4）。
> 参考实现这一段保留，是因为它决定"你该抄谁的形状"，不是我们现在的行为。

## 2. 判别性实验：Direct 树会不会削弱（2026-09-22）

早先我们以为"多个通道同时给会互相削弱"，于是给每个形态键开一个独立 Override 图层（52 键 ≈ 52 层 + 104 个片段）。
**这条理由是错的**，同一个 Direct 树只改一个变量的实测：

| 条件 | jawOpen（参数 0.6） | mouthSmileLeft（参数 0.8） | 结论 |
| --- | --- | --- | --- |
| **Direct + Write Defaults 开** | **60.0** | **80.0** | 精确 = `参数 × 100`：不归一化、不互相削弱 |
| Direct + Write Defaults 关（没有别的写入者） | 0 | 0 | 归零 |
| Direct + Write Defaults 关（基础动画也在写同一个键） | 98.98 → 246.28 → 1059.33 | 131.97 → 531.97 | **发散** |

机制（`98.976` 精确等于递推 `out ← 0.6×100 + 0.4×out` 的第五项）与完整数字见
[踩过的坑 · 混合树](pitfalls/BLEND_TREE_TRAPS.md) §1（探针日志行是 `HO_WDON` / `HO_WDOFF`，
用例 `Tests~/FaceTrackingValidation.cs:781`、`:791`）。结论两条：

- **Direct 树本身没问题** —— Unity 官方文档也明说 Direct 就是"把参数映射到子权重"，并点名可用于混合表情的形态键。
- **不可用的组合是「Direct 树 + 写默认值关闭」**，而参考实现所有状态都是 WD 开 —— 这才是两边真正的分岔点。
  这个组合**代码直接拒**，不留给运气：`Compile` 里检查"状态关 WD 且动作里有 Direct 树"就抛
  （`HoFaceAnimationAssets.cs:513-519`；用例 `Tests~/FaceTrackingValidation.cs:223-231`）。

"必须 WD Off"来自早期**接管角色 Animator**的设计（那时 WD On 会把身体动画每帧复位）。
改影子求值之后这条约束过期了：影子台是隔离的、没有别的写入者，WD On 完全无害 —— 我们自己的图层数才从 52 收到 1
（影子台的形状见 `HoFaceAnimationSession.cs:10-24`、`:119-127`）。

## 3. 现在的做法：控制器编辑页**就地**装配（2026-09-25 复核）

**代码不生成树，也不生成姿势。** 三件事分开，各归各的作者：

| 谁 | 出什么 | 长什么样 |
| --- | --- | --- |
| **控制器**（作者在混合树编辑器里编，或拿现成的一份） | 树形、坐标、参数 | 一份完整 `.controller`；它引用的每个片段就是一个**槽位** |
| **动画文件夹**（形态键基础动画生成器 `HoUnityTools/形态键基础动画` / 手 K） | 姿势数据 | 一堆 `.anim`，文件名 = 槽位名（比如每个形态键一份 `<键名>.anim`） |
| **调试对象**（场景里的角色实例） | 写谁 | 它下面**所有** `SkinnedMeshRenderer` |

**装配 = 把「你自己复制到工程里的那一份控制器」就地改成能跑的样子。** 面板上就是这几步：

```
预置控制器目录（Editor/FaceTracking/Controllers/，现在只有一份 README）
  ↓ 菜单「HoUnityTools/面捕/控制器编辑」→「打开目录」（你自己把要的那份复制进工程，之后归你管）
  ↓ 把它拖进「控制器」栏 —— 这一栏选中的那份就是会话真正跑的那一份
  ↓ 点「原地装配」＝
      ① 按槽位名把「动画文件夹」里的同名 .anim 填进树与状态（没找到就保留控制器自带的那份）
      ② 把每条形态键曲线重绑到「调试对象」的网格上
      ③ 清掉填完之后已经没人引用的临时片段
  ↓ 就地改这一份：不新建资产、不改名、不移动、不动 GUID、不碰层与参数
```

装配的入口是 `HoFaceAnimationAssets.Adopt(controller, 同一个路径, …)`：`sourcePath == targetPath` 时整段复制被跳过
（`HoFaceAnimationAssets.cs:122-131`），面板就是这么调的（`HoFaceControllerToolWindow.cs:289-291`）。

> **旧流程已经不存在**：「混合树模板 → 复制出一份新控制器 → 输出到某路径」这条（以及设置里的
> `assemblyOutputPath` 字段）删了。`Adopt` 的 `template != target` 复制分支还在，但只给脚本与用例用
> （用例走的就是它：`Tests~/FaceTrackingValidation.cs:128`、`:216`）；面板只走"模板 = 自己"的原地分支。
> 设置里还留着 `treeTemplateGuid` / `treeTemplatePath`（`HoFaceDebugSettings.cs:53-60`），注释也写明
> "工具页已经不需要它了，保留给脚本与验收用例"。

| 项 | 现状 |
| --- | --- |
| 图层 / 状态 / 参数 | **作者的事**，装配不增不减（`HoFaceControllerToolWindow.cs:22-25` 的注释；用例里那层 `Ho/00 Drive` 装配前后原样，`Tests~/FaceTrackingValidation.cs:225-226`） |
| 槽位填充 | 文件夹里同名 `.anim` **顶替**控制器自带的那份；「详情」栏实时列出每个槽位是"文件夹 / 模板自带 / 缺"（状态串在 `HoFaceAnimationAssets.cs:41-59`，读数在 `:147-166`） |
| 驱动对象 | 调试对象下所有 `SkinnedMeshRenderer`；一个键可以落在多个网格上（`HoFaceAnimationAssets.cs:294-316`；用例 `Tests~/FaceTrackingValidation.cs:195-210`） |
| 模型上没有的键 | 那条曲线**原样留着不动**（作者的格子数据不丢），「详情」栏报出"这些格子落不到任何网格上"（`HoFaceAnimationAssets.cs:252-260`、`Inspect` `:364-409`） |
| 外部片段（别人的 `.anim`） | 先复制成本文件的子资产再改，树里的引用一起换掉；**共享资产一个字节不动**（`HoFaceAnimationAssets.cs:276-286`；用例 `Tests~/FaceTrackingValidation.cs:155-163`） |
| GUID | 就地装配根本不换文件，GUID 自然不变。`Adopt` 的复制分支也会把 GUID 塞回去（`RestoreGuid`，`HoFaceAnimationAssets.cs:346-358`；用例 `Tests~/FaceTrackingValidation.cs:212-220`） |
| 幂等 | 反复装配结果一致（`Retarget` 的注释 `HoFaceAnimationAssets.cs:252-260`；用例 `Tests~/FaceTrackingValidation.cs:204-210`） |

**为什么动画要放在文件夹里**：树是"结构"，动画是"数据"。分开之后，改姿势不用碰树、换模型不用重做动画
（装配时按**形态键名**重绑，这份文件夹跟模型无关），而且"缺哪些动画"变成一个能显示出来的事实。
"缺"有三种：文件夹里没有、控制器自带的片段也没写任何键 —— 「详情」栏逐槽位说清楚是哪一种（`HoFaceClipSlot.Status`，`HoFaceAnimationAssets.cs:52-59`）。

**代价是诚实的**：我们不再能保证控制器的形状，所以「详情」栏改成**从资产读实况**
（几层几个状态、会写哪些键、哪些键这台模型没有），而不是从配置推断：`HoFaceAnimationAssets.Inspect`
（`HoFaceAnimationAssets.cs:360-409`）→ `HoFaceControllerToolWindow.cs:366-395`。

**边界仍在**：运行期只允许形态键曲线（`Compile`，`HoFaceAnimationAssets.cs:471-472`）；无 Behaviour
（`:506-512`）、无同步图层（`:423-427`）、无事件/对象曲线/Humanoid（`:461-462`）、Direct 树必须 WD 开（`:513-519`）。
装配**不拦**这些（`Adopt` 不调 `Compile`），**开始驱动时才拒**（会话构造里先编译验证一次，
`HoFaceAnimationSession.cs:110`），面板会说是哪一条。

### 3.1 装配之后，谁负责什么

| 东西 | 谁管 |
| --- | --- |
| 树形、格子坐标、参数名 | **控制器作者**（在 Unity 的混合树编辑器里） |
| 每格写哪个键、写多少 | **动画文件夹里的片段**（控制器自带的片段只是兜底） |
| 动画驱动哪些网格 | 调试对象（装配时作用于曲线绑定；没有"驱动对象列表"这种东西了） |
| 参数的值怎么算出来 | 中间层（[面捕中间层处理](FACE_TRACKING_MIDDLE_LAYER.md)）；控制器只等着被喂 |
| 哪些键算我们拥有的、要不要抄回真模型 | 会话：通道模式（`Manual` / `Hold` / `Neutral` / `Release`）+ 输出所有权（`HoFaceAnimationSession.cs:176-216`、`Runtime/FaceTracking/HoFaceOutputOwnership.cs`；见 [面捕设计](FACE_TRACKING_DESIGN.md) §5.1「占用表：谁拥有哪个键」） |

**幂等**：装配是幂等的（反复装配结果一致）。换动画文件夹、改调试对象，都只是再装配一次；
把某个网格从调试对象下移走，它上面的绑定也就没了 —— 键在别的网格上时不会留下孤儿绑定
（用例 `Tests~/FaceTrackingValidation.cs:204-210`）。

**下面 §5 那张命名表不是"我们现在生成的形状"**，而是**编控制器与姿势时的作者约定**
（我们上一版生成器就是照它出的，所以留着当参考）。这个仓库里没有 `ho-2d-test1.controller`：
那份控制器在作者手上，仓库只负责装配它（预置目录现在是空的，只有一份 README，
`Editor/FaceTracking/Controllers/README.md`）。

### 3.2 我们**不再**做的事（以及为什么）

| 删掉的东西 | 现在怎么做 | 证据 |
| --- | --- | --- |
| **区域门控 / 区域闸** | **不注入开关**：哪些键算数由使用者自己的混合树/参数决定 | `HoFaceAnimationAssets.Allowed()` 里已经不看了（`HoFaceAnimationAssets.cs:97-103`）；会话里明写"没有区域门控"（`HoFaceAnimationSession.cs:205-206`） |
| ↳ 后果 | 编译出来的绑定数**就是控制器里有的那些键**（不再按区域排除） | 用例 `Tests~/FaceTrackingValidation.cs:120-121`、`:135`、`:243` |
| ↳ Direct 树的每个子节点**必须**挂一个参数 | 那是**使用者那一侧**的事：夹具自己造了一个常量 1 的 `Fixture/RegionsOn` 当"永远算数" | `Tests~/FaceTrackingValidation.cs:521-527`、`:499` |
| ↳ `Ho/Drive/Gate/*` 参数 | **不存在了**：`HoFaceNaming.cs` 里只剩 `Ho/Drive` 根、两根轴语义与 `LidAxis()`（`HoFaceNaming.cs:14-31`） | 全仓库 `.cs` 里已经搜不到 `Ho/Drive/Gate` |
| **双眼同步（`HoFaceEyeSync`）** | 这类左右整形属于**参数生产**，写在中间层的输入行/输出行里（表达式 + 曲线 + 修饰符），不该在会话里做 | `HoFaceAnimationSession.cs:224-227`（"这里曾经是：`HoFaceEyeSync.Apply(...)`"）；面板上那三个开关（eyeSync / eyeSyncMix / eyeSyncSingleKey）一并退休（用例 `Tests~/FaceTrackingValidation.cs:309-312`） |
| **"驱动对象列表"（手工维护的网格表）** | 要驱动的网格 = 调试对象下**所有** `SkinnedMeshRenderer`，`Meshes()` 每次现取新表 | `HoFaceDebugSettings.cs:115-121`；用例 `Tests~/FaceTrackingValidation.cs:118-119`、`:196-197` |
| **旧面板 UI** | 旧按钮名（"按 Animator 填充 / 装配控制器 / 检查绑定"）在面板上没有了；现在的按钮是「原地装配 / 只填动画 / 只重绑网格」（`HoFaceControllerToolWindow.cs:255`、`:260`、`:265`），详情折叠栏叫「详情」（`:347-363`），槽位读数在 `Slots()`（`HoFaceAnimationAssets.cs:147-166`） | 上引行号。代码注释里那两处旧名（`HoFaceAnimationAssets.cs:61` 的「控制器结构」、`:145` 的「动画填充」）**2026-09-25 已跟着改成「详情」栏** |

> 区域门控不是"做不到"（Direct 子节点挂参数就是门控，嵌套还是**相乘**的 —— 见
> [踩过的坑 · 混合树](pitfalls/BLEND_TREE_TRAPS.md) §3），而是**我们不再替使用者决定**。
> 想门控就在自己的控制器里画。这条决定的完整论述在 [面捕设计](FACE_TRACKING_DESIGN.md) §5「为什么没有门控」。

## 4. 参考实现里我们抄了 / 还没抄的

| 参考实现的东西 | 我们 | 落在哪 |
| --- | --- | --- |
| **二维区域**（眼睑 `开合 × 眯眼` 五格姿势） | ✅ 抄了（我们那份控制器的六格是它的直系后代） | 控制器（§3、§5） |
| **区域门控**（`EyeTrackingActive` / `LipTrackingActive`） | ❌ **没抄，而且删干净了**（连 `Ho/Drive/Gate/*` 参数一起）—— 哪些键算数由使用者自己的树/参数决定 | 参考实现见 §1.1/§1.2；理由与证据见 §3.2 |
| **参数预处理链**（平滑 / 灵敏度 / 上下限） | ✅ 抄了，但**在树外面** | 中间层（[面捕中间层处理](FACE_TRACKING_MIDDLE_LAYER.md)） |
| **EyeSync 式左右交叉混合** | ❌ **没抄**（曾有 C# 的 `HoFaceEyeSync`，2026-09-25 删除）—— 这类整形写在中间层的输入行/输出行里 | §1.4 是参考实现；`HoFaceAnimationSession.cs:224-227` |
| **耦合**：`SmileFrown` 同时驱动嘴角 / 脸颊 / 酒窝 | ✅ 已确认（资产A L207-218、L417-428） | 我们**没抄**：耦合留在中间层 / 控制器里 |
| `JawOpen` 是 `(JawOpen, MouthClosed)` 的二维空间 | ⚠️ **未能确认**：资产A 里以 `JawOpen` 为 blendParameter 的三处（L2052 / L10891 / L14791）都是 `m_BlendType: 0`（1D），那个 `m_BlendParameterY: …MouthClosed` 是 1D 树的**残留字段** | 我们没抄，也不需要 —— 见 §4.1 |
| **限制 / 修正**：若干子树外面套 `FaceTrackingLimits` | ❌ 没抄 | 中间层欠账 |
| **眼球**：一棵 2D 树管四方向，坐标 **±0.7**（中性 `(0,0)`） | ✅ 已确认五姿势；Shinano 加对角共 **9 姿势** | ❌ 还没做（要加就加在控制器里，§4.1 有完整形状） |

后三条都是同一个处方的更多例子：**会重叠的语义进同一棵树 / 参数算术留在外面**
（判据见 [混合树的能力边界](BLEND_TREE_LIMITS.md)）。

### 4.1 成对通道与双向轴：一手取证（2026-09-23）

**为什么单开一节**：本节的结论来自 **原始 YAML**（并用 `.meta` 把 GUID 解成片段名），不是来自
`.research/` 里的转储 —— 那些转储有两处会骗人，规矩见 [踩过的坑 · YAML 与转储](pitfalls/UNITY_YAML_AND_DUMPS.md)。

一手资产（都在本机）：
- **A** = `D:\Unity_Fork\HoUnityTools\.research\VRCFaceTracking-Templates\Packages\adjerry91.vrcft.templates\Animators\ARkit Blendshapes\FX - Face Tracking - ARKit Blendshapes.controller`
- **B** = kipfel 随包分发的旧版副本（路径见 `.research` 调查记录），与 A 多处**字节级同构**
- **C** = Shinano 第三方实现 `FX_FT_added 2.controller`

| 通道对 | 成熟实现的实际形状（一手） | 出处 | 与我们的差异 |
| --- | --- | --- | --- |
| `jawLeft` / `jawRight` | **合成一根 `JawX ∈ [−1,1]`，3 姿势 1D**：thr `−1 = Jaw_Left` / `0 = Jaw_X 0`（中性）/ `+1 = Jaw_Right`。**控制器里根本没有 `JawLeft`/`JawRight` 参数** | A `Jaw X Blend` L2186-2218；参数表 L7102 | 我们是两个直通叶子（没合）—— 即中间层给每个键一行直通（`HoFaceMiddleware.cs:236-238`） |
| `mouthLeft` / `mouthRight` | 同形：`Mouth X Blend`，3 姿势 1D，`−1/0/+1` | A L8207-8245；B L8088-8120（同构） | 同上 |
| `mouthSmile` / `mouthFrown`（每侧） | **合成一根 `SmileFrownLeft/Right ∈ [−1,1]`，但树是两棵各 2 姿势的 1D 分占半轴**：欣 `[0,1]`、悲 `[−1,0]` —— **不是一棵 3 姿势树** | A `Mouth Smile Left Blend` L9566-9587、`Mouth Frown Left Blend` L5259-5280；`…Right` L609-631 | 我们是直通叶子 |
| ↳ 反例 | Shinano 把同一件事做成**一棵 4 姿势 1D**：thr `−0.8 / −0.1 / +0.1 / +0.8`（Min/Max 也收窄到 ±0.8） | C `Mouth Sad Smile Left` L20266-20306 | —— |
| `eyeLookIn` / `eyeLookOut` | **压成一根水平轴，坐标对称 ±0.7**（不是 0..1 半轴） | A `Eye Look Left Blend` L2343-2397（`m_BlendType: 1` = SimpleDirectional2D） | 我们是直通叶子 |
| `eyeLookUp` / `eyeLookDown` | **第二根垂直轴，同为 ±0.7，与水平轴同处一棵 2D 树（5 姿势）**；左右眼用不同水平轴，垂直轴**共用** | 同上；Shinano 加对角共 9 姿势（C L21526-） | 同上 |
| `eyeBlink` / `eyeWide` | **不是 ±1 双向轴**：C# 先加成一根 **0..1 的"眼睑位置"** `EyeLid = Openness*0.75 + EyeWide*0.25` —— **中性落在 0.75**、闭眼 0、睁大 1.0；再与 `EyeSquint` 组成 FreeformCartesian2D（5 姿势：Blink `(0,0)` / Neutral `(0.75,0)` / Wide `(1,0)` / Squint `(0.25,1)` / OpenSquint `(0.75,1)`） | C# `UnifiedExpressionsParameters.cs` L57-58（`.research/VRCFaceTracking/VRCFaceTracking.Core/Params/Expressions/`）；A `Right Eye Lid Blend` L10898-10952 | **我们已经不是"两个直通叶子"**：默认中间层把每只眼合成一根**双向轴**（`Ho/Drive/Lid/<侧>/BlinkWide = eyeBlink<侧> − eyeWide<侧>`，`HoFaceMiddleware.cs:244-249`）。但合成出来的轴是 **±1、中性在 0**，与成熟做法的 **0..1、中性 0.7–0.8** 仍不同（见下面第二张表） |
| N 通道 → 2 轴 → 一棵 2D 树 | **有，至少 4 个实例**（眼动 4→2；眼睑 3→2；`Brow Sad` 用左右两根双向轴；`Brow Sad Emulation` `(−0.7,−0.7)`） | A L2343 / L10898 / L3687-3742 | 我们只有眼睑那两根轴是合出来的（中间层的 4 行输出），其余是直通 |

**"合并出来的那个值是谁写的"这件事，成熟实现和我们架构一致（一手确认）**：`FT/v2/*` 由**外部
VRCFT C# 经 OSC 写入**（模板 README 明写"不要拿 `FT/v2/` 当输入，那是 OSC 原始值"），进图后再由
**动画曲线直接写 Animator 参数**（clip `attribute: OSCm/Proxy/…`、`path:` 为空、`classID: 95`、
单关键帧；Proxy 常量 ±1.25 = clamp(Smooth,±1)×1.25）装配/夹取，**树只读 `OSCm/Proxy/*`，自己不产参数**。
⇒ 我们"**参数算术留在树外面、树只消费**"的选择与成熟做法一致，不需要改；区别只是那个"外面"在我们的架构里是
**中间层的输出行**（`HoFaceMiddleware.cs:84-109`），在成熟实现里是上游 C# + 曲线驱动参数。

**这次取证纠正/推翻的我们自己的记录**：

| 我们的说法 | 结论 |
| --- | --- |
| 归档 `docs/archive/FACE_TRACKING_PIPELINE_SPLIT.md:1249`「Shinano `SmileSadLeft/Right` 是 3 姿势双向 1D」 | ❌ **旧说法是错的**（那条归档已经跟着复核改成"~~3 姿势~~；Shinano 是一棵 4 姿势"）；结论仍成立且有一手出处：**4 姿势、阈值 −0.8/−0.1/+0.1/+0.8**（C L20266-20306） |
| 本文旧版「`JawOpen` 是 `(JawOpen, MouthClosed)` 二维空间」 | ⚠️ **未能确认**（A 里那三处都是 1D + 残留 `m_BlendParameterY`） |
| 归档 `docs/archive/arkit-mouthclose-report.md:446`「真正二维的树只有 `EyeRightX × EyeY` 与 `EyeLeftX × EyeY`」 | ✅ 确认（坐标 `(0.7,0)` / `(0,0.7)` / `(0,-0.7)`） |
| `BLEND_TREE_LIMITS.md`「`OSCm/Proxy/v2/*` 那些 `*Smoother*` 是曲线驱动参数」 | ✅ 确认（空 path + `classID 95`） |
| 全局左右合一的 `SmileSad`/`SmileFrown` | 参数在 C# 里存在（左右取平均），但**成熟控制器里没有一棵树消费它**（资产A 里搜不到 `SmileSad`）—— 我们不用做 |

**眼睑开合轴：跨血统一手指证（2026-09-23 第二批）**

上一段里"中性 0.75"那条只有**一个血统**（VRCFT 官方模板 + 它的 C#），不足以说"成熟做法"。
补查了独立血统后，结论更硬了 —— 而且**我们现在的做法在所有成熟资产里都找不到**：

| 血统 | 眼睑轴 | 中性落点 | 树 | 姿势 |
| --- | --- | --- | --- | --- |
| **VRCFT 官方模板**（ARKit 支 L4297 / UE 支 L4041；kipfel 随包副本与它同血统、同 clip GUID，**不算独立来源**） | 每眼一根、**0..1**（1 = 睁大） | **0.75** —— C# `Openness*0.75 + EyeWide*0.25` 给的（`UnifiedExpressionsParameters.cs` L57-58），作者又把 Neutral 摆到 `m_Position {x:0.75,y:0}`，参数默认也是 0.75：**三处一致** | `FreeformCartesian2D` | **5**：Blink `(0,0)` / Neutral `(0.75,0)` / Wide `(1,0)` / Squint `(0.25,1)` / OpenSquint `(0.75,1)` |
| **JINGO 系**（Shinano / Mafuyu / Manuka，**共享动画 GUID ⇒ 同一血统**） | 每眼一根、**0..1** | **0.7–0.8 平台**：作者把同一个中性片段**钉在两个阈值上**（0.75 落在平台正中）。参数默认三条不一：Shinano 控制器 0.75；Mafuyu 控制器 0 而 Expression Parameters 0.75；Manuka 控制器 0 而 Expression Parameters **0.8** | **Simple1D**（这根轴上没有 squint；squint 在别处） | 4（t = 0 / 0.7 / 0.8 / 1） |
| **legacy / SRanipal**（同 repo 的另一代参数） | 每眼一根、**−1..1**（squeeze 占负半轴） | **0.8** —— C# `EyeWide*0.2 + Openness*0.8` 给的 | Simple1D | 4 |
| **kipfel 自有 FX 层**（不是模板副本） | 每眼一根、0..1 | **t = 0.7**（作者摆的；`m_Position` 里的 `0.75` 在 Simple1D 下不参与运算，是 2D→1D 残留） | Simple1D | 3 |
| **我们现在** | **每眼一根 ±1 的双向轴**（中间层一行表达式合出来的）：`Ho/Drive/Lid/<侧>/BlinkWide = eyeBlink<侧> − eyeWide<侧>`，默认曲线是 −1..1 恒等（`HoFaceCurve.SignedIdentity`，`HoFaceMiddleware.cs:29-30`），眯眼是独立一行（`HoFaceMiddleware.cs:250-254`）。轴语义：`+1` 闭 / `−1` 睁大 / `0` 中性（`HoFaceNaming.cs:19-23`） | **0**（闭−睁大 = 0 时是中性） | 控制器里由作者摆（我们那份把它摆成 `A3` 的 X 轴：`X0` 睁大 / `X1` 中性 / `X2` 闭，§5.1） | —— |

三条硬结论（都有一手出处）：

1. **没有任何一个成熟血统把 `blink` / `wide` 当两个 animator 参数各占一棵树** —— 合并是普遍的，
   而且**合并发生在上游**（VRCFT 的 C# 里；控制器只消费一根 `EyeLid`）。我们现在也合了，
   但合在**中间层的输出行**里（`HoFaceMiddleware.cs:244-249`）—— 与"上游合并、控制器只消费"是同一个形状。
2. **"中性 0.75"不是统一事实**：0.75（v2 血统）/ 0.7–0.8 平台（JINGO）/ 0.8（legacy）/ 0.7（kipfel 自有层）。
   **共同区间是 0.7–0.8，且轴一律是 `0..1`、`1 = 睁大`** —— **不是我们这种 `±1`、中性居中**。
3. 同一个 0.75 有**三种来源**要分清：C# 公式给、作者摆坐标给、参数默认值给（同厂商内部都不一致）。

⇒ 于是"照哪种血统编控制器"这件事被证据定住了。**控制器侧没有预设**（模板那套已删除，见 §3），
但**中间层有一份内置默认**（没配置文件时用它：`HoFaceMiddlewareDefaults`，`HoFaceMiddleware.cs:176-272`），
眼睑那两根轴就在里面 —— 想照哪个血统，改那几行、或者写自己的配置文件即可。这三条留着是因为它们决定**你该抄谁的形状**：

- **VRCFT 官方模板 ARKit 支**：每眼一根 `0..1`（中性 0.75）+ 5 姿势 `FreeformCartesian2D`（含 squint）。
  姿势数值已一手测出，且**只写** `eyeBlink*/eyeWide*/eyeSquint*` 三键（层 Override/weight 1，值是 0~100 绝对值）：
  `(0,0)`→blink100 / `(0.75,0)`→全 0 / `(1,0)`→wide100 / `(0.25,1)`→blink90+squint100 / `(0.75,1)`→squint100。
- **跨血统的共同核**：**每眼一根 `0..1`、中性落 0.7–0.8**；形状用 `Simple1D` 平台式
  （JINGO/kipfel 那种 4 阈值 + squint 独立一根轴）—— 4 个资产这么做，比 v2 的 2D 五姿势更"common"。

**⚠️ JINGO 血统的姿势写的是模型专有键**（`EyeClosedJoyfulLeft/Right`、`EyeDilationLeft/Right`、
`EyeIrisSmallLeft/Right`、`BrowLowererLeft/Right`），标准 ARKit 网格上**没有这些键**。
照那份血统编控制器，目标模型就得有这些键 —— 没有的那些格子落不到任何网格上，
「控制器编辑」页的**「详情」栏**会把它们列出来（`HoFaceAssetInfo.missing`，`HoFaceAnimationAssets.cs:364-409`；
面板 `HoFaceControllerToolWindow.cs:379-386`）。装配时曲线原样留着，不删。

**仍未取证、因此不许当成"标准姿势表"到处套的**：这些姿势片段**各自写多少**（参考资产的姿势写的是
它们自己模型的专有键）。在测出"每个姿势 → `eyeBlink*/eyeWide*/eyeSquint*` 的数值"之前，
别把这些数字当成跨血统的事实。

**还待定的一件事**：`smile`/`frown` 用哪种形状 —— Jerry 的两棵半轴树（各 2 姿势）还是 Shinano 的一棵 4 姿势树（±0.8，Min/Max 也收窄）。

## 5. 命名约定（**作者约定**，不是代码规则）

> **代码只认其中一小部分。** `Runtime/FaceTracking/HoFaceNaming.cs` 里只剩：
> `ParameterRoot = "Ho/Drive"`、两根轴语义 `BlinkWide` / `Squint`、以及 `LidAxis(side, horizontal)`
> （`HoFaceNaming.cs:14-31`）—— 也就是 `Ho/Drive/Lid/<Left|Right>/<BlinkWide|Squint>` 这 4 个名字。
> 它们是**中间层默认输出行**用的名字（`HoFaceMiddleware.cs:240-255`），**不是**会话硬编码的写入目标：
> 会话只写"控制器里真有这个参数"的那些输出行（`HoFaceAnimationSession.cs:242`），
> 控制器里没有它们（别的血统）时，这几笔写不进去就算了，不猜也不补（`HoFaceNaming.cs:11-12`）。
> **格子名、树名、坐标代码完全不认**（`HoFaceNaming.cs:6-9`：控制器里那些树的形状、每格写什么键，
> 都不再由代码规定）——它们活在控制器里，这份约定是给**编控制器与姿势的人**看的。

**名字是唯一能把「混合树里的格子」和「去 DCC 做形态键时的那张清单」对上的东西**，
所以它定在**一处**（这份文档 + `HoFaceNaming.cs` 里那几个参数名），不许各自拼字符串（散着写迟早会漂）。

```
<树>__<x语义>__<y语义>__<方阵>X<x>Y<y>          例：LidL__BlinkWide__Squint__A3X2Y2
```

### 5.1 名字的形状

**每个字段都用 `__`（双下划线）分隔，名字里不出现单下划线** —— 于是解析不需要任何约定，
四段各自回答一个问题：

| 字段 | 规则 |
| --- | --- |
| `<树>` | `LidL` / `LidR` / …。**必须有**：左右两棵树除了名字完全一样，不带侧就会重名 |
| `<x语义>` | X 轴的语义，**正端在前**：眼睑开合「闭 / 睁大」→ `BlinkWide` |
| `<y语义>` | Y 轴的语义，同上：单端轴只写正端 → `Squint`。**这两段合起来就是"这棵树在混什么"**，光看片段名就知道，不必回头翻代码 |
| `<方阵>` | `A` + 每轴刻度数，**必定方形**（两根轴同一套刻度）。`A3` = 每轴 3 个刻度 |
| `X<x>Y<y>` | 坐标：**从 0 开始、左下为原点、永不出现负号**；可以取小数（`X0.5`） |

**为什么用方阵而不是"3x2"**：奇数刻度的中间刻度就是中线，两轴刻度数一致，于是
"中轴在哪"不再需要一条规则，也不会被误读成"关于 0 对称的 0.5 坐标" —— 而后者恰恰是默认误读
（Unity 的 2D 混合树心智模型就是"motion 围绕中心 `(0,0)` 组成多边形"）。
**刻度线性铺满该轴的值域**，顺序即从负端到正端：

| 轴 | 值域 | `A3` 的刻度 → 轴值 |
| --- | --- | --- |
| X 眼睑开合（双边） | −1 睁大 … +1 闭 | `X0`→−1、`X1`→0（**中线**）、`X2`→+1 |
| Y 眼睑眯眼（单边） | 0 不眯 … +1 眯满 | `Y0`→0、`Y1`→0.5（半眯）、`Y2`→+1 |

**方阵不必铺满。** 我们 X 走满三档，Y 只用 `Y0` 与 `Y2` 两行，中间 `Y1`（半眯）空着，空行交给树插值。
**下面按 Unity 2D 混合树图的朝向打印**（坐标随轴值增加 → `Y2` 在图的上方、`Y0` 在下方）：

```
LidL__BlinkWide__Squint__A3X0Y2 睁大+眯   …X1Y2 眯   …X2Y2 闭+眯     ← Y2（眯满）= 图的上方
LidL__BlinkWide__Squint__A3X0Y1 （空）    …X1Y1 （空） …X2Y1 （空）   ← Y1（半眯，没摆）
LidL__BlinkWide__Squint__A3X0Y0 睁大      …X1Y0 中性  …X2Y0 闭        ← Y0（不眯）= 图的下方
```
（上表为省宽度省略了公共前缀 `LidL__BlinkWide__Squint__`。）

> **原点为什么在左下、为什么不是 DX 的左上。** DX 的左上原点是**光栅/渲染目标/屏幕**的约定
> （行主序 + V 轴向下），它管纹理与帧缓冲，不管参数网格；而且 Unity 自己就两套并用
> （屏幕/GUI 左上，纹理 UV 左下，`Texture2D` 第 0 行在底，DX 平台还要靠 `UNITY_UV_STARTS_AT_TOP` 抹平）。
> 我们的网格**只被 Unity 的 2D 混合树图与本文档画出来**，那是一张普通笛卡尔图（`+Y` 朝上），
> 所以取左下 0、坐标随轴值单调增加 —— 名字、图、文档三边一致。
> **将来如果要把这张网格当查找纹理/矩阵索引**（2D LUT、跟别的模块交换矩阵），
> 这里必须改成**左上 0、行主序**，否则在纹理边界上一定出一次上下翻。那时记得本文档与用例一起改。

### 5.2 对照表与词典

**眼睑对照表**（左眼；右眼同构，`LidL`→`LidR`、键名 `Left`→`Right`）：
表按图的朝向排（上排 `Y2` 在前）：

| 片段名 | 坐标 | 树里 `Pos`（轴值） | 这一格写的键与值 |
| --- | --- | --- | --- |
| `LidL__BlinkWide__Squint__A3X0Y2` | X0 Y2 | (−1, +1) | `eyeWideLeft 100` + `eyeSquintLeft 100` |
| `LidL__BlinkWide__Squint__A3X1Y2` | X1 Y2 | (0, +1) | `eyeBlinkLeft 90` + `eyeSquintLeft 100` |
| `LidL__BlinkWide__Squint__A3X2Y2` | X2 Y2 | (+1, +1) | `eyeBlinkLeft 100` |
| `LidL__BlinkWide__Squint__A3X0Y0` | X0 Y0 | (−1, 0) | `eyeWideLeft 100` |
| `LidL__BlinkWide__Squint__A3X1Y0` | X1 Y0 | (0, 0) | 全 0 |
| `LidL__BlinkWide__Squint__A3X2Y0` | X2 Y0 | (+1, 0) | `eyeBlinkLeft 100` |

**眯那一行（`Y2`）的 `blink 90` 是这张表最有价值的一条**：眯眼自带眨眼量，所以"又眨又眯"永远不会
加和成 200。名字里已经有轴语义（知道在混什么）与坐标（知道哪一格），**但回答不了"这一格写多少"**
—— 那必须查这张表。`A3X2Y2`（闭+眯）那格的**艺术决定**同理："眨满时眯眼还剩多少"。

**树名与参数名**：

| 东西 | 名字 | 为什么 |
| --- | --- | --- |
| 眼睑 2D 树 | `LidL` / `LidR` | 片段名的 `<树>` 段就是它 |
| 区域 Direct 子树 | `EyeRegion` / `LipRegion` | 只是**分块**用；门控参数已经不由我们注入（§3.2） |
| 眼睑两根轴参数 | `Ho/Drive/Lid/Left/BlinkWide`、`…/Squint` | **与片段名里 `__` 前那两段是同一批词**，所以"名字 → 参数"是机械映射；`A3` 的 X 轴就是 `BlinkWide`、Y 轴就是 `Squint`。这 4 个名字是代码里唯一还留着的（`HoFaceNaming.cs:28-30`），由中间层的默认输出行写出去（`HoFaceMiddleware.cs:244-255`） |
| 通道参数 | `ARKit/<键名>` | **不变** —— 它是输入语义，不属于驱动层产出。它是默认中间层的输出行名（`HoFaceMiddleware.cs:236-238`），会话也按这个前缀把值显示到面板那一行（`HoFaceAnimationSession.cs:471-475`） |

**参数名一律 ASCII**（它是给后端吃的，以后 VRChat 参数名有字符限制），只有片段名与树名是给人看的。
`Ho/Drive/Lid/*` 是默认中间层会去写的那几个名字 —— 写之前先查这个参数在不在，
所以控制器里没有它们（别的血统）时，这几笔写不进去就算了，不猜也不补。

**加新树时照抄这张词典，不要逐棵树拍脑袋**（方阵一律 `<A>+刻度数`，坐标左下为原点、全正，轴语义写进参数名）：

| 轴 | +1 / −1 | 参数里的轴名 | 方阵与占用 | 摆了的格子 |
| --- | --- | --- | --- | --- |
| 眼睑开合 | 闭 / 睁大 | `BlinkWide` | `A3` 的 X 轴 | `X0` `X1` `X2` 三档全走 |
| 眼睑眯眼 | 眯 / 无 | `Squint` | `A3` 的 Y 轴 | 只用 `Y0`（不眯）与 `Y2`（眯满），`Y1` 半眯空着 |
| 凝视内外 | 内 / 外 | `InOut` | 未来 | —— |
| 凝视上下 | 上 / 下 | `UpDown` | 未来 | —— |
| 唇角 | 欣 / 悲 | `SmileFrown` | 未来 1D（单轴，格式待定） | —— |
| 横向 | 右 / 左 | `RightLeft` | 未来 1D | —— |

**归属不靠名字判断**：装配时按"调试对象里谁有这个键"重绑，不靠树名/片段名认领 ——
所以改名字不会漏掉任何东西（名字是控制器与动画那侧的数据，按键名对得上就行，
`HoFaceAnimationAssets.cs:294-340`）。

## 6. 复现

```powershell
# 控制器结构（层 / 状态 / 树骨架 / 参数驱动 / 参数表）
python .research/inspect_arkit_controller.py     # → .research/arkit-controller-report.txt
python .research/inspect_shared_controller.py    # → .research/shared-controller-report.txt

# 判别性实验（Direct 树 × Write Defaults）、装配与就地重绑那批断言：同一套用例
# 跑法见 docs/pitfalls/VALIDATION_LOOP.md，探针行看 HO_WDON / HO_WDOFF
```

**解析这些 YAML 的坑（字段名、子资产、转储会骗人的地方）单开了一份**：
[踩过的坑 · Unity YAML 与转储](pitfalls/UNITY_YAML_AND_DUMPS.md)。
