#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Hollow.HoUnityTools.MmdMotion;
using Hollow.HoUnityTools.RigConstraints;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.MmdMotion
{
    /// <summary>Creates a reusable VMD binding asset from an existing FBX/GLTF hierarchy.</summary>
    public static class HoVmdBindingProfileBuilder
    {
        public static HoVmdBindingProfile Build(HoVmdAnimation animation, GameObject target, string assetPath, Transform skeletonRoot = null)
        {
            if (animation == null) throw new ArgumentNullException(nameof(animation));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                throw new ArgumentException("The profile path must be an Assets/... path.", nameof(assetPath));
            if (skeletonRoot != null && skeletonRoot != target.transform && !skeletonRoot.IsChildOf(target.transform))
                throw new ArgumentException("主骨架必须位于目标角色根下。", nameof(skeletonRoot));

            var profile = ScriptableObject.CreateInstance<HoVmdBindingProfile>();
            profile.name = Path.GetFileNameWithoutExtension(assetPath);
            profile.sourceModel = animation.modelName;

            var transforms = (skeletonRoot != null ? skeletonRoot : target.transform).GetComponentsInChildren<Transform>(true);
            var byName = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (var group in transforms.GroupBy(x => x.name, StringComparer.Ordinal))
                if (group.Count() == 1) byName.Add(group.Key, group.First());
            var animator = skeletonRoot != null ? skeletonRoot.GetComponentInParent<Animator>() : target.GetComponent<Animator>();
            var ik = target.GetComponentsInChildren<HoIkConstraint>(true);

            foreach (string sourceName in animation.boneFrames.Select(x => x.boneName).Distinct(StringComparer.Ordinal))
            {
                Transform targetTransform = FindBone(sourceName, byName, animator);
                var controls = ik.Where(x => Normalize(x.IkName) == Normalize(sourceName) && x.Target != null).ToArray();
                if (controls.Length == 1) targetTransform = controls[0].Target;
                if (targetTransform == null) continue;
                profile.bones.Add(new HoVmdBindingProfile.BoneMap
                {
                    sourceName = sourceName,
                    targetPath = RelativePath(target.transform, targetTransform)
                });
            }

            var shapes = new List<(SkinnedMeshRenderer renderer, int index)>();
            foreach (SkinnedMeshRenderer renderer in target.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh == null) continue;
                for (int i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
                {
                    shapes.Add((renderer, i));
                }
            }
            foreach (string sourceName in animation.morphFrames.Select(x => x.morphName).Distinct(StringComparer.Ordinal))
            {
                var candidates = shapes.Where(x => x.renderer.sharedMesh.GetBlendShapeName(x.index) == sourceName).ToArray();
                if (candidates.Length == 0) candidates = shapes.Where(x => Normalize(x.renderer.sharedMesh.GetBlendShapeName(x.index)) == Normalize(sourceName)).ToArray();
                if (candidates.Length != 1) continue;
                var shape = candidates[0];
                profile.morphs.Add(new HoVmdBindingProfile.MorphMap
                {
                    sourceName = sourceName,
                    rendererPath = RelativePath(target.transform, shape.renderer.transform),
                    blendShapeName = shape.renderer.sharedMesh.GetBlendShapeName(shape.index)
                });
            }
            var switches = animation.showIkFrames.SelectMany(x => x.toggles ?? Array.Empty<HoVmdIkToggle>())
                .Select(x => x.boneName).Union(ik.Select(x => x.IkName), StringComparer.Ordinal);
            foreach (string source in switches)
            {
                var candidates = ik.Where(x => Normalize(x.IkName) == Normalize(source)).ToArray();
                if (candidates.Length == 1)
                    profile.ik.Add(new HoVmdBindingProfile.IkMap { sourceName = source,
                        constraintPath = RelativePath(target.transform, candidates[0].transform), constraintName = candidates[0].IkName });
            }

            AssetDatabase.CreateAsset(profile, AssetDatabase.GenerateUniqueAssetPath(assetPath));
            AssetDatabase.SaveAssetIfDirty(profile);
            return profile;
        }

        [MenuItem("HoUnityTools/MMD/角色绑定与表情检查")]
        private static void CreateFromSelection()
        {
            HoVmdBindingWindow.Open();
        }

        private static Transform FindBone(string source, Dictionary<string, Transform> byName, Animator animator)
        {
            if (byName.TryGetValue(source, out Transform exact)) return exact;
            string key = Normalize(source);
            var normalized = byName.Where(x => Normalize(x.Key) == key).Select(x => x.Value).ToArray();
            if (normalized.Length == 1) return normalized[0];
            string alias = BoneAlias(source);
            if (alias != null && byName.TryGetValue(alias, out var aliased)) return aliased;
            if (animator != null)
            {
                HumanBodyBones human = HumanBone(source);
                if (human != HumanBodyBones.LastBone && animator.isHuman)
                    return animator.GetBoneTransform(human);
            }
            return null;
        }

        private static HumanBodyBones HumanBone(string name)
        {
            string side = name.StartsWith("左", StringComparison.Ordinal) ? "Left" : name.StartsWith("右", StringComparison.Ordinal) ? "Right" : string.Empty;
            if (name == "上半身") return HumanBodyBones.Spine;
            if (name == "上半身2") return HumanBodyBones.Chest;
            if (name == "下半身") return HumanBodyBones.Hips;
            if (name == "首") return HumanBodyBones.Neck;
            if (name == "頭") return HumanBodyBones.Head;
            if (name == "左肩" || name == "右肩") return side == "Left" ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder;
            if (name == "左腕" || name == "右腕") return side == "Left" ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
            if (name == "左ひじ" || name == "右ひじ") return side == "Left" ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm;
            if (name == "左手首" || name == "右手首") return side == "Left" ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
            if (name == "左足" || name == "右足") return side == "Left" ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg;
            if (name == "左ひざ" || name == "右ひざ") return side == "Left" ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg;
            if (name == "左足首" || name == "右足首") return side == "Left" ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot;
            if (name == "左つま先" || name == "右つま先") return side == "Left" ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes;
            return HumanBodyBones.LastBone;
        }

        private static string BoneAlias(string name)
        {
            switch (name)
            {
                case "下半身": return "hips";
                case "上半身": return "spine";
                case "上半身2": return "chest";
                case "首": return "neck";
                case "頭": return "head";
            }
            string suffix = name.StartsWith("左", StringComparison.Ordinal) ? ".L" : name.StartsWith("右", StringComparison.Ordinal) ? ".R" : null;
            if (suffix == null) return null;
            switch (name.Substring(1))
            {
                case "肩": return "shoulder" + suffix;
                case "腕": return "upper_arm" + suffix;
                case "ひじ": return "lower_arm" + suffix;
                case "手首": return "hand" + suffix;
                case "足": return "upper_leg" + suffix;
                case "ひざ": return "lower_leg" + suffix;
                case "足首": return "foot" + suffix;
                case "つま先": return "toes" + suffix;
                default: return null;
            }
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var chars = value.Normalize(NormalizationForm.FormKC).Where(char.IsLetterOrDigit).ToArray();
            return new string(chars).ToLowerInvariant();
        }

        private static string RelativePath(Transform root, Transform target)
        {
            if (target == root) return ".";
            var names = new List<string>();
            for (Transform cursor = target; cursor != null && cursor != root; cursor = cursor.parent)
                names.Add(cursor.name);
            names.Reverse();
            return string.Join("/", names);
        }
    }
}
#endif
