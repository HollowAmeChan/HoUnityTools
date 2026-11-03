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
`Tests~/AnimationClipPreviewValidation.cs`、`docs/guides/ANIMATION_CLIP_PREVIEW.md` 之类）。
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

## 7. 面捕横跨包、角色与插件：单边绿不算验过

2026-11-03 核对：旧的“九份求值源码都复制进网络插件”已不是当前分工。以插件仓库
`Mods-Ho/HoFaceTracking/Core/PORTED.md` 与包内 `Tests~/SyncFaceModCore.ps1` 的现行清单为准。

- **包与角色运行模块**：`Runtime/FaceTracking/` 是规则、表情与姿态实现主本，角色构建时进入角色 Mod。
- **网络插件**：独立仓库 `D:\Unity_Project\BreakWarudo\Assets\HoWarudoModTests`，负责接收、标定、节点交付和诊断。当前清单同步 `HoJson.cs` 与 `HoVtsPacket.cs` 等协议源码，不重复运行角色规则。
- **新桌面工具**：计划位于本仓库 `Tools~/HoFaceStudio/`，链接编译纯 C# 核心主本，不复制出另一份桌面求值器；目标方案见 [双文件架构](../face-tracking/HOFACE_VALUE_IR_ARCHITECTURE.md)。

| 变更范围 | 必须核对的边界 |
| --- | --- |
| 现行同步清单内的协议源码 | 改包侧主本，执行 `Tests~/SyncFaceModCore.ps1`，再以 `-Check` 检查；插件独立编译及 UMod 校验 |
| 角色规则、IR、姿态与共享核心 | 桌面独立构建、Unity 角色编译、相关语义回归和实际角色打包；不要默认向网络插件复制 |
| 插件节点、接收器或桥接 | 插件 `tools/compile-check.ps1 -ModsRoots Mods-Ho` 及实际宿主链路；离线编译不能替代实际 Mod 安全校验 |
| 共享类型、命名空间、输入阶段或消息接口 | 检查全部消费者，保留版本、数据阶段与实际输出对照 |

确需跨仓库副本时，先更新已入库清单，再同步与检查；不要编辑生成副本，也不要用 junction、子 asmdef 或本机临时补源码绕过构建边界。

桌面 `.csproj` 链接同仓库源码不是让两个 Unity 工程互相引用源码。UI、网络和文件 IO 依赖不得进入角色共享核心。

两仓库可通过 [HoFace 作者工作区](../../Tools~/HoFaceStudio/HoFace.authoring.code-workspace) 同时查看。`.research/` 始终是忽略目录，不能作为同步规则、依赖清单或验证脚本的唯一出处。
