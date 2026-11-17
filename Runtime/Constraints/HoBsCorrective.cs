using System;
using System.Collections.Generic;
using Hollow.HoUnityTools.FaceTracking;
using UnityEngine;

namespace Hollow.HoUnityTools.Constraints
{
    /// <summary>One central list of final local-rotation driven corrective blend shapes.</summary>
    [ExecuteAlways, DisallowMultipleComponent, DefaultExecutionOrder(32001)]
    [AddComponentMenu("HoUnityTools/Constraints/Ho BS Corrective")]
    public sealed partial class HoBsCorrective : MonoBehaviour
    {
        public enum Channel { X, Y, Z }

        [Serializable]
        public sealed class Variable
        {
            public string name = "other_x";
            public Transform bone;
            public Channel channel;
            [HideInInspector] public string bonePath = string.Empty;
            [HideInInspector] public string sourceBone = string.Empty;
        }

        [Serializable]
        public sealed class Rule
        {
            public string id = string.Empty;
            public bool enabled = true;
            public Transform bone;
            public string expression = "clamp(x / 1.57079632679, 0, 1)";
            public string bsName = "_BSC_pose1";
            public string mode = "EXPRESSION";
            public float rbfRadius = 0.5f;
            public bool rbfIncludeBase = true;
            public Vector3 rbfBase;
            public List<RbfPoint> rbfPoints = new List<RbfPoint>();
            public SkinnedMeshRenderer target;
            public string shapeName = string.Empty;
            [TextArea(1, 4)] public string comment = string.Empty;
            public List<Variable> variables = new List<Variable>();
            [HideInInspector] public string sourceId = string.Empty;
            [HideInInspector] public string bonePath = string.Empty;
            [HideInInspector] public string targetPath = string.Empty;
            [HideInInspector] public string sourceBone = string.Empty;
            [NonSerialized] public string lastError;
            [NonSerialized] public Vector3 lastRadians;
            [NonSerialized] public float lastOutput;
        }

        [Serializable]
        public sealed class RbfPoint
        {
            public Vector3 pose;
            [Range(0f, 1f)] public float weight = 1f;
        }

        [SerializeField] private List<Rule> rules = new List<Rule>();
        [NonSerialized] private bool editorPreview;
        private bool rebuild = true;
        private bool blockedByOwner;
        private int lastOwnershipVersion;
        private static int ownershipVersion;
        private static readonly Dictionary<(SkinnedMeshRenderer, Mesh, int), HoBsCorrective> Owners =
            new Dictionary<(SkinnedMeshRenderer, Mesh, int), HoBsCorrective>();
        private readonly List<CompiledRule> compiled = new List<CompiledRule>();

        private sealed class CompiledRule
        {
            public Rule source;
            public bool enabled;
            public Transform bone;
            public SkinnedMeshRenderer target;
            public Mesh mesh;
            public string shapeName, text;
            public int index = -1;
            public HoBsCorrectiveExpression expression;
            public float[] rbfCoefficients;
            public Vector3[] rbfPositions;
            public float[] rbfWeights;
            public VariableState[] variables;
            public readonly Dictionary<string, float> values = new Dictionary<string, float>(StringComparer.Ordinal);
            public Func<string, float> read;
            public bool owns, written;
            public float restoreWeight, lastWritten;
            public float Read(string name) => values.TryGetValue(name, out float value) ? value : 0f;
        }

        private struct VariableState
        {
            public Variable source;
            public string name;
            public Transform bone;
            public Channel channel;
        }

        public IList<Rule> Rules => rules;
        public bool EditorPreviewActive => editorPreview;

        private bool ShouldEvaluate => isActiveAndEnabled && gameObject.scene.IsValid() &&
            (Application.IsPlaying(gameObject) || !Application.isPlaying && editorPreview);

        private void OnEnable()
        {
            Active.Add(this);
            InstallFinalPosePass();
            rebuild = true;
        }

        private void OnDisable() { Active.Remove(this); Release(); }
        private void OnDestroy() { Active.Remove(this); Release(); }
        private void OnValidate() { rebuild = true; }

        public void SetEditorPreview(bool value)
        {
            editorPreview = value;
            if (value) EvaluateNow(); else Release();
        }

        public void InvalidateBindings() { rebuild = true; }

        /// <summary>Import replaces only rules belonging to the same metadata asset; manual rows survive.</summary>
        public void ReplaceImportedRules(string sourceId, IEnumerable<Rule> replacements)
        {
            if (string.IsNullOrEmpty(sourceId)) throw new ArgumentException("导入来源标识不能为空", nameof(sourceId));
            var next = new List<Rule>();
            foreach (Rule rule in rules)
                if (rule == null || !string.Equals(rule.sourceId, sourceId, StringComparison.Ordinal)) next.Add(rule);
            if (replacements != null)
                foreach (Rule rule in replacements)
                {
                    if (rule == null) continue;
                    rule.sourceId = sourceId;
                    next.Add(rule);
                }
            rules = next;
            rebuild = true;
        }

        /// <summary>Call after the host has completed Animator / IK / constraints / script bone writes.</summary>
        public void EvaluateNow()
        {
            if (!ShouldEvaluate) { Release(); return; }
            if (rebuild || BindingsChanged() || blockedByOwner && lastOwnershipVersion != ownershipVersion) Build();
            foreach (CompiledRule item in compiled)
            {
                if (!item.enabled || (item.expression == null && !string.Equals(item.source.mode, "RBF", StringComparison.Ordinal)) || !item.owns || !ValidTarget(item)) continue;
                if (item.bone == null || HasMissingVariableBone(item))
                { item.source.lastError = "测量骨骼已丢失"; Restore(item); continue; }
                if (HoFaceOutputOwnership.IsReserved(item.target, item.index))
                { item.source.lastError = "目标形态键已被面捕接管"; Restore(item); continue; }
                Vector3 radians = ReadLocalRadians(item.bone.localRotation);
                item.source.lastRadians = radians;
                item.values["x"] = radians.x; item.values["y"] = radians.y; item.values["z"] = radians.z;
                foreach (VariableState variable in item.variables)
                    item.values[variable.name] = Select(ReadLocalRadians(variable.bone.localRotation), variable.channel);
                float output = item.expression != null
                    ? item.expression.Evaluate(item.read)
                    : EvaluateRbf(item, radians);
                if (float.IsNaN(output) || float.IsInfinity(output)) output = 0;
                item.source.lastOutput = Mathf.Clamp01(output);
                float current = item.target.GetBlendShapeWeight(item.index);
                if (!item.written || !Mathf.Approximately(current, item.lastWritten)) item.restoreWeight = current;
                item.lastWritten = item.source.lastOutput * 100f;
                item.target.SetBlendShapeWeight(item.index, item.lastWritten);
                item.written = true;
                item.source.lastError = null;
            }
        }

        public static bool ValidateRule(Rule rule, out string error)
        {
            error = null;
            if (rule == null) { error = "规则为空"; return false; }
            if (rule.bone == null) { error = "未指定测量骨骼"; return false; }
            if (rule.target == null || rule.target.sharedMesh == null) { error = "未指定有效目标网格"; return false; }
            if (ResolveShape(rule.target.sharedMesh, rule.shapeName) < 0)
            { error = "目标网格中找不到形态键：" + rule.shapeName; return false; }
            if (string.Equals(rule.mode, "RBF", StringComparison.Ordinal))
            {
                if (!TryBuildRbf(rule, out _, out _, out error)) return false;
                return true;
            }
            return TryExpression(rule, out _, out error);
        }

        private static bool TryBuildRbf(Rule rule, out Vector3[] positions, out float[] coefficients, out string error)
        {
            positions = null; coefficients = null; error = null;
            var points = new List<RbfPoint>();
            if (rule.rbfIncludeBase) points.Add(new RbfPoint { pose = rule.rbfBase, weight = 0f });
            if (rule.rbfPoints != null) points.AddRange(rule.rbfPoints);
            if (points.Count == 0) { error = "RBF 至少需要一个姿态控制点"; return false; }
            positions = new Vector3[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                positions[i] = points[i].pose;
                for (int j = 0; j < i; j++)
                    if ((positions[i] - positions[j]).sqrMagnitude < 1e-10f)
                    { error = "RBF 控制点过近或重复"; return false; }
            }
            var matrix = new double[points.Count, points.Count];
            for (int row = 0; row < points.Count; row++)
                for (int column = 0; column < points.Count; column++)
                    matrix[row, column] = Gaussian(Vector3.Distance(positions[row], positions[column]), rule.rbfRadius) + (row == column ? 1e-6 : 0);
            var values = new double[points.Count];
            for (int i = 0; i < points.Count; i++) values[i] = points[i].weight;
            coefficients = SolveLinear(matrix, values);
            return coefficients != null;
        }

        private static float Gaussian(float distance, float radius)
        {
            float safe = Mathf.Max(radius, 1e-6f);
            return Mathf.Exp(-(distance * distance) / (2f * safe * safe));
        }

        private static float[] SolveLinear(double[,] source, double[] values)
        {
            int size = values.Length; var matrix = new double[size, size + 1];
            for (int row = 0; row < size; row++) { for (int column = 0; column < size; column++) matrix[row, column] = source[row, column]; matrix[row, size] = values[row]; }
            for (int pivot = 0; pivot < size; pivot++)
            {
                int best = pivot; for (int row = pivot + 1; row < size; row++) if (Math.Abs(matrix[row, pivot]) > Math.Abs(matrix[best, pivot])) best = row;
                if (Math.Abs(matrix[best, pivot]) < 1e-9) return null;
                for (int column = pivot; column <= size; column++) { double temp = matrix[pivot, column]; matrix[pivot, column] = matrix[best, column]; matrix[best, column] = temp; }
                double divisor = matrix[pivot, pivot]; for (int column = pivot; column <= size; column++) matrix[pivot, column] /= divisor;
                for (int row = 0; row < size; row++) { if (row == pivot) continue; double factor = matrix[row, pivot]; for (int column = pivot; column <= size; column++) matrix[row, column] -= factor * matrix[pivot, column]; }
            }
            var result = new float[size]; for (int i = 0; i < size; i++) result[i] = (float)matrix[i, size]; return result;
        }

        private static float EvaluateRbf(CompiledRule item, Vector3 current)
        {
            if (item.rbfPositions == null || item.rbfCoefficients == null) return 0f;
            float output = 0f;
            for (int i = 0; i < item.rbfPositions.Length; i++) output += item.rbfCoefficients[i] * Gaussian(Vector3.Distance(current, item.rbfPositions[i]), item.source.rbfRadius);
            return output;
        }

        private static bool TryExpression(Rule rule, out HoBsCorrectiveExpression expression, out string error)
        {
            var names = new List<string>();
            if (rule.variables != null)
                foreach (Variable variable in rule.variables)
                {
                    if (variable == null || variable.bone == null)
                    { expression = null; error = "附加变量未指定测量骨骼"; return false; }
                    names.Add(variable.name);
                }
            return HoBsCorrectiveExpression.TryParse(rule.expression, names, out expression, out error);
        }

        private static int ResolveShape(Mesh mesh, string name)
        {
            if (mesh == null || string.IsNullOrEmpty(name)) return -1;
            // Correctives must not silently bind to another similarly named shape.
            return mesh.GetBlendShapeIndex(name);
        }

        private void Build()
        {
            Release();
            rebuild = false;
            blockedByOwner = false;
            foreach (Rule rule in rules)
            {
                var item = new CompiledRule
                {
                    source = rule, enabled = rule != null && rule.enabled, bone = rule?.bone,
                    target = rule?.target, mesh = rule?.target != null ? rule.target.sharedMesh : null,
                    shapeName = rule?.shapeName, text = rule?.expression
                };
                item.read = item.Read;
                var variables = new List<VariableState>();
                if (rule?.variables != null)
                    foreach (Variable variable in rule.variables)
                        variables.Add(new VariableState { source = variable, name = variable?.name,
                            bone = variable?.bone, channel = variable != null ? variable.channel : Channel.X });
                item.variables = variables.ToArray();
                compiled.Add(item);
                if (rule == null) continue;
                rule.lastError = null;
                rule.lastOutput = 0;
                if (!item.enabled) continue;
                if (!ValidateRule(rule, out string error))
                { rule.lastError = error; continue; }
                if (string.Equals(rule.mode, "RBF", StringComparison.Ordinal))
                {
                    if (!TryBuildRbf(rule, out item.rbfPositions, out item.rbfCoefficients, out error)) { rule.lastError = error; continue; }
                    var weights = new List<float>();
                    if (rule.rbfIncludeBase) weights.Add(0f);
                    foreach (RbfPoint point in rule.rbfPoints ?? new List<RbfPoint>()) weights.Add(point.weight);
                    item.rbfWeights = weights.ToArray();
                }
                else if (!TryExpression(rule, out item.expression, out error))
                { rule.lastError = error; continue; }
                item.index = ResolveShape(item.mesh, rule.shapeName);
                var key = (item.target, item.mesh, item.index);
                if (Owners.TryGetValue(key, out HoBsCorrective owner) && owner != null)
                {
                    rule.lastError = owner == this ? "同一中控中重复的目标形态键" : "目标形态键已被另一个 BS 修型组件接管";
                    if (owner != this) blockedByOwner = true;
                    continue;
                }
                Owners[key] = this;
                item.owns = true;
            }
            lastOwnershipVersion = ownershipVersion;
        }

        private bool BindingsChanged()
        {
            if (compiled.Count != rules.Count) return true;
            for (int i = 0; i < rules.Count; i++)
            {
                Rule rule = rules[i]; CompiledRule item = compiled[i];
                if (!ReferenceEquals(rule, item.source)) return true;
                if (rule == null) continue;
                if (rule.enabled != item.enabled || rule.bone != item.bone || rule.target != item.target ||
                    (rule.target != null ? rule.target.sharedMesh : null) != item.mesh ||
                    rule.shapeName != item.shapeName || rule.expression != item.text ||
                    rule.mode != item.source.mode || rule.rbfRadius != item.source.rbfRadius ||
                    (rule.variables?.Count ?? 0) != item.variables.Length) return true;
                if (string.Equals(rule.mode, "RBF", StringComparison.Ordinal))
                {
                    if (rule.rbfIncludeBase != item.source.rbfIncludeBase || rule.rbfBase != item.source.rbfBase)
                        return true;
                    int pointCount = (rule.rbfIncludeBase ? 1 : 0) + (rule.rbfPoints?.Count ?? 0);
                    if (item.rbfPositions == null || item.rbfWeights == null || item.rbfPositions.Length != pointCount)
                        return true;
                    int pointIndex = 0;
                    if (rule.rbfIncludeBase)
                    {
                        if (item.rbfWeights[pointIndex] != 0f) return true;
                        pointIndex++;
                    }
                    foreach (RbfPoint point in rule.rbfPoints ?? new List<RbfPoint>())
                    {
                        if (item.rbfPositions[pointIndex] != point.pose || item.rbfWeights[pointIndex] != point.weight)
                            return true;
                        pointIndex++;
                    }
                }
                for (int v = 0; v < item.variables.Length; v++)
                {
                    Variable variable = rule.variables[v]; VariableState state = item.variables[v];
                    if (!ReferenceEquals(variable, state.source) || variable != null &&
                        (variable.name != state.name || variable.bone != state.bone || variable.channel != state.channel)) return true;
                }
            }
            return false;
        }

        private static bool ValidTarget(CompiledRule item) => item.target != null && item.mesh != null &&
            item.target.sharedMesh == item.mesh && item.index >= 0 && item.index < item.mesh.blendShapeCount;

        private static bool HasMissingVariableBone(CompiledRule item)
        {
            foreach (VariableState variable in item.variables) if (variable.bone == null) return true;
            return false;
        }

        private static void Restore(CompiledRule item)
        {
            if (!item.written) return;
            if (ValidTarget(item) && !HoFaceOutputOwnership.IsReserved(item.target, item.index) &&
                Mathf.Approximately(item.target.GetBlendShapeWeight(item.index), item.lastWritten))
                item.target.SetBlendShapeWeight(item.index, item.restoreWeight);
            item.written = false;
        }

        private void Release()
        {
            foreach (CompiledRule item in compiled)
            {
                Restore(item);
                if (!item.owns) continue;
                var key = (item.target, item.mesh, item.index);
                if (Owners.TryGetValue(key, out HoBsCorrective owner) && owner == this)
                { Owners.Remove(key); ownershipVersion++; }
            }
            compiled.Clear();
        }

        private static float Select(Vector3 value, Channel channel) => channel == Channel.X ? value.x :
            channel == Channel.Y ? value.y : value.z;

        /// <summary>
        /// Decompose the imported local quaternion with the Blender XYZ
        /// convention. Unity's Quaternion.eulerAngles uses a different order.
        /// </summary>
        private static Vector3 ReadLocalRadians(Quaternion rotation)
        {
            float xx = rotation.x * rotation.x;
            float yy = rotation.y * rotation.y;
            float zz = rotation.z * rotation.z;
            float ww = rotation.w * rotation.w;
            float m00 = ww + xx - yy - zz;
            float m10 = 2f * (rotation.x * rotation.y + rotation.w * rotation.z);
            float m20 = 2f * (rotation.x * rotation.z - rotation.w * rotation.y);
            float m21 = 2f * (rotation.y * rotation.z + rotation.w * rotation.x);
            float m22 = ww - xx - yy + zz;
            float y = Mathf.Asin(Mathf.Clamp(-m20, -1f, 1f));
            float cy = Mathf.Cos(y);
            float x;
            float z;
            if (Mathf.Abs(cy) > 1e-5f)
            {
                x = Mathf.Atan2(m21, m22);
                z = Mathf.Atan2(m10, m00);
            }
            else
            {
                x = Mathf.Atan2(-2f * (rotation.y * rotation.z - rotation.w * rotation.x),
                    1f - 2f * (xx + zz));
                z = 0f;
            }
            return new Vector3(x, y, z);
        }
    }
}
