#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Hollow.HoUnityTools.RigConstraints;

namespace Hollow.HoUnityTools.Editor.RigConstraints
{
    [CustomEditor(typeof(HoIkConstraint))]
    internal sealed class HoIkConstraintEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            HoIkConstraint constraint = (HoIkConstraint)target;
            EditorGUILayout.HelpBox(
                "链条顺序必须是：效应器的父骨骼 → 更靠近根的骨骼。MMD 足 IK 常见顺序是脚踝、膝盖、大腿；toe IK 可以用脚趾父骨或脚掌作为效应器。",
                MessageType.Info);

            DrawDefaultInspector();

            EditorGUILayout.Space(6.0f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("记录当前绑定旋转"))
                {
                    Undo.RecordObject(constraint, "Capture IK bind rotations");
                    constraint.CaptureBindPose();
                    EditorUtility.SetDirty(constraint);
                }
                if (GUILayout.Button("播放态手动求解"))
                {
                    constraint.SolveNow();
                }
            }

            if (!string.IsNullOrEmpty(constraint.LastError))
                EditorGUILayout.HelpBox(constraint.LastError, MessageType.Warning);
        }
    }
}
#endif
