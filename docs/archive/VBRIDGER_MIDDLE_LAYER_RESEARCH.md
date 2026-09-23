# VBridger 的中间层：一手逆向记录（2026-09-23）

> **已归档（2026-09-23）。** 这不是现状文档，而是**别人的成熟实现长什么样**的一手证据：
> 我们自己的中间层（[面捕中间层处理](../FACE_TRACKING_MIDDLE_LAYER.md)）就是照它的形状设计的。
> 结论已经抽进那份活文档；这里留"当初凭什么这么设计"的现场。
>
> 全部来自本机安装 `D:\Steam\steamapps\common\VBridger`（**只读**）。
> 反编译产物、解密后的 10 份预设、本地化词表都在 `.research/vbridger/`（gitignore）。

## 1. 存档格式：不是一个能保护东西的加密

`Assembly-CSharp.dll` 里 `GearMenu.EncryptDecrypt` 是个**16 字符循环 XOR**，不是什么加密库：

```csharp
private static readonly string keyWord = "p3s6v9y$B&E)H@Mc";   // 16 字符
private string fileVersion = "vbridgerV2";
// 存：File.WriteAllText(path, EncryptDecrypt(fileVersion + JsonConvert.SerializeObject(store)))
// 读：t = EncryptDecrypt(File.ReadAllText(path));
//     t.StartsWith("vbridgerV2") ? Json<ParameterStore>(t.Substring(11)) : JsonUtility<ParameterStore>(t)
```

文件头验证：`'v'^'p'=0x06`、`'b'^'3'=0x51`、`'r'^'s'=0x01`、`'i'^'6'=0x5F` —— 正好是观察到的 `06 51 01 5F`。
10 份自带预设里 8 份是这种"现代"格式，2 份**没有前缀**（旧版 Unity `JsonUtility` 格式）。

**顺带排掉一个假线索**：DLL 里的 `Deflate` / `Password` / `EncryptionSecret = "Sheeesh"` / `KonamiObject`
是**彩蛋**，不在存档路径上。而 `%LocalLow%\PiPuProductions\VBridger\InputCurvesBck.vbsettings`
是**纯文本 JSON**（没加密）。

**对我们的意义**：这层"保护"花一个下午就能还原，代价却是可读性、可 diff 性和向前兼容
（它自己的两份预设已经落在旧分支上了）。所以我们的配置是**带版本号的可读 JSON**，不加密。

## 2. 一行输出 = `ParameterField`（反编译 L7564-7679）

| 字段 | 类型 | 含义 |
| --- | --- | --- |
| `output` | string | 发往下游（VMC / VTS / ARKit）的名字，自由文本 |
| `equation` | string | 表达式（X，或唯一那个值）；`equationY` / `equationZ` 只在 `vectorMode` 时用 |
| `min` / `max` | string | 曲线定义域 + 钳制 + 报给 VTS 的参数范围 |
| `defaultValue` | string | 丢脸 / 载入时推的值 |
| `delay` | string | **毫秒**；FIFO 队列，`delayCount = round(delay * 0.06)` 帧 |
| `smooth` | float | 0..1 的 EMA 系数（`Mathf.Lerp(prev, target, smooth)`） |
| `delayOn` / `smoothOn` / `stepOn` | bool | 三个修饰符各自的开关 |
| `on` | **string** `"true"/"false"` | 整行启用（是字符串，不是 bool） |
| `vectorMode` | bool | 一行驱动 X/Y/Z 三个输出 |
| `curve` | AnimationCurve | 输出映射 `y = f(x)`，`x ∈ [min,max]`，wrap = Clamp |
| `stepDetails2` | List&lt;List&lt;float&gt;&gt; | `[trigger, target, threshold, hold_ms]` |
| `faceSend` | bool | "Send Face Detection Loss"（构造时**拷全局**的值，是个瑕疵） |

**所有数值字段都是字符串** —— 这是它向前兼容差的根因（自己的预设因此落了两个分支）。

## 3. 两条曲线，两个层次

- **输出曲线**（每行一条）：在表达式之后、修饰符之前：
  `ProcessValue = curve.Evaluate(v) → lerp(smooth) → Stepped()`。**延迟在曲线之前** —— 它自己跟
  "曲线在平滑之前"是矛盾的，这一处不值得抄。
- **输入曲线**（按**输入名**，全局一份，另一个文件）：
  `inputCurves[n].curve.Evaluate(MapValue(raw, 0..1, blendshapeCalibration[n]))` ——
  先校准源、再塑形输出。存成纯 JSON `{"data":{<名字>:{…}}}`。

> 逆向时一度以为每行有两条曲线（`curve1` / `curve2` / `CurvePair`）：那是它**通用曲线编辑器 demo**
> 里的东西（`RTAnimationCurve` + `class CurveForm`），**不在 `ParameterField` 里、也不出现在任何存档中**。

## 4. 表达式语言

`ExpressionSolver`（MIT，© 2015 Antti Kuukka）被**编进** `Assembly-CSharp`（`AK` 命名空间）。

- 内置：`sin cos tan asin acos atan atan2 sqrt abs sign floor ceil min max sinh cosh tanh exp log log10 round rand clamp approx pow strlen`，
  常量 `e` / `pi`，字符串字面量 `'...'` 与 `&& || == != <= >= ^ %`。
- VBridger 每行额外加四个（L5630-5633）：`stabil(var,dif)`（防抖迟滞）、`time(inc,max)`、
  `if(cond,then,else)`、`lerp(a,b,t)`。
- 输入是它自己那 86 个 `SceneData.shapekeys`（ARKit 的 `*_L/*_R`、`eyeLeftX..headRotZ`、
  `Sound Input` / `volume`、15 个 `viseme_*` + 15 个 `viseme_*_abs`、`faceFound` …），
  而 VTS / iFacialMocap 的名字集只是**索引映射的重命名表**，不是另一套变量。
- **行可以读别的行**：`LateUpdate` 把每行结果 `SetGlobalVariable(cleanedName, 值)` 发布出去
  （vector 行发布 `nameX/Y/Z`）。**没有任何环检测** —— 顺序是偶然的。

## 5. UI 词汇（它的本地化表 "UI Elements"，英文）

- 行：`Add New Output`、`Output Parameter`、`Enter Equation`、`Min`、`Max`、`Default`、
  `Reset To Default`、`Load`、`Save`、`Preview`、`Debug Values`。
- 曲线右键菜单：`Add Key` / `Edit Key` / `Delete Key` / `Copy Curve` / `Paste Curve`；
  切线模式 `Clamped Auto` `Auto` `Free Smooth` `Flat` `Broken` `Free` `Linear` `Constant`；
  作用目标 `Left Tangent` / `Right Tangent` / `Both Tangents`。
- 面板：`Input Curves`、`Output Curves`、`Input Curves Settings`（`Revert` / `Import` / `Export` / `Reset`）。
- 修饰符：`Output Modifiers` → `Smoothing` / `Delay` / `Steps`（`Trigger` / `Target` / `Threshold` / `Hold Time`）。
- 形状：**左边一列可滚动的行卡片，右边是被选中那一行的曲线与修饰符**；点卡片就把曲线载进共享曲线面板。

## 6. 什么跟预设走、什么跟机器走

| 层 | 存在哪 | 内容 |
| --- | --- | --- |
| **预设**（`.vbridger`） | 文件，可以互换 | 整个有序行列表 |
| **机器**（`persistentDataPath`） | 本机 | `InputCurves*.vbsettings`（输入曲线）、`blendshapeCalibration`（62 个 float 的"静止归零"，`Calibrate` 按钮抓的）、`VBridgerSettings`（**另一把** key 加密） |

**没有"按角色"的概念** —— 一个角色 = 选一份预设 + 一套输入词表。路由：非 vector 行走
VMC `/VMC/Ext/Blend/Val` + `/Apply` 或 VTS `InjectParameterDataRequest`；vector 行走
`name-pos` / `name-position`（大小写不敏感）决定是位置还是旋转，发 `/VMC/Ext/Bone/Pos`；
新名字会自动向 VTS 声明（`VTSCustomParameter{名字, 说明, min, max, 默认}`）；还总发一个 `VBridgerFaceFound`。

## 7. 它给我们的八条启示（我们采纳了哪几条）

| # | 启示 | 我们 |
| --- | --- | --- |
| 1 | **两层曲线**：按输入的校准曲线 + 按输出的响应曲线 | ✅ 采纳（输入曲线在组件上，响应曲线在配置里） |
| 2 | 一行就是**一条扁平的有序记录**，行级状态不落盘 | ✅ 采纳（逐帧状态在会话里，配置里只有数据） |
| 3 | **强类型字段 + 版本号**，别学它全是字符串 | ✅ 采纳（`format` + `version`，字段有类型） |
| 4 | 行能读行很powerful，但要**加环检测** | ⏳ 我们暂时**不支持行间引用**（见活文档"还没做"） |
| 5 | 修饰符顺序应当是 clamp → 曲线 → 平滑 → 分档 | ✅ 采纳（顺序由作者列出决定，且没有它那个"延迟在曲线前"的矛盾） |
| 6 | 分档用数据 `[trigger,target,threshold,hold]`，边界档自动补 | ✅ 采纳（`HoFaceStep`）；毫秒换算它用 `ms × 0.06` |
| 7 | **别混淆存档**：一个下午就能还原，却赔上可读性 | ✅ 采纳（可读 JSON，不加密） |
| 8 | 把"声明过的输出白名单"和"行输出"分开，新名字自动登记 | 部分（我们拿控制器的参数表当白名单：控制器里没有的名字就跳过） |
