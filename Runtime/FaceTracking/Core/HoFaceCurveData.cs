using System;

namespace Hollow.HoUnityTools.FaceTracking.Core
{
    [Serializable]
    public struct HoFaceCurveKey
    {
        public float time, value, inTangent, outTangent;
        public HoFaceCurveKey(float time, float value) : this(time, value, 0, 0) { }
        public HoFaceCurveKey(float time, float value, float inTangent, float outTangent)
        {
            this.time = time; this.value = value;
            this.inTangent = inTangent; this.outTangent = outTangent;
        }
    }

    /// <summary>
    /// Unweighted cubic Hermite keys used by the HoFace JSON format.
    /// The format does not store Unity weighted tangents or wrap modes.
    /// </summary>
    [Serializable]
    public sealed class HoFaceCurveData
    {
        private HoFaceCurveKey[] points;
        public HoFaceCurveData(params HoFaceCurveKey[] keys) { this.keys = keys; }
        public HoFaceCurveKey[] keys
        {
            get => (HoFaceCurveKey[])points.Clone();
            set
            {
                points = value == null ? new HoFaceCurveKey[0] : (HoFaceCurveKey[])value.Clone();
                Array.Sort(points, (a, b) => a.time.CompareTo(b.time));
            }
        }
        public int length => points.Length;
        public static HoFaceCurveData Linear(float t0, float v0, float t1, float v1)
        {
            float slope = t1 == t0 ? 0 : (v1 - v0) / (t1 - t0);
            return new HoFaceCurveData(new HoFaceCurveKey(t0, v0, slope, slope),
                new HoFaceCurveKey(t1, v1, slope, slope));
        }
        public float Evaluate(float time)
        {
            if (points.Length == 0) return 0;
            if (time <= points[0].time) return points[0].value;
            int last = points.Length - 1;
            if (time >= points[last].time) return points[last].value;
            int low = 0, high = last;
            while (high - low > 1)
            {
                int middle = (low + high) / 2;
                if (time < points[middle].time) high = middle; else low = middle;
            }
            HoFaceCurveKey a = points[low], b = points[high];
            if (float.IsInfinity(a.outTangent) || float.IsInfinity(b.inTangent)) return a.value;
            float span = b.time - a.time;
            if (span <= 0) return b.value;
            float t = (time - a.time) / span, t2 = t * t, t3 = t2 * t;
            return (2 * t3 - 3 * t2 + 1) * a.value + (t3 - 2 * t2 + t) * span * a.outTangent
                + (-2 * t3 + 3 * t2) * b.value + (t3 - t2) * span * b.inTangent;
        }
    }
}
