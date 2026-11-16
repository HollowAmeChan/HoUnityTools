using UnityEngine;

namespace Hollow.HoUnityTools.HoCamera
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("HoUnityTools/HoCamera")]
    public sealed class HoCamera : MonoBehaviour
    {
        [SerializeField] private HoCameraAsset asset;
        [SerializeField] private bool applyOnEnable;
        [SerializeField] private bool play;
        [SerializeField] private bool loop = true;
        [SerializeField, Min(0f)] private float currentFrame;

        public HoCameraAsset Asset { get => asset; set => asset = value; }
        public bool Play { get => play; set => play = value; }
        public float CurrentFrame { get => currentFrame; set => currentFrame = Mathf.Max(0f, value); }
        public Camera TargetCamera => GetComponent<Camera>();

        private void OnEnable()
        {
            // An imported asset should not overwrite hand-authored scene state on editor reload.
            if (Application.isPlaying && applyOnEnable) ApplySnapshot();
        }

        private void OnValidate() => currentFrame = Mathf.Max(0f, currentFrame);

        private void Update()
        {
            if (!play || asset == null || asset.Frames.Count == 0) return;
            asset.ApplyAtFrame(TargetCamera, currentFrame);
            currentFrame += Time.deltaTime * asset.FrameRate;
            if (currentFrame > asset.EndFrame)
            {
                if (loop) currentFrame = asset.StartFrame;
                else { currentFrame = asset.EndFrame; play = false; }
            }
        }

        public void ApplySnapshot() => asset?.ApplySnapshot(TargetCamera);
        public void ApplyAtCurrentFrame() => asset?.ApplyAtFrame(TargetCamera, currentFrame);
    }
}
