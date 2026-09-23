// HoFastBuildWarudoModWindow.HostAssemblies.cs
//
// 「宿主自带程序集」的判据 —— 决定一个 Prefab 组件该不该把源码复制进 Mod。
//
// ─────────────────────────────────────────────────────────────
// 为什么是静态名单，而不是去读游戏目录
//
// 方案一（已废弃）：从活动工作区的导出目录反推 <游戏>/Warudo_Data/Managed，再读目录。
// 这条路**不可靠**：
//   · 用户机器上可能根本没装 Warudo（只在别的机器上跑）
//   · 导出目录完全可以指向临时目录、网络盘、别人的机器
//   · 结论会随工作区配置变化，同一个 Prefab 两次构建可能不同
//
// 方案二（也废弃）：拿 SDK 当判据。SDK 只是运行时环境的**镜像**，实测不够准：
//   · SDK 的 asmdef 63 个里只有 28 个在宿主 Managed 中，缺的全是 Editor / Samples / Tests
//     （UniVRM.Editor / VRM.Tests / VRM.Samples.* / Warudo.Editor / Warudo.Mod.Tools …）
//   · SDK 的 DLL 53 个里只有 45 个在宿主里，缺的全是编辑器工具
//     （UMod-Editor / DOTweenEditor / Trivial.ImGUI / Animancer.Lite …）
//   -> 拿 SDK 当判据，会把一堆编辑器程序集误判成「宿主自带」。
//
// 现方案：**实测生成的全量静态名单**（HoWarudoHostAssemblies.generated.cs）。
// 由 <游戏>/Warudo_Data/Managed 的 400 个 DLL 过滤而来，去掉 .NET 框架家族
// （mscorlib / netstandard / System* / Microsoft* / I18N* …）和 Assembly-CSharp 家族，
// 剩 330 个。确定、离线、不受工作区配置影响，也不需要用户装环境。

using System;
using System.Collections.Generic;

namespace Hollow.HoUnityTools.Editor.Warudo
{
    internal sealed partial class HoFastBuildWarudoModWindow
    {
        private static HashSet<string> s_HostAssemblyLookup;

        /// <summary>
        /// 这个程序集是不是宿主（Warudo）自带的。
        /// 是 -> Mod 不该复制它的源码，保留 Prefab 里的组件引用即可。
        /// </summary>
        internal static bool IsWarudoHostAssembly(string assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName))
                return false;

            // 硬保护，和名单是否被误编辑无关：
            // 用户自己的脚本编译进 Assembly-CSharp 系列，那几个**永远**不算宿主自带，
            // 否则用户写的组件会被判成「不用复制」，Mod 装上去就是一堆 Missing Script。
            if (IsUserOwnedAssemblyName(assemblyName))
                return false;

            if (s_HostAssemblyLookup == null)
                s_HostAssemblyLookup = new HashSet<string>(
                    HoWarudoHostAssemblies.Names, StringComparer.OrdinalIgnoreCase);

            return s_HostAssemblyLookup.Contains(assemblyName);
        }

        private static bool IsUserOwnedAssemblyName(string assemblyName)
        {
            return assemblyName.StartsWith("Assembly-CSharp", StringComparison.OrdinalIgnoreCase);
        }
    }
}
