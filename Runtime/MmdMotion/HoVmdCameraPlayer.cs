using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.MmdMotion
{
    /// <summary>Plays a VMD camera track directly on the Camera attached to this object.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(32060)]
    [AddComponentMenu("HoUnityTools/MMD/VMD Camera Player")]
    public sealed class HoVmdCameraPlayer : MonoBehaviour
    {
        [SerializeField] public new HoVmdAnimation animation;
        [SerializeField] public Camera targetCamera;
        [SerializeField] public Transform cameraTarget;
        [SerializeField, Min(1.0f)] public float frameRate = 30.0f;
        [SerializeField] public bool playOnEnable = true;
        [SerializeField] public bool loop = true;
        [SerializeField, Min(0.0f)] public float currentFrame;
        [SerializeField, Min(0.0001f)] public float mmdToUnityScale = 0.08f;
        [SerializeField] public bool snapAdjacentFrames = true;
        [SerializeField, Min(0.0001f)] public float orthographicSizeMultiplier = 1.0f;
        [SerializeField] public Transform motionOrigin;
        [SerializeField] public Vector3 centerOffset;
        [SerializeField, Min(.01f)] public float distanceMultiplier = 1;
        [SerializeField] public bool portraitFraming;
        [SerializeField, Range(0, 100)]
        [Tooltip("0 保留作者视角。提高下限会在保持轨道中心景别的同时缩短透视镜头距离，方便室内运镜避开远处墙体。不会进行碰撞检测。")]
        public float minimumPerspectiveFov;
        [SerializeField, Min(.1f)] public float minimumVerticalSpan = .95f;
        [SerializeField, Min(.1f)] public float maximumVerticalSpan = 2.05f;
        [SerializeField, Tooltip("可选：沿画面横轴/纵轴跟随此目标，保留作者的旋转、景别和切换。留空保留原运镜。可用于固定机位改竖屏后角色横移或弯腰出画的情况。")]
        public Transform framingFollowTarget;
        [SerializeField, Tooltip("X 为横向、Y 为纵向；各轴 0 保留作者构图，1 完全跟随。")]
        public Vector2 framingFollowWeight = Vector2.right;
        [SerializeField, Tooltip("沿相机右/上方向的跟随中心偏移，单位为世界米；仅对启用跟随的轴有效。")]
        public Vector2 framingFollowOffset;

        private bool playing;
        private readonly List<HoVmdCameraFrame> cameraTrack = new List<HoVmdCameraFrame>();
        private bool trackDirty = true;

        public HoVmdAnimation Animation { get => animation; set { animation = value; trackDirty = true; } }
        public Camera TargetCamera => targetCamera != null ? targetCamera : GetComponent<Camera>();
        public Transform CameraTarget { get => cameraTarget; set => cameraTarget = value; }
        public float CurrentFrame { get => currentFrame; set => currentFrame = Mathf.Max(0.0f, value); }
        public float FrameRate { get => frameRate; set => frameRate = Mathf.Max(1.0f, value); }
        public bool Playing { get => playing; set => playing = value; }

        private void Reset() => targetCamera = GetComponent<Camera>();

        private void OnEnable()
        {
            targetCamera = GetComponent<Camera>();
            playing = playOnEnable;
            trackDirty = true;
        }

        private void OnValidate()
        {
            frameRate = Mathf.Max(1.0f, frameRate);
            currentFrame = Mathf.Max(0.0f, currentFrame);
            mmdToUnityScale = Mathf.Max(0.0001f, mmdToUnityScale);
            orthographicSizeMultiplier = Mathf.Max(0.0001f, orthographicSizeMultiplier);
            trackDirty = true;
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying || !playing || animation == null)
                return;
            targetCamera = GetComponent<Camera>();
            if (targetCamera == null)
                return;
            if (trackDirty) RebuildTrack();
            if (cameraTrack.Count == 0) return;

            ApplyAtFrame(currentFrame);
            currentFrame += Time.deltaTime * frameRate;
            if (currentFrame > LastFrame)
            {
                if (loop) currentFrame %= Mathf.Max(1.0f, LastFrame);
                else { currentFrame = LastFrame; playing = false; }
            }
        }

        public uint LastFrame => cameraTrack.Count > 0
            ? cameraTrack[cameraTrack.Count - 1].frame
            : animation != null ? animation.LastCameraFrame : 0;

        public void RebuildTrack()
        {
            cameraTrack.Clear();
            if (animation != null && animation.cameraFrames != null)
            {
                cameraTrack.AddRange(animation.cameraFrames);
                cameraTrack.Sort((a, b) => a.frame.CompareTo(b.frame));
            }
            trackDirty = false;
        }

        public void ApplyAtFrame(float frame)
        {
            if (animation == null || TargetCamera == null)
                return;
            if (trackDirty) RebuildTrack();
            if (cameraTrack.Count == 0) return;

            HoVmdCameraFrame value = Sample(frame);
            Vector3 center = new Vector3(-value.targetPosition.x, value.targetPosition.y, -value.targetPosition.z) * mmdToUnityScale + centerOffset;
            float distance = value.distance * mmdToUnityScale * distanceMultiplier;
            float authoredFov = Mathf.Clamp(value.viewAngle, 1, 179);
            float outputFov = value.perspectiveOff ? authoredFov : Mathf.Max(authoredFov, minimumPerspectiveFov);
            float authoredTangent = Mathf.Tan(authoredFov * Mathf.Deg2Rad * .5f);
            if (portraitFraming)
            {
                float span = Mathf.Clamp(2 * Mathf.Abs(distance) * authoredTangent, minimumVerticalSpan,
                    Mathf.Max(minimumVerticalSpan, maximumVerticalSpan));
                distance = Mathf.Sign(distance) * span / (2 * Mathf.Tan(outputFov * Mathf.Deg2Rad * .5f));
                if (span >= 1.6f) center.y = Mathf.Min(center.y, span * .5f + .01f);
            }
            else if (outputFov > authoredFov)
            {
                // Dolly towards the unchanged orbit center while retaining its
                // projected vertical span. Authored perspective is unchanged at 0.
                distance *= authoredTangent / Mathf.Tan(outputFov * Mathf.Deg2Rad * .5f);
            }
            Vector3 euler = new Vector3(-value.rotation.x * Mathf.Rad2Deg, 180.0f - value.rotation.y * Mathf.Rad2Deg, -value.rotation.z * Mathf.Rad2Deg);
            Quaternion rotation = Quaternion.Euler(euler);
            if (motionOrigin != null)
            {
                center = motionOrigin.TransformPoint(center);
                rotation = motionOrigin.rotation * rotation;
                distance *= Mathf.Abs(motionOrigin.lossyScale.y);
            }
            if (framingFollowTarget != null)
            {
                // Evaluate from the authored center every time. No tracking
                // history means seeks and offline frame sampling agree.
                Vector3 right = rotation * Vector3.right;
                Vector3 up = rotation * Vector3.up;
                Vector3 delta = framingFollowTarget.position - center;
                center += right * ((Vector3.Dot(delta, right) + framingFollowOffset.x) *
                    Mathf.Clamp01(framingFollowWeight.x));
                center += up * ((Vector3.Dot(delta, up) + framingFollowOffset.y) *
                    Mathf.Clamp01(framingFollowWeight.y));
            }
            if (cameraTarget != null)
            {
                cameraTarget.position = center;
                cameraTarget.rotation = rotation;
            }

            Camera camera = TargetCamera;
            camera.transform.rotation = rotation;
            // A normal negative VMD distance places the camera behind its forward
            // direction so it looks towards the orbit center, not away from it.
            camera.transform.position = center + rotation * Vector3.forward * distance;
            camera.fieldOfView = outputFov;
            camera.orthographic = value.perspectiveOff;
            if (value.perspectiveOff)
                camera.orthographicSize = Mathf.Max(0.0001f,
                    Mathf.Abs(distance) * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f) * orthographicSizeMultiplier);
        }

        private HoVmdCameraFrame Sample(float frame)
        {
            if (frame <= cameraTrack[0].frame) return cameraTrack[0];
            int next = 1;
            while (next < cameraTrack.Count && cameraTrack[next].frame < frame) next++;
            HoVmdCameraFrame value = cameraTrack[Mathf.Clamp(next - 1, 0, cameraTrack.Count - 1)];
            if (next >= cameraTrack.Count) return value;
            HoVmdCameraFrame b = cameraTrack[next];
            if (frame >= b.frame) return b;
            uint gap = b.frame - value.frame;
            // Adjacent camera keys are commonly authored as hard cuts.
            if (snapAdjacentFrames && gap <= 1) return value;
            float u = (frame - value.frame) / Mathf.Max(1, gap);
            value.targetPosition = b.interpolation.separatePosition
                ? new Vector3(Mathf.Lerp(value.targetPosition.x, b.targetPosition.x, Bezier(b.interpolation.positionX, u)),
                    Mathf.Lerp(value.targetPosition.y, b.targetPosition.y, Bezier(b.interpolation.positionY, u)),
                    Mathf.Lerp(value.targetPosition.z, b.targetPosition.z, Bezier(b.interpolation.positionZ, u)))
                : Vector3.Lerp(value.targetPosition, b.targetPosition, Bezier(b.interpolation.movement, u));
            value.distance = Mathf.Lerp(value.distance, b.distance, Bezier(b.interpolation.distance, u));
            float rotationTime = Bezier(b.interpolation.rotation, u);
            value.rotation = new Vector3(LerpRadians(value.rotation.x, b.rotation.x, rotationTime),
                LerpRadians(value.rotation.y, b.rotation.y, rotationTime), LerpRadians(value.rotation.z, b.rotation.z, rotationTime));
            value.viewAngle = Mathf.Lerp(value.viewAngle, b.viewAngle, Bezier(b.interpolation.viewAngle, u));
            return value;
        }

        private static float LerpRadians(float a, float b, float t) =>
            (a * Mathf.Rad2Deg + Mathf.DeltaAngle(a * Mathf.Rad2Deg, b * Mathf.Rad2Deg) * t) * Mathf.Deg2Rad;

        private static float Bezier(HoVmdBezier curve, float x)
        {
            float t = x;
            for (int i = 0; i < 6; i++)
            {
                float omt = 1.0f - t;
                float f = 3.0f * omt * omt * t * curve.x1 + 3.0f * omt * t * t * curve.x2 + t * t * t - x;
                float d = 3.0f * omt * omt * curve.x1 + 6.0f * omt * t * (curve.x2 - curve.x1) + 3.0f * t * t * (1.0f - curve.x2);
                if (Mathf.Abs(d) < 1e-5f) break;
                t = Mathf.Clamp01(t - f / d);
            }
            float remaining = 1.0f - t;
            return 3.0f * remaining * remaining * t * curve.y1 +
                3.0f * remaining * t * t * curve.y2 + t * t * t;
        }
    }
}
