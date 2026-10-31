using System.Collections.Generic;
using Hollow.HoUnityTools.Constraints;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Constraints
{
    [CustomEditor(typeof(HoBlendShapeOverwrite))]
    internal sealed class HoBlendShapeOverwriteEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox("启用时将覆盖规则应用到 Mesh 列表；物体或组件禁用时立即恢复。运行时自动生效。", MessageType.Info);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("meshes"), new GUIContent("Mesh 对象列表"), true);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("覆盖规则", EditorStyles.boldLabel);
            SerializedProperty rules = serializedObject.FindProperty("rules");
            for (int i = 0; i < rules.arraySize; i++)
            {
                SerializedProperty item = rules.GetArrayElementAtIndex(i);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(item.FindPropertyRelative("shapeName"), GUIContent.none);
                    EditorGUILayout.PropertyField(item.FindPropertyRelative("value"), GUIContent.none);
                    if (GUILayout.Button("−", GUILayout.Width(24))) { rules.DeleteArrayElementAtIndex(i); break; }
                }
            }
            if (GUILayout.Button("添加规则（BS 名称 + 值）"))
            {
                int index = rules.arraySize++;
                SerializedProperty item = rules.GetArrayElementAtIndex(index);
                item.FindPropertyRelative("shapeName").stringValue = "";
                item.FindPropertyRelative("value").floatValue = 100;
            }
            EditorGUILayout.Space();
            SerializedProperty mode = serializedObject.FindProperty("restoreMode");
            mode.enumValueIndex = EditorGUILayout.Popup("禁用时恢复", mode.enumValueIndex, new[] { "指定值", "启用前的值" });
            if (mode.enumValueIndex == 0)
                EditorGUILayout.PropertyField(serializedObject.FindProperty("restoreValue"), new GUIContent("恢复值"));
            bool changed = serializedObject.ApplyModifiedProperties();
            var component = (HoBlendShapeOverwrite)target;
            if (changed) component.EvaluateOverwrite();
            foreach (HoBlendShapeOverwrite.Rule rule in component.Rules)
            {
                if (rule == null || string.IsNullOrWhiteSpace(rule.shapeName)) continue;
                bool found = false;
                foreach (SkinnedMeshRenderer renderer in component.Meshes)
                    if (renderer != null && renderer.sharedMesh != null &&
                        (renderer.sharedMesh.GetBlendShapeIndex(rule.shapeName) >= 0 ||
                         HoShapeKeyResolver.TryResolve(renderer.sharedMesh, rule.shapeName, out _))) { found = true; break; }
                if (!found) EditorGUILayout.HelpBox("列表中找不到形态键：" + rule.shapeName, MessageType.Warning);
            }
            using (new EditorGUI.DisabledScope(Application.isPlaying || EditorUtility.IsPersistent(component)))
            {
                if (GUILayout.Button(component.EditorPreviewActive ? "停止编辑预览并恢复" : "开始编辑预览"))
                    HoBlendShapeOverwritePreview.Set(component, !component.EditorPreviewActive);
            }
            EditorGUILayout.HelpBox("编辑预览只在本次会话有效，保存前自动停止并恢复。没有指定键的网格会跳过。不要让其他动画或组件同时控制同一个键。", MessageType.None);
        }
    }

    [InitializeOnLoad]
    internal static class HoBlendShapeOverwritePreview
    {
        private static readonly HashSet<HoBlendShapeOverwrite> Previewing = new HashSet<HoBlendShapeOverwrite>();
        static HoBlendShapeOverwritePreview()
        {
            AssemblyReloadEvents.beforeAssemblyReload += StopAll;
            EditorApplication.quitting += StopAll;
            EditorApplication.playModeStateChanged += _ => StopAll();
            EditorSceneManager.sceneSaving += (_, __) => StopAll();
            PrefabStage.prefabSaving += _ => StopAll();
            Undo.undoRedoPerformed += StopAll;
            EditorApplication.update += Tick;
        }
        internal static void Set(HoBlendShapeOverwrite component, bool value)
        {
            if (value) Previewing.Add(component); else Previewing.Remove(component);
            component.SetEditorPreview(value);
            SceneView.RepaintAll();
        }
        private static void Tick()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            foreach (HoBlendShapeOverwrite component in Previewing)
                if (component != null) component.EvaluateOverwrite();
        }
        internal static void StopAll()
        {
            foreach (HoBlendShapeOverwrite component in Previewing)
                if (component != null) component.SetEditorPreview(false);
            Previewing.Clear();
        }
    }

    internal sealed class HoBlendShapeOverwriteSaveGuard : AssetModificationProcessor
    {
        private static string[] OnWillSaveAssets(string[] paths)
        {
            HoBlendShapeOverwritePreview.StopAll();
            return paths;
        }
    }
}
