using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.Constraints
{
    public enum HoBlinkUpdateMode
    {
        LateUpdate,
        Update,
        FixedUpdate,
        Manual
    }

    public enum HoBlinkIntervalDistribution
    {
        [InspectorName("均匀")]
        Uniform,

        [InspectorName("指数")]
        Exponential
    }

    public enum HoBlinkDriverKind
    {
        [InspectorName("形态键")]
        ShapeKey,

        [InspectorName("自动眨眼")]
        AutoBlink,

        [InspectorName("手动")]
        Manual,

        /// <summary>眼皮动的速度（0..1）：闭合/张开那一下才有值，用来做"弹一下"的果冻。</summary>
        [InspectorName("眼皮速度")]
        BlinkSpeed
    }

    public enum HoBlinkDriverRange
    {
        [InspectorName("单极")]
        Unipolar,

        [InspectorName("双极")]
        Bipolar
    }

    public enum HoBlinkMissingPolicy
    {
        Skip,
        Warn,
        Error
    }

    /// <summary>自动眨眼输出的左右通道选择。正常眨眼两个通道相同，只有脚本 wink 才会分开。</summary>
    public enum HoBlinkSide
    {
        [InspectorName("双眼")]
        Both,

        [InspectorName("左")]
        Left,

        [InspectorName("右")]
        Right
    }

    /// <summary>以"读哪个键"为主体的一条映射规则。输出映射项用共享的 <see cref="HoShapeKeyTarget"/>。</summary>
    [Serializable]
    public sealed class HoBlinkRule
    {
        [SerializeField]
        private string label = string.Empty;

        [SerializeField]
        private bool enabled = true;

        [SerializeField]
        private HoBlinkDriverKind driverKind = HoBlinkDriverKind.ShapeKey;

        [SerializeField]
        private string positiveKey = string.Empty;

        [SerializeField]
        private string negativeKey = string.Empty;

        [SerializeField]
        private HoBlinkDriverRange driverRange = HoBlinkDriverRange.Unipolar;

        [SerializeField]
        private bool invert;

        [SerializeField]
        private bool readWrittenThisFrame;

        [SerializeField]
        private HoBlinkMissingPolicy missingPolicy = HoBlinkMissingPolicy.Skip;

        [Header("Jelly")]
        [SerializeField]
        private bool jellyEnabled = true;

        [SerializeField, Range(0.1f, 20.0f)]
        private float frequency = 4.0f;

        [SerializeField, Range(0.05f, 2.0f)]
        private float dampingRatio = 0.25f;

        [SerializeField, Range(0.0f, 0.5f)]
        private float inputSmoothing = 0.03f;

        [SerializeField, Range(0.002f, 0.05f)]
        private float maxStep = 0.016f;

        [SerializeField]
        private bool resetOnEnable = true;

        [SerializeField]
        private List<HoShapeKeyTarget> targets = new List<HoShapeKeyTarget>();

        public string Label
        {
            get => label;
            set => label = value;
        }

        public bool Enabled
        {
            get => enabled;
            set => enabled = value;
        }

        public HoBlinkDriverKind DriverKind
        {
            get => driverKind;
            set => driverKind = value;
        }

        public string PositiveKey
        {
            get => positiveKey;
            set => positiveKey = value;
        }

        public string NegativeKey
        {
            get => negativeKey;
            set => negativeKey = value;
        }

        public HoBlinkDriverRange DriverRange
        {
            get => driverRange;
            set => driverRange = value;
        }

        public bool Invert
        {
            get => invert;
            set => invert = value;
        }

        public bool ReadWrittenThisFrame
        {
            get => readWrittenThisFrame;
            set => readWrittenThisFrame = value;
        }

        public HoBlinkMissingPolicy MissingPolicy
        {
            get => missingPolicy;
            set => missingPolicy = value;
        }

        public bool JellyEnabled
        {
            get => jellyEnabled;
            set => jellyEnabled = value;
        }

        public float Frequency
        {
            get => frequency;
            set => frequency = value;
        }

        public float DampingRatio
        {
            get => dampingRatio;
            set => dampingRatio = value;
        }

        public float InputSmoothing
        {
            get => inputSmoothing;
            set => inputSmoothing = value;
        }

        public float MaxStep
        {
            get => maxStep;
            set => maxStep = value;
        }

        public bool ResetOnEnable
        {
            get => resetOnEnable;
            set => resetOnEnable = value;
        }

        public List<HoShapeKeyTarget> Targets => targets;

        public void Sanitize()
        {
            frequency = Mathf.Clamp(frequency, 0.1f, 20.0f);
            dampingRatio = Mathf.Clamp(dampingRatio, 0.05f, 2.0f);
            inputSmoothing = Mathf.Clamp(inputSmoothing, 0.0f, 0.5f);
            maxStep = Mathf.Clamp(maxStep, 0.002f, 0.05f);
            if (targets == null)
            {
                targets = new List<HoShapeKeyTarget>();
            }

            for (int i = 0; i < targets.Count; i++)
            {
                targets[i]?.Sanitize();
            }
        }
    }

    /// <summary>自动眨眼的时序参数（求解器输入，不含随机状态）。</summary>
    public struct HoBlinkTiming
    {
        public float intervalMin;
        public float intervalMax;
        public HoBlinkIntervalDistribution distribution;
        public float closeDuration;
        public float holdDuration;
        public float openDuration;
        public float doubleBlinkChance;
        public float doubleBlinkGap;

        public void Sanitize()
        {
            intervalMin = Mathf.Max(0.05f, intervalMin);
            intervalMax = Mathf.Max(intervalMin, intervalMax);
            closeDuration = Mathf.Max(0.01f, closeDuration);
            holdDuration = Mathf.Max(0.0f, holdDuration);
            openDuration = Mathf.Max(0.01f, openDuration);
            doubleBlinkChance = Mathf.Clamp01(doubleBlinkChance);
            doubleBlinkGap = Mathf.Max(0.02f, doubleBlinkGap);
        }
    }

    public enum HoBlinkPhase
    {
        Wait,
        Closing,
        Hold,
        Opening
    }

    /// <summary>自动眨眼的运行时状态（单计时器，一个输出值）。</summary>
    public struct HoBlinkState
    {
        public HoBlinkPhase phase;
        public float timer;
        public float value;
        public bool pendingDoubleBlink;
        public float drivenTime;
        public float pauseRemaining;
    }

    /// <summary>一条规则的果冻弹簧状态。</summary>
    public struct HoJellyState
    {
        public float smoothed;
        public float value;
        public float velocity;
    }
}
