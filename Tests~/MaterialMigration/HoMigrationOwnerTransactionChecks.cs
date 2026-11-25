using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Hollow.HoUnityTools.Editor.MaterialMigration;
using Object = UnityEngine.Object;

// Disposable, no-lil host. A tiny named source fixture tests transaction identity,
// rather than shader compatibility or rendering. Production assets are never used.
public static class HoMigrationOwnerTransactionChecks
{
    static string folder, output, suffix;
    static int checks;
    static readonly FieldInfo Hook = typeof(HoMaterialMigration).GetField("FaultInjection", BindingFlags.Static | BindingFlags.NonPublic);

    public static void Run()
    {
        Check(!UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().Any(p=>p.name=="jp.lilxyzw.liltoon"),"synthetic source fixture requires a separate host without the legacy package");
        suffix = Environment.GetEnvironmentVariable("HO_REFERENCE_FIXTURE_SUFFIX") ?? "";
        Check(System.Text.RegularExpressions.Regex.IsMatch(suffix, "^[A-Za-z0-9_-]+$"), "explicit unique fixture suffix");
        folder = "Assets/OwnerTransactionFixture" + suffix;
        output = Environment.GetEnvironmentVariable("HO_MIGRATION_TEST_OUTPUT");
        Check(Application.isBatchMode && !AssetDatabase.IsValidFolder(folder) && !string.IsNullOrEmpty(output), "fresh private batch host");
        Directory.CreateDirectory(output);
        AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
        string shaderPath = folder + "/SyntheticLegacy.shader";
        File.WriteAllText(shaderPath, "Shader \"lilToon\" { Properties { _Color(\"Color\",Color)=(1,1,1,1) _MainTex(\"Base\",2D)=\"white\"{} _MainTexHSVG(\"HSVG\",Vector)=(0,1,1,1) _Metallic(\"Metal\",Float)=0 _Smoothness(\"Smooth\",Float)=0.5 _UseReflection(\"Reflection\",Float)=1 _ApplyReflection(\"Environment\",Float)=0 _SpecularStrength(\"Specular\",Float)=1 _Reflectance(\"F0\",Float)=0.04 } SubShader { Pass {} } }");
        AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceSynchronousImport);
        Check(Shader.Find("HoShader/Cloth URP") != null && Shader.Find("HoShader/Eye URP") != null, "dedicated target packages installed");
        PrefabTransaction(shaderPath);
        ModelTransaction(shaderPath);
        Debug.Log("HO_OWNER_TRANSACTIONS_OK checks=" + checks);
    }

    static void PrefabTransaction(string shaderPath)
    {
        string materialPath = folder + "/PrefabSource.mat", forkPath = folder + "/PrefabForks/Nested/Eye.mat";
        var source = CreateSource(materialPath, shaderPath);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = GameObject.CreatePrimitive(PrimitiveType.Cube);root.name = "Shared";root.GetComponent<Renderer>().sharedMaterial = source;
        string prefabPath = folder + "/Shared.prefab", inheritedPath = folder + "/Inherited.unity", retainPath = folder + "/Retain.unity";
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);Object.DestroyImmediate(root);
        PrefabUtility.InstantiatePrefab(prefab, scene);EditorSceneManager.SaveScene(scene, inheritedPath);
        scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        PrefabUtility.InstantiatePrefab(prefab, scene);EditorSceneManager.SaveScene(scene, retainPath);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var row = Row(materialPath, shaderPath);
        var plan = Plan("Prefab", row,
            Action(prefabPath, "Shared[0]", source, row, "Eye", forkPath),
            Action(inheritedPath, "Shared[0]", source, row, "Eye", forkPath),
            Action(retainPath, "Shared[0]", source, row, "Cloth", materialPath));
        var report = Preview(plan, "Prefab");
        Check(report.referenceOwners.intents.Length == 3 && report.referenceOwners.slotOwners.Length == 2, "main Preview folds expanded same-family consumers but retains opposite anchor");
        Check(report.referenceOwners.intents[0].ownerId == report.referenceOwners.intents[1].ownerId && report.referenceOwners.intents[0].ownerId != report.referenceOwners.intents[2].ownerId, "every original ownership intention survives folding");
        var baseline = Snapshot();
        string backup = output + "/prefab-backup";
        Reject(() => HoMaterialMigration.Apply(PlanPath("Prefab"), ReportPath("Prefab"), backup), "all original intents require review");
        Check(!Directory.Exists(backup), "review rejection precedes backup creation");Same(baseline, "review rejection is read-only");
        Approve(plan);Write(PlanPath("Prefab"), plan);
        var sourceImporter = AssetImporter.GetAtPath(materialPath);string originalUserData = sourceImporter.userData;
        try {
            sourceImporter.userData = "fixture pending importer setting";EditorUtility.SetDirty(sourceImporter);
            Reject(() => HoMaterialMigration.Apply(PlanPath("Prefab"), ReportPath("Prefab"), output + "/dirty-source-importer-backup"), "Apply rejects unsaved source material importer settings");
            Check(!Directory.Exists(output + "/dirty-source-importer-backup") && sourceImporter != null && EditorUtility.IsDirty(sourceImporter) && sourceImporter.userData == "fixture pending importer setting", "source importer rejection precedes backup and retains native edit");
            Same(baseline, "source importer rejection writes no assets or metadata");
        }
        finally { sourceImporter.userData = originalUserData;EditorUtility.ClearDirty(sourceImporter); }
        HoMaterialMigration.Apply(PlanPath("Prefab"), ReportPath("Prefab"), backup);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        source = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        var fork = AssetDatabase.LoadAssetAtPath<Material>(forkPath);
        Check(source.shader.name == "HoShader/Cloth URP" && AssetDatabase.AssetPathToGUID(materialPath) == row.sourceGuid, "native anchor converted with original GUID");
        Check(fork != null && fork.shader.name == "HoShader/Eye URP", "native Eye fork created");
        Inspect(inheritedPath, r => {
            Check(r.sharedMaterial == fork, "inherited scene reload resolves prefab fork");
            Check(!(PrefabUtility.GetPropertyModifications(r.gameObject) ?? Array.Empty<PropertyModification>()).Any(p => p.propertyPath.StartsWith("m_Materials", StringComparison.Ordinal)), "folded scene gains no redundant material override");
        });
        Inspect(retainPath, r => {
            Check(r.sharedMaterial == source, "conflicting scene reload retains original GUID and Cloth");
            Check((PrefabUtility.GetPropertyModifications(r.gameObject) ?? Array.Empty<PropertyModification>()).Any(p => p.propertyPath.StartsWith("m_Materials", StringComparison.Ordinal) && p.objectReference == source), "opposite retained intent creates necessary scene override");
        });
        var journal = Journal(backup);
        Check(!journal.originals.Any(f => f.path == inheritedPath || f.path == inheritedPath + ".meta"), "unchanged expanded scene is a read-only dependency, not a write owner");
        Check(journal.readOnlyDependencies.Any(f => f.path == inheritedPath) && journal.readOnlyDependencies.Any(f => f.path == inheritedPath + ".meta"), "folded dependency asset and meta remain guarded");
        HoMaterialMigration.Restore(backup);Same(baseline, "owner transaction Restore returns complete file/directory/meta set");
        Check(AssetDatabase.LoadAssetAtPath<Material>(materialPath).shader.name == "lilToon", "Restore reloads original native material");

        // A late reference failure must roll back both authoring owners, including
        // the retain-anchor override, rather than only restoring material files.
        plan.batchId = "OwnerReferenceFailure" + suffix;
        Preview(plan, "ReferenceFailure");baseline = Snapshot();bool reached = false;
        Hook.SetValue(null, (Action<string>)(stage => {
            if (stage != "after-references") return;
            reached = File.Exists(forkPath) && AssetDatabase.LoadAssetAtPath<Material>(materialPath).shader.name == "HoShader/Cloth URP";
            Inspect(retainPath, r => Check(r.sharedMaterial == AssetDatabase.LoadAssetAtPath<Material>(materialPath), "late failure reached saved retain override"));
            throw new InvalidOperationException("fixture failure after owner reference writes");
        }));
        string failedBackup = output + "/reference-failure-backup";
        try { Reject(() => HoMaterialMigration.Apply(PlanPath("ReferenceFailure"), ReportPath("ReferenceFailure"), failedBackup), "owner-reference fault rolls back"); }
        finally { Hook.SetValue(null, null); }
        Check(reached && Journal(failedBackup).state == "RolledBack", "late owner failure journal records rollback");
        Same(baseline, "late owner rollback restores every asset, ancestor and meta");
    }

    static void ModelTransaction(string shaderPath)
    {
        string materialPath = folder + "/ModelSource.mat", forkPath = folder + "/ModelForks/Nested/Eye.mat";
        var source = CreateSource(materialPath, shaderPath);
        string modelPath = folder + "/Exact.obj", mtlPath = folder + "/Exact.mtl";
        File.WriteAllText(mtlPath, "newmtl ExactSource\nKd 0.5 0.5 0.5\n");
        File.WriteAllText(modelPath, "mtllib Exact.mtl\no Surface\nv 0 0 0\nv 1 0 0\nv 0 1 0\nusemtl ExactSource\nf 1 2 3\n");
        AssetDatabase.ImportAsset(mtlPath, ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
        importer.materialLocation = ModelImporterMaterialLocation.InPrefab;importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.SaveAndReimport();
        importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "ExactSource"), source);importer.SaveAndReimport();
        var renderer = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath).GetComponentsInChildren<Renderer>(true).Single();
        Check(renderer.sharedMaterial == source, "real native importer source remap established");
        var row = Row(materialPath, shaderPath);
        var plan = Plan("Model", row, Action(modelPath, Hierarchy(renderer.transform), source, row, "Eye", forkPath));
        string alternatePath = folder + "/AlternateModelSource.mat";CreateSource(alternatePath, shaderPath);
        foreach (bool caseAlias in new[] { false, true })
        {
            var invalid = JsonUtility.FromJson<HoMaterialMigrationPlan>(JsonUtility.ToJson(plan));
            var conflicting = JsonUtility.FromJson<HoMaterialMigrationSlotAction>(JsonUtility.ToJson(invalid.slotActions[0]));
            conflicting.targetFamily = "Hair";
            if (caseAlias) conflicting.targetAssetPath = forkPath.Replace("/Eye.mat", "/EYE.mat");
            else {
                var alternate = Row(alternatePath, shaderPath);invalid.rows = new[] { invalid.rows[0], alternate };
                conflicting.targetSourceGuid = alternate.sourceGuid;
            }
            invalid.slotActions = new[] { invalid.slotActions[0], conflicting };
            string name = caseAlias ? "ConflictingRemapAlias" : "ConflictingRemapPath";
            Write(PlanPath(name), invalid);var unmodified = Snapshot();
            Reject(() => HoMaterialMigration.Preview(PlanPath(name), folder + "/" + name + "Preview", ReportPath(name)), "remap-only families cannot share a fork path, including case aliases");
            Check(!File.Exists(ReportPath(name)) && !AssetDatabase.IsValidFolder(folder + "/" + name + "Preview"), "fork identity conflict rejected before preview creation");
            Same(unmodified, "fork identity conflict leaves Assets unchanged");
        }
        var report = Preview(plan, "Model");
        Check(report.referenceOwners.slotOwners.Length == 0 && report.referenceOwners.remaps.Length == 1, "main Preview produces exact remap-only fork without fake writable model slot");
        var remap = report.referenceOwners.remaps[0];
        Check(remap.identifier.sourceName == "ExactSource" && remap.identifier.sourceType == typeof(Material).FullName && remap.identifier.provenance == "GetExternalObjectMap", "preview freezes actual typed external key");
        Approve(plan);Write(PlanPath("Model"), plan);
        var baseline = Snapshot();
        string backup = output + "/model-backup";
        importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);float scaleBeforeApply = importer.globalScale;
        try {
            importer.globalScale = scaleBeforeApply + .25f;EditorUtility.SetDirty(importer);
            Reject(() => HoMaterialMigration.Apply(PlanPath("Model"), ReportPath("Model"), output + "/dirty-apply-backup"), "Apply rejects unsaved native importer settings before transaction");
            Check(!Directory.Exists(output + "/dirty-apply-backup") && importer != null && EditorUtility.IsDirty(importer) && importer.globalScale == scaleBeforeApply + .25f, "dirty importer Apply rejection precedes backup and preserves native edit");
            Same(baseline, "dirty importer Apply rejection creates no output assets or parent metas");
        }
        finally { importer.globalScale = scaleBeforeApply;EditorUtility.ClearDirty(importer); }
        HoMaterialMigration.Apply(PlanPath("Model"), ReportPath("Model"), backup);
        var journal = Journal(backup);
        Check(journal.originals.Any(f => f.path == modelPath + ".meta") && !journal.originals.Any(f => f.path == modelPath), "only importer metadata is writable and backed up");
        Check(journal.readOnlyDependencies.Any(f => f.path == modelPath), "source model asset is guarded read-only dependency");
        renderer = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath).GetComponentsInChildren<Renderer>(true).Single();
        Check(renderer.sharedMaterial == AssetDatabase.LoadAssetAtPath<Material>(forkPath) && renderer.sharedMaterial.shader.name == "HoShader/Eye URP", "Apply reimport and reload bind final dedicated fork");

        var applied = Snapshot();string journalText = File.ReadAllText(backup + "/journal.json");
        byte[] modelBytes = File.ReadAllBytes(modelPath);File.AppendAllText(modelPath, "\n# later source edit\n");
        var edited = Snapshot();
        Reject(() => HoMaterialMigration.Restore(backup), "Restore rejects changed read-only model input");Same(edited, "dependency rejection performs no partial restore or owned deletion");
        Check(File.ReadAllText(backup + "/journal.json") == journalText, "dependency rejection preserves Verified journal");
        File.WriteAllBytes(modelPath, modelBytes);Same(applied, "fixture restores source bytes without reimporting settings");

        importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);float originalScale = importer.globalScale;
        try {
            importer.globalScale = originalScale + .125f;EditorUtility.SetDirty(importer);
            Check(EditorUtility.IsDirty(importer), "importer has real unsaved native setting change");
            Reject(() => HoMaterialMigration.Restore(backup), "Restore rejects unsaved remap importer settings");
            Check(importer != null && EditorUtility.IsDirty(importer) && importer.globalScale == originalScale + .125f, "rejected Restore preserves unsaved native importer edit");
            Same(applied, "dirty importer rejection performs no disk mutation");
            Check(File.ReadAllText(backup + "/journal.json") == journalText, "dirty importer rejection preserves Verified journal");
        }
        finally { importer.globalScale = originalScale;EditorUtility.ClearDirty(importer); }
        HoMaterialMigration.Restore(backup);Same(baseline, "exact importer Restore removes fork/folders and restores all original bytes");
        renderer = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath).GetComponentsInChildren<Renderer>(true).Single();
        Check(renderer.sharedMaterial == AssetDatabase.LoadAssetAtPath<Material>(materialPath) && renderer.sharedMaterial.shader.name == "lilToon", "native imported renderer reload returns original GUID and shader");

        plan.batchId = "OwnerRemapFailure" + suffix;
        Preview(plan, "RemapFailure");baseline = Snapshot();bool reached = false;
        Hook.SetValue(null, (Action<string>)(stage => {
            if (stage != "after-importer-remaps") return;
            var current = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath).GetComponentsInChildren<Renderer>(true).Single();
            reached = current.sharedMaterial == AssetDatabase.LoadAssetAtPath<Material>(forkPath) && !baseline.files[modelPath + ".meta"].Equals(Hash(modelPath + ".meta"), StringComparison.Ordinal);
            throw new InvalidOperationException("fixture failure after real importer metadata save and reimport");
        }));
        string failedBackup = output + "/remap-failure-backup";
        try { Reject(() => HoMaterialMigration.Apply(PlanPath("RemapFailure"), ReportPath("RemapFailure"), failedBackup), "exact remap hook failure rolls back"); }
        finally { Hook.SetValue(null, null); }
        Check(reached && Journal(failedBackup).state == "RolledBack", "remap fault reached real saved key and durable rollback");
        Same(baseline, "remap fault restores source/importer/fork metadata and entire file set");
        renderer = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath).GetComponentsInChildren<Renderer>(true).Single();
        Check(renderer.sharedMaterial == AssetDatabase.LoadAssetAtPath<Material>(materialPath), "failed remap rollback reimports original native binding");
    }

    static Material CreateSource(string path, string shaderPath)
    {
        var source = new Material(AssetDatabase.LoadAssetAtPath<Shader>(shaderPath));
        AssetDatabase.CreateAsset(source, path);AssetDatabase.SaveAssetIfDirty(source);return source;
    }
    static HoMaterialMigrationRow Row(string path, string shaderPath) => new() {
        assetPath = path, sourceGuid = AssetDatabase.AssetPathToGUID(path), sourceLocalId = 2100000,
        sourceSha256 = Hash(path), sourceMetaSha256 = Hash(path + ".meta"), sourceShaderPath = shaderPath,
        sourceShaderGuid = AssetDatabase.AssetPathToGUID(shaderPath), sourceShaderSha256 = Hash(shaderPath), targetFamily = "Cloth", requiresSlotFork = true
    };
    static HoMaterialMigrationPlan Plan(string name, HoMaterialMigrationRow row, params HoMaterialMigrationSlotAction[] actions) => new() {
        projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")), batchId = "Owner" + name + suffix,
        rows = new[] { row }, slotActions = actions, resolveReferenceOwners = true, retainMixedModelAnchors = true
    };
    static HoMaterialMigrationSlotAction Action(string path, string hierarchy, Material source, HoMaterialMigrationRow row, string family, string target)
    {
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long id);
        return new HoMaterialMigrationSlotAction { kind = path.EndsWith(".obj") ? "model-remap-review" : family == "Cloth" ? "retain-anchor" : "assign-fork",
            containerPath = path, containerGuid = AssetDatabase.AssetPathToGUID(path), containerSha256 = Hash(path), containerMetaSha256 = Hash(path + ".meta"),
            rendererPath = hierarchy, rendererType = typeof(MeshRenderer).FullName, componentIndex = 0, slot = 0,
            expectedMaterialGuid = guid, expectedMaterialLocalId = id, targetSourceGuid = row.sourceGuid, targetFamily = family, targetAssetPath = target };
    }
    static HoMaterialMigration.PreviewReport Preview(HoMaterialMigrationPlan plan, string name)
    {
        Write(PlanPath(name), plan);var report = HoMaterialMigration.Preview(PlanPath(name), folder + "/" + name + "Preview", ReportPath(name));
        Check(report.referenceOwners != null && report.referenceOwners.blockers.Length == 0, "preview native ownership has no blockers: " + string.Join(";", report.referenceOwners?.blockers ?? Array.Empty<string>()));
        Check(report.proposals.Length == 2 && report.proposals.All(p => p.blockers.Length == 0), "anchor/fork converters have no blockers: " + string.Join(";", report.proposals.SelectMany(p => p.blockers)));
        return report;
    }
    static void Approve(HoMaterialMigrationPlan plan) { foreach (var row in plan.rows) { row.reviewed = true;row.acceptFamilyModelChanges = true; }foreach (var action in plan.slotActions) action.reviewed = true; }
    static string PlanPath(string name) => output + "/" + name + "-plan.json";
    static string ReportPath(string name) => output + "/" + name + "-preview.json";
    static HoMaterialMigration.Journal Journal(string backup) => JsonUtility.FromJson<HoMaterialMigration.Journal>(File.ReadAllText(backup + "/journal.json"));
    static void Inspect(string path, Action<Renderer> visit) { var scene = EditorSceneManager.OpenPreviewScene(path);try { visit(scene.GetRootGameObjects().Single().GetComponent<Renderer>()); }finally { EditorSceneManager.ClosePreviewScene(scene); } }
    static string Hierarchy(Transform node) { var names = new List<string>();for (; node != null;node = node.parent) names.Add(node.name + "[" + node.GetSiblingIndex() + "]");names.Reverse();return string.Join("/", names); }
    sealed class AssetSnapshot { public Dictionary<string, string> files;public string[] directories; }
    static AssetSnapshot Snapshot() => new() { files = Directory.GetFiles("Assets", "*", SearchOption.AllDirectories).ToDictionary(p => p.Replace('\\', '/'), Hash, StringComparer.Ordinal), directories = Directory.GetDirectories("Assets", "*", SearchOption.AllDirectories).Select(p => p.Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal).ToArray() };
    static void Same(AssetSnapshot before, string label) { var after = Snapshot();Check(before.files.Count == after.files.Count && before.files.All(p => after.files.TryGetValue(p.Key, out var hash) && p.Value == hash) && before.directories.SequenceEqual(after.directories), label + "; changed=" + string.Join(",", before.files.Keys.Intersect(after.files.Keys).Where(p => before.files[p] != after.files[p]).Take(8)) + "; added=" + string.Join(",", after.files.Keys.Except(before.files.Keys).Take(8))); }
    static void Reject(Action action, string label) { bool rejected = false;try { action(); }catch (Exception error) { Debug.Log("EXPECTED_OWNER_REJECTION " + label + ": " + error.Message);rejected = true; }Check(rejected, label); }
    static void Check(bool value, string label) { checks++;if (!value) throw new InvalidOperationException("Owner transaction: " + label); }
    static void Write(string path, object value) => File.WriteAllText(path, JsonUtility.ToJson(value, true));
    static string Hash(string path) { using var hash = SHA256.Create();return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
}
