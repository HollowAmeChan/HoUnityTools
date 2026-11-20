#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Hollow.HoUnityTools.Constraints;
using Hollow.HoUnityTools.Editor.Constraints;
using Hollow.HoUnityTools.Editor.RigConstraints;
using UnityEditor;
using UnityEngine;

/// <summary>Run in a disposable Unity editor project with the v5 Blender fixtures beside this file.</summary>
public static class HoBsCorrectiveChecks
{
    [Serializable] private sealed class Samples { public Sample[] items; }
    [Serializable] private sealed class Sample { public float[] quaternion; public Weights weights; }
    // JsonUtility cannot deserialize dictionaries or '-' in field identifiers.
    [Serializable] private sealed class Weights { public float task1; public float task2; }
    [Serializable] private sealed class Cases { public Case[] items; }
    [Serializable] private sealed class Case { public float[] current; public float[] pose; public float radius; public float weight; public float expected; }
    private static Quaternion Q(float[] q) => new Quaternion(q[0], q[1], q[2], q[3]);

    private static void Check(bool value, string message)
    { if (!value) throw new Exception(message); }
    private static void Close(float actual, float expected, string label, float tolerance = 0.004f)
    { Check(Math.Abs(actual - expected) < tolerance, label + ": " + actual + " != " + expected); }
    private static void Reject(string json, string label)
    {
        try { HoFbxBsCorrectiveImportUtility.Parse(new TextAsset(json)); }
        catch (Exception) { return; }
        throw new Exception("Accepted invalid metadata: " + label);
    }
    private static Transform Child(Transform parent, string name)
    {
        Transform t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }
    private static SkinnedMeshRenderer Renderer(Transform parent, string name, string[] shapes)
    {
        Transform t = Child(parent, name);
        var renderer = t.gameObject.AddComponent<SkinnedMeshRenderer>();
        var mesh = new Mesh { name = name + "Data" };
        mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
        mesh.triangles = new[] { 0, 1, 2 };
        foreach (string shape in shapes)
            mesh.AddBlendShapeFrame(shape, 100, new[] { Vector3.zero, Vector3.right * 0.2f, Vector3.zero }, null, null);
        renderer.sharedMesh = mesh;
        return renderer;
    }

    private static void CheckRealFbxTransforms()
    {
        if (!Directory.Exists(Path.Combine(Application.dataPath, "Editor/FbxFixtures")))
            throw new Exception("Real FBX fixtures missing; run validation with -BlenderPath.");
        foreach (string kind in new[] { "direct", "mch", "hofbx" })
        {
            string prefix = "Assets/Editor/FbxFixtures/" + kind;
            GameObject neutralAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefix + "_neutral.fbx");
            GameObject posedAsset = AssetDatabase.LoadAssetAtPath<GameObject>(
                kind == "hofbx" ? "Assets/Editor/FbxFixtures/mch_pose.fbx" : prefix + "_pose.fbx");
            Check(neutralAsset != null && posedAsset != null, "FBX asset import failed");
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(neutralAsset);
            var metadata = new TextAsset(File.ReadAllText(Path.Combine(Application.dataPath, "Editor/FbxFixtures/" + kind + ".json")));
            Check(HoFbxBsCorrectiveImportUtility.Apply(metadata, instance) == 1, "real FBX output count");
            var component = instance.GetComponent<HoBsCorrective>();
            Check(component != null && component.Rules[0].measurementIsMch == (kind != "direct"), "real measurement mapping");
            component.SetEditorPreview(true);
            component.EvaluateNow();
            Close(component.Rules[0].lastOutput, 0, "real " + kind + " bind neutrality", 1e-6f);
            foreach (Transform posed in posedAsset.GetComponentsInChildren<Transform>(true))
            {
                if (posed == posedAsset.transform) continue;
                var destination = instance.GetComponentsInChildren<Transform>(true).SingleOrDefault(t => t.name == posed.name);
                if (destination != null) destination.localRotation = posed.localRotation;
            }
            component.EvaluateNow();
            Close(component.Rules[0].lastOutput, .7f, "real " + kind + " pose weight", 2e-5f);
            instance.transform.rotation = Quaternion.Euler(-21, 13, 57);
            component.Rules[0].bone.parent.rotation = Quaternion.Euler(33, -40, 17);
            component.EvaluateNow();
            Close(component.Rules[0].lastOutput, .7f, "real " + kind + " parent invariance", 2e-5f);
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    public static void Run()
    {
        try
        {
            string folder = Path.Combine(Application.dataPath, "Editor");
            string json = File.ReadAllText(Path.Combine(folder, "bs_authoring_results.json"));
            var config = HoFbxBsCorrectiveImportUtility.Parse(new TextAsset(json));
            Check(config.version == 5 && config.rules[0].tasks.Length == 2, "v5 tasks");
            var weightedConfig = HoFbxBsCorrectiveImportUtility.Parse(new TextAsset(json));
            weightedConfig.rules[0].tasks[0].enabled = false;
            weightedConfig.rules[0].tasks[0].weight = 0.25f;
            weightedConfig.rules[0].tasks[1].weight = 0.4f;
            var weightedRoundTrip = HoFbxBsCorrectiveImportUtility.Parse(
                new TextAsset(JsonUtility.ToJson(weightedConfig)));
            Check(!weightedRoundTrip.rules[0].tasks[0].enabled &&
                Math.Abs(weightedRoundTrip.rules[0].tasks[0].weight - 0.25f) < 1e-5f &&
                Math.Abs(weightedRoundTrip.rules[0].tasks[1].weight - 0.4f) < 1e-5f,
                "pose enabled/weight metadata");
            var root = new GameObject("BsAuthoringArmature");
            Transform parent = Child(root.transform, "Parent");
            Transform bone = Child(parent, "MCH_UpperArm.L");
            var coat = Renderer(root.transform, "Coat", new[] { "_BSC_pose1", "_BSC_pose2" });
            var trim = Renderer(root.transform, "Trim", new[] { "_BSC_pose1", "_BSC_pose2" });
            var metadata = new TextAsset(json) { name = "BlenderFixture" };
            Check(HoFbxBsCorrectiveImportUtility.TryPreview(metadata, root, out int count, out string error), error);
            Check(count == 4, "preview must count both poses on both rule meshes");
            Check(root.GetComponent<HoBsCorrective>() == null, "preview mutated scene");
            Check(HoFbxBsCorrectiveImportUtility.Apply(metadata, root) == 4, "apply count");
            HoBsCorrective component = root.GetComponent<HoBsCorrective>();
            Check(component != null && component.Rules.Count == 4, "central component");
            Check(component.Rules.Select(r => r.id).Distinct().Count() == 4, "flattened ids");
            Check(component.Rules.All(r => r.bone == bone && r.sourceBone == "UpperArm.L"), "MCH mapping");
            Check(component.Rules.Count(r => r.bsName == "_BSC_pose1" && r.weight == 1) == 2, "first pose weights");
            Check(component.Rules.Count(r => r.bsName == "_BSC_pose2" && r.weight == 1) == 2, "second pose weights");
            component.Rules.Add(new HoBsCorrective.Rule { id = "manual", enabled = false });
            Check(HoFbxBsCorrectiveImportUtility.Apply(metadata, root) == 4, "reimport");
            Check(component.Rules.Count == 5 && component.Rules[0].id == "manual", "manual row was lost");

            string sampleJson = File.ReadAllText(Path.Combine(folder, "bs_authoring_samples.json"))
                .Replace("\"task-1\"", "\"task1\"").Replace("\"task-2\"", "\"task2\"");
            Samples samples = JsonUtility.FromJson<Samples>("{\"items\":" + sampleJson + "}");
            component.SetEditorPreview(true);
            foreach (Sample sample in samples.items)
            {
                bone.localRotation = new Quaternion(sample.quaternion[0], -sample.quaternion[1], -sample.quaternion[2], sample.quaternion[3]);
                foreach (Quaternion parentRotation in new[] { Quaternion.identity, Quaternion.Euler(31, -47, 19) })
                {
                    parent.localRotation = parentRotation;
                    root.transform.rotation = Quaternion.Euler(-17, 23, 43);
                    component.EvaluateNow();
                    Close(coat.GetBlendShapeWeight(0), sample.weights.task1 * 100, "Coat raise parity");
                    Close(coat.GetBlendShapeWeight(1), sample.weights.task2 * 100, "Coat second pose parity");
                    Close(trim.GetBlendShapeWeight(0), sample.weights.task1 * 100, "Trim first pose parity");
                    Close(trim.GetBlendShapeWeight(1), sample.weights.task2 * 100, "Trim second pose parity");
                }
            }
            // Each pose's copies across meshes share one independent cache.
            var compiled = (System.Collections.IList)typeof(HoBsCorrective).GetField("compiled", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(component);
            var kernels = new System.Collections.Generic.Dictionary<string, object>();
            object boneInput = null;
            foreach (object item in compiled)
            {
                object value = item.GetType().GetField("kernel").GetValue(item);
                if (value == null) continue;
                var source = (HoBsCorrective.Rule)item.GetType().GetField("source").GetValue(item);
                if (kernels.TryGetValue(source.bsName, out object previous))
                    Check(ReferenceEquals(previous, value), "same pose recomputed per mesh");
                else kernels.Add(source.bsName, value);
                object input = value.GetType().GetField("input").GetValue(value);
                if (boneInput == null) boneInput = input;
                else Check(ReferenceEquals(boneInput, input), "bone input not shared");
            }
            Check(kernels.Count == 2, "independent shared kernels not built");
            // A changed radius in the same editor frame must invalidate bindings.
            HoBsCorrective.Rule first = component.Rules.First(r => r.enabled);
            bone.localRotation = new Quaternion(samples.items[3].quaternion[0], -samples.items[3].quaternion[1], -samples.items[3].quaternion[2], samples.items[3].quaternion[3]);
            component.EvaluateNow();
            float before = first.lastOutput;
            first.radius = 1.0f;
            component.EvaluateNow();
            Check(Math.Abs(before - first.lastOutput) > 0.001f, "radius cache remained stale");
            first.radius = 0.1f;
            component.EvaluateNow();
            float narrowPointOutput = first.lastOutput;
            first.radius = 0.6f;
            component.EvaluateNow();
            Check(Math.Abs(narrowPointOutput - first.lastOutput) > 0.001f, "point radius did not affect RBF output");
            // Add/remove/disable another output cannot change this pose curve.
            float unaffected = first.lastOutput;
            var neighbor = component.Rules.First(r => r.enabled && r.bsName != first.bsName);
            neighbor.radius = Mathf.PI;
            component.EvaluateNow();
            Close(first.lastOutput, unaffected, "neighbor radius changed own curve", 1e-6f);
            neighbor.enabled = false;
            component.EvaluateNow();
            Close(first.lastOutput, unaffected, "neighbor enable changed own curve", 1e-6f);
            first.weight = .4f;
            component.EvaluateNow();
            Close(first.lastOutput, unaffected * .4f, "live weight cache", 1e-6f);
            component.SetEditorPreview(false);
            Close(coat.GetBlendShapeWeight(0), 0, "preview restore");
            Close(trim.GetBlendShapeWeight(1), 0, "preview restore");

            // View preview is independent from the 2D weight preview and must not write BS values.
            float viewPreviewBaseline = coat.GetBlendShapeWeight(0);
            HoBsCorrectivePreview.SetViewPreview(component, true);
            Check(HoBsCorrectivePreview.IsViewPreviewing(component), "view preview switch did not enable");
            component.EvaluateNow();
            Close(coat.GetBlendShapeWeight(0), viewPreviewBaseline, "view preview wrote blend shape");
            HoBsCorrectivePreview.SetViewPreview(component, false);
            Check(!HoBsCorrectivePreview.IsViewPreviewing(component), "view preview switch did not disable");

            var cases = JsonUtility.FromJson<Cases>(File.ReadAllText(Path.Combine(folder, "bs_independent_cases.json")));
            foreach (Case test in cases.items)
            {
                Close(HoBsCorrective.EvaluatePose(Q(test.current), Q(test.pose), test.radius, test.weight),
                    test.expected, "independent quaternion parity", 2e-6f);
                Quaternion negative = Q(test.current);
                negative = new Quaternion(-negative.x, -negative.y, -negative.z, -negative.w);
                Close(HoBsCorrective.EvaluatePose(negative, Q(test.pose), test.radius, test.weight),
                    test.expected, "quaternion sign parity", 2e-6f);
            }
            // Run the reported 100-degree dense case through real renderer writes.
            var denseRoot = new GameObject("DenseIndependent");
            Transform denseBone = Child(denseRoot.transform, "Main");
            var denseRenderer = Renderer(denseRoot.transform, "Mesh", new[] { "A", "B", "C" });
            var dense = denseRoot.AddComponent<HoBsCorrective>();
            foreach (var entry in new[] { (45f, "A"), (60f, "B"), (90f, "C") })
                dense.Rules.Add(new HoBsCorrective.Rule { bone = denseBone, target = denseRenderer,
                    shapeName = entry.Item2, pose = Quaternion.AngleAxis(entry.Item1, Vector3.right),
                    radius = 100 * Mathf.Deg2Rad });
            dense.SetEditorPreview(true);
            denseBone.localRotation = Quaternion.AngleAxis(60, Vector3.right);
            dense.EvaluateNow();
            Close(denseRenderer.GetBlendShapeWeight(0), 93.925f, "dense first");
            Check(dense.Rules.Sum(r => r.lastOutput) > 2f, "stacked outputs were normalized");
            for (int degrees = 45; degrees <= 180; degrees++)
            {
                denseBone.localRotation = Quaternion.AngleAxis(degrees, Vector3.right);
                dense.EvaluateNow();
                float expected = HoBsCorrective.EvaluatePose(denseBone.localRotation,
                    dense.Rules[0].pose, dense.Rules[0].radius);
                Close(denseRenderer.GetBlendShapeWeight(0), expected * 100, "dense falloff");
            }
            denseBone.localRotation = Quaternion.identity;
            dense.EvaluateNow();
            Check(dense.Rules.All(r => r.lastOutput == 0f), "neutral was not zero");
            UnityEngine.Object.DestroyImmediate(denseRoot);
            CheckRealFbxTransforms();

            var invalid = HoFbxBsCorrectiveImportUtility.Parse(metadata);
            invalid.rules[0].tasks[1].pointId = invalid.rules[0].tasks[0].pointId;
            Reject(JsonUtility.ToJson(invalid), "duplicate link");
            invalid = HoFbxBsCorrectiveImportUtility.Parse(metadata);
            invalid.rules[0].rbf.basePose[0] = 0.2f;
            Reject(JsonUtility.ToJson(invalid), "editable baseline");
            invalid = HoFbxBsCorrectiveImportUtility.Parse(metadata);
            invalid.rules[0].tasks[0].targets[1].mesh = invalid.rules[0].tasks[0].targets[0].mesh;
            invalid.rules[0].tasks[0].targets[1].shapeKey = invalid.rules[0].tasks[0].targets[0].shapeKey;
            Reject(JsonUtility.ToJson(invalid), "duplicate output");
            invalid = HoFbxBsCorrectiveImportUtility.Parse(metadata);
            invalid.rules[0].tasks[0].weight = 1.1f;
            Reject(JsonUtility.ToJson(invalid), "pose weight out of range");
            invalid = HoFbxBsCorrectiveImportUtility.Parse(metadata);
            invalid.rules[0].rbf.controlPoints[0].radius = 0f;
            Reject(JsonUtility.ToJson(invalid), "non-positive point radius");
            invalid = HoFbxBsCorrectiveImportUtility.Parse(metadata);
            invalid.rules[0].rbf.controlPoints[0].pose = new float[] { 0, 0, 0, 1 };
            Reject(JsonUtility.ToJson(invalid), "point duplicates baseline");
            Reject(json.Replace("\"version\": 5", "\"version\": 4"), "v4 schema");
            // Removing one target must cancel the entire import before replacing any existing data.
            UnityEngine.Object.DestroyImmediate(trim.gameObject);
            Check(!HoFbxBsCorrectiveImportUtility.TryPreview(metadata, root, out _, out _), "missing renderer accepted");
            Check(component.Rules.Count == 5, "failed preview mutated rules");
            UnityEngine.Object.DestroyImmediate(root);
            Debug.Log("BS_CORRECTIVE_UNITY_OK: v5 independent, dense overlap, quaternion parity, parent invariance, live cache, reimport");
            File.WriteAllText(Path.Combine(Application.dataPath, "../results.txt"), "BS_CORRECTIVE_UNITY_OK\n");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }
}
#endif
