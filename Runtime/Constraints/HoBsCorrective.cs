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
        [Serializable]
        public sealed class Rule
        {
            public string id = string.Empty;
            public bool enabled = true;
            public Transform bone;
            public string bsName = "_BSC_pose1";
            public Quaternion pose = Quaternion.identity;
            public float radius = Mathf.PI / 6f;
            [Range(0f, 1f)] public float weight = 1f;
            public SkinnedMeshRenderer target;
            public string shapeName = string.Empty;
            [HideInInspector] public string sourceId = string.Empty;
            [HideInInspector] public string bonePath = string.Empty;
            [HideInInspector] public string targetPath = string.Empty;
            [HideInInspector] public string sourceBone = string.Empty;
            [HideInInspector] public Quaternion referenceRotation = Quaternion.identity;
            [HideInInspector] public bool measurementIsMch;
            [NonSerialized] public string lastError;
            [NonSerialized] public float lastDistance;
            [NonSerialized] public float lastOutput;
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
        private readonly List<BoneInput> inputs = new List<BoneInput>();

        private sealed class CompiledRule
        {
            public Rule source;
            public bool enabled;
            public Transform bone;
            public SkinnedMeshRenderer target;
            public Mesh mesh;
            public string shapeName;
            public int index = -1;
            public Quaternion pose;
            public Quaternion referenceRotation;
            public bool measurementIsMch;
            public float radius, weight;
            public PoseKernel kernel;
            public bool owns, written;
            public float restoreWeight, lastWritten;
        }

        private sealed class BoneInput
        {
            public Transform bone;
            public Quaternion rotation;
            public Quaternion referenceRotation;
            public bool measurementIsMch;
            public readonly double[] normalized = new double[4];
            public double neutral;
            public bool valid;
            public void Update()
            {
                if (bone == null) { valid = false; return; }
                Quaternion next = bone.localRotation;
                if (valid && rotation.Equals(next)) return;
                rotation = next;
                Quaternion relative = ReadLocalRotation(next, referenceRotation);
                valid = Normalize(relative, normalized);
                neutral = valid ? NeutralAngle(normalized) : 0;
            }
        }

        // One pose across several meshes is evaluated once per distinct local
        // input, including edits made several times within the same frame.
        private sealed class PoseKernel
        {
            public BoneInput input;
            public readonly double[] pose = new double[4];
            public float radius, weight, output, distance;
            public double neutral;
            private Quaternion lastInput;
            private bool hasInput;
            public void Update()
            {
                if (hasInput && lastInput.Equals(input.rotation)) return;
                double d = RotationAngle(input.normalized, pose);
                distance = (float)d;
                output = (float)(weight * Smoothstep(1.0 - d / radius) *
                    Smoothstep((input.neutral + neutral - d) / (2.0 * neutral)));
                lastInput = input.rotation;
                hasInput = true;
            }
        }

        public IList<Rule> Rules => rules;
        public bool EditorPreviewActive => editorPreview;

        /// <summary>Remove the bone's rest orientation in its own local axes, including MCH followers.</summary>
        public static Quaternion ReadLocalRotation(Quaternion localRotation, Quaternion referenceRotation)
        {
            // A rest-oriented MCH follows the zeroed main bone with its bind
            // offset. local * inverse(bind) expresses the motion in the parent's
            // axes; Blender TRANSFORMS / LOCAL_SPACE uses the bone's own axes.
            return Quaternion.Inverse(referenceRotation) * localRotation;
        }

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
            foreach (BoneInput input in inputs) input.Update();
            foreach (CompiledRule item in compiled)
            {
                if (!item.enabled || !item.owns || !ValidTarget(item)) continue;
                if (item.bone == null)
                { item.source.lastError = "测量骨骼已丢失"; Restore(item); continue; }
                if (HoFaceOutputOwnership.IsReserved(item.target, item.index))
                { item.source.lastError = "目标形态键已被面捕接管"; Restore(item); continue; }
                if (!item.kernel.input.valid)
                { item.source.lastError = "骨骼局部旋转无效"; Restore(item); continue; }
                item.kernel.Update();
                item.source.lastDistance = item.kernel.distance;
                float output = item.kernel.output;
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
            var pose = new double[4];
            if (!Normalize(rule.pose, pose) || NeutralAngle(pose) < 1e-5)
            { error = "修型姿态四元数无效或与基准姿态相同"; return false; }
            if (!Normalize(rule.referenceRotation, new double[4]))
            { error = "测量骨骼静止旋转无效"; return false; }
            if (!Finite(rule.radius) || rule.radius <= 0 || rule.radius > Math.PI + 1e-6 ||
                !Finite(rule.weight) || rule.weight < 0 || rule.weight > 1)
            { error = "影响半径必须在 0°..180°，权重必须在 0..1"; return false; }
            return true;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool Normalize(Quaternion q, double[] result)
        {
            if (!Finite(q.x) || !Finite(q.y) || !Finite(q.z) || !Finite(q.w)) return false;
            double length = Math.Sqrt((double)q.x*q.x + (double)q.y*q.y + (double)q.z*q.z + (double)q.w*q.w);
            if (length < 1e-12) return false;
            result[0] = q.x/length; result[1] = q.y/length;
            result[2] = q.z/length; result[3] = q.w/length;
            return true;
        }

        private static double AngleFromDot(double dot)
        {
            dot = Math.Min(1.0, Math.Abs(dot));
            return 2.0 * Math.Atan2(Math.Sqrt(Math.Max(0.0, 1.0-dot*dot)), dot);
        }

        private static double RotationAngle(double[] a, double[] b) =>
            AngleFromDot(a[0]*b[0] + a[1]*b[1] + a[2]*b[2] + a[3]*b[3]);
        private static double NeutralAngle(double[] q) => AngleFromDot(q[3]);

        private static double Smoothstep(double value)
        {
            double t = Math.Max(0.0, Math.Min(1.0, value));
            return t*t*(3.0-2.0*t);
        }

        public static float EvaluatePose(Quaternion current, Quaternion pose, float radius, float weight = 1f)
        {
            var a = new double[4]; var b = new double[4];
            if (!Normalize(current, a) || !Normalize(pose, b) ||
                !Finite(radius) || radius <= 0 || radius > Math.PI+1e-6 ||
                !Finite(weight) || weight < 0 || weight > 1) return 0;
            double c = NeutralAngle(b);
            if (c < 1e-5) return 0;
            double d = RotationAngle(a,b);
            return (float)(weight * Smoothstep(1.0-d/radius) *
                Smoothstep((NeutralAngle(a)+c-d)/(2.0*c)));
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
            var kernels = new List<PoseKernel>();
            foreach (Rule rule in rules)
            {
                var item = new CompiledRule
                {
                    source = rule, enabled = rule != null && rule.enabled, bone = rule?.bone,
                    target = rule?.target, mesh = rule?.target != null ? rule.target.sharedMesh : null,
                    shapeName = rule?.shapeName, radius = rule?.radius ?? 0,
                    weight = rule?.weight ?? 0, pose = rule?.pose ?? Quaternion.identity,
                    referenceRotation = rule?.referenceRotation ?? Quaternion.identity,
                    measurementIsMch = rule != null && rule.measurementIsMch
                };
                compiled.Add(item);
                if (rule == null) continue;
                rule.lastError = null;
                rule.lastOutput = 0;
                if (!item.enabled) continue;
                if (!ValidateRule(rule, out string error))
                { rule.lastError = error; continue; }
                BoneInput input = inputs.Find(value => value.bone == rule.bone &&
                    value.referenceRotation.Equals(rule.referenceRotation) && value.measurementIsMch == rule.measurementIsMch);
                if (input == null)
                { input = new BoneInput { bone = rule.bone, referenceRotation = rule.referenceRotation,
                    measurementIsMch = rule.measurementIsMch }; inputs.Add(input); }
                foreach (PoseKernel kernel in kernels)
                {
                    if (kernel.input != input || kernel.radius != item.radius || kernel.weight != item.weight) continue;
                    var normalized = new double[4];
                    Normalize(rule.pose, normalized);
                    if (RotationAngle(kernel.pose, normalized) < 1e-7)
                    { item.kernel = kernel; break; }
                }
                if (item.kernel == null)
                {
                    item.kernel = new PoseKernel { input = input, radius = item.radius, weight = item.weight };
                    Normalize(rule.pose, item.kernel.pose);
                    item.kernel.neutral = NeutralAngle(item.kernel.pose);
                    kernels.Add(item.kernel);
                }
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
                    rule.shapeName != item.shapeName || rule.radius != item.radius ||
                    rule.weight != item.weight || !rule.pose.Equals(item.pose) ||
                    !rule.referenceRotation.Equals(item.referenceRotation) || rule.measurementIsMch != item.measurementIsMch)
                    return true;
            }
            return false;
        }

        private static bool ValidTarget(CompiledRule item) => item.target != null && item.mesh != null &&
            item.target.sharedMesh == item.mesh && item.index >= 0 && item.index < item.mesh.blendShapeCount;

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
            inputs.Clear();
        }

    }
}
