using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

// Read-only native migration inventory. No material UPM dependency.
namespace Hollow.HoUnityTools.Editor.MaterialMigration
{
public static class HoNativeMaterialReferenceAudit
{
    [Serializable] public sealed class Report
    {
        public int version = 1;
        public string project, unity;
        public string[] packages;
        public List<MaterialRow> materials = new();
        public List<Slot> rendererSlots = new();
        public List<Remap> importerRemaps = new();
        public List<Curve> materialCurves = new();
        public List<string> inspectedScenes = new(), inspectedPrefabs = new(), inspectedModels = new(), inspectedClips = new(), errors = new();
        public string limitations = "Native serialized/imported asset resolution; no runtime material generation, scripts/MPB effects or visual equivalence proof. Preview scenes are closed without saving. Missing GUID strings require the separate text inventory.";
    }
    [Serializable] public sealed class Reference
    {
        public string path, guid, name;
        public long localId;
        public bool persistent;
        public int instanceId;
    }
    [Serializable] public sealed class MaterialRow
    {
        public Reference asset, shader;
        public bool legacy, embedded, missingShader;
        public string shaderName;
    }
    [Serializable] public sealed class Slot
    {
        public string container, rendererPath, rendererGlobalId, rendererType, correspondingPrefab;
        public int componentIndex, slot;
        public Reference material;
    }
    [Serializable] public sealed class Remap
    {
        public string model, importerType, sourceType, sourceName;
        public Reference target;
    }
    [Serializable] public sealed class Curve
    {
        public Reference clip;
        public string targetPath, targetType, property;
        public bool objectReference;
        public List<Reference> materialKeys = new();
    }
    private static readonly HashSet<string> Legacy = new(StringComparer.Ordinal)
    {
        "lilToon", "Hidden/lilToonOutline", "Hidden/lilToonCutout", "Hidden/lilToonCutoutOutline",
        "Hidden/lilToonTransparent", "Hidden/lilToonTransparentOutline",
        "Hidden/lilToonOnePassTransparent", "Hidden/lilToonOnePassTransparentOutline",
        "Hidden/lilToonTwoPassTransparent", "Hidden/lilToonTwoPassTransparentOutline"
    };
    private static readonly HashSet<string> Models = new(StringComparer.OrdinalIgnoreCase)
    { ".fbx", ".pmx", ".pmd", ".obj", ".dae", ".blend", ".3ds", ".gltf", ".glb" };

    // CLI: -executeMethod Hollow.HoUnityTools.Editor.MaterialMigration.HoNativeMaterialReferenceAudit.Run
    // Supply HO_NATIVE_MATERIAL_AUDIT_OUTPUT outside the project.
    public static void Run()
    {
        string output = Environment.GetEnvironmentVariable("HO_NATIVE_MATERIAL_AUDIT_OUTPUT");
        if (string.IsNullOrEmpty(output)) throw new InvalidOperationException("Set HO_NATIVE_MATERIAL_AUDIT_OUTPUT to a new external JSON path.");
        Report report = Write(output);
        if (report.errors.Count != 0)
            throw new InvalidOperationException("Native audit incomplete; inspect errors in " + output);
    }

    public static Report Write(string output)
    {
        string absolute = Path.GetFullPath(output);
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        if (absolute.StartsWith(root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || File.Exists(absolute))
            throw new InvalidOperationException("Audit report must be a new file outside the project.");
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Run the audit after compilation/import, outside Play Mode.");
        var report = new Report { project = root, unity = Application.unityVersion,
            packages = PackageInfo.GetAllRegisteredPackages().Select(p => p.name + "@" + p.version + "|" + p.resolvedPath).OrderBy(x => x).ToArray() };
        var materials = new HashSet<string>(StringComparer.Ordinal);
        var clips = new HashSet<string>(StringComparer.Ordinal);
        string[] paths = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/", StringComparison.Ordinal)
            || p.StartsWith("Packages/", StringComparison.Ordinal) && IsEmbeddedPackage(p)).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        foreach (string path in paths.Where(p => p.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith(".anim", StringComparison.OrdinalIgnoreCase) || Models.Contains(Path.GetExtension(p))))
        {
            try
            {
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is Material material) AddMaterial(material);
                    if (asset is AnimationClip clip) AddClip(clip);
                }
                if (!Models.Contains(Path.GetExtension(path))) continue;
                report.inspectedModels.Add(path);
                var modelRoot = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (modelRoot != null) AddRenderers(path, modelRoot.GetComponentsInChildren<Renderer>(true));
                var importer = AssetImporter.GetAtPath(path);
                if (importer == null) continue;
                foreach (var pair in importer.GetExternalObjectMap())
                    if (pair.Key.type == typeof(Material) || pair.Value is Material)
                    {
                        report.importerRemaps.Add(new Remap { model = path, importerType = importer.GetType().FullName,
                            sourceType = pair.Key.type?.FullName, sourceName = pair.Key.name, target = Ref(pair.Value) });
                        if (pair.Value is Material remapped) AddMaterial(remapped);
                    }
            }
            catch (Exception error) { report.errors.Add(path + ": " + error); }
        }
        foreach (string path in paths.Where(p => p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) throw new InvalidOperationException("Prefab could not be resolved.");
                AddRenderers(path, prefab.GetComponentsInChildren<Renderer>(true));
                report.inspectedPrefabs.Add(path);
            }
            catch (Exception error) { report.errors.Add(path + ": " + error); }
        }
        foreach (string path in paths.Where(p => p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)))
        {
            Scene preview = default;
            try
            {
                preview = EditorSceneManager.OpenPreviewScene(path);
                if (!preview.IsValid() || !preview.isLoaded) throw new InvalidOperationException("Scene could not be resolved.");
                foreach (GameObject obj in preview.GetRootGameObjects()) AddRenderers(path, obj.GetComponentsInChildren<Renderer>(true));
                report.inspectedScenes.Add(path);
            }
            catch (Exception error) { report.errors.Add(path + ": " + error); }
            finally { if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview); }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(absolute));
        File.WriteAllText(absolute, JsonUtility.ToJson(report, true));
        Debug.Log($"HO_NATIVE_MATERIAL_AUDIT materials={report.materials.Count} slots={report.rendererSlots.Count} curves={report.materialCurves.Count} errors={report.errors.Count} output={absolute}");
        return report;

        void AddMaterial(Material material)
        {
            Reference id = Ref(material);
            if (!materials.Add(Key(id))) return;
            report.materials.Add(new MaterialRow { asset = id, shader = Ref(material.shader), shaderName = material.shader != null ? material.shader.name : null,
                legacy = material.shader != null && Legacy.Contains(material.shader.name), embedded = !id.path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase),
                missingShader = material.shader == null || material.shader.name == "Hidden/InternalErrorShader" });
        }
        void AddClip(AnimationClip clip)
        {
            Reference id = Ref(clip);
            if (!clips.Add(Key(id))) return;
            report.inspectedClips.Add(Key(id));
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                if (MaterialProperty(binding)) report.materialCurves.Add(Row(binding, false));
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                if (!MaterialProperty(binding) && !keys.Any(k => k.value is Material)) continue;
                var row = Row(binding, true);
                foreach (var key in keys) if (key.value is Material material) { row.materialKeys.Add(Ref(material)); AddMaterial(material); }
                report.materialCurves.Add(row);
            }
            Curve Row(EditorCurveBinding binding, bool objectReference) => new Curve { clip = id, targetPath = binding.path,
                targetType = binding.type?.FullName, property = binding.propertyName, objectReference = objectReference };
        }
        void AddRenderers(string container, IEnumerable<Renderer> renderers)
        {
            foreach (Renderer renderer in renderers)
            {
                Material[] slots = renderer.sharedMaterials;
                for (int slot = 0; slot < slots.Length; ++slot)
                {
                    Material material = slots[slot];
                    report.rendererSlots.Add(new Slot { container = container, rendererPath = Hierarchy(renderer.transform),
                        rendererGlobalId = GlobalObjectId.GetGlobalObjectIdSlow(renderer).ToString(), rendererType = renderer.GetType().FullName,
                        componentIndex = Array.IndexOf(renderer.GetComponents<Renderer>(), renderer), slot = slot, material = Ref(material),
                        correspondingPrefab = AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(renderer)) });
                    if (material != null) AddMaterial(material);
                }
            }
        }
    }
    private static bool IsEmbeddedPackage(string path)
    {
        var info = PackageInfo.FindForAssetPath(path);
        return info != null && info.source == UnityEditor.PackageManager.PackageSource.Embedded;
    }
    private static bool MaterialProperty(EditorCurveBinding binding) => binding.propertyName.IndexOf("material", StringComparison.OrdinalIgnoreCase) >= 0;
    private static Reference Ref(Object obj)
    {
        if (obj == null) return null;
        bool persistent = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long localId);
        return new Reference { path = AssetDatabase.GetAssetPath(obj), guid = guid, localId = localId, name = obj.name,
            persistent = persistent, instanceId = persistent ? 0 : obj.GetInstanceID() };
    }
    private static string Key(Reference id) => id == null ? "null" : id.persistent ? id.guid + ":" + id.localId : "nonpersistent:" + id.instanceId;
    private static string Hierarchy(Transform transform)
    {
        var names = new List<string>();
        for (Transform current = transform; current != null; current = current.parent)
            names.Add(current.name + "[" + current.GetSiblingIndex() + "]");
        names.Reverse();return string.Join("/", names);
    }
}
}
