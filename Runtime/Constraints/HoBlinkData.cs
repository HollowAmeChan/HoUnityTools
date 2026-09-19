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
        Uniform,
        Exponential
    }

    public enum HoBlinkDriverKind
    {
        ShapeKey,
        AutoBlink,
        Manual
    }

    public enum HoBlinkDriverRange
    {
        Unipolar,
        Bipolar
    }

    public enum HoBlinkMissingPolicy
    {
        Skip,
        Warn,
        Error
    }

    public enum HoBlinkBlendMode
    {
        Additive,
        Override
    }

    public enum HoBlinkMeshScope
    {
        All,
        Index
    }

    /// <summary>自动眨眼输出的左右通道选择。正常眨眼两个通道相同，只有脚本 wink 才会分开。</summary>
    public enum HoBlinkSide
    {
        Both,
        Left,
        Right
    }

    /// <summary>
    /// 目标级 ramp 预设。动态超调（甩过头再回来）只由规则级弹簧负责，
    /// ramp 只做幅度形状与一阶时间包络，避免同一路出现两个弹簧互相打架。
    /// </summary>
    public enum HoBlinkRampPreset
    {
        /// <summary>直接通过（y = x × 强度），果冻感交给弹簧。</summary>
        Direct,

        /// <summary>时间上的柔跟：形状线性，上升/回落都有一阶跟随。</summary>
        SoftFollow,

        /// <summary>幅度放大（1 + 0.25 × 强度 倍），可以超出目标范围。</summary>
        Amplify,

        /// <summary>幅度缓入（x²），起步慢后段快。</summary>
        EaseIn,

        /// <summary>松得慢：上升快、回落慢。</summary>
        SlowRelease,

        /// <summary>阶梯：驱动大于 0 就满值，立即切换。离散形变用。</summary>
        Step,

        /// <summary>自定义曲线与时间常数。</summary>
        Custom
    }

    /// <summary>一条规则里的一个输出映射项。</summary>
    [Serializable]
    public sealed class HoBlinkTarget
    {
        [SerializeField]
        private HoBlinkMeshScope meshScope = HoBlinkMeshScope.All;

        [SerializeField]
        private int meshIndex;

        [SerializeField]
        private string keyName = string.Empty;

        [SerializeField]
        private HoBlinkSide side = HoBlinkSide.Both;

        [SerializeField]
        private HoBlinkBlendMode blendMode = HoBlinkBlendMode.Additive;

        [SerializeField, Range(0.0f, 1.0f)]
        private float weight = 1.0f;

        [SerializeField]
        private float gain = 1.0f;

        [SerializeField]
        private float offset;

        [SerializeField]
        private float outputMin;

        [SerializeField]
        private float outputMax = 100.0f;

        [SerializeField]
        private bool clampToRange = true;

        [SerializeField]
        private HoBlinkRampPreset rampPreset = HoBlinkRampPreset.Direct;

        [SerializeField, Range(0.0f, 2.0f)]
        private float rampIntensity = 1.0f;

        [SerializeField]
        private AnimationCurve rampCurve = AnimationCurve.Linear(0.0f, 0.0f, 1.0f, 1.0f);

        [SerializeField, Min(0.0f)]
        private float rampAttack;

        [SerializeField, Min(0.0f)]
        private float rampRelease;

        public HoBlinkMeshScope MeshScope => meshScope;

        public int MeshIndex => meshIndex;

        public string KeyName => keyName;

        public HoBlinkSide Side => side;

        public HoBlinkBlendMode BlendMode => blendMode;

        public float Weight => weight;

        /// <summary>归一化增益：1.0 约等于满量程（100 键值）。</summary>
        public float Gain => gain;

        /// <summary>归一化偏置：0.5 约等于 50 键值。</summary>
        public float Offset => offset;

        public float OutputMin => outputMin;

        public float OutputMax => outputMax;

        public bool ClampToRange => clampToRange;

        public HoBlinkRampPreset RampPreset => rampPreset;

        public float RampIntensity => rampIntensity;

        public AnimationCurve RampCurve => rampCurve;

        public float RampAttack => rampAttack;

        public float RampRelease => rampRelease;

        public void Sanitize()
        {
            meshIndex = Mathf.Max(0, meshIndex);
            weight = Mathf.Clamp01(weight);
            rampIntensity = Mathf.Clamp(rampIntensity, 0.0f, 2.0f);
            rampAttack = Mathf.Max(0.0f, rampAttack);
            rampRelease = Mathf.Max(0.0f, rampRelease);
            if (rampCurve == null || rampCurve.length == 0)
            {
                rampCurve = AnimationCurve.Linear(0.0f, 0.0f, 1.0f, 1.0f);
            }
        }
    }

    /// <summary>以"读哪个键"为主体的一条映射规则。</summary>
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
        private List<HoBlinkTarget> targets = new List<HoBlinkTarget>();

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

        public List<HoBlinkTarget> Targets => targets;

        public void Sanitize()
        {
            frequency = Mathf.Clamp(frequency, 0.1f, 20.0f);
            dampingRatio = Mathf.Clamp(dampingRatio, 0.05f, 2.0f);
            inputSmoothing = Mathf.Clamp(inputSmoothing, 0.0f, 0.5f);
            maxStep = Mathf.Clamp(maxStep, 0.002f, 0.05f);
            if (targets == null)
            {
                targets = new List<HoBlinkTarget>();
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
