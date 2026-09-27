// 最小 UnityEngine 桩：只为了让 Runtime/FaceTracking/HoFaceExpression.cs 能脱离 Unity 编译运行。
// 语义照 Unity 的 Mathf（Clamp 下限大于上限时按顺序夹一次、Lerp 不截 t 等），见各处注释。
using System;

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
        public static float Sign(float v) => v >= 0f ? 1f : -1f;   // Unity: 0 也返回 1
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);   // min > max 时按顺序夹一次
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;                             // Unity 不截 t（但 Clamp01 会）
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }

    public static class Random
    {
        private static readonly System.Random Rng = new System.Random(12345);
        public static float Range(float min, float max) => min + (float)Rng.NextDouble() * (max - min);
    }
}
