using System;
using System.IO;
using LWGUI;
using UnityEditor;
using UnityEngine;
using Hollow.HoUnityTools.Editor.ShadowResponse;
using LwguiGradient = LWGUI.Runtime.LwguiGradient.LwguiGradient;
using Object = UnityEngine.Object;

// Copy to Assets/Editor in a private host with HoTools and HoShader LWGUI 1.41.1+.
public static class HoShadowResponseLwguiProviderChecks
{
    private static int assertions;
    [Serializable] private sealed class Report
    {
        public string project, unity;
        public int assertions;
        public bool automaticRegistration, catalogDiscovery, deepCopies, wrapping, emptyCurve, stableIdentity, undoRedo, saveDiscard, reload;
    }

    public static void Run()
    {
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        Require(project.StartsWith(Path.GetFullPath("D:/Temp/"), StringComparison.OrdinalIgnoreCase), "private host only");
        string output = Environment.GetEnvironmentVariable("HO_SHADOW_PROVIDER_REPORT");
        Require(!string.IsNullOrEmpty(output) && Path.IsPathRooted(output) && !File.Exists(output), "new absolute report path required");
        assertions = 0;
        string folder = "Assets/__HoShadowProvider_" + Guid.NewGuid().ToString("N");
        AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
        var report = new Report { project = project, unity = Application.unityVersion };
        try
        {
            var profile = ScriptableObject.CreateInstance<HoShadowResponseAsset>();
            var red = new AnimationCurve(new Keyframe(0, .125f, 0, .75f), new Keyframe(1, .875f, .25f, 0))
                { preWrapMode = WrapMode.Loop, postWrapMode = WrapMode.PingPong };
            var green = AnimationCurve.Linear(0, .2f, 1, .8f);
            var blue = new AnimationCurve();
            var alpha = AnimationCurve.Linear(0, .1f, 1, .9f);
            profile.SetCurves(red, green, blue, alpha);
            string path = folder + "/Response.asset";
            AssetDatabase.CreateAsset(profile, path);
            HoShadowResponseBaker.Bake(profile, false);
            Texture2D texture = profile.LookupTexture;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(texture, out string guid, out long localId);
            Require(ShadowRampStorage.TryGetSource(texture, out IShadowRampSource source), "provider registered automatically");
            Require(source.Owner == profile && source.Texture == texture && source.Editable, "matching authoring owner and texture");
            Require(ShadowRampStorage.ResolveTexture(profile) == texture, "curve main asset resolves to its existing LUT");
            Require(!ShadowRampStorage.TryGetSource(Texture2D.whiteTexture, out _), "unowned texture is not captured");
            report.automaticRegistration = true;

            var catalogMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"));
            AssetDatabase.CreateAsset(catalogMaterial, folder + "/Catalog.mat");
            var materialTexture = new Texture2D(8, 1, TextureFormat.RGBAHalf, false, true);
            materialTexture.SetPixels(new Color[8]); materialTexture.Apply(false, false);
            AssetDatabase.AddObjectToAsset(materialTexture, catalogMaterial);
            var palette2D = new Texture2D(8, 32, TextureFormat.RGBAHalf, false, true);
            AssetDatabase.CreateAsset(palette2D, folder + "/Palette2D.asset");
            string passiveProfile = EditorJsonUtility.ToJson(profile);
            string passivePixels = Convert.ToBase64String(texture.GetRawTextureData<byte>().ToArray());
            string passiveAssetBytes = Convert.ToBase64String(File.ReadAllBytes(path));
            string passiveMetaBytes = Convert.ToBase64String(File.ReadAllBytes(path + ".meta"));
            var catalog = ShadowRampStorage.FindTextures(new[] { folder }, ShadowRampSampling.Endpoints);
            Require(Array.IndexOf(catalog, texture) >= 0, "catalog discovers an external ScriptableObject provider's existing LUT");
            Require(Array.IndexOf(catalog, materialTexture) >= 0, "catalog discovers a material-owned native texture child");
            Require(Array.IndexOf(catalog, palette2D) < 0, "catalog excludes two-dimensional palettes");
            Require(Array.TrueForAll(catalog, t => AssetDatabase.GetAssetPath(t).StartsWith(folder + "/", StringComparison.Ordinal)), "catalog stays within requested roots");
            Require(ShadowRampStorage.FindTextures(Array.Empty<string>(), ShadowRampSampling.Endpoints).Length == 0, "empty roots never fall back to a project-wide search");
            Require(passiveProfile == EditorJsonUtility.ToJson(profile) && passivePixels == Convert.ToBase64String(texture.GetRawTextureData<byte>().ToArray()), "catalog leaves authored curves and baked pixels unchanged");
            Require(passiveAssetBytes == Convert.ToBase64String(File.ReadAllBytes(path)) && passiveMetaBytes == Convert.ToBase64String(File.ReadAllBytes(path + ".meta")), "catalog leaves original asset and meta bytes unchanged");
            report.catalogDiscovery = true;

            string before = EditorJsonUtility.ToJson(profile);
            LwguiGradient read = source.ReadGradient();
            Require(read.rawCurves[0].preWrapMode == WrapMode.Loop && read.rawCurves[0].postWrapMode == WrapMode.PingPong, "wrap modes preserved");
            Require(read.rawCurves[0].keys[0].outTangent == red.keys[0].outTangent, "authored tangents preserved");
            Require(read.rawCurves[2].length == 0, "empty curve is not replaced by white");
            read.rawCurves[0].MoveKey(0, new Keyframe(0, .99f));
            Require(before == EditorJsonUtility.ToJson(profile), "reading and mutating returned gradient never changes owner");
            report.deepCopies = report.wrapping = report.emptyCurve = true;

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            var edited = Constant(.3f);
            ShadowRampStorage.Apply(texture, edited);
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(group);
            Require(ShadowRampStorage.IsDirty(texture), "live edit differs from session baseline");
            Require(Mathf.Abs(profile.Evaluate(.5f).r - .3f) < .00001f, "provider writes original authoring asset");
            Require(profile.LookupTexture == texture, "live edit keeps texture object identity");
            CheckTexture(texture, guid, localId, .3f);
            edited.rawCurves[0].MoveKey(0, new Keyframe(0, .95f));
            Require(Mathf.Abs(profile.Evaluate(0).r - .3f) < .00001f, "written curves do not alias editor working copy");
            Undo.PerformUndo();
            Require(Mathf.Abs(profile.Evaluate(.5f).r - red.Evaluate(.5f)) < .00001f, "Undo restores authoring curve");
            Require(source.ReadGradient().rawCurves[0].preWrapMode == WrapMode.Loop, "Undo restores curve wrapping");
            Undo.PerformRedo();
            Require(Mathf.Abs(profile.Evaluate(.5f).r - .3f) < .00001f, "Redo reapplies edited authoring curve");
            report.undoRedo = true;

            ShadowRampStorage.Discard(texture);
            Require(Mathf.Abs(profile.Evaluate(.5f).r - red.Evaluate(.5f)) < .00001f && !ShadowRampStorage.IsDirty(texture), "Discard restores session baseline");
            Require(source.ReadGradient().rawCurves[0].postWrapMode == WrapMode.PingPong, "Discard preserves authored wrap modes");
            ShadowRampStorage.Apply(texture, Constant(.6f));
            ShadowRampStorage.Save(texture);
            Require(!ShadowRampStorage.IsDirty(texture), "Save establishes new baseline");
            ShadowRampStorage.Apply(texture, Constant(.8f));
            ShadowRampStorage.Discard(texture);
            Require(Mathf.Abs(profile.Evaluate(.5f).r - .6f) < .00001f, "Discard after Save restores latest saved value");
            CheckTexture(texture, guid, localId, .6f);
            report.stableIdentity = report.saveDiscard = true;

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            profile = AssetDatabase.LoadAssetAtPath<HoShadowResponseAsset>(path);
            Require(ShadowRampStorage.TryGetSource(profile.LookupTexture, out source), "saved authoring asset resolves after reimport");
            Require(Mathf.Abs(source.ReadGradient().rawCurves[0].Evaluate(.5f) - .6f) < .00001f, "saved gradient survives reimport");
            CheckTexture(profile.LookupTexture, guid, localId, .6f);
            report.reload = true;
            report.assertions = assertions;
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            Debug.Log("HO_SHADOW_LWGUI_PROVIDER_OK assertions=" + assertions + " report=" + output);
        }
        finally
        {
            Undo.ClearAll();
            AssetDatabase.DeleteAsset(folder);
        }
    }

    private static LwguiGradient Constant(float value)
    {
        var result = new LwguiGradient();
        for (int channel = 0; channel < 4; ++channel)
            result.rawCurves[channel] = AnimationCurve.Linear(0, value, 1, value);
        return result;
    }

    private static void CheckTexture(Texture2D texture, string guid, long localId, float value)
    {
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(texture, out string actualGuid, out long actualId);
        Require(actualGuid == guid && actualId == localId, "persistent GUID and localID unchanged");
        Require(texture.width == 1024 && texture.height == 1 && texture.format == TextureFormat.RGBAHalf && !texture.isDataSRGB,
            "native linear RGBAHalf bake preserved");
        Require(texture.filterMode == FilterMode.Bilinear && texture.wrapMode == TextureWrapMode.Clamp && texture.mipmapCount == 1,
            "native clamp bilinear sampling preserved");
        Require(Mathf.Abs(texture.GetPixel(512, 0).r - value) < .001f, "live LUT pixels match authoring value");
    }

    private static void Require(bool condition, string label)
    {
        ++assertions;
        if (!condition) throw new InvalidOperationException("Ho Shadow LWGUI provider: " + label);
    }
}
