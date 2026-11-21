using System;
using System.Collections.Generic;
using System.Text;
using Hollow.HoUnityTools.RigConstraints.ConstraintIR;

namespace Hollow.HoUnityTools.Editor.RigConstraints.ConstraintIR
{
    /// <summary>The distributed copy of the Blender authority. No implementation rules are inferred from IDs.</summary>
    public sealed class HoConstraintIrV4Registry
    {
        public HoConstraintJsonValue Root { get; private set; }
        public string Sha256 { get; private set; }
        private readonly Dictionary<string, HoConstraintJsonValue> implementations = new Dictionary<string, HoConstraintJsonValue>(StringComparer.Ordinal);
        private readonly Dictionary<string, HoConstraintJsonValue> definitions = new Dictionary<string, HoConstraintJsonValue>(StringComparer.Ordinal);
        private HoConstraintIrV4Registry() { }
        public static HoConstraintIrV4Registry Load(string json) { return Load(new UTF8Encoding(false, true).GetBytes(json ?? "")); }
        public static HoConstraintIrV4Registry Load(byte[] utf8)
        {
            if (utf8 == null) throw new ArgumentNullException("utf8");
            string json = new UTF8Encoding(false, true).GetString(utf8);
            var result = new HoConstraintIrV4Registry { Root = HoConstraintJson.Parse(json), Sha256 = HoConstraintJson.Sha256(json) };
            var root = result.Root;
            if (HoConstraintIrShape.Text(root, "id", "registry") != "hotools.rig-constraints.registry") throw new FormatException("Unknown registry identity.");
            HoConstraintIrShape.Integer(root, "version", "registry", 1);
            var types = new HashSet<string>(StringComparer.Ordinal);
            foreach (var type in HoConstraintIrShape.Array(root, "constraintTypes", "registry"))
            {
                string name = HoConstraintIrShape.Text(type, "type", "constraintTypes[]");
                if (!types.Add(name)) throw new FormatException("Duplicate registry constraint type " + name);
                foreach (var kind in HoConstraintIrShape.Array(type, "ownerKinds", name))
                    if (kind.StringOr() != "BONE" && kind.StringOr() != "OBJECT") throw new FormatException("Unknown owner kind in " + name);
            }
            foreach (var definition in HoConstraintIrShape.Array(root, "semanticDefinitions", "registry"))
            {
                string name = HoConstraintIrShape.Text(definition, "type", "semanticDefinitions[]");
                string key = Key(name, HoConstraintIrShape.Integer(definition, "version", name, 1));
                if (result.definitions.ContainsKey(key)) throw new FormatException("Duplicate semantic definition " + key);
                HoConstraintIrShape.Array(definition, "ruleIds", name);
                foreach (var item in HoConstraintIrShape.Array(definition, "sourceConstraintTypes", name))
                    if (!types.Contains(HoConstraintIrShape.TextValue(item, name))) throw new FormatException("Semantic definition references unknown source type.");
                result.definitions.Add(key, definition);
            }
            foreach (var implementation in HoConstraintIrShape.Array(root, "implementations", "registry"))
            {
                string id = HoConstraintIrShape.Text(implementation, "id", "implementations[]");
                int version = HoConstraintIrShape.Integer(implementation, "contractVersion", id, 1);
                string key = Key(id, version), backend = HoConstraintIrShape.Text(implementation, "backendId", id);
                if (id.IndexOf('@') >= 0 || !id.StartsWith(backend.ToLowerInvariant() + ".", StringComparison.Ordinal) || backend != "HoAux" && backend != "Standard" && backend != "Vrc") throw new FormatException("Invalid implementation/backend identity " + id);
                if (result.implementations.ContainsKey(key)) throw new FormatException("Duplicate implementation contract " + key);
                string semantic = HoConstraintIrShape.Text(implementation, "semanticType", id);
                int semanticVersion = HoConstraintIrShape.Integer(implementation, "semanticVersion", id, 1);
                if (!result.definitions.ContainsKey(Key(semantic, semanticVersion))) throw new FormatException("Implementation references an unknown semantic version " + id);
                HoConstraintIrShape.Member(implementation, "conditions", HoConstraintJsonKind.Object, id);
                HoConstraintIrShape.Member(implementation, "verification", HoConstraintJsonKind.Object, id);
                HoConstraintIrShape.Array(implementation, "artifacts", id); HoConstraintIrShape.Array(implementation, "testSuites", id);
                string availability = HoConstraintIrShape.Text(implementation, "availability", id);
                if (availability != "IMPLEMENTED" && availability != "PLANNED" && availability != "DISABLED") throw new FormatException("Invalid availability " + id);
                string fidelity = HoConstraintIrShape.Text(implementation, "fidelity", id);
                if (fidelity != "EXACT" && fidelity != "CONDITIONAL" && fidelity != "APPROXIMATE" && fidelity != "LEGACY" && fidelity != "UNSPECIFIED") throw new FormatException("Invalid fidelity " + id);
                result.implementations.Add(key, implementation);
            }
            return result;
        }
        private static string Key(string id, int version) { return id + "@" + version; }
        public HoConstraintJsonValue FindImplementation(string id, int version)
        { HoConstraintJsonValue value; return implementations.TryGetValue(Key(id, version), out value) ? value : null; }
        public void Validate(HoConstraintIrV4Document document)
        {
            var manifest = document.Root.Get("registry");
            if (manifest.StringAt("id") != Root.StringAt("id") || manifest.Get("version").Int32() != Root.Get("version").Int32() || !string.Equals(manifest.StringAt("sha256"), Sha256, StringComparison.OrdinalIgnoreCase))
                throw new FormatException("IR v4 registry identity/version/SHA-256 differs from the distributed authority.");
            foreach (var semantic in document.Semantics.Values)
            {
                string id = semantic.StringAt("semanticId"), type = semantic.StringAt("semanticType");
                HoConstraintJsonValue definition;
                if (!definitions.TryGetValue(Key(type, semantic.Get("semanticVersion").Int32()), out definition)) throw new FormatException("Unknown semantic definition/version " + id);
                if (semantic.Get("ruleVersion").Int32() != 1 || !Contains(definition.Get("ruleIds"), semantic.StringAt("ruleId"))) throw new FormatException("Unknown translation rule/version " + id);
                if (definition.Get("acceptedEvidence") != null && !Contains(definition.Get("acceptedEvidence"), semantic.StringAt("evidence"))) throw new FormatException("Semantic evidence does not satisfy registration contract " + id);
                var ids = semantic.Get("sourceIds").Items; var expected = definition.Get("sourceConstraintTypes").Items;
                if (ids.Count != expected.Count) throw new FormatException("Semantic member count differs from its contract " + id);
                for (int i = 0; i < ids.Count; i++)
                {
                    var source = document.Sources[ids[i].Text];
                    if (source.Get("constraint").StringAt("constraintType") != expected[i].Text) throw new FormatException("Semantic ordered source type mismatch " + id);
                }
            }
            var exactBindings = new HashSet<string>(StringComparer.Ordinal);
            foreach (var binding in document.Root.Get("implementationBindings").Items)
            {
                string id = binding.StringAt("implementationId"); int version = binding.Get("contractVersion").Int32();
                var implementation = FindImplementation(id, version);
                if (implementation == null) throw new FormatException("Unknown implementation contract/version " + Key(id, version));
                var semantic = document.Semantics[binding.StringAt("semanticId")];
                if (implementation.StringAt("semanticType") != semantic.StringAt("semanticType") || implementation.Get("semanticVersion").Int32() != semantic.Get("semanticVersion").Int32() || implementation.StringAt("backendId") != binding.StringAt("backendId"))
                    throw new FormatException("Implementation semantic/backend mismatch " + id);
                string duplicate = binding.StringAt("semanticId") + "\u001f" + binding.StringAt("backendId") + "\u001f" + Key(id, version);
                if (!exactBindings.Add(duplicate)) throw new FormatException("Duplicate implementation binding " + duplicate);
            }
        }
        internal static bool Contains(HoConstraintJsonValue array, string value)
        { if (array == null || array.Kind != HoConstraintJsonKind.Array) return false; foreach (var item in array.Items) if (item.StringOr() == value) return true; return false; }
    }

    public static class HoConstraintIrV4Import
    {
        public static HoConstraintIrV4Document Read(string json, string registryJson)
        { return Read(json, HoConstraintIrV4Registry.Load(registryJson)); }
        public static HoConstraintIrV4Document Read(string json, byte[] registryUtf8)
        { return Read(json, HoConstraintIrV4Registry.Load(registryUtf8)); }
        public static HoConstraintIrV4Document Read(string json, HoConstraintIrV4Registry registry)
        {
            if (registry == null) throw new ArgumentNullException("registry");
            var document = HoConstraintIrV4Reader.Read(json); document.ValidateInputFingerprint();
            registry.Validate(document); return document;
        }
    }
}
