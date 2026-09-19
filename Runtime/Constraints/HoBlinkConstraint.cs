using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.Constraints
{
    /// <summary>
    /// 眨眼约束：自动眨眼（程序化眨眼写到眼睑键）+ 果冻眼（读形态键，过弹簧-阻尼，映射到别的键）。
    /// 只写形态键，不碰 Transform / 材质；键名以 string 存储，内置表只提供候选与规范标签。
    /// 形态键的绑定、基准快照、合并与写回交给共享的 <see cref="HoShapeKeyWriter"/>。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("HoUnityTools/Constraints/Ho Blink Constraint")]
    public sealed class HoBlinkConstraint : MonoBehaviour, IHoShapeKeyMeshProvider
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
        private List<HoShapeKeyTarget> blinkTargets = new List<HoShapeKeyTarget>();

        [Header("Rules")]
        [SerializeField]
        private List<HoBlinkRule> rules = new List<HoBlinkRule>();

        [Header("Write")]
        [SerializeField, Min(0.0f)]
        private float writeThreshold = 0.01f;

        [SerializeField]
        private bool writingEnabled = true;

        private readonly HoShapeKeyWriter writer = new HoShapeKeyWriter();
        private readonly List<int> scratchDriverIds = new List<int>();

        private int[] blinkTargetIds;
        private int[] ruleTargetIds;
        private int[] ruleTargetStart;
        private int[] ruleTargetCount;
        private int[] driverIds;
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

        /// <summary>调试用：打开后所有规则都读手动滑杆值（不进序列化）。</summary>
        public bool ManualDriveEnabled
        {
            get => manualDriveEnabled;
            set => manualDriveEnabled = value;
        }

        public HoBlinkUpdateMode UpdateMode
        {
            get => updateMode;
            set => updateMode = value;
        }

        public bool WritingEnabled
        {
            get => writingEnabled;
            set
            {
                writingEnabled = value;
                writer.WriteEnabled = value;
            }
        }

        public bool BlinkEnabled
        {
            get => blinkEnabled;
            set => blinkEnabled = value;
        }

        public IReadOnlyList<string> MissingKeys => writer.MissingKeys;

        public int MeshCount => writer.MeshCount;

        public int BindingCount => writer.BindingCount;

        public int RuleCount => rules != null ? rules.Count : 0;

        public int BlinkOutputCount => blinkTargets != null ? blinkTargets.Count : 0;

        public float BlinkValue => Mathf.Max(blinkLeft, blinkRight);

        public HoBlinkPhase BlinkPhase => blinkState.phase;

        public SkinnedMeshRenderer GetMesh(int index)
        {
            return writer.GetMesh(index);
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
            return flat < 0 ? 0.0f : writer.GetTargetOutput(ruleTargetIds[flat]);
        }

        public float GetBlinkTargetOutput(int targetIndex)
        {
            if (blinkTargetIds == null || targetIndex < 0 || targetIndex >= blinkTargetIds.Length)
            {
                return 0.0f;
            }

            return writer.GetTargetOutput(blinkTargetIds[targetIndex]);
        }

        /// <summary>键名是否在任意一个网格上存在（面板用它标黄缺失项）。</summary>
        public bool KeyExists(string keyName)
        {
            return writer.KeyExists(keyName);
        }

        /// <summary>编辑器用：丢弃缓存并重新解析网格与键。</summary>
        public void Rebuild()
        {
            built = false;
            EnsureBuilt();
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

            writer.Reset();
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

            writer.WriteThreshold = writeThreshold;
            writer.WriteEnabled = writingEnabled;
            built = false;
        }

        public void Evaluate(float deltaTime)
        {
            if (!ShouldEvaluate())
            {
                return;
            }

            EnsureBuilt();
            writer.WriteThreshold = writeThreshold;
            writer.WriteEnabled = writingEnabled;

            if (!writingEnabled || !writer.IsBuilt || writer.MeshCount == 0)
            {
                return;
            }

            if (writer.MeshesChanged())
            {
                built = false;
                EnsureBuilt();
                if (!writer.IsBuilt || writer.MeshCount == 0)
                {
                    return;
                }
            }

            float dt = Mathf.Max(0.0f, deltaTime);
            writer.Snapshot();
            StepBlink(dt);

            for (int i = 0; i < blinkTargetIds.Length; i++)
            {
                HoShapeKeyTarget target = blinkTargets[i];
                if (target == null)
                {
                    continue;
                }

                writer.Apply(blinkTargetIds[i], SelectChannel(target.Side), dt);
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
                    HoShapeKeyTarget target = rule.Targets[t];
                    if (target == null)
                    {
                        continue;
                    }

                    writer.Apply(ruleTargetIds[start + t], driverValues[i], dt);
                }
            }

            writer.Write();
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
            if (built && !writer.MeshesChanged())
            {
                return;
            }

            Build();
            ResetState();
            built = true;
        }

        private void Build()
        {
            writer.BeginBuild(renderers);

            blinkTargetIds = new int[blinkTargets != null ? blinkTargets.Count : 0];
            for (int i = 0; i < blinkTargetIds.Length; i++)
            {
                blinkTargetIds[i] = writer.RegisterTarget(blinkTargets[i]);
            }

            takeOverBinding = -1;
            if (blinkTargetIds.Length > 0 && blinkTargets[0] != null)
            {
                // 接管判定读的是"第一个眨眼输出键"的外部基准（别人写的值，不是我们自己的输出）
                scratchDriverIds.Clear();
                writer.RegisterReadKey(blinkTargets[0].KeyName, scratchDriverIds);
                if (scratchDriverIds.Count > 0)
                {
                    takeOverBinding = scratchDriverIds[0];
                }
            }

            int ruleCount = rules != null ? rules.Count : 0;
            ruleTargetStart = new int[ruleCount];
            ruleTargetCount = new int[ruleCount];
            driverPositiveStart = new int[ruleCount];
            driverPositiveCount = new int[ruleCount];
            driverNegativeStart = new int[ruleCount];
            driverNegativeCount = new int[ruleCount];
            jellyStates = new HoJellyState[ruleCount];
            manualValues = new float[ruleCount];
            driverValues = new float[ruleCount];

            List<int> ruleTargets = new List<int>();
            List<int> driverBindings = new List<int>();

            for (int i = 0; i < ruleCount; i++)
            {
                HoBlinkRule rule = rules[i];
                if (rule == null)
                {
                    continue;
                }

                ruleTargetStart[i] = ruleTargets.Count;
                int targetCount = rule.Targets != null ? rule.Targets.Count : 0;
                for (int t = 0; t < targetCount; t++)
                {
                    ruleTargets.Add(writer.RegisterTarget(rule.Targets[t]));
                }

                ruleTargetCount[i] = targetCount;

                if (rule.DriverKind == HoBlinkDriverKind.ShapeKey)
                {
                    scratchDriverIds.Clear();
                    writer.RegisterReadKey(rule.PositiveKey, scratchDriverIds);
                    driverPositiveStart[i] = driverBindings.Count;
                    driverPositiveCount[i] = scratchDriverIds.Count;
                    driverBindings.AddRange(scratchDriverIds);

                    scratchDriverIds.Clear();
                    writer.RegisterReadKey(rule.NegativeKey, scratchDriverIds);
                    driverNegativeStart[i] = driverBindings.Count;
                    driverNegativeCount[i] = scratchDriverIds.Count;
                    driverBindings.AddRange(scratchDriverIds);
                }
            }

            ruleTargetIds = ruleTargets.ToArray();
            driverIds = driverBindings.ToArray();
            writer.EndBuild();
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
                float driven = writer.ReadBinding(takeOverBinding, false);
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
                float current = writer.ReadBinding(driverIds[start + i], readWrittenThisFrame);
                if (i == 0 || current > value)
                {
                    value = current;
                }
            }

            return value;
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

        private static void SanitizeList(List<HoShapeKeyTarget> list)
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
