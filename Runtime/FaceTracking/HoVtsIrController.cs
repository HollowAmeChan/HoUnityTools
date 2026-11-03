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
        [SerializeField] private Transform outputRoot;
        [SerializeField] private TextAsset irAsset;
        [SerializeField, Min(0)] private float weightScale = 100f;
        [SerializeField] private bool autoRun = true;

        private HoFaceIrDocument document;
        private HoFaceIrEvaluator evaluator;
        private TextAsset loadedAsset;
        private int loadedHash;
        private readonly Dictionary<string, float> parameters = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<string, ShapeWriter> writers = new Dictionary<string, ShapeWriter>(StringComparer.Ordinal);
        private string status = "未初始化";

        public HoVtsParameterReceiver ParameterReceiver { get => parameterReceiver; set { Stop(); parameterReceiver = value; } }
        public Transform OutputRoot { get => outputRoot; set { Stop(); outputRoot = value; } }
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
                if (!writers.TryGetValue(target.id, out ShapeWriter writer)) continue;
                float value = evaluator.Values.TryGetValue(target.id, out float weight) ? weight : 0;
                writer.Write(value * weightScale);
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
            if (outputRoot == null || irAsset == null)
            {
                Stop(); status = outputRoot == null ? "未指定输出根物体" : "未指定 IR 文件"; return false;
            }
            string text = irAsset.text ?? string.Empty;
            int hash = text.GetHashCode();
            if (evaluator != null && loadedAsset == irAsset && loadedHash == hash) return true;
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
                foreach (var target in document.targets) writers.Add(target.id, BuildWriter(target));
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

        private ShapeWriter BuildWriter(HoFaceIrTarget target)
        {
            Transform node = string.IsNullOrEmpty(target.slot) ? outputRoot : outputRoot.Find(target.slot);
            if (node == null) throw new InvalidOperationException("找不到 IR 目标物体：" + target.slot);
            var renderer = node.GetComponent<SkinnedMeshRenderer>();
            if (renderer == null || renderer.sharedMesh == null)
                throw new InvalidOperationException("IR 目标没有 SkinnedMeshRenderer：" + target.slot);
            int index = renderer.sharedMesh.GetBlendShapeIndex(target.shape);
            if (index < 0) throw new InvalidOperationException("找不到形态键：" + target.slot + "/" + target.shape);
            return new ShapeWriter(renderer, index);
        }

        public void Release()
        {
            foreach (var writer in writers.Values) writer.Restore();
        }

        public void Stop()
        {
            Release();
            document = null; evaluator = null; loadedAsset = null; loadedHash = 0;
            writers.Clear(); parameters.Clear();
        }

        private void OnDisable() => Stop();
        private void OnDestroy() => Stop();

        private sealed class ShapeWriter
        {
            private readonly SkinnedMeshRenderer renderer;
            private readonly int index;
            private readonly float initial;
            public ShapeWriter(SkinnedMeshRenderer renderer, int index)
            {
                this.renderer = renderer; this.index = index; initial = renderer.GetBlendShapeWeight(index);
            }
            public void Write(float value)
            {
                if (renderer != null) renderer.SetBlendShapeWeight(index, value);
            }
            public void Restore()
            {
                if (renderer != null) renderer.SetBlendShapeWeight(index, initial);
            }
        }
    }
}
