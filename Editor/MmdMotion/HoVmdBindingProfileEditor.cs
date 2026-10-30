#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Hollow.HoUnityTools.MmdMotion;

namespace Hollow.HoUnityTools.Editor.MmdMotion
{
    [CustomEditor(typeof(HoVmdBindingProfile))]
    internal sealed class HoVmdBindingProfileEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox("所有路径相对于播放器的「角色根」，不是相对于本资产。根本身用 . 表示。局部 Bone Bindings 会覆盖这里的骨骼映射。", MessageType.Info);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("sourceModel"), new GUIContent("源模型说明"));
            DrawRows("bones", "骨骼映射", new[] { "sourceName", "targetPath" }, new[] { "VMD 源骨骼名", "目标相对路径" });
            DrawRows("morphs", "表情映射", new[] { "sourceName", "rendererPath", "blendShapeName" }, new[] { "VMD 表情名", "网格相对路径", "目标形态键名" });
            DrawRows("ik", "IK 开关映射", new[] { "sourceName", "constraintPath", "constraintName" }, new[] { "VMD IK 名", "约束相对路径", "约束名称说明" });
            serializedObject.ApplyModifiedProperties();
        }
        private void DrawRows(string property, string label, string[] fields, string[] labels)
        {
            var list = serializedObject.FindProperty(property);
            list.isExpanded = EditorGUILayout.Foldout(list.isExpanded, label + "（" + list.arraySize + "）", true);
            if (!list.isExpanded) return;
            for (int i = 0; i < list.arraySize; i++)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                var entry = list.GetArrayElementAtIndex(i);
                for (int j = 0; j < fields.Length; j++) EditorGUILayout.PropertyField(entry.FindPropertyRelative(fields[j]), new GUIContent(labels[j]));
                if (GUILayout.Button("移除此项")) { list.DeleteArrayElementAtIndex(i); EditorGUILayout.EndVertical(); break; }
                EditorGUILayout.EndVertical();
            }
            if (GUILayout.Button("添加" + label))
            {
                int index = list.arraySize;
                list.arraySize++;
                var entry = list.GetArrayElementAtIndex(index);
                foreach (string field in fields) entry.FindPropertyRelative(field).stringValue = "";
            }
        }
    }
}
#endif
