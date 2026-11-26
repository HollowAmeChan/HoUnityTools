using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.MaterialMigration
{
    // Explicit CLI orchestration. Every batch retains the converter's own review and journal guards.
    public static class HoMaterialMigrationBatch
    {
        [Serializable] public sealed class Manifest { public string projectRoot; public Batch[] batches; }
        [Serializable] public sealed class Batch { public string batchId, plan, report, previewRoot, reviewedPlan, backup; }
        [Serializable] public sealed class Validation { public string projectRoot; public int batches, materials, forks, wrappers, intents, subdivisions; }

        public static void RunPreviewAll()
        {
            foreach (var batch in Read().batches)
            {
                var report = HoMaterialMigration.Preview(batch.plan, batch.previewRoot, batch.report);
                Debug.Log($"HO_BATCH_PREVIEW {batch.batchId} proposals={report.proposals.Length} blocked={report.proposals.Count(p => p.blockers.Length != 0)}");
                if (report.proposals.Any(p => p.blockers.Length != 0) || (report.referenceOwners?.blockers.Length ?? 0) != 0)
                    throw new InvalidOperationException("Batch preview has blockers: " + batch.batchId);
            }
            Debug.Log("HO_BATCH_PREVIEW_ALL_OK");
        }

        public static void RunApplyAll()
        {
            var manifest = Read();
            // Refuse a partial fresh run before writing any batch; resumption needs an explicitly selected manifest.
            foreach (var batch in manifest.batches)
                if (!File.Exists(batch.reviewedPlan) || !File.Exists(batch.report) || Directory.Exists(batch.backup) || File.Exists(batch.backup))
                    throw new InvalidOperationException("Missing reviewed preview or non-new journal directory: " + batch.batchId);
            foreach (var batch in manifest.batches)
            {
                HoMaterialMigration.Apply(batch.reviewedPlan, batch.report, batch.backup);
                Debug.Log("HO_BATCH_APPLY_OK " + batch.batchId);
            }
            Debug.Log("HO_BATCH_APPLY_ALL_OK");
        }

        public static void RunValidateAll()
        {
            var manifest = Read();
            string output = ExternalNewOutput();
            var result = new Validation { projectRoot = manifest.projectRoot };
            foreach (var batch in manifest.batches)
            {
                var plan = JsonUtility.FromJson<HoMaterialMigrationPlan>(File.ReadAllText(batch.reviewedPlan));
                var preview = JsonUtility.FromJson<HoMaterialMigration.PreviewReport>(File.ReadAllText(batch.report));
                var journal = JsonUtility.FromJson<HoMaterialMigration.Journal>(File.ReadAllText(Path.Combine(batch.backup, "journal.json")));
                HoMaterialMigration.ValidateAppliedPlan(plan, preview, journal);
                if (!SameRoot(plan.projectRoot, manifest.projectRoot) || !SameRoot(preview.projectRoot, manifest.projectRoot) || !SameRoot(journal.projectRoot, manifest.projectRoot)
                    || plan.batchId != batch.batchId || preview.batchId != batch.batchId || preview.previewRoot != batch.previewRoot || journal.state != "Verified"
                    || journal.inputFingerprint != preview.inputFingerprint)
                    throw new InvalidOperationException("Journal/preview identity mismatch: " + batch.batchId);
                foreach (var record in journal.originals) RequireHash(record.path, record.afterSha256);
                foreach (var record in journal.created) RequireHash(record.path, record.sha256);
                foreach (var record in journal.readOnlyDependencies) RequireHash(record.path, record.sha256);
                Material Target(string guid, string family)
                {
                    var row = plan.rows.Single(r => r.sourceGuid == guid);
                    string path = row.targetFamily == family ? row.assetPath : plan.slotActions.Where(a => a.targetSourceGuid == guid && a.targetFamily == family)
                        .Select(a => a.targetAssetPath).Distinct().Single();
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    var proposed = preview.proposals.Single(p => p.sourceGuid == guid && p.targetFamily == family);
                    if (material == null || material.shader == null || AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(material.shader)) != proposed.shaderGuid)
                        throw new InvalidOperationException("Saved family shader mismatch: " + path);
                    HoMaterialMigration.ValidateAppliedMaterial(proposed, material, journal.finalRoot);
                    if (material.GetTexturePropertyNames().Any(p => manifest.batches.Any(b => AssetDatabase.GetAssetPath(material.GetTexture(p)).StartsWith(b.previewRoot + "/", StringComparison.Ordinal))))
                        throw new InvalidOperationException("Saved material still depends on a preview resource: " + path);
                    return material;
                }
                foreach (var row in plan.rows)
                {
                    var material = Target(row.sourceGuid, row.targetFamily);
                    if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material, out string guid, out long localId) || guid != row.sourceGuid || localId != row.sourceLocalId)
                        throw new InvalidOperationException("Original material identity changed: " + row.assetPath);
                    result.materials++;
                }
                foreach (var proposal in preview.proposals.Where(p => p.targetFamily != plan.rows.Single(r => r.sourceGuid == p.sourceGuid).targetFamily))
                { Target(proposal.sourceGuid, proposal.targetFamily); result.forks++; }
                if (preview.referenceOwners != null)
                {
                    var owners = HoMigrationReferenceOwners.ValidateAfter(preview.referenceOwners, Target);
                    if (owners.blockers.Length != 0 || owners.verifiedIntentCount != plan.slotActions.Length)
                        throw new InvalidOperationException("Saved owner validation failed: " + string.Join("; ", owners.blockers));
                    result.intents += owners.verifiedIntentCount;
                    if (plan.createModelWrappers)
                    {
                        var wrappers = HoMigrationReferenceOwners.ValidateWrappersAfter(preview.referenceOwners, preview.wrapperRecipes, Target);
                        if (wrappers.blockers.Length != 0) throw new InvalidOperationException(string.Join("; ", wrappers.blockers));
                        result.wrappers += wrappers.verifiedWrapperCount; result.subdivisions += wrappers.verifiedSubdivisionCount;
                    }
                }
                result.batches++;
            }
            File.WriteAllText(output, JsonUtility.ToJson(result, true));
            Debug.Log($"HO_BATCH_RELOAD_OK batches={result.batches} materials={result.materials} forks={result.forks} wrappers={result.wrappers} intents={result.intents} subdivisions={result.subdivisions}");
        }

        public static void RunDeletePreviews()
        {
            var manifest = Read();
            const string prefix = "Assets/__HoMaterialMigrationPreview/";
            var expected = new HashSet<string>(StringComparer.Ordinal);
            var materials = new HashSet<string>(StringComparer.Ordinal);
            foreach (var batch in manifest.batches)
            {
                if (!batch.previewRoot.StartsWith(prefix, StringComparison.Ordinal) || batch.previewRoot != prefix + batch.batchId)
                    throw new InvalidOperationException("Cleanup only accepts the dedicated per-batch preview directory.");
                var plan = JsonUtility.FromJson<HoMaterialMigrationPlan>(File.ReadAllText(batch.reviewedPlan));
                var preview = JsonUtility.FromJson<HoMaterialMigration.PreviewReport>(File.ReadAllText(batch.report));
                var journal = JsonUtility.FromJson<HoMaterialMigration.Journal>(File.ReadAllText(Path.Combine(batch.backup, "journal.json")));
                HoMaterialMigration.ValidateAppliedPlan(plan, preview, journal);
                foreach (var proposal in preview.proposals)
                {
                    expected.Add(proposal.assetPath);
                    foreach (var resource in proposal.resources) expected.Add(resource.assetPath);
                }
                foreach (var row in plan.rows) materials.Add(row.assetPath);
                foreach (var action in plan.slotActions) if (!string.IsNullOrEmpty(action.targetAssetPath)) materials.Add(action.targetAssetPath);
            }
            if (AssetDatabase.GetDependencies(materials.ToArray(), true).Any(p => p.StartsWith(prefix, StringComparison.Ordinal)))
                throw new InvalidOperationException("Production still references a temporary preview.");
            foreach (string path in AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith(prefix, StringComparison.Ordinal) && !AssetDatabase.IsValidFolder(p)))
                if (!expected.Contains(path)) throw new InvalidOperationException("Unplanned asset inside preview directory; preserve and review: " + path);
            foreach (var batch in manifest.batches)
                if (!AssetDatabase.DeleteAsset(batch.previewRoot)) throw new IOException("Could not delete preview " + batch.previewRoot);
            string parent = prefix.TrimEnd('/');
            if (AssetDatabase.GetAllAssetPaths().Any(p => p.StartsWith(prefix, StringComparison.Ordinal))) throw new IOException("Preview root is not empty after cleanup.");
            if (!AssetDatabase.DeleteAsset(parent)) throw new IOException("Could not delete empty preview parent.");
            Debug.Log("HO_BATCH_PREVIEW_CLEANUP_OK");
        }

        public static void RunFinalAudit()
        {
            if (UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().Any(p => p.name == "jp.lilxyzw.liltoon"))
                throw new InvalidOperationException("The legacy shader package is still installed.");
            HoUrpDefaultMaterialMigration.RunVerify();
            HoRetainedMaterialInputsMigration.RunVerify();
            string output = Environment.GetEnvironmentVariable("HO_NATIVE_MATERIAL_AUDIT_OUTPUT") ?? throw new InvalidOperationException("Set HO_NATIVE_MATERIAL_AUDIT_OUTPUT.");
            var report = HoNativeMaterialReferenceAudit.Write(output);
            if (report.errors.Count != 0 || report.materials.Any(m => m.legacy)) throw new InvalidOperationException("Final native audit contains errors or legacy materials.");
            int legacyPmx = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) && p.EndsWith(".pmx", StringComparison.OrdinalIgnoreCase))
                .Count(p => AssetImporter.GetAtPath(p) is Hollow.HoUnityTools.Editor.Pmx.HoPmxImporter importer && importer.materialProfile == Hollow.HoUnityTools.Editor.Pmx.HoPmxMaterialProfile.LilToon);
            if (legacyPmx != 0) throw new InvalidOperationException("Legacy PMX profiles remain: " + legacyPmx);
            Debug.Log($"HO_MATERIAL_PLATFORM_FINAL_OK materials={report.materials.Count} legacy=0 pmxLegacy=0");
        }

        private static Manifest Read()
        {
            string path = Environment.GetEnvironmentVariable("HO_MATERIAL_MIGRATION_BATCHES");
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(path ?? throw new InvalidOperationException("Set HO_MATERIAL_MIGRATION_BATCHES.")));
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (manifest == null || Path.GetFullPath(manifest.projectRoot) != root || manifest.batches == null || manifest.batches.Length == 0
                || manifest.batches.Select(b => b.batchId).Distinct().Count() != manifest.batches.Length)
                throw new InvalidOperationException("Invalid batch manifest/project identity.");
            return manifest;
        }
        private static string ExternalNewOutput()
        {
            string path = Path.GetFullPath(Environment.GetEnvironmentVariable("HO_MATERIAL_MIGRATION_BATCH_RESULT") ?? throw new InvalidOperationException("Set HO_MATERIAL_MIGRATION_BATCH_RESULT."));
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (File.Exists(path) || Directory.Exists(path) || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Result must be a new external file.");
            return path;
        }
        private static void RequireHash(string path, string expected)
        {
            string absolute = Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..")) + Path.DirectorySeparatorChar;
            if (!absolute.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(absolute)) throw new InvalidOperationException("Missing journal file: " + path);
            using var sha = SHA256.Create();
            if (BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(absolute))).Replace("-", "").ToLowerInvariant() != expected)
                throw new InvalidOperationException("Post-migration file changed: " + path);
        }
        private static bool SameRoot(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }
}
