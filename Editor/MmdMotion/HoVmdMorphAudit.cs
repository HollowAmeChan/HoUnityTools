#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Hollow.HoUnityTools.MmdMotion;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.MmdMotion
{
    [Serializable]
    public sealed class HoVmdMorphAuditResult
    {
        public string vmdAsset;
        public string vmdModel;
        public string target;
        public int required;
        public int exactMatches;
        public int normalizedMatches;
        public int localMatches;
        public int profileMatches;
        public int missing;
        public int conflicts;
        public string[] exactMatchNames = Array.Empty<string>();
        public string[] normalizedMatchNames = Array.Empty<string>();
        public string[] localMappedNames = Array.Empty<string>();
        public string[] missingNames = Array.Empty<string>();
        public string[] conflictNames = Array.Empty<string>();
        public string[] profileMappedNames = Array.Empty<string>();
        public string[] profileConflictNames = Array.Empty<string>();

        public bool IsComplete => missing == 0 && conflicts == 0;
    }

    /// <summary>Checks VMD morph dependencies against a Unity character hierarchy.</summary>
    public static class HoVmdMorphAudit
    {
        private sealed class Shape
        {
            public SkinnedMeshRenderer renderer;
            public int index;
            public string name;
        }

        public static HoVmdMorphAuditResult Audit(HoVmdAnimation animation, GameObject target,
            HoVmdBindingProfile profile = null, IEnumerable<HoVmdMotionPlayer.MorphBinding> localBindings = null)
        {
            if (animation == null) throw new ArgumentNullException(nameof(animation));
            if (target == null) throw new ArgumentNullException(nameof(target));
            var shapes = new List<Shape>();
            foreach (SkinnedMeshRenderer renderer in target.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh == null) continue;
                for (int i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
                    shapes.Add(new Shape { renderer = renderer, index = i, name = renderer.sharedMesh.GetBlendShapeName(i) });
            }

            var missing = new List<string>();
            var conflicts = new List<string>();
            var profileMapped = new List<string>();
            var profileConflicts = new List<string>();
            var exactNames = new List<string>();
            var normalizedNames = new List<string>();
            var localNames = new List<string>();
            var localBySource = (localBindings ?? Enumerable.Empty<HoVmdMotionPlayer.MorphBinding>())
                .Where(x => x != null && !string.IsNullOrEmpty(x.vmdName))
                .GroupBy(x => x.vmdName, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.Ordinal);
            int exact = 0, normalized = 0, profileCount = 0, localCount = 0;
            string[] required = (animation.morphFrames ?? new List<HoVmdMorphFrame>())
                .Select(x => x.morphName).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            foreach (string source in required)
            {
                if (localBySource.TryGetValue(source, out var mappings))
                {
                    var validMappings = mappings.Where(x => x.renderer != null &&
                        (x.renderer.transform == target.transform || x.renderer.transform.IsChildOf(target.transform)) &&
                        x.renderer.sharedMesh != null &&
                        x.blendShapeIndex >= 0 && x.blendShapeIndex < x.renderer.sharedMesh.blendShapeCount).ToArray();
                    if (validMappings.Length > 0)
                    {
                        localCount++;
                        localNames.Add(source);
                        if (validMappings.Length != mappings.Length) conflicts.Add(source);
                        continue;
                    }
                }
                if (profile != null && TryProfile(profile, source, target, out bool profileConflict))
                {
                    profileCount++; profileMapped.Add(source);
                    if (profileConflict) { conflicts.Add(source); profileConflicts.Add(source); }
                    continue;
                }
                List<Shape> exactShapes = shapes.Where(x => x.name == source).ToList();
                if (exactShapes.Count == 1) { exact++; exactNames.Add(source); continue; }
                if (exactShapes.Count > 1) { conflicts.Add(source); continue; }
                string normalizedSource = Normalize(source);
                List<Shape> normalizedShapes = shapes.Where(x => Normalize(x.name) == normalizedSource).ToList();
                if (normalizedShapes.Count == 1) { normalized++; normalizedNames.Add(source); continue; }
                if (normalizedShapes.Count > 1) { conflicts.Add(source); continue; }
                missing.Add(source);
            }
            return new HoVmdMorphAuditResult
            {
                vmdAsset = AssetDatabase.GetAssetPath(animation),
                vmdModel = animation.modelName,
                target = target.name,
                required = required.Length,
                exactMatches = exact,
                normalizedMatches = normalized,
                localMatches = localCount,
                profileMatches = profileCount,
                missing = missing.Count,
                conflicts = conflicts.Count,
                exactMatchNames = exactNames.ToArray(),
                normalizedMatchNames = normalizedNames.ToArray(),
                localMappedNames = localNames.ToArray(),
                missingNames = missing.ToArray(),
                conflictNames = conflicts.ToArray(),
                profileMappedNames = profileMapped.ToArray(),
                profileConflictNames = profileConflicts.ToArray()
            };
        }

        public static string WriteJson(HoVmdAnimation animation, GameObject target, HoVmdBindingProfile profile, string path,
            IEnumerable<HoVmdMotionPlayer.MorphBinding> localBindings = null)
        {
            HoVmdMorphAuditResult result = Audit(animation, target, profile, localBindings);
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(fullPath, JsonUtility.ToJson(result, true));
            return JsonUtility.ToJson(result, true);
        }

        private static bool TryProfile(HoVmdBindingProfile profile, string source, GameObject root, out bool conflict)
        {
            conflict = false;
            int matches = 0;
            if (profile == null || profile.morphs == null) return false;
            foreach (var map in profile.morphs)
            {
                if (map == null || map.sourceName != source) continue;
                matches++;
                Transform transform = ResolvePath(root.transform, map.rendererPath);
                SkinnedMeshRenderer renderer = transform != null ? transform.GetComponent<SkinnedMeshRenderer>() : null;
                if (renderer == null || renderer.sharedMesh == null) { conflict = true; return true; }
                int index = renderer.sharedMesh.GetBlendShapeIndex(map.blendShapeName);
                if (index < 0) conflict = true;
            }
            if (matches > 1) conflict = true;
            return matches > 0;
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            string normalized = value.Normalize(NormalizationForm.FormKC);
            var builder = new StringBuilder(normalized.Length);
            foreach (char c in normalized)
                if (char.IsLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
            return builder.ToString();
        }

        private static Transform ResolvePath(Transform root, string path)
        {
            if (root == null) return null;
            if (string.IsNullOrEmpty(path) || path == ".") return root;
            return root.Find(path);
        }
    }
}
#endif
