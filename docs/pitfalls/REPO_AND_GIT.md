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
