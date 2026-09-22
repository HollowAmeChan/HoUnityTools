using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    [Serializable]
    public sealed class HoFacePathRemap
    {
        public string sourcePath = "Body";
        public SkinnedMeshRenderer target;
    }

    /// <summary>Editor-only sessions subscribe to these callbacks. Exported players never open a socket.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(-10000)]
    [AddComponentMenu("HoUnityTools/Face Tracking/Ho Face Tracking Debugger")]
    public sealed class HoFaceTrackingDebugger : MonoBehaviour
    {
        public Animator targetAnimator;
        public RuntimeAnimatorController faceController;
        public HoFaceRegion outputRegions = HoFaceRegion.Expression;
        [Tooltip("仅启用面部形态键；手机头姿与眼骨旋转不会写入角色。")]
        public List<HoFaceChannel> channels = HoFaceTrackingChannels.CreateDefaults();
        public List<HoFacePathRemap> pathRemaps = new List<HoFacePathRemap>();
        [Min(0.1f)] public float staleSeconds = 1f;
        [Min(0.01f)] public float neutralFadeSeconds = 0.2f;
        [Tooltip("分组指数平滑的时长（秒）。0 = 不过滤、直接透传 —— 默认就是 0，避免和手机侧自带的处理叠出额外延迟。"
            + "眼球要跟得紧、眼睑要稳、嘴更黏，所以分三组而不是一个系数。")]
        public float smoothEyelids;
        public float smoothGaze;
        public float smoothMouth;
        [Tooltip("眉、脸颊、鼻子等剩下的键。")]
        public float smoothOther;
        [Tooltip("分组死区：低于它的（实时）输入按 0 处理，以上的部分重新铺满 0..1。用来压住静止时的抖动。0 = 关。\n"
            + "位置对齐参考实现的 OSCm/Sensitivity 分组，但具体曲线没有逐位复刻 —— 这一版先做死区这个最有用的整形。")]
        public float deadZoneEyelids;
        public float deadZoneGaze;
        public float deadZoneMouth;
        public float deadZoneOther;
        [Tooltip("进入播放后自动启动角色动画会话，不会自动连接手机。")]
        public bool startOnPlay;

        /// <summary>按形态键名取分组平滑时长（秒）；0 = 直通。</summary>
        public float SmoothSeconds(string shape) => SmoothSeconds(HoFaceTrackingChannels.SmoothGroup(shape));

        /// <summary>某一组的死区。只作用在**实时输入**上；手动滑杆是调试用的，不该被它吃掉。</summary>
        public float DeadZone(HoFaceSmoothGroup group)
        {
            switch (group)
            {
                case HoFaceSmoothGroup.Eyelids: return deadZoneEyelids;
                case HoFaceSmoothGroup.Gaze: return deadZoneGaze;
                case HoFaceSmoothGroup.Mouth: return deadZoneMouth;
                default: return deadZoneOther;
            }
        }

        /// <summary>
        /// 响应整形（灵敏度）：死区以下归 0，以上重新铺满 0..1。
        /// 静止时面捕总有几十分之一的抖动，死区是压住它最直接的手段。
        /// </summary>
        public float ApplySensitivity(string shape, float value)
        {
            float dead = Mathf.Clamp(DeadZone(HoFaceTrackingChannels.SmoothGroup(shape)), 0f, 0.95f);
            if (dead <= 0f) return value;
            return value <= dead ? 0f : Mathf.Clamp01((value - dead) / (1f - dead));
        }

        /// <summary>某一组的分组平滑时长（秒）；0 = 直通。</summary>
        public float SmoothSeconds(HoFaceSmoothGroup group)        {
            switch (group)
            {
                case HoFaceSmoothGroup.Eyelids: return smoothEyelids;
                case HoFaceSmoothGroup.Gaze: return smoothGaze;
                case HoFaceSmoothGroup.Mouth: return smoothMouth;
                default: return smoothOther;
            }
        }

#if UNITY_EDITOR
        public static event Action<HoFaceTrackingDebugger> EditorTick;
        public static event Action<HoFaceTrackingDebugger> EditorLateTick;
        public static event Action<HoFaceTrackingDebugger> EditorDisabled;
        private void Update() => EditorTick?.Invoke(this);
        /// <summary>动画求值之后才有影子结果可抄，所以写回放在 LateUpdate。</summary>
        private void LateUpdate() => EditorLateTick?.Invoke(this);
        private void OnDisable() => EditorDisabled?.Invoke(this);
#endif

        private void Reset()
        {
            targetAnimator = GetComponent<Animator>();
            if (targetAnimator == null) targetAnimator = GetComponentInParent<Animator>();
            if (targetAnimator == null) targetAnimator = GetComponentInChildren<Animator>();
        }
    }
}
