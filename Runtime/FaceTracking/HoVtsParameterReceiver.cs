using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>A serializable name/value entry used by the VTS role runtime.</summary>
    [Serializable]
    public struct HoVtsParameterSlot
    {
        public string key;
        public float value;

        public HoVtsParameterSlot(string key, float value)
        {
            this.key = key;
            this.value = value;
        }
    }

    /// <summary>
    /// Per-character VTS parameter hub.
    ///
    /// An input adapter provides complete VTS frames. The role's rules publish processed
    /// values for controller and LookAt consumers; no platform API is referenced here.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class HoVtsParameterReceiver : MonoBehaviour
    {
        private List<HoVtsParameterSlot> rawValues = new List<HoVtsParameterSlot>();
        private List<HoVtsParameterSlot> values = new List<HoVtsParameterSlot>();
        // The rule data belongs to the role. It is hidden from the default inspector until
        // the dedicated rule editor is migrated; it is intentionally not a TextAsset path.
        [SerializeField, HideInInspector] private HoFaceMiddleware middleware = new HoFaceMiddleware();
        private bool tracked;
        private double lastFrameTime = -1.0;
        private string source = string.Empty;

        private readonly Dictionary<string, int> rawIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> valueIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> evaluationRaw = new Dictionary<string, float>(StringComparer.Ordinal);
        private HoVtsRuleEvaluator evaluator;
        private double lastEvaluationTime = -1.0;

        public bool Tracked => tracked;
        public bool HasFrame => lastFrameTime >= 0;
        public double LastFrameTime => lastFrameTime;
        public string Source => source ?? string.Empty;
        public int RawCount => rawValues != null ? rawValues.Count : 0;
        public int ValueCount => values != null ? values.Count : 0;
        public HoFaceMiddleware Middleware => middleware;

        // Unity messages deliberately use only Unity/BCL types. Character and input-adapter
        // mods are separate assemblies and must not cast copies of our component type.
        public void HoVtsFindReceiver(Action<GameObject> found)
        {
            if (isActiveAndEnabled) found?.Invoke(gameObject);
        }

        public void HoVtsReceiveFrame(object payload)
        {
            var packet = payload as object[];
            if (!isActiveAndEnabled || packet == null || packet.Length != 5
                || !(packet[0] is Dictionary<string, float>) || !(packet[1] is bool)
                || !(packet[2] is double) || !(packet[3] is string) || !(packet[4] is Action<GameObject>)) return;
            ApplyRawFrame((Dictionary<string, float>)packet[0], (bool)packet[1], (double)packet[2], (string)packet[3]);
            ((Action<GameObject>)packet[4])(gameObject);
        }

        public void HoVtsReleaseInput(object owner)
        {
            if (owner is string name && name == Source) ClearFrame();
        }

        public void SetMiddleware(HoFaceMiddleware value)
        {
            middleware = value ?? new HoFaceMiddleware();
            evaluator = null;
            lastEvaluationTime = -1.0;
        }

        public void RebuildEvaluator()
        {
            evaluator = null;
            lastEvaluationTime = -1.0;
        }

        private void EnsureEvaluator()
        {
            if (evaluator == null) evaluator = new HoVtsRuleEvaluator(middleware);
        }

        /// <summary>Accept one raw adapter frame. Values are copied; the caller may reuse its dictionary.</summary>
        public void ApplyRawFrame(Dictionary<string, float> frame, bool faceTracked, double timestamp,
            string frameSource = null)
        {
            if (double.IsNaN(timestamp) || double.IsInfinity(timestamp) || timestamp < 0)
                throw new ArgumentOutOfRangeException(nameof(timestamp), "Use a finite nonnegative local clock.");
            EnsureTables();
            Replace(rawValues, rawIndices, frame);
            bool wasTracked = tracked;
            tracked = faceTracked;
            lastFrameTime = timestamp;
            source = frameSource ?? string.Empty;

            if (!faceTracked && wasTracked)
            {
                // Keep the raw packet for diagnosis, but do not hold a stale facial expression.
                evaluator = null;
                lastEvaluationTime = -1.0;
            }
            EnsureEvaluator();
            float deltaTime = lastEvaluationTime >= 0.0 && timestamp >= lastEvaluationTime
                ? Mathf.Min((float)(timestamp - lastEvaluationTime), 0.25f)
                : 0f;
            evaluationRaw.Clear();
            if (faceTracked) CopyTo(rawValues, evaluationRaw);
            evaluator.Evaluate(evaluationRaw, deltaTime, timestamp);
            SetProcessedValues(evaluator.Parameters);
            lastEvaluationTime = timestamp;
        }

        /// <summary>Replace processed values after the rule runner has evaluated one frame.</summary>
        public void SetProcessedValues(Dictionary<string, float> processed)
        {
            EnsureTables();
            Replace(values, valueIndices, processed);
        }

        /// <summary>Clear values and mark the receiver untracked without destroying the component.</summary>
        public void ClearFrame(bool clearRaw = true)
        {
            EnsureTables();
            if (clearRaw)
            {
                rawValues.Clear();
                rawIndices.Clear();
            }
            values.Clear();
            valueIndices.Clear();
            tracked = false;
            lastFrameTime = -1.0;
            source = string.Empty;
            evaluator = null;
            lastEvaluationTime = -1.0;
        }

        public bool TryGet(string name, out float value)
        {
            EnsureTables();
            return TryGet(values, valueIndices, name, out value);
        }

        public float Get(string name, float fallback = 0f)
        {
            return TryGet(name, out float value) ? value : fallback;
        }

        public bool TryGetRaw(string name, out float value)
        {
            EnsureTables();
            return TryGet(rawValues, rawIndices, name, out value);
        }

        public float GetRaw(string name, float fallback = 0f)
        {
            return TryGetRaw(name, out float value) ? value : fallback;
        }

        /// <summary>Copy the processed table into a caller-owned dictionary.</summary>
        public void CopyValuesTo(Dictionary<string, float> destination)
        {
            EnsureTables();
            CopyTo(values, destination);
        }

        /// <summary>Copy the raw table into a caller-owned dictionary.</summary>
        public void CopyRawTo(Dictionary<string, float> destination)
        {
            EnsureTables();
            CopyTo(rawValues, destination);
        }

        private void EnsureTables()
        {
            if (rawValues == null) rawValues = new List<HoVtsParameterSlot>();
            if (values == null) values = new List<HoVtsParameterSlot>();
        }

        private static void Replace(List<HoVtsParameterSlot> destination,
            Dictionary<string, int> indices, Dictionary<string, float> sourceValues)
        {
            destination.Clear();
            indices.Clear();
            if (sourceValues == null) return;

            foreach (var pair in sourceValues)
            {
                if (string.IsNullOrEmpty(pair.Key)) continue;
                if (indices.ContainsKey(pair.Key)) continue;
                indices.Add(pair.Key, destination.Count);
                destination.Add(new HoVtsParameterSlot(pair.Key, Safe(pair.Value)));
            }
        }

        private static bool TryGet(List<HoVtsParameterSlot> table,
            Dictionary<string, int> indices, string name, out float value)
        {
            if (table != null && name != null && indices.TryGetValue(name, out int index)
                && index >= 0 && index < table.Count)
            {
                value = table[index].value;
                return true;
            }

            value = 0f;
            return false;
        }

        private static void CopyTo(List<HoVtsParameterSlot> source, Dictionary<string, float> destination)
        {
            if (destination == null) return;
            destination.Clear();
            if (source == null) return;
            foreach (var slot in source)
                if (!string.IsNullOrEmpty(slot.key)) destination[slot.key] = slot.value;
        }

        private static float Safe(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }

        private void OnDisable() { ClearFrame(); }
    }
}
