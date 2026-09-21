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
        [Tooltip("进入播放后自动启动角色动画会话，不会自动连接手机。")]
        public bool startOnPlay;

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
