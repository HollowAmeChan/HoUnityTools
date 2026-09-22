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
        private bool outputExpanded;
        private bool channelsExpanded;
        private bool mappings;
        private double nextRepaint;
        private static readonly string[] Modes = { "实时", "手动", "保持", "中性", "交还" };
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

            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("targetAnimator"), new GUIContent("角色 Animator", "角色根上的 Animator。面捕不会接管它，只借用它的绑定根解析路径。"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("faceController"), new GUIContent("面部控制器", "只含形态键曲线的纯 Unity AnimatorController；在影子层级上求值，不驱动角色本体。"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("startOnPlay"), new GUIContent("播放后自动驱动", "进入播放模式就自动开始驱动，不会自动连接手机。"));
            serializedObject.ApplyModifiedProperties();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("全局连接面板", GUILayout.Height(20))) HoFaceTrackingWindow.ShowWindow();
                bool initialized = rig.faceController != null;
                using (new EditorGUI.DisabledScope(Application.isPlaying || rig.targetAnimator == null))
                {
                    // 命名承载语义：「初始化」明确表达"产出完整文件、之后都在里面改"，且只在开始时点。
                    // 已有控制器时它变成次要按钮 + 省略号，危险性由确认框承担。
                    var content = initialized
                        ? new GUIContent("重新初始化…", "会重新产出一个**完整的**控制器文件，该文件里你的手工改动都会丢失。\n"
                            + "结构改动目前还没做「应用改动」，所以暂时只能走这里；分层生成落地后这一步就不再需要了。")
                        : new GUIContent("初始化控制器", "从源控制器 + 当前配置产出一个完整的控制器文件。\n"
                            + "这是唯一会写文件的动作：之后手工逻辑请在生成物的 (EDIT THIS) 段里加，"
                            + "结构改动走「应用改动」，两者都不会碰你的手工段。");
                    if (GUILayout.Button(content, GUILayout.Height(20))) Initialize(rig);
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("检查绑定", "只做解析，不改任何资产：列出能绑上的输出与找不到的键。"), GUILayout.Height(20))) Check(rig);
                if (GUILayout.Button("定位控制器资产", GUILayout.Height(20)) && rig.faceController != null) { Selection.activeObject = rig.faceController; EditorGUIUtility.PingObject(rig.faceController); }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                // 应用改动 = 就地手术：只重写 Ho/ 生成段，(EDIT THIS) 段和别的层一个字节都不动。
                using (new EditorGUI.DisabledScope(
                    Application.isPlaying || rig.targetAnimator == null || !(rig.faceController is AnimatorController)))
                {
                    if (GUILayout.Button(new GUIContent("应用改动",
                        "把当前配置写进这个控制器：只重写 " + HoFaceAnimationAssets.DriveLayerName + " 那一段，\n"
                        + HoFaceAnimationAssets.EditLayerName + " 段和其它任何层都不会被动。\n"
                        + "这是反复用的那个按钮；「初始化控制器」只在开始时用一次。"), GUILayout.Height(20)))
                        ApplyChanges(rig);
                }
                HoConstraintEditorControls.Flex();
                if (rig.faceController is AnimatorController ac && !HasDriveLayer(ac))
                    HoConstraintEditorControls.Caption("该控制器没有 " + HoFaceAnimationAssets.DriveLayerName + " 段，先初始化");
            }
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
                if (GUILayout.Button(session == null ? "开始驱动" : "停止并交还动画", GUILayout.Height(24)))
                {
                    if (session == null) HoFaceInputHub.Start(rig); else HoFaceInputHub.Stop(rig);
                }

            if (!string.IsNullOrEmpty(HoFaceInputHub.Error(rig))) EditorGUILayout.HelpBox(HoFaceInputHub.Error(rig), MessageType.Error);
            if (!string.IsNullOrEmpty(report)) EditorGUILayout.HelpBox(report, reportIsError ? MessageType.Warning : MessageType.Info);
            if (session != null && session.Compiled != null && session.Compiled.warnings.Count > 0)
                EditorGUILayout.HelpBox(string.Join("\n", session.Compiled.warnings.Take(8)), MessageType.Warning);

            serializedObject.Update();
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(
                ref outputExpanded,
                "输出分工",
                RegionSummary(rig.outputRegions),
                HoConstraintEditorTheme.AccentOutput))
            {
                serializedObject.ApplyModifiedProperties();
                DrawChannels(rig, session);
                return;
            }

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

                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("断流等待", HoConstraintEditorTheme.LabelWidth, "多久没有有效帧就算断流。");
                    serializedObject.FindProperty("staleSeconds").floatValue =
                        HoConstraintEditorControls.NumberField(serializedObject.FindProperty("staleSeconds").floatValue, "秒");
                    HoConstraintEditorControls.Gap();
                    HoConstraintEditorControls.Label("回中性", HoConstraintEditorTheme.LabelWidthSm, "断流后淡回中性值用多久。");
                    serializedObject.FindProperty("neutralFadeSeconds").floatValue =
                        HoConstraintEditorControls.NumberField(serializedObject.FindProperty("neutralFadeSeconds").floatValue, "秒");
                }

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

                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("双眼同步", HoConstraintEditorTheme.LabelWidth,
                        "把左右眼合成一个值再写回去。\n"
                        + "有些模型的左右眨眼键**各自都能闭双眼**，左右一起触发就过眨眼 —— 这时把它调到 1。\n"
                        + "0 = 左右独立（允许 wink）；作用范围照参考实现：眼睑 + 眼球横向，眼球纵向不进去。");
                    serializedObject.FindProperty("eyeSync").floatValue = HoConstraintEditorControls.NumberField(
                        serializedObject.FindProperty("eyeSync").floatValue, null,
                        "0 = 左右独立；1 = 强制两侧同值。", HoConstraintEditorTheme.FieldWidthWide);
                    HoConstraintEditorControls.Gap(4.0f);
                    HoConstraintEditorControls.Label("配比", HoConstraintEditorTheme.LabelWidthSm,
                        "同步到哪个值：0 = 全用左眼，0.5 = 平均，1 = 全用右眼。");
                    serializedObject.FindProperty("eyeSyncMix").floatValue = HoConstraintEditorControls.NumberField(
                        serializedObject.FindProperty("eyeSyncMix").floatValue, null, null, HoConstraintEditorTheme.FieldWidthWide);
                    HoConstraintEditorControls.Flex();
                }

                mappings = HoConstraintEditorControls.InlineFoldout(mappings, "路径重映射", "模型层级和控制器里的路径不一致时用（例如控制器写 Body，模型里是 Meshes/Face）。");
                if (mappings) EditorGUILayout.PropertyField(serializedObject.FindProperty("pathRemaps"), GUIContent.none, true);
            }

            serializedObject.ApplyModifiedProperties();
            DrawChannels(rig, session);
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
        private void Initialize(HoFaceTrackingDebugger rig)
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "初始化面部控制器", "Face_ARKit", "controller",
                "这会产出一个完整的控制器文件。之后你的手工逻辑请在它内部的 (EDIT THIS) 段里加。");
            if (string.IsNullOrEmpty(path)) return;

            var existing = AssetDatabase.LoadMainAssetAtPath(path) as AnimatorController;
            if (existing != null)
            {
                // 覆盖前把代价算出来，而不是笼统说「将被覆盖」。
                string cost = DescribeController(existing);
                int choice = EditorUtility.DisplayDialogComplex(
                    "要覆盖这个控制器吗？",
                    path + "\n\n该文件已存在：" + cost + "。\n"
                    + "覆盖会把它整个重写 —— 你在里面手工加的层、状态、树都会丢失。\n\n"
                    + "想保住现有文件就选「另存为新文件」。",
                    "覆盖并初始化", "取消", "另存为新文件…");
                if (choice == 1) return;
                if (choice == 2)
                {
                    path = EditorUtility.SaveFilePanelInProject(
                        "另存为新的面部控制器", Path.GetFileNameWithoutExtension(path) + "_new", "controller",
                        "原文件不会被改动。");
                    if (string.IsNullOrEmpty(path)) return;
                    if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                    {
                        reportIsError = true;
                        report = "目标已存在，已取消：" + path;
                        return;
                    }
                }
            }

            try
            {
                // 在**最终路径**上判断要不要覆盖：用户可能刚刚改选了「另存为新文件」。
                bool overwrite = AssetDatabase.LoadMainAssetAtPath(path) != null;
                var controller = HoFaceAnimationAssets.Generate(rig.targetAnimator, path, overwrite);
                Undo.RecordObject(rig, "Initialize face controller");
                rig.faceController = controller;
                EnsureChannels(rig);   // 只补齐缺失的通道，不覆盖用户已经调过的
                EditorUtility.SetDirty(rig);
                PrefabUtility.RecordPrefabInstancePropertyModifications(rig);
                reportIsError = false;
                report = "已初始化 " + path + "：" + controller.parameters.Length + " 路 ARKit 参数（"
                    + controller.layers.Length + " 层：驱动段 + 扩展点）。之后就改这个文件里的 (EDIT THIS) 段。";
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
        /// 应用改动：就地手术。只重写驱动段，`(EDIT THIS)` 段与其它层一个字节都不动。
        /// 这是反复用的那个按钮；「初始化控制器」只在开始时用一次。
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
                HoFaceAnimationAssets.Apply(controller, rig.targetAnimator);
                EnsureChannels(rig);
                EditorUtility.SetDirty(rig);
                report = "已应用改动：重写了 " + HoFaceAnimationAssets.DriveLayerName + " 段；"
                    + HoFaceAnimationAssets.EditLayerName + " 段未改动。";
            }
            catch (Exception e) { Fail(e); }
        }

        private static bool HasDriveLayer(AnimatorController controller)
        {
            foreach (var layer in controller.layers)
                if (layer.name == HoFaceAnimationAssets.DriveLayerName) return true;
            return false;
        }

        /// <summary>把一个控制器资产的规模说出来，供覆盖确认框显示代价。</summary>
        private static string DescribeController(AnimatorController controller)
        {
            int states = 0;
            foreach (var layer in controller.layers)
            {
                states += CountStates(layer.stateMachine);
            }

            return controller.layers.Length + " 个图层、" + states + " 个状态、"
                + controller.animationClips.Length + " 个片段";
        }

        private static int CountStates(AnimatorStateMachine machine)
        {
            if (machine == null) return 0;
            int count = machine.states.Length;
            foreach (var child in machine.stateMachines) count += CountStates(child.stateMachine);
            return count;
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
