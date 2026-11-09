# HoFaceStudio 自动化接口

HoFaceStudio 提供本机命名管道 `HoFaceStudio`，供 AI、自动化脚本和测试工具调用。协议版本为 **6**；接口只执行作品、预览、文件与编辑器领域命令，不执行任意 shell。桌面与 CLI 共享 `shared/AutomationContract.cs` 的命令契约。

## 调用方式

先启动 HoFaceStudio，再执行：

```powershell
.\src\HoFace.Console\bin\Release\net10.0\HoFace.Console.exe --help
.\src\HoFace.Console\bin\Release\net10.0\HoFace.Console.exe capabilities
.\src\HoFace.Console\bin\Release\net10.0\HoFace.Console.exe status
.\src\HoFace.Console\bin\Release\net10.0\HoFace.Console.exe screenshot
```

可执行文件位于 `src/HoFace.Console/bin/Release/net10.0/HoFace.Console.exe`。每行请求返回一行 JSON：

```json
{"command":"status","payload":{}}
```

返回包含 `ok`、可选请求 `id` 与 `result`；错误包含 `code` 和 `error`。中文直接以 UTF-8 返回，错误不会输出 .NET 异常堆栈。

推荐用 `--file payload.json` 或 `--stdin` 传递大型文档、中文、Windows 路径，避免多层命令行转义：

```powershell
$cli = '.\src\HoFace.Console\bin\Release\net10.0\HoFace.Console.exe'
& $cli open --file .\open-payload.json
Get-Content -Raw .\curve-payload.json | & $cli setValueCurveKey --stdin
& $cli --batch --file .\requests.jsonl
```

`--batch` 接受逐行 `{"id":"step-1","command":"status","payload":{}}`，每个请求输出一行结果，保留 id。后端拒绝或单行校验失败不会停止后续有效请求；管道 IO 失败会终止批次。命令不支持跨请求事务；需要一次性修改多处结构时，使用完整 `text` 提交，校验后记为一次撤销。

`--timeout-ms` 默认 5000，允许 100–120000，覆盖连接及响应等待。退出码：0 成功；1 后端拒绝；2 参数或 JSON 无效；3 文件/管道 IO 失败；4 超时。超时后先读 `status` / `document` 确认状态，不要盲目重试写入。

## 当前命令

`--help` 离线输出完整契约；`capabilities` 从正在运行的后端读取契约。以下列出主要入口，具体参数以契约为准。

| 命令 | 作用 |
| --- | --- |
| `status` | 返回文档、模式、输入、预览和选中项状态 |
| `document` | 读取完整已应用 value / IR JSON、原文和待应用草稿 |
| `list` | 读取 inputs、outputs、IR 节点、参数与目标；可传 `filter` |
| `selection` | 读取输出行号、节点 id、曲线点、游标和视口 |
| `diagnostics` | 读取实际生效 values/weights、未覆写实时快照和 IR 校验结果 |
| `monitor` | 读取 `raw` VTS 物理输入、`controls` 控制输入、`values` 处理结果和 `weights` IR 权重 |
| `setMonitorOverride` | 按 `stage`、`parameter` 设置临时值 `auto`、`-1`、`0` 或 `1` |
| `clearMonitorOverrides` | 清除指定阶段或全部实时覆写 |
| `screenshot` | 截取完整 WPF 窗口；可传 `{"path":"D:\\temp\\hoface.png"}` |
| `open` | 传 `valuePath` 与 `irPath`；有未保存编辑时拒绝，除非显式 `discardChanges:true` |
| `save` | 保存；可传另存路径；每个文件原子替换，不宣称两个文件构成跨文件事务 |
| `undo` / `redo` | 执行文档撤销和重做 |
| `mode` | 传 `{"value":"Preview"}` 或 `{"value":"RawForward"}` |
| `selectValue` | 按参数名选择 value 行；重名时必须给 `outputIndex`，行号从 0 开始 |
| `selectNode` | 按稳定 id 选择 IR 节点 |
| `selectPoint` | 按 `index` 选择当前图形中的曲线点或混合子项 |
| `cursor` / `clearPreview` | 实际求值临时参数覆盖 / 恢复实时；不修改作品 |
| `setPreviewOverride` | 传 `{"enabled":true}` 进入图形覆盖模式；`false` 立即清除图形覆盖并恢复实时 |
| `evaluate` | 独立求值一帧 raw；不改变实时会话，也不向目标发送 |
| `setValueCurveKey` | 修改 value 曲线指定点的时间、值或切线 |
| `addValueCurveKey` / `deleteValueCurveKey` | 增删曲线点；坐标不能重复，删除时至少保留两个 |
| `setValueExpression` | 修改并校验输出表达式 |
| `setValueModifiers` | 按顺序替换输出规则的 `smooth` / `delay` / `steps` / `jelly` 修饰器链；`list` 会返回完整链与步骤 |
| `setNode` | 修改显示名或合法的 IR 参数轴 |
| `addIrRule` / `deleteIrRule` | 在 root Blend Tree 下增删 Pose、嵌套 Blend Tree 或 Direct 子规则 |
| `deleteIrChild` | 按父节点与索引删除实际存储的 Blend 子项 |
| `clearIrOrphans` | 清理 root 无法到达的规则节点及未引用输出目标 |
| `deletePoseWeight` | 按 Pose ID 和目标 ID 删除一条已存输出权重 |
| `setIrChild` | 修改 IR 1D/2D 子项控制点或 Direct 权重 |
| `setPoseWeight` | 修改 Pose 目标权重 |
| `setDirectWeight` | 修改 Direct 子项常量权重 |
| `vtsStart` / `vtsStop` | 启停 VTS 输入 |
| `previewStart` / `previewStop` | 启停 Blender 最终权重预览 |
| `text` | 传完整 `value` 与 `ir` 文本，校验后替换当前文档 |
| `draftText` / `applyText` / `revertText` | 编辑、应用、还原 JSON 草稿；无效输入不覆盖有效模型 |
| `applyProperties` / `revertProperties` | 应用或还原属性面板草稿 |
| `view` | 读取或设置分栏、窗口尺寸、设置区、编辑区和工作页；中间页索引：0 曲线、1 混合、2 实时监控工作区、3 JSON |
| `recordStart` / `recordStop` | 以 JSONL 记录 raw 帧，传 `{"path":"D:\\temp\\face.jsonl"}` |
| `replayOpen` | 打开 JSONL 录制 |
| `replayPlay` / `replayPause` | 播放或暂停录制 |
| `replaySeek` | 传 `{"time":1.5}` 跳到录制时间 |
| `shutdown` | 返回响应后关闭；默认保护未保存编辑 |

快捷键：`Ctrl+O` 打开、`Ctrl+S` 保存、`Ctrl+Z` 撤销、`Ctrl+Y` / `Ctrl+Shift+Z` 重做。图形中 `F` 适应视图，方向键微调选中点，`Shift` 加大步长，`Delete` 删点，拖动中 `Esc` 取消。图形编辑、表格和 CLI 共用作者文档与撤销栈；撤销保持活动行/节点与图形视口。

## 操作边界

- 先 `status` / `document` / `list`，再修改，最后读回验证；未打开作品时文档命令返回 `INVALID_STATE`。不存在的选择项、无效模式、重名行和非法数值都会显式拒绝。
- 待应用 JSON 或属性草稿不会被图形/CLI 修改静默覆盖。先应用或还原草稿。图形拖动期间拒绝其他写命令，避免一个撤销操作吞并另一个写入。
- 三层监控中，`live` 是物理 raw / 未覆写输入下的完整求值快照，`value` 是逐阶段覆写后实际生效的结果。Raw 与 effective value 使用独立状态求值器，因此 stateful modifiers 不会在一帧内被重复推进。Value 覆写进入逐行 output evaluation，下游 `out("...")` 会看到覆写；同名输出仍按原行序最后一行决定导出值。IR 覆写只影响目标发布，不会改写 `LiveValues`、`LiveWeights` 或 `UnclampedValues`。IR `-1/0/1` 覆写有意绕过目标范围夹取，用于临时压力测试，清除后恢复正常范围。
- `list.nodes` 仅返回 root 可达的树节点；不在混合树中的节点单独放在 `orphanNodes`。Studio 树视图只呈现已引用子规则。`addIrRule` 的 parent 必须是现有 blend/direct 节点；Pose target 可直接输入，缺少的 target 会以 `[0,1]` 默认范围声明。
- Value 输出规则的修饰器在曲线之后按链顺序逐个执行。`smooth` / `delay` 使用 `seconds`；`steps` 按 `trigger` 严格递增，包含 `target`、`hold`、`threshold`、`enterSeconds` 和 `exitSeconds`；`jelly` 是阻尼弹簧，字段为 `strength`（0…1，0 = 直通）、`frequency`（Hz）、`damping`（阻尼比）、`minimum` / `maximum`（输出上下界）。UI 与 `setValueModifiers` 共用该格式；空链保持原规则行为。
- Value 页选择规则会自动打开曲线页，IR 页选择节点会打开混合页；实时监控工作区有 VTS 原始、控制输入、实时处理、实时输出四个独立页签，共用名称/ID 搜索。`view` 的 `monitorStage` 接受 `raw|control|value|ir`，`monitorFilter` 设置搜索词。原有 `centerTab` 索引不变。`cursor` 在曲线页使用表达式值，在混合页使用 X/Y 参数；它与单参数覆写独立。
- `controls` 来自 profile 中不完全由 VTS 标准字段驱动的输入行，按输入参数名识别；标准字段从 profile 或实际数据包取原始拼写，不凭空制造通道。已声明的控制来源不混入 `raw` 列表，未知的实际协议扩展字段仍可在 `raw` 中显示。控制条直接显示 `defaultValue`，CLI 还返回 `usingDefault`、`source`、`fresh`；没有收到来源时显示声明的默认值，收到后缺键则保留最后一个外部值。
- `setMonitorOverride` / `clearMonitorOverrides` 新增 `stage:"control"`，`parameter` 是输入行的参数名（如 `MouthEnabled`）。控制覆盖进入输入行求值后、输出表达式前的规范参数层，离线立即生效；`auto` 恢复外部值或默认值。控制点击不伪造手机数据包、不增加录制帧、不推进会话时钟，也不把控制值写入 RawForward。默认值属于作品配置，临时覆盖不修改默认值、不进入撤销历史。
- 混合图直接显示 1D/2D 实时位置与子项贡献权重。实时位置是蓝色圆点；图形覆盖开启后用橙色十字表示覆盖位置，受影响的 Value 行和 IR 混合节点显示橙色“覆盖”。关闭图形覆盖时清除十字并恢复实时，三阶段监控的独立 pins 不被顺带清除。`cursor` 是显式覆盖操作，会在校验成功后自动开启图形覆盖；`selection.cursor` 返回 `enabled`/`active`，`selection.blend` 返回实时坐标、追踪状态和当前实际显示的 `childWeights`。
- VTS `vtsStart` 在 `Preview` 模式需要已打开作品；`RawForward` 可在无作品状态连接、转发和录制 raw 帧。Processed replay 与 Blender 权重预览仍需作品。图形绘制输入游标与三阶段参数覆写分别管理，`clearPreview` 不会清除 monitor pins。
- `evaluate.raw` 使用 **wire 原始字段名**，例如诊断样例的 `JawOpen`、`MouthSmileLeft`、`MouthSmileRight`。不是归一化后的 `jawOpen` 等名字；可先读取 `list.inputs` 的表达式核对映射。
- 曲线切线可以传字符串 `Infinity` / `-Infinity` 代表阶跃。其他数值必须有限；重复曲线输入坐标被拒绝。
- `screenshot` 渲染 WPF 内容区为 PNG，无需鼠标、键盘、窗口激活或桌面截图。它不包含系统标题栏和独立 ToolTip 弹窗；可用 `view` 切换窗口/标签页后截图。
- 可设置进程级环境变量 `HOFACE_STUDIO_PIPE` 选择隔离管道，桌面与 CLI 必须一致；`HOFACE_STUDIO_NO_WORKSPACE=1` 禁止验收实例读写日常布局偏好。最多 8 个并发连接，空闲客户端不会独占后端；连接空闲 30 秒后释放。

## 可重复验证

```powershell
dotnet build HoFaceStudio.slnx -c Release --artifacts-path ./artifacts/verify-build
dotnet ./artifacts/verify-build/bin/HoFace.Studio.Checks/release/HoFace.Studio.Checks.dll ../..
./tests/Run-AutomationChecks.ps1
```

端到端脚本启动独立管道的真实 WPF 进程，使用样例副本完成读写、保存、撤销、草稿保护、并发、批处理、手动求值与回放输入测试，并生成多个界面状态的截图。它不操作桌面，不连接手机，不写生产作品；测试结束关闭自己启动的进程。

## 后续扩展

录制与回放操作带时间戳的 raw 帧记录、播放游标和独立会话状态。冻结输入、锁定输出和诊断导出仍可在此基础上扩展。录制状态、回放状态和作品文档状态分开返回。

后续可加入：

- `freezeRaw` / `freezeValues` / `freezeWeights`
- `diagnostics` 导出 value 阶段、IR 权重和目标绑定
- `selection` 返回当前曲线点、IR 子项和目标权重

每个命令必须有结构化结果，不能要求 AI 解析 UI 文本猜状态。接口还覆盖 value 曲线、IR 参数轴、1D/2D 控制点、Pose 权重与 Direct 子项权重的读取和提交；图形控件和 AI 命令最终修改同一个作者文档模型，并共用撤销/重做栈。
