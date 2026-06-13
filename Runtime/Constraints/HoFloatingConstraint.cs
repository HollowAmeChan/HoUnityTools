using UnityEngine;

namespace Hollow.HoUnityTools.Constraints
{
    public enum HoFloatingConstraintUpdateMode
    {
        LateUpdate,
        Update,
        FixedUpdate,
        Manual
    }

    public enum HoFloatingConstraintSpace
    {
        World,
        Local
    }

    public enum HoFloatingConstraintWaveform
    {
        Sin,
        Triangle,
        Curve
    }

    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("HoUnityTools/约束/漂浮约束")]
    public sealed class HoFloatingConstraint : MonoBehaviour
    {
        [Header("Update")]
        [SerializeField]
        private HoFloatingConstraintUpdateMode updateMode = HoFloatingConstraintUpdateMode.LateUpdate;

        [SerializeField]
        private bool evaluateInEditMode = true;

        [SerializeField]
        private bool initializeOnEnable = true;

        [Header("Initial Transform")]
        [SerializeField]
        private bool hasInitialTransform;

        [SerializeField]
        private Vector3 initialLocalPosition = Vector3.zero;

        [SerializeField]
        private Vector3 initialLocalRotation = Vector3.zero;

        [SerializeField]
        private Vector3 initialLocalScale = Vector3.one;

        [Header("Offset")]
        [SerializeField]
        private HoFloatingConstraintSpace offsetSpace = HoFloatingConstraintSpace.World;

        [SerializeField]
        private Vector3 positionOffset = Vector3.zero;

        [SerializeField]
        private Vector3 rotationOffset = Vector3.zero;

        [SerializeField]
        private Vector3 scaleOffset = Vector3.zero;

        [Header("Oscillation")]
        [SerializeField]
        private bool oscillationEnabled = true;

        [SerializeField, Min(0.0f)]
        private float oscillationMultiplier = 1.0f;

        [SerializeField]
        private HoFloatingConstraintSpace oscillationSpace = HoFloatingConstraintSpace.World;

        [SerializeField]
        private HoFloatingConstraintWaveform oscillationWaveform = HoFloatingConstraintWaveform.Sin;

        [SerializeField]
        private AnimationCurve oscillationCurve = AnimationCurve.EaseInOut(0.0f, 0.0f, 1.0f, 1.0f);

        [SerializeField, Min(0.0f)]
        private float oscillationFrequency = 0.35f;

        [SerializeField]
        private float oscillationPhase = 0.0f;

        [SerializeField]
        private Vector3 oscillationPositionAmplitude = new Vector3(0.0f, 0.025f, 0.0f);

        [SerializeField]
        private Vector3 oscillationRotationAmplitude = Vector3.zero;

        [SerializeField]
        private Vector3 oscillationScaleAmplitude = Vector3.zero;

        [SerializeField]
        private Vector3 oscillationAxisWeight = Vector3.one;

        [Header("Noise")]
        [SerializeField]
        private bool noiseEnabled = false;

        [SerializeField, Min(0.0f)]
        private float noiseMultiplier = 1.0f;

        [SerializeField]
        private HoFloatingConstraintSpace noiseSpace = HoFloatingConstraintSpace.World;

        [SerializeField, Min(0.0f)]
        private float noiseFrequency = 0.75f;

        [SerializeField]
        private int noiseSeed = 1;

        [SerializeField]
        private Vector3 noisePositionAmplitude = Vector3.zero;

        [SerializeField]
        private Vector3 noiseRotationAmplitude = Vector3.zero;

        [SerializeField]
        private Vector3 noiseScaleAmplitude = Vector3.zero;

        [Header("Debug")]
        [SerializeField]
        private bool drawGizmos = true;

        [SerializeField]
        private Color gizmoColor = new Color(0.35f, 0.9f, 0.95f, 0.85f);

        private Vector3 anchorPosition;
        private Quaternion anchorRotation = Quaternion.identity;
        private Vector3 anchorScale = Vector3.one;
        private Vector3 anchorLocalPosition;
        private Vector3 anchorLocalRotation;
        private Vector3 anchorLocalScale = Vector3.one;
        private Vector3 currentPosition;
        private Quaternion currentRotation = Quaternion.identity;
        private Vector3 currentScale = Vector3.one;
        private double lastUpdateTime;
        private bool initialized;

        public Vector3 AnchorPosition => anchorPosition;

        public Quaternion AnchorRotation => anchorRotation;

        public Vector3 CurrentPosition => currentPosition;

        public Quaternion CurrentRotation => currentRotation;

        public Vector3 CurrentScale => currentScale;

        public bool HasInitialTransform => hasInitialTransform;

        public HoFloatingConstraintUpdateMode UpdateMode
        {
            get => updateMode;
            set => updateMode = value;
        }

        private void OnEnable()
        {
            if (initializeOnEnable)
            {
                ResetState();
            }
            else
            {
                lastUpdateTime = GetTime();
            }
        }

        private void OnDisable()
        {
            initialized = false;
        }

        private void Update()
        {
            if (updateMode == HoFloatingConstraintUpdateMode.Update)
            {
                EvaluateWithCurrentDelta();
            }
        }

        private void LateUpdate()
        {
            if (updateMode == HoFloatingConstraintUpdateMode.LateUpdate)
            {
                EvaluateWithCurrentDelta();
            }
        }

        private void FixedUpdate()
        {
            if (updateMode == HoFloatingConstraintUpdateMode.FixedUpdate)
            {
                Evaluate(Time.fixedDeltaTime);
            }
        }

        private void OnValidate()
        {
            initialLocalScale = Max(initialLocalScale, Vector3.zero);
            oscillationMultiplier = Mathf.Max(0.0f, oscillationMultiplier);
            oscillationFrequency = Mathf.Max(0.0f, oscillationFrequency);
            noiseMultiplier = Mathf.Max(0.0f, noiseMultiplier);
            noiseFrequency = Mathf.Max(0.0f, noiseFrequency);
        }

        public void ResetState()
        {
            if (!hasInitialTransform)
            {
                Transform self = transform;
                anchorLocalPosition = self.localPosition;
                anchorLocalRotation = NormalizeEuler(self.localEulerAngles);
                anchorLocalScale = self.localScale;
            }

            GetAnchorPose(out anchorPosition, out anchorRotation, out anchorScale);
            currentPosition = anchorPosition;
            currentRotation = anchorRotation;
            currentScale = anchorScale;
            lastUpdateTime = GetTime();
            initialized = true;
        }

        public void SaveInitialTransform()
        {
            Transform self = transform;
            initialLocalPosition = self.localPosition;
            initialLocalRotation = NormalizeEuler(self.localEulerAngles);
            initialLocalScale = self.localScale;
            hasInitialTransform = true;
            ResetState();
        }

        public void RestoreInitialTransform()
        {
            if (!hasInitialTransform)
            {
                return;
            }

            Transform self = transform;
            self.SetLocalPositionAndRotation(initialLocalPosition, Quaternion.Euler(initialLocalRotation));
            self.localScale = initialLocalScale;
            ResetState();
        }

        public void ClearInitialTransform()
        {
            hasInitialTransform = false;
            initialLocalPosition = Vector3.zero;
            initialLocalRotation = Vector3.zero;
            initialLocalScale = Vector3.one;
            ResetState();
        }

        public void Evaluate(float deltaTime)
        {
            if (!ShouldEvaluate())
            {
                return;
            }

            if (!initialized)
            {
                ResetState();
            }

            GetAnchorPose(out anchorPosition, out anchorRotation, out anchorScale);

            Vector3 finalPosition = anchorPosition;
            Quaternion finalRotation = anchorRotation;
            Vector3 finalScale = anchorScale;

            ApplyOffset(ref finalPosition, ref finalRotation, ref finalScale);
            ApplyOscillation(ref finalPosition, ref finalRotation, ref finalScale);
            ApplyNoise(ref finalPosition, ref finalRotation, ref finalScale);

            currentPosition = finalPosition;
            currentRotation = finalRotation;
            currentScale = finalScale;
            WriteTransform(finalPosition, finalRotation, finalScale);
        }

        private void EvaluateWithCurrentDelta()
        {
            double currentTime = GetTime();
            float deltaTime = initialized ? (float)(currentTime - lastUpdateTime) : 0.0f;
            lastUpdateTime = currentTime;
            Evaluate(deltaTime);
        }

        private bool ShouldEvaluate()
        {
            if (Application.isPlaying)
            {
                return true;
            }

            return evaluateInEditMode;
        }

        private void GetAnchorPose(out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            Vector3 localPosition = hasInitialTransform ? initialLocalPosition : anchorLocalPosition;
            Vector3 localRotation = hasInitialTransform ? initialLocalRotation : anchorLocalRotation;
            Vector3 localScale = hasInitialTransform ? initialLocalScale : anchorLocalScale;

            Transform parent = transform.parent;
            rotation = Quaternion.Euler(localRotation);
            if (parent != null)
            {
                position = parent.TransformPoint(localPosition);
                rotation = parent.rotation * rotation;
            }
            else
            {
                position = localPosition;
            }

            scale = localScale;
        }

        private void ApplyOffset(ref Vector3 finalPosition, ref Quaternion finalRotation, ref Vector3 finalScale)
        {
            finalPosition += TransformVector(positionOffset, offsetSpace);
            finalRotation *= Quaternion.Euler(rotationOffset);
            finalScale += scaleOffset;
        }

        private void ApplyOscillation(ref Vector3 finalPosition, ref Quaternion finalRotation, ref Vector3 finalScale)
        {
            if (!oscillationEnabled)
            {
                return;
            }

            float wave = EvaluateOscillationWave();
            float scaledWave = wave * oscillationMultiplier;
            Vector3 weightedPositionAmplitude = Vector3.Scale(oscillationPositionAmplitude, oscillationAxisWeight);
            finalPosition += TransformVector(weightedPositionAmplitude * scaledWave, oscillationSpace);
            finalRotation *= Quaternion.Euler(oscillationRotationAmplitude * scaledWave);
            finalScale += oscillationScaleAmplitude * scaledWave;
        }

        private float EvaluateOscillationWave()
        {
            float time = GetScaledTime(oscillationFrequency, oscillationPhase);
            switch (oscillationWaveform)
            {
                case HoFloatingConstraintWaveform.Triangle:
                    return Mathf.PingPong(time * 2.0f, 2.0f) - 1.0f;
                case HoFloatingConstraintWaveform.Curve:
                    if (oscillationCurve == null)
                    {
                        return 0.0f;
                    }

                    return oscillationCurve.Evaluate(Mathf.Repeat(time, 1.0f)) * 2.0f - 1.0f;
                case HoFloatingConstraintWaveform.Sin:
                default:
                    return Mathf.Sin(time * Mathf.PI * 2.0f);
            }
        }

        private void ApplyNoise(ref Vector3 finalPosition, ref Quaternion finalRotation, ref Vector3 finalScale)
        {
            if (!noiseEnabled)
            {
                return;
            }

            Vector3 positionNoise = Vector3.Scale(noisePositionAmplitude, GetSignedNoise3(noiseSeed, noiseFrequency)) * noiseMultiplier;
            Vector3 rotationNoise = Vector3.Scale(noiseRotationAmplitude, GetSignedNoise3(noiseSeed + 17, noiseFrequency)) * noiseMultiplier;
            Vector3 scaleNoise = Vector3.Scale(noiseScaleAmplitude, GetSignedNoise3(noiseSeed + 31, noiseFrequency)) * noiseMultiplier;

            finalPosition += TransformVector(positionNoise, noiseSpace);
            finalRotation *= Quaternion.Euler(rotationNoise);
            finalScale += scaleNoise;
        }

        private Vector3 TransformVector(Vector3 vector, HoFloatingConstraintSpace space)
        {
            return space == HoFloatingConstraintSpace.Local ? anchorRotation * vector : vector;
        }

        private void WriteTransform(Vector3 finalPosition, Quaternion finalRotation, Vector3 finalScale)
        {
            Transform self = transform;
            self.SetPositionAndRotation(finalPosition, finalRotation);
            self.localScale = finalScale;
        }

        private Vector3 GetSignedNoise3(int seed, float frequency)
        {
            float time = GetScaledTime(frequency, 0.0f);
            return new Vector3(
                SignedPerlin(seed * 0.071f + 11.0f, time),
                SignedPerlin(seed * 0.113f + 29.0f, time + 19.0f),
                SignedPerlin(seed * 0.157f + 47.0f, time + 37.0f));
        }

        private static float SignedPerlin(float x, float y)
        {
            return Mathf.PerlinNoise(x, y) * 2.0f - 1.0f;
        }

        private float GetScaledTime(float frequency, float phase)
        {
            return (float)GetTime() * Mathf.Max(0.0f, frequency) + phase;
        }

        private static Vector3 NormalizeEuler(Vector3 euler)
        {
            return new Vector3(
                NormalizeAngle(euler.x),
                NormalizeAngle(euler.y),
                NormalizeAngle(euler.z));
        }

        private static float NormalizeAngle(float angle)
        {
            angle = Mathf.Repeat(angle + 180.0f, 360.0f) - 180.0f;
            return Mathf.Approximately(angle, -180.0f) ? 180.0f : angle;
        }

        private static Vector3 Max(Vector3 value, Vector3 min)
        {
            return new Vector3(
                Mathf.Max(value.x, min.x),
                Mathf.Max(value.y, min.y),
                Mathf.Max(value.z, min.z));
        }

        private static double GetTime()
        {
            return Application.isPlaying ? Time.timeAsDouble : Time.realtimeSinceStartupAsDouble;
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos)
            {
                return;
            }

            Color oldColor = Gizmos.color;
            Gizmos.color = gizmoColor;
            Gizmos.DrawWireSphere(anchorPosition, 0.04f);
            Gizmos.DrawLine(anchorPosition, currentPosition);
            Gizmos.DrawWireSphere(currentPosition, 0.03f);
            Gizmos.color = oldColor;
        }
    }
}
