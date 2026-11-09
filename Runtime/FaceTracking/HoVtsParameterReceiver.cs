using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

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
    /// An input adapter provides complete VTS frames. The component references a reusable
    /// .hoface.json TextAsset whose rules publish processed values for controller and LookAt
    /// consumers; no platform API is referenced here.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HoUnityTools/面捕/VTS 参数接收器")]
    [DefaultExecutionOrder(30000)]
    public sealed class HoVtsParameterReceiver : MonoBehaviour
    {
        private List<HoVtsParameterSlot> rawValues = new List<HoVtsParameterSlot>();
        private List<HoVtsParameterSlot> values = new List<HoVtsParameterSlot>();
        // Legacy inline rules are retained only so old prefabs can still open and be migrated.
        // New characters use profileAsset; the JSON asset is the authoritative rule source.
        [SerializeField, HideInInspector, FormerlySerializedAs("middleware")]
        private HoFaceMiddleware legacyMiddleware = new HoFaceMiddleware();
        [NonSerialized] private HoFaceMiddleware middleware = new HoFaceMiddleware();
        [SerializeField] private TextAsset profileAsset;
        [SerializeField, Min(0.01f)] private float inputTimeoutSeconds = 1f;
        [SerializeField, Min(0f)] private float lossSmoothingSeconds = .3f;
        private bool tracked;
        private bool inputExpired;
        private bool lossActive;
        private bool lossFading;
        private double lossFadeStart;
        private readonly Dictionary<string, float> lossFadeFrom = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> lossFadeTo = new Dictionary<string, float>(StringComparer.Ordinal);
        private double lastFrameTime = -1.0;
        private string source = string.Empty;

        private readonly Dictionary<string, int> rawIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> valueIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> evaluationRaw = new Dictionary<string, float>(StringComparer.Ordinal);
        private HoVtsRuleEvaluator evaluator;
        private TextAsset loadedProfileAsset;
        private int loadedProfileHash;
        private bool profileLoaded;
        private string profileError;
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
        /// <summary>
        /// 只要收到过帧就为 true。丢追时保留这个“中性空帧”，让消费者能平滑回中，
        /// 而不是因为 HasFrame 变 false 直接 Stop 硬回。
        /// </summary>
        public bool HasFrame => lastFrameTime >= 0;
        public bool InputExpired => inputExpired;
        /// <summary>正在把全部参数淡到 profile 默认值；此期间 Tracked 仍为 true，下游照常消费。</summary>
        public bool LossFading => lossFading;
        public double LastFrameTime => lastFrameTime;
        public string Source => source ?? string.Empty;
        public int RawCount => rawValues != null ? rawValues.Count : 0;
        public int ValueCount => values != null ? values.Count : 0;
        public HoFaceMiddleware Middleware { get { EnsureProfileLoaded(); return middleware; } }
        public TextAsset ProfileAsset => profileAsset;
        public string RuleError => !string.IsNullOrEmpty(profileError) ? profileError : evaluator != null ? evaluator.Error : null;
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
        public float LossSmoothingSeconds
        {
            get => lossSmoothingSeconds;
            set => lossSmoothingSeconds = float.IsNaN(value) || float.IsInfinity(value) ? .3f : Mathf.Max(0f, value);
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
            ApplyRawFrame((Dictionary<string, float>)packet[0], (bool)packet[1],
                (double)packet[2], (string)packet[3]);
            ((Action<GameObject>)packet[4])(gameObject);
        }

        public void HoVtsReleaseInput(object owner)
        {
            if (!(owner is string name) || name != Source) return;
            // 输入适配器主动停止也按丢追处理，让全部参数平滑回默认值。
            if (HasFrame) BeginLoss(Time.realtimeSinceStartupAsDouble, false, true);
            else ClearFrame();
        }

        /// <summary>Assign the reusable JSON profile used by this role.</summary>
        public void SetProfile(TextAsset value)
        {
            profileAsset = value;
            legacyMiddleware = new HoFaceMiddleware();
            loadedProfileAsset = null;
            loadedProfileHash = 0;
            profileLoaded = false;
            profileError = null;
            evaluator = null;
            lastEvaluationTime = -1.0;
            RuleRevision++;
            EnsureProfileLoaded();
        }

        /// <summary>
        /// Legacy/test API for an inline rule table. Assigning one explicitly detaches the
        /// profile asset so callers never silently edit a different source of truth.
        /// </summary>
        public void SetMiddleware(HoFaceMiddleware value)
        {
            profileAsset = null;
            middleware = value ?? new HoFaceMiddleware();
            legacyMiddleware = middleware;
            profileLoaded = true;
            loadedProfileAsset = null;
            profileError = null;
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
            EnsureProfileLoaded();
            if (evaluator == null) evaluator = new HoVtsRuleEvaluator(middleware);
        }

        private void EnsureProfileLoaded()
        {
            if (profileAsset == null)
            {
                if (!profileLoaded)
                    middleware = legacyMiddleware ?? new HoFaceMiddleware();
                else if (middleware == null)
                    middleware = new HoFaceMiddleware();
                if (!profileLoaded)
                {
                    profileLoaded = true;
                    profileError = null;
                }
                return;
            }

            string text;
            int hash;
#if UNITY_EDITOR
            text = profileAsset.text ?? string.Empty;
            hash = text.GetHashCode();
            if (profileLoaded && loadedProfileAsset == profileAsset && loadedProfileHash == hash) return;
#else
            if (profileLoaded && loadedProfileAsset == profileAsset) return;
            text = profileAsset.text ?? string.Empty;
            hash = 0;
#endif

            HoFaceMiddleware parsed;
            string error;
            if (HoFaceProfile.TryParse(text, out parsed, out error))
            {
                middleware = parsed ?? new HoFaceMiddleware();
                profileError = null;
            }
            else
            {
                middleware = new HoFaceMiddleware();
                profileError = "规则 JSON 读取失败：" + error;
            }

            loadedProfileAsset = profileAsset;
            loadedProfileHash = hash;
            profileLoaded = true;
            evaluator = null;
            lastEvaluationTime = -1.0;
            RuleRevision++;
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
            tracked = faceTracked;
            lastFrameTime = timestamp;
            inputExpired = false;
            source = frameSource ?? string.Empty;

            if (!faceTracked)
            {
                // VTS 的 tracked=false：开始把全部参数淡到 profile 默认值。
                BeginLoss(timestamp, true, false);
                return;
            }
            CancelLoss();
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
            if (lossFading)
            {
                AdvanceLossFade(now);
                return true;
            }
            if (!HasFrame || now <= lastEvaluationTime || now < lastFrameTime) return false;
            float timeout = inputTimeoutSeconds > 0 && !float.IsNaN(inputTimeoutSeconds) && !float.IsInfinity(inputTimeoutSeconds)
                ? inputTimeoutSeconds : 1f;
            bool timedOut = now - lastFrameTime > timeout;
            if (lossActive)
            {
                // 淡出已结束：只更新超时标记，不重新求值、也不清掉 HasFrame。
                if (timedOut) inputExpired = true;
                return false;
            }
            if (timedOut)
            {
                // 输入完全静默也走同一条丢追路径：先把全部参数淡到默认值。
                BeginLoss(now, false, true);
                return true;
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

        /// <summary>
        /// 收到丢追信号/输入静默：把当前全部处理值在 lossSmoothingSeconds 内淡到
        /// profile 的默认值。淡出期间 Tracked 仍为 true，IR / LookAt / Controller 都照常
        /// 消费这份逐渐归中的参数，所以不需要各组件再各做一套丢追逻辑。
        /// </summary>
        private void BeginLoss(double now, bool frameArrived, bool expired)
        {
            if (lossFading)
            {
                // 淡出期间的重复丢追帧不能把 Tracked 翻回 false，否则下游会停吃这份渐变值。
                tracked = true;
                if (frameArrived) lastFrameTime = now;
                if (expired) inputExpired = true;
                return;
            }
            if (lossActive)
            {
                tracked = false;
                if (frameArrived) lastFrameTime = now;
                if (expired) inputExpired = true;
                return;
            }
            if (values.Count == 0)
            {
                EnterLoss(frameArrived ? now : -1.0, expired);
                return;
            }

            lossFadeFrom.Clear();
            for (int i = 0; i < values.Count; i++)
                if (!string.IsNullOrEmpty(values[i].key)) lossFadeFrom[values[i].key] = values[i].value;

            lossFadeTo.Clear();
            HoFaceMiddleware rules = Middleware;
            if (rules != null && rules.outputs != null)
                foreach (var output in rules.outputs)
                    if (output != null && !string.IsNullOrEmpty(output.parameter))
                        lossFadeTo[output.parameter] = output.defaultValue;

            lossFading = true;
            lossActive = false;
            tracked = true;
            inputExpired = expired;
            lossFadeStart = now;
            if (frameArrived) lastFrameTime = now;
            lastEvaluationTime = now;
            evaluatedUnityFrame = Time.frameCount;
            if (lossSmoothingSeconds <= 0f) AdvanceLossFade(now);
        }

        private void AdvanceLossFade(double now)
        {
            float duration = lossSmoothingSeconds;
            float t = duration <= 0f ? 1f : Mathf.Clamp01((float)((now - lossFadeStart) / duration));
            float s = t * t * (3f - 2f * t);
            for (int i = 0; i < values.Count; i++)
            {
                HoVtsParameterSlot slot = values[i];
                lossFadeFrom.TryGetValue(slot.key, out float from);
                lossFadeTo.TryGetValue(slot.key, out float to);
                values[i] = new HoVtsParameterSlot(slot.key, Mathf.Lerp(from, to, s));
            }
            lastEvaluationTime = now;
            evaluatedUnityFrame = Time.frameCount;
            if (t < 1f) return;
            lossFading = false;
            lossActive = true;
            tracked = false;
            lossFadeFrom.Clear();
            lossFadeTo.Clear();
        }

        private void CancelLoss()
        {
            lossFading = false;
            lossActive = false;
            lossFadeFrom.Clear();
            lossFadeTo.Clear();
        }

        /// <summary>
        /// 没有可淡出的参数时直接进入 untracked 空帧：保留 lastFrameTime，使 HasFrame 仍为 true。
        /// </summary>
        private void EnterLoss(double frameTime, bool expired)
        {
            EnsureTables();
            tracked = false;
            inputExpired = expired;
            lossFading = false;
            lossActive = true;
            lossFadeFrom.Clear();
            lossFadeTo.Clear();
            values.Clear();
            valueIndices.Clear();
            if (frameTime >= 0.0)
            {
                lastFrameTime = frameTime;
                lastEvaluationTime = frameTime;
            }
            evaluatedUnityFrame = Time.frameCount;
        }

        /// <summary>Clear values and mark the receiver untracked without destroying the component.</summary>
        public void ClearFrame(bool clearRaw = true, bool expired = false)
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
            inputExpired = expired;
            lossFading = false;
            lossActive = false;
            lossFadeFrom.Clear();
            lossFadeTo.Clear();
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
        private void OnValidate()
        {
            if (profileAsset != null) legacyMiddleware = new HoFaceMiddleware();
            profileLoaded = false;
            loadedProfileAsset = null;
            RebuildEvaluator();
        }
        private void LateUpdate()
        {
            if (evaluatedUnityFrame != Time.frameCount) Tick(Time.realtimeSinceStartupAsDouble);
        }
    }
}
