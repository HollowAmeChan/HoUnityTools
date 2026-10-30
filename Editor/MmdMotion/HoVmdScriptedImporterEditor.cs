#if UNITY_EDITOR
using Hollow.HoUnityTools.MmdMotion;
using UnityEditor;
using UnityEditor.AssetImporters;
namespace Hollow.HoUnityTools.Editor.MmdMotion
{
    [CustomEditor(typeof(HoVmdScriptedImporter))]
    public sealed class HoVmdScriptedImporterEditor : ScriptedImporterEditor
    {
        private readonly HoVmdBindingPanel binding = new HoVmdBindingPanel();
        public override bool showImportedObject => false;
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var importer = (HoVmdScriptedImporter)target;
            var animation = AssetDatabase.LoadAssetAtPath<HoVmdAnimation>(importer.assetPath);
            EditorGUILayout.LabelField("VMD 动画", EditorStyles.boldLabel);
            if (animation == null) EditorGUILayout.HelpBox("尚未读取到 VMD 数据，请重新导入或检查 Console。", MessageType.Warning);
            else
            {
                HoVmdInspectorGUI.DrawSummary(animation);
                EditorGUILayout.Space();
                binding.Draw(animation);
            }
            ApplyRevertGUI();
        }
    }
}
#endif
