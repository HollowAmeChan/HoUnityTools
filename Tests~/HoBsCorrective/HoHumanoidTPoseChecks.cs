#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using Hollow.HoUnityTools.Editor.RigConstraints;
using UnityEditor;
using UnityEngine;

public static class HoHumanoidTPoseChecks
{
    public static void CheckFixtures()
    {
        foreach (string kind in new[] { "direct", "mch" })
        {
            string folder = "Assets/TPoseFixtures/";
            string path = folder + kind + "_rest.fbx";
            var metadata = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + kind + ".json");
            HumanDescription description = HoFbxHumanoidImportUtility.BuildDescription(metadata, (ModelImporter)AssetImporter.GetAtPath(path));
            var posed = AssetDatabase.LoadAssetAtPath<GameObject>(folder + kind + "_tpose.fbx")
                .GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name);
            foreach (SkeletonBone bone in description.skeleton.Where(b => posed.ContainsKey(b.name)))
            {
                Transform expected = posed[bone.name];
                if (Vector3.Distance(bone.position, expected.localPosition) > 2e-5f ||
                    Quaternion.Angle(bone.rotation, expected.localRotation) > .05f ||
                    Vector3.Distance(bone.scale, expected.localScale) > 2e-5f)
                    throw new Exception($"{kind}/{bone.name} reference differs from exported posed FBX: " +
                        $"position={Vector3.Distance(bone.position, expected.localPosition)} angle={Quaternion.Angle(bone.rotation, expected.localRotation)} scale={Vector3.Distance(bone.scale, expected.localScale)}");
            }
        }
        File.WriteAllText("tpose_fixture_results.txt", "HUMANOID_TPOSE_FBX_PARITY_OK");
    }

    public static void ApplyAndAudit(TextAsset metadata, string path)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var rest = model.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name,
            t => Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale));
        var meshes = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r => r.sharedMesh).ToArray();
        var binds = meshes.Select(m => m.bindposes).ToArray();
        HoFbxHumanoidImportUtility.Apply(metadata, path);
        importer = (ModelImporter)AssetImporter.GetAtPath(path);
        SkeletonBone[] first = importer.humanDescription.skeleton;
        SkeletonBone[] again = HoFbxHumanoidImportUtility.BuildDescription(metadata, importer).skeleton;
        foreach (SkeletonBone bone in first)
        {
            SkeletonBone other = again.Single(b => b.name == bone.name);
            if (Vector3.Distance(bone.position, other.position) > 1e-6f ||
                Quaternion.Angle(bone.rotation, other.rotation) > .05f || Vector3.Distance(bone.scale, other.scale) > 1e-6f)
                throw new Exception("T-Pose repeat import accumulates changes: " + bone.name);
        }
        model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        foreach (Transform transform in model.GetComponentsInChildren<Transform>(true))
            if (rest[transform.name] != Matrix4x4.TRS(transform.localPosition, transform.localRotation, transform.localScale))
                throw new Exception("Humanoid calibration changed the model bind pose: " + transform.name);
        var afterMeshes = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r => r.sharedMesh).ToArray();
        for (int i = 0; i < meshes.Length; i++)
            if (!binds[i].SequenceEqual(afterMeshes[i].bindposes)) throw new Exception("Humanoid calibration changed mesh bind matrices");

        var malformed = new TextAsset(metadata.text.Replace("BONE_REST_LOCAL", "WORLD"));
        if (HoFbxHumanoidImportUtility.TryBuildHumanBones(malformed, out _, out _, out _))
            throw new Exception("Unsupported reference-pose coordinates accepted");
        UnityEngine.Object.DestroyImmediate(malformed);
        File.WriteAllText("tpose_import_results.txt", "HUMANOID_TPOSE_IMPORT_OK");
    }
}
#endif
