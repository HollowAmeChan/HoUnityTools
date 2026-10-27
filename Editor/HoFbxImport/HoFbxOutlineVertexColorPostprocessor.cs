#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.RigConstraints
{
    /// <summary>Per-FBX opt-in. Only imported mesh data is changed; the source FBX is untouched.</summary>
    internal sealed class HoFbxOutlineVertexColorPostprocessor : AssetPostprocessor
    {
        private const string WeldMarker = "\n[HoUnityTools.OutlineVertexColor:v1:weld]";
        private const string SplitMarker = "\n[HoUnityTools.OutlineVertexColor:v1:split]";

        internal static bool IsEnabled(AssetImporter importer)
        {
            string data = importer == null ? string.Empty : importer.userData ?? string.Empty;
            return data.Contains(WeldMarker) || data.Contains(SplitMarker);
        }

        internal static bool GetWeld(AssetImporter importer)
        {
            return importer == null || !(importer.userData ?? string.Empty).Contains(SplitMarker);
        }

        internal static void SetSettings(ModelImporter importer, bool enabled, bool weld)
        {
            if (importer == null) throw new ArgumentNullException(nameof(importer));
            // Retain all other tools' userData byte-for-byte, including non-JSON content.
            importer.userData = WithSettings(importer.userData, enabled, weld);
        }

        internal static string WithSettings(string userData, bool enabled, bool weld)
        {
            string data = (userData ?? string.Empty).Replace(WeldMarker, string.Empty)
                .Replace(SplitMarker, string.Empty);
            return enabled ? data + (weld ? WeldMarker : SplitMarker) : data;
        }

        public override uint GetVersion() => 2;
        public override int GetPostprocessOrder() => 1000;

        private void OnPostprocessModel(GameObject root)
        {
            if (!string.Equals(Path.GetExtension(assetPath), ".fbx", StringComparison.OrdinalIgnoreCase) ||
                !IsEnabled(assetImporter)) return;

            var meshes = new HashSet<Mesh>();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null) meshes.Add(filter.sharedMesh);
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (renderer.sharedMesh != null) meshes.Add(renderer.sharedMesh);

            foreach (Mesh mesh in meshes)
            {
                if (mesh.vertexCount == 0) continue;
                Vector4[] originalTangents=mesh.tangents;
                try
                {
                    HoFbxOutlineCorrectionBuilder.RepairTangentFrames(mesh);
                    // Remove the authored channel explicitly. It must never be interpreted as correction data.
                    mesh.colors=Array.Empty<Color>();
                    Vector4[] encoded = HoFbxOutlineCorrectionBuilder.Build(mesh, GetWeld(assetImporter));
                    var colors = new Color[encoded.Length];
                    for (int i = 0; i < colors.Length; i++)
                        colors[i] = new Color(encoded[i].x, encoded[i].y, encoded[i].z, encoded[i].w);
                    // Create a fresh Float32 RGBA channel, not a blend with the imported colours.
                    mesh.colors = colors;
                }
                catch (Exception exception)
                {
                    mesh.tangents=originalTangents;
                    var fallback=new Color[mesh.vertexCount];
                    for(int i=0;i<fallback.Length;i++)fallback[i]=new Color(.5f,.5f,1,1);
                    mesh.colors=Array.Empty<Color>();mesh.colors=fallback;
                    context.LogImportWarning($"描边顶点色烘焙跳过 Mesh「{mesh.name}」：{exception.Message} " +
                        "请检查模型的 Normals / Tangents 导入设置；已使用中性修正值回退到网格法线，避免将原顶点色误读为修正数据。");
                }
            }
        }
    }
}
#endif
