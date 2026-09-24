# Warudo 打包、工具链与系统脚本

正本：[Warudo FastBuild](../WARUDO_FAST_BUILD.md)；防火墙那条来自面捕面板的「排查」栏（[面捕工作流](../FACE_TRACKING_WORKFLOW.md)）。

## 1. 缺 `.csproj` → 整条链完全静默

**这是"产物里没有 `assemblymodules.dat`、组件全是 Missing Script"最常见的原因。**

UMod 不直接枚举 Mod 目录里的 `.cs`，而是用 `ScriptCompiler.Project.ProjectLocator` 去工程根找 Unity 生成的
`.csproj`（`<Compile Include="Assets\...">` 列表）来决定哪些源码进编译。找不到时的日志原文：

```text
[Compile Scripts]
WARNING: Failed to locate script project file.
Scripts cannot be compiled for the mod export.
Make sure the .csproj file exists
```

失败链每一步都"正常"：FastBuild 正常暂存源码、正常重绑临时 Prefab；UMod 也扫到了脚本
（日志有 `will be compiled into a managed assembly for export`）；然后编译阶段一个脚本都没加入 →
产物没有 `assemblymodules.dat` → 组件程序集停在编辑器侧的 `Assembly-CSharp` → Mod 里全是 Missing Script。

**只看 `Assembly-CSharp` 分不出原因**：暂存副本本身就编译进它，"脚本没勾选"与"勾选了但没编译"表现一样。
真正的区分点是**打包资源清单里有没有暂存的 `Character.prefab`**，以及构建日志的计数。
真实样本（UMod 0.14.5，工程缺 `Assembly-CSharp.csproj`）：产物只有 3 个条目，`umod-compiled` 出现 0 次，
`Assembly-CSharp, Version=` 出现 71 次 —— 而正常产物里 `umod-compiled` 正好也是 71 条。

## 2. 包目录名曾经影响"脚本默认勾选"

依赖列表的默认勾选看"这段脚本是否属于本工具"。早期把包目录**写死**成 `Packages/com.hollow.hounitytools/`，
把包当成 GitHub ZIP 解压到 `Packages/HoUnityTools-master` 时判断恒为 false →
**所有脚本默认不勾选**、一个源码都不复制，得到和上一条一样的坏产物。
现在按包解析出来的实际根路径判断（`file:` / git URL / 手动解压三种安装方式结果一致）。

## 3. 临时 Prefab 的引用规则（两条都要守）

- **不要替换临时 Prefab 的 `m_Script` GUID**：UMod Linker 先读原组件，再按脚本完整类型名找编译后的类型。
- **不能保留包内 `MonoScript` 引用**：否则 UMod 会记录原 asmdef 的程序集名，运行时即使编译了同名类型也显示 Missing Script。

## 4. 运行时脚本禁用反射

UMod 在 `RunCodeValidation` 阶段检查运行时程序集的 API 引用，复制进 Mod 的源码**禁止引用 `System.Reflection`**：

```csharp
exception.GetType().Name              // ← 看起来只是错误文本
typeof(SomeType).GetProperty("Value")
methodInfo.Invoke(target, args)
```

第一行编译后的 IL 会调用 `System.Reflection.MemberInfo.Name`，同样被拒。
运行时错误用固定文本 + `Debug.LogException(exception)` 保留诊断。
（FastBuild 自身在 `Editor` 程序集里可以反射调 UMod 入口 —— 两者必须保持程序集边界。）

## 5. asmdef 与工作区目录

UMod 多 Mod 模式按 Unity 生成的运行时 `.csproj` 判断源码能否进编译：**额外 asmdef 可能让临时源码落到错误的工程文件**，
日志出现 `not in the .csproj file and will not be compiled`。临时脚本必须在普通、可被 Unity 导入的 `Assets` 路径；
`Assets/...~` 这类 Unity 忽略目录**不能**作为 Mod 工作区。

## 6. "Build succeeded" 不等于成功

FastBuild 的临时源码用编辑器条件包装，避免编辑器侧重复类型；而 UMod 构建在已验证的 SDK 版本里仍会生成运行时类型 ——
这是**版本相关的实现**。所以必须同时看构建日志与产物里的程序集类型，不能只看构建返回成功。

## 7. 判据别硬编码

- 早先按 `umod-compiled-` 前缀硬编码判断"脚本是否随 Mod 分发"，换 UMod 版本或编译程序集改名就**误报** →
  改成以 `assemblymodules.dat` 里列出的模块名为准。
- `sharedassets.bin` 是 UnityFS 打包数据，程序集记录靠字节级模式扫描；某次构建的压缩设置读不出时，
  复核会退化成长度受限的探测并把结论标成**"待确认"**，而不是误报成功或失败。

## 8. 提权脚本：参数绑定会以"看起来成功"的方式失败

**症状**：提权跑防火墙脚本时报 `ParameterBindingException`；而 `-File` 模式下同一段脚本被
`-ErrorAction SilentlyContinue` 掩盖，**看起来像是成功了**。

**原因**：把 `-Direction` / `-Action` 和 `-AssociatedNetFirewallApplicationFilter` 写在**同一条**
`Get-NetFirewallRule` 上，在 `-EncodedCommand`（base64 提权那条路）下参数绑定不成立。

**怎么办**：脚本只按程序筛规则，方向和动作放到客户端自己判断。
**验证方式是不提权跑同一段脚本**：它应该停在 `New-NetFirewallRule` 的「拒绝访问」，而不是停在参数绑定上 ——
停在参数绑定说明这条链本身没写对，跟权限无关。
