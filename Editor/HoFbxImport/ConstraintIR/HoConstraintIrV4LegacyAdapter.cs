using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Hollow.HoUnityTools.RigConstraints.ConstraintIR;
using Hollow.HoUnityTools.RigConstraints.Import;

namespace Hollow.HoUnityTools.Editor.RigConstraints.ConstraintIR
{
    public sealed class HoConstraintIrV4Diagnostic
    {
        public string Code, SemanticId, SourceId, BackendId, Message;
        public override string ToString() { return Code + " / " + (SemanticId ?? SourceId) + ": " + Message; }
    }
    public sealed class HoConstraintIrV4LegacyProjection
    {
        public HoConstraintIrV4Document Document;
        public ConstraintConfig Config;
        public readonly List<HoConstraintIrV4Diagnostic> Diagnostics = new List<HoConstraintIrV4Diagnostic>();
        public readonly List<HoConstraintJsonValue> SelectedBindings = new List<HoConstraintJsonValue>();
    }

    /// <summary>Builds a disposable v3 execution view. The authoritative v4 DOM is never replaced by this fixed DTO.</summary>
    public static class HoConstraintIrV4LegacyAdapter
    {
        public static HoConstraintIrV4LegacyProjection Project(HoConstraintIrV4Document document, HoConstraintIrV4Registry registry, string backendId)
        {
            if (document == null || registry == null) throw new ArgumentNullException("document/registry");
            if (backendId != "HoAux" && backendId != "Standard" && backendId != "Vrc") throw new ArgumentException("Unknown backend.", "backendId");
            // Re-read because callers may have edited the DOM since preview. Never apply a stale preview.
            document = HoConstraintIrV4Reader.Read(document.ToJson()); document.ValidateInputFingerprint(); registry.Validate(document);
            var result = new HoConstraintIrV4LegacyProjection { Document = document, Config = NewConfig(document) };
            var config = result.Config;
            config.sourceContext = new HoConstraintIrImportContext { Document = document, BackendId = backendId, SourceFingerprint = document.Root.StringAt("inputFingerprint"), RegistrySha256 = registry.Sha256 };
            var overlay = document.Root.Get("bindings");
            if (overlay == null || overlay.Get("bindingContext") == null || overlay.Get("bindingContext").Get("bones") == null)
                throw new FormatException("IR v4 authoring snapshot has no final FBX binding overlay; it cannot assemble Unity constraints.");
            config.bindingContext = BindingContext(overlay.Get("bindingContext"));
            var boneMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in overlay.Get("boneMap").Items) boneMap.Add(entry.StringAt("sourceBone"), entry.StringAt("exportBone"));
            var overrides = new Dictionary<string, HoConstraintJsonValue>(StringComparer.Ordinal);
            foreach (var entry in overlay.Get("constraintMap").Items) overrides.Add(entry.StringAt("sourceId"), entry);
            var records = new Dictionary<string, BlenderConstraintInfo>(StringComparer.Ordinal);
            var owners = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in document.Sources)
            {
                var raw = item.Value.Get("constraint").Clone(); HoConstraintJsonValue change;
                string owner = item.Value.StringAt("ownerBone");
                if (overrides.TryGetValue(item.Key, out change))
                {
                    owner = change.StringAt("ownerBone", owner);
                    foreach (var key in new[] { "stackIndex", "targetObjectName", "targetBoneName", "references" })
                        if (change.Get(key) != null) raw.Properties[key] = change.Get(key).Clone();
                }
                owners.Add(item.Key, owner); records.Add(item.Key, Constraint(raw));
            }
            var excludedBoundary = new ExcludedDependencyBoundary(document, boneMap, config);
            var dependencyIssues = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in document.Sources)
            {
                string issue = excludedBoundary.Issue(item.Value, records[item.Key], owners[item.Key]);
                if (issue != null)
                {
                    dependencyIssues.Add(item.Key, issue);
                    Diagnostic(result, "ExcludedDependency", null, item.Key, backendId, issue);
                }
            }
            var consumed = new HashSet<string>(StringComparer.Ordinal);
            var selected = new List<Tuple<HoConstraintJsonValue, HoConstraintJsonValue>>();
            foreach (var semantic in document.Semantics.Values)
            {
                string id = semantic.StringAt("semanticId");
                var ids = semantic.Get("sourceIds").Items.Select(x => x.Text).ToList();
                if (ids.Any(x => document.Sources[x].StringAt("ownerKind") != "BONE"))
                { Diagnostic(result, "UnsupportedOwner", id, null, backendId, "Object-owner constraints remain source facts; this backend adapter only drives bones."); continue; }
                var candidates = document.Root.Get("implementationBindings").Items.Where(x => x.StringAt("semanticId") == id && x.StringAt("backendId") == backendId).ToList();
                var locked = candidates.Where(x => x.StringAt("selection") == "LOCKED").ToList();
                if (locked.Count > 0) candidates = locked;
                var viable = new List<HoConstraintJsonValue>(); var reasons = new List<string>();
                foreach (var binding in candidates)
                {
                    var implementation = registry.FindImplementation(binding.StringAt("implementationId"), binding.Get("contractVersion").Int32());
                    string issue = ids.Where(dependencyIssues.ContainsKey).Select(x => dependencyIssues[x]).FirstOrDefault();
                    issue = issue ?? PreprocessingIssue(document, semantic, ids, records, owners, config, implementation);
                    issue = issue ?? Applicability(implementation, semantic, ids.Select(x => records[x]).ToList(), ids.Select(x => owners[x]).ToList(), config, binding);
                    if (issue == null) viable.Add(binding); else reasons.Add(binding.StringAt("implementationId") + ": " + issue);
                }
                if (viable.Count > 1) throw new FormatException("Ambiguous implementation candidates for " + id + "/" + backendId + "; lock one contract explicitly.");
                if (viable.Count == 0)
                {
                    string reason = reasons.Count == 0 ? "No implementation binding for this backend." : string.Join("; ", reasons.ToArray());
                    if (locked.Count > 0) throw new FormatException("Locked implementation is not applicable for " + id + ": " + reason);
                    Diagnostic(result, "Unsupported", id, null, backendId, reason); continue;
                }
                foreach (string source in ids) if (!consumed.Add(source)) throw new FormatException("Composite and atomic implementations would execute source twice: " + source);
                selected.Add(Tuple.Create(semantic, viable[0])); result.SelectedBindings.Add(viable[0]);
                var chosen = registry.FindImplementation(viable[0].StringAt("implementationId"), viable[0].Get("contractVersion").Int32());
                string verification = chosen.Get("verification").StringAt("status");
                if (verification != "TESTED") Diagnostic(result, "Unverified", id, null, backendId, "Implementation evidence is " + verification + "; this is separate from its fidelity claim.");
            }
            var finalSlots = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in document.Sources)
            {
                if (source.Value.StringAt("ownerKind") != "BONE") { Diagnostic(result, "UnsupportedOwner", null, source.Key, backendId, "Object-owner source retained; bone backend does not drive the armature root."); continue; }
                if (!finalSlots.Add(owners[source.Key] + "\u001f" + records[source.Key].stackIndex)) throw new FormatException("Final binding overlay merges constraint stack slots.");
                if (consumed.Contains(source.Key)) continue;
                bool hasSemantic = document.Semantics.Values.Any(s => s.Get("sourceIds").Items.Any(x => x.Text == source.Key));
                string dependencyIssue; dependencyIssues.TryGetValue(source.Key, out dependencyIssue);
                if (hasSemantic || dependencyIssue != null) config.sourceContext.UnsupportedConstraints[owners[source.Key] + "\u001f" + records[source.Key].stackIndex] = dependencyIssue ?? string.Join("; ", result.Diagnostics.Where(x => x.Code == "Unsupported" && document.Semantics[x.SemanticId].Get("sourceIds").Items.Any(s => s.Text == source.Key)).Select(x => x.Message).ToArray());
                config.unknownConstraints.Add(new UnknownConstraintInfo { ownerBone = owners[source.Key], constraint = records[source.Key], reason = dependencyIssue ?? (hasSemantic ? "IR v4 semantic is known but no applicable selected implementation; source retained as an execution barrier" : "UnknownSemantic / no registered semantic; source retained") });
                if (!hasSemantic) Diagnostic(result, "UnknownSemantic", null, source.Key, backendId, "Source has no semantic record.");
            }
            foreach (var pair in selected) AddSemantic(config, pair.Item1, records, owners, boneMap);
            foreach (var aux in config.auxBones)
                if (aux.auxType != "AIM" && config.unknownConstraints.Any(x => x.ownerBone == aux.boneName)) throw new FormatException("Legacy composite owner has an unknown execution barrier: " + aux.boneName);
            ValidateExecutionGraph(config);
            var selectedReport = HoConstraintJsonValue.Array();
            foreach (var binding in result.SelectedBindings)
            {
                var report = binding.Clone(); var implementation = registry.FindImplementation(binding.StringAt("implementationId"), binding.Get("contractVersion").Int32());
                report.Properties["fidelity"] = implementation.Get("fidelity").Clone(); report.Properties["verification"] = implementation.Get("verification").Clone(); selectedReport.Items.Add(report);
            }
            config.sourceContext.SelectedImplementationsJson = HoConstraintJson.Serialize(selectedReport);
            var diagnosticsReport = HoConstraintJsonValue.Array();
            foreach (var diagnostic in result.Diagnostics)
            {
                var entry = HoConstraintJsonValue.Object();
                entry.Properties["code"] = HoConstraintJsonValue.String(diagnostic.Code); entry.Properties["semanticId"] = HoConstraintJsonValue.String(diagnostic.SemanticId);
                entry.Properties["sourceId"] = HoConstraintJsonValue.String(diagnostic.SourceId); entry.Properties["message"] = HoConstraintJsonValue.String(diagnostic.Message); diagnosticsReport.Items.Add(entry);
            }
            config.sourceContext.DiagnosticsJson = HoConstraintJson.Serialize(diagnosticsReport);
            return result;
        }
        private static void Diagnostic(HoConstraintIrV4LegacyProjection p, string code, string semantic, string source, string backend, string message)
        { p.Diagnostics.Add(new HoConstraintIrV4Diagnostic { Code = code, SemanticId = semantic, SourceId = source, BackendId = backend, Message = message }); }
        private static ConstraintConfig NewConfig(HoConstraintIrV4Document doc)
        { return new ConstraintConfig { schema = "hotools.rig-constraint-ir", schemaVersion = 3, armatureName = doc.Root.StringAt("armatureName"), exportTime = doc.Root.StringAt("exportTime"), mchEnabledBones = new List<string>(), mchBindings = new List<MchBindingInfo>(), auxBones = new List<AuxBoneInfo>(), knownConstraints = new List<KnownConstraintInfo>(), unknownConstraints = new List<UnknownConstraintInfo>(), aimBindings = new List<AimBindingInfo>() }; }
        // excludedOwners is an explicit coverage boundary, not evidence that the
        // named bones are absent. Never inspect or reconstruct their omitted stacks.
        private sealed class ExcludedDependencyBoundary
        {
            private readonly string armature;
            private readonly HashSet<string> authoredExcluded, finalExcluded;
            private readonly Dictionary<string, string> authoredParents, finalParents;
            public ExcludedDependencyBoundary(HoConstraintIrV4Document document, Dictionary<string, string> boneMap, ConstraintConfig config)
            {
                armature = document.Root.StringAt("armatureName");
                authoredExcluded = new HashSet<string>(Strings(document.Root.Get("excludedOwners")), StringComparer.Ordinal);
                finalExcluded = new HashSet<string>(authoredExcluded, StringComparer.Ordinal);
                foreach (string name in authoredExcluded) { string mapped; if (boneMap.TryGetValue(name, out mapped)) finalExcluded.Add(mapped); }
                authoredParents = new Dictionary<string, string>(StringComparer.Ordinal);
                var context = document.Root.Get("sourceContext"); var bones = context == null ? null : context.Get("bones");
                if (authoredExcluded.Count > 0 && bones != null)
                {
                    if (bones.Kind != HoConstraintJsonKind.Array) throw new FormatException("sourceContext.bones must be an array.");
                    foreach (var bone in bones.Items) authoredParents.Add(HoConstraintIrShape.Text(bone, "boneName", "sourceContext.bones[]"), HoConstraintIrShape.Text(bone, "parentBone", "sourceContext.bones[]", true));
                }
                finalParents = config.bindingContext.bones.ToDictionary(x => x.boneName, x => x.parentBone, StringComparer.Ordinal);
            }
            public string Issue(HoConstraintJsonValue source, BlenderConstraintInfo final, string finalOwner)
            {
                if (authoredExcluded.Count == 0) return null;
                bool boneOwner = source.StringAt("ownerKind") == "BONE";
                bool generated = source.StringAt("captureStage") == "EXPORT_GENERATED";
                return Inspect(Constraint(source.Get("constraint")), source.StringAt("ownerBone"), boneOwner, generated ? finalParents : authoredParents, generated ? finalExcluded : authoredExcluded, generated ? "generated source" : "authoring")
                    ?? Inspect(final, finalOwner, boneOwner, finalParents, finalExcluded, "final binding");
            }
            private string Inspect(BlenderConstraintInfo constraint, string owner, bool boneOwner, Dictionary<string, string> parents, HashSet<string> excluded, string stage)
            {
                var dependencies = new List<Tuple<string, string>>();
                if (boneOwner) dependencies.Add(Tuple.Create("owner", owner));
                if (constraint.targetObjectName == armature) dependencies.Add(Tuple.Create("target", constraint.targetBoneName));
                if (constraint.constraintType == "ARMATURE" && constraint.references.targets != null)
                    foreach (var row in constraint.references.targets)
                        if (row.properties != null && row.properties.target != null && row.properties.target.name == armature) dependencies.Add(Tuple.Create("Armature source", row.properties.subtarget));
                foreach (var dependency in dependencies)
                {
                    string current = dependency.Item2; var visited = new HashSet<string>(StringComparer.Ordinal);
                    while (!string.IsNullOrEmpty(current))
                    {
                        if (!visited.Add(current)) return stage + " " + dependency.Item1 + " has cyclic parent evidence; excludedOwners dependency boundary cannot be verified";
                        if (excluded.Contains(current)) return stage + " " + dependency.Item1 + " depends on excludedOwners bone " + current + "; its constraint stack is outside this ordinary-constraint export";
                        string parent;
                        if (!parents.TryGetValue(current, out parent)) return stage + " " + dependency.Item1 + " has incomplete parent evidence for " + current + "; excludedOwners dependency boundary cannot be verified";
                        current = parent;
                    }
                }
                return null;
            }
        }
        private static void AddSemantic(ConstraintConfig config, HoConstraintJsonValue semantic, Dictionary<string, BlenderConstraintInfo> records, Dictionary<string, string> owners, Dictionary<string, string> boneMap)
        {
            string type = semantic.StringAt("semanticType"); var parameters = semantic.Get("parameters");
            var ids = semantic.Get("sourceIds").Items.Select(x => x.Text).ToList();
            string owner = owners[ids[0]]; var constraints = ids.Select(x => records[x]).ToList();
            if (ids.Any(x => owners[x] != owner)) throw new FormatException("Final overlay splits one semantic across multiple owners.");
            if (type == "COPY_LOCATION" || type == "COPY_ROTATION" || type == "ARMATURE")
            { config.knownConstraints.Add(new KnownConstraintInfo { ownerBone = owner, relationType = "DIRECT_CONSTRAINT", constraint = constraints[0] }); return; }
            if (config.auxBones.Any(x => x.boneName == owner)) throw new FormatException("Legacy execution view cannot encode multiple composite semantics on owner " + owner);
            string auxType = parameters.StringAt("auxType", type);
            if (type == "AIM") auxType = "AIM"; if (type == "MCH") auxType = "MCH";
            var sources = Strings(parameters.Get("sourceBones"));
            sources = sources.Select(x => boneMap.ContainsKey(x) ? boneMap[x] : x).ToList();
            var aux = new AuxBoneInfo { boneName = owner, auxType = auxType, sourceBones = sources, constraintNames = constraints.Select(x => x.name).ToList(), involvedBones = new List<string>(), constraints = new List<BlenderConstraintInfo>() };
            if (type == "AIM")
            {
                var first = constraints[0]; var second = constraints[1]; aux.sourceBones = new List<string> { first.targetBoneName, second.targetBoneName };
                string declaredTarget = parameters.StringAt("targetBone"), declaredUp = parameters.StringAt("upBone");
                if (boneMap.ContainsKey(declaredTarget ?? "")) declaredTarget = boneMap[declaredTarget]; if (boneMap.ContainsKey(declaredUp ?? "")) declaredUp = boneMap[declaredUp];
                if (declaredTarget != first.targetBoneName || declaredUp != second.targetBoneName) throw new FormatException("Aim semantic targets differ from final source bindings.");
                var aim = new AimBindingInfo { ownerBone = owner, semanticType = "AIM", semanticVersion = 1, targetBone = first.targetBoneName, upBone = second.targetBoneName,
                    aimAxis = parameters.StringAt("aimAxis"), upAxis = parameters.StringAt("upAxis"), upMode = parameters.StringAt("upMode"), muted = Boolean(parameters.Get("muted"), false), constraintStackIndices = constraints.Select(x => x.stackIndex).ToArray() };
                ValidateAim(aim, constraints, config.armatureName); config.aimBindings.Add(aim);
            }
            else if (type == "MCH")
            {
                string source = constraints[0].targetBoneName; aux.sourceBones = new List<string> { source };
                if (constraints[0].targetObjectName != config.armatureName || string.IsNullOrEmpty(source)) throw new FormatException("MCH must target a source in the same armature.");
                if (!config.mchEnabledBones.Contains(source)) config.mchEnabledBones.Add(source);
                config.mchBindings.Add(new MchBindingInfo { sourceBone = source, mchBone = owner, constraint = constraints[0] });
            }
            else if (type == "TWIST" || type == "FAN") aux.constraints.AddRange(constraints);
            else throw new FormatException("Semantic has no legacy projection implementation: " + type);
            aux.involvedBones.AddRange(aux.sourceBones.Concat(new[] { owner }).Distinct()); config.auxBones.Add(aux);
            foreach (var constraint in constraints) config.knownConstraints.Add(new KnownConstraintInfo { ownerBone = owner, relationType = type == "AIM" ? "AIM_BINDING" : type == "MCH" ? "MCH_BINDING" : "AUX_CONSTRAINT", auxBone = owner, auxType = auxType, constraint = constraint });
        }
        private static string Applicability(HoConstraintJsonValue implementation, HoConstraintJsonValue semantic, List<BlenderConstraintInfo> constraints, List<string> owners, ConstraintConfig config, HoConstraintJsonValue binding)
        {
            if (implementation.StringAt("availability") != "IMPLEMENTED") return "availability=" + implementation.StringAt("availability");
            string fidelity = implementation.StringAt("fidelity"), policy = binding.StringAt("fallbackPolicy");
            if (fidelity == "APPROXIMATE" && policy != "ALLOW_APPROXIMATE") return "approximation requires ALLOW_APPROXIMATE";
            if (fidelity == "LEGACY" && policy != "ALLOW_LEGACY") return "legacy behavior requires ALLOW_LEGACY";
            if (fidelity == "UNSPECIFIED") return "unspecified fidelity is not executable";
            if (constraints.Count == 0 || owners.Any(x => x != owners[0])) return "inconsistent semantic owner";
            string type = semantic.StringAt("semanticType"); var rules = implementation.Get("conditions");
            var acceptedConditions = new HashSet<string>(new[] { "ownerKinds", "targetScope", "ownerInput", "requiredBinding", "stackPolicy", "dependencyPolicy", "ownerSpaces", "targetSpaces", "parameters", "influence", "mixModes", "sourceWeights", "registration", "acceptedEvidence", "maintainOffset", "orderedMemberRoles", "copyRotation", "stretch", "auxTypes", "adjacent", "head_tail", "use_bbone_shape", "upMode", "signedAxes", "identities", "muted", "spacePairs", "rigSpace" }, StringComparer.Ordinal);
            foreach (string key in rules.Properties.Keys) if (!acceptedConditions.Contains(key)) return "unimplemented contract condition " + key;
            if (rules.Get("rigSpace") != null &&
                (rules.StringAt("rigSpace") != "FINITE_POSITIVE_UNIFORM_TRS" || implementation.StringAt("backendId") != "HoAux" ||
                 type != "COPY_LOCATION" && type != "COPY_ROTATION" && type != "ARMATURE" && type != "AIM"))
                return "unimplemented rigSpace condition for this implementation";
            if (!HoConstraintIrV4Registry.Contains(rules.Get("ownerKinds"), "BONE")) return "implementation does not admit bone owners";
            if (rules.StringAt("requiredBinding") != "FINAL_FBX" || rules.StringAt("targetScope") != "SAME_ARMATURE" || rules.StringAt("stackPolicy") != "PRESERVE_ORDER_NO_UNKNOWN_WRITE_BARRIER" || rules.StringAt("dependencyPolicy") != "ACYCLIC_NO_LEGACY_FEEDBACK") return "unsupported binding/target/stack/dependency contract";
            var ownerBinding = config.bindingContext.bones.FirstOrDefault(x => x.boneName == owners[0]);
            if (ownerBinding == null) return "final binding context has no owner bone";
            var ownerInput = rules.Get("ownerInput");
            if (ownerInput != null)
            {
                foreach (string key in ownerInput.Properties.Keys) if (key != "poseBasis" && key != "inheritScale" && key != "animation" && key != "connected" && key != "inheritRotation") return "unimplemented owner input condition " + key;
                if (ownerInput.StringAt("poseBasis") == "IDENTITY" && !Identity(ownerBinding.poseBasis)) return "owner poseBasis must be identity";
                if (ownerInput.StringAt("inheritScale") != null && ownerBinding.inheritScale != ownerInput.StringAt("inheritScale")) return "owner inheritScale does not satisfy contract";
                if (ownerInput.StringAt("animation") == "NONE" && ownerBinding.hasPoseAnimation) return "owner has independent pose animation";
                if (ownerInput.Get("connected") != null && ownerBinding.connected != Boolean(ownerInput.Get("connected"), false)) return "connected owner is unsupported";
                if (ownerInput.Get("inheritRotation") != null && ownerBinding.inheritRotation != Boolean(ownerInput.Get("inheritRotation"), true)) return "owner rotation inheritance does not satisfy contract";
            }
            if (rules.Get("registration") != null && semantic.StringAt("evidence") == "RAW_TYPE") return "composite requires explicit/generated/legacy registration";
            if (rules.Get("acceptedEvidence") != null && !HoConstraintIrV4Registry.Contains(rules.Get("acceptedEvidence"), semantic.StringAt("evidence"))) return "evidence outside implementation contract";
            if (rules.Get("auxTypes") != null && !HoConstraintIrV4Registry.Contains(rules.Get("auxTypes"), semantic.Get("parameters").StringAt("auxType"))) return "auxType outside implementation contract";
            if (rules.Get("adjacent") != null && Boolean(rules.Get("adjacent"), false))
                for (int i = 1; i < constraints.Count; i++) if (constraints[i].stackIndex != constraints[i - 1].stackIndex + 1) return "composite members must be adjacent";
            if (type == "TWIST")
            {
                if (constraints[1].stackIndex != constraints[0].stackIndex + 1) return "Twist members must remain adjacent and ordered";
                if (constraints[0].targetObjectName != constraints[1].targetObjectName || constraints[0].targetBoneName != constraints[1].targetBoneName) return "Twist rotation/stretch targets differ";
                string first = ParameterConditions(rules.Get("copyRotation"), constraints[0]); if (first != null) return first;
                string stretch = ParameterConditions(rules.Get("stretch"), constraints[1]); if (stretch != null) return stretch;
            }
            else foreach (var c in constraints) { string issue = ParameterConditions(rules, c); if (issue != null) return issue; }
            if (type != "ARMATURE" && constraints.Any(x => x.targetObjectName != config.armatureName || string.IsNullOrEmpty(x.targetBoneName))) return "constraint target is outside the current armature or absent";
            if (type != "ARMATURE" && constraints.Any(x => !config.bindingContext.bones.Any(b => b.boneName == x.targetBoneName))) throw new FormatException("Selected semantic references a missing final target bone.");
            if (type == "ARMATURE")
            {
                if (constraints[0].references.targets == null || constraints[0].references.targets.Count == 0) return "Armature has no sources";
                double total = 0;
                foreach (var target in constraints[0].references.targets)
                { var p = target.properties; if (p == null || p.target == null || p.target.name != config.armatureName || string.IsNullOrEmpty(p.subtarget) || p.weight < 0 || float.IsNaN(p.weight) || float.IsInfinity(p.weight)) return "invalid Armature source"; if (!config.bindingContext.bones.Any(b => b.boneName == p.subtarget)) throw new FormatException("Armature references missing final source bone " + p.subtarget); total += p.weight; }
                if (total <= 0 || double.IsInfinity(total)) return "Armature source weight sum is not finite positive";
            }
            if (type == "COPY_ROTATION" && implementation.StringAt("backendId") != "HoAux" && policy == "EXACT_ONLY")
            { float weight = constraints[0].parameters.influence; if (weight != 0 && weight != .5f && weight != 1) return "native rotation interpolation is approximate at this influence"; }
            return null;
        }
        private static string PreprocessingIssue(HoConstraintIrV4Document document, HoConstraintJsonValue semantic, List<string> ids, Dictionary<string, BlenderConstraintInfo> records, Dictionary<string, string> owners, ConstraintConfig config, HoConstraintJsonValue implementation)
        {
            var options = document.Root.Get("exportOptions");
            if (options != null && Boolean(options.Get("appliedConstraints"), false)) return "constraints were applied into the exported rest pose; replay would apply them twice";
            var source = document.Sources[ids[0]]; var authoredBones = document.Root.Get("sourceContext") == null ? null : document.Root.Get("sourceContext").Get("bones");
            bool local = semantic.StringAt("semanticType") == "COPY_ROTATION" && records[ids[0]].parameters.owner_space != "WORLD";
            if (authoredBones == null || authoredBones.Kind != HoConstraintJsonKind.Array) return local ? "local Copy Rotation requires authoring rest-space evidence" : null;
            var authoredOwner = authoredBones.Items.FirstOrDefault(x => x.StringAt("boneName") == source.StringAt("ownerBone"));
            if (authoredOwner != null && implementation.StringAt("fidelity") != "LEGACY" && !Identity(Matrix(authoredOwner.Get("poseBasis"))))
                return "authoring owner poseBasis is not identity; preprocessing does not grant an exact static-input contract";
            if (!local) return null;
            var authoredTarget = authoredBones.Items.FirstOrDefault(x => x.StringAt("boneName") == source.Get("constraint").StringAt("targetBoneName"));
            var owner = config.bindingContext.bones.FirstOrDefault(x => x.boneName == owners[ids[0]]);
            var target = config.bindingContext.bones.FirstOrDefault(x => x.boneName == records[ids[0]].targetBoneName);
            if (authoredOwner == null || authoredTarget == null || owner == null || target == null) return "local Copy Rotation is missing author/final owner or target bindings";
            var finalBones = document.Root.Get("bindings").Get("bindingContext").Get("bones").Items;
            var finalOwner = finalBones.First(x => x.StringAt("boneName") == owner.boneName);
            var finalTarget = finalBones.First(x => x.StringAt("boneName") == target.boneName);
            foreach (var bone in new[] { authoredOwner, authoredTarget, finalOwner, finalTarget })
            {
                var rotation = bone.Get("inheritRotation");
                if (rotation == null || rotation.Kind != HoConstraintJsonKind.Boolean || !rotation.Boolean) return "local Copy Rotation requires explicit inheritRotation=true on author/final owner and target";
                if (bone.StringAt("inheritScale") != "FULL") return "local Copy Rotation requires FULL scale inheritance on author/final owner and target";
            }
            var authorObject = document.Root.Get("sourceContext").Get("objectMatrix");
            float[] authorToWorld = authorObject == null ? null : Matrix(authorObject);
            if (!Identity(Matrix(authoredOwner.Get("poseBasis"))) || !SameReference(Matrix(authoredOwner.Get("restMatrix")), authorToWorld, owner.restMatrix, config.bindingContext.objectMatrix) || !SameReference(Matrix(authoredTarget.Get("restMatrix")), authorToWorld, target.restMatrix, config.bindingContext.objectMatrix))
                return "authoring-to-FBX preprocessing changed local rest/pose references; original-space compensation is not implemented";
            foreach (var pair in new[] { Tuple.Create(authoredOwner, finalOwner), Tuple.Create(authoredTarget, finalTarget) })
            {
                string authorParent = pair.Item1.StringAt("parentBone", ""), finalParent = pair.Item2.StringAt("parentBone", "");
                if (authorParent != finalParent) return "local Copy Rotation author/final parent identity changed";
                if (string.IsNullOrEmpty(authorParent)) continue;
                var authorParentBone = authoredBones.Items.FirstOrDefault(x => x.StringAt("boneName") == authorParent);
                var finalParentBone = finalBones.FirstOrDefault(x => x.StringAt("boneName") == finalParent);
                if (authorParentBone == null || finalParentBone == null || !SameReference(Matrix(authorParentBone.Get("restMatrix")), authorToWorld, Matrix(finalParentBone.Get("restMatrix")), config.bindingContext.objectMatrix)) return "local Copy Rotation author/final parent rest reference changed";
            }
            return null;
        }
        private static bool SameReference(float[] author, float[] authorObject, float[] final, float[] finalObject)
        { return authorObject == null ? SameMatrix(author, final) : SameMatrix(Multiply(authorObject, author), Multiply(finalObject, final)); }
        private static float[] Multiply(float[] a, float[] b)
        {
            var result = new float[16];
            for (int row = 0; row < 4; row++) for (int col = 0; col < 4; col++)
                for (int i = 0; i < 4; i++) result[row * 4 + col] += a[row * 4 + i] * b[i * 4 + col];
            return result;
        }
        private static bool SameMatrix(float[] a, float[] b)
        { if (a == null || b == null || a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (Math.Abs(a[i] - b[i]) > .00001f) return false; return true; }
        private static string ParameterConditions(HoConstraintJsonValue rules, BlenderConstraintInfo c)
        {
            if (rules == null) return null;
            var p = c.parameters;
            if (rules.Get("ownerSpaces") != null && !HoConstraintIrV4Registry.Contains(rules.Get("ownerSpaces"), p.owner_space)) return "owner space outside implementation contract";
            if (rules.Get("targetSpaces") != null && !HoConstraintIrV4Registry.Contains(rules.Get("targetSpaces"), p.target_space)) return "target space outside implementation contract";
            if (rules.Get("mixModes") != null && !HoConstraintIrV4Registry.Contains(rules.Get("mixModes"), p.mix_mode)) return "mix mode outside implementation contract";
            var pairs = rules.Get("spacePairs");
            if (pairs != null && !pairs.Items.Any(x => x.Items.Count == 2 && x.Items[0].StringOr() == p.target_space && x.Items[1].StringOr() == p.owner_space)) return "target/owner space pair outside implementation contract";
            var parameters = rules.Get("parameters");
            if (parameters != null) foreach (var pair in parameters.Properties) if (!ParameterEquals(p, pair.Key, pair.Value)) return "parameter does not satisfy contract: " + pair.Key;
            foreach (string key in new[] { "head_tail", "keep_axis", "volume", "use_bbone_shape" }) if (rules.Get(key) != null && !ParameterEquals(p, key, rules.Get(key))) return "parameter does not satisfy contract: " + key;
            var influence = rules.Get("influence");
            if (influence != null)
            {
                float value = p.influence;
                if (float.IsNaN(value) || float.IsInfinity(value)) return "nonfinite influence";
                if (influence.Get("min") != null && value < influence.Get("min").Single() || influence.Get("max") != null && value > influence.Get("max").Single()) return "influence outside implementation range";
                if (influence.Get("values") != null && !influence.Get("values").Items.Any(x => x.Single() == value)) return "influence outside implementation set";
            }
            return null;
        }
        private static bool ParameterEquals(BlenderConstraintParameters p, string name, HoConstraintJsonValue expected)
        {
            var field = typeof(BlenderConstraintParameters).GetField(name, BindingFlags.Instance | BindingFlags.Public);
            if (field == null) throw new FormatException("Registry references an unsupported parameter field " + name);
            object value = field.GetValue(p);
            if (field.FieldType == typeof(bool)) return (bool)value == Boolean(expected, false);
            if (field.FieldType == typeof(float)) return (float)value == expected.Single();
            if (field.FieldType == typeof(string)) return (string)value == HoConstraintIrShape.TextValue(expected, name);
            throw new FormatException("Unsupported registry parameter comparison " + name);
        }
        private static void ValidateAim(AimBindingInfo aim, List<BlenderConstraintInfo> c, string armature)
        {
            string[] axes = { "TRACK_X", "TRACK_Y", "TRACK_Z", "TRACK_NEGATIVE_X", "TRACK_NEGATIVE_Y", "TRACK_NEGATIVE_Z" };
            if (!axes.Contains(aim.aimAxis) || !axes.Contains(aim.upAxis) || aim.aimAxis.Last() == aim.upAxis.Last() || aim.upMode != "OBJECT_POSITION" || aim.ownerBone == aim.targetBone || aim.ownerBone == aim.upBone || aim.targetBone == aim.upBone) throw new FormatException("Invalid Aim axes/identities/up mode.");
            if (c[0].parameters.track_axis != aim.aimAxis || c[1].parameters.track_axis != aim.upAxis || c[1].parameters.lock_axis != "LOCK_" + aim.aimAxis.Last() || c[0].parameters.mute != c[1].parameters.mute || c[0].parameters.mute != aim.muted || c[1].stackIndex != c[0].stackIndex + 1) throw new FormatException("Aim semantic differs from its source members.");
        }
        private static BlenderConstraintInfo Constraint(HoConstraintJsonValue value)
        {
            var result = new BlenderConstraintInfo { name = value.StringAt("name"), stackIndex = value.Get("stackIndex").Int32(), constraintType = value.StringAt("constraintType"), targetObjectName = value.StringAt("targetObjectName"), targetBoneName = value.StringAt("targetBoneName"), parameters = new BlenderConstraintParameters(), references = new BlenderConstraintReferences() };
            foreach (var field in typeof(BlenderConstraintParameters).GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                var item = value.Get("parameters").Get(field.Name); if (item == null) continue;
                if (field.FieldType == typeof(bool)) field.SetValue(result.parameters, Boolean(item, false));
                else if (field.FieldType == typeof(float)) field.SetValue(result.parameters, item.Single());
                else if (field.FieldType == typeof(string)) field.SetValue(result.parameters, HoConstraintIrShape.Text(value.Get("parameters"), field.Name, "parameters", true));
            }
            var targets = value.Get("references").Get("targets");
            if (targets != null)
            {
                if (targets.Kind != HoConstraintJsonKind.Array) throw new FormatException("references.targets must be an array.");
                result.references.targets = new List<BlenderArmatureTarget>();
                foreach (var item in targets.Items)
                {
                    var p = HoConstraintIrShape.Member(item, "properties", HoConstraintJsonKind.Object, "targets[]"); var target = p.Get("target");
                    result.references.targets.Add(new BlenderArmatureTarget { rnaType = item.StringAt("rnaType"), properties = new BlenderArmatureTargetProperties { subtarget = p.StringAt("subtarget"), weight = HoConstraintIrShape.Member(p, "weight", HoConstraintJsonKind.Number, "target").Single(), target = target == null || target.Kind == HoConstraintJsonKind.Null ? null : new BlenderObjectReference { name = target.StringAt("name"), rnaType = target.StringAt("rnaType") } } });
                }
            }
            return result;
        }
        private static ConstraintBindingContext BindingContext(HoConstraintJsonValue value)
        {
            var result = new ConstraintBindingContext { space = value.StringAt("space"), matrixLayout = value.StringAt("matrixLayout"), fbxBoneAxes = value.StringAt("fbxBoneAxes"), objectMatrix = Matrix(value.Get("objectMatrix")), bones = new List<ConstraintBoneBinding>() };
            if (result.space != "BLENDER_ARMATURE" || result.matrixLayout != "ROW_MAJOR") throw new FormatException("Unsupported final binding context space/layout.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var bone in HoConstraintIrShape.Array(value, "bones", "bindingContext"))
            {
                string name = HoConstraintIrShape.Text(bone, "boneName", "bindingContext.bones[]"); if (!names.Add(name)) throw new FormatException("Duplicate binding bone " + name);
                result.bones.Add(new ConstraintBoneBinding { boneName = name, parentBone = HoConstraintIrShape.Text(bone, "parentBone", name, true), inheritScale = HoConstraintIrShape.Text(bone, "inheritScale", name), inheritRotation = Boolean(bone.Get("inheritRotation"), true), restMatrix = Matrix(bone.Get("restMatrix")), poseBasis = Matrix(bone.Get("poseBasis")), connected = Boolean(bone.Get("connected"), false), positionOnly = Boolean(bone.Get("positionOnly"), false), hasPoseAnimation = Boolean(bone.Get("hasPoseAnimation"), false) });
            }
            foreach (var bone in result.bones) if (!string.IsNullOrEmpty(bone.parentBone) && !names.Contains(bone.parentBone)) throw new FormatException("Binding context has missing parent " + bone.parentBone);
            foreach (var bone in result.bones)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal); string current = bone.boneName;
                while (!string.IsNullOrEmpty(current)) { if (!seen.Add(current)) throw new FormatException("Cyclic binding parent hierarchy."); current = result.bones.First(x => x.boneName == current).parentBone; }
            }
            return result;
        }
        private static void ValidateExecutionGraph(ConstraintConfig config)
        {
            var operations = config.knownConstraints.GroupBy(x => x.ownerBone).ToDictionary(x => x.Key, x => x.ToList(), StringComparer.Ordinal);
            var parents = config.bindingContext.bones.ToDictionary(x => x.boneName, x => x.parentBone, StringComparer.Ordinal);
            var edges = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var owner in operations)
            {
                var dependencies = new HashSet<string>(StringComparer.Ordinal);
                var references = owner.Value.Select(x => x.constraint.targetBoneName).Where(x => !string.IsNullOrEmpty(x)).ToList();
                foreach (var operation in owner.Value)
                    if (operation.constraint.references.targets != null) references.AddRange(operation.constraint.references.targets.Select(x => x.properties.subtarget));
                string parent; if (parents.TryGetValue(owner.Key, out parent) && !string.IsNullOrEmpty(parent)) references.Add(parent);
                foreach (string reference in references)
                {
                    string current = reference;
                    while (!string.IsNullOrEmpty(current)) { if (operations.ContainsKey(current)) { dependencies.Add(current); break; } if (!parents.TryGetValue(current, out current)) break; }
                }
                edges.Add(owner.Key, dependencies);
            }
            var visiting = new HashSet<string>(StringComparer.Ordinal); var done = new HashSet<string>(StringComparer.Ordinal);
            foreach (string owner in edges.Keys) Visit(owner, edges, visiting, done);
        }
        private static void Visit(string owner, Dictionary<string, HashSet<string>> edges, HashSet<string> visiting, HashSet<string> done)
        {
            if (done.Contains(owner)) return; if (!visiting.Add(owner)) throw new FormatException("Cyclic v4 execution dependency at " + owner);
            foreach (string dependency in edges[owner]) Visit(dependency, edges, visiting, done);
            visiting.Remove(owner); done.Add(owner);
        }
        private static float[] Matrix(HoConstraintJsonValue value)
        { if (value == null || value.Kind != HoConstraintJsonKind.Array || value.Items.Count != 16) throw new FormatException("Binding matrices require 16 finite numbers."); return value.Items.Select(x => x.Single()).ToArray(); }
        private static bool Identity(float[] matrix)
        { if (matrix == null || matrix.Length != 16) return false; for (int i = 0; i < 16; i++) if (Math.Abs(matrix[i] - (i % 5 == 0 ? 1 : 0)) > .00001) return false; return true; }
        private static bool Boolean(HoConstraintJsonValue value, bool fallback)
        { if (value == null) return fallback; if (value.Kind != HoConstraintJsonKind.Boolean) throw new FormatException("Expected boolean JSON value."); return value.Boolean; }
        private static List<string> Strings(HoConstraintJsonValue value)
        { if (value == null) return new List<string>(); if (value.Kind != HoConstraintJsonKind.Array) throw new FormatException("Expected string array."); return value.Items.Select(x => HoConstraintIrShape.TextValue(x, "sourceBones")).ToList(); }
    }
}
