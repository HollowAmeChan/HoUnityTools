// Run only against the user's named WTD workspace; no controller or animation edits.
using System;
using System.IO;
using System.Linq;
using System.Text;
using Hollow.HoUnityTools.FaceTracking;
using Hollow.HoUnityTools.Editor.FaceTracking;
using UnityEditor;
using UnityEngine;

public static class HoVtsWtdMigration
{
    private const string PendingPlay = "HoVtsWtdMigration.Play";
    [InitializeOnLoadMethod]
    private static void Initialize() { EditorApplication.update += PlayReady; }
    public static void RunPlayBatch()
    {
        SessionState.SetBool(PendingPlay, true);
        EditorApplication.isPlaying = true;
    }
    private static void PlayReady()
    {
        if (!SessionState.GetBool(PendingPlay, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        SessionState.SetBool(PendingPlay, false);
        GameObject instance = null;
        var report = new StringBuilder();
        try
        {
            instance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));
            foreach (var behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour != null && !(behaviour is HoVtsParameterReceiver) && !(behaviour is HoVtsController)) behaviour.enabled = false;
            foreach (var animator in instance.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            var receiver = instance.GetComponentInChildren<HoVtsParameterReceiver>(true);
            var slot = instance.GetComponentInChildren<HoVtsController>(true);
            if (receiver == null || slot == null || slot.ParameterReceiver != receiver) throw new Exception("Migrated component references missing");
            slot.AutoRun = false;
            var raw = new System.Collections.Generic.Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var row in receiver.Middleware.inputs)
                if (row != null && HoFaceExpression.TryParse(row.expression, out var expression, out _))
                {
                    var keys = new System.Collections.Generic.List<string>(); expression.CollectVariables(keys);
                    foreach (var key in keys) raw[key] = 0;
                }
            raw["FaceFound"] = 1;
            receiver.ApplyRawFrame(raw, true, Time.realtimeSinceStartupAsDouble, "WTDValidation");
            if (!slot.EvaluateNow(0)) throw new Exception(slot.Status);
            var meshes = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(mesh => mesh.sharedMesh != null).ToArray();
            var baseline = meshes.Select(mesh => Enumerable.Range(0, mesh.sharedMesh.blendShapeCount).Select(mesh.GetBlendShapeWeight).ToArray()).ToArray();
            int responsive = 0;
            foreach (string channel in HoFaceTrackingChannels.Names)
            {
                var input = receiver.Middleware.inputs.LastOrDefault(row => row != null && row.parameter == channel);
                if (input == null || !raw.ContainsKey(input.expression)) throw new Exception("Missing direct VTS input mapping: " + channel);
                raw[input.expression] = 1;
                receiver.ApplyRawFrame(raw, true, Time.realtimeSinceStartupAsDouble, "WTDValidation");
                if (!slot.EvaluateNow(0)) throw new Exception(slot.Status);
                float maximum = 0;
                for (int m = 0; m < meshes.Length; m++) for (int i = 0; i < baseline[m].Length; i++)
                    maximum = Mathf.Max(maximum, Mathf.Abs(meshes[m].GetBlendShapeWeight(i) - baseline[m][i]));
                if (maximum > .01f) responsive++;
                report.AppendLine(channel + ": max shape delta=" + maximum.ToString("G7"));
                raw[input.expression] = 0;
                receiver.ApplyRawFrame(raw, true, Time.realtimeSinceStartupAsDouble, "WTDValidation");
                slot.EvaluateNow(0);
            }
            if (responsive == 0) throw new Exception("No face channels changed WTD mesh output");
            receiver.ClearFrame();
            if (slot.EvaluateNow(0) || slot.IsReady) throw new Exception("Released input retained controller graph");
            report.AppendLine("Responsive channels: " + responsive + "/52; bound properties=" + slot.BindingCount);
            report.AppendLine("HO_VTS_WTD_RUNTIME PASS");
            File.WriteAllText("Logs/vts-wtd-runtime-result.txt", report.ToString());
            Debug.Log(report.ToString());
            UnityEngine.Object.DestroyImmediate(instance); instance = null;
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            report.AppendLine("HO_VTS_WTD_RUNTIME FAIL\n" + error);
            File.WriteAllText("Logs/vts-wtd-runtime-result.txt", report.ToString());
            if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            Debug.LogException(error); EditorApplication.Exit(1);
        }
    }
    private const string Prefab = "Assets/Hollow/WTD/WTD-BUILD.prefab";
    private const string Controller = "Assets/Hollow/WTD/FT/CTR_Face_ARKit.controller";
    private const string Rules = "Assets/Hollow/WTD/FT/ho-iPhoneVTS.arkit.hoface.json";

    public static void PreserveOfficialPoseBatch()
    {
        GameObject root = null;
        try
        {
            root = PrefabUtility.LoadPrefabContents(Prefab);
            var pose = root.GetComponentInChildren<HoVtsLookAt>(true);
            if (pose == null) throw new Exception("Migrated WTD pose component missing");
            pose.AutoRun = false;
            pose.enabled = false;
            PrefabUtility.SaveAsPrefabAsset(root, Prefab);
            Debug.Log("HO_VTS_WTD_POSE_POLICY PASS: AutoRun=false and component disabled");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        finally { if (root != null) PrefabUtility.UnloadPrefabContents(root); }
    }

    public static void RunBatch()
    {
        GameObject root = null;
        var report = new StringBuilder();
        string directory = Path.GetFullPath("Logs/HoVtsMigration/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(directory);
        try
        {
            if (Application.isPlaying) throw new InvalidOperationException("Migration requires edit mode");
            if (!File.Exists(Prefab) || !File.Exists(Controller) || !File.Exists(Rules)) throw new InvalidOperationException("WTD source assets missing");
            byte[] controllerBefore = File.ReadAllBytes(Controller), rulesBefore = File.ReadAllBytes(Rules), metaBefore = File.ReadAllBytes(Prefab + ".meta");
            File.Copy(Prefab, Path.Combine(directory, "WTD-BUILD.prefab"));
            File.Copy(Prefab + ".meta", Path.Combine(directory, "WTD-BUILD.prefab.meta"));
            report.AppendLine("Backup: " + directory);
            if (!HoFaceProfile.TryParse(File.ReadAllText(Rules), out var rules, out string error)) throw new InvalidOperationException(error);
            var asset = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Controller);
            if (asset == null) throw new InvalidOperationException("Controller cannot load");
            root = PrefabUtility.LoadPrefabContents(Prefab);
            if (root.GetComponentsInChildren<HoVtsParameterReceiver>(true).Length != 0
                || root.GetComponentsInChildren<HoVtsController>(true).Length != 0
                || root.GetComponentsInChildren<HoVtsLookAt>(true).Length != 0)
                throw new InvalidOperationException("WTD already has VTS components; inspect before remigration");
            var originalAnimator = root.GetComponent<Animator>();
            bool originalEnabled = originalAnimator != null && originalAnimator.enabled;
            var originalController = originalAnimator != null ? originalAnimator.runtimeAnimatorController : null;
            int missingBefore = root.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            if (missingBefore != 0) throw new InvalidOperationException("Existing missing scripts: " + missingBefore);
            var host = new GameObject("HoVTS");
            host.transform.SetParent(root.transform, false);
            var receiver = host.AddComponent<HoVtsParameterReceiver>();
            receiver.SetMiddleware(rules);
            var slot = host.AddComponent<HoVtsController>();
            slot.ParameterReceiver = receiver;
            slot.Controller = asset;
            slot.OutputRoot = root.transform;
            HoVtsControllerBindings.Prepare(slot); // Fails before saving if ANY authored path is invalid.
            report.AppendLine("Validated animation properties: " + slot.BindingCount);
            report.AppendLine("Controller clips: " + asset.animationClips.Length);
            report.AppendLine("Rules: " + rules.inputs.Count + " inputs / " + rules.outputs.Count + " outputs");
            var pose = host.AddComponent<HoVtsLookAt>();
            pose.ParameterReceiver = receiver;
            pose.ReferenceFrame = root.transform;
            if (originalAnimator != null && originalAnimator.isHuman)
            {
                pose.Head = originalAnimator.GetBoneTransform(HumanBodyBones.Head);
                pose.LeftEye = originalAnimator.GetBoneTransform(HumanBodyBones.LeftEye);
                pose.RightEye = originalAnimator.GetBoneTransform(HumanBodyBones.RightEye);
            }
            pose.enabled = false; // Official Warudo posture chain remains the current writer.
            pose.AutoRun = false; // UMod can restore a disabled Behaviour as enabled.
            report.AppendLine("Optional pose disabled. Head=" + (pose.Head != null ? pose.Head.name : "unset")
                + ", LeftEye=" + (pose.LeftEye != null ? pose.LeftEye.name : "unset") + ", RightEye=" + (pose.RightEye != null ? pose.RightEye.name : "unset"));
            if (originalAnimator != null && (originalAnimator.enabled != originalEnabled || originalAnimator.runtimeAnimatorController != originalController))
                throw new InvalidOperationException("Original Animator was modified");
            if (PrefabUtility.SaveAsPrefabAsset(root, Prefab) == null) throw new InvalidOperationException("Prefab save failed");
            PrefabUtility.UnloadPrefabContents(root); root = null;
            root = PrefabUtility.LoadPrefabContents(Prefab);
            var savedReceiver = root.GetComponentInChildren<HoVtsParameterReceiver>(true);
            var savedSlot = root.GetComponentInChildren<HoVtsController>(true);
            var savedPose = root.GetComponentInChildren<HoVtsLookAt>(true);
            if (savedSlot == null || savedSlot.ParameterReceiver != savedReceiver || savedSlot.OutputRoot != root.transform
                || savedSlot.Controller != asset || savedSlot.BindingCount == 0 || savedPose == null || savedPose.enabled
                || savedReceiver.Middleware.outputs.Count != rules.outputs.Count) throw new InvalidOperationException("Saved component references invalid");
            HoVtsControllerBindings.Prepare(savedSlot);
            if (!controllerBefore.SequenceEqual(File.ReadAllBytes(Controller)) || !rulesBefore.SequenceEqual(File.ReadAllBytes(Rules))
                || !metaBefore.SequenceEqual(File.ReadAllBytes(Prefab + ".meta"))) throw new InvalidOperationException("Source controller, rule file or prefab GUID changed");
            report.AppendLine("Saved prefab reload passed; role rule/controller references intact; source controller/rules and prefab GUID unchanged.");
            report.AppendLine("HO_VTS_WTD_MIGRATION PASS");
            File.WriteAllText(Path.Combine(directory, "result.txt"), report.ToString());
            Debug.Log(report.ToString());
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            report.AppendLine("HO_VTS_WTD_MIGRATION FAIL\n" + error);
            File.WriteAllText(Path.Combine(directory, "result.txt"), report.ToString());
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
        finally { if (root != null) PrefabUtility.UnloadPrefabContents(root); }
    }
}
