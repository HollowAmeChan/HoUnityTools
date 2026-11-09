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
            // 这里以前还有一个「参数来源」（raw / 中间层输出）下拉。那个字段在 2026-10-31
            // 随「LookAt 不再吃 raw」一起删掉了，编辑器没跟上 ⇒ FindProperty 返回 null，
            // OnInspectorGUI 第 20 行 NRE。现在 LookAt 只读中间层输出，不需要这个开关。
            Field("headEnabled", "控制头部");
            Field("upperBodyEnabled", "上身摆动");
            using (new EditorGUI.DisabledScope(!(serializedObject.FindProperty("upperBodyEnabled")?.boolValue ?? false)))
                Field("upperBodyWeight", "上身摆动强度");
            Field("eyesEnabled", "控制双眼");
            Field("smoothingSeconds", "姿态平滑（秒）", "0 为直通；丢追时使用同样的平滑回到中性姿态。");
            Field("drawGizmos", "场景姿态 Gizmo", "选中组件时显示参考轴、实际骨骼朝向和目标朝向。");
            using (new EditorGUI.DisabledScope(!(serializedObject.FindProperty("drawGizmos")?.boolValue ?? false)))
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

        /// <summary>
        /// 画一个序列化字段。字段被改名/删掉时 FindProperty 会返回 null —— 以前直接传进
        /// PropertyField 就是一条 NRE，整块 Inspector 报废；这里改成跳过并提示，别让编辑器炸。
        /// </summary>
        private void Field(string property, string label, string tooltip = null)
        {
            var found = serializedObject.FindProperty(property);
            if (found == null)
            {
                EditorGUILayout.HelpBox("组件上找不到字段 " + property + "（编辑器与运行时代码不同步）。", MessageType.Warning);
                return;
            }
            EditorGUILayout.PropertyField(found, new GUIContent(label, tooltip));
        }
    }
}
