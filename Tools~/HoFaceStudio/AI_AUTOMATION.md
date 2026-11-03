# HoFaceStudio 自动化接口

HoFaceStudio 提供本机命名管道 `HoFaceStudio`，供 AI、自动化脚本和测试工具调用。接口只操作 HoFaceStudio 领域，不执行任意 shell、PowerShell 或文件系统命令。

## 调用方式

先启动 HoFaceStudio，再执行：

```powershell
HoFace.Console.exe status '{}'
HoFace.Console.exe screenshot '{}'
```

可执行文件位于 `src/HoFace.Console/bin/Release/net10.0/HoFace.Console.exe`。每行请求返回一行 JSON：

```json
{"command":"status","payload":{}}
```

错误返回 `ok: false` 和 `error`。

## 当前命令

| 命令 | 作用 |
| --- | --- |
| `status` | 返回文档、模式、输入、预览和选中项状态 |
| `screenshot` | 截取完整 WPF 窗口；可传 `{"path":"D:\\temp\\hoface.png"}` |
| `open` | 传 `valuePath` 与 `irPath`，打开并校验一对文件 |
| `save` | 原子保存当前两个文档 |
| `undo` / `redo` | 执行文档撤销和重做 |
| `mode` | 传 `{"value":"Preview"}` 或 `{"value":"RawForward"}` |
| `selectValue` | 按参数名选择 value 行 |
| `selectNode` | 按稳定 id 选择 IR 节点 |
| `cursor` | 设置当前预览游标 |
| `vtsStart` / `vtsStop` | 启停 VTS 输入 |
| `previewStart` / `previewStop` | 启停 Blender 最终权重预览 |
| `text` | 传完整 `value` 与 `ir` 文本，校验后替换当前文档 |
| `recordStart` / `recordStop` | 以 JSONL 记录 raw 帧，传 `{"path":"D:\\temp\\face.jsonl"}` |
| `replayOpen` | 打开 JSONL 录制 |
| `replayPlay` / `replayPause` | 播放或暂停录制 |
| `replaySeek` | 传 `{"time":1.5}` 跳到录制时间 |

快捷键仍是主要人工操作入口：`Ctrl+O` 打开、`Ctrl+S` 保存、`Ctrl+Z` 撤销、`Ctrl+Y` 重做。保存、撤销和重做不再依赖顶部按钮。

## 后续扩展

录制与回放操作带时间戳的 raw 帧记录、播放游标和独立会话状态。冻结输入、锁定输出和诊断导出仍可在此基础上扩展。录制状态、回放状态和作品文档状态分开返回。

后续可加入：

- `freezeRaw` / `freezeValues` / `freezeWeights`
- `diagnostics` 导出 value 阶段、IR 权重和目标绑定
- `selection` 返回当前曲线点、IR 子项和目标权重

每个命令必须有结构化结果，不能要求 AI 解析 UI 文本猜状态。
