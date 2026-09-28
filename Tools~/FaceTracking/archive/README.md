# Tools~/FaceTracking/archive —— 已删除的旧工具（墓碑索引）

2026-09-29 重组时，这里的一次性探针与旧工具**已按用户的话删掉**（不是"挪走"）。本文件留档：**曾经有什么、它是干什么的、结论现在在哪、要恢复怎么捞**。

> 恢复任意一份：`git log --diff-filter=D --name-only -- Tools~/FaceTracking/archive/` 找到删除提交，
> 再 `git show <提交>^:Tools~/FaceTracking/archive/<文件名> > <某处>` 取回。
> 现在在跑的脚本在上层 [`../README.md`](../README.md)；为什么删、删了哪些，见 `docs/REORG_PLAN_2026-09-29.md`。

## 语料 / 录样类（功能被 `../ho-traces.py` + `../census-table.py` 取代）

| 曾有的文件 | 当年干什么 | 结论现在在哪 |
| --- | --- | --- |
| `extract-takes-from-log.ps1` | 从 Unity `Editor.log` 里把 `[Ho 面捕统计]` 段子捞出来 | `ho-traces.py`（直接读 `Logs/HoFaceTraces/*.jsonl`） |
| `label-takes.ps1` / `list-take-labels.ps1` / `filter-takes.ps1` | 给原始 dump 插标签 / 列标签 / 合并并剔除某些段 | 打标签脚本改放 `.research/*/label-*.py`（录样后按时间顺序打，写 `recording-labels-*.json`） |
| `residual-channel-census.ps1` / `census-matrix.py` | 逐动作组统计"哪些嘴部通道在动" | `census-table.py`（解析面板统计文本）+ `ho-traces.py stat --group` |
| `eye-census.py` / `eye-scenarios.py` | 眼族三态普查 / 写成 `chain-probe` 的 wire dump | 眼族结论见 `docs/AXES.md` §4、`docs/PARAMETERS.md` §2 |

## 中间层 / 控制器类（功能被 `../check-controller.ps1` / `../profile-row-diff.py` / `../tree-dump.py` 取代）

| 曾有的文件 | 当年干什么 | 结论现在在哪 |
| --- | --- | --- |
| `mouth-audit.py` | 嘴系统的机械审计（规则 / 耦合 / 顺序 / 计数） | 检查器 + `docs/CONTROLLER.md` |
| `param-usage.py` | 哪些控制器参数真的被树消费 | `check-controller.ps1` 的可达性检查 |
| `clip-progress.py` | 每个槽位 `.anim` 到底有没有真曲线 | `clip-dump.py`（逐个看）+ `ho-traces.py` |
| `trace-row-dump.py` | 一次录样里某几行的取值与元数据 | `ho-traces.py` 的 `info` / `keys` / `rows` / `series`（同一件事，已合并） |
| `probe-arkit-keys.py` | 探 ARKit 那套键与片段的配对（大小写不敏感） | ARKit 直通那轮做完即弃；配对规则见 `docs/CONTROLLER.md` |
| `check-payload-pairs.ps1` / `apply-doc-edits.ps1` | patch 载荷配对检查 / 文档批量替换 | 一次性，已完成 |

## 资产 surgery 类（嘴部隔离那一轮，已收尾）

| 曾有的文件 | 当年干什么 | 结论现在在哪 |
| --- | --- | --- |
| `isolate-regions.py` | 生成单区域隔离的控制器剪枝副本 | `docs/archive/MOUTH_ISOLATION_DIAGNOSIS_2026_09_28.md` |
| `sync-mouth-points.py` | 六点锚点同步到 9/11 点细分版 | `docs/DECISIONS.md` §5.7.33 一带 + `AXES.md` §10 存疑 1 |

## C# 探针工程（`dotnet run --project <目录>`，已删除）

| 曾有的目录 | 当年干什么 | 结论现在在哪 |
| --- | --- | --- |
| `chain-probe/` | 脱离 Unity 跑真参数层链 | 已有检查器 + `profile-verify` + 台架 |
| `eye-probe/` | 用运行期求值器按 profile 行序算整条链（含 `out()` 回调） | 同上 |
| `expression-coverage/` | VBridger 隐式输入层 × 我们表达式的覆盖验证 | `docs/DECISIONS.md`（VB 对齐那几节）+ `docs/archive/VBRIDGER_*` |
| `profile-json-test/` | profile JSON 编解码离线往返测试 | `Tools~/FaceTracking/profile-verify/`（真读取器） |
| `take-analysis/` | 面板统计 → "哪个特征最能分开动作" | `Tools~/FaceTracking/census-table.py` + `docs/AXES.md` 的实测定论 |

## 杂项（一次性，已完成）

`fix-doc-encoding.ps1`（文档恢复 BOM+CRLF）· `fix-raw-newlines.py`（修 JSON 里裸换行）· `sync-pkgcopy.ps1`（镜像到 `.research/pkgcopy`）。

⚠️ 规矩没变：**要恢复的是"方法"，不是"结论"** —— 结论都进了 `docs/` 那几份权威文档；恢复脚本前先看它头部注释与当年的结论。
