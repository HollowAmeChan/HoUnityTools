using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Copy to Assets/Editor in a private host with Commercial Skin and LWGUI 1.41.1+.
public static class CommercialSkinPaletteChecks
{
    private static int assertions;
    [Serializable] private sealed class Report
    {
        public string project, unity;
        public int assertions, presets;
        public bool customAndNone, mixedOneUndo, staleSelectionGuard, stableCurve, resourceBytesUnchanged;
    }

    public static void Run()
    {
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        Require(project.StartsWith(Path.GetFullPath("D:/Temp/"), StringComparison.OrdinalIgnoreCase), "private host only");
        string output = Environment.GetEnvironmentVariable("HO_COMMERCIAL_PALETTE_REPORT");
        Require(!string.IsNullOrEmpty(output) && Path.IsPathRooted(output) && !File.Exists(output), "new external report required");
        assertions = 0;
        Type drawerType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("HoShader.SkinURP.Editor.SkinPaletteDrawer")).FirstOrDefault(t => t != null);
        Require(drawerType != null, "actual optional palette drawer compiled");
        object drawer = Activator.CreateInstance(drawerType, new object[] { "Shadow" });
        MethodInfo load = drawerType.GetMethod("LoadPalettes", BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo apply = drawerType.GetMethod("ApplySelection", BindingFlags.NonPublic | BindingFlags.Instance);
        var palettes = (Texture2D[])load.Invoke(null, null);
        Require(palettes.Length == 9 && palettes.All(t => t != null), "all nine palette assets resolve");
        string[] pngBytes = palettes.Select(t => Hash(Physical(AssetDatabase.GetAssetPath(t)))).ToArray();
        string[] metaBytes = palettes.Select(t => Hash(Physical(AssetDatabase.GetAssetPath(t)) + ".meta")).ToArray();
        string[] userData = palettes.Select(t => AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)).userData).ToArray();
        Require(userData.All(t => t == "HoShader.SkinURP.sRGB16"), "original encoding tags intact");
        string folder = "Assets/__CommercialSkinPalette_" + Guid.NewGuid().ToString("N");
        AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
        MaterialEditor single = null, mixed = null, other = null;
        var report = new Report { project = project, unity = Application.unityVersion, presets = palettes.Length };
        try
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.hoshader.skin-urp/Runtime/Shaders/SkinUrp.shader");
            Require(shader != null, "actual Commercial Skin shader resolves");
            var a = new Material(shader); var b = new Material(shader);
            AssetDatabase.CreateAsset(a, folder + "/A.mat"); AssetDatabase.CreateAsset(b, folder + "/B.mat");
            single = (MaterialEditor)UnityEditor.Editor.CreateEditor(a);
            other = (MaterialEditor)UnityEditor.Editor.CreateEditor(b);
            foreach (Texture2D palette in palettes)
            {
                Select(drawer, apply, single, palette, true);
                Check(a, palette, 0, 1, 1);
                Require(palette.width == 256 && palette.height == 64, "two-dimensional palette shape preserved");
            }
            Texture curve = a.GetTexture("_ShadowCurveLUT");
            Require(curve != null && AssetDatabase.GetAssetPath(curve) == folder + "/A.mat", "palette updates preserve material-owned curve");
            var custom = new Texture2D(8, 4, TextureFormat.RGBA32, false, true);
            AssetDatabase.CreateAsset(custom, folder + "/Custom.asset");
            Select(drawer, apply, single, custom, false); Check(a, custom, 1, 1, 0);
            Select(drawer, apply, single, null, false); Check(a, null, 1, 0, 0);
            Require(a.GetTexture("_ShadowCurveLUT") == curve, "custom and None retain original curve identity");
            report.customAndNone = report.stableCurve = true;

            Select(drawer, apply, single, palettes[0], true);
            Select(drawer, apply, other, palettes[1], true);
            mixed = (MaterialEditor)UnityEditor.Editor.CreateEditor(new Object[] { a, b });
            MaterialProperty mixedProperty = Property(mixed);
            Require(mixedProperty.hasMixedValue, "mixed selection is represented by the actual MaterialProperty");
            Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            apply.Invoke(drawer, new object[] { mixedProperty, mixed, palettes[8], true });
            Undo.FlushUndoRecordObjects();
            Check(a, palettes[8], 0, 1, 1); Check(b, palettes[8], 0, 1, 1);
            Undo.PerformUndo();
            Require(a.GetTexture("_SkinResponseLUT") == palettes[0] && b.GetTexture("_SkinResponseLUT") == palettes[1], "one Undo restores both different original palettes");
            Undo.PerformRedo();
            Require(a.GetTexture("_SkinResponseLUT") == palettes[8] && b.GetTexture("_SkinResponseLUT") == palettes[8], "one Redo reapplies both targets");
            report.mixedOneUndo = true;
            MaterialProperty stale = Property(single);
            apply.Invoke(drawer, new object[] { stale, other, null, false });
            Require(b.GetTexture("_SkinResponseLUT") == palettes[8] && b.GetFloat("_ShadowPaletteCustom") == 0, "stale selector callback cannot change another inspector selection");
            report.staleSelectionGuard = true;
            for (int i = 0; i < palettes.Length; ++i)
            {
                string path = AssetDatabase.GetAssetPath(palettes[i]);
                Require(Hash(Physical(path)) == pngBytes[i], "source palette pixels unchanged");
                Require(Hash(Physical(path) + ".meta") == metaBytes[i], "source palette importer bytes unchanged");
                Require(AssetImporter.GetAtPath(path).userData == userData[i], "source palette userData unchanged");
            }
            report.resourceBytesUnchanged = true;
            report.assertions = assertions;
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            Debug.Log("HO_COMMERCIAL_SKIN_PALETTE_OK assertions=" + assertions + " report=" + output);
        }
        finally
        {
            if (single != null) Object.DestroyImmediate(single);
            if (mixed != null) Object.DestroyImmediate(mixed);
            if (other != null) Object.DestroyImmediate(other);
            Undo.ClearAll();
            Require(folder.StartsWith("Assets/__CommercialSkinPalette_", StringComparison.Ordinal), "fixture cleanup scope");
            AssetDatabase.DeleteAsset(folder);
        }
    }

    private static MaterialProperty Property(MaterialEditor editor) => MaterialEditor.GetMaterialProperties(editor.targets).First(p => p.name == "_SkinResponseLUT");
    private static void Select(object drawer, MethodInfo apply, MaterialEditor editor, Texture2D texture, bool preset)
    {
        Undo.IncrementCurrentGroup();
        apply.Invoke(drawer, new object[] { Property(editor), editor, texture, preset });
        Undo.FlushUndoRecordObjects();
    }
    private static void Check(Material m, Texture texture, float custom, float has, float manualSRGB)
    {
        Require(m.GetTexture("_SkinResponseLUT") == texture, "selected palette binding");
        Require(m.GetFloat("_ShadowPaletteCustom") == custom, "custom fallback flag");
        Require(m.GetFloat("_HasSkinResponseLUT") == has, "presence flag");
        Require(m.GetFloat("_SkinResponseManualSRGB") == manualSRGB, "encoding flag");
        if (texture != null) Require(m.GetVector("_SkinResponseLutSize") == new Vector4(1f / texture.width, 1f / texture.height, texture.width, texture.height), "lookup dimensions updated");
    }
    private static string Physical(string path)
    {
        var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
        return package == null ? Path.GetFullPath(path) : Path.Combine(package.resolvedPath, path.Substring(package.assetPath.Length + 1));
    }
    private static string Hash(string path)
    {
        using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path)));
    }
    private static void Require(bool condition, string message)
    {
        ++assertions;
        if (!condition) throw new InvalidOperationException(message);
    }
}
