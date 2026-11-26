using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.ShadowResponse
{
    [CustomEditor(typeof(HoShadowResponseAsset)), CanEditMultipleObjects]
    public sealed class HoShadowResponseAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            bool english = EditorPrefs.GetBool("HoShader.GUI.Language.English", false);
            string[] names = { "red", "green", "blue", "alpha" };
            string[] labels = english ? new[] { "Red (R)", "Green (G)", "Blue (B)", "Alpha (A)" }
                : new[] { "红色 (R)", "绿色 (G)", "蓝色 (B)", "透明度 (A)" };
            for (int i = 0; i < names.Length; ++i)
                EditorGUILayout.PropertyField(serializedObject.FindProperty(names[i]), new GUIContent(labels[i]));
            if (serializedObject.ApplyModifiedProperties()) foreach (var value in targets) HoShadowResponseBaker.Queue((HoShadowResponseAsset)value);
            if (GUILayout.Button(english ? "Update LUT" : "更新 LUT")) foreach (var value in targets) HoShadowResponseBaker.Bake((HoShadowResponseAsset)value);
            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField("LUT", ((HoShadowResponseAsset)target).LookupTexture, typeof(Texture2D), false);
        }
    }
}
