# Tools~/FaceTracking/archive —— 2026-09-29 之前的一次性探针

这些**不再是流程的一部分**，但都还能跑、也有当年的结论。要复用先读它自己的头部注释。
（为什么收在这里：`docs/REORG_PLAN_2026-09-29.md`。现在在跑的脚本在上层 `../README.md`。）

## 语料 / 录样（被 `../ho-traces.py` 那一套取代）

| 文件 | 干什么 |
| --- | --- |
| `extract-takes-from-log.ps1` | 从 Unity `Editor.log` 里把 `[Ho 面捕统计]` 的段子捞出来（面板统计文本的来源） |
| `label-takes.ps1` | 给「录 5 秒」的原始 dump 插 `=== <标签>` 行（一段一个标签） |
| `list-take-labels.ps1` | 列出各语料里的段标签 |
| `filter-takes.ps1` | 合并若干语料并按标签子串剔除（例如把"鼓嘴"那几组剔掉） |
| `residual-channel-census.ps1` | 逐动作组统计"嘴部哪些通道在动" |
| `census-matrix.py` | 把上面那份 markdown 统计压成通道矩阵 |
| `eye-census.py` | 眼睛那批的通道普查（静止 / 眯眼 / 眯眼笑 各 3 段） |
| `eye-scenarios.py` | 把眼族实测均值写成 `chain-probe` 的 wire dump |

## 中间层 / 控制器（被 `../check-controller.ps1` / `../profile-row-diff.py` 取代）

| 文件 | 干什么 |
| --- | --- |
| `mouth-audit.py` | 嘴系统的机械审计（只读）：规则 / 耦合 / 顺序 / 计数 |
| `param-usage.py` | 哪些控制器参数**真的被树消费**了（决定性清单） |
| `clip-progress.py` | 槽位片段进度探针：每个 `.anim` 到底有没有真曲线 |
| `check-payload-pairs.ps1` | 检查 patch 载荷里的 `#PAIR` 两侧是否都到位 |
| `apply-doc-edits.ps1` | 对一份 UTF-8 文本批量做「旧串 → 新串」替换（当年的文档批量编辑） |

## 资产 surgery（嘴部隔离那一轮）

| 文件 | 干什么 |
| --- | --- |
| `isolate-regions.py` | 生成单区域隔离的控制器剪枝副本（保留原动画引用；配 `docs/MOUTH_ISOLATION_DIAGNOSIS_2026_09_28.md`，已归档） |
| `sync-mouth-points.py` | 把用户手调的六点锚点同步到 9/11 点细分版（其余中间点插值当待测初值） |

## 杂项

| 文件 | 干什么 |
| --- | --- |
| `fix-doc-encoding.ps1` | 编辑工具跑完后把文档恢复成「UTF-8 带 BOM + CRLF」 |
| `fix-raw-newlines.py` | 修 JSON 字符串里**裸换行**（转义掉） |
| `sync-pkgcopy.ps1` | 把包镜像到 `.research/pkgcopy`，给一次性验证工程用 |

## C# 探针工程（`dotnet run --project <目录>`）

| 目录 | 干什么 |
| --- | --- |
| `chain-probe/` | 脱离 Unity 跑**真参数层链**（`HoFaceChain` / `HoFaceMiddleware` …，由 `sync.ps1` 从 Runtime 同步） |
| `eye-probe/` | 用**运行期那版**求值器（含 `out()` 回调）按 profile 行序把整条链算一遍 |
| `expression-coverage/` | VBridger 隐式输入层 × 我们中间层表达式的**覆盖验证**（VB 对齐那轮的取证） |
| `profile-json-test/` | profile JSON 编解码的离线往返测试 |
| `take-analysis/` | 把面板统计变成"哪个特征最能分开动作"（左右/上下那些目标文件的来源） |

⚠️ 这些工程的 `bin/` `obj/` 已不再跟踪（见仓库根 `.gitignore`）；要跑就 `dotnet run --project` 现编。
