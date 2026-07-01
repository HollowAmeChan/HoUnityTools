using UnityEngine;

namespace Hollow.HoUnityTools.Constraints
{
    public enum HoFollowConstraintRotationMode
    {
        World,
        Local,
        Target
    }

    public enum HoFollowConstraintOffsetMode
    {
        Local,
        World
    }

    public enum HoFollowConstraintLimitShape
    {
        Sphere,
        Box,
        Cylinder
    }

    public enum HoFollowConstraintUpdateMode
    {
        LateUpdate,
        Update,
        FixedUpdate,
        Manual
    }

    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("HoUnityTools/Constraints/Ho Follow Constraint")]
    public sealed class HoFollowConstraint : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField]
        private Transform target;

        [SerializeField]
        private HoFollowConstraintUpdateMode updateMode = HoFollowConstraintUpdateMode.LateUpdate;

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

        [Header("Follow")]
        [SerializeField, Range(0.0f, 1.0f)]
        private float positionFollow = 0.9f;

        [SerializeField, Range(0.0f, 1.0f)]
        private float rotationFollow = 0.8f;

        [SerializeField, Range(0.0f, 10.0f)]
        private float response = 4.0f;

        [SerializeField, Range(0.0f, 1.0f)]
        private float overshoot = 0.15f;

        [SerializeField, Min(0.0f)]
        private float maxVelocity;

        [SerializeField, Min(0.0f)]
        private float maxAngularVelocity;

        [Header("Axis Constraint")]
        [SerializeField, Range(0.0f, 1.0f)]
        private float lockX = 0.0f;

        [SerializeField, Range(0.0f, 1.0f)]
        private float lockY = 0.0f;

        [SerializeField, Range(0.0f, 1.0f)]
        private float lockZ = 0.0f;

        [SerializeField, Range(0.0f, 1.0f)]
        private float lockPitch = 0.0f;

        [SerializeField, Range(0.0f, 1.0f)]
        private float lockYaw = 0.0f;

        [SerializeField, Range(0.0f, 1.0f)]
        private float lockRoll = 0.0f;

        [Header("Rotation")]
        [SerializeField]
        private HoFollowConstraintRotationMode rotationMode = HoFollowConstraintRotationMode.World;

        [SerializeField]
        private bool keepHorizon = false;

        [SerializeField]
        private bool followYaw = true;

        [SerializeField]
        private bool followPitch = true;

        [SerializeField]
        private bool followRoll = true;

        [Header("Limit")]
        [SerializeField]
        private bool limitEnabled = false;

        [SerializeField]
        private HoFollowConstraintLimitShape limitShape = HoFollowConstraintLimitShape.Sphere;

        [SerializeField, Min(0.0f)]
        private float limitRadius = 1.0f;

        [SerializeField]
        private Vector3 limitBoxSize = Vector3.one;

        [SerializeField, Min(0.0f)]
        private float limitCylinderHeight = 1.0f;

        [SerializeField, Range(0.0f, 1.0f)]
        private float limitSoftness = 0.2f;

        [SerializeField]
        private bool limitClamp = true;

        [Header("Offset")]
        [SerializeField]
        private HoFollowConstraintOffsetMode offsetMode = HoFollowConstraintOffsetMode.Local;

        [SerializeField]
        private Vector3 positionOffset = Vector3.zero;

        [SerializeField]
        private Vector3 rotationOffset = Vector3.zero;

        [Header("Debug")]
        [SerializeField]
        private bool drawGizmos = true;

        [SerializeField]
        private bool drawMotionTrail = false;

        [SerializeField, Range(4, 128)]
        private int motionTrailLength = 32;

        [SerializeField]
        private Color gizmoColor = new Color(0.35f, 0.8f, 1.0f, 0.85f);

        private Vector3 anchorPosition;
        private Quaternion anchorRotation = Quaternion.identity;
        private Vector3 anchorScale = Vector3.one;
        private Vector3 anchorLocalPosition;
        private Vector3 anchorLocalRotation;
        private Vector3 anchorLocalScale = Vector3.one;
        private Quaternion targetAnchorRotation = Quaternion.identity;
        private Vector3 currentPosition;
        private Quaternion currentRotation = Quaternion.identity;
        private Vector3 currentScale = Vector3.one;
        private Vector3 velocity;
        private Vector3 angularVelocity;
        private Vector3 previousEuler;
        private double lastUpdateTime;
        private bool initialized;
        private Vector3[] motionTrail;
        private int motionTrailIndex;
        private int motionTrailCount;

        public Transform Target
        {
            get => target;
            set => target = value;
        }

        public Vector3 Velocity => velocity;

        public Vector3 AngularVelocity => angularVelocity;

        public Vector3 AnchorPosition => anchorPosition;

        public Quaternion AnchorRotation => anchorRotation;

        public Vector3 CurrentPosition => currentPosition;

        public Quaternion CurrentRotation => currentRotation;

        public bool HasInitialTransform => hasInitialTransform;

        public HoFollowConstraintUpdateMode UpdateMode
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
            if (updateMode == HoFollowConstraintUpdateMode.Update)
            {
                EvaluateWithCurrentDelta();
            }
        }

        private void LateUpdate()
        {
            if (updateMode == HoFollowConstraintUpdateMode.LateUpdate)
            {
                EvaluateWithCurrentDelta();
            }
        }

        private void FixedUpdate()
        {
            if (updateMode == HoFollowConstraintUpdateMode.FixedUpdate)
            {
                Evaluate(Time.fixedDeltaTime);
            }
        }

        private void OnValidate()
        {
            maxVelocity = Mathf.Max(0.0f, maxVelocity);
            maxAngularVelocity = Mathf.Max(0.0f, maxAngularVelocity);
            initialLocalScale = Max(initialLocalScale, Vector3.zero);
            limitRadius = Mathf.Max(0.0f, limitRadius);
            limitBoxSize = Max(limitBoxSize, Vector3.zero);
            limitCylinderHeight = Mathf.Max(0.0f, limitCylinderHeight);
            motionTrailLength = Mathf.Clamp(motionTrailLength, 4, 128);
            EnsureMotionTrail();

            if (!Application.isPlaying && initialized)
            {
                currentScale = transform.localScale;
            }
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
            targetAnchorRotation = target != null ? Quaternion.Inverse(target.rotation) * anchorRotation : Quaternion.identity;
            currentPosition = anchorPosition;
            currentRotation = anchorRotation;
            currentScale = anchorScale;
            previousEuler = currentRotation.eulerAngles;
            velocity = Vector3.zero;
            angularVelocity = Vector3.zero;
            lastUpdateTime = GetTime();
            initialized = true;
            ClearMotionTrail();
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

        public void SnapToTarget()
        {
            if (target == null)
            {
                ResetState();
                return;
            }

            GetTargetPose(out Vector3 targetPosition, out Quaternion targetRotation);
            currentPosition = ApplyAxisLock(targetPosition);
            currentRotation = ApplyRotationLock(FilterRotation(targetRotation));
            currentScale = transform.localScale;
            velocity = Vector3.zero;
            angularVelocity = Vector3.zero;
            Vector3 finalPosition = currentPosition;
            Quaternion finalRotation = currentRotation;
            ApplyOffset(ref finalPosition, ref finalRotation);
            WriteTransform(finalPosition, finalRotation, currentScale);
            previousEuler = currentRotation.eulerAngles;
            initialized = true;
            ClearMotionTrail();
            AddMotionTrailPoint(finalPosition);
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

            if (target == null)
            {
                return;
            }

            GetAnchorPose(out anchorPosition, out anchorRotation, out anchorScale);

            float safeDeltaTime = Mathf.Max(0.0f, deltaTime);
            GetTargetPose(out Vector3 desiredPosition, out Quaternion desiredRotation);

            desiredPosition = ApplyAxisLock(desiredPosition);
            desiredRotation = ApplyRotationLock(FilterRotation(desiredRotation));

            UpdatePosition(desiredPosition, safeDeltaTime);
            UpdateRotation(desiredRotation, safeDeltaTime);

            Vector3 finalPosition = currentPosition;
            Quaternion finalRotation = currentRotation;
            Vector3 finalScale = anchorScale;

            ApplyOffset(ref finalPosition, ref finalRotation);
            finalPosition = ApplyLimit(finalPosition);

            WriteTransform(finalPosition, finalRotation, finalScale);
            AddMotionTrailPoint(finalPosition);
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
            Transform self = transform;
            Vector3 localPosition = hasInitialTransform ? initialLocalPosition : anchorLocalPosition;
            Vector3 localRotation = hasInitialTransform ? initialLocalRotation : anchorLocalRotation;
            Vector3 localScale = hasInitialTransform ? initialLocalScale : anchorLocalScale;

            Transform parent = self.parent;
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

        private void GetTargetPose(out Vector3 targetPosition, out Quaternion targetRotation)
        {
            targetPosition = target.position;
            targetRotation = GetTargetRotation();
        }

        private void ApplyOffset(ref Vector3 finalPosition, ref Quaternion finalRotation)
        {
            finalPosition += GetPositionOffset();
            finalRotation *= Quaternion.Euler(rotationOffset);
        }

        private Vector3 GetPositionOffset()
        {
            if (offsetMode != HoFollowConstraintOffsetMode.Local)
            {
                return positionOffset;
            }

            if (target != null)
            {
                return target.TransformVector(positionOffset);
            }

            return anchorRotation * positionOffset;
        }

        private Quaternion GetTargetRotation()
        {
            if (target == null)
            {
                return anchorRotation;
            }

            switch (rotationMode)
            {
                case HoFollowConstraintRotationMode.Local:
                    return transform.parent != null ? transform.parent.rotation * target.localRotation : target.localRotation;
                case HoFollowConstraintRotationMode.Target:
                    return target.rotation * targetAnchorRotation;
                case HoFollowConstraintRotationMode.World:
                default:
                    return target.rotation;
            }
        }

        private Vector3 ApplyAxisLock(Vector3 desiredPosition)
        {
            return new Vector3(
                Mathf.Lerp(desiredPosition.x, anchorPosition.x, lockX),
                Mathf.Lerp(desiredPosition.y, anchorPosition.y, lockY),
                Mathf.Lerp(desiredPosition.z, anchorPosition.z, lockZ));
        }

        private Quaternion FilterRotation(Quaternion desiredRotation)
        {
            Vector3 desiredEuler = NormalizeEuler(desiredRotation.eulerAngles);
            Vector3 anchorEuler = NormalizeEuler(anchorRotation.eulerAngles);

            if (keepHorizon)
            {
                desiredEuler.x = anchorEuler.x;
                desiredEuler.z = anchorEuler.z;
            }

            if (!followPitch)
            {
                desiredEuler.x = anchorEuler.x;
            }

            if (!followYaw)
            {
                desiredEuler.y = anchorEuler.y;
            }

            if (!followRoll)
            {
                desiredEuler.z = anchorEuler.z;
            }

            return Quaternion.Euler(desiredEuler);
        }

        private Quaternion ApplyRotationLock(Quaternion desiredRotation)
        {
            Vector3 desiredEuler = NormalizeEuler(desiredRotation.eulerAngles);
            Vector3 anchorEuler = NormalizeEuler(anchorRotation.eulerAngles);
            Vector3 lockedEuler = new Vector3(
                Mathf.LerpAngle(desiredEuler.x, anchorEuler.x, lockPitch),
                Mathf.LerpAngle(desiredEuler.y, anchorEuler.y, lockYaw),
                Mathf.LerpAngle(desiredEuler.z, anchorEuler.z, lockRoll));
            return Quaternion.Euler(lockedEuler);
        }

        private void UpdatePosition(Vector3 desiredPosition, float deltaTime)
        {
            if (positionFollow <= 0.0f)
            {
                currentPosition = anchorPosition;
                velocity = Vector3.zero;
                return;
            }

            if (deltaTime <= 0.0f)
            {
                currentPosition = Vector3.Lerp(anchorPosition, desiredPosition, positionFollow);
                velocity = Vector3.zero;
                return;
            }

            Vector3 followTarget = Vector3.Lerp(anchorPosition, desiredPosition, positionFollow);
            float smoothTime = ResponseToSmoothTime(response);
            Vector3 oldPosition = currentPosition;
            currentPosition = Vector3.SmoothDamp(currentPosition, followTarget, ref velocity, smoothTime, GetMaxSpeed(maxVelocity), deltaTime);

            if (overshoot > 0.0f)
            {
                Vector3 lead = velocity * (overshoot * deltaTime);
                currentPosition += lead;
            }

            if (maxVelocity > 0.0f)
            {
                Vector3 delta = currentPosition - oldPosition;
                float maxDelta = maxVelocity * deltaTime;
                if (delta.sqrMagnitude > maxDelta * maxDelta)
                {
                    currentPosition = oldPosition + delta.normalized * maxDelta;
                    velocity = (currentPosition - oldPosition) / deltaTime;
                }
            }
        }

        private void UpdateRotation(Quaternion desiredRotation, float deltaTime)
        {
            if (rotationFollow <= 0.0f)
            {
                currentRotation = anchorRotation;
                angularVelocity = Vector3.zero;
                previousEuler = currentRotation.eulerAngles;
                return;
            }

            if (deltaTime <= 0.0f)
            {
                currentRotation = Quaternion.Slerp(anchorRotation, desiredRotation, rotationFollow);
                previousEuler = currentRotation.eulerAngles;
                angularVelocity = Vector3.zero;
                return;
            }

            Quaternion followTarget = Quaternion.Slerp(anchorRotation, desiredRotation, rotationFollow);
            float t = 1.0f - Mathf.Exp(-Mathf.Max(0.0f, response) * deltaTime);
            t = Mathf.Clamp01(t * (1.0f + overshoot));

            Quaternion oldRotation = currentRotation;
            currentRotation = Quaternion.Slerp(currentRotation, followTarget, t);

            if (maxAngularVelocity > 0.0f)
            {
                float angle = Quaternion.Angle(oldRotation, currentRotation);
                float maxAngle = maxAngularVelocity * deltaTime;
                if (angle > maxAngle && angle > 0.0001f)
                {
                    currentRotation = Quaternion.RotateTowards(oldRotation, currentRotation, maxAngle);
                }
            }

            Vector3 euler = currentRotation.eulerAngles;
            angularVelocity = new Vector3(
                Mathf.DeltaAngle(previousEuler.x, euler.x),
                Mathf.DeltaAngle(previousEuler.y, euler.y),
                Mathf.DeltaAngle(previousEuler.z, euler.z)) / deltaTime;
            previousEuler = euler;
        }

        private Vector3 ApplyLimit(Vector3 finalPosition)
        {
            if (!limitEnabled || target == null)
            {
                return finalPosition;
            }

            Vector3 center = target.position + GetPositionOffset();
            Vector3 delta = finalPosition - center;
            Vector3 clampedDelta = delta;

            switch (limitShape)
            {
                case HoFollowConstraintLimitShape.Box:
                    Vector3 halfSize = limitBoxSize * 0.5f;
                    clampedDelta = new Vector3(
                        Mathf.Clamp(delta.x, -halfSize.x, halfSize.x),
                        Mathf.Clamp(delta.y, -halfSize.y, halfSize.y),
                        Mathf.Clamp(delta.z, -halfSize.z, halfSize.z));
                    break;
                case HoFollowConstraintLimitShape.Cylinder:
                    Vector2 xz = new Vector2(delta.x, delta.z);
                    float radius = Mathf.Max(0.0f, limitRadius);
                    if (radius <= 0.0f)
                    {
                        xz = Vector2.zero;
                    }
                    else if (xz.magnitude > radius)
                    {
                        xz = xz.normalized * radius;
                    }

                    float halfHeight = limitCylinderHeight * 0.5f;
                    clampedDelta = new Vector3(xz.x, Mathf.Clamp(delta.y, -halfHeight, halfHeight), xz.y);
                    break;
                case HoFollowConstraintLimitShape.Sphere:
                default:
                    float maxDistance = Mathf.Max(0.0f, limitRadius);
                    if (maxDistance <= 0.0f)
                    {
                        clampedDelta = Vector3.zero;
                    }
                    else if (delta.magnitude > maxDistance)
                    {
                        clampedDelta = delta.normalized * maxDistance;
                    }
                    break;
            }

            if (limitClamp)
            {
                return center + clampedDelta;
            }

            float softness = Mathf.Clamp01(limitSoftness);
            return Vector3.Lerp(finalPosition, center + clampedDelta, 1.0f - softness);
        }

        private void WriteTransform(Vector3 finalPosition, Quaternion finalRotation, Vector3 finalScale)
        {
            Transform self = transform;
            self.SetPositionAndRotation(finalPosition, finalRotation);
            self.localScale = finalScale;
        }

        private static float ResponseToSmoothTime(float value)
        {
            return 1.0f / Mathf.Max(0.01f, value);
        }

        private static float GetMaxSpeed(float value)
        {
            return value > 0.0f ? value : Mathf.Infinity;
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

        private void EnsureMotionTrail()
        {
            if (motionTrail != null && motionTrail.Length == motionTrailLength)
            {
                return;
            }

            motionTrail = new Vector3[motionTrailLength];
            motionTrailIndex = 0;
            motionTrailCount = 0;
        }

        private void ClearMotionTrail()
        {
            EnsureMotionTrail();
            motionTrailIndex = 0;
            motionTrailCount = 0;
        }

        private void AddMotionTrailPoint(Vector3 point)
        {
            if (!drawMotionTrail)
            {
                return;
            }

            EnsureMotionTrail();
            motionTrail[motionTrailIndex] = point;
            motionTrailIndex = (motionTrailIndex + 1) % motionTrail.Length;
            motionTrailCount = Mathf.Min(motionTrailCount + 1, motionTrail.Length);
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos)
            {
                return;
            }

            Color oldColor = Gizmos.color;
            Gizmos.color = gizmoColor;

            if (target != null)
            {
                Vector3 center = target.position + GetPositionOffset();
                Gizmos.DrawLine(transform.position, center);
                Gizmos.DrawWireSphere(center, 0.06f);
                DrawLimitGizmo(center);
            }

            Gizmos.DrawWireSphere(currentPosition, 0.04f);
            DrawMotionTrailGizmo();
            Gizmos.color = oldColor;
        }

        private void DrawLimitGizmo(Vector3 center)
        {
            if (!limitEnabled)
            {
                return;
            }

            switch (limitShape)
            {
                case HoFollowConstraintLimitShape.Box:
                    Gizmos.DrawWireCube(center, limitBoxSize);
                    break;
                case HoFollowConstraintLimitShape.Cylinder:
                    DrawCylinderGizmo(center, Mathf.Max(0.0f, limitRadius), Mathf.Max(0.0f, limitCylinderHeight));
                    break;
                case HoFollowConstraintLimitShape.Sphere:
                default:
                    Gizmos.DrawWireSphere(center, limitRadius);
                    break;
            }
        }

        private static void DrawCylinderGizmo(Vector3 center, float radius, float height)
        {
            const int SegmentCount = 32;
            float halfHeight = height * 0.5f;
            Vector3 previousTop = center + new Vector3(radius, halfHeight, 0.0f);
            Vector3 previousBottom = center + new Vector3(radius, -halfHeight, 0.0f);

            for (int i = 1; i <= SegmentCount; i++)
            {
                float angle = i / (float)SegmentCount * Mathf.PI * 2.0f;
                Vector3 offset = new Vector3(Mathf.Cos(angle) * radius, 0.0f, Mathf.Sin(angle) * radius);
                Vector3 top = center + offset + Vector3.up * halfHeight;
                Vector3 bottom = center + offset - Vector3.up * halfHeight;
                Gizmos.DrawLine(previousTop, top);
                Gizmos.DrawLine(previousBottom, bottom);
                if (i % 8 == 0)
                {
                    Gizmos.DrawLine(top, bottom);
                }

                previousTop = top;
                previousBottom = bottom;
            }
        }

        private void DrawMotionTrailGizmo()
        {
            if (!drawMotionTrail || motionTrail == null || motionTrailCount < 2)
            {
                return;
            }

            for (int i = 1; i < motionTrailCount; i++)
            {
                int previous = (motionTrailIndex - i - 1 + motionTrail.Length) % motionTrail.Length;
                int current = (motionTrailIndex - i + motionTrail.Length) % motionTrail.Length;
                Gizmos.DrawLine(motionTrail[previous], motionTrail[current]);
            }
        }
    }
}
