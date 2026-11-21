using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Hollow.HoUnityTools.RigConstraints;
using Hollow.HoUnityTools.RigConstraints.ConstraintIR;

[Serializable] public sealed class DirectPose
{
    public string name; public float[] matrix;
    public Matrix4x4 Matrix { get { var value = new Matrix4x4(); for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) value[r, c] = matrix[r * 4 + c]; return value; } }
}
[Serializable] public sealed class DirectSample { public string name; public DirectPose[] inputs, expected; }
[Serializable] public sealed class DirectFixture { public string[] ownerNames; public DirectPose[] bones; public DirectSample[] samples; }
[Serializable] public sealed class DirectMeasurement { public string backend, sample, bone, timing; public int writeChannels; public float positionError, angleError, scaleError; }
[Serializable] public sealed class DirectResults { public List<string> failures = new List<string>(); public List<DirectMeasurement> measurements = new List<DirectMeasurement>(); }

/// <summary>Numerical oracle is sampled by Blender; Unity never creates its own expected poses.</summary>
public sealed class DirectRuntimeProbe : MonoBehaviour
{
    public GameObject model; public TextAsset fixture, ir; public string outputPath;
    private DirectFixture data; private readonly List<Route> routes = new List<Route>();
    private readonly Dictionary<string, int> writeChannels = new Dictionary<string, int>();
    private readonly DirectResults result = new DirectResults(); private int sample, frame; private bool ready;
    private sealed class Route
    {
        public string backend; public GameObject rig; public Transform outer; public HoAuxRig ho;
        public Dictionary<string, Transform> bones; public Matrix4x4 conversion;
        public readonly Dictionary<string, Matrix4x4> correction = new Dictionary<string, Matrix4x4>();
    }
    private void Start()
    {
        try
        {
            data = JsonUtility.FromJson<DirectFixture>(fixture.text);
            if (data.samples.Length == 0 || data.ownerNames.Length < 4) throw new Exception("Direct fixture has insufficient oracle coverage.");
            if (data.samples.Any(x => x.inputs.Any(input => data.ownerNames.Contains(input.name)))) throw new Exception("Fixture injects constrained owner output as an independent input.");
            var sourceDocument = HoConstraintJson.Parse(ir.text);
            foreach (var semantic in sourceDocument.Get("semantics").Items)
            {
                string owner = semantic.StringAt("ownerBone"), type = semantic.StringAt("semanticType");
                var binding = sourceDocument.Get("bindings").Get("bindingContext").Get("bones").Items.Single(x => x.StringAt("boneName") == owner);
                var pointDomain = binding.Get("positionOnly");
                int previous; writeChannels.TryGetValue(owner, out previous);
                writeChannels[owner] = previous | HoAuxRig.DirectWriteChannels(type, pointDomain != null && pointDomain.Boolean);
            }
            foreach (string backend in new[] { "HoAux", "Standard" })
            {
                var route = new Route { backend = backend, rig = Instantiate(model), outer = new GameObject(backend + " Direct world").transform };
                route.rig.transform.SetParent(route.outer, false);
                foreach (var animator in route.rig.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
                var names = new HashSet<string>(data.bones.Select(x => x.name));
                route.bones = route.rig.GetComponentsInChildren<Transform>(true).Where(t => names.Contains(t.name)).ToDictionary(t => t.name);
                route.conversion = Fit(route);
                foreach (var bone in data.bones) route.correction[bone.name] = bone.Matrix.inverse * route.conversion.inverse * route.bones[bone.name].localToWorldMatrix;
                int count = Import(route); if (count < 4 || Import(route) != count) throw new Exception("Direct semantic import/reimport failed: " + backend + "/" + count);
                route.ho = route.outer.GetComponent<HoAuxRig>();
                if (backend == "HoAux" && route.ho == null) throw new Exception("HoAux host missing.");
                if (route.ho != null)
                {
                    route.ho.Mode = HoAuxRig.UpdateMode.Manual;
                    var bindings = HoConstraintJson.Parse(ir.text).Get("bindings").Get("bindingContext").Get("bones");
                    foreach (var operation in route.ho.DirectOperations.Where(x => x.type == "ARMATURE"))
                    {
                        var declared = bindings.Items.Single(x => x.StringAt("boneName") == operation.owner.name).Get("positionOnly");
                        if (declared == null || declared.Kind != HoConstraintJsonKind.Boolean || declared.Boolean != operation.positionOnly) throw new Exception("Armature write channels differ from source binding domain.");
                    }
                }
                routes.Add(route);
            }
            ApplyInputs(); ready = true;
        }
        catch (Exception error) { Fail(error); }
    }
    private int Import(Route route)
    {
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Hollow.HoUnityTools.Editor.RigConstraints.HoFbxImportProcessingWindow")).First(t => t != null);
        return (int)type.GetMethod("ApplyConstraints", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { ir, route.rig, Enum.Parse(type.GetNestedType("ConstraintMode", BindingFlags.NonPublic), route.backend), route.outer });
    }
    private Matrix4x4 Fit(Route route)
    {
        var anchor = data.bones[0]; var other = data.bones.First(x => Vector3.Distance(x.Matrix.GetColumn(3), anchor.Matrix.GetColumn(3)) > .01f);
        float scale = Vector3.Distance(route.bones[anchor.name].position, route.bones[other.name].position) / Vector3.Distance(anchor.Matrix.GetColumn(3), other.Matrix.GetColumn(3));
        float best = float.PositiveInfinity; var answer = Matrix4x4.identity;
        foreach (var permutation in new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } })
            for (int mask = 0; mask < 8; mask++)
            {
                var matrix = Matrix4x4.zero; matrix[3, 3] = 1;
                for (int axis = 0; axis < 3; axis++) matrix[axis, permutation[axis]] = scale * ((mask & (1 << axis)) == 0 ? 1 : -1);
                Vector3 offset = route.bones[anchor.name].position - matrix.MultiplyPoint3x4(anchor.Matrix.GetColumn(3)); matrix.SetColumn(3, new Vector4(offset.x, offset.y, offset.z, 1));
                float error = data.bones.Max(x => Vector3.Distance(matrix.MultiplyPoint3x4(x.Matrix.GetColumn(3)), route.bones[x.name].position));
                if (error < best) { best = error; answer = matrix; }
            }
        if (best > .0001f) throw new Exception("Direct FBX coordinate fit failed: " + best); return answer;
    }
    private void ApplyInputs()
    {
        foreach (var route in routes)
        {
            route.outer.position = new Vector3(sample * .01f, -.02f, .03f); route.outer.rotation = Quaternion.Euler(sample, 13, -7); route.outer.localScale = Vector3.one * 1.1f;
            foreach (var input in data.samples[sample].inputs)
            {
                var target = route.bones[input.name]; var world = route.outer.localToWorldMatrix * route.conversion * input.Matrix * route.correction[input.name];
                Matrix4x4 parent = target.parent == null ? Matrix4x4.identity : target.parent.localToWorldMatrix;
                var local = parent.inverse * world; target.localPosition = local.GetColumn(3); target.localRotation = local.rotation; target.localScale = Scale(local);
            }
            if (route.ho != null) route.ho.EvaluateNow();
        }
    }
    private void Update()
    {
        if (!ready) return;
        try
        {
            frame++; if (frame == 2) Capture("initial_capture"); if (frame < 7) return;
            foreach (var route in routes) if (route.ho != null) for (int repeat = 0; repeat < 8; repeat++) route.ho.EvaluateNow();
            Capture("stable"); frame = 0; sample++;
            if (sample == data.samples.Length) { ready = false; Finish(); } else ApplyInputs();
        }
        catch (Exception error) { Fail(error); }
    }
    private void Capture(string timing)
    {
        var expected = data.samples[sample].expected.ToDictionary(x => x.name, x => x.Matrix);
        foreach (var route in routes) foreach (string name in data.ownerNames)
        {
            if (route.ho != null && !string.IsNullOrEmpty(route.ho.LastDirectError)) throw new Exception("Unexpected Direct diagnostic: " + route.ho.LastDirectError);
            var want = route.outer.localToWorldMatrix * route.conversion * expected[name] * route.correction[name]; var actual = route.bones[name];
            float p = Vector3.Distance(actual.position, want.GetColumn(3)), a = Angle(actual.rotation, want.rotation), s = Vector3.Distance(Scale(actual.localToWorldMatrix), Scale(want));
            int channels; writeChannels.TryGetValue(name, out channels);
            if (channels == 0) throw new Exception("Direct owner has no declared write channels: " + name);
            result.measurements.Add(new DirectMeasurement { backend = route.backend, sample = data.samples[sample].name, bone = name, timing = timing, writeChannels = channels, positionError = p, angleError = a, scaleError = s });
            if (float.IsNaN(p) || float.IsNaN(a) || float.IsNaN(s) || float.IsInfinity(p) || float.IsInfinity(a) || float.IsInfinity(s) || route.backend == "HoAux" && ((channels & HoAuxRig.DirectPosition) != 0 && p > .0001f || (channels & HoAuxRig.DirectRotation) != 0 && a > .1f || (channels & HoAuxRig.DirectScaleChannel) != 0 && s > .0001f)) throw new Exception("Direct parity " + route.backend + "/" + name + ": " + p + " m / " + a + " deg / " + s + " channels=" + channels);
        }
    }
    private static Vector3 Scale(Matrix4x4 value) { return new Vector3(value.GetColumn(0).magnitude, value.GetColumn(1).magnitude, value.GetColumn(2).magnitude); }
    private static float Angle(Quaternion a, Quaternion b)
    {
        double x = (double)a.w * b.x - (double)a.x * b.w - (double)a.y * b.z + (double)a.z * b.y, y = (double)a.w * b.y + (double)a.x * b.z - (double)a.y * b.w - (double)a.z * b.x;
        double z = (double)a.w * b.z - (double)a.x * b.y + (double)a.y * b.x - (double)a.z * b.w, w = (double)a.w * b.w + (double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z;
        return (float)(2 * Math.Atan2(Math.Sqrt(x * x + y * y + z * z), Math.Abs(w)) * 180 / Math.PI);
    }
    private void Fail(Exception error) { ready = false; result.failures.Add(error.ToString()); Debug.LogException(error); Finish(); }
    private void Finish()
    {
        File.WriteAllText(outputPath, JsonUtility.ToJson(result, true));
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(result.failures.Count == 0 ? 0 : 1);
#endif
    }
}
