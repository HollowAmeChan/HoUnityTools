# 面捕控制器结构：Jerry 模板 vs 我们的做法

日期：2026-09-22（§5/§6 于 2026-09-23 按现状重写；§5 在删掉生成器后改成"模板 + 动画文件夹 → 装配"）。
本文是**源码实测 + 一次判别性实验**的记录：参考实现怎么搭的、我们怎么把模板与现成动画装配成控制器、
以及「Direct 树 + Write Defaults 关闭会发散」这条判别性实验。
数据来自 `.research/VRCFaceTracking-Templates` 与 `D:\UnityVrcVCC_Projects\VrcMaster`（本地检出，gitignore）
与 `.research/UnityFaceValidation` 的批处理用例。

> 怎么用 / 中间层做什么 / 什么能进树：见 [面捕工作流](FACE_TRACKING_WORKFLOW.md)、
> [面捕中间层处理](FACE_TRACKING_MIDDLE_LAYER.md)、[混合树的能力边界](BLEND_TREE_LIMITS.md)。

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

## 2. 我们原来的生成器：一层一个形态键（已废弃）

`HoFaceAnimationAssets.Generate` 曾经生成 **每个形态键一个独立 Override 图层**，每层一棵 Simple1D 树、两个片段（0 / 100）。一个 52 键的模型 ≈ **52 个图层 + 104 个片段**。

代码里的理由是：

> Independent Override layers avoid Direct-tree normalization and cross-channel attenuation.
> （独立 Override 图层避免 Direct 树的归一化与通道间削弱）

**这条理由是错的 —— 实测否掉了。**（第 5 节记录了替代实现与结果。）

## 3. 判别性实验：Direct 树到底会不会削弱

用例在 `Tests~/FaceTrackingValidation.cs`。同一个模型、同一个 Direct 树（1 图层、52 个子节点、每个子节点用自己的 ARKit 参数当权重），只改一个变量：

| 条件 | jawOpen（参数 0.6） | mouthSmileLeft（参数 0.8） | 结论 |
| --- | --- | --- | --- |
| **Direct + Write Defaults 开** | **60.0** | **80.0** | 精确等于 `参数 × 100`。**不归一化、不互相削弱** |
| Direct + Write Defaults 关（干净物体） | 0 | 0 | 归零 |
| Direct + Write Defaults 关（场景里有基础动画在写同一个键） | 98.98 → 246.28 → 1059.33 | 131.97 → 531.97 | **发散**：逐帧拿"当前值"当基准反复混合 |

`98.976` 精确等于递推 `out ← 0.6×100 + 0.4×out` 的第五项，验证了机制：**Direct 树里权重和不足 1 的那部分 `(1 − Σw)` 会与"当前值"混合；写默认值关闭时这个"当前值"永远不会被复位，于是每帧往上爬，最终发散。**

所以：

- **Direct 树本身没有问题**，Unity 官方文档也明说 Direct 就是用来 map 参数到子权重、并点名"可用于混合表情的形态键"（[Direct blending](https://docs.unity3d.com/6/Documentation/Manual/BlendTree-DirectBlending.html)）。
- **不可用的组合是「Direct 树 + Write Defaults Off」。** 而 Jerry 的控制器**所有状态都是 WD 开** —— 这才是两边真正的分岔点。

## 4. 那我们的 52 个图层怎么评价

**结论：不是"不合理"，但它是为一个已经被放弃的设计付的代价。**

- 当初选多层，表面理由是"避免 Direct 归一化"——**这条已被实验否定**。
- 真正的约束是：我们的管线**强制 Write Defaults 关闭**（`HoFaceAnimationAssets.Compile` 会拒绝 WD On）。在那条约束下 Direct 树确实不能用，`Simple1D + 独立 Override 层`是当时唯一可行的绕法。
- 但那条约束来自**早期"接管角色 Animator、把身体层和面部层叠进同一个 PlayableGraph"的设计**。那套设计已经被实测否掉，改成了**影子求值**（见设计文档 7.1）。

**关键推论：影子是隔离的，没有任何别的写入者。** WD On 会把控制器里出现过的属性每帧复位到默认值 —— 在共享 Animator 的旧设计里这会踩掉身体动画，但在只跑面部控制器的影子台上**完全无害**。也就是说：

> **"必须 WD Off" 现在是一条过期约束。**

放开它，就能换成 Jerry 那种结构，图层数从 52 降到 **1**。

## 5. 现在的做法：模板 + 动画文件夹 + 驱动对象 → 装配（2026-09-23 改）

**代码不生成树，也不生成姿势。** 三件事分开，各归各的作者：

| 谁 | 出什么 | 长什么样 |
| --- | --- | --- |
| **混合树模板**（作者在混合树编辑器里编） | 树形、坐标、门控、参数 | 一份完整 `.controller`；它引用的每个片段就是一个**槽位** |
| **动画文件夹**（形态键基础动画生成器 / 手 K） | 姿势数据 | 一堆 `.anim`，文件名 = 槽位名（比如每个形态键一份 `<键名>.anim`） |
| **驱动对象**（组件上的网格列表） | 写谁 | 这台角色上要驱动的网格 |

初始化（**装配**）就是把这三样合起来：

```
混合树模板（.controller）
  ↓ ① 整份复制到目标路径（层、状态、参数、子树、子片段全跟着走）
  ↓ ② 按槽位名把动画文件夹里的同名 .anim 填进去（没找到就保留模板自带的那份）
  ↓ ③ 把每条形态键曲线重绑到「驱动对象」列表里的网格上
  ↓ ④ 外部片段（别人的 .anim）先复制成本文件的子资产再改，树里的引用一起换掉
目标角色的面部控制器
```

| 项 | 现状 |
| --- | --- |
| 图层 / 状态 / 参数 | **跟着模板走**，我们不增不减（门控、轴这些名字是模板里就有的） |
| 槽位填充 | 文件夹里同名 `.anim` **顶替**模板自带的那份；折叠框「动画填充」实时列出每个槽位是"文件夹 / 模板自带 / 缺" |
| 驱动对象 | 曲线按"列出的网格里谁有这个键"摊开，一个键可以落在多个网格上 |
| 模型上没有的键 | 那条曲线**原样留着不动**（作者的格子数据不丢），但结构摘要会报出"这些格子落不到任何网格上" |
| 覆盖式重新装配 | 文件整个换掉，**GUID 不变** —— 场景里引用它的地方（窥视对象的 Animator）不会断 |
| 模板与文件夹 | **一个字节都不改**（文件夹里的片段是复制出来再改的） |

**为什么动画要放在文件夹里**：树是"结构"，动画是"数据"。分开之后，改姿势不用碰树、换模型不用重做动画
（装配时按**形态键名**重绑，这份文件夹跟模型无关），而且"缺哪些动画"变成一个能显示出来的事实。
"缺"有三种：文件夹里没有、模板自带的片段也没写任何键 —— 面板的折叠框逐槽位说清楚是哪一种。

**代价是诚实的**：我们不再能保证控制器的形状，所以面板改成**从资产读实况**
（几层几个状态、会写哪些键、哪些键这台模型没有），而不是从配置推断。

**边界仍在**：运行期只允许形态键曲线；无 Behaviour、无同步图层、无事件/对象曲线（`Compile` 会拦）。
模板带了这些时装配不拦，但**开始驱动时**会被拒，面板会说是哪一条。

### 5.1 装配之后，谁负责什么

| 东西 | 谁管 |
| --- | --- |
| 树形、格子坐标、门控与轴参数的名字 | **混合树模板** |
| 每格写哪个键、写多少 | **动画文件夹里的片段**（模板自带的片段只是兜底） |
| 动画驱动哪些网格 | 组件上的「驱动对象」列表（装配时作用于曲线绑定） |
| 参数的值怎么算出来 | 中间层（[面捕中间层处理](FACE_TRACKING_MIDDLE_LAYER.md)）；控制器只等着被喂 |
| 哪些键算我们拥有的、要不要抄回真模型 | 会话（区域闸 + 通道模式，见 [调试组件设计](FACE_TRACKING_DEBUGGER_DESIGN.md) §6） |

**幂等**：装配是幂等的（反复装配结果一致）。换模板、换文件夹、改驱动对象列表，都只是再装配一次；
把某个网格从列表里去掉，它上面的绑定也就没了 —— 键在别的网格上时不会留下孤儿绑定。

**下面 §5.2 那张表不是"我们现在生成的形状"**，而是**编模板与姿势时的作者约定**（我们上一版生成器
就是照它出的，所以留着当参考）。这个仓库里没有 `ho-2d-test1.controller`：那份模板在作者手上，
仓库只负责装配它。

### 5.2 命名规则与对照表（**作者约定**，不是代码规则）

> 代码不再生产这些名字了（§5）：格子名与树名活在**混合树模板**里。这份约定留给编模板的人 ——
> `ho-2d-test1` 那份模板按它编，以后新编的树也照它编。**运行期只有参数名还被代码引用**
> （会话要往 `Ho/Drive/Gate/*`、`Ho/Drive/Lid/*` 里写值，写之前先查参数在不在）。

**名字是唯一能把「混合树里的格子」和「去 DCC 做形态键时的那张清单」对上的东西**，
所以它定在**一处**（这份文档 + `Runtime/FaceTracking/HoFaceNaming.cs` 里的那几个参数名），
不许各自拼字符串（散着写迟早会漂）。

```
<树>__<x语义>__<y语义>__<方阵>X<x>Y<y>          例：LidL__BlinkWide__Squint__A3X2Y2
```

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
| 区域 Direct 子树 | `EyeRegion` / `LipRegion` | |
| 区域门控参数 | `Ho/Drive/Gate/Eye`、`…/Lip` | 门控是**参数**，可被任何东西驱动 |
| 眼睑两根轴参数 | `Ho/Drive/Lid/Left/BlinkWide`、`…/Squint` | **与片段名里 `__` 前那两段是同一批词**，所以"名字 → 参数"是机械映射；`A3` 的 X 轴就是 `BlinkWide`、Y 轴就是 `Squint` |
| 通道参数 | `ARKit/<键名>` | **不变** —— 它是输入语义，不属于驱动层产出 |

**参数名一律 ASCII**（它是给后端吃的，以后 VRChat 参数名有字符限制），只有片段名与树名是给人看的。
`Ho/Drive/Gate/*` 与 `Ho/Drive/Lid/*` 是**会话会去写**的那几个名字 —— 写之前先查这个参数在不在，
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

**归属不靠名字判断**：装配时按"驱动对象里谁有这个键"重绑，不靠树名/片段名认领 ——
所以改名字不会漏掉任何东西（名字是模板与动画那侧的数据，按键名对得上就行）。

## 6. 参考实现里我们抄了 / 还没抄的

| 参考实现的东西 | 我们 | 落在哪 |
| --- | --- | --- |
| **二维区域**（眼睑 `开合 × 眯眼` 五格姿势） | ✅ 抄了（我们那份模板的六格是它的直系后代） | 混合树模板（§5） |
| **区域门控**（`EyeTrackingActive` / `LipTrackingActive`） | ✅ 抄了 | `Ho/Drive/Gate/*` 参数 + 两棵区域子树（模板里） |
| **参数预处理链**（平滑 / 灵敏度 / 上下限） | ✅ 抄了，但**在树外面** | 中间层（[面捕中间层处理](FACE_TRACKING_MIDDLE_LAYER.md)） |
| **EyeSync 式左右交叉混合** | ✅ 抄了语义，**实现放在 C#** | `HoFaceEyeSync`（VRCFT 也在 C#：`Correctors.BlendOpposingParams`） |
| **耦合**：`SmileFrown` 同时驱动嘴角 / 脸颊 / 酒窝 | ✅ 已确认（资产A L207-218、L417-428） | 我们**没抄**：耦合留在中间层 / 模板里 |
| `JawOpen` 是 `(JawOpen, MouthClosed)` 的二维空间 | ⚠️ **未能确认**：资产A 里以 `JawOpen` 为 blendParameter 的三处（L2052 / L10891 / L14791）都是 `m_BlendType: 0`（1D），那个 `m_BlendParameterY: …MouthClosed` 是 1D 树的**残留字段** | 我们没抄，也不需要 —— 见 §6.1 |
| **限制 / 修正**：若干子树外面套 `FaceTrackingLimits` | ❌ 没抄 | 中间层欠账 |
| **眼球**：一棵 2D 树管四方向，坐标 **±0.7**（中性 `(0,0)`） | ✅ 已确认五姿势；Shinano 加对角共 **9 姿势** | ❌ 还没做（要加就加在混合树模板里，§6.1 有完整形状） |

后三条都是同一个处方的更多例子：**会重叠的语义进同一棵树 / 参数算术留在外面**
（判据见 [混合树的能力边界](BLEND_TREE_LIMITS.md)）。

### 6.1 成对通道与双向轴：一手取证（2026-09-23）

**为什么单开一节**：我们文档里"参考实现怎么做"的结论，有一部分来自 `.research/` 里的**转储**，
而这次取证发现**转储有两处会骗人**（见本节末尾）。所以下面每一条都回到**原始 YAML**（并用 `.meta`
把 GUID 解成片段名）复核过，"我们的记录"与"原始资产"不一致的地方也标出来了。

一手资产（都在本机）：
- **A** = `D:\Unity_Fork\HoUnityTools\.research\VRCFaceTracking-Templates\Packages\adjerry91.vrcft.templates\Animators\ARkit Blendshapes\FX - Face Tracking - ARKit Blendshapes.controller`
- **B** = kipfel 随包分发的旧版副本（路径见 `.research` 调查记录），与 A 多处**字节级同构**
- **C** = Shinano 第三方实现 `FX_FT_added 2.controller`

| 通道对 | 成熟实现的实际形状（一手） | 出处 | 与我们的差异 |
| --- | --- | --- | --- |
| `jawLeft` / `jawRight` | **合成一根 `JawX ∈ [−1,1]`，3 姿势 1D**：thr `−1 = Jaw_Left` / `0 = Jaw_X 0`（中性）/ `+1 = Jaw_Right`。**控制器里根本没有 `JawLeft`/`JawRight` 参数** | A `Jaw X Blend` L2186-2218；参数表 L7102 | 我们是两个直通叶子（没合） |
| `mouthLeft` / `mouthRight` | 同形：`Mouth X Blend`，3 姿势 1D，`−1/0/+1` | A L8207-8245；B L8088-8120（同构） | 同上 |
| `mouthSmile` / `mouthFrown`（每侧） | **合成一根 `SmileFrownLeft/Right ∈ [−1,1]`，但树是两棵各 2 姿势的 1D 分占半轴**：欣 `[0,1]`、悲 `[−1,0]` —— **不是一棵 3 姿势树** | A `Mouth Smile Left Blend` L9566-9587、`Mouth Frown Left Blend` L5259-5280；`…Right` L609-631 | 我们是直通叶子 |
| ↳ 反例 | Shinano 把同一件事做成**一棵 4 姿势 1D**：thr `−0.8 / −0.1 / +0.1 / +0.8`（Min/Max 也收窄到 ±0.8） | C `Mouth Sad Smile Left` L20266-20306 | —— |
| `eyeLookIn` / `eyeLookOut` | **压成一根水平轴，坐标对称 ±0.7**（不是 0..1 半轴） | A `Eye Look Left Blend` L2343-2397（`m_BlendType: 1` = SimpleDirectional2D） | 我们是直通叶子 |
| `eyeLookUp` / `eyeLookDown` | **第二根垂直轴，同为 ±0.7，与水平轴同处一棵 2D 树（5 姿势）**；左右眼用不同水平轴，垂直轴**共用** | 同上；Shinano 加对角共 9 姿势（C L21526-） | 同上 |
| `eyeBlink` / `eyeWide` | **不是 ±1 双向轴**：C# 先加成一根 **0..1 的"眼睑位置"** `EyeLid = Openness*0.75 + EyeWide*0.25` —— **中性落在 0.75**、闭眼 0、睁大 1.0；再与 `EyeSquint` 组成 FreeformCartesian2D（5 姿势：Blink `(0,0)` / Neutral `(0.75,0)` / Wide `(1,0)` / Squint `(0.25,1)` / OpenSquint `(0.75,1)`） | C# `UnifiedExpressionsParameters.cs` L57-58；A `Right Eye Lid Blend` L10898-10952 | **⚠️ 我们的眼睑轴是 ±1、中性在中间（`A3` 的 `X1`）**，与成熟做法**不同**（见 §6.1 末尾"待定"） |
| N 通道 → 2 轴 → 一棵 2D 树 | **有，至少 4 个实例**（眼动 4→2；眼睑 3→2；`Brow Sad` 用左右两根双向轴；`Brow Sad Emulation` `(−0.7,−0.7)`） | A L2343 / L10898 / L3687-3742 | 我们只有眼睑这一棵 |

**"合并出来的那个值是谁写的"这件事，成熟实现和我们架构一致（一手确认）**：`FT/v2/*` 由**外部
VRCFT C# 经 OSC 写入**（模板 README 明写"不要拿 `FT/v2/` 当输入，那是 OSC 原始值"），进图后再由
**动画曲线直接写 Animator 参数**（clip `attribute: OSCm/Proxy/…`、`path:` 为空、`classID: 95`、
单关键帧；Proxy 常量 ±1.25 = clamp(Smooth,±1)×1.25）装配/夹取，**树只读 `OSCm/Proxy/*`，自己不产参数**。
⇒ 我们"**参数算术留在树外面、树只消费**"的选择与成熟做法一致，不需要改。

**这次取证纠正/推翻的我们自己的记录**：

| 我们的说法 | 结论 |
| --- | --- |
| 归档 `FACE_TRACKING_PIPELINE_SPLIT.md:1249`「Shinano `SmileSadLeft/Right` 是 3 姿势双向 1D」 | ❌ **错**：Shinano 是 **4 姿势、阈值 −0.8/−0.1/+0.1/+0.8**（C L20266-20306） |
| 本文 §6 旧版「`JawOpen` 是 `(JawOpen, MouthClosed)` 二维空间」 | ⚠️ **未能确认**（A 里那三处都是 1D + 残留 `m_BlendParameterY`） |
| 归档 `arkit-mouthclose-report.md:446`「真正二维的树只有 `EyeRightX × EyeY` 与 `EyeLeftX × EyeY`」 | ✅ 确认（坐标 `(0.7,0)` / `(0,0.7)` / `(0,-0.7)`） |
| `BLEND_TREE_LIMITS.md`「`OSCm/Proxy/v2/*` 那些 `*Smoother*` 是曲线驱动参数」 | ✅ 确认（空 path + `classID 95`） |
| 全局左右合一的 `SmileSad`/`SmileFrown` | 参数在 C# 里存在（左右取平均），但**三个成熟控制器里没有一棵树消费它** —— 我们不用做 |

**⚠️ 转储的两个坑（复现时必读，见 §7）**：`inspect_arkit_controller.py` 的 `thr=` 列**不打印 0 与 −1**，
所以 3 子节点的 1D 树在转储里看起来像"两手两脚"；`inspect_bigtree.py` 打印的类型标签
（`[1D]/[Simple1D]/[FreeDir2D]`）**与原始 `m_BlendType` 不符，不能引用**。
凡是从这两个转储得出的形状结论，都要回原始 YAML 复核。

**眼睑开合轴：跨血统一手指证（2026-09-23 第二批）**

上一段里"中性 0.75"那条只有**一个血统**（VRCFT 官方模板 + 它的 C#），不足以说"成熟做法"。
补查了独立血统后，结论更硬了 —— 而且**我们现在的做法在所有成熟资产里都找不到**：

| 血统 | 眼睑轴 | 中性落点 | 树 | 姿势 |
| --- | --- | --- | --- | --- |
| **VRCFT 官方模板**（ARKit 支 L4297 / UE 支 L4041；kipfel 随包副本与它同血统、同 clip GUID，**不算独立来源**） | 每眼一根、**0..1**（1 = 睁大） | **0.75** —— C# `Openness*0.75 + EyeWide*0.25` 给的（`UnifiedExpressionsParameters.cs` L57-58），作者又把 Neutral 摆到 `m_Position {x:0.75,y:0}`，参数默认也是 0.75：**三处一致** | `FreeformCartesian2D` | **5**：Blink `(0,0)` / Neutral `(0.75,0)` / Wide `(1,0)` / Squint `(0.25,1)` / OpenSquint `(0.75,1)` |
| **JINGO 系**（Shinano / Mafuyu / Manuka，**共享动画 GUID ⇒ 同一血统**） | 每眼一根、**0..1** | **0.7–0.8 平台**：作者把同一个中性片段**钉在两个阈值上**（0.75 落在平台正中）。参数默认三条不一：Shinano 控制器 0.75；Mafuyu 控制器 0 而 Expression Parameters 0.75；Manuka 控制器 0 而 Expression Parameters **0.8** | **Simple1D**（这根轴上没有 squint；squint 在别处） | 4（t = 0 / 0.7 / 0.8 / 1） |
| **legacy / SRanipal**（同 repo 的另一代参数） | 每眼一根、**−1..1**（squeeze 占负半轴） | **0.8** —— C# `EyeWide*0.2 + Openness*0.8` 给的 | Simple1D | 4 |
| **kipfel 自有 FX 层**（不是模板副本） | 每眼一根、0..1 | **t = 0.7**（作者摆的；`m_Position` 里的 `0.75` 在 Simple1D 下不参与运算，是 2D→1D 残留） | Simple1D | 3 |
| **我们现在** | **两棵直通叶子**（根本没合并） | —— | —— | —— |

三条硬结论（都有一手出处）：

1. **没有任何一个成熟血统把 `blink` / `wide` 当两个 animator 参数各占一棵树** —— 合并是普遍的，
   而且**合并发生在上游**（VRCFT 的 C# 里；控制器只消费一根 `EyeLid`）。我们"两个直通叶子"确实不对。
2. **"中性 0.75"不是统一事实**：0.75（v2 血统）/ 0.7–0.8 平台（JINGO）/ 0.8（legacy）/ 0.7（kipfel 自有层）。
   **共同区间是 0.7–0.8，且轴一律是 `0..1`、`1 = 睁大`** —— **不是我们这种 `±1`、中性居中**。
3. 同一个 0.75 有**三种来源**要分清：C# 公式给、作者摆坐标给、参数默认值给（同厂商内部都不一致）。

⇒ 于是"照哪种血统编控制器"这件事被证据定住了。**代码里不再有预设**（模板那套已删除，见 §5），
这三条留着是因为它们决定**你该抄谁的形状**：

- **VRCFT 官方模板 ARKit 支**：每眼一根 `0..1`（中性 0.75）+ 5 姿势 `FreeformCartesian2D`（含 squint）。
  姿势数值已一手测出，且**只写** `eyeBlink*/eyeWide*/eyeSquint*` 三键（层 Override/weight 1，值是 0~100 绝对值）：
  `(0,0)`→blink100 / `(0.75,0)`→全 0 / `(1,0)`→wide100 / `(0.25,1)`→blink90+squint100 / `(0.75,1)`→squint100。
- **跨血统的共同核**：**每眼一根 `0..1`、中性落 0.7–0.8**；形状用 `Simple1D` 平台式
  （JINGO/kipfel 那种 4 阈值 + squint 独立一根轴）—— 4 个资产这么做，比 v2 的 2D 五姿势更"common"。

**⚠️ JINGO 血统的姿势写的是模型专有键**（`EyeClosedJoyfulLeft/Right`、`EyeDilationLeft/Right`、
`EyeIrisSmallLeft/Right`、`BrowLowererLeft/Right`），标准 ARKit 网格上**没有这些键**。
照那份血统编控制器，目标模型就得有这些键 —— 没有的那些格子落不到任何网格上，
面板的「控制器结构」会把它们列出来（装配时曲线原样留着，不删）。

**仍未取证、因此不许当成"标准姿势表"到处套的**：这些姿势片段**各自写多少**（参考资产的姿势写的是
它们自己模型的专有键）。在测出"每个姿势 → `eyeBlink*/eyeWide*/eyeSquint*` 的数值"之前，
别把这些数字当成跨血统的事实。

**还待定的一件事**：`smile`/`frown` 用哪种形状 —— Jerry 的两棵半轴树（各 2 姿势）还是 Shinano 的一棵 4 姿势树（±0.8，Min/Max 也收窄）。

## 7. 复现

```powershell
# 控制器结构（层 / 状态 / 树骨架 / 参数驱动 / 参数表）
python .research/inspect_arkit_controller.py     # → .research/arkit-controller-report.txt
python .research/inspect_shared_controller.py    # → .research/shared-controller-report.txt

# 判别性实验（Direct 树 × Write Defaults）
# 把 Tests~/FaceTrackingValidation.cs 放进一次性 Unity 工程的 Assets/Editor，
# 跑 HoFaceTrackingValidation.RunBatch，看 HO_WDON / HO_WDOFF 两行
# （同一份用例也覆盖装配：整份复制、外部片段复制、重绑驱动对象、覆盖不改 GUID）
```

踩过的解析坑：Unity YAML 里混合树的子节点字段是 `m_Childs`（不是 `m_Children`）、类型字段是 `m_BlendType`（不是 `m_Type`）；文档 id 在 `--- !u!206 &-123` 里，`&` 后面直接换行，不能用 `split(' ')` 截。

**转储本身的两个坑（2026-09-23 取证时发现，别用它们下形状结论）**：
- `inspect_arkit_controller.py` 的 `thr=` 列**不打印等于 0 与 −1 的阈值** → 一棵 3 子节点的 1D 树
  在报告里看起来像"两手两脚"，据此会数错姿势。
- `inspect_bigtree.py` 打印的类型标签（`[1D]` / `[Simple1D]` / `[FreeDir2D]`）**与原始 `m_BlendType` 不符**，
  不可引用；要看类型请回原始 YAML。
- 片段名要用 `.meta` 把 GUID 解回来（转储里只有 `{fileID: …, guid: …}`）。
