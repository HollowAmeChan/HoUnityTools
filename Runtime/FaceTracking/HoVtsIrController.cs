using System;
using System.Collections.Generic;
using UnityEngine;
using Hollow.HoUnityTools.FaceTracking.Core;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>
    /// Character-owned static expression graph. It consumes processed value
    /// parameters and writes final shape-key weights directly, without an
    /// Animator, controller asset, proxy hierarchy, or Warudo-specific API.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HoUnityTools/面捕/VTS 表情 IR")]
    [DefaultExecutionOrder(31000)]
    public sealed class HoVtsIrController : MonoBehaviour
    {
        [SerializeField] private HoVtsParameterReceiver parameterReceiver;
        [SerializeField] private List<SkinnedMeshRenderer> targetMeshes = new List<SkinnedMeshRenderer>();
        [SerializeField] private TextAsset irAsset;
        [SerializeField, Min(0)] private float weightScale = 100f;
        [SerializeField] private bool autoRun = true;

        private HoFaceIrDocument document;
        private HoFaceIrEvaluator evaluator;
        private TextAsset loadedAsset;
        private int loadedHash;
        private readonly List<SkinnedMeshRenderer> loadedRenderers = new List<SkinnedMeshRenderer>();
        private readonly List<Mesh> loadedMeshes = new List<Mesh>();
        private readonly Dictionary<string, float> parameters = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ShapeWriter>> writers = new Dictionary<string, List<ShapeWriter>>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> finalWeights = new Dictionary<string, float>(StringComparer.Ordinal);
        private bool hasOutputFrame;
        private double outputReceiverTime = -1;
        private string status = "未初始化";

        public HoVtsParameterReceiver ParameterReceiver { get => parameterReceiver; set { Stop(); parameterReceiver = value; } }
        public List<SkinnedMeshRenderer> TargetMeshes => targetMeshes;
        public TextAsset IrAsset { get => irAsset; set { Stop(); irAsset = value; } }
        public float WeightScale
        {
            get => weightScale;
            set { weightScale = Finite(value) ? Mathf.Max(0, value) : 100f; InvalidateOutput(); }
        }
        public bool AutoRun { get => autoRun; set => autoRun = value; }
        public string Status => status;
        public int TargetCount => document?.targets.Count ?? 0;
        /// <summary>Only the last completed write for the receiver's current evaluation is readable.</summary>
        public bool HasOutputFrame => hasOutputFrame && isActiveAndEnabled
            && parameterReceiver != null && parameterReceiver.isActiveAndEnabled
            && parameterReceiver.HasFrame && parameterReceiver.Tracked
            && outputReceiverTime == parameterReceiver.LastEvaluationTime
            && loadedAsset == irAsset && MeshesUnchanged();
        public ulong OutputRevision { get; private set; }
        /// <summary>Changes when the IR document or explicit mesh bindings are rebuilt.</summary>
        public ulong BindingRevision { get; private set; }

        /// <summary>
        /// Read the cached, range-clamped and WeightScale-multiplied value actually written
        /// to at least one listed mesh. Reading never evaluates the IR or reads a downstream writer.
        /// </summary>
        public bool TryGetFinalWeight(string keyName, out float weight)
        {
            weight = 0;
            return !string.IsNullOrEmpty(keyName) && HasOutputFrame && finalWeights.TryGetValue(keyName, out weight);
        }

        public void CopyFinalWeightsTo(IDictionary<string, float> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            if (HasOutputFrame) foreach (var pair in finalWeights) destination.Add(pair.Key, pair.Value);
        }

        /// <summary>Binding ownership, including a target whose present output happens to be zero.</summary>
        public bool IsTargetBound(SkinnedMeshRenderer renderer, int index)
        {
            if (renderer == null || renderer.sharedMesh == null || index < 0
                || index >= renderer.sharedMesh.blendShapeCount) return false;
            if (!writers.TryGetValue(renderer.sharedMesh.GetBlendShapeName(index), out List<ShapeWriter> bindings)) return false;
            foreach (var binding in bindings) if (binding.Matches(renderer, index)) return true;
            return false;
        }

        private void LateUpdate()
        {
            if (autoRun) EvaluateNow();
        }

        public bool EvaluateNow()
        {
            if (!isActiveAndEnabled) { Release(); status = "组件未启用"; return false; }
            if (!EnsureRuntime()) return false;
            if (!parameterReceiver.HasFrame || !parameterReceiver.Tracked)
            {
                Release();
                status = parameterReceiver.HasFrame ? "等待追踪" : "等待输入帧";
                return false;
            }
            parameterReceiver.CopyValuesTo(parameters);
            evaluator.Evaluate(parameters);
            InvalidateOutput();
            try
            {
                // Reserve the complete set before writing anything, so conflicts cannot leave a partial pose.
                foreach (var targetWriters in writers.Values)
                    foreach (var writer in targetWriters) writer.Reserve(this);
                foreach (var target in document.targets)
                {
                    if (!writers.TryGetValue(target.id, out List<ShapeWriter> targetWriters)) continue;
                    float value = evaluator.Values.TryGetValue(target.id, out float weight) ? weight : 0;
                    float final = value * weightScale;
                    if (!Finite(final)) throw new InvalidOperationException("形态键权重不是有限数：" + target.id);
                    foreach (var writer in targetWriters) writer.Write(final);
                    finalWeights[target.id] = final;
                }
            }
            catch (Exception exception) { Release(); status = "IR 写入失败：" + exception.Message; return false; }
            outputReceiverTime = parameterReceiver.LastEvaluationTime;
            hasOutputFrame = true;
            OutputRevision++;
            status = "运行中";
            return true;
        }

        private bool EnsureRuntime()
        {
            if (parameterReceiver == null || !parameterReceiver.isActiveAndEnabled)
            {
                Stop(); status = "未指定或未启用参数接收器"; return false;
            }
            if (irAsset == null || targetMeshes == null || targetMeshes.Count == 0)
            {
                Stop();
                status = irAsset == null ? "未指定 IR 文件" : "未指定驱动网格列表";
                return false;
            }
            string text = irAsset.text ?? string.Empty;
            int hash = text.GetHashCode();
            if (evaluator != null && loadedAsset == irAsset && loadedHash == hash && MeshesUnchanged()) return true;
            Stop();
            if (!HoFaceIrDocument.TryParse(text, out HoFaceIrDocument parsed, out string error))
            {
                status = "IR 读取失败：" + error;
                return false;
            }
            try
            {
                document = parsed;
                evaluator = new HoFaceIrEvaluator(document);
                BuildWriters();
                BindingRevision++;
                loadedAsset = irAsset;
                loadedHash = hash;
                status = "已加载 " + document.targets.Count + " 个目标";
                return true;
            }
            catch (Exception exception)
            {
                Stop(); status = "IR 绑定失败：" + exception.Message; return false;
            }
        }

        private void BuildWriters()
        {
            var renderers = new HashSet<SkinnedMeshRenderer>();
            foreach (var renderer in targetMeshes)
            {
                if (renderer == null || renderer.sharedMesh == null)
                    throw new InvalidOperationException("驱动网格列表含空对象或未指定 Mesh。");
                if (!renderers.Add(renderer))
                    throw new InvalidOperationException("驱动网格重复：" + renderer.name);
                loadedRenderers.Add(renderer);
                loadedMeshes.Add(renderer.sharedMesh);

                foreach (var target in document.targets)
                {
                    int index = renderer.sharedMesh.GetBlendShapeIndex(target.id);
                    if (index < 0) continue;
                    if (!writers.TryGetValue(target.id, out List<ShapeWriter> targetWriters))
                    {
                        targetWriters = new List<ShapeWriter>();
                        writers.Add(target.id, targetWriters);
                    }
                    targetWriters.Add(new ShapeWriter(renderer, index));
                }
            }
        }

        private bool MeshesUnchanged()
        {
            if (targetMeshes == null || loadedRenderers.Count != targetMeshes.Count) return false;
            for (int i = 0; i < targetMeshes.Count; i++)
                if (targetMeshes[i] == null || loadedRenderers[i] != targetMeshes[i] ||
                    loadedMeshes[i] != targetMeshes[i].sharedMesh) return false;
            return true;
        }

        public void Release()
        {
            InvalidateOutput();
            foreach (var targetWriters in writers.Values)
                foreach (var writer in targetWriters) writer.Restore(this);
            HoFaceOutputOwnership.Release(this);
        }

        private void InvalidateOutput() { hasOutputFrame = false; outputReceiverTime = -1; finalWeights.Clear(); }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public void Stop()
        {
            Release();
            document = null; evaluator = null; loadedAsset = null; loadedHash = 0;
            writers.Clear(); parameters.Clear(); loadedRenderers.Clear(); loadedMeshes.Clear();
        }

        private void OnValidate() => Stop();
        private void OnDisable() => Stop();
        private void OnDestroy() => Stop();

        private sealed class ShapeWriter
        {
            private readonly SkinnedMeshRenderer renderer;
            private readonly Mesh mesh;
            private readonly int index;
            private float initial, lastWritten;
            private bool written;
            public ShapeWriter(SkinnedMeshRenderer renderer, int index)
            {
                this.renderer = renderer; mesh = renderer.sharedMesh;
                this.index = index;
            }
            public bool Matches(SkinnedMeshRenderer target, int targetIndex) =>
                renderer == target && mesh == target.sharedMesh && index == targetIndex;
            public void Reserve(object owner)
            {
                HoFaceOutputOwnership.Reserve(renderer, index, owner);
                if (!written) initial = renderer.GetBlendShapeWeight(index);
            }
            public void Write(float value)
            {
                if (renderer != null && renderer.sharedMesh == mesh)
                {
                    renderer.SetBlendShapeWeight(index, value);
                    lastWritten = value; written = true;
                }
            }
            public void Restore(object owner)
            {
                if (written && renderer != null && renderer.sharedMesh == mesh
                    && HoFaceOutputOwnership.IsReservedBy(renderer, index, owner)
                    && Mathf.Approximately(renderer.GetBlendShapeWeight(index), lastWritten))
                    renderer.SetBlendShapeWeight(index, initial);
                written = false;
            }
        }
    }
}
