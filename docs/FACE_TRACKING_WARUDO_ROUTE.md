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
  指定了配置文件就**只认它的两类行**，**没有内置默认兜底**（这是刻意的，不是 bug：
  `docs/pitfalls/FACE_TRACKING_PIPELINE.md` §3）。
  ⚠️ **Warudo 侧口径一致**：mod 的「HoFace参数处理」节点上「配置文件」留空 = **这一层不做事**（不输出任何参数），
  **没有内置默认**（见 §2.0 / §2.0.1）—— 2026-09-25 起两边统一为"空 = 空表"。
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

## 2. Warudo 侧：最终连线（**我们的 3 + 官方 3 = 6 个节点**；走 VB 则 5 个；两个 mod）

### 2.0 先把"今天真实存在什么"与"目标形态"分开

**今天真实存在什么**（✅ mod 源码 + 运行期日志）：

| 项 | 今天 | 依据 |
|---|---|---|
| mod 个数 | **1 个**：`[PluginType] Id = hollow.hofacetracking`，Name `Ho Face Tracking`，v`0.2.0` | `HoFaceTrackingPlugin.cs:32-46` |
| 节点个数 | **4 个**（`NodeTypes` 列全）：接收器 / `HoFace参数处理` / `HoFace控制求解` / 调试日志（2026-09-25 拆完，见 §2.0.1） | `HoFaceTrackingPlugin.cs:38-44` |
| 沙箱目录名 | `…/StreamingAssets/Plugins/Data/hollow.hofacetracking/`（= pluginId） | `Player.log`：`[Ho 面捕] 中间层配置目录：…（现有 2 份配置）` |

| 节点（面板标题） | 状态 | 输出的口（2026-09-25 收口 + 拆分后） |
|---|---|---|
| `HoFaceVTS接收器`（旧标题 `Ho Face 接收器（VTS 手机）`，2026-09-25 改名） | **正式** | **3 个**（**数据口在前、`状态` 在最下**）：`原始值`(10)（字典，列表语义，喂参数处理）、`新鲜`(20)（布尔，喂参数处理「输入新鲜」——这是信号不是给人看的）、`状态`(30)（一行文本：来源 / 状态说明 / 本帧键 / 帧·坏帧 / 距上帧；手机模式外加**只在真丢包时**出现的来源提示，本机模式改报客户端数与注入次数）。<br>砍掉的六个：`运行中` `本帧键数` `距上帧秒` `累计帧/坏帧` `外来来源` `本帧原始值` —— 都只是"给人看一眼"，不驱动任何节点，`本帧原始值` 还是 `原始值` 的文本版（想看就把 `原始值` 接「Ho调试日志」，那边摊成 `线名 = 值`）。<br>输入侧另有 **VTS 服务端模式**（勾选框 + `API 端口`），见 §2.0.1 第三条来源。 |
| `HoFace参数处理` | **正式** | **3 个**：`参数`（字典，列表语义 —— 输出行的结果，**键已去掉 `ARKit/` 前缀**）+ `有脸`（布尔）+ `状态`（四行：配置行 · 问题 · 沙箱路径 · 沙箱里现成的配置）。<br>这就是两层之间**唯一的接口**；`数值预览` 那份长文本按「重读配置」按钮写进 `Player.log`（摊开的就是出口那份参数）。 |
| `HoFace控制求解` | **正式** | **6 个**：与官方接收器**同形的 5 个**（`Is Tracked` / `BlendShapes` 字典 / `Head Position` / `Root Position` / `Bone Rotations` 数组）+ 一个 `状态`（多行：参数几个键 · 形状几个 · 有脸 · 头姿；+ `控制器：…`）。**零配置**（唯一那个"配置"是必填的 `控制器` bundle 选择 —— 它是求值场所，不是映射）；输入 = `参数`（字典）+ `有脸`（布尔，**默认 true**）+ `控制器`（**必填**，沙箱 `*.bundle` 下拉，见下面的 §2.0.2）。<br>⚠️ **没有可用的控制器就不吐任何输出**（5 个口全中性，`状态` 里点名原因）—— 见 §2.0.3。<br>⚠️ 它的 `NodeType.Id` **沿用旧「Ho Face 处理链」那个** `7c3a91d6-…`，所以升级时指官方三个节点的 5 根线不会断。 |
| `Ho调试日志` | **正式（通用件，跟面捕无关）** | 一个入口 + 一块**只读**显示 + 一个复制按钮（**没有任何输出口**）：`[DataInput] object 写入`（什么类型都能接）+ `[Markdown] [Transient] 日志`（**只读渲染、选不中**）+ `[Trigger(30)] 复制`（写 `GUIUtility.systemCopyBuffer`）。**为什么显示不是"能选中的多行框"**：值在动时框每帧重画、**选区被冲掉**（用户实测：Ctrl+A 还没复制就没了），所以复制只能交给按钮；`[Markdown]` 这一行是**照抄官方「查看值」**（`--attrs`：`[Markdown(13, False, False)] public String Text`）—— 控件由特性决定，照抄特性即复用同一控件（`InspectValueNode` 本身 public 非 sealed、`OnUpdate` virtual，继承也行，但它靠"字段被推"喂值，对我们不灵还是得 override）。**按钮用 `[Trigger]` 而不是 `[FlowInput]`**：官方节点的按钮全是 `[Trigger(order)]`（`CommentNode.Edit/Done`、`SetAssetPositionNode.AlignTargetWithAsset`…），它**不占口**；`[FlowInput]` 也能点，但会多一个 flow 出口 socket（第一版就是那么写的）。⚠️ 查官方用法要写 `--find-attr TriggerAttribute`（带后缀），写 `Trigger` 会静默返回空。**两条必须照抄**（实测）：① 写显示字段要「字段赋值 **+ `BroadcastDataInput`**」—— 只 `SetDataInput` 时端口有新值而界面**不重画**；② 输入口用 `object`（用 `string` 的话非字符串上游接不进来）；③ 上游**直接接在「日志」那一行上也可以** —— 节点会用 `Graph.GetInputDataConnections` 探到、然后不再覆盖它；④ **值不等推**：顺着连线取 `OutputNode` + `OutputPort`，调口上的求值器 **`DataOutputPort.ComputedValue`（public `Func<Object>`，非反射）**，端口/字段只作兜底 —— 实测"线接对了、口也对，字段就是不进值"，而且**不是每帧读**（10 Hz：直读=替上游求值一次，见 §8）（⚠️ 这步**不能**写成 `MethodInfo.Invoke`/`GetType().Name`：UMod 安全校验禁 `System.Reflection`，本地 lint 已能拦，见 [打包与工具链](pitfalls/BUILD_AND_TOOLING.md) §4.1）；⑤ 断流**不清空**，保持最后一次内容方便复制；⑥ **显示认几类值**（`Describe`）：字符串原样、名→值的表（排序摊平）、**数组/列表逐项**（`[i] = (x, y, z, w)`）、`Vector3`/`Quaternion` 用 F3 —— ⚠️ 数组这条修过：`object` 口拿到 `Bone Rotations`（`Quaternion[]`）时只靠 `ToString()` 屏上只有 `UnityEngine.Quaternion[]` 一行，而官方「检查值」把数组序列化成 JSON，所以"官方的能出值"，差的不是口、是显示。**"看着接了却没值"它能自己定性**：每 0.5 秒（只在还没拿到值时）把「每个输入口接了什么」写进 `Player.log`，孤儿线的判据是 `DataConnection.InputPort == null`。坑记录见 [从蓝图里取证](pitfalls/WARUDO_INSPECTION.md) §7–§8 |

**📖 §2.0.2 控制器模式（**必填**，2026-09-25 加 / 当天改成必填）**：`HoFace控制求解` 有一个**必填**输入 —— 一个
**AssetBundle**（沙箱里的文件，节点上是下拉列表），里面装着「控制器 + 它绑定的 rig 预制体」；
在隐藏影子上跑真控制器、把结果采出来（形状 + 骨骼偏移）。两条 Unity 硬约束：**`.controller` 文件运行时读不了**
（编辑器格式，`UnityEditor.Animations` 不在播放器里），**代理造不出来**（运行时无法枚举 `AnimationClip` 的绑定 ——
`AnimationUtility` 是编辑器专属 —— 所以 bundle 必须带控制器原配的那套层级）。
**路径口径与中间层配置同一套**：插件沙箱（`PluginPersistentDataManager`）里的文件名 + `ReadFileBytes`
+ `AssetBundle.LoadFromMemory`（本地 lint 与真机都放行）；列目录只能用 `GetFileEntries`
（`GetFiles`/`GetDirectories` 的签名带 `System.IO.SearchOption`，一碰就构建失败）。
节点上是 `[AutoComplete]` 下拉列表（`AutoCompleteList` / `AutoCompleteEntry{label,value}`）。
🚨 **`AutoComplete*` 方法必须是 `async UniTask<AutoCompleteList>`**：写成同步返回 `AutoCompleteList`
会让**整个节点注册失败、面板上消失**，而**本地编译全绿**（运行期注册器的检查）。2026-09-25 实测原话：
`Exception: Method HoFaceParameterNode::AutoCompleteProfile does not return UniTask`1` →
`Could not register node type …`（另一个节点同一条）。查这类 API 时**别只看返回类型名** ——
官方那些"返回 `AutoCompleteList`"的样本是**字段**，方法一律是 `UniTask<AutoCompleteList>`。
代码在 `Core/HoFaceController.cs`；✅ **运行期全部实测**（2026-09-25）：bundle 读得了、隐藏影子上的 `Animator` 照常跑 ——
参数写进去、混合树解算、`GetBlendShapeWeight` 采回来整条通了
（实测 `写入 jawOpen 0.186 / mouthSmileLeft 0.096 → 采到 0.2673 / 0.2194`，两个形状都跟着输入动）。
**审查那条**：那次构建 `Illegal Assembly Reference = '0'`，唯一被点名的是 `System.IO`（`Path.GetFileName`，已改掉），
也就是说 `UnityEngine.AssetBundle` **没被安全校验拦**。❓ 仍未验：真控制器（别人的 VRM 控制器）、骨骼那条（要 Humanoid Avatar）。
细节见 mod `README.md` §1.1.2 / §1.6 / §1.7 与 [打包与工具链](pitfalls/BUILD_AND_TOOLING.md) §4.2 / §4.3。

**📖 §2.0.3 控制器是唯一的求值路径（2026-09-25 定，当天落地）**：原来"没控制器就直接把参数装配成输出"
那条退路**砍了** —— 它等于把量纲/曲线/名字的锅甩给下游，而且**不报错**（画面看着像在工作，实际全错）。
现在 `控制器` 留空或载入失败 ⇒ 5 个输出口**全中性**（`BlendShapes` 空字典 / 位置零 / 骨骼 identity /
`Is Tracked` 假），`状态` 里那句 `⚠ 没有可用的控制器…` 就是"为什么不吐"的说明书。
`Is Tracked` 必须跟着假：否则官方那条链会拿"全中性"当"追到了"去应用，头/根回零 = 角色突然弹回原姿势。
⚠️ 结果：**`Core/HoFaceSolver.cs` 现在只管头/根位置**（参数里的保留名），它的形状/骨骼那两份输出没有消费者。
（"VB 自己算好直接喂"不构成保留退路的理由：那只是跳过中间层，依旧要喂给本节点，所以依旧要控制器。）

⚠️ **`状态` 在控制器模式下会多报一行 `写入 <参数名> <值>`**（2026-09-25 补，最多 6 个），
载入时还会各写一行自检/采样日志到 `Player.log`：控制器模式的 `BlendShapes` **只来自代理网格**，
所以"某个形状恒 0"有三个独立原因 —— ①参数没写进 Animator ②控制器没把那格推到网格上
③网格上根本没有那个形状名（`GetBlendShapeWeight` 按名字取，Unity 不报错、那格永远 0）。
只报"对上参数 N 个"（个数）时分不清（现场就是这么卡的：`mouthSmileLeft` 参数层 0.946、控制器里恒 0）。
细节与判据见 mod `README.md` §1.1.2。

⚠️ **`写入` ≠ `采到` 不是错**：采到的是"控制器那条树这一帧的输出"，真实控制器有自己的混合/曲线；
要判断的是它**跟着输入动没动**。

⚠️ **换 bundle / 换代码之后必须重新部署**（这次卡最久的不是代码，是部署，见 mod `README.md` §1.7）：
节点上是**沙箱 `*.bundle` 的下拉列表**（`[AutoComplete]`），选中的是**文件名**
（`ReadFileBytes` + `AssetBundle.LoadFromMemory`）—— 不再手填绝对路径。
⚠️ **落点由你负责**：打包在包内 FastBuild 的 **HoFT 页**，那一页**输出目录由你选**（通常选 Warudo 的插件沙箱），
所以"打进沙箱"不是自动的 —— **打完自己确认它落在沙箱里**（这一条正是 §1.7 那次放错落点的教训）。
`.warudo` 仍是打包产物：改了 `.cs` 不重打包就是旧 DLL；而且 `Prepare` 对**同名**文件直接返回
⇒ **换了文件也必须按 `重读控制器`**。一眼判据：自检行里的 `state=<哈希>` 与 `clip` 名字变没变。

**2026-09-25 清掉的三个临时节点**（摸底用完就删；旧蓝图里那个「调试台」会被同 Id 的「Ho调试日志」接替）：
`Ho Face 原始值（按线名）`（接收器的「原始值」口就够了）、
`Ho Face 角色探针`（结论已落进本文 §3–§5；**它的观察窗也一起没了** —— §7 待办 #2/#3 的判据要另找工具）、
旧的 `Ho Face 调试台` 形态（多行框 + 抓取/追加/清空/摘要收成两个口子）。

⚠️ **轮询由接收器节点驱动**：`OnUpdate` 里调 `HoFaceInputState.Poll()`（`HoFaceReceiverStatusNode.cs:42-46`）——
它不在图里，就**没有人收包**。
⚠️ **两个节点都没有 flow 触发**：输出口惰性求值、一帧只算一次
（`HoFaceParameterNode.cs` 与 `HoFaceSolverNode.cs` 的 `Ensure()` 都用 `Time.frameCount` 兜），
因为 Warudo 没承诺节点之间的执行顺序 —— 不赌顺序。想手动催就用节点上的「重读配置」/「重读控制器」按钮。
⚠️ 口径按拆分后的两个节点算：参数处理 **3 个**输出口（`参数`/`有脸`/`状态`）、
控制求解 **6 个**（官方同形的 5 个 + `状态`）。

### 2.0.1 ✅ 已完成：「Ho Face 处理链」拆成两个节点（2026-09-25 定 → 当天落地）

**拆点不是新架构 —— 它已经在代码里了。** `HoFaceChain.Evaluate` 就是三层，要拆的那条缝正好在中间
（✅ 落地后：`EvaluateInputs` / `EvaluateOutputs` 留在 `Core/HoFaceChain.cs`，`Assemble` 搬去 `Core/HoFaceSolver.cs`；
参数层出口多一份 `Parameters` 字典，键已去 `ARKit/` 前缀）：

```csharp
public void Evaluate(Dictionary<string, float> rawValues, float deltaTime, double now)
{
    current = rawValues;
    EvaluateInputs(rawValues, deltaTime, now);   // 输入行：规范名 = 曲线(表达式(裸线名…))   → 参数处理
    EvaluateOutputs(deltaTime, now);             // 输出行：参数名 = 曲线(表达式(规范名…))  → 参数处理
    Assemble(rawValues);                         // 装配成 5 个口                          → 控制求解
}
```

（`Core/HoFaceChain.cs:220-227`；`Assemble` 在 `:276-298` —— `ARKit/` 去前缀进 `BlendShapes`，
9 个保留名进欧拉角 / 头位 / 根位，骨骼数组只写 `Head`。）

| 节点（面板标题） | 拿什么 | 吐什么 | 配置 |
|---|---|---|---|
| **`HoFace参数处理`** | `原始值`（裸线名字典）+ `新鲜` + `配置文件` | **`参数`**（dict）+ **`有脸`**（bool） | **有**：`*.hoface.json`，输入行 + 输出行的曲线 / 修饰符。⚠️ **「配置文件」留空 = 这一层不做事**（不输出任何参数，**没有内置默认**） |
| **`HoFace控制求解`** | **`参数`**（dict）+ **`有脸`**（bool） | 官方同形 5 个：`Is Tracked` / `BlendShapes` / `Head Position` / `Root Position` / `Bone Rotations`（外加一个 `状态`） | **零配置**（红线，见第 2 条） |

**名字的含义**：`HoFace控制求解` 的"求解"是**从参数反求动画输出**（骨骼旋转偏移 / 头位 / 根位 / 融合形状），
不是 IK 那个意思。名字就这么定。

**两层之间唯一的接口 =「参数处理输出行那份字典」**，词表口径**对齐官方 `BlendShapes` 的命名**
（2026-09-25 修正过一次：不要带 `ARKit/` 前缀）：

| 键 | 值 | 说明 |
|---|---|---|
| **裸规范名**（`JawOpen` / `EyeBlinkLeft` …52 个） | float | 就是官方 `BlendShapes` 字典的命名 —— 参数处理出口时**去掉 `ARKit/` 前缀**（`Assemble` 本来就是这么去的，`Core/HoFaceChain.cs:278-282`） |
| `Head/RotX|Y|Z` | 度，X→Y→Z | 保留名，`Quaternion.Euler(x, y, z)` |
| `Head/PosX|Y|Z` / `Root/PosX|Y|Z` | 米 | 保留名（相对角色根 / 根位置） |

（`Core/HoFaceChain.cs:46-76`。profile 里**照旧写** `ARKit/<规范名>` —— 那是配置层的写法，
前缀只在参数处理内部存在，出口不带。）

**四条先定死的**（都是这次讨论定下来的）：

1. **`有脸` / `IsTracked` 由上游算，求解不算。** 今天的判据是"新鲜 **且** `FaceFound ≠ 0`，
   而 `FaceFound` 是**裸线名**（判据在 `Nodes/HoFaceParameterNode.cs` 的 `HasFace()`）—— 那是**协议知识**，
   求解只拿到规范名、根本看不到。所以 `有脸` 是**输入口**：参数处理按 VTS / iFacialMocap 的规矩算它，
   接收器的 VTS 服务端模式则直接拿 VTS 注入请求里的 `faceFound`（见下）。顺带省一根线
   （今天的「新鲜」只喂 `IsTracked`，折进这一个 bool 就够）。
2. **控制求解保持"零配置"。** 一旦往它里面塞平滑 / 曲线，"别的源跳过参数处理"这条唯一的卖点就没了。
3. **不做"接错层自证"。** 两层都是 `Dictionary<string,float>`，接错是用户自己的事 ——
   不额外加判据、不往 `状态` 里加提示（2026-09-25 明确否掉）。
4. **缺键 = 中性**：契约里没有的键，求解按 0 / identity 处理（例如 VB 那条路不会给
   `Head/RotX`，那求解的头姿就是 identity，头/根由 VB 自己那边的骨骼输出承担）。这条今天已经是行为
   （`Reserved()` 缺行返回 0，`Core/HoFaceChain.cs:309-314`），只是现在要**写成承诺**。

**为什么值得拆**：

* **允许用户不接中间层**：VB 那一路几乎与我们的中间层配置平行，用户直接喂求解；
* 与 Unity 侧两份文档**一对一**：`FACE_TRACKING_MIDDLE_LAYER.md` ↔ 参数处理、
  `FACE_TRACKING_CONTROLLER_STRUCTURE.md` ↔ 控制求解 —— 一份文档一个节点；
* 第三个输入源只需要新增"源 → 参数"，**不碰求解**（给它一份带 iFacialMocap 输入行的配置文件即可 ——
  参数处理**自己不带内置输入行**：`配置文件` 留空就什么都不输出）。

**代价（要认的）**：中间那份字典从"内部实现"变成**公开接口**（要冻结、写进文档 —— 好在就是输出行词汇，成本低）；
图上多一个节点、多两根线；两个节点各有一份帧护栏与诊断。

**✅ 拆的时候是这么少接一次线的**：老「Ho Face 处理链」的 `NodeType.Id`（`7c3a91d6-4f2b-48e7-9a15-63d8f0b2c47e`）
**给了控制求解** —— 连到官方三个应用节点的那 5 根线原样保住；参数处理拿了新 Id（`a41d0c86-…`），
要重接的只有接收器过来的那几根（3 根 < 5 根）。背景：Id 或口名一变，老连线就是孤儿线
（[从蓝图里取证](pitfalls/WARUDO_INSPECTION.md) §7）。

**✅ 第三条来源已落地：接收器的「VTS 服务端模式」（2026-09-25 写进 mod，未在 Warudo 里跑过）**

目标：用户**继续用 VB 原来的 VTS 输出模式**（不用为自己的用途换模式），我们把那份数据接过来。
**关键事实：这不是"同一个 socket 上多收一种包"，两者方向是反的**：

| 路径 | 谁主动 | 传输 |
|---|---|---|
| 今天收的（手机） | **我们**发 `iOSTrackingDataRequest`，手机把数据发回我们 | UDP |
| VB 的 VTS 模式 | **VB 当客户端**，连一个 VTS **服务端** | WebSocket（默认 `ws://localhost:8001`）+ 插件握手 + `InjectParameterDataRequest`（`.research/vts-creator-workflow/vts-api.md:124/1379`） |

所以加的是"接收器里的另一个模式"，实现的是 **VTS 公开 API 的服务端那一侧**（三条）：

① UDP `47779` 上每 2 秒发一份 `VTubeStudioAPIStateBroadcast`（VTS 就是这么广播的，**unsolicited**、不是请求应答 ——
所以我们只要也广播一份，VB 的客户端列表里就会出现我们；`vts-api.md:183-204`）；
② WebSocket 服务端 + 插件握手（`AuthenticationTokenRequest` → 我们直接给 token；`AuthenticationRequest` → 回 authenticated）；
③ 解析 `InjectParameterDataRequest`：`data.parameterValues[] = {id, value}` → 一份字典、`data.faceFound` → **有脸**
（VTS 自己就给了这个字段），并且**对任何参数都回成功、不报错**（真 VTS 对不存在的参数会报错，我们照收）。

**代码落在哪**：`Core/HoVtsApiServer.cs`（广播 + WebSocket 服务端 + 握手 + 收注入）+
`Core/HoVtsApiPacket.cs`（报文解析/应答，纯静态）；节点上多一个勾选框与一个端口，**没有新节点**。
细节与取舍（无线程、端口从 `8002` 起、参数名照收、`faceFound` 直接用）见 mod `README.md` §1.1.1。

**两条实现上的硬约束**（都是踩出来/查出来的）：

* **握手要的 SHA-1 只能手写**：UMod 安全校验**禁 `System.Security.Cryptography`**
  （本机实测：拿 `Trivial.CodeSecurity` 的默认规则集跑"只用 `SHA1.Create()`"的探针 → Illegal namespace = 1，
  同条件控制组 = 0）。手写版用 RFC 6455 官方向量自证过（`dGhlIHNhbXBsZSBub25jZQ==` → `s3pPLMBiTxaQ9kYGzzhZRbK+xOo=`）。
  ⚠️ 顺带一条：**默认规则集比 UMod 实际用的严**（拿它跑我们今天在跑的那个 mod 会报 55 条，
  而真机构建是过的），所以它**不能**当"本地验证器"用，只能当"探针问某个命名空间让不让用"。
* **参数名不是 ARKit**：VB 的 VTS 兼容预设注入的是 **VTS 自己那套参数名**（`MouthOpen` / `EyeOpenLeft`…），
  所以这份数据照旧要过**参数处理的输入行**改名 —— 别以为接上就与规范名对齐了（2026-09-25 更正）。

**❓ 唯一的未验证项**：VB 的客户端列表**认不认一个"自称 VTS"的服务端**（代码该做的都做了：广播字段、
握手、token、应答形态全照官方文档；但没在真机上让 VB 连一次）。**用户明确说不先验、直接写**，
所以这一条留着 —— 第一次真机跑的时候看 VB 列表里有没有 `Ho Face Tracking (Warudo)`。

**⚠️ VMC 那条不做**：Warudo 自带 VMC（`GET_VMC_RECEIVER_DATA` 节点，输出 `IsTracked` + `BlendShapes` 字典，
形状正好对齐），技术上最省 —— **但它要用户把 VB 切到 VMC 模式**，等于把成本转嫁给用户、还可能弄断他原来的 VTS 用途，
所以 2026-09-25 明确否掉（只作为假 VTS 走不通时的后备）。

**目标形态**（📖 计划，尚未落地）：

```
[HoVtsTrack mod]                        [HoVtsTrackController mod]                   [官方节点 ×3]
  HoVts 接收器  ──原始值/新鲜/状态──▶  HoFace参数处理 ──参数/有脸──▶ HoFace控制求解 ──▶ 设置角色面部追踪 BlendShape 列表
              （裸线名原样交出）        （中间层配置：裸线名→规范名，    （零配置：从参数反求         覆盖角色骨骼旋转偏移列表
                                        曲线/修饰符）                    动画输出）                  覆盖角色根位置

  〔同一个接收器的另一个模式，✅ 已实现〕VTS 服务端模式：VB 的 VTS 输出 ──参数/有脸──▶ HoFace控制求解
  〔别的源〕官方面捕源（iFacialMocap 等）→ 参数处理（须先给它配置文件；留空 = 这层不做事）──▶ 同上
```

* **6 = 我们的 3 个 + 官方那 3 个**（`Ho调试日志` 是可选调试件，不算在内）。
  **走 VB 的路线也是 5 个**：`接收器（用 VTS 服务端模式）+ HoFace控制求解 + 官方 3`（**不加节点**）。
* **✅ 2026-09-25 的拆分已落地：`Ho Face 处理链` 已拆成 `HoFace参数处理` + `HoFace控制求解`，老节点不再存在。**
  理由与做法见上面 §2.0.1（余下的"两个 mod"是另一件事，见最后一条）。
* **`HoVtsTrack`** —— 通用接收节点：只做"读值 + 直通传参"，外加状态与调试输出。**写一次以后基本不用再动。**
* **`HoVtsTrackController`** —— 语义上同样通用，但**带着我们指定的中间层配置 + 控制器**：
  内部在影子上跑控制器、把结果反算出来，直接产出 BS 列表 / 骨骼旋转偏移 / 根位置。
* **应用端不碰 Warudo 的通用机械**（`SWITCH_*` / `SMOOTH_*` / `MERGE_*` / `EMPTY_*` / `DEFAULT_*` / `LOOK_AT`），
  只留那三个官方应用节点。
* **现状离目标差在哪**：① 还是 1 个 mod（拆不拆见下）；② ~~"控制器"还是**数据树**~~ →
  **已改成真控制器**（沙箱 bundle + 影子 Animator，见 §2.0.2；数据树只剩参数层与头/根位置装配）；③ 求解端的**口径**是"与官方接收器同形"（输出口叫 `Bone Rotations`），
  目标形态那边直接叫"骨骼旋转偏移" —— 端口名与类型的最终口径**还没定**（下游那个 apply 节点吃的确实是
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
（我们的「HoFace控制求解」正是照这五个做的：`Nodes/HoFaceSolverNode.cs`。）

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
`UnityEditor` / P/Invoke / `UMod-ModTools`；**不支持 `.asmdef` / 已编译 DLL / 第三方 NuGet**。
⚠️ **"不支持 ScriptableObject"这条要读准**（2026-09-25 实测）：它指的是**UMod 不替你加载 `.asset` 资源**；
`ScriptableObject` / `MonoBehaviour` 这两个**类型**在 mod 程序集里**能编译、本地 lint 也 clean**。
所以"跟着角色预制件一起发的组件"（例如动态参数 Hub）是可行的 —— 那类 mod 打包本来就是既有能力
（FastBuild 复制源码进包、UMod 编译）。详见 [动态参数](FACE_TRACKING_DYNAMIC_PARAMETERS.md)。
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
混合树只能在我们的**影子 Animator** 上跑（控制器从 AssetBundle 来，见 §2.0.2 —— 这就是 Warudo 侧现在的做法），
或者做成**数据树**（那是早期在"跑不了控制器"前提下的打算，已被 §2.0.2 取代）。

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
节点上的「状态」口直接给路径，不用猜 Warudo 的目录结构（2026-09-25 之前是一个单独的「沙箱目录」口，收口时并进了「状态」的第二行）。

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
  所以接收器的「状态」口在**真丢包时**会把被丢的来源 IP:端口摆出来）、
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
  ⚠️⚠️ **但"VTS 手机 = PascalCase"只对官方那台 iOS App 成立，别当普适规律**（2026-09-25 实测反例）：
  **安卓版 VTS**（用户实测那台）**形态键发的是 iFacialMocap 命名**（`jawOpen` / `eyeBlink_L` / `mouthSmile_L` / `browInnerUp_R`…），
  而**标量发的才是 VTS 命名**（`Rotation_x` / `Position_x` / `EyeLeft_x` / `FaceFound`…），另加 PascalCase 的
  `EyeBlinkLeft/Right` —— 一份 payload 里两套方言并存。56 键的"PascalCase 形态键"照样一个都没有 ⇒ 当时按
  PascalCase 写的那份调试配置读出来全是 `缺`（原始字典里根本没那些键）。**教训：输入行按"设备实际发的"
  写 —— 现在一台设备一份单方言配置，不再靠"给每个规范名配两行"当兼容层。**
  完整的 65 键实测清单抄在 mod `README.md` §1.4；对应的调试配置叫 `ho-debug-androidVTS.hoface.json`。
* **iPhone VTS 是另一种（干净的）方言**（2026-09-25 实测 67 键）：形状那一半是**纯 VTS PascalCase**
  （`JawOpen` / `MouthSmileLeft`…），**52 个形状全部到位、0 个对不上**；标量那半边两台**同名**。
  对应配置：`ho-debug-iphoneVTS.hoface.json`。两者都在包内 `Editor/FaceTracking/Profiles/`，
  由 `.warudo-mod-research/.tools/gen-default-profile.ps1` 生成（名字取自源码与 catalog，不手抄）。
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
* ✅ **曾经"今天不做"、现在已经做完**：Warudo 侧的"影子 Animator 加载控制器"。
  当时的判断是"插件 mod 不能读盘（无 `System.IO`），而播放器无法从文件加载 `AnimatorController`（只有 AssetBundle 能）"，
  所以打算一直用**数据树**。**结论已经反转**：读写走**插件沙箱 API**（不是 `System.IO`），
  沙箱里放一个 AssetBundle，运行时 `ReadFileBytes` + `LoadFromMemory` 就能拿到 `RuntimeAnimatorController`，
  在隐藏影子上跑真控制器并采形状/骨骼 —— 见 §2.0.2（✅ 实测）与 `Core/HoFaceController.cs`。
  于是 **Warudo 侧的"混合树"不再是数据树**：控制器那条路是**唯一**求值路径（§2.0.3）。
  数据树（`Core/HoFaceChain.cs`）现在只负责**参数层**求值，`Core/HoFaceSolver.cs` 只剩头/根位置装配。
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
| 5 | 影子 Animator：`AddComponent<Animator>()` + 控制器 + 反算 | Unity 侧 ✅ 已落地（`HoFaceAnimationSession.cs:119-127`，影子台 + 只写拥有的键）。**Warudo 侧也已落地**（§2.0.2 / `Core/HoFaceController.cs`：沙箱读 bundle + 影子 Animator + 采形状与骨骼）—— 早期"不打算走这条路、改数据树"的判断已作废 | ✅ 两侧都完成 |
| 6 | Unity 侧调试面板：影子算 + 纯写入角色预制件，角色不挂任何组件 | ✅ 已落地（全局面板 + `HoFaceDebugHost` 宿主；角色上零组件） | ✅ **已完成** |
| 7 | 清掉 3 个临时节点 + 收缩 `NodeTypes` | 面板上只剩接收器 + `HoFace参数处理` + `HoFace控制求解` + 调试日志 | ✅ **2026-09-25 完成**（§2.0） |
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
