using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Pmx
{
    public static class HoPmxMaterialExtraction
    {
        public static int Extract(string importerAssetPath, string folderAssetPath)
        {
            folderAssetPath = folderAssetPath.Replace('\\', '/').TrimEnd('/');
            if (!folderAssetPath.StartsWith("Assets/", StringComparison.Ordinal) && folderAssetPath != "Assets")
                throw new ArgumentException("材质必须提取到项目的 Assets 目录。");
            if (!AssetDatabase.IsValidFolder(folderAssetPath))
                throw new ArgumentException("请选择已存在的 Assets 文件夹。");
            var importer = AssetImporter.GetAtPath(importerAssetPath) as HoPmxImporter;
            if (importer == null) throw new ArgumentException("请选择 Ho PMX 资产。");
            var report = AssetDatabase.LoadAllAssetsAtPath(importerAssetPath).OfType<HoPmxImportReport>().FirstOrDefault();
            if (report == null) throw new InvalidOperationException("该 PMX 尚未成功导入。");
            int count = 0;
            var extractedTextures = new Dictionary<Texture, Texture>();
            foreach (var entry in report.materials)
            {
                var source = entry.material;
                if (source == null || AssetDatabase.GetAssetPath(source) != importerAssetPath) continue;
                string name = entry.sourceName;
                foreach (char invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
                if (string.IsNullOrWhiteSpace(name)) name = "Material";
                string path = AssetDatabase.GenerateUniqueAssetPath(folderAssetPath + "/" + entry.index.ToString("D3") + "_" + name + ".mat");
                // Original PNG/TGA/etc. stay external references. Only role-converted data
                // subassets are copied, so the extracted material is independently editable.
                var material = new Material(source) { name = source.name };
                foreach (string property in material.GetTexturePropertyNames())
                {
                    Texture texture = material.GetTexture(property);
                    if (texture == null || AssetDatabase.GetAssetPath(texture) != importerAssetPath) continue;
                    if (!extractedTextures.TryGetValue(texture, out var extracted))
                    {
                        string textureFolder = folderAssetPath + "/Textures";
                        if (!AssetDatabase.IsValidFolder(textureFolder)) AssetDatabase.CreateFolder(folderAssetPath, "Textures");
                        extracted = UnityEngine.Object.Instantiate(texture);
                        extracted.name = texture.name;
                        string textureName = texture.name;
                        foreach (char invalid in Path.GetInvalidFileNameChars()) textureName = textureName.Replace(invalid, '_');
                        AssetDatabase.CreateAsset(extracted, AssetDatabase.GenerateUniqueAssetPath(textureFolder + "/" + textureName + ".asset"));
                        extractedTextures.Add(texture, extracted);
                    }
                    material.SetTexture(property, extracted);
                }
                AssetDatabase.CreateAsset(material, path);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), entry.identifier), material);
                ++count;
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.WriteImportSettingsIfDirty(importerAssetPath);
            AssetDatabase.ImportAsset(importerAssetPath, ImportAssetOptions.ForceUpdate);
            return count;
        }
    }
}
