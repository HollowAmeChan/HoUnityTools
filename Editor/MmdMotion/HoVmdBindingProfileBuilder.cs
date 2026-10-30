#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hollow.HoUnityTools.MmdMotion;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.MmdMotion
{
    /// <summary>Creates a reusable VMD binding asset from an existing FBX/GLTF hierarchy.</summary>
    public static class HoVmdBindingProfileBuilder
    {
        public static HoVmdBindingProfile Build(HoVmdAnimation animation, GameObject target, string assetPath)
        {
            if (animation == null) throw new ArgumentNullException(nameof(animation));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                throw new ArgumentException("The profile path must be an Assets/... path.", nameof(assetPath));

            var profile = ScriptableObject.CreateInstance<HoVmdBindingProfile>();
            profile.name = Path.GetFileNameWithoutExtension(assetPath);
            profile.sourceModel = animation.modelName;

            var transforms = target.GetComponentsInChildren<Transform>(true);
            var byName = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform transform in transforms)
                if (!byName.ContainsKey(transform.name)) byName.Add(transform.name, transform);
            var animator = target.GetComponentInChildren<Animator>(true);
            var mappedTransforms = new HashSet<Transform>();

            foreach (string sourceName in animation.boneFrames.Select(x => x.boneName).Distinct(StringComparer.Ordinal))
            {
                Transform targetTransform = FindBone(sourceName, byName, animator);
                if (targetTransform == null) continue;
                profile.bones.Add(new HoVmdBindingProfile.BoneMap
                {
                    sourceName = sourceName,
                    targetPath = RelativePath(target.transform, targetTransform)
                });
                mappedTransforms.Add(targetTransform);
            }

            var shapes = new Dictionary<string, (SkinnedMeshRenderer renderer, int index)>(StringComparer.Ordinal);
            foreach (SkinnedMeshRenderer renderer in target.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh == null) continue;
                for (int i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
                {
                    string name = renderer.sharedMesh.GetBlendShapeName(i);
                    if (!shapes.ContainsKey(name)) shapes.Add(name, (renderer, i));
                    string normalized = Normalize(name);
                    if (!shapes.ContainsKey(normalized)) shapes.Add(normalized, (renderer, i));
                }
            }
            foreach (string sourceName in animation.morphFrames.Select(x => x.morphName).Distinct(StringComparer.Ordinal))
            {
                if (!TryShape(shapes, sourceName, out var shape)) continue;
                profile.morphs.Add(new HoVmdBindingProfile.MorphMap
                {
                    sourceName = sourceName,
                    rendererPath = RelativePath(target.transform, shape.renderer.transform),
                    blendShapeName = shape.renderer.sharedMesh.GetBlendShapeName(shape.index)
                });
            }

            AssetDatabase.CreateAsset(profile, AssetDatabase.GenerateUniqueAssetPath(assetPath));
            AssetDatabase.SaveAssets();
            return profile;
        }

        [MenuItem("Tools/Ho/MMD/Create Binding Profile From Selected VMD And Character")]
        private static void CreateFromSelection()
        {
            var animation = Selection.activeObject as HoVmdAnimation;
            var target = Selection.activeGameObject;
            if (animation == null || target == null)
            {
                EditorUtility.DisplayDialog("Ho VMD Binding", "Select a HoVmdAnimation asset, then a target character GameObject.", "OK");
                return;
            }
            string path = EditorUtility.SaveFilePanelInProject("Save VMD Binding Profile", target.name + " VMD Binding", "asset", "Choose a profile path.");
            if (string.IsNullOrEmpty(path)) return;
            Build(animation, target, path);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<HoVmdBindingProfile>(path);
        }

        private static bool TryShape(Dictionary<string, (SkinnedMeshRenderer renderer, int index)> shapes, string source, out (SkinnedMeshRenderer renderer, int index) result)
        {
            if (shapes.TryGetValue(source, out result)) return true;
            return shapes.TryGetValue(Normalize(source), out result);
        }

        private static Transform FindBone(string source, Dictionary<string, Transform> byName, Animator animator)
        {
            if (byName.TryGetValue(source, out Transform exact)) return exact;
            string key = Normalize(source);
            foreach (var pair in byName)
                if (Normalize(pair.Key) == key) return pair.Value;
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
            string core = name.TrimStart('左', '右', 'Ｉ', 'K');
            if (name == "センター") return HumanBodyBones.Hips;
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

        private static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var chars = value.Where(char.IsLetterOrDigit).ToArray();
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
