#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Hollow.HoUnityTools.Editor.MmdMotion;
using Hollow.HoUnityTools.MmdMotion;
using Hollow.HoUnityTools.RigConstraints;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class VmdTrackMappingValidation
{
    [Serializable] private sealed class Report
    {
        public string animation;
        public long scanMilliseconds;
        public int transformsInSkeleton, boneTracks, mappedBones, morphTracks, mappedMorphs, ikTracks, mappedIkTargets;
        public bool profileOptional, ikSwitchesApplied, prefabUnchanged, scenePreserved;
        public List<string> unmappedBones = new List<string>();
        public List<string> unmappedMorphs = new List<string>();
        public List<string> unmappedIk = new List<string>();
        public List<string> failures = new List<string>();
    }

    public static void Run()
    {
        const string prefabPath = "Assets/Hollow/WTD-summer/WTD-BUILD.prefab";
        const string methodName = "ScanAndMatch";
        Scene active = SceneManager.GetActiveScene();
        bool wasDirty = active.isDirty;
        string prefabHash = Hash(prefabPath);
        GameObject root = null;
        UnityEditor.Editor inspector = null;
        Scene preview = EditorSceneManager.NewPreviewScene();
        var result = new Report();
        try
        {
            HoVmdAnimation animation = AssetDatabase.FindAssets("t:HoVmdAnimation")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<HoVmdAnimation>)
                .Where(x => x != null)
                .Where(x => x.boneFrames.Any(f => !string.IsNullOrEmpty(f.boneName)))
                .OrderByDescending(x => x.boneFrames.Select(f => f.boneName).Where(n => !string.IsNullOrEmpty(n))
                    .Distinct(StringComparer.Ordinal).Count() +
                    x.morphFrames.Select(f => f.morphName).Where(n => !string.IsNullOrEmpty(n))
                    .Distinct(StringComparer.Ordinal).Count())
                .FirstOrDefault();
            if (animation == null) throw new InvalidOperationException("No imported VMD bone animation was found.");
            result.animation = AssetDatabase.GetAssetPath(animation);

            root = PrefabUtility.LoadPrefabContents(prefabPath);
            Transform skeleton = root.GetComponentsInChildren<Transform>(true).Single(x => x.name == "Armature-Swimsuit");
            result.transformsInSkeleton = skeleton.GetComponentsInChildren<Transform>(true).Length;
            var player = root.GetComponentInChildren<HoVmdMotionPlayer>(true);
            if (player == null) player = root.AddComponent<HoVmdMotionPlayer>();
            player.Animation = animation;
            player.ModelRoot = root.transform;
            player.SkeletonRoot = skeleton;
            player.BindingProfile = null;
            inspector = UnityEditor.Editor.CreateEditor(player);
            MethodInfo scan = inspector.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (scan == null) throw new MissingMethodException(inspector.GetType().FullName, methodName);
            var stopwatch = Stopwatch.StartNew();
            scan.Invoke(inspector, null);
            stopwatch.Stop();
            result.scanMilliseconds = stopwatch.ElapsedMilliseconds;

            result.boneTracks = animation.boneFrames.Select(x => x.boneName).Where(x => !string.IsNullOrEmpty(x))
                .Distinct(StringComparer.Ordinal).Count();
            result.mappedBones = player.BoneBindings.Count(x => x != null && x.target != null);
            result.unmappedBones = player.BoneBindings.Where(x => x == null || x.target == null)
                .Select(x => x?.vmdName ?? "<null>").ToList();
            result.morphTracks = animation.morphFrames.Select(x => x.morphName).Where(x => !string.IsNullOrEmpty(x))
                .Distinct(StringComparer.Ordinal).Count();
            result.mappedMorphs = player.MorphBindings.Count(x => x != null && x.renderer != null && x.blendShapeIndex >= 0);
            result.unmappedMorphs = player.MorphBindings.Where(x => x == null || x.renderer == null || x.blendShapeIndex < 0)
                .Select(x => x?.vmdName ?? "<null>").ToList();
            result.ikTracks = animation.showIkFrames.Where(x => x.toggles != null).SelectMany(x => x.toggles)
                .Select(x => x.boneName).Distinct(StringComparer.Ordinal).Count();
            result.mappedIkTargets = player.IkToggleBindings.Count(x => x != null && x.targetObject != null);
            result.unmappedIk = player.IkToggleBindings.Where(x => x == null || x.targetObject == null)
                .Select(x => x?.vmdName ?? "<null>").ToList();
            result.profileOptional = player.BindingProfile == null;

            var constraints = root.GetComponentsInChildren<HoIkConstraint>(true)
                .Where(x => x.Target != null).ToDictionary(x => Normalize(x.IkName), x => x);
            foreach (var binding in player.BoneBindings.Where(x => x != null && x.vmdName != null))
                if (constraints.TryGetValue(Normalize(binding.vmdName), out var constraint) &&
                    binding.target != constraint.Target)
                    result.failures.Add("IK bone track did not map to Ho IK Target: " + binding.vmdName);
            foreach (var binding in player.IkToggleBindings.Where(x => x != null && x.targetObject != null))
                if (constraints.TryGetValue(Normalize(binding.vmdName), out var constraint) &&
                    binding.targetObject != constraint.gameObject)
                    result.failures.Add("IK toggle did not map to its Ho IK component object: " + binding.vmdName);

            player.ApplyAtFrame(0.0f);
            result.ikSwitchesApplied = true;
            foreach (var binding in player.IkToggleBindings)
            {
                if (binding == null || binding.targetObject == null ||
                    !constraints.TryGetValue(Normalize(binding.vmdName), out var constraint)) continue;
                bool expected = SampleIk(animation, binding.vmdName, 0.0f);
                if (constraint.SolveEnabled != expected)
                {
                    result.ikSwitchesApplied = false;
                    result.failures.Add("VMD IK toggle did not update Ho IK state: " + binding.vmdName);
                }
            }
            if (player.BoneBindings.Count != result.boneTracks) result.failures.Add("Bone list does not contain exactly the active VMD bone tracks.");
            if (player.MorphBindings.Count != result.morphTracks) result.failures.Add("Morph list does not contain exactly the active VMD morph tracks.");
            if (player.IkToggleBindings.Count != result.ikTracks) result.failures.Add("IK list does not contain exactly the active VMD IK toggle tracks.");
        }
        catch (Exception error) { result.failures.Add(error.ToString()); }
        finally
        {
            if (inspector != null) Object.DestroyImmediate(inspector);
            if (root != null) PrefabUtility.UnloadPrefabContents(root);
            EditorSceneManager.ClosePreviewScene(preview);
            result.prefabUnchanged = Hash(prefabPath) == prefabHash;
            result.scenePreserved = SceneManager.GetActiveScene() == active && active.isDirty == wasDirty;
            if (!result.prefabUnchanged) result.failures.Add("WTD prefab changed during validation.");
            if (!result.scenePreserved) result.failures.Add("Active scene or dirty state changed during validation.");
            Directory.CreateDirectory("Library/HoMmdStages");
            File.WriteAllText("Library/HoMmdStages/vmd-track-mapping-validation.json",
                JsonUtility.ToJson(result, true));
        }
        if (result.failures.Count > 0) throw new InvalidOperationException(string.Join("\n", result.failures));
        UnityEngine.Debug.Log("HO_VMD_TRACK_MAPPING_VALIDATION_PASS bones=" + result.mappedBones + "/" + result.boneTracks +
            " morphs=" + result.mappedMorphs + "/" + result.morphTracks +
            " ik=" + result.mappedIkTargets + "/" + result.ikTracks);
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var builder = new StringBuilder(value.Normalize(NormalizationForm.FormKC).Length);
        foreach (char c in value.Normalize(NormalizationForm.FormKC))
            if (char.IsLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
        return builder.ToString();
    }

    private static bool SampleIk(HoVmdAnimation animation, string name, float frame)
    {
        bool enabled = true;
        foreach (var frameData in animation.showIkFrames)
        {
            if (frameData.frame > frame || frameData.toggles == null) continue;
            foreach (var toggle in frameData.toggles)
                if (toggle.boneName == name) enabled = toggle.enabled;
        }
        return enabled;
    }

    private static string Hash(string path)
    {
        using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));
    }
}
#endif
