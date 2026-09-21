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

    /// <summary>
    /// 规则读什么信号。**面板上不给切换**：规则是哪种由"谁创建了它"决定（预设 / ＋规则 菜单），
    /// UI 只显示这个信号真正需要的字段。
    /// </summary>
    public enum HoBlinkDriverKind
    {
        /// <summary>读形态键（凝视 / 眨眼键…）。跟随时长什么样由弹簧决定，永远带回弹。</summary>
        [InspectorName("键")]
        ShapeKey,

        /// <summary>自动眨眼本帧的闭眼量（0..1）。旧数据兼容用，预设不再生成。</summary>
        [InspectorName("闭眼量")]
        AutoBlink,

        /// <summary>调试滑杆。</summary>
        [InspectorName("手动")]
        Manual,

        /// <summary>眼皮动的速度（0..1）。旧数据兼容用。</summary>
        [InspectorName("眼皮速度")]
        BlinkSpeed,

        /// <summary>
        /// 眨眼加速度（0..1，已按曲线曲率与闭合时长归一化）。
        /// 眨眼那一下的加速度是几毫秒宽的尖峰，弹簧被它踢一下、在眼皮停住的那段时间里自己振 —— 果冻感的来源。
        /// </summary>
        [InspectorName("眨眼")]
        BlinkAccel
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

        /// <summary>
        /// 驱动灵敏度（只有「眨眼」这类自己算出来的信号用得上；形态键驱动用目标的增益就够）。
        /// 「眨眼追踪」的"强度"就是这个。
        /// </summary>
        [SerializeField, Range(0.0f, 4.0f)]
        private float driverGain = 1.0f;

        /// <summary>
        /// 眨眼加速度的包络回落时长（秒）：加速度尖峰只有几毫秒宽，直接喂弹簧冲量太小、
        /// 参数会变得极难调，所以先让它瞬间起峰、再按这个时长衰减成一个"小鼓包"。
        /// 只有「眨眼」模式用得上。
        /// </summary>
        [SerializeField, Range(0.005f, 0.2f)]
        private float blinkEnvelope = 0.025f;

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
            set => jellyEnabled = value;        }

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

        /// <summary>驱动灵敏度（眨眼模式的"强度"）。</summary>
        public float DriverGain
        {
            get => driverGain;
            set => driverGain = value;
        }

        /// <summary>眨眼加速度包络的回落时长（秒）。</summary>
        public float BlinkEnvelope
        {
            get => blinkEnvelope;
            set => blinkEnvelope = value;
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
            driverGain = Mathf.Clamp(driverGain, 0.0f, 4.0f);
            blinkEnvelope = Mathf.Clamp(blinkEnvelope, 0.005f, 0.2f);
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
