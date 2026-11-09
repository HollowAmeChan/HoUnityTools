# HoFaceStudio

这里是计划内嵌于 HoUnityTools 的 Windows 面捕制作控制台工程位置。

当前包括可独立编译的共享 value / IR 核心、WPF 桌面作者工具、本机 CLI/命名管道、VTS 输入、Blender 权重预览及 Unity raw 转发。诊断样例用于验证链路，不代替生产角色调参验收。

当前实现范围与复现命令见 [核心契约](CORE_CONTRACT.md)，阶段状态见 [实施记录](../../docs/face-tracking/HOFACE_IMPLEMENTATION_STATUS.md)。

AI / 自动化接口见 [AI_AUTOMATION.md](AI_AUTOMATION.md)。它通过本机命名管道提供可发现的完整命令、文档与节点读写、后台截图、选区、诊断求值、临时参数覆盖、VTS、预览、保存及录制回放。冻结分阶段求值仍是后续工作。

Release 构建与启动：

```powershell
dotnet build HoFaceStudio.slnx -c Release
.\src\HoFace.Studio\bin\Release\net10.0-windows\HoFaceStudio.exe
```

打开 `.hoface.value.json` 时优先寻找同名 `.hoface.ir.json`，无需重复选择已有配对文件。界面支持筛选列表、独立记忆 Value / IR 选择、曲线/1D/2D 编辑、精确数值、表达式与参数轴、JSON 草稿、保存和撤销/重做。两条分隔条可拖动；连接与文件信息默认收起；底部“收起编辑区”可把空间留给属性。说明集中在悬停提示，错误保留为可操作的行内反馈。布局关闭时保存到本机用户目录，不进入作品。

1D/2D 混合图上直接显示实时蓝色圆点和子项权重。顶部“覆盖”默认关闭；打开后点击、拖动图形进行临时预览，橙色十字与左侧“覆盖”标记指出实际覆盖的位置和项目。关闭覆盖立即恢复实时值；实时观测不会修改控制点、JSON 或撤销记录。

实时监控分为“VTS 原始”“控制输入”“实时处理”“实时输出”四个独立页，共用搜索。Gate 与形态开关在控制输入页显示配置默认值和当前值，离线也可查看及临时覆盖；悬停显示 `× / −1 / 0 / 1`，覆盖变色，`×` 恢复外部输入或配置默认值。CLI 使用 `stage:"control"` 操作控制输入，协议版本为 6。

VTS iPhone UDP 接收已接入制作会话；制作预览执行外部 value + IR，最终权重通过 UDP 发给 Blender 薄适配器。Warudo Mod 不增加作者调试桥接，正式运行由角色组件内部的 `HoVtsIrController` 执行 value + IR；Unity 可挂 `HoFaceStudioRawInput` 接收 raw 帧，让 Unity 的 value、LookAt 和其他消费者继续运行。两种输出模式互斥，录制与作品状态独立。

## 文档

- [双文件与两种调试模式](../../docs/face-tracking/HOFACE_VALUE_IR_ARCHITECTURE.md)
- [Windows UI 选型、交互与工程结构](../../docs/face-tracking/HOFACE_STUDIO_TECH_DECISION.md)
- [实施里程碑与验收](../../docs/face-tracking/HOFACE_IMPLEMENTATION_PLAN.md)

## WTD 测试作品修复

[`repair_wtd_tracking.py`](tools/repair_wtd_tracking.py) 针对现有 WTD 清爽轴的 Value／IR 配对。它不覆盖源文件，只生成候选配对和可交给 Studio 的文本请求：

- 移除瞳孔、眼仁、高光的效果分支、专用轴、开关与 BS 输出声明；左右眼睑及眼部 Gate 不动。
- 眼睑轴改为 `0 常态／0.5 半闭／1 闭眼`。**闭眼只看 blink 一根线性斜坡**：`(blink − 0.10) / 0.70`，0.10 以下算睁（实测笑的时候 blink 中位只有 0.10，中位 0.45 就是半闭），0.80 算满闭（实测完全闭眼 0.99）。旧的 `− 3.9 × squint` 去污项已删掉——它在笑时会减掉 0.6，正是以前「完全闭也冲不到 1」的真凶。
- `−0.2 张大` 由 ARKit 的 `eyeWide` 直接压过去（闭眼时这一项自动失效，不会把眼睛掰开），`1.2 过闭` 没有对应输入，只留给轴值修饰器里的「果冻」推出去。`eyeWide` 两条输入行带标定曲线：实机录音里故意睁大只到 **0.513**、静息 0.036，所以曲线是 **0…0.12 死区、0.12…0.40 线性到满档、0.40 以上算 1**（不标定的话使劲瞪也只有半档，还会让静息那点 0.1 的 wide 把眼睛常年顶宽）；配套的闭眼门是 `clamp((0.50 − blink) / 0.30, 0, 1)`，blink ≤0.20 全额放行。
- 喜悦轴和嘴对齐：喜悦证据取 **`max(嘴的喜悦, 标定后的眯眼证据)`** —— 实测大张嘴笑时 `eyeSquint` 只有 0.05（眯眼那一路按 `(squint−0.06)/0.22` 标定，只在眼睛没闭时算数），所以光靠眯眼永远拉不满，必须让嘴带着眼睛走，同时保留「嘴不笑、只有眼睛眯」的那种笑。
- 皱眉默认算**悲伤**（`Eye/*/Emotion` 拉到 −1），愤怒要手动开 `AngerEnabled`（`Gate/Anger`，默认 0）：开着时皱眉才进愤怒表，wink 保护（同侧压眉只在睁眼时保留、闭眼要双侧压眉）照旧。这个开关放在 value 层，因为 IR 的 `blend1d/blend2d` 子项不读 `weightParameter`，挂在愤怒分支上会把整张主表一起淡掉。
- 果冻（修饰器 `kind: "jelly"`）是一维阻尼弹簧：`strength` 0…1 是强度，`frequency`（Hz）、`damping`（阻尼比，1 = 不过冲）是手感，`minimum／maximum` 是输出上下界。**强度 0 时完全直通**（值和没有这个修饰器一模一样、绝不会越过两个界）；强度 > 0 才把弹簧过冲混进来，并夹在上下界之间。Studio 里选中修饰器链中的「果冻」即可调这三个数，`setValueModifiers` 走同一份字段。修脚本重跑时会**沿用行上已有的果冻参数**，不会把手上调好的手感冲掉。
- 嘴宽继续使用现有 `0／0.5／1` 的缩、常、宽。把有符号的原值交给曲线，而不是直接夹到 0～1。保留旧 −0.93、+2 端点；中性平台默认 −0.14～+0.40，包含旧静置点及当前约 +0.356 的静置样本。
- 左右一律按 ARKit 第一人称（`Left` = 角色自身左侧）。 `graph.mouth.move` 的 `嘴位置-Left` 放 `Mouth/MoveX` 正侧、`嘴位置-Right` 放负侧；`graph.jaw` 相反，因为 `Jaw/Side = jawRight − jawLeft` 的正侧是角色右侧。迁移时两张表的两端都被接反，嘴和下巴会一起朝反方向走。
- 鼓嘴不再区分左右（ARKit 只有一路 `cheekPuff`，采不到单侧，也没法把它从「歪嘴 + 鼓嘴」里分出来）。中位时两侧一起鼓，量只取 `cheekPuff`（`--cheek-knee` 起、`--cheek-full` 满，默认 0.40、0.60，膝盖在实测噘嘴上界 0.1769 与横移噪声 ≤0.35 之上）；嘴巴往哪一侧平移，就按同比例削弱那一侧的鼓嘴量，另一侧保持满档。鼓嘴姿势的 1D 刻度从 0.39 挪到轴满 0.65，削弱才有过渡可见。
- 鼓嘴生效时按 `1 − 鼓嘴量` 关掉 `Gate/MouthMove`：鼓嘴自身已推动嘴唇，再叠上嘴整体位移就会变成歪嘴。旧控制器是同一条规矩（鼓腮关嘴平移）；削弱单侧仍然读 `out("Mouth/MoveX")` 的原值，不受这道门影响。

从 Studio 目录运行，`$value`、`$ir` 为修复前备份，`$work` 为新的输出目录。先按上文构建 Release：

```powershell
python .\tools\repair_wtd_tracking.py $value $ir $work
.\tests\Test-WtdTrackingRepair.ps1 -BeforeValue $value -BeforeIr $ir `
    -Value "$work/candidate.value.json" -Ir "$work/candidate.ir.json"
```

验证脚本使用同一个共享求值器回归默认标定。通过后，在 Studio 打开确认过的实际文件，备份并确认没有草稿；使用 CLI `text --file <apply-payload.json>`、`save` 写回。整套规则进入一次撤销，不需要改变 IR 文件格式、果冻组件或 Blender 模型。

## 工程约定

- 推荐 WPF + .NET 10，选型需要用真实交互原型确认。
- `.hoface.value.json` 处理参数，`.hoface.ir.json` 组合静态形态键姿势。
- 默认外部求值并发送最终权重；另有裸值转发至现有角色接收组件的测试模式。
- 共享核心的计划主本位于 `Runtime/FaceTracking/Core/`，桌面项目链接编译，UI 不进入核心。
- Warudo 的角色内嵌模块服务于发布运行和角色链路测试，不作为默认制作调试后端。
- `Tools~` 内的桌面源码不进入 Unity 常规脚本导入；构建输出继续忽略。
- 创建真实 `.csproj` 时需添加本目录专用的 Git 入库例外；当前仓库默认忽略 `.csproj`。

## 同工作区查看两端

打开 [HoFace.authoring.code-workspace](HoFace.authoring.code-workspace)，可同时查看：

1. HoUnityTools：运行时代码主本、桌面工程与文档；
2. `BreakWarudo/Assets/HoWarudoModTests`：网络插件、节点、现行同步清单和验证工具。

相对路径按本机现有 `D:/Unity_Fork/HoUnityTools` 与 `D:/Unity_Project/BreakWarudo` 布局设置。配置不会自动改变当前 Codex 窗口的工作区；其他机器需要调整第二个目录。
