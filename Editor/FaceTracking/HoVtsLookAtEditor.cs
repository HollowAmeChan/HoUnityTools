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
            Field("upperBodyChain", "上身骨骼链", "按 Spine → Chest → UpperChest 顺序填写。它是分布式姿态跟随链，不是单根骨骼 IK。通常优先使用 Chest/UpperChest，不要只填头骨。");
            Field("head", "头部骨骼");
            Field("leftEye", "左眼骨骼");
            Field("rightEye", "右眼骨骼");
            var source = serializedObject.FindProperty("parameterSource");
            source.enumValueIndex = EditorGUILayout.Popup("参数来源", source.enumValueIndex, new[] { "标定后原始值", "中间层输出" });
            Field("headEnabled", "控制头部");
            Field("upperBodyEnabled", "上身摆动");
            using (new EditorGUI.DisabledScope(!serializedObject.FindProperty("upperBodyEnabled").boolValue))
                Field("upperBodyWeight", "上身摆动强度");
            Field("eyesEnabled", "控制双眼");
            Field("smoothingSeconds", "姿态平滑（秒）", "0 为直通；丢追时使用同样的平滑回到中性姿态。");
            Field("drawGizmos", "场景姿态 Gizmo", "选中组件时显示参考轴、实际骨骼朝向和目标朝向。");
            using (new EditorGUI.DisabledScope(!serializedObject.FindProperty("drawGizmos").boolValue))
                Field("gizmoLength", "Gizmo 长度");
            Field("autoRun", "自动运行");
            serializedObject.ApplyModifiedProperties();
            var lookAt = (HoVtsLookAt)target;
            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField("状态", lookAt.Status);
                HoVtsLookAtDebug debug = lookAt.GetDebug();
                EditorGUILayout.LabelField("输入", debug.hasFrame ? (debug.tracked ? "追踪中" : "未追踪") : "无当前帧");
                EditorGUILayout.LabelField("说明", "黄 = 实际骨骼前方，紫 = VTS 目标前方，红绿蓝 = 参考坐标轴");
            }
        }

        private void Field(string property, string label, string tooltip = null) =>
            EditorGUILayout.PropertyField(serializedObject.FindProperty(property), new GUIContent(label, tooltip));
    }
}
