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
            EditorGUILayout.PropertyField(serializedObject.FindProperty("outputRoot"), new GUIContent("输出根物体"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("weightScale"), new GUIContent("Unity 权重倍率"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("autoRun"), new GUIContent("自动运行"));
            serializedObject.ApplyModifiedProperties();
            var component = (HoVtsIrController)target;
            EditorGUILayout.LabelField("目标数", component.TargetCount.ToString());
            if (Application.isPlaying) EditorGUILayout.LabelField("状态", component.Status);
        }
    }
}
