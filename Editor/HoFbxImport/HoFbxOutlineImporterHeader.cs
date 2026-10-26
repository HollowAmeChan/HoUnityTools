#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.RigConstraints
{
    /// <summary>Adds controls to the native FBX header; retains Unity's Model/Rig/Animation/Materials inspector.</summary>
    [InitializeOnLoad]
    internal static class HoFbxOutlineImporterHeader
    {
        static HoFbxOutlineImporterHeader()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI += DrawHeader;
        }

        internal static bool Supports(UnityEditor.Editor editor)
        {
            if (!(editor is AssetImporterEditor) || editor.targets.Length == 0) return false;
            foreach (UnityEngine.Object target in editor.targets)
                if (!(target is ModelImporter importer) ||
                    !string.Equals(Path.GetExtension(importer.assetPath), ".fbx", StringComparison.OrdinalIgnoreCase))
                    return false;
            return true;
        }

        private static void DrawHeader(UnityEditor.Editor editor)
        {
            if (!Supports(editor)) return;
            bool enabled = HoFbxOutlineVertexColorPostprocessor.IsEnabled((AssetImporter)editor.target);
            bool weld = HoFbxOutlineVertexColorPostprocessor.GetWeld((AssetImporter)editor.target);
            bool mixedEnabled = false, mixedWeld = false;
            foreach (UnityEngine.Object target in editor.targets)
            {
                mixedEnabled |= enabled != HoFbxOutlineVertexColorPostprocessor.IsEnabled((AssetImporter)target);
                mixedWeld |= weld != HoFbxOutlineVertexColorPostprocessor.GetWeld((AssetImporter)target);
            }

            bool previousMixed = EditorGUI.showMixedValue;
            try
            {
                EditorGUI.showMixedValue = mixedEnabled;
                EditorGUI.BeginChangeCheck();
                bool nextEnabled = EditorGUILayout.ToggleLeft(new GUIContent("烘焙 lilToon 描边顶点色",
                    "HoUnityTools：默认关闭。覆盖此 FBX 的全部 RGBA 顶点色，RGB 为方向、A 为厚度。\n" +
                    "点击原生 Apply 应用；关闭后 Apply 可恢复 FBX 原顶点色。需要导入法线和切线，无需 GD。"), enabled);
                if (EditorGUI.EndChangeCheck())
                {
                    UpdateSettings(editor, nextEnabled, null);
                    enabled = nextEnabled;
                    mixedEnabled = false;
                }

                if (enabled && !mixedEnabled)
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        EditorGUI.showMixedValue = mixedWeld;
                        EditorGUI.BeginChangeCheck();
                        bool nextWeld = EditorGUILayout.Toggle(new GUIContent("连接重合顶点",
                            "跨 UV / 硬法线接缝生成连续方向。只连接蒙皮权重及所有形态键位置变化一致的顶点。"), weld);
                        if (EditorGUI.EndChangeCheck()) UpdateSettings(editor, null, nextWeld);
                        EditorGUI.showMixedValue = false;
                        EditorGUILayout.LabelField("lilToon 描边：选择 RGBA 方向 / 宽度模式", EditorStyles.wordWrappedMiniLabel);
                    }
                }
            }
            finally { EditorGUI.showMixedValue = previousMixed; }
        }

        internal static void UpdateSettings(UnityEditor.Editor editor, bool? enabled, bool? weld)
        {
            if (!Supports(editor)) return;
            // Serialized changes join the native importer's Apply/Revert and Undo workflow.
            // Edit each target separately so multi-selection preserves distinct third-party userData.
            foreach (UnityEngine.Object target in editor.targets)
            {
                var importer = (ModelImporter)target;
                using (var serialized = new SerializedObject(importer))
                {
                    SerializedProperty userData = serialized.FindProperty("m_UserData");
                    userData.stringValue = HoFbxOutlineVertexColorPostprocessor.WithSettings(userData.stringValue,
                        enabled ?? HoFbxOutlineVertexColorPostprocessor.IsEnabled(importer),
                        weld ?? HoFbxOutlineVertexColorPostprocessor.GetWeld(importer));
                    serialized.ApplyModifiedProperties();
                }
            }
            editor.serializedObject.SetIsDifferentCacheDirty();
            editor.serializedObject.UpdateIfRequiredOrScript();
        }
    }
}
#endif
