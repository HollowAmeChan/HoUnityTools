#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Hollow.HoUnityTools.RigConstraints;

namespace Hollow.HoUnityTools.Editor.RigConstraints
{
    [CustomEditor(typeof(HoAuxRig))]
    internal sealed class HoAuxRigEditor : UnityEditor.Editor
    {
        private HoAuxRig.OperationType newOperationType = HoAuxRig.OperationType.Twist;
        private Transform newOwner;
        private Transform newTarget;
        private float newWeight = 1.0f;

        public override void OnInspectorGUI()
        {
            HoAuxRig rig = (HoAuxRig)target;
            EditorGUILayout.HelpBox(
                $"单组件 Rig 中控：{rig.Layers.Count} 层，{CountOperations(rig)} 条操作。" +
                "层会按顺序执行，Parent 先于 Twist，Fan 最后执行。",
                MessageType.Info);

            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();

            GUILayout.Space(6f);
            DrawAddOperationPanel(rig);

            GUILayout.Space(6f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("以当前姿态重新绑定", GUILayout.Height(26f)))
                {
                    Undo.RecordObject(rig, "重新绑定 HoAux Rig");
                    rig.CaptureBindPose();
                    EditorUtility.SetDirty(rig);
                }
                if (GUILayout.Button("立即计算", GUILayout.Height(26f)))
                {
                    Undo.RecordObject(rig, "计算 HoAux Rig");
                    rig.EvaluateNow();
                    EditorUtility.SetDirty(rig);
                }
            }

            if (GUILayout.Button("清空全部 Rig 操作", GUILayout.Height(24f)) &&
                EditorUtility.DisplayDialog(
                    "清空 HoAux Rig",
                    "只会清空这个中控组件中的层和操作，不会删除骨骼或通用约束。",
                    "清空",
                    "取消"))
            {
                Undo.RecordObject(rig, "清空 HoAux Rig");
                rig.ClearOperations();
                EditorUtility.SetDirty(rig);
            }
        }

        private void DrawAddOperationPanel(HoAuxRig rig)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("新增绑定操作", EditorStyles.boldLabel);
                newOperationType = (HoAuxRig.OperationType)EditorGUILayout.EnumPopup(
                    "类型", newOperationType);
                newOwner = (Transform)EditorGUILayout.ObjectField(
                    "Owner", newOwner, typeof(Transform), true);
                newTarget = (Transform)EditorGUILayout.ObjectField(
                    "Target", newTarget, typeof(Transform), true);
                newWeight = EditorGUILayout.Slider("权重", newWeight, 0.0f, 1.0f);

                using (new EditorGUI.DisabledScope(newOwner == null || newTarget == null))
                {
                    if (GUILayout.Button("添加到对应层"))
                    {
                        Undo.RecordObject(rig, "新增 HoAux Rig 操作");
                        rig.AddOperation(newOperationType, newOwner, newTarget, newWeight);
                        EditorUtility.SetDirty(rig);
                        newOwner = null;
                        newTarget = null;
                    }
                }
            }
        }

        private static int CountOperations(HoAuxRig rig)
        {
            int count = 0;
            foreach (HoAuxRig.Layer layer in rig.Layers)
            {
                if (layer != null && layer.operations != null)
                    count += layer.operations.Count;
            }
            return count;
        }
    }
}
#endif
