using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    [CustomEditor(typeof(HoVtsLookAt))]
    public sealed class HoVtsLookAtEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            Field("parameterReceiver", "参数接收器");
            Field("referenceFrame", "姿态参考物体", "使用这个物体的轴向解释 VTS 角度，通常是角色根物体。");
            Field("head", "头部骨骼");
            Field("leftEye", "左眼骨骼");
            Field("rightEye", "右眼骨骼");
            var source = serializedObject.FindProperty("parameterSource");
            source.enumValueIndex = EditorGUILayout.Popup("参数来源", source.enumValueIndex, new[] { "标定后原始值", "中间层输出" });
            Field("headEnabled", "控制头部");
            Field("eyesEnabled", "控制双眼");
            Field("smoothingSeconds", "姿态平滑（秒）", "0 为直通；丢追时使用同样的平滑回到中性姿态。");
            Field("autoRun", "自动运行");
            serializedObject.ApplyModifiedProperties();
            if (Application.isPlaying) EditorGUILayout.LabelField("状态", ((HoVtsLookAt)target).Status);
        }

        private void Field(string property, string label, string tooltip = null) =>
            EditorGUILayout.PropertyField(serializedObject.FindProperty(property), new GUIContent(label, tooltip));
    }
}
