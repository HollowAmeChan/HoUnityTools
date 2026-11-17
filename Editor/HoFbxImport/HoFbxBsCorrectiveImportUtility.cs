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
            public string rotationOrder;
            public string angleUnit;
            public string kernel;
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
            public string bsName;
            public Target[] targets;
            public string comment;
            public RbfConfig rbf;
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
            public float[] pose;
            public float value = 1f;
        }

        [Serializable]
        public sealed class RbfConfig
        {
            public float radius = 0.5f;
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
            if (config == null || config.schema != Schema || config.version != 2)
                throw new InvalidDataException("不支持的 BS 修型 schema/version；需要 hotools.bs-corrective v2。");
            if (config.poseSpace != "LOCAL" || config.rotationOrder != "XYZ" || config.angleUnit != "radian" ||
                config.kernel != "gaussian" || config.fbxBoneAxes != "Y_X")
                throw new InvalidDataException("BS 修型必须声明 LOCAL / XYZ / radian / gaussian 和配套 HoFBX Y_X 骨轴。");
            if (config.rules == null)
                throw new InvalidDataException("BS 修型 JSON 缺少 rules；显式空数组可清除此来源的导入规则。");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (Rule rule in config.rules)
            {
                if (rule == null || string.IsNullOrWhiteSpace(rule.id) || !ids.Add(rule.id))
                    throw new InvalidDataException("BS 修型规则 id 为空或重复。");
                string label = "BS 修型规则「" + rule.id + "」";
                if (string.IsNullOrWhiteSpace(rule.armatureName) || string.IsNullOrWhiteSpace(rule.bone))
                    throw new InvalidDataException(label + " 缺少 armatureName / bone。");
                Target[] targets = rule.targets;
                if (targets == null || targets.Length == 0)
                    throw new InvalidDataException(label + " 缺少 targets。");
                foreach (Target target in targets)
                    if (target == null || string.IsNullOrWhiteSpace(target.mesh) || string.IsNullOrWhiteSpace(target.shapeKey))
                        throw new InvalidDataException(label + " 包含不完整的目标网格或形态键。");
                if (rule.rbf == null || rule.rbf.controlPoints == null ||
                    rule.rbf.controlPoints.Length == 0 && !rule.rbf.includeBase)
                    throw new InvalidDataException(label + " 至少需要一个 RBF 姿态控制点。");
                if (rule.rbf.basePose != null && rule.rbf.basePose.Length != 3)
                    throw new InvalidDataException(label + " 的 basePose 必须包含 XYZ 三个值。");
                foreach (RbfPoint point in rule.rbf.controlPoints ?? Array.Empty<RbfPoint>())
                    if (point == null || point.pose == null || point.pose.Length != 3)
                        throw new InvalidDataException(label + " 包含不完整的 RBF 控制点。");
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
                Transform armature = ResolveArmature(rig.transform, rule.armatureName);
                string measurementName = string.IsNullOrWhiteSpace(rule.measurementBone) ? rule.bone : rule.measurementBone;
                Transform bone = UniqueBone(armature, measurementName);
                Target[] targets = rule.targets;
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
                        // Keep the old id for a single target; suffix expanded targets so
                        // editor diagnostics can distinguish rows from one Blender rule.
                        id = targets.Length == 1 ? rule.id : rule.id + ":" + targetIndex,
                        sourceId = sourceId,
                        enabled = rule.enabled,
                        bone = bone,
                        target = target,
                        shapeName = targetData.shapeKey,
                        bsName = string.IsNullOrWhiteSpace(rule.bsName) ? targetData.shapeKey : rule.bsName,
                        rbfRadius = rule.rbf.radius,
                        rbfIncludeBase = rule.rbf.includeBase,
                        rbfBase = ToVector3(rule.rbf.basePose),
                        rbfPoints = ToRbfPoints(rule.rbf.controlPoints),
                        comment = rule.comment ?? string.Empty,
                        bonePath = AnimationUtility.CalculateTransformPath(bone, searchRoot),
                        targetPath = AnimationUtility.CalculateTransformPath(target.transform, searchRoot),
                        // Keep the authored source name for auditability; bone points to
                        // the exported MCH measurement transform when preprocessing used it.
                        sourceBone = rule.bone,
                    });
                }
            }
            return result;
        }

        private static int CountTargets(Rule rule) => rule.targets?.Length ?? 0;

        private static Vector3 ToVector3(float[] values) => values != null && values.Length == 3
            ? new Vector3(values[0], values[1], values[2]) : Vector3.zero;

        private static List<HoBsCorrective.RbfPoint> ToRbfPoints(RbfPoint[] points)
        {
            var result = new List<HoBsCorrective.RbfPoint>();
            foreach (RbfPoint point in points ?? Array.Empty<RbfPoint>())
                result.Add(new HoBsCorrective.RbfPoint { pose = ToVector3(point.pose), weight = point.value });
            return result;
        }

        private static Transform ResolveSearchRoot(Transform rig)
        {
            Animator animator = rig.GetComponentInParent<Animator>();
            if (animator != null) return animator.transform;
            GameObject prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(rig.gameObject);
            if (prefabRoot != null) return prefabRoot.transform;
            return rig.parent != null ? rig.parent : rig;
        }

        private static Transform ResolveArmature(Transform rig, string name)
        {
            if (rig.name == name) return rig;
            Transform[] candidates = rig.GetComponentsInChildren<Transform>(true).Where(x => x.name == name).ToArray();
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
