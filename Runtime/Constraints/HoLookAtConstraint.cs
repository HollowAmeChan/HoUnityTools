using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.Constraints
{
    /// <summary>
    /// 注视约束：角色看向一个物体或鼠标。三大块可以独立开关：
    /// ① 脊椎跟随（Unity 的 bodyWeight，沿脊柱分摊）、② 头颈跟随（headWeight）、③ 眼睛跟随（四条方向曲线 → 形态键 / 眼球骨骼）。
    ///
    /// 时序切成两段（见 docs/LOOKAT_CONSTRAINT.md）：
    ///   OnAnimatorIK  —— 算目标方向、死区/分工/限位，把"头部承担方向"交给 Unity 的 LookAt IK，缓存眼睛残余角；
    ///   LateUpdate    —— 用残余角写眼睛（形态键与眼球骨骼），必须晚于头部 IK 的结果。
    /// 只支持 humanoid（Avatar + 图层 IK Pass）；眼睛形态键部分不依赖 humanoid。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("HoUnityTools/Constraints/Ho Look At Constraint")]
    public sealed class HoLookAtConstraint : MonoBehaviour, IHoShapeKeyMeshProvider
    {
        private const int ChannelCount = 6;

        [Header("Target")]
        [SerializeField]
        private HoLookAtMode targetMode = HoLookAtMode.Transform;

        [SerializeField]
        private Transform target;

        [SerializeField]
        private Vector3 targetOffset;

        [SerializeField]
        private HoLookAtInputSource inputSource = HoLookAtInputSource.Auto;

        [SerializeField]
        private Camera mouseCamera;

        [SerializeField]
        private HoLookAtMouseSampleMode mouseSampleMode = HoLookAtMouseSampleMode.AngleMap;

        [SerializeField]
        private Vector2 mouseSensitivity = new Vector2(30.0f, 20.0f);

        [SerializeField, Range(0.0f, 0.45f)]
        private float mouseDeadZone = 0.05f;

        [SerializeField, Min(0.1f)]
        private float mouseDistance = 3.0f;

        [SerializeField]
        private bool mouseProjectToPlane;

        [SerializeField]
        private float mousePlaneHeight;

        [SerializeField]
        private LayerMask mouseRaycastMask = ~0;

        [SerializeField]
        private bool mouseHoldOffscreen = true;

        [Header("Common")]
        [SerializeField]
        private Animator animator;

        [SerializeField]
        private Transform reference;

        [SerializeField, Range(0.0f, 1.0f)]
        private float weight = 1.0f;

        [SerializeField]
        private bool evaluateInEditMode = false;

        [Header("Spine")]
        [SerializeField]
        private bool spineEnabled = true;

        [SerializeField, Range(0.0f, 1.0f)]
        private float bodyWeight = 0.3f;

        [SerializeField, Range(0.0f, 90.0f)]
        private float spineMinAngle;

        [Header("Head")]
        [SerializeField]
        private bool headEnabled = true;

        [SerializeField, Range(0.0f, 1.0f)]
        private float headWeight = 1.0f;

        [SerializeField, Range(0.0f, 1.0f)]
        private float clampWeight = 0.6f;

        [SerializeField, Range(0.0f, 89.0f)]
        private float deadZone = 8.0f;

        [SerializeField, Range(0.0f, 1.0f)]
        private float headShare = 0.7f;

        [SerializeField, Range(0.0f, 179.0f)]
        private float yawLimit = 70.0f;

        [SerializeField, Range(0.0f, 179.0f)]
        private float pitchLimitUp = 40.0f;

        [SerializeField, Range(0.0f, 179.0f)]
        private float pitchLimitDown = 30.0f;

        [SerializeField, Min(0.0f)]
        private float aimSmoothing = 0.06f;

        [SerializeField, Min(0.0f)]
        private float aimMaxSpeed = 360.0f;

        [Header("Eyes")]
        [SerializeField]
        private bool eyesEnabled = true;

        [SerializeField]
        private List<Renderer> renderers = new List<Renderer>();

        [SerializeField, Range(0.0f, 1.0f)]
        private float eyeWeight = 1.0f;

        [SerializeField, Min(0.0f)]
        private float eyeSmoothing = 0.04f;

        [SerializeField]
        private bool eyeEllipseClamp = true;

        [SerializeField, Range(0.0f, 1.0f)]
        private float eyesWeightToUnity;

        [SerializeField]
        private AnimationCurve horizontalInner = AnimationCurve.Linear(0.0f, 0.0f, 1.0f, 1.0f);

        [SerializeField]
        private AnimationCurve horizontalOuter = AnimationCurve.Linear(0.0f, 0.0f, 1.0f, 1.0f);

        [SerializeField]
        private AnimationCurve verticalUp = AnimationCurve.Linear(0.0f, 0.0f, 1.0f, 1.0f);

        [SerializeField]
        private AnimationCurve verticalDown = AnimationCurve.Linear(0.0f, 0.0f, 1.0f, 1.0f);

        [SerializeField]
        private Vector4 eyeAngleLimit = new Vector4(30.0f, 30.0f, 20.0f, 25.0f);

        [SerializeField]
        private List<HoLookAtEyeEntry> eyeEntries = new List<HoLookAtEyeEntry>();

        [SerializeField]
        private bool driveEyeBones = true;

        [SerializeField, Range(0.0f, 1.0f)]
        private float eyeBoneWeight = 1.0f;

        [SerializeField]
        private bool eyeBoneUseCurve = true;

        [Header("Lost")]
        [SerializeField]
        private HoLookAtLostBehavior lostBehavior = HoLookAtLostBehavior.Return;

        [SerializeField, Min(0.0f)]
        private float returnDelay = 0.4f;

        [SerializeField, Min(0.0f)]
        private float returnSpeed = 90.0f;

        [SerializeField, Min(0.0f)]
        private float teleportAngleThreshold = 120.0f;

        [Header("Write")]
        [SerializeField, Min(0.0f)]
        private float writeThreshold = 0.01f;

        private readonly HoShapeKeyWriter writer = new HoShapeKeyWriter();
        private readonly int[,] eyeTargetIds = new int[ChannelCount, 2];

        private HoLookAtState state;
        private bool built;
        private float externalWeight = 1.0f;
        private float externalSpineWeight = 1.0f;
        private float externalHeadWeight = 1.0f;
        private float externalEyeWeight = 1.0f;
        private bool externalEnabled = true;
        private bool externalPointValid;
        private Vector3 externalPoint;
        private Vector3 lastKnownTargetPoint;
        private bool hasLastKnownPoint;
        private float mouseAngleYaw;
        private float mouseAnglePitch;

        // ── 公开接口（给状态机 / Blueprint / Timeline / 脚本用）────────────────

        /// <summary>外部权重乘子（与面板上的总权重相乘）。</summary>
        public float Weight
        {
            get => externalWeight;
            set => externalWeight = Mathf.Clamp01(value);
        }

        public float SpineWeight
        {
            get => externalSpineWeight;
            set => externalSpineWeight = Mathf.Clamp01(value);
        }

        public float HeadWeight
        {
            get => externalHeadWeight;
            set => externalHeadWeight = Mathf.Clamp01(value);
        }

        public float EyeWeight
        {
            get => externalEyeWeight;
            set => externalEyeWeight = Mathf.Clamp01(value);
        }

        public bool Enabled
        {
            get => externalEnabled;
            set => externalEnabled = value;
        }

        public HoLookAtMode Mode
        {
            get => targetMode;
            set => targetMode = value;
        }

        public void SetEnabled(bool value)
        {
            externalEnabled = value;
        }

        public void SetMode(HoLookAtMode mode)
        {
            targetMode = mode;
            externalPointValid = false;
        }

        public void SetTarget(Transform value)
        {
            target = value;
            externalPointValid = false;
            hasLastKnownPoint = false;
        }

        /// <summary>用世界坐标当目标（覆盖面板设置，直到 SetTarget 或 ClearExternalTarget）。</summary>
        public void SetTargetPoint(Vector3 worldPoint)
        {
            externalPoint = worldPoint;
            externalPointValid = true;
        }

        public void ClearExternalTarget()
        {
            externalPointValid = false;
        }

        // ── 只读调试信息 ────────────────────────────────────────────────────

        public HoLookAtAngles TotalAngles { get; private set; }

        public HoLookAtAngles HeadAngles { get; private set; }

        public float EyeYaw => state.eyeYaw;

        public float EyePitch => state.eyePitch;

        public bool HasTarget => state.hasTarget;

        public bool IkRecentlyCalled => Application.isPlaying && Time.timeAsDouble - state.lastIkTime < 0.5;

        public bool AnimatorIsHuman => animator != null && animator.isHuman;

        public bool AnimatorHasAvatar => animator != null && animator.avatar != null;

        public int MeshCount => writer.MeshCount;

        public int BindingCount => writer.BindingCount;

        public IReadOnlyList<string> MissingKeys => writer.MissingKeys;

        public SkinnedMeshRenderer GetMesh(int index)
        {
            return writer.GetMesh(index);
        }

        public bool KeyExists(string keyName)
        {
            return writer.KeyExists(keyName);
        }

        /// <summary>编辑器用：丢弃缓存并重新解析键。</summary>
        public void Rebuild()
        {
            built = false;
            EnsureBuilt();
        }

        public void CollectChildRenderers()
        {
            SkinnedMeshRenderer[] found = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            renderers.Clear();
            for (int i = 0; i < found.Length; i++)
            {
                renderers.Add(found[i]);
            }

            built = false;
        }

        /// <summary>本帧某个通道的量（0..1，已过曲线）；给调试读数用。</summary>
        public float GetChannelAmount(HoLookAtEyeChannel channel)
        {
            GetChannelAmounts(channel, out float left, out float right);
            return Mathf.Max(left, right);
        }

        public float GetChannelTargetOutput(HoLookAtEyeChannel channel, bool rightEye)
        {
            int index = (int)channel;
            if (index < 0 || index >= ChannelCount)
            {
                return 0.0f;
            }

            // 同一个通道只注册第一条条目（左右眼各一个键），多余条目会被忽略
            int id = eyeTargetIds[index, rightEye ? 1 : 0];
            return id < 0 ? 0.0f : writer.GetTargetOutput(id);
        }

        // ── 生命周期 ────────────────────────────────────────────────────────

        private void OnEnable()
        {
            built = false;
            EnsureBuilt();
            ResetState();
        }

        private void OnDisable()
        {
            built = false;
        }

        private void LateUpdate()
        {
            if (!ShouldEvaluate())
            {
                return;
            }

            EnsureBuilt();
            ApplyEyes(Mathf.Max(0.0f, GetDeltaTime()));
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (!ShouldEvaluate())
            {
                return;
            }

            EnsureBuilt();
            HoLookAtAngles head = ApplyHead(Mathf.Max(0.0f, GetDeltaTime()));
            state.lastIkTime = Time.timeAsDouble;

            if (animator == null)
            {
                return;
            }

            animator.SetLookAtPosition(HoLookAtSolver.PointFromAngles(
                GetPivot(),
                GetReferenceForward(),
                GetReferenceUp(),
                head,
                GetTargetDistance()));

            float spine = spineEnabled ? bodyWeight * externalSpineWeight : 0.0f;
            float totalWeight = Mathf.Clamp01(weight * externalWeight) * (externalEnabled ? 1.0f : 0.0f);
            if (headEnabled && externalHeadWeight > 0.0f && state.applyLookAt)
            {
                float headPart = Mathf.Clamp01(headWeight * externalHeadWeight);
                animator.SetLookAtWeight(totalWeight, spine, headPart, eyesWeightToUnity, clampWeight);
            }
            else
            {
                animator.SetLookAtWeight(0.0f, 0.0f, 0.0f, 0.0f, clampWeight);
            }
        }

        private void OnValidate()
        {
            cameraCullingGuard();
            writer.WriteThreshold = writeThreshold;
            if (eyeEntries != null)
            {
                for (int i = 0; i < eyeEntries.Count; i++)
                {
                    eyeEntries[i]?.Sanitize();
                }
            }

            built = false;
        }

        private void cameraCullingGuard()
        {
            // 面板上的数值保护（名字保留以免 OnValidate 里出现魔法表达式）
            mouseDeadZone = Mathf.Clamp(mouseDeadZone, 0.0f, 0.45f);
            mouseDistance = Mathf.Max(0.1f, mouseDistance);
            eyeAngleLimit = new Vector4(
                Mathf.Max(1.0f, eyeAngleLimit.x),
                Mathf.Max(1.0f, eyeAngleLimit.y),
                Mathf.Max(1.0f, eyeAngleLimit.z),
                Mathf.Max(1.0f, eyeAngleLimit.w));
        }

        private bool ShouldEvaluate()
        {
            if (Application.isPlaying)
            {
                return true;
            }

            return evaluateInEditMode;
        }

        private float GetDeltaTime()
        {
            return Application.isPlaying ? Time.deltaTime : 1.0f / 60.0f;
        }

        private void EnsureBuilt()
        {
            if (built && !writer.MeshesChanged())
            {
                return;
            }

            Build();
            built = true;
        }

        private void Build()
        {
            writer.WriteThreshold = writeThreshold;
            writer.BeginBuild(renderers);

            for (int c = 0; c < ChannelCount; c++)
            {
                eyeTargetIds[c, 0] = -1;
                eyeTargetIds[c, 1] = -1;
            }

            if (eyeEntries != null)
            {
                for (int i = 0; i < eyeEntries.Count; i++)
                {
                    HoLookAtEyeEntry entry = eyeEntries[i];
                    if (entry == null)
                    {
                        continue;
                    }

                    int index = Mathf.Clamp((int)entry.Channel, 0, ChannelCount - 1);
                    if (eyeTargetIds[index, 0] < 0)
                    {
                        eyeTargetIds[index, 0] = writer.RegisterTarget(entry.LeftEye);
                        eyeTargetIds[index, 1] = writer.RegisterTarget(entry.RightEye);
                    }
                }
            }

            writer.EndBuild();
        }

        public void ResetState()
        {
            state = default;
            writer.Reset();
            hasLastKnownPoint = false;
        }

        // ── 第一段：目标解算 + 头部 ─────────────────────────────────────────

        private HoLookAtAngles ApplyHead(float deltaTime)
        {
            HoLookAtAngles total = ResolveTotalAngles(deltaTime);
            TotalAngles = total;

            HoHeadSettings head = new HoHeadSettings
            {
                deadZone = deadZone,
                headShare = headShare,
                yawLimit = yawLimit,
                pitchLimitUp = pitchLimitUp,
                pitchLimitDown = pitchLimitDown
            };
            head.Sanitize();

            float effectiveWeight = Mathf.Clamp01(weight * externalWeight) * (externalEnabled ? 1.0f : 0.0f);
            HoLookAtAngles headAngles = HoLookAtSolver.Split(total, head, effectiveWeight, ref state);
            HeadAngles = headAngles;
            return headAngles;
        }

        /// <summary>算出"总角度"：目标方向 → 参考系 yaw/pitch → 平滑/瞬移处理 → 丢失行为。</summary>
        private HoLookAtAngles ResolveTotalAngles(float deltaTime)
        {
            Vector3 pivot = GetPivot();
            Vector3 forward = GetReferenceForward();
            Vector3 up = GetReferenceUp();
            HoLookAtAngles target = default;
            bool hasTarget = TryGetTargetDirection(pivot, forward, up, out Vector3 direction, out bool isDirection);

            if (!hasTarget)
            {
                state.hasTarget = false;
                state.lostTime += deltaTime;
                if (lostBehavior == HoLookAtLostBehavior.Disable)
                {
                    state.eyeYaw = 0.0f;
                    state.eyePitch = 0.0f;
                    state.applyLookAt = false;
                    return default;
                }

                state.applyLookAt = true;
                if (lostBehavior == HoLookAtLostBehavior.Hold || state.lostTime < returnDelay)
                {
                    target.yaw = state.smoothedYaw;
                    target.pitch = state.smoothedPitch;
                    return target;
                }

                // 回中立
                float t = returnSpeed > 0.0f ? returnSpeed * deltaTime : 999.0f;
                state.smoothedYaw = Mathf.MoveTowards(state.smoothedYaw, 0.0f, t);
                state.smoothedPitch = Mathf.MoveTowards(state.smoothedPitch, 0.0f, t);
                target.yaw = state.smoothedYaw;
                target.pitch = state.smoothedPitch;
                return target;
            }

            state.hasTarget = true;
            state.applyLookAt = true;
            state.lostTime = 0.0f;

            if (isDirection)
            {
                target = HoLookAtSolver.Decompose(forward, up, direction);
            }

            ApplySmoothedAngles(target, deltaTime, false);
            target.yaw = state.smoothedYaw;
            target.pitch = state.smoothedPitch;
            return target;
        }

        private void ApplySmoothedAngles(HoLookAtAngles target, float deltaTime, bool immediate)
        {
            float jump = Mathf.Abs(Mathf.DeltaAngle(state.smoothedYaw, target.yaw))
                         + Mathf.Abs(Mathf.DeltaAngle(state.smoothedPitch, target.pitch));
            if (!immediate && teleportAngleThreshold > 0.0f && jump > teleportAngleThreshold)
            {
                state.smoothedYaw = target.yaw;
                state.smoothedPitch = target.pitch;
                return;
            }

            if (immediate)
            {
                state.smoothedYaw = target.yaw;
                state.smoothedPitch = target.pitch;
                return;
            }

            state.smoothedYaw = HoLookAtSolver.SmoothAngle(state.smoothedYaw, target.yaw, deltaTime, aimSmoothing, aimMaxSpeed);
            state.smoothedPitch = HoLookAtSolver.SmoothAngle(state.smoothedPitch, target.pitch, deltaTime, aimSmoothing, aimMaxSpeed);
        }

        private bool TryGetTargetDirection(Vector3 pivot, Vector3 forward, Vector3 up, out Vector3 direction, out bool isDirection)
        {
            direction = forward;
            isDirection = false;

            if (externalPointValid)
            {
                direction = externalPoint - pivot;
                isDirection = true;
                return direction.sqrMagnitude > 1e-6f;
            }

            if (targetMode == HoLookAtMode.Mouse)
            {
                HoMouseSettings settings = new HoMouseSettings
                {
                    inputSource = inputSource,
                    camera = mouseCamera,
                    sampleMode = mouseSampleMode,
                    sensitivity = mouseSensitivity,
                    deadZone = mouseDeadZone,
                    distance = mouseDistance,
                    projectToPlane = mouseProjectToPlane,
                    planeHeight = mousePlaneHeight,
                    raycastMask = mouseRaycastMask,
                    holdOffscreen = mouseHoldOffscreen
                };

                HoPointerSample sample = HoMousePointer.Sample(settings);
                if (!sample.valid)
                {
                    return mouseHoldOffscreen && state.hasTarget;
                }

                if (mouseSampleMode == HoLookAtMouseSampleMode.AngleMap)
                {
                    Camera camera = mouseCamera != null ? mouseCamera : Camera.main;
                    Vector2 offset = HoMousePointer.ScreenToAngleOffset(camera, sample.screenPosition, mouseDeadZone);
                    mouseAngleYaw = offset.x * mouseSensitivity.x;
                    mouseAnglePitch = offset.y * mouseSensitivity.y;
                    direction = HoLookAtSolver.DirectionFromAngles(forward, up, mouseAngleYaw, mouseAnglePitch);
                    isDirection = true;
                    return true;
                }

                if (sample.hasWorldPoint)
                {
                    lastKnownTargetPoint = sample.worldPoint;
                    hasLastKnownPoint = true;
                    direction = sample.worldPoint - pivot;
                    isDirection = true;
                    return direction.sqrMagnitude > 1e-6f;
                }

                return mouseHoldOffscreen && hasLastKnownPoint;
            }

            if (target == null)
            {
                return false;
            }

            Vector3 point = target.position + target.TransformVector(targetOffset);
            lastKnownTargetPoint = point;
            hasLastKnownPoint = true;
            direction = point - pivot;
            isDirection = true;
            return direction.sqrMagnitude > 1e-6f;
        }

        private Vector3 GetPivot()
        {
            if (animator != null && animator.isHuman)
            {
                Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (head != null)
                {
                    return head.position;
                }
            }

            return transform.position;
        }

        private float GetTargetDistance()
        {
            if (target != null && targetMode == HoLookAtMode.Transform)
            {
                return Mathf.Max(0.1f, Vector3.Distance(GetPivot(), target.position));
            }

            return 1.0f;
        }

        private Vector3 GetReferenceForward()
        {
            Transform frame = GetReferenceTransform();
            return frame != null ? frame.forward : Vector3.forward;
        }

        private Vector3 GetReferenceUp()
        {
            Transform frame = GetReferenceTransform();
            return frame != null ? frame.up : Vector3.up;
        }

        private Transform GetReferenceTransform()
        {
            if (reference != null)
            {
                return reference;
            }

            return transform;
        }

        // ── 第二段：眼睛 ────────────────────────────────────────────────────

        private void ApplyEyes(float deltaTime)
        {
            float effectiveWeight = Mathf.Clamp01(weight * externalWeight) * (externalEnabled ? 1.0f : 0.0f);
            float eyes = eyesEnabled ? Mathf.Clamp01(eyeWeight * externalEyeWeight) * effectiveWeight : 0.0f;

            GetEyeSettings(out HoEyeSettings settings, out AnimationCurve inner, out AnimationCurve outer, out AnimationCurve up, out AnimationCurve down);

            float yaw = state.eyeYaw * eyes;
            float pitch = state.eyePitch * eyes;
            if (eyeEllipseClamp)
            {
                HoLookAtSolver.ClampToEllipse(ref yaw, ref pitch, settings.angleLimitInner, settings.angleLimitUp);
            }

            state.smoothedEyeYaw = HoLookAtSolver.SmoothAngle(state.smoothedEyeYaw, yaw, deltaTime, eyeSmoothing, 0.0f);
            state.smoothedEyePitch = HoLookAtSolver.SmoothAngle(state.smoothedEyePitch, pitch, deltaTime, eyeSmoothing, 0.0f);
            yaw = state.smoothedEyeYaw;
            pitch = state.smoothedEyePitch;

            if (writer.IsBuilt && writer.MeshCount > 0)
            {
                writer.WriteEnabled = true;
                writer.Snapshot();

                for (int c = 0; c < ChannelCount; c++)
                {
                    GetChannelAmounts((HoLookAtEyeChannel)c, out float leftAmount, out float rightAmount);
                    ApplyEyeChannel((HoLookAtEyeChannel)c, 0, leftAmount, deltaTime, inner, outer, up, down, settings);
                    ApplyEyeChannel((HoLookAtEyeChannel)c, 1, rightAmount, deltaTime, inner, outer, up, down, settings);
                }

                writer.Write();
            }

            ApplyEyeBones(yaw, pitch, settings, inner, outer, up, down);
        }

        private void ApplyEyeChannel(
            HoLookAtEyeChannel channel,
            int eyeIndex,
            float angle,
            float deltaTime,
            AnimationCurve inner,
            AnimationCurve outer,
            AnimationCurve up,
            AnimationCurve down,
            in HoEyeSettings settings)
        {
            int id = eyeTargetIds[(int)channel, eyeIndex];
            if (id < 0)
            {
                return;
            }

            AnimationCurve curve;
            float limit;
            GetCurveAndLimit(channel, inner, outer, up, down, settings, out curve, out limit);

            float normalized = limit > 0.0f ? Mathf.Clamp01(angle / limit) : 0.0f;
            float shaped = curve != null && curve.length > 0 ? Mathf.Clamp01(curve.Evaluate(normalized)) : normalized;
            writer.Apply(id, shaped, deltaTime);
        }

        private static void GetCurveAndLimit(
            HoLookAtEyeChannel channel,
            AnimationCurve inner,
            AnimationCurve outer,
            AnimationCurve up,
            AnimationCurve down,
            in HoEyeSettings settings,
            out AnimationCurve curve,
            out float limit)
        {
            switch (channel)
            {
                case HoLookAtEyeChannel.Inner:
                case HoLookAtEyeChannel.LookRight:
                    curve = inner;
                    limit = settings.angleLimitInner;
                    break;

                case HoLookAtEyeChannel.Up:
                    curve = up;
                    limit = settings.angleLimitUp;
                    break;

                case HoLookAtEyeChannel.Down:
                    curve = down;
                    limit = settings.angleLimitDown;
                    break;

                default:
                    curve = outer;
                    limit = settings.angleLimitOuter;
                    break;
            }
        }

        private void GetChannelAmounts(HoLookAtEyeChannel channel, out float leftEye, out float rightEye)
        {
            float yaw = state.eyeYaw;
            float pitch = state.eyePitch;
            float positiveYaw = Mathf.Max(yaw, 0.0f);
            float negativeYaw = Mathf.Max(-yaw, 0.0f);
            float positivePitch = Mathf.Max(pitch, 0.0f);
            float negativePitch = Mathf.Max(-pitch, 0.0f);

            switch (channel)
            {
                case HoLookAtEyeChannel.Inner:
                    // 内/外是相对眼球的：左眼的"内"= 往右看，右眼的"内"= 往左看
                    leftEye = positiveYaw;
                    rightEye = negativeYaw;
                    break;

                case HoLookAtEyeChannel.Outer:
                    leftEye = negativeYaw;
                    rightEye = positiveYaw;
                    break;

                case HoLookAtEyeChannel.LookLeft:
                    // 左右族是相对头的：两只眼同一个键、同一个量
                    leftEye = negativeYaw;
                    rightEye = negativeYaw;
                    break;

                case HoLookAtEyeChannel.LookRight:
                    leftEye = positiveYaw;
                    rightEye = positiveYaw;
                    break;

                case HoLookAtEyeChannel.Up:
                    leftEye = positivePitch;
                    rightEye = positivePitch;
                    break;

                default:
                    leftEye = negativePitch;
                    rightEye = negativePitch;
                    break;
            }
        }

        private void GetEyeSettings(
            out HoEyeSettings settings,
            out AnimationCurve inner,
            out AnimationCurve outer,
            out AnimationCurve up,
            out AnimationCurve down)
        {
            settings = new HoEyeSettings
            {
                angleLimitInner = eyeAngleLimit.x,
                angleLimitOuter = eyeAngleLimit.y,
                angleLimitUp = eyeAngleLimit.z,
                angleLimitDown = eyeAngleLimit.w,
                ellipseClamp = eyeEllipseClamp
            };
            settings.Sanitize();
            inner = horizontalInner;
            outer = horizontalOuter;
            up = verticalUp;
            down = verticalDown;
        }

        private void ApplyEyeBones(
            float yaw,
            float pitch,
            in HoEyeSettings settings,
            AnimationCurve inner,
            AnimationCurve outer,
            AnimationCurve up,
            AnimationCurve down)
        {
            if (!driveEyeBones || animator == null || !animator.isHuman || eyeBoneWeight <= 0.0f)
            {
                return;
            }

            Transform left = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            Transform right = animator.GetBoneTransform(HumanBodyBones.RightEye);
            if (left == null && right == null)
            {
                return;
            }

            float shapedYaw = eyeBoneUseCurve
                ? ShapeBoneAngle(yaw, settings.angleLimitInner, settings.angleLimitOuter, inner, outer)
                : yaw;
            float shapedPitch = eyeBoneUseCurve
                ? ShapeBoneAngle(pitch, settings.angleLimitUp, settings.angleLimitDown, up, down)
                : pitch;

            Vector3 upAxis = GetReferenceUp();
            Vector3 rightAxis = Vector3.Cross(upAxis, GetReferenceForward()).normalized;
            Quaternion delta = Quaternion.AngleAxis(shapedYaw, upAxis) * Quaternion.AngleAxis(-shapedPitch, rightAxis);

            float boneWeight = Mathf.Clamp01(eyeBoneWeight);
            Quaternion blended = Quaternion.Slerp(Quaternion.identity, delta, boneWeight);
            if (left != null)
            {
                left.rotation = blended * left.rotation;
            }

            if (right != null)
            {
                right.rotation = blended * right.rotation;
            }
        }

        private static float ShapeBoneAngle(float angle, float positiveLimit, float negativeLimit, AnimationCurve positiveCurve, AnimationCurve negativeCurve)
        {
            bool positive = angle >= 0.0f;
            float limit = positive ? positiveLimit : negativeLimit;
            AnimationCurve curve = positive ? positiveCurve : negativeCurve;
            if (limit <= 0.0f)
            {
                return 0.0f;
            }

            float normalized = Mathf.Clamp01(Mathf.Abs(angle) / limit);
            float shaped = curve != null && curve.length > 0 ? Mathf.Clamp01(curve.Evaluate(normalized)) : normalized;
            return Mathf.Sign(angle) * limit * shaped;
        }
    }
}
