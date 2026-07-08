using UnityEngine;

namespace Hollow.HoUnityTools
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("HoUnityTools/Ho Scene To Game View Sync")]
    public sealed class HoSceneToGameViewSync : MonoBehaviour
    {
        [Tooltip("持续自动跟随Scene视图。默认关闭，建议优先使用手动吸附。")]
        public bool enableSync;

        [Tooltip("自动跟随频率（每秒次数）")]
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

        [Tooltip("自动跟随是否在播放模式下也生效")]
        public bool syncInPlayMode;

        internal Vector3 LastScenePosition { get; set; }
        internal Quaternion LastSceneRotation { get; set; }
        internal float LastSceneFOV { get; set; }
        internal float LastSceneNearClipPlane { get; set; }
        internal float LastSceneFarClipPlane { get; set; }
        internal float LastSyncTime { get; set; }

        internal Camera AttachedCamera => GetComponent<Camera>();

        private void OnValidate()
        {
            if (syncFrequency < 1f)
                syncFrequency = 1f;
        }
    }
}
