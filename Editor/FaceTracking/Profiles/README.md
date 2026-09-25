# 默认配置层（Profiles）

这里放**发货用的中间层配置**（`*.hoface.json`），当成一个仓库用。

> ⚠️ **这份配置的"输出侧"是过渡件。** 终点是**动画 = 状态**（一份动画表达一个状态，动画之间没有标准命名，
> 哪些轴成 2D 混合树由作者按模型决定），不是 ARKit 形态键。现在输出行写的是 52 个 `ARKit/<规范名>`，
> 只是为了在形态键做完之前能拿现成模型把整条链跑通。等动画做完，**输出行换成"规范名 → 作者参数名"**，
> 两层之间的接口不变（还是一份 `Dictionary<string,float>`）。详见
> [面捕工作流 §2.1](../../docs/FACE_TRACKING_WORKFLOW.md)。
>
> **输入侧（线名 → 规范名）是长期资产**，不会随这个变化作废。

## 现在有什么

* `ho-vts-default.hoface.json` —— 默认配置层：**线名 → 规范名 → 输出**的映射骨架。
  用 `.warudo-mod-research/.tools/gen-default-profile.ps1` 生成，名字全部取自
  `docs/VTS_HIGH_QUALITY_FACE_CATALOG.json`（52 个原始 ARKit 逐名 + 三种方言拼写），
  所以不会手抄错。

## 怎么用（⚠️ Warudo 读不到这个包）

1. 把它**复制到 Warudo 的插件沙箱**：`Warudo_Data/StreamingAssets/Plugins/Data/hollow.hofacetracking/`。
   在 Warudo 里就是「HoFace参数处理」节点上的 **`打开文件夹`** 按钮，或 `状态` 口里那个「沙箱：」路径。
2. 在节点的 `配置文件` 下拉里选它（下拉列的就是沙箱里的 `*.hoface.json`）。
3. 改也在那边的副本上改 —— 改这个包里的文件**不会**影响 Warudo（配置按文件时间戳失效重读，存盘一秒内生效）。

## 这份配置**不是**校准过的

它只做名字映射，**没有**曲线校准、没有滤波：

* VBridger 自己的输入曲线仓（`.research/vbridger/decrypted/InputCurvesBck...`）是 **68 条全直通**
  （0→0 / 1→1，min/max 0..1），里面没有可搬的校准；
* VBridger 预设里那些增益/曲线是给它**自己的通道集**调的，不是给 ARKit 直通用的；
* 按 [参数空间文档](../../docs/VTS_FACE_PARAMETER_SPACES.md)：表达式值、存档默认值、模型最终中性
  **不必相等**，所以校准是**每演员/每设备**的活，属于你自己那份副本。

## 三种方言与覆盖规则

输入行覆盖：**设备实发拼写**（小写 + `_L/_R`，如 `jawOpen` / `mouthSmile_L`）与
**VTS PascalCase**（如 `JawOpen` / `MouthSmileLeft`）。

⚠️ **同名多行 = 最后一行生效（覆盖/优先级），而且缺数据时不回退**（那一格就是 0）。
所以两种形态键方言会**抢同一个规范名** —— 同时用两台设备是不成立的，
选一台、把不需要的那套行删掉最干净。

⚠️ 头/眼那 12 行必须带自己的**宽曲线**（度数与位置会被默认 0..1 夹成 0/1；
症状是"头姿恒 `(0,0,0)°` 而形态键一切正常"）。头位单位**未标定**。

## 改了生成器或 catalog 之后

```powershell
& .warudo-mod-research\.tools\gen-default-profile.ps1
dotnet run --project .research\profile-json-test
```

第二条会用**我们自己的解析器**（不是肉眼看 JSON）验这份文件：能不能读进来、
52 个规范名与设备拼写是否都在、输出行有没有重复、`Head/*` 宽曲线是否真的透明。
它是"改了生成器或 catalog 之后忘了重新生成"的唯一守门人。
