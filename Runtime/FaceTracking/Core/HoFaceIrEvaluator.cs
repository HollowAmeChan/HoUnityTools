using System;
using System.Collections.Generic;

namespace Hollow.HoUnityTools.FaceTracking.Core
{
    /// <summary>Static pose graph. All targets are accumulated before one final declared-range clamp.</summary>
    public sealed class HoFaceIrEvaluator
    {
        private readonly HoFaceIrDocument document;
        private readonly Dictionary<string, int> nodeIds = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> defaults = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly float[][] values, weights;
        private readonly bool[] evaluated;
        private IReadOnlyDictionary<string, float> parameters;
        public readonly Dictionary<string, float> Values = new Dictionary<string, float>(StringComparer.Ordinal);
        public readonly Dictionary<string, float> UnclampedValues = new Dictionary<string, float>(StringComparer.Ordinal);
        public HoFaceIrDocument Document => document;
        public HoFaceIrEvaluator(HoFaceIrDocument document)
        {
            this.document = document ?? throw new ArgumentNullException(nameof(document));
            var errors = document.Validate();
            if (errors.Count != 0) throw new ArgumentException(string.Join("\n", errors.ToArray()), nameof(document));
            foreach (var p in document.parameters) defaults[p.name] = p.defaultValue;
            values = new float[document.nodes.Count][];
            weights = new float[document.nodes.Count][];
            evaluated = new bool[document.nodes.Count];
            for (int i = 0; i < document.nodes.Count; i++)
            {
                nodeIds[document.nodes[i].id] = i;
                values[i] = new float[document.targets.Count];
                weights[i] = new float[document.nodes[i].children.Count];
            }
        }
        public void Evaluate(IReadOnlyDictionary<string, float> parameters)
        {
            this.parameters = parameters;
            Array.Clear(evaluated, 0, evaluated.Length);
            var root = EvaluateNode(nodeIds[document.root]);
            Values.Clear(); UnclampedValues.Clear();
            for (int i = 0; i < root.Length; i++)
            {
                var t = document.targets[i];
                UnclampedValues[t.id] = root[i];
                Values[t.id] = HoFacePortableMath.Clamp(root[i], t.min, t.max);
            }
        }
        public float[] CopyChildWeights(string id)
        {
            if (id == null || !nodeIds.TryGetValue(id, out int index)) return new float[0];
            return (float[])weights[index].Clone();
        }
        private float Read(string name)
        {
            if (name == null) return 0;
            float result;
            if (parameters == null || !parameters.TryGetValue(name, out result))
                defaults.TryGetValue(name, out result);
            return float.IsNaN(result) || float.IsInfinity(result) ? 0 : result;
        }
        private float[] EvaluateNode(int index)
        {
            if (evaluated[index]) return values[index];
            evaluated[index] = true;
            var node = document.nodes[index];
            var result = values[index];
            Array.Clear(result, 0, result.Length);
            if (node.type == "pose")
            {
                foreach (var value in node.weights)
                    for (int i = 0; i < document.targets.Count; i++)
                        if (document.targets[i].id == value.target) { result[i] = value.value; break; }
                return result;
            }
            var blend = weights[index];
            Array.Clear(blend, 0, blend.Length);
            if (node.type == "blend1d") Blend1D(node, Read(node.parameterX), blend);
            else if (node.type == "blend2d") Blend2D(node, Read(node.parameterX), Read(node.parameterY), blend);
            else
            {
                double total = 0;
                for (int i = 0; i < blend.Length; i++)
                {
                    var c = node.children[i];
                    double influence = string.IsNullOrEmpty(c.weightParameter) ? c.weight : (double)Read(c.weightParameter) * c.weight;
                    blend[i] = (float)Math.Max(0, Math.Min(float.MaxValue, influence));
                    total += blend[i];
                }
                if (node.normalize && total > 0)
                    for (int i = 0; i < blend.Length; i++) blend[i] = (float)(blend[i] / total);
            }
            for (int i = 0; i < blend.Length; i++)
            {
                if (blend[i] == 0) continue;
                float[] child = EvaluateNode(nodeIds[node.children[i].node]);
                for (int j = 0; j < result.Length; j++)
                    result[j] = (float)Math.Max(-float.MaxValue,
                        Math.Min(float.MaxValue, (double)result[j] + (double)child[j] * blend[i]));
            }
            return result;
        }
        private static void Blend1D(HoFaceIrNode node, float value, float[] blend)
        {
            int left = -1, right = -1, low = 0, high = 0;
            for (int i = 0; i < blend.Length; i++)
            {
                float t = node.children[i].threshold;
                if (t < node.children[low].threshold) low = i;
                if (t > node.children[high].threshold) high = i;
                if (t <= value && (left < 0 || t > node.children[left].threshold)) left = i;
                if (t >= value && (right < 0 || t < node.children[right].threshold)) right = i;
            }
            if (left < 0) { blend[low] = 1; return; }
            if (right < 0) { blend[high] = 1; return; }
            if (left == right) { blend[left] = 1; return; }
            float f = (value - node.children[left].threshold) / (node.children[right].threshold - node.children[left].threshold);
            blend[left] = 1 - f; blend[right] = f;
        }
        private static void Blend2D(HoFaceIrNode node, float x, float y, float[] blend)
        {
            // Cartesian gradient-band influences, not triangulated barycentric interpolation.
            double total = 0;
            for (int i = 0; i < blend.Length; i++)
            {
                var a = node.children[i];
                double influence = 1;
                for (int j = 0; j < blend.Length; j++)
                {
                    if (i == j) continue;
                    var b = node.children[j];
                    double dx = b.x - a.x, dy = b.y - a.y, length = dx * dx + dy * dy;
                    double projection = 1 - ((x - a.x) * dx + (y - a.y) * dy) / length;
                    influence = Math.Min(influence, Math.Max(0, Math.Min(1, projection)));
                }
                blend[i] = (float)influence;
                total += influence;
            }
            if (total > 0)
                for (int i = 0; i < blend.Length; i++) blend[i] = (float)(blend[i] / total);
        }
    }
}
