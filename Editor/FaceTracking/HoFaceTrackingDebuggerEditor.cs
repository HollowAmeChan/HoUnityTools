using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hollow.HoUnityTools.Editor.Constraints;
using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    [CustomEditor(typeof(HoFaceTrackingDebugger))]
    public sealed class HoFaceTrackingDebuggerEditor : UnityEditor.Editor
    {
        private Vector2 scroll;
        private string search = "";
        private string report = "";
        private bool reportIsError;
        private bool setupExpanded = true;
        private bool initExpanded = true;
        private bool outputExpanded = true;
        private bool middleExpanded = true;
        private bool channelsExpanded;
        private double nextRepaint;
        private static readonly string[] Modes = { "实时", "手动", "保持", "中性", "交还" };

        /// <summary>眼睑的三档。底层还是原来那两个布尔字段（序列化兼容），但**呈现成互斥三选一**。</summary>
        private static readonly string[] EyeLidModeLabels = { "左右独立", "强制同眨", "单键双眼" };
        private const string ModeTooltip = "这一路输入怎么来：\n实时 = 用手机数据\n手动 = 用滑杆\n保持 = 冻结在当前值\n中性 = 写设定的中性值\n交还 = 不碰这个键，让给基础动画";
        private const string ControllerValueTooltip = "真正写进混合树参数的值（面捕输入经过增益与钳制之后）。";

        private void OnEnable() => EditorApplication.update += Refresh;
        private void OnDisable() => EditorApplication.update -= Refresh;
        private void Refresh()
        {
            if (EditorApplication.timeSinceStartup < nextRepaint) return;
            nextRepaint = EditorApplication.timeSinceStartup + 0.1;
            Repaint();
        }

        public override void OnInspectorGUI()
        {
            var rig = (HoFaceTrackingDebugger)target;
            var session = HoFaceInputHub.Session(rig);
            HoConstraintEditorControls.Title(
                "Ho 面捕调试",
                rig.faceController != null ? rig.faceController.name : "未指定控制器",
                (session != null ? "驱动中" : "待机", session != null),
                (Application.isPlaying ? "播放中" : "编辑中", Application.isPlaying));

            // ── 接线：这台组件接到哪 ──────────────────────────────────────────────
            string setupSummary = rig.faceController != null ? rig.faceController.name : "未指定控制器";
            if (HoConstraintEditorSectionGui.DrawSectionHeader(ref setupExpanded, "接线", setupSummary,
                HoConstraintEditorTheme.AccentMesh))
            using (HoConstraintEditorControls.Card())
            {
                serializedObject.Update();
                EditorGUILayout.PropertyField(serializedObject.FindProperty("targetAnimator"), new GUIContent("角色 Animator", "角色根上的 Animator。面捕不会接管它，只借用它的绑定根解析路径。"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("faceController"), new GUIContent("面部控制器", "只含形态键曲线的纯 Unity AnimatorController；在影子层级上求值，不驱动角色本体。"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("startOnPlay"), new GUIContent("运行时自动开始", "进入播放模式就自动开始驱动；驱动意外掉线（异常、接收线程出错）时会自动重试。\n它不会替你连接手机 —— 手机那边要自己开始发送。"));
                serializedObject.ApplyModifiedProperties();

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("全局连接面板", GUILayout.Height(20))) HoFaceTrackingWindow.ShowWindow();
                    HoConstraintEditorControls.Flex();
                    using (new EditorGUI.DisabledScope(!Application.isPlaying))
                        if (GUILayout.Button(session == null ? "开始驱动" : "停止并交还动画", GUILayout.Height(20)))
                        {
                            if (session == null) HoFaceInputHub.Start(rig); else HoFaceInputHub.Stop(rig);
                        }
                }
            }

            // ── 初始化：把一份现成的混合树文件搬到这台角色上 ──────────────────────
            // 控制器是**作品**（Jerry 的 vrc-common、我们自己编的 ho-2d-test1…），在混合树编辑器里编出来，
            // 自带片段与动画。所以这一栏没有"生成配置"，只有两件事：搬哪一份、驱动哪些网格。
            // 参数不由控制器声明 —— 它只等着被喂，喂什么由中间层决定（见 docs/FACE_TRACKING_WORKFLOW.md）。
            if (HoConstraintEditorSectionGui.DrawSectionHeader(ref initExpanded, "初始化", InitSummary(rig),
                HoConstraintEditorTheme.AccentBlink))
            using (HoConstraintEditorControls.Card())
            {
                serializedObject.Update();
                EditorGUILayout.PropertyField(serializedObject.FindProperty("sourceController"),
                    new GUIContent("源控制器", "要搬运的那份混合树文件。初始化会把**整份文件**复制成面部控制器，\n"
                        + "只把动画驱动的对象换成下面的驱动对象（层、状态、参数、子树、片段都跟着走）。"));
                serializedObject.ApplyModifiedProperties();

                if (rig.sourceController == null)
                    HoConstraintEditorControls.Caption("先选一份源控制器。");
                else if (!(rig.sourceController is AnimatorController))
                    HoConstraintEditorControls.Caption("源控制器必须是纯 Unity AnimatorController（不接受 OverrideController）。");

                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Label("驱动对象", HoConstraintEditorTheme.LabelWidth,
                        "面捕要驱动哪些网格 —— 初始化把控制器里的形态键动画重绑到这些网格上。\n"
                        + "控制器写了某个键、而这些网格里谁都没有时，那一格会被跳过（下面的结构摘要会列出来）。");
                    if (HoConstraintEditorControls.Button("按 Animator 填充", "把角色 Animator 下所有网格填进来。"))
                    {
                        Undo.RecordObject(rig, "Fill face meshes");
                        rig.meshes = rig.targetAnimator != null
                            ? new List<SkinnedMeshRenderer>(rig.targetAnimator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                            : new List<SkinnedMeshRenderer>();
                        EditorUtility.SetDirty(rig);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(rig);
                    }

                    HoConstraintEditorControls.Flex();
                }

                serializedObject.Update();
                EditorGUILayout.PropertyField(serializedObject.FindProperty("meshes"), GUIContent.none, true);
                serializedObject.ApplyModifiedProperties();

                using (new EditorGUILayout.HorizontalScope())
                {
                    bool initialized = rig.faceController != null;
                    using (new EditorGUI.DisabledScope(Application.isPlaying || rig.targetAnimator == null
                        || rig.sourceController == null || rig.meshes == null || rig.meshes.Count == 0))
                    {
                        // 命名承载语义：「初始化」明确表达"产出文件、之后都在里面改"。
                        // 一个按钮两种情形（见 Initialize 的注释）：已经有控制器 → 就地重写它；
                        // 第一次 → 问路径新建。源控制器就是它自己时就地重绑。
                        var content = initialized
                            ? new GUIContent("重新初始化…", "把源控制器**整份**搬过来覆盖它：\n"
                                + "GUID 不变，所以引用它的地方不会断；旧的层/片段/参数由这次搬运决定。")
                            : new GUIContent("初始化控制器", "把源控制器整份搬成一份属于这台角色的控制器。\n"
                                + "这是唯一会写文件的动作。");
                        if (GUILayout.Button(content, GUILayout.Height(20))) Initialize(rig);
                    }

                    // 写盘的动作只有上面那一个：目标就是组件当前在用的那个控制器（没指定时才问路径）。
                    using (new EditorGUI.DisabledScope(Application.isPlaying || rig.targetAnimator == null))
                    if (GUILayout.Button(new GUIContent("定位资产", "选中这个控制器资产。"), GUILayout.Height(20)) && rig.faceController != null)
                    {
                        Selection.activeObject = rig.faceController;
                        EditorGUIUtility.PingObject(rig.faceController);
                    }

                    HoConstraintEditorControls.Flex();
                }
            }

            if (!string.IsNullOrEmpty(HoFaceInputHub.Error(rig))) EditorGUILayout.HelpBox(HoFaceInputHub.Error(rig), MessageType.Error);
            if (!string.IsNullOrEmpty(report)) EditorGUILayout.HelpBox(report, reportIsError ? MessageType.Warning : MessageType.Info);
            if (session != null && session.Compiled != null && session.Compiled.warnings.Count > 0)
                EditorGUILayout.HelpBox(string.Join("\n", session.Compiled.warnings.Take(8)), MessageType.Warning);

            serializedObject.Update();
            // 折叠只决定"这一栏的内容画不画"，不决定"后面还有没有别的栏"。
            // 这里原来写成 `if (!展开) { DrawChannels(); return; }` —— 一个 return 把下面的
            // 「参数生产」整段跳过了，于是它只在输出栏展开时才存在，看着像是藏在里面。
            if (HoConstraintEditorSectionGui.DrawSectionHeader(
                ref outputExpanded,
                "控制器输出参数设置",
                RegionSummary(rig.outputRegions),
                HoConstraintEditorTheme.AccentOutput))
            using (HoConstraintEditorControls.Card())
            {
                var regions = serializedObject.FindProperty("outputRegions");
                // 两个闸，对齐参考实现的 EyeTrackingActive / LipTrackingActive。
                using (HoConstraintEditorControls.Row(true))
                {
                    DrawRegion(regions, EyeMask, "眼（眼皮 / 眉）",
                        "面捕驱动这一块：eyeBlink / eyeSquint / eyeWide / brow。\n"
                        + "**眉归眼区** —— 参考实现里眉挂的是 EyeTrackingActive（眉毛跟着眼神走，不是跟着嘴走）。\n"
                        + "断流后交还给自动眨眼。");
                    HoConstraintEditorControls.Gap();
                    DrawRegion(regions, LipMask, "唇（嘴 / 脸颊）",
                        "面捕驱动这一块：jaw / mouth / tongue / cheek / noseSneer。\n"
                        + "颊鼻归唇区 —— 参考实现里它们挂的是 LipTrackingActive。");
                    HoConstraintEditorControls.Flex();
                }

                // 凝视不是第三个闸，只是**面捕这一侧的开关** —— LookAt 那边也有自己的开关，
                // 怎么分工由用户定，所以这里默认开着，不预设"凝视归 LookAt"。
                using (HoConstraintEditorControls.Row(true))
                {
                    DrawRegion(regions, HoFaceRegion.Gaze, "凝视形态键（eyeLook*）",
                        "面捕这一侧是否驱动 eyeLook* 键。默认开。\n"
                        + "如果你同时用 HoLookAt 驱动眼球，那边也有一个开关 —— 两边只留一个，\n"
                        + "否则同一个方向会被写两遍（本面板会提示，但不会拦你）。");
                    HoConstraintEditorControls.Flex();
                }

                // 结构报告：**从资产读出来**，不是从配置推断。
                // 控制器是搬来的作品，中间层有些处理是隐式的（重叠由姿势表权衡），
                // 所以这里把控制器实际的形状、以及"哪些键这台模型上没有"说出来。
                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("控制器结构", HoConstraintEditorTheme.LabelWidth,
                        "**从资产读出来的实况**（不是配置声明）：几层、几个状态、几个片段、会写哪些形态键。\n"
                        + "控制器的形状由它自己决定 —— 要改结构就去编那份控制器，然后重新初始化。\n"
                        + "面捕只驱动自己有通道的标准 ARKit 键：控制器里写的别的键（别人模型的专有键）不归我们。");
                    HoConstraintEditorControls.Caption(StructureSummary(rig));
                    HoConstraintEditorControls.Flex();
                }

                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("断流等待", HoConstraintEditorTheme.LabelWidth, "多久没有有效帧就算断流。");
                    serializedObject.FindProperty("staleSeconds").floatValue =
                        HoConstraintEditorControls.NumberField(serializedObject.FindProperty("staleSeconds").floatValue, "秒", null,
                            HoConstraintEditorTheme.FieldWidthWide + 14.0f);
                    HoConstraintEditorControls.Gap(8.0f);
                    HoConstraintEditorControls.Label("回中性", HoConstraintEditorTheme.LabelWidth, "断流后淡回中性值用多久。");
                    serializedObject.FindProperty("neutralFadeSeconds").floatValue =
                        HoConstraintEditorControls.NumberField(serializedObject.FindProperty("neutralFadeSeconds").floatValue, "秒", null,
                            HoConstraintEditorTheme.FieldWidthWide + 14.0f);
                    HoConstraintEditorControls.Flex();
                }
            }

            // ── 参数生产：中间层的处理器，一行一个 ────────────────────────────────
            // **这一栏是为长大准备的**：以后新的整形（ramp / 抑制 / 轴合并 / 模式开关）都加在这里，
            // 别塞回上面那两栏 —— 上面两栏回答"接到哪""写什么"，这里回答"值怎么被加工"。
            if (HoConstraintEditorSectionGui.DrawSectionHeader(ref middleExpanded, "参数生产", MiddleSummary(rig),
                HoConstraintEditorTheme.AccentDriver))
            using (HoConstraintEditorControls.Card())
            {
                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("分组平滑", HoConstraintEditorTheme.LabelWidth,
                        "每组一个指数平滑时长（秒）；0 = 不过滤、直接透传。\n"
                        + "眼球要跟得紧、眼睑要稳、嘴更黏，所以分组而不是一个统一系数。\n"
                        + "面板「面捕输入 → Controller 实值」两列就是平滑前 / 平滑后。");
                    HoConstraintEditorControls.Label("眼睑", HoConstraintEditorTheme.LabelWidthSm);
                    serializedObject.FindProperty("smoothEyelids").floatValue = HoConstraintEditorControls.NumberField(serializedObject.FindProperty("smoothEyelids").floatValue, null, null, HoConstraintEditorTheme.FieldWidthWide);
                    HoConstraintEditorControls.Gap(4.0f);
                    HoConstraintEditorControls.Label("眼球", HoConstraintEditorTheme.LabelWidthSm);
                    serializedObject.FindProperty("smoothGaze").floatValue = HoConstraintEditorControls.NumberField(serializedObject.FindProperty("smoothGaze").floatValue, null, null, HoConstraintEditorTheme.FieldWidthWide);
                    HoConstraintEditorControls.Gap(4.0f);
                    HoConstraintEditorControls.Label("嘴", HoConstraintEditorTheme.LabelWidthSm);
                    serializedObject.FindProperty("smoothMouth").floatValue = HoConstraintEditorControls.NumberField(serializedObject.FindProperty("smoothMouth").floatValue, null, null, HoConstraintEditorTheme.FieldWidthWide);
                    HoConstraintEditorControls.Gap(4.0f);
                    HoConstraintEditorControls.Label("其它", HoConstraintEditorTheme.LabelWidthSm);
                    serializedObject.FindProperty("smoothOther").floatValue = HoConstraintEditorControls.NumberField(serializedObject.FindProperty("smoothOther").floatValue, null, null, HoConstraintEditorTheme.FieldWidthWide);
                    HoConstraintEditorControls.Flex();
                }

                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("分组死区", HoConstraintEditorTheme.LabelWidth,
                        "低于它的实时输入按 0 处理，以上重新铺满 —— 压住静止时的抖动。0 = 关。\n"
                        + "只作用在实时输入上；手动滑杆是调试用的，不受影响。\n"
                        + "位置对齐参考实现的 OSCm/Sensitivity 分组。");
                    HoConstraintEditorControls.Label("眼睑", HoConstraintEditorTheme.LabelWidthSm);
                    serializedObject.FindProperty("deadZoneEyelids").floatValue = HoConstraintEditorControls.NumberField(serializedObject.FindProperty("deadZoneEyelids").floatValue, null, null, HoConstraintEditorTheme.FieldWidthWide);
                    HoConstraintEditorControls.Gap(4.0f);
                    HoConstraintEditorControls.Label("眼球", HoConstraintEditorTheme.LabelWidthSm);
                    serializedObject.FindProperty("deadZoneGaze").floatValue = HoConstraintEditorControls.NumberField(serializedObject.FindProperty("deadZoneGaze").floatValue, null, null, HoConstraintEditorTheme.FieldWidthWide);
                    HoConstraintEditorControls.Gap(4.0f);
                    HoConstraintEditorControls.Label("嘴", HoConstraintEditorTheme.LabelWidthSm);
                    serializedObject.FindProperty("deadZoneMouth").floatValue = HoConstraintEditorControls.NumberField(serializedObject.FindProperty("deadZoneMouth").floatValue, null, null, HoConstraintEditorTheme.FieldWidthWide);
                    HoConstraintEditorControls.Gap(4.0f);
                    HoConstraintEditorControls.Label("其它", HoConstraintEditorTheme.LabelWidthSm);
                    serializedObject.FindProperty("deadZoneOther").floatValue = HoConstraintEditorControls.NumberField(serializedObject.FindProperty("deadZoneOther").floatValue, null, null, HoConstraintEditorTheme.FieldWidthWide);
                    HoConstraintEditorControls.Flex();
                }

                // 眼睑：**三种互斥档位**，不是两个独立开关。
                // （曾经就是两个布尔，其中"同步关 + 单键开"根本到不了 —— 用户踩过，所以收成一个三选一。）
                using (HoConstraintEditorControls.Row())
                {
                    SerializedProperty sync = serializedObject.FindProperty("eyeSync");
                    SerializedProperty single = serializedObject.FindProperty("eyeSyncSingleKey");
                    int mode = sync.boolValue ? (single.boolValue ? 2 : 1) : 0;
                    HoConstraintEditorControls.Label("眼睑", HoConstraintEditorTheme.LabelWidth,
                        "按模型上那对眨眼键是**哪种做法**来选（三档互斥）：\n"
                        + "· 左右独立：每个键各管一只眼 —— 能 wink，绝大多数模型是这种。\n"
                        + "· 强制同眨：把左右合成一个值再写回两侧 —— 两眼闭得不一致时用；开了就不能 wink。\n"
                        + "· 单键双眼：只驱动一侧、另一侧写 0 —— 只给「左右键各自都能闭双眼」的模型。");
                    mode = HoConstraintEditorControls.Segmented(
                        HoConstraintEditorControls.Next(HoConstraintEditorControls.SegmentedWidth(EyeLidModeLabels)),
                        mode, EyeLidModeLabels, "按模型的眨眼键怎么做的来选；选错会表现为 wink 没了或只有一只眼眨。");
                    sync.boolValue = mode >= 1;
                    single.boolValue = mode >= 2;

                    HoConstraintEditorControls.Gap(8.0f);
                    using (new EditorGUI.DisabledScope(mode == 0))
                    {
                        HoConstraintEditorControls.Label("配比", HoConstraintEditorTheme.LabelWidthSm,
                            "同步到哪个值：0 = 全用左眼，0.5 = 平均，1 = 全用右眼。");
                        serializedObject.FindProperty("eyeSyncMix").floatValue = HoConstraintEditorControls.NumberField(
                            serializedObject.FindProperty("eyeSyncMix").floatValue, null, null, HoConstraintEditorTheme.FieldWidthWide);
                    }

                    HoConstraintEditorControls.Flex();
                }
            }

            serializedObject.ApplyModifiedProperties();
            DrawChannels(rig, session);
        }

        /// <summary>初始化摘要：搬了没有、搬过来的是几层几个片段。</summary>
        private static string InitSummary(HoFaceTrackingDebugger rig)
        {
            if (rig.faceController == null) return "未初始化";
            if (!(rig.faceController is AnimatorController controller)) return "不是 AnimatorController";
            var info = HoFaceAnimationAssets.Inspect(controller, rig.meshes);
            return "已初始化 · " + info.layers + " 层 / " + info.clips + " 个片段";
        }

        /// <summary>
        /// 控制器资产的**实况**（从资产读，不是从配置推断）：层/状态/片段/会写哪些形态键，
        /// 以及"控制器写了、但这台模型的驱动对象上没有"的那些键。
        /// </summary>
        private static string StructureSummary(HoFaceTrackingDebugger rig)
        {
            if (!(rig.faceController is AnimatorController controller)) return "未指定控制器";
            var info = HoFaceAnimationAssets.Inspect(controller, rig.meshes);
            if (info.clips == 0) return "这份控制器里没有片段";

            string text = info.layers + " 层 · " + info.states + " 个状态 · " + info.clips + " 个片段 · 写 "
                + (info.shapes.Count + info.missing.Count) + " 个形态键";
            if (info.otherCurves > 0)
                text += "\n另有 " + info.otherCurves + " 条非形态键曲线（按原路径保留）："
                    + string.Join("、", info.otherKinds.Take(4));
            if (info.missing.Count > 0)
                text += "\n⚠ 驱动对象上没有这些键，对应格子会被跳过：" + string.Join("、", info.missing);
            return text;
        }

        /// <summary>
        /// 中间层摘要：**开着哪几类处理器**。中间层只会越长越多，所以它的摘要不列数值，
        /// 只报"哪几件在干活"—— 一眼能看出"我现在到底加工了什么"。
        /// </summary>
        private static string MiddleSummary(HoFaceTrackingDebugger rig)
        {
            var parts = new List<string>();
            if (rig.smoothEyelids > 0.0f || rig.smoothGaze > 0.0f || rig.smoothMouth > 0.0f || rig.smoothOther > 0.0f)
                parts.Add("平滑");
            if (rig.deadZoneEyelids > 0.0f || rig.deadZoneGaze > 0.0f || rig.deadZoneMouth > 0.0f || rig.deadZoneOther > 0.0f)
                parts.Add("死区");
            if (rig.eyeSync) parts.Add(rig.eyeSyncSingleKey ? "双眼同步·单键" : "双眼同步");

            // 轴生产也是照着资产报的：控制器里真有那两根参数，写进去才算数。
            if (rig.faceController is AnimatorController controller)
                foreach (var p in controller.parameters)
                    if (p.name == HoFaceNaming.LidAxis(0, true))
                    {
                        parts.Add("眼睑轴");
                        break;
                    }

            return parts.Count == 0 ? "全部关" : string.Join(" · ", parts);
        }

        /// <summary>
        /// 「眼」「唇」两个闸覆盖的区域，对齐参考实现的 `EyeTrackingActive` / `LipTrackingActive`：
        /// **眼 = 眼睑 + 眉**（眉跟着眼神走）、**唇 = 嘴 + 颊鼻**。凝视是单独一个开关，不在两闸里。
        /// </summary>
        private const HoFaceRegion EyeMask = HoFaceRegion.Eyelids | HoFaceRegion.Brows;

        private const HoFaceRegion LipMask = HoFaceRegion.Mouth | HoFaceRegion.Cheeks;

        private static string RegionSummary(HoFaceRegion regions)
        {
            bool eyes = (regions & EyeMask) != 0;
            bool lips = (regions & LipMask) != 0;
            bool gaze = (regions & HoFaceRegion.Gaze) != 0;
            return (eyes ? "眼" : "眼 ✕") + " · " + (lips ? "唇" : "唇 ✕") + (gaze ? " · 凝视给面捕" : "");
        }

        private void DrawChannels(HoFaceTrackingDebugger rig, HoFaceAnimationSession session)
        {
            serializedObject.Update();
            var channels = serializedObject.FindProperty("channels");
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(
                ref channelsExpanded,
                "输入参数",
                channels.arraySize + " 路",
                HoConstraintEditorTheme.AccentRules))
            {
                return;
            }

            using (HoConstraintEditorControls.Card())
            {
                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Label("筛选", HoConstraintEditorTheme.LabelWidthSm);
                    search = EditorGUI.TextField(HoConstraintEditorControls.NextFlexible(80.0f), search, HoConstraintEditorTheme.Field);
                    HoConstraintEditorControls.Gap();
                    if (HoConstraintEditorControls.Button("全实时", "把所有通道切回用手机数据。")) SetMode(channels, HoFaceInputMode.Live);
                    HoConstraintEditorControls.Gap(4.0f);
                    if (HoConstraintEditorControls.Button("全手动", "全部改用滑杆，方便没有设备时调试。")) SetMode(channels, HoFaceInputMode.Manual);
                    HoConstraintEditorControls.Gap(4.0f);
                    if (HoConstraintEditorControls.Button("全中性", "全部写中性值，相当于暂时关掉面捕输出。")) SetMode(channels, HoFaceInputMode.Neutral);
                }

                scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(300.0f));
                for (int i = 0; i < channels.arraySize; i++)
                {
                    DrawChannelRow(channels.GetArrayElementAtIndex(i), session);
                }

                EditorGUILayout.EndScrollView();
            }

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// 一行一路：键名 / 模式 / 手动滑杆或原值→输入 / Controller 实值。
        /// 原来一路占三行（粗体名、模式行、Controller 行），52 路就是一面墙；这里压成一行。
        /// </summary>
        private void DrawChannelRow(SerializedProperty channel, HoFaceAnimationSession session)
        {
            string shape = channel.FindPropertyRelative("shape").stringValue;
            if (!string.IsNullOrEmpty(search) && shape.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) return;

            int index = HoFaceTrackingChannels.IndexOf(shape);
            if (index < 0)
            {
                using (HoConstraintEditorControls.Row(true)) HoConstraintEditorControls.Caption("未知 ARKit 键：" + shape);
                return;
            }

            using (HoConstraintEditorControls.Row(true))
            {
                HoConstraintEditorControls.Label(shape, 116.0f, "标准 ARKit 键名。");
                var mode = channel.FindPropertyRelative("mode");
                mode.enumValueIndex = HoConstraintEditorControls.Dropdown(
                    HoConstraintEditorControls.Next(58.0f), mode.enumValueIndex, Modes, ModeTooltip);

                if (mode.enumValueIndex == (int)HoFaceInputMode.Manual)
                {
                    var manual = channel.FindPropertyRelative("manual");
                    manual.floatValue = HoConstraintEditorControls.MiniSlider(manual.floatValue, 0.0f, 1.0f, 0.0f, "拖动改手动值，双击回 0。");
                }
                else
                {
                    string raw = HoFaceInputHub.ReceivedAt[index] > 0 ? HoFaceInputHub.Raw[index].ToString("F3") : "未收到";
                    HoConstraintEditorControls.Caption("原值 " + raw + (session != null ? " → " + session.Effective[index].ToString("F3") : ""));
                    HoConstraintEditorControls.Flex();
                }

                if (session != null)
                {
                    GUI.Label(HoConstraintEditorControls.Next(46.0f), new GUIContent(session.ControllerValues[index].ToString("F3"), ControllerValueTooltip), HoConstraintEditorTheme.Value);
                }
            }
        }

        /// <summary>输出闸开关。调用方负责开一行；这里只管画一个勾选框并回写。
        /// mask 可以是多位的组合（「唇」= 嘴|眉|脸颊），任一位为真即视为开，切换时整组一起写。</summary>
        private static void DrawRegion(SerializedProperty property, HoFaceRegion mask, string label, string tooltip = null)
        {
            bool enabled = (property.intValue & (int)mask) != 0;
            bool next = HoConstraintEditorControls.Toggle(label, enabled, tooltip);
            if (enabled != next) property.intValue = next ? property.intValue | (int)mask : property.intValue & ~(int)mask;
        }

        private static void SetMode(SerializedProperty channels, HoFaceInputMode mode)
        {
            for (int i = 0; i < channels.arraySize; i++) channels.GetArrayElementAtIndex(i).FindPropertyRelative("mode").enumValueIndex = (int)mode;
        }

        /// <summary>
        /// 初始化 = 把源控制器**整份**搬成这台角色的面部控制器。**目标就是组件当前在用的那个资产**，
        /// 所以用户只看到**一个按钮**：
        /// <list type="bullet">
        /// <item>已经有控制器 → 就地重写它（GUID 不变，引用它的地方不会断）；</item>
        /// <item>还没有 → 问路径新建；目标已存在时再确认一次覆盖。</item>
        /// <item>源控制器就是它自己 → 就地重绑动画对象（没有可复制的东西）。</item>
        /// </list>
        /// </summary>
        private void Initialize(HoFaceTrackingDebugger rig)
        {
            var source = rig.sourceController as AnimatorController;
            if (source == null) { Fail(new InvalidOperationException("先选一份源控制器。")); return; }
            if (rig.meshes == null || rig.meshes.Count == 0) { Fail(new InvalidOperationException("驱动对象列表是空的 —— 先「按 Animator 填充」。")); return; }
            if (rig.targetAnimator == null) { Fail(new InvalidOperationException("先指定角色 Animator。")); return; }

            string sourcePath = AssetDatabase.GetAssetPath(source);
            var current = rig.faceController as AnimatorController;
            string path = current != null ? AssetDatabase.GetAssetPath(current) : "";
            if (string.IsNullOrEmpty(path))
            {
                path = EditorUtility.SaveFilePanelInProject("初始化面部控制器", "Face_Controller", "controller",
                    "产出一份属于这台角色的面部控制器；以后「重新初始化」只动这一个文件。");
                if (string.IsNullOrEmpty(path)) return;
                if (AssetDatabase.LoadMainAssetAtPath(path) != null
                    && !EditorUtility.DisplayDialog("要覆盖这个控制器吗？",
                        path + "\n\n该文件已存在，会被整个重写。", "覆盖并初始化", "取消"))
                    return;
            }
            else if (string.Equals(path, sourcePath, StringComparison.Ordinal))
            {
                // 源就是目标：没有可复制的，只把动画对象重绑一遍。
                if (!EditorUtility.DisplayDialog("就地重绑吗？",
                    path + "\n\n这份文件既是源也是目标，会就地重绑它的动画对象。", "重绑", "取消"))
                    return;
            }
            else if (!EditorUtility.DisplayDialog("重新初始化吗？",
                path + "\n\n会把源控制器（" + source.name + "）**整份**搬过来覆盖它。\n"
                + "资产 GUID 不变，所以引用它的地方（比如窥视对象）不会断。", "覆盖", "取消"))
                return;

            try
            {
                var controller = HoFaceAnimationAssets.Adopt(source, path, rig.meshes, rig.targetAnimator, true);
                Undo.RecordObject(rig, "Initialize face controller");
                rig.faceController = controller;
                EnsureChannels(rig);   // 只补齐缺失的通道，不覆盖用户已经调过的
                EditorUtility.SetDirty(rig);
                PrefabUtility.RecordPrefabInstancePropertyModifications(rig);
                reportIsError = false;
                var info = HoFaceAnimationAssets.Inspect(controller, rig.meshes);
                report = "已初始化 " + path + "：" + info.layers + " 层 / " + info.clips + " 个片段，驱动 "
                    + info.shapes.Count + " 个形态键"
                    + (info.missing.Count > 0 ? "（" + info.missing.Count + " 个键驱动对象上没有，已跳过）" : "") + "。";
            }
            catch (Exception e) { Fail(e); }
        }

        /// <summary>失败必须显眼 —— 之前和成功消息共用蓝色 Info 框，结果被用户当提示略过去了。</summary>
        private void Fail(Exception e)
        {
            reportIsError = true;
            report = e.Message;
        }

        /// <summary>
        /// 补齐缺失的标准 ARKit 通道。**不覆盖已有的** —— 初始化不该动用户调过的配置，
        /// 隐式重置（原来的 `channels = CreateDefaults()`）是这套契约明令禁止的。
        /// </summary>
        private static void EnsureChannels(HoFaceTrackingDebugger rig)
        {
            if (rig.channels == null)
            {
                rig.channels = HoFaceTrackingChannels.CreateDefaults();
                return;
            }

            var have = new HashSet<string>(StringComparer.Ordinal);
            foreach (var channel in rig.channels)
                if (channel != null && !string.IsNullOrEmpty(channel.shape)) have.Add(channel.shape);
            foreach (var fresh in HoFaceTrackingChannels.CreateDefaults())
                if (!have.Contains(fresh.shape)) rig.channels.Add(fresh);
            rig.channels.RemoveAll(c => c == null || string.IsNullOrEmpty(c.shape));
        }

        private void Check(HoFaceTrackingDebugger rig)
        {
            try
            {
                using (var compiled = HoFaceAnimationAssets.Compile(rig))
                    report = "可绑定输出：" + compiled.bindings.Count + "\n" + string.Join("\n", compiled.warnings.Take(12));
            }
            catch (Exception e) { Fail(e); }
        }
    }
}
