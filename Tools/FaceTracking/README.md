# Tools/FaceTracking —— 参与工程的脚本（不是探针）

这里放**决定并验证你工程里那份 `PTP_CTR_Face_VTS.controller`** 的脚本。它们属于仓库；
`.research/` 只放一次性探针、测量语料、资产surgery 的临时脚本。

| 脚本 | 干什么 | 跑法 |
| --- | --- | --- |
| `make-vts-controller.ps1` | 从 profile 生成控制器骨架（区域 → 表 / Direct 张量积表 / 1D 表 / 开关 → 槽位 + 空片段）。**已存在的片段不覆盖**（作者烘进去的姿势安全） | `powershell -File Tools/FaceTracking/make-vts-controller.ps1` |
| `check-controller.ps1` | 对着 profile 核控制器：参数名/默认值/树型/轴接线/槽位名/槽位总数/**从根可达性** | `powershell -File Tools/FaceTracking/check-controller.ps1 -Path <x.controller>` |
| `make-checker-fixture.ps1` | 造一份"形状正确"的夹具，验检查器的**通过路径**（检查器自己坏了也要能发现） | `powershell -File Tools/FaceTracking/make-checker-fixture.ps1` |
| `fix-slot-guids.ps1` | 把槽位片段按 `md5('ho-face-slot:<槽位名>')` 的 GUID 规则摆回去（生成器没有资产库，GUID 靠名字推） | 见脚本头部 |
| `fix-bom.ps1` | 给本目录 `.ps1` 补 UTF-8 BOM —— PowerShell 5.1 对**没有 BOM** 的 .ps1 按 ANSI 读，中文全糊 | `powershell -File Tools/FaceTracking/fix-bom.ps1` |

## ⚠️ 三处同步（这坑真栽过两次）

同一份控制器结构现在有**三份实现**，改结构必须一起改：

| 实现 | 谁在用 | 备注 |
| --- | --- | --- |
| `Editor/FaceTracking/HoFaceControllerSkeletonBuilder.cs` | 编辑器面板里"装配控制器" | **生成器不编它** ⇒ 只改它、生成结果一点不变（2026-09-28 白跑一轮） |
| `Tools/FaceTracking/make-vts-controller.ps1` | 命令行生成 | 真正产出 rig 里那份资产的就是它 |
| `Tools/FaceTracking/check-controller.ps1` | 机器核验 | 期望值写在这里：树型、槽位规则、槽位总数 |

## 其它相关但不在这里的东西

* `Tests~/`：Unity 批处理验证用例（拷进一次性工程跑）；
* `.research/`：一次性探针（`eye-probe` / `chain-probe` / `profile-verify` …）、面捕语料、资产 surgery 脚本；
* `docs/VTS_HQ_CONTROLLER.md`：控制器的当前状态与每一节决策记录（改结构请顺手加一节）。
