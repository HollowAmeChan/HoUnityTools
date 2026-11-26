using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Hollow.HoUnityTools.Editor.Pmx;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Runs only in the disposable project prepared by prepare-specialized-validation.py.
public static class HoPmxSpecializedValidation
{
    private static readonly List<string> Passed = new();
    public static void Run()
    {
        try
        {
            Check(File.Exists("ProjectSettings/HoPmxSpecializedValidationHost"), "disposable validation host marker");
            foreach (string path in new[] { "Assets/fresh.pmx", "Assets/eye.pmx", "Assets/existing-implicit0.pmx",
                "Assets/external.mat", "Assets/extracted-eye.mat", "Assets/coverage-source.png", "Assets/hair-transparent.pmx",
                "Assets/hair-cutout.pmx", "Assets/extracted-hair.mat" }) AssetDatabase.DeleteAsset(path);
            Check(Shader.Find("lilToon") == null, "lilToon absent");
            const string fresh = "Assets/fresh.pmx";
            WritePmx(fresh, new[] { "[ho:Cloth] cloth", "[ho:Hair] hair", "[ho:Skin] skin", "[ho:Eye] eye", "[ho:Environment] stage" });
            AssetDatabase.ImportAsset(fresh, ImportAssetOptions.ForceSynchronousImport);
            var importer = (HoPmxImporter)AssetImporter.GetAtPath(fresh);
            Check(importer.materialProfile == HoPmxMaterialProfile.SpecializedUrp, "new source profile2");
            var report = Report(fresh);
            Check(report.materials.Count == 5 && report.materials.All(x => x.material != null), "five material families generated");
            foreach (var item in report.materials)
                Check(item.material.shader.name == (item.targetFamily == HoPmxMaterialFamily.Skin ? "Ho/Skin/URP/Stylized" : item.targetFamily == HoPmxMaterialFamily.Eye ? "Ho/Skin/URP/Eye" : item.targetFamily == HoPmxMaterialFamily.Environment ? "Ho/Environment/URP/Lit" : "Ho/" + item.targetFamily + "/URP/" + item.targetFamily), "shader family " + item.targetFamily);
            string guid = AssetDatabase.AssetPathToGUID(fresh);
            var ids = Ids(fresh);
            AssetDatabase.ImportAsset(fresh, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            Check(ids.OrderBy(x => x).SequenceEqual(Ids(fresh).OrderBy(x => x)), "generated material fileIDs stable on reimport");
            Check(AssetDatabase.AssetPathToGUID(fresh) == guid, "PMX GUID stable");

            var external = new Material(Shader.Find("Ho/Cloth/URP/Cloth")) { name = "External" };
            AssetDatabase.CreateAsset(external, "Assets/external.mat");
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), HoPmxImporter.MaterialIdentifier(0)), external);
            importer.SaveAndReimport();
            Check(Report(fresh).materials[0].material == external, "external material preserved");
            Check(Report(fresh).materials[1].material.shader.name == "Ho/Hair/URP/Hair", "unmapped slots still generated");

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            texture.SetPixels(new[] { new Color(1, 1, 1, .2f), new Color(1, 1, 1, .6f), Color.white, Color.clear });
            texture.Apply();
            File.WriteAllBytes("Assets/coverage-source.png", texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset("Assets/coverage-source.png", ImportAssetOptions.ForceSynchronousImport);
            WritePmx("Assets/hair-transparent.pmx", new[] { "[ho:Hair] hair" }, diffuseAlpha: .35f);
            AssetDatabase.ImportAsset("Assets/hair-transparent.pmx", ImportAssetOptions.ForceSynchronousImport);
            var transparentHair = Report("Assets/hair-transparent.pmx").materials[0].material;
            Check(Mathf.Approximately(transparentHair.GetFloat("_SurfaceMode"), 2) &&
                Mathf.Approximately(transparentHair.GetFloat("_Opacity"), .35f) &&
                Mathf.Approximately(transparentHair.GetColor("_BaseColor").a, 1), "Hair diffuse alpha .35 maps to transparent opacity");
            WritePmx("Assets/hair-cutout.pmx", new[] { "[ho:Hair] hair" }, "coverage-source.png", .7f);
            AssetDatabase.ImportAsset("Assets/hair-cutout.pmx", ImportAssetOptions.ForceSynchronousImport);
            var hairImporter = (HoPmxImporter)AssetImporter.GetAtPath("Assets/hair-cutout.pmx");
            hairImporter.materialSettings.Add(new HoPmxMaterialSetting { materialIndex = 0, alphaMode = HoPmxAlphaMode.Cutout });
            EditorUtility.SetDirty(hairImporter); hairImporter.SaveAndReimport();
            var cutoutHair = Report("Assets/hair-cutout.pmx").materials[0].material;
            var hairCoverage = (Texture2D)cutoutHair.GetTexture("_BaseMap");
            Check(cutoutHair.IsKeywordEnabled("_ALPHATEST_ON") && Mathf.Approximately(cutoutHair.GetFloat("_Opacity"), 1) &&
                Mathf.Approximately(cutoutHair.GetColor("_BaseColor").a, 1), "Hair cutout uses baked texture coverage instead of ignored opacity");
            Check(Mathf.Abs(hairCoverage.GetPixel(0, 0).a - .14f) < .004f &&
                hairCoverage.GetPixels().Count(x => x.a >= cutoutHair.GetFloat("_Cutoff")) == 1,
                "Hair cutout alpha is texture alpha times diffuse alpha");
            var extractedHair = new Material(cutoutHair);
            AssetDatabase.CreateAsset(extractedHair, "Assets/extracted-hair.mat");
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(hairCoverage, out string hairGuid, out long hairId);
            hairImporter.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), HoPmxImporter.MaterialIdentifier(0)), extractedHair);
            hairImporter.SaveAndReimport();
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(extractedHair.GetTexture("_BaseMap"), out string retainedHairGuid, out long retainedHairId);
            Check(hairGuid == retainedHairGuid && hairId == retainedHairId && extractedHair.GetTexture("_BaseMap") != null,
                "extracted Hair coverage subasset retained after remap");
            WritePmx("Assets/eye.pmx", new[] { "[ho:Eye] eye" }, "coverage-source.png");
            AssetDatabase.ImportAsset("Assets/eye.pmx", ImportAssetOptions.ForceSynchronousImport);
            var eyeImporter = (HoPmxImporter)AssetImporter.GetAtPath("Assets/eye.pmx");
            eyeImporter.materialSettings.Add(new HoPmxMaterialSetting { materialIndex = 0, alphaMode = HoPmxAlphaMode.Cutout });
            EditorUtility.SetDirty(eyeImporter);
            eyeImporter.SaveAndReimport();
            var eye = Report("Assets/eye.pmx").materials[0].material;
            var coverage = (Texture2D)eye.GetTexture("_CoverageMap");
            Check(coverage != null && Mathf.Abs(coverage.GetPixel(0, 0).r - .2f) < .01f, "Eye texture alpha converted to linear coverage R");
            Check(Mathf.Approximately(eye.GetFloat("_SurfaceMode"), 1), "Eye cutout mode synchronized");
            var extractedEye = new Material(eye);
            AssetDatabase.CreateAsset(extractedEye, "Assets/extracted-eye.mat");
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(coverage, out string coverageGuid, out long coverageId);
            eyeImporter.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), HoPmxImporter.MaterialIdentifier(0)), extractedEye);
            eyeImporter.SaveAndReimport();
            var retainedCoverage = extractedEye.GetTexture("_CoverageMap");
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(retainedCoverage, out string retainedGuid, out long retainedId);
            Check(retainedCoverage != null && retainedGuid == coverageGuid && retainedId == coverageId,
                "extracted Eye coverage subasset retained after remap");

            // Hand-authored old metadata deliberately omits materialProfile, matching old files.
            const string legacy = "Assets/existing-implicit0.pmx";
            WritePmx(legacy, Array.Empty<string>());
            File.WriteAllText(legacy + ".meta", "fileFormatVersion: 2\nguid: 8cd6df8e4bb44dd29f6191474a506340\nScriptedImporter:\n  internalIDToNameTable: []\n  externalObjects: {}\n  serializedVersion: 2\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n  script: {fileID: 11500000, guid: 98384b903402a1b40b12003cca9a9b0f, type: 3}\n  scale: 0.08\n  generateOutline: 1\n");
            AssetDatabase.ImportAsset(legacy, ImportAssetOptions.ForceSynchronousImport);
            var legacyImporter = (HoPmxImporter)AssetImporter.GetAtPath(legacy);
            Check(legacyImporter.materialProfile == HoPmxMaterialProfile.LilToon, "existing missing profile remains legacy0");
            Directory.CreateDirectory("Results");
            File.WriteAllText("Results/specialized-validation.json", "{\"status\":\"passed\",\"checks\":" + JsonUtility.ToJson(new Checks { values = Passed.ToArray() }) + "}");
            Debug.Log("HO_PMX_SPECIALIZED_PASSED " + Passed.Count);
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Directory.CreateDirectory("Results");
            File.WriteAllText("Results/specialized-failure.txt", e.ToString());
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }
    [Serializable] private sealed class Checks { public string[] values; }

    public static void RunHairCoverageGpu()
    {
        bool previousAsync = ShaderUtil.allowAsyncCompilation;
        try
        {
            Check(File.Exists("ProjectSettings/HoPmxSpecializedValidationHost"), "disposable validation host marker");
            Check(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "GPU coverage validation requires a graphics device");
            ShaderUtil.allowAsyncCompilation = false;
            using var transparent = new TemporaryMaterial(Report("Assets/hair-transparent.pmx").materials[0].material);
            using var cutout = new TemporaryMaterial(Report("Assets/hair-cutout.pmx").materials[0].material);
            var blended = DrawHairPass(transparent.Value, "Forward");
            Check(Mathf.Abs(blended[32 + 64 * 32].a - .35f) < .005f, "real Hair Forward preserves PMX diffuse alpha .35");
            var visible = DrawHairPass(cutout.Value, "Forward");
            var depth = DrawHairPass(cutout.Value, "DepthOnly");
            int centers = 0;
            foreach (int y in new[] { 16, 48 }) foreach (int x in new[] { 16, 48 })
                if (visible[x + y * 64].a > .5f) centers++;
            Check(centers == 1, "real Hair cutout preserves one surviving alpha-times-diffuse quadrant");
            for (int i = 0; i < visible.Length; ++i)
                if ((visible[i].a > .5f) != (depth[i].r >= 0))
                    throw new InvalidOperationException("Hair Forward/DepthOnly coverage differs at " + i);
            Check(true, "real Hair Forward and DepthOnly use identical baked coverage");
            Check(!ShaderUtil.ShaderHasError(transparent.Value.shader), "Hair shader compiles without errors");
            File.WriteAllText("Results/hair-coverage-gpu.json", JsonUtility.ToJson(new Checks { values = Passed.ToArray() }, true));
            Debug.Log("HO_PMX_HAIR_COVERAGE_GPU_PASSED " + Passed.Count);
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            File.WriteAllText("Results/hair-coverage-gpu-failure.txt", e.ToString());
            Debug.LogException(e); EditorApplication.Exit(1);
        }
        finally { ShaderUtil.allowAsyncCompilation = previousAsync; }
    }

    private sealed class TemporaryMaterial : IDisposable
    {
        internal readonly Material Value;
        internal TemporaryMaterial(Material source) { Value = new Material(source); Value.SetFloat("_Cull", 0); }
        public void Dispose() => UnityEngine.Object.DestroyImmediate(Value);
    }

    private static Color[] DrawHairPass(Material material, string passName)
    {
        var target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var readback = new Texture2D(64, 64, TextureFormat.RGBAFloat, false, true);
        var mesh = new Mesh();
        var previousTarget = RenderTexture.active;
        Vector4 previousCamera = Shader.GetGlobalVector("_WorldSpaceCameraPos");
        try
        {
            mesh.vertices = new[] { new Vector3(-1, -1, .5f), new Vector3(1, -1, .5f), new Vector3(-1, 1, .5f), new Vector3(1, 1, .5f) };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            mesh.normals = Enumerable.Repeat(Vector3.back, 4).ToArray();
            mesh.tangents = Enumerable.Repeat(new Vector4(1, 0, 0, 1), 4).ToArray();
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            int pass = material.FindPass(passName);
            if (pass < 0) throw new InvalidOperationException("Missing Hair pass " + passName);
            target.Create();
            using (var command = new CommandBuffer())
            {
                command.SetRenderTarget(target);
                command.ClearRenderTarget(true, true, new Color(-1, 0, 0, 0));
                command.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                command.SetGlobalVector("_WorldSpaceCameraPos", new Vector4(0, 0, -5, 1));
                command.DrawMesh(mesh, Matrix4x4.identity, material, 0, pass);
                Graphics.ExecuteCommandBuffer(command);
            }
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); readback.Apply();
            return readback.GetPixels();
        }
        finally
        {
            RenderTexture.active = previousTarget;
            Shader.SetGlobalVector("_WorldSpaceCameraPos", previousCamera);
            target.Release(); UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(readback); UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    private static HoPmxImportReport Report(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<HoPmxImportReport>().First();
    private static long[] Ids(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().Select(x =>
        { AssetDatabase.TryGetGUIDAndLocalFileIdentifier(x, out string _, out long id); return id; }).ToArray();
    private static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name);
        Passed.Add(name);
    }
    private static void WritePmx(string path, string[] names, string texture = null, float diffuseAlpha = 1)
    {
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(Encoding.ASCII.GetBytes("PMX ")); writer.Write(2f);
        writer.Write(new byte[] { 8, 1, 0, 1, 1, 1, 1, 1, 1 });
        Text(writer, "Synthetic validation"); Text(writer, ""); Text(writer, ""); Text(writer, "");
        writer.Write(0); writer.Write(0); // no vertices/indices: isolates material regeneration
        writer.Write(texture == null ? 0 : 1); if (texture != null) Text(writer, texture);
        writer.Write(names.Length);
        foreach (string name in names)
        {
            Text(writer, name); Text(writer, "");
            foreach (float value in new[] { 1f, 1f, 1f, diffuseAlpha, .04f, .04f, .04f, 32f, .2f, .2f, .2f }) writer.Write(value);
            writer.Write((byte)12); // cast + receive shadows
            foreach (float value in new[] { 0f, 0f, 0f, 1f, 0f }) writer.Write(value);
            writer.Write((sbyte)(texture == null ? -1 : 0)); writer.Write((sbyte)-1); writer.Write((byte)0);
            writer.Write((byte)1); writer.Write((byte)0); Text(writer, ""); writer.Write(0);
        }
        for (int i = 0; i < 5; i++) writer.Write(0); // bone/morph/frame/rigid/joint counts
    }
    private static void Text(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes);
    }
}
