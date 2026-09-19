using UnityEngine;

namespace Hollow.HoUnityTools.Constraints
{
    /// <summary>
    /// 目标级 ramp 预设的实现。动态超调只由规则级弹簧负责，
    /// 这里只做"幅度形状 + 一阶时间包络"，与设计文档一致。
    /// </summary>
    public static class HoBlinkRampPresets
    {
        private const float MinIntensity = 0.001f;
        private const float MaxTimeConstant = 2.0f;

        /// <summary>幅度形状。预设对双极驱动按奇函数处理（左右/上下对称）。</summary>
        public static float Shape(HoBlinkRampPreset preset, float intensity, AnimationCurve curve, float x)
        {
            switch (preset)
            {
                case HoBlinkRampPreset.Direct:
                    return x * intensity;

                case HoBlinkRampPreset.Amplify:
                    return x * (1.0f + 0.25f * intensity);

                case HoBlinkRampPreset.EaseIn:
                {
                    float magnitude = Mathf.Abs(x);
                    return Mathf.Sign(x) * magnitude * magnitude;
                }

                case HoBlinkRampPreset.Step:
                    if (x > 0.0f)
                    {
                        return 1.0f;
                    }

                    return x < 0.0f ? -1.0f : 0.0f;

                case HoBlinkRampPreset.Custom:
                {
                    if (curve == null || curve.length == 0)
                    {
                        return x;
                    }

                    float magnitude = Mathf.Abs(x);
                    return Mathf.Sign(x) * curve.Evaluate(magnitude);
                }

                case HoBlinkRampPreset.SoftFollow:
                case HoBlinkRampPreset.SlowRelease:
                default:
                    return x;
            }
        }

        /// <summary>一阶时间包络的时间常数（秒）。0 表示立即到位。</summary>
        public static void Times(
            HoBlinkRampPreset preset,
            float intensity,
            float customAttack,
            float customRelease,
            out float attack,
            out float release)
        {
            switch (preset)
            {
                case HoBlinkRampPreset.SoftFollow:
                    attack = Scale(0.06f, intensity);
                    release = Scale(0.12f, intensity);
                    break;

                case HoBlinkRampPreset.EaseIn:
                    attack = Scale(0.12f, intensity);
                    release = Scale(0.20f, intensity);
                    break;

                case HoBlinkRampPreset.SlowRelease:
                    attack = Scale(0.04f, intensity);
                    release = Scale(0.40f, intensity);
                    break;

                case HoBlinkRampPreset.Custom:
                    attack = Mathf.Clamp(customAttack, 0.0f, MaxTimeConstant);
                    release = Mathf.Clamp(customRelease, 0.0f, MaxTimeConstant);
                    break;

                case HoBlinkRampPreset.Direct:
                case HoBlinkRampPreset.Amplify:
                case HoBlinkRampPreset.Step:
                default:
                    attack = 0.0f;
                    release = 0.0f;
                    break;
            }
        }

        /// <summary>按预设语义缩放时间常数：强度越大越快（时间常数 ÷ 强度）。</summary>
        private static float Scale(float seconds, float intensity)
        {
            if (intensity <= MinIntensity)
            {
                return 0.0f;
            }

            return Mathf.Clamp(seconds / intensity, 0.0f, MaxTimeConstant);
        }
    }
}
