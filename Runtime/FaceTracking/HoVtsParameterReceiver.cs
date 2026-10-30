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
    [AddComponentMenu("HoUnityTools/面捕/VTS 参数接收器")]
    [DefaultExecutionOrder(30000)]
    public sealed class HoVtsParameterReceiver : MonoBehaviour
    {
        private List<HoVtsParameterSlot> rawValues = new List<HoVtsParameterSlot>();
        private List<HoVtsParameterSlot> values = new List<HoVtsParameterSlot>();
        // Role-owned data. The dedicated editor edits detached drafts before explicit save.
        [SerializeField, HideInInspector] private HoFaceMiddleware middleware = new HoFaceMiddleware();
        [SerializeField, Min(0.01f)] private float inputTimeoutSeconds = 1f;
        private bool tracked;
        private bool inputExpired;
        private double lastFrameTime = -1.0;
        private string source = string.Empty;

        private readonly Dictionary<string, int> rawIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> valueIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> evaluationRaw = new Dictionary<string, float>(StringComparer.Ordinal);
        private HoVtsRuleEvaluator evaluator;
        private double lastEvaluationTime = -1.0;
        private int evaluatedUnityFrame = -1;
        private readonly Dictionary<string, float> inputOverrides = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> outputOverrides = new Dictionary<string, float>(StringComparer.Ordinal);
        private string overrideSource;
        public event Action<HoVtsParameterReceiver, double, float> Evaluated;
        public int RuleRevision { get; private set; }
        public bool TryReadRuleStages(bool input, int row, out float expression, out float curve, out bool active)
        {
            expression = curve = 0; active = false;
            return evaluator != null && evaluator.TryReadStages(input, row, out expression, out curve, out active);
        }

        /// <summary>Transient diagnostic overrides. They never modify serialized role rules.</summary>
        public void SetDebugOverrides(IReadOnlyDictionary<string, float> inputs, IReadOnlyDictionary<string, float> outputs, string ownerSource = null)
        {
            CopyOverrides(inputs, inputOverrides); CopyOverrides(outputs, outputOverrides);
            overrideSource = ownerSource;
        }
        public void ClearDebugOverrides() { inputOverrides.Clear(); outputOverrides.Clear(); overrideSource = null; }
        private static void CopyOverrides(IReadOnlyDictionary<string, float> sourceValues, Dictionary<string, float> destination)
        {
            destination.Clear();
            if (sourceValues != null) foreach (var pair in sourceValues)
                if (!string.IsNullOrEmpty(pair.Key)) destination[pair.Key] = Safe(pair.Value);
        }
        public bool TryReadInputRow(int row, out float configured, out float effective, out bool fresh)
        {
            configured = effective = 0; fresh = false;
            return evaluator != null && evaluator.TryReadInput(row, out configured, out effective, out fresh);
        }
        public bool TryReadOutputRow(int row, out float modified, out float published)
        {
            modified = published = 0;
            return evaluator != null && evaluator.TryReadOutput(row, out modified, out published);
        }

        public bool Tracked => tracked;
        public bool HasFrame => lastFrameTime >= 0;
        public bool InputExpired => inputExpired;
        public double LastFrameTime => lastFrameTime;
        public string Source => source ?? string.Empty;
        public int RawCount => rawValues != null ? rawValues.Count : 0;
        public int ValueCount => values != null ? values.Count : 0;
        public HoFaceMiddleware Middleware => middleware;
        public string RuleError => evaluator != null ? evaluator.Error : null;
        public double LastEvaluationTime => lastEvaluationTime;
        public float InputTimeoutSeconds
        {
            get => inputTimeoutSeconds;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0)
                    throw new ArgumentOutOfRangeException(nameof(value));
                inputTimeoutSeconds = value;
            }
        }

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
            RuleRevision++;
            evaluator = null;
            lastEvaluationTime = -1.0;
        }

        public void RebuildEvaluator()
        {
            RuleRevision++;
            evaluator = null;
            lastEvaluationTime = -1.0;
            evaluatedUnityFrame = -1;
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
            if (timestamp < lastFrameTime || timestamp < lastEvaluationTime)
                throw new ArgumentOutOfRangeException(nameof(timestamp), "Input and evaluation must use the same monotonic local clock.");
            EnsureTables();
            Replace(rawValues, rawIndices, frame);
            bool wasTracked = tracked;
            tracked = faceTracked;
            lastFrameTime = timestamp;
            inputExpired = false;
            source = frameSource ?? string.Empty;

            if (!faceTracked && wasTracked)
            {
                // Keep the raw packet for diagnosis, but do not hold a stale facial expression.
                evaluator = null;
                lastEvaluationTime = -1.0;
            }
            EvaluateAt(timestamp);
        }

        /// <summary>
        /// Advance rules and loss detection without inventing a new input packet. The owning
        /// host may call this for offline stepping; readers never advance time. Unity runs it
        /// once in LateUpdate when the adapter has not already evaluated this render frame.
        /// </summary>
        public bool Tick(double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0)
                throw new ArgumentOutOfRangeException(nameof(now));
            if (!HasFrame || now <= lastEvaluationTime || now < lastFrameTime) return false;
            float timeout = inputTimeoutSeconds > 0 && !float.IsNaN(inputTimeoutSeconds) && !float.IsInfinity(inputTimeoutSeconds)
                ? inputTimeoutSeconds : 1f;
            if (now - lastFrameTime > timeout)
            {
                inputExpired = true;
                if (tracked)
                {
                    tracked = false;
                    evaluator = null;
                    lastEvaluationTime = -1;
                }
            }
            EvaluateAt(now);
            return true;
        }

        private void EvaluateAt(double now)
        {
            EnsureEvaluator();
            float deltaTime = lastEvaluationTime >= 0.0 && now >= lastEvaluationTime
                ? (float)(now - lastEvaluationTime)
                : 0f;
            evaluationRaw.Clear();
            if (tracked) CopyTo(rawValues, evaluationRaw);
            bool useOverrides = overrideSource == null || overrideSource == Source;
            evaluator.SetOverrides(useOverrides ? inputOverrides : null, useOverrides ? outputOverrides : null);
            evaluator.Evaluate(evaluationRaw, deltaTime, now);
            SetProcessedValues(evaluator.Parameters);
            lastEvaluationTime = now;
            evaluatedUnityFrame = Time.frameCount;
            Evaluated?.Invoke(this, now, deltaTime);
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
            inputExpired = false;
            lastFrameTime = -1.0;
            source = string.Empty;
            evaluator = null;
            lastEvaluationTime = -1.0;
            evaluatedUnityFrame = -1;
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
        private void OnValidate() { RebuildEvaluator(); }
        private void LateUpdate()
        {
            if (evaluatedUnityFrame != Time.frameCount) Tick(Time.realtimeSinceStartupAsDouble);
        }
    }
}
