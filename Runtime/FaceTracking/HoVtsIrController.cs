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
        private string status = "未初始化";

        public HoVtsParameterReceiver ParameterReceiver { get => parameterReceiver; set { Stop(); parameterReceiver = value; } }
        public List<SkinnedMeshRenderer> TargetMeshes => targetMeshes;
        public TextAsset IrAsset { get => irAsset; set { Stop(); irAsset = value; } }
        public float WeightScale { get => weightScale; set => weightScale = Mathf.Max(0, value); }
        public bool AutoRun { get => autoRun; set => autoRun = value; }
        public string Status => status;
        public int TargetCount => document?.targets.Count ?? 0;

        private void LateUpdate()
        {
            if (autoRun) EvaluateNow();
        }

        public bool EvaluateNow()
        {
            if (!EnsureRuntime()) return false;
            if (!parameterReceiver.HasFrame || !parameterReceiver.Tracked)
            {
                Release();
                status = parameterReceiver.HasFrame ? "等待追踪" : "等待输入帧";
                return false;
            }
            parameterReceiver.CopyValuesTo(parameters);
            evaluator.Evaluate(parameters);
            foreach (var target in document.targets)
            {
                if (!writers.TryGetValue(target.id, out List<ShapeWriter> targetWriters)) continue;
                float value = evaluator.Values.TryGetValue(target.id, out float weight) ? weight : 0;
                foreach (var writer in targetWriters) writer.Write(value * weightScale);
            }
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
            if (loadedRenderers.Count != targetMeshes.Count) return false;
            for (int i = 0; i < targetMeshes.Count; i++)
                if (targetMeshes[i] == null || loadedRenderers[i] != targetMeshes[i] ||
                    loadedMeshes[i] != targetMeshes[i].sharedMesh) return false;
            return true;
        }

        public void Release()
        {
            foreach (var targetWriters in writers.Values)
                foreach (var writer in targetWriters) writer.Restore();
        }

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
            private readonly float initial;
            public ShapeWriter(SkinnedMeshRenderer renderer, int index)
            {
                this.renderer = renderer; mesh = renderer.sharedMesh;
                this.index = index; initial = renderer.GetBlendShapeWeight(index);
            }
            public void Write(float value)
            {
                if (renderer != null && renderer.sharedMesh == mesh) renderer.SetBlendShapeWeight(index, value);
            }
            public void Restore()
            {
                if (renderer != null && renderer.sharedMesh == mesh) renderer.SetBlendShapeWeight(index, initial);
            }
        }
    }
}
