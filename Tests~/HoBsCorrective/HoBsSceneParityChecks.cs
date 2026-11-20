#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Hollow.HoUnityTools.Constraints;
using Hollow.HoUnityTools.Animations;
using Hollow.HoUnityTools.Editor.RigConstraints;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class HoBsSceneParityChecks
{
    public const string Folder = "Assets/00动画/角色/羽啾/";
    public static void Run()
    {
        try
        {
            if (Environment.GetCommandLineArgs().Contains("-hoBsImportHumanoid"))
            {
                if (Directory.Exists("Assets/TPoseFixtures")) HoHumanoidTPoseChecks.CheckFixtures();
                HoHumanoidTPoseChecks.ApplyAndAudit(AssetDatabase.LoadAssetAtPath<TextAsset>(Folder + "HoFBX/羽啾_humanoid.json"), Folder + "羽啾.fbx");
            }
            EditorSceneManager.OpenScene(Folder + "羽啾.unity");
            var corrective = UnityEngine.Object.FindObjectOfType<HoBsCorrective>();
            var preview = UnityEngine.Object.FindObjectOfType<HoAnimationPreviewer>();
            if (corrective == null || preview == null || preview.Clip == null)
                throw new Exception("Saved scene has missing corrective / animation / clip references");
            var samples = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/BlenderSamples.json");
            var data = JsonUtility.FromJson<HoBsSceneParityPlayer.Samples>(samples.text);
            var report = new HoBsSceneParityPlayer.Report { unity = Application.unityVersion,
                clip = preview.Clip.name, frameCount = data.frames.Length,
                savedMchOutputs = corrective.Rules.Count(r => r.measurementIsMch),
                savedMissingBones = corrective.Rules.Count(r => r.bone == null) };
            foreach (var rule in corrective.Rules.Where(r => r.bone != null))
            {
                var original = PrefabUtility.GetCorrespondingObjectFromOriginalSource(rule.bone);
                report.maxSavedBindError = Mathf.Max(report.maxSavedBindError,
                    Quaternion.Angle(rule.referenceRotation, original.localRotation));
            }
            var metadata = AssetDatabase.LoadAssetAtPath<TextAsset>(Folder + "HoFBX/羽啾_bs_corrective.json");
            if (Environment.GetCommandLineArgs().Contains("-hoBsRefreshImport"))
            {
                // A changed FBX may invalidate saved scene references. Rebuild
                // imported rows in this disposable scene, just as HoFBX does.
                HoFbxBsCorrectiveImportUtility.Apply(metadata, corrective.gameObject);
                report.bindingsRefreshed = true;
            }
            var rules = data.outputs.Select(o => corrective.Rules.Single(r => r.target.name == o.mesh && r.shapeName == o.shape)).ToArray();
            report.outputCount = rules.Length;
            report.measurementMchOutputs = rules.Count(r => r.measurementIsMch);
            foreach (var rule in rules)
            {
                if (rule.bone == null) throw new Exception("Missing measurement bone: " + rule.sourceBone);
                int index = rule.target.sharedMesh.GetBlendShapeIndex(rule.shapeName);
                var vertices = new Vector3[rule.target.sharedMesh.vertexCount];
                rule.target.sharedMesh.GetBlendShapeFrameVertices(index, 0, vertices, null, null);
                if (vertices.Any(v => v.sqrMagnitude > 1e-14f)) report.nonemptyOutputs++;
            }
            var transforms = corrective.GetComponentsInChildren<Transform>(true);
            var sources = data.bones.Select(n => transforms.Single(t => t.name == n)).ToArray();
            var binds = sources.Select(t => PrefabUtility.GetCorrespondingObjectFromOriginalSource(t).localRotation).ToArray();
            // Compare saved imported rows against a fresh import of the current sidecar.
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "羽啾.fbx");
            var fresh = (GameObject)PrefabUtility.InstantiatePrefab(model);
            DumpGeometry(fresh);
            DumpAvatarTPose(fresh, corrective);
            HoFbxBsCorrectiveImportUtility.Apply(metadata, fresh);
            var imported = fresh.GetComponent<HoBsCorrective>();
            foreach (var rule in rules)
            {
                var expected = imported.Rules.Single(r => r.target.name == rule.target.name && r.shapeName == rule.shapeName);
                report.maxSidecarPoseError = Mathf.Max(report.maxSidecarPoseError, Quaternion.Angle(rule.pose, expected.pose));
                report.maxSidecarRadiusError = Mathf.Max(report.maxSidecarRadiusError, Mathf.Abs(rule.radius - expected.radius));
            }
            UnityEngine.Object.DestroyImmediate(fresh);
            var probe = corrective.gameObject.AddComponent<HoBsSceneParityPlayer>();
            probe.corrective = corrective;
            probe.preview = preview;
            probe.samples = samples;
            probe.sourceBones = sources;
            probe.sourceBinds = binds;
            probe.initialReport = JsonUtility.ToJson(report);
            EditorApplication.delayCall += () => EditorApplication.isPlaying = true;
        }
        catch (Exception exception)
        {
            File.WriteAllText("scene_error.txt", exception.ToString());
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static void DumpAvatarTPose(GameObject model, HoBsCorrective corrective)
    {
        var reference = UnityEngine.Object.Instantiate(model);
        try
        {
            var setup = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.AvatarSetupTool");
            var method = setup.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == "TransferDescriptionToPose" && m.GetParameters().Length == 2);
            var importer = AssetImporter.GetAtPath(Folder + "羽啾.fbx");
            method.Invoke(null, new object[] { new SerializedObject(importer), reference.transform });
            var bones = reference.GetComponentsInChildren<Transform>(true);
            var values = corrective.Rules.GroupBy(r => r.sourceBone).Select(group => {
                var rule = group.First();
                var source = bones.Single(b => b.name == rule.sourceBone);
                var original = model.GetComponentsInChildren<Transform>(true).Single(b => b.name == rule.sourceBone);
                Quaternion delta = Quaternion.Inverse(original.localRotation) * source.localRotation;
                if (rule.measurementIsMch)
                    delta = Quaternion.Inverse(rule.referenceRotation) * delta * rule.referenceRotation;
                return new TPoseBone { name = rule.sourceBone, rotation = delta };
            }).ToArray();
            var originals = model.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name);
            var skeleton = bones.Where(b => originals.ContainsKey(b.name)).Select(b => {
                var original = originals[b.name];
                return new TPoseTransform { name = b.name,
                    restRotation = Quaternion.Inverse(model.transform.rotation) * original.rotation,
                    rotation = Quaternion.Inverse(reference.transform.rotation) * b.rotation,
                    restPosition = model.transform.InverseTransformPoint(original.position),
                    position = reference.transform.InverseTransformPoint(b.position) };
            }).ToArray();
            File.WriteAllText("avatar_tpose.json", JsonUtility.ToJson(new TPoseReport { bones = values, skeleton = skeleton }, true));
        }
        finally { UnityEngine.Object.DestroyImmediate(reference); }
    }
    [Serializable] private sealed class TPoseReport { public TPoseBone[] bones; public TPoseTransform[] skeleton; }
    [Serializable] private sealed class TPoseBone { public string name; public Quaternion rotation; }
    [Serializable] private sealed class TPoseTransform
    {
        public string name;
        public Quaternion restRotation, rotation;
        public Vector3 restPosition, position;
    }

    [Serializable] private sealed class GeometryReport
    {
        public Bone[] bones;
        public Geometry[] meshes;
    }
    [Serializable] private sealed class Bone { public string name; public Vector3 position; }
    [Serializable] private sealed class Geometry { public string name; public int vertices; public string[] keys; }
    private static void DumpGeometry(GameObject root)
    {
        string folder = "Geometry";
        Directory.CreateDirectory(folder);
        var meshes = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(r => r.name == "外衣" || r.name == "外衣-额外带子").ToArray();
        foreach (var renderer in meshes)
        {
            var mesh = renderer.sharedMesh;
            Matrix4x4 matrix = root.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            using (var writer = new BinaryWriter(File.Create(folder + "/" + renderer.name + ".bin")))
            {
                foreach (var vertex in mesh.vertices)
                {
                    Vector3 p = matrix.MultiplyPoint3x4(vertex);
                    writer.Write(p.x); writer.Write(p.y); writer.Write(p.z);
                }
                var deltas = new Vector3[mesh.vertexCount];
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    mesh.GetBlendShapeFrameVertices(i, 0, deltas, null, null);
                    foreach (var delta in deltas)
                    {
                        Vector3 p = matrix.MultiplyVector(delta);
                        writer.Write(p.x); writer.Write(p.y); writer.Write(p.z);
                    }
                }
            }
        }
        File.WriteAllText(folder + "/geometry.json", JsonUtility.ToJson(new GeometryReport {
            bones = root.GetComponentsInChildren<Transform>(true).Select(t => new Bone {
                name = t.name, position = root.transform.InverseTransformPoint(t.position) }).ToArray(),
            meshes = meshes.Select(r => new Geometry { name = r.name, vertices = r.sharedMesh.vertexCount,
                keys = Enumerable.Range(0, r.sharedMesh.blendShapeCount).Select(r.sharedMesh.GetBlendShapeName).ToArray() }).ToArray()
        }, true));
    }
}
#endif
