using System;
using UnityEngine;

namespace Hollow.HoUnityTools.Constraints
{
    public enum HoFloatingConstraintSpace
    {
        World = 0,
        Local = 1
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("HoUnityTools/Constraints/Ho Floating Constraint")]
    public sealed class HoFloatingConstraint : MonoBehaviour
    {
        [SerializeField, Tooltip("Position amplitudes use world axes or the starting rotation's axes. Rotation amplitudes use local axes.")]
        private HoFloatingConstraintSpace oscillationSpace = HoFloatingConstraintSpace.World;

        [Header("Oscillation")]
        [SerializeField]
        private bool oscillationEnabled = true;

        [SerializeField, Min(0f), Tooltip("Cycles per second.")]
        private float oscillationFrequency = 0.35f;

        [SerializeField, Min(0f)]
        private float oscillationMultiplier = 1f;

        [SerializeField]
        private Vector3 oscillationPositionAmplitude = new Vector3(0f, 0.025f, 0f);

        [SerializeField, Tooltip("Euler amplitude in degrees.")]
        private Vector3 oscillationRotationAmplitude;

        [Header("Noise")]
        [SerializeField]
        private bool noiseEnabled;

        [SerializeField, Min(0f)]
        private float noiseFrequency = 0.75f;

        [SerializeField, Min(0f)]
        private float noiseMultiplier = 1f;

        [SerializeField]
        private int noiseSeed = 1;

        [SerializeField]
        private Vector3 noisePositionAmplitude;

        [SerializeField, Tooltip("Euler amplitude in degrees.")]
        private Vector3 noiseRotationAmplitude;

        // Legacy rest poses are already stored on some assets independently of their Transform.
        [SerializeField, HideInInspector] private bool hasInitialTransform;
        [SerializeField, HideInInspector] private Vector3 initialLocalPosition;
        [SerializeField, HideInInspector] private Vector3 initialLocalRotation;

        private Vector3 restLocalPosition;
        private Quaternion restLocalRotation;
        private bool drovePosition;
        private bool droveRotation;

        private void OnEnable()
        {
            Transform self = transform;
            restLocalPosition = hasInitialTransform ? initialLocalPosition : self.localPosition;
            restLocalRotation = hasInitialTransform ? Quaternion.Euler(initialLocalRotation) : self.localRotation;
        }

        private void LateUpdate()
        {
            double time = Time.timeAsDouble;
            bool oscillates = oscillationEnabled && oscillationMultiplier > 0f;
            bool addsNoise = noiseEnabled && noiseMultiplier > 0f;

            Vector3 positionOffset = Vector3.zero;
            Quaternion rotationOffset = Quaternion.identity;
            bool drivesPosition = false;
            bool drivesRotation = false;

            if (oscillates)
            {
                double cycles = time * Mathf.Max(0f, oscillationFrequency);
                float wave = (float)Math.Sin(cycles * 2.0 * Math.PI) * oscillationMultiplier;
                positionOffset = oscillationPositionAmplitude * wave;
                rotationOffset = Quaternion.Euler(oscillationRotationAmplitude * wave);
                drivesPosition = oscillationPositionAmplitude.sqrMagnitude > 0f;
                drivesRotation = oscillationRotationAmplitude.sqrMagnitude > 0f;
            }

            if (addsNoise)
            {
                float sample = (float)(time * Mathf.Max(0f, noiseFrequency));
                if (noisePositionAmplitude.sqrMagnitude > 0f)
                {
                    positionOffset += Vector3.Scale(noisePositionAmplitude, SampleNoise(noiseSeed, sample)) * noiseMultiplier;
                    drivesPosition = true;
                }

                if (noiseRotationAmplitude.sqrMagnitude > 0f)
                {
                    Vector3 euler = Vector3.Scale(noiseRotationAmplitude, SampleNoise(noiseSeed + 17, sample)) * noiseMultiplier;
                    rotationOffset *= Quaternion.Euler(euler);
                    drivesRotation = true;
                }
            }

            Transform self = transform;
            if (drivesPosition || drovePosition)
            {
                Transform parent = self.parent;
                if (oscillationSpace == HoFloatingConstraintSpace.Local)
                {
                    Quaternion restWorldRotation = parent != null ? parent.rotation * restLocalRotation : restLocalRotation;
                    positionOffset = restWorldRotation * positionOffset;
                }

                Vector3 localOffset = parent != null ? parent.InverseTransformVector(positionOffset) : positionOffset;
                self.localPosition = restLocalPosition + localOffset;
            }

            if (drivesRotation || droveRotation)
            {
                self.localRotation = restLocalRotation * rotationOffset;
            }

            drovePosition = drivesPosition;
            droveRotation = drivesRotation;
        }

        private void OnDisable()
        {
            // Remove our last offset so enabling again captures the rest pose.
            if (Application.IsPlaying(gameObject))
            {
                if (drovePosition)
                {
                    transform.localPosition = restLocalPosition;
                }

                if (droveRotation)
                {
                    transform.localRotation = restLocalRotation;
                }
            }

            drovePosition = false;
            droveRotation = false;
        }

        private static Vector3 SampleNoise(int seed, float time)
        {
            return new Vector3(
                Mathf.PerlinNoise(seed * 0.071f + 11f, time),
                Mathf.PerlinNoise(seed * 0.113f + 29f, time + 19f),
                Mathf.PerlinNoise(seed * 0.157f + 47f, time + 37f)) * 2f - Vector3.one;
        }
    }
}
