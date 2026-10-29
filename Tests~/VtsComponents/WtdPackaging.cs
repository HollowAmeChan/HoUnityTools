// Actual WTD FastBuild validation. Outputs only to Logs/HoVtsWtdPackaging; restores ExportSettings.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class HoVtsWtdPackaging
{
    private const string Pending = "HoVtsWtdPackaging.Pending";
    private const string DirectoryName = "Logs/HoVtsWtdPackaging";
    private const string SettingsPath = "Assets/ExportSettings.asset";
    private const string StatePath = DirectoryName + "/state.json";
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private static Type WindowType => AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType("Hollow.HoUnityTools.Editor.Warudo.HoFastBuildWarudoModWindow")).First(t => t != null);
    [InitializeOnLoadMethod]
    private static void Initialize() { EditorApplication.update += Resume; }

    public static void RunBatch()
    {
        if (Directory.Exists(DirectoryName))
        {
            Debug.LogError("Validation directory already exists; preserve previous evidence before rerunning");
            EditorApplication.Exit(1);
            return;
        }
        object state = null;
        try
        {
            Directory.CreateDirectory(DirectoryName);
            File.Copy(SettingsPath, DirectoryName + "/ExportSettings.backup.asset");
            File.Copy("Assets/Hollow/WTD/WTD-BUILD.prefab", DirectoryName + "/WTD-BUILD.before.prefab");
            var settings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(SettingsPath);
            var serialized = new SerializedObject(settings);
            var profiles = serialized.FindProperty("exportProfiles");
            int selected = -1;
            for (int i = 0; i < profiles.arraySize; i++)
                if (profiles.GetArrayElementAtIndex(i).FindPropertyRelative("modName").stringValue == "WTD") selected = i;
            if (selected < 0) throw new Exception("WTD export profile missing");
            serialized.FindProperty("activeProfile").intValue = selected;
            profiles.GetArrayElementAtIndex(selected).FindPropertyRelative("modExportPath").stringValue = Path.GetFullPath(DirectoryName + "/Artifacts").Replace('\\', '/');
            Directory.CreateDirectory(DirectoryName + "/Artifacts");
            serialized.FindProperty("showOutputDirectory").boolValue = false;
            serialized.FindProperty("clearConsoleOnBuild").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssets();
            var window = ScriptableObject.CreateInstance(WindowType);
            try
            {
                WindowType.GetField("sourcePrefabPath", Instance).SetValue(window, "Assets/Hollow/WTD/WTD-BUILD.prefab");
                WindowType.GetField("exportSettingsPath", Instance).SetValue(window, SettingsPath);
                WindowType.GetField("cleanupTemporaryAssets", Instance).SetValue(window, true);
                WindowType.GetMethod("RefreshDependencyPreview", Instance).Invoke(window, new object[] { false });
                state = WindowType.GetMethod("CreateStagedBuildState", Instance).Invoke(window, null);
                File.WriteAllText(StatePath, JsonUtility.ToJson(state, true));
                Call("ApplyTemporaryPlayerSettings", state);
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
            SessionState.SetBool(Pending, true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("HO_VTS_WTD_PACKAGING waiting for staged compilation");
        }
        catch (Exception error) { Finish(state, null, error); }
    }

    private static void Resume()
    {
        if (SessionState.GetBool(Pending + ".Load", false) && EditorApplication.isPlaying && !EditorApplication.isCompiling)
        {
            SessionState.SetBool(Pending + ".Load", false);
            LoadRuntime();
            return;
        }
        if (!SessionState.GetBool(Pending, false) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        SessionState.SetBool(Pending, false);
        object state = null;
        try
        {
            var stateType = WindowType.GetNestedType("BuildState", BindingFlags.NonPublic);
            state = JsonUtility.FromJson(File.ReadAllText(StatePath), stateType);
            if (EditorUtility.scriptCompilationFailed) throw new Exception("Staged scripts failed compilation");
            Call("PrepareStagedPrefab", state);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>((string)stateType.GetField("temporaryPrefabPath").GetValue(state));
            var behaviours = prefab.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
                Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) == 0, "no staged missing script");
            var receiver = behaviours.Single(b => b != null && b.GetType().FullName == "Hollow.HoUnityTools.FaceTracking.HoVtsParameterReceiver");
            var controller = behaviours.Single(b => b != null && b.GetType().FullName == "Hollow.HoUnityTools.FaceTracking.HoVtsController");
            var pose = behaviours.Single(b => b != null && b.GetType().FullName == "Hollow.HoUnityTools.FaceTracking.HoVtsLookAt");
            var cs = new SerializedObject(controller);
            var rs = new SerializedObject(receiver);
            Require(cs.FindProperty("parameterReceiver").objectReferenceValue == receiver, "staged receiver reference");
            Require(cs.FindProperty("controller").objectReferenceValue != null && cs.FindProperty("bindings").arraySize == 208, "WTD controller with 208 bindings");
            Require(rs.FindProperty("middleware").FindPropertyRelative("inputs").arraySize == 74
                && rs.FindProperty("middleware").FindPropertyRelative("outputs").arraySize == 66, "WTD embedded rule graph");
            var ps = new SerializedObject(pose);
            Require(!pose.enabled && !ps.FindProperty("autoRun").boolValue && ps.FindProperty("parameterReceiver").objectReferenceValue == receiver, "optional pose remains disabled, not automatic and connected");
            Call("TrySyncEditorProjects");
            Call("ValidateTemporaryPlayerSettings", state);
            Call("ValidateStagedScriptsAreInProjects", state);
            Call("ApplyTemporaryExportSettings", state);
            object result = Call("InvokeOfficialBuild", SettingsPath);
            string summary = (string)Call("ValidateBuildResult", result);
            object verification = Call("RunArtifactVerification", state, result);
            Require(verification != null && !(bool)verification.GetType().GetProperty("HasProblems").GetValue(verification), "actual artifact components verified");
            string report = (string)verification.GetType().GetField("report").GetValue(verification);
            Require(report.Contains("HoVtsParameterReceiver") && report.Contains("HoVtsController") && report.Contains("HoVtsLookAt"), "all three role types covered by artifact report");
            Finish(state, summary + "\n" + report, null);
        }
        catch (Exception error) { Finish(state, null, error); }
    }

    public static void RunLoadBatch()
    {
        SessionState.SetBool(Pending + ".Load", true);
        EditorApplication.isPlaying = true;
    }
    private static void LoadRuntime()
    {
        GameObject instance = null;
        UMod.ModHost host = null;
        var delivery = new HoFaceTracking.Core.HoVtsFrameDelivery();
        var report = new StringBuilder();
        try
        {
            string artifact = Path.GetFullPath(DirectoryName + "/Artifacts/WTD.warudo");
            host = UMod.Mod.Load(new Uri(artifact), true);
            Require(host != null && host.IsModLoaded, "actual WTD Mod loaded");
            var prefab = host.SharedAssets.Load<GameObject>("Character");
            Require(prefab != null, "WTD Character resolves from Mod assets");
            instance = UnityEngine.Object.Instantiate(prefab);
            var components = instance.GetComponentsInChildren<MonoBehaviour>(true);
            var receiver = components.Single(b => b != null && b.GetType().FullName == "Hollow.HoUnityTools.FaceTracking.HoVtsParameterReceiver");
            var slot = components.Single(b => b != null && b.GetType().FullName == "Hollow.HoUnityTools.FaceTracking.HoVtsController");
            var pose = components.Single(b => b != null && b.GetType().FullName == "Hollow.HoUnityTools.FaceTracking.HoVtsLookAt");
            Require(receiver.GetType().Assembly.GetName().Name != "HoUnityTools.Runtime", "WTD uses its distributed Mod component types");
            bool poseAutomatic = (bool)pose.GetType().GetProperty("AutoRun").GetValue(pose);
            Require(!poseAutomatic, "bundled optional pose does not run automatically");
            report.AppendLine("UMod pose Behaviour.enabled=" + pose.enabled + "; persisted AutoRun=" + poseAutomatic);
            foreach (var transform in instance.GetComponentsInChildren<Transform>(true))
                Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) == 0, "no loaded missing script");
            foreach (var behaviour in components) if (behaviour != null && behaviour != receiver && behaviour != slot) behaviour.enabled = false;
            foreach (var animator in instance.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            slot.GetType().GetProperty("AutoRun").SetValue(slot, false);
            var evaluate = slot.GetType().GetMethod("EvaluateNow", new[] { typeof(float) });
            object rules = receiver.GetType().GetProperty("Middleware").GetValue(receiver);
            var inputs = ((System.Collections.IEnumerable)rules.GetType().GetField("inputs").GetValue(rules)).Cast<object>().ToArray();
            Func<object, string, string> field = (row, name) => (string)row.GetType().GetField(name).GetValue(row);
            var raw = new System.Collections.Generic.Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var row in inputs) if (row != null && !string.IsNullOrEmpty(field(row, "expression"))) raw[field(row, "expression")] = 0;
            raw["FaceFound"] = 1;
            Require(delivery.Deliver(instance, raw, true, Time.realtimeSinceStartupAsDouble, "WTD-Mod-Validation"), "production bridge delivers into actual WTD Mod");
            Require((bool)evaluate.Invoke(slot, new object[] { 0f }), "bundled WTD controller evaluates");
            var meshes = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(mesh => mesh.sharedMesh != null).ToArray();
            var baseline = meshes.Select(mesh => Enumerable.Range(0, mesh.sharedMesh.blendShapeCount).Select(mesh.GetBlendShapeWeight).ToArray()).ToArray();
            int responsive = 0;
            var names = (string[])receiver.GetType().Assembly.GetType("Hollow.HoUnityTools.FaceTracking.HoFaceTrackingChannels").GetField("Names").GetValue(null);
            foreach (string channel in names)
            {
                var row = inputs.Last(input => input != null && field(input, "parameter") == channel);
                string wire = field(row, "expression");
                raw[wire] = 1;
                Require(delivery.Deliver(instance, raw, true, Time.realtimeSinceStartupAsDouble, "WTD-Mod-Validation"), "channel delivered");
                Require((bool)evaluate.Invoke(slot, new object[] { 0f }), "channel evaluated");
                float delta = 0;
                for (int m = 0; m < meshes.Length; m++) for (int i = 0; i < baseline[m].Length; i++)
                    delta = Mathf.Max(delta, Mathf.Abs(meshes[m].GetBlendShapeWeight(i) - baseline[m][i]));
                if (delta > .01f) responsive++;
                report.AppendLine(channel + ": max shape delta=" + delta.ToString("G7"));
                raw[wire] = 0;
                delivery.Deliver(instance, raw, true, Time.realtimeSinceStartupAsDouble, "WTD-Mod-Validation"); evaluate.Invoke(slot, new object[] { 0f });
            }
            Require(responsive == 52, "52/52 channels drive loaded WTD Mod meshes");
            delivery.Release();
            Require(!(bool)evaluate.Invoke(slot, new object[] { 0f }) && !(bool)slot.GetType().GetProperty("IsReady").GetValue(slot), "input release stops actual WTD Mod graph");
            report.AppendLine("Responsive channels: " + responsive + "/52");
            report.AppendLine("HO_VTS_WTD_MOD_RUNTIME PASS");
            UnityEngine.Object.DestroyImmediate(instance); instance = null;
            host.UnloadMod(); host = null;
            File.WriteAllText(DirectoryName + "/runtime-result.txt", report.ToString());
            Debug.Log(report.ToString()); EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            delivery.Release();
            if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            if (host != null) host.UnloadMod();
            report.AppendLine("HO_VTS_WTD_MOD_RUNTIME FAIL\n" + error);
            File.WriteAllText(DirectoryName + "/runtime-result.txt", report.ToString());
            Debug.LogException(error); EditorApplication.Exit(1);
        }
    }

    private static void Finish(object state, string report, Exception error)
    {
        try
        {
            if (state != null) Call("FinishBuildState", state, true);
            string backup = DirectoryName + "/ExportSettings.backup.asset";
            if (File.Exists(backup))
            {
                File.Copy(backup, SettingsPath, true);
                AssetDatabase.ImportAsset(SettingsPath, ImportAssetOptions.ForceSynchronousImport);
            }
            string prefabBackup = DirectoryName + "/WTD-BUILD.before.prefab";
            if (File.Exists(prefabBackup) && !File.ReadAllBytes(prefabBackup).SequenceEqual(File.ReadAllBytes("Assets/Hollow/WTD/WTD-BUILD.prefab")))
                throw new Exception("FastBuild modified source WTD prefab");
        }
        catch (Exception cleanup) { error = new Exception("Environment restoration failed", cleanup); }
        string text = error == null ? "HO_VTS_WTD_PACKAGING PASS\n" + report : "HO_VTS_WTD_PACKAGING FAIL\n" + error;
        File.WriteAllText(DirectoryName + "/result.txt", text);
        if (error == null) Debug.Log(text); else Debug.LogException(error);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        if (message != "no staged missing script") Debug.Log("HO_VTS_WTD_PACKAGING ok " + message);
    }
    private static object Call(string name, params object[] args)
    {
        try { return WindowType.GetMethod(name, Static).Invoke(null, args); }
        catch (TargetInvocationException error) { throw error.InnerException ?? error; }
    }
}
