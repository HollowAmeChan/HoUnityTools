using UnityEngine;

namespace Hollow.HoUnityTools.Constraints
{
    /// <summary>
    /// 目标级 ramp 预设的实现：只做"幅度形状 + 一阶时间包络"。
    /// 动态超调交给调用方的弹簧（眨眼是规则级果冻，注视是头部/眼睛的平滑）。
    /// </summary>
    public static class HoShapeKeyRampPresets
    {
        private const float MinIntensity = 0.001f;
        private const float MaxTimeConstant = 2.0f;

        /// <summary>幅度形状。预设对双极驱动按奇函数处理（左右/上下对称）。</summary>
        public static float Shape(HoShapeKeyRampPreset preset, float intensity, AnimationCurve curve, float x)
        {
            switch (preset)
            {
                case HoShapeKeyRampPreset.Direct:
                    return x * intensity;

                case HoShapeKeyRampPreset.Amplify:
                    return x * (1.0f + 0.25f * intensity);

                case HoShapeKeyRampPreset.EaseIn:
                {
                    float magnitude = Mathf.Abs(x);
                    return Mathf.Sign(x) * magnitude * magnitude;
                }

                case HoShapeKeyRampPreset.Step:
                    if (x > 0.0f)
                    {
                        return 1.0f;
                    }

                    return x < 0.0f ? -1.0f : 0.0f;

                case HoShapeKeyRampPreset.Custom:
                {
                    if (curve == null || curve.length == 0)
                    {
                        return x;
                    }

                    float magnitude = Mathf.Abs(x);
                    return Mathf.Sign(x) * curve.Evaluate(magnitude);
                }

                case HoShapeKeyRampPreset.SoftFollow:
                case HoShapeKeyRampPreset.SlowRelease:
                default:
                    return x;
            }
        }

        /// <summary>一阶时间包络的时间常数（秒）。0 表示立即到位。</summary>
        public static void Times(
            HoShapeKeyRampPreset preset,
            float intensity,
            float customAttack,
            float customRelease,
            out float attack,
            out float release)
        {
            switch (preset)
            {
                case HoShapeKeyRampPreset.SoftFollow:
                    attack = Scale(0.06f, intensity);
                    release = Scale(0.12f, intensity);
                    break;

                case HoShapeKeyRampPreset.EaseIn:
                    attack = Scale(0.12f, intensity);
                    release = Scale(0.20f, intensity);
                    break;

                case HoShapeKeyRampPreset.SlowRelease:
                    attack = Scale(0.04f, intensity);
                    release = Scale(0.40f, intensity);
                    break;

                case HoShapeKeyRampPreset.Custom:
                    attack = Mathf.Clamp(customAttack, 0.0f, MaxTimeConstant);
                    release = Mathf.Clamp(customRelease, 0.0f, MaxTimeConstant);
                    break;

                case HoShapeKeyRampPreset.Direct:
                case HoShapeKeyRampPreset.Amplify:
                case HoShapeKeyRampPreset.Step:
                default:
                    attack = 0.0f;
                    release = 0.0f;
                    break;
            }
        }

        /// <summary>一阶包络：时间常数 0 表示直通。</summary>
        public static float Envelope(float current, float target, float deltaTime, float attack, float release)
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
