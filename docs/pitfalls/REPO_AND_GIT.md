# 仓库与提交：这个仓库里同时有别人在干活

## 1. 提交信息里有引号就别用命令行参数

症状：提交信息被 `"` 拆成几段，`git commit -m "…"` 只吃下第一段，于是**代码改动混进了文档提交**。
怎么办：把信息写进文件，用 `-F`：

```powershell
git commit -F ".research\commit-msg-<主题>.txt"
```

信息文件放 `.research/`（gitignore），顺手也是这次改动的说明存档。

## 2. 只 `git add -- <明确路径>`，永远不要 `-A`

这个工作区里有**并行 worker 的未跟踪文件**（`Runtime/AnimationTools/`、
`Tests~/AnimationClipPreviewValidation.cs`、`docs/ANIMATION_CLIP_PREVIEW.md` 之类）。
`-A` 会把它们一起提交，甚至把别人没写完的东西带进历史。逐条列路径最省事也最安全。

提交前先 `git status --porcelain` 看一眼：**不属于这次改动的行，一个都不要 add**。

## 3. `.research/` 是草稿区

- `.research/` 整个在 `.gitignore` 里：转储、一次性 Unity 工程、日志、提交信息文件都在那儿。
- `warudo-mod-research/` **未跟踪且不要提交**（别人的东西）。
- 一次性验证工程（`.research/UnityFaceValidation*`）体积很大，永远不进仓库；
  要复现就照 [批处理验证](VALIDATION_LOOP.md) 自己搭一个。

## 4. Windows 上的并发写

症状：`ReplaceFileW EIO (Win32 1175)` —— 同一份文件被 Unity（重新导入）和我们同时写。
怎么办：**重试那次编辑**即可，不用查代码。（Unity 正在导入时最容易撞。）

## 5. 新资产要 `.meta`，别等 Unity

手写 `.meta`（格式见 [Unity 资产与编辑器](UNITY_ASSET_PITFALLS.md) §7）比补一个"忘了 .meta"的
提交便宜。新文件夹也一样要给 `.meta`。

## 6. 提交粒度

一个改动一个提交，信息写**为什么**（不是"改了什么"）：结论尽量带实测数字或出处，
"未验证"的部分要明说未验证 —— 下次读历史的人靠这个判断能不能信。

## 7. 面捕这一系列**横跨两个仓库**：单边绿不算验过

包（本仓库）与 mod（`D:\Unity_Project\BreakWarudo\Assets\HoWarudoModTests`，**它自己的仓库**、分支 `main`）
是同一个功能的两半：包定词表与主本（`Runtime/FaceTracking/` + `Editor/FaceTracking/`），
mod 是同一套逻辑的**运行期**（`Mods-Ho/HoFaceTracking/`）。
两边是**两个 Unity 工程、两个程序集**，工程之间不能互相引用源码 ⇒
求值器那 **10 份是"搬"过去的**（不是引用），清单在 mod 侧 `Mods-Ho/HoFaceTracking/Core/PORTED.md` §1。

| 你改了什么 | 必须顺手做什么 | 不做会怎样 |
|---|---|---|
| 包里那 10 份（`HoFaceMiddleware` / `HoFaceProfile` / `HoFaceProfileJson` / `HoJson` / `HoVtsPacket` / `HoFaceTrackingChannels` / `HoFaceExpression` / `HoFaceNaming` / `HoFaceSemanticHub` / `HoFaceSemanticAsset`） | 跑 `.research/sync-modcore.ps1`，再两边各过一遍编译（包：`.warudo-mod-research/.tools/compile-check-package.ps1`；mod：`tools/compile-check.ps1`） | mod 侧还是**旧语义**，**而且编译照样过** —— 表现是"Warudo 里和面板里不一样"，最难查 |
| 直接改 mod 的 `Core/` 副本 | **别改**（那 6 行文件头就是提醒），改包侧再同步 | 下次同步**无声覆盖** |
| 包侧的公共类型 / 命名空间 / 菜单名 | 两边都重编译一遍 | mod 是**另一份源码**，包侧改了它不会自动跟过去 |
| mod 的节点 / 接收器 / 控制器 | `Assets/HoWarudoModTests/tools/compile-check.ps1` | Roslyn 全绿也可能**真构建失败**：`System.Reflection` / `System.IO` 只有 UMod 的 `RunCodeValidation` 拦（§4.1 of [构建与工具](BUILD_AND_TOOLING.md)） |
| 只在 Unity 面板里看到"绿" | 别下结论 —— 面板绿只证明包侧那一半 | 见 [HO 参数规范 §0.0](../PARAMETER_HO.md) 那张"改了哪一层 / 两边各怎么验"的表 |

⚠️ **"两边各写一份"是本项目的既定做法，而且不止这一处**（两边都要存在的类型、角色预制件上的资产…）：
别为某一处单独找"少写一份"的路子（不引用包侧、也不开子 asmdef），**清单 + 同步是唯一的保证**。
⚠️ `.research/`（本仓库）与 mod 侧工作区都**不是**唯一出处：`.research/` 整个在 `.gitignore` 里，
所以文档里写到脚本时，**同时要说清"清单/规则在哪个入库的文件里"**。
