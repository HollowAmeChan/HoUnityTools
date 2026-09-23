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
        private Vector2 clipScroll;
        private string search = "";
        private string report = "";
        private bool reportIsError;
        private bool setupExpanded = true;
        private bool initExpanded = true;
        private bool outputExpanded = true;
        private bool middleExpanded = true;
        private bool channelsExpanded;
        private bool clipsExpanded;
        /// <summary>「动画填充」折叠框的缓存：模板 + 文件夹没变、且刚算过，就不重复扫树。</summary>
        private RuntimeAnimatorController slotsFor;
        private string slotsFolder;
        private List<HoFaceClipSlot> slots;
        private double slotsAt;
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

            // ── 初始化（装配）：模板 + 动画文件夹 + 驱动对象 → 这台角色的控制器 ─────────
            // 三件事分开：树形是**模板作者**的、姿势是**动画作者**的、写谁是**这台角色**的。
            // 所以这一栏没有"生成配置"，只有这三样输入 + 一个折叠框如实显示每个槽位被填成了什么。
            // 参数不由控制器声明 —— 它只等着被喂，喂什么由中间层决定（见 docs/FACE_TRACKING_WORKFLOW.md）。
            if (HoConstraintEditorSectionGui.DrawSectionHeader(ref initExpanded, "初始化", InitSummary(rig),
                HoConstraintEditorTheme.AccentBlink))
            using (HoConstraintEditorControls.Card())
            {
                serializedObject.Update();
                EditorGUILayout.PropertyField(serializedObject.FindProperty("treeTemplate"),
                    new GUIContent("混合树模板", "一份完整的 .controller：树形、坐标、门控、参数都在里面。\n"
                        + "装配时整份复制成面部控制器，只把动画驱动的对象换成下面的驱动对象。\n"
                        + "树里引用的每个片段就是一个**槽位**，按名字去动画文件夹里找同名 .anim。"));
                serializedObject.ApplyModifiedProperties();

                if (rig.treeTemplate == null)
                    HoConstraintEditorControls.Caption("先选一份混合树模板。");
                else if (!(rig.treeTemplate is AnimatorController))
                    HoConstraintEditorControls.Caption("模板必须是纯 Unity AnimatorController（不接受 OverrideController）。");

                // 动画文件夹：存成路径字符串（组件是 Runtime 程序集，拿不了 UnityEditor 的 DefaultAsset）。
                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("动画文件夹", HoConstraintEditorTheme.LabelWidth,
                        "装现成片段的地方（比如每个形态键一份 `<键名>.anim`）。\n"
                        + "装配时按**槽位名**找同名 .anim 填进去；文件夹里没有的槽位保留模板自带的那份。\n"
                        + "片段是**按形态键名**重绑到驱动对象上的，所以这份文件夹跟模型无关，可以复用。");
                    var folder = string.IsNullOrEmpty(rig.animationFolder)
                        ? null
                        : AssetDatabase.LoadAssetAtPath<DefaultAsset>(rig.animationFolder);
                    var picked = (DefaultAsset)EditorGUILayout.ObjectField(folder, typeof(DefaultAsset), false);
                    if (picked != folder)
                    {
                        serializedObject.FindProperty("animationFolder").stringValue =
                            picked != null ? AssetDatabase.GetAssetPath(picked) : "";
                        serializedObject.ApplyModifiedProperties();
                        slotsFor = null;   // 折叠框缓存失效
                    }

                    HoConstraintEditorControls.Flex();
                }

                if (!string.IsNullOrEmpty(rig.animationFolder) && !AssetDatabase.IsValidFolder(rig.animationFolder))
                    HoConstraintEditorControls.Caption("这个动画文件夹不在工程里了，重新指一个。");

                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Label("驱动对象", HoConstraintEditorTheme.LabelWidth,
                        "面捕要驱动哪些网格 —— 装配把控制器里的形态键动画重绑到这些网格上。\n"
                        + "某个键在这些网格里谁都没有时，那条曲线原样留着不动（作者的格子数据不丢），"
                        + "但那些格子落不到任何网格上（结构摘要会列出来）。");
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

                DrawClipSlots(rig);

                using (new EditorGUILayout.HorizontalScope())
                {
                    bool initialized = rig.faceController != null;
                    using (new EditorGUI.DisabledScope(Application.isPlaying || rig.targetAnimator == null
                        || rig.treeTemplate == null || rig.meshes == null || rig.meshes.Count == 0))
                    {
                        // 命名承载语义：装配 = 产出一份属于这台角色的控制器，之后都在里面。
                        // 一个按钮两种情形（见 Initialize 的注释）：已经有控制器 → 就地重写它；
                        // 第一次 → 问路径新建。模板就是它自己时就地填与重绑。
                        var content = initialized
                            ? new GUIContent("重新装配…", "拿模板 + 动画文件夹 + 驱动对象重来一遍：\n"
                                + "GUID 不变，所以引用它的地方不会断；旧的层/片段/参数由这次装配决定。")
                            : new GUIContent("装配控制器", "把模板整份搬成一份属于这台角色的控制器，并填入动画。\n"
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

            // ── 参数生产：中间层配置，一行一个输出 ────────────────────────────────
            // **这一栏回答"值怎么被加工"**：输入侧（模式 / 输入曲线 / 中性）+ 双眼同步在这里，
            // 真正的"参数生产"（一列 `参数 = 曲线(表达式) + 有序修饰符`）在我们自己的配置文件里，
            // 由「面捕配置」窗口编辑 —— 那一层跟控制器模板成对，跟这台角色无关。
            if (HoConstraintEditorSectionGui.DrawSectionHeader(ref middleExpanded, "参数生产", MiddleSummary(rig),
                HoConstraintEditorTheme.AccentDriver))
            using (HoConstraintEditorControls.Card())
            {
                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("中间层配置", HoConstraintEditorTheme.LabelWidth,
                        "一列输出行：参数名 = 曲线(表达式) + 有序修饰符。\n"
                        + "它跟**控制器模板成对**（vrc-common 控制器配 vrc-common 中间层），所以在这里手选。\n"
                        + "留空 = 内置默认（52 个 ARKit 直通 + 眼睑两根轴）。");
                    serializedObject.FindProperty("profile").objectReferenceValue =
                        EditorGUILayout.ObjectField(serializedObject.FindProperty("profile").objectReferenceValue,
                            typeof(TextAsset), false);
                    HoConstraintEditorControls.Gap(6.0f);
                    if (HoConstraintEditorControls.Button("面捕配置",
                        "打开配置窗口：预览每一行的表达式与曲线、直接改、保存回这个文件。", false, 76.0f))
                        HoFaceProfileWindow.Open(rig.profile);
                    HoConstraintEditorControls.Flex();
                }

                // 只报"实事"：几行、哪些行的表达式用不了、哪些参数名重复、用到的键模型上有没有。
                var loaded = rig.Middleware;
                if (rig.profile == null)
                {
                    HoConstraintEditorControls.Caption("用内置默认：52 个 ARKit 直通 + 眼睑两根轴。");
                }
                else if (loaded == null)
                {
                    HoConstraintEditorControls.Caption("⚠ 配置读不进来：" + rig.ProfileError);
                }
                else
                {
                    ProfileSummary(rig, loaded);
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

        /// <summary>
        /// 「动画填充」折叠框：模板里每个片段都是一个**槽位**，按槽位名去动画文件夹找同名 `.anim`。
        /// 这里显示的是"这份模板 + 这份文件夹"现在会装出什么 —— **不用先装配就能看**，
        /// 因为它读的是模板与文件夹本身，不是已落盘的控制器。
        /// </summary>
        private void DrawClipSlots(HoFaceTrackingDebugger rig)
        {
            clipsExpanded = HoConstraintEditorControls.InlineFoldout(clipsExpanded, "动画填充",
                "模板里每个片段都是一个槽位：按槽位名去动画文件夹里找同名 .anim，找到就用它的，找不到就保留模板自带的那份。\n"
                + "这里不需要先装配就能看：它读的是模板与文件夹本身。");
            var list = SlotsFor(rig);
            if (rig.treeTemplate == null)
            {
                HoConstraintEditorControls.Caption("选了模板之后，这里会列出它的每个槽位被填成了什么。");
                return;
            }

            if (list == null || list.Count == 0)
            {
                HoConstraintEditorControls.Caption("这份模板里没有片段槽位。");
                return;
            }

            int fromFolder = 0, own = 0, missing = 0;
            foreach (var slot in list)
            {
                if (slot.fromFolder != null) fromFolder++;
                else if (slot.Writes) own++;
                else missing++;
            }

            HoConstraintEditorControls.Caption("槽位 " + list.Count + " 个：文件夹 " + fromFolder
                + " · 模板自带 " + own + " · 缺 " + missing);
            if (!clipsExpanded) return;

            // 有问题的排前面：缺的（什么都没写）→ 用模板自带的（可能是占位）→ 文件夹的。
            var ordered = new List<HoFaceClipSlot>(list);
            ordered.Sort((a, b) => Rank(a).CompareTo(Rank(b)));
            clipScroll = EditorGUILayout.BeginScrollView(clipScroll, GUILayout.Height(130.0f));
            foreach (var slot in ordered)
            {
                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.ValueText(slot.Status, 62.0f);
                    HoConstraintEditorControls.Gap(4.0f);
                    HoConstraintEditorControls.Caption(slot.name
                        + (slot.uses > 1 ? "　×" + slot.uses : "")
                        + (slot.fromFolder == null && slot.Writes ? "　（模板自带）" : "")
                        + (slot.Status == "缺" ? "　（这个槽位不会写任何键）" : ""));
                    HoConstraintEditorControls.Flex();
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private static int Rank(HoFaceClipSlot slot) => slot.fromFolder != null ? 2 : slot.Writes ? 1 : 0;

        /// <summary>槽位列表带缓存（一秒一次），免得每次重画都重扫一遍几百棵树。</summary>
        private List<HoFaceClipSlot> SlotsFor(HoFaceTrackingDebugger rig)
        {
            bool fresh = slotsFor == rig.treeTemplate && slotsFolder == rig.animationFolder
                && EditorApplication.timeSinceStartup - slotsAt < 1.0;
            if (fresh) return slots;
            slotsFor = rig.treeTemplate;
            slotsFolder = rig.animationFolder;
            slotsAt = EditorApplication.timeSinceStartup;
            slots = HoFaceAnimationAssets.Slots(rig.treeTemplate, rig.animationFolder);
            return slots;
        }

        /// <summary>初始化摘要：装配了没有、装过来的是几层几个片段。</summary>
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
        /// 中间层摘要：**配置名 + 几行**，外加"这一层开着哪几件事"。
        /// 中间层的本体在配置文件里（不在这栏的字段上），所以摘要不再列数值。
        /// </summary>
        private static string MiddleSummary(HoFaceTrackingDebugger rig)
        {
            var loaded = rig.Middleware;
            string name = loaded == null || string.IsNullOrEmpty(loaded.displayName) ? "内置默认" : loaded.displayName;
            var parts = new List<string> { name + " " + rig.Outputs().Count + " 行" };
            if (rig.eyeSync) parts.Add(rig.eyeSyncSingleKey ? "双眼同步·单键" : "双眼同步");
            if (rig.profile != null && loaded == null) parts.Add("⚠ 配置读不进来");
            return string.Join(" · ", parts);
        }

        /// <summary>
        /// 配置摘要：**只报实事，不写散文**。行数、哪几行的表达式用不了、重复参数名、
        /// 变量里不是标准 ARKit 键的（会被当 0）、控制器里没有那个参数名的、以及还没实现的修饰符。
        /// </summary>
        private static void ProfileSummary(HoFaceTrackingDebugger rig, HoFaceMiddleware loaded)
        {
            int bad = 0, duplicate = 0, unknown = 0, delay = 0, noParameter = 0;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var declared = new HashSet<string>(StringComparer.Ordinal);
            if (rig.faceController is AnimatorController controller)
                foreach (var parameter in controller.parameters) declared.Add(parameter.name);

            foreach (var row in loaded.outputs)
            {
                if (row == null) continue;
                if (!seen.Add(row.parameter)) duplicate++;
                if (declared.Count > 0 && !declared.Contains(row.parameter)) noParameter++;
                if (row.modifiers != null)
                    foreach (var modifier in row.modifiers)
                        if (modifier != null && modifier.kind == HoFaceModifierKind.Delay && modifier.Active) delay++;

                if (HoFaceExpression.TryParse(row.expression, out var parsed, out _))
                {
                    var names = new List<string>();
                    parsed.CollectVariables(names);
                    foreach (string shape in names)
                    {
                        if (HoFaceTrackingChannels.IndexOf(shape) < 0) unknown++;
                        else keys.Add(shape);
                    }
                }
                else
                {
                    bad++;
                }
            }

            var parts = new List<string> { loaded.outputs.Count + " 行", keys.Count + " 个源键" };
            if (bad > 0) parts.Add("⚠ " + bad + " 行表达式有错");
            if (duplicate > 0) parts.Add("⚠ " + duplicate + " 个重复参数名");
            if (unknown > 0) parts.Add("⚠ " + unknown + " 个变量不是标准 ARKit 键（按 0 算）");
            if (noParameter > 0) parts.Add("⚠ " + noParameter + " 个参数名控制器里没有（会被跳过）");
            if (delay > 0) parts.Add("⚠ " + delay + " 个「延迟」修饰符还没实现");
            HoConstraintEditorControls.Caption(string.Join(" · ", parts));
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
                    HoConstraintEditorControls.Caption("原值 " + raw + (session != null ? " → " + session.Input[index].ToString("F3") : ""));
                    HoConstraintEditorControls.Flex();
                }

                // 输入曲线：横轴 = 原始输入（0..1），纵轴 = 整形后的输入。
                // 这就是"这张脸打不满 1"的解法（VBridger 的 Input Curve）：点一下弹曲线编辑器。
                var curve = channel.FindPropertyRelative("inputCurve");
                EditorGUILayout.PropertyField(curve, GUIContent.none, GUILayout.Width(44.0f));

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
        /// 装配 = 模板（树形）+ 动画文件夹（数据）+ 驱动对象（写谁）→ 这台角色的控制器。
        /// **目标就是组件当前在用的那个资产**，所以用户只看到**一个按钮**：
        /// <list type="bullet">
        /// <item>已经有控制器 → 就地重写它（GUID 不变，引用它的地方不会断）；</item>
        /// <item>还没有 → 问路径新建；目标已存在时再确认一次覆盖。</item>
        /// <item>模板就是它自己 → 就地填动画与重绑（没有可复制的东西）。</item>
        /// </list>
        /// </summary>
        private void Initialize(HoFaceTrackingDebugger rig)
        {
            var template = rig.treeTemplate as AnimatorController;
            if (template == null) { Fail(new InvalidOperationException("先选一份混合树模板。")); return; }
            if (rig.meshes == null || rig.meshes.Count == 0) { Fail(new InvalidOperationException("驱动对象列表是空的 —— 先「按 Animator 填充」。")); return; }
            if (rig.targetAnimator == null) { Fail(new InvalidOperationException("先指定角色 Animator。")); return; }
            if (!string.IsNullOrEmpty(rig.animationFolder) && !AssetDatabase.IsValidFolder(rig.animationFolder))
            { Fail(new InvalidOperationException("动画文件夹不在工程里了：" + rig.animationFolder)); return; }

            string templatePath = AssetDatabase.GetAssetPath(template);
            var current = rig.faceController as AnimatorController;
            string path = current != null ? AssetDatabase.GetAssetPath(current) : "";
            if (string.IsNullOrEmpty(path))
            {
                path = EditorUtility.SaveFilePanelInProject("装配面部控制器", "Face_Controller", "controller",
                    "产出一份属于这台角色的面部控制器；以后「重新装配」只动这一个文件。");
                if (string.IsNullOrEmpty(path)) return;
                if (AssetDatabase.LoadMainAssetAtPath(path) != null
                    && !EditorUtility.DisplayDialog("要覆盖这个控制器吗？",
                        path + "\n\n该文件已存在，会被整个重写。", "覆盖并装配", "取消"))
                    return;
            }
            else if (string.Equals(path, templatePath, StringComparison.Ordinal))
            {
                // 模板就是目标：没有可复制的，只填动画 + 重绑驱动对象。
                if (!EditorUtility.DisplayDialog("就地装配吗？",
                    path + "\n\n这份文件既是模板也是目标，会就地填入动画并重绑驱动对象。", "装配", "取消"))
                    return;
            }
            else if (!EditorUtility.DisplayDialog("重新装配吗？",
                path + "\n\n会把模板（" + template.name + "）**整份**搬过来覆盖它，并填入动画文件夹里的片段。\n"
                + "资产 GUID 不变，所以引用它的地方（比如窥视对象）不会断。", "覆盖", "取消"))
                return;

            try
            {
                var controller = HoFaceAnimationAssets.Adopt(template, path, rig.meshes, rig.targetAnimator,
                    rig.animationFolder, true);
                Undo.RecordObject(rig, "Assemble face controller");
                rig.faceController = controller;
                EnsureChannels(rig);   // 只补齐缺失的通道，不覆盖用户已经调过的
                EditorUtility.SetDirty(rig);
                PrefabUtility.RecordPrefabInstancePropertyModifications(rig);
                reportIsError = false;
                var info = HoFaceAnimationAssets.Inspect(controller, rig.meshes);
                int own = 0, missing = 0;
                foreach (var slot in HoFaceAnimationAssets.Slots(template, rig.animationFolder))
                {
                    if (slot.fromFolder != null) continue;
                    if (slot.Writes) own++; else missing++;
                }

                report = "已装配 " + path + "：" + info.layers + " 层 / " + info.clips + " 个片段，驱动 "
                    + info.shapes.Count + " 个形态键"
                    + (info.missing.Count > 0 ? "（" + info.missing.Count + " 个键驱动对象上没有）" : "")
                    + "；槽位用模板自带的 " + own + " 个、缺 " + missing + " 个。";
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
