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
    ///
    /// 参数刻意收得很紧：面板上只留"每天会调的"，其余都在「高级」里；
    /// 眼球通道只存"键名 + 增益"，不再让每个通道各配一套 ramp/范围。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("HoUnityTools/Constraints/Ho Look At Constraint")]
    public sealed class HoLookAtConstraint : MonoBehaviour, IHoShapeKeyMeshProvider
    {
        private const int ChannelCount = 6;

        /// <summary>交给 Unity 的 clampWeight 固定 0：限位由本组件的角度上限负责，避免二次夹取让读数对不上。</summary>
        private const float UnityClampWeight = 0.0f;

        [Header("目标")]
        [SerializeField]
        private HoLookAtMode targetMode = HoLookAtMode.Transform;

        [SerializeField]
        private Transform target;

        [SerializeField]
        private Vector3 targetOffset;

        [SerializeField]
        private Camera mouseCamera;

        [SerializeField]
        private Vector2 mouseSensitivity = new Vector2(30.0f, 20.0f);

        [SerializeField, Range(0.0f, 0.45f)]
        private float mouseDeadZone = 0.05f;

        [Header("角色")]
        [SerializeField]
        private Animator animator;

        [SerializeField]
        private Transform reference;

        [SerializeField, Range(0.0f, 1.0f)]
        private float weight = 1.0f;

        [SerializeField]
        private bool evaluateInEditMode = false;

        [Header("① 脊椎跟随")]
        [SerializeField]
        private bool spineEnabled = true;

        [SerializeField, Range(0.0f, 1.0f)]
        private float bodyWeight = 0.3f;

        [Header("② 头颈跟随")]
        [SerializeField]
        private bool headEnabled = true;

        [SerializeField, Range(0.0f, 1.0f)]
        private float headWeight = 1.0f;

        [SerializeField, Range(0.0f, 89.0f)]
        private float deadZone = 8.0f;

        [SerializeField, Range(0.0f, 1.0f)]
        private float headShare = 0.7f;

        [SerializeField, Range(0.0f, 179.0f)]
        private float headLimitYaw = 70.0f;

        [SerializeField, Range(0.0f, 179.0f)]
        private float headLimitPitch = 40.0f;

        [Header("③ 眼睛跟随")]
        [SerializeField]
        private bool eyesEnabled = true;

        [SerializeField]
        private List<Renderer> renderers = new List<Renderer>();

        [SerializeField, Range(0.0f, 1.0f)]
        private float eyeWeight = 1.0f;

        [SerializeField, Min(0.0f)]
        private float eyeSmoothing = 0.04f;

        [SerializeField]
        private Vector4 eyeAngleLimit = new Vector4(30.0f, 30.0f, 20.0f, 25.0f);

        [SerializeField]
        private AnimationCurve horizontalInner = AnimationCurve.Linear(0.0f, 0.0f, 1.0f, 1.0f);

        [SerializeField]
        private AnimationCurve horizontalOuter = AnimationCurve.Linear(0.0f, 0.0f, 1.0f, 1.0f);

        [SerializeField]
        private AnimationCurve verticalUp = AnimationCurve.Linear(0.0f, 0.0f, 1.0f, 1.0f);

        [SerializeField]
        private AnimationCurve verticalDown = AnimationCurve.Linear(0.0f, 0.0f, 1.0f, 1.0f);

        [SerializeField]
        private List<HoLookAtEyeEntry> eyeEntries = new List<HoLookAtEyeEntry>();

        [Header("丢失与瞬移")]
        [SerializeField]
        private HoLookAtLostBehavior lostBehavior = HoLookAtLostBehavior.Return;

        // ── 高级（面板默认折起，改起来才知道自己在干什么的才会进来）──────────

        [Header("高级")]
        [SerializeField]
        private HoLookAtMouseSampleMode mouseSampleMode = HoLookAtMouseSampleMode.AngleMap;

        [SerializeField]
        private HoLookAtMouseSpace mouseAngleSpace = HoLookAtMouseSpace.ScreenRelative;

        [SerializeField, Min(0.1f)]
        private float mouseDistance = 3.0f;

        [SerializeField]
        private LayerMask mouseRaycastMask = ~0;

        [SerializeField]
        private bool mouseHoldOffscreen = true;

        [SerializeField, Range(0.0f, 90.0f)]
        private float spineMinAngle;

        [SerializeField, Min(0.0f)]
        private float aimSmoothing = 0.06f;

        [SerializeField, Min(0.0f)]
        private float aimMaxSpeed = 360.0f;

        /// <summary>眼球骨骼权重：0 = 完全不动眼球骨骼（只写形态键）。</summary>
        [SerializeField, Range(0.0f, 1.0f)]
        private float eyeBoneWeight;

        [SerializeField, Min(0.0f)]
        private float returnDelay = 0.4f;

        [SerializeField, Min(0.0f)]
        private float returnSpeed = 90.0f;

        [SerializeField, Min(0.0f)]
        private float teleportAngleThreshold = 120.0f;

        [SerializeField, Min(0.0f)]
        private float writeThreshold = 0.01f;

        [SerializeField]
        private HoShapeKeyMergeMode mergeMode = HoShapeKeyMergeMode.Saturate;

        [Header("调试")]
        [SerializeField]
        private bool drawGizmos = true;

        private readonly HoShapeKeyWriter writer = new HoShapeKeyWriter();
        private readonly int[,] eyeTargetIds = new int[ChannelCount, 2];
        private readonly List<HoShapeKeyTarget> eyeTargets = new List<HoShapeKeyTarget>();

        private HoLookAtState state;
        private HoLookAtIkRelay relay;
        private bool cameraWarned;
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
        private bool hasMouseAngles;
        private Vector3 lastDirection = Vector3.forward;

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
            hasMouseAngles = false;
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

        /// <summary>解算用的支点（humanoid 是头骨，否则是本物体）。</summary>
        public Vector3 Pivot => GetPivot();

        /// <summary>本帧解算出来的目标方向（世界空间）。</summary>
        public Vector3 LastDirection => lastDirection;

        /// <summary>目标点（跟随物体 / 场景点模式才有；否则是支点前方 1 米）。</summary>
        public bool HasTargetPoint => hasLastKnownPoint;

        public Vector3 LastTargetPoint => lastKnownTargetPoint;

        public Vector3 ReferenceForward => GetReferenceForward();

        public Vector3 ReferenceUp => GetReferenceUp();

        public float HeadLimitYaw => headLimitYaw;

        public float HeadLimitPitch => headLimitPitch;

        public bool DrawGizmosEnabled => drawGizmos;

        public bool IkRecentlyCalled => Application.isPlaying && Time.timeAsDouble - state.lastIkTime < 0.5;

        public bool AnimatorIsHuman => animator != null && animator.isHuman;

        public bool AnimatorHasAvatar => animator != null && animator.avatar != null;

        /// <summary>组件是否就在 Animator 所在物体上（否则靠转发器收 OnAnimatorIK）。</summary>
        public bool AnimatorOnSameObject => animator != null && animator.gameObject == gameObject;

        /// <summary>转发器是否已经挂上（组件不在 Animator 物体上时才有意义）。</summary>
        public bool IkRelayActive => relay != null;

        public string AnimatorName => animator != null ? animator.gameObject.name : "（未找到）";

        /// <summary>鼠标换算用的相机名（手动指定）；"（未指定）"表示面板上还没填。</summary>
        public string ResolvedCameraName => mouseCamera != null ? mouseCamera.name : "（未指定）";

        public bool MouseCameraAssigned => mouseCamera != null;

        public int MeshCount => writer.MeshCount;

        public int BindingCount => writer.BindingCount;

        public IReadOnlyList<string> MissingKeys => writer.MissingKeys;

        /// <summary>本帧写满（或我们这一路被合并策略削过）的键，面板用。</summary>
        public void CollectSaturatedKeys(List<HoShapeKeySaturation> results)
        {
            writer.CollectSaturated(results);
        }

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

        /// <summary>某个通道这一帧实际写出去的形态键值（0..100）。</summary>
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

        /// <summary>调试快照：面板条形读数与场景 Gizmo 共用。</summary>
        public HoLookAtDebug GetDebug()
        {
            HoLookAtDebug debug = new HoLookAtDebug
            {
                targetYaw = state.smoothedYaw,
                targetPitch = state.smoothedPitch,
                headYaw = HeadAngles.yaw,
                headPitch = HeadAngles.pitch,
                eyeYaw = state.smoothedEyeYaw,
                eyePitch = state.smoothedEyePitch,
                hasTarget = state.hasTarget,
                targetValid = state.applyLookAt
            };

            GetEyeSettings(out HoEyeSettings settings, out AnimationCurve inner, out AnimationCurve outer, out AnimationCurve up, out AnimationCurve down);

            // 六条通道的量 = 和写形态键时同一套算法（角度 → 按上限归一化 → 过曲线）。
            // 内/外 与 看右/看左 共用同一条曲线与上限，所以四个独立值撑起六个通道。
            debug.inner = ShapeAmount(Mathf.Max(state.smoothedEyeYaw, 0.0f), settings.angleLimitInner, inner);
            debug.lookRight = debug.inner;
            debug.outer = ShapeAmount(Mathf.Max(-state.smoothedEyeYaw, 0.0f), settings.angleLimitOuter, outer);
            debug.lookLeft = debug.outer;
            debug.up = ShapeAmount(Mathf.Max(state.smoothedEyePitch, 0.0f), settings.angleLimitUp, up);
            debug.down = ShapeAmount(Mathf.Max(-state.smoothedEyePitch, 0.0f), settings.angleLimitDown, down);

            if (hasLastKnownPoint)
            {
                debug.hasTargetPoint = true;
                debug.targetPoint = lastKnownTargetPoint;
            }
            else
            {
                debug.targetPoint = Pivot + lastDirection * GetTargetDistance();
            }

            return debug;
        }

        // ── 生命周期 ────────────────────────────────────────────────────────

        private void OnEnable()
        {
            built = false;
            ResolveAnimator();
            EnsureBuilt();
            EnsureIkRelay();
            ResetState();
        }

        private void OnDisable()
        {
            // 关掉组件时把我们写过的眼动键交还给基准，别让眼睛停在最后一次的方向上
            writer.RestoreWritten();
            built = false;
            ReleaseIkRelay();
        }

        private void OnDestroy()
        {
            ReleaseIkRelay();
        }

        /// <summary>Animator 为空时按 自己 → 父级 → 子级 找。</summary>
        private void ResolveAnimator()
        {
            if (animator != null)
            {
                return;
            }

            animator = GetComponent<Animator>();
            if (animator == null)
            {
                animator = GetComponentInParent<Animator>();
            }

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }
        }

        /// <summary>
        /// 组件不在 Animator 物体上时，在 Animator 物体上挂一个转发器（只在播放模式添加）。
        /// </summary>
        private void EnsureIkRelay()
        {
            if (animator == null || animator.gameObject == gameObject)
            {
                ReleaseIkRelay();
                return;
            }

            if (!Application.isPlaying)
            {
                // 编辑模式不往别的物体上加组件（会把场景标脏），只用已有的
                relay = animator.gameObject.GetComponent<HoLookAtIkRelay>();
                if (relay != null)
                {
                    relay.Owner = this;
                }

                return;
            }

            if (relay == null || relay.gameObject != animator.gameObject)
            {
                ReleaseIkRelay();
                relay = animator.gameObject.GetComponent<HoLookAtIkRelay>();
                if (relay == null)
                {
                    relay = animator.gameObject.AddComponent<HoLookAtIkRelay>();
                }
            }

            relay.Owner = this;
        }

        private void ReleaseIkRelay()
        {
            if (relay != null && relay.Owner == this)
            {
                relay.Owner = null;
            }

            relay = null;
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
            HandleAnimatorIK(layerIndex);
        }

        /// <summary>IK 回调本体：自己就在 Animator 物体上时由 OnAnimatorIK 调用，否则由 <see cref="HoLookAtIkRelay"/> 转发。</summary>
        public void HandleAnimatorIK(int layerIndex)
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
            if (spine > 0.0f && spineMinAngle > 0.0f && TotalAngles.Magnitude < spineMinAngle)
            {
                // 起始角：总角度还小的时候不让身体参与，避免小幅度注视也带着上半身一起动
                spine = 0.0f;
            }

            float totalWeight = Mathf.Clamp01(weight * externalWeight) * (externalEnabled ? 1.0f : 0.0f);
            if (headEnabled && externalHeadWeight > 0.0f && state.applyLookAt)
            {
                float headPart = Mathf.Clamp01(headWeight * externalHeadWeight);
                animator.SetLookAtWeight(totalWeight, spine, headPart, 0.0f, UnityClampWeight);
            }
            else
            {
                animator.SetLookAtWeight(0.0f, 0.0f, 0.0f, 0.0f, UnityClampWeight);
            }
        }

        private void OnValidate()
        {
            mouseDeadZone = Mathf.Clamp(mouseDeadZone, 0.0f, 0.45f);
            mouseDistance = Mathf.Max(0.1f, mouseDistance);
            eyeAngleLimit = new Vector4(
                Mathf.Max(1.0f, eyeAngleLimit.x),
                Mathf.Max(1.0f, eyeAngleLimit.y),
                Mathf.Max(1.0f, eyeAngleLimit.z),
                Mathf.Max(1.0f, eyeAngleLimit.w));
            writer.WriteThreshold = writeThreshold;
            writer.MergeMode = mergeMode;
            if (eyeEntries != null)
            {
                for (int i = 0; i < eyeEntries.Count; i++)
                {
                    eyeEntries[i]?.Sanitize();
                }
            }

            built = false;
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
            writer.MergeMode = mergeMode;
            writer.BeginBuild(renderers);
            eyeTargets.Clear();

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
                    if (eyeTargetIds[index, 0] >= 0)
                    {
                        continue;
                    }

                    eyeTargetIds[index, 0] = RegisterEyeKey(entry.LeftEye);
                    eyeTargetIds[index, 1] = RegisterEyeKey(entry.RightEye);
                }
            }

            writer.EndBuild();
        }

        /// <summary>
        /// 面板上只存"键名 + 增益"，这里现造一个运行期目标交给写入器
        /// （瞬发 ramp：眼睛的平滑由 eyeSmoothing 统一负责，通道不再各配一套）。
        /// </summary>
        private int RegisterEyeKey(HoLookAtEyeKey key)
        {
            if (key == null || !key.HasKey)
            {
                return -1;
            }

            HoShapeKeyTarget runtime = HoShapeKeyTarget.CreateRuntime(key.KeyName, key.Gain);
            eyeTargets.Add(runtime);
            return writer.RegisterTarget(runtime);
        }

        public void ResetState()
        {
            state = default;
            writer.RestoreWritten();
            writer.Reset();
            hasLastKnownPoint = false;
            hasMouseAngles = false;
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
                yawLimit = headLimitYaw,
                pitchLimit = headLimitPitch
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
                lastDirection = direction;
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
                    inputSource = HoLookAtInputSource.Auto,
                    camera = mouseCamera,
                    sampleMode = mouseSampleMode,
                    angleSpace = mouseAngleSpace,
                    sensitivity = mouseSensitivity,
                    deadZone = mouseDeadZone,
                    distance = mouseDistance,
                    raycastMask = mouseRaycastMask,
                    holdOffscreen = mouseHoldOffscreen
                };

                HoPointerSample sample = HoMousePointer.Sample(settings);
                if (!sample.valid)
                {
                    // 指针不可用（窗口失焦 / 没有输入设备）：离屏保持时沿用上一次的方向，而不是漂回中立
                    if (!mouseHoldOffscreen)
                    {
                        return false;
                    }

                    if (mouseSampleMode == HoLookAtMouseSampleMode.AngleMap && hasMouseAngles)
                    {
                        direction = HoLookAtSolver.DirectionFromAngles(forward, up, mouseAngleYaw, mouseAnglePitch);
                        isDirection = true;
                        return true;
                    }

                    if (hasLastKnownPoint)
                    {
                        direction = lastKnownTargetPoint - pivot;
                        isDirection = true;
                        return direction.sqrMagnitude > 1e-6f;
                    }

                    return state.hasTarget;
                }

                if (mouseSampleMode == HoLookAtMouseSampleMode.AngleMap)
                {
                    Camera camera = GetMouseCamera();
                    Vector2 offset = HoMousePointer.ScreenToAngleOffset(camera, sample.screenPosition, mouseDeadZone);
                    mouseAngleYaw = offset.x * mouseSensitivity.x;
                    mouseAnglePitch = offset.y * mouseSensitivity.y;
                    hasMouseAngles = true;

                    if (mouseAngleSpace == HoLookAtMouseSpace.ScreenRelative && camera != null)
                    {
                        // 观众视角（第三人称，我面对角色）：
                        //   鼠标在屏幕中心 → 角色看向观察者（也就是看向镜头方向的反向）；
                        //   鼠标在右上     → 角色看向观察者的右上方。
                        // 在观察者坐标系里就是 (-Z 朝向观察者, +X 观察者右, +Y 上)，
                        // 所以直接用 (tan(yaw), tan(pitch), -1) 构造方向。
                        // 注意：不能用相机 forward 当基准 —— 那是射进屏幕里的方向，
                        // 对"面对相机"的角色来说等于让它看自己背后（平转角会变成 ±150° 以上）。
                        Transform view = camera.transform;
                        float yawRadians = mouseAngleYaw * Mathf.Deg2Rad;
                        float pitchRadians = mouseAnglePitch * Mathf.Deg2Rad;
                        Vector3 local = new Vector3(Mathf.Tan(yawRadians), Mathf.Tan(pitchRadians), -1.0f);
                        direction = view.TransformDirection(local.normalized);
                    }
                    else
                    {
                        // 角色相对（或找不到相机时的兜底）：把鼠标当成角色自己的注视摇杆
                        direction = HoLookAtSolver.DirectionFromAngles(forward, up, mouseAngleYaw, mouseAnglePitch);
                    }

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

        /// <summary>
        /// 鼠标换算用的相机：**只用手动指定的那个**（不自动猜）。
        /// 空的时候警告一次，角度映射会退回角色相对坐标系（正面机位下左右是反的）。
        /// </summary>
        private Camera GetMouseCamera()
        {
            if (mouseCamera != null && mouseCamera.gameObject.activeInHierarchy)
            {
                return mouseCamera;
            }

            if (!cameraWarned && targetMode == HoLookAtMode.Mouse && Application.isPlaying)
            {
                cameraWarned = true;
                Debug.LogWarning(
                    "[HoLookAtConstraint] 鼠标模式没有指定「相机」：请在面板上把观众视角的相机拖进「相机」字段"
                    + "（也可以用「填入场景里的相机」按钮）。在此之前角度映射会退回角色相对坐标系，正面机位下看起来是左右反的。",
                    this);
            }

            return null;
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

            return Mathf.Max(0.1f, mouseDistance);
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

            // 默认用 Animator 所在物体（角色根）的朝向：那才是角色的面向。
            // 退回到组件自己的 transform 往往是个骨骼/空物体，轴向是随机的，
            // 会让"总角度"读数与限位全部失准。
            if (animator != null)
            {
                return animator.transform;
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

            // 斜向看时按椭圆夹取，避免"过转"（外圈比内圈小的时候特别明显）
            HoLookAtSolver.ClampToEllipse(ref yaw, ref pitch, settings.angleLimitInner, settings.angleLimitUp);

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
            writer.Apply(id, ShapeAmount(angle, limit, curve), deltaTime);
        }

        /// <summary>角度 →（按上限归一化）→ 过曲线。写形态键、转眼球骨骼、调试读数共用同一套。</summary>
        private static float ShapeAmount(float angle, float limit, AnimationCurve curve)
        {
            if (limit <= 0.0f)
            {
                return 0.0f;
            }

            float normalized = Mathf.Clamp01(angle / limit);
            return curve != null && curve.length > 0 ? Mathf.Clamp01(curve.Evaluate(normalized)) : normalized;
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
                angleLimitDown = eyeAngleLimit.w
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
            if (eyeBoneWeight <= 0.0f || animator == null || !animator.isHuman)
            {
                return;
            }

            Transform left = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            Transform right = animator.GetBoneTransform(HumanBodyBones.RightEye);
            if (left == null && right == null)
            {
                return;
            }

            float shapedYaw = ShapeBoneAngle(yaw, settings.angleLimitInner, settings.angleLimitOuter, inner, outer);
            float shapedPitch = ShapeBoneAngle(pitch, settings.angleLimitUp, settings.angleLimitDown, up, down);

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

            return Mathf.Sign(angle) * limit * ShapeAmount(Mathf.Abs(angle), limit, curve);
        }
    }
}
