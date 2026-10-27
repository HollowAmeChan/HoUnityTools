using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Pmx
{
    [CustomEditor(typeof(HoPmxImporter))]
    public sealed class HoPmxImporterEditor : ScriptedImporterEditor
    {
        private int tab;
        private bool showInformation;
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var importer = (HoPmxImporter)target;
            var report = AssetDatabase.LoadAllAssetsAtPath(importer.assetPath).OfType<HoPmxImportReport>().FirstOrDefault();
            tab = GUILayout.Toolbar(tab, new[] { "模型", "材质", "表情" });
            EditorGUILayout.Space(6);
            if (tab == 0)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("scale"), new GUIContent("缩放", "默认 0.08，将 MMD 单位转换为 Unity 米。"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("recalculateNormals"), new GUIContent("重算法线", "默认保留 PMX 原始法线。"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("generateOutline"), new GUIContent("启用源描边"));
                if (report != null)
                {
                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("PMX " + report.pmxVersion.ToString("0.0") + " · " +
                        report.vertexCount.ToString("N0") + " 顶点 · " + report.triangleCount.ToString("N0") + " 三角面", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField(report.materials.Count + " 材质 · 静态场景", EditorStyles.miniLabel);
                }
            }
            else if (tab == 1)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("readRayMaterials"), new GUIContent("读取 Ray 配套材质"));
                if (serializedObject.FindProperty("readRayMaterials").boolValue)
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("rayMaterialMapping"),
                        new GUIContent("材质映射", "留空时查找 PMX 同目录及直属子目录中的唯一 Material_MME.emd。"));
                if (report != null)
                {
                    bool pending = HasModified();
                    using (new EditorGUI.DisabledScope(pending))
                    {
                        if (GUILayout.Button("提取材质…"))
                        {
                            string directory = Path.GetFullPath(Path.GetDirectoryName(importer.assetPath));
                            string folder = EditorUtility.SaveFolderPanel("提取 Ho lilToon 材质", directory, "Materials");
                            if (!string.IsNullOrEmpty(folder))
                            {
                                try
                                {
                                    serializedObject.ApplyModifiedProperties();
                                    HoPmxMaterialExtraction.Extract(importer.assetPath, FileUtil.GetProjectRelativePath(folder));
                                    GUIUtility.ExitGUI();
                                }
                                catch (Exception e) when (!(e is ExitGUIException))
                                { EditorUtility.DisplayDialog("提取材质", e.Message, "确定"); }
                            }
                        }
                    }
                    if (pending) EditorGUILayout.LabelField("应用导入设置后可提取材质。", EditorStyles.miniLabel);
                    EditorGUILayout.Space(5);
                    var mappings = importer.GetExternalObjectMap();
                    foreach (var entry in report.materials)
                    {
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        EditorGUILayout.LabelField(entry.index.ToString("D3") + "  " + entry.sourceName, EditorStyles.boldLabel);
                        var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), entry.identifier);
                        mappings.TryGetValue(id, out var external);
                        using (new EditorGUI.DisabledScope(pending))
                        {
                            EditorGUI.BeginChangeCheck();
                            var next = (Material)EditorGUILayout.ObjectField("外部材质", external, typeof(Material), false);
                            if (EditorGUI.EndChangeCheck())
                            {
                                serializedObject.ApplyModifiedProperties();
                                Undo.RecordObject(importer, "PMX 材质关联");
                                if (next == null) importer.RemoveRemap(id); else importer.AddRemap(id, next);
                                importer.SaveAndReimport();
                                GUIUtility.ExitGUI();
                            }
                        }
                        DrawMaterialSetting(entry);
                        EditorGUILayout.LabelField(entry.alphaMode + " · " + entry.alphaReason, EditorStyles.miniLabel);
                        EditorGUILayout.EndVertical();
                    }
                }
            }
            else if (report != null)
            {
                EditorGUILayout.LabelField("表情状态在导入时烘焙到静态模型。", EditorStyles.miniLabel);
                var settings = serializedObject.FindProperty("morphSettings");
                foreach (var morph in report.morphs)
                {
                    int index = FindIndex(settings, "morphIndex", morph.index);
                    float weight = index < 0 ? 0 : settings.GetArrayElementAtIndex(index).FindPropertyRelative("weight").floatValue;
                    bool supported = morph.type == 0 || morph.type == 1 || (morph.type >= 3 && morph.type <= 8);
                    using (new EditorGUI.DisabledScope(!supported))
                    {
                        EditorGUI.BeginChangeCheck();
                        float value = EditorGUILayout.Slider(new GUIContent(morph.sourceName, morph.behavior), weight, 0, 1);
                        if (EditorGUI.EndChangeCheck())
                        {
                            var item = EnsureEntry(settings, "morphIndex", morph.index);
                            item.FindPropertyRelative("weight").floatValue = value;
                        }
                    }
                }
                if (report.morphs.Count == 0) EditorGUILayout.LabelField("源 PMX 没有表情。", EditorStyles.miniLabel);
            }
            if (report != null && report.warnings.Count > 0)
            {
                EditorGUILayout.Space(5);
                showInformation = EditorGUILayout.Foldout(showInformation, "导入信息（" + report.warnings.Count + "）", true);
                if (showInformation)
                    foreach (string warning in report.warnings)
                        EditorGUILayout.HelpBox(warning, MessageType.Info);
            }
            ApplyRevertGUI();
        }

        private void DrawMaterialSetting(HoPmxMaterialInfo info)
        {
            var settings = serializedObject.FindProperty("materialSettings");
            int index = FindIndex(settings, "materialIndex", info.index);
            HoPmxAlphaMode mode = index < 0 ? HoPmxAlphaMode.Auto :
                (HoPmxAlphaMode)settings.GetArrayElementAtIndex(index).FindPropertyRelative("alphaMode").enumValueIndex;
            EditorGUI.BeginChangeCheck();
            mode = (HoPmxAlphaMode)EditorGUILayout.EnumPopup("透明方式", mode);
            if (EditorGUI.EndChangeCheck())
                EnsureEntry(settings, "materialIndex", info.index).FindPropertyRelative("alphaMode").enumValueIndex = (int)mode;
            if (mode == HoPmxAlphaMode.Cutout)
            {
                var item = EnsureEntry(settings, "materialIndex", info.index);
                EditorGUILayout.PropertyField(item.FindPropertyRelative("alphaCutoff"), new GUIContent("裁切阈值"));
            }
        }

        private static int FindIndex(SerializedProperty list, string key, int value)
        {
            for (int i = 0; i < list.arraySize; ++i)
                if (list.GetArrayElementAtIndex(i).FindPropertyRelative(key).intValue == value) return i;
            return -1;
        }
        private static SerializedProperty EnsureEntry(SerializedProperty list, string key, int value)
        {
            int i = FindIndex(list, key, value);
            if (i >= 0) return list.GetArrayElementAtIndex(i);
            i = list.arraySize++;
            var entry = list.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative(key).intValue = value;
            if (entry.FindPropertyRelative("alphaMode") != null)
            {
                entry.FindPropertyRelative("alphaMode").enumValueIndex = 0;
                entry.FindPropertyRelative("alphaCutoff").floatValue = 0.5f;
            }
            if (entry.FindPropertyRelative("weight") != null) entry.FindPropertyRelative("weight").floatValue = 0;
            return entry;
        }
    }
}
