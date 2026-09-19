using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.Constraints
{
    /// <summary>
    /// 眨眼约束：自动眨眼（程序化眨眼写到眼睑键）+ 果冻眼（读形态键，过弹簧-阻尼，映射到别的键）。
    /// 只写形态键，不碰 Transform / 材质；键名以 string 存储，内置表只提供候选与规范标签。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("HoUnityTools/Constraints/Ho Blink Constraint")]
    public sealed class HoBlinkConstraint : MonoBehaviour
    {
        /// <summary>被外部驱动多久之后暂停自动眨眼。</summary>
        private const float DrivenThresholdTime = 0.5f;

        [Header("Update")]
        [SerializeField]
        private HoBlinkUpdateMode updateMode = HoBlinkUpdateMode.LateUpdate;

        // 默认关：编辑模式下写形态键会改动序列化数据、把场景标脏，作者预览走"手动驱动"。
        [SerializeField]
        private bool evaluateInEditMode = false;

        [Header("Meshes")]
        [SerializeField]
        private List<Renderer> renderers = new List<Renderer>();

        [Header("Blink")]
        [SerializeField]
        private bool blinkEnabled = true;

        [SerializeField]
        private HoBlinkIntervalDistribution intervalDistribution = HoBlinkIntervalDistribution.Exponential;

        [SerializeField, Min(0.05f)]
        private float intervalMin = 2.0f;

        [SerializeField, Min(0.05f)]
        private float intervalMax = 6.0f;

        [SerializeField, Min(0.01f)]
        private float closeDuration = 0.08f;

        [SerializeField, Min(0.0f)]
        private float holdDuration = 0.04f;

        [SerializeField, Min(0.01f)]
        private float openDuration = 0.14f;

        [SerializeField]
        private AnimationCurve blinkCurve = CreateDefaultBlinkCurve();

        [SerializeField, Range(0.0f, 1.0f)]
        private float strength = 1.0f;

        [SerializeField, Range(0.0f, 1.0f)]
        private float doubleBlinkChance = 0.15f;

        [SerializeField, Min(0.02f)]
        private float doubleBlinkGap = 0.16f;

        [SerializeField]
        private int randomSeed;

        [SerializeField]
        private bool pauseWhenDriven = true;

        [SerializeField, Range(0.0f, 100.0f)]
        private float pauseThreshold = 5.0f;

        [SerializeField, Min(0.0f)]
        private float pauseDuration = 2.0f;

        [SerializeField]
        private List<HoBlinkTarget> blinkTargets = new List<HoBlinkTarget>();

        [Header("Rules")]
        [SerializeField]
        private List<HoBlinkRule> rules = new List<HoBlinkRule>();

        [Header("Write")]
        [SerializeField, Min(0.0f)]
        private float writeThreshold = 0.01f;

        [SerializeField]
        private bool writingEnabled = true;

        private struct Binding
        {
            public int MeshIndex;
            public int KeyIndex;
            public float BaseValue;
            public float LastWritten;
            public float Sum;
            public float OverrideValue;
            public bool HasOverride;
            public bool EverWritten;
        }

        private struct CompiledTarget
        {
            public int BindingStart;
            public int BindingCount;
            public float Envelope;
            public float Output;
        }

        private SkinnedMeshRenderer[] meshCache;
        private Mesh[] meshRefs;
        private Dictionary<string, int>[] meshLookups;
        private int lastRendererCount = -1;

        private Binding[] bindings;
        private int[] bindingIds;

        private CompiledTarget[] blinkCompiled;
        private CompiledTarget[] ruleCompiled;
        private int[] ruleTargetStart;
        private int[] ruleTargetCount;

        private int[] driverBindingIds;
        private int[] driverPositiveStart;
        private int[] driverPositiveCount;
        private int[] driverNegativeStart;
        private int[] driverNegativeCount;

        private HoJellyState[] jellyStates;
        private float[] manualValues;
        private float[] driverValues;
        private int takeOverBinding = -1;

        private HoBlinkState blinkState;
        private System.Random random;
        private float blinkLeft;
        private float blinkRight;
        private float winkRemaining;
        private float winkLeft;
        private float winkRight;
        private double lastUpdateTime;
        private bool built;
        private bool manualDriveEnabled;
        private readonly List<string> missingKeys = new List<string>();

        /// <summary>调试用：打开后所有规则都读手动滑杆值（不进序列化）。</summary>
        public bool ManualDriveEnabled
        {
            get => manualDriveEnabled;
            set => manualDriveEnabled = value;
        }

        /// <summary>编辑器用：丢弃缓存并重新解析网格与键。</summary>
        public void Rebuild()
        {
            built = false;
            EnsureBuilt();
        }

        public HoBlinkUpdateMode UpdateMode
        {
            get => updateMode;
            set => updateMode = value;
        }

        public bool WritingEnabled
        {
            get => writingEnabled;
            set => writingEnabled = value;
        }

        public bool BlinkEnabled
        {
            get => blinkEnabled;
            set => blinkEnabled = value;
        }

        public IReadOnlyList<string> MissingKeys => missingKeys;

        public int MeshCount => meshCache != null ? meshCache.Length : 0;

        public int BindingCount => bindings != null ? bindings.Length : 0;

        public int RuleCount => rules != null ? rules.Count : 0;

        public int BlinkOutputCount => blinkTargets != null ? blinkTargets.Count : 0;

        public float BlinkValue => Mathf.Max(blinkLeft, blinkRight);

        public HoBlinkPhase BlinkPhase => blinkState.phase;

        public SkinnedMeshRenderer GetMesh(int index)
        {
            if (meshCache == null || index < 0 || index >= meshCache.Length)
            {
                return null;
            }

            return meshCache[index];
        }

        public HoBlinkRule GetRule(int index)
        {
            if (rules == null || index < 0 || index >= rules.Count)
            {
                return null;
            }

            return rules[index];
        }

        public float GetDriverValue(int ruleIndex)
        {
            if (driverValues == null || ruleIndex < 0 || ruleIndex >= driverValues.Length)
            {
                return 0.0f;
            }

            return driverValues[ruleIndex];
        }

        public float GetJellyValue(int ruleIndex)
        {
            if (jellyStates == null || ruleIndex < 0 || ruleIndex >= jellyStates.Length)
            {
                return 0.0f;
            }

            return jellyStates[ruleIndex].value;
        }

        public int GetRuleTargetCount(int ruleIndex)
        {
            if (ruleTargetCount == null || ruleIndex < 0 || ruleIndex >= ruleTargetCount.Length)
            {
                return 0;
            }

            return ruleTargetCount[ruleIndex];
        }

        public float GetRuleTargetOutput(int ruleIndex, int targetIndex)
        {
            int flat = FlatRuleTarget(ruleIndex, targetIndex);
            if (flat < 0)
            {
                return 0.0f;
            }

            return ruleCompiled[flat].Output;
        }

        public float GetBlinkTargetOutput(int targetIndex)
        {
            if (blinkCompiled == null || targetIndex < 0 || targetIndex >= blinkCompiled.Length)
            {
                return 0.0f;
            }

            return blinkCompiled[targetIndex].Output;
        }

        /// <summary>键名是否在任意一个网格上存在（面板用它标黄缺失项）。</summary>
        public bool KeyExists(string keyName)
        {
            if (meshLookups == null || string.IsNullOrEmpty(keyName))
            {
                return false;
            }

            for (int i = 0; i < meshLookups.Length; i++)
            {
                if (HoShapeKeyResolver.TryResolve(meshLookups[i], keyName, out _))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>把子级里所有 SkinnedMeshRenderer 收进网格列表（面板按钮用）。</summary>
        public void CollectChildRenderers()
        {
            SkinnedMeshRenderer[] found = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            renderers.Clear();
            for (int i = 0; i < found.Length; i++)
            {
                renderers.Add(found[i]);
            }

            built = false;
        }

        public void SetManualDriverValue(int ruleIndex, float value)
        {
            if (manualValues == null || ruleIndex < 0 || ruleIndex >= manualValues.Length)
            {
                return;
            }

            manualValues[ruleIndex] = value;
        }

        /// <summary>立刻眨一次（双眼，走完整曲线）。</summary>
        public void TriggerBlink()
        {
            HoBlinkTiming timing = BuildTiming();
            HoBlinkGenerator.Request(ref blinkState, timing);
            winkRemaining = 0.0f;
        }

        /// <summary>显式 wink：duration 秒内左右通道取给定值（自动眨眼让位）。</summary>
        public void TriggerBlink(float left01, float right01, float duration)
        {
            winkLeft = Mathf.Clamp01(left01);
            winkRight = Mathf.Clamp01(right01);
            winkRemaining = Mathf.Max(0.01f, duration);
        }

        public void ResetState()
        {
            blinkState = default;
            blinkLeft = 0.0f;
            blinkRight = 0.0f;
            winkRemaining = 0.0f;
            winkLeft = 0.0f;
            winkRight = 0.0f;
            random = new System.Random(randomSeed != 0 ? randomSeed : GetInstanceID());

            if (jellyStates != null)
            {
                for (int i = 0; i < jellyStates.Length; i++)
                {
                    HoJellySolver.Reset(ref jellyStates[i], 0.0f);
                }
            }

            if (manualValues != null)
            {
                for (int i = 0; i < manualValues.Length; i++)
                {
                    manualValues[i] = 0.0f;
                }
            }

            if (bindings != null)
            {
                for (int i = 0; i < bindings.Length; i++)
                {
                    bindings[i].EverWritten = false;
                    bindings[i].Sum = 0.0f;
                    bindings[i].HasOverride = false;
                }
            }

            lastUpdateTime = GetTime();
        }

        private void OnEnable()
        {
            built = false;
            EnsureBuilt();
        }

        private void OnDisable()
        {
            built = false;
        }

        private void Update()
        {
            if (updateMode == HoBlinkUpdateMode.Update)
            {
                EvaluateWithCurrentDelta();
            }
        }

        private void LateUpdate()
        {
            if (updateMode == HoBlinkUpdateMode.LateUpdate)
            {
                EvaluateWithCurrentDelta();
            }
        }

        private void FixedUpdate()
        {
            if (updateMode == HoBlinkUpdateMode.FixedUpdate)
            {
                Evaluate(Time.fixedDeltaTime);
            }
        }

        private void OnValidate()
        {
            intervalMax = Mathf.Max(intervalMin, intervalMax);
            closeDuration = Mathf.Max(0.01f, closeDuration);
            holdDuration = Mathf.Max(0.0f, holdDuration);
            openDuration = Mathf.Max(0.01f, openDuration);
            doubleBlinkGap = Mathf.Max(0.02f, doubleBlinkGap);
            pauseDuration = Mathf.Max(0.0f, pauseDuration);
            writeThreshold = Mathf.Max(0.0f, writeThreshold);
            if (blinkCurve == null || blinkCurve.length == 0)
            {
                blinkCurve = CreateDefaultBlinkCurve();
            }

            SanitizeList(blinkTargets);
            if (rules != null)
            {
                for (int i = 0; i < rules.Count; i++)
                {
                    rules[i]?.Sanitize();
                }
            }

            built = false;
        }

        public void Evaluate(float deltaTime)
        {
            if (!ShouldEvaluate())
            {
                return;
            }

            EnsureBuilt();
            if (!writingEnabled || meshCache == null || meshCache.Length == 0)
            {
                return;
            }

            if (MeshesChanged())
            {
                built = false;
                EnsureBuilt();
                if (meshCache.Length == 0)
                {
                    return;
                }
            }

            float dt = Mathf.Max(0.0f, deltaTime);
            Snapshot();
            StepBlink(dt);

            for (int i = 0; i < blinkCompiled.Length; i++)
            {
                HoBlinkTarget target = blinkTargets[i];
                if (target == null)
                {
                    continue;
                }

                ApplyTarget(ref blinkCompiled[i], target, SelectChannel(target.Side), dt);
            }

            for (int i = 0; i < rules.Count; i++)
            {
                HoBlinkRule rule = rules[i];
                if (rule == null || !rule.Enabled)
                {
                    if (driverValues != null && i < driverValues.Length)
                    {
                        driverValues[i] = 0.0f;
                    }

                    continue;
                }

                float raw = ReadDriver(rule, i);
                if (rule.JellyEnabled)
                {
                    HoJellySolver.Step(
                        ref jellyStates[i],
                        raw,
                        dt,
                        rule.Frequency,
                        rule.DampingRatio,
                        rule.InputSmoothing,
                        rule.MaxStep);
                    driverValues[i] = jellyStates[i].value;
                }
                else
                {
                    HoJellySolver.Reset(ref jellyStates[i], raw);
                    driverValues[i] = raw;
                }

                int start = ruleTargetStart[i];
                int count = ruleTargetCount[i];
                for (int t = 0; t < count; t++)
                {
                    HoBlinkTarget target = rule.Targets[t];
                    if (target == null)
                    {
                        continue;
                    }

                    ApplyTarget(ref ruleCompiled[start + t], target, driverValues[i], dt);
                }
            }

            WriteBindings();
        }

        private void EvaluateWithCurrentDelta()
        {
            double currentTime = GetTime();
            float deltaTime = built ? (float)(currentTime - lastUpdateTime) : 0.0f;
            lastUpdateTime = currentTime;
            Evaluate(deltaTime);
        }

        private bool ShouldEvaluate()
        {
            if (Application.isPlaying)
            {
                return true;
            }

            return evaluateInEditMode;
        }

        private void EnsureBuilt()
        {
            if (built && !MeshesChanged() && lastRendererCount == (renderers != null ? renderers.Count : 0))
            {
                return;
            }

            Build();
            ResetState();
            built = true;
        }

        private void Build()
        {
            missingKeys.Clear();

            List<SkinnedMeshRenderer> meshes = new List<SkinnedMeshRenderer>();
            if (renderers != null)
            {
                for (int i = 0; i < renderers.Count; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer == null)
                    {
                        continue;
                    }

                    SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
                    if (skinned == null)
                    {
                        skinned = renderer.GetComponent<SkinnedMeshRenderer>();
                    }

                    if (skinned == null || skinned.sharedMesh == null || meshes.Contains(skinned))
                    {
                        continue;
                    }

                    meshes.Add(skinned);
                }
            }

            meshCache = meshes.ToArray();
            meshRefs = new Mesh[meshCache.Length];
            meshLookups = new Dictionary<string, int>[meshCache.Length];
            for (int i = 0; i < meshCache.Length; i++)
            {
                meshRefs[i] = meshCache[i].sharedMesh;
                meshLookups[i] = HoShapeKeyResolver.BuildLookup(meshRefs[i]);
            }

            lastRendererCount = renderers != null ? renderers.Count : 0;

            Dictionary<long, int> bindingMap = new Dictionary<long, int>();
            List<Binding> bindingList = new List<Binding>();
            List<int> flatIds = new List<int>();

            blinkCompiled = CompileTargets(blinkTargets, bindingMap, bindingList, flatIds, true);
            takeOverBinding = blinkCompiled.Length > 0 && blinkCompiled[0].BindingCount > 0
                ? flatIds[blinkCompiled[0].BindingStart]
                : -1;

            int ruleCount = rules != null ? rules.Count : 0;
            ruleCompiled = new CompiledTarget[0];
            ruleTargetStart = new int[ruleCount];
            ruleTargetCount = new int[ruleCount];
            driverPositiveStart = new int[ruleCount];
            driverPositiveCount = new int[ruleCount];
            driverNegativeStart = new int[ruleCount];
            driverNegativeCount = new int[ruleCount];
            jellyStates = new HoJellyState[ruleCount];
            manualValues = new float[ruleCount];
            driverValues = new float[ruleCount];

            List<CompiledTarget> ruleTargets = new List<CompiledTarget>();
            List<int> driverIds = new List<int>();

            for (int i = 0; i < ruleCount; i++)
            {
                HoBlinkRule rule = rules[i];
                if (rule == null)
                {
                    continue;
                }

                CompiledTarget[] compiled = CompileTargets(rule.Targets, bindingMap, bindingList, flatIds, true);
                ruleTargetStart[i] = ruleTargets.Count;
                ruleTargetCount[i] = compiled.Length;
                ruleTargets.AddRange(compiled);

                if (rule.DriverKind == HoBlinkDriverKind.ShapeKey)
                {
                    driverPositiveStart[i] = driverIds.Count;
                    driverPositiveCount[i] = ResolveKeyBindings(rule.PositiveKey, bindingMap, bindingList, driverIds, true);
                    driverNegativeStart[i] = driverIds.Count;
                    driverNegativeCount[i] = ResolveKeyBindings(rule.NegativeKey, bindingMap, bindingList, driverIds, false);
                }
            }

            ruleCompiled = ruleTargets.ToArray();
            driverBindingIds = driverIds.ToArray();
            bindings = bindingList.ToArray();
            bindingIds = flatIds.ToArray();
        }

        private CompiledTarget[] CompileTargets(
            List<HoBlinkTarget> list,
            Dictionary<long, int> bindingMap,
            List<Binding> bindingList,
            List<int> flatIds,
            bool collectMissing)
        {
            int count = list != null ? list.Count : 0;
            CompiledTarget[] result = new CompiledTarget[count];
            for (int i = 0; i < count; i++)
            {
                HoBlinkTarget target = list[i];
                int start = flatIds.Count;
                int resolved = 0;
                if (target != null && !string.IsNullOrEmpty(target.KeyName))
                {
                    int from = 0;
                    int to = meshCache.Length;
                    if (target.MeshScope == HoBlinkMeshScope.Index)
                    {
                        from = Mathf.Clamp(target.MeshIndex, 0, Mathf.Max(0, meshCache.Length - 1));
                        to = Mathf.Min(from + 1, meshCache.Length);
                    }

                    for (int m = from; m < to; m++)
                    {
                        if (!HoShapeKeyResolver.TryResolve(meshLookups[m], target.KeyName, out int keyIndex))
                        {
                            continue;
                        }

                        flatIds.Add(GetOrAddBinding(bindingMap, bindingList, m, keyIndex));
                    }

                    resolved = flatIds.Count - start;
                    if (resolved == 0 && collectMissing)
                    {
                        AddMissing(target.KeyName);
                    }
                }

                result[i] = new CompiledTarget
                {
                    BindingStart = start,
                    BindingCount = resolved
                };
            }

            return result;
        }

        private int ResolveKeyBindings(
            string keyName,
            Dictionary<long, int> bindingMap,
            List<Binding> bindingList,
            List<int> ids,
            bool collectMissing)
        {
            if (string.IsNullOrEmpty(keyName))
            {
                return 0;
            }

            int start = ids.Count;
            for (int m = 0; m < meshCache.Length; m++)
            {
                if (!HoShapeKeyResolver.TryResolve(meshLookups[m], keyName, out int keyIndex))
                {
                    continue;
                }

                ids.Add(GetOrAddBinding(bindingMap, bindingList, m, keyIndex));
            }

            int resolved = ids.Count - start;
            if (resolved == 0 && collectMissing)
            {
                AddMissing(keyName);
            }

            return resolved;
        }

        private static int GetOrAddBinding(Dictionary<long, int> map, List<Binding> list, int meshIndex, int keyIndex)
        {
            long key = ((long)meshIndex << 32) | (uint)keyIndex;
            if (map.TryGetValue(key, out int id))
            {
                return id;
            }

            id = list.Count;
            list.Add(new Binding { MeshIndex = meshIndex, KeyIndex = keyIndex });
            map.Add(key, id);
            return id;
        }

        private void AddMissing(string keyName)
        {
            if (string.IsNullOrEmpty(keyName) || missingKeys.Contains(keyName))
            {
                return;
            }

            missingKeys.Add(keyName);
        }

        private bool MeshesChanged()
        {
            if (meshCache == null || meshRefs == null || meshCache.Length != meshRefs.Length)
            {
                return true;
            }

            for (int i = 0; i < meshCache.Length; i++)
            {
                if (meshCache[i] == null || meshRefs[i] != meshCache[i].sharedMesh)
                {
                    return true;
                }
            }

            return false;
        }

        private void Snapshot()
        {
            for (int i = 0; i < bindings.Length; i++)
            {
                bindings[i].BaseValue = meshCache[bindings[i].MeshIndex].GetBlendShapeWeight(bindings[i].KeyIndex);
                bindings[i].Sum = 0.0f;
                bindings[i].HasOverride = false;
                bindings[i].OverrideValue = 0.0f;
            }
        }

        private void StepBlink(float deltaTime)
        {
            if (winkRemaining > 0.0f)
            {
                winkRemaining = Mathf.Max(0.0f, winkRemaining - deltaTime);
                blinkLeft = winkLeft;
                blinkRight = winkRight;
                return;
            }

            if (!blinkEnabled)
            {
                blinkState.phase = HoBlinkPhase.Wait;
                blinkState.timer = 0.0f;
                blinkState.value = 0.0f;
                blinkState.pauseRemaining = 0.0f;
                blinkState.drivenTime = 0.0f;
                blinkLeft = 0.0f;
                blinkRight = 0.0f;
                return;
            }

            if (pauseWhenDriven && takeOverBinding >= 0)
            {
                float driven = bindings[takeOverBinding].BaseValue;
                if (driven > pauseThreshold)
                {
                    blinkState.drivenTime += deltaTime;
                    if (blinkState.drivenTime >= DrivenThresholdTime)
                    {
                        blinkState.pauseRemaining = pauseDuration;
                        blinkState.drivenTime = 0.0f;
                    }
                }
                else
                {
                    blinkState.drivenTime = Mathf.Max(0.0f, blinkState.drivenTime - deltaTime);
                }
            }

            HoBlinkTiming timing = BuildTiming();
            HoBlinkGenerator.Step(ref blinkState, timing, deltaTime, blinkCurve, random);

            float value = Mathf.Clamp01(blinkState.value) * strength;
            blinkLeft = value;
            blinkRight = value;
        }

        private HoBlinkTiming BuildTiming()
        {
            HoBlinkTiming timing = new HoBlinkTiming
            {
                intervalMin = intervalMin,
                intervalMax = intervalMax,
                distribution = intervalDistribution,
                closeDuration = closeDuration,
                holdDuration = holdDuration,
                openDuration = openDuration,
                doubleBlinkChance = doubleBlinkChance,
                doubleBlinkGap = doubleBlinkGap
            };
            timing.Sanitize();
            return timing;
        }

        private float SelectChannel(HoBlinkSide side)
        {
            switch (side)
            {
                case HoBlinkSide.Left:
                    return blinkLeft;
                case HoBlinkSide.Right:
                    return blinkRight;
                default:
                    return Mathf.Max(blinkLeft, blinkRight);
            }
        }

        private float ReadDriver(HoBlinkRule rule, int ruleIndex)
        {
            if (manualDriveEnabled && manualValues != null && ruleIndex >= 0 && ruleIndex < manualValues.Length)
            {
                return rule.Invert ? -manualValues[ruleIndex] : manualValues[ruleIndex];
            }

            float value;
            switch (rule.DriverKind)
            {
                case HoBlinkDriverKind.AutoBlink:
                    value = SelectChannel(HoBlinkSide.Both);
                    if (rule.DriverRange == HoBlinkDriverRange.Bipolar)
                    {
                        value = value * 2.0f - 1.0f;
                    }

                    break;

                case HoBlinkDriverKind.Manual:
                    value = manualValues[ruleIndex];
                    break;

                default:
                {
                    float positive = ReadKeyGroup(driverPositiveStart[ruleIndex], driverPositiveCount[ruleIndex], rule.ReadWrittenThisFrame);
                    float negative = ReadKeyGroup(driverNegativeStart[ruleIndex], driverNegativeCount[ruleIndex], rule.ReadWrittenThisFrame);
                    value = rule.DriverRange == HoBlinkDriverRange.Bipolar
                        ? (positive - negative) / 100.0f
                        : positive / 100.0f;
                    break;
                }
            }

            return rule.Invert ? -value : value;
        }

        /// <summary>同一驱动键出现在多个网格上时取最大值（"任意一个网格上有这个表情"）。</summary>
        private float ReadKeyGroup(int start, int count, bool readWrittenThisFrame)
        {
            float value = 0.0f;
            for (int i = 0; i < count; i++)
            {
                int id = driverBindingIds[start + i];
                float current = readWrittenThisFrame ? PendingValue(id) : bindings[id].BaseValue;
                if (i == 0 || current > value)
                {
                    value = current;
                }
            }

            return value;
        }

        /// <summary>本帧到目前为止这一路会写出的值（供 readWrittenThisFrame 的链式联动）。</summary>
        private float PendingValue(int bindingId)
        {
            Binding binding = bindings[bindingId];
            return binding.HasOverride ? binding.OverrideValue : binding.BaseValue + binding.Sum;
        }

        private void ApplyTarget(ref CompiledTarget compiled, HoBlinkTarget target, float driverValue, float deltaTime)
        {
            float x = driverValue * target.Gain + target.Offset;
            float shaped = HoBlinkRampPresets.Shape(target.RampPreset, target.RampIntensity, target.RampCurve, x);
            HoBlinkRampPresets.Times(
                target.RampPreset,
                target.RampIntensity,
                target.RampAttack,
                target.RampRelease,
                out float attack,
                out float release);

            compiled.Envelope = Envelope(compiled.Envelope, shaped, deltaTime, attack, release);

            float output = compiled.Envelope * 100.0f;
            if (target.ClampToRange)
            {
                float min = Mathf.Min(target.OutputMin, target.OutputMax);
                float max = Mathf.Max(target.OutputMin, target.OutputMax);
                output = Mathf.Clamp(output, min, max);
            }

            compiled.Output = output;
            if (target.Weight <= 0.0f || compiled.BindingCount == 0)
            {
                return;
            }

            for (int i = 0; i < compiled.BindingCount; i++)
            {
                int id = bindingIds[compiled.BindingStart + i];
                if (target.BlendMode == HoBlinkBlendMode.Override)
                {
                    bindings[id].HasOverride = true;
                    bindings[id].OverrideValue = Mathf.Lerp(bindings[id].BaseValue, output, target.Weight);
                }
                else
                {
                    bindings[id].Sum += output * target.Weight;
                }
            }
        }

        private static float Envelope(float current, float target, float deltaTime, float attack, float release)
        {
            if (deltaTime <= 0.0f)
            {
                return target;
            }

            float timeConstant = target > current ? attack : release;
            if (timeConstant <= 0.0f)
            {
                return target;
            }

            float t = 1.0f - Mathf.Exp(-deltaTime / timeConstant);
            return Mathf.Lerp(current, target, t);
        }

        private void WriteBindings()
        {
            for (int i = 0; i < bindings.Length; i++)
            {
                float final = bindings[i].HasOverride
                    ? bindings[i].OverrideValue
                    : bindings[i].BaseValue + bindings[i].Sum;

                if (bindings[i].EverWritten && Mathf.Abs(final - bindings[i].LastWritten) <= writeThreshold)
                {
                    continue;
                }

                SkinnedMeshRenderer mesh = meshCache[bindings[i].MeshIndex];
                if (mesh == null)
                {
                    continue;
                }

                mesh.SetBlendShapeWeight(bindings[i].KeyIndex, final);
                bindings[i].LastWritten = final;
                bindings[i].EverWritten = true;
            }
        }

        private int FlatRuleTarget(int ruleIndex, int targetIndex)
        {
            if (ruleTargetStart == null || ruleTargetCount == null)
            {
                return -1;
            }

            if (ruleIndex < 0 || ruleIndex >= ruleTargetStart.Length)
            {
                return -1;
            }

            if (targetIndex < 0 || targetIndex >= ruleTargetCount[ruleIndex])
            {
                return -1;
            }

            return ruleTargetStart[ruleIndex] + targetIndex;
        }

        private static void SanitizeList(List<HoBlinkTarget> list)
        {
            if (list == null)
            {
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                list[i]?.Sanitize();
            }
        }

        private static AnimationCurve CreateDefaultBlinkCurve()
        {
            AnimationCurve curve = new AnimationCurve();
            curve.AddKey(new Keyframe(0.0f, 0.0f, 0.0f, 2.5f));
            curve.AddKey(new Keyframe(0.65f, 1.0f, 1.2f, 0.0f));
            curve.AddKey(new Keyframe(1.0f, 1.0f, 0.0f, 0.0f));
            return curve;
        }

        private static double GetTime()
        {
            return Application.isPlaying ? Time.timeAsDouble : Time.realtimeSinceStartupAsDouble;
        }
    }
}
