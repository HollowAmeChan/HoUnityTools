using System;

namespace Hollow.HoUnityTools.FaceTracking.Core
{
    /// <summary>Scalar math for the non-Unity host. Algorithms remain shared source.</summary>
    public static class HoFacePortableMath
    {
        public static float Abs(float x) => Math.Abs(x);
        public static float Acos(float x) => (float)Math.Acos(x);
        public static float Asin(float x) => (float)Math.Asin(x);
        public static float Atan(float x) => (float)Math.Atan(x);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Ceil(float x) => (float)Math.Ceiling(x);
        public static float Floor(float x) => (float)Math.Floor(x);
        public static float Round(float x) => (float)Math.Round(x, MidpointRounding.ToEven);
        public static float Cos(float x) => (float)Math.Cos(x);
        public static float Sin(float x) => (float)Math.Sin(x);
        public static float Tan(float x) => (float)Math.Tan(x);
        public static float Exp(float x) => (float)Math.Exp(x);
        public static float Log(float x) => (float)Math.Log(x);
        public static float Log10(float x) => (float)Math.Log10(x);
        public static float Pow(float x, float y) => (float)Math.Pow(x, y);
        public static float Sqrt(float x) => (float)Math.Sqrt(x);
        public static float Sign(float x) => x >= 0 ? 1 : -1;
        public static float Min(float x, float y) => Math.Min(x, y);
        public static float Max(float x, float y) => Math.Max(x, y);
        public static float Clamp(float x, float min, float max) => x < min ? min : x > max ? max : x;
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp(t, 0, 1);
    }

    /// <summary>Owned by one evaluation session, so replay does not use global random state.</summary>
    public sealed class HoFaceRandomSource
    {
        private uint state;
        public HoFaceRandomSource(uint seed = 1) { state = seed == 0 ? 1u : seed; }
        public float Range(float min, float max)
        {
            uint x = state;
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            state = x;
            return min + (max - min) * ((x >> 8) / 16777216f);
        }
    }

#if HO_FACE_PORTABLE
    [AttributeUsage(AttributeTargets.Field)]
    internal sealed class HoFaceTooltipAttribute : Attribute
    {
        public HoFaceTooltipAttribute(string text) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    internal sealed class HoFaceInspectorNameAttribute : Attribute
    {
        public HoFaceInspectorNameAttribute(string text) { }
    }

    internal static class HoFacePortableRandom
    {
        private static readonly HoFaceRandomSource Source = new HoFaceRandomSource();
        public static float Range(float min, float max) => Source.Range(min, max);
    }
#endif
}
