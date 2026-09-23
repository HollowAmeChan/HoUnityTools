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
        /// <summary>目标网格上真实存在的形态键（用来跟模板"需要的键"做差集）。</summary>
        private readonly HashSet<string> shapeKeys = new HashSet<string>();
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

            // ── 初始化：程序化产出「状态 + 驱动映射」──────────────────────────────
            // **这一栏的产物不是"几个参数"，而是一大片状态与驱动映射**（片段 + 混合树 + 门控参数）。
            // 这是整个流程的关键一句：初始化 = 生产状态与映射，不是手搓。
            // 所以以后要加什么状态，就是往这一栏加"生成配置"（预设开关），而不是让用户自己去编。
            if (HoConstraintEditorSectionGui.DrawSectionHeader(ref initExpanded, "初始化", InitSummary(rig),
                HoConstraintEditorTheme.AccentBlink))
            using (HoConstraintEditorControls.Card())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool initialized = rig.faceController != null;
                    using (new EditorGUI.DisabledScope(Application.isPlaying || rig.targetAnimator == null))
                    {
                        // 命名承载语义：「初始化」明确表达"产出文件、之后都在里面改"。
                        // 一个按钮两种情形（见 Initialize 的注释）：我们的控制器 → 就地重写；
                        // 第一次 / 别人的控制器 → 问路径新建。
                        var content = initialized
                            ? new GUIContent("重新初始化…", "就地重写这个控制器的**整个文件**：\n"
                                + "旧的树、片段、参数清干净，GUID 不变（外部引用不会断）。\n"
                                + "换模板走这里。")
                            : new GUIContent("初始化控制器", "按当前配置产出一个完整的控制器文件。\n"
                                + "这是唯一会写文件的动作。");
                        if (GUILayout.Button(content, GUILayout.Height(20))) Initialize(rig);
                    }

                    // 「重新初始化」是**唯一**写盘的动作：目标就是组件当前在用的那个控制器。
                    // 它自己会挑路径 —— 是我们的控制器就就地重写（GUID 不变），否则才问路径新建。
                    // 所以不需要第二个按钮，也不需要用户自己备份。
                    using (new EditorGUI.DisabledScope(Application.isPlaying || rig.targetAnimator == null))
                    if (GUILayout.Button(new GUIContent("定位资产", "选中这个控制器资产。"), GUILayout.Height(20)) && rig.faceController != null)
                    {
                        Selection.activeObject = rig.faceController;
                        EditorGUIUtility.PingObject(rig.faceController);
                    }

                    HoConstraintEditorControls.Flex();
                }

                // 模板：决定眼睑那几棵树长什么样、**需要哪些键**。留空 = 内置默认（ho-2d-test1）。
                serializedObject.Update();
                EditorGUILayout.PropertyField(serializedObject.FindProperty("template"),
                    new GUIContent("混合树模板", "决定眼睑那几棵树长什么样、需要哪些键。\n留空 = 内置默认（ho-2d-test1）。\n"
                        + "改模板是**结构改动**，要走「应用改动」或重新初始化才生效。"));
                serializedObject.ApplyModifiedProperties();

                var template = rig.template != null ? rig.template.spec : HoFaceTemplateDefaults.TwoDTest1();
                if (rig.targetAnimator != null)
                {
                    // 只报"缺键"这一件实事（缺了不报错、只是那几格写不进去，所以必须说出来）；
                    // 模板的血统、出处、需要哪些键都写在模板资产自己的 notes 里，不在面板上堆。
                    CollectShapeKeys(rig, shapeKeys);
                    var missing = new List<string>();
                    foreach (string key in template.UsedKeys())
                        if (!shapeKeys.Contains(key)) missing.Add(key);
                    if (missing.Count > 0)
                        HoConstraintEditorControls.Caption("⚠ 网格缺这些键，对应格子里的它们会被跳过："
                            + string.Join("、", missing));
                }

                if (rig.faceController is AnimatorController controller && !HasDriveLayer(controller))
                    HoConstraintEditorControls.Caption("该控制器没有 " + HoFaceAnimationAssets.DriveLayerName + " 段，先初始化");
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
                // 中间层有些处理是隐式的（眼睑被并成一棵 2D 树、重叠由姿势表权衡），
                // 这里把控制器里实际的形状说出来，用户就不必靠行为去猜。
                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("控制器结构", HoConstraintEditorTheme.LabelWidth,
                        "**从资产读出来的实际形状**（不是配置声明）。\n"
                        + "眼睑是若干棵 2D 树、每棵里有作者摆好的姿势 —— blink 与 squint 的重叠就是靠姿势权衡的，"
                        + "那件事本身不可关。\n"
                        + "改完结构要按「应用改动」才生效。");
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

                // 路径重映射不是第二个折叠 —— 折叠栏下只用 box 分块，层级只有一层。
                EditorGUILayout.LabelField("路径重映射", EditorStyles.boldLabel);
                HoConstraintEditorControls.Caption("模型层级和控制器里的路径不一致时用（例如控制器写 Body，模型里是 Meshes/Face）。");
                EditorGUILayout.PropertyField(serializedObject.FindProperty("pathRemaps"), GUIContent.none, true);
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

        /// <summary>初始化摘要：控制器在不在、生成段有几层。这一栏将来会长出"生成配置"（预设开关）。</summary>
        private static string InitSummary(HoFaceTrackingDebugger rig)
        {
            if (rig.faceController == null) return "未初始化";
            if (!(rig.faceController is AnimatorController controller)) return "不是 AnimatorController";
            if (!HasDriveLayer(controller)) return "缺 " + HoFaceAnimationAssets.DriveLayerName;
            int managed = 0;
            foreach (var layer in controller.layers)
                if (HoFaceAnimationAssets.IsManagedLayer(layer.name))
                    managed++;
            return "已初始化 · 生成段 " + managed + " 层";
        }

        /// <summary>
        /// 控制器里**实际的**驱动层形状（从资产读，不是从配置推断）。
        /// 中间层有些处理是隐式的 —— 眼睑被并成 2D 树、重叠由姿势表权衡 —— 所以这里把它报出来。
        /// </summary>
        private static string StructureSummary(HoFaceTrackingDebugger rig)
        {
            if (!(rig.faceController is AnimatorController controller)) return "未指定控制器";

            BlendTree drive = null;
            foreach (var layer in controller.layers)
            {
                if (layer.name != HoFaceAnimationAssets.DriveLayerName || layer.stateMachine == null) continue;
                foreach (var state in layer.stateMachine.states)
                    drive = state.state.motion as BlendTree;
            }

            if (drive == null) return "这个控制器里没有 " + HoFaceAnimationAssets.DriveLayerName + " 段";

            int lidTrees = 0, lidPoses = 0, regionTrees = 0, regionLeaves = 0;
            foreach (var child in drive.children)
            {
                if (!(child.motion is BlendTree tree)) continue;
                if (tree.blendType == BlendTreeType.FreeformCartesian2D)
                {
                    lidTrees++;
                    lidPoses += tree.children.Length;
                }
                else if (tree.blendType == BlendTreeType.Direct)
                {
                    regionTrees++;
                    regionLeaves += tree.children.Length;
                }
            }

            return "眼睑 " + lidTrees + " 棵 2D 树 / 共 " + lidPoses + " 格姿势 · 区域子树 " + regionTrees
                + " 棵 / " + regionLeaves + " 个直通叶子 · 门控 " + HoFaceAnimationAssets.EyeGateName
                + " + " + HoFaceAnimationAssets.LipGateName;
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

            // 轴生产是结构性的（控制器里有对应的 2D 树就有），所以只要控制器是这套生成物就报出来。
            if (rig.faceController != null) parts.Add("眼睑轴");
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
        /// 初始化控制器：从源控制器 + 当前配置产出**一个完整文件**。
        ///
        /// 这是唯一会写文件的动作，而且是破坏性的 —— 目标已存在时里面的手工改动会全丢。
        /// 所以路径每次都问，覆盖前必须把代价说清楚，并给一条「另存为新文件」的出路。
        /// </summary>
        /// <summary>组件上选的模板；没选就返回 null —— 生成器会用内置默认（ho-2d-test1）。</summary>
        private static HoFaceTemplateSpec TemplateOf(HoFaceTrackingDebugger rig) =>
            rig != null && rig.template != null ? rig.template.spec : null;

        /// <summary>目标 Animator 下所有网格上真实存在的形态键（跟模板"需要的键"做差集用）。</summary>
        private static void CollectShapeKeys(HoFaceTrackingDebugger rig, HashSet<string> into)
        {
            into.Clear();
            if (rig == null || rig.targetAnimator == null) return;
            foreach (var mesh in rig.targetAnimator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (mesh.sharedMesh == null || mesh.GetComponentInParent<Animator>() != rig.targetAnimator) continue;
                for (int i = 0; i < mesh.sharedMesh.blendShapeCount; i++)
                    into.Add(mesh.sharedMesh.GetBlendShapeName(i));
            }
        }

        /// <summary>
        /// 初始化 = 产出（或重写）控制器。**目标就是组件当前在用的那个资产**。
        ///
        /// 分两种情形，用户只看到**一个按钮**：
        /// <list type="bullet">
        /// <item>控制器是我们的（有驱动段）→ **就地重写整个文件**：GUID 不变，旧内容清干净。切模板走这条
        /// —— 所以**不需要用户自己备份**。</item>
        /// <item>第一次（组件上还没有控制器），或那个控制器不是我们生成的 → 问路径新建；目标已存在时再确认一次覆盖。</item>
        /// </list>
        /// </summary>
        private void Initialize(HoFaceTrackingDebugger rig)
        {
            var current = rig.faceController as AnimatorController;
            if (current != null && HasDriveLayer(current))
            {
                string currentPath = AssetDatabase.GetAssetPath(current);
                if (!EditorUtility.DisplayDialog("重新初始化吗？",
                    currentPath + "\n\n会就地重写这个控制器的**整个文件**（旧的树、片段、参数都会清干净）。\n"
                    + "资产 GUID 不变，所以引用它的地方（比如窥视对象）不会断。",
                    "重写", "取消"))
                    return;

                ApplyChanges(rig);
                return;
            }

            string path = current != null ? AssetDatabase.GetAssetPath(current) : "";
            if (string.IsNullOrEmpty(path))
            {
                path = EditorUtility.SaveFilePanelInProject("初始化面部控制器", "Face_ARKit", "controller",
                    "产出一个完整的控制器文件；以后「重新初始化」只重写里面的驱动段。");
                if (string.IsNullOrEmpty(path)) return;
            }

            if (AssetDatabase.LoadMainAssetAtPath(path) != null
                && !EditorUtility.DisplayDialog("要覆盖这个控制器吗？",
                    path + "\n\n该文件已存在，覆盖会把它**整个**重写（包括你自己加的层）。", "覆盖并初始化", "取消"))
                return;
            try
            {
                var controller = HoFaceAnimationAssets.Generate(rig.targetAnimator, path, true, TemplateOf(rig));
                Undo.RecordObject(rig, "Initialize face controller");
                rig.faceController = controller;
                EnsureChannels(rig);   // 只补齐缺失的通道，不覆盖用户已经调过的
                EditorUtility.SetDirty(rig);
                PrefabUtility.RecordPrefabInstancePropertyModifications(rig);
                reportIsError = false;
                report = "已初始化 " + path + "：" + controller.parameters.Length + " 路 ARKit 参数（"
                    + controller.layers.Length + " 层：驱动段）。";
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
        /// 重新初始化：就地重写整个控制器文件（GUID 不变）。「初始化控制器」只在开始时用一次。
        /// </summary>
        private void ApplyChanges(HoFaceTrackingDebugger rig)
        {
            if (!(rig.faceController is AnimatorController controller))
            {
                report = "请先「初始化控制器」，或选一个本工具产出的控制器。";
                return;
            }

            try
            {
                HoFaceAnimationAssets.Apply(controller, rig.targetAnimator, TemplateOf(rig));
                EnsureChannels(rig);
                EditorUtility.SetDirty(rig);
                report = "已重新初始化：整个文件重写完毕（GUID 未变）。";
            }
            catch (Exception e) { Fail(e); }
        }

        private static bool HasDriveLayer(AnimatorController controller)
        {
            foreach (var layer in controller.layers)
                if (layer.name == HoFaceAnimationAssets.DriveLayerName) return true;
            return false;
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
