using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    [CustomEditor(typeof(HoVtsIrController))]
    public sealed class HoVtsIrControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("parameterReceiver"), new GUIContent("参数接收器"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("irAsset"), new GUIContent("IR 文件"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("targetMeshes"), new GUIContent("驱动网格"), true);
            EditorGUILayout.HelpBox("只处理清单中的网格。IR target ID 按同名形态键写入，不扫描其他场景物体。", MessageType.Info);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("weightScale"), new GUIContent("Unity 权重倍率"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("autoRun"), new GUIContent("自动运行"));
            serializedObject.ApplyModifiedProperties();
            var component = (HoVtsIrController)target;
            EditorGUILayout.LabelField("目标数", component.TargetCount.ToString());
            if (Application.isPlaying) EditorGUILayout.LabelField("状态", component.Status);
        }
    }
}
