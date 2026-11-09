using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    [Serializable]
    public sealed class HoVtsLookAtDebug
    {
        public string status;
        public bool hasFrame;
        public bool tracked;
        public Vector3 referencePosition;
        public Vector3 referenceForward;
        public Vector3 referenceUp;
        public Vector3 headPosition;
        public Vector3 leftEyePosition;
        public Vector3 rightEyePosition;
        public Quaternion headTargetRotation = Quaternion.identity;
        public Quaternion leftEyeTargetRotation = Quaternion.identity;
        public Quaternion rightEyeTargetRotation = Quaternion.identity;
        public Quaternion headActualRotation = Quaternion.identity;
        public Quaternion leftEyeActualRotation = Quaternion.identity;
        public Quaternion rightEyeActualRotation = Quaternion.identity;
        public bool hasHead;
        public bool hasLeftEye;
        public bool hasRightEye;
    }

    /// <summary>眼球驱动方式：骨骼模式转眼骨，形态键模式直接写眼球方向形态键。</summary>
    public enum HoVtsEyeDriver
    {
        [InspectorName("骨骼")] Bone,
        [InspectorName("形态键")] ShapeKey
    }

    /// <summary>
    /// 眼骨四点标定（VRC 式）：rest 为静止局部朝向，left/right/up/down 为眼球看满对应方向
    /// 时的局部朝向。未标定（captured=false）时运行时保持原来的 PoseRotation 行为。
    /// </summary>
    [Serializable]
    public sealed class HoVtsEyeBoneCalibration
    {
        public bool captured;
        public Quaternion rest = Quaternion.identity;
        public Quaternion left = Quaternion.identity;
        public Quaternion right = Quaternion.identity;
        public Quaternion up = Quaternion.identity;
        public Quaternion down = Quaternion.identity;
    }

    /// <summary>形态键模式下每只眼睛四个方向对应的形态键名。</summary>
    [Serializable]
    public sealed class HoVtsEyeKeyTargets
    {
        public string left, right, up, down;
    }

    /// <summary>
    /// VTS head/eye rotation constraint. Uses the reference frame's axes rather than assuming
    /// the model's bone axes. No Animator, Avatar, IK pass, or face controller is required.
    /// 眼球可选骨骼或形态键两种驱动：骨骼模式支持四点标定，形态键模式直接写眼球方向键。
    /// 注意：形态键模式写入的键不能同时作为 HoVtsIrController 的表情输出目标，否则两边会互相覆写；
    /// 瞳孔/眼仁这类键目前不在 IR 的输出里，可以安全使用。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HoUnityTools/面捕/VTS 头眼姿态")]
    [DefaultExecutionOrder(32000)]
    public sealed class HoVtsLookAt : MonoBehaviour
    {
        [SerializeField] private HoVtsParameterReceiver parameterReceiver;
        [SerializeField] private Transform referenceFrame;
        [SerializeField] private Transform upperBody;
        [SerializeField] private Transform[] upperBodyChain = new Transform[0];
        [SerializeField] private Transform head;
        [SerializeField] private Transform leftEye;
        [SerializeField] private Transform rightEye;
        [SerializeField] private HoVtsEyeDriver eyeDriver = HoVtsEyeDriver.Bone;
        [SerializeField] private HoVtsEyeBoneCalibration leftBoneCalibration = new HoVtsEyeBoneCalibration();
        [SerializeField] private HoVtsEyeBoneCalibration rightBoneCalibration = new HoVtsEyeBoneCalibration();
        [SerializeField, Min(1f)] private float boneFullAngle = 30f;
        [SerializeField] private List<SkinnedMeshRenderer> eyeKeyMeshes = new List<SkinnedMeshRenderer>();
        [SerializeField] private HoVtsEyeKeyTargets leftKeyTargets = new HoVtsEyeKeyTargets();
        [SerializeField] private HoVtsEyeKeyTargets rightKeyTargets = new HoVtsEyeKeyTargets();
        [SerializeField, Min(1f)] private float keyFullAngle = 30f;
        [SerializeField, Range(0f, 200f)] private float keyWeightScale = 100f;
        [SerializeField] private bool previewCalibration;
        [SerializeField] private float previewYaw;
        [SerializeField] private float previewPitch;
        [SerializeField] private bool headEnabled = true;
        [SerializeField, Range(0f, 1f)] private float headWeight = 1f;
        [SerializeField, Range(0f, 180f)] private float headMaxAngle = 180f;
        [SerializeField] private bool upperBodyEnabled;
        [SerializeField, Range(0f, 1f)] private float upperBodyWeight = .25f;
        [SerializeField, Range(0f, 180f)] private float upperBodyMaxAngle = 180f;
        [SerializeField] private bool eyesEnabled = true;
        [SerializeField] private bool autoRun = true;
        [SerializeField, Min(0)] private float smoothingSeconds;
        [SerializeField] private bool drawGizmos = true;
        [SerializeField, Min(0.01f)] private float gizmoLength = 0.12f;

        private RotationState[] upperBodyStates = new RotationState[0];
        private RotationState headState, leftState, rightState;
        private HoVtsEyeDriver resolvedEyeDriver;
        private bool resolvedLeftCalibration, resolvedRightCalibration;
        private readonly List<EyeKeyBinding> eyeKeyBindings = new List<EyeKeyBinding>();
        private readonly List<SkinnedMeshRenderer> boundEyeKeyRenderers = new List<SkinnedMeshRenderer>();
        private readonly List<Mesh> boundEyeKeyMeshes = new List<Mesh>();
        private readonly List<string> boundEyeKeyNames = new List<string>();
        private readonly float[] eyeKeyWeights = new float[8];
        private readonly float[] eyeKeyTargetWeights = new float[8];
        private bool eyeKeyBindingsLoaded, eyeKeyWeightsValid, eyeKeyWeightsPrimed;
        private string eyeKeyBindingError;
        private Transform resolvedReference;
        private HoVtsParameterReceiver resolvedReceiver;
        private string status = "未初始化";
        private int lastPreCullFrame = -1;

        public HoVtsParameterReceiver ParameterReceiver { get => parameterReceiver; set { if (parameterReceiver != value) { Stop(); parameterReceiver = value; } } }
        public Transform ReferenceFrame { get => referenceFrame; set { if (referenceFrame != value) { Stop(); referenceFrame = value; } } }
        public Transform UpperBody { get => upperBody; set { if (upperBody != value) { Stop(); upperBody = value; } } }
        public Transform[] UpperBodyChain
        {
            get => upperBodyChain;
            set { Stop(); upperBodyChain = value ?? new Transform[0]; }
        }
        public Transform Head { get => head; set { if (head != value) { Stop(); head = value; } } }
        public Transform LeftEye { get => leftEye; set { if (leftEye != value) { Stop(); leftEye = value; } } }
        public Transform RightEye { get => rightEye; set { if (rightEye != value) { Stop(); rightEye = value; } } }
        public HoVtsEyeDriver EyeDriver { get => eyeDriver; set { Stop(); eyeDriver = value; } }
        public HoVtsEyeBoneCalibration LeftBoneCalibration => leftBoneCalibration ??= new HoVtsEyeBoneCalibration();
        public HoVtsEyeBoneCalibration RightBoneCalibration => rightBoneCalibration ??= new HoVtsEyeBoneCalibration();
        public float BoneFullAngle { get => boneFullAngle; set => boneFullAngle = Mathf.Max(1f, value); }
        public List<SkinnedMeshRenderer> EyeKeyMeshes => eyeKeyMeshes ??= new List<SkinnedMeshRenderer>();
        public HoVtsEyeKeyTargets LeftKeyTargets => leftKeyTargets ??= new HoVtsEyeKeyTargets();
        public HoVtsEyeKeyTargets RightKeyTargets => rightKeyTargets ??= new HoVtsEyeKeyTargets();
        public float KeyFullAngle { get => keyFullAngle; set => keyFullAngle = Mathf.Max(1f, value); }
        public float KeyWeightScale { get => keyWeightScale; set => keyWeightScale = Mathf.Clamp(value, 0f, 200f); }
        public bool PreviewCalibration => previewCalibration;
        public float PreviewYaw => previewYaw;
        public float PreviewPitch => previewPitch;
        public bool HeadEnabled { get => headEnabled; set => headEnabled = value; }
        public float HeadWeight { get => headWeight; set => headWeight = Mathf.Clamp01(value); }
        public float HeadMaxAngle { get => headMaxAngle; set => headMaxAngle = Mathf.Clamp(value, 0f, 180f); }
        public bool UpperBodyEnabled { get => upperBodyEnabled; set => upperBodyEnabled = value; }
        public float UpperBodyWeight { get => upperBodyWeight; set => upperBodyWeight = Mathf.Clamp01(value); }
        public float UpperBodyMaxAngle { get => upperBodyMaxAngle; set => upperBodyMaxAngle = Mathf.Clamp(value, 0f, 180f); }
        public bool EyesEnabled { get => eyesEnabled; set => eyesEnabled = value; }
        public bool AutoRun { get => autoRun; set => autoRun = value; }
        public float SmoothingSeconds { get => smoothingSeconds; set => smoothingSeconds = Mathf.Max(0, value); }
        public bool DrawGizmos { get => drawGizmos; set => drawGizmos = value; }
        public float GizmoLength { get => gizmoLength; set => gizmoLength = Mathf.Max(0.01f, value); }
        public string Status => status;

        /// <summary>
        /// 预览标定：只接管眼球求值，头和上身不变。骨骼模式用标定后的四点插值，形态键模式按同一
        /// 角度算权重（角度按 VTS 语义，+x 向右、+y 向上）。开启后 Play 模式不读 VTS 的眼球参数；
        /// 关闭则在下一帧恢复正常求值。编辑模式调用只影响 Gizmo。
        /// </summary>
        public void SetPreview(bool on, float yawDegrees, float pitchDegrees)
        {
            previewCalibration = on;
            previewYaw = float.IsNaN(yawDegrees) || float.IsInfinity(yawDegrees) ? 0f : Mathf.Clamp(yawDegrees, -180f, 180f);
            previewPitch = float.IsNaN(pitchDegrees) || float.IsInfinity(pitchDegrees) ? 0f : Mathf.Clamp(pitchDegrees, -180f, 180f);
        }

        private void OnEnable() { Camera.onPreCull += OnCameraPreCull; }
        private void LateUpdate() { if (autoRun) EvaluateNow(Time.deltaTime); }

        private void OnCameraPreCull(Camera camera)
        {
            if (!autoRun || lastPreCullFrame == Time.frameCount) return;
            lastPreCullFrame = Time.frameCount;
            if (!EnsureTargets()) return;

            // Warudo writes its humanoid pose after ordinary MonoBehaviour
            // LateUpdate. Re-apply our sampled pose immediately before rendering.
            // 只重放本帧在 LateUpdate 里已经平滑过的 offset；这里若用 blend=1，
            // 每帧渲染前都会直接贴到目标值，smoothingSeconds 就等于失效。
            Quaternion frame = referenceFrame.rotation;
            bool written = false;
            if (headEnabled) written |= headState != null && headState.Apply(frame, 0f);
            if (upperBodyEnabled && upperBodyStates != null)
                foreach (var state in upperBodyStates) written |= state.Apply(frame, 0f);
            written |= ApplyEyes(frame, 0f);
            if (written) UpdateStatus(true);
        }
        public bool EvaluateNow() => EvaluateNow(Time.deltaTime);

        public bool EvaluateNow(float deltaTime)
        {
            if (!EnsureTargets()) return false;
            float dt = float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) ? 0 : Mathf.Max(0, deltaTime);
            // 丢追淡出由接收器统一做（lossSmoothingSeconds）；这里直接跟随它的逐帧值，
            // 不要再叠一层 smoothingSeconds，否则实际丢追时长会变成两段平滑相加。
            float blend = parameterReceiver.LossFading
                ? 1f
                : smoothingSeconds > 0 ? 1 - Mathf.Exp(-dt / smoothingSeconds) : 1;
            Quaternion frame = referenceFrame.rotation;
            bool written = Apply(headState, headEnabled, "Rotation_x", "Rotation_y", "Rotation_z", frame, blend, false, headWeight);
            written |= ApplyBody(frame, blend);
            written |= ApplyEyes(frame, blend);
            UpdateStatus(written);
            return written;
        }

        /// <summary>调试视图与 Inspector 共用的姿态快照。</summary>
        public HoVtsLookAtDebug GetDebug()
        {
            var debug = new HoVtsLookAtDebug
            {
                status = status,
                hasFrame = parameterReceiver != null && parameterReceiver.HasFrame,
                tracked = parameterReceiver != null && parameterReceiver.Tracked,
                referencePosition = referenceFrame != null ? referenceFrame.position : transform.position,
                referenceForward = referenceFrame != null ? referenceFrame.forward : transform.forward,
                referenceUp = referenceFrame != null ? referenceFrame.up : transform.up,
                hasHead = head != null,
                hasLeftEye = leftEye != null,
                hasRightEye = rightEye != null,
                headPosition = head != null ? head.position : Vector3.zero,
                leftEyePosition = leftEye != null ? leftEye.position : Vector3.zero,
                rightEyePosition = rightEye != null ? rightEye.position : Vector3.zero,
                headActualRotation = head != null ? head.rotation : Quaternion.identity,
                leftEyeActualRotation = leftEye != null ? leftEye.rotation : Quaternion.identity,
                rightEyeActualRotation = rightEye != null ? rightEye.rotation : Quaternion.identity
            };
            Quaternion frame = referenceFrame != null ? referenceFrame.rotation : Quaternion.identity;
            debug.headTargetRotation = ReadPoseTarget(frame, "Rotation_x", "Rotation_y", "Rotation_z", false, headWeight);
            if (headState != null) debug.headTargetRotation = headState.TargetRotation(frame);
            debug.leftEyeTargetRotation = EyeDebugTarget(frame, leftEye, leftBoneCalibration, leftState, "EyeLeft_x", "EyeLeft_y", "EyeLeft_z");
            debug.rightEyeTargetRotation = EyeDebugTarget(frame, rightEye, rightBoneCalibration, rightState, "EyeRight_x", "EyeRight_y", "EyeRight_z");
            return debug;
        }

        /// <summary>眼球目标朝向：预览优先；运行中的骨骼状态随后覆盖；未标定时保持旧 Gizmo 行为。</summary>
        private Quaternion EyeDebugTarget(Quaternion frame, Transform eye, HoVtsEyeBoneCalibration calibration,
            RotationState state, string x, string y, string z)
        {
            if (previewCalibration)
            {
                if (calibration != null && calibration.captured && eye != null)
                    return LocalToWorld(eye, CalibratedEyeRotation(calibration, previewYaw, previewPitch, 0f));
                // 未标定时仍走旧的 frame 相对路径；运行时用状态里的真实目标，编辑模式退回近似值。
                if (state != null) return state.TargetRotation(frame);
                return frame * PoseRotation(previewYaw, previewPitch, 0f, true) * Quaternion.Inverse(frame);
            }
            if (state != null) return state.TargetRotation(frame);
            if (calibration != null && calibration.captured && eye != null
                && parameterReceiver != null && parameterReceiver.Tracked
                && Read(x, out float vx) && Read(y, out float vy) && Read(z, out float vz))
                return LocalToWorld(eye, CalibratedEyeRotation(calibration, vx, vy, vz));
            return ReadPoseTarget(frame, x, y, z, true);
        }

        private static Quaternion LocalToWorld(Transform target, Quaternion local)
            => (target.parent != null ? target.parent.rotation : Quaternion.identity) * local;

        private Quaternion ReadPoseTarget(Quaternion frame, string x, string y, string z, bool eye, float weight = 1f)
        {
            if (parameterReceiver == null || !Read(x, out float vx) || !Read(y, out float vy) || !Read(z, out float vz))
                return frame;
            Quaternion delta = eye ? PoseRotation(vx, vy, vz, true) : HeadPose(vx, vy, vz);
            float w = Mathf.Clamp01(weight);
            if (w < 1f) delta = Quaternion.Slerp(Quaternion.identity, delta, w);
            return frame * delta * Quaternion.Inverse(frame);
        }

        private bool EnsureTargets()
        {
            bool driveEyeBones = eyeDriver == HoVtsEyeDriver.Bone;
            // 预览时不需要 VTS 帧也能验证眼球；头/上身仍按正常逻辑回中性。
            if (parameterReceiver == null || !parameterReceiver.isActiveAndEnabled
                || (!(previewCalibration && Application.isPlaying) && !parameterReceiver.HasFrame)
                || referenceFrame == null)
            {
                Stop();
                status = referenceFrame == null ? "未指定姿态参考物体" : "等待参数接收器输入";
                return false;
            }
            if (driveEyeBones)
            {
                if (head == null && leftEye == null && rightEye == null && ResolveUpperBodyChain().Length == 0)
                {
                    Stop(); status = "未指定头部或眼球骨骼"; return false;
                }
            }
            else if (head == null && ResolveUpperBodyChain().Length == 0 && !HasEyeKeyMeshes())
            {
                Stop(); status = "未指定头部或形态键网格"; return false;
            }
            if ((driveEyeBones && head != null && (head == leftEye || head == rightEye))
                || (driveEyeBones && leftEye != null && leftEye == rightEye)
                || UpperBodyTargetsOverlap(driveEyeBones))
            {
                Stop(); status = "头部与左右眼必须使用不同骨骼"; return false;
            }
            if (InvalidReference(head) || (driveEyeBones && (InvalidReference(leftEye) || InvalidReference(rightEye))))
            {
                Stop(); status = "姿态参考物体不能位于被驱动骨骼内"; return false;
            }
            bool leftCaptured = leftBoneCalibration != null && leftBoneCalibration.captured;
            bool rightCaptured = rightBoneCalibration != null && rightBoneCalibration.captured;
            if (resolvedReference != referenceFrame || resolvedReceiver != parameterReceiver
                || resolvedEyeDriver != eyeDriver
                || resolvedLeftCalibration != leftCaptured || resolvedRightCalibration != rightCaptured
                || (headState != null ? headState.Target : null) != head
                || (leftState != null ? leftState.Target : null) != (driveEyeBones ? leftEye : null)
                || (rightState != null ? rightState.Target : null) != (driveEyeBones ? rightEye : null))
            {
                Stop();
                resolvedReference = referenceFrame;
                resolvedReceiver = parameterReceiver;
                resolvedEyeDriver = eyeDriver;
                resolvedLeftCalibration = leftCaptured;
                resolvedRightCalibration = rightCaptured;
                Transform[] bodyTargets = ResolveUpperBodyChain();
                upperBodyStates = new RotationState[bodyTargets.Length];
                for (int i = 0; i < bodyTargets.Length; i++) upperBodyStates[i] = new RotationState(bodyTargets[i]);
                headState = head != null ? new RotationState(head) : null;
                // 形态键模式不创建眼球 RotationState，也不读写眼骨；眼球完全由权重驱动。
                leftState = driveEyeBones && leftEye != null ? new RotationState(leftEye) : null;
                rightState = driveEyeBones && rightEye != null ? new RotationState(rightEye) : null;
            }
            if (!driveEyeBones) EnsureEyeKeyBindings();
            return true;
        }

        private Transform[] ResolveUpperBodyChain()
        {
            if (upperBodyChain != null && upperBodyChain.Length > 0)
            {
                var result = new List<Transform>();
                foreach (var target in upperBodyChain)
                    if (target != null && !result.Contains(target)) result.Add(target);
                return result.ToArray();
            }
            return upperBody != null ? new[] { upperBody } : new Transform[0];
        }

        private bool UpperBodyTargetsOverlap(bool includeEyes)
        {
            var targets = ResolveUpperBodyChain();
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == head || (includeEyes && (targets[i] == leftEye || targets[i] == rightEye))) return true;
                for (int j = i + 1; j < targets.Length; j++)
                    if (targets[i] == targets[j]) return true;
            }
            return false;
        }

        private bool InvalidReference(Transform target) => target != null && (referenceFrame == target || referenceFrame.IsChildOf(target));

        private bool Read(string key, out float value) => parameterReceiver.TryGet(key, out value);

        private bool Apply(RotationState state, bool drive, string x, string y, string z,
            Quaternion frame, float blend, bool eye, float weight)
        {
            if (state == null) return false;
            if (!drive) { state.Release(); return false; }
            if (parameterReceiver.Tracked)
            {
                if (Read(x, out float vx) && Read(y, out float vy) && Read(z, out float vz))
                {
                    Quaternion pose = eye ? PoseRotation(vx, vy, vz, true) : HeadPose(vx, vy, vz);
                    float w = Mathf.Clamp01(weight);
                    state.Sample(w >= 1f ? pose : Quaternion.Slerp(Quaternion.identity, pose, w));
                }
            }
            else state.ReturnToNeutral();
            return state.Apply(frame, blend);
        }

        private bool ApplyEyes(Quaternion frame, float blend)
        {
            if (eyeDriver == HoVtsEyeDriver.ShapeKey) return ApplyEyeShapeKeys(blend);
            bool preview = previewCalibration && Application.isPlaying;
            bool written = false;
            written |= ApplyEyeBone(leftState, leftBoneCalibration, "EyeLeft_x", "EyeLeft_y", "EyeLeft_z", frame, blend, preview);
            written |= ApplyEyeBone(rightState, rightBoneCalibration, "EyeRight_x", "EyeRight_y", "EyeRight_z", frame, blend, preview);
            return written;
        }

        /// <summary>
        /// 骨骼模式眼球求值。已标定时按四点插值写局部旋转；未标定时完全沿用原来的
        /// frame 相对偏移路径（PoseRotation + RotationState），保证旧场景行为不变。
        /// </summary>
        private bool ApplyEyeBone(RotationState state, HoVtsEyeBoneCalibration calibration,
            string x, string y, string z, Quaternion frame, float blend, bool preview)
        {
            if (state == null) return false;
            if (!eyesEnabled) { state.Release(); return false; }
            bool calibrated = calibration != null && calibration.captured;
            if (preview)
            {
                // 预览角度与 VTS 同语义：+x 向右、+y 向上；预览不带 roll。
                if (calibrated) state.SampleLocal(CalibratedEyeRotation(calibration, previewYaw, previewPitch, 0f));
                else state.Sample(PoseRotation(previewYaw, previewPitch, 0f, true));
                return state.Apply(frame, blend);
            }
            if (!parameterReceiver.Tracked)
            {
                // 丢追回中性：未标定仍回 identity 偏移；标定后回到记录的静止局部朝向。
                if (calibrated) state.SampleLocal(CalibratedEyeRotation(calibration, 0f, 0f, 0f));
                else state.ReturnToNeutral();
                return state.Apply(frame, blend);
            }
            if (!Read(x, out float vx) || !Read(y, out float vy) || !Read(z, out float vz))
                return state.Apply(frame, blend);
            if (calibrated) state.SampleLocal(CalibratedEyeRotation(calibration, vx, vy, vz));
            else state.Sample(PoseRotation(vx, vy, vz, true));
            return state.Apply(frame, blend);
        }

        /// <summary>
        /// VRC 式四点插值。方向与 PoseRotation 的眼球语义一致：+x 为向右（yn>0 用 right），
        /// +y 为向上（pn>0 用 up）。PoseRotation 对眼球把 y 取反后绕 X 轴转，等价于 +y 抬头，
        /// 所以这里的 pn 不再取反。z（roll）沿用原语义最后叠加。
        /// </summary>
        private Quaternion CalibratedEyeRotation(HoVtsEyeBoneCalibration calibration, float yaw, float pitch, float roll)
        {
            float full = Mathf.Max(1f, boneFullAngle);
            float yn = Mathf.Clamp(yaw / full, -1f, 1f);
            float pn = Mathf.Clamp(pitch / full, -1f, 1f);
            Quaternion rest = calibration.rest;
            Quaternion inverse = Quaternion.Inverse(rest);
            Quaternion result = rest
                * Quaternion.Slerp(Quaternion.identity, inverse * calibration.left, Mathf.Max(0f, -yn))
                * Quaternion.Slerp(Quaternion.identity, inverse * calibration.right, Mathf.Max(0f, yn))
                * Quaternion.Slerp(Quaternion.identity, inverse * calibration.up, Mathf.Max(0f, pn))
                * Quaternion.Slerp(Quaternion.identity, inverse * calibration.down, Mathf.Max(0f, -pn));
            if (roll != 0f) result *= Quaternion.AngleAxis(roll, Vector3.forward);
            return result;
        }

        /// <summary>
        /// 形态键模式求值。每侧四个键的方向权重与骨骼模式同一朝向：left 取 max(0,-yn)、
        /// right 取 max(0,yn)、up 取 max(0,pn)、down 取 max(0,-pn)。
        /// 和骨骼模式共用 smoothingSeconds：先算目标权重，再让当前权重按 blend 追赶。
        /// </summary>
        private bool ApplyEyeShapeKeys(float blend)
        {
            if (!eyesEnabled) { ResetEyeKeyWeights(); return false; }
            if (eyeKeyBindings.Count == 0) return false;

            bool hasTarget;
            if (previewCalibration && Application.isPlaying)
            {
                SetEyeKeyWeight(0, previewYaw, previewPitch);
                SetEyeKeyWeight(1, previewYaw, previewPitch);
                hasTarget = true;
            }
            else if (!parameterReceiver.Tracked)
            {
                // 丢追回中性：目标写 0，但仍按 smoothingSeconds 收回去。
                ClearEyeKeyTarget();
                hasTarget = true;
            }
            else if (!Read("EyeLeft_x", out float leftYaw) || !Read("EyeLeft_y", out float leftPitch)
                || !Read("EyeRight_x", out float rightYaw) || !Read("EyeRight_y", out float rightPitch))
            {
                // 参数不完整时保持上一次的目标和当前值，和骨骼路径一致。
                if (!eyeKeyWeightsValid) return false;
                hasTarget = false;
            }
            else
            {
                SetEyeKeyWeight(0, leftYaw, leftPitch);
                SetEyeKeyWeight(1, rightYaw, rightPitch);
                hasTarget = true;
            }

            if (hasTarget)
            {
                eyeKeyWeightsValid = true;
                if (!eyeKeyWeightsPrimed)
                {
                    Array.Copy(eyeKeyTargetWeights, eyeKeyWeights, eyeKeyWeights.Length);
                    eyeKeyWeightsPrimed = true;
                }
                else
                {
                    float t = Mathf.Clamp01(blend);
                    for (int i = 0; i < eyeKeyWeights.Length; i++)
                        eyeKeyWeights[i] = Mathf.Lerp(eyeKeyWeights[i], eyeKeyTargetWeights[i], t);
                }
            }

            WriteEyeKeyWeights();
            return true;
        }

        private void SetEyeKeyWeight(int side, float yaw, float pitch)
        {
            float full = Mathf.Max(1f, keyFullAngle);
            float yn = Mathf.Clamp(yaw / full, -1f, 1f);
            float pn = Mathf.Clamp(pitch / full, -1f, 1f);
            int index = side * 4;
            eyeKeyTargetWeights[index] = EyeKeyWeight(Mathf.Max(0f, -yn));
            eyeKeyTargetWeights[index + 1] = EyeKeyWeight(Mathf.Max(0f, yn));
            eyeKeyTargetWeights[index + 2] = EyeKeyWeight(Mathf.Max(0f, pn));
            eyeKeyTargetWeights[index + 3] = EyeKeyWeight(Mathf.Max(0f, -pn));
        }

        private void ClearEyeKeyTarget()
        {
            for (int i = 0; i < eyeKeyTargetWeights.Length; i++) eyeKeyTargetWeights[i] = 0f;
        }

        private float EyeKeyWeight(float normalized) => Mathf.Clamp(normalized * keyWeightScale, 0f, 100f);

        private void WriteEyeKeyWeights()
        {
            for (int i = 0; i < eyeKeyBindings.Count; i++)
            {
                var binding = eyeKeyBindings[i];
                binding.Write(eyeKeyWeights[binding.Slot]);
            }
        }

        private void ResetEyeKeyWeights()
        {
            for (int i = 0; i < eyeKeyWeights.Length; i++)
            {
                eyeKeyWeights[i] = 0f;
                eyeKeyTargetWeights[i] = 0f;
            }
            for (int i = 0; i < eyeKeyBindings.Count; i++) eyeKeyBindings[i].Reset();
            eyeKeyWeightsValid = false;
            eyeKeyWeightsPrimed = false;
        }

        /// <summary>只在网格/键名变化时重建绑定；每帧只做引用比较，不调用 GetBlendShapeIndex。</summary>
        private void EnsureEyeKeyBindings()
        {
            if (!EyeKeyConfigurationMatches()) BuildEyeKeyBindings();
        }

        private bool EyeKeyConfigurationMatches()
        {
            if (!eyeKeyBindingsLoaded || boundEyeKeyNames.Count != 8) return false;
            int count = eyeKeyMeshes != null ? eyeKeyMeshes.Count : 0;
            if (boundEyeKeyRenderers.Count != count) return false;
            for (int i = 0; i < count; i++)
            {
                var renderer = eyeKeyMeshes[i];
                if (boundEyeKeyRenderers[i] != renderer) return false;
                if (boundEyeKeyMeshes[i] != (renderer != null ? renderer.sharedMesh : null)) return false;
            }
            for (int slot = 0; slot < 8; slot++)
                if (!string.Equals(boundEyeKeyNames[slot], KeyName(slot), StringComparison.Ordinal)) return false;
            return true;
        }

        private void BuildEyeKeyBindings()
        {
            ReleaseEyeKeyBindings();
            var used = new HashSet<(SkinnedMeshRenderer, int)>();
            var missing = new List<string>();
            if (eyeKeyMeshes != null)
            {
                foreach (var renderer in eyeKeyMeshes)
                {
                    boundEyeKeyRenderers.Add(renderer);
                    boundEyeKeyMeshes.Add(renderer != null ? renderer.sharedMesh : null);
                }
            }
            for (int slot = 0; slot < 8; slot++)
            {
                string key = KeyName(slot);
                boundEyeKeyNames.Add(key);
                if (string.IsNullOrEmpty(key)) continue;
                bool found = false;
                if (eyeKeyMeshes != null)
                {
                    foreach (var renderer in eyeKeyMeshes)
                    {
                        if (renderer == null || renderer.sharedMesh == null) continue;
                        int index = renderer.sharedMesh.GetBlendShapeIndex(key);
                        if (index < 0) continue;
                        if (!used.Add((renderer, index))) { found = true; continue; }
                        eyeKeyBindings.Add(new EyeKeyBinding(renderer, index, slot, key));
                        found = true;
                    }
                }
                if (!found && !missing.Contains(key)) missing.Add(key);
            }
            eyeKeyBindingsLoaded = true;
            if (eyeKeyMeshes == null || eyeKeyMeshes.Count == 0) eyeKeyBindingError = "未指定形态键网格";
            else if (missing.Count > 0) eyeKeyBindingError = "未找到形态键：" + string.Join("、", missing);
            else if (eyeKeyBindings.Count == 0) eyeKeyBindingError = "未配置有效的形态键名";
            else eyeKeyBindingError = null;
        }

        private void ReleaseEyeKeyBindings()
        {
            for (int i = 0; i < eyeKeyBindings.Count; i++) eyeKeyBindings[i].Reset();
            eyeKeyBindings.Clear();
            boundEyeKeyRenderers.Clear();
            boundEyeKeyMeshes.Clear();
            boundEyeKeyNames.Clear();
            for (int i = 0; i < eyeKeyWeights.Length; i++)
            {
                eyeKeyWeights[i] = 0f;
                eyeKeyTargetWeights[i] = 0f;
            }
            eyeKeyBindingsLoaded = false;
            eyeKeyWeightsValid = false;
            eyeKeyWeightsPrimed = false;
            eyeKeyBindingError = null;
        }

        private string KeyName(int slot)
        {
            var targets = slot < 4 ? leftKeyTargets : rightKeyTargets;
            if (targets == null) return null;
            switch (slot & 3)
            {
                case 0: return targets.left;
                case 1: return targets.right;
                case 2: return targets.up;
                default: return targets.down;
            }
        }

        private bool HasEyeKeyMeshes()
        {
            if (eyeKeyMeshes == null) return false;
            for (int i = 0; i < eyeKeyMeshes.Count; i++)
            {
                var renderer = eyeKeyMeshes[i];
                if (renderer != null && renderer.sharedMesh != null) return true;
            }
            return false;
        }

        private void UpdateStatus(bool written)
        {
            if (!written) status = "等待完整姿态参数";
            else if (previewCalibration && Application.isPlaying) status = "预览中";
            else if (parameterReceiver.LossFading) status = "丢追平滑中";
            else status = parameterReceiver.Tracked ? "运行中" : "丢追回中性";
            if (eyeDriver == HoVtsEyeDriver.ShapeKey && !string.IsNullOrEmpty(eyeKeyBindingError))
                status += "；" + eyeKeyBindingError;
        }

        private bool ApplyBody(Quaternion frame, float blend)
        {
            if (upperBodyStates == null || upperBodyStates.Length == 0) return false;
            if (!upperBodyEnabled || upperBodyWeight <= 0f)
            {
                foreach (var state in upperBodyStates) state.Release();
                return false;
            }
            bool written = false;
            if (parameterReceiver.Tracked
                && Read("Rotation_x", out float vx)
                && Read("Rotation_y", out float vy)
                && Read("Rotation_z", out float vz))
            {
                Quaternion pose = BodyPose(vx, vy, vz);
                float perBone = Mathf.Clamp01(upperBodyWeight) / upperBodyStates.Length;
                foreach (var state in upperBodyStates)
                {
                    state.Sample(Quaternion.Slerp(Quaternion.identity, pose, perBone));
                    written |= state.Apply(frame, blend);
                }
            }
            else
            {
                foreach (var state in upperBodyStates)
                {
                    state.ReturnToNeutral();
                    written |= state.Apply(frame, blend);
                }
            }
            return written;
        }

        /// <summary>
        /// Official VTS semantic axes: Rotation_x = FaceAngleX (left/right),
        /// Rotation_y = FaceAngleY (up/down), Rotation_z = FaceAngleZ (lean).
        /// 眼球的 x/y 分量对应水平/垂直，所以只做轴语义转换，不把三轴直接当 Unity Euler 顺序。
        /// 用 AngleAxis 组合避免直接 Quaternion.Euler 的轴顺序把某一方向压掉。
        /// </summary>
        private static Quaternion PoseRotation(float x, float y, float z, bool eye)
        {
            // Head pose uses official semantic axes. Eye pose keeps the existing
            // Eye*_x/y convention; CalibratedEyeRotation follows the same +x right /
            // +y up semantics so both eye paths agree on direction.
            float yaw = eye ? x : x;
            float pitch = eye ? -y : y;
            float roll = z;
            return Quaternion.AngleAxis(yaw, Vector3.up)
                * Quaternion.AngleAxis(pitch, Vector3.right)
                * Quaternion.AngleAxis(roll, Vector3.forward);
        }

        /// <summary>头部源姿态：按 headMaxAngle 截断总转角。</summary>
        private Quaternion HeadPose(float x, float y, float z) => LimitedPose(x, y, z, headMaxAngle);

        /// <summary>上身源姿态：按 upperBodyMaxAngle 截断总转角。</summary>
        private Quaternion BodyPose(float x, float y, float z) => LimitedPose(x, y, z, upperBodyMaxAngle);

        private static Quaternion LimitedPose(float x, float y, float z, float maxAngle)
        {
            Quaternion pose = PoseRotation(x, y, z, false);
            float limit = Mathf.Clamp(maxAngle, 0f, 180f);
            return limit >= 180f ? pose : Quaternion.RotateTowards(Quaternion.identity, pose, limit);
        }

        public void Stop()
        {
            if (upperBodyStates != null) foreach (var state in upperBodyStates) state?.Release();
            upperBodyStates = new RotationState[0];
            headState?.Release(); leftState?.Release(); rightState?.Release();
            headState = null; leftState = null; rightState = null;
            ReleaseEyeKeyBindings();
            resolvedReference = null; resolvedReceiver = null;
            resolvedEyeDriver = HoVtsEyeDriver.Bone;
            resolvedLeftCalibration = resolvedRightCalibration = false;
            lastPreCullFrame = -1;
            status = "已停止";
        }
        private void OnDisable()
        {
            Camera.onPreCull -= OnCameraPreCull;
            Stop();
        }

        private void OnDestroy()
        {
            Camera.onPreCull -= OnCameraPreCull;
            Stop();
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;
            HoVtsLookAtDebug debug = GetDebug();
            float length = Mathf.Max(0.01f, gizmoLength);
            DrawBasis(debug.referencePosition, Quaternion.LookRotation(debug.referenceForward, debug.referenceUp), length * 1.25f);
            DrawPoseGizmo(debug.headPosition, debug.headActualRotation, debug.headTargetRotation, debug.hasHead, length);
            DrawPoseGizmo(debug.leftEyePosition, debug.leftEyeActualRotation, debug.leftEyeTargetRotation, debug.hasLeftEye, length * 0.55f);
            DrawPoseGizmo(debug.rightEyePosition, debug.rightEyeActualRotation, debug.rightEyeTargetRotation, debug.hasRightEye, length * 0.55f);
        }

        private static void DrawPoseGizmo(Vector3 origin, Quaternion actual, Quaternion target, bool enabled, float length)
        {
            if (!enabled) return;
            Gizmos.color = new Color(1f, .75f, .2f, .9f);
            Gizmos.DrawLine(origin, origin + actual * Vector3.forward * length);
            Gizmos.color = new Color(.75f, .35f, 1f, .9f);
            Gizmos.DrawLine(origin, origin + target * Vector3.forward * length);
            Gizmos.color = new Color(.2f, .85f, 1f, .8f);
            Gizmos.DrawLine(origin, origin + actual * Vector3.up * (length * .45f));
        }

        private static void DrawBasis(Vector3 origin, Quaternion rotation, float length)
        {
            Gizmos.color = Color.red; Gizmos.DrawLine(origin, origin + rotation * Vector3.right * length);
            Gizmos.color = Color.green; Gizmos.DrawLine(origin, origin + rotation * Vector3.up * length);
            Gizmos.color = Color.blue; Gizmos.DrawLine(origin, origin + rotation * Vector3.forward * length);
        }
#endif

        /// <summary>一个 (renderer, shape index) 绑定。释放时只回写本组件写过的键。</summary>
        private sealed class EyeKeyBinding
        {
            public readonly SkinnedMeshRenderer Renderer;
            public readonly int Index, Slot;
            public readonly string Key;
            private readonly Mesh mesh;
            private bool written;

            public EyeKeyBinding(SkinnedMeshRenderer renderer, int index, int slot, string key)
            {
                Renderer = renderer; Index = index; Slot = slot; Key = key;
                mesh = renderer != null ? renderer.sharedMesh : null;
            }

            public void Write(float weight)
            {
                if (Renderer == null || Renderer.sharedMesh != mesh) return;
                Renderer.SetBlendShapeWeight(Index, weight);
                written = true;
            }

            public void Reset()
            {
                if (!written) return;
                if (Renderer != null && Renderer.sharedMesh == mesh) Renderer.SetBlendShapeWeight(Index, 0f);
                written = false;
            }
        }

        private sealed class RotationState
        {
            public readonly Transform Target;
            private Quaternion rest, applied, desired = Quaternion.identity, offset = Quaternion.identity;
            private bool sampled, written, local;

            public RotationState(Transform target) { Target = target; }
            public void Sample(Quaternion rotation)
            {
                local = false;
                desired = rotation;
                if (!sampled) offset = desired;
                sampled = true;
            }
            /// <summary>标定后的眼骨路径：desired 直接就是局部朝向，不再做 frame 相对偏移。</summary>
            public void SampleLocal(Quaternion rotation)
            {
                local = true;
                desired = rotation;
                if (!sampled) offset = desired;
                sampled = true;
            }
            public void ReturnToNeutral() { local = false; desired = Quaternion.identity; }
            public Quaternion TargetRotation(Quaternion frame)
            {
                if (!sampled || Target == null) return Target != null ? Target.rotation : Quaternion.identity;
                Quaternion parent = Target.parent != null ? Target.parent.rotation : Quaternion.identity;
                if (local) return parent * offset;
                return frame * offset * Quaternion.Inverse(frame) * parent * rest;
            }
            public bool Apply(Quaternion frame, float blend)
            {
                if (!sampled || Target == null) return false;
                if (local)
                {
                    if (!written) rest = Target.localRotation;
                    offset = Quaternion.Slerp(offset, desired, blend);
                    Target.localRotation = offset;
                    applied = Target.localRotation;
                    written = true;
                    return true;
                }
                // If an upstream animation changed the local pose, use that as this frame's
                // baseline. Otherwise keep the saved baseline, so the offset never accumulates.
                if (!written || Target.localRotation != applied) rest = Target.localRotation;
                offset = Quaternion.Slerp(offset, desired, blend);
                Quaternion parent = Target.parent != null ? Target.parent.rotation : Quaternion.identity;
                Target.rotation = frame * offset * Quaternion.Inverse(frame) * parent * rest;
                applied = Target.localRotation;
                written = true;
                return true;
            }
            public void Release()
            {
                // Do not undo a newer external writer when our component releases ownership.
                if (written && Target != null && Target.localRotation == applied) Target.localRotation = rest;
                desired = offset = Quaternion.identity;
                sampled = written = local = false;
            }
        }
    }
}
