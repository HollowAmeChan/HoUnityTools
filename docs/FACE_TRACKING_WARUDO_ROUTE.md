# 面捕方案总览：VTS 裸输入 → 中间层反算 → 喂进 Warudo 官方面捕蓝图

> 本文是这套面捕方案的**权威路线图**。带 ✅ 的是有本机实测/转储依据的；❓ 是未验证的推断。
> 环境：Warudo 0.15.0 + Mod Tool 0.14.4.8（Warudo 侧）/ Unity 6000.3.15f1（HoUnityTools 包侧）。
> 取证来源：真机程序集反射转储（`.research/warudo-knobs/`，含 437 条 `typeId → 类型` 对照）、
> 运行期探针（`Mods-Ho/HoFaceTracking`）、以及**本机场景文件本身**
> `Warudo_Data/StreamingAssets/Scenes/DefaultScene.json`。参数标准见 [参数标准表](PARAMETER_STANDARDS.md)。

---

## 0. 一句话

**VTS 裸输入 → 中间层配置映射成干净参数（VB 那样的输入行）→ 我们自己的混合树控制器算 →
反算成"这个角色身上实际做了什么" → 直接喂 Warudo 官方面捕蓝图的下游节点。**

没有我们发明的标准名层。输出的键就是角色上真实存在的键。

---

## 1. Unity 侧（HoUnityTools 包）

* **角色：正常做，不挂任何面捕相关组件。** 这是硬约束 —— 它模拟 Warudo 侧"不耦合进角色 mod"。
* **调试走一个专用面板**（不是挂在角色上的组件）。它吃三样：
  1. **一个新的控制器**（带混合树）—— Unity 侧控制器能跑；
  2. **一份裸参数处理配置**（中间层：线名 → 规范名的输入行 + 表达式 / 曲线 / 有序修饰符）；
  3. **连接手机设备的能力**（VTS 手机 / iFacialMocap，UDP）。
* **运行中完整算一遍，再纯写入到角色预制件上**：影子 Animator 跑控制器 → 取出结果
  （形态键权重、骨头旋转、根位置）→ 直接写角色。**不许借角色自己的 Animator。**
* 为什么必须这样：Warudo 里我们的控制器对**本体无效**，只能纯写入；Unity 侧的调试环境
  必须模拟同一个约束，否则调出来的东西搬过去就不一样。

---

## 2. Warudo 侧：最终连线（**5 个节点，两个 mod**）

```
[HoVtsTrack mod]                    [HoVtsTrackController mod]              [官方节点 ×3]
  HoVts 接收器      ──原始值/状态/调试──▶  中间层+控制器（合并成一个节点）  ──▶ 设置角色面部追踪 BlendShape 列表
                   （裸线名原样交出）       内部 = 我们的中间层配置 + 控制器         覆盖角色骨骼旋转偏移列表
                                          输出 = BS 列表 / 骨骼旋转偏移 / 根位置     覆盖角色根位置
```

* **`HoVtsTrack`** —— **通用 Warudo 插件**：一个接收器节点，只做"读值 + 直通传参"，
  外加状态与调试输出。**写一次以后基本不用再动。**
* **`HoVtsTrackController`** —— 语义上同样通用，但**带着我们指定的中间层配置 + 动画控制器**：
  内部在影子上跑控制器、把结果反算出来，直接产出 BS 列表 / 骨骼旋转偏移 / 根位置。
* 应用端不碰 Warudo 的通用机械（`SWITCH_*` / `SMOOTH_*` / `MERGE_*` / `EMPTY_*` /
  `DEFAULT_*` / `LOOK_AT`），只留那三个官方应用节点。

### 2.1 应用节点的字段：两处必须知道的默认值

| 节点 | 关键字段 | 官方默认 | 说明 |
|---|---|---|---|
| 设置角色面部追踪 BlendShape 列表 | `BlendShapes` | — | 键就是**角色自己的形态键名** |
| 覆盖角色骨骼旋转偏移列表 | `BoneRotationOffsets` | — | **偏移**语义：单位四元数 = 不改那根骨头 |
| 覆盖角色根位置 | `RootPosition` / `RootPositionWeight` / `Immediate` / `AllowFloating` | 全 `0` / `false` | ⚠️ **权重默认是 `0.0`，不是 1** —— 留空 = 这个节点永远不生效。官方图里它是**被接线驱动的**（`SWITCH_FLOAT.Output → RootPositionWeight`，即 `1 − IsTracked`） |

⚠️ **"根位置的值本身能不能表达 0 权重"：按下面 §2.2 的规律，几乎可以肯定"不能"。**

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
（❓ 仍未实测；这条是从整个 API 的权重分布规律推的，比单看签名强）。
若成立：`(0,0,0)` + 权重 1 不是"不加偏移"，而是**"断言根在原点"** ——
**能表达 mute 的只有权重，值本身表达不了**（那个权重在语义上就是**节点级的 mute**）。

实测判据（一分钟、不用构建）：`RootPosition` 设 `(0,0,0)`、权重设 `1`，给角色放一段
**带位移**的动作，看位移还在不在 —— 还在 = 增量语义（值够用、权重多余）；
被钉住 = 绝对语义。

**这一条不影响面捕**：面捕本来没有根运动，所以权重填 1 + 值恒为原点在"只有面捕"的场景里
看不出差别。真要做根运动时改值即可，连线不用动。

---

## 3. 官方蓝图解剖（★ 这一节是设计的直接依据）

✅ **证据**：本机场景 `DefaultScene.json` 里那张图 `面部追踪 - iFacialMocap`（21 个节点），
连同节点端口定义与全部 25 条数据连线 + 5 条流程连线，都是从这个文件里解出来的。

### 3.1 官方接收器节点的端口 —— 这就是接口

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
（`api-scan` 转储）。

→ **五个端口，不多不少。对上它就能直接塞进官方那张图，不需要自己造图。**

### 3.2 接线

```
ON_UPDATE ─flow→ SET_CHARACTER_TRACKING_BLENDSHAPES ─→ OVERRIDE_CHARACTER_BONE_ROTATION_OFFSETS
            ─→ OVERRIDE_CHARACTER_ROOT_POSITION

融合形状：
  接收器.BlendShapes → SWITCH_BLENDSHAPE_LIST.IfTrue
  接收器.IsTracked   → SWITCH_BLENDSHAPE_LIST.Condition
  EMPTY_BLENDSHAPE_LIST.Output → SWITCH_BLENDSHAPE_LIST.IfFalse
  → SMOOTH_BLENDSHAPES → GENERATE_HEAD_&_EYES_MOTION.BlendShapes
  → .OutputBlendShapes → SET_CHARACTER_TRACKING_BLENDSHAPES.BlendShapes

骨骼：
  接收器.BoneRotations → SWITCH_ROTATIONS.IfTrue
  DEFAULT_CHARACTER_BONE_ROTATIONS.Output → SWITCH_ROTATIONS.IfFalse
  → MERGE_CHARACTER_BONE_ROTATIONS（Face/Head/Pelvis/LeftLeg/RightLeg 五个口接同一源）
  → SMOOTH_ROTATIONS → GENERATE_HEAD_&_EYES_MOTION.BoneRotations
  → CHARACTER_LOOK_AT_TARGET → .OutputBoneRotations
  → OVERRIDE_CHARACTER_BONE_ROTATION_OFFSETS.BoneRotationOffsets

根位置：
  接收器.RootPosition → SMOOTH_POSITION → OVERRIDE_CHARACTER_ROOT_POSITION.RootPosition
  SWITCH_FLOAT.Output  → OVERRIDE_CHARACTER_ROOT_POSITION.RootPositionWeight

权重：
  接收器.IsTracked → SWITCH_FLOAT.Condition；SWITCH_FLOAT.Output → SUBTRACT_FLOAT.B
  SUBTRACT_FLOAT.Result → GENERATE_HEAD_&_EYES_MOTION.Weight

收拾：
  ON_DISABLE_GRAPH → RESET_CHARACTER_TRACKING_BLENDSHAPES → RESET_CHARACTER_BONES
```

### 3.3 每个节点是什么，以及它体现了什么考虑

| 节点 | 端口 / 功能 | 体现的考虑 |
|---|---|---|
| `SWITCH_FLOAT` / `SWITCH_BLENDSHAPE_LIST` / `SWITCH_ROTATION_LIST` | `Condition` + `IfTrue` / `IfFalse`，**并且两个方向各自有 `TransitionTime` / `TransitionDelay` / `TransitionEasing`** | **断流不是硬切，是一次带缓动和延迟的过渡。** 追踪丢失 → 换掉输入（融合形状换空列表、骨骼换默认）+ 用过渡时间淡出 |
| `SMOOTH_BLENDSHAPES` / `SMOOTH_ROTATIONS` / `SMOOTH_POSITION` | 各自 `SmoothTime`（滑条 0–2 s），其中 `SmoothBlendShapeListNode : ProcessBlendShapesNode` | 平滑是**分通道**给的，不是全局一个系数 |
| `MERGE_CHARACTER_BONE_ROTATIONS` | **9 个身体部位口**：`Face` / `Head` / `Pelvis` / `LeftArm` / `RightArm` / `LeftFingers` / `RightFingers` / `LeftLeg` / `RightLeg` → 一个 `Quaternion[]` | **官方支持"多个追踪器各管一块身体再合并"**。面捕图只接了 5 个口；这也解释了接收器为什么要输出一个大数组 |
| `DEFAULT_CHARACTER_BONE_ROTATIONS` | **没有任何输入端口**，只输出 `Quaternion[]` | 它是"不追踪时用的那份旋转"。配合下面的 offsets 语义 → **默认 = 不改动** |
| `OVERRIDE_CHARACTER_BONE_ROTATION_OFFSETS` | `Quaternion[] BoneRotationOffsets` + `Immediate` | **是偏移（offset），不是绝对旋转** —— 叠在角色自身动画的旋转之上 |
| `OVERRIDE_CHARACTER_BONE_ROTATIONS` | `BoneLocalRotations` + `BoneRotationWeights` | 另一条路：**绝对局部旋转 + 每根骨骼一个权重**。字段名直接证明这一层是**局部空间** |
| `OVERRIDE_CHARACTER_BONE_POSITIONS` | `BoneLocalPositions` + `BonePositionWeights` + `SkipNonHipsBones` + `SkipEyeBones` | 位置层专门考虑过"**只给胯、别动眼**"（眼骨位置不能让外部动捕乱推） |
| `SET_CHARACTER_TRACKING_BLENDSHAPES`（`: OverrideCharacterBlendShapesNode`） | `Character` + `Dictionary<string,float> BlendShapes` + `ApplyToAllSkinnedMeshes` + `TargetSkinnedMesh` + `UseVRMBlendShapeProxy` + `AdditiveVRMBlendShapeClips` + `ClampAllBlendShapes` + `ClampedBlendShapes` / `UnclampedBlendShapes` | 键就是**角色自己形态键名**（自动补全走角色）；同时覆盖"所有网格/指定网格""VRM 代理/非 VRM""哪些键要夹" |
| `CHARACTER_LOOK_AT_TARGET` | `BoneRotations` + `Character` + `Target` + `Enabled` + `Weight` / `HeadWeight` / `EyesWeight` + `MaximumLookAtAngle`(30–135°) + `MaximalHeadRotation` / `MaximalEyeRotation`（度）+ `SmoothHeadTime` / `SmoothEyesTime` | 注视是**叠加在追踪结果上的后处理**，头/眼分开限幅、分开平滑 |
| `EMPTY_BLENDSHAPE_LIST` | 无输入 → 空 `Dictionary<string,float>` | 提供"空"作为一个显式常量，配合 switch |
| `FloatSubtractNode` | `A - B` | 用来算 `1 - IsTracked` 当淡出权重 |
| `ON_UPDATE` / `ON_DISABLE_GRAPH` | 事件 → flow | 每帧推进；停用图时**归位**（reset）而不是留着上一帧 |
| `GENERATE_HEAD_&_EYES_MOTION`（面板上叫**生成头部待机动画**） | ⚠️ **不在 `Warudo.Plugins.Core` 里** —— 它属于官方 iFacialMocap 插件，函数体反射不到。**完整端口表来自用户截图**（✅ 见过界面，❓ 内部行为未知）：输入 `BlendShape 列表` / `骨骼旋转列表` / `权重` / `角色` / `应用`；生成开关 `自动眨眼` / `自动眼部运动` / `自动头部运动`；眨眼参数 `眨眼 BlendShape 列表` / `眨眼间隔(X–Y)` / `眨眼速度` / `默认闭眼程度` / `移除输入眼部 BlendShape`；视线参数 `视线权重` / `最大视线角度`；头部参数 `头部倾斜` / `转头间隔`；输出 `输出 BlendShape 列表` / `输出骨骼旋转` | **这不只是"面捕转头眼运动"，它同时是丢追时的待机生成器**：`移除输入眼部 BlendShape = 是` 说明它把输入的眨眼键**剔掉、换成自己合成的**（接管而非让路）。所以官方丢追时的填充**来自这个节点**，不是来自角色自己的动画 |

> ⚠️ **上一条曾经写错过，教训记在这里**：早先我只看**连线**就把这个节点的输入当成
> `BlendShapes` / `BoneRotations` / `Weight` 三个，于是断言"图里没有待机生成节点"。
> **连线只给出被连上的端口，不等于完整端口表**；而这个节点在插件程序集里、反射不到。
> 教训：**判断一个节点"有什么能力"必须拿到它的完整端口表**，否则会得出方向完全相反的结论
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

### 4.1 mod 能加载自己包里的资产 ✅

```
UMod.ModHost : MonoBehaviour
    prop public IModAssets Assets {get;}
    prop public IModAssets SharedAssets {get;}      ← 对应产物里的 sharedassets.bin
UMod.IModAssets                                       （住在 UMod-Interface.dll）
    prop  public Boolean CanLoadAssets {get;}
    prop  public Int32 AssetCount {get;}
    method public T Load<T>(String nameOrPath)
    method public T[] LoadWithSubAssets<T>(String nameOrPath)
    method public T Instantiate<T>(String nameOrPath)
```

`Plugin.ModHost` 是 public，所以：

```csharp
var assets = this.Plugin.ModHost.SharedAssets;
if (assets != null && assets.CanLoadAssets)
    shadowAnimator.runtimeAnimatorController = assets.Load<RuntimeAnimatorController>("<名字或路径>");
```

**两个实测到的坑：**

* **`ModAssetsBridge` 不是公开类型** —— 就是那个带 `FindAllRelativeNames()` 的实现，
  cast 去枚举包内资产名会报 `CS0122`。`IModAssets` 没有枚举接口 →
  **只能按名字/路径加载，拿不到清单**。
* **运行时没有 `AnimatorController` 这个类型** —— 它是 `UnityEditor.Animations` 的编辑器独占类；
  玩家端只有 `RuntimeAnimatorController`（`Animator.runtimeAnimatorController` 收的就是它）。

禁令边界：被点名的是 **`UMod-ModTools`**；`UMod.dll` / `UMod-Interface.dll` 不在禁令里。
`tools/compile-check.ps1` 的引用列表已补上这两个。

### 4.2 角色身上没有 controller ✅

实测：`本体：enabled=True activeAndEnabled=True avatar=humanoid isHuman=True controller=null`。
Warudo 的角色动画走 Animancer 直接播 clip。→ **在角色本体上跑混合树这条路不存在**，
混合树只能在我们的影子 Animator 上跑（控制器从 4.1 那条路来）。

### 4.3 `CharacterAsset` 不交出"裸预制件"，但活实例够用 ✅

整条继承链（`CharacterAsset : FromSourceGameObjectAsset : GameObjectAsset : Asset`）没有 `Prefab`
字段、没有 public 加载入口（`CreateGameObject()` 是 `protected virtual`）。能拿到的是活实例：
`AvatarClone` / `GameObject` / `Animator` / `SkinnedMeshRenderers` / `BlendShapes` /
`HumanBodyBoneToBodyTransforms`。

**骨架与网格是同一个根下的两块**，复刻要复刻根：

```
Character Root                            ← GameObject（复刻这个）
  Character Parent/Root/…                  ← 31 个渲染器都挂这儿
  Character Avatar Clone Parent
    Character Avatar Clone                 ← AvatarClone：998 子物体 + Animator，0 渲染器
```

### 4.4 中间层配置文件放插件沙箱 ✅（已跑通）

`Plugin.PersistentData`（`PluginPersistentDataManager`）是沙箱化文件 API。
实测路径：`Warudo_Data/StreamingAssets/Plugins/Data/<pluginId>/`，
本机已经出现 `hollow.hofacetracking/ho-2d-test1.hoface.json`。

⚠️ **`GetFiles` 不能用**：它第三个参数是 `System.IO.SearchOption`，而 UMod 构建期审查
禁止引用 `System.IO.*`。只能用 `GetFileEntries(相对路径, 通配, Func<string,bool>)`。

### 4.5 ✅ 已解决：profile 的 JSON 读写换成我们自己的

**曾经的问题**：沙箱里那份 `ho-2d-test1.hoface.json` 只有 **342 字节** ——
`format` / `version` / `displayName` / `notes` 四个字段在，**`inputs` / `outputs` 两个
`List<内部类>` 整个没了**，而文件末尾的 `}` 是完整的（不是截断，是**序列化器跳过了这两个字段**）。

**读路径同样是坏的**：运行期实测 `配置问题 = "配置文件里一行输出都没有。"`，
而 `可用配置` 明明列出了那两个文件 —— 也就是 `JsonUtility.FromJson` 解析出了空列表。

根因：**Unity 的 `JsonUtility` 在播放器里会静默丢掉这两个 `List<内部类>` 字段，而编辑器里是好的。**
这类"编辑器里好好的、搬到 Warudo 就变样"的差异，对中间层配置这种心脏部件不能容忍。

**现在的做法**：`Runtime/FaceTracking/HoFaceProfileJson.cs` —— 自己写的读写器
（`HoFaceProfile` 退化成"格式的名字 + 入口"）。顺带多了两件事：
未知字段统一跳过（向前兼容照旧）、**报错带字符位置**。

**验证**：`.research/profile-json-test` 离线跑**包里的真源码**（桩件顶替 UnityEngine），
`dotnet run` → **87/87 通过**，含 `写→读→写 文本完全一致（字节稳定）`、
128 输入行 / 56 输出行、曲线关键点与修饰符**顺序**往返、转义与 Unicode 往返、
`a{b}c[d],e:f"g` 这种带括号逗号的字符串、坏 JSON 报字符位置。

> 教训：**别用 `JsonUtility` 存我们的数据。** 它在编辑器里能工作，在播放器里对
> "内部类 + 嵌套 List"这套结构会静默降级，症状是"面板里明明有值、Warudo 里什么都没有"，
> 极难往回查。

### 4.6 ✅ 第三次：VTS 收包也丢字段（规则就此钉死）

同一类坑又咬了一次，这次在收包：VTS 载荷里 `BlendShapes` 是 `List<{k,v}>`
（官方示例里就是 `List<VTSTrackingDataEntry>`），用 `JsonUtility.FromJson` 解析的结果是 ——
**12 个头眼分量全在、`FaceFound`/`Hotkey`/`Timestamp` 都在，52 个形态键全丢**
（运行期实测 `本帧键数=15`）。包"解析成功"、字段名也对，丢的正是最要紧的东西。

规则因此变成硬的、覆盖**所有**数据路径：

> **我们的数据一律不用 `JsonUtility`** —— 配置文件读写、接收端线格式，全部走
> `Runtime/FaceTracking/HoJson.cs` 里自己写的读取器。
> 接收器解析搬到 `HoVtsPacket.cs`（纯静态、不碰 socket），所以能脱离 Unity 离线测。

### 4.7 VTS 手机协议的确切形状（✅ 官方文档 + 手机截图核对）

* **不是"手机主动推流"。** 手机不接受目标地址：它把数据发回**请求包的源 IP**，
  端口用请求里 `ports` 数组指定的。所以**手机上除了那个开关没有要填的东西**。
* 我们这边填：`手机 IPv4` = 手机的局域网 IP（**必须填对**，源 IP 过滤会静默丢包）、
  `手机端口` = `21412`（或 App 上显示的）、`本机端口` = 数据回来的落点（如 `49985`）。
* 请求包（`HoVtsPacket.BuildRequest`，离线测试断言了原文）：
  `{"messageType":"iOSTrackingDataRequest","time":5,"sentBy":"HoFaceTracking","ports":[49985]}`
  `time` 允许 0.5–10 → **每秒续一次**；`sentBy` 是**手机上 `Connected VSF Clients:` 列表里显示的名字**。
* **线名拼写 = PascalCase**：官方 `VTSARKitBlendshape.cs` 那份枚举里 52 个名字是
  `EyeBlinkLeft` / `JawOpen` / `MouthSmileLeft` / `TongueOut`…
  我们默认配置里"首字母大写"的规则**逐个对得上**（离线测试覆盖了 52 个）。
* 本机端口实测：官方 iFacialMocap 接收器资源占 `49983`（关掉它才空出来）、我们占 `49985`；
  防火墙已有 `Warudo.exe` 的 UDP 任意端口入站放行规则，不用另加。

---

## 5. 空间问题 ❓（唯一未定的技术点）

* ✅ 消费端要**局部**：`BoneLocalRotations` / `BoneLocalPositions`（见 3.3）。
* ✅ 骨骼那条路是**偏移**语义（`BoneRotationOffsets`，`DEFAULT = 不改`）。
* ❓ **偏移相对什么基准**：有 `InitialBoneLocalPositions` / `InitialBoneWorldPositions`，
  但公开面上**没有 `InitialBoneLocalRotations`**。
* 判据：用探针**数值比对**同一根骨头的 `localPosition/position/localRotation/rotation`
  与那几个数组同下标元素，相等即那个空间；都不等说明它是加载时抓的初始基准 ——
  那正是我们算偏移要对齐的基准。

---

## 6. 明确不做 / 搁置

* **搁置**：注册 `CharacterTrackingTemplate` 让官方"面捕"下拉项一键生成我们的图。
  机制是现成的（`Apply(CharacterAsset) → (List<Asset>, List<Graph>)`），但**先不碰自动生成**，
  先把节点本身做对。
* **不做** tracker asset（`GenericTrackerAsset` 子类）：那条路会在角色上建一份追踪器资源，
  等于把中间层配置变**每角色一份**，与"中间层是设备/控制器级"冲突。
* **不做**接收器里的改名 / 量纲 / 断流回中性：前两样在中间层配置里，最后一样在图上（3.2）。
* **不依赖**官方那套"一个 mod 带一堆资源"的打包方式；我们只带**自己的控制器**。

---

## 7. 待办

| # | 事项 | 判据 |
|---|---|---|
| 1 | ~~修 profile 读写~~ ✅ **已完成**（见 §4.5）：`HoFaceProfileJson.cs` + 离线往返测试 **52/52** | 沙箱里的 profile 读出来是 128 输入行 / 56 输出行 |
| 2 | mod 里放一个 `.controller`，验证 `SharedAssets.AssetCount` 变正、`Load` 取得回 | 探针 `Mod 资产` 一节（已知 `AssetCount=2`，名字约定还没试出来） |
| 3 | ~~量出骨骼数组的空间与偏移基准~~ ✅ **已量出**（见 §5） | 数组按 `HumanBodyBones` 索引、是**局部**的；基准 = `InitialBoneLocalPositions`，只有 Hips 会变 |
| 4 | 中间层节点输出对齐 3.1 的 5 个端口，替换掉官方接收器节点后行为不变 | 图上换掉接收器后面部照常动 |
| 5 | 影子 Animator：`AddComponent<Animator>()` + `Load<RuntimeAnimatorController>` + 反算 | 打参数进去、读回形态键权重与骨头旋转 |
| 6 | Unity 侧调试面板：影子算 + 纯写入角色预制件，角色不挂任何组件 | 面板里算出来的值与 Warudo 里一致 |

---

## 8. 测试怎么跑

| 测什么 | 怎么跑 | 现在的结果 |
|---|---|---|
| 中间层配置的 JSON 读写 + VTS 收包 | `dotnet run --project .research/profile-json-test` | **87/87 通过** |
| 表达式求值器对 VBridger 的覆盖 | `dotnet run --project .research/expression-coverage` | 19/19 |
| mod 脚本能不能对着真机 DLL 编译 | `tools/compile-check.ps1`（BreakWarudo 工程里） | 全绿 |
| Warudo 运行期行为 | 读 `AppData\LocalLow\HakuyaLabs\Warudo\Player.log` | — |

> **`Player.log` 这条很重要**：Warudo 没有界面控制台，但我们的 `Debug.Log` 会落到那儿，
> 所以验证运行期行为不用截图，直接读文件就行（面板上的「查看值」节点是给人看的备份）。
> 另外 `.research/` 下的离线测试跑的是**包里的真源码**（用桩件顶替 UnityEngine），
> 所以"包里的代码到底对不对"不需要等 Warudo 构建就能验。
