#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Hollow.HoUnityTools.MmdMotion;
using Hollow.HoUnityTools.PlaySpeed;
using Hollow.HoUnityTools.RigConstraints;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Temporary Editor-only file queue. No components or assets are saved to the scene.
[InitializeOnLoad]
public static class HoAuxChestAudit
{
    [Serializable] public sealed class Request
    {
        public string id, output, configurePrefab, sourceArmature;
        public int frames = 120;
        public bool correctAfterPhysics, disableFinalPass, manualRig;
    }
    [Serializable] public sealed class Response { public string id, status, result; }
    [Serializable] public sealed class BonePose { public string name; public Vector3 position; public Quaternion rotation; }
    [Serializable] public sealed class Sample
    {
        public int frame;
        public float leftPhysicsAngle, rightPhysicsAngle, attachmentCorrectionAngle, attachmentCorrectionDistance;
        public float leftChangedByCorrection, rightChangedByCorrection;
        public string errorBefore, errorAfter;
        public BonePose[] beforePhysics, afterPhysics, corrected;
        public bool renderCaptured;
        public float renderAttachmentErrorAngle, renderAttachmentErrorDistance;
        public int postPhysicsRefreshFrame;
        public int directEvaluationCount;
        public float protectedLocalPositionError, protectedLocalRotationError, protectedLocalScaleError, protectedWorldMatrixError;
    }
    struct ProtectedPose { public Vector3 position, scale; public Quaternion rotation; public Matrix4x4 world; }
    [Serializable] public sealed class MeshBinding { public string name; public int attachmentWeightedVertices; public bool active; }
    [Serializable] public sealed class Report
    {
        public string error, rigName, rigMode;
        public int preSimulationCount, postSimulationCount, directOperations;
        public bool finalPassEnabled;
        public MeshBinding[] attachmentMeshes;
        public string[] rigInventory;
        public int protectedTransformCount, protectedMchCount, totalMchCount;
        public string[] mchChildren;
        public List<Sample> samples = new List<Sample>();
    }

    const string SessionKey = "HoAuxChestAudit.Request";
    static string Work => Path.GetFullPath("Library/HoAuxChest");
    static Request request;
    static HoAuxRig rig;
    static Transform rigSpace;
    static HoVmdSequencePlayer sequence;
    static readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
    static Transform[] protectedTransforms;
    static Report report;
    static BonePose[] beforePhysics;
    static FieldInfo preField, postField;
    static Action preCallback, postCallback;
    static double began;
    static int startFrame;
    static float previousCaptureDelta;
    static bool previousBackground;
    static bool active;
    static Camera camera;
    static RenderTexture previousTarget, target;
    static bool previousCameraEnabled;

    static HoAuxChestAudit()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && !string.IsNullOrEmpty(SessionState.GetString(SessionKey, "")))
                EditorApplication.delayCall += Begin;
            if (state == PlayModeStateChange.ExitingPlayMode && active)
                Finish("Play ended before the audit completed.");
            if (state == PlayModeStateChange.EnteredEditMode) SessionState.EraseString(SessionKey);
        };
    }

    static void Poll()
    {
        if (active)
        {
            if (sequence != null) sequence.ExternalTime = Mathf.Max(0, Time.frameCount - startFrame) / 30f;
            EditorApplication.QueuePlayerLoopUpdate();
            if (EditorApplication.timeSinceStartup - began > 120) Finish("Audit timed out.");
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string path = Path.Combine(Work, "request.json");
        if (!File.Exists(path)) return;
        try
        {
            request = JsonUtility.FromJson<Request>(File.ReadAllText(path));
            File.Delete(path);
            if (Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
                throw new InvalidOperationException("Save scene edits before the runtime audit.");
            if (!string.IsNullOrEmpty(request.configurePrefab))
            {
                if (PrefabStageUtility.GetCurrentPrefabStage() != null)
                    throw new InvalidOperationException("Close Prefab Mode before updating the saved rig setting.");
                string asset = request.configurePrefab;
                string backup = Path.Combine(Work, "prefab-before-phase-config.prefab");
                if (!File.Exists(backup)) File.Copy(Path.GetFullPath(asset), backup);
                GameObject root = PrefabUtility.LoadPrefabContents(asset);
                try
                {
                    HoAuxRig selected = root.GetComponentsInChildren<HoAuxRig>(true).Single(x => x.SourceArmature == request.sourceArmature);
                    selected.DirectPhase = HoAuxRig.DirectExecutionPhase.FinalPose;
                    PrefabUtility.SaveAsPrefabAsset(root, asset);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
                WriteResponse("ok", "Configured FinalPose for " + request.sourceArmature);
                return;
            }
            SessionState.SetString(SessionKey, JsonUtility.ToJson(request));
            EditorApplication.isPlaying = true;
        }
        catch (Exception e) { WriteResponse("error", e.ToString()); }
    }

    static void Begin()
    {
        try
        {
            request = JsonUtility.FromJson<Request>(SessionState.GetString(SessionKey, ""));
            began = EditorApplication.timeSinceStartup;
            active = true;
            previousCaptureDelta = Time.captureDeltaTime;
            previousBackground = Application.runInBackground;
            report = new Report();
            HoAuxRig[] rigs = Object.FindObjectsByType<HoAuxRig>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            report.rigInventory = rigs.Select(x => x.name + " | " + x.SourceArmature + " | direct=" +
                x.DirectOperationCount + " | " + string.Join(",", x.DirectOperations.Select(op => op.owner?.name).Distinct())).ToArray();
            rig = rigs.First(x => x.DirectOperations.Any(op => op.ownerPath != null && op.ownerPath.Contains("C1_ChestAddon")));
            if (request.manualRig) rig.Mode = HoAuxRig.UpdateMode.Manual;
            report.rigName = rig.name;
            report.rigMode = rig.Mode.ToString();
            report.directOperations = rig.DirectOperationCount;
            rig.DirectPhase = request.disableFinalPass ? HoAuxRig.DirectExecutionPhase.WithLayers : HoAuxRig.DirectExecutionPhase.FinalPose;
            report.finalPassEnabled = rig.DirectPhase == HoAuxRig.DirectExecutionPhase.FinalPose;
            rigSpace = (Transform)typeof(HoAuxRig).GetField("directSpace", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(rig);
            if (rigSpace == null) throw new InvalidOperationException("Direct rig space not found.");
            bones.Clear();
            foreach (Transform bone in rigSpace.GetComponentsInChildren<Transform>(true))
                if (bone.name.Contains("ChestAddon") || bone.name == "breast_L" || bone.name == "breast_R")
                    if (!bones.ContainsKey(bone.name)) bones.Add(bone.name, bone);
            if (!bones.ContainsKey("CTR_C1_ChestAddon")) throw new InvalidOperationException("Attachment bone not found.");
            Transform[] skeleton = rigSpace.GetComponentsInChildren<Transform>(true);
            Transform[] directOwners = rig.DirectOperations.Select(op => op.owner).Where(x => x != null).Distinct().ToArray();
            protectedTransforms = skeleton.Where(t => !directOwners.Any(owner => t == owner || t.IsChildOf(owner))).ToArray();
            report.protectedTransformCount = protectedTransforms.Length;
            report.protectedMchCount = protectedTransforms.Count(t => t.name.StartsWith("MCH", StringComparison.Ordinal));
            report.totalMchCount = skeleton.Count(t => t.name.StartsWith("MCH", StringComparison.Ordinal));
            report.mchChildren = skeleton.Where(t => t.name.StartsWith("MCH", StringComparison.Ordinal) && t.childCount > 0)
                .Select(t => t.name + ": " + t.childCount).ToArray();
            Transform parts = null;
            for (Transform parent = rigSpace; parent != null && parts == null; parent = parent.parent)
                parts = parent.Find("Parts");
            if (parts != null)
            {
                parts.Find("巫女服")?.gameObject.SetActive(false);
                parts.Find("泳装")?.gameObject.SetActive(true);
            }
            var meshBindings = new List<MeshBinding>();
            if (parts != null)
                foreach (SkinnedMeshRenderer renderer in parts.parent.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    int index = Array.IndexOf(renderer.bones, bones["CTR_C1_ChestAddon"]);
                    if (index < 0 || renderer.sharedMesh == null) continue;
                    int count = renderer.sharedMesh.boneWeights.Count(w =>
                        (w.boneIndex0 == index && w.weight0 > .001f) ||
                        (w.boneIndex1 == index && w.weight1 > .001f) ||
                        (w.boneIndex2 == index && w.weight2 > .001f) ||
                        (w.boneIndex3 == index && w.weight3 > .001f));
                    if (count > 0) meshBindings.Add(new MeshBinding
                    {
                        name = renderer.name, attachmentWeightedVertices = count,
                        active = renderer.enabled && renderer.gameObject.activeInHierarchy
                    });
                }
            report.attachmentMeshes = meshBindings.ToArray();
            sequence = Object.FindObjectsByType<HoVmdSequencePlayer>(FindObjectsSortMode.None)
                .FirstOrDefault(x => x.Retargeter != null);
            if (sequence != null) { sequence.ExternallyDriven = true; sequence.Playing = false; }
            camera = sequence?.CameraPlayer?.TargetCamera;
            if (camera != null)
            {
                previousTarget = camera.targetTexture;
                previousCameraEnabled = camera.enabled;
                target = new RenderTexture(360, 640, 24, RenderTextureFormat.ARGBHalf);
                target.Create();
                camera.targetTexture = target;
                camera.enabled = true;
            }
            RenderPipelineManager.beginCameraRendering += BeforeRendering;
            Time.captureDeltaTime = 1f / 30;
            Application.runInBackground = true;
            HoPlaySpeedControl.SetOverride(rig, 1f, lockPanel: true);
            startFrame = Time.frameCount;
            Type magica = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("MagicaCloth2.MagicaManager")).First(t => t != null);
            preField = magica.GetField("OnPreSimulation", BindingFlags.Public | BindingFlags.Static);
            postField = magica.GetField("OnPostSimulation", BindingFlags.Public | BindingFlags.Static);
            preCallback = Before;
            postCallback = After;
            preField.SetValue(null, Delegate.Combine((Delegate)preField.GetValue(null), preCallback));
            postField.SetValue(null, Delegate.Combine((Delegate)postField.GetValue(null), postCallback));
        }
        catch (Exception e) { Finish(e.ToString()); }
    }

    static BonePose[] Capture() => bones.OrderBy(x => x.Key).Select(x => new BonePose
    {
        name = x.Key, position = rigSpace.InverseTransformPoint(x.Value.position),
        rotation = Quaternion.Inverse(rigSpace.rotation) * x.Value.rotation
    }).ToArray();

    static BonePose Pose(BonePose[] poses, string name) => poses.First(p => p.name == name);
    static float Angle(BonePose[] a, BonePose[] b, string name) => Quaternion.Angle(Pose(a, name).rotation, Pose(b, name).rotation);

    static void Before()
    {
        if (!active) return;
        try
        {
            if (request.manualRig) rig.EvaluateNow();
            report.preSimulationCount++;
            beforePhysics = Capture();
        }
        catch (Exception e) { Finish(e.ToString()); }
    }

    static void After()
    {
        if (!active || beforePhysics == null) return;
        try
        {
            report.postSimulationCount++;
            BonePose[] after = Capture();
            ProtectedPose[] protectedBefore = protectedTransforms.Select(t => new ProtectedPose
            {
                position = t.localPosition, rotation = t.localRotation, scale = t.localScale, world = t.localToWorldMatrix
            }).ToArray();
            string errorBefore = rig.LastDirectError;
            // Prove the direct stack against the same frame's final physics inputs.
            // The next ordinary LateUpdate restores the authored baseline again.
            typeof(HoAuxRig).GetMethod("RestoreDirectOwners", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(rig, null);
            typeof(HoAuxRig).GetMethod("EvaluateDirectOperations", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(rig, null);
            BonePose[] corrected = Capture();
            var sample = new Sample
            {
                frame = Time.frameCount,
                leftPhysicsAngle = Angle(beforePhysics, after, "breast_L"),
                rightPhysicsAngle = Angle(beforePhysics, after, "breast_R"),
                attachmentCorrectionAngle = Angle(after, corrected, "CTR_C1_ChestAddon"),
                attachmentCorrectionDistance = Vector3.Distance(Pose(after, "CTR_C1_ChestAddon").position, Pose(corrected, "CTR_C1_ChestAddon").position),
                leftChangedByCorrection = Angle(after, corrected, "breast_L"),
                rightChangedByCorrection = Angle(after, corrected, "breast_R"),
                errorBefore = errorBefore, errorAfter = rig.LastDirectError,
                beforePhysics = beforePhysics, afterPhysics = after, corrected = corrected
            };
            for (int i = 0; i < protectedTransforms.Length; i++)
            {
                Transform bone = protectedTransforms[i];
                ProtectedPose prior = protectedBefore[i];
                sample.protectedLocalPositionError = Mathf.Max(sample.protectedLocalPositionError, Vector3.Distance(prior.position, bone.localPosition));
                sample.protectedLocalRotationError = Mathf.Max(sample.protectedLocalRotationError, Quaternion.Angle(prior.rotation, bone.localRotation));
                sample.protectedLocalScaleError = Mathf.Max(sample.protectedLocalScaleError, Vector3.Distance(prior.scale, bone.localScale));
                Matrix4x4 world = bone.localToWorldMatrix;
                for (int j = 0; j < 16; j++)
                    sample.protectedWorldMatrixError = Mathf.Max(sample.protectedWorldMatrixError, Mathf.Abs(prior.world[j] - world[j]));
            }
            report.samples.Add(sample);
            if (!request.correctAfterPhysics)
                foreach (BonePose pose in after)
                    if (pose.name.Contains("ChestAddon"))
                    {
                        Transform bone = bones[pose.name];
                        bone.SetPositionAndRotation(rigSpace.TransformPoint(pose.position), rigSpace.rotation * pose.rotation);
                    }
            if (report.samples.Count >= request.frames) EditorApplication.delayCall += () => Finish(null);
        }
        catch (Exception e) { Finish(e.ToString()); }
    }

    static void BeforeRendering(ScriptableRenderContext context, Camera renderingCamera)
    {
        if (!active || renderingCamera != camera || report.samples.Count == 0) return;
        Sample sample = report.samples[report.samples.Count - 1];
        if (sample.frame != Time.frameCount || sample.renderCaptured) return;
        BonePose[] actual = Capture();
        sample.renderCaptured = true;
        sample.renderAttachmentErrorAngle = Angle(actual, sample.corrected, "CTR_C1_ChestAddon");
        sample.renderAttachmentErrorDistance = Vector3.Distance(
            Pose(actual, "CTR_C1_ChestAddon").position, Pose(sample.corrected, "CTR_C1_ChestAddon").position);
        sample.postPhysicsRefreshFrame = rig.LastDirectEvaluationFrame;
        sample.directEvaluationCount = rig.DirectEvaluationsThisFrame;
    }

    static void Finish(string error)
    {
        if (!active) return;
        active = false;
        RenderPipelineManager.beginCameraRendering -= BeforeRendering;
        if (preField != null) preField.SetValue(null, Delegate.Remove((Delegate)preField.GetValue(null), preCallback));
        if (postField != null) postField.SetValue(null, Delegate.Remove((Delegate)postField.GetValue(null), postCallback));
        Time.captureDeltaTime = previousCaptureDelta;
        Application.runInBackground = previousBackground;
        HoPlaySpeedControl.Release(rig);
        if (camera != null) { camera.targetTexture = previousTarget; camera.enabled = previousCameraEnabled; }
        if (target != null) { target.Release(); Object.Destroy(target); target = null; }
        if (report == null) report = new Report();
        report.error = error;
        string output = Path.GetFullPath(request.output);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output, JsonUtility.ToJson(report, true));
        WriteResponse(error == null ? "ok" : "error", error ?? output);
        EditorApplication.isPlaying = false;
    }

    static void WriteResponse(string status, string result)
    {
        Directory.CreateDirectory(Work);
        File.WriteAllText(Path.Combine(Work, "result.json"), JsonUtility.ToJson(new Response { id = request?.id, status = status, result = result }, true));
    }
}
#endif
