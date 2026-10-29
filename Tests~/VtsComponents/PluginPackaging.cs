// Builds the actual input plugin into a validation directory, preserving project export settings.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class HoVtsPluginPackaging
{
    private const string Folder = "Logs/HoVtsPluginPackaging";
    private const string SettingsPath = "Assets/ExportSettings.asset";
    private static Type WindowType => AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType("Hollow.HoUnityTools.Editor.Warudo.HoFastBuildWarudoModWindow")).First(t => t != null);
    public static void RunBatch()
    {
        if (Directory.Exists(Folder)) { Debug.LogError("Plugin validation directory already exists"); EditorApplication.Exit(1); return; }
        Exception failure = null;
        string report = "";
        Directory.CreateDirectory(Folder);
        File.Copy(SettingsPath, Folder + "/ExportSettings.backup.asset");
        try
        {
            var settings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(SettingsPath);
            var serialized = new SerializedObject(settings);
            var profiles = serialized.FindProperty("exportProfiles");
            int selected = -1;
            for (int i = 0; i < profiles.arraySize; i++)
                if (profiles.GetArrayElementAtIndex(i).FindPropertyRelative("modName").stringValue == "HoFaceTracking") selected = i;
            Require(selected >= 0, "plugin profile exists");
            serialized.FindProperty("activeProfile").intValue = selected;
            Directory.CreateDirectory(Folder + "/Artifacts");
            profiles.GetArrayElementAtIndex(selected).FindPropertyRelative("modExportPath").stringValue = Path.GetFullPath(Folder + "/Artifacts").Replace('\\', '/');
            serialized.FindProperty("showOutputDirectory").boolValue = false;
            serialized.FindProperty("clearConsoleOnBuild").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssets();
            WindowType.GetMethod("TrySyncEditorProjects", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            var build = WindowType.GetMethod("InvokeOfficialBuild", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { SettingsPath });
            report = (string)WindowType.GetMethod("ValidateBuildResult", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new[] { build });
            string artifact = Path.GetFullPath(Folder + "/Artifacts/HoFaceTracking.warudo");
            var verifier = WindowType.Assembly.GetType("Hollow.HoUnityTools.Editor.Warudo.HoFastBuildArtifactVerifier");
            var method = verifier.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                .Single(m => m.Name == "Verify" && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == typeof(bool));
            var verification = method.Invoke(null, new object[] { artifact, true });
            Require(!(bool)verification.GetType().GetProperty("HasProblems").GetValue(verification), "plugin artifact container and compiled module verified");
            var types = ((IEnumerable)verification.GetType().GetField("compiledTypes").GetValue(verification)).Cast<string>().ToArray();
            foreach (string name in new[] { "HoFaceTracking.PluginMod.HoFaceTrackingPlugin", "HoFaceTracking.Nodes.HoVtsCharacterInputNode",
                "HoFaceTracking.Nodes.HoFaceVtsRampNode", "HoFaceTracking.Nodes.HoFaceVtsTrackingOutputNode", "HoFaceTracking.Core.HoVtsFrameDelivery" })
                Require(types.Contains(name), "distributed type " + name);
            var plugin = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("HoFaceTracking.PluginMod.HoFaceTrackingPlugin")).First(t => t != null);
            var attr = plugin.GetCustomAttributes(false).Single(a => a.GetType().Name == "PluginTypeAttribute");
            var nodes = (Type[])attr.GetType().GetProperty("NodeTypes").GetValue(attr);
            foreach (string name in new[] { "HoVtsCharacterInputNode", "HoFaceVtsRampNode", "HoFaceVtsTrackingOutputNode" })
                Require(nodes.Any(t => t.Name == name), "plugin registration " + name);
            report += "\n" + verification.GetType().GetField("report").GetValue(verification);
            report += "\nDistributed types verified: " + types.Length;
        }
        catch (Exception error) { failure = error is TargetInvocationException ? error.InnerException : error; }
        finally
        {
            try
            {
                File.Copy(Folder + "/ExportSettings.backup.asset", SettingsPath, true);
                AssetDatabase.ImportAsset(SettingsPath, ImportAssetOptions.ForceSynchronousImport);
            }
            catch (Exception error) { failure = error; }
        }
        string text = failure == null ? "HO_VTS_PLUGIN_PACKAGING PASS\n" + report : "HO_VTS_PLUGIN_PACKAGING FAIL\n" + failure;
        File.WriteAllText(Folder + "/result.txt", text);
        if (failure == null) Debug.Log(text); else Debug.LogException(failure);
        EditorApplication.Exit(failure == null ? 0 : 1);
    }
    private static void Require(bool condition, string text)
    {
        if (!condition) throw new Exception(text);
        Debug.Log("HO_VTS_PLUGIN_PACKAGING ok " + text);
    }
}
