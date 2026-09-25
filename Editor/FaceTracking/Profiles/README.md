# 配置层（Profiles）

这里放**发货用的中间层配置**（`*.hoface.json`），当成一个仓库用。

> ⚠️ **"输出侧"是过渡件。** 终点是**动画 = 状态**（一份动画表达一个状态，动画之间没有标准命名，
> 哪些轴成 2D 混合树由作者按模型决定），不是 ARKit 形态键。现在输出行里那 52 条**裸规范名**
> （`eyeBlinkLeft`、`jawOpen`…）只是为了在形态键做完之前能拿现成模型把整条链跑通。
> 等动画做完，**输出行换成"规范名 → 作者参数名"**，
> 两层之间的接口不变（还是一份 `Dictionary<string,float>`）。详见
> [面捕工作流 §2.1](../../docs/FACE_TRACKING_WORKFLOW.md)。
>
> **输入侧（线名 → 规范名）是长期资产**，不会随这个变化作废。

## 现在有什么

| 文件 | 是什么 |
|---|---|
| `ho-iPhoneVTS.hoface.json` | **正式中间层**（这一层现在只有它）：67 条输入行 + **90 条出口**（原始 ARKit 52 + 官方 VTS 20 + VB 自造 5 + 姿态向量 12 + `FaceFound`）。公式机械取自本机 VBridger，见下 |
| `ho-debug-iphoneVTS.hoface.json` | **iPhone 上的 VTS** 的**纯直通参考**：67 输入 / 67 输出，零改名、零曲线 |
| `ho-debug-androidVTS.hoface.json` | **安卓手机上的 VTS** 的**纯直通参考**：65 输入 / 65 输出，同上 |

**"直通参考"和"中间层"是两件事**，别混：`ho-debug-*` 只负责"原样记录这台设备发了什么"
（`parameter = expression = 线名`，两层曲线恒等），它是**实测表**的落盘形式，职责只有记录；
`ho-iPhoneVTS` 才是**真在加工**的那一层（改名 / 表达式 / 曲线）。

⚠️ **`ho-debug-*` 不能当能跑的配置用**。中间层是靠**输入行的 `parameter`** 去
`HoFaceTrackingChannels.IndexOf` 解析通道的，而那个索引表是 **88 条**（52 个规范名 +
`Left`/`Right` 结尾者的 `_L`/`_R` 别名），比较是 `StringComparer.Ordinal`（**逐字**）。
实测（走真的 `IndexOf`）：

| 配置 | 输入行 | 解析到的通道 | 为什么 |
|---|---|---|---|
| `ho-iPhoneVTS` | 67 | **52 / 52** ✅ | 形状行的 `parameter` 就是规范名 |
| `ho-debug-iphoneVTS` | 67 | **0 / 52** | 线名是 PascalCase `JawOpen`，规范名与别名都是 camelCase |
| `ho-debug-androidVTS` | 65 | **40 / 52** | 小驼峰 + `_L/_R` 命中别名表；剩 12 个它不发 |

⚠️ **核这类事别用 PowerShell**：它的 `-contains`/`-eq`/`-match` 默认**忽略大小写**，会把
iPhone 那份假报成 52/52；而"只比 52 个规范名"又会漏掉别名表、把安卓报成 12/52。两个坑都踩过。

它同时是 **Unity 那套验证用例的夹具**（`Tests~/FaceTrackingValidation.cs` 的 `PrepareValidationProfile`）：
读它、原样写成验证工程里的 `Assets/ValidationProfile.hoface.json`。所以**改它的输入行会连带影响那套用例**——
夹具会当场查 `jawOpen` 那一路在不在（行为用例靠它串"包 → 参数"整条链），缺了就直接抛。

设备那两份由 `gen-default-profile.ps1` 生成，名字全部取自实测**真值**（**不手抄、不按命名规则推断**）：

```powershell
# 设备那两份（纯直通参考预设）的内容 = PARAMETER_DEVICE_VERIFICATION.md 里对应表的 wire 列。
# 改线名就改那份文档的实测表，然后：
& .warudo-mod-research\.tools\gen-verif-tables.ps1       # 刷新文档里的生成表

# 正式中间层：公式从本机 VBridger 机械提取，不手抄
& .warudo-mod-research\.tools\gen-iphone-vts-profile.ps1
```

⚠️ **设备那两份的线名是实测抄下来的**（`docs/PARAMETER_DEVICE_VERIFICATION.md` 的 `wire` 列），
**不能推断**：第一版安卓配置照 iFacialMocap 的 `_L/_R` 规则拼出 `browInnerUp`，
而设备发的是 `browInnerUp_L/R` ⇒ 那一格永远没数据，另有 12 行指向设备**根本不发**的键。
**这类错误不报错、只是静默不动**，所以线名必须来自实测。
**一份设备一份**：两台设备的名字集合不同（安卓 65 / 苹果 67 个键），互相套用就会静默失效。

⚠️ 生成器是**纯 ASCII** 的（Windows PowerShell 5.1 把无 BOM 的 `.ps1` 当 ANSI 读，脚本里放中文会炸），
所以中文 `notes` 从命令行 `-Notes` 传进去。

## `ho-iPhoneVTS` 的口径（正式中间层）

出口是**两份并行 + 一套额外**，共 **90 行**：

| 组 | 行数 | 出口名 | 是什么 |
|---|---|---|---|
| **G1 原始 ARKit 52** | 52 | **裸规范名**：`eyeBlinkLeft`、`jawOpen`… | **无损直通**。控制器要细节（某个具体形变、自己画轴）就读这组 |
| **G2 官方 VTS 追踪参数** | 20 | `MouthOpen`、`FaceAngleX`、`Brows`… | **合成**出来的语义轴（**有损**，见下） |
| **G3a VB 自造参数** | 5 | `MouthFunnel`、`MouthPucker`、`MouthShrug`、`MouthPressLipOpen`、`BrowInnerUp` | VTS 词表没有的概念，喂 VTS 要注册 |
| **G3b 姿态向量** | 12 | `Face/Angle/*`、`Face/Pos/*`、`Body/Angle/*`、`Body/Pos/*` | 4 组 × XYZ |
| **G3c 协议层信号** | 1 | `FaceFound` | 丢追动画用 |

**为什么 G1 和 G2 要同时出**：G2 是**故意有损**的——`MouthOpen` 把
`mouthClose`/`mouthRoll*`/`mouthFunnel` 折成一个数、`MouthSmile` 把
`mouthFrown*`/`mouthPucker`/`mouthDimple*` 折成一个数，因为 VTS 没有那些参数。
**要那份细节就只能读 G1。** 这也是 VBridger 自己的做法：它的 VMC 发送循环遍历整个
`outputValues`（里面躺着全部原始形态键名），所以原始量是**裸名**发出去的，合成行只是再往里加键。

⚠️ **G1 不加前缀**。曾经写成 `ARKit/<规范名>`，理由是"要个命名空间跟合成组分开"——错两层：
VB 不这么干，而且**加了前缀就谁都读不到**（控制器只写自己参数表里声明过的名字，
`ARKit/eyeBlinkLeft` 与任何声明都对不上，52 行白算）。

**只有姿态向量带命名空间**（`Face/`·`Body/`），因为那两个真的会撞：
`FaceAngleX` 是官方标量（`±90` 不缩放），`Face/Angle/X` 是 VB 向量分量（`±30` 带 `0.66` 阻尼）。
⚠️ `Head/*` 是**已删的 `ho-vts-default`** 的保留名，别再用。

**大小写不用管**：全部字典都是 `StringComparer.Ordinal`（大小写敏感），所以出口里有 5 对只差
大小写的名字（`cheekPuff`/`CheekPuff`、`mouthFunnel`/`MouthFunnel`、`mouthPucker`/`MouthPucker`、
`browInnerUp`/`BrowInnerUp`、`tongueOut`/`TongueOut`）**是两个不同的键、两个不同的参数**，
程序里区分得开。两套并存是故意的（原始裸名 + VB 的合成名），改名就等于发明 VTS 词表里没有的名字。

**不出的**：`MousePositionX/Y`（没有鼠标）、`Voice*` / `VoiceA..O`（这条链上没有麦克风）、
`FaceAngry`（官方标 EXPERIMENTAL、且没有对应的 ARKit 形态量）；
以及 `Eye_Squint_L/R`、`BrowL/R`、`EyeOpen`、`EyeX/Y` ——
它们是**已有参数换拼写或换聚合**（同一个值写两个名字），信息在 G1/G2 里都有。

逐参数的公式、值域、来源见 [HO 参数规范](../../../docs/PARAMETER_HO.md)。

⚠️ **VBridger 自己的那批自定义出口一个都不抄**：`MouthFunnel`、`MouthPucker`、`MouthShrug`、
`Eye_Squint_L/R`、`MouthPressLipOpen`、`BrowInnerUp` 都是 VB 私有名（要靠
`ParameterCreationRequest` 注册），**不是 VTS 标准**。别看到 VB 预设里有就以为是规范。

### 公式从哪来（不手抄）

`.research/vbridger/extract_vts_formulas.ps1` 从本机安装的 10 份已解密预设里按**出口名**提出全部候选式。
**选公式按"哪份预设"，不按"出口名"**——VB V3.0 的 `EyeRightX` 是用 `eyeLookIn_L`/`eyeLookOut_L`
算的、`EyeRightY` 还加了 `browOuterUp_L`，**名字根本不描述来源**。每条出口的 `notes` 里都记了用的是哪份。

一处例外：`EyeLeftX/Y` 与 `EyeRightX/Y` **不用 VB 的合成**——iPhone 本来就在发
`EyeLeft_x/y/z` / `EyeRight_x/y/z` 原始眼球标量，直接引设备值。

### 还没标定的地方（**别当它已经对了**）

* `FaceAngle*` / `FacePosition*` 的正负号与轴向是 **VBridger 的**（`-headRotY`、`headPosX*-1`），
  属于**照抄结构**——它吃的那两根设备轴（`Rotation_*` / `Position_*`）**还没在实机上核过**。
* 头位**单位未标定**（手机原始值直通）。
* 两者都在**你自己的副本**上修：配置文件窗口里改表达式，或翻转符号后看实时读数。

曲线：每条出口都写了自己的曲线，**纵轴是参数值不是百分数**（写成 `0..100` 会把权重放大 100 倍）。
`Brows` / `MouthOpen` / `MouthSmile` / `EyeOpen*` / `Brow*Y` 是静息 0.5 的**合成量**、能出 `[0,1]`，
所以它们的曲线**故意比 0..1 宽**——用默认 0..1 会把合法值**静默夹掉**。

## 两台设备的**实测**方言（2026-09-25，各一次完整 dump）

| 设备 | 键数 | 形状那半边 | 标量那半边 |
|---|---|---|---|
| **iPhone VTS** | 67 | **干净的 VTS PascalCase**：`JawOpen` / `MouthSmileLeft`… —— 52 个形状**全部到位、0 个对不上** | `Rotation_x`… / `Position_x`… / `EyeLeft_x`… / `FaceFound` / `Hotkey` / `Timestamp` |
| **安卓 VTS** | 65 | 形状主体是 **iFacialMocap 小驼峰 + `_L/_R`**（`jawOpen` / `mouthSmile_L`），但**内眉/外眉带后缀**（`browInnerUp_L/R`）、**眨眼两种拼写都发** ⇒ 只有 **44 / 52** 个规范名有源 | 同上（**两台同名**） |

⚠️ **安卓那条"眨眼两种都发"是有来历的**：最早那次调试里"只有头系和眨眼在动"，
真因就是别的形状行**没有数据**、而眨眼恰好有 PascalCase 那对在喂。
所以安卓那份**刻意给眨眼两行**（指向同一条通道，不存在互相顶掉的问题）。

核对用的工具：`.warudo-mod-research\.tools\gen-verif-tables.ps1`（把设备统计表从
`analyze-dump-stats.ps1` 的输出刷进 `PARAMETER_DEVICE_VERIFICATION.md`）。

## 怎么用（⚠️ Warudo 读不到这个包）

1. 把要用的那份**复制到 Warudo 的插件沙箱**：`Warudo_Data/StreamingAssets/Plugins/Data/hollow.hofacetracking/`。
   在 Warudo 里就是「HoFace参数处理」节点上的 **`打开文件夹`** 按钮，或 `状态` 口里那个「沙箱：」路径。
2. 在节点的 `配置文件` 下拉里选它（下拉列的就是沙箱里的 `*.hoface.json`）。
3. 改也在那边的副本上改 —— 改这个包里的文件**不会**影响 Warudo（配置按文件时间戳失效重读，存盘一秒内生效）。

## 一份文件 = **一台设备**

⚠️ **同名多行 = 最后一行生效（覆盖/优先级），而且缺数据时不回退**（那一格就是 0）。
所以**两种通道**混在一份文件里时，后声明的那行没数据会把有数据的那行顶掉 ⇒ 那一格恒 0，
而且症状极具误导性（我们为此查过一整轮）。**换设备就换文件。**

（唯一允许"同一条通道写两行"的情况是安卓那对眨眼：两行读的是**同一个通道**，不存在顶掉。）

⚠️ 头/眼那 12 行必须带自己的**宽曲线**（度数与位置会被默认 0..1 夹成 0/1；
症状是"头姿恒 `(0,0,0)°` 而形态键一切正常"）。头位单位**未标定**。

## 这些配置**不是**校准过的

`ho-debug-*` 只做原样记录，`ho-iPhoneVTS` 只做**合成**，两份都**没有**曲线校准、没有滤波：

* VBridger 自己的输入曲线仓（`.research/vbridger/decrypted/InputCurvesBck...`）是 **68 条全直通**
  （0→0 / 1→1，min/max 0..1），里面没有可搬的校准；
* VBridger 预设里那些增益/曲线是给它**自己的通道集**调的，不是给 ARKit 直通用的；
* 按 [参数空间文档](../../docs/VTS_FACE_PARAMETER_SPACES.md)：表达式值、存档默认值、模型最终中性
  **不必相等**，所以校准是**每演员/每设备**的活，属于你自己那份副本。
* 起点（VBridger AdvancedARKit V3 的实测滤波时间，秒）写在各文件的 `notes` 里。

## 改了生成器或配置之后

```powershell
dotnet run --project .research\profile-json-test
```

这条会用**我们自己的解析器**（不是肉眼看 JSON）验这三份文件：

* **发货中间层**（`ho-iPhoneVTS`）：与 `docs/VTS_HIGH_QUALITY_FACE_CATALOG.json` 交叉核对 ——
  输入行里 52 个形状行的 `parameter` 必须是规范名（写成设备线名就**一个通道都解析不到**）、
  非通道的 15 行必须不改名、正好 67 条输入；出口 52 条原始量用**裸名**且恒等直通、
  没有 `Head/*`、向量命名空间正好 12 行、每条表达式能被解析、引用的变量都有来源、
  严格无重复、静息 0.5 的合成量没有用 0..1 曲线；
* **设备配置**：**零改名**（`parameter` 必须等于 `expression`）、出口键与输入线名逐一对应、
  无 `ARKit/` 前缀与 `Head/*`、**进出口两层曲线都恒等**（不恒等会把值静默夹掉）；
* **规范文档**（`docs/PARAMETER_HO.md`）：按组核对 52/20/5/12/1，且 §0 声明的数字与实际一致。

⚠️ 最值钱的两条：
① "输入行的 `parameter` 必须是规范名" —— 写成设备线名时 52 个通道**一个都解析不到**，
   中间层静默不产出任何形状参数（真发生过）；
② "出口引用的变量都要有来源" —— 名字错一个那一段恒为 0 且毫无提示。

它是"改了生成器之后忘了重新生成"的唯一守门人。当前：**142 passed / 0 failed**。
