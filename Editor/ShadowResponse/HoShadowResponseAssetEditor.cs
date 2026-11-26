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
            foreach (string name in new[] { "red", "green", "blue", "alpha" }) EditorGUILayout.PropertyField(serializedObject.FindProperty(name));
            if (serializedObject.ApplyModifiedProperties()) foreach (var value in targets) HoShadowResponseBaker.Queue((HoShadowResponseAsset)value);
            if (GUILayout.Button("更新 LUT / Update LUT")) foreach (var value in targets) HoShadowResponseBaker.Bake((HoShadowResponseAsset)value);
            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField("LUT", ((HoShadowResponseAsset)target).LookupTexture, typeof(Texture2D), false);
        }
    }
}
