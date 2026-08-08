# WarudoModUtils

此目录用于存放 Warudo 使用的运行时调试工具。

当前目录仅包含 Warudo 运行时骨骼调试组件，组件入口为：

`HoUnityTools/Warudo Mod Utils/HoWarudo Runtime Bone Debug Renderer`

这里的代码保持运行时独立，不依赖 `UnityEditor` 或 Warudo 私有程序集，方便后续由 Warudo 蓝图或脚本传递参数。

中控设计记录：

`HoWarudoRuntimeHubDesign.md`
