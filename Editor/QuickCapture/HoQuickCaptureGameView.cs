// HoQuickCaptureGameView.cs -- 问游戏视图"你现在多大"，以及临时把它改成指定尺寸
//
// 为什么单独一个文件：这块全是**版本相关的黑魔法**，出事只会出在这里，集中放好排查。
//
// 两条路，按 Unity 版本分叉（和 Unity Recorder 5.x 的 GameViewSize.cs 分叉方式一致）：
//   · Unity 2022.2+ → 公开 API `UnityEditor.PlayModeWindow`（GetRenderingResolution /
//     SetCustomRenderingResolution）。这是正路，优先走。
//   · 更老（本包下限 2021.3）→ 反射 `UnityEditor.PlayModeView.GetMainPlayModeView()` 拿那个
//     EditorWindow，再读它的非公开 `targetSize`。
//
// ⚠️ 三个坑：
//   1) 别用 `Screen.width/height` 当游戏视图分辨率：它们受游戏视图的 Scale 滑杆和
//      "自定义渲染分辨率"影响，而且编辑模式下没有意义。
//   2) `SetCustomRenderingResolution` **不会自己还原**（Unity 官方文档明说游戏视图不会自动
//      回到原分辨率）。所以谁改谁负责还原 —— 见 <see cref="RestoreGameViewSize"/>，
//      而且还原要放在收尾路径里，出错也得还原。
//   3) 反射一律"宽进宽出"：拿不到就返回 false 并只提示一次，**不要**抛出去。
//      游戏视图那些类型在每个 Unity 版本里都可能改名，这类失败不该把录制流程打断 ——
//      最坏的结果只是"出图按游戏视图当前大小"，那本来也是默认行为。
using System;
using System.Collections;
using System.Reflection;
using Hollow.HoUnityTools.Runtime.QuickCapture;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.QuickCapture
{
    /// <summary>游戏视图渲染分辨率的读写（跨 Unity 版本）。</summary>
    internal static class HoQuickCaptureGameView
    {
        /// <summary>我们往游戏视图尺寸列表里塞的那个自定义项的名字。</summary>
        private const string CustomSizeName = "Ho 快速渲染";

        /// <summary>`UnityEditor.GameViewSizeType.FixedResolution` 的底层值。</summary>
        private const int GameViewSizeTypeFixedResolution = 1;

        /// <summary>反射失败只提示一次，免得每帧刷屏。</summary>
        private static bool s_LoggedFailure;

        // ══════════════════════════════════════════════════════════════
        // 读
        // ══════════════════════════════════════════════════════════════

        /// <summary>游戏视图当前的**渲染**分辨率。拿不到时返回 false 并给出 1x1。</summary>
        public static bool GetRenderSize(out int width, out int height)
        {
            width = 1;
            height = 1;

            // ① 首选 `Handles.GetMainGameViewSize()`。
            //    它在 2021.3 与 Unity 6 上都是**公开、有文档、没标过时**的，
            //    实现就是 `PlayModeView.GetMainPlayModeViewTargetSize()` —— 和我们想要的是同一个值。
            //    比下面两条都省事，而且是唯一一条跨版本完全一致的公开路。
            try
            {
                Vector2 size = Handles.GetMainGameViewSize();
                if (size.x >= 1f && size.y >= 1f)
                {
                    width = Mathf.RoundToInt(size.x);
                    height = Mathf.RoundToInt(size.y);
                    return true;
                }
            }
            catch (Exception exception)
            {
                LogOnce("Handles.GetMainGameViewSize 抛异常：" + exception.Message);
            }

            // ② Unity 2022.2+ 的公开门面。
            //    ⚠️ 它会**顺手创建一个游戏视图窗口**（内部走 GetOrCreateWindow），
            //    而且在非 GameView 的视图上会直接抛 "Unsupported PlayModeView type"。
            //    所以先问 GetViewType（它自己也会抛，一并包住），确认是 GameView 再取值。
#if UNITY_2022_2_OR_NEWER
            if (PlayModeWindowViewIsGameView())
            {
                try
                {
                    PlayModeWindow.GetRenderingResolution(out uint w, out uint h);
                    if (w > 0 && h > 0)
                    {
                        width = (int)w;
                        height = (int)h;
                        return true;
                    }
                }
                catch (Exception exception)
                {
                    LogOnce("PlayModeWindow.GetRenderingResolution 抛异常：" + exception.Message);
                }
            }
#else
            // ③ 老版本的兜底：反射主 PlayModeView 的非公开 targetSize。
            if (TryGetRenderSizeByReflection(out width, out height))
            {
                return true;
            }
#endif
            return false;
        }

        // ══════════════════════════════════════════════════════════════
        // 写
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 把游戏视图的渲染分辨率临时改成 <paramref name="width"/> x <paramref name="height"/>。
        /// 改成功了返回 true；改不动返回 false（调用方据此说明"出图可能被拉伸"）。
        /// </summary>
        public static bool TrySetCustomSize(int width, int height)
        {
            if (width < 1 || height < 1)
            {
                return false;
            }

            // 不是 Game 视图就别改了：Simulator 视图下 SetCustomRenderingResolution
            // 只打一条日志然后什么都不干，不挡住的话我们会以为改成功了。
            if (!PlayModeWindowViewIsGameView())
            {
                LogOnce("主播放视图不是 Game 视图（设备模拟器？），改不了渲染分辨率。");
                return false;
            }

#if UNITY_2022_2_OR_NEWER
            try
            {
                PlayModeWindow.SetCustomRenderingResolution((uint)width, (uint)height, CustomSizeName);
                return true;
            }
            catch (Exception exception)
            {
                LogOnce("PlayModeWindow.SetCustomRenderingResolution 抛异常：" + exception.Message);
                return false;
            }
#else
            try
            {
                return SetCustomSizeByReflection(width, height);
            }
            catch (Exception exception)
            {
                LogOnce("反射改游戏视图尺寸失败：" + exception.Message);
                return false;
            }
#endif
        }

        /// <summary>
        /// 把游戏视图改回某个尺寸（录制结束的收尾）。走的是和设置同一个入口，
        /// 所以不会出现"设得进去、还原不回来"这种一半成功。
        /// </summary>
        public static void RestoreGameViewSize(int width, int height)
        {
            if (width < 1 || height < 1)
            {
                return;
            }

            TrySetCustomSize(width, height);
        }

        // ══════════════════════════════════════════════════════════════
        // 老版本的路（Unity &lt; 2022.2）
        // ══════════════════════════════════════════════════════════════

#if !UNITY_2022_2_OR_NEWER
        /// <summary>反射主 PlayModeView 的非公开 targetSize。</summary>
        private static bool TryGetRenderSizeByReflection(out int width, out int height)
        {
            width = 1;
            height = 1;

            try
            {
                object view = GetMainPlayModeView();
                if (view == null)
                {
                    LogOnce("没有活动的 PlayModeView（游戏视图没开着？）。");
                    return false;
                }

                PropertyInfo targetSize = FindProperty(
                    view.GetType(),
                    "targetSize",
                    BindingFlags.Instance);
                if (targetSize == null)
                {
                    LogOnce("PlayModeView 上没有 targetSize。");
                    return false;
                }

                Vector2 size = (Vector2)targetSize.GetValue(view, null);
                if (size.x < 1f || size.y < 1f)
                {
                    return false;
                }

                width = Mathf.RoundToInt(size.x);
                height = Mathf.RoundToInt(size.y);
                return true;
            }
            catch (Exception exception)
            {
                LogOnce("反射读游戏视图尺寸失败：" + exception.Message);
                return false;
            }
        }

        /// <summary>
        /// Unity 2021.3 的路子，照 Unity Recorder 老分支的做法：
        /// 在 GameViewSizes 的当前组里找一个叫 <see cref="CustomSizeName"/> 的自定义尺寸
        /// （没有就新建），改宽高，再选中它。
        /// </summary>
        private static bool SetCustomSizeByReflection(int width, int height)
        {
            Type sizesType = Type.GetType("UnityEditor.GameViewSizes,UnityEditor");
            Type sizeType = Type.GetType("UnityEditor.GameViewSize,UnityEditor");
            Type sizeKindType = Type.GetType("UnityEditor.GameViewSizeType,UnityEditor");
            if (sizesType == null || sizeType == null || sizeKindType == null)
            {
                LogOnce("找不到 GameViewSizes / GameViewSize / GameViewSizeType 类型。");
                return false;
            }

            PropertyInfo instanceProp = sizesType.BaseType == null
                ? null
                : FindProperty(sizesType.BaseType, "instance", BindingFlags.Static);
            object instance = instanceProp == null ? null : instanceProp.GetValue(null, null);
            if (instance == null)
            {
                LogOnce("GameViewSizes.instance 拿不到。");
                return false;
            }

            PropertyInfo groupProp = FindProperty(sizesType, "currentGroup", BindingFlags.Instance);
            object group = groupProp == null ? null : groupProp.GetValue(instance, null);
            if (group == null)
            {
                LogOnce("GameViewSizes.currentGroup 拿不到。");
                return false;
            }

            // 先在自定义列表里找一个已经叫这个名字的，省得每录一次就多一条死条目。
            object size = FindExistingCustomSize(group);
            if (size != null)
            {
                SetMember(size, "width", width);
                SetMember(size, "height", height);
            }
            else
            {
                size = CreateCustomSize(sizeType, sizeKindType, width, height);
                if (size == null)
                {
                    return false;
                }

                MethodInfo addCustomSize = FindMethod(
                    group.GetType(),
                    "AddCustomSize",
                    BindingFlags.Instance);
                if (addCustomSize == null)
                {
                    LogOnce("GameViewSizes 组上没有 AddCustomSize。");
                    return false;
                }

                addCustomSize.Invoke(group, new[] { size });
            }

            return SelectSize(group, size);
        }

        private static object FindExistingCustomSize(object group)
        {
            FieldInfo customField = FindField(
                group.GetType(),
                "m_Custom",
                BindingFlags.Instance);
            var customSizes = customField == null
                ? null
                : customField.GetValue(group) as IEnumerable;
            if (customSizes == null)
            {
                return null;
            }

            foreach (object candidate in customSizes)
            {
                FieldInfo baseTextField = FindField(
                    candidate.GetType(),
                    "m_BaseText",
                    BindingFlags.Instance);
                string baseText = baseTextField == null
                    ? null
                    : baseTextField.GetValue(candidate) as string;
                if (baseText == CustomSizeName)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static object CreateCustomSize(Type sizeType, Type sizeKindType, int width, int height)
        {
            // ⚠️ 枚举那个参数必须用 `Enum.ToObject` 造，**不能直接塞 int 1**。
            // `ConstructorInfo.Invoke` 不会替你把 int 转成枚举（CoreCLR 直接
            // ArgumentException，Mono 上看运气）。塞 int 的症状是这条反射路静默失败 ——
            // 外面有 catch，所以只表现为"游戏视图分辨率改不了"，看不出原因。
            object fixedResolution = Enum.ToObject(sizeKindType, GameViewSizeTypeFixedResolution);
            ConstructorInfo ctor = sizeType.GetConstructor(
                new[] { sizeKindType, typeof(int), typeof(int), typeof(string) });
            if (ctor == null)
            {
                LogOnce("GameViewSize 的构造函数签名变了。");
                return null;
            }

            return ctor.Invoke(new[] { fixedResolution, (object)width, height, CustomSizeName });
        }

        /// <summary>把某个尺寸选中（等于在游戏视图左上角那个下拉里点它）。</summary>
        private static bool SelectSize(object group, object size)
        {
            MethodInfo indexOf = FindMethod(group.GetType(), "IndexOf", BindingFlags.Instance);
            if (indexOf == null)
            {
                LogOnce("GameViewSizes 组上没有 IndexOf。");
                return false;
            }

            int index = (int)indexOf.Invoke(group, new[] { size });

            // 内置尺寸排在自定义前面，所以自定义项的真实下标要先跳过内置数量。
            FieldInfo builtinField = FindField(group.GetType(), "m_Builtin", BindingFlags.Instance);
            object builtin = builtinField == null ? null : builtinField.GetValue(group);
            bool isBuiltin = false;
            if (builtin != null)
            {
                MethodInfo contains = FindMethod(builtin.GetType(), "Contains", BindingFlags.Instance);
                isBuiltin = contains != null && (bool)contains.Invoke(builtin, new[] { size });
            }

            if (!isBuiltin)
            {
                MethodInfo builtinCount = FindMethod(
                    group.GetType(),
                    "GetBuiltinCount",
                    BindingFlags.Instance);
                if (builtinCount != null)
                {
                    index += (int)builtinCount.Invoke(group, null);
                }
            }

            object view = GetMainPlayModeView();
            if (view == null)
            {
                LogOnce("没有活动的 PlayModeView，改不了尺寸。");
                return false;
            }

            MethodInfo select = FindMethod(
                view.GetType(),
                "SizeSelectionCallback",
                BindingFlags.Instance);
            if (select == null)
            {
                LogOnce("PlayModeView 上没有 SizeSelectionCallback。");
                return false;
            }

            select.Invoke(view, new[] { (object)index, size });
            return true;
        }

        private static object GetMainPlayModeView()
        {
            Type playModeViewType = Type.GetType("UnityEditor.PlayModeView,UnityEditor");
            if (playModeViewType == null)
            {
                LogOnce("找不到 UnityEditor.PlayModeView。");
                return null;
            }

            MethodInfo getMain = FindMethod(
                playModeViewType,
                "GetMainPlayModeView",
                BindingFlags.Static);
            if (getMain == null)
            {
                LogOnce("UnityEditor.PlayModeView 上没有 GetMainPlayModeView。");
                return null;
            }

            return getMain.Invoke(null, null);
        }
#endif

        // ══════════════════════════════════════════════════════════════
        // 反射小工具：公开与非公开一起找
        //
        // ⚠️ `targetSize` 等成员**一定要带 NonPublic**：
        //    它在 2021.3 是 `protected`、在 Unity 6 是 `internal`，
        //    只给 Public 的话会静默拿不到（GetProperty 返回 null），
        //    症状是"分辨率永远读不出来"，而不会报任何错。
        //    另外 `targetSize` 是**继承自 PlayModeView** 的，
        //    所以反射要反射那个 EditorWindow 的**运行时类型**，别去反射 GameView 的声明类型。
        // ══════════════════════════════════════════════════════════════

        private static PropertyInfo FindProperty(Type type, string name, BindingFlags scope)
        {
            return type.GetProperty(name, scope | BindingFlags.Public | BindingFlags.NonPublic);
        }

        private static MethodInfo FindMethod(Type type, string name, BindingFlags scope)
        {
            return type.GetMethod(name, scope | BindingFlags.Public | BindingFlags.NonPublic);
        }

        private static FieldInfo FindField(Type type, string name, BindingFlags scope)
        {
            return type.GetField(name, scope | BindingFlags.Public | BindingFlags.NonPublic);
        }

        private static void SetMember(object target, string name, int value)
        {
            PropertyInfo property = FindProperty(target.GetType(), name, BindingFlags.Instance);
            if (property != null)
            {
                property.SetValue(target, value, null);
            }
        }

        /// <summary>
        /// 主播放视图现在是 Game 视图吗？
        ///
        /// 为什么要先问这一句：`PlayModeWindow.GetViewType()` 在遇到没见过的视图类型时
        /// **会抛** "Unsupported PlayModeView type"；而且设备模拟器（Simulator）视图下
        /// `SetCustomRenderingResolution` 只会打一条日志然后什么都不做 —— 也就是说
        /// "切分辨率"会静默失败。所以两条路都先过这道闸。
        /// （Unity Recorder 的做法更硬：它直接把模拟器视图**切回** Game 视图；
        /// 我们不抢用户的视图选择，只在不是 Game 视图时说明"改不了"。）
        /// </summary>
        private static bool PlayModeWindowViewIsGameView()
        {
#if UNITY_2022_2_OR_NEWER
            try
            {
                return PlayModeWindow.GetViewType() == PlayModeWindow.PlayModeViewTypes.GameView;
            }
            catch (Exception exception)
            {
                LogOnce("PlayModeWindow.GetViewType 抛异常：" + exception.Message);
                return false;
            }
#else
            return true;
#endif
        }

        private static void LogOnce(string message)
        {
            if (s_LoggedFailure)
            {
                return;
            }

            s_LoggedFailure = true;
            Debug.LogWarning("[快速渲染] 游戏视图分辨率接口不可用（只提示这一次）：" + message);
        }
    }
}
