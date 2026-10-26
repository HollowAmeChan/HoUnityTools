using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Load this in an Editor assembly with the package and the fixture DLL installed.
// Uses a disposable prefab; the optional real prefab is loaded read-only.
public static class HoFastBuildDependencyScanValidation
{
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string FixtureFolder = "Assets/HoFastBuildScanValidationTemp";
    private static readonly StringBuilder Report = new StringBuilder();

    public static void Run(string realPrefabPath, string reportPath)
    {
        Report.Clear();
        ScriptableObject window = null;
        GameObject root = null;
        bool ownsFixtureFolder = false;
        try
        {
            Type windowType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("Hollow.HoUnityTools.Editor.Warudo.HoFastBuildWarudoModWindow"))
                .First(t => t != null);
            window = ScriptableObject.CreateInstance(windowType);
            Assert(!AssetDatabase.IsValidFolder(FixtureFolder), "Fixture folder must be unused");
            AssetDatabase.CreateFolder("Assets", "HoFastBuildScanValidationTemp");
            ownsFixtureFolder = true;
            MethodInfo hostCheck = windowType.GetMethod("IsWarudoHostAssembly", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (string assembly in new[] { "MagicaClothV2", "DynamicBone", "RootMotion", "VRC.SDK3.Dynamics.PhysBone", "UnityEngine.AnimationModule" })
                Assert((bool)hostCheck.Invoke(null, new object[] { assembly }), "Host whitelist recognizes " + assembly);
            foreach (string assembly in new[] { "Assembly-CSharp", "Assembly-CSharp-firstpass", "HoFastBuildScanFixtures" })
                Assert(!(bool)hostCheck.Invoke(null, new object[] { assembly }), "Custom assembly is not host supplied: " + assembly);
            root = new GameObject("ScanFixture");
            root.AddComponent<Camera>();
            root.AddComponent<Light>();
            root.AddComponent<UnityEngine.Animations.ParentConstraint>();
            var child = new GameObject("Inactive");
            child.transform.SetParent(root.transform);
            child.SetActive(false);
            child.AddComponent<Light>();
            child.AddComponent(FindFixtureType("FirstComponent"));
            child.AddComponent(FindFixtureType("SecondComponent"));
            PrefabUtility.SaveAsPrefabAsset(root, FixtureFolder + "/Fixture.prefab");
            Object.DestroyImmediate(root);
            root = null;

            Scan(window, FixtureFolder + "/Fixture.prefab");
            object[] rows = Rows(window);
            Assert(rows.Sum(r => Value<int>(r, "referenceCount")) == 8, "All eight mounts, including inactive children");
            Assert(rows.Length == 6, "Repeated Transform/Light mounts merge by type");
            Assert(Value<int>(Row(rows, "UnityEngine.Light"), "referenceCount") == 2, "Inactive Light retained");
            Assert(rows.Where(r => Value<bool>(r, "nativeComponent")).All(r =>
                Value<bool>(r, "hostProvided") && !Value<bool>(r, "copySource")), "Native components recognized as host supplied");
            foreach (string name in new[] { "FirstComponent", "SecondComponent" })
            {
                object row = Row(rows, "HoFastBuildScanFixtures." + name);
                Assert(Value<string>(row, "sourcePath").EndsWith(".dll", StringComparison.OrdinalIgnoreCase), name + " has DLL MonoScript");
                Assert(!Value<bool>(row, "hostProvided") && !Value<bool>(row, "copySource"), name + " is visible and unconfirmed");
            }
            Assert((int)Call(window, "CountComponentsWithoutRuntimeSource") == 2, "Both unknown DLL components counted");
            Call(window, "SetRuntimeScriptSelection", true);
            Assert(rows.All(r => !Value<bool>(r, "copySource")), "Select all never copies native or DLL components");
            Call(window, "RefreshDependencyPreview", true);
            Assert(Rows(window).Length == 6, "Selection-preserving refresh handles shared DLL paths");

            if (!string.IsNullOrEmpty(realPrefabPath))
            {
                Scan(window, realPrefabPath);
                rows = Rows(window);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(realPrefabPath);
                int actualCount = prefab.GetComponentsInChildren<Component>(true).Count(c => c != null);
                Assert(rows.Sum(r => Value<int>(r, "referenceCount")) == actualCount, "Real prefab has no omitted component mounts");
                Assert(rows.Any(r => Value<bool>(r, "bakeBeforeBuild")), "Armature authoring configuration included");
                Assert(rows.Where(r => Value<string>(r, "typeName").StartsWith("MagicaCloth2.", StringComparison.Ordinal))
                    .All(r => Value<bool>(r, "hostProvided") && !Value<bool>(r, "copySource")), "MC2 keeps host references");
                Assert((int)Call(window, "CountComponentsWithoutRuntimeSource") == 0, "Real prefab default runtime dependencies are covered");
                int localMounts = rows.Where(r => Value<bool>(r, "copySource")).Sum(r => Value<int>(r, "referenceCount"));
                Assert(localMounts > 0, "Real prefab includes local runtime source");
                windowType.GetField("copySelectedScripts", InstanceFlags).SetValue(window, false);
                Assert((int)Call(window, "CountComponentsWithoutRuntimeSource") == localMounts, "Disabling source copying reports only local runtime mounts");
                windowType.GetField("copySelectedScripts", InstanceFlags).SetValue(window, true);
                string[] closure = ((IEnumerable)Call(window, "CollectRuntimeSourceClosure")).Cast<string>().ToArray();
                Assert(!closure.Any(p => p.IndexOf("MagicaCloth", StringComparison.OrdinalIgnoreCase) >= 0), "MC2 source excluded from closure");
                Assert(!closure.Any(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)), "DLLs excluded from source closure");
                Assert(!closure.Any(p => p.EndsWith("HoCombineArmature.cs", StringComparison.OrdinalIgnoreCase)), "Baked authoring source excluded from closure");
                Report.AppendLine("Real prefab: " + rows.Length + " types / " + actualCount + " mounts");
                foreach (object row in rows)
                    Report.AppendLine(Value<string>(row, "typeName") + " x" + Value<int>(row, "referenceCount") +
                        " host=" + Value<bool>(row, "hostProvided") + " native=" + Value<bool>(row, "nativeComponent") +
                        " bake=" + Value<bool>(row, "bakeBeforeBuild") + " copy=" + Value<bool>(row, "copySource"));
            }
            Report.AppendLine("PASS");
        }
        catch (Exception exception)
        {
            Report.AppendLine("FAIL: " + exception);
            throw;
        }
        finally
        {
            if (root != null) Object.DestroyImmediate(root);
            if (window != null) Object.DestroyImmediate(window);
            if (ownsFixtureFolder && AssetDatabase.IsValidFolder(FixtureFolder)) AssetDatabase.DeleteAsset(FixtureFolder);
            File.WriteAllText(reportPath, Report.ToString());
        }
    }

    private static Type FindFixtureType(string name)
    {
        return Type.GetType("HoFastBuildScanFixtures." + name + ", HoFastBuildScanFixtures", true);
    }

    private static void Scan(ScriptableObject window, string path)
    {
        window.GetType().GetField("sourcePrefabPath", InstanceFlags).SetValue(window, path);
        Call(window, "RefreshDependencyPreview", false);
    }

    private static object Call(object instance, string name, params object[] args)
    {
        return instance.GetType().GetMethod(name, InstanceFlags).Invoke(instance, args);
    }

    private static object[] Rows(object window)
    {
        return ((IEnumerable)window.GetType().GetField("scriptPreview", InstanceFlags).GetValue(window)).Cast<object>().ToArray();
    }

    private static T Value<T>(object row, string field) { return (T)row.GetType().GetField(field).GetValue(row); }
    private static object Row(object[] rows, string name) { return rows.Single(r => Value<string>(r, "typeName") == name); }
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Report.AppendLine("OK: " + message);
    }
}
