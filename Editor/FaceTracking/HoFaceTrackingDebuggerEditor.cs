using System;
using System.Linq;
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
        private bool mappings, output;
        private double nextRepaint;
        private static readonly string[] Modes = { "实时", "手动", "保持", "中性", "交还" };

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
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
                if (GUILayout.Button(session == null ? "开始驱动" : "停止并交还动画", GUILayout.Height(28)))
                {
                    if (session == null) HoFaceInputHub.Start(rig); else HoFaceInputHub.Stop(rig);
                }
            if (!Application.isPlaying) EditorGUILayout.HelpBox("先连接手机查看参数；进入 Play Mode 后启动动画。也可不连接手机，使用手动参数调试。", MessageType.Info);
            if (!string.IsNullOrEmpty(HoFaceInputHub.Error(rig))) EditorGUILayout.HelpBox(HoFaceInputHub.Error(rig), MessageType.Error);
            if (!string.IsNullOrEmpty(report)) EditorGUILayout.HelpBox(report, MessageType.Info);
            if (session != null)
            {
                EditorGUILayout.LabelField(session.StateSummary, EditorStyles.miniLabel);
                if (session.Compiled != null && session.Compiled.warnings.Count > 0) EditorGUILayout.HelpBox(string.Join("\n", session.Compiled.warnings.Take(8)), MessageType.Warning);
            }

            serializedObject.Update();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("输出分工", EditorStyles.boldLabel);
            var regions = serializedObject.FindProperty("outputRegions");
            DrawRegion(regions, HoFaceRegion.Mouth, "嘴与舌头");
            DrawRegion(regions, HoFaceRegion.Brows, "眉毛");
            DrawRegion(regions, HoFaceRegion.Cheeks, "脸颊与鼻子");
            DrawRegion(regions, HoFaceRegion.Eyelids, "眼睑 / 眨眼");
            DrawRegion(regions, HoFaceRegion.Gaze, "凝视形态键（使用 LookAt 时关闭）");
            EditorGUILayout.PropertyField(serializedObject.FindProperty("staleSeconds"), new GUIContent("断流等待（秒）"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("neutralFadeSeconds"), new GUIContent("回中性时长（秒）"));
            mappings = EditorGUILayout.Foldout(mappings, "路径重映射 / Body → Face", true);
            if (mappings) EditorGUILayout.PropertyField(serializedObject.FindProperty("pathRemaps"), GUIContent.none, true);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("输入参数（归一化 0..1）", EditorStyles.boldLabel);
            search = EditorGUILayout.TextField("筛选", search);
            var channels = serializedObject.FindProperty("channels");
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("全部实时")) SetMode(channels, HoFaceInputMode.Live);
                if (GUILayout.Button("全部手动")) SetMode(channels, HoFaceInputMode.Manual);
                if (GUILayout.Button("全部中性")) SetMode(channels, HoFaceInputMode.Neutral);
            }
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(330));
            for (int i = 0; i < channels.arraySize; i++)
            {
                var channel = channels.GetArrayElementAtIndex(i);
                string shape = channel.FindPropertyRelative("shape").stringValue;
                if (!string.IsNullOrEmpty(search) && shape.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                int index = HoFaceTrackingChannels.IndexOf(shape);
                if (index < 0) { EditorGUILayout.LabelField("未知 ARKit 键：" + shape); continue; }
                EditorGUILayout.LabelField(shape, EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    var mode = channel.FindPropertyRelative("mode");
                    mode.enumValueIndex = EditorGUILayout.Popup(mode.enumValueIndex, Modes, GUILayout.Width(65));
                    if (mode.enumValueIndex == (int)HoFaceInputMode.Manual)
                    {
                        var manual = channel.FindPropertyRelative("manual");
                        manual.floatValue = EditorGUILayout.Slider(manual.floatValue, 0f, 1f);
                    }
                    else
                    {
                        string raw = HoFaceInputHub.ReceivedAt[index] > 0 ? HoFaceInputHub.Raw[index].ToString("F3") : "未收到";
                        EditorGUILayout.LabelField("原值 " + raw + (session != null ? " → 输入 " + session.Effective[index].ToString("F3") : ""));
                    }
                }
                if (session != null) EditorGUILayout.LabelField("Controller " + session.ControllerValues[index].ToString("F3"), EditorStyles.miniLabel);
            }
            EditorGUILayout.EndScrollView();
            output = EditorGUILayout.Foldout(output, "输出实值 / 高级参数映射", true);
            if (output)
            {
                if (session?.Compiled != null)
                    foreach (var binding in session.Compiled.bindings)
                        if (binding.renderer != null) EditorGUILayout.LabelField(binding.path + "/" + binding.shape, binding.renderer.GetBlendShapeWeight(binding.index).ToString("F2"));
                EditorGUILayout.PropertyField(channels, new GUIContent("参数名 / 增益 / 中性值"), true);
            }
            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawRegion(SerializedProperty property, HoFaceRegion region, string label)
        {
            bool enabled = (property.intValue & (int)region) != 0;
            bool next = EditorGUILayout.ToggleLeft(label, enabled);
            if (enabled != next) property.intValue = next ? property.intValue | (int)region : property.intValue & ~(int)region;
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
