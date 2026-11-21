using System;
using System.Collections.Generic;
using System.Globalization;

namespace Hollow.HoUnityTools.RigConstraints.ConstraintIR
{
    public sealed class HoConstraintIrV4Document
    {
        public HoConstraintJsonValue Root { get; private set; }
        public Dictionary<string, HoConstraintJsonValue> Sources { get; private set; }
        public Dictionary<string, HoConstraintJsonValue> Semantics { get; private set; }
        internal HoConstraintIrV4Document(HoConstraintJsonValue root, Dictionary<string, HoConstraintJsonValue> sources, Dictionary<string, HoConstraintJsonValue> semantics)
        { Root = root; Sources = sources; Semantics = semantics; }
        public string ToJson(bool sortKeys = false) { return HoConstraintJson.Serialize(Root, sortKeys); }
        /// <summary>Hash lexical numbers verbatim. Exporters must use the same canonical number spelling.</summary>
        public string ComputeInputFingerprint()
        {
            var root = Root.Clone(); root.Properties.Remove("exportTime"); root.Properties.Remove("inputFingerprint");
            return HoConstraintJson.Sha256(HoConstraintJson.Serialize(root, true));
        }
        public void ValidateInputFingerprint()
        {
            string expected = Root.StringAt("inputFingerprint");
            if (string.IsNullOrEmpty(expected) || !string.Equals(expected, ComputeInputFingerprint(), StringComparison.OrdinalIgnoreCase))
                throw new FormatException("IR v4 inputFingerprint is absent or does not match the source, semantics, and binding document.");
        }
    }

    /// <summary>Shape validation and lossless reading. Does not reinterpret v2/v3 or execute any implementation.</summary>
    public static class HoConstraintIrV4Reader
    {
        public static HoConstraintIrV4Document Read(string json)
        {
            var root = HoConstraintJson.Parse(json); HoConstraintIrShape.Object(root, "$ ");
            if (HoConstraintIrShape.Text(root, "schema", "$") != "hotools.rig-constraint-ir" || HoConstraintIrShape.Integer(root, "schemaVersion", "$") != 4)
                throw new FormatException("This reader accepts hotools.rig-constraint-ir schemaVersion 4 only; v2/v3 must retain their existing reader.");
            HoConstraintIrShape.Text(root, "armatureName", "$"); string timestamp = HoConstraintIrShape.Text(root, "exportTime", "$");
            DateTimeOffset parsedTime;
            if (!timestamp.EndsWith("Z", StringComparison.Ordinal) || !DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedTime) || parsedTime.Offset != TimeSpan.Zero) throw new FormatException("exportTime must be RFC3339 UTC ending in Z.");
            HoConstraintIrShape.Text(root, "blenderVersion", "$", true); Stage(HoConstraintIrShape.Text(root, "captureStage", "$"));
            var registry = HoConstraintIrShape.Member(root, "registry", HoConstraintJsonKind.Object, "$");
            HoConstraintIrShape.Text(registry, "id", "$.registry"); HoConstraintIrShape.Integer(registry, "version", "$.registry", 1);
            HoConstraintIrShape.Sha256(registry, "sha256", "$.registry"); HoConstraintIrShape.Sha256(root, "inputFingerprint", "$");
            var sources = new Dictionary<string, HoConstraintJsonValue>(StringComparer.Ordinal);
            var slots = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in HoConstraintIrShape.Array(root, "sources", "$"))
            {
                string id = HoConstraintIrShape.Text(source, "sourceId", "$.sources[]");
                if (sources.ContainsKey(id)) throw new FormatException("Duplicate sourceId " + id);
                string owner = HoConstraintIrShape.Text(source, "ownerBone", id);
                string ownerKind = HoConstraintIrShape.Text(source, "ownerKind", id); string sourceStage = HoConstraintIrShape.Text(source, "captureStage", id);
                if (ownerKind != "BONE" && ownerKind != "OBJECT") throw new FormatException("Invalid source ownerKind " + ownerKind);
                Stage(sourceStage);
                var c = HoConstraintIrShape.Member(source, "constraint", HoConstraintJsonKind.Object, id);
                int index = HoConstraintIrShape.Integer(c, "stackIndex", id, 0);
                HoConstraintIrShape.Text(c, "name", id, true); HoConstraintIrShape.Text(c, "constraintType", id);
                HoConstraintIrShape.Text(c, "targetObjectName", id, true); HoConstraintIrShape.Text(c, "targetBoneName", id, true);
                HoConstraintIrShape.Member(c, "parameters", HoConstraintJsonKind.Object, id); HoConstraintIrShape.Member(c, "references", HoConstraintJsonKind.Object, id);
                if (!slots.Add(ownerKind + "\u001f" + owner + "\u001f" + sourceStage + "\u001f" + index)) throw new FormatException("Duplicate source owner/stage/stack slot " + owner + "/" + index);
                sources.Add(id, source);
            }
            var semantics = new Dictionary<string, HoConstraintJsonValue>(StringComparer.Ordinal);
            foreach (var semantic in HoConstraintIrShape.Array(root, "semantics", "$"))
            {
                string id = HoConstraintIrShape.Text(semantic, "semanticId", "$.semantics[]");
                if (semantics.ContainsKey(id)) throw new FormatException("Duplicate semanticId " + id);
                string owner = HoConstraintIrShape.Text(semantic, "ownerBone", id);
                HoConstraintIrShape.Text(semantic, "semanticType", id); HoConstraintIrShape.Integer(semantic, "semanticVersion", id, 1);
                HoConstraintIrShape.Text(semantic, "ruleId", id); HoConstraintIrShape.Integer(semantic, "ruleVersion", id, 1);
                string evidence = HoConstraintIrShape.Text(semantic, "evidence", id);
                if (evidence != "RAW_TYPE" && evidence != "GENERATED" && evidence != "EXPLICIT" && evidence != "LEGACY") throw new FormatException("Unknown semantic evidence " + evidence);
                HoConstraintIrShape.Member(semantic, "parameters", HoConstraintJsonKind.Object, id);
                var ids = HoConstraintIrShape.Array(semantic, "sourceIds", id); var seen = new HashSet<string>(StringComparer.Ordinal);
                if (ids.Count == 0) throw new FormatException("Semantic " + id + " has no sourceIds.");
                foreach (var item in ids)
                {
                    string sourceId = HoConstraintIrShape.TextValue(item, id + ".sourceIds"); HoConstraintJsonValue source;
                    if (!seen.Add(sourceId) || !sources.TryGetValue(sourceId, out source)) throw new FormatException("Duplicate or missing source reference " + sourceId + " in " + id);
                    if (source.StringAt("ownerBone") != owner) throw new FormatException("Semantic owner differs from source owner: " + id);
                }
                semantics.Add(id, semantic);
            }
            var locked = new HashSet<string>(StringComparer.Ordinal); var lockedSources = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var binding in HoConstraintIrShape.Array(root, "implementationBindings", "$"))
            {
                string semantic = HoConstraintIrShape.Text(binding, "semanticId", "implementationBinding");
                if (!semantics.ContainsKey(semantic)) throw new FormatException("Missing semantic reference " + semantic);
                string backend = HoConstraintIrShape.Text(binding, "backendId", semantic);
                if (backend != "Standard" && backend != "HoAux" && backend != "Vrc") throw new FormatException("Unknown backend " + backend);
                HoConstraintIrShape.Text(binding, "implementationId", semantic); HoConstraintIrShape.Integer(binding, "contractVersion", semantic, 1);
                string selection = HoConstraintIrShape.Text(binding, "selection", semantic);
                if (selection != "CANDIDATE" && selection != "LOCKED") throw new FormatException("Unknown selection " + selection);
                if (selection == "LOCKED" && !locked.Add(semantic + "\u001f" + backend)) throw new FormatException("Multiple locked implementations for " + semantic + "/" + backend);
                if (selection == "LOCKED") foreach (var source in semantics[semantic].Get("sourceIds").Items)
                {
                    string key = backend + "\u001f" + source.Text, previous;
                    if (lockedSources.TryGetValue(key, out previous) && previous != semantic) throw new FormatException("Locked composite and atomic semantics consume source twice: " + source.Text);
                    lockedSources[key] = semantic;
                }
                string fallback = HoConstraintIrShape.Text(binding, "fallbackPolicy", semantic);
                if (fallback != "EXACT_ONLY" && fallback != "ALLOW_APPROXIMATE" && fallback != "ALLOW_LEGACY") throw new FormatException("Unknown fallbackPolicy " + fallback);
            }
            var overlay = root.Get("bindings");
            if (overlay != null)
            {
                HoConstraintIrShape.Object(overlay, "$.bindings"); if (HoConstraintIrShape.Text(overlay, "captureStage", "$.bindings") != "POSTPROCESS") throw new FormatException("Final bindings must declare POSTPROCESS captureStage.");
                var mapped = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in HoConstraintIrShape.Array(overlay, "constraintMap", "$.bindings"))
                {
                    string id = HoConstraintIrShape.Text(entry, "sourceId", "constraintMap");
                    if (!sources.ContainsKey(id) || !mapped.Add(id)) throw new FormatException("Duplicate or missing constraintMap sourceId " + id);
                    foreach (var key in new[] { "ownerBone", "targetObjectName", "targetBoneName" })
                        if (entry.Get(key) != null) HoConstraintIrShape.Text(entry, key, id, key != "ownerBone");
                    if (entry.Get("stackIndex") != null) HoConstraintIrShape.Integer(entry, "stackIndex", id, 0);
                    if (entry.Get("references") != null) HoConstraintIrShape.Member(entry, "references", HoConstraintJsonKind.Object, id);
                }
                mapped.Clear();
                foreach (var entry in HoConstraintIrShape.Array(overlay, "boneMap", "$.bindings"))
                {
                    string name = HoConstraintIrShape.Text(entry, "sourceBone", "boneMap"); HoConstraintIrShape.Text(entry, "exportBone", "boneMap");
                    if (!mapped.Add(name)) throw new FormatException("Duplicate boneMap sourceBone " + name);
                }
                if (overlay.Get("bindingContext") != null) HoConstraintIrShape.Member(overlay, "bindingContext", HoConstraintJsonKind.Object, "$.bindings");
            }
            if (root.Get("sourceContext") != null) HoConstraintIrShape.Member(root, "sourceContext", HoConstraintJsonKind.Object, "$");
            return new HoConstraintIrV4Document(root, sources, semantics);
        }
        private static void Stage(string value)
        { if (value != "AUTHORING" && value != "POSTPROCESS" && value != "EXPORT_GENERATED") throw new FormatException("Unknown captureStage " + value); }
    }

    public static class HoConstraintIrShape
    {
        public static void Object(HoConstraintJsonValue value, string path)
        { if (value == null || value.Kind != HoConstraintJsonKind.Object) throw new FormatException(path + " must be an object."); }
        public static HoConstraintJsonValue Member(HoConstraintJsonValue parent, string key, HoConstraintJsonKind kind, string path)
        {
            Object(parent, path); var value = parent.Get(key);
            if (value == null || value.Kind != kind) throw new FormatException(path + "." + key + " must be " + kind + ".");
            return value;
        }
        public static string TextValue(HoConstraintJsonValue value, string path)
        { if (value == null || value.Kind != HoConstraintJsonKind.String || string.IsNullOrEmpty(value.Text)) throw new FormatException(path + " must be a nonempty string."); return value.Text; }
        public static string Text(HoConstraintJsonValue parent, string key, string path, bool allowEmpty = false)
        { var value = Member(parent, key, HoConstraintJsonKind.String, path); if (!allowEmpty && string.IsNullOrEmpty(value.Text)) throw new FormatException(path + "." + key + " must not be empty."); return value.Text; }
        public static int Integer(HoConstraintJsonValue parent, string key, string path, int minimum = int.MinValue)
        { int value = Member(parent, key, HoConstraintJsonKind.Number, path).Int32(); if (value < minimum) throw new FormatException(path + "." + key + " is out of range."); return value; }
        public static List<HoConstraintJsonValue> Array(HoConstraintJsonValue parent, string key, string path)
        { return Member(parent, key, HoConstraintJsonKind.Array, path).Items; }
        public static string Sha256(HoConstraintJsonValue parent, string key, string path)
        {
            string value = Text(parent, key, path); if (value.Length != 64) throw new FormatException(path + "." + key + " must be SHA-256 hex.");
            foreach (char c in value) if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f') && !(c >= 'A' && c <= 'F')) throw new FormatException(path + "." + key + " must be SHA-256 hex.");
            return value;
        }
    }
}
