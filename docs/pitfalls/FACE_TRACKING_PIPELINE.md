# 面捕流水线的坑：值没到、包丢了、时间对不上

正本：[面捕工作流](../FACE_TRACKING_WORKFLOW.md)（怎么用）、[面捕中间层处理](../FACE_TRACKING_MIDDLE_LAYER.md)（值怎么加工）、
[面捕设计与已验证机制](../FACE_TRACKING_DESIGN.md)（机制层）。
这份只留**咬过我们的那些**，每条都带当时的现场。

## 1. `JsonUtility` 会**静默丢掉** `List<嵌套类>` 字段（咬过三次）

症状（三次都是"解析成功、字段名也对、东西没了"）：

| 现场 | 表现 |
| --- | --- |
| 写中间层配置 | 写出来只有 **342 字节**，行全丢 |
| 读中间层配置 | 面板报「配置文件里一行输出都没有」 |
| 收 VTS 手机包 | `本帧键数=15` —— 12 个头/眼分量在，**52 个形态键全没了** |

原因：`JsonUtility.FromJson` 在 **Warudo 播放器**里对 `List<嵌套类>` 的字段会静默丢弃
（同一个类在编辑器里是好的，所以"编辑器里试过"完全不能作证）。
三次踩的是同一个模式：`List<VTSBlendShapeEntry>`、`List<HoFaceOutput>`。

怎么办：**我们自己的 JSON 只在 `HoJsonReader` 上写**（手写状态机），三份实现：
`Runtime/FaceTracking/HoJson.cs`（读取器，文件头记着这三次）、`HoVtsPacket.cs`（手机包）、
`HoFaceProfileJson.cs`（配置文件）。判据：离线台架 `.research/profile-json-test`（87 条）+
`Tests~/FaceTrackingValidation.cs`。**新加任何要落盘/来自网络的结构，都不许再碰 `JsonUtility`。**

## 2. 两个时钟：包到了、合并里也有值，**通道就是不写**

症状：手机明明在发，面板「参数输入」里裸值在动、包统计在涨，但脸不动。
现场（`HO_LIVE` 日志，第 120 帧）：

```
connected=True running=True port=49986 packets=121 invalid=0 rejected=0
hasJawWire=True hubJaw=0.9 inputRows=128 jawRow=35 rowExpr='JawOpen' weight=17
```

`17` 是**基础动画**的值 —— 说明我们一个字节都没写。链路上每一步都是好的，只在最后一步断的。

原因：会话判"这一包新不新鲜"算的是 `会话的现在 − 包上的时间戳`，而这两个数来自**两个不同的时钟**：
会话读 `EditorApplication.timeSinceStartup`，接收端在**后台线程**给包打时间戳时读 `Stopwatch`。
两者差一个**恒定偏移**，差值就永远越界（`fresh` 为假 → 按"不新鲜"处理 → 不写）。

**第二个坑藏在修法里**：把会话那边也改成 `timeSinceStartup` 看似"统一"了，但那个属性
**只能在主线程读** —— 接收线程一读就抛
`get_timeSinceStartup can only be called from the main thread`，线程当场退出。
症状会从"包有但不写"变成**"连不上"**（`packets=0`、`running=False`），方向完全被带偏。

怎么办：只有**一个实现** `Editor/FaceTracking/HoFaceClock.cs`（`Stopwatch`：任何线程可读、单调、
域重载不影响），`HoFaceReceiverBase.Now` 与 `HoFaceInputHub.Now` 都只是别名。
排查入口就是 `HO_LIVE` 那条日志：`packets=0` 看 socket/端口，`hasJawWire=False` 看协议解析，
`hubJaw` 有值而 `weight` 不动就看时钟。

## 3. 指定了配置文件，线名 → 规范名**只认它的输入行**

症状：手动值能驱动模型（说明影子台、绑定、写回都是好的），**实时输入完全不动**；
或者"换了一份配置之后实时那条链就失效了"。

原因：`HoFaceDebugSettings.Inputs()` 的语义是
`有配置文件 ? 配置文件里的 inputs : 内置默认表`。配置文件里没有 `inputs` 字段就是**空表** ——
一条输入行都没有，通道层自然拿不到任何值。
（内置默认表只在"**还没指定配置文件**"时生效，这是刻意的：不做隐式兜底。）

怎么办：配置里把要用的线名写全，而且**VTS 手机的线名是 PascalCase**（`JawOpen`、`EyeBlinkLeft`），
不是规范名的拼法。用例里有一条断言专门守这件事（"输入行也是从配置文件读的"）。
**这不是 bug，是设计** —— 遇到它先怀疑配置，不要怀疑时钟（那看第 2 条）。

## 4. 在 `OnEnable` 里存 `ScriptableSingleton`

症状：日志里出现 `You may not pass in objects that are already persistent`，
而且**那次清理没有落盘**（下次打开还是旧数据）。

原因：`HoFaceInputEnvironment.OnEnable` 是 `CreateAndLoad()` 反序列化路径上的一环，
那一刻资产已经持久化了，`Save(true)` 会被 Unity 拒收。

怎么办：推迟一帧 —— `EditorApplication.delayCall += () => Save(true);`。
内存里那份列表本来就是干净的，晚一帧写效果一样。

## 5. VTS 手机是**请求式**的，不是推送

症状：手机 App 开着、"3rd Party PC Clients"也开了、IP 也对，就是**一个包都收不到**。

原因：手机只在**收到请求之后**才回数据，而且回的是**请求包的源 IP** + 请求里 `ports` 指定的端口；
手机端界面上**没有"目标地址"可填**。请求有租期（官方允许 0.5–10 秒），所以必须周期续。

怎么办：看面板「排查 → 包统计」里的 **`请求 N`** 在不在涨 —— 涨说明我们在正常续约，
问题在回程（防火墙 / 手机与电脑不同网段 / Wi-Fi 被蜂窝或 VPN 分流）；不涨说明我们没发出去。
判据：`HoVtsPacket.BuildRequest`（原文）与 `VtsIphoneReceiver.OnTick`（每约 1 秒续 5 秒）。

## 6. 丢追踪时手机**还在发**包，只是少了那 52 个键

症状：把"有没有在追"判成"收到包了没有"，于是丢脸时把上一帧的表情**冻住**（而正确的行为是回中性）。

原因：丢追踪时手机仍然发那 15 个标量（时间戳 / 热键 / `FaceFound` / 头姿 / 双眼），
**只有 `BlendShapes` 消失**，所以键数在 `65 ↔ 15` 之间跳。

怎么办：`IsTracked` 跟 **`FaceFound`**，不要跟"包里有东西"。
判据：本机实测的键数（65 / 15），见 `Runtime/FaceTracking/HoVtsPacket.cs`（`FaceFoundKey`）
与 Warudo 侧中间层节点的 `IsTracked` 说明（那份代码在 mod 仓库，不在这个仓库里）。
