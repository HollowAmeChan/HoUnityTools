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
| `ho-vts-default.hoface.json` | **默认配置层**（骨架）：三种方言的线名 → 规范名 + 58 条输出行。给人复制当起点用 |
| `ho-debug-iphoneVTS.hoface.json` | **iPhone 上的 VTS** 的调试配置（单方言，65 输入 / 58 输出） |
| `ho-debug-androidVTS.hoface.json` | **安卓手机上的 VTS** 的调试配置（单方言，67 输入 / 58 输出） |

三份都由脚本生成，名字全部取自源码与 `docs/VTS_HIGH_QUALITY_FACE_CATALOG.json`（**不手抄**）：

```powershell
& .warudo-mod-research\.tools\gen-default-profile.ps1
& .warudo-mod-research\.tools\gen-device-profile.ps1 -Device iphoneVTS  -Notes $n -ShapeNote '…'
& .warudo-mod-research\.tools\gen-device-profile.ps1 -Device androidVTS -Notes $n -ShapeNote '…'
```

⚠️ 生成器是**纯 ASCII** 的（Windows PowerShell 5.1 把无 BOM 的 `.ps1` 当 ANSI 读，脚本里放中文会炸），
所以中文 `notes` 从命令行 `-Notes` 传进去。

## 两台设备的**实测**方言（2026-09-25，各一次完整 dump）

| 设备 | 键数 | 形状那半边 | 标量那半边 |
|---|---|---|---|
| **iPhone VTS** | 67 | **干净的 VTS PascalCase**：`JawOpen` / `MouthSmileLeft`… —— 52 个形状**全部到位、0 个对不上** | `Rotation_x`… / `Position_x`… / `EyeLeft_x`… / `FaceFound` / `Hotkey` / `Timestamp` |
| **安卓 VTS** | 65 | **iFacialMocap 小驼峰 + `_L/_R`**：`jawOpen` / `mouthSmile_L`…，**但眨眼那一对两种都发**（`eyeBlink_L/R` **和** `EyeBlinkLeft/Right`） | 同上（**两台同名**） |

⚠️ **安卓那条"眨眼两种都发"是有来历的**：最早那次调试里"只有头系和眨眼在动"，
真因就是别的形状行**没有数据**、而眨眼恰好有 PascalCase 那对在喂。
所以安卓那份**刻意给眨眼两行**（指向同一条通道，不存在互相顶掉的问题）。

核对用的工具：`.warudo-mod-research/.tools/check-device-keys.ps1`（拿一份设备键清单去比规范名，报"对不上"和"没发"）。

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

它们只做名字映射，**没有**曲线校准、没有滤波：

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

* 默认配置：52 个规范名与三种方言拼写是否都在、输出行有没有重复、`Head/*` 宽曲线是否真的透明；
* 设备配置：**实测键清单里的每一个**是否都有行（`Hotkey`/`Timestamp` 故意豁免）、
  是不是**单一方言**（成对通道的两种拼写不能同时出现，眨眼那一对是已知例外）、
  输出行是否 52 + 6 且无重复、`Head/*` 曲线是否透明。

它是"改了生成器或 catalog 之后忘了重新生成"的唯一守门人。当前：**107 passed / 0 failed**。
