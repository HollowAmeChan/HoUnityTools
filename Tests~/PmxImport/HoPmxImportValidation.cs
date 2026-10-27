using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Hollow.HoUnityTools.Editor.Pmx;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Copied into a dedicated validation project by Tools~/PmxImport/prepare-validation.py.
// Test data stays in that local project; copyrighted source models are not bundled here.
public static class HoPmxImportValidation
{
    private const string Results = "Results";
    private static readonly List<string> Passed = new List<string>();
    private static readonly List<SampleResult> Samples = new List<SampleResult>();
    private static readonly string[] Paths =
    {
        "Assets/Samples/马戏团/场景.pmx",
        "Assets/Samples/血月古堡/1.pmx",
        "Assets/Samples/云景千山/云景千山v2.pmx",
        "Assets/Samples/朱木古堂/主体.pmx",
        "Assets/Samples/朱木古堂/吊饰.pmx",
        "Assets/Samples/朱木古堂/家具.pmx",
        "Assets/Samples/朱木古堂/外院.pmx",
    };
    private static readonly int[] TriangleCounts = { 286342, 241472, 88210, 254448, 38160, 70147, 34162 };
    private static readonly int[] MaterialCounts = { 29, 15, 43, 35, 3, 7, 5 };

    [Serializable] private sealed class SampleResult
    {
        public string path;
        public int renderers, triangles, materials, normalMappedMaterials, metalMappedMaterials;
        public string[] warnings;
        public string[] alphaModes;
    }
    [Serializable] private sealed class Result
    {
        public string status;
        public string failure;
        public string[] checks;
        public SampleResult[] samples;
    }

    private static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name);
        Passed.Add(name);
    }

    public static void Run()
    {
        Directory.CreateDirectory(Results);
        WriteResult("RUNNING", null);
        try
        {
            Check(Shader.Find("lilToon") != null, "Ho lilToon shader is available");
            ResetValidationImports();
            SourceImageChecks();
            CoordinateFixtureChecks();
            for (int i = 0; i < Paths.Length; ++i) ImportSample(Paths[i], TriangleCounts[i], MaterialCounts[i]);
            SpecificMaterialChecks();
            InspectorAndExtractionChecks();
            DataTextureExtractionChecks();
            MorphBakeChecks();
            WriteResult("PASS", null);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            WriteResult("FAIL", exception.ToString());
            EditorApplication.Exit(1);
        }
    }

    private static void WriteResult(string status, string failure)
    {
        var result = new Result { status = status, failure = failure, checks = Passed.ToArray(), samples = Samples.ToArray() };
        File.WriteAllText(Path.Combine(Results, "import-results.json"), JsonUtility.ToJson(result, true));
        File.WriteAllText(Path.Combine(Results, "import-results.txt"), status + " " + Passed.Count + " checks\n" + failure + "\n" + string.Join("\n", Passed));
    }

    private static HoPmxImportReport Report(string path)
    {
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<HoPmxImportReport>().Single();
    }

    private static void ResetValidationImports()
    {
        foreach (string path in Paths)
        {
            var importer = AssetImporter.GetAtPath(path) as HoPmxImporter;
            if (importer == null) continue;
            bool changed = importer.GetExternalObjectMap().Count > 0 || importer.materialSettings.Count > 0 ||
                importer.morphSettings.Count > 0 || Mathf.Abs(importer.scale - 0.08f) > 0.000001f;
            if (!changed) continue;
            foreach (var key in importer.GetExternalObjectMap().Keys.ToArray()) importer.RemoveRemap(key);
            importer.materialSettings.Clear();
            importer.morphSettings.Clear();
            importer.scale = 0.08f;
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
        }
    }

    private static void CoordinateFixtureChecks()
    {
        const string directory = "Assets/GeneratedPmxFixture";
        Directory.CreateDirectory(directory);
        var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        image.SetPixels32(new[] { new Color32(255, 0, 0, 255), new Color32(0, 255, 0, 255),
            new Color32(0, 0, 255, 255), new Color32(255, 255, 255, 255) });
        image.Apply();
        File.WriteAllBytes(directory + "/fixture.png", image.EncodeToPNG());
        Object.DestroyImmediate(image);
        const string path = directory + "/fixture.pmx";
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            Action<string> text = value => { byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes); };
            Action<float, float, float> v3 = (x, y, z) => { writer.Write(x); writer.Write(y); writer.Write(z); };
            Action<float, float, float, float> v4 = (x, y, z, w) => { v3(x, y, z); writer.Write(w); };
            writer.Write(new byte[] { 80, 77, 88, 32 }); writer.Write(2.0f);
            writer.Write((byte)8); writer.Write(new byte[] { 1, 0, 1, 1, 1, 1, 1, 1 });
            text("Synthetic one-sided fixture"); text("Fixture"); text(""); text("");
            writer.Write(3);
            for (int i = 0; i < 3; ++i)
            {
                v3(i == 2 ? 1 : 0, i == 1 ? 1 : 0, 0); v3(0, 0, -1);
                writer.Write(i == 2 ? 1f : 0f); writer.Write(i == 1 ? 1f : 0f);
                writer.Write((byte)0); writer.Write((byte)0); writer.Write(1f);
            }
            writer.Write(3); writer.Write(new byte[] { 0, 1, 2 });
            writer.Write(1); text(".\\fixture.png");
            writer.Write(1); text("Surface"); text("Surface");
            v4(1, 1, 1, 1); v3(0, 0, 0); writer.Write(0f); v3(0.5f, 0.5f, 0.5f);
            writer.Write((byte)0); v4(0, 0, 0, 1); writer.Write(0f);
            writer.Write((byte)0); writer.Write((byte)255); writer.Write((byte)0);
            writer.Write((byte)1); writer.Write((byte)0); text(""); writer.Write(3);
            writer.Write(1); text("Bone"); text("Bone"); v3(0, 0, 0); writer.Write((byte)255);
            writer.Write(0); writer.Write((ushort)0); v3(0, 1, 0);
            writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
        }
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Check(model != null, "Generated PMX fixture imports without external assets");
        var mesh = model.GetComponentsInChildren<MeshFilter>().Single().sharedMesh;
        Check((mesh.vertices[2] - new Vector3(-0.08f, 0, 0)).sqrMagnitude < 1e-10f,
            "PMX coordinate conversion is a Y-180 rotation with scale, preserving handedness");
        Check((mesh.normals[0] - Vector3.forward).sqrMagnitude < 1e-10f && mesh.triangles.SequenceEqual(new[] { 0, 1, 2 }),
            "Authored normals rotate with positions and triangle winding stays unchanged");
        Check((mesh.uv[0] - new Vector2(0, 1)).sqrMagnitude < 1e-10f && (mesh.uv[1] - Vector2.zero).sqrMagnitude < 1e-10f,
            "PMX texture V coordinate flips once");
        Check(Report(path).materials[0].material.GetTexture("_MainTex") != null,
            "Dot-backslash texture path resolves relative to PMX directory");
    }

    private static void SourceImageChecks()
    {
        var decoder = typeof(HoPmxImporter).Assembly.GetType("Hollow.HoUnityTools.Editor.Pmx.HoPmxSourceImage");
        var method = decoder.GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic);
        byte[] bmp;
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(new byte[] { 66, 77 }); writer.Write(62); writer.Write(0); writer.Write(54);
            writer.Write(40); writer.Write(2); writer.Write(1); writer.Write((ushort)1); writer.Write((ushort)24);
            writer.Write(0); writer.Write(8); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
            writer.Write(new byte[] { 42, 128, 192, 3, 2, 1, 0, 0 });
            bmp = stream.ToArray();
        }
        object[] arguments = { bmp, ".bmp", 0, 0, null };
        Check((bool)method.Invoke(null, arguments) && (int)arguments[2] == 2 && (int)arguments[3] == 1 &&
              ((byte[])arguments[4]).SequenceEqual(new byte[] { 192, 128, 42, 255, 1, 2, 3, 255 }),
            "Source BMP decoder preserves data channels and handles row padding without GPU readback");
        var tga = new byte[22];
        tga[2] = 10; tga[12] = 2; tga[14] = 1; tga[16] = 24;
        tga[18] = 129; tga[19] = 42; tga[20] = 128; tga[21] = 192;
        arguments = new object[] { tga, ".tga", 0, 0, null };
        Check((bool)method.Invoke(null, arguments) && (int)arguments[2] == 2 && (int)arguments[3] == 1 &&
              ((byte[])arguments[4]).SequenceEqual(new byte[] { 192, 128, 42, 255, 192, 128, 42, 255 }),
            "Source RLE TGA decoder preserves normal/metallic channel bytes");
        var oriented = new byte[34];
        oriented[2] = 2; oriented[12] = 2; oriented[14] = 2; oriented[16] = 32; oriented[17] = 0x38;
        Array.Copy(new byte[] { 0, 0, 255, 64, 0, 255, 0, 128, 255, 0, 0, 192, 255, 255, 255, 255 }, 0, oriented, 18, 16);
        arguments = new object[] { oriented, ".tga", 0, 0, null };
        Check((bool)method.Invoke(null, arguments) && ((byte[])arguments[4]).SequenceEqual(new byte[]
              { 255, 255, 255, 255, 0, 0, 255, 192, 0, 255, 0, 128, 255, 0, 0, 64 }),
            "Source TGA decoder respects top/right origin and retains populated alpha");
        arguments = new object[] { bmp.Take(56).ToArray(), ".bmp", 0, 0, null };
        Check(!(bool)method.Invoke(null, arguments), "Truncated source image is rejected without partial decoded data");
    }

    private static void ImportSample(string path, int triangles, int materials)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(path);
        Check(importer is HoPmxImporter, path + ": registered native PMX importer");
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Check(model != null, path + ": GameObject main asset");
        var report = Report(path);
        Check(report.triangleCount == triangles && report.materials.Count == materials, path + ": source topology/material counts");
        var filters = model.GetComponentsInChildren<MeshFilter>(true);
        Check(filters.Length > 0 && filters.All(x => x.sharedMesh != null), path + ": static mesh renderers");
        int actualTriangles = filters.Sum(x => (int)x.sharedMesh.GetIndexCount(0) / 3);
        Check(actualTriangles == triangles, path + ": generated triangle count");
        Check(filters.All(x => x.sharedMesh.normals.Length == x.sharedMesh.vertexCount && x.sharedMesh.uv.Length == x.sharedMesh.vertexCount), path + ": explicit normals and UV retained");
        var renderers = model.GetComponentsInChildren<MeshRenderer>(true);
        var assigned = renderers.SelectMany(x => x.sharedMaterials).Where(x => x != null).Distinct().ToArray();
        Check(assigned.Length == materials, path + ": every source material assigned");
        Check(assigned.All(x => x.shader != null && x.shader.name.IndexOf("lilToon", StringComparison.OrdinalIgnoreCase) >= 0), path + ": only lilToon shader family");
        Check(assigned.All(x => x.GetTexture("_MainTex") != null), path + ": relative base texture references resolved");
        Samples.Add(new SampleResult
        {
            path = path, renderers = renderers.Length, triangles = actualTriangles, materials = assigned.Length,
            normalMappedMaterials = assigned.Count(x => x.GetTexture("_BumpMap") != null),
            metalMappedMaterials = assigned.Count(x => x.GetTexture("_MetallicGlossMap") != null),
            warnings = report.warnings.ToArray(),
            alphaModes = report.materials.Select(x => x.sourceName + ": " + x.alphaMode + " (" + x.alphaReason + ")").ToArray(),
        });
    }

    private static void SpecificMaterialChecks()
    {
        var castle = Report(Paths[1]);
        var glass = castle.materials.Single(x => x.sourceName == "彩窗");
        Check(glass.alphaMode == HoPmxAlphaMode.Transparent, "Castle stained glass keeps continuous alpha");
        Check(Mathf.Abs(glass.material.GetColor("_Color").a - 0.6f) < 0.001f && glass.material.GetFloat("_ZWrite") == 0, "Castle glass alpha/state are transparent");
        Check(castle.materials.All(x => x.material.GetFloat("_Cull") == 0), "Castle source double-sided flags are used");
        var circus = Report(Paths[0]);
        Check(circus.materials.Count(x => x.material.GetFloat("_UseOutline") > 0.5f) == 27, "Circus 27 source outlines are enabled");
        var mountain = Report(Paths[2]);
        Check(!string.IsNullOrEmpty(mountain.rayMappingPath), "Cloud mountain EMD mapping discovered");
        Check(mountain.materials.Count(x => x.material.GetTexture("_BumpMap") != null) >= 21, "Cloud mountain enabled Ray normal maps imported");
        Check(mountain.materials.Count(x => x.material.GetTexture("_MetallicGlossMap") != null) >= 20, "Cloud mountain enabled Ray metallic maps imported");
        Check(mountain.materials.Any(x => x.material.GetTexture("_MetallicGlossMap") != null && x.material.GetFloat("_Metallic") > 0), "Ray metallic maps have a nonzero multiplier");
        Check(mountain.materials.All(x => x.material.GetFloat("_UseEmission") == 0), "Disabled Ray emissive placeholders do not enable emission");
        var hall = Report(Paths[3]);
        Check(hall.materials.Single(x => x.sourceName == "窗纸半透明").alphaMode == HoPmxAlphaMode.Transparent, "Hall window-paper texture keeps continuous alpha");
        Check(glass.material.GetFloat("_Cutoff") < 0.01f &&
              hall.materials.Single(x => x.sourceName == "窗纸半透明").material.GetFloat("_Cutoff") < 0.01f,
            "Transparent lilToon pass does not clip away source alpha below 0.5");
    }

    private static void InspectorAndExtractionChecks()
    {
        const string path = "Assets/Samples/血月古堡/1.pmx";
        var importer = (HoPmxImporter)AssetImporter.GetAtPath(path);
        var editor = UnityEditor.Editor.CreateEditor(importer) as ScriptedImporterEditor;
        var assetEditor = UnityEditor.Editor.CreateEditor(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        Check(editor != null && editor.GetType().Name == "HoPmxImporterEditor", "PMX has a native custom importer Inspector");
        typeof(AssetImporterEditor).GetMethod("InternalSetAssetImporterTargetEditor", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(editor, new object[] { assetEditor });
        var serialized = new SerializedObject(importer);
        var scale = serialized.FindProperty("scale");
        Check(scale != null, "Scale setting is serialized in importer");
        float originalScale = scale.floatValue;
        scale.floatValue = originalScale * 2;
        serialized.ApplyModifiedProperties();
        Check(editor.HasModified(), "Inspector setting changes participate in Apply/Revert");
        typeof(AssetImporterEditor).GetMethod("ResetValues", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(editor, null);
        Check(!editor.HasModified() && Mathf.Abs(importer.scale - originalScale) < 0.000001f, "Native Revert restores PMX import scale");
        Object.DestroyImmediate(editor);
        Object.DestroyImmediate(assetEditor);

        const string materialFolder = "Assets/ExtractedCastleMaterials";
        if (!AssetDatabase.IsValidFolder(materialFolder)) AssetDatabase.CreateFolder("Assets", "ExtractedCastleMaterials");
        HoPmxMaterialExtraction.Extract(path, materialFolder);
        var extracted = Report(path).materials.Select(x => x.material).ToArray();
        Check(extracted.Length == 15 && extracted.All(x => AssetDatabase.GetAssetPath(x).StartsWith(materialFolder + "/", StringComparison.Ordinal)), "Extract Materials creates and remaps all source slots");
        var selected = extracted[5];
        string externalPath = AssetDatabase.GetAssetPath(selected);
        selected.SetColor("_Color", new Color(0.2f, 0.4f, 0.6f, 0.35f));
        EditorUtility.SetDirty(selected);
        AssetDatabase.SaveAssets();
        importer = (HoPmxImporter)AssetImporter.GetAtPath(path);
        importer.scale = originalScale * 2;
        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();
        var after = Report(path).materials[5].material;
        Check(AssetDatabase.GetAssetPath(after) == externalPath && Mathf.Abs(after.GetColor("_Color").a - 0.35f) < 0.001f, "Reimport keeps extracted material reference and user edit");
        importer = (HoPmxImporter)AssetImporter.GetAtPath(path);
        importer.scale = originalScale;
        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();
    }

    private static void MorphBakeChecks()
    {
        const string path = "Assets/Samples/朱木古堂/主体.pmx";
        var importer = (HoPmxImporter)AssetImporter.GetAtPath(path);
        var before = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<MeshFilter>(true)
            .ToDictionary(x => x.name, x => x.sharedMesh.uv);
        var morph = Report(path).morphs.Single(x => x.sourceName == "透→白");
        importer.morphSettings = new List<HoPmxMorphSetting> { new HoPmxMorphSetting { morphIndex = morph.index, weight = 1 } };
        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();
        var after = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<MeshFilter>(true);
        Check(after.Any(x => !x.sharedMesh.uv.SequenceEqual(before[x.name])), "Hall window-paper UV morph can bake its selected state");
        importer = (HoPmxImporter)AssetImporter.GetAtPath(path);
        importer.morphSettings.Clear();
        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();
        Check(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<MeshFilter>(true)
            .All(x => x.sharedMesh.uv.SequenceEqual(before[x.name])), "Clearing the baked UV morph restores source UVs");
    }

    private static void DataTextureExtractionChecks()
    {
        const string path = "Assets/Samples/云景千山/云景千山v2.pmx";
        const string folder = "Assets/ExtractedMountainMaterials";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "ExtractedMountainMaterials");
        HoPmxMaterialExtraction.Extract(path, folder);
        var normalPaths = new Dictionary<int, string>();
        var metalPaths = new Dictionary<int, string>();
        foreach (var entry in Report(path).materials)
        {
            var normal = entry.material.GetTexture("_BumpMap");
            var metal = entry.material.GetTexture("_MetallicGlossMap");
            if (normal != null) normalPaths[entry.index] = AssetDatabase.GetAssetPath(normal);
            if (metal != null) metalPaths[entry.index] = AssetDatabase.GetAssetPath(metal);
        }
        Check(normalPaths.Count >= 21 && metalPaths.Count >= 20 &&
              normalPaths.Values.Concat(metalPaths.Values).All(x => x.StartsWith(folder + "/", StringComparison.Ordinal) && x.EndsWith(".asset", StringComparison.Ordinal)),
            "Extracted Ray normal/metallic data textures are independent linear Texture2D assets");
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        foreach (var entry in Report(path).materials)
        {
            if (normalPaths.TryGetValue(entry.index, out string normalPath))
                Check(entry.material.GetTexture("_BumpMap") != null && AssetDatabase.GetAssetPath(entry.material.GetTexture("_BumpMap")) == normalPath,
                    "Reimport keeps extracted normal texture for source material " + entry.index);
            if (metalPaths.TryGetValue(entry.index, out string metalPath))
                Check(entry.material.GetTexture("_MetallicGlossMap") != null && AssetDatabase.GetAssetPath(entry.material.GetTexture("_MetallicGlossMap")) == metalPath,
                    "Reimport keeps extracted metallic texture for source material " + entry.index);
        }
    }

    public static void RenderSmoke()
    {
        Directory.CreateDirectory(Results);
        var owned = new List<Object>();
        string imageDirectory = Path.Combine(Results, "render-" + QualitySettings.activeColorSpace + "-" + SystemInfo.graphicsDeviceType);
        Directory.CreateDirectory(imageDirectory);
        File.WriteAllText(Path.Combine(imageDirectory, "render-results.txt"), "RUNNING " + DateTime.UtcNow.ToString("O"));
        try
        {
            Check(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "Visual validation uses a real graphics device");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
            owned.Add(rendererData);
            var pipeline = UniversalRenderPipelineAsset.Create(rendererData);
            pipeline.supportsHDR = true;
            pipeline.msaaSampleCount = 1;
            owned.Add(pipeline);
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.5f);
            var sunObject = new GameObject("Validation Sun");
            var sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.3f;
            sunObject.transform.rotation = Quaternion.Euler(45, -30, 0);
            owned.Add(sunObject);
            for (int i = 0; i < 4; ++i)
            {
                var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Paths[i]));
                owned.Add(root);
                var renderers = root.GetComponentsInChildren<Renderer>().Where(x =>
                    !x.name.EndsWith("_天空", StringComparison.Ordinal)).ToArray();
                // The castle's authored sky is a surrounding opaque mesh; inspecting it
                // from outside only proves the sphere renders. Hide it in this fixture.
                foreach (var sky in root.GetComponentsInChildren<Renderer>().Except(renderers)) sky.enabled = false;
                Bounds bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                // Whole-scene views are visual smoke evidence, not a comparison to author renders.
                var cameraObject = new GameObject("Validation Camera");
                owned.Add(cameraObject);
                var camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.075f, 0.09f, 0.12f, 1);
                camera.fieldOfView = 42;
                float radius = Mathf.Max(0.1f, bounds.extents.magnitude);
                Vector3 direction = new Vector3(0.2f, 0.35f, -1).normalized;
                camera.transform.position = bounds.center + direction * radius * 2.6f;
                camera.transform.LookAt(bounds.center);
                camera.nearClipPlane = radius * 0.001f;
                camera.farClipPlane = radius * 10;
                var target = new RenderTexture(960, 640, 24, RenderTextureFormat.ARGB32);
                owned.Add(target);
                camera.targetTexture = target;
                var previous = RenderTexture.active;
                var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                owned.Add(image);
                root.SetActive(false);
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply();
                var emptyPixels = image.GetPixels32();
                root.SetActive(true);
                for (int frame = 0; frame < 3; ++frame) camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply();
                RenderTexture.active = previous;
                string label = new[] { "circus", "castle", "mountain", "hall" }[i];
                File.WriteAllBytes(Path.Combine(imageDirectory, label + ".png"), image.EncodeToPNG());
                var pixels = image.GetPixels32();
                int magenta = pixels.Count(p => p.r > 230 && p.g < 25 && p.b > 230);
                Check(magenta < pixels.Length / 100, label + ": render contains no substantial error-shader magenta");
                int changed = pixels.Select((pixel, index) =>
                    Math.Abs(pixel.r - emptyPixels[index].r) + Math.Abs(pixel.g - emptyPixels[index].g) + Math.Abs(pixel.b - emptyPixels[index].b))
                    .Count(delta => delta > 8);
                Check(changed > pixels.Length / 100, label + ": rendered geometry changes more than one percent of the empty frame");
                root.SetActive(false);
                cameraObject.SetActive(false);
            }
            File.WriteAllText(Path.Combine(imageDirectory, "render-results.txt"), "PASS\n" + string.Join("\n", Passed));
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            File.WriteAllText(Path.Combine(imageDirectory, "render-results.txt"), "FAIL\n" + exception);
            EditorApplication.Exit(1);
        }
        finally
        {
            GraphicsSettings.defaultRenderPipeline = null;
            QualitySettings.renderPipeline = null;
            for (int i = owned.Count - 1; i >= 0; --i) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
        }
    }
}
