# 面捕方案总览：VTS 裸输入 → 中间层反算 → 喂进 Warudo 官方面捕蓝图

> **这份文档只讲 Warudo 侧的落地**：产物怎么划分（几个 mod / 几个节点）、Tracking 层怎么用、节点之间怎么连线。
> Unity 侧的机制 → [面捕设计：已验证的机制层](FACE_TRACKING_DESIGN.md)；怎么用 → [面捕工作流](FACE_TRACKING_WORKFLOW.md)；
> 值怎么加工 → [面捕中间层处理](FACE_TRACKING_MIDDLE_LAYER.md)；控制器长什么样 → [面捕控制器结构](FACE_TRACKING_CONTROLLER_STRUCTURE.md)；
> 历史调查（只作证据、不是现状）→ [`archive/`](archive/)；被咬过的现场 → [`pitfalls/`](pitfalls/)。
> 参数标准见 [参数标准表](PARAMETER_STANDARDS.md)。
>
> **标记约定**：✅ = 本机实测（有日志 / 文件 / 反射作依据，尽量给到 `文件:行`）；📖 = 官方文档或官方产物所述；
> ❓ = **未实测**的推断，可能是错的。**凡是没有 ✅ 的断言都当❓读。**
>
> **环境**：Warudo `0.15.0`（读自 `DefaultScene.json` 的 `appVersion`）；mod 侧工程用 Mod Tool 0.14.4.8 / Unity 2021.3.45f2
> （见 `Assets/HoWarudoModTests/docs/打包与脚本规范.md:4`）；本包侧的验证工程是 Unity 6000.3.15f1
> （`.research/UnityFaceValidation2/ProjectSettings/ProjectVersion.txt`）。
>
> **取证来源**（本文每条结论都指回这里之一）：
> 1. **真机程序集反射**：`.research/warudo-knobs`（`dotnet run -- <dll> <类型名>`；DLL 在 `D:\steam\steamapps\common\Warudo\Warudo_Data\Managed`）——
>    三处比对同一件事：**反射**（编译期真值、含继承）、场景里节点实例的 **`dataInputs`**（该实例未被连线的口）、**`dataConnections`**（被连线的口）。
> 2. **本机场景文件**：`Warudo_Data/StreamingAssets/Scenes/DefaultScene.json` —— 官方那张图 `面部追踪 - iFacialMocap` 的
>    节点、端口类型、每个口的当前值、全部 25 条数据连线与 5 条流程连线都在里面。
> 3. **运行期探针输出**：`AppData\LocalLow\HakuyaLabs\Warudo\Player.log`（2026-09-24）—— 那时候那台
>    **角色探针**打出来的整段报告。⚠️ 探针本身**已于 2026-09-25 删除**（§2.0），所以本文里凡是引
>    「探针「X」一节」的地方，都是指**当时那份报告**（证据留在 `Player.log` 里），不是现在还能跑出来的东西。
> 4. **mod 源码**：`Assets/HoWarudoModTests/Mods-Ho/HoFaceTracking/`（**另一个工程**，本文只读不改）。

---

## 0. 一句话

**VTS 裸输入 → 中间层配置映射成干净参数（VBridger 那样的输入行）→ 我们的求值/控制器算 →
反算成"这个角色身上实际做了什么" → 直接喂 Warudo 官方面捕蓝图的下游三个应用节点。**

没有我们发明的标准名层：输出的键就是**角色上真实存在的键**。
（✅ 本机那个角色上，52 个 ARKit 规范名**逐字**就是它自己的形态键名 —— `Player.log` 探针「ARKit 对照：52/52」。）

---

## 1. Unity 侧（HoUnityTools 包）：今天真实是什么

* **角色预制件零组件、零引用。** 调试状态落在一个普通对象 + 一个宿主上：
  `Assets/HoFaceDebugSettings.json`（`Editor/FaceTracking/HoFaceDebugSettings.cs:10-26`），
  每帧由 `HoFaceDebugHost`（`[InitializeOnLoad]` + `EditorApplication.update`，`HoFaceDebugHost.cs:9-25`）推。
  关掉窗口调试照跑 —— tick 不在窗口里。
* **输入只剩 VTS 手机一条**：源类型枚举里只有 `VtsIphone`（`Editor/FaceTracking/HoFaceInputEnvironment.cs:17-21`）。
  iFacialMocap 的接收端**已删**（它给不出 `FaceFound`，而"丢追回中性"整条机制就靠它）。
* **中间层 `*.hoface.json` 是必填总闸**（Unity 侧）：`profilePath` 空着时面板下面全锁住
  （`HoFaceDebugSettings.cs:65-66`、`:86-87`）。它同时是**"线名 → 规范名"的唯一映射表** ——
  指定了配置文件就**只认它的输入行**，不再拿内置默认表来补（这是刻意的，不是 bug：
  `docs/pitfalls/FACE_TRACKING_PIPELINE.md` §3）。
  ⚠️ **Warudo 侧不同**：mod 的处理链节点上「配置文件」留空 = 用内置默认（见 §2.0），别把两边混起来。
* **控制器用「控制器编辑」在工程里就地装配**（菜单 `HoUnityTools/面捕/控制器编辑`）：
  先把预置目录里的控制器**复制一份到工程**，再拖进来按槽位名填片段、按形态键名重绑曲线。
  装配**不新建资产、不改名、不移动、不动 GUID**，也不碰层与参数
  （`Editor/FaceTracking/HoFaceControllerToolWindow.cs:12-30`、`:41-42`）。
* **影子台上跑控制器、只写自己拥有的键**：会话建一台隐藏 `Ho Face Shadow`（`HideAndDontSave`）+ 独立 `Animator`
  （`Editor/FaceTracking/HoFaceAnimationSession.cs:119-127`），求值完把结果抄到真实 Renderer，
  且只抄占用表里属于本次会话的键（`Runtime/FaceTracking/HoFaceOutputOwnership.cs:7-26`）。
  角色自己的 Animator / Controller / LookAt / 别的约束都不受影响。机制细节见
  [面捕设计](FACE_TRACKING_DESIGN.md)，这里不重复。
* **为什么必须这样**：Warudo 里我们的东西对角色本体无效，只能纯写入；Unity 侧调试环境要模拟同一个约束，
  否则调出来的搬过去就不一样。

### 1.1 已删概念（再出现就是错的）

| 曾经的东西 | 现在的状态 | 依据 |
|---|---|---|
| 挂在角色上的 `HoFaceTrackingDebugger` **组件** | **已删**。现在是全局面板 + `HoFaceDebugHost` 宿主 | `FACE_TRACKING_DESIGN.md:9`、`HoFaceDebugHost.cs:10-15` |
| iFacialMocap 作为**现行输入** | **已删**，只剩 VTS 手机。⚠️ 它的线名还留在**内置默认配置的输入行**里当退役字段（没有任何接收端会产出那些名字，所以那几行永远是"缺键、保持上一帧"） | `HoFaceInputEnvironment.cs:13-21`；`Runtime/FaceTracking/HoFaceMiddleware.cs:205,209-219` |
| 区域门控（把"哪块脸算数"做成注入的开关） | **已删**。改由使用者自己的混合树决定 | `HoFaceAnimationSession.cs:205`、`FACE_TRACKING_DESIGN.md:145` |
| 双眼同步（`HoFaceEyeSync`） | **已删**，那属于混合树的事 | `HoFaceAnimationSession.cs:224`、`Tests~/FaceTrackingValidation.cs:309` |
| `assemblyOutputPath` | **不存在了**（全仓库搜不到这个字段） | 全仓库无匹配 |

---

## 2. Warudo 侧：最终连线（**5 个节点，两个 mod**）

### 2.0 先把"今天真实存在什么"与"目标形态"分开

**今天真实存在什么**（✅ mod 源码 + 运行期日志）：

| 项 | 今天 | 依据 |
|---|---|---|
| mod 个数 | **1 个**：`[PluginType] Id = hollow.hofacetracking`，Name `Ho Face Tracking`，v`0.2.0` | `HoFaceTrackingPlugin.cs:32-46` |
| 节点个数 | **3 个**（`NodeTypes` 列全） | 同上 `:38-43` |
| 沙箱目录名 | `…/StreamingAssets/Plugins/Data/hollow.hofacetracking/`（= pluginId） | `Player.log`：`[Ho 面捕] 中间层配置目录：…（现有 2 份配置）` |

| 节点（面板标题） | 状态 | 干什么 |
|---|---|---|
| `Ho Face 接收器（VTS 手机）` | **正式** | 收手机 UDP，**原样**交出"线名 → 原值" + 状态 |
| `Ho Face 处理链` | **正式** | 中间层 + 控制器合一：读 `*.hoface.json`，产出与官方接收器**同形的 5 个端口** |
| `Ho调试日志` | **正式（通用件，跟面捕无关）** | 就两行：`[DataInput] object 写入`（什么类型都能接）+ `[DataInput] [MultilineInput] [Transient] 日志`（**能鼠标全选、能 Ctrl+C**）。没有按钮、没有说明文字。**为什么不用官方那种 `[Markdown]`**：那是只读渲染、选不中复制不出来；而 Warudo 没有"自绘节点 UI"的口子（`Warudo.Core` 里没有自定义绘制特性/基类方法），可编辑多行框是唯一自带文本选择的控件。**两条必须照抄**（实测）：① 写这个框要「字段赋值 **+ `BroadcastDataInput`**」—— 只 `SetDataInput` 时端口有新值而界面**不重画**；② 输入口用 `object`（用 `string` 的话非字符串上游接不进来）。坑记录见 [从蓝图里取证](pitfalls/WARUDO_INSPECTION.md) §7 |

**2026-09-25 清掉的三个临时节点**（摸底用完就删；旧蓝图里那个「调试台」会被同 Id 的「Ho调试日志」接替）：
`Ho Face 原始值（按线名）`（接收器的「原始值」口就够了）、
`Ho Face 角色探针`（结论已落进本文 §3–§5；**它的观察窗也一起没了** —— §7 待办 #2/#3 的判据要另找工具）、
旧的 `Ho Face 调试台` 形态（多行框 + 抓取/追加/清空/摘要收成两个口子）。

⚠️ **轮询由接收器节点驱动**：`OnUpdate` 里调 `HoFaceInputState.Poll()`（`HoFaceReceiverStatusNode.cs:42-46`）——
它不在图里，就**没有人收包**。
⚠️ 处理链节点**没有 flow 触发**：5 个输出口惰性求值、一帧只算一次（`HoFaceMiddlewareNode.cs:70-84`），
因为 Warudo 没承诺节点之间的执行顺序 —— 不赌顺序。想手动催就用节点上的「重读配置」按钮。

**目标形态**（📖 计划，尚未落地）：

```
[HoVtsTrack mod]                    [HoVtsTrackController mod]              [官方节点 ×3]
  HoVts 接收器      ──原始值/状态/调试──▶  中间层+控制器（合并成一个节点）  ──▶ 设置角色面部追踪 BlendShape 列表
                   （裸线名原样交出）       内部 = 我们的中间层配置 + 控制器         覆盖角色骨骼旋转偏移列表
                                          输出 = BS 列表 / 骨骼旋转偏移 / 根位置     覆盖角色根位置
```

* **5 = 我们的 2 个 + 官方那 3 个**。
* **`HoVtsTrack`** —— 通用接收节点：只做"读值 + 直通传参"，外加状态与调试输出。**写一次以后基本不用再动。**
* **`HoVtsTrackController`** —— 语义上同样通用，但**带着我们指定的中间层配置 + 控制器**：
  内部在影子上跑控制器、把结果反算出来，直接产出 BS 列表 / 骨骼旋转偏移 / 根位置。
* **应用端不碰 Warudo 的通用机械**（`SWITCH_*` / `SMOOTH_*` / `MERGE_*` / `EMPTY_*` / `DEFAULT_*` / `LOOK_AT`），
  只留那三个官方应用节点。
* **现状离目标差在哪**：① 还是 1 个 mod（拆不拆见下）；② "控制器"还是**数据树**（`Core/HoFaceChain.cs`）而不是
  `.controller`；③ 今天的处理链端**口径**是"与官方接收器同形"（输出口叫 `Bone Rotations`），
  目标形态那个合一节点直接输出"骨骼旋转偏移" —— 端口名与类型的最终口径**还没定**（下游那个 apply 节点吃的确实是
  offset，所以**语义**上今天已经是偏移了：单位四元数 = 不改那根骨头，见 §3.3 与 §3.4 第 3 条）。
* **为什么现在没拆成两个 mod**：Warudo **每个 mod 各自编译成一个程序集**，同名类型跨 mod 是**不同的 `Type`**，
  拆开就得复制代码 + 靠端口通信；边界应该是"**mod 的种类**"（角色 / 插件），不是"功能模块"
  （`HoFaceTrackingPlugin.cs:12-15`）。
  ❓ 拆成两个 mod **能不能成立**取决于一件事：**跨 mod 只能传 Unity 原生类型与 Warudo 自有类型**（见 §4 开头）。
  "裸线名 → 原值"（`Dictionary<string,float>` / `bool` / `Vector3` / `Quaternion[]`）正好落在这个范围内，
  所以端口通信这条路**看起来**可行 —— 但**两个 mod 的实际连线一次都没在 Warudo 里跑过**（§7 待办 #4），标❓。

### 2.1 应用节点的字段：两处必须知道的默认值

| 节点 | 关键字段（✅ 反射 + 场景里的实际值） | 官方默认 | 说明 |
|---|---|---|---|
| `SET_CHARACTER_TRACKING_BLENDSHAPES`（设置角色面部追踪 BlendShape 列表） | `Character` / `Dictionary<string,float> BlendShapes` / `ApplyToAllSkinnedMeshes` / `TargetSkinnedMesh` / `UseVRMBlendShapeProxy` / `AdditiveVRMBlendShapeClips` / `ClampAllBlendShapes` / `ClampedBlendShapes` / `UnclampedBlendShapes` | — | 键就是**角色自己的形态键名**（自动补全走角色）；同时覆盖"所有网格 / 指定网格""VRM 代理 / 非 VRM""哪些键要夹" |
| `OVERRIDE_CHARACTER_BONE_ROTATION_OFFSETS`（覆盖角色骨骼旋转偏移列表） | `Character` / `Quaternion[] BoneRotationOffsets` / `Immediate` | — | **偏移**语义：单位四元数 = 不改那根骨头 |
| `OVERRIDE_CHARACTER_ROOT_POSITION`（覆盖角色根位置） | `Character` / `Vector3 RootPosition` / `Single RootPositionWeight` / `Immediate` / `AllowFloating` | 值全 `0` / 全 `false`；**`RootPositionWeight` 默认 `0.0`** | ⚠️ **权重默认是 `0.0`，不是 1** —— 留空 = 这个节点永远不生效。官方图里它是**被接线驱动的**：`SWITCH_FLOAT.Output → RootPositionWeight`（也就是 `IsTracked`，见 §3.2） |

⚠️ **"根位置的值本身能不能表达 0 权重"：按 §2.2 的规律，几乎可以肯定"不能"。**

### 2.2 权重的分布规律（顺带解掉"根位置是绝对还是增量"）

把四个覆盖节点的字段摆一起，规律立刻出来 —— **权重只出现在"值是绝对的"那种节点上；
凡是增量的写法都有个天然的"不发表意见"的值，所以不需要权重**：

| 节点 | 值字段 | 权重 | 为什么 |
|---|---|---|---|
| 覆盖角色骨骼旋转 | `Quaternion[] BoneLocalRotations`（**绝对**） | `Single[] BoneRotationWeights` —— **逐骨头一个** | 绝对值得能说"第 7 根别动"，一个标量做不到 |
| 覆盖角色骨骼位置 | `Vector3[] BoneLocalPositions`（**绝对**） | `Single[] BonePositionWeights` —— 逐骨头 | 同上 |
| 覆盖角色骨骼旋转偏移 | `Quaternion[] BoneRotationOffsets`（**增量**） | **没有** | 单位四元数本身就是"不改" |
| 覆盖角色根位置 | `Vector3 RootPosition` | `Single RootPositionWeight` —— 一个标量 | 根只有一根，所以一个值配一个权重 |

→ **`RootPosition` 有权重、名字也不叫 `...Offset`，所以它极可能是"绝对值"**
（❓ **仍未实测**；这条是从整个 API 的权重分布规律推的，比单看签名强）。
若成立：`(0,0,0)` + 权重 1 不是"不加偏移"，而是**"断言根在原点"** ——
**能表达 mute 的只有权重，值本身表达不了**（那个权重在语义上就是**节点级的 mute**）。

实测判据（一分钟、不用构建）：`RootPosition` 设 `(0,0,0)`、权重设 `1`，给角色放一段
**带位移**的动作，看位移还在不在 —— 还在 = 增量语义（值够用、权重多余）；被钉住 = 绝对语义。

**这一条不影响面捕**：面捕本来没有根运动，所以权重填 1 + 值恒为原点在"只有面捕"的场景里
看不出差别。真要做根运动时改值即可，连线不用动。

---

## 3. 官方蓝图解剖（★ 这一节是设计的直接依据）

✅ **证据**：本机场景 `DefaultScene.json` 里那张图 `面部追踪 - iFacialMocap`，
节点端口定义、每个口的当前值、25 条数据连线 + 5 条流程连线，**全部从这个文件里解出来的**。
⚠️ 本机这份图里是 **21 个节点 = 官方的 20 个 + 我们临时塞进去的「Ho Face 原始值（按线名）」1 个**；
而且 `GET_IFACIALMOCAP_RECEIVER_DATA` 的 `Receiver` 口**当前是空的**（没指定接收器资源），
所以这张图在本机现在跑不起来 —— 解剖它的**结构**不受影响。

### 3.1 官方接收器节点的端口 —— 这就是接口

✅ 直接读自场景里那个节点实例（`dataInputs` / `dataOutputs` 的 `type` 字段）：

```
GET_IFACIALMOCAP_RECEIVER_DATA
  dataOutputs:
    IsTracked      bool
    RootPosition   UnityEngine.Vector3
    BoneRotations  UnityEngine.Quaternion[]
    BlendShapes    System.Collections.Generic.Dictionary<string, float>
    HeadPosition   UnityEngine.Vector3
  dataInputs:
    Receiver       Warudo.Plugins.iFacialMocap.Assets.iFacialMocapReceiverAsset
```

第二处独立确认：社区 mod `veasu.vtubestudio.GetVTubeStudioDataNode` 的成员正好是这五个
（`api-scan` 转储，早前取过）。

→ **五个端口，不多不少。对上它就能直接塞进官方那张图，不需要自己造图。**
（我们的处理链节点正是照这五个做的：`HoFaceMiddlewareNode.cs:195-257`。）

### 3.2 接线（✅ 逐条读自 `dataConnections` / `flowConnections`）

```
ON_UPDATE ─flow→ SET_CHARACTER_TRACKING_BLENDSHAPES ─→ OVERRIDE_CHARACTER_BONE_ROTATION_OFFSETS
            ─→ OVERRIDE_CHARACTER_ROOT_POSITION

融合形状：
  接收器.BlendShapes → SWITCH_BLENDSHAPE_LIST.IfTrue
  接收器.IsTracked   → SWITCH_BLENDSHAPE_LIST.Condition
  EMPTY_BLENDSHAPE_LIST.Output → SWITCH_BLENDSHAPE_LIST.IfFalse
  SWITCH_BLENDSHAPE_LIST.Output → SMOOTH_BLENDSHAPES.BlendShapes
  SMOOTH_BLENDSHAPES.SmoothedBlendShapes → GENERATE_HEAD_&_EYES_MOTION.BlendShapes
  GENERATE_HEAD_&_EYES_MOTION.OutputBlendShapes → SET_CHARACTER_TRACKING_BLENDSHAPES.BlendShapes

骨骼：
  接收器.BoneRotations → SWITCH_ROTATIONS.IfTrue
  DEFAULT_CHARACTER_BONE_ROTATIONS.Output → SWITCH_ROTATIONS.IfFalse
  接收器.IsTracked → SWITCH_ROTATIONS.Condition
  SWITCH_ROTATIONS.Output → MERGE_CHARACTER_BONE_ROTATIONS（Face/Head/Pelvis/LeftLeg/RightLeg 五个口接同一源）
  MERGE_CHARACTER_BONE_ROTATIONS.BoneRotations → SMOOTH_ROTATIONS.Rotations
  SMOOTH_ROTATIONS.SmoothedRotations → GENERATE_HEAD_&_EYES_MOTION.BoneRotations
  GENERATE_HEAD_&_EYES_MOTION.OutputBoneRotations → CHARACTER_LOOK_AT_TARGET.BoneRotations
  CHARACTER_LOOK_AT_TARGET.OutputBoneRotations → OVERRIDE_CHARACTER_BONE_ROTATION_OFFSETS.BoneRotationOffsets

根位置：
  接收器.RootPosition → SMOOTH_POSITION.InputPosition
  SMOOTH_POSITION.SmoothedPosition → OVERRIDE_CHARACTER_ROOT_POSITION.RootPosition
  SWITCH_FLOAT.Output → OVERRIDE_CHARACTER_ROOT_POSITION.RootPositionWeight

权重（⚠️ 这一段的两个方向**很容易记反**，值都从场景里读出来了）：
  接收器.IsTracked → SWITCH_FLOAT.Condition
  SWITCH_FLOAT.IfTrue = 1.0、IfFalse = 0.0  →  SWITCH_FLOAT.Output = IsTracked（0 或 1）
  SUBTRACT_FLOAT.A = 1.0、B ← SWITCH_FLOAT.Output  →  SUBTRACT_FLOAT.Result = 1 − IsTracked
  SUBTRACT_FLOAT.Result → GENERATE_HEAD_&_EYES_MOTION.Weight
  SWITCH_FLOAT.Output   → CHARACTER_LOOK_AT_TARGET.Weight

收拾：
  ON_DISABLE_GRAPH → RESET_CHARACTER_TRACKING_BLENDSHAPES → RESET_CHARACTER_BONES
```

→ 于是：**`角色看向目标`（LookAt）的权重就是 `IsTracked`**（有人在追 → 注视生效）；
**官方待机生成器的权重是 `1 − IsTracked`**（丢追 → 待机接管）。
（早前这条写反过，记在这里：`1 − IsTracked` 是**待机生成器**的权重，不是 LookAt 的。）

### 3.3 每个节点是什么，以及它体现了什么考虑

| 节点（✅ 真实类型见括号） | 端口 / 功能 | 体现的考虑 |
|---|---|---|
| `SWITCH_FLOAT`(`SwitchFloatNode`) / `SWITCH_BLENDSHAPE_LIST`(`SwitchBlendShapeListNode`) / **`SWITCH_ROTATIONS`**(`SwitchRotationListNode`) | `Condition` + `IfTrue` / `IfFalse`，**并且两个方向各自有 `TransitionTime` / `TransitionDelay` / `TransitionEasing`**。节点默认是"软"的：`IfTrue=0` / `IfFalse=0` / `TransitionTime=0.4s`（`InOutSine`）；官方图里把它改成了 `IfTrue=1` / `IfFalse=0` / `TransitionTime=2.0s` | **断流不是硬切，是一次带缓动和延迟的过渡。** 追踪丢失 → 换掉输入（融合形状换空列表、骨骼换默认）+ 用过渡时间淡出 |
| `SMOOTH_BLENDSHAPES`(`SmoothBlendShapeListNode : ProcessBlendShapesNode`) / `SMOOTH_ROTATIONS`(`SmoothRotationListNode`) / `SMOOTH_POSITION`(`SmoothPositionNode`) | 各自 `SmoothTime`（滑条 0–2 s；图里的值：0.2 / 0.6 / 0.6）。`ProcessBlendShapesNode` 就是"有 `BlendShapes` 字段 + `AutoCompleteBlendShapes()`"那个基类 | 平滑是**分通道**给的，不是全局一个系数 |
| `MERGE_CHARACTER_BONE_ROTATIONS`(`MergeCharacterBoneRotationListNode`) | **9 个身体部位口**：`Face` / `Head` / `Pelvis` / `LeftArm` / `RightArm` / `LeftFingers` / `RightFingers` / `LeftLeg` / `RightLeg` → 一个 `Quaternion[]`（`BoneRotations()` 输出） | **官方支持"多个追踪器各管一块身体再合并"**。面捕图只接了 5 个口；这也解释了接收器为什么要输出一个大数组 |
| `DEFAULT_CHARACTER_BONE_ROTATIONS`(`DefaultCharacterBoneRotationListNode`) | **一个字段都没有**，只有 `Quaternion[] Output()` | 它是"不追踪时用的那份旋转"。配合 offsets 语义 → **默认 = 不改动** |
| `OVERRIDE_CHARACTER_BONE_ROTATION_OFFSETS`(`OverrideCharacterBoneRotationOffsetsNode`) | `Character` + `Quaternion[] BoneRotationOffsets` + `Immediate` | **是偏移（offset），不是绝对旋转** —— 叠在角色自身动画的旋转之上 |
| `OVERRIDE_CHARACTER_BONE_ROTATIONS`(`OverrideCharacterBoneRotationsNode`) | `BoneLocalRotations` + `BoneRotationWeights` | 另一条路：**绝对局部旋转 + 每根骨骼一个权重**。字段名直接证明这一层是**局部空间** |
| `OVERRIDE_CHARACTER_BONE_POSITIONS`(`OverrideCharacterBonePositionsNode`) | `BoneLocalPositions` + `BonePositionWeights` + `SkipNonHipsBones` + `SkipEyeBones` | 位置层专门考虑过"**只给胯、别动眼**"（眼骨位置不能让外部动捕乱推） |
| `SET_CHARACTER_TRACKING_BLENDSHAPES`(`SetCharacterTrackingBlendShapesNode : OverrideCharacterBlendShapesNode`) | 见 §2.1 | 键就是**角色自己形态键名**；同时覆盖"所有网格 / 指定网格""VRM 代理 / 非 VRM""哪些键要夹" |
| `CHARACTER_LOOK_AT_TARGET`(`CharacterLookAtTargetNode`) | `BoneRotations` + `Character` + `Target` + `Enabled` + `Weight` / `HeadWeight` / `EyesWeight` + `MaximumLookAtAngle`(30–135°) + `MaximalHeadRotation`(Vector3) / `MaximalEyeRotation`(Vector2)（**度**）+ `SmoothHeadTime` / `SmoothEyesTime` | 注视是**叠加在追踪结果上的后处理**，头 / 眼分开限幅、分开平滑 |
| `EMPTY_BLENDSHAPE_LIST`(`EmptyBlendShapeListNode`) | 无输入 → 空 `Dictionary<string,float>` | 提供"空"作为一个显式常量，配合 switch |
| `SUBTRACT_FLOAT`(`FloatSubtractNode`) | `A - B` | 用来算 `1 - IsTracked` 当**待机生成器**的权重（§3.2） |
| `ON_UPDATE` / `ON_DISABLE_GRAPH` | 事件 → flow | 每帧推进；停用图时**归位**（reset）而不是留着上一帧 |
| `GENERATE_HEAD_&_EYES_MOTION`（面板上叫**生成头部待机动画**；✅ 真身是 `Warudo.Plugins.ProceduralAnimations.Nodes.GenerateIdleHeadAnimationNode : CharacterDaemonNode`，住在 **`Assembly-CSharp.dll`**） | ✅ **完整端口表**（反射 + 场景两处一致）：输入 `BlendShapes`(Dictionary) / `BoneRotations`(Quaternion[]) / `Enabled` / `AutoBlinking` / `AutoEyeMovements` / `AutoHeadMovements` / `Weight` / `BlinkBlendShapes`(string[]) / `LookWeight` / `MaxLookAngles`（嵌套 `MaxLookAnglesData`：上/下/左/右，默认 4/10/15/15） / `BlinkInterval`(Vector2, 默认 1–10) / `BlinkSpeed` / `SquintEyes`（默认闭眼程度） / `RemoveInputEyeBlendShapes`(图中 = `true`) / `HeadTilt`（嵌套 `HeadTiltData`：X/Y/Z） / `LookAroundInterval`(Vector2, 默认 8–30) / `MaxLookAroundAngle` / `Character` / `ShowCharacterDaemon`；输出 `OutputBlendShapes` / `OutputBoneRotations` | **这不只是"面捕转头眼运动"，它同时是丢追时的待机生成器**：`RemoveInputEyeBlendShapes = true` 说明它把输入的眨眼键**剔掉、换成自己合成的**（接管而非让路）。所以官方丢追时的填充**来自这个节点**，不是来自角色自己的动画 |

> ⚠️ **上一条曾经写错过两次，教训记在这里**：
> ① 早先只看**连线**就把它的输入当成 `BlendShapes` / `BoneRotations` / `Weight` 三个，于是断言"图里没有待机生成节点"。
> ② 后来又说它"属于 iFacialMocap 插件、反射不到、端口表只能靠截图" —— 也错了：
> 它是**官方 ProceduralAnimations 插件**的节点，类型在 `Assembly-CSharp.dll` 里，反射得到。
>
> **正确的做法：端口表有三个来源，缺一个都会看漏。**
>
> | 来源 | 给的是 | 会漏什么 |
> |---|---|---|
> | **反射**（`warudo-knobs` + 真机 DLL） | 编译期真值，**含继承来的口** | 无（但看不到本地化标签与默认值） |
> | 场景里节点实例的 `dataInputs` | 该实例**未被连线**的口 + **类型 + 当前值 + 默认值 + 滑条范围** | 被连线驱动的口（Warudo 不把它存进 `dataInputs`） |
> | 场景里的 `dataConnections` | **被连线驱动**的那些口 | 没被连线的口 |
>
> 实例：`SWITCH_ROTATIONS` 的 `IfTrue` / `IfFalse` 是正常 `[DataInput]` 字段（反射看得到），
> 但场景里它们**只出现在连线里**；`GENERATE_HEAD_&_EYES_MOTION` 的 `BoneRotations` 同理。
> 教训：**判断一个节点"有什么能力"必须拿到完整端口表**，否则会得出方向完全相反的结论
> （"丢追靠释放" vs "丢追靠生成"是两套完全不同的实现）。

### 3.4 三条直接结论

1. **「断流回中性」在图上，不在接收器里。** 接收器只管如实报 `IsTracked`。
   这印证了"接收器不做任何隐式处理"那条决定。
   （但要注意：图上做这件事的方式是**生成待机**，见 §3.3 最后一行 —— 不是"释放"。）
2. **应用端三个节点全在 Tracking 层**，`Character` 在各自节点上选。
   `BlendShapes` 就是个 `Dictionary<string,float>`，**键就是角色上真实的形态键** ——
   我们**不需要标准键名**。
3. **骨骼那条路是"偏移"，不是"绝对"。** 我们自己算出来的东西应该以 **offset（相对基准的增量）**
   的形式交出去；`DEFAULT = 不改` 这个语义是整套设计的基线。

---

## 4. 已确证的机制与硬边界

**先把最硬的那条摆前面**（📖 `Assets/HoWarudoModTests/docs/打包与脚本规范.md` §6，本机 mod 构建实测）：
UMod 在编译后做 API 引用审查，命中即**构建失败** ——
禁用 `System.IO.*` / `System.Reflection`（`exception.GetType().Name` 也会被拒，IL 里是 `MemberInfo.Name`）/
`UnityEditor` / P/Invoke / `UMod-ModTools`；**不支持 `.asmdef` / ScriptableObject / 已编译 DLL / 第三方 NuGet**。
→ **推论：跨 mod 只能传 Unity 原生类型与 Warudo 自有类型**（同名类型在不同 mod 里是不同 `Type`，
既不能反射对方的类型，也不能共享我们自己的类型）。这正是 §2.0 那个拆法的可行性边界。

### 4.1 mod 能加载自己包里的资产 ✅（能拿到句柄，按名字还没取到东西）

```
UMod.ModHost : MonoBehaviour
    prop public IModAssets Assets {get;}
    prop public IModAssets SharedAssets {get;}      ← 对应产物里的 sharedassets.bin
UMod.IModAssets                                       （住在 UMod-Interface.dll）
    prop  public Boolean CanLoadAssets {get;}
    prop  public Int32 AssetCount {get;}
    method public T Load<T>(String nameOrPath)
    method public T Load<T>(Int32 assetID)            ← ← 这一条很关键，见下
    method public T[] LoadWithSubAssets<T>(String nameOrPath)
    method public T Instantiate<T>(String nameOrPath)
```

✅ **运行期实测**（`Player.log` 里角色探针的「Mod 资产」一节）：

```
HasAssets=True  IsModLoaded=True
SharedAssets：CanLoadAssets=True  AssetCount=2
Assets：CanLoadAssets=True  AssetCount=2
Load("HoFaceTree") -> **取不到**
```

`Plugin.ModHost` 是 public（编进 mod 的代码 `this.Plugin.ModHost.SharedAssets` 能编译、能跑），所以：

```csharp
var assets = this.Plugin.ModHost.SharedAssets;
if (assets != null && assets.CanLoadAssets)
    shadowAnimator.runtimeAnimatorController = assets.Load<RuntimeAnimatorController>("<名字或路径>");
```

**三个坑与一条新发现：**

* **`ModAssetsBridge` 不是公开类型** —— `UMod.Bridge.ModAssetsBridge`（就是那个带
  `FindAllNames()` / `FindAllRelativeNames()` 的实现）`IsPublic=False` 且是抽象类（反射确认），
  cast 去枚举包内资产名会报 `CS0122`（本机编译实测）。
* **`IModAssets` 本身没有枚举接口** → 按**名字**加载时**拿不到清单**，只能按候选名字一个个试。
  ✅ 实测：`AssetCount=2`，但 `Load("HoFaceTree")`（猜的资产名）**取不到** —— 名字约定还没试出来。
* ❓ **新发现（未实测，但把上一条的死结打开了一半）**：`IModAssets` 有一整套 **`Int32 assetID`** 重载
  （`Load<T>(int)` / `LoadWithSubAssets<T>(int)` / `Instantiate<T>(int)`）。
  也就是说 **`AssetCount` + ID 枚举可以拿到"清单"** —— 逐个 ID 试、看 `Load` 回来的类型即可，
  不必依赖那个不公开的实现类。**没试过**，标❓。
* **运行时没有 `AnimatorController` 这个类型** —— 它是 `UnityEditor.Animations` 的编辑器独占类；
  玩家端只有 `RuntimeAnimatorController`（`Animator.runtimeAnimatorController` 收的就是它）。
  ⚠️ 探针那句"（**是 RuntimeAnimatorController**）"的分支**从来没走到过**（Load 没成功），
  所以"取回来的运行时类型就是它"目前仍是 ❓ **未实测**。

禁令边界：被点名的是 **`UMod-ModTools`**；`UMod.dll` / `UMod-Interface.dll` 不在禁令里。
离线编译检查的引用列表已经补上这两个
（`Assets/HoWarudoModTests/tools/compile-check.ps1:37-53`）。

### 4.2 角色身上没有 controller ✅

✅ 实测（`Player.log`，角色探针）：

```
本体：enabled=True  activeAndEnabled=True  avatar=humanoid  isHuman=True
controller：**null** —— 这具 Animator 上没有混合树可跑。
CloneAnimator：在（Character Avatar Clone）
```

Warudo 的角色动画走 Animancer 直接播 clip。→ **在角色本体上跑混合树这条路不存在**，
混合树只能在我们的影子 Animator 上跑（控制器从 4.1 那条路来），或者干脆做成**数据树**（Warudo 侧今天的选择）。

### 4.3 `CharacterAsset` 不交出"裸预制件"，但活实例够用 ✅

整条继承链（`CharacterAsset : FromSourceGameObjectAsset : GameObjectAsset : Asset`）没有 `Prefab`
字段、没有 public 加载入口（`CreateGameObject()` 是 `protected virtual`）—— ✅ 反射确认：
`FromSourceGameObjectAsset` 上只有 `Source` / `SourceMeta` 两个**字符串**，`GameObjectAsset` 上
`public GameObject GameObject {get;set;}` 就是那个活实例。能拿到的是活实例：
`AvatarClone` / `AvatarCloneParent` / `GameObject` / `Animator` / `CloneAnimator` / `SkinnedMeshRenderers` /
`BlendShapes` / `HumanBodyBoneToBodyTransforms` / `RootTransform` 等（反射 `CharacterAsset` 确认）。

**骨架与网格是同一个根下的两块**，复刻要复刻根 —— ✅ 实测（探针报告）：

```
GameObject（GameObjectAsset.GameObject）：Character Root
AvatarClone：Character Avatar Clone（与 GameObject 不是同一个对象）
AvatarCloneParent：Character Avatar Clone Parent
31 个渲染器里，在 AvatarClone 下面的：0 个     ← 复刻 AvatarClone 拿到 0 个形态键就是这个原因
```

✅ 复刻（`Instantiate(GameObject)`）的结果也很干净：`SkinnedMeshRenderer 31，Animator 1，Transform 1000，组件总数 1112`；
形态键 **副本 161 / 本体 161 → 对得上**；**骨骼重映射 29729/29729 根骨骼指向副本内部 → 完全重映射，副本能独立驱动**。
⚠️ 副本会跑一次 `Awake`，带弹簧骨 / 布料 / 物理的角色可能在那一下注册到全局管理器（当时那台探针只在
摸底时用；探针已删，这条留给以后真要做常驻影子实例时参考 —— 要记得关掉它们）。

### 4.4 中间层配置文件放插件沙箱 ✅（已跑通）

`Plugin.PersistentData`（`PluginPersistentDataManager`）是沙箱化文件 API。
✅ 实测路径：`Warudo_Data/StreamingAssets/Plugins/Data/<pluginId>/`，
本机已经在用：`…/Plugins/Data/hollow.hofacetracking/ho-2d-test1.hoface.json`（+ 一份 `ho-full-test.hoface.json`）。
节点上的「沙箱目录」口直接给路径，不用猜 Warudo 的目录结构。

⚠️ **`GetFiles` 不能用**：它第三个参数是 `System.IO.SearchOption`，而 UMod 构建期审查禁止引用 `System.IO.*`
（反射 `PluginPersistentDataManager` 确认签名）。只能用 `GetFileEntries(相对路径, 通配, Func<string,bool>)`。
配置按文件时间戳失效重读；运行中新丢进去的文件靠"找不到就重列一次（1 秒冷却）"才看得见。

> 两边读的是**同一份文件内容**（同一个格式、同一份真源码），不是同一个路径：
> **Warudo 侧放插件沙箱**（上面这个），**Unity 侧 `profilePath` 指工程里的那份**
> （`HoFaceDebugSettings.cs:23-25`）。改完要自己同步过去。

### 4.5 中间层配置的 JSON 读写：自己写的读写器 ✅（已完成）

**曾经的现场**：沙箱里那份配置只有 **342 字节** —— `format` / `version` / `displayName` / `notes` 四个字段在，
`inputs` / `outputs` 两个 `List<内部类>` 整个没了，而文件末尾的 `}` 是完整的（**不是截断，是序列化器跳过了这两个字段**）。
读路径同样是坏的：运行期报「配置文件里一行输出都没有」。
根因：**Unity 的 `JsonUtility` 在播放器里会静默丢掉这两个 `List<内部类>` 字段，而编辑器里是好的。**

**现在的做法**：`Runtime/FaceTracking/HoFaceProfileJson.cs` —— 自己写的读写器
（`HoFaceProfile` 退化成"格式的名字 + 入口"）。顺带多了两件事：未知字段统一跳过（向前兼容照旧）、**报错带字符位置**。

✅ **验证**：`.research/profile-json-test` 离线跑**包里的真源码**（桩件顶替 UnityEngine）→ **87/87 通过**
（本次复核重跑过），含 `写→读→写 文本完全一致（字节稳定）`、128 输入行 / 56 输出行、曲线关键点与修饰符**顺序**往返、
转义与 Unicode 往返、坏 JSON 报字符位置。
✅ **线上判据**：`Player.log` 里处理链状态行 = `ho-2d-test1.hoface.json#… · 输入行 128 / 输出行 56`。

### 4.6 规则：我们的数据一律不用 `JsonUtility`

同一类坑咬了三次（写配置 / 读配置 / **收 VTS 包**）。收包那次：VTS 载荷里 `BlendShapes` 是 `List<{k,v}>`，
用 `JsonUtility.FromJson` 解析的结果是 —— **12 个头眼分量全在、`FaceFound`/`Hotkey`/`Timestamp` 都在，
52 个形态键全丢**（运行期实测「本帧键数=15」）。包"解析成功"、字段名也对，丢的正是最要紧的东西。

> **规则（覆盖所有数据路径）：我们的数据一律不用 `JsonUtility`** —— 配置文件读写、接收端线格式，
> 全部走自写的读取器（`Runtime/FaceTracking/HoJson.cs`、`HoVtsPacket.cs`、`HoFaceProfileJson.cs`；
> mod 侧是搬过去的那几份，见 `Core/PORTED.md`）。
> 三次现场与排查入口 → [`pitfalls/FACE_TRACKING_PIPELINE.md`](pitfalls/FACE_TRACKING_PIPELINE.md) §1，本文不复述。

### 4.7 VTS 手机协议的确切形状（✅ 官方文档 + App 侧核对 + 运行期日志）

* **不是"手机主动推流"。** 手机不接受目标地址：它把数据发回**请求包的源 IP**，
  端口用请求里 `ports` 数组指定的。所以**手机上除了那个开关没有要填的东西**。
* 我们这边填：`手机 IPv4` = 手机的局域网 IP（**必须填对**，源 IP 过滤会静默丢包 ——
  所以接收器专门有个「外来来源」口把被丢的来源 IP:端口摆出来）、
  `手机端口` = `21412`（默认值，或 App 上显示的）、`本机端口` = 数据回来的落点（默认 `49985`）。
* 请求包（`HoVtsPacket.BuildRequest`，离线测试断言了原文）：
  `{"messageType":"iOSTrackingDataRequest","time":5,"sentBy":"HoFaceTracking","ports":[49985]}`
  `time` 允许 0.5–10 → **每秒续一次**；`sentBy` 是**手机上 `Connected VSF Clients:` 列表里显示的名字**。
* ✅ 运行期实测：`状态=监听 49985 ← 192.168.1.92:21412`，`本帧键` 在 `0 / 15 / 65` 之间跳
  （**65 = 有脸的帧，15 = 丢追的帧**，见 §4.6 与 `FaceFound`）。
* **线名拼写 = PascalCase**：官方 `VTSARKitBlendshape.cs` 那份枚举里 52 个名字是
  `EyeBlinkLeft` / `JawOpen` / `MouthSmileLeft` / `TongueOut`…，我们内置默认里"首字母大写"的规则
  （`Runtime/FaceTracking/HoFaceMiddleware.cs:190-192`）**逐个对得上**（离线测试覆盖了 52 个）。
  ⚠️ 规范名（`eyeBlinkLeft`）与线名（`EyeBlinkLeft`）是两套拼写，别混。
* **出处**：协议来自官方仓库 <https://github.com/DenchiSoft/VTubeStudioBlendshapeUDPReceiverTest>
  （README 原话 "Apps like VSeeFace and VBridger use this."；载荷定义 `VTubeStudioRawTrackingData.cs`）——
  **是官方给的，不是逆向出来的**（原记录见 `Core/HoVtsIphoneReceiver.cs:8-20`）；
  官方协议页与线协议全文另见 [参数标准表](PARAMETER_STANDARDS.md) §6。
* 本机端口：官方 iFacialMocap 接收器资源占 `49983`（要用那个得先关掉它）、我们占 `49985`（**早前实测**；
  ⚠️ 本次复核**没能再验** —— 本机场景里那个接收器资源是空的，见 §3 开头）。

---

## 5. 骨骼数组的空间与基准：**一半实测、一半仍未实测**

* ✅ **消费端要局部**：`BoneLocalRotations` / `BoneLocalPositions`（字段名本身就是证据，见 §3.3）。
* ✅ **骨骼那条路是偏移语义**（`BoneRotationOffsets`，`DEFAULT = 不改`）。
* ✅ **已实测（`Player.log` 探针「空间」一节）**：
  * 四个数组长度都是 **55** = `(int)HumanBodyBones.LastBone`，**按 `HumanBodyBones` 枚举值索引** ——
    `Hips=0` / `Head=10` / `LeftHand=17` 三个下标上，数组元素与那根骨头的属性**逐位相等**；
  * `InitialBoneLocalPositions[i]` == `transform.localPosition` ✅、`InitialBoneWorldPositions[i]` == `transform.position` ✅
    → **这两个是"局部位置"与"世界位置"**，这一点被区分开了（同一根骨头的这两个数本来就不同）。
* ❓ **仍未实测的两条**：
  1. **`EndOfLateUpdateBoneRotations` 是局部还是世界旋转** —— 那一次角色**全身旋转都是 identity**，
     局部/世界两个数组与两个属性全都相等，区分不开。
  2. **这些数组是"当前姿态"还是"加载时抓的初始基准"** —— 那一次角色**静止**，当前姿态与初始姿态重合，
     同样区分不开。**所以"基准 = `InitialBoneLocalPositions`"这句目前只是名字给的暗示，不是实测。**
* **判据（得先加个临时观察窗）**：让角色**带上动画**（旋转非 identity、根有位移），再打一次「空间」那组读数
  （原来那台角色探针**已删**，所以这段要先临时加回去、或者在做 `HoVtsTrackController` 时顺手量）——
  同一根骨头的 `localPosition` / `position` / `localRotation` / `rotation` 与四个数组同下标元素逐个比：
  跟哪个相等，那个数组就是那个空间；**都不等**说明它不是"当前姿态"而是别的基准。
  这正是我们算偏移要对齐的基准（对齐错了，写进去的旋转会整体歪一个常量）。

---

## 6. 明确不做 / 搁置

* **搁置**：注册 `CharacterTrackingTemplate` 让官方"面捕"下拉项一键生成我们的图。
  机制是现成的（`Apply(CharacterAsset) → (List<Asset>, List<Graph>)`），但**先不碰自动生成**，
  先把节点本身做对。
* **不做** tracker asset（`GenericTrackerAsset` 子类）：那条路会在角色上建一份追踪器资源，
  等于把中间层配置变**每角色一份**，与"中间层是设备 / 控制器级"冲突。
* **不做**接收器里的改名 / 量纲 / 断流回中性：前两样在中间层配置里，最后一样在图上（§3.2）。
* **今天不做**（但仍挂在待办 #2 上）：Warudo 侧的"影子 Animator 加载 `.controller`"。插件 mod 不能读盘
  （无 `System.IO`），而 Unity 播放器**无法从文件加载 `AnimatorController`**（只有 AssetBundle 能）——
  唯一可能的路就是 §4.1 那条 `SharedAssets`。**在它被验证之前**，Warudo 侧的混合树是**数据树**，
  由 `Core/HoFaceChain.cs` 求值。（Unity 侧不受这条影响：那里本来就有真正的 `.controller` 和影子 Animator。）
* **不依赖**官方那套"一个 mod 带一堆资源"的打包方式；我们只带**自己的控制器**。
* 已经删掉的东西见 §1.1，别再把它们写回来。

---

## 7. 待办

| # | 事项 | 判据 | 状态 |
|---|---|---|---|
| 1 | 修 profile 读写（换成自写 JSON） | 沙箱里的 profile 读出来是 128 输入行 / 56 输出行 | ✅ **已完成**（§4.5；离线 87/87，`Player.log` 已见 128/56） |
| 2 | mod 里放一个 `.controller`，验证 `SharedAssets.AssetCount` 变正、`Load` 取得回 | 当年那台探针的「Mod 资产」一节给过 `AssetCount=2` ✅，但 `Load("HoFaceTree")` **取不到** —— 先按 **assetID 0..AssetCount-1** 逐个试（§4.1 那条新发现），再回过头定名字约定。⚠️ **探针已删，观察窗要临时加** | ❓ **未实测** |
| 3 | 骨骼数组的空间与偏移基准 | 一半已量出：**局部/世界**与**按 `HumanBodyBones` 索引** ✅（§5）。**基准那一半仍未测** —— 要带非 identity 旋转的角色再量一次（观察窗同上） | 🔶 **半条** |
| 4 | 处理链节点替换掉官方接收器节点后行为不变 | 图上换掉接收器，面部照常动。**一次都没在 Warudo 里跑过** | ❓ **未实测** |
| 5 | 影子 Animator：`AddComponent<Animator>()` + 控制器 + 反算 | Unity 侧 ✅ 已落地（`HoFaceAnimationSession.cs:119-127`，影子台 + 只写拥有的键）。**Warudo 侧不打算走这条路**（§6），改数据树 | ✅ Unity 侧完成 |
| 6 | Unity 侧调试面板：影子算 + 纯写入角色预制件，角色不挂任何组件 | ✅ 已落地（全局面板 + `HoFaceDebugHost` 宿主；角色上零组件） | ✅ **已完成** |
| 7 | 清掉 3 个临时节点 + 收缩 `NodeTypes` | 面板上只剩接收器 + 处理链 + 调试日志 | ✅ **2026-09-25 完成**（§2.0） |
| 8 | 按目标形态拆成 `HoVtsTrack` / `HoVtsTrackController` 两个 mod | 两个 `.warudo` 各自能加载、端口连起来能跑 | ❓ 待做 |

---

## 8. 测试怎么跑

| 测什么 | 怎么跑 | 现在的结果 |
|---|---|---|
| 中间层配置的 JSON 读写 + VTS 收包解析 | `dotnet run --project .research/profile-json-test` | **87/87 通过**（本次复核重跑） |
| 表达式求值器对 VBridger 的覆盖 | `dotnet run --project .research/expression-coverage` | **19/19 通过**（本次复核重跑） |
| mod 脚本能不能对着真机 DLL 编译 | `Assets/HoWarudoModTests/tools/compile-check.ps1`（**mod 工程里**，`-ModsRoots Mods,Mods-Ho`） | 全绿（引用表含 `UMod.dll` / `UMod-Interface.dll`） |
| 官方节点的类型 / 端口 / 字段 | `dotnet run --project .research/warudo-knobs -- Warudo.Plugins.Core.dll <类型名>`（真机 DLL 在 `D:\steam\...\Warudo_Data\Managed`；`--attrs` 连特性一起打） | 本文 §2 / §3 的字段表就是这么来的 |
| Warudo 运行期行为 | 读 `AppData\LocalLow\HakuyaLabs\Warudo\Player.log`（会话日志另在 `Logs\WarudoLog-<启动时间>.log.gz`） | — |

> **`Player.log` 这条很重要**：Warudo 没有界面控制台，但我们的 `Debug.Log` 会落到那儿，
> 所以验证运行期行为不用截图，直接读文件就行（想要屏上能复制的文本，用通用的「Ho调试日志」节点 ——
> 上游接进去它就显示，按按钮整段复制）。
> 另外 `.research/` 下的离线测试跑的是**包里的真源码**（用桩件顶替 UnityEngine），
> 所以"包里的代码到底对不对"不需要等 Warudo 构建就能验。
