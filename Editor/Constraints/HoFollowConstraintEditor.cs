using System;
using Hollow.HoUnityTools.Constraints;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Constraints
{
    [CustomEditor(typeof(HoFollowConstraint))]
    [CanEditMultipleObjects]
    internal sealed class HoFollowConstraintEditor : HoMotionConstraintEditor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            Field("target", "跟随目标");
            Field("updateMode", "更新时机", "同帧的 IK 或其他约束需要读取结果时，要让跟随先执行。");
            Field("space", "坐标空间", "父级本地空间会直接继承父级运动。世界空间也会对父级运动做阻尼。");

            EditorGUILayout.Space();
            Field("positionFollow", "位置跟随");
            Field("rotationFollow", "旋转跟随");
            Field("response", "响应速度");
            Field("overshoot", "惯性");

            EditorGUILayout.Space();
            Field("lockX", "锁定 X");
            Field("lockY", "锁定 Y");
            Field("lockZ", "锁定 Z");
            Field("keepHorizon", "保持水平", "保留初始俯仰和滚转，只跟随目标的偏航。");
            Field("positionOffset", "位置偏移", "沿目标自身的轴偏移，随目标缩放。");
            Field("rotationOffset", "旋转偏移", "在跟随结果上叠加的角度。");
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            if (GUILayout.Button(new GUIContent("清空", "应用原有的清空预设。保留目标和更新时机。")))
            {
                ApplyPreset(ApplyClearPreset);
            }
        }

        private static void ApplyClearPreset(SerializedObject item)
        {
            item.FindProperty("space").intValue = (int)HoFollowConstraintSpace.Local;
            item.FindProperty("positionFollow").floatValue = 1f;
            item.FindProperty("rotationFollow").floatValue = 1f;
            item.FindProperty("response").floatValue = 10f;
            item.FindProperty("overshoot").floatValue = 0f;
            item.FindProperty("lockX").floatValue = 0f;
            item.FindProperty("lockY").floatValue = 0f;
            item.FindProperty("lockZ").floatValue = 0f;
            item.FindProperty("keepHorizon").boolValue = false;
            item.FindProperty("positionOffset").vector3Value = Vector3.zero;
            item.FindProperty("rotationOffset").vector3Value = Vector3.zero;
        }
    }

    internal abstract class HoMotionConstraintEditor : UnityEditor.Editor
    {
        protected void Field(string property, string label, string tooltip = null)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty(property), new GUIContent(label, tooltip));
        }

        protected void ApplyPreset(Action<SerializedObject> apply)
        {
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("应用约束预设");
            foreach (var selected in targets)
            {
                using (var item = new SerializedObject(selected))
                {
                    // In Play Mode the transform may already contain the effect being edited.
                    if (!Application.IsPlaying(selected))
                    {
                        var pose = ((Component)selected).transform;
                        item.FindProperty("hasInitialTransform").boolValue = true;
                        item.FindProperty("initialLocalPosition").vector3Value = pose.localPosition;
                        item.FindProperty("initialLocalRotation").vector3Value = pose.localEulerAngles;
                    }

                    apply(item);
                    item.ApplyModifiedProperties();
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
            serializedObject.Update();
        }
    }
}
