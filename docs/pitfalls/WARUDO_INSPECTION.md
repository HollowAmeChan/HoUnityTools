# 从蓝图里取证：会看漏的那些事

正本：[面捕在 Warudo 的路线](../FACE_TRACKING_WARUDO_ROUTE.md)（官方那张图的逐节点解码）、
[Warudo 打包、工具链与系统脚本](BUILD_AND_TOOLING.md)。
这份只留**我们真的因此下过错误结论**的那些。取证的手段就四样：**反射 DLL / 场景文件 / `Player.log` / mod 源码**。

> ⚠️ 本文提到的那台 **「角色探针」节点已于 2026-09-25 删掉**（结论都落进路线图了）。
> 凡是引"探针报告 / 那一次读数"的地方，都是**当时**的读数（证据留在 `Player.log` 里）；
> 现在要再量同一件事，得先临时加个观察窗。

## 1. 端口表要**三个来源求并集**，少一个就会得出反的结论

症状：把 `GENERATE_HEAD_&_EYES_MOTION` 的输入当成 3 个，于是断言"官方图里根本没有待机生成节点" ——
**方向完全相反**（它就在那张图里，而且权重是被 `1 − IsTracked` 驱动的）。

原因：**被连线驱动的输入口不会存进场景里的 `dataInputs`**（Warudo 不存），而没被连线的口不会出现在
`dataConnections` 里。所以：

| 来源 | 给什么 | 会漏什么 |
| --- | --- | --- |
| **反射 DLL**（`dotnet run -- <dll> <TypeName>`，见 `.research/warudo-knobs`） | 全部 `[DataInput]`/`[DataOutput]` 成员、类型、默认值 | 图里**实际**连了什么、值是多少 |
| 场景里的 `dataInputs`（节点实例） | 没被连线的口的实际取值 | **被连线驱动的口**（`SWITCH_ROTATIONS.IfTrue/IfFalse`、`GENERATE….BoneRotations` 都只出现在连线里） |
| `dataConnections` | 谁连到谁 | 没被连线的口 |

教训：**反射 ∪ `dataInputs` ∪ `dataConnections`**。只看后两个 → 会漏接口；只看反射 → 会把"接口里有"
当成"图里在用"。

## 2. "这个节点属于哪个插件"不能从它在哪张图里猜

症状：`GENERATE_HEAD_&_EYES_MOTION`（面板上叫**生成头部待机动画**）出现在**官方 iFacialMocap 那张图**里，
就被当成 iFacialMocap 插件的节点 → 反射时找错程序集 → 以为"函数体反射不到" → 退回用截图数端口，
整条结论的质量掉一档。

真相（✅ 反射）：它是 **ProceduralAnimations** 的 `GenerateIdleHeadAnimationNode`，类型在
**`Assembly-CSharp.dll`**，与 iFacialMocap 插件无关。

怎么办：**先按类型名反射整个 DLL 集合**（`Assembly-CSharp.dll` 常常就是答案），别按"它出现在哪张图"推归属。

## 3. 开关的方向取决于 `IfTrue/IfFalse` 的**当前值** —— 别凭字段名猜

症状：把 `角色看向目标`（LookAt）的权重记成 `1 − IsTracked`、把待机生成器记成 `IsTracked`（**正好反了**）。

原因：`SWITCH_FLOAT` 的输出方向由它的 `IfTrue`/`IfFalse` 决定，而**默认值是 `0/0`**、
`TransitionTime` 默认 `0.4`；这张图里被改成 `IfTrue=1.0 / IfFalse=0.0`、`TransitionTime=2.0`。
不看实际值，"条件为真时输出哪个"就纯靠猜。

本机实际链路（✅ 读自 `DefaultScene.json`，记在这里防止再记反）：

```
接收器.IsTracked → SWITCH_FLOAT.Condition        （IfTrue=1.0 / IfFalse=0.0）
SWITCH_FLOAT.Output = IsTracked                   → CHARACTER_LOOK_AT_TARGET.Weight
SUBTRACT_FLOAT.A = 1.0，B ← SWITCH_FLOAT.Output    → Result = 1 − IsTracked
                                                  → GENERATE_HEAD_&_EYES_MOTION.Weight
```

## 4. 静止姿态下量不出"基准" —— 那是无效测量

症状：用探针量骨骼数组，得到"`InitialBoneLocalPositions` 就是基准、`EndOfLateUpdateBoneRotations`
是当前姿态"这一堆结论，看着很硬。

原因：那次角色**全身旋转都是 identity、也没有根位移**，于是"局部 / 世界"与"当前 / 初始"**全都相等** ——
**区分不开**，任何结论都只是重言式。

怎么办：要量这种问题，先让角色带上**非 identity 旋转 + 根位移**再跑探针。
现有 ✅ 的只是：数组长度 55 = `(int)HumanBodyBones.LastBone`、下标就是 `HumanBodyBones` 枚举值、
`InitialBoneLocalPositions[i] == localPosition`、`InitialBoneWorldPositions[i] == position`。

## 5. `IModAssets` 拿不到资源名字清单，但**能按 ID 枚举**

症状：`Plugin.ModHost.SharedAssets`（`UMod.IModAssets`）没有枚举名字的接口，`ModAssetsBridge` 又非公开
（`IsPublic=False`，直接 cast 会 `CS0122`），于是"包里的资源叫什么"成了死结，只能靠猜名字 `Load("HoFaceTree")`
—— 猜不中就以为"资源没打进去"。

真相：`IModAssets` 有一整套 **`Load<T>(int assetID)`** 重载（✅ 反射）。
`AssetCount` 能拿到（本机实测 `CanLoadAssets=True / AssetCount=2`），于是 **`0..AssetCount-1` 逐个试**即可，
不用猜名字（❓ 还没实测跑通）。

## 6. `Player.log` 里探针报告是**多行**的

症状：`Select-String 'Ho 面捕'` 只捞到第一行，后面那张表全看不见，于是以为探针没输出。

怎么办：按行号取那一段（本机那份报告从第 1468 行起），或者读全文再切片。
`Player.log` 的路径与"怎么读"见 [Warudo 打包、工具链与系统脚本](BUILD_AND_TOOLING.md)。

## 7. "官方节点是**读**还是**等着被喂**" —— 去看它的方法体（IL）

症状：我们自己写的调试节点收不到上游写的值（字段与 `GetDataInput` 都是空），
而同一个上游接到官方「查看值」上就有值 —— 光看签名看不出差别。

怎么办：`warudo-knobs` 现在有 `--il`（反射读 `MethodBody`，把 call/field/string 的 token 解析成名字）：

```powershell
dotnet run --project .research/warudo-knobs -- Warudo.Plugins.Core.dll InspectValueNode OnUpdate --il
```

官方 `InspectValueNode.OnUpdate` 的真身（2026-09-25 本机 DLL）：

```csharp
public override void OnUpdate()
{
    if (Graph != Context.OpenedScene.GetSelectedGraph()) return;          // 只在"当前打开的那张图"里更新
    if (!Context.Service<WebSocketService>().HasConnectedSessions()) return;  // 只在本机有 WebSocket 会话时更新
    InvokeFlow(null, false);
    Text = JsonConvert.SerializeObject(A, serializerSettings);            // ← 读的是 **字段** A
    BroadcastDataInput("Text");                                          // ← 只广播 Text
}
```

结论（三条都很有用）：
1. 官方节点**读字段、不拉端口**：`ldfld A` → 序列化 → `stfld Text` → `BroadcastDataInput("Text")`。
   所以"上游把值写进下游字段"这条机制**是有的**；我们的字段是空 ⇒ **线根本没送到我们那个口上**。
2. 它有两道 early-return：**只在自己那张图**（`GetSelectedGraph()`）、**只在本机有 WebSocket 会话**时更新。
   自己写节点时别照抄这两条（我们不需要）。
3. **`BroadcastDataInput` 才是让界面刷新那一句**，光有字段赋值不够 —— 我们踩了两轮才认下来：
   有一版用 `SetDataInput(key, value, broadcast: true)` 代替它，结果**端口里明明是新值
   （日志 `显示口 45 字符`）、界面上那块 `[Markdown]` 纹丝不动**（只显示创建时的初始文字）。
   把官方那两句（`Text = 值; BroadcastDataInput(nameof(Text));`）补回去，同一份代码立刻就画出来了。
   → 这段显示字段，照抄这两句；`SetDataInput` 只影响端口值，不等于界面会重画。

**顺带一个"想要能复制的文本"的结论**：`[Markdown]` 是**只读渲染**（官方「查看值」也复制不出来），
而 Warudo 没有"自绘节点 UI"的口子（`Warudo.Core` 里没有自定义绘制特性、也没有相关基类方法，
反射 `--list Custom/Draw/UI` 全空）。所以"能鼠标选中、能 Ctrl+C 的框"只有一个选择：
**`[DataInput]` + `[MultilineInput]` 的可编辑多行框**（同样是"字段赋值 + `BroadcastDataInput`"去写它）。
我们最终的「Ho调试日志」就是这个形态：一个入口 + 一个能复制的框，没有按钮、没有说明文字。

**连带教训：改端口名/类型会让蓝图里已有的连线变成孤儿。** 我们的调试节点从 `Content`/`Source`(string)
改成 `A`(object) 之后，旧连线指向的键在新类型上不存在 ——
UI 上可能**还画着**那条线（看着接在「写入」上），但求值时找不到端口，值永远不进来；
`Player.log` 只会显示"端口 空 · 字段 空"。

**这种"看着接了却没值"现在可以一句话定性**：`Graph.GetInputDataConnections(Node)` 返回
`IReadOnlyDictionary<String, List<DataConnection>>`，**键就是输入口名**，`DataConnection` 上带
`OutputNode` / `OutputPort` / `InputNode` / `InputPort`。于是节点自己就能分清三种情况：

| 情况 | 从 API 上看到的样子 |
|---|---|
| 线没接上 | 字典里没有你那个键（或者整张字典是空的） |
| 接在一个**已经不存在**的旧口上（孤儿线） | 有 `"Content"` 这样的键，但该连接的 `InputPort == null` |
| 接对了、上游还没给值 | 键是 `"A"`、`InputPort` 非空，端口值却是空 |

`InputPort == null` 就是孤儿线的**确定判据**（比"看 UI 画没画"可靠）。我们的「Ho调试日志」把这句诊断
每 0.5 秒往 `Player.log` 写一次（`Nodes/HoDebugLogNode.cs`），**只在还没拿到值的时候写、且只在连线变化时写**
—— 界面上依旧只有一个入口 + 一个能复制的框，不为了调试往 UI 上堆东西。

**另一条顺手的宽容设计**：如果上游是**直接接在显示框那一行**上（老版本那种用法），节点就用
`GetInputDataConnections` 探到这一点、**不再去覆盖那个字段**，让它自己显示。这样"接入口"和"接显示框"
两种接法都能用 —— 用户不用知道我们内部换了哪个字段名。

（`DataConnection.OutputNode.Name` 拿到的是**标题还是内部名**没验过；只拿它认人，不参与逻辑。）

**这件事的结论在 §8：别再赌"上游会推进来"，顺着连线直接调上游那个口。**

## 8. 别赌"上游会把值推进来"：`[DataOutput]` 是**方法**，顺着连线自己调

症状（2026-09-25 本机）：调试节点的线**确实接在**「写入」上，`Player.log` 里写得明明白白：

```
[Ho 调试日志] 输入连线：「A」←Ho Face 接收器（VTS 手机）.RawValues
```

可 `A` 就是空的，端口也是空的 —— 而同一根上游喂官方「查看值」有数据。
前后查了两轮"是不是线接错了"，都不是。

**先纠正一个误读**：官方 `InspectValueNode.OnUpdate` 里那句 `InvokeFlow(null, false)` **不是"把上游拉过来"**。
`warudo-knobs --il` 顺着读下去：

```csharp
// Node.InvokeFlow(String key, Boolean invokeWhenDisabled)
public void InvokeFlow(string key, bool invokeWhenDisabled)
{
    Graph.InvokeFlow(this, key, invokeWhenDisabled);              // 就一层转发
}

// Graph.InvokeFlow(Node node, String key, Boolean invokeWhenDisabled)：key == null 那一支
if (key == null) { invokedFlow.Invoke(node, null); return; }      // 把自己接回流程
```

也就是说它是"**我这一帧有新东西，把我接回流程往下游传**"，跟"上游什么时候灌进我的字段"是两码事。
（顺便：`Graph.InvokeFlow` 里有一处 0.4 s 节流 `lastInvokeFlowBroadcastTimestamps`，是给"广播活跃连线"用的。）

**真正的定规** —— Warudo 的口**不是字段对字段的赋值**：

> `[DataInput]` = public 字段；**`[DataOutput]` = public 无参方法**。

我们的接收器节点正是这样：`public Dictionary<string, float> RawValues() => HoFaceInputState.Snapshot();`
—— **纯读**，调一次就有值，根本不需要谁来推。

**所以可靠的做法**（`Nodes/HoDebugLogNode.cs` 定案，"谁先跑、什么时候灌字段"一概不赌）：

1. `Graph.GetInputDataConnections(this)` 拿到上游 `DataConnection`；
2. 取 `connection.OutputNode` + `connection.OutputPort.Key`；
3. **按口名在「上游节点的类型」上找那个 public 无参方法，直接调它**（属性/字段也顺手认一下）；
4. 读到的就是这一帧的值。端口/字段那条老路留着当**兜底**（真被推过来时照样认）。

副作用心里要有数：这等于**替流程图求值一次上游那个口**。对 `Snapshot()` 这种纯读无所谓；
要是上游那个口本身有副作用，就得先想清楚（我们自己的节点都是"读状态"，安全）。

**顺带一个好处**：直读对**孤儿线**照样有效 —— 只要 `OutputNode`/`OutputPort` 还在，
哪怕线上挂的键已经不是我们的口，值也读得出来。所以"改端口名把老线弄成孤儿"不再致命
（诊断日志照旧会提示，见 §7）。
