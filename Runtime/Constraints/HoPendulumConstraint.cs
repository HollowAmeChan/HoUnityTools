using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.Constraints
{
    public enum HoPendulumConstraintUpdateMode
    {
        LateUpdate,
        Update,
        FixedUpdate,
        Manual
    }

    public enum HoPendulumDriveSource
    {
        /// <summary>采样自身 Transform 的运动。液面、惯性晃动用这个。</summary>
        SelfMotion,

        /// <summary>采样父级 Transform 的运动。被驱动的子物体用这个。</summary>
        ParentMotion,

        /// <summary>采样指定锚点 Transform 的运动。</summary>
        AnchorTransform,

        /// <summary>不采样运动，由脚本或手动输入提供加速度。</summary>
        Manual
    }

    /// <summary>
    /// 摆锤约束：把锚点运动解算成一组摆锤参数，再通过绑定写到材质属性、Shader 全局、
    /// UnityEvent 或另一个 Transform。它不写自身 Transform，定位是参数生成器。
    /// <para>
    /// 倾斜部分由加速度驱动：匀速不产生稳态倾斜，恒加速度稳态满足 tanθ = a/g，
    /// 朝向跟随让液面始终趋向世界水平。固有频率由「液面响应频率」独立给出，
    /// 「摆长」只决定摆锤可视化位置与离心项半径，勾选「摆长决定频率」时才换算成物理单摆频率。
    /// </para>
    /// <para>
    /// 竖直方向的伸缩由径向弹簧描述：静止伸长 ΔL 同时给出静息伸长量与固有频率 √(g/ΔL)。
    /// </para>
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("HoUnityTools/Constraints/Ho Pendulum Constraint")]
    public sealed class HoPendulumConstraint : MonoBehaviour
    {
        private const int RendererBlockPruneThreshold = 64;

        /// <summary>角速度低通的时间常数（毫秒）。</summary>
        private const float AngularVelocitySmoothingMilliseconds = 30.0f;

        [Header("Update")]
        [SerializeField]
        private HoPendulumConstraintUpdateMode updateMode = HoPendulumConstraintUpdateMode.LateUpdate;

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

        [Header("Anchor")]
        [SerializeField]
        private HoPendulumDriveSource driveSource = HoPendulumDriveSource.SelfMotion;

        [SerializeField]
        private Transform anchor;

        [Header("Pendulum")]
        [SerializeField, Min(0.0f)]
        private float length = 0.25f;

        [SerializeField]
        private bool frequencyFromLength;

        [SerializeField, Range(HoPendulumSolver.MinFrequency, HoPendulumSolver.MaxFrequency)]
        private float frequency = 3.0f;

        [SerializeField, Range(0.0f, 2.0f)]
        private float dampingRatio = 0.18f;

        [SerializeField, Range(HoPendulumSolver.MinMaxAngle, HoPendulumSolver.MaxMaxAngle)]
        private float maxAngle = 20.0f;

        [SerializeField, Range(0.0f, 1.0f)]
        private float saturationSoftness = 0.65f;

        [SerializeField, Range(0.0f, 10.0f)]
        private float sensitivity = 1.0f;

        [SerializeField, Range(0.0f, 1.0f)]
        private float gravityInfluence = 1.0f;

        [SerializeField, Range(0.0f, 1.0f)]
        private float centrifugalInfluence = 1.0f;

        [SerializeField, Min(0.001f)]
        private float referenceGravity = 9.81f;

        [Header("Stretch")]
        [SerializeField]
        private bool radialEnabled = true;

        [SerializeField, Min(0.0f)]
        private float restElongation = 0.02f;

        [SerializeField, Range(0.0f, 2.0f)]
        private float radialDampingRatio = 0.25f;

        [Header("Sampling")]
        [SerializeField, Range(HoMotionEstimator.MinWindow, HoMotionEstimator.MaxWindow)]
        private int estimationWindow = HoMotionEstimator.DefaultWindow;

        [SerializeField, Range(0.0f, 500.0f)]
        private float equilibriumSmoothing = 20.0f;

        [SerializeField, Range(1.0f / 2000.0f, 0.5f)]
        private float maxStep = 1.0f / 240.0f;

        [Header("Manual Input")]
        [SerializeField]
        private Vector3 manualAcceleration = Vector3.zero;

        [SerializeField]
        private float inputValue;

        [SerializeField]
        private float sourceValueMin;

        [SerializeField]
        private float sourceValueMax = 1.0f;

        [SerializeField, Min(0.0f)]
        private float inputAcceleration = 9.81f;

        [SerializeField]
        private Vector3 inputAxis = Vector3.right;

        [Header("Output")]
        [SerializeField]
        private Material sharedMaterial;

        [SerializeField]
        private bool writeToSharedMaterial;

        [SerializeField]
        private List<HoPendulumBinding> bindings = new List<HoPendulumBinding>();

        [Header("Debug")]
        [SerializeField]
        private bool drawGizmos = true;

        [SerializeField]
        private bool drawSwingPlane = true;

        [SerializeField]
        private bool drawEquilibrium = true;

        [SerializeField]
        private bool drawAcceleration = true;

        [SerializeField]
        private bool drawMotionTrail;

        [SerializeField, Range(4, 128)]
        private int motionTrailLength = 32;

        [SerializeField]
        private bool gizmoSizeFromBounds = true;

        [SerializeField, Min(0.001f)]
        private float gizmoSize = 0.5f;

        [SerializeField, Min(0.01f)]
        private float gizmoScale = 1.0f;

        [SerializeField, Range(0.0f, 5.0f)]
        private float accelerationGizmoScale = 0.5f;

        [SerializeField, Range(0.1f, 10.0f)]
        private float accelerationGizmoMaxScale = 2.0f;

        [SerializeField]
        private Color gizmoColor = new Color(0.55f, 0.85f, 1.0f, 0.85f);

        [SerializeField]
        private Color swingColor = new Color(1.0f, 0.72f, 0.3f, 0.9f);

        [SerializeField]
        private Color equilibriumColor = new Color(0.45f, 1.0f, 0.62f, 0.7f);

        [SerializeField]
        private Color accelerationColor = new Color(1.0f, 0.35f, 0.35f, 0.9f);

        private readonly Dictionary<Renderer, MaterialPropertyBlock> rendererBlocks =
            new Dictionary<Renderer, MaterialPropertyBlock>();

        private readonly List<Renderer> rendererOrder = new List<Renderer>();

        private readonly HashSet<Renderer> rendererTouched = new HashSet<Renderer>();

        private HoPendulumSolver solver;
        private HoPendulumSolverOutput solverOutput;
        private HoMotionEstimator motionEstimator;
        private Quaternion previousRotation = Quaternion.identity;
        private Vector3 linearVelocity;
        private Vector3 linearAcceleration;
        private Vector3 angularVelocity;
        private Vector3 localAcceleration;
        private Vector3 localUp = Vector3.up;
        private Vector3 localAngularVelocity;
        private Vector3 anchorPosition;
        private Quaternion anchorRotation = Quaternion.identity;
        private Vector3 localBobOffset;
        private Vector3 bobPosition;
        private Vector3 swingDirection = Vector3.down;
        private float tiltX;
        private float tiltZ;
        private float totalAngle;
        private float angularSpeed;
        private float bobDistance;
        private float normalizedValue = 0.5f;
        private double lastUpdateTime;
        private double motionTime;
        private bool initialized;
        private Vector3[] motionTrail;
        private int motionTrailIndex;
        private int motionTrailCount;

        public HoPendulumConstraintUpdateMode UpdateMode
        {
            get => updateMode;
            set => updateMode = value;
        }

        public HoPendulumDriveSource DriveSource
        {
            get => driveSource;
            set => driveSource = value;
        }

        public Transform Anchor
        {
            get => anchor;
            set => anchor = value;
        }

        public float Length
        {
            get => length;
            set => length = Mathf.Max(0.0f, value);
        }

        public float Frequency
        {
            get => frequency;
            set => frequency = Mathf.Clamp(value, HoPendulumSolver.MinFrequency, HoPendulumSolver.MaxFrequency);
        }

        public bool HasInitialTransform => hasInitialTransform;

        public Vector3 AnchorPosition => anchorPosition;

        public Quaternion AnchorRotation => anchorRotation;

        public Vector3 AnchorVelocity => linearVelocity;

        public Vector3 AnchorAcceleration => linearAcceleration;

        public Vector3 AnchorAngularVelocity => angularVelocity;

        public float AngleX => solverOutput.angleX;

        public float AngleZ => solverOutput.angleZ;

        public float Angle => totalAngle;

        public float EquilibriumAngleX => solverOutput.equilibriumAngleX;

        public float EquilibriumAngleZ => solverOutput.equilibriumAngleZ;

        public float TiltX => tiltX;

        public float TiltZ => tiltZ;

        public float Amplitude => solverOutput.amplitude;

        public float Phase => solverOutput.phase;

        public float NormalizedValue => normalizedValue;

        public float EffectiveGravity => solverOutput.effectiveGravity;

        public float Stretch => solverOutput.radialOffset;

        public float BobDistance => bobDistance;

        public bool Saturated => solverOutput.saturated;

        public Vector3 LocalNormal => solverOutput.localNormal;

        public Vector3 LocalEquilibriumNormal => solverOutput.localEquilibriumNormal;

        public Vector3 Swing => swingDirection;

        public Vector3 BobOffset => localBobOffset;

        public Vector3 BobPosition => bobPosition;

        public IReadOnlyList<HoPendulumBinding> Bindings => bindings;

        /// <summary>调试绘制当前使用的参考尺寸（世界单位，米）。</summary>
        public float GizmoReferenceSize => ResolveGizmoReferenceSize(GetDriveTransform());

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
            if (updateMode == HoPendulumConstraintUpdateMode.Update)
            {
                EvaluateWithCurrentDelta();
            }
        }

        private void LateUpdate()
        {
            if (updateMode == HoPendulumConstraintUpdateMode.LateUpdate)
            {
                EvaluateWithCurrentDelta();
            }
        }

        private void FixedUpdate()
        {
            if (updateMode == HoPendulumConstraintUpdateMode.FixedUpdate)
            {
                Evaluate(Time.fixedDeltaTime);
            }
        }

        private void OnValidate()
        {
            length = Mathf.Max(0.0f, HoPendulumSolverInput.SanitizeFloat(length));
            frequency = Mathf.Clamp(
                HoPendulumSolverInput.SanitizeFloat(frequency),
                HoPendulumSolver.MinFrequency,
                HoPendulumSolver.MaxFrequency);
            dampingRatio = Mathf.Clamp(HoPendulumSolverInput.SanitizeFloat(dampingRatio), 0.0f, 2.0f);
            maxAngle = Mathf.Clamp(
                HoPendulumSolverInput.SanitizeFloat(maxAngle),
                HoPendulumSolver.MinMaxAngle,
                HoPendulumSolver.MaxMaxAngle);
            saturationSoftness = Mathf.Clamp01(HoPendulumSolverInput.SanitizeFloat(saturationSoftness));
            sensitivity = Mathf.Clamp(HoPendulumSolverInput.SanitizeFloat(sensitivity), 0.0f, 10.0f);
            gravityInfluence = Mathf.Clamp01(HoPendulumSolverInput.SanitizeFloat(gravityInfluence));
            centrifugalInfluence = Mathf.Clamp01(HoPendulumSolverInput.SanitizeFloat(centrifugalInfluence));
            referenceGravity = Mathf.Clamp(HoPendulumSolverInput.SanitizeFloat(referenceGravity), 0.001f, 1000.0f);
            restElongation = Mathf.Clamp(HoPendulumSolverInput.SanitizeFloat(restElongation), 0.0f, 100.0f);
            radialDampingRatio = Mathf.Clamp(HoPendulumSolverInput.SanitizeFloat(radialDampingRatio), 0.0f, 2.0f);
            estimationWindow = Mathf.Clamp(estimationWindow, HoMotionEstimator.MinWindow, HoMotionEstimator.MaxWindow);
            equilibriumSmoothing = Mathf.Clamp(HoPendulumSolverInput.SanitizeFloat(equilibriumSmoothing), 0.0f, 500.0f);
            maxStep = Mathf.Clamp(HoPendulumSolverInput.SanitizeFloat(maxStep), 1.0f / 2000.0f, 0.5f);
            inputAcceleration = Mathf.Max(0.0f, HoPendulumSolverInput.SanitizeFloat(inputAcceleration));
            initialLocalScale = Max(initialLocalScale, Vector3.zero);
            gizmoSize = Mathf.Max(0.001f, HoPendulumSolverInput.SanitizeFloat(gizmoSize));
            gizmoScale = Mathf.Max(0.01f, HoPendulumSolverInput.SanitizeFloat(gizmoScale));
            accelerationGizmoScale = Mathf.Clamp(HoPendulumSolverInput.SanitizeFloat(accelerationGizmoScale), 0.0f, 5.0f);
            accelerationGizmoMaxScale = Mathf.Clamp(HoPendulumSolverInput.SanitizeFloat(accelerationGizmoMaxScale), 0.1f, 10.0f);
            motionTrailLength = Mathf.Clamp(motionTrailLength, 4, 128);
            EnsureMotionTrail();
            ResetBindingCaches();
        }

        /// <summary>把采样基准与摆锤状态复位到当前位姿，不会产生启用瞬间的抖动。</summary>
        public void ResetState()
        {
            Transform drive = GetDriveTransform();
            if (drive != null)
            {
                previousRotation = drive.rotation;
            }

            motionEstimator.EnsureCapacity(estimationWindow);
            motionEstimator.Reset();
            motionTime = 0.0;
            if (drive != null)
            {
                motionEstimator.Push(drive.position, motionTime);
                motionEstimator.Estimate();
            }

            linearVelocity = Vector3.zero;
            linearAcceleration = Vector3.zero;
            angularVelocity = Vector3.zero;
            localAcceleration = Vector3.zero;
            localAngularVelocity = Vector3.zero;

            solver.Reset();
            solverOutput = new HoPendulumSolverOutput();
            tiltX = 0.0f;
            tiltZ = 0.0f;
            totalAngle = 0.0f;
            angularSpeed = 0.0f;
            bobDistance = length;

            if (drive != null)
            {
                anchorPosition = drive.position;
                anchorRotation = drive.rotation;
                UpdateDerivedPose();
            }

            lastUpdateTime = GetTime();
            initialized = true;
            ClearMotionTrail();
            ResetBindingCaches();
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

        /// <summary>取通道当前值。标量通道只占用 x 分量。</summary>
        public Vector3 GetChannelValue(HoPendulumChannel channel)
        {
            switch (channel)
            {
                case HoPendulumChannel.TiltX:
                    return new Vector3(tiltX, 0.0f, 0.0f);
                case HoPendulumChannel.TiltZ:
                    return new Vector3(tiltZ, 0.0f, 0.0f);
                case HoPendulumChannel.Tilt:
                    return new Vector3(tiltX, 0.0f, tiltZ);
                case HoPendulumChannel.AngleX:
                    return new Vector3(solverOutput.angleX, 0.0f, 0.0f);
                case HoPendulumChannel.AngleZ:
                    return new Vector3(solverOutput.angleZ, 0.0f, 0.0f);
                case HoPendulumChannel.Angle:
                    return new Vector3(totalAngle, 0.0f, 0.0f);
                case HoPendulumChannel.TiltEuler:
                    // 把局部 up 转到当前液面法线所需的欧拉角：绕 Z 轴 -AngleX、绕 X 轴 +AngleZ。
                    return new Vector3(solverOutput.angleZ, 0.0f, -solverOutput.angleX);
                case HoPendulumChannel.Swing:
                    return swingDirection;
                case HoPendulumChannel.BobOffset:
                    return localBobOffset;
                case HoPendulumChannel.BobPosition:
                    return bobPosition;
                case HoPendulumChannel.Amplitude:
                    return new Vector3(solverOutput.amplitude, 0.0f, 0.0f);
                case HoPendulumChannel.Phase:
                    return new Vector3(solverOutput.phase, 0.0f, 0.0f);
                case HoPendulumChannel.Normalized:
                    return new Vector3(normalizedValue, 0.0f, 0.0f);
                case HoPendulumChannel.EffectiveGravity:
                    return new Vector3(solverOutput.effectiveGravity, 0.0f, 0.0f);
                case HoPendulumChannel.Stretch:
                    return new Vector3(solverOutput.radialOffset, 0.0f, 0.0f);
                case HoPendulumChannel.AnchorSpeed:
                    return new Vector3(linearVelocity.magnitude, 0.0f, 0.0f);
                case HoPendulumChannel.AnchorAngularSpeed:
                    return new Vector3(angularSpeed, 0.0f, 0.0f);
                case HoPendulumChannel.AnchorAcceleration:
                    return linearAcceleration;
                default:
                    return Vector3.zero;
            }
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

            Transform drive = GetDriveTransform();
            if (drive == null)
            {
                return;
            }

            float safeDeltaTime = Mathf.Max(0.0f, HoPendulumSolverInput.SanitizeFloat(deltaTime));
            motionTime += safeDeltaTime;
            SampleMotion(drive, safeDeltaTime);

            anchorPosition = drive.position;
            anchorRotation = drive.rotation;

            solver.Step(BuildSolverInput(), safeDeltaTime, out solverOutput);
            UpdateDerivedPose();
            ApplyBindings(drive);
            AddMotionTrailPoint(bobPosition);
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

        private Transform GetDriveTransform()
        {
            switch (driveSource)
            {
                case HoPendulumDriveSource.ParentMotion:
                    return transform.parent != null ? transform.parent : transform;
                case HoPendulumDriveSource.AnchorTransform:
                    return anchor != null ? anchor : transform;
                case HoPendulumDriveSource.SelfMotion:
                case HoPendulumDriveSource.Manual:
                default:
                    return transform;
            }
        }

        private void SampleMotion(Transform drive, float deltaTime)
        {
            Vector3 position = drive.position;
            Quaternion rotation = drive.rotation;

            // 速度与加速度由最近若干帧位置的二次最小二乘拟合给出。
            // 逐帧二阶差分在 20Hz 采样、位置量化、编辑器拖拽这些工况下会放大出几十 m/s² 的
            // 加速度噪声，直接表现成液面抽搐；拟合对匀速与恒加速都是精确的，噪声增益低得多。
            // 时间戳用累计的 deltaTime，而不是实时时钟：Evaluate(dt) 被脚本以固定步长驱动时，
            // 两者必须同一个时间基准，否则拟合出来的加速度会被真实帧间隔污染。
            motionEstimator.EnsureCapacity(estimationWindow);
            motionEstimator.Push(position, motionTime);
            motionEstimator.Estimate();

            linearVelocity = HoPendulumSolverInput.SanitizeVector(motionEstimator.Velocity);
            linearAcceleration = HoPendulumSolverInput.SanitizeVector(motionEstimator.Acceleration);

            if (deltaTime > 0.0f)
            {
                Quaternion delta = rotation * Quaternion.Inverse(previousRotation);
                delta.ToAngleAxis(out float angle, out Vector3 axis);
                Vector3 instantAngularVelocity = Vector3.zero;
                if (!float.IsNaN(axis.x) && !float.IsNaN(axis.y) && !float.IsNaN(axis.z) && !float.IsNaN(angle))
                {
                    if (angle > 180.0f)
                    {
                        angle -= 360.0f;
                    }

                    instantAngularVelocity = axis * (angle * Mathf.Deg2Rad / deltaTime);
                }

                // 角速度没有对应的拟合通道（一次拟合只给到角速度本身），仍用一阶低通。
                instantAngularVelocity = HoPendulumSolverInput.SanitizeVector(instantAngularVelocity);
                angularVelocity = Vector3.Lerp(
                    angularVelocity,
                    instantAngularVelocity,
                    SmoothingBlend(AngularVelocitySmoothingMilliseconds, deltaTime));
            }

            previousRotation = rotation;
        }

        private HoPendulumSolverInput BuildSolverInput()
        {
            Quaternion inverseRotation = Quaternion.Inverse(anchorRotation);
            Vector3 acceleration;

            if (driveSource == HoPendulumDriveSource.Manual)
            {
                acceleration = manualAcceleration + inputAxis.normalized * (GetNormalizedInput() * inputAcceleration);
            }
            else
            {
                acceleration = linearAcceleration;
            }

            localAcceleration = HoPendulumSolverInput.SanitizeVector(inverseRotation * acceleration);
            localUp = HoPendulumSolverInput.SanitizeVector(inverseRotation * Vector3.up);
            localAngularVelocity = HoPendulumSolverInput.SanitizeVector(inverseRotation * angularVelocity);

            HoPendulumSolverInput input = HoPendulumSolverInput.CreateDefault();
            input.localAcceleration = localAcceleration;
            input.localUp = localUp;
            input.localAngularVelocity = localAngularVelocity;
            input.length = length;
            input.frequency = ResolveFrequency();
            input.dampingRatio = dampingRatio;
            input.maxAngle = maxAngle;
            input.sensitivity = sensitivity;
            input.gravity = referenceGravity;
            input.gravityInfluence = gravityInfluence;
            input.centrifugalInfluence = centrifugalInfluence;
            input.maxStep = maxStep;
            input.saturationSoftness = saturationSoftness;
            input.equilibriumSmoothing = equilibriumSmoothing * 0.001f;
            input.radialEnabled = radialEnabled;
            input.restElongation = restElongation;
            input.radialDampingRatio = radialDampingRatio;
            input.Sanitize();
            return input;
        }

        private float ResolveFrequency()
        {
            if (!frequencyFromLength)
            {
                return frequency;
            }

            float derived = HoPendulumSolver.FrequencyFromLength(length, referenceGravity);
            return derived > 0.0f ? derived : frequency;
        }

        private float GetNormalizedInput()
        {
            float range = sourceValueMax - sourceValueMin;
            if (Mathf.Abs(range) < 0.000001f)
            {
                return 0.0f;
            }

            return Mathf.Clamp01((inputValue - sourceValueMin) / range);
        }

        private void UpdateDerivedPose()
        {
            tiltX = TanDegrees(solverOutput.angleX);
            tiltZ = TanDegrees(solverOutput.angleZ);
            totalAngle = solverOutput.tiltMagnitude;
            angularSpeed = angularVelocity.magnitude * Mathf.Rad2Deg;

            bobDistance = Mathf.Max(0.0f, length + solverOutput.radialOffset);

            Vector3 localBobDirection = -solverOutput.localNormal;
            if (localBobDirection.sqrMagnitude < 0.000001f)
            {
                localBobDirection = Vector3.down;
            }

            localBobDirection.Normalize();
            localBobOffset = localBobDirection * bobDistance;
            bobPosition = anchorPosition + anchorRotation * localBobOffset;
            swingDirection = anchorRotation * localBobDirection;

            if (driveSource == HoPendulumDriveSource.Manual)
            {
                normalizedValue = GetNormalizedInput();
            }
            else
            {
                float safeMaxAngle = Mathf.Max(0.0001f, maxAngle);
                normalizedValue = 0.5f + 0.5f * Mathf.Clamp(solverOutput.angleX / safeMaxAngle, -1.0f, 1.0f);
            }
        }

        private void ApplyBindings(Transform drive)
        {
            if (bindings == null || bindings.Count == 0)
            {
                return;
            }

            rendererOrder.Clear();
            rendererTouched.Clear();
            PruneRendererBlocks();

            for (int i = 0; i < bindings.Count; i++)
            {
                HoPendulumBinding binding = bindings[i];
                if (binding == null || !binding.Enabled)
                {
                    continue;
                }

                Vector3 value = binding.ApplyScaleAndBias(GetChannelValue(binding.Channel));
                switch (binding.Target)
                {
                    case HoPendulumBindingTarget.RendererProperty:
                        ApplyRendererBinding(binding, value);
                        break;
                    case HoPendulumBindingTarget.ShaderGlobal:
                        binding.ApplyToGlobal(value);
                        break;
                    case HoPendulumBindingTarget.Event:
                        binding.ApplyToEvent(value);
                        break;
                    case HoPendulumBindingTarget.Transform:
                        binding.ApplyToTransform(drive, value);
                        break;
                }
            }

            for (int i = 0; i < rendererOrder.Count; i++)
            {
                Renderer target = rendererOrder[i];
                if (target == null)
                {
                    continue;
                }

                target.SetPropertyBlock(rendererBlocks[target]);
            }
        }

        private void ApplyRendererBinding(HoPendulumBinding binding, Vector3 value)
        {
            Renderer[] targets = binding.ResolveRenderers(transform);
            for (int i = 0; i < targets.Length; i++)
            {
                Renderer target = targets[i];
                if (target == null)
                {
                    continue;
                }

                if (!rendererBlocks.TryGetValue(target, out MaterialPropertyBlock block))
                {
                    block = new MaterialPropertyBlock();
                    rendererBlocks.Add(target, block);
                }

                if (rendererTouched.Add(target))
                {
                    // 每帧只读取一次渲染器现有属性块，既保留其他组件写入的属性，
                    // 也避免同一帧内后一条绑定把前一条的值覆盖回旧值。
                    target.GetPropertyBlock(block);
                    rendererOrder.Add(target);
                }

                binding.ApplyToPropertyBlock(block, value);
            }

            if (writeToSharedMaterial && sharedMaterial != null && !string.IsNullOrEmpty(binding.PropertyName))
            {
                sharedMaterial.SetFloat(binding.PropertyName, value.x);
            }
        }

        private void PruneRendererBlocks()
        {
            if (rendererBlocks.Count <= RendererBlockPruneThreshold)
            {
                return;
            }

            var stale = new List<Renderer>();
            foreach (KeyValuePair<Renderer, MaterialPropertyBlock> pair in rendererBlocks)
            {
                if (pair.Key == null)
                {
                    stale.Add(pair.Key);
                }
            }

            for (int i = 0; i < stale.Count; i++)
            {
                rendererBlocks.Remove(stale[i]);
            }
        }

        private void ResetBindingCaches()
        {
            if (bindings == null)
            {
                return;
            }

            for (int i = 0; i < bindings.Count; i++)
            {
                bindings[i]?.ResetRuntimeCache();
            }

            rendererBlocks.Clear();
            rendererOrder.Clear();
            rendererTouched.Clear();
        }

        /// <summary>编辑器与预设使用：追加一条渲染器属性绑定。</summary>
        public HoPendulumBinding AddRendererPropertyBinding(
            HoPendulumChannel channel,
            string propertyName,
            float scale,
            Vector3 bias,
            string label)
        {
            if (bindings == null)
            {
                bindings = new List<HoPendulumBinding>();
            }

            HoPendulumBinding binding = new HoPendulumBinding();
            binding.ConfigureRendererProperty(channel, propertyName, scale, bias, label);
            bindings.Add(binding);
            return binding;
        }

        /// <summary>编辑器与预设使用：清空所有绑定。</summary>
        public void ClearBindings()
        {
            if (bindings == null)
            {
                bindings = new List<HoPendulumBinding>();
                return;
            }

            bindings.Clear();
            rendererBlocks.Clear();
            rendererOrder.Clear();
            rendererTouched.Clear();
        }

        /// <summary>毫秒时间常数的一阶低通系数。</summary>
        private static float SmoothingBlend(float milliseconds, float deltaTime)
        {
            if (milliseconds <= 0.0f)
            {
                return 1.0f;
            }

            return 1.0f - Mathf.Exp(-deltaTime / (milliseconds * 0.001f));
        }

        private static float TanDegrees(float degrees)
        {
            float clamped = Mathf.Clamp(degrees, -89.0f, 89.0f);
            return Mathf.Tan(clamped * Mathf.Deg2Rad);
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

        /// <summary>
        /// 调试绘制的参考尺寸。与摆长解耦：摆长是可视化/离心参数，把它当成 Gizmo 基准会导致
        /// 改摆长（甚至设成 0）时整套调试绘制跟着塌掉。默认按驱动源的渲染器包围盒取最长边；
        /// 没有渲染器或包围盒退化（例如渲染器还没挂网格）时回退到手动尺寸，避免塌成 0。
        /// </summary>
        private float ResolveGizmoReferenceSize(Transform drive)
        {
            float baseSize = gizmoSize;

            if (gizmoSizeFromBounds)
            {
                Transform owner = drive != null ? drive : transform;
                if (owner != null)
                {
                    Renderer[] renderers = owner.GetComponentsInChildren<Renderer>(true);
                    bool hasBounds = false;
                    Bounds bounds = default;
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        Renderer renderer = renderers[i];
                        if (renderer == null)
                        {
                            continue;
                        }

                        if (!hasBounds)
                        {
                            bounds = renderer.bounds;
                            hasBounds = true;
                        }
                        else
                        {
                            bounds.Encapsulate(renderer.bounds);
                        }
                    }

                    if (hasBounds)
                    {
                        Vector3 size = bounds.size;
                        float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
                        if (longest > 0.0001f)
                        {
                            baseSize = longest;
                        }
                    }
                }
            }

            return Mathf.Max(0.001f, baseSize) * Mathf.Max(0.01f, gizmoScale);
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos)
            {
                return;
            }

            Transform drive = GetDriveTransform();
            if (drive == null)
            {
                return;
            }

            bool live = Application.isPlaying || initialized;
            Vector3 origin = live ? anchorPosition : drive.position;
            Quaternion rotation = live ? anchorRotation : drive.rotation;
            Vector3 normal = solverOutput.localNormal.sqrMagnitude < 0.000001f ? Vector3.up : solverOutput.localNormal;
            Vector3 equilibriumNormal = solverOutput.localEquilibriumNormal.sqrMagnitude < 0.000001f
                ? Vector3.up
                : solverOutput.localEquilibriumNormal;

            // 全部尺寸都从参考尺寸派生，与摆长无关。
            float size = ResolveGizmoReferenceSize(drive);
            float sphereRadius = size * 0.05f;
            float discRadius = size * 0.4f;
            float normalLength = size * 0.5f;

            // 摆锤位置仍然用真实摆长（这是会输出到 BobPosition 的位置）。
            float distance = Mathf.Max(0.0f, length + solverOutput.radialOffset);
            Vector3 bob = origin + rotation * (-normal * distance);

            Color oldColor = Gizmos.color;
            Gizmos.color = gizmoColor;
            Gizmos.DrawWireSphere(origin, sphereRadius);
            Gizmos.DrawLine(origin, bob);
            Gizmos.DrawWireSphere(bob, sphereRadius * 0.9f);

            if (drawSwingPlane)
            {
                DrawPlaneGizmo(origin, rotation, normal, discRadius, swingColor);
            }

            if (drawEquilibrium)
            {
                DrawPlaneGizmo(origin, rotation, equilibriumNormal, discRadius * 1.08f, equilibriumColor);
                Gizmos.color = equilibriumColor;
                Gizmos.DrawLine(origin, origin + rotation * equilibriumNormal * normalLength);
            }

            Gizmos.color = swingColor;
            Gizmos.DrawLine(origin, origin + rotation * normal * normalLength);

            if (drawAcceleration && accelerationGizmoScale > 0.0f)
            {
                Vector3 acceleration = live ? linearAcceleration : Vector3.zero;
                float gaugeLength = size * accelerationGizmoScale;
                Vector3 arrow = acceleration * (gaugeLength / Mathf.Max(0.001f, referenceGravity));

                // 拖拽/急停时加速度本来就有几十 g，箭头会顶到截断值；截断值可调，默认 2 倍参考尺寸。
                float maxLength = size * accelerationGizmoMaxScale;
                if (arrow.magnitude > maxLength)
                {
                    arrow = arrow.normalized * maxLength;
                }

                if (arrow.magnitude > size * 0.02f)
                {
                    DrawArrowGizmo(
                        origin,
                        arrow,
                        accelerationColor,
                        Mathf.Max(size * 0.01f, arrow.magnitude * 0.25f),
                        gaugeLength);
                }
            }

            Gizmos.color = gizmoColor;
            DrawMotionTrailGizmo();
            Gizmos.color = oldColor;
        }

        private static void DrawPlaneGizmo(Vector3 origin, Quaternion rotation, Vector3 normal, float radius, Color color)
        {
            if (normal.sqrMagnitude < 0.000001f)
            {
                normal = Vector3.up;
            }

            Color previous = Gizmos.color;
            Gizmos.color = color;

            Matrix4x4 oldMatrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(origin, rotation * Quaternion.FromToRotation(Vector3.up, normal), Vector3.one);
            DrawCircleGizmo(radius, 48);
            Gizmos.matrix = oldMatrix;

            Gizmos.color = previous;
        }

        private static void DrawArrowGizmo(Vector3 origin, Vector3 offset, Color color, float headLength, float gaugeLength)
        {
            Color previous = Gizmos.color;
            Gizmos.color = color;

            Vector3 tip = origin + offset;
            Gizmos.DrawLine(origin, tip);

            Vector3 direction = offset.normalized;
            Vector3 side = Vector3.Cross(direction, Vector3.up);
            if (side.sqrMagnitude < 0.0001f)
            {
                side = Vector3.Cross(direction, Vector3.right);
            }

            side.Normalize();
            Vector3 back = tip - direction * headLength;
            Gizmos.DrawLine(tip, back + side * headLength * 0.5f);
            Gizmos.DrawLine(tip, back - side * headLength * 0.5f);

            // 1g 刻度：箭头长度以它为基准，一眼能看出"几倍重力"。
            if (gaugeLength > 0.0f && gaugeLength < offset.magnitude)
            {
                Vector3 gauge = origin + direction * gaugeLength;
                float halfWidth = Mathf.Max(headLength * 0.6f, offset.magnitude * 0.05f);
                Gizmos.DrawLine(gauge - side * halfWidth, gauge + side * halfWidth);
            }

            Gizmos.color = previous;
        }

        private static void DrawCircleGizmo(float radius, int segmentCount)
        {
            float step = Mathf.PI * 2.0f / Mathf.Max(3, segmentCount);
            Vector3 previous = new Vector3(radius, 0.0f, 0.0f);
            for (int i = 1; i <= segmentCount; i++)
            {
                float angle = step * i;
                Vector3 current = new Vector3(Mathf.Cos(angle) * radius, 0.0f, Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(previous, current);
                previous = current;
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
