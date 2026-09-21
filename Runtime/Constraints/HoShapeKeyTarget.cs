using System;
using UnityEngine;

namespace Hollow.HoUnityTools.Constraints
{
    public enum HoShapeKeyMeshScope
    {
        [InspectorName("全部")]
        All,

        [InspectorName("指定")]
        Index
    }

    public enum HoShapeKeyBlendMode
    {
        /// <summary>基准 + 映射结果（基准是"别人写的值"，不会把自己上一帧的写入当基准）。</summary>
        [InspectorName("叠加")]
        Additive,

        /// <summary>直接写映射结果（与基准按权重插值）。</summary>
        [InspectorName("覆盖")]
        Override
    }

    /// <summary>
    /// 目标级 ramp 预设。动态超调（甩过头再回来）由调用方的弹簧负责，
    /// ramp 只做幅度形状与一阶时间包络，避免同一路出现两个弹簧互相打架。
    /// </summary>
    public enum HoShapeKeyRampPreset
    {
        /// <summary>直接通过（y = x × 强度）。</summary>
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

    /// <summary>
    /// 一个形态键输出映射：写哪个网格的哪个键、怎么换算。
    /// 眨眼约束与注视约束共用（字段名保持不变，改名不丢序列化数据）。
    /// </summary>
    [Serializable]
    public sealed class HoShapeKeyTarget
    {
        [SerializeField]
        private HoShapeKeyMeshScope meshScope = HoShapeKeyMeshScope.All;

        [SerializeField]
        private int meshIndex;

        /// <summary>
        /// 这一路是干什么的（例如「高光 · 压扁」）。只给人和预设看：
        /// 面板上显示成一个标签，「按名字接目标键」按它决定去找哪种键。
        /// </summary>
        [SerializeField]
        private string label = string.Empty;

        [SerializeField]
        private string keyName = string.Empty;

        [SerializeField]
        private HoBlinkSide side = HoBlinkSide.Both;

        [SerializeField]
        private HoShapeKeyBlendMode blendMode = HoShapeKeyBlendMode.Additive;

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
        private HoShapeKeyRampPreset rampPreset = HoShapeKeyRampPreset.Direct;

        [SerializeField, Range(0.0f, 2.0f)]
        private float rampIntensity = 1.0f;

        [SerializeField]
        private AnimationCurve rampCurve = AnimationCurve.Linear(0.0f, 0.0f, 1.0f, 1.0f);

        [SerializeField, Min(0.0f)]
        private float rampAttack;

        [SerializeField, Min(0.0f)]
        private float rampRelease;

        public HoShapeKeyMeshScope MeshScope => meshScope;

        public int MeshIndex => meshIndex;

        /// <summary>这一路是干什么的（面板标签 / 按名字接键的角色）。</summary>
        public string Label => label;

        public string KeyName => keyName;

        /// <summary>自动眨眼的左右通道选择（注视约束不使用）。</summary>
        public HoBlinkSide Side => side;

        public HoShapeKeyBlendMode BlendMode => blendMode;

        public float Weight => weight;

        /// <summary>归一化增益：1.0 约等于满量程（100 键值）。</summary>
        public float Gain => gain;

        /// <summary>归一化偏置：0.5 约等于 50 键值。</summary>
        public float Offset => offset;

        public float OutputMin => outputMin;

        public float OutputMax => outputMax;

        public bool ClampToRange => clampToRange;

        public HoShapeKeyRampPreset RampPreset => rampPreset;

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

        /// <summary>
        /// 运行期构造（不序列化）。给"宿主只存键名 + 增益"的场合用 ——
        /// 例如注视约束的眼睛通道，避免每个通道都序列化 16 个字段。
        /// </summary>
        public static HoShapeKeyTarget CreateRuntime(string keyName, float gain, HoShapeKeyRampPreset rampPreset = HoShapeKeyRampPreset.Direct)
        {
            HoShapeKeyTarget target = new HoShapeKeyTarget
            {
                keyName = keyName ?? string.Empty,
                gain = gain,
                rampPreset = rampPreset,
                rampIntensity = 1.0f,
                blendMode = HoShapeKeyBlendMode.Additive,
                weight = 1.0f,
                meshScope = HoShapeKeyMeshScope.All,
                outputMin = 0.0f,
                outputMax = 100.0f,
                clampToRange = true
            };
            return target;
        }
    }
}
