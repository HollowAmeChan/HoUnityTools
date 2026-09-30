using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Hollow.HoUnityTools.Editor.QuickCapture
{
    internal static class HoQuickCaptureCameraRenderer
    {
        /// <summary>
        /// TAA 预热的进度，给面板画进度条用。
        ///
        /// 用户报过：「预热没进度条显示搞得我以为卡了」——
        /// 16 帧静默地等，从面板上看和"卡死"完全一样。所以预热期间必须看得见进度。
        /// 放在静态字段上是因为它要跨"帧泵 → 引擎 → 面板"三层传递，而这三层各有各的生命周期；
        /// 会话一开始预热就置位，`Dispose()` 里清掉。
        /// </summary>
        public static bool WarmupActive { get; private set; }

        public static int WarmupDone { get; private set; }

        public static int WarmupTotal { get; private set; }

        /// <summary>会话结束时把预热进度收掉（幂等）。</summary>
        internal static void ClearWarmup()
        {
            WarmupActive = false;
            WarmupDone = 0;
            WarmupTotal = 0;
        }

        // The pump owns this session until readback/cancellation. Keep one target throughout TAA
        // warmup, and only sample distinct engine frames: same-frame rerenders do not update history.
        internal sealed class Session : IDisposable
        {
            private const int TemporalSamples = 16;
            private readonly Camera camera;
            private readonly bool transparent;
            private readonly RenderTexture previousTarget;
            private readonly double startedAt;
            private HoQuickCaptureSkyboxOff backgroundScope;
            private bool disposed;
            private bool capturedState;
            private bool wantsTaa;
            private int lastFrame = -1;
            private int samples;
            public RenderTexture Target { get; private set; }
            public bool UsedFallbackCamera { get; private set; }

            internal Session(Camera source, int width, int height, bool forceTransparent)
            {
                if (width < 1 || height < 1 || width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize)
                    throw new ArgumentOutOfRangeException(nameof(width), "截帧分辨率超出设备支持范围。");
                UsedFallbackCamera = source == null;
                camera = source != null ? source : ResolveMainCamera();
                if (camera == null)
                    throw new InvalidOperationException("找不到可用相机，请指定一台场景相机。");
                transparent = forceTransparent;
                previousTarget = camera.targetTexture;
                startedAt = EditorApplication.timeSinceStartup;
                try
                {
#if HO_URP_AVAILABLE
                    var extra = camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                    if (IsUrpActive() && extra != null)
                    {
                        if (extra.renderType != UnityEngine.Rendering.Universal.CameraRenderType.Base)
                            throw new InvalidOperationException("请指定 Base 相机；Overlay 相机需通过所属 Base 相机栈截帧。");
#if UNITY_2023_1_OR_NEWER
                        wantsTaa = extra.renderPostProcessing
                            && extra.antialiasing == UnityEngine.Rendering.Universal.AntialiasingMode.TemporalAntiAliasing;
#endif
                    }
#endif
                    Target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
                    {
                        name = "HoQuickCaptureCameraTarget",
                        wrapMode = TextureWrapMode.Clamp,
                        hideFlags = HideFlags.HideAndDontSave,
                        antiAliasing = 1,
                    };
                    if (!Target.Create())
                        throw new InvalidOperationException("无法创建截帧 RenderTexture。");
                    capturedState = true;
                    // ⚠️ 这里**故意不动** `camera.enabled`、也**不动** `camera.targetDisplay`。
                    //
                    // 用户报过：「我挂上摄像机 3，输出到 display 3 上，但是截帧后会显示
                    // display 3 没东西在输出」。原因就是这两行曾经存在：
                    //   · `targetDisplay = 0` 把相机从 display N 挪到主显示；
                    //   · `enabled = false` 干脆把它关了。
                    // 而这个会话要活到 TAA 预热结束（16 帧），于是**整段时间里 display N
                    // 名下没有任何启用中的相机**，Unity 就会报"这个 display 没有东西在输出"。
                    //
                    // 关键是：**这两行本来就没必要**。
                    // 只要 `targetTexture` 被设上，这台相机就不往它的 display 上画了
                    //（这正是我们要的效果），`enabled` 与 `targetDisplay` 都不会影响
                    // "渲染到 RT 然后读回"这件事。既然不必要，就别去动用户的多显示器配置。
                    camera.targetTexture = Target;
                    if (transparent)
                        backgroundScope = HoQuickCaptureSkyboxOff.Apply(camera);
#if HO_URP_AVAILABLE && UNITY_2023_1_OR_NEWER
                    if (wantsTaa && extra != null)
                        extra.resetHistory = true;
#endif
                }
                catch { Dispose(); throw; }
            }

            // False means still warming up. The pump handles failures and disposes the session.
            public bool RenderNext()
            {
                if (disposed || camera == null)
                    throw new InvalidOperationException("截帧相机已被销毁或会话已结束。");
                if (EditorApplication.timeSinceStartup - startedAt > 30.0)
                    throw new InvalidOperationException(
                        "TAA 预热没能在 30 秒内凑够 " + TemporalSamples + " 个新帧"
                        + "（已经拿到 " + samples + " 帧）。"
                        + "这是 Anti-aliasing = TAA 的固有限制：TAA 靠**连续多帧**累积抖动才收敛，"
                        + "单帧出不来。可以：① 确认编辑器窗口没被最小化、播放正常推进；"
                        + "② 把相机的 Anti-aliasing 换成 SMAA（单帧就有）；"
                        + "③ 或者干脆关掉抗锯齿。");
                if (wantsTaa && samples > 0 && Time.frameCount == lastFrame)
                    return false;

                // 把预热进度挂到全局，面板据此画进度条。
                // 用户报过：「预热没进度条显示搞得我以为卡了」—— 16 帧静默地等，
                // 从面板上看和"卡死"完全一样，所以这段必须有可见的进度。
                WarmupDone = samples;
                WarmupTotal = wantsTaa ? TemporalSamples : 1;
                WarmupActive = true;

                RenderTexture previousActive = RenderTexture.active;
                try
                {
                    // Keep the global alpha override strictly inside the render request.
                    using (var alpha = transparent ? HoQuickCaptureAlphaOutputScope.Apply() : null)
                        RenderWithPipeline(camera, Target);
                }
                finally { RenderTexture.active = previousActive; }
                lastFrame = Time.frameCount;
                samples++;
                return !wantsTaa || samples >= TemporalSamples;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                ClearWarmup();
                backgroundScope?.Dispose();
                if (capturedState && camera != null)
                {
                    camera.targetTexture = previousTarget;
#if HO_URP_AVAILABLE && UNITY_2023_1_OR_NEWER
                    // Capture history must not leak into the next normal Game View frame.
                    var extra = camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                    if (extra != null && extra.antialiasing == UnityEngine.Rendering.Universal.AntialiasingMode.TemporalAntiAliasing)
                        extra.resetHistory = true;
#endif
                }
                if (Target != null)
                {
                    if (RenderTexture.active == Target) RenderTexture.active = null;
                    Target.Release();
                    Object.DestroyImmediate(Target);
                    Target = null;
                }
            }
        }

        private static void RenderWithPipeline(Camera camera, RenderTexture destination)
        {
            if (GraphicsSettings.currentRenderPipeline == null)
            {
                camera.Render();
                return;
            }
#if HO_URP_AVAILABLE && UNITY_2023_1_OR_NEWER
            if (IsUrpActive())
            {
                var request = new RenderPipeline.StandardRequest { destination = destination };
                if (!RenderPipeline.SupportsRenderRequest(camera, request))
                    throw new NotSupportedException("当前 URP 不支持 StandardRequest 截帧。");
                RenderPipeline.SubmitRenderRequest(camera, request);
                return;
            }
#endif
            // An invalid context or a different rendering pipeline is not a valid fallback.
            throw new NotSupportedException("当前 SRP/Unity 版本不支持此相机截帧入口；URP 相机截帧需要 Unity 2023.1 或更新版本。");
        }

#if HO_URP_AVAILABLE
        private static bool IsUrpActive() => GraphicsSettings.currentRenderPipeline
            is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
#endif

        // Only the Game View fallback uses this heuristic. Black/transparent camera output is valid.
        internal static bool IsBlank(RenderTexture source)
        {
            if (source == null) return true;
            RenderTexture previous = RenderTexture.active;
            Texture2D probe = null;
            try
            {
                RenderTexture.active = source;
                probe = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                probe.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0, false);
                probe.Apply(false, false);
                Color32[] pixels = probe.GetPixels32();
                int step = Mathf.Max(1, pixels.Length / 512);
                for (int i = 0; i < pixels.Length; i += step)
                    if (pixels[i].r != 0 || pixels[i].g != 0 || pixels[i].b != 0) return false;
                return true;
            }
            catch (Exception) { return true; }
            finally
            {
                RenderTexture.active = previous != null && previous.IsCreated() ? previous : null;
                if (probe != null) Object.DestroyImmediate(probe);
            }
        }

        private static Camera ResolveMainCamera()
        {
            if (Camera.main != null) return Camera.main;
            foreach (Camera candidate in Resources.FindObjectsOfTypeAll<Camera>())
                if (candidate.isActiveAndEnabled && candidate.gameObject.scene.IsValid()
                    && candidate.gameObject.scene.isLoaded && candidate.cameraType == CameraType.Game) return candidate;
            return null;
        }
    }
}
