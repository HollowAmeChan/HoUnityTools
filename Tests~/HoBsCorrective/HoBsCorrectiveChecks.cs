#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Hollow.HoUnityTools.Constraints;
using Hollow.HoUnityTools.Editor.RigConstraints;
using UnityEditor;
using UnityEngine;

/// <summary>Run in a disposable Unity editor project with the v3 Blender fixtures beside this file.</summary>
public static class HoBsCorrectiveChecks
{
    [Serializable] private sealed class Samples { public Sample[] items; }
    [Serializable] private sealed class Sample { public float[] quaternion; public Weights weights; }
    // JsonUtility cannot deserialize dictionaries or '-' in field identifiers.
    [Serializable] private sealed class Weights { public float task1; public float task2; }

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

    public static void Run()
    {
        try
        {
            string folder = Path.Combine(Application.dataPath, "Editor");
            string json = File.ReadAllText(Path.Combine(folder, "bs_authoring_results.json"));
            var config = HoFbxBsCorrectiveImportUtility.Parse(new TextAsset(json));
            Check(config.version == 3 && config.rules[0].tasks.Length == 2, "v3 tasks");
            var root = new GameObject("BsAuthoringArmature");
            Transform parent = Child(root.transform, "Parent");
            Transform bone = Child(parent, "MCH_UpperArm.L");
            var coat = Renderer(root.transform, "Coat", new[] { "ArmRaise.L", "ArmRaise.Second", "ArmFold.L" });
            var trim = Renderer(root.transform, "Trim", new[] { "StripeRaise", "StripeFold" });
            var metadata = new TextAsset(json) { name = "BlenderFixture" };
            Check(HoFbxBsCorrectiveImportUtility.TryPreview(metadata, root, out int count, out string error), error);
            Check(count == 5, "preview must count task outputs, including two shapes on one mesh");
            Check(root.GetComponent<HoBsCorrective>() == null, "preview mutated scene");
            Check(HoFbxBsCorrectiveImportUtility.Apply(metadata, root) == 5, "apply count");
            HoBsCorrective component = root.GetComponent<HoBsCorrective>();
            Check(component != null && component.Rules.Count == 5, "central component");
            Check(component.Rules.Select(r => r.id).Distinct().Count() == 5, "flattened ids");
            Check(component.Rules.All(r => r.bone == bone && r.sourceBone == "UpperArm.L"), "MCH mapping");
            Check(component.Rules.Count(r => r.rbfPoints[0].weight == 1 && r.rbfPoints[1].weight == 0) == 3, "first task weights");
            Check(component.Rules.Count(r => r.rbfPoints[0].weight == 0 && r.rbfPoints[1].weight == 1) == 2, "second task weights");
            component.Rules.Add(new HoBsCorrective.Rule { id = "manual", enabled = false });
            Check(HoFbxBsCorrectiveImportUtility.Apply(metadata, root) == 5, "reimport");
            Check(component.Rules.Count == 6 && component.Rules[0].id == "manual", "manual row was lost");

            string sampleJson = File.ReadAllText(Path.Combine(folder, "bs_authoring_samples.json"))
                .Replace("\"task-1\"", "\"task1\"").Replace("\"task-2\"", "\"task2\"");
            Samples samples = JsonUtility.FromJson<Samples>("{\"items\":" + sampleJson + "}");
            component.SetEditorPreview(true);
            foreach (Sample sample in samples.items)
            {
                bone.localRotation = new Quaternion(sample.quaternion[0], sample.quaternion[1], sample.quaternion[2], sample.quaternion[3]);
                foreach (Quaternion parentRotation in new[] { Quaternion.identity, Quaternion.Euler(31, -47, 19) })
                {
                    parent.localRotation = parentRotation;
                    root.transform.rotation = Quaternion.Euler(-17, 23, 43);
                    component.EvaluateNow();
                    Close(coat.GetBlendShapeWeight(0), sample.weights.task1 * 100, "Coat raise parity");
                    Close(coat.GetBlendShapeWeight(1), sample.weights.task1 * 100, "Coat second key parity");
                    Close(coat.GetBlendShapeWeight(2), sample.weights.task2 * 100, "Coat fold parity");
                    Close(trim.GetBlendShapeWeight(0), sample.weights.task1 * 100, "Trim raise parity");
                    Close(trim.GetBlendShapeWeight(1), sample.weights.task2 * 100, "Trim fold parity");
                }
            }
            // Equivalent output rules must reuse the same Gaussian sample cache.
            var compiled = (System.Collections.IList)typeof(HoBsCorrective).GetField("compiled", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(component);
            object kernel = null;
            foreach (object item in compiled)
            {
                FieldInfo field = item.GetType().GetField("kernel");
                object value = field.GetValue(item);
                if (value == null) continue;
                if (kernel == null) kernel = value;
                else Check(ReferenceEquals(kernel, value), "same RBF domain recomputed for each output");
            }
            Check(kernel != null, "shared kernel not built");
            // A changed radius in the same editor frame must invalidate bindings.
            HoBsCorrective.Rule first = component.Rules.First(r => r.enabled);
            bone.localRotation = new Quaternion(samples.items[3].quaternion[0], samples.items[3].quaternion[1], samples.items[3].quaternion[2], samples.items[3].quaternion[3]);
            component.EvaluateNow();
            float before = first.lastOutput;
            first.rbfRadius = 1.0f;
            component.EvaluateNow();
            Check(Math.Abs(before - first.lastOutput) > 0.001f, "radius cache remained stale");
            component.SetEditorPreview(false);
            Close(coat.GetBlendShapeWeight(0), 0, "preview restore");
            Close(trim.GetBlendShapeWeight(1), 0, "preview restore");

            var invalid = HoFbxBsCorrectiveImportUtility.Parse(metadata);
            invalid.rules[0].tasks[1].pointId = invalid.rules[0].tasks[0].pointId;
            Reject(JsonUtility.ToJson(invalid), "duplicate link");
            invalid = HoFbxBsCorrectiveImportUtility.Parse(metadata);
            invalid.rules[0].rbf.basePose[0] = 0.2f;
            Reject(JsonUtility.ToJson(invalid), "editable baseline");
            invalid = HoFbxBsCorrectiveImportUtility.Parse(metadata);
            invalid.rules[0].tasks[0].targets[1].shapeKey = invalid.rules[0].tasks[0].targets[0].shapeKey;
            Reject(JsonUtility.ToJson(invalid), "duplicate output");
            invalid = HoFbxBsCorrectiveImportUtility.Parse(metadata);
            invalid.rules[0].rbf.controlPoints[0].pose = new float[] { 0, 0, 0 };
            Reject(JsonUtility.ToJson(invalid), "point duplicates baseline");
            Reject(json.Replace("\"version\": 3", "\"version\": 2"), "v2 schema");
            // Removing one target must cancel the entire import before replacing any existing data.
            UnityEngine.Object.DestroyImmediate(trim.gameObject);
            Check(!HoFbxBsCorrectiveImportUtility.TryPreview(metadata, root, out _, out _), "missing renderer accepted");
            Check(component.Rules.Count == 6, "failed preview mutated rules");
            UnityEngine.Object.DestroyImmediate(root);
            Debug.Log("BS_CORRECTIVE_UNITY_OK: v3 import, five outputs, Blender parity, parent invariance, cache invalidation, reimport, validation");
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
