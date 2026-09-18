# Warudo FastBuild 设计与验证

## 适用范围

FastBuild 面向一个已经在 Unity 中完成配置的角色 Prefab。它只负责准备可交给 Warudo SDK 的临时 Mod 目录，然后调用 UMod 官方构建入口；它不是 Warudo 的 `Setup Character` 替代品，也不创建蓝图或 Plugin 资产。

面板有两个入口，打开的是同一个窗口：

- 顶栏菜单 `HoUnityTools/FastBuildWarudoMod`：不要求选中 Prefab；窗口没有源 Prefab 时会采用当前选中的 Prefab。
- Project 窗口右键 Prefab 的 `Assets/HoUnityTools/FastBuildWarudoMod`：始终把右键的 Prefab 作为源 Prefab。

本记录基于以下环境验证：

- Warudo Mod Tool 0.14.4.8
- Unity 2021.3.45f2
- 角色输出目录：Warudo 数据目录下的 `StreamingAssets/Characters`

## 安装方式会改变构建结果（重要）

依赖列表里的默认勾选取决于“这段脚本源码是否属于本工具”。早期版本把包目录写死成
`Packages/com.hollow.hounitytools/`，于是**同一个 Prefab、同一份代码，安装方式不同会得到不同结果**：

| 安装方式 | Unity 里的资源路径 | 早期版本是否命中 |
| --- | --- | --- |
| `manifest.json` 里 `file:` 指向本地目录 | `Packages/com.hollow.hounitytools/...` | 命中 |
| Package Manager 里 Add package from git URL | `Packages/com.hollow.hounitytools/...` | 命中 |
| 下载 GitHub ZIP 解压后放进 `Packages/` | `Packages/HoUnityTools-master/...` | **不命中** |

不命中的后果是**完全静默**的，没有任何报错：

1. 依赖列表里 `HoAuxRig` 这类运行时脚本默认不勾选；
2. FastBuild 按勾选复制源码，于是一个源码都不复制；
3. UMod 没有源码可编译，产物里**根本没有 `assemblymodules.dat`**；
4. 临时 Prefab 没有重绑脚本，每个组件都保留编辑器侧的 `Assembly-CSharp` 引用；
5. 产出的 Mod 在 Warudo 里全是 Missing Script。

现在改为按包解析出来的**实际根路径**判断：优先 `PackageInfo.FindForAssembly`，失败则按本包
`Editor/HoUnityTools.Editor.asmdef` 的位置反推包根。三种安装方式结果一致，不再依赖目录名；
`IsHostProvidedRuntimeScript` 对 Warudo SDK 同样按包名而不是目录名判断。

遇到异常时可以这样自查：

- 依赖面板标题出现 `N 个不编译`、并弹出报错色提示框 → 有组件不会随 Mod 编译。
- 产物复核报告出现 `缺少条目：assemblymodules.dat` → 这次构建根本没有编译 Mod 脚本。
- 产物里搜不到 `umod-compiled`，却能搜到大量 `Assembly-CSharp, Version=`。

一个真实的失败样本（手动解压安装、UMod 0.14.5）长这样：产物只有 3 个条目，`umod-compiled`
出现 0 次，`Assembly-CSharp, Version=` 出现 71 次，而正常产物里 `umod-compiled` 记录正好也是 71 条
—— Prefab 序列化完全一致，只差程序集解析。

## Warudo 的打包模型

Warudo 的普通 Mod 是 `Assets` 下的一个 Mod 文件夹。Prefab、材质、贴图、网格和运行时脚本都必须位于这个文件夹内，UMod 扫描该目录并将引用资源写入 `.warudo`。角色 Mod 的根 Prefab 固定命名为 `Character`，构建结果放入 `Characters` 数据目录。

Warudo 的 Plugin Mod 也是普通 Mod，但额外要求源码中有继承 `Plugin` 的脚本。HoAuxRig 是挂在角色 Prefab 上的运行时 `MonoBehaviour`，不属于 Plugin Mod；它随角色 Mod 的脚本编译结果一起链接。因此本工具不依赖蓝图，也不需要创建 Plugin。

官方参考：

- [创建你的第一个 Mod](https://docs.warudo.app/zh/docs/modding/creating-your-first-mod)
- [角色 Mod](https://docs.warudo.app/zh/docs/modding/character-mod)
- [Plugin Mod](https://docs.warudo.app/zh/docs/scripting/plugin-mod)

## 工作区（Export Profile）

Warudo 用一个 `ExportSettings` 资源保存全部导出配置，其中 `exportProfiles` 数组的每一项就是一个
“工作区”，官方文档里叫 Export Profile。工作区决定这次构建的 Mod 名称、作者、版本、Mod 资产目录、
导出目录、图标和引用的其它 Mod。FastBuild 构建时读取的 `modAssetPath` 就是活动工作区的那一份。

面板的“Warudo 工作区”一栏就是围绕这份数据做管理，能力与 `UMod.ModTools.Export.ExportSettings`
公开 API（0.14.4.8 实测）一一对应：

| 面板操作 | 调用的 SDK 入口 | 说明 |
| --- | --- | --- |
| 点列表切换活动工作区 | `SetActiveExportProfile(int)` | 切换后写入 `activeProfile` 并保存资源 |
| 新建工作区 | `CreateNewExportProfile(bool makeActive)` | 追加一项并立即设为活动工作区 |
| 删除当前 | `DeleteExportProfile(int)` | 仅移除配置项，不动磁盘文件 |
| 清理重名 | `RemoveDuplicateProfiles()` | 按 Mod 名称去重，保留每组第一项 |
| 打开官方设置窗口 | `UMod.Exporter.SettingsWindow.ShowWindow(false, 0)` | 打开 uMod 设置窗口的 Mod 页 |
| 校验显示 | `ValidateName` / `ValidateAssetPath` / `ValidateVersion` / `ValidateBuildAndRun` | 直接显示 SDK 自己的校验结果 |

这些入口都通过反射调用，和 `ModToolsUtil.StartBuild` 一样，HoUnityTools 仍然不强依赖 Warudo DLL。

面板显示与校验的规则：

- 列表逐项显示 Mod 名称、Mod 资产目录和状态（`就绪` / `<未命名>` / `目录无效` / `重名`）。
- 详情区显示活动工作区的图标、名称、作者、版本、说明、两个目录、引用 Mod 数量，以及 SDK 校验结果。
- Mod 资产目录必须存在、位于 `Assets` 下，并且不能是 `Assets` 根目录。这一条比 SDK 的
  `ValidateAssetPath()` 更严格，因为把整个 `Assets` 当 Mod 工作区会让 UMod 把所有资源都打进产物。
- 导出目录不存在或未设置时会给出提示，目录存在时可以直接在访达/资源管理器里定位。

新建工作区时的默认值：

- `modExportPath` 和 `modAuthor` 从当前工作区继承——这两项通常跟随工程环境，不必重复填写。
- `modName` 取一个不重名的默认名（`MOD_New`、`MOD_New2`……）。
- `modAssetPath` 取 `Assets/<modName>`。该目录一开始并不存在，详情区会提示并可以直接点“创建目录”建好。
- 版本沿用 SDK 构造函数的默认值 `1.0.0`。

删除工作区只会从 `ExportSettings` 移除配置项，不会删除 Mod 资产目录或已经构建出来的 `.warudo`。
面板在只剩一个工作区时会拒绝删除，避免落到无法构建的空状态；确实要清空请用官方设置窗口。

工作区列表会在数组长度或活动下标被外部改动时自动重新读取（例如在官方窗口里切换），字段内容的改动
用面板的刷新按钮同步。存在未完成的 FastBuild 流程时，所有会改动 `ExportSettings` 的操作都会被禁用，
避免和 `ApplyTemporaryExportSettings` 的临时改写冲突。

切换活动工作区之后会调用 `UMod.BuildEngine.ReferenceAssemblyLoader.LoadReferencedAssemblies(false)`，
与官方设置窗口保持一致，确保引用程序集列表跟着工作区一起切换。

## FastBuild 流程

```text
选择源 Prefab
    -> 扫描直接挂载的 MonoBehaviour 和资源依赖
    -> 在 Assets/HoFastBuildWarudoModTemp 下创建临时副本
    -> 临时根 Prefab 改名为 Character
    -> 按预览复制勾选的源码，移除编辑器专用组件
    -> 临时切换 ExportSettings 的活动 modAssetPath
    -> 等待 Unity 域重载后调用 UMod.ModToolsUtil.StartBuild
    -> 直接读取 .warudo 复核组件是否真的挂上（控制台输出）
    -> 恢复 ExportSettings，按选项删除临时目录
```

源 Prefab、源脚本和活动 Mod 目录不会被直接改写。构建期间的 ExportSettings 修改会写入 `Library/HoFastBuildWarudoMod/*.json`，用于域重载或 Unity 重启后的恢复。恢复失败时不会删除临时目录或状态文件。

官方构建入口的实际签名为：

```csharp
UMod.BuildEngine.ModToolsUtil.StartBuild(
    ExportSettings settings,
    Action invalidExportSettingsCallback = null);
```

FastBuild 通过反射查找这个入口，因此 HoUnityTools 包本身不强依赖 Warudo DLL。没有 SDK 的工程仍可安装并使用其他 HoUnityTools 功能；FastBuild 窗口会保留入口，但整个面板会禁用并显示原因。

## 为什么使用临时副本

Warudo 的 `Setup Character` 会对选中的对象做骨骼归一化、Prefab 拆包和 Transform 修改。对于已经在 Blender/Unity 侧完成约束和姿态处理的角色，这些副作用可能破坏结果。所以 FastBuild 只复制 Prefab，将副本根节点命名为 `Character`，再调用官方 Build API。

不要替换临时 Prefab 的 `m_Script` GUID。UMod Linker 会先读取原组件，再按脚本的完整类型名寻找编译后的类型；保留原引用才能让链接过程和 Unity 中的组件语义一致。

## 脚本依赖和安全边界

依赖预览的入口是 Prefab 上直接挂载的 `MonoBehaviour`。当前工具会合并同一源码的多个挂载，并显示引用次数。构建时，如果选中的脚本属于非 Editor asmdef，FastBuild 会继续收集该运行时程序集目录下的源码闭包，避免临时 Mod 只带组件脚本而遗失 `HoUnityTools.Runtime` 这类程序集内部类型；Editor 目录、`AssemblyInfo.cs` 和明确的编辑器骨骼绘制器会被排除。

脚本复制遵循以下规则：

- `HoAuxRig` 是独立运行时脚本，可以复制到临时 Mod。
- 位于 `Editor` 目录的脚本不作为运行时源码复制。
- `HoBoneRenderer` 含编辑器可视化逻辑，默认从临时 Prefab 移除。
- 默认勾选规则按**包名**判断脚本是否属于本工具或本工程，而不是按目录名。把本包解压到
  `Packages/任意目录名`（例如 `Packages/HoUnityTools-master`）同样能被正确识别；
  早期版本写死了 `Packages/com.hollow.hounitytools/`，目录名不同就会静默地把所有脚本取消勾选，
  构建出来的产物里组件仍指向 `Assembly-CSharp`，且不会生成 `assemblymodules.dat`。
- MC1 (`MagicaCloth`) 与 MC2 (`MagicaCloth2`) 由 Warudo 宿主提供。FastBuild 会保留
  Prefab 上的组件引用，但禁止复制其源码，也不会把其 asmdef 源码闭包加入临时 Mod。
- 如果工程启用了 `FBXSDK_RUNTIME`，FastBuild 会在 UMod 构建期间临时移除该 Standalone
  Define，避免 Autodesk FBX 包的运行时测试程序集污染 Player 编译；构建结束或失败后恢复原值。
- Warudo SDK 或其他包的脚本默认保留原引用，不主动复制；启用复制前必须确认源码和依赖可以由 UMod 编译。
- 源码会短暂出现在 Unity 的运行时编译列表，构建完成后随临时目录一起清理。
- FastBuild 会把临时 Prefab 上需要随 Mod 编译的组件重绑到临时脚本副本。不能保留包内
  `MonoScript` 引用，否则 UMod 会记录原 asmdef 程序集名，运行时即使已编译同名类型也会显示 Missing Script。
- 依赖面板会在构建前统计“既不随 Mod 编译、也不由宿主提供、也不会被移除”的组件，
  并用报错色的提示框列出数量；这些组件在产物里必然是 Missing Script，面板标题也会显示
  `N 个不编译`。

### UMod 运行时安全审查

UMod 会在脚本编译后检查运行时程序集的 API 引用。复制进角色 Mod 的源码禁止引用 `System.Reflection` 或通过反射访问成员，否则构建会在 `RunCodeValidation` 阶段失败。

以下写法也属于反射引用，不能出现在运行时脚本中：

```csharp
exception.GetType().Name
typeof(SomeType).GetProperty("Value")
methodInfo.Invoke(target, args)
```

其中 `exception.GetType().Name` 看起来只是错误文本，但编译后的 IL 会调用 `System.Reflection.MemberInfo.Name`，同样会被拒绝。运行时错误显示应使用固定文本，并用 `Debug.LogException(exception)` 保留诊断信息。

FastBuild 自身位于 `Editor` 程序集，可以反射调用 UMod 官方构建入口；这不等于运行时 Mod 可以使用反射。两者必须保持程序集边界。

当前版本不创建额外 asmdef。UMod 多 Mod 模式会根据 Unity 生成的运行时 `.csproj` 判断源码是否能进入编译；额外 asmdef 可能让临时源码落到错误的工程文件，导致构建日志出现 `not in the .csproj file and will not be compiled`。临时脚本必须位于普通、可被 Unity 导入的 `Assets` 路径；`Assets/...~` 等 Unity 忽略目录不能作为 Mod 工作区。

为避免 Unity 编辑器侧出现重复类型，FastBuild 临时源码使用编辑器条件包装；Warudo UMod 构建在已验证的 SDK 版本中仍会生成运行时类型。这个行为属于 SDK 版本相关实现，不能只以“Build succeeded”判断成功，必须同时检查构建日志和产物中的程序集类型。

## 产物检查

`.warudo` 文件包含 UMod 自己的文件头，后面是 ZIP。一次成功的角色构建至少应看到：

```text
modinfo.dat
sharedassets.bin
sharedassets.meta
assemblymodules.dat
```

`assemblymodules.dat` 中应存在 UMod 生成的运行时程序集，并能通过反编译或类型表确认以下运行时类型（原始二进制的字符串表可能会拆分命名空间和类型名）：

```text
Hollow.HoUnityTools.RigConstraints.HoAuxRig
```

UMod `Build.log` 应同时出现以下信息：

```text
Adding source file to build: ...HoAuxRig.cs
Compile successful!
```

只看到 `BUILD SUCCEEDED` 而没有源码加入和类型产物，不足以证明脚本可用。

### 自动复核（FastBuild 内建）

上面这些检查现在由 FastBuild 在 `StartBuild` 返回后自动执行，结论直接打印到 Unity 控制台；
临时目录被清理前完成，因此期望基准就是 UMod 实际打包的那份临时 `Character.prefab`。

复核覆盖的内容：

- 容器结构：四个必需条目是否齐全，以及 `sharedassets.meta` 里的打包资源清单。
- Mod 元数据：`modinfo.dat` 中是否出现当前 ExportProfile 的 Mod 名称。
- 运行时类型表：解析 `assemblymodules.dat` 里内嵌 PE 的 ECMA-335 TypeDef 表，
  得到 Mod 程序集实际包含的类型全名（等价于文档里“检查类型表”的人工步骤，不需要反编译工具）。
- 分发程序集集合：以 `assemblymodules.dat` 里列出的模块名为准，判断组件记录的程序集是否会随 Mod 分发。
  早先的版本用 `umod-compiled-` 前缀硬编码判断，换 UMod 版本或编译程序集改名就会误报。
- 组件挂载：扫描 `sharedassets.bin` 中 UMod Linker 为每个 MonoBehaviour 写下的
  `[程序集显示名][类型全名]` 记录，与临时 Prefab 上的组件逐一比对。

每个组件会得到三种结论之一：

| 结论 | 含义 |
| --- | --- |
| `OK` | 组件已链接到本次构建的 Mod 程序集（或确认由 Warudo 宿主程序集提供）。 |
| `缺失` | 产物里没有该组件的程序集记录，运行时会是 Missing Script。 |
| `待确认` | 组件记录了非 Mod 程序集，或已链接但类型表里没有该类型，需要人工判断。 |

如果 `assemblymodules.dat` 或 `sharedassets.bin` 读不出来，复核会标记为“复核不完整”并按待确认处理，
不会因为读不到证据就报告成功；报告里会列出每个条目的声明长度与压缩后长度，以及读取失败的具体异常。

组件记录了 `Assembly-CSharp`、`HoUnityTools.Runtime` 这类工程程序集时，通常意味着 UMod 没有把
FastBuild 复制的脚本编进构建。这时请检查 UMod `Build.log` 是否出现：

```text
not in the .csproj file and will not be compiled
```

控制台输出形如：

```text
[HoUnityTools] FastBuild 产物复核
  产物：.../Characters/MOD_辅助骨测试.warudo
  条目：modinfo.dat, sharedassets.bin, sharedassets.meta, assemblymodules.dat
  Mod 程序集：umod-compiled-xxxxxxxx-....
  编译类型：55 个
  程序集记录：997 条
  组件复核（3/3 已确认）：
    [OK] Character / Hollow.HoUnityTools.RigConstraints.HoAuxRig -> umod-compiled-xxxx（FastBuild 复制的脚本已编译进 Mod 程序集。）
    [待确认] Character/X / Some.Component -> Assembly-CSharp（该程序集不会随 Mod 分发。）
```

发现缺失时用 `Debug.LogError` 额外提示，存在 `待确认` 时用 `Debug.LogWarning`。
完整报告同时写入 `Library/HoFastBuildWarudoMod/last-verification.txt`，窗口的“产物复核”面板
可以展开查看，也可以在不重新构建的情况下按刷新按钮重新复核。

`sharedassets.bin` 是 UnityFS 打包数据，程序集记录以字节级模式扫描取得。若某次构建的
压缩设置让记录无法直接读出，复核会退化成长度受限的字节探测，并把结论标成“待确认”而不是
误报成功或失败。

## 已排除的尝试

- 把 Mod 工作区放到 Unity 忽略目录：Prefab 无法可靠导入和链接。
- 让 FastBuild 调用 `Setup Character`：会改变骨骼和 Prefab 结构。
- 用新 GUID 替换 Prefab 的 `m_Script`：破坏 UMod 按完整类型名链接的契约。
- 依赖额外 asmdef 隔离临时脚本：多 Mod 项目中可能被 UMod 选入错误的 `.csproj`。
- 把脚本复制闭包假设为自动完成：当前预览只保证直接挂载脚本，辅助源码必须人工审查。
- 用固定目录名判断脚本是否属于本工具：`file:` 与 git 安装落在 `Packages/<包名>/`，
  手动解压却落在 `Packages/<仓库名>/`，写死目录名会让默认勾选静默失效并产出 Missing Script 的产物。

## 构建前检查

1. 当前工程已导入 Warudo SDK，窗口顶部显示“SDK 已就绪”。
2. 选中的对象是 Project 中可加载的 Prefab，且没有 Missing Script。
3. ExportSettings 存在，活动工作区的 Mod 目录位于 `Assets` 下、存在，并且不是 `Assets` 根目录。
4. 依赖列表中只勾选可在 Warudo 运行时编译的源码；面板若提示 `N 个不编译`，说明有组件会变成 Missing Script。
5. 构建后确认控制台的“产物复核”报告里没有 `缺失`；`待确认` 需要人工判断，再在 Warudo 的 `Characters` 目录验证角色。

## 恢复和清理

FastBuild 在调用官方构建前记录原始 `modAssetPath`。域重载、编译失败或 UMod 抛出异常都会先尝试恢复这个路径；恢复失败时会保留状态文件和临时目录，避免继续删除证据。正常完成后，按“构建完成后清理临时目录”选项删除 `Assets/HoFastBuildWarudoModTemp/<build-id>`，并清理空目录。
