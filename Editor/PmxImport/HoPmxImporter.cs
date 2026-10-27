using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hollow.HoUnityTools.Editor.Pmx
{
    [ScriptedImporter(1, "pmx", 100)]
    public sealed class HoPmxImporter : ScriptedImporter
    {
        [SerializeField, Min(0.00001f)] public float scale = 0.08f;
        [SerializeField] public bool recalculateNormals;
        [SerializeField] public bool generateOutline = true;
        [SerializeField] public bool readRayMaterials = true;
        [SerializeField] public DefaultAsset rayMaterialMapping;
        [SerializeField] public List<HoPmxMaterialSetting> materialSettings = new List<HoPmxMaterialSetting>();
        [SerializeField] public List<HoPmxMorphSetting> morphSettings = new List<HoPmxMorphSetting>();

        public override bool SupportsRemappedAssetType(Type type) { return type == typeof(Material); }
        public static string MaterialIdentifier(int index) { return "PMX_Material_" + index.ToString("D4"); }

        public override void OnImportAsset(AssetImportContext context)
        {
            GameObject root = null;
            try
            {
                if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0)
                    throw new InvalidDataException("模型缩放必须是大于零的有限数。");
                string source = HoPmxTextures.AbsoluteAssetPath(context.assetPath);
                HoPmxDocument doc = HoPmxReader.Read(source);
                var report = ScriptableObject.CreateInstance<HoPmxImportReport>();
                report.name = "Ho PMX 导入信息";
                report.hideFlags = HideFlags.DontSaveInBuild;
                report.sourceName = doc.Name;
                report.pmxVersion = doc.Version;
                report.vertexCount = doc.Vertices.Count;
                report.triangleCount = doc.Indices.Length / 3;
                report.boneCount = doc.Bones.Count;
                report.rigidBodyCount = doc.RigidBodyCount;
                report.jointCount = doc.JointCount;
                report.softBodyCount = doc.SoftBodyCount;
                report.warnings.AddRange(doc.Warnings);
                context.AddObjectToAsset("HoPmxImportReport", report);
                foreach (var material in doc.Materials)
                {
                    int i = report.materials.Count;
                    report.materials.Add(new HoPmxMaterialInfo
                    {
                        index = i, sourceName = material.Name, identifier = MaterialIdentifier(i), memo = material.Comment,
                        diffuse = ColorOf(material.Diffuse), ambient = ColorOf(material.Ambient),
                        specular = ColorOf(material.Specular), shininess = material.Shininess,
                        sourceFlags = material.Flags, edgeColor = ColorOf(material.EdgeColor), edgeSize = material.EdgeSize,
                        sharedToonIndex = material.SharedToon ? material.ToonIndex : -1, sphereMode = material.SphereMode
                    });
                }
                foreach (var morph in doc.Morphs)
                    report.morphs.Add(new HoPmxMorphInfo
                    {
                        index = report.morphs.Count, sourceName = morph.Name, type = morph.Type,
                        behavior = morph.Type == 0 || morph.Type == 1 || (morph.Type >= 3 && morph.Type <= 8)
                            ? "导入时烘焙" : "仅识别，不执行"
                    });
                HoPmxMorphBake.Apply(doc, morphSettings, report);
                if (doc.Materials.Exists(x => (x.Flags & 0xe0) != 0))
                    report.warnings.Add("PMX 2.1 的顶点颜色/点/线绘制标志只保留原始值，当前仍按静态三角面导入。");
                if (doc.RigidBodyCount + doc.JointCount + doc.SoftBodyCount > 0)
                    report.warnings.Add("当前按静态场景导入；刚体、Joint 和 SoftBody 只记录数量。");
                HoPmxRayDocument ray = readRayMaterials
                    ? HoPmxRayReader.Read(source, rayMaterialMapping == null ? null : HoPmxTextures.AbsoluteAssetPath(AssetDatabase.GetAssetPath(rayMaterialMapping)))
                    : new HoPmxRayDocument();
                report.rayMappingPath = ray.EmdPath;
                report.warnings.AddRange(ray.Warnings);
                root = new GameObject(Path.GetFileNameWithoutExtension(source));
                context.AddObjectToAsset("model", root);
                context.SetMainObject(root);
                var remaps = GetExternalObjectMap();
                using (var textures = new HoPmxTextures(context, report))
                {
                    foreach (var dependency in ray.Dependencies) textures.Depend(dependency);
                    int start = 0;
                    for (int i = 0; i < doc.Materials.Count; ++i)
                    {
                        var pmx = doc.Materials[i];
                        var info = report.materials[i];
                        info.baseTexturePath = TexturePath(doc, pmx.TextureIndex, source, textures);
                        info.toonTexturePath = pmx.SharedToon ? null : TexturePath(doc, pmx.ToonIndex, source, textures);
                        info.sphereTexturePath = TexturePath(doc, pmx.SphereTextureIndex, source, textures);
                        ray.Materials.TryGetValue(i, out var rayMaterial);
                        info.rayEffectPath = rayMaterial == null ? null : rayMaterial.FxPath;
                        if (rayMaterial != null && !rayMaterial.RecognizedTemplate) rayMaterial = null;
                        var mode = textures.Classify(doc, pmx, start, info.baseTexturePath, out string reason);
                        float cutoff = 0.5f;
                        if (rayMaterial != null && rayMaterial.AlphaMode == "Cutout")
                        { mode = HoPmxAlphaMode.Cutout; cutoff = rayMaterial.Cutoff; reason = "Ray 专用 Cutout 材质"; }
                        var setting = materialSettings.Find(x => x.materialIndex == i);
                        if (setting != null && setting.alphaMode != HoPmxAlphaMode.Auto)
                        { mode = setting.alphaMode; cutoff = setting.alphaCutoff; reason = "Inspector 覆盖"; }
                        // lilToon's transparent pass still clips against _Cutoff. A cutout
                        // default of 0.5 would erase translucent window/cloth pixels.
                        if (mode == HoPmxAlphaMode.Transparent) cutoff = 0.001f;
                        info.alphaMode = mode;
                        info.alphaReason = reason;
                        info.alphaCutoff = cutoff;
                        string identifier = MaterialIdentifier(i);
                        var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), identifier);
                        Material material;
                        // Recreate deterministic converted texture subassets even when a material
                        // is remapped; external materials may still refer to those textures.
                        Material generated = HoPmxMaterials.Build(pmx, info, rayMaterial, textures, scale, generateOutline, report);
                        if (remaps.TryGetValue(id, out var external) && external is Material mapped)
                        {
                            material = mapped;
                            DestroyImmediate(generated);
                        }
                        else
                        {
                            material = generated;
                            context.AddObjectToAsset(identifier, material);
                        }
                        info.material = material;
                        BuildMesh(context, root.transform, doc, pmx, i, start, material);
                        start += pmx.IndexCount;
                    }
                }
                if (report.materials.Exists(x => !string.IsNullOrEmpty(x.toonTexturePath) || x.sharedToonIndex >= 0))
                    report.warnings.Add("已保留 Toon 来源；初始材质使用 Ho lilToon 阴影参数，不把 MMD 光照 Ramp 当作 UV 阴影贴图。");
                if (report.materials.Exists(x => x.sphereMode != 0 && !string.IsNullOrEmpty(x.sphereTexturePath)))
                    report.warnings.Add("Sphere 来源已记录；当前未自动转换其混合/采样行为。");
                if (report.warnings.Count > 0)
                    context.LogImportWarning("Ho PMX 导入完成，" + report.warnings.Count + " 条提示，详见 Inspector 的导入信息。");
            }
            catch (Exception e)
            {
                context.LogImportError("Ho PMX 导入失败：" + e.Message);
                if (root == null)
                {
                    root = new GameObject(Path.GetFileNameWithoutExtension(context.assetPath));
                    context.AddObjectToAsset("model", root);
                    context.SetMainObject(root);
                }
            }
        }

        private void BuildMesh(AssetImportContext context, Transform root, HoPmxDocument doc,
            HoPmxMaterial material, int materialIndex, int start, Material targetMaterial)
        {
            if (material.IndexCount == 0) return;
            var map = new Dictionary<int, int>();
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv = new List<Vector2>();
            var colors = new List<Color>();
            var extras = new List<Vector4>[doc.AdditionalUvCount];
            for (int c = 0; c < extras.Length; ++c) extras[c] = new List<Vector4>();
            var indices = new int[material.IndexCount];
            for (int p = 0; p < indices.Length; ++p)
            {
                int sourceIndex = doc.Indices[start + p];
                if (!map.TryGetValue(sourceIndex, out int index))
                {
                    index = positions.Count;
                    map.Add(sourceIndex, index);
                    var v = doc.Vertices[sourceIndex];
                    // MMD and Unity are both left-handed. Rotate 180 degrees around Y,
                    // retaining winding, and flip texture V into Unity's image convention.
                    positions.Add(new Vector3(-v.Position.X, v.Position.Y, -v.Position.Z) * scale);
                    normals.Add(new Vector3(-v.Normal.X, v.Normal.Y, -v.Normal.Z).normalized);
                    uv.Add(new Vector2(v.Uv.X, 1 - v.Uv.Y));
                    colors.Add(new Color(1, 1, 1, v.EdgeScale));
                    for (int c = 0; c < extras.Length; ++c)
                    {
                        var a = v.AdditionalUvs[c];
                        extras[c].Add(new Vector4(a.X, a.Y, a.Z, a.W));
                    }
                }
                indices[p] = index;
            }
            var mesh = new Mesh { name = materialIndex.ToString("D3") + "_" + material.Name,
                indexFormat = positions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv);
            mesh.SetColors(colors);
            for (int c = 0; c < extras.Length; ++c) mesh.SetUVs(c + 1, extras[c]);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
            if (recalculateNormals) mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            context.AddObjectToAsset("mesh_" + materialIndex.ToString("D4"), mesh);
            var child = new GameObject(materialIndex.ToString("D3") + "_" + material.Name);
            child.transform.SetParent(root, false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = targetMaterial;
            renderer.shadowCastingMode = (material.Flags & 4) == 0 ? ShadowCastingMode.Off :
                (material.Flags & 1) != 0 ? ShadowCastingMode.TwoSided : ShadowCastingMode.On;
            renderer.receiveShadows = (material.Flags & 8) != 0;
        }

        private static string TexturePath(HoPmxDocument doc, int index, string source, HoPmxTextures textures)
        { return index < 0 || index >= doc.Textures.Count ? null : textures.Resolve(source, doc.Textures[index]); }
        internal static Color ColorOf(HoPmxVector4 c) { return new Color(c.X, c.Y, c.Z, c.W); }
        internal static Color ColorOf(HoPmxVector3 c) { return new Color(c.X, c.Y, c.Z, 1); }
    }
}
