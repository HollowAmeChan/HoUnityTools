using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hollow.HoUnityTools.Constraints;
using Hollow.HoUnityTools.Animations;
using UnityEngine;

/// <summary>Measures the saved scene's actual preview component and final PlayerLoop outputs.</summary>
[DefaultExecutionOrder(32002)]
public sealed class HoBsSceneParityPlayer : MonoBehaviour
{
    [Serializable] public sealed class Rotation { public float x, y, z, w; }
    [Serializable] public sealed class Frame { public int frame; public float time; public Rotation[] rotations; public float[] weights; }
    [Serializable] public sealed class Output { public string mesh, shape; }
    [Serializable] public sealed class Samples { public string[] bones; public Output[] outputs; public Frame[] frames; }
    [Serializable] public sealed class Report
    {
        public string unity, clip;
        public int frameCount, outputCount, nonemptyOutputs;
        public int savedMchOutputs, savedMissingBones, measurementMchOutputs;
        public bool bindingsRefreshed;
        public float maxSavedBindError, maxSidecarPoseError, maxSidecarRadiusError;
        public float maxReplayWeightError, maxAnimationWeightError, maxMchSourceAngle, maxSourceAnimationAngle;
        public List<Frame> frames = new List<Frame>();
    }
    public HoBsCorrective corrective;
    public HoAnimationPreviewer preview;
    public TextAsset samples;
    public Transform[] sourceBones;
    public Quaternion[] sourceBinds;
    public string initialReport;
    private Samples data;
    private Report report;
    private HoBsCorrective.Rule[] rules;
    private int next;
    private bool pending;
    private static Quaternion Q(Rotation q) => new Quaternion(q.x, -q.y, -q.z, q.w);

    // Quaternion.Angle rounds small float-dot differences to zero; retain
    // sub-degree residuals when auditing the two retargeters.
    public static float Angle(Quaternion a, Quaternion b)
    {
        double lengthA = Math.Sqrt((double)a.x*a.x + (double)a.y*a.y + (double)a.z*a.z + (double)a.w*a.w);
        double lengthB = Math.Sqrt((double)b.x*b.x + (double)b.y*b.y + (double)b.z*b.z + (double)b.w*b.w);
        double dot = Math.Min(1.0, Math.Abs(((double)a.x*b.x + (double)a.y*b.y + (double)a.z*b.z + (double)a.w*b.w) / (lengthA * lengthB)));
        return (float)(2.0 * Math.Atan2(Math.Sqrt(Math.Max(0.0, 1.0-dot*dot)), dot) * 180.0 / Math.PI);
    }

    private void Start()
    {
        try
        {
            data = JsonUtility.FromJson<Samples>(samples.text);
            report = JsonUtility.FromJson<Report>(initialReport);
            rules = data.outputs.Select(o => corrective.Rules.Single(r => r.target.name == o.mesh && r.shapeName == o.shape)).ToArray();
            preview.Pause();
        }
        catch (Exception exception) { Fail(exception); }
    }
    private void Update()
    {
        if (data == null) return;
        try
        {
            if (pending) Record();
            if (next == data.frames.Length) { Finish(); return; }
            preview.SetTime(data.frames[next].time, false);
            pending = true;
        }
        catch (Exception exception) { Fail(exception); }
    }
    private void Record()
    {
        var expected = data.frames[next];
        var measured = new Frame { frame = expected.frame, time = expected.time,
            weights = new float[rules.Length], rotations = new Rotation[data.bones.Length] };
        for (int i = 0; i < rules.Length; i++)
        {
            var rule = rules[i];
            if (!string.IsNullOrEmpty(rule.lastError)) throw new Exception(rule.lastError);
            measured.weights[i] = rule.target.GetBlendShapeWeight(rule.target.sharedMesh.GetBlendShapeIndex(rule.shapeName)) * .01f;
            report.maxAnimationWeightError = Mathf.Max(report.maxAnimationWeightError, Mathf.Abs(measured.weights[i] - expected.weights[i]));
        }
        for (int i = 0; i < data.bones.Length; i++)
        {
            var rule = rules.First(r => r.sourceBone == data.bones[i]);
            Quaternion relative = HoBsCorrective.ReadLocalRotation(rule.bone.localRotation, rule.referenceRotation);
            Quaternion source = Quaternion.Inverse(sourceBinds[i]) * sourceBones[i].localRotation;
            if (rule.measurementIsMch)
                source = Quaternion.Inverse(rule.referenceRotation) * source * rule.referenceRotation;
            measured.rotations[i] = new Rotation { x = relative.x, y = -relative.y, z = -relative.z, w = relative.w };
            report.maxMchSourceAngle = Mathf.Max(report.maxMchSourceAngle, Angle(relative, source));
            report.maxSourceAnimationAngle = Mathf.Max(report.maxSourceAnimationAngle, Angle(source, Q(expected.rotations[i])));
        }
        report.frames.Add(measured);
        if (Environment.GetCommandLineArgs().Contains("-hoBsRender") && new[] { 0, 324, 684 }.Contains(expected.frame))
            Capture(expected.frame);
        next++;
        pending = false;
    }
    private void Capture(int frame)
    {
        Directory.CreateDirectory("Renders");
        var targets = corrective.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.enabled).ToArray();
        var materials = targets.Select(r => r.sharedMaterials).ToArray();
        var material = new Material(Shader.Find("Standard"));
        material.color = new Color(.48f, .46f, .42f);
        material.SetFloat("_Glossiness", .28f);
        var sceneLights = UnityEngine.Object.FindObjectsOfType<Light>();
        var lightStates = sceneLights.Select(l => l.enabled).ToArray();
        foreach (var existing in sceneLights) existing.enabled = false;
        for (int i = 0; i < targets.Length; i++) targets[i].sharedMaterials = materials[i].Select(m => material).ToArray();
        var cameraObject = new GameObject("Parity camera");
        var camera = cameraObject.AddComponent<Camera>();
        var animator = preview.TargetAnimator;
        Vector3 head = animator.GetBoneTransform(HumanBodyBones.Head).position;
        Vector3 hips = animator.GetBoneTransform(HumanBodyBones.Hips).position;
        Vector3 center = Vector3.Lerp(hips, head, .55f);
        camera.transform.position = center + new Vector3(.12f, .08f, 3f);
        camera.transform.LookAt(center);
        camera.orthographic = true;
        camera.orthographicSize = Mathf.Max(.55f, Vector3.Distance(head, hips) * .95f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.16f, .18f, .21f);
        camera.nearClipPlane = .01f;
        var lightObject = new GameObject("Parity light");
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = .85f;
        light.transform.rotation = Quaternion.Euler(35, -145, 0);
        var texture = new RenderTexture(900, 1000, 24);
        camera.targetTexture = texture;
        var before = RenderTexture.active;
        var pixels = new Texture2D(900, 1000, TextureFormat.RGB24, false);
        var correct = rules.Select(r => r.target.GetBlendShapeWeight(r.target.sharedMesh.GetBlendShapeIndex(r.shapeName))).ToArray();
        for (int variant = 0; variant < 2; variant++)
        {
            for (int i = 0; i < rules.Length; i++)
            {
                var rule = rules[i];
                float value = variant == 0 ? HoBsCorrective.EvaluatePose(rule.bone.localRotation * Quaternion.Inverse(rule.referenceRotation),
                    rule.pose, rule.radius, rule.weight) * 100 : correct[i];
                rule.target.SetBlendShapeWeight(rule.target.sharedMesh.GetBlendShapeIndex(rule.shapeName), value);
            }
            // Freeze the evaluated skin for this render. Two Camera.Render calls
            // in one Update otherwise reuse Unity's previous skinning buffer.
            var snapshots = new List<GameObject>();
            var bakedMeshes = new List<Mesh>();
            foreach (var renderer in targets)
            {
                var baked = new Mesh();
                renderer.BakeMesh(baked);
                var snapshot = new GameObject("Evaluated skin snapshot");
                snapshot.transform.SetParent(renderer.transform, false);
                snapshot.AddComponent<MeshFilter>().sharedMesh = baked;
                snapshot.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
                renderer.enabled = false;
                snapshots.Add(snapshot); bakedMeshes.Add(baked);
            }
            camera.Render();
            RenderTexture.active = texture;
            pixels.ReadPixels(new Rect(0, 0, 900, 1000), 0, 0);
            pixels.Apply();
            File.WriteAllBytes($"Renders/unity_{(variant == 0 ? "before" : "fixed")}_{frame:0000}.png", pixels.EncodeToPNG());
            foreach (var snapshot in snapshots) UnityEngine.Object.DestroyImmediate(snapshot);
            foreach (var mesh in bakedMeshes) UnityEngine.Object.DestroyImmediate(mesh);
            foreach (var renderer in targets) renderer.enabled = true;
        }
        RenderTexture.active = before;
        for (int i = 0; i < targets.Length; i++) targets[i].sharedMaterials = materials[i];
        for (int i = 0; i < sceneLights.Length; i++) sceneLights[i].enabled = lightStates[i];
        Destroy(cameraObject); Destroy(lightObject); Destroy(material); Destroy(pixels);
        texture.Release(); Destroy(texture);
    }
    private void Finish()
    {
        // Repeat exact Blender inputs on this scene's saved rules. This isolates
        // the corrective convention from the two Humanoid retargeters.
        foreach (var sample in data.frames)
        {
            foreach (var group in rules.GroupBy(r => r.bone))
            {
                var rule = group.First();
                Quaternion rotation = Q(sample.rotations[Array.IndexOf(data.bones, rule.sourceBone)]);
                rule.bone.localRotation = rule.referenceRotation * rotation;
            }
            corrective.EvaluateNow();
            for (int i = 0; i < rules.Length; i++)
                report.maxReplayWeightError = Mathf.Max(report.maxReplayWeightError, Mathf.Abs(rules[i].lastOutput - sample.weights[i]));
        }
        File.WriteAllText("scene_results.json", JsonUtility.ToJson(report, true));
        if (report.maxReplayWeightError > 1e-5f)
            throw new Exception("Blender/Unity matching-input weight error: " + report.maxReplayWeightError);
        if (report.maxMchSourceAngle > .1f)
            throw new Exception("Measurement/source local rotation error: " + report.maxMchSourceAngle);
        data = null;
        Debug.Log("HO_BS_SCENE_PARITY_DONE");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }
    private void Fail(Exception exception)
    {
        data = null;
        File.WriteAllText("scene_error.txt", exception.ToString());
        Debug.LogException(exception);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(1);
#endif
    }
}
