using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Hollow.HoUnityTools.Editor.Pmx
{
    /// <summary>Reads the supported Ray material_common macro contract from EMD and FX source.</summary>
    public static class HoPmxRayReader
    {
        private const int MaxFileBytes = 4 * 1024 * 1024;
        private static readonly Regex Mapping = new Regex(@"^Obj\[(\d+)\]\s*=\s*(.*?)\s*$", RegexOptions.CultureInvariant);
        private static readonly Regex Directive = new Regex(@"^\s*#\s*(\w+)\s*(.*)$", RegexOptions.CultureInvariant);
        private static readonly Regex Define = new Regex(@"^(\w+)(?:\s+(.*))?$", RegexOptions.CultureInvariant);
        private static readonly Regex Constant = new Regex(@"^\s*(?:static\s+)?const\s+(?:float|half|int)([234]?)\s+(\w+)\s*=\s*(.*?)\s*;\s*$", RegexOptions.CultureInvariant);

        public static HoPmxRayDocument Read(string pmxAbsolutePath, string explicitEmdPath = null)
        {
            if (string.IsNullOrWhiteSpace(pmxAbsolutePath)) throw new ArgumentException("PMX path is required.", nameof(pmxAbsolutePath));
            var document = new HoPmxRayDocument();
            string directory = Path.GetDirectoryName(Path.GetFullPath(pmxAbsolutePath));
            try
            {
                string emd = FindEmd(directory, explicitEmdPath, document);
                if (emd == null) return document;
                document.EmdPath = emd;
                AddDependency(document, emd);
                bool inEffects = false;
                var cache = new Dictionary<string, HoPmxRayMaterial>(StringComparer.OrdinalIgnoreCase);
                foreach (string original in ReadText(emd).Split('\n'))
                {
                    string line = original.Trim();
                    if (line.StartsWith("[", StringComparison.Ordinal))
                    {
                        inEffects = string.Equals(line, "[Effect]", StringComparison.OrdinalIgnoreCase);
                        continue;
                    }
                    if (!inEffects || line.StartsWith(";", StringComparison.Ordinal)) continue;
                    Match match = Mapping.Match(line);
                    int materialIndex;
                    if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out materialIndex)) continue;
                    if (materialIndex > 1000000) { Warn(document, "EMD material index exceeds the supported range: " + match.Groups[1].Value); continue; }
                    string reference = Unquote(match.Groups[2].Value.Trim());
                    if (string.IsNullOrWhiteSpace(reference) || string.Equals(reference, "none", StringComparison.OrdinalIgnoreCase)) continue;
                    string fx = Resolve(Path.GetDirectoryName(emd), reference);
                    if (!File.Exists(fx)) { AddDependency(document, fx); Warn(document, "Missing Ray FX: " + fx); continue; }
                    HoPmxRayMaterial material;
                    if (!cache.TryGetValue(fx, out material))
                    {
                        material = ReadMaterial(fx, document);
                        cache.Add(fx, material);
                    }
                    if (document.Materials.ContainsKey(materialIndex)) Warn(document, "Duplicate EMD material slot; last mapping is used: " + materialIndex);
                    document.Materials[materialIndex] = material;
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
            {
                Warn(document, "Ray material settings could not be read: " + e.Message);
            }
            return document;
        }

        private static string FindEmd(string directory, string explicitPath, HoPmxRayDocument document)
        {
            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                string resolved = Resolve(directory, explicitPath);
                AddDependency(document, resolved);
                if (File.Exists(resolved)) return resolved;
                Warn(document, "Missing selected EMD: " + resolved);
                return null;
            }
            var matches = new List<string>();
            // The PMX package and its immediate material subfolders are searched. Parent
            // packages and arbitrary recursive trees are intentionally not candidates.
            CollectEmd(directory, matches);
            foreach (string child in Directory.GetDirectories(directory).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Take(256)) CollectEmd(child, matches);
            matches.Sort(StringComparer.OrdinalIgnoreCase);
            if (matches.Count == 1) return matches[0];
            if (matches.Count > 1) Warn(document, "Multiple Material_MME.emd files found; select one in the importer: " + string.Join("; ", matches));
            return null;
        }

        private static void CollectEmd(string directory, List<string> matches)
        {
            foreach (string file in Directory.GetFiles(directory, "*.emd"))
                if (string.Equals(Path.GetFileName(file), "Material_MME.emd", StringComparison.OrdinalIgnoreCase)) matches.Add(Path.GetFullPath(file));
        }

        private static HoPmxRayMaterial ReadMaterial(string path, HoPmxRayDocument document)
        {
            var state = new SourceState(document);
            state.Read(path, 0);
            var result = new HoPmxRayMaterial { FxPath = path, RecognizedTemplate = state.KnownCommon };
            foreach (var entry in state.Values) result.Macros[entry.Key] = entry.Value;
            if (!state.KnownCommon)
            {
                Warn(document, "FX is not a recognized Ray material_common template; its settings were not applied: " + path);
                return result;
            }
            result.AlbedoMapFrom = state.Int("ALBEDO_MAP_FROM", 0);
            result.BaseTexturePath = Texture(state, path, "ALBEDO", result.AlbedoMapFrom);
            result.Albedo = state.Vector("albedo", new[] { 1f, 1f, 1f });
            result.AlbedoApplyDiffuse = state.Int("ALBEDO_MAP_APPLY_DIFFUSE", 0) != 0;
            if (result.AlbedoMapFrom != 0 && (state.Int("ALBEDO_MAP_APPLY_SCALE", 0) != 0 ||
                state.Int("ALBEDO_MAP_UV_FLIP", 0) != 0 || state.Vector("albedoMapLoopNum", new[] { 1f, 1f, 1f }).Any(x => Math.Abs(x - 1f) > 0.00001f)))
                Warn(document, "Ray albedo scale/UV mapping is retained as source data and needs manual material adjustment: " + path);
            result.NormalMapFrom = state.Int("NORMAL_MAP_FROM", 0);
            result.NormalMapType = state.Int("NORMAL_MAP_TYPE", 0);
            result.NormalTexturePath = Texture(state, path, "NORMAL", result.NormalMapFrom);
            result.NormalScale = state.Float("normalMapScale", 1f);
            result.NormalUvFlip = state.Int("NORMAL_MAP_UV_FLIP", 0);
            result.NormalLoop = state.Float("normalMapLoopNum", 1f);
            if (result.NormalMapFrom != 0 && result.NormalMapType > 1)
                Warn(document, "Ray normal type " + result.NormalMapType + " needs a separate height/object-space conversion: " + path);

            result.MetallicMapFrom = state.Int("METALNESS_MAP_FROM", 0);
            result.MetallicTexturePath = Texture(state, path, "METALNESS", result.MetallicMapFrom);
            result.MetallicChannel = Channel(state, "METALNESS_MAP_SWIZZLE");
            result.Metallic = state.Float("metalness", 0f);
            result.MetallicApplyScale = state.Int("METALNESS_MAP_APPLY_SCALE", 0);
            result.MetallicUvFlip = state.Int("METALNESS_MAP_UV_FLIP", 0);
            result.MetallicLoop = state.Float("metalnessMapLoopNum", 1f);

            result.SmoothnessMapFrom = state.Int("SMOOTHNESS_MAP_FROM", 0);
            result.SmoothnessTexturePath = Texture(state, path, "SMOOTHNESS", result.SmoothnessMapFrom);
            result.SmoothnessChannel = Channel(state, "SMOOTHNESS_MAP_SWIZZLE");
            result.Smoothness = state.Float("smoothness", 0f);
            result.SmoothnessMapType = state.Int("SMOOTHNESS_MAP_TYPE", 0);
            result.SmoothnessApplyScale = state.Int("SMOOTHNESS_MAP_APPLY_SCALE", 0);
            result.SmoothnessUvFlip = state.Int("SMOOTHNESS_MAP_UV_FLIP", 0);
            result.SmoothnessLoop = state.Float("smoothnessMapLoopNum", 1f);
            result.SmoothnessFromMaterialPower = result.SmoothnessMapFrom == 9;

            result.AlphaMapFrom = state.Int("ALPHA_MAP_FROM", 0);
            result.AlphaTexturePath = Texture(state, path, "ALPHA", result.AlphaMapFrom);
            result.AlphaChannel = Channel(state, "ALPHA_MAP_SWIZZLE", 3);
            result.Alpha = state.Float("alpha", 1f);
            // Standard Ray separates opaque and transparent passes using ALPHA_THRESHOLD.
            // That threshold alone is not evidence that the whole material is Cutout.
            if (state.KnownCutout)
            {
                result.AlphaMode = "Cutout";
                result.Cutoff = Math.Max(0f, Math.Min(1f, state.Float("ALPHA_THRESHOLD", 0.75f)));
            }

            result.EmissionEnabled = state.Int("EMISSIVE_ENABLE", 0) != 0;
            if (result.EmissionEnabled)
            {
                result.EmissionMapFrom = state.Int("EMISSIVE_MAP_FROM", 0);
                result.EmissionTexturePath = Texture(state, path, "EMISSIVE", result.EmissionMapFrom);
                result.EmissionColor = state.Vector("emissive", new[] { 1f, 1f, 1f });
                result.EmissionIntensity = state.Float("emissiveIntensity", 1f);
                if (result.EmissionMapFrom != 0 || state.Int("EMISSIVE_MAP_APPLY_MORPH_COLOR", 0) != 0 ||
                    state.Int("EMISSIVE_MAP_APPLY_MORPH_INTENSITY", 0) != 0 || state.Int("EMISSIVE_MAP_APPLY_BLINK", 0) != 0)
                    Warn(document, "Ray emissive texture alpha/scale or animated controls need manual material verification: " + path);
            }
            foreach (string feature in new[] { "NORMAL_SUB_MAP_FROM", "ALBEDO_SUB_ENABLE", "CUSTOM_ENABLE", "PARALLAX_MAP_FROM", "OCCLUSION_MAP_FROM" })
                if (state.Int(feature, 0) != 0) Warn(document, "Ray feature is retained as source data but has no automatic material mapping: " + feature + " in " + path);
            return result;
        }

        private static int Channel(SourceState state, string key, int fallback = 0)
        {
            int value = state.Int(key, fallback);
            if (value >= 0 && value <= 3) return value;
            Warn(state.Document, "Unsupported Ray texture channel " + key + "=" + value + "; using " + fallback);
            return fallback;
        }

        private static string Texture(SourceState state, string fxPath, string prefix, int from)
        {
            if (from == 0 || from >= 3) return null; // Built-in PMX texture/material values are resolved by the caller.
            if (from != 1)
            {
                Warn(state.Document, "Ray " + prefix + " texture source " + from + " is not a supported 2D file source: " + fxPath);
                return null;
            }
            string reference = state.String(prefix + "_MAP_FILE");
            if (string.IsNullOrWhiteSpace(reference))
            {
                Warn(state.Document, "Enabled Ray texture has no literal file reference: " + prefix + " in " + fxPath);
                return null;
            }
            string path = Resolve(Path.GetDirectoryName(fxPath), reference);
            AddDependency(state.Document, path);
            if (!File.Exists(path)) Warn(state.Document, "Missing enabled Ray texture: " + path);
            return path;
        }

        private static string Resolve(string directory, string reference)
        {
            string normalized = reference.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.IsPathRooted(normalized) ? normalized : Path.Combine(directory, normalized));
        }

        private static string Unquote(string value)
        {
            if (value.Length >= 2 && ((value[0] == '"' && value[value.Length - 1] == '"') || (value[0] == '\'' && value[value.Length - 1] == '\''))) return value.Substring(1, value.Length - 2);
            return value;
        }

        private static void AddDependency(HoPmxRayDocument document, string path)
        {
            if (!document.Dependencies.Contains(path, StringComparer.OrdinalIgnoreCase)) document.Dependencies.Add(path);
        }
        private static void Warn(HoPmxRayDocument document, string warning)
        {
            if (!document.Warnings.Contains(warning)) document.Warnings.Add(warning);
        }

        private static string ReadText(string path)
        {
            var info = new FileInfo(path);
            if (info.Length > MaxFileBytes) throw new IOException("Ray source exceeds 4 MiB: " + path);
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            int offset = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
            try { return new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset); }
            catch (DecoderFallbackException)
            {
                // MMD packages commonly contain both Chinese ANSI and Shift-JIS effects.
                // Existing relative references disambiguate these when names are non-ASCII;
                // comments alone must not decide which texture path gets imported.
                string best = Encoding.Default.GetString(bytes);
                int bestScore = -1;
                foreach (int codePage in new[] { 936, 932 })
                {
                    try
                    {
                        string candidate = Encoding.GetEncoding(codePage).GetString(bytes);
                        int score = ReferenceScore(candidate, Path.GetDirectoryName(path));
                        if (score > bestScore) { best = candidate; bestScore = score; }
                    }
                    catch (ArgumentException) { }
                    catch (NotSupportedException) { }
                }
                return best;
            }
        }

        private static int ReferenceScore(string text, string directory)
        {
            int score = 0;
            foreach (Match match in Regex.Matches(text, "\"([^\"\\r\\n]+)\""))
            {
                try { if (File.Exists(Resolve(directory, match.Groups[1].Value))) score++; }
                catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is IOException) { }
            }
            foreach (string line in text.Split('\n'))
            {
                Match match = Mapping.Match(line.Trim());
                if (!match.Success) continue;
                try { if (File.Exists(Resolve(directory, Unquote(match.Groups[2].Value)))) score++; }
                catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is IOException) { }
            }
            return score;
        }

        private sealed class SourceState
        {
            public readonly HoPmxRayDocument Document;
            public readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly HashSet<string> stack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private int filesRead;
            public bool KnownCommon;
            public bool KnownCutout;
            public SourceState(HoPmxRayDocument document) { Document = document; }

            public void Read(string path, int depth)
            {
                AddDependency(Document, path);
                if (!File.Exists(path)) { Warn(Document, "Missing Ray include: " + path); return; }
                if (depth > 16 || ++filesRead > 128) { Warn(Document, "Ray include limit reached: " + path); return; }
                if (!stack.Add(path)) { Warn(Document, "Cyclic Ray include: " + path); return; }
                try
                {
                    string text = StripComments(ReadText(path));
                    string name = Path.GetFileName(path);
                    if (name.StartsWith("material_common", StringComparison.OrdinalIgnoreCase) &&
                        text.Contains("GetSmoothness") && text.Contains("GetMetalness") && text.Contains("ShininessToSmoothness"))
                    {
                        KnownCommon = true;
                        if (name.IndexOf("cutout", StringComparison.OrdinalIgnoreCase) >= 0) KnownCutout = true;
                    }
                    string[] lines = text.Replace("\\\r\n", " ").Replace("\\\n", " ").Split('\n');
                    var conditions = new Stack<Conditional>();
                    bool active = true;
                    foreach (string line in lines)
                    {
                        Match directive = Directive.Match(line);
                        if (directive.Success)
                        {
                            string command = directive.Groups[1].Value;
                            string argument = directive.Groups[2].Value.Trim();
                            if (command == "if" || command == "ifdef" || command == "ifndef")
                            {
                                bool condition = command == "ifdef" ? Values.ContainsKey(argument) : command == "ifndef" ? !Values.ContainsKey(argument) : EvaluateCondition(argument);
                                conditions.Push(new Conditional { Parent = active, Taken = condition, Active = active && condition });
                                active = conditions.Peek().Active;
                                continue;
                            }
                            if (command == "elif" || command == "else")
                            {
                                if (conditions.Count == 0) { Warn(Document, "Unmatched Ray #" + command + ": " + path); continue; }
                                Conditional condition = conditions.Peek();
                                bool chosen = !condition.Taken && (command == "else" || EvaluateCondition(argument));
                                condition.Active = condition.Parent && chosen;
                                condition.Taken |= chosen;
                                active = condition.Active;
                                continue;
                            }
                            if (command == "endif")
                            {
                                if (conditions.Count == 0) { Warn(Document, "Unmatched Ray #endif: " + path); continue; }
                                Conditional condition = conditions.Pop();
                                active = condition.Parent;
                                continue;
                            }
                            if (!active) continue;
                            if (command == "define")
                            {
                                Match define = Define.Match(argument);
                                if (define.Success) Values[define.Groups[1].Value] = define.Groups[2].Success ? define.Groups[2].Value.Trim() : "1";
                            }
                            else if (command == "undef") Values.Remove(argument);
                            else if (command == "include")
                            {
                                string reference = StringValue(argument, 0);
                                if (reference != null) Read(Resolve(Path.GetDirectoryName(path), reference), depth + 1);
                                else Warn(Document, "Non-literal Ray include was not read: " + argument + " in " + path);
                            }
                            else if (command == "error") Warn(Document, "Ray template declares an unsupported configuration: " + argument + " in " + path);
                            continue;
                        }
                        if (!active) continue;
                        Match constant = Constant.Match(line);
                        if (constant.Success) Values[constant.Groups[2].Value] = constant.Groups[3].Value;
                    }
                    if (conditions.Count != 0) Warn(Document, "Unbalanced Ray preprocessor conditions: " + path);
                }
                finally { stack.Remove(path); }
            }

            private bool EvaluateCondition(string expression)
            {
                double value;
                if (HoPmxRayExpression.TryEvaluate(expression, Values, true, out value)) return value != 0;
                Warn(Document, "Ray preprocessor expression could not be evaluated; branch skipped: " + expression);
                return false;
            }
            public int Int(string key, int fallback)
            {
                float value = Float(key, fallback);
                return value >= int.MinValue && value <= int.MaxValue ? (int)value : fallback;
            }
            public float Float(string key, float fallback)
            {
                string expression;
                double value;
                if (!Values.TryGetValue(key, out expression)) return fallback;
                if (HoPmxRayExpression.TryEvaluate(expression, Values, false, out value) && !double.IsInfinity(value) && !double.IsNaN(value)) return (float)value;
                Warn(Document, "Ray value could not be evaluated: " + key + "=" + expression);
                return fallback;
            }
            public string String(string key)
            {
                string value;
                return Values.TryGetValue(key, out value) ? StringValue(value, 0) : null;
            }
            private string StringValue(string value, int depth)
            {
                if (depth > 16) return null;
                value = value.Trim();
                if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"') return Unquote(value);
                string other;
                return Values.TryGetValue(value, out other) ? StringValue(other, depth + 1) : null;
            }
            public float[] Vector(string key, float[] fallback)
            {
                string value;
                if (!Values.TryGetValue(key, out value)) return fallback;
                Match constructor = Regex.Match(value, @"^(?:float|half)[234]\s*\((.*)\)$");
                if (constructor.Success) value = constructor.Groups[1].Value;
                string[] components = value.Split(',');
                var result = new float[3];
                for (int i = 0; i < 3; i++)
                {
                    double number;
                    if (!HoPmxRayExpression.TryEvaluate(components[Math.Min(i, components.Length - 1)], Values, false, out number))
                    { Warn(Document, "Ray vector could not be evaluated: " + key + "=" + value); return fallback; }
                    result[i] = (float)number;
                }
                return result;
            }
        }

        private sealed class Conditional { public bool Parent; public bool Taken; public bool Active; }

        private static string StripComments(string input)
        {
            var output = new StringBuilder(input.Length);
            bool quoted = false, block = false, line = false;
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i], next = i + 1 < input.Length ? input[i + 1] : '\0';
                if (line) { if (c == '\n') { line = false; output.Append(c); } continue; }
                if (block) { if (c == '*' && next == '/') { block = false; i++; output.Append(' '); } else if (c == '\n') output.Append(c); continue; }
                if (!quoted && c == '/' && next == '/') { line = true; i++; continue; }
                if (!quoted && c == '/' && next == '*') { block = true; i++; output.Append(' '); continue; }
                if (c == '"' && (i == 0 || input[i - 1] != '\\')) quoted = !quoted;
                output.Append(c);
            }
            return output.ToString();
        }
    }
}
