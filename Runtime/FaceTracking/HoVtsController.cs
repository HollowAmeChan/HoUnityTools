using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>Animation ownership compiled in the editor and carried inside the character prefab.</summary>
    [Serializable]
    public struct HoVtsAnimationBinding
    {
        public string path;
        public string property;
        public bool blendShape;

        public HoVtsAnimationBinding(string path, string property, bool blendShape)
        {
            this.path = path;
            this.property = property;
            this.blendShape = blendShape;
        }
    }

    /// <summary>
    /// Character-owned controller slot. A manually evaluated private hierarchy avoids depending
    /// on the character Animator being enabled. Only authored animation properties are copied back.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HoUnityTools/面捕/VTS 表情控制器")]
    [DefaultExecutionOrder(31000)]
    public sealed class HoVtsController : MonoBehaviour
    {
        [SerializeField] private HoVtsParameterReceiver parameterReceiver;
        [SerializeField] private Transform outputRoot;
        [SerializeField] private RuntimeAnimatorController controller;
        [SerializeField] private bool autoRun = true;
        [SerializeField, HideInInspector] private RuntimeAnimatorController bindingController;
        [SerializeField, HideInInspector] private HoVtsAnimationBinding[] bindings = new HoVtsAnimationBinding[0];

        private readonly Dictionary<string, float> parameters = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly List<AnimatorControllerParameter> controllerParameters = new List<AnimatorControllerParameter>();
        private readonly List<PropertyWriter> writers = new List<PropertyWriter>();
        private readonly Dictionary<string, Transform> proxies = new Dictionary<string, Transform>(StringComparer.Ordinal);
        private PlayableGraph graph;
        private AnimatorControllerPlayable playable;
        private GameObject evaluationRoot;
        private HoVtsParameterReceiver resolvedReceiver;
        private Transform resolvedOutputRoot;
        private RuntimeAnimatorController resolvedController;
        private bool written;
        private string status = "未初始化";
        private int lastPreCullFrame = -1;

        public HoVtsParameterReceiver ParameterReceiver
        {
            get => parameterReceiver;
            set { if (parameterReceiver != value) { Stop(); parameterReceiver = value; } }
        }

        public Transform OutputRoot
        {
            get => outputRoot;
            set { if (outputRoot != value) { Stop(); outputRoot = value; } }
        }

        public RuntimeAnimatorController Controller
        {
            get => controller;
            set { if (controller != value) { Stop(); controller = value; } }
        }

        public bool AutoRun { get => autoRun; set => autoRun = value; }
        public bool IsReady => graph.IsValid();
        public string Status => status;
        public int BindingCount => bindings != null ? bindings.Length : 0;

        /// <summary>Called by authoring/build tools, never inferred from observed pose changes.</summary>
        public void SetAnimationBindings(RuntimeAnimatorController owner, HoVtsAnimationBinding[] compiled)
        {
            Stop();
            bindingController = owner;
            bindings = compiled != null ? (HoVtsAnimationBinding[])compiled.Clone() : new HoVtsAnimationBinding[0];
        }

        private void OnEnable() { Camera.onPreCull += OnCameraPreCull; }
        private void LateUpdate() { if (autoRun) EvaluateNow(Time.deltaTime); }

        private void OnCameraPreCull(Camera camera)
        {
            if (!autoRun || lastPreCullFrame == Time.frameCount) return;
            lastPreCullFrame = Time.frameCount;
            if (!EnsureRuntime()) return;

            // Warudo's CharacterAsset writes humanoid bones in its own LateUpdate,
            // after ordinary component LateUpdate. Re-apply the already evaluated
            // controller pose immediately before rendering so authored transform
            // channels are not erased by that pipeline.
            if (!written)
            {
                EvaluateNow(0f);
                return;
            }
            foreach (var writer in writers) writer.Write();
        }

        public bool EvaluateNow() => EvaluateNow(Time.deltaTime);

        public bool EvaluateNow(float deltaTime)
        {
            if (!EnsureRuntime()) return false;
            resolvedReceiver.CopyValuesTo(parameters);
            foreach (var parameter in controllerParameters)
            {
                // Missing outputs use the asset's defaults, including constant-one and gate parameters.
                bool present = parameters.TryGetValue(parameter.name, out float value);
                switch (parameter.type)
                {
                    case AnimatorControllerParameterType.Float:
                        playable.SetFloat(parameter.nameHash, present ? value : parameter.defaultFloat);
                        break;
                    case AnimatorControllerParameterType.Int:
                        playable.SetInteger(parameter.nameHash, present ? Mathf.RoundToInt(value) : parameter.defaultInt);
                        break;
                    case AnimatorControllerParameterType.Bool:
                        playable.SetBool(parameter.nameHash, present ? value > 0.5f : parameter.defaultBool);
                        break;
                    // Triggers are events; a continuously sampled float dictionary does not fire them.
                }
            }

            graph.Evaluate(float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) ? 0f : Mathf.Max(0f, deltaTime));
            foreach (var writer in writers) writer.Write();
            written = true;
            status = resolvedReceiver.Tracked ? "运行中" : "运行中（无追踪）";
            return true;
        }

        public void Rebuild() { Stop(); }

        private bool EnsureRuntime()
        {
            if (controller == null || outputRoot == null || parameterReceiver == null || !parameterReceiver.isActiveAndEnabled)
            {
                Stop();
                status = controller == null ? "未指定动画控制器" : outputRoot == null ? "未指定输出根物体"
                    : parameterReceiver == null ? "未指定参数接收器" : "参数接收器已停用";
                return false;
            }
            if (!parameterReceiver.HasFrame && parameterReceiver.ValueCount == 0)
            {
                Stop();
                status = "等待输入帧";
                return false;
            }
            if (graph.IsValid() && resolvedController == controller && resolvedOutputRoot == outputRoot
                && resolvedReceiver == parameterReceiver) return true;
            Stop();
            if (bindingController != controller || bindings == null || bindings.Length == 0)
            {
                status = "请在编辑器刷新控制器动画绑定后保存角色";
                return false;
            }

            try
            {
                resolvedController = controller;
                resolvedOutputRoot = outputRoot;
                resolvedReceiver = parameterReceiver;
                evaluationRoot = new GameObject("HoVtsController Evaluation") { hideFlags = HideFlags.HideAndDontSave };
                evaluationRoot.SetActive(false);
                evaluationRoot.transform.localPosition = outputRoot.localPosition;
                evaluationRoot.transform.localRotation = outputRoot.localRotation;
                evaluationRoot.transform.localScale = outputRoot.localScale;
                proxies.Add(string.Empty, evaluationRoot.transform);
                foreach (var binding in bindings) BuildWriter(binding);
                var animator = evaluationRoot.AddComponent<Animator>();
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.applyRootMotion = false;
                evaluationRoot.SetActive(true);
                graph = PlayableGraph.Create("HoVtsController");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                playable = AnimatorControllerPlayable.Create(graph, controller);
                AnimationPlayableOutput.Create(graph, "Face", animator).SetSourcePlayable(playable);
                for (int i = 0; i < playable.GetParameterCount(); i++) controllerParameters.Add(playable.GetParameter(i));
                graph.Play();
                return true;
            }
            catch (Exception error)
            {
                Stop();
                status = "控制器初始化失败：" + error.Message;
                return false;
            }
        }

        private Transform Proxy(string path)
        {
            path = path ?? string.Empty;
            if (proxies.TryGetValue(path, out Transform existing)) return existing;
            int slash = path.LastIndexOf('/');
            string parentPath = slash < 0 ? string.Empty : path.Substring(0, slash);
            Transform parent = Proxy(parentPath);
            Transform source = outputRoot.Find(path);
            if (source == null) throw new InvalidOperationException("输出根下找不到动画路径：" + path);
            var node = new GameObject(source.name) { hideFlags = HideFlags.HideAndDontSave }.transform;
            node.SetParent(parent, false);
            node.localPosition = source.localPosition;
            node.localRotation = source.localRotation;
            node.localScale = source.localScale;
            proxies.Add(path, node);
            return node;
        }

        private void BuildWriter(HoVtsAnimationBinding binding)
        {
            Transform source = string.IsNullOrEmpty(binding.path) ? outputRoot : outputRoot.Find(binding.path);
            if (source == null) throw new InvalidOperationException("输出根下找不到动画路径：" + binding.path);
            Transform proxy = Proxy(binding.path);
            if (binding.blendShape)
            {
                var target = source.GetComponent<SkinnedMeshRenderer>();
                string shape = binding.property.Substring("blendShape.".Length);
                int index = target != null && target.sharedMesh != null ? target.sharedMesh.GetBlendShapeIndex(shape) : -1;
                if (index < 0) throw new InvalidOperationException("找不到形态键：" + binding.path + "/" + shape);
                var mirror = proxy.GetComponent<SkinnedMeshRenderer>();
                if (mirror == null)
                {
                    mirror = proxy.gameObject.AddComponent<SkinnedMeshRenderer>();
                    mirror.sharedMesh = target.sharedMesh;
                    mirror.forceRenderingOff = true;
                    for (int i = 0; i < target.sharedMesh.blendShapeCount; i++)
                        mirror.SetBlendShapeWeight(i, target.GetBlendShapeWeight(i));
                }
                writers.Add(new ShapeWriter(target, mirror, index));
            }
            else
            {
                var writer = new TransformWriter(source, proxy, binding.property);
                // Quaternion channels belong to one rotation, so restore/write it only once per object.
                if (!writers.Exists(item => item is TransformWriter other && other.SameProperty(writer))) writers.Add(writer);
            }
        }

        /// <summary>Release owned properties and destroy only the private evaluation objects.</summary>
        public void Stop()
        {
            if (written) foreach (var writer in writers) writer.Restore();
            written = false;
            if (graph.IsValid()) graph.Destroy();
            if (evaluationRoot != null)
            {
                evaluationRoot.SetActive(false);
                if (Application.isPlaying) Destroy(evaluationRoot);
                else DestroyImmediate(evaluationRoot);
            }
            evaluationRoot = null;
            resolvedController = null;
            resolvedOutputRoot = null;
            resolvedReceiver = null;
            writers.Clear();
            proxies.Clear();
            controllerParameters.Clear();
            lastPreCullFrame = -1;
            status = "已停止";
        }

        private void OnDisable()
        {
            Camera.onPreCull -= OnCameraPreCull;
            Stop();
        }

        private void OnDestroy()
        {
            Camera.onPreCull -= OnCameraPreCull;
            Stop();
        }

        private abstract class PropertyWriter
        {
            public abstract void Write();
            public abstract void Restore();
        }

        private sealed class ShapeWriter : PropertyWriter
        {
            private readonly SkinnedMeshRenderer target, proxy;
            private readonly int index;
            private readonly float initial;
            public ShapeWriter(SkinnedMeshRenderer target, SkinnedMeshRenderer proxy, int index)
            {
                this.target = target; this.proxy = proxy; this.index = index;
                initial = target.GetBlendShapeWeight(index);
            }
            public override void Write() { if (target != null && proxy != null) target.SetBlendShapeWeight(index, proxy.GetBlendShapeWeight(index)); }
            public override void Restore() { if (target != null) target.SetBlendShapeWeight(index, initial); }
        }

        private sealed class TransformWriter : PropertyWriter
        {
            private readonly Transform target, proxy;
            private readonly int kind, axis;
            private readonly float initial;
            private readonly Quaternion initialRotation;

            public TransformWriter(Transform target, Transform proxy, string property)
            {
                this.target = target; this.proxy = proxy;
                kind = property.StartsWith("m_LocalPosition.", StringComparison.Ordinal) ? 0
                    : property.StartsWith("m_LocalScale.", StringComparison.Ordinal) ? 1
                    : property.StartsWith("m_LocalRotation.", StringComparison.Ordinal) ? 2
                    : property.StartsWith("localEulerAngles", StringComparison.Ordinal) ? 3 : -1;
                char channel = property[property.Length - 1];
                axis = channel == 'x' ? 0 : channel == 'y' ? 1 : channel == 'z' ? 2 : channel == 'w' ? 3 : -1;
                if (kind < 0 || axis < 0 || (axis == 3 && kind != 2))
                    throw new InvalidOperationException("不支持的骨骼动画属性：" + property);
                initialRotation = target.localRotation;
                if (kind != 2) initial = Read(target)[axis];
            }

            public bool SameProperty(TransformWriter other) => target == other.target && kind == other.kind && (kind == 2 || axis == other.axis);
            private Vector3 Read(Transform node) => kind == 0 ? node.localPosition : kind == 1 ? node.localScale : node.localEulerAngles;
            private void Assign(float value)
            {
                Vector3 current = Read(target);
                current[axis] = value;
                if (kind == 0) target.localPosition = current;
                else if (kind == 1) target.localScale = current;
                else target.localEulerAngles = current;
            }
            public override void Write()
            {
                if (target == null || proxy == null) return;
                if (kind == 2) target.localRotation = proxy.localRotation;
                else Assign(Read(proxy)[axis]);
            }
            public override void Restore()
            {
                if (target == null) return;
                if (kind == 2) target.localRotation = initialRotation;
                else Assign(initial);
            }
        }
    }
}
