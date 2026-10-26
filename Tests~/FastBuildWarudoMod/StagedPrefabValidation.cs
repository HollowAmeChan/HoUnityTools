using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Two phases separated by a real Unity script compilation/domain reload.
// Keep the report directory outside Assets. Call Begin once, then Finish after compilation.
public static class HoFastBuildStagedPrefabValidation
{
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static Type WindowType => AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType("Hollow.HoUnityTools.Editor.Warudo.HoFastBuildWarudoModWindow"))
        .First(t => t != null);

    public static void Begin(string prefabPath, string reportDirectory)
    {
        Directory.CreateDirectory(reportDirectory);
        var window = ScriptableObject.CreateInstance(WindowType);
        try
        {
            WindowType.GetField("sourcePrefabPath", PrivateInstance).SetValue(window, prefabPath);
            WindowType.GetField("exportSettingsPath", PrivateInstance).SetValue(window, "Assets/ExportSettings.asset");
            WindowType.GetField("cleanupTemporaryAssets", PrivateInstance).SetValue(window, false);
            WindowType.GetMethod("RefreshDependencyPreview", PrivateInstance).Invoke(window, new object[] { false });
            object state = WindowType.GetMethod("CreateStagedBuildState", PrivateInstance).Invoke(window, null);
            File.WriteAllText(Path.Combine(reportDirectory, "state.json"), JsonUtility.ToJson(state, true));
            File.Copy("Assets/ExportSettings.asset", Path.Combine(reportDirectory, "ExportSettings.backup.asset"));
            Call("ApplyTemporaryPlayerSettings", state);
            File.WriteAllText(Path.Combine(reportDirectory, "phase.txt"), "awaiting-compile");
        }
        finally { Object.DestroyImmediate(window); }
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
    }

    public static void Finish(string reportDirectory, bool buildArtifact)
    {
        string reportPath = Path.Combine(reportDirectory, "result.txt");
        Type stateType = WindowType.GetNestedType("BuildState", BindingFlags.NonPublic);
        object state = JsonUtility.FromJson(File.ReadAllText(Path.Combine(reportDirectory, "state.json")), stateType);
        string prefabPath = (string)stateType.GetField("temporaryPrefabPath").GetValue(state);
        var report = new System.Text.StringBuilder();
        File.WriteAllText(Path.Combine(reportDirectory, "phase.txt"), "running");
        try
        {
            Require(!EditorUtility.scriptCompilationFailed, "Staged source closure compiles in Unity");
            Call("PrepareStagedPrefab", state);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Require(prefab != null, "Staged prefab saved");
            Require(!prefab.GetComponentsInChildren<Transform>(true).Any(t =>
                PrefabUtility.IsPartOfPrefabInstance(t.gameObject)), "Saved export copy has no nested prefab connections");
            Require(prefab.GetComponentsInChildren<Transform>(true).Sum(t =>
                GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)) == 0, "No missing staged components");
            MonoBehaviour[] behaviours = prefab.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (string name in new[] { "HoAuxRig", "HoRotationConstraint" })
            {
                MonoBehaviour[] selected = behaviours.Where(b => b != null && b.GetType().Name == name).ToArray();
                Require(selected.Length > 0, name + " remains attached");
                foreach (MonoBehaviour component in selected)
                {
                    MonoScript script = MonoScript.FromMonoBehaviour(component);
                    Require(script != null && AssetDatabase.GetAssetPath(script).StartsWith("Assets/HoFastBuildWarudoModTemp/"), name + " uses staged MonoScript");
                    Require(script.GetClass() == component.GetType(), name + " MonoScript resolves to attached type");
                    Require(component.GetType().Assembly.GetName().Name == "Assembly-CSharp", name + " is compiled in staging assembly");
                }
            }
            Require(!behaviours.Any(b => b != null && b.GetType().Name == "HoBoneRenderer"), "Editor bone visualizers removed");
            Require(!behaviours.Any(b => b != null && b.GetType().Name == "HoCombineArmature"), "Armature authoring baked and removed");
            object expected = Call("CollectExpectedComponents", state);
            int stagedCount = ((IEnumerable)expected).Cast<object>().Count(e => (bool)e.GetType().GetField("stagedRuntimeScript").GetValue(e));
            Require(stagedCount == 3, "All three local runtime mounts recorded for artifact verification");
            report.AppendLine("PASS: Unity compilation, staging, prefab save/reload, and expected component collection");

            if (buildArtifact)
            {
                Call("ValidateTemporaryPlayerSettings", state);
                Call("ValidateStagedScriptsAreInProjects", state);
                Object settings = AssetDatabase.LoadAssetAtPath<Object>("Assets/ExportSettings.asset");
                var serialized = new SerializedObject(settings);
                int profileIndex = serialized.FindProperty("activeProfile").intValue;
                SerializedProperty profile = serialized.FindProperty("exportProfiles").GetArrayElementAtIndex(profileIndex);
                string outputDirectory = Path.Combine(reportDirectory, "output");
                Directory.CreateDirectory(outputDirectory);
                profile.FindPropertyRelative("modExportPath").stringValue = outputDirectory.Replace('\\', '/');
                serialized.FindProperty("showOutputDirectory").boolValue = false;
                serialized.FindProperty("clearConsoleOnBuild").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Call("ApplyTemporaryExportSettings", state);
                object result = Call("InvokeOfficialBuild", "Assets/ExportSettings.asset");
                report.AppendLine(Convert.ToString(Call("ValidateBuildResult", result)));
                object verification = Call("RunArtifactVerification", state, result);
                Require(verification != null, "Artifact verification returned a result");
                report.AppendLine(Convert.ToString(verification.GetType().GetField("report").GetValue(verification)));
                Require(!(bool)verification.GetType().GetProperty("HasProblems").GetValue(verification), "Artifact components verified");
                report.AppendLine("PASS: official UMod build and artifact verification");
            }
        }
        catch (Exception exception)
        {
            report.AppendLine("FAIL: " + exception);
            Debug.LogException(exception);
        }
        finally
        {
            // Restore the exact settings bytes, including output/UI preferences changed by the harness.
            File.Copy(Path.Combine(reportDirectory, "ExportSettings.backup.asset"), "Assets/ExportSettings.asset", true);
            AssetDatabase.ImportAsset("Assets/ExportSettings.asset", ImportAssetOptions.ForceUpdate);
            Call("RestorePlayerSettings", state);
            File.WriteAllText(reportPath, report.ToString());
            File.WriteAllText(Path.Combine(reportDirectory, "phase.txt"), "done");
            // Retained live .cs files would collide with the user's next build. Preserve
            // diagnostics outside Assets as soon as this run ends, including failed runs.
            string stagedRoot = (string)stateType.GetField("temporaryAssetRoot").GetValue(state);
            string absoluteStagedRoot = Path.GetFullPath(stagedRoot);
            string allowedRoot = Path.GetFullPath("Assets/HoFastBuildWarudoModTemp") + Path.DirectorySeparatorChar;
            if (!absoluteStagedRoot.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected staging archive source");
            string archive = Path.Combine(reportDirectory, "staged");
            Directory.Move(absoluteStagedRoot, archive);
            if (File.Exists(absoluteStagedRoot + ".meta")) File.Move(absoluteStagedRoot + ".meta", archive + ".meta");
            AssetDatabase.Refresh();
        }
    }

    private static object Call(string name, params object[] arguments)
    {
        try { return WindowType.GetMethod(name, PrivateStatic).Invoke(null, arguments); }
        catch (TargetInvocationException exception) { throw exception.InnerException ?? exception; }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Debug.Log("[FastBuild staging validation] " + message);
    }
}
