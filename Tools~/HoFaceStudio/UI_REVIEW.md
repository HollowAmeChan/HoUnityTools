# HoFaceStudio UI / 交互验收

验收日期：2026-11-03。范围是桌面作者工具及 AI CLI，不代替手机、生产角色、Blender 或 Warudo 的完整艺术效果验收。

| 用户问题 | 当前实现 | 验证入口 |
| --- | --- | --- |
| 模块说明常驻过多 | 操作说明使用原生 ToolTip；连接设置与文件路径默认收起；行内仅保留实际错误、字段标签和状态 | `MainWindow.xaml`、`App.xaml`；后台截图的默认/展开设置状态 |
| 区域不能调整大小 | 结构列表、编辑器、属性间使用两个原生 GridSplitter；JSON 内部有纵向分隔条；关闭保存布局比例 | `MainWindow.Workspace.cs`；`view` 调整后检查实际列宽 |
| 中间面板过大 | 默认图形宽度与属性接近；提供“收起编辑区”，收起后列宽为 0，展开恢复比例 | `Run-AutomationChecks.ps1` 的收起/恢复断言与 `pose-focus.png` |
| 撤销会切活动项 | Value 按参数与重名序号恢复选择，IR 按节点 ID；筛选不销毁选择；两类标签页分别记忆 | `HoFace.Studio.Checks`：跨选择撤销/重做、过滤、标签页、文本重排与重排撤销 |
| 编辑行为不完整 | 表格与图形联动；曲线增删、键盘微调、取消拖动；表达式/参数轴可编辑；2D 每个子项一行 X/Y | 同一作者文档及历史栈；原生控件状态和文档回读 |
| 失败编辑丢内容 | JSON 与属性草稿计入未保存状态；无效输入保留；失败修改回滚，刷新模型引用；打开/关闭默认保护未保存作品 | 交互回归中的无效表达式、JSON、重复曲线点、参数轴和关闭/打开保护 |
| 游标只改变图标 | 临时参数覆盖实际调用共享 IR 核心，持续跨输入帧生效；“恢复实时”清除覆盖 | 真实 IPC 测试用中性回放帧验证覆盖保持，再清除并验证实际源权重 |
| AI 只有写命令，无法核对 | 共享命令契约、完整文档/节点读取、精确写入、诊断、后台截图、批处理、请求 ID、结构化错误和超时 | `AI_AUTOMATION.md`、CLI `--help` / `capabilities`、端到端脚本 |

## 复现

在仓库根目录执行：

```powershell
dotnet build Tools~/HoFaceStudio/HoFaceStudio.slnx -c Release
dotnet Tools~/HoFaceStudio/tests/HoFace.Studio.Checks/bin/Release/net10.0-windows/HoFace.Studio.Checks.dll .
./Tools~/HoFaceStudio/tests/Run-AutomationChecks.ps1 -BuildRoot ./Tools~/HoFaceStudio/src
dotnet Tools~/HoFaceStudio/tests/HoFace.Core.Checks/bin/Release/net10.0-windows/HoFace.Core.Checks.dll .
```

端到端脚本会打印当次 artifacts 目录，保留默认、最小窗口、二维混合、姿势、收起编辑区、JSON 草稿和展开设置的 PNG。截图来自 WPF 渲染命令，验收过程不操纵桌面。

共享核心与传输另外有回归；本次只报告所执行检查的实际结果，不将通过诊断样例视为生产角色调参完成。
