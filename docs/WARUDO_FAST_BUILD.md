# Warudo FastBuild 设计与验证

## 适用范围

FastBuild 面向一个已经在 Unity 中完成配置的角色 Prefab。它只负责准备可交给 Warudo SDK 的临时 Mod 目录，然后调用 UMod 官方构建入口；它不是 Warudo 的 `Setup Character` 替代品，也不创建蓝图或 Plugin 资产。

本记录基于以下环境验证：

- Warudo Mod Tool 0.14.4.8
- Unity 2021.3.45f2
- 角色输出目录：Warudo 数据目录下的 `StreamingAssets/Characters`

## Warudo 的打包模型

Warudo 的普通 Mod 是 `Assets` 下的一个 Mod 文件夹。Prefab、材质、贴图、网格和运行时脚本都必须位于这个文件夹内，UMod 扫描该目录并将引用资源写入 `.warudo`。角色 Mod 的根 Prefab 固定命名为 `Character`，构建结果放入 `Characters` 数据目录。

Warudo 的 Plugin Mod 也是普通 Mod，但额外要求源码中有继承 `Plugin` 的脚本。HoAuxRig 是挂在角色 Prefab 上的运行时 `MonoBehaviour`，不属于 Plugin Mod；它随角色 Mod 的脚本编译结果一起链接。因此本工具不依赖蓝图，也不需要创建 Plugin。

官方参考：

- [创建你的第一个 Mod](https://docs.warudo.app/zh/docs/modding/creating-your-first-mod)
- [角色 Mod](https://docs.warudo.app/zh/docs/modding/character-mod)
- [Plugin Mod](https://docs.warudo.app/zh/docs/scripting/plugin-mod)

## FastBuild 流程

```text
选择源 Prefab
    -> 扫描直接挂载的 MonoBehaviour 和资源依赖
    -> 在 Assets/HoFastBuildWarudoModTemp 下创建临时副本
    -> 临时根 Prefab 改名为 Character
    -> 按预览复制勾选的源码，移除编辑器专用组件
    -> 临时切换 ExportSettings 的活动 modAssetPath
    -> 等待 Unity 域重载后调用 UMod.ModToolsUtil.StartBuild
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
- MC1 (`MagicaCloth`) 与 MC2 (`MagicaCloth2`) 由 Warudo 宿主提供。FastBuild 会保留
  Prefab 上的组件引用，但禁止复制其源码，也不会把其 asmdef 源码闭包加入临时 Mod。
- 如果工程启用了 `FBXSDK_RUNTIME`，FastBuild 会在 UMod 构建期间临时移除该 Standalone
  Define，避免 Autodesk FBX 包的运行时测试程序集污染 Player 编译；构建结束或失败后恢复原值。
- Warudo SDK 或其他包的脚本默认保留原引用，不主动复制；启用复制前必须确认源码和依赖可以由 UMod 编译。
- 源码会短暂出现在 Unity 的运行时编译列表，构建完成后随临时目录一起清理。

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

## 已排除的尝试

- 把 Mod 工作区放到 Unity 忽略目录：Prefab 无法可靠导入和链接。
- 让 FastBuild 调用 `Setup Character`：会改变骨骼和 Prefab 结构。
- 用新 GUID 替换 Prefab 的 `m_Script`：破坏 UMod 按完整类型名链接的契约。
- 依赖额外 asmdef 隔离临时脚本：多 Mod 项目中可能被 UMod 选入错误的 `.csproj`。
- 把脚本复制闭包假设为自动完成：当前预览只保证直接挂载脚本，辅助源码必须人工审查。

## 构建前检查

1. 当前工程已导入 Warudo SDK，窗口顶部显示“SDK 已就绪”。
2. 选中的对象是 Project 中可加载的 Prefab，且没有 Missing Script。
3. ExportSettings 存在，活动 Mod 目录位于 `Assets` 下并且不是 `Assets` 根目录。
4. 依赖列表中只勾选可在 Warudo 运行时编译的源码。
5. 构建后检查 `Build.log` 和 `assemblymodules.dat`，再在 Warudo 的 `Characters` 目录验证角色。

## 恢复和清理

FastBuild 在调用官方构建前记录原始 `modAssetPath`。域重载、编译失败或 UMod 抛出异常都会先尝试恢复这个路径；恢复失败时会保留状态文件和临时目录，避免继续删除证据。正常完成后，按“构建完成后清理临时目录”选项删除 `Assets/HoFastBuildWarudoModTemp/<build-id>`，并清理空目录。
