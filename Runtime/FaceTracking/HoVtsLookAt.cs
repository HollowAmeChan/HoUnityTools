using System;
using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
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
    [AddComponentMenu("")]
    [DefaultExecutionOrder(32000)]
    public sealed class HoVtsLookAt : MonoBehaviour
    {
        [SerializeField] private HoVtsParameterReceiver parameterReceiver;
        [SerializeField] private Transform referenceFrame;
        [SerializeField] private Transform head;
        [SerializeField] private Transform leftEye;
        [SerializeField] private Transform rightEye;
        [SerializeField] private HoVtsPoseParameterSource parameterSource = HoVtsPoseParameterSource.CalibratedRaw;
        [SerializeField] private bool headEnabled = true;
        [SerializeField] private bool eyesEnabled = true;
        [SerializeField] private bool autoRun = true;
        [SerializeField, Min(0)] private float smoothingSeconds;

        private RotationState headState, leftState, rightState;
        private Transform resolvedReference;
        private HoVtsParameterReceiver resolvedReceiver;
        private HoVtsPoseParameterSource resolvedSource;
        private string status = "未初始化";

        public HoVtsParameterReceiver ParameterReceiver { get => parameterReceiver; set { if (parameterReceiver != value) { Stop(); parameterReceiver = value; } } }
        public Transform ReferenceFrame { get => referenceFrame; set { if (referenceFrame != value) { Stop(); referenceFrame = value; } } }
        public Transform Head { get => head; set { if (head != value) { Stop(); head = value; } } }
        public Transform LeftEye { get => leftEye; set { if (leftEye != value) { Stop(); leftEye = value; } } }
        public Transform RightEye { get => rightEye; set { if (rightEye != value) { Stop(); rightEye = value; } } }
        public HoVtsPoseParameterSource ParameterSource { get => parameterSource; set { if (parameterSource != value) { Stop(); parameterSource = value; } } }
        public bool HeadEnabled { get => headEnabled; set => headEnabled = value; }
        public bool EyesEnabled { get => eyesEnabled; set => eyesEnabled = value; }
        public bool AutoRun { get => autoRun; set => autoRun = value; }
        public float SmoothingSeconds { get => smoothingSeconds; set => smoothingSeconds = Mathf.Max(0, value); }
        public string Status => status;

        private void LateUpdate() { if (autoRun) EvaluateNow(Time.deltaTime); }
        public bool EvaluateNow() => EvaluateNow(Time.deltaTime);

        public bool EvaluateNow(float deltaTime)
        {
            if (!EnsureTargets()) return false;
            float dt = float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) ? 0 : Mathf.Max(0, deltaTime);
            float blend = smoothingSeconds > 0 ? 1 - Mathf.Exp(-dt / smoothingSeconds) : 1;
            Quaternion frame = referenceFrame.rotation;
            bool written = Apply(headState, headEnabled, "Rotation_x", "Rotation_y", "Rotation_z", frame, blend);
            written |= Apply(leftState, eyesEnabled, "EyeLeft_x", "EyeLeft_y", "EyeLeft_z", frame, blend);
            written |= Apply(rightState, eyesEnabled, "EyeRight_x", "EyeRight_y", "EyeRight_z", frame, blend);
            status = written ? parameterReceiver.Tracked ? "运行中" : "丢追回中性" : "等待完整姿态参数";
            return written;
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
            if ((head != null && (head == leftEye || head == rightEye)) || (leftEye != null && leftEye == rightEye))
            {
                Stop(); status = "头部与左右眼必须使用不同骨骼"; return false;
            }
            if (InvalidReference(head) || InvalidReference(leftEye) || InvalidReference(rightEye))
            {
                Stop(); status = "姿态参考物体不能位于被驱动骨骼内"; return false;
            }
            if (resolvedReference != referenceFrame || resolvedReceiver != parameterReceiver || resolvedSource != parameterSource
                || (headState != null ? headState.Target : null) != head
                || (leftState != null ? leftState.Target : null) != leftEye
                || (rightState != null ? rightState.Target : null) != rightEye)
            {
                Stop();
                resolvedReference = referenceFrame;
                resolvedReceiver = parameterReceiver;
                resolvedSource = parameterSource;
                headState = head != null ? new RotationState(head) : null;
                leftState = leftEye != null ? new RotationState(leftEye) : null;
                rightState = rightEye != null ? new RotationState(rightEye) : null;
            }
            return true;
        }

        private bool InvalidReference(Transform target) => target != null && (referenceFrame == target || referenceFrame.IsChildOf(target));

        private bool Read(string key, out float value) => parameterSource == HoVtsPoseParameterSource.CalibratedRaw
            ? parameterReceiver.TryGetRaw(key, out value) : parameterReceiver.TryGet(key, out value);

        private bool Apply(RotationState state, bool drive, string x, string y, string z, Quaternion frame, float blend)
        {
            if (state == null) return false;
            if (!drive) { state.Release(); return false; }
            if (parameterReceiver.Tracked)
            {
                if (Read(x, out float vx) && Read(y, out float vy) && Read(z, out float vz))
                    state.Sample(Quaternion.Euler(vx, vy, vz));
            }
            else state.ReturnToNeutral();
            return state.Apply(frame, blend);
        }

        public void Stop()
        {
            headState?.Release(); leftState?.Release(); rightState?.Release();
            headState = null; leftState = null; rightState = null;
            resolvedReference = null; resolvedReceiver = null;
            status = "已停止";
        }
        private void OnDisable() { Stop(); }
        private void OnDestroy() { Stop(); }

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
