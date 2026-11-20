#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hollow.HoUnityTools.Constraints;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.RigConstraints
{
    /// <summary>Explicit, validated HoFBX BS corrective import. Never runs from an asset postprocessor.</summary>
    public static class HoFbxBsCorrectiveImportUtility
    {
        public const string MetadataKind = "bsCorrective";
        public const string MetadataSuffix = "_bs_corrective.json";
        public const string Schema = "hotools.bs-corrective";

        [Serializable]
        public sealed class Config
        {
            public string schema;
            public int version;
            public string poseSpace;
            public string rotationRepresentation;
            public string angleUnit;
            public string kernel;
            public string evaluation;
            public string distanceMetric;
            public string neutralMode;
            public string fbxBoneAxes;
            public Rule[] rules;
        }

        [Serializable]
        public sealed class Rule
        {
            public string id;
            public bool enabled = true;
            public string armatureName;
            public string bone;
            public string measurementBone;
            public string[] influenceObjects;
            public Task[] tasks;
            public RbfConfig rbf;
        }

        [Serializable]
        public sealed class Task
        {
            public string id;
            public string bsName;
            public string pointId;
            public bool enabled = true;
            public float weight = 1f;
            public Target[] targets;
        }

        [Serializable]
        public sealed class Target
        {
            public string mesh;
            public string shapeKey;
        }

        [Serializable]
        public sealed class RbfPoint
        {
            public string id;
            public float[] pose;
            public float radius;
        }

        [Serializable]
        public sealed class RbfConfig
        {
            public bool includeBase = true;
            public float[] basePose;
            public RbfPoint[] controlPoints;
        }

        /// <summary>Validate metadata and every scene reference without modifying the scene.</summary>
        public static bool TryPreview(TextAsset metadata, GameObject rig, out int count, out string error)
        {
            count = 0;
            error = null;
            try
            {
                Config config = Parse(metadata);
                count = config.rules.Sum(CountTargets);
                if (rig != null) Prepare(config, rig, SourceId(metadata));
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        /// <summary>Replace only this metadata asset's rules; preserve manual rules and other sources.</summary>
        public static int Apply(TextAsset metadata, GameObject rig, Transform host = null)
        {
            if (rig == null) throw new ArgumentNullException(nameof(rig), "请选择目标骨架。");
            if (Application.isPlaying || EditorUtility.IsPersistent(rig) || !rig.scene.IsValid())
                throw new InvalidOperationException("请在编辑模式的场景实例或 Prefab 内容中导入 BS 修型。");
            if (host == null) host = rig.transform;
            if (EditorUtility.IsPersistent(host) || !host.gameObject.scene.IsValid() || host.gameObject.scene != rig.scene)
                throw new InvalidOperationException("BS 修型中控必须位于目标骨架所在的场景或 Prefab 内容中。");

            Config config = Parse(metadata);
            string sourceId = SourceId(metadata);
            List<HoBsCorrective.Rule> prepared = Prepare(config, rig, sourceId);
            HoBsCorrective[] existing = host.GetComponents<HoBsCorrective>();
            if (existing.Length > 1)
                throw new InvalidOperationException("中控物体上有多个 HoBsCorrective，无法唯一确定要更新的组件。");

            Undo.IncrementCurrentGroup();
            int transaction = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("导入 HoFBX BS 修型");
            try
            {
                HoBsCorrective component = existing.Length == 0
                    ? Undo.AddComponent<HoBsCorrective>(host.gameObject)
                    : existing[0];
                Undo.RecordObject(component, "更新 BS 修型规则");
                component.ReplaceImportedRules(sourceId, prepared);
                EditorUtility.SetDirty(component);
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                Undo.CollapseUndoOperations(transaction);
                return prepared.Count;
            }
            catch
            {
                Undo.RevertAllDownToGroup(transaction);
                throw;
            }
        }

        public static Config Parse(TextAsset metadata)
        {
            if (metadata == null) throw new ArgumentNullException(nameof(metadata), "没有 BS 修型 JSON。");
            Config config = JsonUtility.FromJson<Config>(metadata.text);
            if (config == null || config.schema != Schema || config.version != 5)
                throw new InvalidDataException("不支持的 BS 修型 schema/version；需要 hotools.bs-corrective v5 独立叠加。");
            if (config.poseSpace != "LOCAL" || config.rotationRepresentation != "quaternion-xyzw" || config.angleUnit != "radian" ||
                config.kernel != "smoothstep-compact" || config.fbxBoneAxes != "Y_X" ||
                config.evaluation != "independent" || config.distanceMetric != "quaternion-angle" ||
                config.neutralMode != "triangle-smoothstep")
                throw new InvalidDataException("BS 修型必须声明 v5 局部四元数角距离、独立平滑衰减及固定基准归零契约。");
            if (config.rules == null)
                throw new InvalidDataException("BS 修型 JSON 缺少 rules；显式空数组可清除此来源的导入规则。");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var outputs = new HashSet<string>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (Rule rule in config.rules)
            {
                if (rule == null || string.IsNullOrWhiteSpace(rule.id) || !ids.Add(rule.id))
                    throw new InvalidDataException("BS 修型规则 id 为空或重复。");
                string label = "BS 修型规则「" + rule.id + "」";
                if (string.IsNullOrWhiteSpace(rule.armatureName) || string.IsNullOrWhiteSpace(rule.bone))
                    throw new InvalidDataException(label + " 缺少 armatureName / bone。");
                if (rule.rbf == null || rule.rbf.controlPoints == null ||
                    rule.rbf.controlPoints.Length == 0)
                    throw new InvalidDataException(label + " 至少需要一个 RBF 姿态控制点。");
                if (!rule.rbf.includeBase || !ValidPose(rule.rbf.basePose) ||
                    rule.rbf.basePose[0] != 0f || rule.rbf.basePose[1] != 0f ||
                    rule.rbf.basePose[2] != 0f || Math.Abs(rule.rbf.basePose[3]) != 1f)
                    throw new InvalidDataException(label + " 的基准姿态必须固定为零。");
                var pointIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (RbfPoint point in rule.rbf.controlPoints ?? Array.Empty<RbfPoint>())
                    if (point == null || string.IsNullOrWhiteSpace(point.id) || !pointIds.Add(point.id) ||
                        !ValidPose(point.pose) || !Finite(point.radius) || point.radius <= 0f || point.radius > Math.PI + 1e-6 ||
                        NeutralAngle(point.pose) < 1e-5)
                        throw new InvalidDataException(label + " 包含不完整、重复或非有限的 RBF 控制点。");
                if (rule.tasks == null || rule.tasks.Length == 0)
                    throw new InvalidDataException(label + " 缺少修型任务。");
                var taskIds = new HashSet<string>(StringComparer.Ordinal);
                var usedPoints = new HashSet<string>(StringComparer.Ordinal);
                foreach (Task task in rule.tasks)
                {
                    if (task == null || string.IsNullOrWhiteSpace(task.id) || !taskIds.Add(task.id) ||
                        string.IsNullOrWhiteSpace(task.pointId) || !pointIds.Contains(task.pointId) || !usedPoints.Add(task.pointId))
                        throw new InvalidDataException(label + " 每个控制点必须唯一对应一个修型任务。");
                    if (string.IsNullOrWhiteSpace(task.bsName) || !names.Add(rule.armatureName + "\0" + task.bsName))
                        throw new InvalidDataException(label + " 的任务 BS 名称为空或重复。");
                    if (!Finite(task.weight) || task.weight < 0f || task.weight > 1f)
                        throw new InvalidDataException(label + " 的姿态权重必须在 0..1。");
                    if (task.targets == null || task.targets.Length == 0)
                        throw new InvalidDataException(label + " 的修型任务缺少目标。");
                    foreach (Target target in task.targets)
                    {
                        if (target == null || string.IsNullOrWhiteSpace(target.mesh) || string.IsNullOrWhiteSpace(target.shapeKey))
                            throw new InvalidDataException(label + " 包含不完整的目标网格或形态键。");
                        if (rule.enabled && task.enabled && !outputs.Add(target.mesh + "\0" + target.shapeKey))
                            throw new InvalidDataException(label + " 存在重复驱动的目标形态键。");
                    }
                }
                if (!usedPoints.SetEquals(pointIds))
                    throw new InvalidDataException(label + " 有控制点未关联修型任务。");
            }
            return config;
        }

        private static List<HoBsCorrective.Rule> Prepare(Config config, GameObject rig, string sourceId)
        {
            Transform searchRoot = ResolveSearchRoot(rig.transform);
            SkinnedMeshRenderer[] renderers = searchRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var result = new List<HoBsCorrective.Rule>();
            foreach (Rule rule in config.rules)
            {
                Transform armature = ResolveArmature(rig.transform, rule.armatureName,
                    config.rules.Select(value => value.armatureName).Distinct().Count() == 1);
                string measurementName = string.IsNullOrWhiteSpace(rule.measurementBone) ? rule.bone : rule.measurementBone;
                Transform bone = UniqueBone(armature, measurementName);
                foreach (Task task in rule.tasks)
                {
                    RbfPoint point = rule.rbf.controlPoints.Single(value => value.id == task.pointId);
                    Target[] targets = task.targets;
                    for (int targetIndex = 0; targetIndex < targets.Length; targetIndex++)
                    {
                        Target targetData = targets[targetIndex];
                        SkinnedMeshRenderer[] matchingMeshes = renderers.Where(x => x.name == targetData.mesh).ToArray();
                        if (matchingMeshes.Length != 1)
                            throw new InvalidDataException($"网格「{targetData.mesh}」匹配到 {matchingMeshes.Length} 个 SkinnedMeshRenderer，必须唯一。");
                        SkinnedMeshRenderer target = matchingMeshes[0];
                        if (target.sharedMesh == null || target.sharedMesh.GetBlendShapeIndex(targetData.shapeKey) < 0)
                            throw new InvalidDataException($"网格「{targetData.mesh}」没有精确名称形态键「{targetData.shapeKey}」。");
                        result.Add(new HoBsCorrective.Rule
                        {
                            id = rule.id + ":" + task.id + ":" + targetIndex,
                            sourceId = sourceId,
                            enabled = rule.enabled && task.enabled,
                            bone = bone,
                            target = target,
                            shapeName = targetData.shapeKey,
                            bsName = task.bsName,
                            // FBX Y_X exports mirror Blender's local Y/Z
                            // quaternion components into Unity's handedness.
                            pose = new Quaternion(point.pose[0], -point.pose[1], -point.pose[2], point.pose[3]).normalized,
                            radius = point.radius,
                            weight = task.weight,
                            referenceRotation = BindRotation(bone),
                            measurementIsMch = measurementName != rule.bone,
                            bonePath = AnimationUtility.CalculateTransformPath(bone, searchRoot),
                            targetPath = AnimationUtility.CalculateTransformPath(target.transform, searchRoot),
                            // Keep the authored source name for auditability; bone points to
                            // the exported MCH measurement transform when preprocessing used it.
                            sourceBone = rule.bone,
                        });
                    }
                }
            }
            return result;
        }

        private static int CountTargets(Rule rule) => rule.tasks.Sum(task => task.targets.Length);

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool ValidPose(float[] value) => value != null && value.Length == 4 && value.All(Finite) &&
            Math.Abs(value.Sum(v => (double)v * v) - 1.0) < 1e-5;
        private static Quaternion ToQuaternion(float[] value) =>
            new Quaternion(value[0], value[1], value[2], value[3]).normalized;
        private static Quaternion BindRotation(Transform bone)
        {
            Transform original = PrefabUtility.GetCorrespondingObjectFromOriginalSource(bone);
            return original != null ? original.localRotation : bone.localRotation;
        }
        private static double NeutralAngle(float[] value) =>
            2.0 * Math.Atan2(Math.Sqrt((double)value[0]*value[0] + (double)value[1]*value[1] +
                (double)value[2]*value[2]), Math.Abs(value[3]));

        private static Transform ResolveSearchRoot(Transform rig)
        {
            Animator animator = rig.GetComponentInParent<Animator>();
            if (animator != null) return animator.transform;
            GameObject prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(rig.gameObject);
            if (prefabRoot != null) return prefabRoot.transform;
            return rig.parent != null ? rig.parent : rig;
        }

        private static Transform ResolveArmature(Transform rig, string name, bool allowModelRoot)
        {
            if (rig.name == name) return rig;
            Transform[] candidates = rig.GetComponentsInChildren<Transform>(true).Where(x => x.name == name).ToArray();
            // Unity may collapse the sole armature object into the FBX asset
            // root and rename it after the file. Bone/mesh names remain strict.
            if (candidates.Length == 0 && allowModelRoot) return rig;
            if (candidates.Length != 1)
                throw new InvalidDataException($"目标骨架下的 armature「{name}」匹配到 {candidates.Length} 个物体，必须唯一。");
            return candidates[0];
        }

        private static Transform UniqueBone(Transform armature, string name)
        {
            Transform[] candidates = armature.GetComponentsInChildren<Transform>(true)
                .Where(x => x != armature && x.name == name).ToArray();
            if (candidates.Length != 1)
                throw new InvalidDataException($"骨架「{armature.name}」中的骨骼「{name}」匹配到 {candidates.Length} 个物体，必须唯一。");
            return candidates[0];
        }

        private static string SourceId(TextAsset metadata)
        {
            string path = AssetDatabase.GetAssetPath(metadata);
            string guid = string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
            return "HoFBX.bsCorrective:" + (string.IsNullOrEmpty(guid) ? metadata.name : guid);
        }
    }
}
#endif
