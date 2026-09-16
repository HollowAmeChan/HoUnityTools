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

    /// <summary>解算所在的坐标系。锚点、期望位姿、阻尼状态、速度上限都活在这一个坐标系里，世界只在读写边界出现一次。</summary>
    public enum HoFollowConstraintSpace
    {
        /// <summary>世界空间。旧行为：父级运动会进入阻尼，表现为相对父级的滑移。</summary>
        World = 0,

        /// <summary>锚点坐标系（父级本地空间；无父级时等同世界）。父级运动刚性传递，不进入软跟随。</summary>
        Local = 1
    }

    /// <summary>
    /// 跟随约束。位姿解算全部发生在「锚点坐标系」里：锚点本身就是父级本地常量，所以坐标系就是父级，无父级时坐标系就是世界。
    /// 父级的平移/旋转/缩放不出现在任何状态量里，因此不会产生软跟随滞后；只有目标相对坐标系的运动才会被阻尼。
    /// 跟点与目标共用父级时，父级动 = 两者一起刚性动，约束不会再加一层低通。
    /// </summary>
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

        [Header("Space")]
        // 新字段带初始化器：老场景反序列化时缺这个键，会拿到 Local（修复后的行为），需要旧行为的实例再手动切回 World。
        [SerializeField]
        private HoFollowConstraintSpace space = HoFollowConstraintSpace.Local;

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

        // 以下状态全部在锚点坐标系里：Local 模式 = 父级本地空间，World 模式 = 世界空间。
        private Vector3 currentFramePosition;
        private Quaternion currentFrameRotation = Quaternion.identity;
        private Vector3 currentScale = Vector3.one;

        // 未保存初始变换时，锚点用启用/首次求值那一刻的本地位姿。
        private Vector3 anchorLocalPosition;
        private Vector3 anchorLocalRotation;
        private Vector3 anchorLocalScale = Vector3.one;

        private Vector3 velocity;
        private Vector3 angularVelocity;
        private Vector3 previousEuler;

        // 阻尼器本帧追的位姿（已含跟随比例与轴锁定，未含偏移/限制），也用来做「还差多远」的读数。
        private Vector3 desiredFramePosition;
        private Quaternion desiredFrameRotation = Quaternion.identity;

        // ResetState 时捕获：锚点朝向在目标坐标系里的固定偏移，只在 Target 旋转模式下使用。
        private Quaternion targetFrameAnchorRotation = Quaternion.identity;

        // 上一帧的坐标系，用来在父级或模式变化时把状态换算过去。
        private Transform previousFrame;
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

        /// <summary>求解器内部的相对速度（锚点坐标系，每秒）。世界速度还包含坐标系自身的运动，不在这里。</summary>
        public Vector3 Velocity => velocity;

        public Vector3 AngularVelocity => angularVelocity;

        public HoFollowConstraintSpace Space
        {
            get => space;
            set => space = value;
        }

        /// <summary>是否真的在锚点坐标系里解算：Local 模式且存在父级。</summary>
        public bool UsesAnchorSpace => Frame != null;

        public Vector3 AnchorPosition => ToWorld(GetAnchorFramePosition());

        public Quaternion AnchorRotation => ToWorldRotation(GetAnchorFrameRotation());

        public Vector3 CurrentPosition => ToWorld(currentFramePosition);

        public Quaternion CurrentRotation => ToWorldRotation(currentFrameRotation);

        public Vector3 CurrentFramePosition => currentFramePosition;

        public Quaternion CurrentFrameRotation => currentFrameRotation;

        public Vector3 DesiredFramePosition => desiredFramePosition;

        public Quaternion DesiredFrameRotation => desiredFrameRotation;

        public bool HasInitialTransform => hasInitialTransform;

        public HoFollowConstraintUpdateMode UpdateMode
        {
            get => updateMode;
            set => updateMode = value;
        }

        // Local 模式用父级当坐标系；World 模式没有坐标系（世界量即坐标系量）；无父级时两者等价。
        private Transform Frame => space == HoFollowConstraintSpace.Local ? transform.parent : null;

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

            GetAnchorFramePose(out Vector3 anchorFramePosition, out Quaternion anchorFrameRotation, out Vector3 anchorFrameScale);
            currentFramePosition = anchorFramePosition;
            currentFrameRotation = anchorFrameRotation;
            currentScale = anchorFrameScale;
            desiredFramePosition = anchorFramePosition;
            desiredFrameRotation = anchorFrameRotation;
            targetFrameAnchorRotation = target != null
                ? Quaternion.Inverse(GetRawTargetFrameRotation()) * anchorFrameRotation
                : Quaternion.identity;
            previousEuler = currentFrameRotation.eulerAngles;
            velocity = Vector3.zero;
            angularVelocity = Vector3.zero;
            previousFrame = Frame;
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

            SyncFrame();

            GetAnchorFramePose(out Vector3 anchorFramePosition, out Quaternion anchorFrameRotation, out _);
            GetTargetFramePose(out Vector3 targetFramePosition, out Quaternion targetFrameRotation);

            currentFramePosition = ApplyAxisLock(targetFramePosition, anchorFramePosition);
            currentFrameRotation = ApplyRotationLock(FilterRotation(targetFrameRotation, anchorFrameRotation), anchorFrameRotation);
            desiredFramePosition = currentFramePosition;
            desiredFrameRotation = currentFrameRotation;
            velocity = Vector3.zero;
            angularVelocity = Vector3.zero;
            currentScale = transform.localScale;

            Vector3 finalPosition = currentFramePosition;
            Quaternion finalRotation = currentFrameRotation;
            ApplyOffset(ref finalPosition, ref finalRotation, GetFrameOffset());
            WriteTransform(finalPosition, finalRotation, currentScale);

            previousEuler = currentFrameRotation.eulerAngles;
            initialized = true;
            ClearMotionTrail();
            AddMotionTrailPoint(ToWorld(finalPosition));
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

            SyncFrame();

            if (target == null)
            {
                return;
            }

            float safeDeltaTime = Mathf.Max(0.0f, deltaTime);

            GetAnchorFramePose(out Vector3 anchorFramePosition, out Quaternion anchorFrameRotation, out Vector3 anchorFrameScale);
            GetTargetFramePose(out Vector3 targetFramePosition, out Quaternion targetFrameRotation);

            Vector3 desiredPosition = ApplyAxisLock(targetFramePosition, anchorFramePosition);
            Quaternion desiredRotation = ApplyRotationLock(FilterRotation(targetFrameRotation, anchorFrameRotation), anchorFrameRotation);

            desiredFramePosition = Vector3.Lerp(anchorFramePosition, desiredPosition, positionFollow);
            desiredFrameRotation = Quaternion.Slerp(anchorFrameRotation, desiredRotation, rotationFollow);

            UpdatePosition(desiredFramePosition, safeDeltaTime);
            UpdateRotation(desiredFrameRotation, safeDeltaTime);

            Vector3 frameOffset = GetFrameOffset();
            Vector3 finalPosition = currentFramePosition;
            Quaternion finalRotation = currentFrameRotation;

            ApplyOffset(ref finalPosition, ref finalRotation, frameOffset);
            finalPosition = ApplyLimit(finalPosition, targetFramePosition + frameOffset);

            WriteTransform(finalPosition, finalRotation, anchorFrameScale);
            AddMotionTrailPoint(ToWorld(finalPosition));
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

        /// <summary>坐标系（父级或模式）变了：把状态换算到新坐标系，世界位姿保持不变。</summary>
        private void SyncFrame()
        {
            Transform frame = Frame;
            if (frame == previousFrame)
            {
                return;
            }

            if (initialized)
            {
                Vector3 worldPosition = FrameToWorld(previousFrame, currentFramePosition);
                Quaternion worldRotation = FrameToWorldRotation(previousFrame, currentFrameRotation);
                currentFramePosition = WorldToFrame(frame, worldPosition);
                currentFrameRotation = WorldToFrameRotation(frame, worldRotation);
                velocity = Vector3.zero;
                angularVelocity = Vector3.zero;
                previousEuler = currentFrameRotation.eulerAngles;
            }

            previousFrame = frame;
        }

        /// <summary>
        /// 锚点在锚点坐标系里的位姿。锚点本身是父级本地常量，所以 Local 模式下本地量就是坐标系量；
        /// World 模式下要把它抬到世界。
        /// </summary>
        private void GetAnchorFramePose(out Vector3 framePosition, out Quaternion frameRotation, out Vector3 frameScale)
        {
            Vector3 localPosition = hasInitialTransform ? initialLocalPosition : anchorLocalPosition;
            Vector3 localRotation = hasInitialTransform ? initialLocalRotation : anchorLocalRotation;
            frameScale = hasInitialTransform ? initialLocalScale : anchorLocalScale;
            frameRotation = Quaternion.Euler(localRotation);

            if (Frame != null)
            {
                framePosition = localPosition;
                return;
            }

            Transform parent = transform.parent;
            if (parent != null)
            {
                framePosition = parent.TransformPoint(localPosition);
                frameRotation = parent.rotation * frameRotation;
            }
            else
            {
                framePosition = localPosition;
            }
        }

        private Vector3 GetAnchorFramePosition()
        {
            GetAnchorFramePose(out Vector3 framePosition, out _, out _);
            return framePosition;
        }

        private Quaternion GetAnchorFrameRotation()
        {
            GetAnchorFramePose(out _, out Quaternion frameRotation, out _);
            return frameRotation;
        }

        private void GetTargetFramePose(out Vector3 framePosition, out Quaternion frameRotation)
        {
            framePosition = ToFrame(target.position);
            frameRotation = GetTargetFrameRotation();
        }

        private void ApplyOffset(ref Vector3 finalPosition, ref Quaternion finalRotation, Vector3 frameOffset)
        {
            finalPosition += frameOffset;
            finalRotation *= Quaternion.Euler(rotationOffset);
        }

        /// <summary>
        /// 偏移的世界位移（与旧行为逐字一致）：Local = 目标自身轴、含目标缩放；World = 世界方向。
        /// </summary>
        private Vector3 GetWorldOffset()
        {
            if (offsetMode != HoFollowConstraintOffsetMode.Local)
            {
                return positionOffset;
            }

            if (target != null)
            {
                return target.TransformVector(positionOffset);
            }

            return ToWorldRotation(GetAnchorFrameRotation()) * positionOffset;
        }

        /// <summary>
        /// 把偏移换算到锚点坐标系：世界位移除一次坐标系缩放，写回本地时再乘回来，所以世界结果不变。
        /// offsetMode = World 时「世界方向偏移」在父级自转下会被软跟随，这是该模式的固有代价。
        /// </summary>
        private Vector3 GetFrameOffset()
        {
            return WorldToFrameVector(Frame, GetWorldOffset());
        }

        private Quaternion GetTargetFrameRotation()
        {
            if (target == null)
            {
                return GetAnchorFrameRotation();
            }

            switch (rotationMode)
            {
                case HoFollowConstraintRotationMode.Local:
                    // 坐标系模式：目标本地旋转就是坐标系量（旧的世界结果在外面乘回父级旋转，数值相同）。
                    // 世界模式没有坐标系，沿用旧的世界结果。
                    return Frame == null && transform.parent != null
                        ? transform.parent.rotation * target.localRotation
                        : target.localRotation;
                case HoFollowConstraintRotationMode.Target:
                    return GetRawTargetFrameRotation() * targetFrameAnchorRotation;
                case HoFollowConstraintRotationMode.World:
                default:
                    return GetRawTargetFrameRotation();
            }
        }

        private Quaternion GetRawTargetFrameRotation()
        {
            return target != null ? ToFrameRotation(target.rotation) : GetAnchorFrameRotation();
        }

        /// <summary>轴锁定在锚点坐标系里做：锁的是坐标系轴，不是世界轴。</summary>
        private Vector3 ApplyAxisLock(Vector3 desiredPosition, Vector3 anchorFramePosition)
        {
            return new Vector3(
                Mathf.Lerp(desiredPosition.x, anchorFramePosition.x, lockX),
                Mathf.Lerp(desiredPosition.y, anchorFramePosition.y, lockY),
                Mathf.Lerp(desiredPosition.z, anchorFramePosition.z, lockZ));
        }

        private Quaternion FilterRotation(Quaternion desiredRotation, Quaternion anchorFrameRotation)
        {
            Vector3 desiredEuler = NormalizeEuler(desiredRotation.eulerAngles);
            Vector3 anchorEuler = NormalizeEuler(anchorFrameRotation.eulerAngles);

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

        private Quaternion ApplyRotationLock(Quaternion desiredRotation, Quaternion anchorFrameRotation)
        {
            Vector3 desiredEuler = NormalizeEuler(desiredRotation.eulerAngles);
            Vector3 anchorEuler = NormalizeEuler(anchorFrameRotation.eulerAngles);
            Vector3 lockedEuler = new Vector3(
                Mathf.LerpAngle(desiredEuler.x, anchorEuler.x, lockPitch),
                Mathf.LerpAngle(desiredEuler.y, anchorEuler.y, lockYaw),
                Mathf.LerpAngle(desiredEuler.z, anchorEuler.z, lockRoll));
            return Quaternion.Euler(lockedEuler);
        }

        private void UpdatePosition(Vector3 followTarget, float deltaTime)
        {
            // 没有时间步长时直接落到期望位姿；跟随比例为 0 时期望位姿就是锚点，二者等价。
            if (positionFollow <= 0.0f || deltaTime <= 0.0f)
            {
                currentFramePosition = followTarget;
                velocity = Vector3.zero;
                return;
            }

            Vector3 oldPosition = currentFramePosition;
            currentFramePosition = Vector3.SmoothDamp(currentFramePosition, followTarget, ref velocity, ResponseToSmoothTime(response), GetMaxSpeed(maxVelocity), deltaTime);

            if (overshoot > 0.0f)
            {
                Vector3 lead = velocity * (overshoot * deltaTime);
                currentFramePosition += lead;
            }

            if (maxVelocity > 0.0f)
            {
                Vector3 delta = currentFramePosition - oldPosition;
                float maxDelta = maxVelocity * deltaTime;
                if (delta.sqrMagnitude > maxDelta * maxDelta)
                {
                    currentFramePosition = oldPosition + delta.normalized * maxDelta;
                    velocity = (currentFramePosition - oldPosition) / deltaTime;
                }
            }
        }

        private void UpdateRotation(Quaternion followTarget, float deltaTime)
        {
            if (rotationFollow <= 0.0f || deltaTime <= 0.0f)
            {
                currentFrameRotation = followTarget;
                previousEuler = currentFrameRotation.eulerAngles;
                angularVelocity = Vector3.zero;
                return;
            }

            float t = 1.0f - Mathf.Exp(-Mathf.Max(0.0f, response) * deltaTime);
            t = Mathf.Clamp01(t * (1.0f + overshoot));

            Quaternion oldRotation = currentFrameRotation;
            currentFrameRotation = Quaternion.Slerp(currentFrameRotation, followTarget, t);

            if (maxAngularVelocity > 0.0f)
            {
                float angle = Quaternion.Angle(oldRotation, currentFrameRotation);
                float maxAngle = maxAngularVelocity * deltaTime;
                if (angle > maxAngle && angle > 0.0001f)
                {
                    currentFrameRotation = Quaternion.RotateTowards(oldRotation, currentFrameRotation, maxAngle);
                }
            }

            Vector3 euler = currentFrameRotation.eulerAngles;
            angularVelocity = new Vector3(
                Mathf.DeltaAngle(previousEuler.x, euler.x),
                Mathf.DeltaAngle(previousEuler.y, euler.y),
                Mathf.DeltaAngle(previousEuler.z, euler.z)) / deltaTime;
            previousEuler = euler;
        }

        /// <summary>限制形状在锚点坐标系里：球体与坐标系朝向无关，盒体/圆柱跟着坐标系转。</summary>
        private Vector3 ApplyLimit(Vector3 finalPosition, Vector3 center)
        {
            if (!limitEnabled)
            {
                return finalPosition;
            }

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

        /// <summary>把坐标系位姿写回 Transform：有坐标系时写本地量（父级运动刚性传递），否则写世界位姿。</summary>
        private void WriteTransform(Vector3 framePosition, Quaternion frameRotation, Vector3 scale)
        {
            Transform self = transform;
            if (Frame != null)
            {
                self.SetLocalPositionAndRotation(framePosition, frameRotation);
            }
            else
            {
                self.SetPositionAndRotation(framePosition, frameRotation);
            }

            self.localScale = scale;
        }

        private Vector3 ToFrame(Vector3 worldPosition)
        {
            return WorldToFrame(Frame, worldPosition);
        }

        private Vector3 ToWorld(Vector3 framePosition)
        {
            return FrameToWorld(Frame, framePosition);
        }

        private Quaternion ToFrameRotation(Quaternion worldRotation)
        {
            return WorldToFrameRotation(Frame, worldRotation);
        }

        private Quaternion ToWorldRotation(Quaternion frameRotation)
        {
            return FrameToWorldRotation(Frame, frameRotation);
        }

        private static Vector3 WorldToFrame(Transform frame, Vector3 worldPosition)
        {
            return frame != null ? frame.InverseTransformPoint(worldPosition) : worldPosition;
        }

        private static Vector3 FrameToWorld(Transform frame, Vector3 framePosition)
        {
            return frame != null ? frame.TransformPoint(framePosition) : framePosition;
        }

        private static Quaternion WorldToFrameRotation(Transform frame, Quaternion worldRotation)
        {
            return frame != null ? Quaternion.Inverse(frame.rotation) * worldRotation : worldRotation;
        }

        private static Quaternion FrameToWorldRotation(Transform frame, Quaternion frameRotation)
        {
            return frame != null ? frame.rotation * frameRotation : frameRotation;
        }

        /// <summary>世界方向的位移换算成本地位移，带上父级缩放，保证换算回世界后位移不变。</summary>
        private static Vector3 WorldToFrameVector(Transform frame, Vector3 worldVector)
        {
            return frame != null ? frame.InverseTransformVector(worldVector) : worldVector;
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
                Vector3 center = target.position + GetWorldOffset();
                Gizmos.DrawLine(transform.position, center);
                Gizmos.DrawWireSphere(center, 0.06f);
                DrawLimitGizmo(center);
            }

            Gizmos.DrawWireSphere(ToWorld(currentFramePosition), 0.04f);
            DrawMotionTrailGizmo();
            Gizmos.color = oldColor;
        }

        private void DrawLimitGizmo(Vector3 center)
        {
            if (!limitEnabled)
            {
                return;
            }

            Transform frame = Frame;
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.matrix = frame != null
                ? Matrix4x4.TRS(center, frame.rotation, frame.lossyScale)
                : Matrix4x4.TRS(center, Quaternion.identity, Vector3.one);

            switch (limitShape)
            {
                case HoFollowConstraintLimitShape.Box:
                    Gizmos.DrawWireCube(Vector3.zero, limitBoxSize);
                    break;
                case HoFollowConstraintLimitShape.Cylinder:
                    DrawCylinderGizmo(Vector3.zero, Mathf.Max(0.0f, limitRadius), Mathf.Max(0.0f, limitCylinderHeight));
                    break;
                case HoFollowConstraintLimitShape.Sphere:
                default:
                    Gizmos.DrawWireSphere(Vector3.zero, limitRadius);
                    break;
            }

            Gizmos.matrix = previousMatrix;
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
