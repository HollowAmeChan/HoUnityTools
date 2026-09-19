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
        private const int ChannelCount = 4;

        /// <summary>交给 Unity 的 clampWeight 固定 0：限位由本组件的角度上限负责，避免二次夹取让读数对不上。</summary>
        private const float UnityClampWeight = 0.0f;

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

        [SerializeField]
        private Animator animator;

        [SerializeField]
        private Transform reference;

        [SerializeField, Range(0.0f, 1.0f)]
        private float weight = 1.0f;

        [SerializeField]
        private bool evaluateInEditMode = false;

        [SerializeField]
        private bool spineEnabled = true;

        [SerializeField, Range(0.0f, 1.0f)]
        private float bodyWeight = 0.3f;

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

        [SerializeField]
        private bool eyesEnabled = true;

        [SerializeField]
        private HoLookAtEyeDriver eyeDriver = HoLookAtEyeDriver.EyeBones;

        [SerializeField, Range(0.0f, 1.0f)]
        private float eyeWeight = 1.0f;

        [SerializeField, Min(0.0f)]
        private float eyeSmoothing = 0.04f;

        /// <summary>骨骼模式的左右限位（度）：眼球最多往左右各转多少。</summary>
        [SerializeField, Range(0.0f, 89.0f)]
        private float eyeBoneLimitYaw = 15.0f;

        /// <summary>骨骼模式的上下限位（度）。</summary>
        [SerializeField, Range(0.0f, 89.0f)]
        private float eyeBoneLimitPitch = 10.0f;

        [SerializeField]
        private List<Renderer> renderers = new List<Renderer>();

        [SerializeField]
        private Vector4 eyeAngleLimit = new Vector4(15.0f, 15.0f, 10.0f, 10.0f);

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

        [SerializeField]
        private HoLookAtLostBehavior lostBehavior = HoLookAtLostBehavior.Return;

        // ── 高级（面板默认折起，改起来才知道自己在干什么的才会进来）──────────

        [SerializeField]
        private HoLookAtMouseSampleMode mouseSampleMode = HoLookAtMouseSampleMode.CursorPoint;

        /// <summary>一次性迁移标记：老场景的默认是"角度摇杆"，那时还没有准星模式。</summary>
        [SerializeField, HideInInspector]
        private bool mouseModeMigrated;

        [SerializeField]
        private HoLookAtMouseSpace mouseAngleSpace = HoLookAtMouseSpace.ScreenRelative;

        /// <summary>
        /// 「跟随鼠标」的灵敏度：1 = 鼠标推到画面边缘时视线转到相机视角的边缘（屏幕上 1:1 跟手）。
        /// 调大更"甩"、调小更"稳"；角度上限由相机 FOV 决定，不会像射线交点那样在角色附近失控。
        /// </summary>
        [SerializeField, Range(0.1f, 3.0f)]
        private float mouseAimGain = 1.0f;

        /// <summary>「跟随鼠标」的映射原点：true = 角色在屏幕上的位置（鼠标指着角色 = 看镜头）。</summary>
        [SerializeField]
        private bool mouseAimFromCharacter = true;

        [SerializeField, Min(0.1f)]
        private float mouseDistance = 3.0f;

        [SerializeField]
        private LayerMask mouseRaycastMask = ~0;

        [SerializeField]
        private bool mouseHoldOffscreen = true;

        [SerializeField, Range(0.0f, 90.0f)]
        private float spineMinAngle;

        /// <summary>
        /// 头朝向偏差（度，yaw/pitch）：模型静止姿势的头部朝向跟"角色正前方"不一定重合，
        /// 那是模型常数 —— 眼睛看不到、也算不出来，只能在这里补。
        /// 症状：看着总是固定偏一点（怎么移动目标都偏那么多）时，用 Gizmo 对着调这个数。
        /// </summary>
        [SerializeField]
        private Vector2 headDirectionTrim;

        [SerializeField, Min(0.0f)]
        private float aimSmoothing = 0.06f;

        [SerializeField, Min(0.0f)]
        private float aimMaxSpeed = 360.0f;

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

        [SerializeField]
        private bool drawGizmos = true;

        /// <summary>Game 视图里的屏幕叠加（鼠标点 / 目光落点 / 误差读数）。只在编辑器里生效。</summary>
        [SerializeField]
        private bool drawOverlay = true;

        /// <summary>编辑器专用：鼠标在 Scene 视图上时，用 Scene 视图的相机当观众视角。</summary>
        [SerializeField]
        private bool useSceneViewMouse = true;

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
        private Quaternion leftEyeRest = Quaternion.identity;
        private Quaternion rightEyeRest = Quaternion.identity;
        private bool eyeBonesCaptured;
        private Quaternion headPoseBeforeIk = Quaternion.identity;
        private bool headHasPoseBeforeIk;
        private bool headMeasuredThisFrame;
        private float headAppliedFactor;
        private Vector2 lastPointerScreen;
        private bool hasPointerScreen;
        private Ray lastPointerRay;
        private bool hasPointerRay;

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

        public bool DrawOverlayEnabled => drawOverlay;

        public bool UseSceneViewMouse => useSceneViewMouse;

        /// <summary>本次鼠标采样实际用的相机（Scene 视图鼠标时就是 Scene 视图相机）；屏幕叠加用它投影。</summary>
        public Camera PointerCamera { get; private set; }

        /// <summary>本次用的鼠标射线（只有"射线命中"模式才画：那时它才是指向的定义本身）。</summary>
        public bool HasPointerRay => hasPointerRay && mouseSampleMode == HoLookAtMouseSampleMode.Raycast;

        /// <summary>当前鼠标取法（面板/调试用）。</summary>
        public HoLookAtMouseSampleMode MouseSampleMode => mouseSampleMode;

        /// <summary>「跟随鼠标」当前的角度上限（度）：屏幕边缘对应的角度 = 半 FOV × 灵敏度。</summary>
        public Vector2 MouseAimHalfAngles
        {
            get
            {
                Camera aimCamera = PointerCamera != null ? PointerCamera : mouseCamera;
                HoMousePointer.GetHalfFov(aimCamera, GetPivot(), out float halfYaw, out float halfPitch);
                return new Vector2(halfYaw * mouseAimGain, halfPitch * mouseAimGain);
            }
        }

        public Ray LastPointerRay => lastPointerRay;

        public Vector2 LastPointerScreen => lastPointerScreen;

        /// <summary>当前的眼睛驱动模式（骨骼 / 形态键）。</summary>
        public HoLookAtEyeDriver EyeDriver => eyeDriver;

        public bool EyeBonesAvailable => animator != null && animator.isHuman
                                         && (animator.GetBoneTransform(HumanBodyBones.LeftEye) != null
                                             || animator.GetBoneTransform(HumanBodyBones.RightEye) != null);

        /// <summary>骨骼模式实际转出去的角度（度）。</summary>
        public float AppliedEyeYaw => state.smoothedEyeYaw;

        public float AppliedEyePitch => state.smoothedEyePitch;

        public float EyeBoneLimitYaw => eyeBoneLimitYaw;

        public float EyeBoneLimitPitch => eyeBoneLimitPitch;

        /// <summary>头部估计朝向（我们让 Unity 看哪 × 它施加的比例 + 模型偏差 trim）。</summary>
        public float HeadEstimateYaw => state.headEstimateYaw;

        public float HeadEstimatePitch => state.headEstimatePitch;

        /// <summary>本帧头部实际转过的量（旋转增量），只作信息用。</summary>
        public float HeadDeltaYaw => state.headDeltaYaw;

        public float HeadDeltaPitch => state.headDeltaPitch;

        /// <summary>
        /// 目光落点误差（度）：目标角 −（头部实际 + 眼睛实际）。
        /// 强度拉满、没被限位夹住、平滑跟得上时应该接近 0 —— 这个数就是"指哪看哪"的自检。
        /// </summary>
        public float GazeErrorYaw => Mathf.DeltaAngle(state.headEstimateYaw + state.smoothedEyeYaw, TotalAngles.yaw);

        public float GazeErrorPitch => Mathf.DeltaAngle(state.headEstimatePitch + state.smoothedEyePitch, TotalAngles.pitch);

        /// <summary>形态键模式用：有没有可写的网格/绑定。</summary>
        public bool ShapeKeyEyesActive => eyeDriver == HoLookAtEyeDriver.ShapeKeys && MeshCount > 0;

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
                headEstimateYaw = state.headEstimateYaw,
                headEstimatePitch = state.headEstimatePitch,
                headDeltaYaw = state.headDeltaYaw,
                headDeltaPitch = state.headDeltaPitch,
                eyeYaw = state.smoothedEyeYaw,
                eyePitch = state.smoothedEyePitch,
                eyeOverflowYaw = state.eyeOverflowYaw,
                eyeOverflowPitch = state.eyeOverflowPitch,
                hasTarget = state.hasTarget,
                targetValid = state.applyLookAt
            };

            GetEyeSettings(out HoEyeSettings settings, out AnimationCurve inner, out AnimationCurve outer, out AnimationCurve up, out AnimationCurve down);

            // 四条通道的量 = 和写形态键时同一套算法（角度 → 按上限归一化 → 过曲线）。
            // 往左/往右/看上/看下各一条曲线与上限，两只眼共用同一个量。
            debug.lookRight = ShapeAmount(Mathf.Max(state.smoothedEyeYaw, 0.0f), settings.angleLimitInner, inner);
            debug.lookLeft = ShapeAmount(Mathf.Max(-state.smoothedEyeYaw, 0.0f), settings.angleLimitOuter, outer);
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

            // 实际目光方向与落点：落点取"和目标同样距离"处，方便屏幕上对比两个点
            debug.gazeYaw = debug.headEstimateYaw + debug.eyeYaw;
            debug.gazePitch = debug.headEstimatePitch + debug.eyePitch;
            Vector3 gazeDirection = HoLookAtSolver.DirectionFromAngles(
                GetReferenceForward(),
                GetReferenceUp(),
                debug.gazeYaw,
                debug.gazePitch);
            float gazeDistance = Vector3.Distance(Pivot, debug.targetPoint);
            debug.gazePoint = Pivot + gazeDirection * Mathf.Max(0.1f, gazeDistance);
            debug.hasGazePoint = true;

            return debug;
        }

        // ── 生命周期 ────────────────────────────────────────────────────────

        private void OnEnable()
        {
            MigrateMouseMode();
            built = false;
            ResolveAnimator();
            EnsureBuilt();
            EnsureIkRelay();
            ResetState();
        }

        private void OnDisable()
        {
            // 关掉组件时把自己动过的东西交还：形态键硬写回基准，眼球骨骼放回叠加前的姿势
            writer.RestoreWritten();
            RestoreEyeBones();
            built = false;
            ReleaseIkRelay();
        }

        private void OnDestroy()
        {
            ReleaseIkRelay();
        }

        /// <summary>
         /// 一次性迁移：老的默认值是"角度摇杆"，它跟鼠标位置没有几何关系（灵敏度是人为定的），
         /// 所以"鼠标指哪看哪"在那套映射下永远对不上。装过这个版本之后自动切成"准星"，
         /// 想用摇杆手感再手动切回去（标记已经置位，不会再被改）。
         /// </summary>
        private void MigrateMouseMode()
        {
            if (mouseModeMigrated)
            {
                return;
            }

            mouseModeMigrated = true;
            if (mouseSampleMode == HoLookAtMouseSampleMode.AngleMap)
            {
                mouseSampleMode = HoLookAtMouseSampleMode.CursorPoint;
            }
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

            // 记下"交给 Unity 之前"的头部姿势：LateUpdate 里拿它和 IK 之后的姿势一比，
            // 就知道头部**实际**转了多少（不是我们命令它转多少）。
            CacheHeadPoseBeforeIk();

            // 角度是从两眼中点解出来的，但交给 Unity 的瞄准点必须从**头骨原点**出发：
            // Unity 转的是头骨，从眼睛出发的点会让头的朝向差那么一点。
            animator.SetLookAtPosition(HoLookAtSolver.PointFromAngles(
                GetHeadOrigin(),
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
            float headFactor = 0.0f;
            if (headEnabled && externalHeadWeight > 0.0f && state.applyLookAt)
            {
                float headPart = Mathf.Clamp01(headWeight * externalHeadWeight);
                animator.SetLookAtWeight(totalWeight, spine, headPart, 0.0f, UnityClampWeight);
                headFactor = totalWeight * headPart;
            }
            else
            {
                animator.SetLookAtWeight(0.0f, 0.0f, 0.0f, 0.0f, UnityClampWeight);
            }

            // 头部**估计**朝向 = 我们让它看的方向 × Unity 实际施加的比例（totalWeight × headWeight）。
            // Unity 的 LookAt 契约就是"把头朝向我们给的点"，所以这个估计比"量骨骼旋转增量"可靠得多 ——
            // 后者丢掉了动画/静止姿势本身那一份头部角度，会固定偏几度（骨骼局部坐标系里也确实算不出绝对朝向）。
            headAppliedFactor = Mathf.Clamp01(headFactor);
            state.headCommanded = headFactor > 0.0f;
            state.headEstimateYaw = head.yaw * headAppliedFactor + headDirectionTrim.x;
            state.headEstimatePitch = head.pitch * headAppliedFactor + headDirectionTrim.y;
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

            // 骨骼模式不写形态键：一个目标都不注册。
            // 于是从形态键模式切过来时，写入器会在重建收尾把之前写过的键硬写回基准（自动清场）。
            if (eyeDriver == HoLookAtEyeDriver.ShapeKeys && eyeEntries != null)
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

                    int leftId = RegisterEyeKey(entry.LeftEye);

                    // 两只眼填同一个键（VRM/Meta 的 LookLeft 这种双眼共用的键）时只注册一次：
                    // 注册两遍会让同一个键被写两遍、和翻倍直接顶到 100。
                    int rightId = HoLookAtEyeKey.SameKeyName(entry.LeftEye, entry.RightEye)
                        ? leftId
                        : RegisterEyeKey(entry.RightEye);

                    eyeTargetIds[index, 0] = leftId;
                    eyeTargetIds[index, 1] = rightId;
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

            // 注意：这里**不乘总强度** —— 总强度由 Unity 的 SetLookAtWeight(weight) 施加一次，
            // 眼睛那边自己也乘一次（各一次）。以前两处都乘，weight = 0.5 时实际只剩 0.25。
            // 眼睛范围一起传进去：残余超过眼球能转的范围时，超出的部分还给头，
            // 否则就会出现"总角 18°、头 7°、眼球限位 10°"→ 目光永远差 1° 的情况。
            HoLookAtAngles headAngles = HoLookAtSolver.Split(total, head, GetEyeRangeSettings(), 1.0f, ref state);
            HeadAngles = headAngles;
            return headAngles;
        }

        /// <summary>
        /// 当前模式下的眼球活动范围（给分工用：残余超出它的部分还给头）。
        /// 骨骼模式就是那两个限位（左右对称），形态键模式是四个角度上限。
        /// </summary>
        private HoEyeSettings GetEyeRangeSettings()
        {
            if (eyeDriver == HoLookAtEyeDriver.EyeBones)
            {
                HoEyeSettings bones = new HoEyeSettings
                {
                    angleLimitInner = eyeBoneLimitYaw,
                    angleLimitOuter = eyeBoneLimitYaw,
                    angleLimitUp = eyeBoneLimitPitch,
                    angleLimitDown = eyeBoneLimitPitch
                };
                bones.Sanitize();
                return bones;
            }

            GetEyeSettings(out HoEyeSettings settings, out _, out _, out _, out _);
            return settings;
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
                    pivot = pivot,
                    distance = mouseDistance,
                    raycastMask = mouseRaycastMask,
                    holdOffscreen = mouseHoldOffscreen,
                    useSceneViewMouse = useSceneViewMouse
                };

                HoPointerSample sample = HoMousePointer.Sample(settings);
                if (sample.valid)
                {
                    // 记下来给 Game 视图叠加/调试用（那里不能用 UnityEngine.Input：项目可能只开了 Input System）
                    lastPointerScreen = sample.screenPosition;
                    hasPointerScreen = true;
                    lastPointerRay = sample.ray;
                    hasPointerRay = sample.hasRay;
                    if (sample.camera != null)
                    {
                        PointerCamera = sample.camera;
                    }
                }
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

                if (mouseSampleMode == HoLookAtMouseSampleMode.CursorPoint)
                {
                    // 「跟随鼠标」：归一化偏移 × 相机半 FOV × 灵敏度，基准方向 = 看镜头。
                    // 这是 VRM 生态的做法（three-vrm 的 LookAtRangeMap：input × outputScale 度），
                    // 也是"角色面对镜头"这一类唯一稳的做法：
                    //   · 鼠标指着角色（屏幕上的两眼中点）→ 视线正对镜头；
                    //   · 鼠标到画面边缘 → 视线转到相机视角的边缘（跟手、大致 1:1）；
                    //   · 角度天然被 FOV 限制，没有"射线交点"在角色附近半径趋零、方向乱摆的问题。
                    Camera aimCamera = sample.camera != null ? sample.camera : GetMouseCamera();
                    if (aimCamera != null)
                    {
                        Rect pixelRect = aimCamera.pixelRect;
                        float halfWidth = Mathf.Max(1.0f, pixelRect.width * 0.5f);
                        float halfHeight = Mathf.Max(1.0f, pixelRect.height * 0.5f);

                        Vector2 origin = new Vector2(pixelRect.x + halfWidth, pixelRect.y + halfHeight);
                        if (mouseAimFromCharacter)
                        {
                            Vector3 pivotScreen = aimCamera.WorldToScreenPoint(pivot);
                            if (pivotScreen.z > 0.0f)
                            {
                                origin = new Vector2(pivotScreen.x, pivotScreen.y);
                            }
                        }

                        float nx = Mathf.Clamp((sample.screenPosition.x - origin.x) / halfWidth, -2.0f, 2.0f);
                        float ny = Mathf.Clamp((sample.screenPosition.y - origin.y) / halfHeight, -2.0f, 2.0f);

                        HoMousePointer.GetHalfFov(aimCamera, pivot, out float halfYaw, out float halfPitch);
                        mouseAngleYaw = nx * halfYaw * mouseAimGain;
                        mouseAnglePitch = ny * halfPitch * mouseAimGain;
                        hasMouseAngles = true;

                        // 观察者坐标系构造方向（+X 观众右、+Y 上、−Z 朝向观众 = 看镜头）
                        Transform view = aimCamera.transform;
                        Vector3 local = new Vector3(
                            Mathf.Tan(mouseAngleYaw * Mathf.Deg2Rad),
                            Mathf.Tan(mouseAnglePitch * Mathf.Deg2Rad),
                            -1.0f);
                        direction = view.TransformDirection(local.normalized);
                        isDirection = true;
                        return true;
                    }

                    // 没相机就退回"以屏幕中心为原点的角度摇杆"，至少还能动
                }

                if (mouseSampleMode == HoLookAtMouseSampleMode.AngleMap)
                {
                    // 相机会跟采样走：编辑器里鼠标在 Scene 视图上时，这里拿到的是 Scene 视图相机
                    Camera camera = sample.camera != null ? sample.camera : GetMouseCamera();
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

        /// <summary>
        /// 解算用的支点 = **两眼的中点**（没有眼球骨骼时退回 Head）。
        ///
        /// 为什么不直接用头骨原点：眼睛在头骨前面约 10cm，从头骨算出来的方向，
        /// 眼睛那条视线会整体偏一点点（2~4°，在脸上非常显眼）。
        /// 从眼睛出发算方向，视线才正好落在目标上；交给 Unity 的瞄准点仍以头骨原点为起点（见 HandleAnimatorIK）。
        /// </summary>
        private Vector3 GetPivot()
        {
            if (animator != null && animator.isHuman)
            {
                Transform left = animator.GetBoneTransform(HumanBodyBones.LeftEye);
                Transform right = animator.GetBoneTransform(HumanBodyBones.RightEye);
                if (left != null && right != null)
                {
                    return (left.position + right.position) * 0.5f;
                }

                if (left != null)
                {
                    return left.position;
                }

                if (right != null)
                {
                    return right.position;
                }

                Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (head != null)
                {
                    return head.position;
                }
            }

            return transform.position;
        }

        /// <summary>头骨原点：SetLookAtPosition 用它当射线起点（Unity 的 IK 转的是头骨，瞄准点要从头骨出发）。</summary>
        private Vector3 GetHeadOrigin()
        {
            if (animator != null && animator.isHuman)
            {
                Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (head != null)
                {
                    return head.position;
                }
            }

            return GetPivot();
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

        /// <summary>
        /// 眼睛残余角 → 眼睛。两套驱动**按模式二选一**（不混用）：
        ///   骨骼模式（默认）：残余角直接转到 humanoid 的 LeftEye/RightEye 上，精确指向，不需要标定；
        ///   形态键模式：残余角过四条方向曲线写成凝视键，需要按模型标定角度上限。
        /// 两条路都在 LateUpdate（头部 IK 之后）。
        /// </summary>
        private void ApplyEyes(float deltaTime)
        {
            float effectiveWeight = Mathf.Clamp01(weight * externalWeight) * (externalEnabled ? 1.0f : 0.0f);
            float eyes = eyesEnabled ? Mathf.Clamp01(eyeWeight * externalEyeWeight) * effectiveWeight : 0.0f;

            // 眼睛补的是"目标 − 头部估计朝向"。
            // 头部估计 = 我们让 Unity 看的方向 × 它实际施加的比例，所以：
            //   · 头没转到位（headWeight < 1、IK 上限）时眼睛自动补齐；
            //   · 「只看眼睛」时 headEnabled = false → 估计为 0 → 眼睛吃全部；
            //   · 模型静止姿势的固定偏差用 headDirectionTrim 修。
            float headYaw = state.headEstimateYaw;
            float headPitch = state.headEstimatePitch;

            if (headMeasuredThisFrame)
            {
                // 顺带量一下"这一帧头实际转了多少"，给调试区看 IK 有没有偷懒
                MeasureActualHead(out float deltaYaw, out float deltaPitch);
                state.headDeltaYaw = deltaYaw;
                state.headDeltaPitch = deltaPitch;
                headMeasuredThisFrame = false;
            }

            float yaw = (TotalAngles.yaw - headYaw) * eyes;
            float pitch = (TotalAngles.pitch - headPitch) * eyes;
            state.eyeYaw = yaw;
            state.eyePitch = pitch;

            if (eyeDriver == HoLookAtEyeDriver.EyeBones)
            {
                HoLookAtSolver.ClampAxes(ref yaw, ref pitch, eyeBoneLimitYaw, eyeBoneLimitPitch);
                SmoothEyes(ref yaw, ref pitch, deltaTime);
                ApplyEyeBones(yaw, pitch);
                return;
            }

            GetEyeSettings(out HoEyeSettings settings, out AnimationCurve inner, out AnimationCurve outer, out AnimationCurve up, out AnimationCurve down);
            HoLookAtSolver.ClampAxes(ref yaw, ref pitch, settings.angleLimitInner, settings.angleLimitUp);
            SmoothEyes(ref yaw, ref pitch, deltaTime);

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
        }

        private void SmoothEyes(ref float yaw, ref float pitch, float deltaTime)
        {
            state.smoothedEyeYaw = HoLookAtSolver.SmoothAngle(state.smoothedEyeYaw, yaw, deltaTime, eyeSmoothing, 0.0f);
            state.smoothedEyePitch = HoLookAtSolver.SmoothAngle(state.smoothedEyePitch, pitch, deltaTime, eyeSmoothing, 0.0f);
            yaw = state.smoothedEyeYaw;
            pitch = state.smoothedEyePitch;
        }

        /// <summary>OnAnimatorIK 里、调用 SetLookAtWeight **之前**记下头部姿态（这时候还是动画姿势）。</summary>
        private void CacheHeadPoseBeforeIk()
        {
            if (headMeasuredThisFrame)
            {
                // 本帧已经记过（多个图层勾了 IK Pass 时会回调多次）：只在第一次记，
                // 否则第二次记到的是"已经被第一层 IK 转过"的姿势，量出来的贡献就少了一截。
                return;
            }

            if (animator == null || !animator.isHuman)
            {
                return;
            }

            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head == null)
            {
                return;
            }

            headPoseBeforeIk = head.rotation;
            headHasPoseBeforeIk = true;
            headMeasuredThisFrame = true;
        }

        /// <summary>
        /// 量出头部本帧**实际转过多少**（度）—— 只是信息：和"我们让它转多少"对比可以看出 IK 有没有做到。
        ///
        /// 注意它**不能**当绝对朝向用：这是"IK 前→后"的旋转增量，不含动画/静止姿势本身那一份头部角度。
        /// 而头骨骼的局部坐标系里根本不存在"绝对前方"（差一个模型常数），所以绝对朝向只能按
        /// Unity 的瞄准约定去估计（见 HandleAnimatorIK 里的 headEstimate）。
        /// </summary>
        private void MeasureActualHead(out float yaw, out float pitch)
        {
            yaw = 0.0f;
            pitch = 0.0f;

            if (!headHasPoseBeforeIk || animator == null || !animator.isHuman)
            {
                return;
            }

            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head == null)
            {
                return;
            }

            Vector3 forward = GetReferenceForward();
            Vector3 up = GetReferenceUp();
            Quaternion delta = head.rotation * Quaternion.Inverse(headPoseBeforeIk);
            HoLookAtAngles angles = HoLookAtSolver.Decompose(forward, up, delta * forward);
            yaw = angles.yaw;
            pitch = angles.pitch;
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
                case HoLookAtEyeChannel.LookRight:
                    // 往右 = 左眼的内侧（In）；曲线与上限沿用原来的"内"
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
                    // 往左 = 外侧（Out），沿用原来的"外"
                    curve = outer;
                    limit = settings.angleLimitOuter;
                    break;
            }
        }

        private void GetChannelAmounts(HoLookAtEyeChannel channel, out float leftEye, out float rightEye)
        {
            // 四条通道都是"两只眼同一个量"：横向的 In/Out 差异已经由"哪只眼填哪个键"表达掉了
            // （往右 = 左眼 In + 右眼 Out），所以这里不再按眼别分叉。
            float amount;
            switch (channel)
            {
                case HoLookAtEyeChannel.LookLeft:
                    amount = Mathf.Max(-state.eyeYaw, 0.0f);
                    break;

                case HoLookAtEyeChannel.LookRight:
                    amount = Mathf.Max(state.eyeYaw, 0.0f);
                    break;

                case HoLookAtEyeChannel.Up:
                    amount = Mathf.Max(state.eyePitch, 0.0f);
                    break;

                default:
                    amount = Mathf.Max(-state.eyePitch, 0.0f);
                    break;
            }

            leftEye = amount;
            rightEye = amount;
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

        /// <summary>
        /// 骨骼模式：把残余角（已在外面乘过强度、夹过限位、平滑过）叠加到眼球骨骼上。
        /// 走世界空间叠加，所以**不猜骨骼轴向**；角是多少就转多少，因此能精确指向目标。
        /// </summary>
        private void ApplyEyeBones(float yaw, float pitch)
        {
            if (animator == null || !animator.isHuman)
            {
                return;
            }

            Transform left = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            Transform right = animator.GetBoneTransform(HumanBodyBones.RightEye);
            if (left == null && right == null)
            {
                return;
            }

            // 记下叠加前的姿势：组件被关掉且动画也不写这两根骨头时，用它把眼睛放回去
            if (left != null)
            {
                leftEyeRest = left.rotation;
            }

            if (right != null)
            {
                rightEyeRest = right.rotation;
            }

            eyeBonesCaptured = true;

            Vector3 upAxis = GetReferenceUp();
            Vector3 rightAxis = Vector3.Cross(upAxis, GetReferenceForward()).normalized;
            Quaternion delta = Quaternion.AngleAxis(yaw, upAxis) * Quaternion.AngleAxis(-pitch, rightAxis);

            if (left != null)
            {
                left.rotation = delta * left.rotation;
            }

            if (right != null)
            {
                right.rotation = delta * right.rotation;
            }
        }

        /// <summary>把眼球骨骼放回上一次叠加前的姿势（组件被关掉时用）。</summary>
        private void RestoreEyeBones()
        {
            if (!eyeBonesCaptured || animator == null || !animator.isHuman)
            {
                return;
            }

            Transform left = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            Transform right = animator.GetBoneTransform(HumanBodyBones.RightEye);
            if (left != null)
            {
                left.rotation = leftEyeRest;
            }

            if (right != null)
            {
                right.rotation = rightEyeRest;
            }

            eyeBonesCaptured = false;
        }

#if UNITY_EDITOR
        // ── Game 视图屏幕叠加（只在编辑器里编译；构建里这段不存在）────────────
        //
        // 为什么需要它：用鼠标调试时人只能待在 Game 视图，而 Gizmo 只在 Scene 视图里画，
        // 于是"能操作的地方看不到信息"。这里把关键信息直接叠在 Game 视图上：
        //   青色十字 = 鼠标位置（本次采样用的那个点）
        //   黄色圆点 = 目标点
        //   紫色圆点 = 实际目光落点（和目标同距离）
        //   两点之间的连线 = 偏差；读数里也有度数

        private static Texture2D overlayPixel;

        private void OnGUI()
        {
            if (!drawOverlay || !isActiveAndEnabled || !ShouldEvaluate())
            {
                return;
            }

            HoLookAtDebug debug = GetDebug();
            Camera camera = PointerCamera != null ? PointerCamera : mouseCamera;
            Texture2D pixel = GetOverlayPixel();

            Vector2 mouseGui = hasPointerScreen
                ? ToGuiPoint(lastPointerScreen)
                : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 gazeGui = Vector2.zero;
            Vector2 targetGui = Vector2.zero;
            bool hasGaze = false;
            bool hasTargetDot = false;

            if (camera != null)
            {
                hasGaze = TryProject(camera, debug.gazePoint, out gazeGui);
                hasTargetDot = TryProject(camera, debug.targetPoint, out targetGui);
            }
            else
            {
                // 没相机时也能用：直接把角度画成横向偏移（至少看得见"在动 / 偏多少"）
                mouseGui = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            }

            GUI.depth = -1000;

            if (hasTargetDot)
            {
                DrawOverlayDot(pixel, targetGui, new Color(1.0f, 0.82f, 0.25f, 0.95f), 5.0f);
            }

            if (hasGaze)
            {
                DrawOverlayDot(pixel, gazeGui, new Color(0.80f, 0.55f, 1.0f, 0.95f), 5.0f);
                if (hasTargetDot)
                {
                    DrawOverlayLine(pixel, gazeGui, targetGui, new Color(1.0f, 1.0f, 1.0f, 0.55f), 1.0f);
                }
            }

            DrawOverlayCross(pixel, mouseGui, new Color(0.30f, 0.95f, 0.95f, 0.95f), 9.0f, 1.0f);

            float error = Mathf.Abs(GazeErrorYaw) + Mathf.Abs(GazeErrorPitch);
            string text =
                "Ho 注视　" + (targetMode == HoLookAtMode.Mouse ? "鼠标" : "物体")
                + "　总 " + TotalAngles.yaw.ToString("0.0") + "°/" + TotalAngles.pitch.ToString("0.0") + "°"
                + "　头估计 " + state.headEstimateYaw.ToString("0.0") + "°/" + state.headEstimatePitch.ToString("0.0") + "°"
                + "　眼 " + state.smoothedEyeYaw.ToString("0.0") + "°/" + state.smoothedEyePitch.ToString("0.0") + "°"
                + "　头增量 " + state.headDeltaYaw.ToString("0.0") + "°/" + state.headDeltaPitch.ToString("0.0") + "°"
                + (Mathf.Abs(state.eyeOverflowYaw) + Mathf.Abs(state.eyeOverflowPitch) > 0.5f
                    ? "　眼球超范围 " + state.eyeOverflowYaw.ToString("0.0") + "°/" + state.eyeOverflowPitch.ToString("0.0") + "°（已还给头）"
                    : string.Empty)
                + "　误差 " + GazeErrorYaw.ToString("0.0") + "°/" + GazeErrorPitch.ToString("0.0") + "°"
                + (error < 1.0f ? "（精确）" : "（偏了）")
                + (eyeDriver == HoLookAtEyeDriver.EyeBones ? "　骨骼" : "　形态键");

            GUIContent content = new GUIContent(text);
            Vector2 size = EditorOverlayStyle.CalcSize(content);
            Rect box = new Rect(8.0f, 8.0f, size.x + 12.0f, size.y + 6.0f);
            GUI.color = new Color(0.0f, 0.0f, 0.0f, 0.55f);
            GUI.DrawTexture(box, pixel);
            GUI.color = Color.white;
            GUI.Label(new Rect(box.x + 6.0f, box.y + 3.0f, size.x, size.y), content, EditorOverlayStyle);
        }

        private static GUIStyle overlayStyle;

        private static GUIStyle EditorOverlayStyle
        {
            get
            {
                if (overlayStyle == null)
                {
                    overlayStyle = new GUIStyle(GUI.skin.label)
                    {
                        fontSize = 12,
                        richText = false
                    };
                    overlayStyle.normal.textColor = Color.white;
                }

                return overlayStyle;
            }
        }

        private static Texture2D GetOverlayPixel()
        {
            if (overlayPixel == null)
            {
                overlayPixel = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                overlayPixel.SetPixel(0, 0, Color.white);
                overlayPixel.Apply();
            }

            return overlayPixel;
        }

        private static Vector2 ToGuiPoint(Vector2 screenPoint)
        {
            // 屏幕像素（左下原点）→ GUI 坐标（左上原点，并且尊重 Game 视图的缩放）
            return GUIUtility.ScreenToGUIPoint(new Vector2(screenPoint.x, Screen.height - screenPoint.y));
        }

        private static bool TryProject(Camera camera, Vector3 worldPoint, out Vector2 guiPoint)
        {
            Vector3 screen = camera.WorldToScreenPoint(worldPoint);
            if (screen.z <= 0.0f)
            {
                guiPoint = Vector2.zero;
                return false;
            }

            guiPoint = ToGuiPoint(new Vector2(screen.x, screen.y));
            return true;
        }

        private static void DrawOverlayDot(Texture2D pixel, Vector2 center, Color color, float size)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size), pixel);
            GUI.color = previous;
        }

        private static void DrawOverlayCross(Texture2D pixel, Vector2 center, Color color, float size, float thickness)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(center.x - size, center.y - thickness * 0.5f, size * 2.0f, thickness), pixel);
            GUI.DrawTexture(new Rect(center.x - thickness * 0.5f, center.y - size, thickness, size * 2.0f), pixel);
            GUI.color = previous;
        }

        private static void DrawOverlayLine(Texture2D pixel, Vector2 from, Vector2 to, Color color, float thickness)
        {
            Vector2 delta = to - from;
            float length = delta.magnitude;
            if (length < 0.5f)
            {
                return;
            }

            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            Matrix4x4 previous = GUI.matrix;
            Color previousColor = GUI.color;
            GUI.color = color;
            GUIUtility.RotateAroundPivot(angle, from);
            GUI.DrawTexture(new Rect(from.x, from.y - thickness * 0.5f, length, thickness), pixel);
            GUI.matrix = previous;
            GUI.color = previousColor;
        }
#endif
    }
}
