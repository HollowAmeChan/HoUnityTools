using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hollow.HoUnityTools.MmdMotion;
using Hollow.HoUnityTools.RigConstraints;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Hollow.HoUnityTools.Editor.Pmx
{
    /// <summary>Extracts a skinned, non-physics PMX pose reference without changing static stage import mode.</summary>
    public static class HoPmxCharacterExtraction
    {
        public static string Extract(string sourcePath, string folder)
        {
            if (!folder.StartsWith("Assets/") || folder.Split('/').Contains("..")) throw new ArgumentException("Use an Assets/... folder.");
            string prefabPath = folder + "/角色姿态.prefab";
            if (File.Exists(prefabPath)) throw new IOException("Character reference already exists: " + prefabPath);
            var importer = AssetImporter.GetAtPath(sourcePath) as HoPmxImporter;
            if (importer == null) throw new ArgumentException("Select a successfully imported Ho PMX asset.");
            var doc = HoPmxReader.Read(Path.GetFullPath(sourcePath));
            if (doc.Bones.Count == 0) throw new InvalidOperationException("PMX contains no bones.");
            Directory.CreateDirectory(folder + "/Materials"); Directory.CreateDirectory(folder + "/Meshes");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            HoPmxMaterialExtraction.Extract(sourcePath, folder + "/Materials");
            var report = AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<HoPmxImportReport>().First();
            float scale = importer.scale;
            var root = new GameObject(Path.GetFileNameWithoutExtension(sourcePath));
            try
            {
                var skeleton = new GameObject("MMD 骨架").transform; skeleton.SetParent(root.transform, false);
                Transform[] bones = doc.Bones.Select(b => new GameObject(b.Name).transform).ToArray();
                for (int i = 0; i < bones.Length; ++i)
                {
                    bones[i].SetParent(skeleton, false);
                    bones[i].position = Convert(doc.Bones[i].Position) * scale;
                }
                for (int i = 0; i < bones.Length; ++i)
                {
                    int parent = doc.Bones[i].ParentIndex;
                    if (parent < 0) continue;
                    if (parent == i || parent >= bones.Length) throw new InvalidDataException("Invalid PMX bone parent: " + i);
                    // Reject cycles instead of silently manufacturing a hierarchy.
                    var visited = new HashSet<int> { i };
                    int cursor = parent;
                    while (cursor >= 0)
                    {
                        if (!visited.Add(cursor)) throw new InvalidDataException("PMX bone hierarchy contains a cycle.");
                        cursor = doc.Bones[cursor].ParentIndex;
                    }
                    bones[i].SetParent(bones[parent], true);
                }
                var rig = root.AddComponent<HoPmxPoseRig>();
                foreach (int i in Enumerable.Range(0, bones.Length).OrderBy(x => doc.Bones[x].TransformLevel).ThenBy(x => x))
                {
                    var b = doc.Bones[i];
                    if (b.AppendParentIndex >= 0 && (b.Flags & 0x300) != 0)
                    {
                        Transform donor = bones[b.AppendParentIndex];
                        rig.Appends.Add(new HoPmxPoseRig.Append { owner = bones[i], source = donor, ratio = b.AppendRatio,
                            rotation = (b.Flags & 0x100) != 0, position = (b.Flags & 0x200) != 0,
                            restPosition = bones[i].localPosition, restRotation = bones[i].localRotation,
                            sourceRestPosition = donor.localPosition, sourceRestRotation = donor.localRotation });
                    }
                    if (b.IkTargetIndex >= 0 && b.IkLinks.Count > 0)
                    {
                        var ik = bones[i].gameObject.AddComponent<HoIkConstraint>();
                        ik.IkName = b.Name; ik.Target = bones[i]; ik.Effector = bones[b.IkTargetIndex];
                        ik.Mode = HoIkConstraint.UpdateMode.Manual; ik.UsePole = false;
                        ik.Iterations = Mathf.Clamp(b.IkIterations, 1, 80);
                        ik.MaxStepAngle = b.IkAngleLimit * Mathf.Rad2Deg;
                        foreach (var link in b.IkLinks)
                        {
                            var entry = new HoIkConstraint.Link { bone = bones[link.BoneIndex], useLocalEulerLimits = link.HasLimits };
                            if (link.HasLimits)
                            {
                                entry.localEulerMin = new Vector3(-link.Maximum.X, link.Minimum.Y, -link.Maximum.Z) * Mathf.Rad2Deg;
                                entry.localEulerMax = new Vector3(-link.Minimum.X, link.Maximum.Y, -link.Minimum.Z) * Mathf.Rad2Deg;
                                bool x = Mathf.Abs(entry.localEulerMax.x - entry.localEulerMin.x) > .01f;
                                bool y = Mathf.Abs(entry.localEulerMax.y - entry.localEulerMin.y) > .01f;
                                bool z = Mathf.Abs(entry.localEulerMax.z - entry.localEulerMin.z) > .01f;
                                entry.useAxisMask = true; entry.useX = x; entry.useY = y; entry.useZ = z;
                            }
                            ik.Links.Add(entry);
                        }
                        ik.CaptureBindPose();
                    }
                }
                Matrix4x4[] bindposes = bones.Select(b => b.worldToLocalMatrix * root.transform.localToWorldMatrix).ToArray();
                int start = 0;
                for (int m = 0; m < doc.Materials.Count; ++m)
                {
                    var material = doc.Materials[m];
                    if (material.IndexCount == 0) continue;
                    int[] sourceVertices = doc.Indices.Skip(start).Take(material.IndexCount).Distinct().ToArray();
                    var lookup = sourceVertices.Select((source, index) => new { source, index }).ToDictionary(x => x.source, x => x.index);
                    int[] triangles = doc.Indices.Skip(start).Take(material.IndexCount).Select(x => lookup[x]).ToArray();
                    start += material.IndexCount;
                    var mesh = new Mesh { name = m.ToString("D3") + "_" + material.Name,
                        indexFormat = sourceVertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                    mesh.vertices = sourceVertices.Select(x => Convert(doc.Vertices[x].Position) * scale).ToArray();
                    mesh.normals = sourceVertices.Select(x => Convert(doc.Vertices[x].Normal).normalized).ToArray();
                    mesh.uv = sourceVertices.Select(x => new Vector2(doc.Vertices[x].Uv.X, 1 - doc.Vertices[x].Uv.Y)).ToArray();
                    mesh.colors = sourceVertices.Select(x => new Color(1, 1, 1, doc.Vertices[x].EdgeScale)).ToArray();
                    mesh.triangles = triangles;
                    mesh.boneWeights = sourceVertices.Select(x => Weight(doc.Vertices[x], bones.Length)).ToArray();
                    mesh.bindposes = bindposes;
                    for (int morph = 0; morph < doc.Morphs.Count; ++morph)
                    {
                        if (doc.Morphs[morph].Type != 0 && doc.Morphs[morph].Type != 1) continue;
                        var deltas = new Vector3[sourceVertices.Length];
                        FillMorph(doc, morph, 1, lookup, deltas, scale, new HashSet<int>());
                        if (deltas.Any(x => x.sqrMagnitude > 1e-15f))
                            mesh.AddBlendShapeFrame(doc.Morphs[morph].Name, 100, deltas, null, null);
                    }
                    mesh.RecalculateTangents(); mesh.RecalculateBounds();
                    AssetDatabase.CreateAsset(mesh, folder + "/Meshes/" + m.ToString("D3") + ".asset");
                    var node = new GameObject(mesh.name); node.transform.SetParent(root.transform, false);
                    var renderer = node.AddComponent<SkinnedMeshRenderer>();
                    renderer.sharedMesh = mesh; renderer.bones = bones; renderer.rootBone = skeleton;
                    renderer.sharedMaterial = report.materials[m].material; renderer.updateWhenOffscreen = true;
                    renderer.shadowCastingMode = (material.Flags & 4) != 0 ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    renderer.receiveShadows = (material.Flags & 8) != 0;
                    renderer.localBounds = new Bounds(mesh.bounds.center, mesh.bounds.size * 3 + Vector3.one);
                }
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                File.WriteAllText(folder + "/实现范围.md",
                    "# 无物理 PMX 姿态参考\n\n包含骨架、BDEF 蒙皮、顶点/组合表情、追加旋转/位移与 Ho IK；不导入刚体或物理。\n" +
                    "SDEF/QDEF 暂按线性蒙皮近似，UV/材质/骨骼 morph 与局部追加规则尚未完整实现；用于姿态交叉对照，不宣称完整 MMD 渲染兼容。\n");
                AssetDatabase.SaveAssets();
                return prefabPath;
            }
            finally { Object.DestroyImmediate(root); }
        }

        static BoneWeight Weight(HoPmxVertex v, int count)
        {
            int[] indices = { v.Bone0, v.Bone1, v.Bone2, v.Bone3 };
            float[] weights = { v.Weight0, v.Weight1, v.Weight2, v.Weight3 };
            for (int i = 0; i < 4; ++i)
                if (indices[i] < 0 || indices[i] >= count || weights[i] < 0) { indices[i] = 0; weights[i] = 0; }
            float sum = weights.Sum();
            if (sum < 1e-7f) { weights[0] = 1; sum = 1; }
            return new BoneWeight { boneIndex0 = indices[0], boneIndex1 = indices[1], boneIndex2 = indices[2], boneIndex3 = indices[3],
                weight0 = weights[0] / sum, weight1 = weights[1] / sum, weight2 = weights[2] / sum, weight3 = weights[3] / sum };
        }
        static void FillMorph(HoPmxDocument doc, int index, float factor, Dictionary<int, int> lookup,
            Vector3[] deltas, float scale, HashSet<int> chain)
        {
            if (index < 0 || index >= doc.Morphs.Count || !chain.Add(index)) return;
            var morph = doc.Morphs[index];
            if (morph.Type == 1)
                foreach (var item in morph.VertexOffsets)
                    if (lookup.TryGetValue(item.VertexIndex, out int vertex)) deltas[vertex] += Convert(item.Offset) * scale * factor;
            if (morph.Type == 0)
                foreach (var item in morph.GroupOffsets) FillMorph(doc, item.MorphIndex, factor * item.Weight, lookup, deltas, scale, chain);
            chain.Remove(index);
        }
        static Vector3 Convert(HoPmxVector3 v) => new Vector3(-v.X, v.Y, -v.Z);
    }
}
