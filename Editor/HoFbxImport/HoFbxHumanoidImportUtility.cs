#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.RigConstraints
{
    /// <summary>Import authored Humanoid mapping and T-Pose from the same HoFBX metadata.</summary>
    public static class HoFbxHumanoidImportUtility
    {
        [Serializable] private sealed class Mapping { public Armature[] armatures; }
        [Serializable] private sealed class Armature { public string armatureName; public Bone[] bones; public TPose tPose; }
        [Serializable] private sealed class Bone { public string boneName, humanName; }
        [Serializable] private sealed class TPose
        {
            public string space, fbxBoneAxes, rotationRepresentation;
            public PoseBone[] bones;
        }
        [Serializable] private sealed class PoseBone
        {
            public string boneName, parentName;
            public float[] restPosition, position, rotation, scale;
        }

        public static bool TryBuildHumanBones(TextAsset metadata, out HumanBone[] bones, out int count, out string error)
        {
            bones = Array.Empty<HumanBone>(); count = 0; error = string.Empty;
            try { bones = BuildHumanBones(Parse(metadata)); count = bones.Length; return true; }
            catch (Exception exception) { error = exception.Message; return false; }
        }

        public static int ReferenceBoneCount(TextAsset metadata) =>
            Parse(metadata).armatures.Sum(a => a?.tPose?.bones?.Length ?? 0);

        public static HumanDescription BuildDescription(TextAsset metadata, ModelImporter importer)
        {
            if (importer == null) throw new ArgumentNullException(nameof(importer));
            Mapping mapping = Parse(metadata);
            HumanDescription description = importer.humanDescription;
            description.human = BuildHumanBones(mapping);
            if (description.human.Length == 0) throw new InvalidDataException("最终 Humanoid 映射为空。");
            if (!mapping.armatures.Any(a => a?.tPose != null)) return description;
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(importer.assetPath);
            if (model == null) throw new InvalidDataException("无法读取 FBX 原始骨架。");
            Transform[] transforms = model.GetComponentsInChildren<Transform>(true);
            var overrides = new Dictionary<Transform, SkeletonBone>();
            foreach (Armature armature in mapping.armatures.Where(a => a?.tPose != null))
            {
                Transform[] roots = transforms.Where(t => t.name == armature.armatureName).ToArray();
                // Unity may strip an FBX node named Armature. Only a single
                // authored armature can unambiguously use the model root.
                Transform root = roots.Length == 1 ? roots[0] :
                    roots.Length == 0 && mapping.armatures.Length == 1 ? model.transform : null;
                if (root == null) throw new InvalidDataException($"T-Pose 骨架「{armature.armatureName}」无法唯一定位。");
                Transform[] hierarchy = root.GetComponentsInChildren<Transform>(true);
                var resolved = new Dictionary<PoseBone, Transform>();
                var unitRatios = new List<float>();
                foreach (PoseBone pose in armature.tPose.bones)
                {
                    Transform[] matches = hierarchy.Where(t => t.name == pose.boneName).ToArray();
                    if (matches.Length != 1)
                        throw new InvalidDataException($"T-Pose 骨骼「{pose.boneName}」匹配到 {matches.Length} 个节点；请检查骨架和 Optimize Game Objects。");
                    Transform bone = matches[0];
                    if (!string.IsNullOrEmpty(pose.parentName) && (bone.parent == null || bone.parent.name != pose.parentName))
                        throw new InvalidDataException($"T-Pose 骨骼「{pose.boneName}」的父级与 FBX 不一致，请重新导出 Humanoid 数据。");
                    resolved.Add(pose, bone);
                    float authoredLength = V(pose.restPosition).magnitude;
                    if (authoredLength > 1e-5f && bone.localPosition.magnitude > 1e-7f)
                        unitRatios.Add(bone.localPosition.magnitude / authoredLength);
                }
                unitRatios.Sort();
                float units = unitRatios.Count == 0 ? 1f : unitRatios[unitRatios.Count / 2];
                foreach (var pair in resolved)
                {
                    PoseBone pose = pair.Key;
                    Transform bone = pair.Value;
                    Vector3 offset = V(pose.position) * units;
                    offset.x = -offset.x;
                    Quaternion rotation = new Quaternion(pose.rotation[0], -pose.rotation[1], -pose.rotation[2], pose.rotation[3]).normalized;
                    var value = new SkeletonBone { name = bone.name,
                        position = bone.localPosition + bone.localRotation * Vector3.Scale(bone.localScale, offset),
                        rotation = bone.localRotation * rotation,
                        scale = Vector3.Scale(bone.localScale, V(pose.scale)) };
                    if (!overrides.TryAdd(bone, value))
                        throw new InvalidDataException($"骨骼「{bone.name}」被多个 T-Pose 标定重复指定。");
                }
            }
            description.skeleton = transforms.Select(t => overrides.TryGetValue(t, out SkeletonBone value) ? value :
                new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray();
            return description;
        }

        public static int Apply(TextAsset metadata, string modelPath)
        {
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("无法获取 FBX ModelImporter。");
            HumanDescription next = BuildDescription(metadata, importer);
            HumanDescription previous = importer.humanDescription;
            var previousType = importer.animationType;
            var previousSetup = importer.avatarSetup;
            Avatar previousSource = importer.sourceAvatar;
            try
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.humanDescription = next;
                importer.SaveAndReimport();
                if (ReferenceBoneCount(metadata) > 0)
                {
                    Animator animator = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath).GetComponent<Animator>();
                    if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                        throw new InvalidDataException("保存的 T-Pose 无法建立有效 Humanoid Avatar，请在 Blender 检查并重新保存标定。");
                }
                return next.human.Length;
            }
            catch
            {
                importer.animationType = previousType;
                importer.avatarSetup = previousSetup;
                importer.sourceAvatar = previousSource;
                importer.humanDescription = previous;
                importer.SaveAndReimport();
                throw;
            }
        }

        private static Mapping Parse(TextAsset metadata)
        {
            if (metadata == null) throw new InvalidDataException("没有 Humanoid 映射 JSON。");
            Mapping mapping = JsonUtility.FromJson<Mapping>(metadata.text);
            if (mapping?.armatures == null) throw new InvalidDataException("Humanoid JSON 缺少 armatures。");
            foreach (Armature armature in mapping.armatures.Where(a => a?.tPose != null))
            {
                TPose pose = armature.tPose;
                if (pose.space != "BONE_REST_LOCAL" || pose.fbxBoneAxes != "Y_X" || pose.rotationRepresentation != "quaternion-xyzw" ||
                    pose.bones == null || pose.bones.Length == 0)
                    throw new InvalidDataException("Humanoid T-Pose 必须使用 HoFBX Y_X 静置骨骼局部四元数变换。");
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (PoseBone bone in pose.bones)
                {
                    if (bone == null || string.IsNullOrWhiteSpace(bone.boneName) || !names.Add(bone.boneName) ||
                        !Valid(bone.restPosition, 3) || !Valid(bone.position, 3) || !Valid(bone.rotation, 4) || !Valid(bone.scale, 3) ||
                        bone.scale.Any(v => Math.Abs(v) < 1e-8) || Math.Abs(bone.rotation.Sum(v => (double)v*v) - 1.0) > 1e-4)
                        throw new InvalidDataException("Humanoid T-Pose 包含重复骨骼或无效变换。");
                }
                if ((armature.bones ?? Array.Empty<Bone>()).Any(b => b != null && !string.IsNullOrEmpty(b.boneName) && !names.Contains(b.boneName)))
                    throw new InvalidDataException("Humanoid T-Pose 缺少映射骨骼，请在 Blender 重新保存完整标定。");
            }
            return mapping;
        }

        private static HumanBone[] BuildHumanBones(Mapping mapping)
        {
            var mapped = new Dictionary<string, string>(StringComparer.Ordinal);
            var usedBones = new HashSet<string>(StringComparer.Ordinal);
            foreach (Armature armature in mapping.armatures)
                foreach (Bone bone in armature?.bones ?? Array.Empty<Bone>())
                    if (bone != null && !string.IsNullOrEmpty(bone.boneName) && !string.IsNullOrEmpty(bone.humanName) &&
                        HumanTrait.BoneName.Contains(bone.humanName) && !mapped.ContainsKey(bone.humanName) && usedBones.Add(bone.boneName))
                        mapped.Add(bone.humanName, bone.boneName);
            return HumanTrait.BoneName.Where(mapped.ContainsKey).Select(name => new HumanBone {
                humanName = name, boneName = mapped[name], limit = new HumanLimit { useDefaultValues = true } }).ToArray();
        }
        private static bool Valid(float[] values, int count) => values != null && values.Length == count &&
            values.All(v => !float.IsNaN(v) && !float.IsInfinity(v));
        private static Vector3 V(float[] values) => new Vector3(values[0], values[1], values[2]);
    }
}
#endif
