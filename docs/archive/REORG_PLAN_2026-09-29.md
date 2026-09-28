# 文档与工具重组：审查与方案（2026-09-29）

> 起因（用户定）：「文档区、tools 区文件太他妈多了，堆了好多天的旧实验旧工具旧描述以及已经被推翻的事情」。
> 目标：**权威文档尽量集中、只描述现在在跑的最终方案**；旧材料拆进 `pitfalls/`（踩坑）与 `archive/`（归档）；
> 被取代的平行工具直接删（git 历史里仍查得到）。**先完整 review，再动刀** —— 本文件就是那次 review。

## 1. 现状清点

### 1.1 docs/（git 跟踪 106 个文件）

| 区 | 规模 | 状态 |
| --- | --- | --- |
| 顶层 | 20 篇 md ≈ 800 KB | **主要问题区**：混着设计、研究、状态快照、被推翻的结论 |
| `archive/` | 7 篇 | 已归档，OK |
| `pitfalls/` | 16 篇 | 踩坑，OK（部分与顶层重复） |
| `完善的功能/` | 6 篇 | **活跃**（今天还在改）；与面捕主线无关，是约束/注视/Warudo 那些功能 |
| `measurements/` | **空** | 但代码里还引用 `docs/measurements/README.md` ⇒ **悬空引用** |

顶层 20 篇按证据分类（`引用` = 被 `*.cs` 注释引用；日期 = 最后一次改动）：

| 文档 | 日期 | 引用 | 判定 |
| --- | --- | --- | --- |
| `VTS_HQ_CONTROLLER.md` (279 KB) | 09-29 | 4 | **拆**：现状 → `AXES.md` + `CONTROLLER.md`；历史 → `DECISIONS.md` |
| `FACE_TRACKING_MIDDLE_LAYER.md` | 09-27 | 6 | **留并合并**进 `PIPELINE.md`（中间层机制） |
| `FACE_TRACKING_CONTROLLER_STRUCTURE.md` | 09-26 | 4 | **留并合并**进 `CONTROLLER.md` |
| `FACE_TRACKING_NAMING.md` | 09-27 | 1 | **留**（命名权威，与 `VTS_HIGH_QUALITY_FACE_CATALOG.json` 配套） |
| `FACE_TRACKING_DYNAMIC_PARAMETERS.md` | 09-27 | 2 | **留**（Hub 规格）；与 pitfalls 里同名那篇分工写清 |
| `BLEND_TREE_LIMITS.md` (35 KB) | 09-27 | 1 | **留**（混合树实测边界），可归到测量 |
| `PARAMETER_HO.md` (64 KB) | 09-27 | 1 | **留**（我们的参数规范） |
| `PARAMETER_DEVICE_VERIFICATION.md` | 09-26 | 1 | **留**（设备实测表） |
| `PARAMETER_STANDARDS.md` (84 KB) | 09-27 | 0 | **留**（形态键/参数的参考底表） |
| `VTS_HIGH_QUALITY_FACE_CONTRACT.md` (51 KB) | 09-26 | 0 | **待读**：与目录 JSON 是否重复，重复就归档 |
| `FACE_TRACKING_WARUDO_ROUTE.md` (80 KB) | 09-27 | 0 | **待读**：总览里"已完成/已推翻"的部分要摘掉，只留现在这条路 |
| `FACE_TRACKING_DESIGN.md` | 09-27 | 0 | **合并**（机制层已验证结论）→ `PIPELINE.md`，然后归档 |
| `FACE_TRACKING_WORKFLOW.md` | 09-27 | 0 | **合并** → `PIPELINE.md` §5/§6 与 `../Tools~/FaceTracking/README.md` |
| `ANIMATOR_OUTPUT_PIPELINE_DESIGN.md` | 09-26 | 0 | **待读**（输出范围/物理阶段）→ 多半并进 `CONTROLLER.md` |
| `VTS_FACE_PARAMETER_SPACES.md` | 09-27 | 0 | **归档**（研究） |
| `VTS_CREATOR_WORKFLOW_RESEARCH.md` | 09-25 | 0 | **归档**（研究） |
| `FACE_PIPELINE_STATUS_2026_09_26.md` | 09-26 | 0 | **归档**（带日期的状态快照） |
| `MOUTH_ISOLATION_DIAGNOSIS_2026_09_28.md` | 09-29 | 0 | **归档**（带日期的诊断） |
| `MOUTH_REVIEW_2026-09-28.md` | 09-29 | 0 | **归档**（带日期的 review） |
| `完善的功能/PENDULUM_CONSTRAINT.md` | 09-24 | 1 | **移到** `完善的功能/`（它是功能文档，不是面捕） |
| `README.md` | — | — | **重写**：唯一入口（现在在跑什么 + 去哪看） |

### 1.2 Tools~/（git 跟踪 93 个文件；`Tools~/FaceTracking` 下 37 个脚本 + 8 个子目录）

| 类别 | 文件 | 判定 |
| --- | --- | --- |
| **在跑的流程** | `make-vts-controller.ps1` · `check-controller.ps1` · `wire-slot-clips.ps1` · `HoSlotNames.psd1` · `fix-slot-guids.ps1` · `fix-bom.ps1` · `check-controller-integrity.py` · `make-checker-fixture.ps1` · `templates/` | **留** |
| **录样速查/速统/速对**（09-29 新建） | `ho-traces.py` · `census-table.py` · `jaw-take-census.py` · `jaw-side-fit.py` · `tree-dump.py` · `clip-dump.py` · `profile-row-diff.py` · `trace-row-dump.py` · `controller-block-diff.py` | **留** |
| **C# 探针工程** | `profile-verify/`（README 引用，真读取器） | **留** |
| | `chain-probe/` · `expression-coverage/` · `eye-probe/` · `profile-json-test/` · `take-analysis/` | **归档**（09-27 那批一次性探针；已被 09-29 那套查询工具取代） |
| **一次性探针脚本**（09-27 那批） | `eye-census.py` · `eye-scenarios.py` · `mouth-audit.py` · `param-usage.py` · `census-matrix.py` · `residual-channel-census.ps1` · `filter-takes.ps1` · `extract-takes-from-log.ps1` · `label-takes.ps1` · `list-take-labels.ps1` · `clip-progress.ps1` · `check-payload-pairs.ps1` · `sync-pkgcopy.ps1` · `fix-doc-encoding.ps1` · `fix-raw-newlines.py` · `apply-doc-edits.ps1` · `isolate-regions.py` · `sync-mouth-points.py` | **归档**（`isolate-regions.py` / `sync-mouth-points.py` 是嘴部隔离那轮的，先归档不删） |
| **垃圾（已跟踪！）** | `bin/` `obj/` 共 **28** 个 · `__pycache__` **1** · `.pyc` **1** | **删 + 补 `.gitignore`**（本轮已做） |

⚠️ 另外发现：`FACE_TRACKING_WARUDO_ROUTE.md` 等文档被 `.cs` 注释以**旧路径**引用（`docs/完善的功能/EDITOR_UI_SYSTEM.md` / `docs/LOOKUP…` / `docs/measurements/README.md`），
而文件其实在 `docs/完善的功能/` 或根本不存在 ⇒ 重组时必须**逐个对一遍悬空引用**（改 `.cs` 会触发你 Unity 重编译，等你不在调的时候再做）。

## 2. 目标结构（提议）

```
docs/
  README.md          ← 唯一入口：现在在跑什么 / 从哪看（一页）
  PIPELINE.md        ← 单一权威：VTS 裸输入 → 中间层 → 控制器 → Warudo（合并 middle-layer / design / workflow）
  AXES.md            ← 每根轴的口：表达式 / 曲线 / 刻度 / 实测定论（从 VTS_HQ_CONTROLLER.md 抽"现状"）
  CONTROLLER.md      ← 控制器结构 + 槽位命名 + 装配与检查流程（合并 structure / naming / blend-tree-limits）
  PARAMETERS.md      ← 参数规范与设备实测（合并 PARAMETER_HO / PARAMETER_DEVICE_VERIFICATION / PARAMETER_STANDARDS）
  （不单独建 `Tools~/FaceTracking/README.md` —— Tools 区的说明就是 `Tools~/FaceTracking/README.md`，只有一份，避免两处同步）
  DECISIONS.md       ← 决策日志（原 VTS_HQ_CONTROLLER.md 的历史部分，倒序；只增不改）
  pitfalls/          ← 踩坑（保留，去重）
  archive/           ← 历史与被推翻的（含带日期的快照 / 研究 / 旧诊断）
  features/          ← 其它功能（现「完善的功能」，是否改名见 §3）
Tools~/FaceTracking/
  （在跑的流程 + 查询工具）+ README.md
  archive/           ← 一次性探针（保留可查，但不占主目录）
```

## 3. 需要你拍板的 5 件事

1. `完善的功能/` 要不要改成英文 `features/`（与其它目录统一）？
2. `VTS_HQ_CONTROLLER.md`（279 KB、48 处被引用）的拆法：现状 → `AXES.md` + `CONTROLLER.md`，历史 → `DECISIONS.md`（原文件保留为历史或直接删，git 里仍在）？
3. `vbparity` profile（A/B 实验）与 `expression-coverage` 探针：归档还是留？
4. 一次性探针脚本：**直接删**（git 里查得到）还是**移到 `Tools~/FaceTracking/archive/`**？
5. `docs/measurements/README.md` 被 `.cs` 引用但不存在：补一个 README，还是等你不在调 Unity 时改掉代码注释？

## 4. 执行顺序（每步一个提交，可回退）

1. **删垃圾 + `.gitignore`**（本轮已做：28 个 bin/obj、`__pycache__`、`.pyc`）
2. Tools 区：一次性探针 → `archive/`，重写 `Tools~/FaceTracking/README.md`（只列在跑的）
3. docs 区：建新权威文档（先写新、内容从旧文档搬），再删/归档被合并的旧文档
4. `VTS_HQ_CONTROLLER.md` 拆成 现状 / 历史
5. `../README.md` 重写为唯一入口；最后跑一遍**悬空引用扫描**（文档互引 + `.cs` 引用）

## 5. 执行进度（边做边更新）

| 步骤 | 状态 | 提交 |
| --- | --- | --- |
| 1 删构建垃圾 + `.gitignore` | ✅ 完成 | `b9c2875` |
| 2 Tools 区归档（23 项）+ README 重写 | ✅ 完成 | `1b7dfed` |
| 3 docs 历史/研究类进 `archive/`、`measurements` 补说明、`PENDULUM` 归到功能目录 | ✅ 完成 | `469e36f` |
| 4 子目录里的悬空 markdown 链接（archive / pitfalls / 完善的功能 共 6 个文件） | ✅ 完成 | 本轮 |
| 5 权威文档合并：`PIPELINE.md`(88K) / `CONTROLLER.md`(74K) / `AXES.md`(46K) / `DECISIONS.md`(2722 行) / `PARAMETERS.md`(56K) | ✅ 完成 | 本轮 |
| 6 顶层悬空链接修复 + `../README.md` 重写为唯一入口 + 全量 BOM | ✅ 完成（悬空 134 → 0 条真问题） | 本轮 |
| 7 `.cs` 里的旧描述（3 处文档路径 + 1 处旧计数） | ⏸ **等用户不在调 Unity 时** | — |

### 7 的三处（先列好，到时照做）

| 文件:行 | 现在写的 | 应该改成 |
| --- | --- | --- |
| `Editor/AnimationTools/HoAnimationPreviewTimeline.cs:20` | `docs/完善的功能/EDITOR_UI_SYSTEM.md` | `docs/完善的功能/EDITOR_UI_SYSTEM.md` |
| `Editor/Constraints/HoPendulumConstraintEditor.cs:102` | `docs/完善的功能/PENDULUM_CONSTRAINT.md` | `docs/完善的功能/PENDULUM_CONSTRAINT.md`（⚠️ 这条是**本次重组**搬出来的） |
| `Runtime/Constraints/HoLookAtConstraint.cs:10` | `docs/完善的功能/LOOKAT_CONSTRAINT.md` | `docs/完善的功能/LOOKAT_CONSTRAINT.md` |

⚠️ 另外 `docs/measurements/README.md` 那条悬空引用**已经修好**（补齐了说明文件，见 `469e36f`）。
