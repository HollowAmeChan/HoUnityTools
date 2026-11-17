using System;
using System.Collections.Generic;
using Hollow.HoUnityTools.Constraints;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Constraints
{
    [CustomEditor(typeof(HoBsCorrective))]
    internal sealed class HoBsCorrectiveEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var component = (HoBsCorrective)target;
            serializedObject.Update();
            EditorGUILayout.HelpBox("BS 修型中控读取导入骨骼的最终局部旋转，再按 Blender XYZ 顺序进行 RBF 插值。姿态单位是弧度，HoFBX 导出预处理使用 MCH 时，规则会改指对应的导出测量骨骼。", MessageType.Info);
            SerializedProperty rules = serializedObject.FindProperty("rules");
            for (int i = 0; i < rules.arraySize; i++)
            {
                SerializedProperty rule = rules.GetArrayElementAtIndex(i);
                bool deleted = false;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PropertyField(rule.FindPropertyRelative("enabled"), GUIContent.none, GUILayout.Width(18));
                        string shape = rule.FindPropertyRelative("shapeName").stringValue;
                        rule.isExpanded = EditorGUILayout.Foldout(rule.isExpanded, (i + 1) + ". " +
                            (string.IsNullOrEmpty(shape) ? "新修型规则" : shape), true);
                        using (new EditorGUI.DisabledScope(i == 0))
                            if (GUILayout.Button("↑", GUILayout.Width(24))) { rules.MoveArrayElement(i, i - 1); break; }
                        using (new EditorGUI.DisabledScope(i + 1 == rules.arraySize))
                            if (GUILayout.Button("↓", GUILayout.Width(24))) { rules.MoveArrayElement(i, i + 1); break; }
                        if (GUILayout.Button("−", GUILayout.Width(24)))
                        { rules.DeleteArrayElementAtIndex(i); deleted = true; }
                    }
                    if (!deleted && rule.isExpanded)
                    {
                        EditorGUILayout.PropertyField(rule.FindPropertyRelative("bone"), new GUIContent("测量骨骼"));
                        DrawRbf(rule);
                        EditorGUILayout.PropertyField(rule.FindPropertyRelative("bsName"), new GUIContent("BS 名称"));
                        EditorGUILayout.PropertyField(rule.FindPropertyRelative("target"), new GUIContent("目标网格"));
                        DrawShapeName(rule);
                        EditorGUILayout.PropertyField(rule.FindPropertyRelative("comment"), new GUIContent("注释"));
                    }
                    if (!deleted && i < component.Rules.Count)
                    {
                        HoBsCorrective.Rule runtime = component.Rules[i];
                        if (runtime != null && runtime.enabled)
                        {
                            if (!HoBsCorrective.ValidateRule(runtime, out string error))
                                EditorGUILayout.HelpBox(error, MessageType.Warning);
                            else if (!string.IsNullOrEmpty(runtime.lastError))
                                EditorGUILayout.HelpBox(runtime.lastError, MessageType.Warning);
                            if (component.EditorPreviewActive || Application.isPlaying)
                                EditorGUILayout.LabelField("输入 / 输出", $"XYZ = {runtime.lastRadians.x:F4}, {runtime.lastRadians.y:F4}, {runtime.lastRadians.z:F4} rad  →  {runtime.lastOutput:F4}");
                        }
                    }
                }
                if (deleted) break;
            }
            if (GUILayout.Button("添加 BS 修型规则"))
            {
                int i = rules.arraySize++;
                SerializedProperty rule = rules.GetArrayElementAtIndex(i);
                rule.isExpanded = true;
                rule.FindPropertyRelative("id").stringValue = Guid.NewGuid().ToString("N");
                rule.FindPropertyRelative("enabled").boolValue = true;
                rule.FindPropertyRelative("bone").objectReferenceValue = null;
                rule.FindPropertyRelative("target").objectReferenceValue = null;
                rule.FindPropertyRelative("shapeName").stringValue = string.Empty;
                rule.FindPropertyRelative("bsName").stringValue = "_BSC_pose1";
                rule.FindPropertyRelative("rbfRadius").floatValue = 0.5f;
                rule.FindPropertyRelative("rbfPoints").arraySize = 0;
                rule.FindPropertyRelative("comment").stringValue = string.Empty;
                foreach (string field in new[] { "sourceId", "bonePath", "targetPath", "sourceBone" })
                    rule.FindPropertyRelative(field).stringValue = string.Empty;
            }
            if (serializedObject.ApplyModifiedProperties())
            {
                component.InvalidateBindings();
                component.EvaluateNow();
            }
            using (new EditorGUI.DisabledScope(Application.isPlaying || EditorUtility.IsPersistent(component)))
                if (GUILayout.Button(component.EditorPreviewActive ? "停止编辑预览并恢复" : "开始编辑预览"))
                    HoBsCorrectivePreview.Set(component, !component.EditorPreviewActive);
            EditorGUILayout.HelpBox("基准姿态固定为局部零旋转。HoFBX v3 中每个控制点对应一个修型任务，导入时展开任务中的各个网格/形态键目标；该任务的点为 1，其他点为 0。编辑预览在保存、撤销或重载前自动恢复原 BS 值。", MessageType.None);
            if (component.EditorPreviewActive || Application.isPlaying) Repaint();
        }

        private static void DrawShapeName(SerializedProperty rule)
        {
            SerializedProperty name = rule.FindPropertyRelative("shapeName");
            var renderer = rule.FindPropertyRelative("target").objectReferenceValue as SkinnedMeshRenderer;
            Mesh mesh = renderer != null ? renderer.sharedMesh : null;
            if (mesh == null || mesh.blendShapeCount == 0)
            { EditorGUILayout.PropertyField(name, new GUIContent("目标形态键")); return; }
            var names = new List<string> { "（选择形态键）" };
            for (int i = 0; i < mesh.blendShapeCount; i++) names.Add(mesh.GetBlendShapeName(i));
            int selectedIndex = 0;
            for (int i = 1; i < names.Count; i++)
                if (names[i] == name.stringValue) { selectedIndex = i; break; }
            int selected = EditorGUILayout.Popup("目标形态键", selectedIndex, names.ToArray());
            if (selected > 0) name.stringValue = names[selected];
        }

        private static void DrawRbf(SerializedProperty rule)
        {
            EditorGUILayout.PropertyField(rule.FindPropertyRelative("rbfRadius"), new GUIContent("RBF 半径"));
            EditorGUILayout.LabelField("基准姿态", "0° / 0° / 0°（固定）");
            SerializedProperty points = rule.FindPropertyRelative("rbfPoints");
            points.isExpanded = EditorGUILayout.Foldout(points.isExpanded, "姿态控制点", true);
            if (!points.isExpanded) return;
            EditorGUI.indentLevel++;
            for (int i = 0; i < points.arraySize; i++)
            {
                SerializedProperty point = points.GetArrayElementAtIndex(i);
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.PropertyField(point.FindPropertyRelative("pose"), new GUIContent("姿态"));
                    EditorGUILayout.PropertyField(point.FindPropertyRelative("weight"), new GUIContent("权重"));
                    if (GUILayout.Button("−", GUILayout.Width(24))) { points.DeleteArrayElementAtIndex(i); break; }
                }
            }
            if (GUILayout.Button("添加姿态控制点"))
            {
                int index = points.arraySize++;
                SerializedProperty point = points.GetArrayElementAtIndex(index);
                point.FindPropertyRelative("pose").vector3Value = Vector3.zero;
                point.FindPropertyRelative("weight").floatValue = 1f;
            }
            EditorGUI.indentLevel--;
        }
    }

    [InitializeOnLoad]
    internal static class HoBsCorrectivePreview
    {
        private static readonly HashSet<HoBsCorrective> Previewing = new HashSet<HoBsCorrective>();
        static HoBsCorrectivePreview()
        {
            AssemblyReloadEvents.beforeAssemblyReload += StopAll;
            EditorApplication.quitting += StopAll;
            EditorApplication.playModeStateChanged += _ => StopAll();
            EditorSceneManager.sceneSaving += (_, __) => StopAll();
            PrefabStage.prefabSaving += _ => StopAll();
            Undo.undoRedoPerformed += StopAll;
            EditorApplication.update += Tick;
        }
        internal static void Set(HoBsCorrective component, bool value)
        {
            if (value) Previewing.Add(component); else Previewing.Remove(component);
            component.SetEditorPreview(value);
            SceneView.RepaintAll();
        }
        private static void Tick()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            foreach (HoBsCorrective component in Previewing)
                if (component != null) component.EvaluateNow();
            if (Previewing.Count > 0) SceneView.RepaintAll();
        }
        internal static void StopAll()
        {
            foreach (HoBsCorrective component in Previewing)
                if (component != null) component.SetEditorPreview(false);
            Previewing.Clear();
        }
    }

    internal sealed class HoBsCorrectiveSaveGuard : AssetModificationProcessor
    {
        private static string[] OnWillSaveAssets(string[] paths)
        { HoBsCorrectivePreview.StopAll(); return paths; }
    }
}
