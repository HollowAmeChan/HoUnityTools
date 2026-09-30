// HoQuickCaptureDriver.cs -- 挂在场景里的**帧泵**：等一帧画完 → 拍下来 → 交给面板写盘
//
// 为什么必须有这个运行时组件，而不是编辑器窗口自己每帧拍：
//   `ScreenCapture.*` 只在**播放模式**下有意义，而且官方文档明确要求
//   "一定要等这一帧渲染完再拍 —— 用协程 yield WaitForEndOfFrame 来保证"。
//   编辑器窗口的 OnGUI / EditorApplication.update 都不在那个时间点上：
//     · OnGUI 早于这一帧的渲染；
//     · EditorApplication.update 在帧与帧之间，拿到的是上一帧，
//       而且"这一帧到底画没画"是编辑器说了算，不是我们说了算。
//   所以照 Unity Recorder 的做法：往场景里塞一个 MonoBehaviour，用
//   `yield return new WaitForEndOfFrame()` 卡在渲染结束之后拍。
//
// 还有一个**必须**在这里做的事：读像素。
//   `RenderTexture.active` 是全局状态，录完必须还原（见 ReadbackPixels 的 finally）。
//
// 组件是运行时创建的：`hideFlags = HideFlags.DontSave` + `DontDestroyOnLoad`，
// 所以它**不会**弄脏场景、不会存进场景文件、也不会在切场景时被销毁。
// 录制结束由 <see cref="HoQuickCaptureEngine"/> 负责销毁它。
using System;
using System.Collections;
using UnityEngine;

namespace Hollow.HoUnityTools.Runtime.QuickCapture
{
    /// <summary>
    /// 抓一帧所需的一切。由引擎在开始录制前配好，驱动只读不写。
    /// </summary>
    public sealed class HoQuickCapturePlan
    {
        /// <summary>出图宽。</summary>
        public int Width;

        /// <summary>出图高。</summary>
        public int Height;

        /// <summary>录制帧率，用来写 <c>Time.captureDeltaTime</c>。</summary>
        public float FrameRate = 30f;

        /// <summary>一共要出多少帧。</summary>
        public int TargetFrameCount = 1;

        /// <summary>每帧要写到的绝对路径（由引擎按帧号拼好，驱动只管往里丢图）。</summary>
        public string Folder;

        /// <summary>文件名前缀（不含帧号与扩展名）。</summary>
        public string FilePrefix = "HoCapture";

        /// <summary>PNG 编码质量 0~100。</summary>
        public int PngQuality = 50;

        /// <summary>是不是"只拍一张"（截图模式）。</summary>
        public bool IsSingleShot;

        /// <summary>
        /// 这一帧要不要落成一个图片文件。
        /// **视频模式（MP4）下是 false** —— 帧直接交给编码器，磁盘上只有那一个视频文件。
        /// </summary>
        public bool WritesImageFiles = true;

        /// <summary>落盘用的图片格式（<see cref="WritesImageFiles"/> 为 false 时不看）。</summary>
        public HoQuickCaptureImageFormat ImageFormat = HoQuickCaptureImageFormat.Png;

        /// <summary>画面从哪来：游戏视图合成结果（默认）/ 指定相机渲进 RT（可透明）。</summary>
        public HoQuickCaptureRenderSource RenderSource = HoQuickCaptureRenderSource.GameView;

        /// <summary>
        /// <see cref="HoQuickCaptureRenderSource.Camera"/> 时要渲的那台相机。
        /// 为空时会退回"自动挑一台"（主摄像机），并给出说明。
        /// </summary>
        public Camera SourceCamera;

        /// <summary>
        /// 渲进 RT 时是否临时把相机改成"透明背景"（Clear Flags = Solid Color + 背景 alpha = 0）。
        /// 只对 <see cref="HoQuickCaptureRenderSource.Camera"/> 有意义，拍完会**还原**。
        ///
        /// 透明能不能真的出得来还取决于渲染管线：URP 要勾上 URP Asset 里的
        /// `Allow Post Process Alpha Output`（否则后处理会把 alpha 写回 1）。
        /// </summary>
        public bool ForceTransparentBackground;
    }
    /// <summary>
    /// 帧泵。挂在隐藏的运行时物体上，驱动整段抓取。
    /// </summary>
    [AddComponentMenu("")]
    public sealed class HoQuickCaptureDriver : MonoBehaviour
    {
        private HoQuickCapturePlan plan;

        /// <summary>每攒够一帧（或攒失败）调一次，参数是刚出炉的帧。主线程调用。</summary>
        private Action<HoQuickCapturedFrame> onFrameCaptured;

        private RenderTexture target;

        /// <summary>录制被中途掐断（退出播放 / 关面板）时置位，循环据此收摊。</summary>
        private bool abortRequested;

        /// <summary>暂停：模拟冻结，但**不**抓帧。</summary>
        private bool pauseRequested;

        /// <summary>进暂停之前的 <c>Time.timeScale</c>，恢复时还回去。</summary>
        private float timeScaleBeforePause;

        private int capturedCount;

        /// <summary>录制期间被我们改写过的 <c>Time.captureDeltaTime</c> 原值，收尾时还原。</summary>
        private float originalCaptureDeltaTime;

        /// <summary>开始录制时的 <c>Time.timeScale</c>（正常是 1）。</summary>
        private float originalTimeScale = 1f;

        private bool captureDeltaTimeOwned;

        /// <summary>已经抓了多少帧（成功的）。面板画进度条用。</summary>
        public int CapturedCount => capturedCount;

        /// <summary>
        /// 最近一次抓帧失败的原因；成功一次就清空。
        /// 失败帧**不**走 <c>onFrameCaptured</c>（那样会占掉一个帧号、在序列里留洞），
        /// 所以失败信息靠这个属性传给引擎，由引擎的看门狗顺手读走。
        /// </summary>
        public string LastFailure { get; private set; }

        /// <summary>
        /// 这一帧是**什么时候**抓到的，用 `Time.realtimeSinceStartup`（Unity 运行时时钟）。
        ///
        /// 引擎拿它当"帧泵还活着吗"的心跳。为什么要这个心跳：
        /// `yield return new WaitForEndOfFrame()` 在**游戏视图看不见**的时候可能永远不返回
        /// （Unity Recorder 的文档里明确写了这个坑：游戏视图被场景视图挡住时
        /// "Unity can't reach WaitForEndOfFrame，录制会一直等下去，而模拟照常前进"）。
        /// 那种情况下我们既不报错也不出图，用户只会看到面板卡住 —— 所以必须自己发现并说清楚。
        ///
        /// ⚠️ **这个钟必须和引擎那边判定用的钟是同一个。**
        /// 这里能安全地用运行时时钟，是因为它**只在播放模式下被测**（抓帧本来就必须在播放模式），
        /// 一次播放会话内它不会归零。早先两边用了不同的钟（一个运行时、一个编辑器），
        /// 一相减就是几千秒，症状是**每段录制都在 8 秒左右被误判成"没在渲染"并砍断**。
        /// 改这里就要**同时改引擎的 OnEditorUpdate**。
        ///
        /// 顺带：不带 `UnityEditor` 依赖，这个类才能在运行时程序集里干净地编译。
        /// </summary>
        public double LastFrameRealtime { get; private set; }

        /// <summary>
        /// 抓完之后回调一次（正常录满 / 被停 / 出错都算）。**由引擎传进来。**
        ///
        /// 为什么不直接调引擎：帧泵在**运行时程序集**里，而引擎在 Editor 程序集里，
        /// 依赖方向只能是 Editor → Runtime。所以这里反过来收一个回调 ——
        /// 帧泵不认识引擎，是引擎认识帧泵。
        /// </summary>
        private Action onFinished;

        /// <summary>
        /// 起一段抓取。由 <see cref="HoQuickCaptureEngine"/> 调用。
        /// </summary>
        /// <param name="capturePlan">这一段的参数。</param>
        /// <param name="frameCallback">每抓到一帧调一次。</param>
        /// <param name="finishedCallback">整段结束时调一次（引擎据此收尾）。</param>
        public void Begin(
            HoQuickCapturePlan capturePlan,
            Action<HoQuickCapturedFrame> frameCallback,
            Action finishedCallback)
        {
            plan = capturePlan;
            onFrameCaptured = frameCallback;
            onFinished = finishedCallback;
            capturedCount = 0;
            abortRequested = false;
            pauseRequested = false;
            LastFrameRealtime = Time.realtimeSinceStartup;

            originalTimeScale = Time.timeScale;
            originalCaptureDeltaTime = Time.captureDeltaTime;

            // 录制期间把"游戏时间"从真实时间上摘下来：
            // 每画一帧，Time.time 前进 1/帧率，跟这一帧实际画了多久无关。
            // 这就是"不要真实时间"（离线渲染）的实现点 —— 机器慢也不会丢帧、不会变慢动作。
            if (!plan.IsSingleShot && plan.FrameRate > 0f)
            {
                Time.captureDeltaTime = 1f / plan.FrameRate;
                captureDeltaTimeOwned = true;
            }

            StartCoroutine(Capture());
        }

        /// <summary>请求收摊：当前这一帧拍完就退出循环。</summary>
        public void Abort()
        {
            abortRequested = true;
        }

        public void SetPaused(bool paused)
        {
            if (pauseRequested == paused)
            {
                return;
            }

            pauseRequested = paused;
            if (paused)
            {
                timeScaleBeforePause = Time.timeScale;
                Time.timeScale = 0f;
            }
            else
            {
                Time.timeScale = timeScaleBeforePause;
            }
        }

        private void OnDisable()
        {
            // 物体被销毁 / 组件被禁用时兜底：借走的东西一定要还。
            RestoreGlobalState();
        }

        private void OnDestroy()
        {
            RestoreGlobalState();
            ReleaseTarget();
        }

        /// <summary>
        /// 放掉抓帧缓冲。
        ///
        /// 必须显式放：它是 `new RenderTexture(...)` 来的，还带着 `HideFlags.HideAndDontSave`，
        /// 于是 `Resources.UnloadUnusedAssets` **不会**回收它 —— 不放就是每次会话漏一张
        /// 出图分辨率大小的显存（4K 那一张就是几十 MB）。
        /// </summary>
        private void ReleaseTarget()
        {
            if (target == null)
            {
                return;
            }

            target.Release();
            Destroy(target);
            target = null;
        }

        /// <summary>
        /// 把 <c>Time.captureDeltaTime</c> 与 <c>Time.timeScale</c> 还原。
        /// 幂等 —— OnDisable 与 OnDestroy 都会调，还两次没关系。
        /// </summary>
        private void RestoreGlobalState()
        {
            if (captureDeltaTimeOwned)
            {
                captureDeltaTimeOwned = false;
                Time.captureDeltaTime = originalCaptureDeltaTime;
            }

            if (pauseRequested)
            {
                pauseRequested = false;
                Time.timeScale = timeScaleBeforePause;
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 主循环
        // ══════════════════════════════════════════════════════════════

        private IEnumerator Capture()
        {
            EnsureTarget();

            // 刚改过游戏视图分辨率 / 刚进播放的头一帧，画面可能还是空的，
            // 所以先空等一帧再开始拍（和 Unity Recorder 的 REC-589 处理同一个道理）。
            yield return new WaitForEndOfFrame();

            // ⚠️ try/finally 不是可选的：
            // 循环体里任何一句抛出去（ScreenCapture 出错、回调里 Path.Combine 出错、
            // 面板那边抛异常……），协程就直接结束了 —— 后面的 RestoreGlobalState 与
            // NotifyDriverFinished 再也不会跑到。症状是**整个播放会话**被留在
            // `Time.captureDeltaTime = 1/帧率`（甚至 `timeScale = 0`）上，而且引擎一直是
            // "正在录制"、面板卡死没有任何出口。finally 是这条路上唯一的保证。
            try
            {
                while (!abortRequested)
                {
                    // 暂停时**不抓帧，但继续每帧转一圈**：这样"解除暂停"能在下一帧立刻接上。
                    //
                    // 这里只认 `pauseRequested`，**不认 `Time.timeScale <= 0`**：
                    // 游戏自己把 timeScale 设成 0（暂停菜单、命中停顿）是**合法状态**，
                    // 不该被当成"我们暂停了"。真要是它一直不恢复，看门狗会以
                    // "没有帧在渲染"报出来，不用在这里猜。
                    if (pauseRequested)
                    {
                        yield return new WaitForEndOfFrame();
                        continue;
                    }

                    yield return new WaitForEndOfFrame();

                    HoQuickCapturedFrame frame;
                    string failure;
                    if (!CaptureNow(out frame, out failure))
                    {
                        // 抓帧失败：**不吃帧号、不占目标帧数**，循环直接重试。
                        //
                        // 为什么不把它当成"一帧"交上去：那样失败一次就在序列里留一个空洞
                        // （0000 / 0002 / 0003…），`ffmpeg -i prefix_%06d.png` 到空洞就断，
                        // 用户拿到一段被截断的视频而且不知道为什么。
                        // 宁可多等一帧重试，也不要交出一个断掉的序列。
                        LastFailure = failure;
                        LastFrameRealtime = Time.realtimeSinceStartup;
                        continue;
                    }

                    LastFailure = null;
                    capturedCount++;
                    LastFrameRealtime = Time.realtimeSinceStartup;
                    if (onFrameCaptured != null)
                    {
                        onFrameCaptured(frame);
                    }

                    if (plan.IsSingleShot || capturedCount >= plan.TargetFrameCount)
                    {
                        break;
                    }
                }
            }
            finally
            {
                RestoreGlobalState();
                if (onFinished != null)
                {
                    onFinished();
                }
            }
        }

        /// <summary>
        /// 这一帧该叫什么名字。
        ///
        /// 单张截图用**拍摄时刻**：连拍几张不会互相覆盖，也不会出现 `_0000` 这种
        /// 让人以为是序列的名字。序列帧才用补零的帧号 —— 那个是为了让
        /// ffmpeg / AE 能按顺序吃进去。
        ///
        /// 视频模式（MP4）下帧是交给编码器的、不落盘，文件名只用来做提示，
        /// 所以直接给 null，免得面板上显示一个根本不存在的文件。
        ///
        /// `public static` 是为了让**编辑模式帧泵**（HoQuickCaptureEditModePump）
        /// 用同一套命名 —— 两条路各写一份名字规则，迟早会不一致。
        /// </summary>
        public static string BuildFrameFileName(HoQuickCapturePlan forPlan, int frameIndex)
        {
            if (forPlan == null || !forPlan.WritesImageFiles)
            {
                return null;
            }

            string extension = HoQuickCaptureNaming.ExtensionFor(forPlan.ImageFormat);
            return forPlan.IsSingleShot
                ? HoQuickCaptureNaming.BuildScreenshotFileName(forPlan.FilePrefix, DateTime.Now, extension)
                : HoQuickCaptureNaming.BuildFileName(forPlan.FilePrefix, frameIndex, extension);
        }

        private string BuildFrameFileName()
        {
            return BuildFrameFileName(plan, capturedCount);
        }

        private void EnsureTarget()
        {
            int width = Mathf.Max(1, plan.Width);
            int height = Mathf.Max(1, plan.Height);

            if (target != null && (target.width != width || target.height != height))
            {
                target.Release();
                Destroy(target);
                target = null;
            }

            if (target == null)
            {
                // ARGB32：和 Unity Recorder 给游戏视图抓帧用的格式一致。
                // depth 0 = 不要深度缓冲，我们只取颜色。
                target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
                {
                    name = "HoQuickCaptureTarget",
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                target.Create();
            }
        }

        /// <summary>
        /// 把像素数组的行序上下翻过来（原地，只换行不换列）。
        /// 见 <see cref="ReadbackPixels"/> 里关于"为什么必须翻"的说明。
        /// </summary>
        internal static void FlipRowsInPlace(Color32[] pixels, int width, int height)
        {
            if (pixels == null || width <= 0 || height <= 1)
            {
                return;
            }

            int stride = width;
            Color32[] scratch = null;
            for (int y = 0; y < height / 2; y++)
            {
                int top = y * stride;
                int bottom = (height - 1 - y) * stride;

                if (scratch == null)
                {
                    scratch = new Color32[stride];
                }

                Array.Copy(pixels, top, scratch, 0, stride);
                Array.Copy(pixels, bottom, pixels, top, stride);
                Array.Copy(scratch, 0, pixels, bottom, stride);
            }
        }

        /// <summary>
        /// 真抓一帧：把游戏视图的合成结果拍进 <see cref="target"/>，再读回 CPU。
        /// </summary>
        private bool CaptureNow(out HoQuickCapturedFrame frame, out string failure)
        {
            frame = null;
            failure = null;

            if (target == null)
            {
                failure = "抓帧缓冲没了（RenderTexture 被释放）。";
                return false;
            }

            // ① 拍进 RenderTexture。
            //    这是 **Unity Recorder 抓游戏视图用的同一个调用**：拿到的是"最终呈现给用户的那一帧"
            //    （多相机合成 + 后处理 + UI 都在里面），不是某个相机的裸渲染。
            ScreenCapture.CaptureScreenshotIntoRenderTexture(target);

            // ② 从 RenderTexture 造出一帧（行序、翻转、纹理都在那一个方法里）。
            //    这条是**游戏视图抓屏**，实测原始数据是顶边在前，所以不做额外翻转。
            if (!TryBuildFrameFrom(target, sourceIsFlipped: false, out frame, out failure))
            {
                return false;
            }

            // 序号与文件名由这里盖上：`TryBuildFrameFrom` 是两条路共用的，
            // 不该知道"这是第几帧、该叫什么"。
            frame.Index = capturedCount;
            frame.FileName = BuildFrameFileName();
            return true;
        }

        /// <summary>
        /// 把一个 RenderTexture 的内容做成一帧（读回 CPU、摆好行序、建纹理）。
        ///
        /// 抽出来是因为**两条路都要用同一套**（播放模式的帧泵 / 编辑模式帧泵 / 相机直渲）：
        /// 区别只在"怎么把画面弄进 RenderTexture"，之后的处理必须完全一致。
        /// </summary>
        /// <param name="source">已经画好的 RenderTexture。</param>
        /// <param name="sourceIsFlipped">
        /// 这个 source 的行序**与游戏视图那条路相反**吗？
        ///
        /// 游戏视图那条（`ScreenCapture.CaptureScreenshotIntoRenderTexture`）实测原始数据是
        /// **顶边在前**（见下面的长注释）。而**相机渲进 RT 那条是反过来的（底边在前）** ——
        /// 这是用户实拍报回来的：同一套翻转逻辑，游戏视图出来是正的，相机那条上下颠倒。
        ///
        /// 所以这里用**异或**来决定到底翻不翻：`sourceIsFlipped` 与下面那条"原始是顶边在前"
        /// 的结论一异或，两条路各自都对，而且**每条路仍然只翻一次**。
        /// </param>
        internal static bool TryBuildFrameFrom(
            RenderTexture source,
            bool sourceIsFlipped,
            out HoQuickCapturedFrame frame,
            out string failure)
        {
            frame = null;
            failure = null;

            // 读回 CPU。**这里不翻** —— 两份行序统一在下面算，避免"这里翻一次、那里再翻一次"又抵消。
            Color32[] raw = ReadbackPixels(source);
            if (raw == null)
            {
                failure = "读回像素失败（RenderTexture 还没画完？）。";
                return false;
            }

            // ── 行序：实测结论（2026-09-30，Windows / D3D11）──
            //
            // **`ReadPixels` + `GetPixels32` 交回来的原始数据是「第一行 = 图像顶边」。**
            //
            // 这条是**试出来的，不是推出来的** —— Unity 没写清这一步的行序，而
            // `Graphics.CopyTexture` 的文档偏偏说 RenderTexture 的原点在左下、Texture2D 的在左上，
            // 两个原点不同，所以光看文档推不出结论。当时按"底边在前"实现，出图整个上下颠倒；
            // 翻过来就正了。
            //
            // ⚠️ **这条结论只对"游戏视图抓屏"那个 RT 成立。相机自己渲出来的 RT 是反的**
            //   （同样实测：相机那条路上下颠倒）。由 sourceIsFlipped 传进来区分。
            //
            // ⚠️ 换机器 / 换图形后端（D3D12 / Vulkan / OpenGL）时如果发现出图又倒了，
            //    那说明这一格因后端而异 —— 到时候在这里加回一个开关，
            //    别再一次次靠猜改代码（这个坑连踩过三次了）。
            //
            // 于是两份各造一份，**只翻一次**：
            //   · topDown  —— 图片编码要的（编码器按数组顺序当扫描行往下写）；
            //   · bottomUp —— 纹理 / 视频要的（Unity 的纹理行序）。
            bool rawIsTopDown = !sourceIsFlipped;

            Color32[] topDown;
            Color32[] bottomUp;
            if (rawIsTopDown)
            {
                topDown = raw;
                bottomUp = new Color32[raw.Length];
                Array.Copy(raw, bottomUp, raw.Length);
                FlipRowsInPlace(bottomUp, source.width, source.height);
            }
            else
            {
                bottomUp = raw;
                topDown = new Color32[raw.Length];
                Array.Copy(raw, topDown, raw.Length);
                FlipRowsInPlace(topDown, source.width, source.height);
            }

            var texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };

            try
            {
                // 纹理用"底边在前"那份建：`SetPixels32` 是 Unity 的纹理行序，
                // 而缩略图与视频都读这张纹理。
                texture.SetPixels32(bottomUp);
                texture.Apply(false, false);
            }
            catch (Exception exception)
            {
                // 半成品贴图不能留下：失败帧身上不带 Texture，就没人会去销毁它了。
                UnityEngine.Object.DestroyImmediate(texture);
                failure = "把像素写进贴图失败：" + exception.Message;
                return false;
            }

            frame = new HoQuickCapturedFrame
            {
                Texture = texture,
                PixelData = topDown,
                BottomUp = bottomUp,
                Width = source.width,
                Height = source.height,
                GameTime = Time.time,
            };
            return true;
        }

        /// <summary>
        /// 把 RenderTexture 读成 CPU 端的原始 <see cref="Color32"/> 数组。**不做任何翻转。**
        ///
        /// 用同步的 <c>ReadPixels</c> 而不是 <c>AsyncGPUReadback</c>：这里是离线渲染，
        /// 一个固定步长的帧**没有任何理由**比机器最快能画的速度更早到来，
        /// 所以主线程停顿一下换个"拿到手就是这一帧"的确定性，是划算的。
        /// （Unity Recorder 两条路都有；它异步是因为它还要照顾实时录制。）
        ///
        /// ⚠️ **行序在这里不做假设**：Unity 没说清 `ReadPixels` + `GetPixels32` 拼出来是
        /// 顶边在前还是底边在前。所以这里只负责"把原始数据交出去"，
        /// 由 <see cref="CaptureNow"/> 按**实测结论**摆成两份（见那里的说明）——
        /// 这样也保证**整个流程只翻一次**，不会两条路各翻一次又抵消。
        /// </summary>
        private static Color32[] ReadbackPixels(RenderTexture source)
        {
            RenderTexture previous = RenderTexture.active;
            Texture2D staging = null;
            try
            {
                RenderTexture.active = source;
                staging = new Texture2D(
                    source.width,
                    source.height,
                    TextureFormat.RGBA32,
                    false,
                    false);
                staging.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0, false);
                staging.Apply(false, false);

                return staging.GetPixels32();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[快速渲染] 读回像素出错：" + exception.Message);
                return null;
            }
            finally
            {
                // RenderTexture.active 是全局状态，不还原的话后面所有 Graphics.Blit / 相机渲染都会写错地方。
                RenderTexture.active = previous;
                if (staging != null)
                {
                    Destroy(staging);
                }
            }
        }
    }

    /// <summary>刚抓下来、还没落盘的一帧。</summary>
    public sealed class HoQuickCapturedFrame
    {
        /// <summary>在本次会话里的顺序号（从 0 开始）。文件名按它排。</summary>
        public int Index;

        /// <summary>文件名（含扩展名）。</summary>
        public string FileName;

        /// <summary>
        /// 像素，**行序是"第一行 = 图像顶边"**（我们统一成这个约定，见下面两个访问器）。
        /// </summary>
        public Color32[] PixelData;

        /// <summary>这张图本身，给「高级」里的缩略图用。交出去之后由引擎负责销毁。</summary>
        public Texture2D Texture;

        public int Width;
        public int Height;

        /// <summary>抓这一帧时的游戏时间（<c>Time.time</c>），录制时长就按它算。</summary>
        public float GameTime;

        /// <summary>非空 = 这一帧没抓成。</summary>
        public string Error;

        /// <summary>
        /// 同一帧像素的**底边在前**版本（Unity 的纹理行序）。抓帧时一次性算好，见 CaptureNow。
        ///
        /// 于是这一帧上**两种行序各有一份，各有明确用途**：
        ///   · <see cref="PixelData"/> 顶边在前 —— 图片编码按数组顺序写扫描行，要顶边在前；
        ///   · <see cref="BottomUp"/> 底边在前 —— `Texture2D` / `MediaEncoder` 走的是纹理行序。
        /// 谁用哪一份，看下面的访问器名字。
        /// </summary>
        public Color32[] BottomUp;

        /// <summary>
        /// 给**图片编码**用的像素：**第一行 = 图像顶边**。
        ///
        /// `EncodeToPNG / EncodeToJPG / EncodeToEXR` 是按数组顺序当扫描行往下写的，
        /// 所以喂给它们的数据必须顶边在前，否则存出来就是上下颠倒的。
        /// </summary>
        public Color32[] GetTopDownPixels()
        {
            return PixelData;
        }

        /// <summary>
        /// 给**纹理 / 视频编码**用的像素：**第一行 = 图像底边**（Unity 的约定）。
        /// `Texture2D.SetPixels32` 与 `MediaEncoder.AddFrame(Texture2D, …)` 都是这个约定。
        /// </summary>
        public Color32[] GetBottomUpPixels()
        {
            return BottomUp;
        }
    }
}
