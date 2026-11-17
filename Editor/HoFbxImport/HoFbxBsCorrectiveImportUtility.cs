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
            public string rotationOrder;
            public string angleUnit;
            public string rotationSpace;
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
            public string mesh;
            public string shapeKey;
            public string bsName;
            public string mode;
            public float rbfRadius = 0.5f;
            public bool rbfIncludeBase = true;
            public float[] rbfBase;
            public RbfPoint[] rbfPoints;
            // New Blender authoring format: one corrective rule may drive many targets.
            // mesh/shapeKey remain for v1 metadata written by older HoTools versions.
            public Target[] targets;
            public string expression;
            public string comment;
            public Variable[] variables;
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
            public float weight = 1f;
        }

        [Serializable]
        public sealed class Variable
        {
            public string name;
            public string bone;
            public string channel;
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
            if (config == null || config.schema != Schema || config.version != 1)
                throw new InvalidDataException("不支持的 BS 修型 schema/version；需要 hotools.bs-corrective v1。");
            if (config.rotationOrder != "XYZ" || config.angleUnit != "radian" ||
                config.rotationSpace != "LOCAL" || config.fbxBoneAxes != "Y_X")
                throw new InvalidDataException("BS 修型必须声明 LOCAL / XYZ / radian 和配套 HoFBX Y_X 骨轴。");
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
                Target[] targets = TargetsFor(rule);
                if (targets.Length == 0)
                    throw new InvalidDataException(label + " 缺少 targets（或旧格式 mesh / shapeKey）。");
                foreach (Target target in targets)
                    if (target == null || string.IsNullOrWhiteSpace(target.mesh) || string.IsNullOrWhiteSpace(target.shapeKey))
                        throw new InvalidDataException(label + " 包含不完整的目标网格或形态键。");
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (Variable variable in rule.variables ?? Array.Empty<Variable>())
                {
                    if (variable == null || string.IsNullOrWhiteSpace(variable.name) ||
                        !names.Add(variable.name) || string.IsNullOrWhiteSpace(variable.bone) ||
                        (variable.channel != "X" && variable.channel != "Y" && variable.channel != "Z"))
                        throw new InvalidDataException(label + " 包含不完整或重复的额外旋转变量。");
                }
                if (string.Equals(rule.mode, "RBF", StringComparison.Ordinal))
                {
                    if (rule.rbfPoints == null || rule.rbfPoints.Length == 0 && !rule.rbfIncludeBase)
                        throw new InvalidDataException(label + " 至少需要一个 RBF 姿态控制点。");
                    if (rule.rbfBase != null && rule.rbfBase.Length != 3)
                        throw new InvalidDataException(label + " 的 rbfBase 必须包含 XYZ 三个值。");
                    foreach (RbfPoint point in rule.rbfPoints ?? Array.Empty<RbfPoint>())
                        if (point == null || point.pose == null || point.pose.Length != 3)
                            throw new InvalidDataException(label + " 包含不完整的 RBF 控制点。");
                }
                else if (!HoBsCorrectiveExpression.TryParse(rule.expression, names, out _, out string expressionError))
                    throw new InvalidDataException(label + " 表达式无效：" + expressionError);
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
                var variables = new List<HoBsCorrective.Variable>();
                foreach (Variable variable in rule.variables ?? Array.Empty<Variable>())
                {
                    Transform variableBone = UniqueBone(armature, variable.bone);
                    variables.Add(new HoBsCorrective.Variable
                    {
                        name = variable.name,
                        bone = variableBone,
                        channel = (HoBsCorrective.Channel)Enum.Parse(typeof(HoBsCorrective.Channel), variable.channel),
                        bonePath = AnimationUtility.CalculateTransformPath(variableBone, searchRoot),
                        sourceBone = variable.bone,
                    });
                }
                Target[] targets = TargetsFor(rule);
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
                        expression = rule.expression,
                        bsName = string.IsNullOrWhiteSpace(rule.bsName) ? targetData.shapeKey : rule.bsName,
                        mode = string.IsNullOrEmpty(rule.mode) ? "EXPRESSION" : rule.mode,
                        rbfRadius = rule.rbfRadius,
                        rbfIncludeBase = rule.rbfIncludeBase,
                        rbfBase = ToVector3(rule.rbfBase),
                        rbfPoints = ToRbfPoints(rule.rbfPoints),
                        comment = rule.comment ?? string.Empty,
                        bonePath = AnimationUtility.CalculateTransformPath(bone, searchRoot),
                        targetPath = AnimationUtility.CalculateTransformPath(target.transform, searchRoot),
                        // Keep the authored source name for auditability; bone points to
                        // the exported MCH measurement transform when preprocessing used it.
                        sourceBone = rule.bone,
                        variables = new List<HoBsCorrective.Variable>(variables),
                    });
                }
            }
            return result;
        }

        private static Target[] TargetsFor(Rule rule)
        {
            if (rule.targets != null && rule.targets.Length > 0) return rule.targets;
            if (!string.IsNullOrWhiteSpace(rule.mesh) || !string.IsNullOrWhiteSpace(rule.shapeKey))
                return new[] { new Target { mesh = rule.mesh, shapeKey = rule.shapeKey } };
            return Array.Empty<Target>();
        }

        private static int CountTargets(Rule rule) => TargetsFor(rule).Length;

        private static Vector3 ToVector3(float[] values) => values != null && values.Length == 3
            ? new Vector3(values[0], values[1], values[2]) : Vector3.zero;

        private static List<HoBsCorrective.RbfPoint> ToRbfPoints(RbfPoint[] points)
        {
            var result = new List<HoBsCorrective.RbfPoint>();
            foreach (RbfPoint point in points ?? Array.Empty<RbfPoint>())
                result.Add(new HoBsCorrective.RbfPoint { pose = ToVector3(point.pose), weight = point.weight });
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
