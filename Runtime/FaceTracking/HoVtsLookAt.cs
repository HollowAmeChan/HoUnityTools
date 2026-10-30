using System;
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

    public enum HoVtsPoseParameterSource
    {
        CalibratedRaw,
        Processed
    }

    /// <summary>
    /// VTS head/eye rotation constraint. Uses the reference frame's axes rather than assuming
    /// the model's bone axes. No Animator, Avatar, IK pass, or face controller is required.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HoUnityTools/面捕/VTS 头眼姿态")]
    [DefaultExecutionOrder(32000)]
    public sealed class HoVtsLookAt : MonoBehaviour
    {
        [SerializeField] private HoVtsParameterReceiver parameterReceiver;
        [SerializeField] private Transform referenceFrame;
        [SerializeField] private Transform upperBody;
        [SerializeField] private Transform head;
        [SerializeField] private Transform leftEye;
        [SerializeField] private Transform rightEye;
        [SerializeField] private HoVtsPoseParameterSource parameterSource = HoVtsPoseParameterSource.CalibratedRaw;
        [SerializeField] private bool headEnabled = true;
        [SerializeField] private bool upperBodyEnabled;
        [SerializeField, Range(0f, 1f)] private float upperBodyWeight = .25f;
        [SerializeField] private bool eyesEnabled = true;
        [SerializeField] private bool autoRun = true;
        [SerializeField, Min(0)] private float smoothingSeconds;
        [SerializeField] private bool drawGizmos = true;
        [SerializeField, Min(0.01f)] private float gizmoLength = 0.12f;

        private RotationState upperBodyState, headState, leftState, rightState;
        private Transform resolvedReference;
        private HoVtsParameterReceiver resolvedReceiver;
        private HoVtsPoseParameterSource resolvedSource;
        private string status = "未初始化";

        public HoVtsParameterReceiver ParameterReceiver { get => parameterReceiver; set { if (parameterReceiver != value) { Stop(); parameterReceiver = value; } } }
        public Transform ReferenceFrame { get => referenceFrame; set { if (referenceFrame != value) { Stop(); referenceFrame = value; } } }
        public Transform UpperBody { get => upperBody; set { if (upperBody != value) { Stop(); upperBody = value; } } }
        public Transform Head { get => head; set { if (head != value) { Stop(); head = value; } } }
        public Transform LeftEye { get => leftEye; set { if (leftEye != value) { Stop(); leftEye = value; } } }
        public Transform RightEye { get => rightEye; set { if (rightEye != value) { Stop(); rightEye = value; } } }
        public HoVtsPoseParameterSource ParameterSource { get => parameterSource; set { if (parameterSource != value) { Stop(); parameterSource = value; } } }
        public bool HeadEnabled { get => headEnabled; set => headEnabled = value; }
        public bool UpperBodyEnabled { get => upperBodyEnabled; set => upperBodyEnabled = value; }
        public float UpperBodyWeight { get => upperBodyWeight; set => upperBodyWeight = Mathf.Clamp01(value); }
        public bool EyesEnabled { get => eyesEnabled; set => eyesEnabled = value; }
        public bool AutoRun { get => autoRun; set => autoRun = value; }
        public float SmoothingSeconds { get => smoothingSeconds; set => smoothingSeconds = Mathf.Max(0, value); }
        public bool DrawGizmos { get => drawGizmos; set => drawGizmos = value; }
        public float GizmoLength { get => gizmoLength; set => gizmoLength = Mathf.Max(0.01f, value); }
        public string Status => status;

        private void LateUpdate() { if (autoRun) EvaluateNow(Time.deltaTime); }
        public bool EvaluateNow() => EvaluateNow(Time.deltaTime);

        public bool EvaluateNow(float deltaTime)
        {
            if (!EnsureTargets()) return false;
            float dt = float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) ? 0 : Mathf.Max(0, deltaTime);
            float blend = smoothingSeconds > 0 ? 1 - Mathf.Exp(-dt / smoothingSeconds) : 1;
            Quaternion frame = referenceFrame.rotation;
            bool written = Apply(headState, headEnabled, "Rotation_x", "Rotation_y", "Rotation_z", frame, blend, false);
            written |= ApplyBody(frame, blend);
            written |= Apply(leftState, eyesEnabled, "EyeLeft_x", "EyeLeft_y", "EyeLeft_z", frame, blend, true);
            written |= Apply(rightState, eyesEnabled, "EyeRight_x", "EyeRight_y", "EyeRight_z", frame, blend, true);
            status = written ? parameterReceiver.Tracked ? "运行中" : "丢追回中性" : "等待完整姿态参数";
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
            debug.headTargetRotation = ReadPoseTarget(frame, "Rotation_x", "Rotation_y", "Rotation_z", false);
            debug.leftEyeTargetRotation = ReadPoseTarget(frame, "EyeLeft_x", "EyeLeft_y", "EyeLeft_z", true);
            debug.rightEyeTargetRotation = ReadPoseTarget(frame, "EyeRight_x", "EyeRight_y", "EyeRight_z", true);
            if (headState != null) debug.headTargetRotation = headState.TargetRotation(frame);
            if (leftState != null) debug.leftEyeTargetRotation = leftState.TargetRotation(frame);
            if (rightState != null) debug.rightEyeTargetRotation = rightState.TargetRotation(frame);
            return debug;
        }

        private Quaternion ReadPoseTarget(Quaternion frame, string x, string y, string z, bool eye)
        {
            if (parameterReceiver == null || !Read(x, out float vx) || !Read(y, out float vy) || !Read(z, out float vz))
                return frame;
            Quaternion delta = PoseRotation(vx, vy, vz, eye);
            return frame * delta * Quaternion.Inverse(frame);
        }

        private bool EnsureTargets()
        {
            if (parameterReceiver == null || !parameterReceiver.isActiveAndEnabled || !parameterReceiver.HasFrame || referenceFrame == null)
            {
                Stop();
                status = referenceFrame == null ? "未指定姿态参考物体" : "等待参数接收器输入";
                return false;
            }
            if (head == null && leftEye == null && rightEye == null)
            {
                Stop(); status = "未指定头部或眼球骨骼"; return false;
            }
            if ((head != null && (head == leftEye || head == rightEye))
                || (leftEye != null && leftEye == rightEye)
                || (upperBody != null && (upperBody == head || upperBody == leftEye || upperBody == rightEye)))
            {
                Stop(); status = "头部与左右眼必须使用不同骨骼"; return false;
            }
            if (InvalidReference(head) || InvalidReference(leftEye) || InvalidReference(rightEye))
            {
                Stop(); status = "姿态参考物体不能位于被驱动骨骼内"; return false;
            }
            if (resolvedReference != referenceFrame || resolvedReceiver != parameterReceiver || resolvedSource != parameterSource
                || (upperBodyState != null ? upperBodyState.Target : null) != upperBody
                || (headState != null ? headState.Target : null) != head
                || (leftState != null ? leftState.Target : null) != leftEye
                || (rightState != null ? rightState.Target : null) != rightEye)
            {
                Stop();
                resolvedReference = referenceFrame;
                resolvedReceiver = parameterReceiver;
                resolvedSource = parameterSource;
                upperBodyState = upperBody != null ? new RotationState(upperBody) : null;
                headState = head != null ? new RotationState(head) : null;
                leftState = leftEye != null ? new RotationState(leftEye) : null;
                rightState = rightEye != null ? new RotationState(rightEye) : null;
            }
            return true;
        }

        private bool InvalidReference(Transform target) => target != null && (referenceFrame == target || referenceFrame.IsChildOf(target));

        private bool Read(string key, out float value) => parameterSource == HoVtsPoseParameterSource.CalibratedRaw
            ? parameterReceiver.TryGetRaw(key, out value) : parameterReceiver.TryGet(key, out value);

        private bool Apply(RotationState state, bool drive, string x, string y, string z,
            Quaternion frame, float blend, bool eye)
        {
            if (state == null) return false;
            if (!drive) { state.Release(); return false; }
            if (parameterReceiver.Tracked)
            {
                if (Read(x, out float vx) && Read(y, out float vy) && Read(z, out float vz))
                    state.Sample(PoseRotation(vx, vy, vz, eye));
            }
            else state.ReturnToNeutral();
            return state.Apply(frame, blend);
        }

        private bool ApplyBody(Quaternion frame, float blend)
        {
            if (upperBodyState == null) return false;
            if (!upperBodyEnabled || upperBodyWeight <= 0f)
            {
                upperBodyState.Release();
                return false;
            }
            if (parameterReceiver.Tracked
                && Read("Rotation_x", out float vx)
                && Read("Rotation_y", out float vy)
                && Read("Rotation_z", out float vz))
            {
                Quaternion pose = PoseRotation(vx, vy, vz, false);
                upperBodyState.Sample(Quaternion.Slerp(Quaternion.identity, pose, Mathf.Clamp01(upperBodyWeight)));
            }
            else upperBodyState.ReturnToNeutral();
            return upperBodyState.Apply(frame, blend);
        }

        /// <summary>
        /// VTS 的姿态分量不是 Unity Transform 的 XYZ 顺序：
        /// Rotation_x/y/z 对应抬低头、左右转、歪头；Face 规范中 X/Y 为负号。
        /// 眼球的 x/y 分量对应左右/上下，所以要换到 Unity 的 yaw/pitch 轴。
        /// 用 AngleAxis 组合避免直接 Quaternion.Euler 的轴顺序把某一方向压掉。
        /// </summary>
        private static Quaternion PoseRotation(float x, float y, float z, bool eye)
        {
            float yaw = eye ? x : -y;
            float pitch = eye ? -y : -x;
            float roll = z;
            return Quaternion.AngleAxis(yaw, Vector3.up)
                * Quaternion.AngleAxis(pitch, Vector3.right)
                * Quaternion.AngleAxis(roll, Vector3.forward);
        }

        public void Stop()
        {
            upperBodyState?.Release(); headState?.Release(); leftState?.Release(); rightState?.Release();
            upperBodyState = null; headState = null; leftState = null; rightState = null;
            resolvedReference = null; resolvedReceiver = null;
            status = "已停止";
        }
        private void OnDisable() { Stop(); }
        private void OnDestroy() { Stop(); }

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

        private sealed class RotationState
        {
            public readonly Transform Target;
            private Quaternion rest, applied, desired = Quaternion.identity, offset = Quaternion.identity;
            private bool sampled, written;

            public RotationState(Transform target) { Target = target; }
            public void Sample(Quaternion rotation)
            {
                desired = rotation;
                if (!sampled) offset = desired;
                sampled = true;
            }
            public void ReturnToNeutral() { desired = Quaternion.identity; }
            public Quaternion TargetRotation(Quaternion frame)
            {
                if (!sampled || Target == null) return Target != null ? Target.rotation : Quaternion.identity;
                Quaternion parent = Target.parent != null ? Target.parent.rotation : Quaternion.identity;
                return frame * offset * Quaternion.Inverse(frame) * parent * rest;
            }
            public bool Apply(Quaternion frame, float blend)
            {
                if (!sampled || Target == null) return false;
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
                sampled = written = false;
            }
        }
    }
}
