// HoQuickCaptureEngine.cs -- 快速渲染的大脑：什么时候能拍、拍到哪了、东西借了怎么还
//
// 这一层不加界面，只管三件事：
//   1) **准入检查**：没在播放模式、目录写不进去、正在录 —— 当场拒绝并给出理由，
//      而不是让它跑到一半才炸。
//   2) **落盘**：帧从运行时组件手里接过来就写 PNG。写盘放在这里（编辑器侧）而不是
//      运行时组件里，是因为"往磁盘写"这件事本来就是编辑器的活；顺带也让面板能统计进度。
//   3) **有借有还**：`Time.captureDeltaTime`、`Time.timeScale`、游戏视图分辨率、
//      临时物体 —— 借出去的四个东西，在**任何**收尾路径上都要还回去：
//      正常录完 / 手动停 / 退出播放 / 关面板 / 脚本重编译 / 域重载。
//      "借了不还"的具体症状：工程被留在慢动作上、或者游戏视图分辨率被改成一个没人认识的尺寸。
//
// 一条硬约束：**必须在播放模式**。原因见 HoQuickCaptureDriver 顶部 —— 抓帧要
// `WaitForEndOfFrame`，而那只在播放模式下有帧循环可等。
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Hollow.HoUnityTools.Runtime.QuickCapture;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Hollow.HoUnityTools.Editor.QuickCapture
{
    /// <summary>引擎现在在干嘛。</summary>
    public enum HoQuickCaptureState
    {
        /// <summary>闲着。</summary>
        Idle,

        /// <summary>正在拍一张截图。</summary>
        Screenshot,

        /// <summary>正在录制。</summary>
        Recording,

        /// <summary>录制中，但暂停了（模拟冻结，画面还在画）。</summary>
        Paused,
    }

    /// <summary>面板读的实时状态快照。</summary>
    public struct HoQuickCaptureStatus
    {
        public HoQuickCaptureState State;

        /// <summary>本次会话一共要出多少帧。</summary>
        public int TargetFrames;

        /// <summary>已经写盘多少帧。</summary>
        public int WrittenFrames;

        /// <summary>已经抓到多少帧（含写盘失败的）。</summary>
        public int CapturedFrames;

        /// <summary>这一帧抓到时的游戏时间（秒），用来显示"录到第几秒了"。</summary>
        public float GameTime;

        /// <summary>出图宽高。</summary>
        public int Width;

        public int Height;

        /// <summary>本次会话的落盘目录（绝对路径）。</summary>
        public string Folder;

        /// <summary>最后写出去的那个文件（绝对路径）。</summary>
        public string LastFile;

        /// <summary>本次会话第一帧的名字，给面板显示"输出长这样"。</summary>
        public string FirstFile;

        /// <summary>本次会话的**游戏时间**总长（= 帧数 / 帧率）。不是墙上时间。</summary>
        public float TotalDuration;

        /// <summary>出错信息；非空时面板画红的。</summary>
        public string Error;

        /// <summary>警告信息（没失败，但用户该知道）。</summary>
        public string Warning;

        /// <summary>
        /// TAA 预热进行中。为 true 时面板要替掉录制那一行画一条进度，
        /// 否则 16 帧静默地等看起来就是卡死（用户报过：「预热没进度条显示搞得我以为卡了」）。
        /// </summary>
        public bool WarmupActive;

        /// <summary>预热已经喂了多少帧。</summary>
        public int WarmupDone;

        /// <summary>预热一共要多少帧。</summary>
        public int WarmupTotal;

        public bool IsBusy => State != HoQuickCaptureState.Idle;

        public bool CanPause => State == HoQuickCaptureState.Recording || State == HoQuickCaptureState.Paused;

        /// <summary>进度 0~1（没有目标帧数时返回 0）。</summary>
        public float Progress
        {
            get
            {
                if (TargetFrames <= 0)
                {
                    return 0f;
                }

                return Mathf.Clamp01((float)WrittenFrames / TargetFrames);
            }
        }
    }

    /// <summary>抓取会话的引擎（静态状态机）。</summary>
    [InitializeOnLoad]
    internal static class HoQuickCaptureEngine
    {
        /// <summary>临时物体名。用 Ho 前缀，方便在 Hierarchy 里一眼认出是谁留下的。</summary>
        private const string DriverObjectName = "Ho-QuickCapture";

        /// <summary>缩略图长边的像素数。</summary>
        private const int PreviewLongSide = 256;

        /// <summary>
        /// 帧泵多久没出帧就判定"画面根本没在画"（墙上时间，秒）。
        ///
        /// 这个阈值只用于**报错**，所以宁可定得宽：离线渲染一帧要多久取决于工程
        /// （4K + 光线追踪那种一秒一帧都不奇怪），定 3 秒会把正常录制误判成卡死。
        /// 8 秒没出过任何一帧，基本只可能是游戏视图根本没在渲染。
        /// </summary>
        private const double StallTimeoutSeconds = 8.0;

        /// <summary>开始录制后先给这么久再开始算停摆（场景加载 / 转场那一下可能卡好几秒）。</summary>
        private const double StallGraceSeconds = 3.0;

        private static HoQuickCaptureState state = HoQuickCaptureState.Idle;
        private static HoQuickCapturePlan plan;
        private static HoQuickCaptureDriver driver;
        private static GameObject driverObject;

        private static int targetFrames;
        private static int writtenFrames;
        private static int capturedFrames;
        private static string sessionFolder;
        private static string firstFile;
        private static string lastFile;
        private static string error;
        private static string warning;
        private static float gameTime;

        private static int restoreGameViewWidth;
        private static int restoreGameViewHeight;
        private static bool gameViewResolutionOwned;
        private static bool abortAfterCurrentFrame;
        private static double sessionStartRealtime;
        private static bool stallReported;
        private static float expectedStep;
        private static float previousGameTime;
        private static bool clockConflictReported;
        private static Texture2D preview;
        private static Color32[] previewPixels;

        /// <summary>视频模式下的编码器（PNG 序列时是 null）。</summary>
        private static HoQuickCaptureMediaEncoder videoEncoder;

        /// <summary>视频模式下的目标文件（PNG 序列时是 null）。</summary>
        private static string videoOutputPath;

        /// <summary>当前这次会话是不是走**编辑模式帧泵**（而不是播放模式的 MonoBehaviour）。</summary>
        private static bool editModePump;

        /// <summary>快照变了（进帧、写盘、状态切换、出错）。面板订阅它来重画。</summary>
        public static event Action Changed;

        static HoQuickCaptureEngine()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += OnEditorUpdate;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        // ══════════════════════════════════════════════════════════════
        // 对外状态
        // ══════════════════════════════════════════════════════════════

        public static bool IsBusy => state != HoQuickCaptureState.Idle;

        public static bool IsRecording =>
            state == HoQuickCaptureState.Recording || state == HoQuickCaptureState.Paused;

        public static bool IsPaused => state == HoQuickCaptureState.Paused;

        /// <summary>缩略图（可能为 null）。面板只读，不要销毁它。</summary>
        public static Texture2D Preview => preview;

        public static HoQuickCaptureStatus GetStatus()
        {
            return new HoQuickCaptureStatus
            {
                State = state,
                TargetFrames = targetFrames,
                WrittenFrames = writtenFrames,
                CapturedFrames = capturedFrames,
                GameTime = gameTime,
                Width = plan == null ? 0 : plan.Width,
                Height = plan == null ? 0 : plan.Height,
                Folder = sessionFolder,
                FirstFile = firstFile,
                LastFile = lastFile,
                TotalDuration = plan == null ? 0f : plan.TargetFrameCount / Mathf.Max(1f, plan.FrameRate),
                Error = error,
                Warning = warning,
                WarmupActive = HoQuickCaptureCameraRenderer.WarmupActive,
                WarmupDone = HoQuickCaptureCameraRenderer.WarmupDone,
                WarmupTotal = HoQuickCaptureCameraRenderer.WarmupTotal,
            };
        }

        /// <summary>
        /// 截帧的准入检查。返回 null 表示可以。**界面靠它决定按钮灰不灰，所以不能有副作用。**
        ///
        /// ⚠️ **只在播放模式下允许** —— 这一条是用户明确要求的，别再放开：
        /// 「你还是别允许非 play 模式抓帧吧，bg 太多了」。
        ///
        /// 编辑模式曾经做过（走 <see cref="HoQuickCaptureEditModePump"/> 自己催帧再抓），
        /// 但那条路为了对齐"游戏视图到底哪一刻画完"要处理一堆时序分支：
        /// `RepaintAllViews` + `QueuePlayerLoopUpdate` + 等 tick / 等渲染回调、
        /// 游戏视图最小化或被挡住时催不出来、抓到的到底是游戏视图还是编辑器界面……
        /// **每一个分支都是一个 bug**，而收益只是"不用按 Play"。
        /// 砍掉之后，截帧和录制走**同一条**帧泵（`WaitForEndOfFrame`），时序只有一个真相。
        ///
        /// 相机直渲（要透明背景时走的那条）在播放模式下同样走编辑模式帧泵 ——
        /// 那是引擎内的确定性路径，与"编辑模式抓游戏视图"无关，不受这条限制影响。
        /// </summary>
        public static string CheckCanStart()
        {
            string blocked = CheckCommon();
            if (blocked != null)
            {
                return blocked;
            }

            if (!Application.isPlaying)
            {
                return "截帧和录制都要在播放模式下：编辑模式里没有正在推进的游戏帧，"
                    + "抓到的画面不可靠。先按 Play。";
            }

            // 播放模式独有的一条：`Time.captureDeltaTime` 是**被 `Time.timeScale` 缩放**的
            // （Unity 文档："Time.time advances at an interval of captureDeltaTime,
            // **scaled by Time.timeScale**"）。所以进录制时 timeScale 必须是 1，
            // 否则"离线步进"的步长就不等于 1/帧率：
            //   · timeScale = 0（游戏自己的暂停菜单）→ 游戏时间根本不动，一帧都拍不出来；
            //   · timeScale = 0.5（慢动作）→ 每帧只前进半个步长，步长校验会把它当成"时钟被抢"。
            if (Application.isPlaying && !Mathf.Approximately(Time.timeScale, 1f))
            {
                return Time.timeScale <= 0f
                    ? "现在 Time.timeScale = 0（游戏自己暂停了？）：游戏时间不往前走，拍不出东西。先让它恢复。"
                    : "现在 Time.timeScale = " + Time.timeScale.ToString("0.###")
                      + "（不是 1）：离线步进按 timeScale 缩放，步长会对不上。先把它设回 1。";
            }

            return null;
        }

        /// <summary>
        /// 录制的准入检查。返回 null 表示可以。
        ///
        /// 以前这里还要单独拦一次"不在播放模式"，现在 <see cref="CheckCanStart"/> 已经
        /// 把播放模式作为共同前置条件了（截帧也一样只能在播放模式），所以这里不用再判一遍。
        /// </summary>
        public static string CheckCanRecord()
        {
            return CheckCanStart();
        }

        /// <summary>两种动作共用的那几条前置条件。</summary>
        private static string CheckCommon()
        {
            if (IsBusy)
            {
                return "正在" + (IsRecording ? "录制" : "截图") + "，等它结束。";
            }

            // `-batchmode` 下**根本没有渲染管线**，而且 `WaitForEndOfFrame` 官方文档写明
            // "批处理模式下不会运行"。于是帧泵永远等不到那一帧：既不报错也不出图。
            // Unity Recorder 自己的 KnownIssues 里就写着这条（"recording never starts"）。
            // 与其让它挂在那儿，不如一眼挡住 —— 批处理/CI 里也没人来点这个按钮，纯粹是防呆。
            if (InternalEditorUtility.inBatchMode)
            {
                return "批处理模式（-batchmode）下没有渲染管线，抓不了帧。要拍就开正常的编辑器。";
            }

            if (EditorApplication.isPaused)
            {
                return "Unity 编辑器自己处于暂停状态（工具栏那个暂停）：先取消暂停，画面才在画。";
            }

            return null;
        }

        // ══════════════════════════════════════════════════════════════
        // 入口：截图
        // ══════════════════════════════════════════════════════════════

        /// <summary>拍一张当前游戏视图，落到输出目录。返回是否开拍了。</summary>
        public static bool TakeScreenshot()
        {
            string blocked = CheckCanStart();
            if (blocked != null)
            {
                SetError(blocked);
                return false;
            }

            HoQuickCaptureSettingsData data = HoQuickCaptureSettings.Data;
            string folderError;
            string folder = HoQuickCaptureSettings.ResolveOutputFolder(out folderError);
            if (folder == null)
            {
                SetError(folderError);
                return false;
            }

            // 分辨率取不到就**不能开拍**：`GetRenderSize` 失败时给的是 1x1 占位值，
            // 不看返回值的话会一路拍出 1x1 的 PNG，而且全程不报错。
            if (!data.ResolveOutputSize(out int width, out int height))
            {
                SetError("读不到游戏视图的分辨率（游戏视图窗口没开着？）。把 Game 视图打开再拍，或者在「分辨率」里切「自定义」。");
                return false;
            }

            // 要透明却选了 jpg：jpg 根本没有 alpha 通道，选了也是白搭。当场说清楚，
            // 别让用户拿到一张"看着正常但背景是黑/白"的图去猜。
            if (data.renderSource == HoQuickCaptureRenderSource.Camera
                && data.transparentBackground
                && data.imageFormat == HoQuickCaptureImageFormat.Jpg)
            {
                SetError("要透明背景就不能存 jpg —— jpg 没有 alpha 通道。"
                    + "把格式换成 png（或 exr）再拍。");
                return false;
            }

            var newPlan = new HoQuickCapturePlan
            {
                Width = width,
                Height = height,
                FrameRate = Mathf.Max(1f, data.frameRate),
                TargetFrameCount = 1,
                Folder = folder,
                FilePrefix = data.filePrefix,
                PngQuality = data.pngQuality,
                IsSingleShot = true,
                WritesImageFiles = true,
                ImageFormat = data.imageFormat,
                RenderSource = data.renderSource,
                SourceCamera = data.sourceCamera,
                ForceTransparentBackground = data.transparentBackground,
            };

            BeginSession(newPlan, HoQuickCaptureState.Screenshot, false);
            return true;
        }

        // ══════════════════════════════════════════════════════════════
        // 入口：录制
        // ══════════════════════════════════════════════════════════════

        /// <summary>开始录制。返回是否开录了。</summary>
        public static bool StartRecording()
        {
            string blocked = CheckCanRecord();
            if (blocked != null)
            {
                SetError(blocked);
                return false;
            }

            HoQuickCaptureSettingsData data = HoQuickCaptureSettings.Data;
            string folderError;
            string folder = HoQuickCaptureSettings.ResolveOutputFolder(out folderError);
            if (folder == null)
            {
                SetError(folderError);
                return false;
            }

            if (data.perTakeSubfolder)
            {
                folder = Path.Combine(
                    folder,
                    HoQuickCaptureNaming.BuildTakeFolderName(DateTime.Now, folder));
                try
                {
                    Directory.CreateDirectory(folder);
                }
                catch (Exception exception)
                {
                    SetError("建不了这次录制的子目录：" + exception.Message);
                    return false;
                }
            }

            int frames = data.ResolveTargetFrames();
            if (!data.ResolveOutputSize(out int width, out int height))
            {
                SetError("读不到游戏视图的分辨率（游戏视图窗口没开着？）。把 Game 视图打开再录，或者在「分辨率」里切「自定义」。");
                return false;
            }

            bool toVideo = data.videoFormat != HoQuickCaptureVideoFormat.PngSequence;

            // H.264 要求宽高都是偶数。奇数尺寸下编码器会直接报错，
            // 而用户看到的是"录到一半断了"。在开录前就说清楚。
            if (toVideo && (width % 2 != 0 || height % 2 != 0))
            {
                SetError("H.264 要求宽高都是偶数，现在是 " + width + "x" + height
                    + "。把分辨率调成偶数（" + (width & ~1) + "x" + (height & ~1) + "），或者把格式换成 PNG 序列。");
                return false;
            }

            var newPlan = new HoQuickCapturePlan
            {
                Width = width,
                Height = height,
                FrameRate = Mathf.Max(1f, data.frameRate),
                TargetFrameCount = frames,
                Folder = folder,
                FilePrefix = data.filePrefix,
                PngQuality = data.pngQuality,
                IsSingleShot = false,
                WritesImageFiles = !toVideo,
                ImageFormat = HoQuickCaptureImageFormat.Png,
                RenderSource = data.renderSource,
                SourceCamera = data.sourceCamera,
                ForceTransparentBackground = data.transparentBackground,
            };

            // ── 要视频就先开编码器 ──
            // 起不来（分辨率不合法 / 引擎里没有编码器 / 文件被占）就**当场拒绝开录**，
            // 而不是录完几十秒才发现没有产物。
            HoQuickCaptureMediaEncoder encoder = null;
            string videoPath = null;
            if (toVideo)
            {
                string extension = HoQuickCaptureNaming.ExtensionFor(HoQuickCaptureVideoFormat.Mp4);
                videoPath = Path.Combine(
                    folder,
                    HoQuickCaptureNaming.BuildVideoFileName(data.filePrefix, DateTime.Now, extension));

                string startError;
                encoder = HoQuickCaptureMediaEncoder.Start(
                    videoPath,
                    width,
                    height,
                    Mathf.Max(1f, data.frameRate),
                    data.videoBitrateMbps,
                    data.h264Profile,
                    data.keyframeIntervalSeconds,
                    out startError);

                if (encoder == null)
                {
                    SetError("开不了视频编码器：" + startError
                        + "\n（把格式换成「PNG 序列」可以完全不用它。）");
                    return false;
                }
            }

            // 自定义分辨率时把游戏视图也切过去：抓帧拿的是游戏视图的合成结果，
            // 游戏视图比目标小的话，出图会被放大（糊）。
            bool lockResolution = data.resolutionMode == HoQuickCaptureResolutionMode.Custom
                && data.lockGameViewResolution;

            BeginSession(newPlan, HoQuickCaptureState.Recording, lockResolution);

            // 会话真的起来了才把编码器挂上去（BeginSession 里出错会提前 return）。
            videoEncoder = encoder;
            videoOutputPath = videoPath;
            if (toVideo)
            {
                lastFile = videoPath;
            }

            return true;
        }

        /// <summary>暂停 / 继续录制（模拟冻结，画面继续画）。</summary>
        public static void SetPaused(bool paused)
        {
            if (!IsRecording || driver == null)
            {
                return;
            }

            if (paused && state == HoQuickCaptureState.Recording)
            {
                driver.SetPaused(true);
                state = HoQuickCaptureState.Paused;
                Notify();
            }
            else if (!paused && state == HoQuickCaptureState.Paused)
            {
                driver.SetPaused(false);
                state = HoQuickCaptureState.Recording;
                Notify();
            }
        }

        /// <summary>停下当前会话（录制就把已经拍到的收好，截图就直接取消）。</summary>
        public static void Stop()
        {
            if (!IsBusy)
            {
                return;
            }

            // 编辑模式没有"驱动组件"可 Abort，直接把帧泵摘掉再收摊 ——
            // 不摘的话它下一个 tick 还会回调过来，跟这里的收摊撞车（FinishSession 现在幂等，但没必要）。
            if (editModePump)
            {
                HoQuickCaptureEditModePump.Cancel();
                editModePump = false;
                FinishSession(cancelled: true);
                return;
            }

            if (driver == null)
            {
                FinishSession(cancelled: true);
                return;
            }

            abortAfterCurrentFrame = true;
            driver.Abort();
            // 暂停着的时候循环卡在"等下一帧"上，得先解冻它才会走到收摊那一步。
            if (state == HoQuickCaptureState.Paused)
            {
                driver.SetPaused(false);
                state = HoQuickCaptureState.Recording;
                Notify();
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 会话生命周期
        // ══════════════════════════════════════════════════════════════

        private static void BeginSession(HoQuickCapturePlan newPlan, HoQuickCaptureState newState, bool lockResolution)
        {
            plan = newPlan;
            state = newState;
            targetFrames = newPlan.TargetFrameCount;
            writtenFrames = 0;
            capturedFrames = 0;
            sessionFolder = newPlan.Folder;
            firstFile = null;
            lastFile = null;
            error = null;
            warning = null;
            gameTime = 0f;
            abortAfterCurrentFrame = false;
            gameViewResolutionOwned = false;
            stallReported = false;
            clockConflictReported = false;
            sessionStartRealtime = Time.realtimeSinceStartup;
            expectedStep = newPlan.IsSingleShot ? 0f : 1f / Mathf.Max(1f, newPlan.FrameRate);
            previousGameTime = float.NaN;

            bool resolutionChanged = false;
            if (lockResolution)
            {
                // 记下当前分辨率，收尾时还回去。
                // Unity Recorder 在这里是**不还原**的（官方文档明说拍完不回退），
                // 我们不学那一条：借了东西要还，否则用户下次进播放会莫名其妙换分辨率。
                if (HoQuickCaptureGameView.GetRenderSize(out restoreGameViewWidth, out restoreGameViewHeight))
                {
                    if (restoreGameViewWidth != newPlan.Width || restoreGameViewHeight != newPlan.Height)
                    {
                        if (HoQuickCaptureGameView.TrySetCustomSize(newPlan.Width, newPlan.Height))
                        {
                            gameViewResolutionOwned = true;
                            resolutionChanged = true;
                        }
                        else
                        {
                            warning = "游戏视图分辨率切不过去，出图可能被拉伸。";
                        }
                    }
                }
                else
                {
                    warning = "读不到游戏视图分辨率，没法临时切换，出图可能被拉伸。";
                }
            }
            else if (HoQuickCaptureGameView.GetRenderSize(out int liveWidth, out int liveHeight)
                     && (liveWidth != newPlan.Width || liveHeight != newPlan.Height))
            {
                warning = "出图 " + newPlan.Width + "x" + newPlan.Height
                    + " 与游戏视图 " + liveWidth + "x" + liveHeight + " 不一致，画面会被拉伸。";
            }

            if (resolutionChanged)
            {
                // 分辨率刚变，第一帧很可能是空的/旧的 —— 驱动那边会多等一帧（见 Capture()）。
                warning = null;
            }

            // ⚠️ 这一段**必须**包住：只要分岔出去的帧泵里有一句抛出去，
            // 异常就会顺着 OnGUI 冒出去，而状态已经是"忙"、驱动对象也建了一半 ——
            // 症状就是面板卡在"正在录制"、按钮全灰，只有靠看门狗或退出播放才解得开。
            // 出过一次真的：帧泵当时写在 Editor 程序集里，`AddComponent` 直接报
            // "Can't add script behaviour ... because it is an editor script"。
            try
            {
                // 两条路的分工（注意：**都只在播放模式下**，见 CheckCanStart）：
                //   · 来源 = 指定相机 → 走编辑模式帧泵。它自己渲 RT，与"游戏视图有没有在画"
                //     无关，还能跨帧预热 TAA；这是确定性的路径，播放模式下也一样用它。
                //   · 来源 = 游戏视图 → 走 MonoBehaviour + WaitForEndOfFrame，那一帧是画好的。
                bool useEditPump = plan.RenderSource == HoQuickCaptureRenderSource.Camera;

                if (useEditPump)
                {
                    // ── 渲指定相机 ──
                    // 这条不依赖编辑器重画，只借编辑器的 update tick 推进 TAA 预热。
                    editModePump = true;
                    string pumpError;
                    if (!HoQuickCaptureEditModePump.Start(
                            plan,
                            OnFrameCaptured,
                            OnEditModePumpFinished,
                            out pumpError))
                    {
                        throw new InvalidOperationException(pumpError);
                    }
                }
                else
                {
                    // ── 播放模式 ──
                    editModePump = false;
                    driverObject = new GameObject(DriverObjectName)
                    {
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                    Object.DontDestroyOnLoad(driverObject);
                    driver = driverObject.AddComponent<HoQuickCaptureDriver>();
                    if (driver == null)
                    {
                        throw new InvalidOperationException(
                            "挂不上帧泵组件（它必须在运行时程序集里，不能在 Editor 文件夹下）。");
                    }

                    // 帧泵在**运行时程序集**里，不认识引擎（依赖方向只能是 Editor → Runtime），
                    // 所以"这段录完了"靠回调传过来。捕获的是这一台的引用，
                    // 这样即使之后又开了新会话，旧驱动的回调也不会误伤新的。
                    HoQuickCaptureDriver startedDriver = driver;
                    driver.Begin(plan, OnFrameCaptured, () => NotifyDriverFinished(startedDriver));
                }
            }
            catch (Exception exception)
            {
                SetError("起不了帧泵：" + exception.Message);
                // 收干净：把已经建出来的东西销毁掉，状态回到空闲，别留个半死的会话。
                DestroyDriverImmediate();
                state = HoQuickCaptureState.Idle;
                plan = null;
                editModePump = false;
                Notify();
                return;
            }

            Notify();
        }

        /// <summary>编辑模式帧泵结束（参数是"有什么要提醒的"，null = 正常）。</summary>
        private static void OnEditModePumpFinished(string note)
        {
            editModePump = false;

            // 编辑模式只拍一张，所以帧泵一结束就是整段结束。
            // 因为是"截图"而不是"录制"，走 cancelled: false。
            FinishSession(cancelled: false);

            if (!string.IsNullOrEmpty(note))
            {
                // 退路提示（比如"这张是主摄像机直渲、没有 UI"）盖掉别的警告，
                // 因为它是用户看图之前最该知道的一条。
                warning = note;
                Notify();
            }
        }

        /// <summary>
        /// 立刻销毁驱动（不做延迟）。只给"上面那段抛异常了"这种兜底用 ——
        /// 正常收尾请走 <see cref="DestroyDriver"/>，它会延到下一个 tick
        ///（因为它是在驱动的协程里被调用的）。
        /// </summary>
        private static void DestroyDriverImmediate()
        {
            driver = null;
            if (driverObject != null)
            {
                Object.DestroyImmediate(driverObject);
                driverObject = null;
            }
        }

        /// <summary>驱动抓完一帧（或抓失败）时回调。主线程。</summary>
        private static void OnFrameCaptured(HoQuickCapturedFrame frame)
        {
            if (frame == null)
            {
                return;
            }

            // ⚠️ 这里**必须**整体包住：本方法是驱动的帧循环直接调用的，
            // 漏出去的异常会顺着协程把帧循环打断 —— 而帧循环的 finally 只能还时钟，
            // "已经抓到的帧"和"面板状态"就没人收了。
            // 写盘、编码、缩略图任何一步出错，都该变成一条错误提示，而不是一次静默的会话中断。
            try
            {
                HandleFrame(frame);
            }
            catch (Exception exception)
            {
                SetError("处理第 " + (frame.Index + 1) + " 帧时出错：" + exception.Message);
                ReleaseFrame(frame);
            }
            finally
            {
                Notify();
            }
        }

        private static void HandleFrame(HoQuickCapturedFrame frame)
        {
            capturedFrames++;

            if (!string.IsNullOrEmpty(frame.Error))
            {
                // 失败帧不参与"步长"校验：它没有像素，但**带真实游戏时间**（驱动给的），
                // 所以把基准跟着它往前推一格，下一帧的步长才是对的。
                previousGameTime = frame.GameTime;
                SetError(frame.Error);
                ReleaseFrame(frame);
                return;
            }

            // 时间只在**成功帧**上更新：失败帧不带有效时长，
            // 拿它去更新读数会让"已录"从 12.4s 突然跳回 0.0s。
            gameTime = frame.GameTime;

            // 钟被抢了吗？
            // Time.captureDeltaTime 是**全局**的，别人（尤其是 Unity Recorder）会改写它。
            // 已知的一条：Unity Recorder 在 Variable 模式下第二次非跳过帧会把
            // captureDeltaTime 直接写成 0（Recorder.ResetDeltaTime()，它自己的 m_FrameInterval
            // 只在 Constant 模式下被赋值），Constant 模式帧率不同时也会覆盖并报一条冲突错误。
            // 一旦被改写，"离线步进"就名存实亡 —— 游戏时间会按真实时间走，录制变成实时录制。
            // 所以这里每次都对一下步长：对不上就当场报错收摊，而不是给用户一段节奏乱七八糟的序列。
            if (!CheckClockSteady(frame))
            {
                return;
            }

            UpdatePreview(frame);

            // ── 视频模式：这一帧直接交给编码器，不落盘 ──
            if (videoEncoder != null)
            {
                // 纹理在抓帧时就是按"底边在前"建好的（`MediaEncoder` 吃的正是这一套行序），
                // 所以这里直接交纹理，不用再摆一次。
                if (!videoEncoder.WriteFrame(frame.Texture))
                {
                    SetError("视频编码中断：" + videoEncoder.Fault);
                }
                else
                {
                    writtenFrames++;
                    lastFile = videoOutputPath;
                }

                ReleaseFrame(frame);
                return;
            }

            string path = Path.Combine(sessionFolder, frame.FileName);
            try
            {
                byte[] png = EncodePng(frame, plan.PngQuality);
                if (png == null)
                {
                    SetError("PNG 编码失败（第 " + (frame.Index + 1) + " 帧）。");
                }
                else
                {
                    File.WriteAllBytes(path, png);
                    writtenFrames++;
                    if (firstFile == null)
                    {
                        firstFile = path;
                    }

                    lastFile = path;
                }
            }
            catch (Exception exception)
            {
                SetError("写文件失败（第 " + (frame.Index + 1) + " 帧）：" + exception.Message);
            }
            finally
            {
                ReleaseFrame(frame);
            }
        }

        /// <summary>驱动跑完了（正常录满 / 被停 / 出错）。</summary>
        internal static void NotifyDriverFinished(HoQuickCaptureDriver finishedDriver)
        {
            if (finishedDriver != driver)
            {
                return;
            }

            FinishSession(cancelled: abortAfterCurrentFrame);
        }

        private static void FinishSession(bool cancelled)
        {
            // 幂等：编辑模式那条路是"帧泵回调"与"手动停止"两个入口都能收摊的，
            // 不挡一下就会收两次（两次日志、两次 Reveal）。这里以状态为准。
            if (state == HoQuickCaptureState.Idle)
            {
                return;
            }

            HoQuickCaptureState finishedState = state;
            int written = writtenFrames;

            // ── 视频模式：收尾（写完索引，文件才算封口）──
            // 这一步放在还东西之前：封口要一点时间。
            // 它不碰 Time / 游戏视图，所以顺序上放前面只是"别让它被别的事打断"，不是硬要求。
            FinalizeVideo(sessionFolder, written);

            // ── 还东西（顺序无所谓，但不能漏） ──
            if (gameViewResolutionOwned)
            {
                gameViewResolutionOwned = false;
                HoQuickCaptureGameView.RestoreGameViewSize(restoreGameViewWidth, restoreGameViewHeight);
            }

            DestroyDriver();

            plan = null;
            state = HoQuickCaptureState.Idle;
            abortAfterCurrentFrame = false;

            bool wasVideo = videoOutputPath != null;
            string producedVideo = videoOutputPath;
            videoOutputPath = null;

            if (!cancelled && error == null)
            {
                string summary = finishedState == HoQuickCaptureState.Screenshot
                    ? "截图已保存：" + lastFile
                    : wasVideo
                        ? "录制完成：" + written + " 帧 → " + producedVideo
                        : "录制完成：" + written + " 帧 → " + sessionFolder;
                Debug.Log("[快速渲染] " + summary);
            }
            else if (cancelled)
            {
                Debug.Log("[快速渲染] 已停止，已经写出 " + written + " 帧 → "
                    + (wasVideo ? producedVideo : sessionFolder));
            }

            HoQuickCaptureSettingsData data = HoQuickCaptureSettings.Data;
            if (data.revealWhenDone && string.IsNullOrEmpty(error) && written > 0)
            {
                // 视频模式直接定位到那个 mp4；图片模式单张定位文件、序列定位目录。
                string toReveal = wasVideo
                    ? producedVideo
                    : finishedState == HoQuickCaptureState.Screenshot
                        ? lastFile
                        : sessionFolder;
                if (!string.IsNullOrEmpty(toReveal))
                {
                    EditorApplication.delayCall += () => EditorUtility.RevealInFinder(toReveal);
                }
            }

            // 产物落在 Assets/ 下面就得刷一下资产库：不然图在磁盘上但 Project 窗口看不见，
            // 而且下一次刷新会把上百张全尺寸 PNG 当 Texture 资产导进来（很慢、很占内存）。
            // 默认目录（工程根目录下的 HoQuickCapture）刻意在 Assets 外面，所以这里只是兜底。
            if (written > 0 && !string.IsNullOrEmpty(sessionFolder) && IsInsideAssets(sessionFolder))
            {
                EditorApplication.delayCall += AssetDatabase.Refresh;
            }

            Notify();
        }

        /// <summary>
        /// 关掉视频编码器：把文件封口。
        ///
        /// **这一步掉了文件就是坏的** —— 编码器要 `Dispose` 才会把索引写完，
        /// 少了它文件时长是 0、播放器打不开。所以不论正常结束、手动停止、还是退出播放，
        /// 都必须走到（<see cref="FinishSession"/> 的每条路径都会调它）。
        /// </summary>
        private static void FinalizeVideo(string folder, int written)
        {
            if (videoEncoder == null)
            {
                return;
            }

            HoQuickCaptureMediaEncoder encoder = videoEncoder;
            videoEncoder = null;

            string finishError;
            bool ok = encoder.Finish(out finishError);
            string path = videoOutputPath;
            encoder.Dispose();

            if (written <= 0)
            {
                SetError("一帧都没录到，没有生成视频。");
                DeleteBrokenVideo(path);
                return;
            }

            if (!ok)
            {
                SetError("视频收尾失败：" + (finishError ?? "未知原因"));
                DeleteBrokenVideo(path);
                return;
            }

            if (!string.IsNullOrEmpty(path) && !File.Exists(path))
            {
                SetError("编码器报告成功，但文件不存在：" + path);
                return;
            }

            lastFile = path;
            if (firstFile == null)
            {
                firstFile = path;
            }
        }

        /// <summary>收尾失败时把半截文件删掉：留一个打不开的 mp4 比没有更让人困惑。</summary>
        private static void DeleteBrokenVideo(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // 删不掉就算了，反正已经报错了
            }
        }

        /// <summary>这个目录是不是在工程的 Assets/ 里面。</summary>
        private static bool IsInsideAssets(string folder)
        {
            try
            {
                string assets = Path.GetFullPath(Application.dataPath)
                    .Replace('\\', '/')
                    .TrimEnd('/');
                string target = Path.GetFullPath(folder)
                    .Replace('\\', '/')
                    .TrimEnd('/');
                return target.StartsWith(assets + "/", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void DestroyDriver()
        {
            HoQuickCaptureDriver departing = driver;
            GameObject departingObject = driverObject;

            // 先把引用清掉再销毁：这样即使销毁过程里又回调过来（OnDisable/OnDestroy），
            // 也不会看到一个"半死"的驱动。
            driver = null;
            driverObject = null;

            if (departingObject == null && departing == null)
            {
                return;
            }

            // ⚠️ 这里**正在驱动的协程里**（OnFrameCaptured → NotifyDriverFinished → FinishSession）。
            // 协程跑到一半把自己的宿主 DestroyImmediate 掉，是 Unity 会警告的那类操作：
            // 迭代器后面的代码会在一个已销毁的对象上继续跑。
            // 所以延到下一个编辑器 tick 再销毁 —— 那时协程已经正常结束了。
            //
            // 销毁之前先 Abort()：看门狗强拆那种情况下协程可能还活着
            //（卡在 WaitForEndOfFrame 上），得让它下一圈就退出，别再多抓一帧。
            if (departing != null)
            {
                departing.Abort();
            }

            EditorApplication.delayCall += () =>
            {
                if (departingObject != null)
                {
                    Object.DestroyImmediate(departingObject);
                }
            };
        }

        // ══════════════════════════════════════════════════════════════
        // 编码 / 缩略图
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 把一帧编成图片字节。格式看 <see cref="HoQuickCapturePlan.ImageFormat"/>。
        ///
        /// **什么时候强制不透明**（把 alpha 写成 255）：
        ///   · 游戏视图抓下来的东西本来就该是不透明的（Unity Recorder 那边还专门做了个
        ///     MakeOpaque 着色器保证这一点），而且带 alpha 的 PNG 更大、丢进剪辑软件还可能
        ///     被当成带透明通道的素材；
        ///   · 但**「指定相机 + 透明背景」那条路必须原样保留 alpha** —— 那正是它的用途。
        /// **EXR 永远不碰**：它的用途就是拿去做合成，alpha 是有效数据，改了反而是错的。
        /// </summary>
        private static byte[] EncodePng(HoQuickCapturedFrame frame, int quality)
        {
            if (frame.Texture == null)
            {
                return null;
            }

            bool wantsTransparency = plan != null
                && plan.RenderSource == HoQuickCaptureRenderSource.Camera
                && plan.ForceTransparentBackground;

            bool keepAlpha = wantsTransparency
                || (plan != null && plan.ImageFormat == HoQuickCaptureImageFormat.Exr);

            if (!keepAlpha)
            {
                // 纹理在抓帧时就摆正了（底边在前 = Unity 的纹理行序），
                // 而三个编码调用都是"从纹理里读"的，所以这里改完像素还要写回纹理。
                Color32[] bottomUp = frame.GetBottomUpPixels();
                if (bottomUp != null)
                {
                    for (int i = 0; i < bottomUp.Length; i++)
                    {
                        bottomUp[i].a = 255;
                    }

                    frame.Texture.SetPixels32(bottomUp);
                    frame.Texture.Apply(false, false);
                }
            }

            if (plan != null && plan.ImageFormat == HoQuickCaptureImageFormat.Exr)
            {
                return frame.Texture.EncodeToEXR();
            }

            if (plan != null && plan.ImageFormat == HoQuickCaptureImageFormat.Jpg)
            {
                // JPG 没有质量重载的问题：`EncodeToJPG(int)` 从很早就有了。
                return frame.Texture.EncodeToJPG(JpegQualityFromPngQuality(quality));
            }

            if (!SupportsPngQuality)
            {
                return frame.Texture.EncodeToPNG();
            }

            return EncodePngWithQuality(frame.Texture, quality);
        }

        /// <summary>
        /// 面板上那个「质量」滑杆对两种格式都管用：PNG 是压缩力度，JPG 是有损质量。
        /// 两者的方向**相反**（PNG 0 = 编得快/文件大，JPG 0 = 质量最差），所以这里翻一下。
        /// </summary>
        private static int JpegQualityFromPngQuality(int pngQuality)
        {
            return Mathf.Clamp(100 - Mathf.Clamp(pngQuality, 0, 100), 1, 100);
        }

        /// <summary>
        /// <c>Texture2D.EncodeToPNG(int quality)</c> 是 **Unity 2022.1 才有的重载**（本包下限是 2021.3），
        /// 而且它不是虚的 —— 整个方法都在，只是少了那个签名，所以只能靠反射调：
        /// 老版本上退回无参的 <c>EncodeToPNG()</c>，图一样是无损的，只是压缩力度不可调。
        /// </summary>
        private static bool SupportsPngQuality
        {
            get
            {
                if (!pngQualityProbed)
                {
                    pngQualityProbed = true;
                    pngQualityMethod = typeof(Texture2D).GetMethod(
                        "EncodeToPNG",
                        new[] { typeof(int) });
                }

                return pngQualityMethod != null;
            }
        }

        private static MethodInfo pngQualityMethod;
        private static bool pngQualityProbed;

        private static byte[] EncodePngWithQuality(Texture2D texture, int quality)
        {
            try
            {
                return (byte[])pngQualityMethod.Invoke(
                    texture,
                    new object[] { Mathf.Clamp(quality, 0, 100) });
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[快速渲染] 带质量的 PNG 编码失败，退回默认压缩：" + exception.Message);
                pngQualityMethod = null;
                return texture.EncodeToPNG();
            }
        }

        /// <summary>把最新一帧降采样成缩略图（最近邻抽点，够看构图，不吃 CPU）。</summary>
        private static void UpdatePreview(HoQuickCapturedFrame frame)
        {
            Color32[] sourcePixels = frame.GetBottomUpPixels();
            if (sourcePixels == null || frame.Width < 1 || frame.Height < 1)
            {
                return;
            }

            int longSide = Mathf.Max(frame.Width, frame.Height);
            float scale = longSide > PreviewLongSide ? (float)PreviewLongSide / longSide : 1f;
            int width = Mathf.Max(1, Mathf.RoundToInt(frame.Width * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(frame.Height * scale));

            if (preview == null || preview.width != width || preview.height != height)
            {
                if (preview != null)
                {
                    Object.DestroyImmediate(preview);
                }

                preview = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                previewPixels = new Color32[width * height];
            }

            if (previewPixels == null || previewPixels.Length != width * height)
            {
                previewPixels = new Color32[width * height];
            }

            // 最近邻抽点：按目标像素反查源像素。整段 O(缩略图面积)，和出图多大无关。
            for (int y = 0; y < height; y++)
            {
                int sourceY = Mathf.Clamp((int)((y + 0.5f) * frame.Height / height), 0, frame.Height - 1);
                int sourceRow = sourceY * frame.Width;
                int targetRow = y * width;
                for (int x = 0; x < width; x++)
                {
                    int sourceX = Mathf.Clamp((int)((x + 0.5f) * frame.Width / width), 0, frame.Width - 1);
                    previewPixels[targetRow + x] = sourcePixels[sourceRow + sourceX];
                }
            }

            preview.SetPixels32(previewPixels);
            preview.Apply(false, false);
        }

        private static void ReleaseFrame(HoQuickCapturedFrame frame)
        {
            if (frame.Texture != null)
            {
                Object.DestroyImmediate(frame.Texture);
                frame.Texture = null;
            }

            frame.PixelData = null;
        }

        /// <summary>
        /// 校验"离线步进"还在生效：相邻两帧的游戏时间差应该正好是 1/帧率。
        ///
        /// 为什么值得单独查一遍 —— `Time.captureDeltaTime` 是全局状态，别人会动它。
        /// 已经知道的一条（在 Unity Recorder 5.1.6 源码里核对过）：
        /// `Recorder.ResetDeltaTime()` 在 `FrameRatePlayback.Variable` 下，第二次非跳过帧
        /// 会把 `Time.captureDeltaTime` 写成 **0** —— 因为它的 `m_FrameInterval` 只在 Constant
        /// 模式下才被赋值。Constant 模式且帧率与我们不同时，它则会覆盖成它自己的帧率。
        /// 两种情况的结果一样：我们的"离线步进"没了，录出来的东西按真实时间走。
        ///
        /// 与其给用户一段节奏不对的序列，不如当场停下来说清楚。
        /// </summary>
        private static bool CheckClockSteady(HoQuickCapturedFrame frame)
        {
            if (expectedStep <= 0f)
            {
                return true;
            }

            if (float.IsNaN(previousGameTime))
            {
                previousGameTime = frame.GameTime;
                return true;
            }

            float step = frame.GameTime - previousGameTime;
            previousGameTime = frame.GameTime;

            // 容差取半个步长：不同 Unity 版本对 captureDeltaTime 的取整方式略有差别，
            // 但不该差出半帧去。
            if (Mathf.Abs(step - expectedStep) <= expectedStep * 0.5f)
            {
                return true;
            }

            ReleaseFrame(frame);
            if (!clockConflictReported)
            {
                clockConflictReported = true;
                SetError(
                    "游戏时钟被别人抢走了：这一帧的游戏时间前进了 " + step.ToString("0.####")
                    + " 秒，应该是 " + expectedStep.ToString("0.####") + " 秒。"
                    + "最常见的原因是本工程同时开着 Unity Recorder（它会改 / 清掉 Time.captureDeltaTime）。"
                    + "关掉 Recorder 再录。已经录到的帧不受影响。");
            }

            Stop();
            return false;
        }

        // ══════════════════════════════════════════════════════════════
        // 收尾兜底
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 心跳检查：帧泵还在出帧吗？
        ///
        /// 为什么必须有这个：`WaitForEndOfFrame` 在游戏视图不可见时**可能永远不返回**，
        /// 于是既不报错也不出图，用户只看到面板卡在 0/150 上，完全不知道是自己把游戏视图挡住了
        /// （Unity Recorder 的文档把这个坑写明了，它自己也是"一直等下去"）。
        /// 这里主动发现并说清楚：报错 + 收摊，别让面板假死。
        /// </summary>
        private static void OnEditorUpdate()
        {
            if (!IsBusy)
            {
                return;
            }

            // TAA 预热期间要让面板重画，否则那 16 帧里进度条是**冻住的**
            //（用户要进度条本来就是为了别看起来像卡死，条不动就等于没做）。
            if (HoQuickCaptureCameraRenderer.WarmupActive)
            {
                Action repaint = Changed;
                if (repaint != null)
                {
                    repaint();
                }
            }

            // 编辑模式那条路有自己的重试与超时（帧泵里 MaxPumpAttempts），
            // 而且它的 driver 本来就是 null —— 让看门狗在这里插手只会把正常等待误判成"驱动没了"
            // 从而提前收摊。所以编辑模式下不看门。
            if (editModePump)
            {
                return;
            }

            // 驱动被外部干掉了（它是 HideAndDontSave 的，脚本里 Destroy 根物体、
            // 场景重置之类都可能顺手清掉它）。Unity 的"假 null"让 driver == null，
            // 而状态的 IsBusy 还是 true —— 早先这里直接 return，于是**永远卡在"录制中"**，
            // 按钮全灰、游戏视图分辨率也一直借着。所以假 null 等于"它已经结束了"，直接收摊。
            if (driver == null)
            {
                FinishSession(cancelled: true);
                return;
            }

            if (stallReported)
            {
                return;
            }

            // 驱动那边抓帧失败时**不**交帧（不占帧号、不留洞），失败原因走这个属性过来。
            // 在这里读走并按普通错误显示：能看到原因，序列也不断。
            if (!string.IsNullOrEmpty(driver.LastFailure) && string.IsNullOrEmpty(error))
            {
                SetError("抓帧失败：" + driver.LastFailure + "（在重试，序列不会断）");
            }

            // ⚠️ 两个必须用**同一个钟**：心跳是 `Time.realtimeSinceStartup`（见
            // HoQuickCaptureDriver.LastFrameRealtime 的说明），所以这里也用同一个。
            // 早先这里用的是 `EditorApplication.timeSinceStartup`（编辑器启动算起、不归零），
            // 而心跳当时用的是运行时时钟（进播放归零）—— 两个一减就是几千秒，
            // 于是**每段录制都会在 8 秒左右被误判成"游戏视图没在渲染"并砍断**。
            //
            // 用运行时时钟是安全的：这里只在播放模式下被走到（抓帧必须播放），
            // 一次播放会话内它不归零。
            //
            // 先给一段宽限：起录后切场景 / 加载那一下卡几秒是正常的。
            if (Time.realtimeSinceStartup - sessionStartRealtime < StallGraceSeconds)
            {
                return;
            }

            if (Time.realtimeSinceStartup - driver.LastFrameRealtime < StallTimeoutSeconds)
            {
                return;
            }

            stallReported = true;
            SetError(
                "已经 " + StallTimeoutSeconds.ToString("0")
                + " 秒没有拍到任何一帧：多半是游戏视图没在渲染。"
                + "把 Game 视图切到前台（别被 Scene 视图或别的窗口完全挡住、别最小化）再试。");

            // ⚠️ 这里**不能**只调 Stop()。
            // Stop() 走的是"礼貌"路径：置 `abortRequested`，等帧循环自己在下一圈看到它再收摊。
            // 而看门狗要处理的**正是帧循环卡在 WaitForEndOfFrame 上根本回不来的情况** ——
            // 那种情况下循环永远不会再跑一圈，Stop() 置的标志没人读，
            // 于是状态永远停在"录制中"、captureDeltaTime 一直借着、按钮全部点不动。
            // 所以这里直接收摊：FinishSession 会销毁那个卡住的驱动物体，协程随之结束。
            FinishSession(cancelled: true);
        }

        /// <summary>
        /// 退出播放 = 帧循环没了，驱动那句 <c>WaitForEndOfFrame</c> 永远等不到，
        /// 所以必须在这里主动掐断并还东西。**这是最容易漏的一条路径**：
        /// 用户录到一半直接按停止播放，如果这里不收拾，
        /// Time.captureDeltaTime 与游戏视图分辨率就会留在工程里。
        /// </summary>
        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode || change == PlayModeStateChange.EnteredEditMode)
            {
                if (IsBusy)
                {
                    FinishSession(cancelled: true);
                }
            }
        }

        /// <summary>脚本重编译会走域重载：静态状态全没了，先把借的东西还掉。</summary>
        private static void OnBeforeAssemblyReload()
        {
            if (IsBusy)
            {
                FinishSession(cancelled: true);
            }
        }

        private static void SetError(string message)
        {
            error = message;
            if (!string.IsNullOrEmpty(message))
            {
                Debug.LogWarning("[快速渲染] " + message);
            }

            Notify();
        }

        /// <summary>面板上那个 ✕：把当前这条提示清掉（不影响正在跑的会话）。</summary>
        public static void ClearNotice()
        {
            if (string.IsNullOrEmpty(error) && string.IsNullOrEmpty(warning))
            {
                return;
            }

            error = null;
            warning = null;
            Notify();
        }

        private static void Notify()
        {
            Action handler = Changed;
            if (handler != null)
            {
                handler();
            }
        }

        /// <summary>面板关掉时把缩略图收了（引擎自己活到域重载为止）。</summary>
        internal static void ReleasePreview()
        {
            if (preview != null)
            {
                Object.DestroyImmediate(preview);
                preview = null;
                previewPixels = null;
            }
        }
    }
}
