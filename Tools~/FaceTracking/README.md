# Tools~/FaceTracking —— 在跑的脚本（不是探针）

这里只放**决定并验证你工程里那份 `PTP_CTR_Face_VTS.controller`** 的脚本，以及**录样的查询/算账工具**。
一次性探针、旧实验、被取代的工具全部在 `archive/`（可查、可复用，但不占主目录；2026-09-29 重组，见 `docs/REORG_PLAN_2026-09-29.md`）。

## A. 流程（生成 → 接线 → 检查）

| 脚本 | 干什么 | 跑法 |
| --- | --- | --- |
| `make-vts-controller.ps1` | 从 profile 生成控制器骨架（区域 → 表 / Direct 张量积表 / 1D 表 / 开关 → 槽位 + 空片段）。**已存在的片段不覆盖**（作者烘进去的姿势安全） | `powershell -File Tools~/FaceTracking/make-vts-controller.ps1 -Template <模板.controller> [-ArkitPassthrough -ArkitProfile <p>] [-NoClips]`<br>⚠️ `-Template` **必填**；PowerShell 5.1 下还要显式给 `-ArkitProfile`（`$PSScriptRoot` 在 param 默认值里是空的） |
| `wire-slot-clips.ps1` | 把 `Animations/` 里的片段接进槽位（按 `<槽位名>` 或 `HO-<槽位名>` 匹配，优先 On；幂等，会打印缺哪些） | `powershell -File Tools~/FaceTracking/wire-slot-clips.ps1 -Apply`<br>⚠️ **生成完必须跑**，否则槽位是空的（"动画没了"多半是这一步没跑） |
| `check-controller.ps1` | 对着 profile 核控制器：参数名/默认值/树型/轴接线/槽位名/槽位总数/从根可达性/每格坐标 | `powershell -File Tools~/FaceTracking/check-controller.ps1 -Path <x.controller>` |
| ↑ **隔离模式** `-Isolation` | 只查这份控制器**有**的东西（`DIAG_*` 单区控制器天然缺树/缺参数）：「缺」降级成注，树型/轴接线/**刻度与逐格坐标**/槽位名/变体镜像/WD/Normalize 照旧严格。**验收基准就是这些隔离控制器** | `… -Path <DIAG_xxx.controller> -Isolation` |
| `check-controller-integrity.py` | 不依赖 profile 的**完整性**检查：块结构、悬空引用、状态机接线（"文件坏没坏一跑就知道"） | `python Tools~/FaceTracking/check-controller-integrity.py <x.controller>` |
| `make-checker-fixture.ps1` | 造一份"形状正确"的夹具，验检查器的**通过路径**（检查器自己坏了也要能发现） | `powershell -File Tools~/FaceTracking/make-checker-fixture.ps1` |
| `fix-slot-guids.ps1` | 把槽位片段按 `md5('ho-face-slot:<槽位名>')` 的 GUID 规则摆回去（生成器没有资产库，GUID 靠名字推） | 见脚本头部 |
| `fix-bom.ps1` | 给本目录 `.ps1` 补 UTF-8 BOM —— PowerShell 5.1 对**没有 BOM** 的 `.ps1` 按 ANSI 读，中文全糊 | `powershell -File Tools~/FaceTracking/fix-bom.ps1` |
| `HoSlotNames.psd1` | **槽位名权威**（树 → 每格的名字）；生成器与检查器都读它 | 数据文件 |
| `templates/` | 生成器要的**模板控制器**（借 Unity 亲手写的序列化形状） | 数据文件 |

### profile 侧的验证

| 工具 | 干什么 | 跑法 |
| --- | --- | --- |
| `profile-verify/`（C#） | 用**真读取器**解析 `.hoface.json` 并报 `problems`。⚠️ **改完 profile 必跑** —— 括号少一个它会直接说 `expression does not parse` | `dotnet run --project Tools~/FaceTracking/profile-verify -- <x.hoface.json>`（原来 `bin/` 里那份 exe 已不再跟踪） |

## B. 录样（`Logs/HoFaceTraces/*.jsonl`）的速查 / 速统 / 速对

录样格式：**第 1 行头部**（`label` / `intervalSeconds` / `profileJson` …）、**中间一行一个 sample**（`inputs[]` = 配置输入行、`outputs[]` = 输出行、`wires[]` = 原始线）、**最后 1 行页脚**（`kind=end` / `samples`）。
命名空间：inputs **裸名**、outputs 加 `out:`、wires 加 `w:`；take 可用**序号 / 文件名子串 / 组名**指定。

| 脚本 | 干什么 | 例子 |
| --- | --- | --- |
| `ho-traces.py` | **速查 / 速统 / 速对**一把抓：`ls` `info` `keys` `rows` `stat` `series` `check` `diff` `csv` | `python Tools~/FaceTracking/ho-traces.py ls`<br>`… stat --group 静置 --channels jawOpen mouthClose`<br>`… diff 4 7 --moved`<br>`… check` |
| `census-table.py` | 解析**面板导出的统计文本**（`=== 组名` + 每次 `min avg max 波动`）按组打表 —— 翻老语料用 | `python Tools~/FaceTracking/census-table.py .research/takes-side.txt` |
| `jaw-take-census.py` | 下巴那批：通道定名、**阶梯归因**（输入没解释的跳变）、表达式 A/B、锁存门模拟 | `python Tools~/FaceTracking/jaw-take-census.py` |
| `jaw-side-fit.py` | 「平移下巴」那批：各组横向振幅 / `(JawSide, Jaw)` **落点** / 伪影对照 / 离现有格子的距离 | `python Tools~/FaceTracking/jaw-side-fit.py` |
| `trace-row-dump.py` | 一次录样里某几行的取值与元数据（含"运行时用的是哪份表达式"的对账） | `python Tools~/FaceTracking/trace-row-dump.py <x.jsonl> Mouth/Y` |

## C. 资产/配置的速查与对照

| 脚本 | 干什么 | 例子 |
| --- | --- | --- |
| `tree-dump.py` | 从 `.controller` 打一棵 BlendTree（子节点名 / 坐标 / 阈值 / 权重参数）—— 查"格子摆在哪" | `python Tools~/FaceTracking/tree-dump.py <x.controller> MouthJaw` |
| `clip-dump.py` | 速查一个 `.anim` 写了什么（形变键 + 值）—— 判断作者在某个姿势里烘了多少东西 | `python Tools~/FaceTracking/clip-dump.py <x.anim> --top 12` |
| `profile-row-diff.py` | 若干份 profile 的同一批行并排打（表达式 / 修饰符 / 曲线 / notes 尾 + 括号平衡） | `python Tools~/FaceTracking/profile-row-diff.py "Mouth/Jaw" a.json b.json` |
| `controller-block-diff.py` | 两份 `.controller` 的**语义**对照（fileID 归一化后按块比多重集；文本 diff 会被块顺序淹没） | `python Tools~/FaceTracking/controller-block-diff.py a.controller b.controller` |
| `probe-arkit-keys.py` | 探 ARKit 那套键与片段的配对（大小写不敏感） | `python Tools~/FaceTracking/probe-arkit-keys.py` |

## ⚠️ 生成前先切走 Animator 窗口

生成器是**纯文本写盘**（不走 AssetDatabase 导入）⇒ Unity 重新导入时旧子资产被销毁，
而正开着那份 `.controller` 的 Animator 窗口还攥着旧引用 ⇒ 刷屏
`MissingReferenceException: … AnimatorStateMachine … has been destroyed`。
**症状无害**（资产是好的）：切走窗口 → 右键资产 **Reimport** → 再打开即可。
⚠️ 同理：**别人正在 Unity 里调那份资产时，别写它**（2026-09-29 栽过一次）。

## ⚠️ 三处同步（这坑真栽过两次）

同一份控制器结构有**三份实现**，改结构必须一起改：

| 实现 | 谁在用 | 备注 |
| --- | --- | --- |
| `Editor/FaceTracking/HoFaceControllerSkeletonBuilder.cs` | 编辑器面板里"装配控制器" | **生成器不编它** ⇒ 只改它、生成结果一点不变（2026-09-28 白跑一轮）；⚠️ 它现在**落后于**生成器（`MouthJaw` 还是 1D 2 格） |
| `Tools~/FaceTracking/make-vts-controller.ps1` | 命令行生成 | 真正产出 rig 里那份资产的就是它 |
| `Tools~/FaceTracking/check-controller.ps1` | 机器核验 | 期望值写在这里：树型、槽位规则、刻度清单、槽位总数 |

## ⚠️ 两个编码/写盘坑

* `.ps1` / `.psd1` / `docs/*.md` 都要 **UTF-8 BOM**（PS 5.1 按 ANSI 读没 BOM 的脚本，中文全糊）；用 `edit` 工具改过之后 BOM 会掉，改完 **必查**。
* rig 那份 `.controller` **原本没有 BOM**，别用 `utf-8-sig` 去写它。

## archive/

`archive/` = 2026-09-29 之前的一次性探针与旧工具（`isolate-regions.py` / `sync-mouth-points.py` / `eye-*` / `*-census` / `chain-probe` / `eye-probe` / `profile-json-test` / `take-analysis` / `expression-coverage` …）。
它们仍然可跑，但**不再是流程的一部分**；要复用先读脚本头部的用法与当年的结论。
