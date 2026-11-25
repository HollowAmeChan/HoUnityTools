using UnityEngine;

namespace Hollow.HoUnityTools.Constraints
{
    public enum HoFollowConstraintUpdateMode
    {
        // Keep the serialized values used by scenes and prefabs.
        LateUpdate = 0,
        Update = 1
    }

    public enum HoFollowConstraintSpace
    {
        World = 0,
        Local = 1
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("HoUnityTools/Constraints/Ho Follow Constraint")]
    public sealed class HoFollowConstraint : MonoBehaviour
    {
        [SerializeField]
        private Transform target;

        [SerializeField, Tooltip("Use Update when another solver needs this pose later in the same frame.")]
        private HoFollowConstraintUpdateMode updateMode = HoFollowConstraintUpdateMode.Update;

        [SerializeField, Tooltip("Local damps motion relative to the parent. World also damps the parent's motion.")]
        private HoFollowConstraintSpace space = HoFollowConstraintSpace.Local;

        [Header("Follow")]
        [SerializeField, Range(0f, 1f), Tooltip("Blend from the starting position to the target position.")]
        private float positionFollow = 0.9f;

        [SerializeField, Range(0f, 1f), Tooltip("Blend from the starting rotation to the target rotation.")]
        private float rotationFollow = 0.8f;

        [SerializeField, Range(0f, 10f), Tooltip("Higher values follow the target faster.")]
        private float response = 4f;

        [SerializeField, Range(0f, 1f), Tooltip("Adds momentum to position follow and speeds up rotation follow.")]
        private float overshoot = 0.15f;

        [Header("Axis Locks")]
        [SerializeField, Range(0f, 1f)]
        private float lockX;

        [SerializeField, Range(0f, 1f)]
        private float lockY;

        [SerializeField, Range(0f, 1f)]
        private float lockZ;

        [SerializeField, Tooltip("Keep the starting pitch and roll in the selected space.")]
        private bool keepHorizon;

        [Header("Offsets")]
        [SerializeField, Tooltip("Position offset along the target's axes, including its scale.")]
        private Vector3 positionOffset;

        [SerializeField, Tooltip("Euler offset in degrees, applied after following the rotation.")]
        private Vector3 rotationOffset;

        // Legacy rest poses are already stored on some assets independently of their Transform.
        [SerializeField, HideInInspector] private bool hasInitialTransform;
        [SerializeField, HideInInspector] private Vector3 initialLocalPosition;
        [SerializeField, HideInInspector] private Vector3 initialLocalRotation;

        private Vector3 restLocalPosition;
        private Quaternion restLocalRotation;
        private Transform previousFrame;
        private Vector3 position;
        private Quaternion rotation;
        private Vector3 velocity;

        public Transform Target
        {
            get => target;
            set => target = value;
        }

        public HoFollowConstraintSpace Space
        {
            get => space;
            set => space = value;
        }

        public HoFollowConstraintUpdateMode UpdateMode
        {
            get => updateMode;
            set => updateMode = value;
        }

        private void OnEnable()
        {
            ResetState();
        }

        private void Update()
        {
            if (updateMode == HoFollowConstraintUpdateMode.Update && Time.deltaTime > 0f)
            {
                Evaluate(Time.deltaTime);
            }
        }

        private void LateUpdate()
        {
            // Old FixedUpdate and Manual values fall back to LateUpdate.
            if (updateMode != HoFollowConstraintUpdateMode.Update && Time.deltaTime > 0f)
            {
                Evaluate(Time.deltaTime);
            }
        }

        public void ResetState()
        {
            if (!isActiveAndEnabled || !Application.IsPlaying(gameObject))
            {
                return;
            }

            Transform self = transform;
            restLocalPosition = hasInitialTransform ? initialLocalPosition : self.localPosition;
            restLocalRotation = hasInitialTransform ? Quaternion.Euler(initialLocalRotation) : self.localRotation;
            Transform parent = self.parent;
            previousFrame = space == HoFollowConstraintSpace.Local ? parent : null;
            GetRestPose(parent, previousFrame, out position, out rotation);
            velocity = Vector3.zero;
        }

        public void SnapToTarget()
        {
            Evaluate(0f);
        }

        public void Evaluate(float deltaTime)
        {
            if (!isActiveAndEnabled || !Application.IsPlaying(gameObject) || target == null)
            {
                return;
            }

            Transform self = transform;
            if (target.IsChildOf(self))
            {
                return;
            }

            Transform parent = self.parent;
            Transform frame = space == HoFollowConstraintSpace.Local ? parent : null;
            SyncFrame(frame);
            GetRestPose(parent, frame, out Vector3 restPosition, out Quaternion restRotation);

            Vector3 targetPosition = target.position;
            Quaternion targetRotation = target.rotation;
            Vector3 offset = target.TransformVector(positionOffset);
            if (frame != null)
            {
                targetPosition = frame.InverseTransformPoint(targetPosition);
                targetRotation = Quaternion.Inverse(frame.rotation) * targetRotation;
                offset = frame.InverseTransformVector(offset);
            }

            targetPosition = new Vector3(
                Mathf.Lerp(targetPosition.x, restPosition.x, lockX),
                Mathf.Lerp(targetPosition.y, restPosition.y, lockY),
                Mathf.Lerp(targetPosition.z, restPosition.z, lockZ));

            if (keepHorizon)
            {
                Vector3 restEuler = restRotation.eulerAngles;
                targetRotation = Quaternion.Euler(restEuler.x, targetRotation.eulerAngles.y, restEuler.z);
            }

            Vector3 desiredPosition = Vector3.Lerp(restPosition, targetPosition, positionFollow);
            Quaternion desiredRotation = Quaternion.Slerp(restRotation, targetRotation, rotationFollow);
            if (deltaTime <= 0f || positionFollow <= 0f)
            {
                position = desiredPosition;
                velocity = Vector3.zero;
            }
            else
            {
                float smoothTime = 1f / Mathf.Max(0.01f, response);
                position = Vector3.SmoothDamp(position, desiredPosition, ref velocity, smoothTime, Mathf.Infinity, deltaTime);
                position += velocity * (overshoot * deltaTime);
            }

            float rotationBlend = deltaTime <= 0f || rotationFollow <= 0f
                ? 1f
                : (1f - Mathf.Exp(-Mathf.Max(0f, response) * deltaTime)) * (1f + overshoot);
            rotation = Quaternion.Slerp(rotation, desiredRotation, rotationBlend);

            Vector3 finalPosition = position + offset;
            Quaternion finalRotation = rotation * Quaternion.Euler(rotationOffset);
            if (frame != null)
            {
                self.SetLocalPositionAndRotation(finalPosition, finalRotation);
            }
            else
            {
                self.SetPositionAndRotation(finalPosition, finalRotation);
            }
        }

        private void GetRestPose(Transform parent, Transform frame, out Vector3 restPosition, out Quaternion restRotation)
        {
            if (frame == null && parent != null)
            {
                restPosition = parent.TransformPoint(restLocalPosition);
                restRotation = parent.rotation * restLocalRotation;
            }
            else
            {
                restPosition = restLocalPosition;
                restRotation = restLocalRotation;
            }
        }

        private void SyncFrame(Transform frame)
        {
            if (previousFrame == frame)
            {
                return;
            }

            // Preserve the smoothed pose when switching space or changing parent.
            if (previousFrame != null)
            {
                position = previousFrame.TransformPoint(position);
                rotation = previousFrame.rotation * rotation;
            }

            if (frame != null)
            {
                position = frame.InverseTransformPoint(position);
                rotation = Quaternion.Inverse(frame.rotation) * rotation;
            }

            previousFrame = frame;
            velocity = Vector3.zero;
        }
    }
}
