using Hollow.HoUnityTools;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor
{
    [CustomEditor(typeof(SceneToGameViewSync))]
    internal sealed class SceneToGameViewSyncEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("此组件将Scene视图相机的变化同步到Game视图相机。", MessageType.Info);
            EditorGUILayout.Space();

            DrawProperty("enableSync", "启用同步", "启用或禁用Scene到Game的同步");

            if (serializedObject.FindProperty("enableSync").boolValue)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("同步选项", EditorStyles.boldLabel);

                DrawProperty("syncPosition", "同步位置", "同步相机位置");
                DrawProperty("syncRotation", "同步旋转", "同步相机旋转");
                DrawProperty("syncFOV", "同步FOV", "同步相机视野");
                DrawProperty("syncClippingPlanes", "同步裁切", "同步相机近远裁切平面");
                DrawProperty("syncInPlayMode", "播放模式下同步", "在播放模式下也进行同步");

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("性能选项", EditorStyles.boldLabel);
                EditorGUILayout.Slider(serializedObject.FindProperty("syncFrequency"), 1f, 60f, new GUIContent("同步频率", "每秒同步次数"));

                EditorGUILayout.Space();
                if (GUILayout.Button("立即同步"))
                {
                    foreach (Object selectedTarget in targets)
                    {
                        if (selectedTarget is SceneToGameViewSync sync)
                            SceneToGameViewSyncDriver.SyncNow(sync);
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawProperty(string propertyName, string label, string tooltip)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty(propertyName), new GUIContent(label, tooltip));
        }
    }
}
