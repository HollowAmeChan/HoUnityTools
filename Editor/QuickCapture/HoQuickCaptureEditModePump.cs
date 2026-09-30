// HoQuickCaptureEditModePump.cs -- 编辑模式下的帧泵（不在播放模式也能拍一张）
//
// 为什么播放模式那套在这里用不了：
//   · 播放模式靠 `MonoBehaviour` + 协程 `yield return new WaitForEndOfFrame()` 等"这一帧画完"；
//     编辑模式**没有那个帧循环**，协程永远等不到。
//   · 而且编辑模式下游戏视图**平时根本不重画** —— 它只在你动它（或场景变化）时才画一次。
//     所以想拍，得自己**催一帧出来**。
//
// 怎么催（这就是本文件存在的全部理由）：
//   ① `InternalEditorUtility.RepaintAllViews()` —— 让游戏视图排队重画；
//   ② `EditorApplication.QueuePlayerLoopUpdate()` —— 让播放循环（含摄像机渲染）真的跑一次；
//   ③ 等**下一个** `EditorApplication.update` tick —— 那时这一帧已经画完了，再抓。
//   三步都不能少：不催就没画面，催完当场抓又会抓到上一帧。
//
// 抓画面本身仍然用 `ScreenCapture.CaptureScreenshotIntoRenderTexture` ——
// 和播放模式**同一个调用**，所以两条路拍出来的东西是一致的（含后处理与 UI）。
//
// 万一编辑模式下那个调用拿不到画面（各版本行为不完全一致），退一步用
// `Camera.Render()` 渲主摄像机。那一路**没有 UI、没有多相机合成**，所以会明确告诉用户
// "这张是主摄像机直渲"，让他知道看到的和游戏视图可能不一样 —— 不假装一样。
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Hollow.HoUnityTools.Runtime.QuickCapture;

namespace Hollow.HoUnityTools.Editor.QuickCapture
{
    /// <summary>
    /// 编辑模式帧泵：催帧 → 抓 → 交出去。一次会话抓一帧（编辑模式下没有"时间在走"这回事）。
    /// </summary>
    internal sealed class HoQuickCaptureEditModePump
    {
        /// <summary>催帧之后最多等几个 update tick 就放弃（每个 tick 都重新催一次）。</summary>
        private const int MaxPumpAttempts = 12;

        private readonly HoQuickCapturePlan plan;
        private readonly Action<HoQuickCapturedFrame> onFrame;

        /// <summary>结束时回调，参数是"这次有什么要提醒的"（null = 一切正常）。</summary>
        private readonly Action<string> onFinished;

        private RenderTexture target;
        private Camera fallbackCamera;
        private int attempts;
        private bool started;
        private bool finished;
        private string lastFailure;

        private HoQuickCaptureEditModePump(
            HoQuickCapturePlan capturePlan,
            Action<HoQuickCapturedFrame> frameCallback,
            Action<string> finishedCallback)
        {
            plan = capturePlan;
            onFrame = frameCallback;
            onFinished = finishedCallback;
        }

        /// <summary>
        /// 当前活着的那一台（编辑模式同时只会有一段抓取）。
        /// 引擎"停止"时要把挂在 <see cref="EditorApplication.update"/> 上的这一台摘掉，
        /// 否则它下一个 tick 还会回调过来，跟引擎的收摊撞车。
        /// </summary>
        private static HoQuickCaptureEditModePump active;

        /// <summary>强行中止当前编辑模式抓取（没有就什么都不做）。</summary>
        public static void Cancel()
        {
            HoQuickCaptureEditModePump pump = active;
            if (pump == null)
            {
                return;
            }

            pump.finished = true;
            EditorApplication.update -= pump.OnEditorUpdate;
            pump.Dispose();
            active = null;
        }

        /// <summary>起一次编辑模式抓取。返回 false 表示连准备都没成功（原因在 <paramref name="error"/>）。</summary>
        public static bool Start(
            HoQuickCapturePlan plan,
            Action<HoQuickCapturedFrame> frameCallback,
            Action<string> finishedCallback,
            out string error)
        {
            error = null;

            // 上一次没收干净的，先摘掉。
            Cancel();

            var pump = new HoQuickCaptureEditModePump(plan, frameCallback, finishedCallback);

            if (!pump.Prepare())
            {
                error = pump.lastFailure ?? "编辑模式下准备抓帧失败。";
                pump.Dispose();
                return false;
            }

            active = pump;
            pump.started = true;
            EditorApplication.update += pump.OnEditorUpdate;
            pump.BeginWaitForFrame();
            return true;
        }

        private bool Prepare()
        {
            if (plan.Width < 1 || plan.Height < 1)
            {
                lastFailure = "分辨率不合法：" + plan.Width + "x" + plan.Height;
                return false;
            }

            target = new RenderTexture(plan.Width, plan.Height, 0, RenderTextureFormat.ARGB32)
            {
                name = "HoQuickCaptureEditModeTarget",
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            target.Create();
            return true;
        }

        /// <summary>催一帧：重画视图 + 让播放循环跑一次，然后等下一个 tick。</summary>
        private void BeginWaitForFrame()
        {
            attempts++;
            if (attempts > MaxPumpAttempts)
            {
                Finish("催了 " + MaxPumpAttempts + " 次也没等到一帧画面。"
                    + "编辑模式下游戏视图可能没在重画 —— 点一下游戏视图窗口再试。");
                return;
            }

            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            EditorApplication.QueuePlayerLoopUpdate();
        }

        private void OnEditorUpdate()
        {
            if (!started || finished)
            {
                return;
            }

            // 每次 tick 都先催下一帧：不催的话编辑模式下画面是"冻"的，
            // 第一次扑空之后就再也等不到了。
            if (attempts <= MaxPumpAttempts)
            {
                TryCapture();
            }

            if (!finished)
            {
                BeginWaitForFrame();
            }
        }

        private void TryCapture()
        {
            string note = null;
            bool captured;

            // 这个 target 的内容是不是"**相机渲出来的**"？决定读回时要不要额外翻一次。
            // 三条路里只有"游戏视图抓屏"那个 RT 是顶边在前；相机渲出来的都是底边在前。
            bool fromCameraRender;

            if (plan.RenderSource == HoQuickCaptureRenderSource.Camera)
            {
                // ── 指定相机渲进 RT（要透明背景就走这条）──
                // 这条路是**确定性**的：渲一次就有，不需要催帧，也不受"游戏视图有没有在重画"影响。
                captured = TryRenderSourceCamera(out note);
                fromCameraRender = true;
            }
            else
            {
                // ── 游戏视图合成结果 ──
                // ① 先试和播放模式同一条路：直接把游戏视图的合成结果拍进 RenderTexture。
                captured = false;
                fromCameraRender = false;
                try
                {
                    ScreenCapture.CaptureScreenshotIntoRenderTexture(target);
                    captured = !HoQuickCaptureCameraRenderer.IsBlank(target);
                }
                catch (Exception)
                {
                    captured = false;
                }

                // ② 不行就退到"渲一台相机"（那条也是相机渲出来的 ⇒ 要额外翻）。
                if (!captured)
                {
                    captured = TryRenderFallbackCamera(out note);
                    fromCameraRender = captured;
                }
            }

            if (!captured)
            {
                // 这一次没成不算失败，下一个 tick 还会再试
                //（编辑模式下催帧头几次扑空是正常的；相机那条路失败则会带 note 直接收摊）。
                if (note != null)
                {
                    Finish(note);
                }

                return;
            }

            HoQuickCapturedFrame frame;
            string failure;
            if (!HoQuickCaptureDriver.TryBuildFrameFrom(target, fromCameraRender, out frame, out failure))
            {
                Finish(failure);
                return;
            }

            // 序号与文件名交给驱动那套命名规则（两条路必须一致）。
            frame.Index = 0;
            frame.FileName = HoQuickCaptureDriver.BuildFrameFileName(plan, 0);

            if (onFrame != null)
            {
                onFrame(frame);
            }

            // 退路提示走 onFinished 传出去（引擎据此在面板上写一句警告）——
            // 不能只留在本地字段里，那个对象马上就要被丢掉了。
            Finish(note);
        }

        /// <summary>
        /// 用户选的「指定相机」那条路：渲进 RT（可透明）。
        /// 失败时把原因写进 <paramref name="note"/> 并返回 false。
        /// </summary>
        private bool TryRenderSourceCamera(out string note)
        {
            note = null;

            RenderTexture rendered = HoQuickCaptureCameraRenderer.Render(
                plan.SourceCamera,
                plan.Width,
                plan.Height,
                plan.ForceTransparentBackground,
                out bool usedFallbackCamera,
                out string renderNote,
                out string error);

            if (rendered == null)
            {
                note = error ?? "渲指定相机失败。";
                return false;
            }

            // 把结果换到我们复用的 target 上（后面统一从 target 取像素）。
            // 这一张是新建的，用完就销毁，别留着。
            try
            {
                Graphics.Blit(rendered, target);
            }
            finally
            {
                rendered.Release();
                UnityEngine.Object.DestroyImmediate(rendered);
            }

            if (HoQuickCaptureCameraRenderer.IsBlank(target))
            {
                note = "指定相机渲出来是空的：确认它启用中、Culling Mask 里有东西、并且不在别的 targetTexture 上。";
                return false;
            }

            // 正常拍完**不吭声**（用户要求少提示）。只有下面两种"你可能想立刻知道"的才说：
            //   · 相机是自动挑的（你以为是 A，结果渲的是 B）；
            //   · 渲染退了路（画面跟预期不一样）。
            List<string> notes = new List<string>();
            if (usedFallbackCamera)
            {
                notes.Add("没指定相机，用了 Camera.main。");
            }

            if (!string.IsNullOrEmpty(renderNote))
            {
                notes.Add(renderNote);
            }

            if (notes.Count > 0)
            {
                note = string.Join(" ", notes.ToArray());
            }

            return true;
        }

        /// <summary>
        /// 退路：把一台相机渲进目标。<b>没有 UI、没有多相机合成</b>，所以会带回一句说明。
        /// 返回 false 表示这一步也没成（原因在 <paramref name="note"/>）。
        /// </summary>
        private bool TryRenderFallbackCamera(out string note)
        {
            note = null;
            Camera camera = ResolveGameCamera();
            if (camera == null)
            {
                return false;
            }

            try
            {
                RenderTexture previous = camera.targetTexture;
                camera.targetTexture = target;
                camera.Render();
                camera.targetTexture = previous;
                note = "这张是**相机直渲**的（编辑模式下拿不到游戏视图的合成结果）："
                    + "没有 Screen Space-Overlay 的 UI，也没有多相机叠加。";
                return true;
            }
            catch (Exception exception)
            {
                note = "相机直渲也失败了：" + exception.Message;
                return false;
            }
        }

        /// <summary>
        /// 找"游戏视图里那台摄像机"：优先 `Camera.main`，没有就挑一台可用的。
        ///
        /// ⚠️ 用 `Resources.FindObjectsOfTypeAll` + **场景有效性过滤**，不用 `FindObjectsOfType`：
        ///   · 前者在 2021.3 与 Unity 6 上签名一致，**不会产生弃用警告**
        ///     （`FindObjectsOfType` 在 Unity 6 已标过时；`FindObjectsByType` 又只在 2022.2+ 才有）；
        ///   · 它会把**资产里的**（预制体）与编辑器内部对象也算进来，所以必须自己筛：
        ///     只认 `scene.IsValid() && scene.isLoaded` 的，那才是当前打开的场景里的东西。
        /// 再叠一层 <see cref="IsUsableGameCamera"/> 把 Scene 视图相机、已设 targetTexture 的排掉。
        /// </summary>
        private Camera ResolveGameCamera()
        {
            if (fallbackCamera != null)
            {
                return fallbackCamera;
            }

            Camera main = Camera.main;
            if (IsUsableGameCamera(main))
            {
                fallbackCamera = main;
                return fallbackCamera;
            }

            foreach (Camera candidate in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (!IsInLoadedScene(candidate))
                {
                    continue;
                }

                if (IsUsableGameCamera(candidate))
                {
                    fallbackCamera = candidate;
                    return fallbackCamera;
                }
            }

            return null;
        }

        /// <summary>这个对象是不是"当前打开的场景里的"（而不是资产 / 编辑器内部对象）。</summary>
        private static bool IsInLoadedScene(Component component)
        {
            if (component == null)
            {
                return false;
            }

            UnityEngine.SceneManagement.Scene scene = component.gameObject.scene;
            return scene.IsValid() && scene.isLoaded;
        }

        private static bool IsUsableGameCamera(Camera camera)
        {
            if (camera == null || !camera.isActiveAndEnabled)
            {
                return false;
            }

            if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.Preview)
            {
                return false;
            }

            // 已经在往别的 RenderTexture 上渲的，不是"游戏视图那台"。
            return camera.targetTexture == null;
        }

        /// <summary>
        /// 整幅是不是单色（= 基本可以断定没画上去）。
        /// 实现放在渲染器那边（<see cref="HoQuickCaptureCameraRenderer.IsBlank"/>），
        /// 因为"游戏视图抓屏"与"相机直渲"两条路都要靠它判断，别各写一份。
        /// </summary>
        private static bool IsBlank(RenderTexture source)
        {
            return HoQuickCaptureCameraRenderer.IsBlank(source);
        }

        private void Finish(string note)
        {
            if (finished)
            {
                return;
            }

            finished = true;
            EditorApplication.update -= OnEditorUpdate;
            if (active == this)
            {
                active = null;
            }

            if (!string.IsNullOrEmpty(note))
            {
                lastFailure = note;
            }

            Dispose();

            if (onFinished != null)
            {
                onFinished(lastFailure);
            }
        }

        private void Dispose()
        {
            if (target == null)
            {
                return;
            }

            // ⚠️ `RenderTexture.active` 是**全局状态**，Release 之前必须先把它摘下来，
            // 否则 Unity 会吼：
            //   "Releasing render texture that is set to be RenderTexture.active!"
            // 谁把它留成 active 的：`HoQuickCaptureCameraRenderer.IsBlank()` 会临时代我们
            // 设成 `RenderTexture.active = source`，正常情况下它的 `finally` 会还原 ——
            // 但**它还原的是"它进来时的那个值"**。如果那时 active 已经是这张 target
            //（比如读像素那一步留下的），它就还原成 target 自己，于是这里一 Release 就中招。
            //
            // 所以这里再兜一次：**这张图要销毁了，就不该再有任何东西指着它**。
            if (RenderTexture.active == target)
            {
                RenderTexture.active = null;
            }

            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            target = null;
        }
    }
}
