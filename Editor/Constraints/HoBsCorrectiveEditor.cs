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
            EditorGUILayout.HelpBox("BS 修型中控读取导入骨骼的最终局部旋转，再按 Blender XYZ 顺序分解。x / y / z 单位是弧度，表达式输出 0～1。HoFbx 导出预处理使用 MCH 时，规则会改指对应的导出测量骨骼。", MessageType.Info);
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
                        SerializedProperty mode = rule.FindPropertyRelative("mode");
                        EditorGUILayout.PropertyField(mode, new GUIContent("求值模式"));
                        if (mode.stringValue == "RBF")
                            DrawRbf(rule);
                        else
                            EditorGUILayout.PropertyField(rule.FindPropertyRelative("expression"), new GUIContent("表达式"));
                        EditorGUILayout.PropertyField(rule.FindPropertyRelative("bsName"), new GUIContent("BS 名称"));
                        EditorGUILayout.PropertyField(rule.FindPropertyRelative("target"), new GUIContent("目标网格"));
                        DrawShapeName(rule);
                        EditorGUILayout.PropertyField(rule.FindPropertyRelative("comment"), new GUIContent("注释"));
                        SerializedProperty variables = rule.FindPropertyRelative("variables");
                        variables.isExpanded = EditorGUILayout.Foldout(variables.isExpanded, "附加骨骼变量（左右组合修型）", true);
                        if (variables.isExpanded)
                        {
                            EditorGUI.indentLevel++;
                            for (int v = 0; v < variables.arraySize; v++)
                            {
                                SerializedProperty variable = variables.GetArrayElementAtIndex(v);
                                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                                {
                                    using (new EditorGUILayout.HorizontalScope())
                                    {
                                        EditorGUILayout.PropertyField(variable.FindPropertyRelative("name"), new GUIContent("变量名"));
                                        if (GUILayout.Button("−", GUILayout.Width(24)))
                                        { variables.DeleteArrayElementAtIndex(v); break; }
                                    }
                                    EditorGUILayout.PropertyField(variable.FindPropertyRelative("bone"), new GUIContent("测量骨骼"));
                                    EditorGUILayout.PropertyField(variable.FindPropertyRelative("channel"), new GUIContent("局部旋转通道"));
                                }
                            }
                            if (GUILayout.Button("添加骨骼变量"))
                            {
                                int v = variables.arraySize++;
                                SerializedProperty variable = variables.GetArrayElementAtIndex(v);
                                variable.FindPropertyRelative("name").stringValue = "other_" + v;
                                variable.FindPropertyRelative("bone").objectReferenceValue = null;
                                variable.FindPropertyRelative("channel").enumValueIndex = 0;
                                variable.FindPropertyRelative("bonePath").stringValue = string.Empty;
                                variable.FindPropertyRelative("sourceBone").stringValue = string.Empty;
                            }
                            EditorGUI.indentLevel--;
                        }
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
                rule.FindPropertyRelative("expression").stringValue = "clamp(x / 1.57079632679, 0, 1)";
                rule.FindPropertyRelative("bsName").stringValue = "_BSC_pose1";
                rule.FindPropertyRelative("mode").stringValue = "EXPRESSION";
                rule.FindPropertyRelative("rbfRadius").floatValue = 0.5f;
                rule.FindPropertyRelative("rbfIncludeBase").boolValue = true;
                rule.FindPropertyRelative("rbfBase").vector3Value = Vector3.zero;
                rule.FindPropertyRelative("rbfPoints").arraySize = 0;
                rule.FindPropertyRelative("comment").stringValue = string.Empty;
                rule.FindPropertyRelative("variables").arraySize = 0;
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
            EditorGUILayout.HelpBox("RBF 模式使用主骨骼局部 XYZ 姿态、基准姿态、Gaussian 半径和多个控制点；控制点权重是该姿态的目标 BS 值。表达式模式保留旧版兼容。编辑预览在保存、撤销或重载前自动恢复原 BS 值。单个目标键只允许一条启用规则。", MessageType.None);
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
            EditorGUILayout.PropertyField(rule.FindPropertyRelative("rbfIncludeBase"), new GUIContent("包含基准姿态"));
            EditorGUILayout.PropertyField(rule.FindPropertyRelative("rbfBase"), new GUIContent("基准姿态 XYZ"));
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
