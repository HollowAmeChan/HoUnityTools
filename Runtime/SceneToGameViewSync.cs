using UnityEngine;

namespace Hollow.HoUnityTools
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class SceneToGameViewSync : MonoBehaviour
    {
        [Tooltip("是否启用Scene视图到Game视图的同步")]
        public bool enableSync = true;

        [Tooltip("同步频率 (每秒次数)")]
        [Min(1f)]
        public float syncFrequency = 30f;

        [Tooltip("是否同步位置")]
        public bool syncPosition = true;

        [Tooltip("是否同步旋转")]
        public bool syncRotation = true;

        [Tooltip("是否同步视野 (FOV)")]
        public bool syncFOV = true;

        [Tooltip("是否同步裁切平面")]
        public bool syncClippingPlanes = true;

        [Tooltip("是否在播放模式下也同步")]
        public bool syncInPlayMode;

        internal Camera TargetCamera { get; private set; }
        internal Vector3 LastScenePosition { get; set; }
        internal Quaternion LastSceneRotation { get; set; }
        internal float LastSceneFOV { get; set; }
        internal float LastSceneNearClipPlane { get; set; }
        internal float LastSceneFarClipPlane { get; set; }
        internal float LastSyncTime { get; set; }

        private void OnEnable()
        {
            TargetCamera = GetComponent<Camera>();
        }

        private void OnValidate()
        {
            if (syncFrequency < 1f)
                syncFrequency = 1f;
        }
    }
}
