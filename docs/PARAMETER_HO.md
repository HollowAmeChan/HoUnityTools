# HO 参数规范（我们的中间层选什么）

> 这份是**我们自己的**目标词表的唯一权威表。跟 [参数标准表](PARAMETER_STANDARDS.md) 的分工：
> 那份记**外部标准怎么定**（VTS / ARKit / VMC / VRM / VRCFT 各自的规定，逐条带官方 URL），
> 这份记**我们选什么**——参数叫什么、什么意思、值域多少、从哪个源算出来、用的是谁家公式。
>
> 词表来源是三样东西的差集与并集，全部实测/实读，不靠命名规则推断：
> ① VTS 官方追踪参数 98 条（`docs/VTS_HIGH_QUALITY_FACE_CATALOG.json` 的 `native` 段）；
> ② VBridger 10 份已解密预设里出现的 92 个出口名（`.research/vbridger/decrypted/`）；
> ③ iPhone VTS 实测发来的 67 条线（[设备验证表 §2](PARAMETER_DEVICE_VERIFICATION.md)）。

日期：2026-09-26

> ⚠️ **这份规范落在两个仓库上 —— 改一处必须两边都验，一边绿了不算验过**（§0.0）。

## 0. 一句话与范围

### 0.0 ⚠️ 跨仓：这套东西落在**两个**仓库上，改一处必须两边都验

**这条链不是单仓功能。** 包定词表与主本，mod 是同一套逻辑的运行期；两边各有一个验证口。

| 仓库 | 是什么 | 承载这一系列的哪一层 |
|---|---|---|
| **包** `D:\Unity_Fork\HoUnityTools`（分支 `master`） | Unity 包 `com.hollow.hounitytools`。**主本在这儿** | 本文整份规范；`Runtime/FaceTracking/`（求值器 / 中间层 / 通道名 / 动态参数 Hub）；`Editor/FaceTracking/`（面板 / 会话 / `Profiles/*.hoface.json`）；`Tests~/` |
| **mod** `D:\Unity_Project\BreakWarudo\Assets\HoWarudoModTests`（**它自己的仓库**，分支 `main`） | Warudo mod（Mod Tool 0.14.4.8 / Unity 2021.3.45f2） | `Mods-Ho/HoFaceTracking/`：接收器 / 参数层 / 控制器 / 节点 / 配置 store；**外加从包里搬过去的 9 份 `Core/` 副本** |

**为什么只能搬、不能引用**：两边是**两个 Unity 工程、两个程序集**，工程之间不能互相引用源码；
而"Unity 面板里预览到什么，Warudo 里就输出什么"要求求值器**是同一份** ⇒ 只能搬 + 同步，
代价就是"两边不一致"这个失败模式。清单与同步办法：mod 侧 `Mods-Ho/HoFaceTracking/Core/PORTED.md`。
⚠️ **"两边各写一份"是本项目的既定做法，而且不止这一处**（两边都要存在的类型、角色预制件上的资产…）——
所以别为某一处单独找"少写一份"的路子（不引用包侧、也不开子 asmdef），靠**清单 + 同步**保证不漂。

| 改了哪一层 | 包侧怎么验 | mod 侧怎么验 |
|---|---|---|
| `Runtime/FaceTracking/` 里**搬过去的那 11 份** | `dotnet run --project .research/profile-json-test`（154 条）+ `.research/expression-coverage`（19 条）+ `.research/compile-check-pkg.ps1`（整包三遍：Runtime 玩家视图 / Runtime+Editor / `Tests~`） | **先跑 `.research/sync-modcore.ps1`**，再 `Assets/HoWarudoModTests/tools/compile-check.ps1`；最后**人要真构建一次**（FastBuild → `.warudo`） |
| 只动编辑器面板 / 会话 / 配置 | 批处理 Unity 用例（成功标记 `HO_FACE_TESTS_ALL_PASSED`，见 [批处理验证](pitfalls/VALIDATION_LOOP.md)）+ `.warudo-mod-research/.tools/compile-check-editor.ps1` | 不涉及；但改了 `*.hoface.json` 要重导进沙箱（节点上的 `重读配置`） |
| 只动 mod 的节点 / 接收器 / 控制器 | 不涉及（但别把包侧的公共类型改名，会连带崩 mod） | `tools/compile-check.ps1` —— **它的 UMod 沙箱 lint 只有 mod 侧有**：Roslyn 放过 `System.Reflection` / `System.IO`，UMod 的 `RunCodeValidation` 会让你**真构建失败** |

**两条会静默出错的跨仓规矩**（同 [仓库与提交](pitfalls/REPO_AND_GIT.md) §7）：

1. 改了包侧那 11 份却**没跑同步** ⇒ mod 侧还是旧语义，**而且编译照样过**；表现是"Warudo 里和面板里不一样"。
2. 直接在 mod 的 `Core/` 副本里改 ⇒ 下次同步**无声覆盖**（那 6 行文件头就是提醒）。

⚠️ `.research/` 与 `.warudo-mod-research/` 都**不入库**：上面这些脚本只有本机有，
**别把脚本当成唯一出处** —— 清单以 mod 侧 `Core/PORTED.md` §1 那张表为准。

**这份规范覆盖"我们这条链上出去什么"**：出口是**两份并行 + 一套额外**，共 **90 行**。

| 组 | 行数 | 在哪 | 是什么 |
|---|---|---|---|
| **G1 原始 ARKit 52** | **52** | §3.1 | 无损直通（信息上界） |
| **G2 官方 VTS 追踪参数** | **20** | §3.2 | 合成出来的语义轴 |
| **G3a VB 自造参数** | **5** | §3.3 | VTS 词表没有的概念，要注册 |
| **G3b 姿态向量** | **12** | §3.4 | 4 组 × XYZ |
| **G3c 协议层信号** | **1** | §3.5 | `FaceFound` |
| | **90** | | **出口行总数** |

**还算不出来**（输入契约里没有源）：**11 个**（9 行），逐条列在 §5。

⚠️ 最要紧的一条：**"合成量"不是"这一层对脸的理解"，是"给 VTS 用的折中"。**
`MouthOpen` 把 `mouthClose`/`mouthRoll*`/`mouthFunnel` 折成一个数、`MouthSmile` 把
`mouthFrown*`/`mouthPucker`/`mouthDimple*` 折成一个数——**有损，而且故意有损**。
要细节就读 G1。只出 G2 等于在中间层就把信息扔了。

**不进这份规范的**（有意排除，不是漏了）：

| 排除项 | 为什么 |
|---|---|
| 手部 26 条、手柄 39 条 | 跟面捕无关，且手机不产生 |
| VMC 骨骼向量 `Head` / `Neck` / `LeftEye` / `RightEye` | 那是 `/VMC/Ext/Bone/Pos` 路径的骨骼，不是参数；我们这条链是"参数进参数出"。身体姿态我们改用 G3b 的 `Body/*` 表达 |
| `Eye_Squint_L/R`、`BrowL/R`、`EyeOpen`、`EyeX/Y` | **不是不要**，是它们与已有参数**重复**（换拼写或换聚合方式，同一个值写两个名字）。见 §3.3 的说明 |
| `Visemes`、`Voice*`、`MousePosition*`、`FaceAngry` | 输入契约里没有源。见 §5 |

---

## 1. 输入契约：设备能发什么

这一节是整份规范的**地基**——所有公式只能吃这里有的东西。

### 1.1 iPhone VTS（当前唯一接的设备）

67 条线，实测（[设备验证表 §2](PARAMETER_DEVICE_VERIFICATION.md) 的生成表，203 帧）：

| 半边 | 条数 | 内容 |
|---|---|---|
| **52 个 ARKit 形态量** | 52 | 干净 PascalCase：`JawOpen` / `MouthSmileLeft` / `EyeBlinkLeft`…**52 个全部到位、0 个对不上** |
| **头姿** | 6 | `Rotation_x/y/z`（度）、`Position_x/y/z`（单位未标定） |
| **眼球** | 6 | `EyeLeft_x/y/z`、`EyeRight_x/y/z`（度） |
| **其它** | 3 | `FaceFound`(0/1)、`Hotkey`、`Timestamp` |

**"VTS 苹果侧能不能表达 ARKit 的全部语义"——能，这是实测的，不是推断。**
iPhone 那 203 帧里 52 个形态量**全部 MOVES**，逐个对应 ARKit 52 名单。

⚠️ **但这是分设备的**：安卓那半边只有 60 个 MOVES，`EyeLeft_z`/`EyeRight_z` 恒 0、
`mouthShrugUpper` 只在噪声底。所以"能不能表达"必须**按设备分别确认**。
⚠️ **`MOVES` ≠ 好触发**：`TongueOut` 97.0% 帧接近 0、`EyeLookOutRight` 96.6%、
`EyeLookUp*` 93.6%、`BrowOuterUp*` 93.6%——它们**能动**，但要**明显做动作**才动。
别看到 `MOVES` 就当它随手可得。

### 1.2 设备**不**提供的东西（决定了 §5 那 11 个为什么算不出来）

| 缺什么 | 影响的参数 |
|---|---|
| 麦克风 / 音频（VTS 手机的 tracking 包不带音频，也没有 OVRLipSync） | `VoiceVolume`、`VoiceFrequency`、`VoiceVolumePlusMouthOpen`、`VoiceFrequencyPlusMouthSmile`、`VoiceA/I/U/E/O`、`VoiceSilence`、`Visemes` |
| 鼠标 / 触摸 | `MousePositionX`、`MousePositionY` |
| 生气脸的专用分类器 | `FaceAngry` |

### 1.3 能力边界：这一层**只吃 VTS 输入**

把中间层当一台机器看，它的规格是：

| | |
|---|---|
| **输入** | **只有一条**：VTS 手机协议（`iOSTrackingDataRequest` 那个 JSON） |
| **能吃到的字段** | 52 个形态键 + `Rotation_*` / `Position_*` / `EyeLeft_*` / `EyeRight_*` + `FaceFound` / `Hotkey` / `Timestamp` = **67 条线** |
| **不会做的事** | 不读麦克风、不读鼠标、不读键盘、不自己算音素、不自己采样音频 |
| **输出** | 一份 `Dictionary<string,float>`（§3 那 90 行，控制器声明什么就写什么） |

**所以凡是"需要 VTS 协议之外的输入"的东西，这一层都给不出来**：

| 类别 | 缺什么 | 本规范的处置 |
|---|---|---|
| 15 个音素权重 + 15 个硬判（`viseme_*` / `viseme_*_abs`） | **声学/视觉音素识别**（VB 用的是它自己进程内的 OVRLipSync + 麦克风，见下 §1.5） | ❌ 没有源 |
| `volume` | 麦克风采样 | ❌ 没有源 |
| `Voice*`（`VoiceA/I/U/E/O`、`VoiceSilence`、`VoiceVolume`、`VoiceFrequency` 及各 `Plus` 变体） | 麦克风 + uLipSync（VTS 自己算，不通过手机协议外发） | ❌ 没有源 |
| `MousePositionX/Y` | 鼠标 / 触摸 | ❌ 没有源 |
| `Hotkey` | 手机的屏幕热键 | ⚠️ **有源但恒为 −1**（实测：安卓 173 帧日志里只出现过 `-1`；你手机上的三个快捷键不输出） |

**这是有意的边界，不是暂时没做。** 要接麦克风就是**新增一条输入**（另一个接收端 + 新的线名），
而不是在这一层里塞音频代码 —— 那会毁掉"换个协议只改配置"这条性质。

### 1.4 为什么 VB 有音素输入（而 VTS 开了麦克风口型也不出新参数）

**VB 的音素是它自己算的，跟 VTS 无关。** 本机 `Assembly-CSharp.decompiled.cs`：

* 组件 `LipSyncContextTextureFlip` 持有 **`OVRLipSyncContextBase`** 与 **`uLipSyncMicrophone`**
  （L4776-L4779），`Update()` 里 `lipsyncContext.GetCurrentPhonemeFrame()` 拿 15 个音素权重，
  再按 `smoothAmount` 做 EMA（L4806-L4814）。
* `SetVisemeToParams()`（L4855-4885）把它们写进**表达式变量**：

```
volume      = inputCurves["volume"].curve( 10 帧滑动平均的采样 )      ← 自己算音量
viseme_XX   = inputCurves["viseme_XX"].curve( oldFrame.Visemes[i] )   ← 15 个权重，各自过输入曲线
viseme_XX_abs = 1.0 / 0.0                                             ← 15 个硬判（argmax 那一个为 1）
```

所以 VB 那 100 个输入变量里有 **31 个（15+15+1）是它自己的麦克风管线产出的**，
不是从哪个追踪协议收来的。**VB 既是中间层，也是音频前端。**

**为什么 VTS 开了麦克风口型之后"没有新参数"** —— 这是**设计如此**，官方文档写得很直白
（`vts-lipsync.md` L44、L88-L99）：

> The lipsync system outputs the following voice tracking parameters. You can use them as inputs
> for **ANY** Live2D parameter, not just the mouth parameters.
>
> `ParamMouthOpen` and `ParamMouthForm` are set up just like usual. In addition, set up a
> `ParamSilence`. This parameter will later be hooked up to `VoiceSilence`, so it will be `1`
> when the microphone detects no or almost no sound.

拆开说，三件事各归各位：

| 你以为会变的 | 实际 | 为什么 |
|---|---|---|
| API 参数**列表** | **不变** | `VoiceA/I/U/E/O`、`VoiceSilence`、`VoiceVolume`… 是**固定内置清单**，本来就在（没麦克风时恒 0）。加了麦克风只是**有了数据**，不是**多了名字** |
| API 参数**值** | 变了 | 开麦后由 VTS 内部的 uLipSync 产出 0..1 的元音/静音/音量 |
| 模型内部**怎么接** | **这才是你看到的那件事** | 官方推荐做法是把 `VoiceSilence` 接到 `ParamSilence`，由它决定"有声走元音、无声走面捕"——**改的是模型参数的映射，不是 API 的载荷** |

补一条你观察到的现象：**手机侧开麦克风口型也不会往协议里加字段**。
官方手机协议的载荷只有 `Timestamp` / `Hotkey` / `FaceFound` / `BlendShapes[]` /
`Rotation`/`Position`/`EyeLeft`/`EyeRight`（[参数标准表 §1.8](PARAMETER_STANDARDS.md)），
**没有音频通道**。麦克风口型是 VTS 在自己进程里对麦克风做的，跟发什么包无关。

### 1.5 三个词汇表，一条转换规则

这是整份规范**最容易搞错**的地方：同一个形态量有三套名字在流动，而"哪一层用哪一套"是**固定的**，不是口味问题。

| 层 | 长什么样 | 例子 | 谁定义 |
|---|---|---|---|
| **设备线名** | PascalCase | `JawOpen`、`EyeBlinkLeft`、`MouthSmileLeft` | **VTS 手机协议**（= iOS ARKit 名，官方固定） |
| **规范名**（我们中间层内部） | camelCase | `jawOpen`、`eyeBlinkLeft`、`mouthSmileLeft` | `HoFaceTrackingChannels.Names`（52 条，`Runtime/FaceTracking/`） |
| **出口名**（喂控制器） | 我们选的那套 | `MouthOpen`、`FaceAngleX` | 本规范 §3 |
| *（参考）VB 的内部名* | camelCase + `_L/_R` | `jawOpen`、`eyeBlink_L` | VBridger 的 `SceneData.shapekeys` |

**线名 → 规范名的规则只有一条：首字母小写。** 52 个形状全部适用（这正是 VBridger `vtsKeys`
表与它的内部 `shapekeys` 表之间的关系——两表只差大小写，其余 12 个头/眼项拼写相同）。

在本机 VBridger 源码里，这三套拼写各有一张**按下标对齐**的表，用哪张取决于当前追踪源：

| VB 的表 | 行 | 拼写 | 对应我们这边 |
|---|---|---|---|
| `faceMotionKeys` | L8052 | `BrowDownLeft`（iFacialMocap/MediaPipe 的 `Left/Right` 系） | 我们不接 |
| `vtsKeys` | L8063 | **`BrowDownLeft` / `EyeBlinkLeft` / `JawOpen`** | **= 我们设备发的线名，逐字相同** |
| （无表） | — | `browDown_L` / `eyeBlink_L` / `jawOpen` | = VB 内部名；也是我们的规范名 |

⚠️ **VB 并不"拥有一切方言"**：它拥有的是**三套各自的精确表**，`IndexOf` **逐字命中**，
**没有归一化、没有别名表**。所以"多写几条不同拼写的输入行"不是可选的兼容策略，而是必需——
每多接一种拼写就多一族行。

⚠️ **输入行的左值必须是规范名，右值才是线名**：

    输入行：parameter = 规范名（jawOpen，camelCase）    ← 通道靠这个认
            expression = 设备线名（JawOpen，PascalCase） ← 包里真收到的那个键

因为 `HoFaceAnimationSession` 是拿**输入行的 `parameter`** 去
`HoFaceTrackingChannels.IndexOf(...)` 解析通道的。**写反的后果是静默且全面的**：
一份 `parameter = expression = JawOpen` 的配置**一个通道都解析不到**，中间层什么形状参数都不产出——
不报错、不警告，只是全都不动。

> 这不是假想：`ho-iPhoneVTS.hoface.json` 的第一版就是这么写的，理由听起来还挺有道理——
> "这一份的规范名词表就是设备线名词表"。**错。** 规范名是上游通道表的属性，配置只能去符合它。

⚠️ 只有**非通道**的 15 条（`Rotation_*` / `Position_*` / `EyeLeft_*` / `EyeRight_*` /
`FaceFound` / `Hotkey` / `Timestamp`）可以 `parameter == expression`——它们不是通道，
没有任何东西拿它们去 `IndexOf`，所以没有改名可做也没有改名必要。

> 由此推出一条判据：**一份"纯直通"（`parameter == expression`）的配置能不能真跑起来，
> 完全取决于那台设备的线名能不能逐字命中通道索引表。**
>
> 那个索引表**不是只有 52 个规范名**：`HoFaceTrackingChannels.BuildIndices()` 还给每个
> `Left`/`Right` 结尾的规范名**额外注册了 `_L`/`_R` 别名**（`eyeBlinkLeft` ←→ `eyeBlink_L`），
> 共 **88 条**；比较是 `StringComparer.Ordinal`（**逐字**，`JawOpen` ≠ `jawOpen`）。
>
> 实测（走真的 `IndexOf`）：
>
> | 配置 | 输入行 | 解析到的通道 | 为什么 |
> |---|---|---|---|
> | `ho-iPhoneVTS` | 67 | **52 / 52** ✅ | 52 条形状行的 `parameter` 就是规范名 |
> | `ho-debug-iphoneVTS` | 67 | **0 / 52** | 线名是 PascalCase `JawOpen`，规范名与别名都是 camelCase |
> | `ho-debug-androidVTS` | 65 | **40 / 52** | 小驼峰 + `_L/_R` 命中别名表；剩 12 个它不发 |
>
> 所以那两份调试配置的定位是**实测记录**，不是"能跑的配置"——见
> [Profiles/README.md](../Editor/FaceTracking/Profiles/README.md)。
>
> ⚠️ **核这类事别用 PowerShell**：它的 `-contains`/`-eq`/`-match` **默认忽略大小写**，
> 会让上面第二行假报成 52/52；而"只比 52 个规范名"又会漏掉别名表、把第三行报成 12/52。
> 我两个坑都踩过。**要核就照 C# 的 `Ordinal` + 真 `IndexOf`**（测试里已经这么做了）。

---

## 2. 值域约定（写公式前必须知道的四件事）

### 2.1 官方只规定了两个参数的范围

| 参数 | 范围 | 依据 |
|---|---|---|
| `MouthOpen` | `[0, 1]`，0 = 闭 | VTS wiki 原文 |
| `FaceAngleX` | 示例 min `-30` / max `30` | VTS API 文档示例数组 |
| `FacePositionX` | 示例 min `-10` / max `10` | 同上 |
| 其余全部 | **官方未公开** | 官方自己写那张示例数组 "is incomplete"；权威范围只能运行时 `InputParameterListRequest` 现取 |

⚠️ 网上流传的 `MouthSmile -1..1`、`Brows 0..1` 是**社区/VBridger 的约定**，不是官方规范。
我们沿用 VB 的约定时要说清这一点。

### 2.2 0.5 中立位是 **VB 发明的**，不是 VTS 的

VB 有一整族参数用 **0.5 = 中立**：`Brows`、`BrowLeftY`、`BrowRightY`、`EyeOpenLeft/Right`。
静息时公式算出来就是 0.5，闭眼往 0 走、睁大往 1 走。

这在官方文档里**找不到依据**。它是 VB 为了让"闭眼/睁眼"能用一根双向轴表达而定的私有约定。
我们照用时：**曲线的纵轴范围必须比 0..1 宽**（这些量能出 `[0,1]`），
否则默认的 `0..1` 曲线会把合法值**静默夹掉**——症状是"出口恒为 0 或 1 而输入侧有真值"。

### 2.3 三种量纲并存，且没有任何字段标注单位

| 量纲 | 例子 | 范围 |
|---|---|---|
| 表情权重 | `MouthOpen`、`CheekPuff` | `0..1` |
| 双向偏移 | `MouthX`、`EyeX`、`EyeY` | `-1..1` |
| 角度 | `FaceAngle*` | `±30/±40/±90`（**度**） |
| 位移 | `FacePosition*` | `±10/±15/±50`（**单位未标定**） |

### 2.4 双层曲线都要写

管线是 `出口值 = curve_out( expression_out( 入口值 ) )`，而**输入行自己也有曲线**。
想直通的量纲，**输入行与出口行都得给宽曲线**。只给一层 = 另一层用默认 `0..1` 夹掉。

---

## 3. 出口：**两份并行 + 一套额外**

中间层的出口不是一份清单，而是**三组东西同时出去**。这是照 VBridger 的形状来的——
它同时发**原始 ARKit 52** 与**合成出来的语义参数**，不是二选一。

| 组 | 行数 | 长什么样 | 干什么 |
|---|---|---|---|
| **G1 原始 ARKit 52** | 52 | **裸规范名**：`eyeBlinkLeft`、`jawOpen`、`mouthSmileLeft`… | **无损直通**。控制器要细节（某个具体形变、自己画轴）就读这一组 |
| **G2 官方 VTS 追踪参数** | 20 | `MouthOpen`、`Brows`、`FaceAngleX`… | **合成**出来的语义轴。喂 VTS 就发这组 |
| **G3 VB 自造 + 姿态向量 + 信号** | 18 | `MouthFunnel`、`Face/Angle/X`、`FaceFound`… | VB 私有的一族（VTS 词表没有那些概念）+ 4 组 XYZ 向量 + 1 个协议信号 |
| | **90** | | **出口行的总数** |

**为什么 G1 和 G2 要同时存在**：G2 是**有损**的，而且是故意有损——
`MouthOpen` 把 `mouthClose`/`mouthRoll*`/`mouthFunnel` 折成一个数，
`MouthSmile` 把 `mouthFrown*`/`mouthPucker`/`mouthDimple*` 折成一个数，因为 VTS 没有对应参数。
**要那份细节就只能读 G1。** 只出 G2 等于在中间层就把信息扔了。

⚠️ **G1 用裸名，没有前缀。** 这一点犯过错：曾经写成 `ARKit/<规范名>`，理由是"要个命名空间
跟合成组分开"。**错两层**：① VBridger 不这么干——它的 VMC 发送循环遍历整个
`SceneData.outputValues`（里面躺着全部原始形态键名），所以原始量就是**裸名**发出去的，
合成行只是再往里加键（`Assembly-CSharp.decompiled.cs` L13322-13329）；
② 加了前缀就**谁都读不到**——控制器只写自己参数表里声明过的名字，`ARKit/eyeBlinkLeft`
与任何声明都对不上，于是 52 行白算。

**只有姿态向量带命名空间**（`Face/`·`Body/`），因为那两个**真的会撞**：

| 名字 | 含义 |
|---|---|
| `FaceAngleX` | 官方 VTS 标量：`±90`，不缩放 |
| `Face/Angle/X` | VB 向量分量：`±30`，带 `0.66` 阻尼 |

这是**两个不同的量**，同名会打架。`Head/*` 是**已删的 `ho-vts-default`** 的保留名，别再用。

⚠️ 顺带记一笔：出口里有 5 对名字只差大小写（`cheekPuff`/`CheekPuff`、`mouthFunnel`/`MouthFunnel`、
`mouthPucker`/`MouthPucker`、`browInnerUp`/`BrowInnerUp`、`tongueOut`/`TongueOut`）——
**这是正常的，不是问题**：整条链每个字典都是 `StringComparer.Ordinal`（大小写敏感），
所以它们是不同的键、不同的参数，程序里区分得开，不会互相覆盖。
两套名字并存是**故意的**（原始裸名 + VB 的合成名），改名就等于发明 VTS 词表里没有的名字。

### 3.1 G1 原始 ARKit 52（无损直通）

**就是 §1 那 52 条规范名，一个不多一个不少**，`parameter = expression = <规范名>`，
曲线恒等 `0..1 → 0..1`。**出口名就是裸规范名**（`eyeBlinkLeft`、`jawOpen`…），没有前缀——
理由见 §3 开头那段。

这一组的意义是"**你永远拿得到原始值**"：G2/G3 任何一个合成量的口径你不认同时，
可以直接拿这 52 条自己算，不必改中间层。**它是这一层的信息上界。**

完整名单（= `HoFaceTrackingChannels.Names`，逐字等于 `catalog.raw_arkit[].name`）。
**`parameter` 与 `expression` 都是这个裸名**，所以下面这份名单同时就是出口名：

| # | 规范名 | 组 | # | 规范名 | 组 |
|---|---|---|---|---|---|
| 1 | `eyeBlinkLeft` | 眼睑/注视 | 27 | `mouthFrownRight` | 嘴:笑/撇 |
| 2 | `eyeLookDownLeft` | 眼睑/注视 | 28 | `mouthDimpleLeft` | 嘴:酒窝/拉伸 |
| 3 | `eyeLookInLeft` | 眼睑/注视 | 29 | `mouthDimpleRight` | 嘴:酒窝/拉伸 |
| 4 | `eyeLookOutLeft` | 眼睑/注视 | 30 | `mouthStretchLeft` | 嘴:酒窝/拉伸 |
| 5 | `eyeLookUpLeft` | 眼睑/注视 | 31 | `mouthStretchRight` | 嘴:酒窝/拉伸 |
| 6 | `eyeSquintLeft` | 眼睑/注视 | 32 | `mouthRollLower` | 嘴:卷/耸/压 |
| 7 | `eyeWideLeft` | 眼睑/注视 | 33 | `mouthRollUpper` | 嘴:卷/耸/压 |
| 8 | `eyeBlinkRight` | 眼睑/注视 | 34 | `mouthShrugLower` | 嘴:卷/耸/压 |
| 9 | `eyeLookDownRight` | 眼睑/注视 | 35 | `mouthShrugUpper` | 嘴:卷/耸/压 |
| 10 | `eyeLookInRight` | 眼睑/注视 | 36 | `mouthPressLeft` | 嘴:卷/耸/压 |
| 11 | `eyeLookOutRight` | 眼睑/注视 | 37 | `mouthPressRight` | 嘴:卷/耸/压 |
| 12 | `eyeLookUpRight` | 眼睑/注视 | 38 | `mouthLowerDownLeft` | 嘴:下唇/上唇 |
| 13 | `eyeSquintRight` | 眼睑/注视 | 39 | `mouthLowerDownRight` | 嘴:下唇/上唇 |
| 14 | `eyeWideRight` | 眼睑/注视 | 40 | `mouthUpperUpLeft` | 嘴:下唇/上唇 |
| 15 | `jawForward` | 颌 | 41 | `mouthUpperUpRight` | 嘴:下唇/上唇 |
| 16 | `jawLeft` | 颌 | 42 | `browDownLeft` | 眉 |
| 17 | `jawRight` | 颌 | 43 | `browDownRight` | 眉 |
| 18 | `jawOpen` | 颌 | 44 | `browInnerUp` | 眉 |
| 19 | `mouthClose` | 嘴:张合/圆扁 | 45 | `browOuterUpLeft` | 眉 |
| 20 | `mouthFunnel` | 嘴:张合/圆扁 | 46 | `browOuterUpRight` | 眉 |
| 21 | `mouthPucker` | 嘴:张合/圆扁 | 47 | `cheekPuff` | 颊/鼻/舌 |
| 22 | `mouthLeft` | 嘴:张合/圆扁 | 48 | `cheekSquintLeft` | 颊/鼻/舌 |
| 23 | `mouthRight` | 嘴:张合/圆扁 | 49 | `cheekSquintRight` | 颊/鼻/舌 |
| 24 | `mouthSmileLeft` | 嘴:笑/撇 | 50 | `noseSneerLeft` | 颊/鼻/舌 |
| 25 | `mouthSmileRight` | 嘴:笑/撇 | 51 | `noseSneerRight` | 颊/鼻/舌 |
| 26 | `mouthFrownLeft` | 嘴:笑/撇 | 52 | `tongueOut` | 颊/鼻/舌 |

### 3.2 G2 官方 VTS 追踪参数（20）

`公式来源` 列写的是哪份 VBridger 预设——**选公式按预设，不按出口名**：
VB 的 `EyeRightX` 是用 `eyeLookIn_L`/`eyeLookOut_L` 算的，`EyeRightY` 还加了 `browOuterUp_L`，
**名字根本不描述来源**。

| # | 参数 | 含义 | 值域 | 源 | 公式来源 |
|---|---|---|---|---|---|
| 1 | `FaceAngleX` | 左右转头 | 度 | `Rotation_*` | `VTS_Compatible` |
| 2 | `FaceAngleY` | 抬低头 | 度 | `Rotation_*` | `VTS_Compatible` |
| 3 | `FaceAngleZ` | 歪头 | 度 | `Rotation_*` | `VTS_Compatible` |
| 4 | `FacePositionX` | 头左右位移 | 未标定 | `Position_*` | `VTS_Compatible` |
| 5 | `FacePositionY` | 头上下位移 | 未标定 | `Position_*` | `VTS_Compatible` |
| 6 | `FacePositionZ` | 头远近位移 | 未标定 | `Position_*` | `VTS_Compatible` |
| 7 | `EyeOpenLeft` | 左眼睁开度（0.5 中立） | `0..1` | ARKit 眼睑 | `AdvancedARKit_V3.0` |
| 8 | `EyeOpenRight` | 右眼睁开度（0.5 中立） | `0..1` | ARKit 眼睑 | `AdvancedARKit_V3.0` |
| 9 | `EyeLeftX` | 左眼水平注视 | 度 | `EyeLeft_x` | **设备原值** |
| 10 | `EyeLeftY` | 左眼垂直注视 | 度 | `EyeLeft_y` | **设备原值** |
| 11 | `EyeRightX` | 右眼水平注视 | 度 | `EyeRight_x` | **设备原值** |
| 12 | `EyeRightY` | 右眼垂直注视 | 度 | `EyeRight_y` | **设备原值** |
| 13 | `Brows` | 双眉共同上下（0.5 中立） | `0..1` | ARKit 眉 | `AdvancedARKit_V3.0` |
| 14 | `BrowLeftY` | 左眉上下（0.5 中立） | `0..1` | ARKit 眉 + 嘴 | `AdvancedARKit_V3.0` |
| 15 | `BrowRightY` | 右眉上下（0.5 中立） | `0..1` | ARKit 眉 + 嘴 | `AdvancedARKit_V3.0` |
| 16 | `MouthOpen` | 嘴唇开口 | `0..1`（官方） | ARKit 嘴/颌 | `AdvancedARKit_V3.0` |
| 17 | `MouthSmile` | 综合嘴型（Form） | `-1..1`（社区） | ARKit 嘴 | `AdvancedARKit_V3.0` |
| 18 | `MouthX` | 嘴左右位移 | `-1..1`（社区） | ARKit 嘴 | `AdvancedARKit_V3.0` |
| 19 | `TongueOut` | 伸舌 | `0..1` | `TongueOut` | 各预设恒等 |
| 20 | `CheekPuff` | 鼓腮 | `0..1` | `CheekPuff` | 各预设恒等 |

⚠️ 第 9–12 条我们**不用 VB 的合成**：VB 的 `EyeRightX` 读的是 `eyeLookIn_L`（**左眼**），
`EyeRightY` 还加了 `browOuterUp_L`。iPhone 本来就在发原始眼球标量，直接引设备值更干净。

⚠️ 第 16/17 条是**有损**合成（见 §3 开头）。这是重点：它们是"给 VTS 用的折中"，
不是"这一层对嘴的理解"。要细节就去 G1 拿 `mouthFunnel`/`mouthPucker`/`mouthDimple*`。

### 3.3 G3a VB 自造参数（5）——不在任何官方清单里，**要注册才能用**

VB 为了让 Live2D 能表达 VTS 词表**根本没有的概念**而发明的一族。
喂 VTS 时要走 `ParameterCreationRequest`，**接收端用户必须接受**才生效——这是用它们的代价。

| # | 参数 | 建模的东西（VTS 通用参数**没有**） | 值域 | 源 | 出口公式（照抄 VB） |
|---|---|---|---|---|---|
| 21 | `MouthFunnel` | 圆唇 / 漏斗嘴 | `0..1` | `mouthFunnel`、`jawOpen` | `mouthFunnel - (jawOpen * .2)` |
| 22 | `MouthPucker` | 嘟嘴 | `-1..1` | `mouthDimple*`、`mouthPucker` | `((mouthDimpleRight + mouthDimpleLeft) * 2) - mouthPucker` |
| 23 | `MouthShrug` | 撇嘴 | `0..1` | `mouthShrug*`、`mouthPress*` | `(mouthShrugUpper + mouthShrugLower + mouthPressRight + mouthPressLeft) / 4` |
| 24 | `MouthPressLipOpen` | 压唇 + 唇开 | `-1.3..1.3` | `mouthUpperUp*`、`mouthLowerDown*`、`mouthRoll*` | `((mouthUpperUpRight + mouthUpperUpLeft + mouthLowerDownRight + mouthLowerDownLeft) / 1.8) - (mouthRollLower + mouthRollUpper)` |
| 25 | `BrowInnerUp` | 内眉上抬 | `0..1` | `browInnerUp` | `browInnerUp` |

全部取自 `AdvancedARKit_V3.0`。
⚠️ `MouthPressLipOpen` 的除数 VB 自己四份预设四个值（`/1.2`、`/1.8`、`/16`，
`VisemesARKit` 干脆换整套公式），**没有权威值**；我们取多数派 `/1.8`。

⚠️ **`Eye_Squint_L/R`、`BrowL/R`、`EyeOpen`、`EyeX/Y` 不在这里**（虽然它们在 VB 的出口里）。
原因不是"不重要"，而是：它们要么是**已有参数换了个拼写**（`Eye_Squint_L = eyeSquintLeft`+颊部、
`BrowL ≈ BrowLeftY`），要么是**同一份信息的另一种聚合**（`EyeOpen = eyeBlinkLeft` 单眼、
`EyeX/Y` = 双眼注视合成一根双向轴）。**再出一遍就是同一个值写两个名字。**
那几样信息在 G1（`eyeSquint*`、`eyeLook*`、`brow*`）与 G2 里都已经有了。

### 3.4 G3b 姿态向量（12 = 4 组 × XYZ）

VB 用**向量行**同时表达三个轴，并且它的名字（`FaceAngle` / `FacePosition` / `BodyAngle` /
`BodyPosition`）在 VMC 那条路上还兼作骨骼名。我们不做 VMC 骨骼向量，但**值本身对我们有用**
（控制器可以拿它当"脸的朝向"，不必自己从 `Rotation_*` 拼）。

⚠️ **命名空间是必须的**：G2 里已有 `FaceAngleX`（官方标量，`±90` 不缩放），
而这里的 `Face/Angle/X` 是 **VB 的向量 X 分量**（`±30`，带 `0.66` 阻尼）——**两者是不同的量**，
同名会打架。所以向量组统一加 `Face/`、`Body/` 前缀。

| 组 | 出口参数名 | 含义 | 值域 | 出口公式 | 来源 |
|---|---|---|---|---|---|
| 脸旋转 | `Face/Angle/X` | 左右转头 | `±30` | `-Rotation_y * .66` | `AdvancedARKit_V3.0` |
| | `Face/Angle/Y` | 抬低头 | `±30` | `-Rotation_x * .66` | 同上 |
| | `Face/Angle/Z` | 歪头 | `±30` | `Rotation_z * .66` | 同上 |
| 脸位移 | `Face/Pos/X` | 左右 | 未标定 | `-Position_x` | 同上 |
| | `Face/Pos/Y` | 上下 | 未标定 | `Position_y` | 同上 |
| | `Face/Pos/Z` | 远近 | 未标定 | `-Position_z` | 同上 |
| 身体旋转 | `Body/Angle/X` | 身体左右转 | `±30` | `-Rotation_y * .66` | 同上 |
| | `Body/Angle/Y` | 身体前后倾 | `±30` | `-Rotation_x * .66` | 同上 |
| | `Body/Angle/Z` | 身体侧倾 | `±30` | `Rotation_z * .66` | 同上 |
| 身体位移 | `Body/Pos/X` | 身体左右 | 未标定 | `-Position_x` | 同上 |
| | `Body/Pos/Y` | 身体上下 | 未标定 | `Position_y` | 同上 |
| | `Body/Pos/Z` | 身体远近 | 未标定 | `-Position_z` | 同上 |

⚠️ VB 的 `AdvancedARKit_V2.0` 对这两族还加**交叉项**（用 `(90-abs(rotY))/90` 当权重补偿大幅偏转下的
欧拉角耦合）。我们取 **V3.0 的简洁版**（无交叉项），因为本文件其余公式也全取 V3.0——
**混两份预设会让同一份配置里的口径不一致**，这比"少一点精度"更糟。

⚠️ `BodyAngle` 的 V2.0 版还混入了 `eyeBlink*`（眨眼带动身体，是个彩蛋式的耦合）；
V3.0 版没有。我们用 V3.0。

⚠️ **脸旋转与身体旋转现在算出的是同一个值**（都只吃 `Rotation_*`）。VB 的 `BodyAngle` 是给
"头部转动带动身体"用的，它靠的是**下游骨骼权重**去区分，而不是靠公式。所以这两组不是冗余，
是**同一个输入喂给两个不同的骨骼层级**。控制器若不做身体骨骼，`Body/*` 可以整组不接。

### 3.5 G3c 协议层信号（1）

| 参数 | 含义 | 值域 | 源 | 公式来源 |
|---|---|---|---|---|
| `FaceFound` | 追踪健康（**不是艺术轴**） | `0/1` | `FaceFound` | **设备原值** |

⚠️ 它**不是** VTS 的命名追踪参数。注入报文里 `faceFound` 是与 `parameterValues[]` 平级的
一个布尔字段（VTS 官方示例原文），不是参数数组里的一个 id。VB 也照这个语义处理：
恒定发一个 `VBridgerFaceFound`（`faceFound?1:0`）。

⚠️ 拼写必须逐字是 `FaceFound`（大小写敏感，控制器按 `Dictionary.TryGetValue` 匹配）。

**它进了 `参数` 字典之后谁在读？** 只有**控制器**：它在自己的参数表里声明 `FaceFound`，
`HoFaceController.Solve` 就会把它喂进去（float 口直接写值、bool 口按 `value != 0f`）。
除此之外**当前没有任何读取者**：node 接口不暴露它（图上只有 `参数`/`有脸`/`状态` 三个口，
`有脸` 已在承担"协议层那个 bool"的角色），求解器也不读字典里这一格。
用途只有一个：**丢追动画**（VTS 官方那个字段的原话用途）。

### 3.6 命名不一致——照抄 VB 的原样，**不改**

同一族里 VB 自己就不统一，这是**实测**，不要"顺手统一"：

* `MouthFunnel`（PascalCase）vs `MouthPressLipOpen`（连写）vs `FaceFound`；
* `BrowLeftY`（官方拼法）**读嘴部数据**（`mouthLeft/Right` 当偏航补偿项）——名字不描述来源；
* `EyeRightX`（官方名）在 VB 里**读左眼数据**（我们不抄这个，见 §3.2）。

我们**按用途**微调时要在配置的 `notes` 里写明"改过什么、为什么"。本文件的实际偏离只有两处，
都写在对应行的 `notes` 里：`FaceAngle*`/`FacePosition*` 取 `VTS_Compatible` 的官方标量拼法、
向量组取 V3.0 的简洁公式。

---

## 4. 公式全表（从本机 VBridger 机械提取，不手抄）

提取器：`.research/vbridger/one_formula_each.ps1`（按预设优先级选一条，输出名 → 公式）。
下面的公式是**照抄 VB 的原文**，所以变量名是 **VB 内部拼写**（`eyeBlink_L`、`mouthSmile_L`，
即 `SceneData.shapekeys`）；配置文件里实际写的是**我们的规范名**，见 §1.5 的换算：

| 本文档（VB 原文） | 配置文件里要写的（我们的规范名） |
|---|---|
| `eyeBlink_L` / `eyeBlink_R` | `eyeBlinkLeft` / `eyeBlinkRight` |
| `mouthSmile_L` / `mouthDimple_R` | `mouthSmileLeft` / `mouthDimpleRight` |
| `jawOpen`、`tongueOut`（中线性名） | 同名（本来就是规范名） |
| `headRotX` / `headPosX` | **设备线名** `Rotation_x` / `Position_x`（它们不是通道，没有规范名） |

换算规则只有一条：**`_L/_R` → `Left/Right`，其余原样**（这就是 VB 的 `vtsKeys` 表与它的内部
`shapekeys` 表之间的关系）。对照表在 [参数标准表 §3.2](PARAMETER_STANDARDS.md)。

### 4.1 头姿 / 位移 / 注视

```
FaceAngleX      = -Rotation_y
FaceAngleY      = -Rotation_x
FaceAngleZ      =  Rotation_z
FacePositionX   = -Position_x
FacePositionY   =  Position_y
FacePositionZ   = -Position_z

EyeLeftX   = EyeLeft_x
EyeLeftY   = EyeLeft_y
EyeRightX  = EyeRight_x
EyeRightY  = EyeRight_y
```

⚠️ `FaceAngleX/Y/Z` 与 `FacePositionX/Y/Z` 的**正负号是 VB 的**，属于照抄结构；
它吃的那两根设备轴（`Rotation_*` / `Position_*`）**还没在实机上核过**。

### 4.1b 姿态向量（出口名带 `Face/` · `Body/` 前缀）

取 `AdvancedARKit_V3.0` 的简洁版（无交叉项、`BodyAngle` 不混 `eyeBlink`），与本文件其余公式同源：

```
Face/Angle/X    = -Rotation_y * .66          Body/Angle/X    = -Rotation_y * .66
Face/Angle/Y    = -Rotation_x * .66          Body/Angle/Y    = -Rotation_x * .66
Face/Angle/Z    =  Rotation_z * .66          Body/Angle/Z    =  Rotation_z * .66

Face/Pos/X      = -Position_x                Body/Pos/X      = -Position_x
Face/Pos/Y      =  Position_y                Body/Pos/Y      =  Position_y
Face/Pos/Z      = -Position_z                Body/Pos/Z      = -Position_z
```

⚠️ VB 的对应行本身没有 `Face/`·`Body/` 前缀（它叫 `FaceAngle`、`BodyPosition`…，
还兼作 VMC 骨骼名）。**前缀是我们加的**，为的是不与 G2 的官方标量 `FaceAngleX` 撞名——
两者是不同的量（`FaceAngleX` 是 `±90` 不缩放，`Face/Angle/X` 是 `±30` 带 `0.66`）。

⚠️ **`V2.0` 版有交叉项，我们没取**（见 §3.4 的说明）：V2.0 的
`FaceAngle.Y = -(Rotation_x * ((90-abs(Rotation_y))/90) + Rotation_z * (Rotation_y/45))`，
`BodyAngle = ±Rotation_* * 1.5 + eyeBlink*`。混两份预设会让配置里口径不一致，所以统一 V3.0。
`FaceAngle` 那个向量行的 Y/Z 分量带**交叉项**（`Rotation_x` 与 `Rotation_z` 互相修正，
用 `(90-abs(Rotation_y))/90` 当权重）——这是 VB 为了补偿头部大幅偏转时的欧拉角耦合，
不是随手写的。

### 4.2 眼睑 / 眉

```
EyeOpenLeft    = .5 + (eyeBlinkLeft  * -.8) + (eyeWideLeft  * .8)
EyeOpenRight   = .5 + (eyeBlinkRight * -.8) + (eyeWideRight * .8)

Brows          = .5 + (browOuterUpLeft + browOuterUpRight - browDownLeft - browDownRight) / 4
BrowLeftY      = .5 + (browOuterUpLeft  - browDownLeft)  + ((mouthRight - mouthLeft) / 8)
BrowRightY     = .5 + (browOuterUpRight - browDownRight) + ((mouthLeft - mouthRight) / 8)

BrowInnerUp    = browInnerUp
```

⚠️ `Brows` / `BrowLeftY` / `BrowRightY` / `EyeOpenLeft/Right` **静息就是 0.5**
（见 §2.2）。它们的曲线纵轴必须比 `0..1` 宽。
⚠️ `BrowLeftY`/`BrowRightY` **吃嘴部数据**（`mouthLeft/Right` 当偏航补偿项）——
这是"名字不描述来源"的又一个例子，别以为它只看眉。

**VB 有而我们不出的两个**（同一份信息换拼写，出了就是重复）：

```
BrowL   = ((browOuterUpLeft  - browDownLeft  - mouthRight) / 2) + .5   ≈ BrowLeftY
BrowR   = ((browOuterUpRight - browDownRight - mouthLeft ) / 2) + .5   ≈ BrowRightY
EyeOpen = eyeBlinkLeft                                                 ← 未含 eyeWide，反而更少信息
Eye_Squint_L = (eyeSquintLeft  + cheekSquintLeft ) / 2                 ← 颊部的和；要的话自己加
Eye_Squint_R = (eyeSquintRight + cheekSquintRight) / 2
```

### 4.3 嘴

出口名没变，但**变量是规范名**（与 §3 的两张表一致）：

```
MouthOpen         = (jawOpen - mouthClose) - ((mouthRollUpper + mouthRollLower) * .2) + (mouthFunnel * .2)
MouthSmile        = (2 - (mouthFrownLeft + mouthFrownRight + mouthPucker) + (mouthSmileRight + mouthSmileLeft + ((mouthDimpleLeft + mouthDimpleRight) / 2))) / 4
MouthX            = ((mouthLeft - mouthRight) + (mouthSmileLeft - mouthSmileRight))
TongueOut         = tongueOut
CheekPuff         = cheekPuff

MouthFunnel       = mouthFunnel - (jawOpen * .2)
MouthPucker       = ((mouthDimpleRight + mouthDimpleLeft) * 2) - mouthPucker
MouthShrug        = (mouthShrugUpper + mouthShrugLower + mouthPressRight + mouthPressLeft) / 4
MouthPressLipOpen = ((mouthUpperUpRight + mouthUpperUpLeft + mouthLowerDownRight + mouthLowerDownLeft) / 1.8) - (mouthRollLower + mouthRollUpper)
```

⚠️ `MouthOpen` 与 `MouthSmile` 都是**把 VTS 没有的维度折进来**的合成式：
`MouthOpen` 折了 `mouthClose`（闭唇）、`mouthRoll*`（卷唇）、`mouthFunnel`（圆唇）；
`MouthSmile` 折了 `mouthFrown*`、`mouthPucker`、`mouthDimple*`。
所以它们是**有损**的——这正是 VB 再单独开 `MouthFunnel`/`MouthPucker` 的原因，
也是我们为什么**同时**出 G1 的原始 52。
⚠️ `MouthSmile` 静息 = 0.5（分子上 `2 - …`、分母 `/4`）。
⚠️ `MouthPucker` 里 `(dimple*2) - pucker` 的**正负方向与直觉相反**：dimple 大声时值为正。
⚠️ `MouthPressLipOpen` 的除数在四份预设里是 **四个不同的东西**，VB 自己没定下来：
`/1.2`（`AdvancedARKitSettings`）、`/1.8`（V2.0 / V2.0_Stepped / V2.0_PlusVolume / V3.0）、
`/16`（`VTS_Compatible`），而 `VisemesARKit` 那份**根本不是这一族公式**——它整个改由 `viseme_*` 驱动
（`(0 - viseme_PP) + ((viseme_SS*.5 + viseme_KK*.5 + …) * (1 - viseme_PP))`），
跟嘴部形态量无关。**没有权威值**，我们取 `/1.8`（多数派）。

### 4.4 注视：**我们不做合成**

VB 有一对把双眼注视压成一根双向轴的合成量（`PNGTuber` 预设）：

```
EyeX = eyeLookOutLeft - eyeLookInLeft
EyeY = eyeLookUpLeft  - eyeLookDownLeft
```

⚠️ **我们不抄它**，两个原因：
① 它只看**左眼**（`_L` 侧），是 PNGTuber"双眼当一只用"的做法；
② 它有损——把四对 `eyeLook*Left/Right` 压成两根轴，而**控制器完全可以自己压**
（那是表达式一行的事），中间层没必要替它决定。

需要这种"双眼合并注视"的控制器，用 G1 的 `eyeLook*Left/Right` 自己在树里算；
需要**设备原始眼球标量**的用 G2 的 `EyeLeftX/Y`、`EyeRightX/Y`。

### 4.5 修饰符：我们与 VB 的差别

⚠️ **三个名字都是两边各叫各的，别按字面互译**：

| 我们 | VBridger | 备注 |
|---|---|---|
| `Smooth` 平滑 | `Smoothing` | 同名同义（单位不同，见下） |
| `Delay` 延迟 | `Delay` | 同名同义（它的是帧数） |
| `Steps` **维持** | `Steps` | **英文同词、中文我们重命名过**。VB 英文面板下四个字段是 `Trigger` / `Target` / `Threshold` / `Hold Time`（本地化表核过）。叫"维持"是因为我们看重它**触发后至少保持 hold** 这一面；VB 的原意是"**跳档**"（离散台阶）。两边都对，只是取的角度不同 |

VB 每行有三个**独立开关**（`smoothOn` / `stepOn` / `delayOn`），我们是每行一个**有序列表**
（`modifiers`，顺序可控）。逐项实测对照：

| 维度 | VBridger | 我们 | 判断 |
|---|---|---|---|
| 平滑**语义** | `Lerp(prev, 输入, 1 − smooth)`，**每帧一次** | `1 − exp(−dt / seconds)` | **单位不同**，见下 |
| 平滑**时间基准** | **按帧**（无时间单位，帧率变了手感就变） | **按秒**（时间常数） | 我们帧率无关，**更稳** |
| 平滑**用量** | `V3.0`: 20/26 行；`VisemesARKit`: 20/34；`VMC-Face-Head`: 2 行 | **23 / 90 行**（照 VB 换算） | 已对齐 |
| **延迟** | 帧 FIFO，`delayCount = round(delay × 0.06)`；10 份预设里 `delayOn` **全 false** | **每行一条 FIFO，单位秒**（不跟帧率绑定） | 我们做了，但 VB 从没启用过 |
| **维持**（VB 叫 `Steps`） | `[trigger, target, threshold, hold_ms]`，hold 走 `round(ms × 0.06)` 帧 | `trigger/target/hold/threshold`，hold 用**秒**；有迟滞与最短保持 | 语义接近，单位不同 |
| 维持**用量** | `V2.0_Stepped`: 24/26 行；`PNGTuber`: 9/10 | **0 行** | 未采用 |
| **曲线** | 逐行，通常恒等 | 逐行，90 行**全直线** | 一致 |
| 非恒等曲线 | 全库**只有 1 条**：`EyeRightY`（五份预设共用，零点附近一个浅 S） | 0 条 | 可忽略 |
| **两层曲线** | 输入曲线在**机器级文件**里（68 条，当前全恒等） | 输入曲线**在配置行里**（15 条标量给了宽范围） | 我们概念上更好 |

#### 平滑的单位换算（为什么不能直接搬 VB 的值）

VB 的 UI 滑条是 0..100 存成 0..1（源码 `smoothField.value = smoothVal * 100`、
`smoothVal = value / 100`），然后每帧 `lerp(num, 1 − smoothVal)`。**所以 `smooth` 是个
"每帧比例"，没有时间单位** —— 同一个预设，60fps 和 144fps 手感不同。

我们的 `Smooth` 是时间常数（秒），帧率无关。令两者在**一帧**上等效：

```
1 − exp(−dt/τ) = 1 − smooth        ⇒        τ = −dt / ln(smooth)
```

按 **60fps** 列表（VB 是桌面应用；这也是**最保守**的一档：VB 在高帧率下滤波更弱，
所以取 60fps 的值搬到按秒的实现上，永远不会比它原本更"糊"）：

| VB `smooth` | τ（秒） | 我们用在哪 |
|---|---|---|
| 0.10 | 0.007 | `EyeLeftX/Y`、`EyeRightX/Y`、`EyeOpenLeft/Right`、`MouthSmile`、`MouthX`、`MouthFunnel`、`MouthPucker`、`MouthShrug`、`MouthPressLipOpen` |
| 0.15 | 0.009 | `MouthOpen`、`Brows` |
| 0.19 | 0.010 | `FacePositionX/Y` |
| 0.22 | 0.011 | `FacePositionZ` |
| 0.32 | 0.015 | `BrowInnerUp` |
| 0.40 | 0.018 | `BrowLeftY`、`BrowRightY` |
| 0.67 | 0.042 | `FaceAngleX/Y/Z` |

⚠️ **这些值都不大**：`0.10` **不是"关掉"**，它是"每帧 90%"≈ 7ms —— 一个很轻的滤波。
VB 那 20 行里大部分都在这个量级，真正"黏"的只有 `FaceAngle`（42ms）与眉那一对（18ms）。
**别把 0.007 当成笔误。**

⚠️ **G1（原始 52）不给任何修饰符**：它的全部价值就是"没被动过的值"。
给它加平滑之后就没有任何未滤波的源可以对照了。

#### 我们与 VB 的两个语义差异（调参时能感觉到）

**① 维持的 `threshold` 定义不同。** 我们把 `threshold` 当"触发阈值上下的迟滞带"
（`release = trigger − |threshold|`）；VB 里它是"低于当前档触发点时，要掉多少才退"。
都能防抖，但同一个数字在两边的手感不一样。

**② VB 是按部位给黏度的。** 它的 `BodyAngle` 平滑远小于脸的（0.14 / 0.22 vs
`FaceAngle` 0.67）—— 身体慢、脸快。这正是"每行自己的手感"的原始出处，
也是我们把它做成**每行一个 `modifiers` 列表**（而不是全局一个时间常数）的原因。

---

## 5. 明确**算不出来**的（9 行 / 11 个参数）

这一列不是"以后再做"，是**当前输入契约下不可能**。要它们必须先扩输入。

| 参数 | 缺什么 | 备注 |
|---|---|---|
| `Visemes` | 音素分类器（OVRLipSync / uLipSync） | VB 把它压成一个 `0..12` 的**音素编号**（`viseme_*_abs` 加权和）。⚠️ VB 那份公式里有错位：`viseme_SS_abs * 67` 与声明的 `max=12` 矛盾，`DD` 与 `KK` 共用 `8`。**要抄也得重排音素表** |
| `VoiceVolume` | 麦克风 | — |
| `VoiceFrequency` | 麦克风 + 元音检测 | — |
| `VoiceVolumePlusMouthOpen` | 麦克风 | VB 用 `MouthOpen` 顶上（无音频时退化成一个重复的 `MouthOpen`） |
| `VoiceFrequencyPlusMouthSmile` | 麦克风 | 同上，退化成重复的 `MouthSmile` |
| `VoiceA/I/U/E/O` | 麦克风 + 元音分类 | 官方保证这五个**永不同时为 1** |
| `VoiceSilence` | 麦克风 | PNGTuber 用它做"无声走面捕"的开关 |
| `MousePositionX/Y` | 鼠标 / 触摸 | 手机没有指针 |
| `FaceAngry` | 生气脸专用分类器 | 官方标 EXPERIMENTAL、不推荐；ARKit 52 里没有对应形态量 |

⚠️ `VoiceVolumePlusMouthOpen` / `VoiceFrequencyPlusMouthSmile` 这两个名字听起来像"音频""辅助"，
但 VB 的公式在有麦克风时是**加权混合**、没麦克风时**就是一个重复的 `MouthOpen`/`MouthSmile`**。
所以它们对我们**没有新增信息**——不要为了让参数表"齐全"而把它们算两遍。

---

## 6. 未标定 / 未验证（**别当结论用**）

| 项 | 状态 |
|---|---|
| `FaceAngle*` / `FacePosition*` 的正负号与轴向 | 照抄 VB 的结构，**未在实机核对**。改法是翻配置里那行的符号 |
| `Position_*` 的单位 | **未标定**，手机原始值直通 |
| `EyeLeft_*` / `EyeRight_*` 的单位与零位 | 实测给的是 `Rotation_*` 同级的小数值（`EyeLeft_x` 约 ±20），**未核对是否就是度** |
| `EyeLeftZ` / `EyeRightZ` 是否可用 | iPhone 能填（范围 5.87/6.11），安卓恒 0。含义未定 |
| 0.5 中立位是否该保留 | VB 的私有约定（§2.2）。用 VTS 官方语义（`Brows` 无 0.5）还是 VB 的，**我们还没定** |
| `MouthPressLipOpen` 的除数 | VB 自己四份预设四个值（`/1.2`、`/1.8`、`/16`，`VisemesARKit` 完全换公式），**没有权威值**；我们取多数派 `/1.8` |
| VB 自造参数（§3.3 的 5 个 + §3.4 的向量）的**接收端接受率** | 走 `ParameterCreationRequest`，用户必须手动接受才生效。我们不做 VTS 中转时无所谓，做的时候要测 |

---

## 7. 这份规范怎么用

1. **改配置前先查这里**：参数名、值域、源、公式都在这。配置里的 `notes` 只写"这一行跟规范哪条对应"，不重复公式。
2. **加新参数**：先在这份里加一行（含义 / 值域 / 源 / 公式来源 / 可用性），**再**改配置与生成器。
   顺序反了就会出现"配置里有个没人知道什么意思的参数"。
3. **发现新设备或新语义**：先更新 §1 输入契约（实测表在 [设备验证表](PARAMETER_DEVICE_VERIFICATION.md)），
   再回来看 §5 那 11 个里有没有能解锁的。
4. **外部标准的依据**（官方 URL、逐条引用）在 [参数标准表](PARAMETER_STANDARDS.md)；
   这份不重复抄，只引用。

### 相关文件

| 文件 | 记什么 |
|---|---|
| [参数标准表](PARAMETER_STANDARDS.md) | **外部标准**：VTS / Cubism / ARKit / VMC / VRM / iFacialMocap / VRCFT，逐条带官方 URL |
| [设备验证表](PARAMETER_DEVICE_VERIFICATION.md) | **这台设备实测发了什么、动了多少**（生成表，可由脚本刷新） |
| **本文件** | **我们选什么**：目标词表 + 逐参数定义 + 公式来源 |
| [面捕中间层](FACE_TRACKING_MIDDLE_LAYER.md) | 值怎么被加工（两层行、缺键语义、曲线纪律） |
| [VBridger 的输入 / 输出参数格式](archive/VBRIDGER_IO_VOCABULARY.md) | 归档：VB 那 100 个输入变量与 314 行出口的一手记录 |
