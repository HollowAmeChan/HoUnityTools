// HoQuickCaptureCameraRenderer.cs -- 把**指定相机**渲进一张带 alpha 的 RenderTexture
//
// 为什么要单独一条路：游戏视图的抓屏（ScreenCapture.CaptureScreenshotIntoRenderTexture）
// 拿的是画到屏幕上的最终合成结果，而**屏幕没有透明通道这回事** —— 相机 clear 成什么就是什么。
// 所以透明背景不可能从抓屏拿到，只能让相机自己渲进一张带 alpha 的 RT。
//
// ⚠️ 三个已知门槛（缺一个就得不到透明，而且失败时都是"图是对的、alpha 全是 1"这种静默症状）：
//   ① 相机：`clearFlags = SolidColor` + `backgroundColor.a = 0`。
//      Skybox / DepthOnly 都给不出透明；只改 RGB 不改 A 也没用。
//   ② RT 格式要有 alpha：用 `ARGB32`（8 位够用）或 `ARGBHalf`。
//      **别用 `RenderTextureFormat.Default`** —— 它不保证带 alpha。
//   ③ 渲染管线：URP 的 post-processing 会把 alpha 清掉，要在 URP Asset 里勾上
//      `Allow Post Process Alpha Output`。这一步在包外（用户的 RP Asset），我们只能提示、改不了。
//
// 两条渲染路径：
//   · 内建管线：`camera.Render()`。
//   · URP：`camera.Render()` **不走 URP**（后处理与 URP 的透明处理都会丢），
//     得用 `UniversalRenderPipeline.RenderSingleCamera(context, camera)`。
//     那是 URP 程序集里的公开 API，但本包**不硬依赖 URP**（package.json 里没有它），
//     所以走反射调 —— 没装 URP 就是 no-op，装了才生效。
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Hollow.HoUnityTools.Editor.QuickCapture
{
    /// <summary>把指定相机渲进 RT。用完即还原相机的全部临时改动。</summary>
    internal static class HoQuickCaptureCameraRenderer
    {

        /// <summary>
        /// 渲一帧到一张新的 RenderTexture。调用方负责 <see cref="Object.DestroyImmediate(Object)"/> 它。
        /// 失败时返回 null，原因在 <paramref name="error"/>。
        /// </summary>
        /// <param name="camera">要渲的相机。为空则自动挑一台主摄像机。</param>
        /// <param name="width">宽。</param>
        /// <param name="height">高。</param>
        /// <param name="transparent">是否临时改成透明背景（拍完还原）。</param>
        /// <param name="usedFallbackCamera">true 表示相机是自动挑的，不是用户指定的。</param>
        /// <param name="note">非空 = 这次渲染退过路（图仍然出来了，但有话要说）。</param>
        /// <param name="error">失败原因。</param>
        public static RenderTexture Render(
            Camera camera,
            int width,
            int height,
            bool transparent,
            out bool usedFallbackCamera,
            out string note,
            out string error)
        {
            error = null;
            note = null;
            usedFallbackCamera = false;

            if (width < 1 || height < 1)
            {
                error = "分辨率不合法：" + width + "x" + height;
                return null;
            }

            Camera target = camera;
            if (target == null)
            {
                target = ResolveMainCamera();
                usedFallbackCamera = true;
                if (target == null)
                {
                    error = "找不到可用的相机：指定一台，或者让场景里至少有一台启用中的相机。";
                    return null;
                }
            }

            // ARGB32：**必须有 alpha 通道**才能出透明。别用 Default（不保证带 alpha）。
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "HoQuickCaptureCameraTarget",
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            rt.Create();

            // 借了要还：把这一帧要用到的相机设置全存下来，finally 里原样写回去。
            RenderTexture previousTarget = target.targetTexture;
            CameraClearFlags previousClearFlags = target.clearFlags;
            Color previousBackground = target.backgroundColor;

            // 天空盒不在相机上，是**场景级**的，所以单独一层作用域管它的存与还原。
            // 只在"要透明"时才关 —— 不透明的话天空盒照画，跟平时一样。
            HoQuickCaptureSkyboxOff skyboxOff = null;

            // URP 的后处理会把 alpha 清掉，那个开关也在包外（URP Asset 上）。
            // 同样只在"要透明"时临时打开，拍完改回去。
            HoQuickCaptureAlphaOutputScope alphaScope = null;

            // 说明是攒出来的：临时改了什么 + 渲染退没退路。最后一次性交给 out 参数。
            string accumulated = null;

            try
            {
                target.targetTexture = rt;

                if (transparent)
                {
                    // ⚠️ 顺序要紧：先关天空盒，再设透明清屏色。
                    // 只设清屏色是**不够**的 —— 天空盒是画在背景之上的一层几何，
                    // 不关掉的话天空那块仍然会被它填满（用户报的正是这个）。
                    skyboxOff = HoQuickCaptureSkyboxOff.Apply();

                    string alphaNote;
                    alphaScope = HoQuickCaptureAlphaOutputScope.Apply(out alphaNote);
                    accumulated = Append(accumulated, alphaNote);

                    target.clearFlags = CameraClearFlags.SolidColor;
                    target.backgroundColor = new Color(
                        previousBackground.r,
                        previousBackground.g,
                        previousBackground.b,
                        0f);
                }

                string renderNote;
                RenderWithPipeline(target, rt, out renderNote);
                accumulated = Append(accumulated, renderNote);

                if (skyboxOff != null && skyboxOff.ChangedAnything)
                {
                    accumulated = Append(accumulated,
                        "天空盒已临时关掉（那块直接算全透明）—— 这一张的间接光与反射跟平时不完全一样，"
                        + "拍完已还原。");
                }

                note = accumulated;
                LastRenderNote = accumulated;
                return rt;
            }
            catch (Exception exception)
            {
                error = "渲相机失败：" + exception.Message;
                LastRenderNote = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                return null;
            }
            finally
            {
                // 包外的东西先还（哪怕上面抛了也要还）。
                if (alphaScope != null)
                {
                    alphaScope.Dispose();
                }

                if (skyboxOff != null)
                {
                    skyboxOff.Dispose();
                }

                target.targetTexture = previousTarget;
                target.clearFlags = previousClearFlags;
                target.backgroundColor = previousBackground;
            }
        }

        /// <summary>最近一次渲染留下的说明（退路提示 / 临时改过什么）。</summary>
        public static string LastRenderNote { get; private set; }

        /// <summary>把一句话拼到 note 后面（note 可能是 null）。</summary>
        private static string Append(string note, string addition)
        {
            return string.IsNullOrEmpty(note) ? addition : note + " " + addition;
        }

        /// <summary>
        /// 按当前渲染管线渲一帧。**渲完要验一下**，因为两条路都可能"没画上"：
        ///   · URP 的 `RenderSingleCamera` 需要一个由引擎初始化的 `ScriptableRenderContext`，
        ///     而我们只能自己 new 一个 —— 这在部分 URP 版本上会静默什么都不画；
        ///   · 内建 `camera.Render()` 本身是可靠的，但它在 URP 工程里不走 URP（后处理与透明处理都不参与）。
        /// 所以顺序是"先试 URP → 验 → 空了就退回 camera.Render()"，
        /// 并把最后用的哪条路通过 <paramref name="note"/> 告诉用户（如果退过路）。
        /// </summary>
        private static void RenderWithPipeline(Camera camera, RenderTexture rt, out string note)
        {
            note = null;

#if HO_URP_AVAILABLE
            if (IsUrpActive())
            {
                if (TryRenderWithUrp(camera) && !IsBlank(rt))
                {
                    // URP 接了，而且真画出东西了。
                    return;
                }

                // URP 那条路没成（调用失败，或画出来是空的）—— 退回内建。
                note = "URP 的单相机渲染没画出东西，已退回 `camera.Render()`："
                    + "这张图**不含 URP 的后处理**。";
            }
#endif

            camera.Render();
        }

#if HO_URP_AVAILABLE
        /// <summary>
        /// 当前工程是不是在用 URP。
        ///
        /// `HO_URP_AVAILABLE` 只保证"装了 URP 包"，不保证"这个工程在用 URP"
        ///（可以装了却用内建管线）。所以还要看当前管线资产的类型。
        /// </summary>
        private static bool IsUrpActive()
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null)
            {
                return false;
            }

            // 直接类型判断：Unity 6 里 `UniversalRenderPipelineAsset` 就在这个命名空间下，
            // 2021.3 也在。至于 `renderPipeline` 是不是 `UniversalRenderPipeline`，
            // `RenderSingleCamera` 自己会处理。
            return pipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
        }

        /// <summary>
        /// 让 URP 渲这一台相机。
        ///
        /// ⚠️ URP 下**必须**用这个，`camera.Render()` 会走内建路径 ——
        /// URP 的后处理与透明处理都不参与，结果既不是 URP 的画面、也不带 alpha。
        ///
        /// ⚠️ 它要一个 `ScriptableRenderContext`，而引擎内部那个拿不到，只能自己 new。
        /// 这在部分 URP 版本上会静默不画 —— 所以调用方一定会再验一次 `IsBlank`。
        /// 用户明确同意"可以调 URP 函数"，所以这里是**直接调用**（不再反射）；
        /// 用 `#if HO_URP_AVAILABLE` 保证没装 URP 的工程照样编得过。
        /// </summary>
        private static bool TryRenderWithUrp(Camera camera)
        {
            try
            {
                var context = new ScriptableRenderContext();
                UnityEngine.Rendering.Universal.UniversalRenderPipeline.RenderSingleCamera(context, camera);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[快速渲染] URP 单相机渲染调用失败，退回 camera.Render()（画面可能不含 URP 后处理）："
                    + exception.Message);
                return false;
            }
        }
#endif

        /// <summary>
        /// 整幅是不是单色（= 基本可以断定没画上）。
        /// 稀疏采样，别整帧扫 —— 1080p 一帧 200 万个像素，扫一遍不值当。
        /// </summary>
        internal static bool IsBlank(RenderTexture source)
        {
            if (source == null)
            {
                return true;
            }

            RenderTexture previous = RenderTexture.active;
            Texture2D probe = null;
            try
            {
                RenderTexture.active = source;
                probe = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                probe.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0, false);
                probe.Apply(false, false);

                Color32[] pixels = probe.GetPixels32();
                if (pixels.Length == 0)
                {
                    return true;
                }

                int step = Mathf.Max(1, pixels.Length / 512);
                Color32 first = pixels[0];
                for (int i = 0; i < pixels.Length; i += step)
                {
                    Color32 c = pixels[i];
                    if (c.r != first.r || c.g != first.g || c.b != first.b)
                    {
                        return false;
                    }
                }

                return first.r == 0 && first.g == 0 && first.b == 0;
            }
            catch (Exception)
            {
                return true;
            }
            finally
            {
                RenderTexture.active = previous;
                if (probe != null)
                {
                    Object.DestroyImmediate(probe);
                }
            }
        }

        /// <summary>自动挑一台相机：`Camera.main` 优先，其次当前场景里第一台可用的。</summary>
        private static Camera ResolveMainCamera()
        {
            Camera main = Camera.main;
            if (IsUsable(main))
            {
                return main;
            }

            // 用 FindObjectsOfTypeAll + 场景有效性过滤：2021.3 与 Unity 6 都不出弃用警告
            //（FindObjectsOfType 在 Unity 6 已过时，FindObjectsByType 又只有 2022.2+ 才有）。
            foreach (Camera candidate in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (!IsInLoadedScene(candidate))
                {
                    continue;
                }

                if (IsUsable(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool IsUsable(Camera camera)
        {
            if (camera == null || !camera.isActiveAndEnabled)
            {
                return false;
            }

            // Scene 视图相机与预览相机不是"游戏视图那台"。
            return camera.cameraType == CameraType.Game || camera.cameraType == CameraType.Preview;
        }

        private static bool IsInLoadedScene(Component component)
        {
            if (component == null)
            {
                return false;
            }

            UnityEngine.SceneManagement.Scene scene = component.gameObject.scene;
            return scene.IsValid() && scene.isLoaded;
        }

        /// <summary>
        /// 提醒用户 URP 那个"后处理保留 alpha"的开关。
        /// 这是**包外**的设置（在用户的 URP Asset 上），我们改不了，只能提前说一声，
        /// 否则症状是"图看着对、alpha 全是 1"，很难联想到是它。
        /// </summary>
        public static string DescribeAlphaPitfall()
        {
            return "透明背景还取决于渲染管线：用 URP 的话要在 URP Asset 上勾 `Allow Post Process Alpha Output`，"
                + "否则后处理会把 alpha 写回 1。";
        }
    }
}
