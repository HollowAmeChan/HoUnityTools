using UnityEngine;

namespace Hollow.HoUnityTools.PlaySpeed
{
    /// <summary>Optional scene configuration: applies an explicit speed only during play.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("HoUnityTools/播放速度/自动化倍率")]
    public sealed class HoPlaySpeedOverride : MonoBehaviour
    {
        [SerializeField, Range(0f, HoPlaySpeedControl.MaxSpeed), InspectorName("播放倍率")]
        private float speed = 1f;
        [SerializeField, InspectorName("播放时锁定调速面板")]
        private bool lockPanel = true;
        private float appliedSpeed;
        private bool appliedLock;

        public float Speed
        {
            get => speed;
            set
            {
                speed = Mathf.Clamp(value, 0f, HoPlaySpeedControl.MaxSpeed);
                Apply();
            }
        }
        public bool LockPanel
        {
            get => lockPanel;
            set { lockPanel = value; Apply(); }
        }

        private void OnEnable() => Apply();
        private void OnDisable() => HoPlaySpeedControl.Release(this);
        private void OnValidate()
        {
            if (float.IsNaN(speed) || float.IsInfinity(speed)) speed = 1f;
            speed = Mathf.Clamp(speed, 0f, HoPlaySpeedControl.MaxSpeed);
        }
        private void Update()
        {
            if (speed != appliedSpeed || lockPanel != appliedLock) Apply();
        }

        public void Apply()
        {
            if (Application.isPlaying && isActiveAndEnabled)
            {
                HoPlaySpeedControl.SetOverride(this, speed, lockPanel);
                appliedSpeed = speed;
                appliedLock = lockPanel;
            }
        }
    }
}
