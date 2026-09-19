using System;
using UnityEngine;

namespace Hollow.HoUnityTools.Constraints
{
    /// <summary>
    /// 自动眨眼：单计时器状态机（等待 → 闭合 → 保持 → 张开），一个输出值。
    /// 不做左右眼独立随机；需要 wink 时由组件用显式通道值覆盖。
    /// </summary>
    public static class HoBlinkGenerator
    {
        /// <summary>超过这个帧间隔就认为刚发生过暂停/卡帧，直接回到等待并重抽间隔。</summary>
        public const float HugeDeltaTime = 1.0f;

        private const int MaxPhasesPerFrame = 8;
        private const float Epsilon = 1e-6f;

        public static void Step(
            ref HoBlinkState state,
            in HoBlinkTiming timing,
            float deltaTime,
            AnimationCurve curve,
            System.Random random)
        {
            if (deltaTime <= 0.0f)
            {
                return;
            }

            if (state.pauseRemaining > 0.0f)
            {
                state.pauseRemaining = Mathf.Max(0.0f, state.pauseRemaining - deltaTime);
                state.phase = HoBlinkPhase.Wait;
                state.timer = 0.0f;
                state.value = 0.0f;
                state.pendingDoubleBlink = false;
                return;
            }

            if (deltaTime > HugeDeltaTime)
            {
                state.phase = HoBlinkPhase.Wait;
                state.timer = 0.0f;
                state.value = 0.0f;
                state.pendingDoubleBlink = false;
                return;
            }

            float remaining = deltaTime;
            int guard = 0;
            while (remaining > Epsilon && guard++ < MaxPhasesPerFrame)
            {
                switch (state.phase)
                {
                    case HoBlinkPhase.Wait:
                    {
                        if (state.timer <= 0.0f)
                        {
                            state.timer = SampleInterval(timing, random);
                        }

                        float step = Mathf.Min(state.timer, remaining);
                        state.timer -= step;
                        remaining -= step;
                        state.value = 0.0f;
                        if (state.timer <= 0.0f)
                        {
                            state.phase = HoBlinkPhase.Closing;
                            state.timer = timing.closeDuration;
                        }

                        break;
                    }

                    case HoBlinkPhase.Closing:
                    {
                        float step = Mathf.Min(state.timer, remaining);
                        state.timer -= step;
                        remaining -= step;
                        float t = timing.closeDuration > 0.0f ? 1.0f - state.timer / timing.closeDuration : 1.0f;
                        state.value = CurveValue(curve, Mathf.Clamp01(t));
                        if (state.timer <= 0.0f)
                        {
                            state.value = 1.0f;
                            state.phase = HoBlinkPhase.Hold;
                            state.timer = timing.holdDuration;
                        }

                        break;
                    }

                    case HoBlinkPhase.Hold:
                    {
                        float step = Mathf.Min(state.timer, remaining);
                        state.timer -= step;
                        remaining -= step;
                        state.value = 1.0f;
                        if (state.timer <= 0.0f)
                        {
                            state.phase = HoBlinkPhase.Opening;
                            state.timer = timing.openDuration;
                        }

                        break;
                    }

                    default:
                    {
                        float step = Mathf.Min(state.timer, remaining);
                        state.timer -= step;
                        remaining -= step;
                        float t = timing.openDuration > 0.0f ? 1.0f - state.timer / timing.openDuration : 1.0f;
                        state.value = 1.0f - CurveValue(curve, Mathf.Clamp01(t));
                        if (state.timer <= 0.0f)
                        {
                            state.value = 0.0f;
                            state.phase = HoBlinkPhase.Wait;
                            if (state.pendingDoubleBlink)
                            {
                                state.pendingDoubleBlink = false;
                                state.timer = timing.doubleBlinkGap;
                            }
                            else if (random != null && random.NextDouble() < timing.doubleBlinkChance)
                            {
                                state.timer = timing.doubleBlinkGap;
                            }
                            else
                            {
                                state.timer = SampleInterval(timing, random);
                            }
                        }

                        break;
                    }
                }
            }
        }

        /// <summary>立刻眨一次（走完整曲线）。</summary>
        public static void Request(ref HoBlinkState state, in HoBlinkTiming timing)
        {
            state.phase = HoBlinkPhase.Closing;
            state.timer = timing.closeDuration;
            state.value = 0.0f;
            state.pendingDoubleBlink = false;
        }

        public static float SampleInterval(in HoBlinkTiming timing, System.Random random)
        {
            float u = random != null ? (float)random.NextDouble() : 0.5f;
            if (timing.distribution == HoBlinkIntervalDistribution.Uniform)
            {
                return Mathf.Lerp(timing.intervalMin, timing.intervalMax, u);
            }

            float mean = (timing.intervalMin + timing.intervalMax) * 0.5f;
            float exponential = -mean * Mathf.Log(1.0f - Mathf.Clamp(u, 0.0f, 0.9999f));
            return Mathf.Clamp(timing.intervalMin + exponential, timing.intervalMin, timing.intervalMax);
        }

        /// <summary>眨眼曲线：横轴为相位，纵轴为闭合量；空曲线时退化为线性。</summary>
        public static float CurveValue(AnimationCurve curve, float t)
        {
            if (curve == null || curve.length == 0)
            {
                return t;
            }

            return Mathf.Clamp01(curve.Evaluate(t));
        }
    }

    /// <summary>果冻眼：驱动值上的二阶弹簧-阻尼（半隐式欧拉 + 子步），带一阶输入平滑。</summary>
    public static class HoJellySolver
    {
        public static void Reset(ref HoJellyState state, float value)
        {
            state.smoothed = value;
            state.value = value;
            state.velocity = 0.0f;
        }

        public static void Step(
            ref HoJellyState state,
            float target,
            float deltaTime,
            float frequency,
            float dampingRatio,
            float inputSmoothing,
            float maxStep)
        {
            if (deltaTime <= 0.0f)
            {
                state.smoothed = target;
                state.value = target;
                state.velocity = 0.0f;
                return;
            }

            state.smoothed = inputSmoothing > 0.0f
                ? SmoothExp(state.smoothed, target, deltaTime, inputSmoothing)
                : target;

            float safeFrequency = Mathf.Max(0.01f, frequency);
            float omega = 2.0f * Mathf.PI * safeFrequency;

            // 子步上限同时受 maxStep 与频率约束，保证半隐式欧拉稳定（ωh ≲ 0.8）
            float effectiveMaxStep = Mathf.Max(0.0005f, Mathf.Min(maxStep, 1.0f / (8.0f * safeFrequency)));
            int steps = Mathf.Clamp(Mathf.CeilToInt(deltaTime / effectiveMaxStep), 1, 64);
            float h = deltaTime / steps;

            for (int i = 0; i < steps; i++)
            {
                float acceleration = omega * omega * (state.smoothed - state.value)
                                     - 2.0f * dampingRatio * omega * state.velocity;
                state.velocity += acceleration * h;
                state.value += state.velocity * h;
            }
        }

        /// <summary>一阶低通：时间常数 0 表示直通。</summary>
        public static float SmoothExp(float current, float target, float deltaTime, float timeConstant)
        {
            if (timeConstant <= 0.0f || deltaTime <= 0.0f)
            {
                return target;
            }

            float t = 1.0f - Mathf.Exp(-deltaTime / timeConstant);
            return Mathf.Lerp(current, target, t);
        }
    }
}
