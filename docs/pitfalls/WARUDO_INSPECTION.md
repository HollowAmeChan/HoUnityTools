# 从蓝图里取证：会看漏的三件事

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
