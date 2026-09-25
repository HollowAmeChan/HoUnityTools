# 配置层（Profiles）

这里放**发货用的中间层配置**（`*.hoface.json`），当成一个仓库用。

> ⚠️ **"输出侧"是过渡件。** 终点是**动画 = 状态**（一份动画表达一个状态，动画之间没有标准命名，
> 哪些轴成 2D 混合树由作者按模型决定），不是 ARKit 形态键。现在输出行写的是 52 个 `ARKit/<规范名>`，
> 只是为了在形态键做完之前能拿现成模型把整条链跑通。等动画做完，**输出行换成"规范名 → 作者参数名"**，
> 两层之间的接口不变（还是一份 `Dictionary<string,float>`）。详见
> [面捕工作流 §2.1](../../docs/FACE_TRACKING_WORKFLOW.md)。
>
> **输入侧（线名 → 规范名）是长期资产**，不会随这个变化作废。

## 现在有什么

| 文件 | 是什么 |
|---|---|
| `ho-iPhoneVTS.hoface.json` | **正式中间层**（这一层现在只有它）：67 条 iPhone 线名 + 21 条出口。出口**不是直通**，是**从 ARKit 形态量合成 VTS 官方追踪参数**（`FaceAngleX` / `MouthOpen` / `Brows`…）。公式机械取自本机 VBridger，见下 |
| `ho-debug-iphoneVTS.hoface.json` | **iPhone 上的 VTS** 的**纯直通参考**：67 输入 / 67 输出，零改名、零曲线 |
| `ho-debug-androidVTS.hoface.json` | **安卓手机上的 VTS** 的**纯直通参考**：65 输入 / 65 输出，同上 |

**"直通参考"和"中间层"是两件事**，别混：`ho-debug-*` 只负责"原样记录这台设备发了什么"
（`parameter = expression = 线名`，两层曲线恒等），它是**实测表**的落盘形式，职责只有记录；
`ho-iPhoneVTS` 才是**真在加工**的那一层（改名 / 表达式 / 曲线）。

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

| 层 | 做什么 | 为什么 |
|---|---|---|
| **输入行**（67） | `parameter = expression = 设备线名`，**零改名** | 这一份的**规范名词表就是设备线名词表**。再发明一套名字＝加一层里面什么都没有的映射 |
| **出口**（21） | **ARKit 形态量 → VTS 官方追踪参数** | 出口左值是**控制器参数名**。控制器不保证做 ARKit 语义，所以不写 `ARKit/*`；VTS 追踪参数是**外部有文档的标准**（[参数标准表 §1.1](../../../docs/PARAMETER_STANDARDS.md)），跨模型稳定 |

出口那 21 条 = 官方追踪参数里**我们喂得动**的那 22 个面/鼠标/音频参数去掉鼠标的：

* 头姿 6：`FaceAngleX/Y/Z`、`FacePositionX/Y/Z`
* 眼睑 2：`EyeOpenLeft/Right` ｜ 注视 4：`EyeLeftX/Y`、`EyeRightX/Y`
* 眉 3：`Brows`、`BrowLeftY`、`BrowRightY`
* 嘴 4 + 其它 2：`MouthOpen`、`MouthSmile`、`MouthX`、`TongueOut`、`CheekPuff`、`FaceFound`

**不写的**：`MousePositionX/Y`（没有鼠标）、`Voice*` / `VoiceA..O`（这条链上没有麦克风）、
`FaceAngry`（官方标 EXPERIMENTAL、且没有对应的 ARKit 形态量）。

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

这条会用**我们自己的解析器**（不是肉眼看 JSON）验这四份文件：

* 默认配置：52 个规范名与三种方言拼写是否都在、输出行有没有重复、`Head/*` 宽曲线是否真的透明；
* 设备配置：**零改名**（`parameter` 必须等于 `expression`）、出口键与输入线名逐一对应、
  **没有 `ARKit/` 前缀与 `Head/*` 保留名**、**进出口两层曲线都恒等**（不恒等会把值静默夹掉）；
* 正式中间层（`ho-iPhoneVTS`）：输入行**零改名且正好 67 条**、每条出口表达式**能被我们的解析器读**、
  出口引用的**每个变量都是设备真发的线名**、出口无重复、曲线是恒等直线、
  **静息 0.5 的合成量没有用 0..1 曲线**、出口不写 `ARKit/*` 或 `Head/*`。

⚠️ 那条"出口引用的变量必须是真发线名"是这批断言里最值钱的：写成 ARKit 小驼峰（`jawOpen`）
而设备发 PascalCase（`JawOpen`）时，那一段**恒为 0 且毫无提示**——正是我们踩过一次的坑。

它是"改了生成器之后忘了重新生成"的唯一守门人。当前：**120 passed / 0 failed**。
