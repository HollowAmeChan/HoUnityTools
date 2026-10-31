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
            Vector3 center = new Vector3(-value.targetPosition.x, value.targetPosition.y, -value.targetPosition.z) * mmdToUnityScale;
            Vector3 euler = new Vector3(-value.rotation.x * Mathf.Rad2Deg, value.rotation.y * Mathf.Rad2Deg + 180.0f, value.rotation.z * Mathf.Rad2Deg);
            Quaternion rotation = Quaternion.Euler(euler);
            if (cameraTarget != null)
            {
                cameraTarget.position = center;
                cameraTarget.rotation = rotation;
            }

            Camera camera = TargetCamera;
            camera.transform.rotation = rotation;
            // MMD's signed distance is meaningful: negative values pull the camera through the orbit center.
            camera.transform.position = center - rotation * Vector3.forward * (value.distance * mmdToUnityScale);
            camera.fieldOfView = Mathf.Clamp(value.viewAngle, 1.0f, 179.0f);
            camera.orthographic = value.perspectiveOff;
            if (value.perspectiveOff)
                camera.orthographicSize = Mathf.Max(0.0001f,
                    Mathf.Abs(value.distance * mmdToUnityScale) * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f) * orthographicSizeMultiplier);
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
            value.targetPosition = Vector3.Lerp(value.targetPosition, b.targetPosition, Bezier(b.interpolation.movement, u));
            value.distance = Mathf.Lerp(value.distance, b.distance, Bezier(b.interpolation.distance, u));
            value.rotation = Vector3.Lerp(value.rotation, b.rotation, Bezier(b.interpolation.rotation, u));
            value.viewAngle = (uint)Mathf.Round(Mathf.Lerp(value.viewAngle, b.viewAngle, Bezier(b.interpolation.viewAngle, u)));
            return value;
        }

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
