# Tools~/FaceTracking —— 在跑的脚本 + 调试分析流程

这里只放**现在在跑**的东西：决定/验证 rig 里那份控制器的脚本、录样的查询算账工具。
一次性探针、旧实验、被取代的工具**已删**（墓碑索引与 `git` 捞回命令在 [`archive/README.md`](archive/README.md)）。

---

## A. 调试分析流程（现在这样干）

**一句话**：在**隔离**里调 → **录样** → **查 / 统 / 对** → **分析出落点** → **把工具对齐到资产** → **检查器打分** → **合并回去**。

1. **隔离**（验收基准 = 隔离控制器）：要调的轴 / 区先剪成一份**单区控制器**
   （rig 里 `Diagnostics_20260928/` 下那几份；2026-09-29 时的例子是 `DIAG_NoseRegion.controller`），配一份小 profile
   （`DIAG_Mouth_Manual` / `DIAG_Mouth_Current` / `DIAG_Mouth_Minimal` / `DIAG_Mouth_VBFormula.hoface.json`）。
   * 哪份能直接用、每份干什么 ⇒ rig 里那份说明：`Assets/Hollow/土豆/FT/Diagnostics_20260928/README.md`
   * ⚠️ **验收过的分区（连同它的小 profile）用完就删**（2026-09-29 用户：`DIAG_MouthCore_Aligned6/9/11` 等已验证的那批已经删掉）
     —— 隔离版本就是**临时工作台**，验收完的那一份不再留；要复现当时的环境就靠 `git` 里那几份资产的历史版本。
   * 要排除输入影响：`DIAG_Mouth_Manual.hoface.json`，改两个常量输出行的 `defaultValue`
     （先让 `Open=0` 扫 `Form`，再固定苦 / 中性 / 笑分别扫 `Open`）
2. **调**：**状态点永远由用户在 Animator 里手调**；中间层（profile 的表达式 / 曲线）这一侧只回答「这根线现在是多少」。
3. **录**：面捕调试面板 → 每组 **3 次**、每次约 **5 秒**、**间隔 0.05s**（快动作必须；静置那种慢的才用 0.2s）；
   窗口里**标签留空**，录完按时间顺序事后打标签（`.research/*/label-*.py`：重写头部 + 改名 + 写
   `recording-labels-*.json` + 备份原件）。
4. **查 / 统 / 对**（`ho-traces.py`，take 可用**序号 / 文件名子串 / 组名**指定）：

   | 速查 | 速统 | 速对 |
   | --- | --- | --- |
   | `ls` 目录里有什么 · `info` 头尾元数据 · `keys` 采样行结构 · `rows --moved` 动过的通道 · `series --channels X --every N` 逐帧 | `stat`（单次或 `--group 整组`）min / avg / max / 波动 | `check` 全目录完整性 · `diff A B` 两份逐通道比 · `csv` 导出 |

   通道命名空间：inputs **裸名** · outputs `out:` · wires `w:`。
5. **分析**：
   * `census-table.py` —— 面板导出的统计文本按组打表（翻老语料）
   * `jaw-take-census.py` —— 通道定名 · **阶梯归因**（输入没解释的跳变）· 表达式 A/B · 锁存门模拟
   * `jaw-side-fit.py` —— 「平移下巴」那批：各组振幅 · `(JawSide, Jaw)` **落点** · 伪影对照 · 离现有格子的距离
6. **把工具对齐到资产**（⚠️ **顺序不能反**）：`tree-dump.py <隔离版.controller> <树名>` 读**权威点位** →
   改生成器（`make-vts-controller.ps1` 的 `$*Over`）→ 改检查器（`check-controller.ps1` 的刻度集合 + `$exactPos`）。
7. **打分**：`check-controller.ps1 -Path <隔离版.controller> -Isolation` ⇒ **缺 / 错 0 条**才算这步过
   （树型 / 轴接线 / 刻度与逐格坐标 / 槽位名 / 变体镜像 / WD / Normalize 照旧严格；缺树缺参数降级成「注」）。
8. **合并**：验收过的表 / 轴并回大控制器。⚠️ 大控制器与那份大 profile 是**等待被剪枝**的东西 ——
   合并前检查器对着它报「槽位坐标应为…实际…」是**正常状态**。

**规矩**（口径见 `docs/README.md` 最上面）：旧数字 / 旧约定一律无效、以晚的为准、**数量不写死**、
两层不吃同一个信号、长行不再加魔数。

---

## B. 流程脚本（生成 → 接线 → 检查）

| 脚本 | 干什么 | 跑法 |
| --- | --- | --- |
| `make-vts-controller.ps1` | 从 profile 生成控制器骨架（区域 → 表 / Direct 张量积表 / 1D 表 / 开关 → 槽位 + 空片段）。**已存在的片段不覆盖**（作者烘进去的姿势安全） | `powershell -File Tools~/FaceTracking/make-vts-controller.ps1 -Template <模板.controller> [-ArkitPassthrough -ArkitProfile <p>] [-NoClips]`<br>⚠️ `-Template` **必填**；PowerShell 5.1 下还要显式给 `-ArkitProfile`（`$PSScriptRoot` 在 param 默认值里是空的） |
| `wire-slot-clips.ps1` | 把 `Animations/` 里的片段接进槽位（按 `<槽位名>` 或 `HO-<槽位名>` 匹配，优先 On；幂等，会打印缺哪些） | `powershell -File Tools~/FaceTracking/wire-slot-clips.ps1 -Apply`<br>⚠️ **生成完必须跑**，否则槽位是空的（"动画没了"多半是这一步没跑） |
| `check-controller.ps1` | 对着 profile 核控制器：参数名/默认值/树型/轴接线/槽位名/槽位总数/从根可达性/每格坐标 | `powershell -File Tools~/FaceTracking/check-controller.ps1 -Path <x.controller>` |
| ↑ **隔离模式** `-Isolation` | 只查这份控制器**有**的东西（`DIAG_*` 单区控制器天然缺树/缺参数）：「缺」降级成注，树型/轴接线/**刻度与逐格坐标**/槽位名/变体镜像/WD/Normalize 照旧严格。**验收基准就是这些隔离控制器** | `… -Path <DIAG_xxx.controller> -Isolation` |
| `check-controller-integrity.py` | 不依赖 profile 的**完整性**检查：块结构、悬空引用、状态机接线（"文件坏没坏一跑就知道"） | `python Tools~/FaceTracking/check-controller-integrity.py <x.controller>` |
| `isolate-trees.py` | **按树剪出隔离版控制器**（联合测试用）：区域 Direct 的孩子按名字留/砍，非破坏性、新 GUID；联合示例见 rig 的 `FT/Diagnostics_20260929/README.md` | `python Tools~/FaceTracking/isolate-trees.py <源.controller> <输出.controller> --root-children MouthRegion,NoseRegion --children "MouthRegion=…" ` |
| `make-checker-fixture.ps1` | 造一份"形状正确"的夹具，验检查器的**通过路径**（检查器自己坏了也要能发现） | `powershell -File Tools~/FaceTracking/make-checker-fixture.ps1` |
| `fix-slot-guids.ps1` | 把槽位片段按 `md5('ho-face-slot:<槽位名>')` 的 GUID 规则摆回去（生成器没有资产库，GUID 靠名字推） | 见脚本头部 |
| `fix-bom.ps1` | 给本目录 `.ps1` 补 UTF-8 BOM —— PowerShell 5.1 对**没有 BOM** 的 `.ps1` 按 ANSI 读，中文全糊 | `powershell -File Tools~/FaceTracking/fix-bom.ps1` |
| `HoSlotNames.psd1` | **槽位名权威**（树 → 每格的名字）；生成器与检查器都读它 | 数据文件 |
| `templates/` | 生成器要的**模板控制器**（借 Unity 亲手写的序列化形状） | 数据文件 |

### profile 侧的验证

| 工具 | 干什么 | 跑法 |
| --- | --- | --- |
| `profile-verify/`（C#） | 用**真读取器**解析 `.hoface.json` 并报 `problems`。⚠️ **改完 profile 必跑** —— 括号少一个它会直接说 `expression does not parse` | `dotnet run --project Tools~/FaceTracking/profile-verify -- <x.hoface.json>`（原来 `bin/` 里那份 exe 已不再跟踪） |

## C. 录样（`Logs/HoFaceTraces/*.jsonl`）的速查 / 速统 / 速对

录样格式：**第 1 行头部**（`label` / `intervalSeconds` / `profileJson` …）、**中间一行一个 sample**（`inputs[]` = 配置输入行、`outputs[]` = 输出行、`wires[]` = 原始线）、**最后 1 行页脚**（`kind=end` / `samples`）。

| 脚本 | 干什么 | 例子 |
| --- | --- | --- |
| `ho-traces.py` | **速查 / 速统 / 速对**一把抓：`ls` `info` `keys` `rows` `stat` `series` `check` `diff` `csv` | `python Tools~/FaceTracking/ho-traces.py ls`<br>`… stat --group 静置 --channels jawOpen mouthClose`<br>`… diff 4 7 --moved`<br>`… check` |
| `census-table.py` | 解析**面板导出的统计文本**（`=== 组名` + 每次 `min avg max 波动`）按组打表 —— 翻老语料用 | `python Tools~/FaceTracking/census-table.py .research/takes-side.txt` |
| `jaw-take-census.py` | 下巴那批：通道定名、**阶梯归因**（输入没解释的跳变）、表达式 A/B、锁存门模拟 | `python Tools~/FaceTracking/jaw-take-census.py` |
| `jaw-side-fit.py` | 「平移下巴」那批：各组横向振幅 / `(JawSide, Jaw)` **落点** / 伪影对照 / 离现有格子的距离 | `python Tools~/FaceTracking/jaw-side-fit.py` |

## D. 资产 / 配置的速查与对照

| 脚本 | 干什么 | 例子 |
| --- | --- | --- |
| `tree-dump.py` | 从 `.controller` 打一棵 BlendTree（子节点名 / 坐标 / 阈值 / 权重参数）—— 查"格子摆在哪"、**读隔离版权威点位** | `python Tools~/FaceTracking/tree-dump.py <x.controller> MouthJaw` |
| `clip-dump.py` | 速查一个 `.anim` 写了什么（形变键 + 值）—— 判断作者在某个姿势里烘了多少东西 | `python Tools~/FaceTracking/clip-dump.py <x.anim> --top 12` |
| `profile-row-diff.py` | 若干份 profile 的同一批行并排打（表达式 / 修饰符 / 曲线 / notes 尾 + 括号平衡） | `python Tools~/FaceTracking/profile-row-diff.py "Mouth/Jaw" a.json b.json` |
| `controller-block-diff.py` | 两份 `.controller` 的**语义**对照（fileID 归一化后按块比多重集；文本 diff 会被块顺序淹没） | `python Tools~/FaceTracking/controller-block-diff.py a.controller b.controller` |

---

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
| `Editor/FaceTracking/HoFaceControllerSkeletonBuilder.cs` | 编辑器面板里"装配控制器" | **生成器不编它** ⇒ 只改它、生成结果一点不变（2026-09-28 白跑一轮）；⚠️ 它现在**落后于**生成器 |
| `Tools~/FaceTracking/make-vts-controller.ps1` | 命令行生成 | 真正产出 rig 里那份资产的就是它 |
| `Tools~/FaceTracking/check-controller.ps1` | 机器核验 | 期望值写在这里：树型、槽位规则、刻度清单、逐格坐标 |

## ⚠️ 两个编码/写盘坑

* `.ps1` / `.psd1` / `docs/*.md` 都要 **UTF-8 BOM**（PS 5.1 按 ANSI 读没 BOM 的脚本，中文全糊）；用 `edit` 工具改过之后 BOM 会掉，改完 **必查**。
* rig 那份 `.controller` **原本没有 BOM**，别用 `utf-8-sig` 去写它。
