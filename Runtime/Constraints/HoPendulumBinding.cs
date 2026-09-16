using System;
using UnityEngine;
using UnityEngine.Events;

namespace Hollow.HoUnityTools.Constraints
{
    /// <summary>摆锤约束可以输出的参数通道。</summary>
    public enum HoPendulumChannel
    {
        /// <summary>液面沿本地 X 轴方向的斜率（tan 值），可直接喂给液面 shader。</summary>
        TiltX,

        /// <summary>液面沿本地 Z 轴方向的斜率（tan 值），可直接喂给液面 shader。</summary>
        TiltZ,

        /// <summary>两个轴的斜率合成向量 (TiltX, 0, TiltZ)。</summary>
        Tilt,

        /// <summary>沿本地 X 轴的倾斜角（度）。</summary>
        AngleX,

        /// <summary>沿本地 Z 轴的倾斜角（度）。</summary>
        AngleZ,

        /// <summary>合倾斜角（度）。</summary>
        Angle,

        /// <summary>把局部 up 转到当前液面法线所需的欧拉角 (AngleZ, 0, -AngleX)，单位为度，可直接驱动子物体旋转。</summary>
        TiltEuler,

        /// <summary>摆锤末端相对锚点的世界方向（单位向量）。</summary>
        Swing,

        /// <summary>摆锤末端相对锚点的偏移（锚点本地坐标系，已含径向伸长）。</summary>
        BobOffset,

        /// <summary>摆锤末端世界坐标（已含径向伸长）。</summary>
        BobPosition,

        /// <summary>相对平衡位置的振荡幅度 0~1。</summary>
        Amplitude,

        /// <summary>累加相位（弧度 0~2π）。</summary>
        Phase,

        /// <summary>归一化读数 0~1：手动输入模式下是输入值映射，其余模式是倾斜角映射。</summary>
        Normalized,

        /// <summary>有效重力相对参考重力的倍率（米/秒² 与 g 的比值）。静止为 1。</summary>
        EffectiveGravity,

        /// <summary>径向弹簧伸长量（米）。静止为 0，过载为正，失重为负。</summary>
        Stretch,

        /// <summary>锚点线速度大小（m/s）。</summary>
        AnchorSpeed,

        /// <summary>锚点角速度大小（度/秒）。</summary>
        AnchorAngularSpeed,

        /// <summary>锚点世界加速度。</summary>
        AnchorAcceleration
    }

    /// <summary>绑定目标类型。</summary>
    public enum HoPendulumBindingTarget
    {
        /// <summary>渲染器材质属性，通过 MaterialPropertyBlock 写入，不污染材质资产。</summary>
        RendererProperty,

        /// <summary>Shader 全局 float。</summary>
        ShaderGlobal,

        /// <summary>UnityEvent 回调。</summary>
        Event,

        /// <summary>驱动另一个 Transform 的本地旋转或本地位置。</summary>
        Transform
    }

    /// <summary>Transform 绑定的驱动方式。</summary>
    public enum HoPendulumTransformMode
    {
        /// <summary>按欧拉角旋转（输入为度）。</summary>
        Rotation,

        /// <summary>按偏移量平移（输入为米）。</summary>
        Position
    }

    [Serializable]
    public sealed class HoPendulumFloatEvent : UnityEvent<float>
    {
    }

    [Serializable]
    public sealed class HoPendulumVector3Event : UnityEvent<Vector3>
    {
    }

    /// <summary>通道语义工具。</summary>
    public static class HoPendulumChannels
    {
        /// <summary>该通道是否为向量通道。向量通道走 Vector3 事件与 Transform 绑定。</summary>
        public static bool IsVector(HoPendulumChannel channel)
        {
            switch (channel)
            {
                case HoPendulumChannel.Tilt:
                case HoPendulumChannel.TiltEuler:
                case HoPendulumChannel.Swing:
                case HoPendulumChannel.BobOffset:
                case HoPendulumChannel.BobPosition:
                case HoPendulumChannel.AnchorAcceleration:
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// 一条输出绑定：把某个通道的值（先乘缩放再加偏置）写到材质属性、Shader 全局、UnityEvent 或另一个 Transform。
    /// </summary>
    [Serializable]
    public sealed class HoPendulumBinding
    {
        [SerializeField]
        private bool enabled = true;

        [SerializeField]
        private string label = string.Empty;

        [SerializeField]
        private HoPendulumChannel channel = HoPendulumChannel.TiltX;

        [SerializeField]
        private HoPendulumBindingTarget target = HoPendulumBindingTarget.RendererProperty;

        [SerializeField]
        private float scale = 1.0f;

        [SerializeField]
        private Vector3 bias = Vector3.zero;

        [Header("Renderer Property")]
        [SerializeField]
        private Renderer[] renderers;

        [SerializeField]
        private bool includeChildRenderers = true;

        [SerializeField]
        private string propertyName = "_LiquidTiltX";

        [Header("Shader Global")]
        [SerializeField]
        private string globalName = "_HoPendulumTiltX";

        [Header("Event")]
        [SerializeField]
        private HoPendulumFloatEvent floatEvent = new HoPendulumFloatEvent();

        [SerializeField]
        private HoPendulumVector3Event vectorEvent = new HoPendulumVector3Event();

        [Header("Transform")]
        [SerializeField]
        private Transform transformTarget;

        [SerializeField]
        private HoPendulumTransformMode transformMode = HoPendulumTransformMode.Rotation;

        [SerializeField]
        private Vector3 transformWeight = Vector3.one;

        [NonSerialized]
        private Renderer[] resolvedRenderers;

        [NonSerialized]
        private Transform resolvedOwner;

        [NonSerialized]
        private Transform cachedTransformTarget;

        [NonSerialized]
        private Vector3 cachedLocalPosition;

        [NonSerialized]
        private Quaternion cachedLocalRotation = Quaternion.identity;

        [NonSerialized]
        private bool hasCachedPose;

        [NonSerialized]
        private bool warnedSelfDrive;

        public bool Enabled => enabled;

        public string Label => label;

        public HoPendulumChannel Channel => channel;

        public HoPendulumBindingTarget Target => target;

        public float Scale => scale;

        public Vector3 Bias => bias;

        public string PropertyName => propertyName;

        public string GlobalName => globalName;

        public Transform TransformTarget => transformTarget;

        public HoPendulumTransformMode TransformMode => transformMode;

        public bool IsVectorChannel => HoPendulumChannels.IsVector(channel);

        /// <summary>按缩放与偏置换算通道原始值。</summary>
        public Vector3 ApplyScaleAndBias(Vector3 value)
        {
            return value * scale + bias;
        }

        /// <summary>清空运行时缓存。绑定被重新配置或约束重置时调用。</summary>
        public void ResetRuntimeCache()
        {
            resolvedRenderers = null;
            resolvedOwner = null;
            cachedTransformTarget = null;
            hasCachedPose = false;
            warnedSelfDrive = false;
        }

        /// <summary>解析实际要写入的渲染器列表，结果会被缓存。</summary>
        public Renderer[] ResolveRenderers(Transform owner)
        {
            if (resolvedRenderers != null && resolvedOwner == owner)
            {
                return resolvedRenderers;
            }

            resolvedOwner = owner;
            if (renderers != null && renderers.Length > 0)
            {
                int count = 0;
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] != null)
                    {
                        count++;
                    }
                }

                Renderer[] explicitRenderers = new Renderer[count];
                int index = 0;
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] != null)
                    {
                        explicitRenderers[index] = renderers[i];
                        index++;
                    }
                }

                resolvedRenderers = explicitRenderers;
                return resolvedRenderers;
            }

            if (owner == null)
            {
                resolvedRenderers = new Renderer[0];
                return resolvedRenderers;
            }

            if (includeChildRenderers)
            {
                resolvedRenderers = owner.GetComponentsInChildren<Renderer>(true);
                return resolvedRenderers;
            }

            Renderer self = owner.GetComponent<Renderer>();
            resolvedRenderers = self != null ? new[] { self } : new Renderer[0];
            return resolvedRenderers;
        }

        public void ApplyToPropertyBlock(MaterialPropertyBlock block, Vector3 value)
        {
            if (string.IsNullOrEmpty(propertyName))
            {
                return;
            }

            block.SetFloat(propertyName, value.x);
        }

        public void ApplyToGlobal(Vector3 value)
        {
            if (string.IsNullOrEmpty(globalName))
            {
                return;
            }

            Shader.SetGlobalFloat(globalName, value.x);
        }

        public void ApplyToEvent(Vector3 value)
        {
            if (IsVectorChannel)
            {
                vectorEvent.Invoke(value);
            }
            else
            {
                floatEvent.Invoke(value.x);
            }
        }

        public void ApplyToTransform(Transform driveTransform, Vector3 value)
        {
            Transform targetTransform = transformTarget;
            if (targetTransform == null)
            {
                return;
            }

            if (driveTransform != null && targetTransform == driveTransform)
            {
                if (!warnedSelfDrive)
                {
                    warnedSelfDrive = true;
                    Debug.LogWarning(
                        "[HoUnityTools] 摆锤约束的 Transform 绑定指向了驱动源自身，会造成自反馈，已跳过：" +
                        targetTransform.name,
                        targetTransform);
                }

                return;
            }

            if (!hasCachedPose || cachedTransformTarget != targetTransform)
            {
                cachedTransformTarget = targetTransform;
                cachedLocalPosition = targetTransform.localPosition;
                cachedLocalRotation = targetTransform.localRotation;
                hasCachedPose = true;
            }

            Vector3 weighted = Vector3.Scale(value, transformWeight);
            if (transformMode == HoPendulumTransformMode.Rotation)
            {
                targetTransform.localRotation = cachedLocalRotation * Quaternion.Euler(weighted);
            }
            else
            {
                targetTransform.localPosition = cachedLocalPosition + weighted;
            }
        }

        /// <summary>编辑器与预设使用：改写成渲染器属性绑定。</summary>
        public void ConfigureRendererProperty(
            HoPendulumChannel newChannel,
            string newPropertyName,
            float newScale,
            Vector3 newBias,
            string newLabel)
        {
            channel = newChannel;
            target = HoPendulumBindingTarget.RendererProperty;
            propertyName = newPropertyName;
            scale = newScale;
            bias = newBias;
            label = newLabel;
            enabled = true;
            renderers = null;
            includeChildRenderers = true;
            ResetRuntimeCache();
        }
    }
}
