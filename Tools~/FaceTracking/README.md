# Tools~/FaceTracking —— 参与工程的脚本（不是探针）

这里放**决定并验证你工程里那份 `PTP_CTR_Face_VTS.controller`** 的脚本。它们属于仓库；
`.research/` 只放一次性探针、测量语料、资产surgery 的临时脚本。

| 脚本 | 干什么 | 跑法 |
| --- | --- | --- |
| `make-vts-controller.ps1` | 从 profile 生成控制器骨架（区域 → 表 / Direct 张量积表 / 1D 表 / 开关 → 槽位 + 空片段）。**已存在的片段不覆盖**（作者烘进去的姿势安全） | `powershell -File Tools~/FaceTracking/make-vts-controller.ps1` |
| `check-controller.ps1` | 对着 profile 核控制器：参数名/默认值/树型/轴接线/槽位名/槽位总数/**从根可达性** | `powershell -File Tools~/FaceTracking/check-controller.ps1 -Path <x.controller>` |
| `make-checker-fixture.ps1` | 造一份"形状正确"的夹具，验检查器的**通过路径**（检查器自己坏了也要能发现） | `powershell -File Tools~/FaceTracking/make-checker-fixture.ps1` |
| `fix-slot-guids.ps1` | 把槽位片段按 `md5('ho-face-slot:<槽位名>')` 的 GUID 规则摆回去（生成器没有资产库，GUID 靠名字推） | 见脚本头部 |
| `fix-bom.ps1` | 给本目录 `.ps1` 补 UTF-8 BOM —— PowerShell 5.1 对**没有 BOM** 的 .ps1 按 ANSI 读，中文全糊 | `powershell -File Tools~/FaceTracking/fix-bom.ps1` |

## ⚠️ 生成前先切走 Animator 窗口

生成器是**纯文本写盘**（不走 AssetDatabase 导入）⇒ Unity 重新导入时旧子资产被销毁，
而正开着那份 `.controller` 的 Animator 窗口还攥着旧引用 ⇒ 刷屏
`MissingReferenceException: … AnimatorStateMachine … has been destroyed`。
**症状无害**（资产是好的）：切走窗口 → 右键资产 **Reimport** → 再打开即可。

## ⚠️ 三处同步（这坑真栽过两次）

同一份控制器结构现在有**三份实现**，改结构必须一起改：

| 实现 | 谁在用 | 备注 |
| --- | --- | --- |
| `Editor/FaceTracking/HoFaceControllerSkeletonBuilder.cs` | 编辑器面板里"装配控制器" | **生成器不编它** ⇒ 只改它、生成结果一点不变（2026-09-28 白跑一轮） |
| `Tools~/FaceTracking/make-vts-controller.ps1` | 命令行生成 | 真正产出 rig 里那份资产的就是它 |
| `Tools~/FaceTracking/check-controller.ps1` | 机器核验 | 期望值写在这里：树型、槽位规则、槽位总数 |

## 其它相关但不在这里的东西

### 单区域隔离与嘴部点位对照（2026-09-28）

`isolate-regions.py <源.controller> <源.hoface.json> <新输出目录>` 会生成当前单状态控制器的剪枝副本，保留原动画引用；不修改原控制器、原动画或调试选择。
本轮主线为VTS大预设：优先使用`DIAG_MouthCore_Aligned6/9/11`与Manual配置独立扫Sad/Smile–Open，然后接回原大预设。
`--append-identical`只允许给本工具生成的目录补缺；源哈希必须相同，已有资产内容必须相同。
该工具的6/9点对照针对当前11点嘴表（闭4、半开4、全开3），不是通用任意嘴型自动简化器。
实测与使用步骤见`docs/MOUTH_ISOLATION_DIAGNOSIS_2026_09_28.md`。

`sync-mouth-points.py <六点源.controller> <九点.controller> <十一点.controller> --backup-dir <备份目录>` 将用户调好的六个锚点同步到细分诊断版，其余中间点插值为待测初值。只修改MouthCore坐标，写入前备份，保留六点源、动画引用、树结构与GUID。当前9点/11点已按六点现场测试结果同步，继续用原VTS大预设测试；中性/smile半张点只细化一段开口。

* `Tests~/`：Unity 批处理验证用例（拷进一次性工程跑）；
* `.research/`：一次性探针（`eye-probe` / `chain-probe` / `profile-verify` …）、面捕语料、资产 surgery 脚本；
* `docs/VTS_HQ_CONTROLLER.md`：控制器的当前状态与每一节决策记录（改结构请顺手加一节）。

## 录样（`Logs/HoFaceTraces/*.jsonl`）的速查 / 速统 / 速对（2026-09-29 加）

录样格式：**第 1 行头部**（`label` / `intervalSeconds` / `profileJson` / `settingsJson` …）、
**中间一行一个 sample**（`inputs[]` = 配置输入行、`outputs[]` = 输出行、`wires[]` = 原始线）、
**最后 1 行页脚**（`kind=end` / `reason` / `samples`）。
通道命名空间：inputs 用**裸名**（`jawOpen`）、outputs 加 `out:`（`out:Ho/Drive/Mouth/Jaw`）、wires 加 `w:`。
take 可以用**序号 / 文件名子串 / 组名**指定。

| 脚本 | 干什么 | 例子 |
| --- | --- | --- |
| `ho-traces.py` | **速查 / 速统 / 速对**一把抓：`ls` `info` `keys` `rows` `stat` `series` `check` `diff` `csv` | `python Tools~/FaceTracking/ho-traces.py ls`<br>`… stat --group 静置 --channels jawOpen mouthClose`<br>`… diff 4 7 --moved`<br>`… check` |
| `census-table.py` | 解析**面板导出的统计文本**（`=== 组名` + 每次 `min avg max 波动`）按组打表 —— 翻老语料用 | `python Tools~/FaceTracking/census-table.py .research/takes-side.txt` |
| `jaw-side-fit.py` | 「平移下巴」那批：各组横向振幅 / `(JawSide, Jaw)` **落点锚点** / 伪影对照 / 离现有格子的距离 | `python Tools~/FaceTracking/jaw-side-fit.py` |
| `jaw-take-census.py` | 下巴那批：通道定名、**阶梯归因**（输入没解释的跳变）、表达式 A/B、锁存门模拟 | `python Tools~/FaceTracking/jaw-take-census.py` |
| `tree-dump.py` | 从 `.controller` 打一棵 BlendTree（子节点 / 名字 / 坐标 / 阈值 / 权重参数）—— 查「格子摆在哪」 | `python Tools~/FaceTracking/tree-dump.py <x.controller> MouthJaw` |
| `profile-row-diff.py` | 若干份 profile 的同一批行并排打（表达式 / 修饰符 / 曲线 / notes 尾 + 括号平衡） | `python Tools~/FaceTracking/profile-row-diff.py "Mouth/Jaw" a.json b.json` |
| `trace-row-dump.py` | 一次录样里某几行的取值与元数据（含「运行时用的是哪份表达式」的对账） | `python Tools~/FaceTracking/trace-row-dump.py <x.jsonl> Mouth/Y` |
| `controller-block-diff.py` | 两份 `.controller` 的**语义**对照（fileID 归一化后按块比多重集；文本 diff 会被块顺序淹没） | `python Tools~/FaceTracking/controller-block-diff.py a.controller b.controller` |
| `profile-verify/`（C#） | 用**真读取器**解析 profile 并报 `problems` —— **改完 profile 必跑**（括号少一个它就会说 `expression does not parse`） | `…/profile-verify.exe <x.hoface.json>` |

⚠️ 踩过的坑：`check` 的「越界」与 `--moved` **只查形变类通道**（注视是角度、`Rotation/Position/Angle` 是位姿、还有帧号/时间戳/热键）—— 不排除就满屏假警报（第一版就是这么错的）；要看全部加 `--include-pose`。
⚠️ 录样的标签是**事后按时间顺序**打的（见 `.research/*/label-*.py`：头部 `label` 为空 ⇒ 按序号分组 ⇒ 重写头部 + 改名 + 写 `recording-labels-*.json` 清单，原文件先备份）。
