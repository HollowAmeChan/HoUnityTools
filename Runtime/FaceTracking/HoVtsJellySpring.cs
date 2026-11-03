using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>
    /// A zero-rest overlay: spring(filtered input) - filtered input.
    /// Output clipping and gain never feed back into the spring's signed state.
    /// </summary>
    public struct HoVtsJellySpring
    {
        private HoFaceJellyState spring;
        private float filteredInput, lastInput;
        private bool initialized;

        public float Offset => initialized ? spring.value - filteredInput : 0f;
        public bool IsSettled => !initialized || (Mathf.Abs(Offset) < 0.00001f
            && Mathf.Abs(spring.velocity) < 0.0001f && Mathf.Abs(filteredInput - lastInput) < 0.00001f);

        public void Reset()
        {
            initialized = false;
            filteredInput = lastInput = 0;
            HoFaceJelly.Reset(ref spring);
        }

        public void Reset(float input)
        {
            filteredInput = lastInput = SafeInput(input);
            HoFaceJelly.Reset(ref spring, filteredInput);
            initialized = true;
        }

        public void Step(float input, float deltaTime, HoVtsJellyResponse response)
        {
            input = SafeInput(input);
            if (!initialized) { Reset(input); return; }
            if (!Finite(deltaTime) || deltaTime <= 0) return;
            // A pause, breakpoint or large time seek is not a physical impulse.
            if (deltaTime > 0.25f) { Reset(input); return; }
            lastInput = input;
            float frequency = Mathf.Clamp(Safe(response != null ? response.frequency : 6f, 6f), 0.1f, HoFaceJelly.MaxFrequency);
            float damping = Mathf.Clamp(Safe(response != null ? response.damping : 0.35f, 0.35f), 0.05f, 2f);
            float smoothing = Mathf.Clamp(Safe(response != null ? response.inputSmoothing : 0, 0), 0, 0.2f);
            // The existing solver is semi-implicit Euler. Finer steps also keep overdamped
            // responses stable at the exposed maximum damping and frequency.
            int count = Mathf.Max(1, Mathf.CeilToInt(deltaTime * frequency * 32f));
            float step = deltaTime / count;
            float alpha = smoothing > 0 ? 1f - Mathf.Exp(-step / smoothing) : 1f;
            for (int i = 0; i < count; i++)
            {
                filteredInput = Mathf.Lerp(filteredInput, input, alpha);
                HoFaceJelly.Step(ref spring, filteredInput, step, frequency, damping);
            }
            if (!Finite(spring.value) || !Finite(spring.velocity)) Reset(input);
            if (IsSettled) Reset(input);
        }

        /// <summary>Loss of tracking holds the last equilibrium instead of injecting a fake return gesture.</summary>
        public void Settle(float deltaTime, HoVtsJellyResponse response) => Step(lastInput, deltaTime, response);

        public float WeightOffset(HoVtsJellyResponse response)
        {
            if (response == null) return 0;
            float strength = Mathf.Clamp(Safe(response.strength, 0), 0, 2);
            float limit = Mathf.Clamp(Safe(response.maxAmplitude, 0), 0, 100);
            return Mathf.Clamp(Offset * 100f * strength, -limit, limit);
        }

        private static float SafeInput(float value) => Mathf.Clamp(Safe(value, 0), -10f, 10f);
        private static float Safe(float value, float fallback) => Finite(value) ? value : fallback;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
