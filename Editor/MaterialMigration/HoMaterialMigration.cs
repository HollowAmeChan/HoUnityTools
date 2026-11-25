using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Hollow.HoUnityTools.Editor.MaterialMigration
{
    // An explicit asset operation, never invoked on import, inspector repaint or startup.
    public static class HoMaterialMigration
    {
        internal static Action<string> FaultInjection;
        [Serializable] public sealed class PreviewReport
        {
            public int schemaVersion = 1;
            public string projectRoot, batchId, inputFingerprint, previewRoot;
            public HoMaterialMigrationPlan sourcePlan;
            public HoMigrationReferenceOwners.Report referenceOwners;
            public Proposal[] proposals;
            public string[] retirementNotes;
        }
        [Serializable] public sealed class Proposal
        {
            public string sourceGuid, targetFamily, assetPath, sha256, metaSha256;
            public string shaderGuid, shaderDependencyHash, sourceShaderDependencyHash;
            public string[] differences, blockers;
            public Property[] sourceInputs, proposedInputs;
            public MaterialState sourceState, proposedState;
            public Artifact[] resources;
        }
        [Serializable] public sealed class Artifact
        {
            public string assetPath, guid, sha256, metaSha256;
        }
        [Serializable] public sealed class Property
        {
            public string name, type, textureGuid, texturePath;
            public string dependencyHash, textureSha256, textureMetaSha256;
            public long textureLocalId;
            public float scalar;
            public Vector4 value;
            public Vector2 scale, offset;
        }
        [Serializable] public sealed class MaterialState
        {
            public int queue, giFlags;
            public bool instancing, doubleSidedGI;
            public string[] keywords, disabledPassNames, metadata;
        }
        [Serializable] public sealed class Journal
        {
            public int schemaVersion = 1;
            public string projectRoot, state, finalRoot, inputFingerprint;
            public FileRecord[] originals;
            public CreatedFile[] created;
            public CreatedFile[] readOnlyDependencies = Array.Empty<CreatedFile>();
            public string[] createdAssetPaths;
        }
        [Serializable] public sealed class FileRecord
        {
            public string path, backupFile, beforeSha256, afterSha256;
        }
        [Serializable] public sealed class CreatedFile { public string path, sha256; }

        public static void RunPreview()
        {
            var report = Preview(RequiredEnvironment("HO_MATERIAL_MIGRATION_PLAN"), RequiredEnvironment("HO_MATERIAL_MIGRATION_PREVIEW_ROOT"), RequiredEnvironment("HO_MATERIAL_MIGRATION_REPORT"));
            Debug.Log($"HO_MATERIAL_PREVIEW_OK proposals={report.proposals.Length} blocked={report.proposals.Count(p => p.blockers.Length != 0)} root={report.previewRoot}");
        }
        public static void RunApply() => Apply(RequiredEnvironment("HO_MATERIAL_MIGRATION_PLAN"), RequiredEnvironment("HO_MATERIAL_MIGRATION_REPORT"), RequiredEnvironment("HO_MATERIAL_MIGRATION_BACKUP"));
        public static void RunRestore() => Restore(RequiredEnvironment("HO_MATERIAL_MIGRATION_BACKUP"));
        public static void RunReferenceOwners()
        {
            var plan = ReadPlan(RequiredEnvironment("HO_MATERIAL_MIGRATION_PLAN"));
            if (!plan.resolveReferenceOwners) throw new InvalidOperationException("Enable resolveReferenceOwners explicitly for an authoring-owner audit.");
            string path = RequiredEnvironment("HO_MATERIAL_MIGRATION_REPORT");RequireExternalNewFile(path);
            var report = HoMigrationReferenceOwners.Resolve(plan, plan.retainMixedModelAnchors);WriteJson(path, report);
            Debug.Log($"HO_MATERIAL_REFERENCE_OWNERS_OK intents={report.intents.Length} owners={report.slotOwners.Length} remaps={report.remaps.Length} modelDefaults={report.modelDefaults.Length} pendingWrappers={report.wrappers.Length} blocked={report.blockers.Length}");
            if (report.blockers.Length != 0) throw new InvalidOperationException("Authoring-owner audit contains blockers; see " + path);
        }
        private static string RequiredEnvironment(string name) => Environment.GetEnvironmentVariable(name) is string value && !string.IsNullOrEmpty(value)
            ? value : throw new InvalidOperationException("Set " + name + " explicitly.");

        public static PreviewReport Preview(string planPath, string previewRoot, string reportPath)
        {
            var plan = ReadPlan(planPath);
            RequireExternalNewFile(reportPath);
            RequireExternalNewFile(reportPath + ".md");
            RequireNewAssetPath(previewRoot, false);
            if (!previewRoot.StartsWith("Assets/", StringComparison.Ordinal)) throw new InvalidOperationException("Preview must be inside Assets.");
            foreach (var row in plan.rows) RequireSource(row);
            var owners = plan.resolveReferenceOwners ? HoMigrationReferenceOwners.Resolve(plan, plan.retainMixedModelAnchors) : null;
            var requests = plan.rows.Select(r => (r.sourceGuid, r.targetFamily))
                .Concat(plan.slotActions.Where(a => a.kind == "assign-fork" || plan.resolveReferenceOwners && a.kind == "model-remap-review")
                    .Select(a => (a.targetSourceGuid, a.targetFamily))).Distinct().ToArray();
            var proposals = new List<Proposal>();
            EnsureFolder(previewRoot);
            foreach (var request in requests)
            {
                var row = plan.rows.Single(r => r.sourceGuid == request.Item1);
                var family = Family(request.Item2);
                string stem = row.sourceGuid + "-" + family;
                string resources = previewRoot + "/Resources/" + stem;
                EnsureFolder(resources);
                var source = RequireSource(row);
                var differences = new List<string>();var blockers = new List<string>();
                var decisions = row.modelDecisions.Where(d => d.targetFamily == family.ToString()).Select(d => d.code).ToArray();
                var proposed = HoLegacyMaterialConverter.Convert(source, family, differences, blockers, resources, decisions);
                if (proposed == null) throw new InvalidOperationException("Converter returned no proposal for " + row.assetPath);
                string path = previewRoot + "/" + stem + ".mat";
                // Unity material assets normalize their object name to the file name on import.
                // Use that stable name before hashing; the source keeps its original name on Apply.
                proposed.name = Path.GetFileNameWithoutExtension(path);
                AssetDatabase.CreateAsset(proposed, path);
                AssetDatabase.SaveAssetIfDirty(proposed);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                proposed = AssetDatabase.LoadAssetAtPath<Material>(path);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(proposed.shader, out string shaderGuid, out long _);
                proposals.Add(new Proposal { sourceGuid = row.sourceGuid, targetFamily = family.ToString(), assetPath = path,
                    sha256 = Hash(Absolute(path)), metaSha256 = Hash(Absolute(path + ".meta")), shaderGuid = shaderGuid,
                    shaderDependencyHash = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(proposed.shader)).ToString(),
                    sourceShaderDependencyHash = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(source.shader)).ToString(),
                    differences = differences.Distinct().ToArray(), blockers = blockers.Distinct().ToArray(),
                    sourceInputs = Inputs(source), proposedInputs = Inputs(proposed),
                    sourceState = State(source), proposedState = State(proposed),
                    resources = AssetDatabase.FindAssets("", new[] { resources }).Select(AssetDatabase.GUIDToAssetPath)
                        .Where(p => File.Exists(Absolute(p)) && !p.EndsWith(".meta", StringComparison.Ordinal)).Select(Resource).ToArray() });
            }
            // Recheck sources after import and texture generation, before describing the preview as read-only for them.
            foreach (var row in plan.rows) RequireSource(row);
            foreach (var proposal in proposals)
            {
                AssetDatabase.ImportAsset(proposal.assetPath, ImportAssetOptions.ForceSynchronousImport);
                var material = AssetDatabase.LoadAssetAtPath<Material>(proposal.assetPath);
                if (EditorUtility.IsDirty(material)) AssetDatabase.SaveAssetIfDirty(material);
                proposal.sha256 = Hash(Absolute(proposal.assetPath));proposal.metaSha256 = Hash(Absolute(proposal.assetPath + ".meta"));
                proposal.proposedInputs = Inputs(material);proposal.proposedState = State(material);
                proposal.resources = proposal.resources.Select(r => Resource(r.assetPath)).ToArray();
            }
            var report = new PreviewReport { projectRoot = ProjectRoot, batchId = plan.batchId,
                inputFingerprint = Fingerprint(plan), previewRoot = previewRoot, sourcePlan = plan,
                proposals = proposals.ToArray(), retirementNotes = plan.globalRetirementNotes, referenceOwners = owners };
            WriteJson(reportPath, report);
            string markdown = "# Material migration preview\n\n" + "Project: " + ProjectRoot + "\n\n";
            if (owners != null)
            {
                markdown += $"Authoring owners: {owners.slotOwners.Length}; exact importer remaps: {owners.remaps.Length}; declared model defaults: {owners.modelDefaults.Length}; pending wrappers: {owners.wrappers.Length}.\n\n";
                foreach (string blocker in owners.blockers) markdown += "- OWNER BLOCK: " + blocker + "\n";
                foreach (var value in owners.modelDefaults.Where(d => !d.requestedSubdivisionSatisfied))
                    markdown += $"- MODEL DEFAULT: {value.modelPath} uses {value.effectiveFamily} for requested {value.requestedFamily}; role subdivision is not implemented at this imported model root.\n";
                markdown += "\n";
            }
            foreach (var proposal in report.proposals)
            {
                var sourceRow = plan.rows.Single(r => r.sourceGuid == proposal.sourceGuid);
                markdown += "## " + sourceRow.assetPath + " → " + proposal.targetFamily + "\n\nProposal: `" + proposal.assetPath + "`\n\n";
                foreach (string blocker in proposal.blockers) markdown += "- BLOCK: " + blocker + "\n";
                foreach (string difference in proposal.differences) markdown += "- " + difference + "\n";
                markdown += "\n";
            }
            File.WriteAllText(reportPath + ".md", markdown);
            return report;
        }

        // Review flags may change without invalidating a preview; material inputs and slot actions may not.
        public static void Apply(string reviewedPlanPath, string previewReportPath, string backupDirectory)
        {
            var plan = ReadPlan(reviewedPlanPath);
            var report = JsonUtility.FromJson<PreviewReport>(File.ReadAllText(previewReportPath));
            if (report == null || report.schemaVersion != 1 || report.projectRoot != ProjectRoot || report.inputFingerprint != Fingerprint(plan))
                throw new InvalidOperationException("Preview does not match this project and reviewed input plan.");
            if (plan.resolveReferenceOwners)
            {
                if (report.referenceOwners == null || report.referenceOwners.retainMixedModelAnchors != plan.retainMixedModelAnchors)
                    throw new InvalidOperationException("Preview is missing the selected reference-owner policy.");
                HoMigrationReferenceOwners.ValidateBefore(plan, report.referenceOwners);
            }
            var slotActions = plan.resolveReferenceOwners ? HoMigrationReferenceOwners.ExecutableSlotActions(report.referenceOwners) : plan.slotActions;
            string backup = Path.GetFullPath(backupDirectory);
            if (Within(backup, ProjectRoot) || Directory.Exists(backup) || File.Exists(backup))
                throw new InvalidOperationException("Use a new external backup directory.");
            string finalRoot = "Assets/HoMaterialMigration/" + plan.batchId;
            RequireNewAssetPath(finalRoot, false);
            var selected = new Dictionary<string, Proposal>(StringComparer.Ordinal);
            foreach (var proposal in report.proposals)
            {
                var row = plan.rows.Single(r => r.sourceGuid == proposal.sourceGuid);
                if (!row.reviewed) throw new InvalidOperationException("Unreviewed source family: " + row.assetPath);
                if (proposal.blockers.Length != 0) throw new InvalidOperationException(row.assetPath + ": " + string.Join("; ", proposal.blockers));
                if (!row.acceptFamilyModelChanges && proposal.differences.Any(d => !row.acceptedDifferences.Contains(d)))
                    throw new InvalidOperationException("Review model differences for " + row.assetPath);
                RequireProposal(proposal);
                Material currentSource = RequireSource(row);
                if (AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(currentSource.shader)).ToString() != proposal.sourceShaderDependencyHash
                    || JsonUtility.ToJson(new InputList { values = Inputs(currentSource) }) != JsonUtility.ToJson(new InputList { values = proposal.sourceInputs })
                    || JsonUtility.ToJson(State(currentSource)) != JsonUtility.ToJson(proposal.sourceState))
                    throw new InvalidOperationException("Source shader/texture dependencies or loaded input values changed: " + row.assetPath);
                selected.Add(Key(proposal.sourceGuid, proposal.targetFamily), proposal);
            }
            foreach (var row in plan.rows)
            {
                RequireSource(row);
                if (!selected.ContainsKey(Key(row.sourceGuid, row.targetFamily))) throw new InvalidOperationException("Missing anchor proposal.");
                bool resolvedForkIntent = plan.resolveReferenceOwners && report.referenceOwners.intents.Any(i => i.action.targetSourceGuid == row.sourceGuid
                    && i.action.targetFamily != row.targetFamily && i.resolution != "Blocked");
                if (row.requiresSlotFork && !slotActions.Any(a => a.targetSourceGuid == row.sourceGuid && a.kind == "assign-fork") && !resolvedForkIntent)
                    throw new InvalidOperationException("Shared role requires explicit fork actions: " + row.assetPath);
            }
            foreach (var action in plan.slotActions)
            {
                if (!action.reviewed) throw new InvalidOperationException("Unreviewed slot action: " + action.containerPath);
                RequireContainer(action);
                if (action.kind != "retain-anchor" && action.kind != "assign-fork" && !(plan.resolveReferenceOwners && action.kind == "model-remap-review"))
                    throw new InvalidOperationException("Importer remaps require an explicit owner plan; this apply endpoint accepts scene/prefab actions only.");
                if (action.kind == "assign-fork")
                {
                    RequireNewAssetPath(action.targetAssetPath, true);
                    if (!selected.ContainsKey(Key(action.targetSourceGuid, action.targetFamily))) throw new InvalidOperationException("Missing fork proposal.");
                }
            }
            RequireCleanScenes();
            // Resolve every target before any writes. Prefab inheritance and scene IDs are checked natively.
            WithContainers(slotActions, (action, renderer) => RequireExpected(action, renderer, null), false);
            var forkActions = slotActions.Where(a => a.kind == "assign-fork").ToList();
            if (plan.resolveReferenceOwners)
                forkActions.AddRange(report.referenceOwners.remaps.Where(r => plan.rows.Single(row => row.sourceGuid == r.targetSourceGuid).targetFamily != r.targetFamily)
                    .Select(r => new HoMaterialMigrationSlotAction { kind = "assign-fork", targetSourceGuid = r.targetSourceGuid, targetFamily = r.targetFamily, targetAssetPath = r.targetAssetPath }));
            foreach (var group in forkActions.GroupBy(a => a.targetAssetPath, StringComparer.OrdinalIgnoreCase))
                if (group.Select(a => Key(a.targetSourceGuid, a.targetFamily)).Distinct().Count() != 1
                    || group.Select(a => a.targetAssetPath).Distinct(StringComparer.Ordinal).Count() != 1)
                    throw new InvalidOperationException("A concrete fork path must have one source/family and one path spelling: " + group.Key);
            if (plan.resolveReferenceOwners)
            {
                var remappedModels = new HashSet<string>(report.referenceOwners.remaps.Select(r => r.modelPath), StringComparer.OrdinalIgnoreCase);
                foreach (var proposal in selected.Values)
                    if (proposal.proposedInputs.Any(p => p.type == "Texture" && remappedModels.Contains(p.texturePath ?? "")))
                        throw new InvalidOperationException("A target directly uses a texture subasset from a model being remapped; extract that texture before this transaction: " + proposal.assetPath);
            }
            foreach (var action in forkActions)
            {
                RequireNewAssetPath(action.targetAssetPath, true);
                if (!selected.ContainsKey(Key(action.targetSourceGuid, action.targetFamily))) throw new InvalidOperationException("Missing owner fork proposal.");
            }
            string[] changed = plan.rows.Select(r => r.assetPath).Concat(slotActions.Select(a => a.containerPath)).Distinct()
                .SelectMany(p => new[] { p, p + ".meta" })
                .Concat(plan.resolveReferenceOwners ? report.referenceOwners.remaps.Select(r => r.modelPath + ".meta") : Array.Empty<string>()).Distinct().ToArray();
            var dependencies = plan.resolveReferenceOwners ? report.referenceOwners.containers.SelectMany(c => new[] { c.path, c.path + ".meta" })
                .Except(changed).Select(path => new CreatedFile { path = path, sha256 = Hash(Absolute(path)) }).ToArray() : Array.Empty<CreatedFile>();
            foreach (string path in changed.Concat(dependencies.Select(d => d.path))
                .Where(p => p.EndsWith(".meta", StringComparison.Ordinal)).Select(p => p[..^5]).Distinct())
            {
                var importer = AssetImporter.GetAtPath(path);
                if (importer != null && EditorUtility.IsDirty(importer))
                    throw new InvalidOperationException("Unsaved importer settings must be handled before any migration writes: " + path);
            }
            Directory.CreateDirectory(backup);
            var journal = new Journal { projectRoot = ProjectRoot, state = "Prepared", finalRoot = finalRoot,
                inputFingerprint = Fingerprint(plan), created = Array.Empty<CreatedFile>(), createdAssetPaths = Array.Empty<string>(),
                originals = changed.Select(path => Backup(path, backup)).ToArray(), readOnlyDependencies = dependencies };
            WriteJson(Path.Combine(backup, "plan.json"), plan);WriteJson(Path.Combine(backup, "preview.json"), report);
            string journalPath = Path.Combine(backup, "journal.json");WriteJson(journalPath, journal);
            var createdAssets = new List<string>();
            var prepared = new Dictionary<string, Material>();
            var expectedInputs = new Dictionary<string, string>();
            var expectedStates = new Dictionary<string, string>();
            try
            {
                void PersistCreated() { journal.createdAssetPaths = createdAssets.ToArray();WriteJson(journalPath, journal); }
                EnsureFolder(finalRoot, createdAssets, PersistCreated);
                journal.createdAssetPaths = createdAssets.ToArray();WriteJson(journalPath, journal);
                foreach (var pair in selected)
                {
                    var material = new Material(AssetDatabase.LoadAssetAtPath<Material>(pair.Value.assetPath));
                    prepared.Add(pair.Key, material);
                    var copied = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var resource in pair.Value.resources)
                    {
                        string destination = finalRoot + "/Resources/" + pair.Value.sourceGuid + "-" + pair.Value.targetFamily + "/" + Path.GetFileName(resource.assetPath);
                        EnsureFolder(Path.GetDirectoryName(destination).Replace('\\', '/'), createdAssets, PersistCreated);
                        if (!AssetDatabase.CopyAsset(resource.assetPath, destination)) throw new IOException("Cannot copy derived resource " + resource.assetPath);
                        copied.Add(resource.assetPath, destination);
                        foreach (string property in material.GetTexturePropertyNames())
                            if (AssetDatabase.GetAssetPath(material.GetTexture(property)) == resource.assetPath)
                                material.SetTexture(property, AssetDatabase.LoadAssetAtPath<Texture>(destination));
                    }
                    foreach (var copy in copied.Where(p => p.Key.EndsWith(".recipe.json", StringComparison.Ordinal)))
                    {
                        string originalTexture = copy.Key[..^12];
                        if (!copied.TryGetValue(originalTexture, out string finalTexture)) throw new InvalidOperationException("Recipe has no copied texture: " + copy.Key);
                        var recipe = JsonUtility.FromJson<HoMigrationTextureBaker.Recipe>(File.ReadAllText(Absolute(copy.Key)));
                        recipe.outputGuid = AssetDatabase.AssetPathToGUID(finalTexture);
                        recipe.outputSha256 = Hash(Absolute(finalTexture));recipe.outputMetaSha256 = Hash(Absolute(finalTexture + ".meta"));
                        WriteJson(Absolute(copy.Value), recipe);AssetDatabase.ImportAsset(copy.Value, ImportAssetOptions.ForceSynchronousImport);
                    }
                }
                foreach (var row in plan.rows)
                {
                    Material source = RequireSource(row), proposed = prepared[Key(row.sourceGuid, row.targetFamily)];
                    expectedInputs[row.sourceGuid] = JsonUtility.ToJson(new InputList { values = Inputs(proposed) });
                    expectedStates[row.sourceGuid] = JsonUtility.ToJson(State(proposed));
                    string name = source.name;
                    EditorUtility.CopySerialized(proposed, source);source.name = name;
                    EditorUtility.SetDirty(source);AssetDatabase.SaveAssetIfDirty(source);
                    RequireIdentity(source, row.sourceGuid, row.sourceLocalId);
                    journal.state = "Applied";
                    foreach (var file in journal.originals) file.afterSha256 = Hash(Absolute(file.path));
                    WriteJson(journalPath, journal);
                    FaultInjection?.Invoke("after-material:" + row.sourceGuid);
                }
                var forks = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
                foreach (var action in forkActions)
                {
                    if (forks.ContainsKey(action.targetAssetPath)) continue;
                    var material = new Material(prepared[Key(action.targetSourceGuid, action.targetFamily)]);
                    EnsureFolder(Path.GetDirectoryName(action.targetAssetPath).Replace('\\', '/'), createdAssets, PersistCreated);
                    createdAssets.Add(action.targetAssetPath);journal.createdAssetPaths = createdAssets.ToArray();WriteJson(journalPath, journal);
                    AssetDatabase.CreateAsset(material, action.targetAssetPath);AssetDatabase.SaveAssetIfDirty(material);
                    forks.Add(action.targetAssetPath, material);
                }
                Material FinalTarget(string guid, string family)
                {
                    var row = plan.rows.Single(r => r.sourceGuid == guid);
                    if (row.targetFamily == family) return AssetDatabase.LoadAssetAtPath<Material>(row.assetPath);
                    string[] paths = forkActions.Where(a => a.targetSourceGuid == guid && a.targetFamily == family).Select(a => a.targetAssetPath).Distinct().ToArray();
                    if (paths.Length != 1) throw new InvalidOperationException("Reference-owner target requires one concrete source/family fork path: " + guid + "/" + family);
                    return forks[paths[0]];
                }
                if (plan.resolveReferenceOwners) HoMigrationReferenceOwners.ApplyRemaps(report.referenceOwners, FinalTarget);
                FaultInjection?.Invoke("after-importer-remaps");
                WithContainers(slotActions, (action, renderer) =>
                {
                    var target = action.kind == "assign-fork" ? forks[action.targetAssetPath]
                        : AssetDatabase.LoadAssetAtPath<Material>(plan.rows.Single(r => r.sourceGuid == action.targetSourceGuid).assetPath);
                    var current = renderer.sharedMaterials[action.slot];
                    if (current == target) return;
                    // Every old slot was validated before writes. Upstream prefab writes may
                    // now expose another planned target; retain-anchor must write its own
                    // intentional override instead of silently inheriting that target.
                    if (!forks.Values.Contains(current)) RequireExpected(action, renderer, target);
                    var materials = renderer.sharedMaterials;materials[action.slot] = target;renderer.sharedMaterials = materials;
                    if (PrefabUtility.IsPartOfPrefabInstance(renderer)) PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                    EditorUtility.SetDirty(renderer);
                }, true);
                FaultInjection?.Invoke("after-references");
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                foreach (var row in plan.rows)
                {
                    var material = AssetDatabase.LoadAssetAtPath<Material>(row.assetPath);
                    RequireIdentity(material, row.sourceGuid, row.sourceLocalId);
                    if (material.shader != AssetDatabase.LoadAssetAtPath<Material>(selected[Key(row.sourceGuid, row.targetFamily)].assetPath).shader)
                        throw new InvalidOperationException("Saved shader differs from proposal: " + row.assetPath);
                    if (JsonUtility.ToJson(new InputList { values = Inputs(material) }) != expectedInputs[row.sourceGuid]
                        || JsonUtility.ToJson(State(material)) != expectedStates[row.sourceGuid])
                        throw new InvalidOperationException("Saved material inputs/state differ from the concrete proposal: " + row.assetPath);
                }
                foreach (var action in forkActions.GroupBy(a => a.targetAssetPath).Select(g => g.First()))
                {
                    var fork = AssetDatabase.LoadAssetAtPath<Material>(action.targetAssetPath);
                    var expected = prepared[Key(action.targetSourceGuid, action.targetFamily)];
                    if (fork == null || fork.shader != expected.shader || JsonUtility.ToJson(new InputList { values = Inputs(fork) }) != JsonUtility.ToJson(new InputList { values = Inputs(expected) })
                        || JsonUtility.ToJson(State(fork)) != JsonUtility.ToJson(State(expected)))
                        throw new InvalidOperationException("Saved fork differs from its source/family proposal: " + action.targetAssetPath);
                }
                WithContainers(slotActions, (action, renderer) =>
                {
                    var target = action.kind == "assign-fork" ? AssetDatabase.LoadAssetAtPath<Material>(action.targetAssetPath)
                        : AssetDatabase.LoadAssetAtPath<Material>(plan.rows.Single(r => r.sourceGuid == action.targetSourceGuid).assetPath);
                    if (renderer.sharedMaterials[action.slot] != target)
                        throw new InvalidOperationException("Saved slot differs: " + action.containerPath + " " + action.rendererPath);
                }, false);
                if (plan.resolveReferenceOwners)
                {
                    var validation = HoMigrationReferenceOwners.ValidateAfter(report.referenceOwners, FinalTarget);
                    if (validation.blockers.Length != 0 || validation.verifiedIntentCount != plan.slotActions.Length)
                        throw new InvalidOperationException("Expanded reference verification failed: " + string.Join("; ", validation.blockers));
                    Debug.Log($"HO_MATERIAL_REFERENCE_VERIFY_OK checked={validation.verifiedIntentCount} declaredModelDefaultExceptions={validation.declaredModelDefaultExceptions} pendingWrappers={report.referenceOwners.wrappers.Length}");
                }
                foreach (var dependency in dependencies)
                    if (Hash(Absolute(dependency.path)) != dependency.sha256) throw new InvalidOperationException("Read-only owner dependency changed during migration: " + dependency.path);
                foreach (var file in journal.originals) file.afterSha256 = Hash(Absolute(file.path));
                journal.created = CreatedFiles(createdAssets).Select(path => new CreatedFile { path = path, sha256 = Hash(Absolute(path)) }).ToArray();
                journal.state = "Verified";WriteJson(journalPath, journal);
            }
            catch (Exception original)
            {
                try { Rollback(journal, backup, false);journal.state = "RolledBack";WriteJson(journalPath, journal); }
                catch (Exception rollback) { throw new AggregateException("Apply failed and rollback also failed; preserve the external backup.", original, rollback); }
                throw;
            }
            finally { foreach (var material in prepared.Values) if (material != null) Object.DestroyImmediate(material); }
        }

        public static void Restore(string backupDirectory)
        {
            string backup = Path.GetFullPath(backupDirectory);
            var journal = JsonUtility.FromJson<Journal>(File.ReadAllText(Path.Combine(backup, "journal.json")));
            if (journal == null || journal.schemaVersion != 1 || journal.projectRoot != ProjectRoot || journal.state != "Verified")
                throw new InvalidOperationException("A verified transaction for this project is required.");
            RequireCleanScenes();Rollback(journal, backup, true);journal.state = "Restored";WriteJson(Path.Combine(backup, "journal.json"), journal);
        }

        private static void Rollback(Journal journal, string backup, bool guarded)
        {
            AssetPath(journal.finalRoot);
            foreach (string path in journal.createdAssetPaths) AssetPath(path);
            foreach (var dependency in journal.readOnlyDependencies ?? Array.Empty<CreatedFile>())
            {
                AssetPath(dependency.path);
                if (guarded && Hash(Absolute(dependency.path)) != dependency.sha256)
                    throw new InvalidOperationException("An upstream reference dependency changed; merge before restore: " + dependency.path);
            }
            foreach (var file in journal.originals)
            {
                AssetPath(file.path);
                if (!System.Text.RegularExpressions.Regex.IsMatch(file.backupFile, "^[a-f0-9]{64}\\.bin$")) throw new InvalidOperationException("Invalid backup filename.");
                if (Hash(Path.Combine(backup, file.backupFile)) != file.beforeSha256) throw new InvalidOperationException("Backup has changed: " + file.path);
                if (guarded && Hash(Absolute(file.path)) != file.afterSha256) throw new InvalidOperationException("Post-migration edits require a merge: " + file.path);
            }
            if (guarded)
            {
                foreach (var file in journal.originals.Where(f => f.path.EndsWith(".meta", StringComparison.Ordinal)))
                {
                    var importer = AssetImporter.GetAtPath(file.path[..^5]);
                    if (importer != null && EditorUtility.IsDirty(importer)) throw new InvalidOperationException("Unsaved importer settings require review before restore: " + file.path);
                }
                foreach (string path in journal.originals.Select(f => f.path).Concat(CreatedFiles(journal.createdAssetPaths))
                    .Where(p => !p.EndsWith(".meta", StringComparison.Ordinal)).Distinct())
                    foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                        if (asset != null && EditorUtility.IsDirty(asset)) throw new InvalidOperationException("Unsaved asset edits require review before restore: " + path);
                if (!CreatedFiles(journal.createdAssetPaths).OrderBy(p => p).SequenceEqual(journal.created.Select(p => p.path).OrderBy(p => p)))
                    throw new InvalidOperationException("New files appeared in the migration-owned output.");
                foreach (var file in journal.created) if (Hash(Absolute(file.path)) != file.sha256) throw new InvalidOperationException("Derived asset changed: " + file.path);
            }
            foreach (var file in journal.originals) File.Copy(Path.Combine(backup, file.backupFile), Absolute(file.path), true);
            foreach (string path in journal.createdAssetPaths.Reverse())
            {
                if (!File.Exists(Absolute(path)) && !Directory.Exists(Absolute(path)) && !File.Exists(Absolute(path + ".meta"))) continue;
                if (!AssetDatabase.DeleteAsset(path) || File.Exists(Absolute(path)) || Directory.Exists(Absolute(path)) || File.Exists(Absolute(path + ".meta")))
                    throw new IOException("Could not remove migration-owned output: " + path);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (string path in journal.originals.Select(f => f.path.EndsWith(".meta", StringComparison.Ordinal) ? f.path[..^5] : f.path).Distinct())
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            foreach (var file in journal.originals) if (Hash(Absolute(file.path)) != file.beforeSha256) throw new IOException("Exact restoration failed: " + file.path);
        }

        private static void WithContainers(HoMaterialMigrationSlotAction[] actions, Action<HoMaterialMigrationSlotAction, Renderer> visit, bool save)
        {
            foreach (var group in OrderedContainers(actions))
            {
                if (group.Key.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    var root = PrefabUtility.LoadPrefabContents(group.Key);
                    try
                    {
                        foreach (var action in group) visit(action, Resolve(root.GetComponentsInChildren<Renderer>(true), action));
                        if (save) PrefabUtility.SaveAsPrefabAsset(root, group.Key);
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                }
                else if (group.Key.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                {
                    if (SceneManager.GetSceneByPath(group.Key).isLoaded) throw new InvalidOperationException("Close the clean target scene before migration: " + group.Key);
                    Scene scene = save ? EditorSceneManager.OpenScene(group.Key, OpenSceneMode.Additive) : EditorSceneManager.OpenPreviewScene(group.Key);
                    try
                    {
                        Renderer[] renderers = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>(true)).ToArray();
                        foreach (var action in group) visit(action, Resolve(renderers, action));
                        if (save && !EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save scene: " + group.Key);
                    }
                    finally { if (save) EditorSceneManager.CloseScene(scene, true);else EditorSceneManager.ClosePreviewScene(scene); }
                }
                else throw new InvalidOperationException("Imported model changes need an explicit remap owner action: " + group.Key);
            }
        }
        private static IEnumerable<IGrouping<string, HoMaterialMigrationSlotAction>> OrderedContainers(HoMaterialMigrationSlotAction[] actions)
        {
            var groups = actions.GroupBy(a => a.containerPath).ToArray();
            var pending = groups.Where(g => g.Key.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)).ToList();
            while (pending.Count != 0)
            {
                var ready = pending.FirstOrDefault(g => !AssetDatabase.GetDependencies(g.Key, false).Any(p => p != g.Key && pending.Any(other => other.Key == p)));
                if (ready == null) throw new InvalidOperationException("Prefab owner dependency cycle; resolve authoring owners first.");
                pending.Remove(ready);yield return ready;
            }
            foreach (var group in groups.Where(g => !g.Key.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))) yield return group;
        }
        private static Renderer Resolve(IEnumerable<Renderer> renderers, HoMaterialMigrationSlotAction action)
        {
            var candidates = renderers.Where(r => r.GetType().FullName == action.rendererType && Hierarchy(r.transform) == action.rendererPath
                && Array.IndexOf(r.GetComponents<Renderer>(), r) == action.componentIndex).ToArray();
            if (candidates.Length != 1) throw new InvalidOperationException("Renderer is missing or ambiguous: " + action.containerPath + " " + action.rendererPath);
            return candidates[0];
        }
        private static bool RequireExpected(HoMaterialMigrationSlotAction action, Renderer renderer, Material alreadyApplied)
        {
            var materials = renderer.sharedMaterials;
            if (action.slot < 0 || action.slot >= materials.Length) throw new InvalidOperationException("Material slot has changed.");
            var material = materials[action.slot];
            if (alreadyApplied != null && material == alreadyApplied) return false;
            RequireIdentity(material, action.expectedMaterialGuid, action.expectedMaterialLocalId);return true;
        }
        private static HoMaterialMigrationPlan ReadPlan(string path)
        {
            var plan = JsonUtility.FromJson<HoMaterialMigrationPlan>(File.ReadAllText(path));
            if (plan == null || plan.schemaVersion != 1 || Path.GetFullPath(plan.projectRoot) != ProjectRoot || plan.rows == null || plan.rows.Length == 0
                || plan.rows.Select(r => r.sourceGuid).Distinct().Count() != plan.rows.Length || string.IsNullOrEmpty(plan.batchId)
                || !System.Text.RegularExpressions.Regex.IsMatch(plan.batchId, "^[A-Za-z0-9_-]+$")) throw new InvalidOperationException("Invalid migration plan/project/batch identity.");
            foreach (var row in plan.rows) Family(row.targetFamily);
            if (!string.IsNullOrEmpty(plan.sourceInventoryPath) && Hash(plan.sourceInventoryPath) != plan.sourceInventorySha256) throw new InvalidOperationException("Source inventory changed.");
            if (!string.IsNullOrEmpty(plan.nativeAuditPath) && Hash(plan.nativeAuditPath) != plan.nativeAuditSha256) throw new InvalidOperationException("Native audit changed.");
            plan.slotActions ??= Array.Empty<HoMaterialMigrationSlotAction>();
            foreach (var action in plan.slotActions)
            {
                Family(action.targetFamily);
                if (!plan.rows.Any(r => r.sourceGuid == action.targetSourceGuid)) throw new InvalidOperationException("Slot action source is outside this batch.");
                if (action.kind == "retain-anchor" && (action.expectedMaterialGuid != action.targetSourceGuid
                    || action.targetFamily != plan.rows.Single(r => r.sourceGuid == action.targetSourceGuid).targetFamily))
                    throw new InvalidOperationException("Retain-anchor must identify that source row's original GUID and anchor family.");
                if (action.kind == "assign-fork") RequireNewAssetPath(action.targetAssetPath, true);
            }
            foreach (var fork in plan.slotActions.Where(a => a.kind == "assign-fork" || plan.resolveReferenceOwners && a.kind == "model-remap-review"
                    && a.targetFamily != plan.rows.Single(r => r.sourceGuid == a.targetSourceGuid).targetFamily)
                .GroupBy(a => a.targetAssetPath, StringComparer.OrdinalIgnoreCase))
                if (fork.Select(a => Key(a.targetSourceGuid, a.targetFamily)).Distinct().Count() != 1
                    || fork.Select(a => a.targetAssetPath).Distinct(StringComparer.Ordinal).Count() != 1)
                    throw new InvalidOperationException("One fork asset path cannot represent different source/family proposals: " + fork.Key);
            if (plan.retainMixedModelAnchors && !plan.resolveReferenceOwners)
                throw new InvalidOperationException("A model-default decision requires explicit reference-owner resolution.");
            if (plan.resolveReferenceOwners)
                foreach (var group in plan.slotActions.Where(a => a.kind == "assign-fork" || a.kind == "model-remap-review")
                    .GroupBy(a => Key(a.targetSourceGuid, a.targetFamily)))
                    if (group.Where(a => !string.IsNullOrEmpty(a.targetAssetPath)).Select(a => a.targetAssetPath).Distinct().Count() > 1)
                        throw new InvalidOperationException("Reference-owner resolution requires one source/family fork path: " + group.Key);
            foreach (var row in plan.rows)
            {
                row.modelDecisions ??= Array.Empty<HoMaterialMigrationModelDecision>();
                if (row.modelDecisions.Any(d => d == null || string.IsNullOrWhiteSpace(d.code) || string.IsNullOrWhiteSpace(d.rationale))
                    || row.modelDecisions.Select(d => Key(d.targetFamily, d.code)).Distinct().Count() != row.modelDecisions.Length)
                    throw new InvalidOperationException("Model decisions require unique family/code pairs and a concrete rationale: " + row.assetPath);
                foreach (var decision in row.modelDecisions)
                {
                    Family(decision.targetFamily);
                    if (decision.targetFamily != row.targetFamily && !plan.slotActions.Any(a => (a.kind == "assign-fork" || plan.resolveReferenceOwners && a.kind == "model-remap-review")
                        && a.targetSourceGuid == row.sourceGuid && a.targetFamily == decision.targetFamily))
                        throw new InvalidOperationException("Model decision has no proposal in this batch: " + row.assetPath + " -> " + decision.targetFamily);
                }
            }
            return plan;
        }
        private static Material RequireSource(HoMaterialMigrationRow row)
        {
            AssetPath(row.assetPath);
            if (!row.assetPath.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) || Hash(Absolute(row.assetPath)) != row.sourceSha256
                || Hash(Absolute(row.assetPath + ".meta")) != row.sourceMetaSha256) throw new InvalidOperationException("Source bytes changed: " + row.assetPath);
            var material = AssetDatabase.LoadAssetAtPath<Material>(row.assetPath);RequireIdentity(material, row.sourceGuid, row.sourceLocalId);
            if (EditorUtility.IsDirty(material)) throw new InvalidOperationException("Unsaved source material: " + row.assetPath);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material.shader, out string shaderGuid, out long _);
            if (shaderGuid != row.sourceShaderGuid || Hash(Absolute(row.sourceShaderPath)) != row.sourceShaderSha256) throw new InvalidOperationException("Source shader changed: " + row.assetPath);
            return material;
        }
        private static void RequireProposal(Proposal proposal)
        {
            if (Hash(Absolute(proposal.assetPath)) != proposal.sha256 || Hash(Absolute(proposal.assetPath + ".meta")) != proposal.metaSha256)
                throw new InvalidOperationException("Proposal changed; regenerate preview.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(proposal.assetPath);
            if (material == null || EditorUtility.IsDirty(material) || AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(material.shader)) != proposal.shaderGuid
                || AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(material.shader)).ToString() != proposal.shaderDependencyHash)
                throw new InvalidOperationException("Target shader/proposal state changed.");
            foreach (var resource in proposal.resources)
                if (Hash(Absolute(resource.assetPath)) != resource.sha256 || Hash(Absolute(resource.assetPath + ".meta")) != resource.metaSha256)
                    throw new InvalidOperationException("Derived preview resource changed: " + resource.assetPath);
        }
        private static void RequireContainer(HoMaterialMigrationSlotAction action)
        {
            AssetPath(action.containerPath);
            if (AssetDatabase.AssetPathToGUID(action.containerPath) != action.containerGuid || Hash(Absolute(action.containerPath)) != action.containerSha256
                || Hash(Absolute(action.containerPath + ".meta")) != action.containerMetaSha256) throw new InvalidOperationException("Reference container changed: " + action.containerPath);
        }
        private static void RequireIdentity(Object obj, string guid, long localId)
        {
            if (obj == null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string actualGuid, out long actualId) || actualGuid != guid || actualId != localId)
                throw new InvalidOperationException("Asset GUID/fileID changed: " + guid + ":" + localId);
        }
        private static Property[] Inputs(Material material)
        {
            var properties = new List<Property>();
            for (int i = 0; i < material.shader.GetPropertyCount(); ++i)
            {
                string name = material.shader.GetPropertyName(i);var type = material.shader.GetPropertyType(i);
                var row = new Property { name = name, type = type.ToString() };
                if (type == ShaderPropertyType.Color) row.value = material.GetColor(name);
                else if (type == ShaderPropertyType.Vector) row.value = material.GetVector(name);
                else if (type == ShaderPropertyType.Texture)
                {
                    var texture = material.GetTexture(name);row.texturePath = AssetDatabase.GetAssetPath(texture);
                    if (texture != null) AssetDatabase.TryGetGUIDAndLocalFileIdentifier(texture, out row.textureGuid, out row.textureLocalId);
                    if (!string.IsNullOrEmpty(row.texturePath) && (row.texturePath.StartsWith("Assets/", StringComparison.Ordinal) || row.texturePath.StartsWith("Packages/", StringComparison.Ordinal)))
                    {
                        if (EditorUtility.IsDirty(texture)) throw new InvalidOperationException("Unsaved input texture: " + row.texturePath);
                        row.dependencyHash = AssetDatabase.GetAssetDependencyHash(row.texturePath).ToString();
                        row.textureSha256 = Hash(Absolute(row.texturePath));row.textureMetaSha256 = Hash(Absolute(row.texturePath + ".meta"));
                    }
                    row.scale = material.GetTextureScale(name);row.offset = material.GetTextureOffset(name);
                }
                else row.scalar = type == ShaderPropertyType.Int ? material.GetInteger(name) : material.GetFloat(name);
                properties.Add(row);
            }
            return properties.ToArray();
        }
        [Serializable] private sealed class InputList { public Property[] values; }
        private static MaterialState State(Material material) => new MaterialState
        {
            queue = material.renderQueue, giFlags = (int)material.globalIlluminationFlags, instancing = material.enableInstancing, doubleSidedGI = material.doubleSidedGI,
            keywords = material.shaderKeywords.OrderBy(k => k, StringComparer.Ordinal).ToArray(),
            disabledPassNames = Enumerable.Range(0, material.passCount).Select(material.GetPassName).Where(p => !material.GetShaderPassEnabled(p)).OrderBy(p => p, StringComparer.Ordinal).ToArray(),
            metadata = new[] { "RenderType", "HO_MaterialSource", "HO_PMX_MaterialIndex", "HO_PMX_MaterialFamily" }.Select(k => k + "=" + material.GetTag(k, false, "")).ToArray()
        };
        private static Artifact Resource(string path) => new Artifact { assetPath = path, guid = AssetDatabase.AssetPathToGUID(path), sha256 = Hash(Absolute(path)), metaSha256 = Hash(Absolute(path + ".meta")) };
        private static FileRecord Backup(string path, string backup)
        {
            string name = HashBytes(Encoding.UTF8.GetBytes(path)) + ".bin";File.Copy(Absolute(path), Path.Combine(backup, name));
            return new FileRecord { path = path, backupFile = name, beforeSha256 = Hash(Absolute(path)) };
        }
        private static IEnumerable<string> CreatedFiles(IEnumerable<string> assets)
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in assets)
            {
                if (Directory.Exists(Absolute(path))) foreach (string file in Directory.GetFiles(Absolute(path), "*", SearchOption.AllDirectories)) paths.Add(Relative(file));
                else if (File.Exists(Absolute(path))) paths.Add(path);
                if (File.Exists(Absolute(path + ".meta"))) paths.Add(path + ".meta");
            }
            return paths.OrderBy(p => p, StringComparer.Ordinal);
        }
        private static string Fingerprint(HoMaterialMigrationPlan plan)
        {
            var input = JsonUtility.FromJson<HoMaterialMigrationPlan>(JsonUtility.ToJson(plan));
            foreach (var row in input.rows) { row.reviewed = false;row.acceptFamilyModelChanges = false;row.acceptedDifferences = Array.Empty<string>(); }
            foreach (var action in input.slotActions) action.reviewed = false;
            return HashBytes(Encoding.UTF8.GetBytes(JsonUtility.ToJson(input)));
        }
        private static HoMigrationFamily Family(string name) => Enum.TryParse(name, out HoMigrationFamily family) && Enum.IsDefined(typeof(HoMigrationFamily), family)
            && family.ToString() == name ? family : throw new InvalidOperationException("Unknown target family: " + name);
        private static void RequireCleanScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; ++i) if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Unsaved scenes must be handled before migration.");
        }
        private static void RequireNewAssetPath(string path, bool material)
        {
            AssetPath(path);
            RequireDirectoryAncestors(path);
            if (material && !path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) || File.Exists(Absolute(path)) || Directory.Exists(Absolute(path)) || File.Exists(Absolute(path + ".meta")))
                throw new InvalidOperationException("Output must be a new owned asset path: " + path);
        }
        private static void RequireDirectoryAncestors(string path)
        {
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            while (parent != "Assets" && parent.Length != 0)
            {
                if (File.Exists(Absolute(parent)) || !Directory.Exists(Absolute(parent)) && File.Exists(Absolute(parent + ".meta")))
                    throw new InvalidOperationException("An output ancestor is an existing asset/orphan meta, not a directory: " + parent);
                parent = Path.GetDirectoryName(parent).Replace('\\', '/');
            }
        }
        private static void AssetPath(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || path.Split('/').Contains("..") || path.Contains('\\')
                || path.Split('/').Any(part => part.Length == 0 || part == "." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) || !Within(Absolute(path), Absolute("Assets")))
                throw new InvalidOperationException("Asset path must remain inside this project's Assets: " + path);
        }
        private static void EnsureFolder(string path, List<string> created = null, Action persist = null)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            AssetPath(path);
            if (File.Exists(Absolute(path)) || Directory.Exists(Absolute(path)) || File.Exists(Absolute(path + ".meta")))
                throw new InvalidOperationException("Folder path already exists outside the expected imported-directory state: " + path);
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');EnsureFolder(parent, created, persist);
            string guid = AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
            if (string.IsNullOrEmpty(guid)) throw new IOException("Cannot create asset folder " + path);
            string actual = AssetDatabase.GUIDToAssetPath(guid);AssetPath(actual);
            if (created != null && !created.Contains(actual)) { created.Add(actual);persist?.Invoke(); }
            if (!string.Equals(Path.GetDirectoryName(actual).Replace('\\', '/'), parent, StringComparison.OrdinalIgnoreCase)) throw new IOException("Created folder escaped its requested parent: " + actual);
            if (!string.Equals(actual, path, StringComparison.OrdinalIgnoreCase)) throw new IOException("Unity renamed the requested output folder; refusing an ambiguous output: " + actual);
        }
        private static void RequireExternalNewFile(string path)
        {
            string absolute = Path.GetFullPath(path);
            if (Within(absolute, ProjectRoot) || File.Exists(absolute)) throw new InvalidOperationException("Use a new external report file.");
        }
        private static void WriteJson(string path, object data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + ".tmp";File.WriteAllText(temporary, JsonUtility.ToJson(data, true));
            if (File.Exists(path)) File.Replace(temporary, path, null);else File.Move(temporary, path);
        }
        private static string Hierarchy(Transform value)
        {
            var names = new List<string>();for (; value != null;value = value.parent) names.Add(value.name + "[" + value.GetSiblingIndex() + "]");
            names.Reverse();return string.Join("/", names);
        }
        private static string Hash(string path) => HashBytes(File.ReadAllBytes(path));
        private static string HashBytes(byte[] bytes) { using var sha = SHA256.Create();return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        private static string Key(string guid, string family) => guid + "/" + family;
        private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        private static string Absolute(string path)
        {
            if (path != null && path.StartsWith("Packages/", StringComparison.Ordinal))
            {
                var package = PackageInfo.FindForAssetPath(path);
                if (package == null) throw new InvalidOperationException("Package path is not resolved: " + path);
                return Path.GetFullPath(Path.Combine(package.resolvedPath, path.Substring(("Packages/" + package.name + "/").Length)));
            }
            return Path.GetFullPath(Path.Combine(ProjectRoot, path));
        }
        private static string Relative(string path) => Path.GetRelativePath(ProjectRoot, path).Replace('\\', '/');
        private static bool Within(string path, string root) => path.StartsWith(root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
