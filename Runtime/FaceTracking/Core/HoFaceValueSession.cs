using System;
using System.Collections.Generic;

namespace Hollow.HoUnityTools.FaceTracking.Core
{
    /// <summary>Host-independent, explicit-clock value session with per-session expression state.</summary>
    public sealed class HoFaceValueSession
    {
        private readonly HoFaceMiddleware profile;
        private HoVtsRuleEvaluator evaluator;
        private HoFaceRandomSource random;
        private double lastTime = -1;
        private int frame;
        private readonly uint seed;
        public HoVtsRuleEvaluator Evaluator => evaluator;
        public IReadOnlyDictionary<string, float> Values => evaluator.Parameters;
        public HoFaceValueSession(HoFaceMiddleware profile, uint seed = 1)
        {
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            this.seed = seed;
            Reset();
        }
        public void Reset()
        {
            random = new HoFaceRandomSource(seed);
            evaluator = new HoVtsRuleEvaluator(profile) { RandomRange = random.Range };
            lastTime = -1; frame = 0;
        }
        public void Evaluate(Dictionary<string, float> raw, double time)
        {
            if (double.IsNaN(time) || double.IsInfinity(time) || time < 0 || time < lastTime)
                throw new ArgumentOutOfRangeException(nameof(time), "Use a finite, monotonic session clock.");
            if (time == lastTime) return;
            float delta = lastTime < 0 ? 0 : (float)(time - lastTime);
            evaluator.FrameIndex = frame++;
            evaluator.Evaluate(raw, delta, time);
            lastTime = time;
        }
    }

    public static class HoFaceValueDocument
    {
        public const string Format = "ho-face-value";
        public const int Version = 1;
        public static string Write(HoFaceMiddleware profile) => HoFaceProfileJson.Write(profile, Format, Version);
        public static bool TryParse(string json, out HoFaceMiddleware profile, out string error) =>
            HoFaceProfileJson.TryParse(json, out profile, out error);
    }
}
