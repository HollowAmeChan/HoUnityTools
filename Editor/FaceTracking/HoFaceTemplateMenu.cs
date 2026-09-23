using Hollow.HoUnityTools.FaceTracking;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    /// <summary>
    /// 把内置的模板定义落成 <c>.asset</c> 文件 —— 这样"预设"就是**可编辑、可复制**的文件，而不是代码：
    /// 想改姿势表、或做自己的模板，复制一份改就行（`ho-2d-test1` 那种要反复改的实验品尤其需要这一点）。
    ///
    /// 对应的内置定义在 <see cref="HoFaceTemplateDefaults"/>（数据与出处都写在那儿），
    /// 这里只负责"落盘成文件"，不发明任何新数据。
    /// </summary>
    internal static class HoFaceTemplateMenu
    {
        private const string ParentFolder = "Assets/HoUnityTools";
        private const string Folder = ParentFolder + "/FaceTemplates";

        [MenuItem("HoUnityTools/面捕/生成模板资产（vrc-jerry + vrc-common）", false, 43)]
        private static void Create()
        {
            if (!AssetDatabase.IsValidFolder(ParentFolder)) AssetDatabase.CreateFolder("Assets", "HoUnityTools");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(ParentFolder, "FaceTemplates");

            Write("vrc-jerry", HoFaceTemplateDefaults.VrcJerry());
            Write("vrc-common", HoFaceTemplateDefaults.VrcCommon());
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void Write(string fileName, HoFaceTemplateSpec spec)
        {
            string path = Folder + "/" + fileName + ".asset";
            var asset = AssetDatabase.LoadAssetAtPath<HoFaceTemplate>(path);
            bool created = asset == null;
            if (created)
            {
                asset = ScriptableObject.CreateInstance<HoFaceTemplate>();
                AssetDatabase.CreateAsset(asset, path);
            }

            asset.spec = spec;
            EditorUtility.SetDirty(asset);
            Debug.Log("[Ho 面捕] " + (created ? "生成" : "更新") + "模板资产：" + path
                + "（" + spec.displayName + "；需要：" + spec.requiredKeysNote + "）", asset);
        }
    }
}
