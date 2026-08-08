# WarudoModUtils

此目录用于存放 Warudo 使用的运行时工具和 `HoWarudoRuntimeHub` 中控入口。

当前运行时组件入口包括：

- `HoUnityTools/Warudo Mod Utils/HoWarudo Runtime Hub`
- `HoUnityTools/Warudo Mod Utils/HoWarudo Runtime Bone Debug Renderer`

这里的代码保持运行时独立，不依赖 `UnityEditor` 或 Warudo 私有程序集，方便后续由 Warudo 蓝图或脚本传递参数。

中控设计记录：

`HoWarudoRuntimeHubDesign.md`
