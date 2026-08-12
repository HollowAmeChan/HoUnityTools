# WarudoModUtils

此目录用于存放 Warudo 使用的运行时工具和 `HoWarudoRuntimeHub` 中控入口。

当前运行时组件入口包括：

- `HoUnityTools/Warudo Mod Utils/HoWarudo Runtime Hub`
- `HoUnityTools/Warudo Mod Utils/HoWarudo Runtime Bone Debug Renderer`
- `HoUnityTools/Warudo Mod Utils/HoWarudo Blend Shape Billboard`

这里的代码保持运行时独立，不依赖 `UnityEditor` 或 Warudo 私有程序集，方便后续由 Warudo 蓝图或脚本传递参数。

中控设计记录：

`HoWarudoRuntimeHubDesign.md`

`HoWarudoBlendShapeBillboard` 为独立组件，不注册到 Runtime Hub。指定一个
`SkinnedMeshRenderer` 后，它会在世界中显示文字监视屏，并读取该网格当前的 Blend Shape
实际权重。屏幕直接使用组件 Transform 的位置和朝向，可将组件挂到角色旁边的空物体上，
也可选择启用面向相机。名称和值分别对齐成列，每列默认最多显示 20 行，更多条目向右扩展；
每列最大行数可在组件中调整。
组件不使用置顶绘制。
