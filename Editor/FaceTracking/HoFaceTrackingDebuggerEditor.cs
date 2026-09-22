using System;
using System.Linq;
using Hollow.HoUnityTools.Editor.Constraints;
using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    [CustomEditor(typeof(HoFaceTrackingDebugger))]
    public sealed class HoFaceTrackingDebuggerEditor : UnityEditor.Editor
    {
        private Vector2 scroll;
        private string search = "";
        private string report = "";
        private bool outputExpanded = true;
        private bool channelsExpanded = true;
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
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("targetAnimator"), new GUIContent("角色 Animator"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("faceController"), new GUIContent("面部控制器"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("startOnPlay"), new GUIContent("播放后自动驱动"));
            serializedObject.ApplyModifiedProperties();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("全局连接面板")) HoFaceTrackingWindow.ShowWindow();
                using (new EditorGUI.DisabledScope(Application.isPlaying || rig.targetAnimator == null))
                    if (GUILayout.Button("生成 ARKit 控制器")) Generate(rig);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("检查绑定")) Check(rig);
                if (GUILayout.Button("定位控制器资产") && rig.faceController != null) { Selection.activeObject = rig.faceController; EditorGUIUtility.PingObject(rig.faceController); }
            }
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
                using (new EditorGUI.DisabledScope(Application.isPlaying || rig.targetAnimator == null))
                    if (GUILayout.Button(new GUIContent("生成 ARKit 控制器", "扫描角色上真实存在的 ARKit 键，生成每个键一个覆盖图层的纯 Unity 控制器。"), GUILayout.Height(20))) Generate(rig);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("检查绑定", "只做解析，不改任何资产：列出能绑上的输出与找不到的键。"), GUILayout.Height(20))) Check(rig);
                if (GUILayout.Button("定位控制器资产", GUILayout.Height(20)) && rig.faceController != null) { Selection.activeObject = rig.faceController; EditorGUIUtility.PingObject(rig.faceController); }
            }
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
                if (GUILayout.Button(session == null ? "开始驱动" : "停止并交还动画", GUILayout.Height(24)))
                {
                    if (session == null) HoFaceInputHub.Start(rig); else HoFaceInputHub.Stop(rig);
                }

            if (!string.IsNullOrEmpty(HoFaceInputHub.Error(rig))) EditorGUILayout.HelpBox(HoFaceInputHub.Error(rig), MessageType.Error);
            if (!string.IsNullOrEmpty(report)) EditorGUILayout.HelpBox(report, MessageType.Info);
            if (session != null && session.Compiled != null && session.Compiled.warnings.Count > 0)
                EditorGUILayout.HelpBox(string.Join("\n", session.Compiled.warnings.Take(8)), MessageType.Warning);

            serializedObject.Update();
            if (!HoConstraintEditorControls.Section(
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
                using (HoConstraintEditorControls.Row(true))
                {
                    DrawRegion(regions, HoFaceRegion.Mouth, "嘴与舌头");
                    HoConstraintEditorControls.Gap();
                    DrawRegion(regions, HoFaceRegion.Brows, "眉毛");
                    HoConstraintEditorControls.Gap();
                    DrawRegion(regions, HoFaceRegion.Cheeks, "脸颊与鼻子");
                }

                using (HoConstraintEditorControls.Row(true))
                {
                    DrawRegion(regions, HoFaceRegion.Eyelids, "眼睑 / 眨眼");
                    HoConstraintEditorControls.Gap();
                    DrawRegion(regions, HoFaceRegion.Gaze, "凝视键（用 LookAt 时关）");
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

                mappings = HoConstraintEditorControls.InlineFoldout(mappings, "路径重映射", "模型层级和控制器里的路径不一致时用（例如控制器写 Body，模型里是 Meshes/Face）。");
                if (mappings) EditorGUILayout.PropertyField(serializedObject.FindProperty("pathRemaps"), GUIContent.none, true);
            }

            serializedObject.ApplyModifiedProperties();
            DrawChannels(rig, session);
        }

        private static string RegionSummary(HoFaceRegion regions)
        {
            int count = 0;
            if ((regions & HoFaceRegion.Mouth) != 0) count++;
            if ((regions & HoFaceRegion.Brows) != 0) count++;
            if ((regions & HoFaceRegion.Cheeks) != 0) count++;
            if ((regions & HoFaceRegion.Eyelids) != 0) count++;
            if ((regions & HoFaceRegion.Gaze) != 0) count++;
            return count + " / 5 组";
        }

        private void DrawChannels(HoFaceTrackingDebugger rig, HoFaceAnimationSession session)
        {
            serializedObject.Update();
            var channels = serializedObject.FindProperty("channels");
            if (!HoConstraintEditorControls.Section(
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

        /// <summary>输出分组开关。调用方负责开一行；这里只管画一个勾选框并回写。</summary>
        private static void DrawRegion(SerializedProperty property, HoFaceRegion region, string label)
        {
            bool enabled = (property.intValue & (int)region) != 0;
            bool next = HoConstraintEditorControls.Toggle(label, enabled, RegionTooltip(region));
            if (enabled != next) property.intValue = next ? property.intValue | (int)region : property.intValue & ~(int)region;
        }

        private static string RegionTooltip(HoFaceRegion region)
        {
            switch (region)
            {
                case HoFaceRegion.Mouth: return "jawOpen、mouthClose、mouthSmile 等。";
                case HoFaceRegion.Brows: return "browDown / browInnerUp / browOuterUp 等。";
                case HoFaceRegion.Cheeks: return "cheekPuff、cheekSquint、noseSneer 等。";
                case HoFaceRegion.Eyelids: return "eyeBlink / eyeSquint / eyeWide。断流后交还给自动眨眼。";
                default: return "eyeLook* 凝视形态键。用 HoLookAt 驱动眼球时关掉，否则两个方向会叠起来转两次。";
            }
        }

        private static void SetMode(SerializedProperty channels, HoFaceInputMode mode)
        {
            for (int i = 0; i < channels.arraySize; i++) channels.GetArrayElementAtIndex(i).FindPropertyRelative("mode").enumValueIndex = (int)mode;
        }

        private void Generate(HoFaceTrackingDebugger rig)
        {
            string path = EditorUtility.SaveFilePanelInProject("保存新的 ARKit 调试控制器", "Face_ARKit", "controller", "源模型与既有控制器不会被修改。");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var controller = HoFaceAnimationAssets.Generate(rig.targetAnimator, path);
                Undo.RecordObject(rig, "Assign ARKit face controller");
                rig.faceController = controller;
                rig.channels = HoFaceTrackingChannels.CreateDefaults();
                EditorUtility.SetDirty(rig);
                PrefabUtility.RecordPrefabInstancePropertyModifications(rig);
                report = "已生成 " + controller.parameters.Length + " 路 ARKit 混合树。凝视输出仍由上面的分组开关控制。";
            }
            catch (Exception e) { report = e.Message; }
        }

        private void Check(HoFaceTrackingDebugger rig)
        {
            try
            {
                using (var compiled = HoFaceAnimationAssets.Compile(rig))
                    report = "可绑定输出：" + compiled.bindings.Count + "\n" + string.Join("\n", compiled.warnings.Take(12));
            }
            catch (Exception e) { report = e.Message; }
        }
    }
}
