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
                "链条顺序：效应器父骨骼 → 根。脚 IK：效应器=脚踝，链=膝盖→大腿；toe IK：效应器=脚趾，链=脚踝。Pole 是链外的弯曲方向参考；不要让 Target 成为被解算骨骼的子物体。",
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
