using UnityEngine;

namespace Hollow.HoUnityTools.PlaySpeed
{
    // Created only in play mode by HoPlaySpeedControl; never saved in a scene.
    [DefaultExecutionOrder(32000)]
    internal sealed class HoPlaySpeedDriver : MonoBehaviour
    {
        private void OnEnable() => HoPlaySpeedControl.RegisterDriver(this);
        private void Update() => HoPlaySpeedControl.Tick();
        private void LateUpdate() => HoPlaySpeedControl.Tick();
    }
}
