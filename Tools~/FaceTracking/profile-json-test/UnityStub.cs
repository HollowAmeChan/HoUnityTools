// Minimal UnityEngine stub so Runtime/FaceTracking/*.cs can compile and RUN outside Unity.
//
// Everything here mirrors Unity's semantics as closely as the code under test needs:
//   * Mathf  -- same behaviours as the real one (see the comments in expression-coverage/UnityStub.cs)
//   * AnimationCurve / Keyframe -- cubic Hermite between keys, which is what Unity does.
//     Unity linearly extrapolates outside the key range; we clamp instead. That is fine here
//     because HoFaceCurve.Transfer() clamps x into [firstKey, lastKey] before evaluating,
//     so the extrapolation branch is unreachable from the code under test.
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public static class Mathf
    {
        public const float PI = (float)Math.PI;
        public static float Abs(float v) => Math.Abs(v);
        public static float Acos(float v) => (float)Math.Acos(v);
        public static float Asin(float v) => (float)Math.Asin(v);
        public static float Atan(float v) => (float)Math.Atan(v);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Ceil(float v) => (float)Math.Ceiling(v);
        public static float Floor(float v) => (float)Math.Floor(v);
        public static float Round(float v) => (float)Math.Round(v, MidpointRounding.ToEven);
        public static float Cos(float v) => (float)Math.Cos(v);
        public static float Sin(float v) => (float)Math.Sin(v);
        public static float Tan(float v) => (float)Math.Tan(v);
        public static float Exp(float v) => (float)Math.Exp(v);
        public static float Log(float v) => (float)Math.Log(v);
        public static float Log10(float v) => (float)Math.Log10(v);
        public static float Pow(float a, float b) => (float)Math.Pow(a, b);
        public static float Sqrt(float v) => (float)Math.Sqrt(v);
        public static float Sign(float v) => v >= 0f ? 1f : -1f;   // Unity: 0 also returns 1
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }

    public static class Random
    {
        private static readonly System.Random Rng = new System.Random(12345);
        public static float Range(float min, float max) => min + (float)Rng.NextDouble() * (max - min);
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class TooltipAttribute : Attribute
    {
        public TooltipAttribute(string tooltip) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class InspectorNameAttribute : Attribute
    {
        public InspectorNameAttribute(string displayName) { }
    }

    /// <summary>Unity 的关键点：时间/值/两侧切线。</summary>
    public struct Keyframe
    {
        public float time;
        public float value;
        public float inTangent;
        public float outTangent;

        public Keyframe(float time, float value) : this(time, value, 0f, 0f) { }

        public Keyframe(float time, float value, float inTangent, float outTangent)
        {
            this.time = time;
            this.value = value;
            this.inTangent = inTangent;
            this.outTangent = outTangent;
        }
    }

    /// <summary>
    /// 关键点之间的**三次 Hermite**（Unity 的做法）。范围之外我们夹住端点 ——
    /// 被测代码在求值前会把 x 夹进关键点范围，所以外推那条分支走不到。
    /// </summary>
    public class AnimationCurve
    {
        private readonly Keyframe[] _keys;

        public AnimationCurve(params Keyframe[] keys)
        {
            _keys = keys ?? new Keyframe[0];
        }

        public static AnimationCurve Linear(float timeStart, float valueStart, float timeEnd, float valueEnd)
        {
            float dt = timeEnd - timeStart;
            float slope = dt == 0f ? 0f : (valueEnd - valueStart) / dt;
            return new AnimationCurve(
                new Keyframe(timeStart, valueStart, slope, slope),
                new Keyframe(timeEnd, valueEnd, slope, slope));
        }

        public Keyframe[] keys { get { return _keys; } }

        public int length { get { return _keys.Length; } }

        public float Evaluate(float time)
        {
            if (_keys.Length == 0) return 0f;
            if (_keys.Length == 1) return _keys[0].value;
            if (time <= _keys[0].time) return _keys[0].value;
            if (time >= _keys[_keys.Length - 1].time) return _keys[_keys.Length - 1].value;

            for (int i = 0; i < _keys.Length - 1; i++)
            {
                Keyframe a = _keys[i];
                Keyframe b = _keys[i + 1];
                if (time < a.time || time > b.time) continue;

                float dt = b.time - a.time;
                if (dt <= 0f) return b.value;

                float t = (time - a.time) / dt;
                float t2 = t * t;
                float t3 = t2 * t;
                float h00 = 2f * t3 - 3f * t2 + 1f;
                float h10 = t3 - 2f * t2 + t;
                float h01 = -2f * t3 + 3f * t2;
                float h11 = t3 - t2;
                return h00 * a.value + h10 * dt * a.outTangent + h01 * b.value + h11 * dt * b.inTangent;
            }

            return _keys[_keys.Length - 1].value;
        }
    }
}
