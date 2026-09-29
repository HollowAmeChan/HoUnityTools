// HoQuickCaptureMediaEncoder.cs -- 用 Unity **自带**的编码器写 MP4（H.264）
//
// 为什么用这个而不是自带一份 ffmpeg：
//   · **零依赖**：`UnityEditor.Media.MediaEncoder` 是引擎自带的（Windows 上底层是
//     Media Foundation）。不用下载任何东西、不用往仓库里塞二进制 ——
//     而一份静态 ffmpeg.exe 有 100 MB 以上，**超过 GitHub 单文件 100 MiB 的硬上限、根本提交不进去**；
//     走 Git LFS 更糟：Unity 官方文档说 LFS 没装客户端时会**静默**检出指针文件而不是真二进制，
//     于是编码器变成一个 130 字节的文本文件而没有任何报错。
//   · **跨版本一致**：类型与公开签名在 2021.3 与 Unity 6 上完全相同
//     （2021.3/2022.3 里它在 `UnityEditor.CoreModule.dll`，Unity 6 拆到了
//     `UnityEditor.MediaModule.dll`，命名空间与类型名没变 —— 已用 Roslyn 对两套编辑器程序集各编过一遍）。
//
// 容器由**文件扩展名**决定。Unity 原生代码里的检查写着：
//   "H264 or H265 codec must be used for a .mp4 container." / "VP8 must be used for a .webm container."
// 也就是说它只能写 `.mp4`（H.264/H.265）与 `.webm`（VP8）—— **写不了 `.mov`**
//（MOV/ProRes 是 Recorder 包自带的原生插件 `ProResWrapper.dll`，不在引擎里）。
// 所以面板上只给 MP4 与 PNG 序列两个选项，不放一个必然失败的。
//
// 编码器由**调用哪个构造函数**决定（`VideoTrackEncoderAttributes(H264EncoderAttributes)`
// 就是 H.264）—— codec 字段是私有的，赋不了值。
//
// ⚠️ 已知边界（都是 Unity 侧的，不是我们的）：
//   · **宽高必须偶数**（mp4 不接受奇数分辨率）；
//   · **不支持 Linux 编辑器**（Windows / macOS 可以；Unity 自己的 XML 文档写着 "for macOS and Windows only"）；
//   · mp4 没有 alpha 通道（所以 `includeAlpha = false`）；
//   · **只在编辑器里有**（`UnityEditor` 命名空间），播放器构建里没有；
//   · `AddFrame` 是**同步阻塞**的。在"离线步进"里可接受 —— 我们本来就不追求墙上时间，只要求帧数确定。
using System;
using System.IO;
using Hollow.HoUnityTools.Runtime.QuickCapture;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Hollow.HoUnityTools.Editor.QuickCapture
{
    /// <summary>用 Unity 自带编码器写 MP4。</summary>
    internal sealed class HoQuickCaptureMediaEncoder : IDisposable
    {
        private MediaEncoder encoder;
        private int frameRate;
        private bool closed;
        private string faultMessage;

        /// <summary>出问题了就有值。</summary>
        public string Fault => faultMessage;

        /// <summary>已经写进去多少帧。</summary>
        public int FramesWritten { get; private set; }

        /// <summary>输出文件（含扩展名）。</summary>
        public string OutputPath { get; private set; }

        private HoQuickCaptureMediaEncoder()
        {
        }

        /// <summary>
        /// 开一个编码会话。
        /// </summary>
        /// <param name="outputPath">输出文件；扩展名决定容器（要 `.mp4`）。</param>
        /// <param name="width">宽（必须偶数）。</param>
        /// <param name="height">高（必须偶数）。</param>
        /// <param name="frameRate">帧率。</param>
        /// <param name="bitrateMbps">目标码率（Mbps）；&lt;= 0 时按分辨率估一个。</param>
        /// <param name="h264Profile">0 = Baseline，1 = Main，2 = High。</param>
        /// <param name="keyframeIntervalSeconds">关键帧间隔（秒）。</param>
        /// <param name="error">失败原因。</param>
        public static HoQuickCaptureMediaEncoder Start(
            string outputPath,
            int width,
            int height,
            float frameRate,
            float bitrateMbps,
            int h264Profile,
            float keyframeIntervalSeconds,
            out string error)
        {
            error = null;

            if (width <= 0 || height <= 0)
            {
                error = "分辨率不合法：" + width + "x" + height;
                return null;
            }

            if (width % 2 != 0 || height % 2 != 0)
            {
                error = "H.264 要求宽高都是偶数，现在是 " + width + "x" + height
                    + "。把分辨率调成偶数（" + (width & ~1) + "x" + (height & ~1) + "），或者改用 PNG 序列。";
                return null;
            }

            int rate = Mathf.Clamp(Mathf.RoundToInt(frameRate), 1, 240);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            }
            catch (Exception exception)
            {
                error = "建不了输出目录：" + exception.Message;
                return null;
            }

            var instance = new HoQuickCaptureMediaEncoder
            {
                frameRate = rate,
                OutputPath = outputPath,
            };

            try
            {
                // 码率：没填就按"每像素每帧 0.1 bit"估一个（1080p30 ≈ 6 Mbps）。
                // 注意 `targetBitRate` 是 **uint**（bps）—— 传 int 编不过。
                uint targetBitRate = bitrateMbps > 0f
                    ? (uint)Math.Round(bitrateMbps * 1000.0 * 1000.0)
                    : (uint)Math.Max(2_000_000L, (long)(width * (long)height * rate * 0.1));

                var h264 = new H264EncoderAttributes
                {
                    // 关键帧间隔。太小文件大，太大拖动定位不灵。
                    gopSize = (uint)Mathf.Max(1, Mathf.RoundToInt(rate * Mathf.Max(0.25f, keyframeIntervalSeconds))),
                    numConsecutiveBFrames = 2,
                    profile = ProfileFromIndex(h264Profile),
                };

                var attributes = new VideoTrackEncoderAttributes(h264)
                {
                    frameRate = new MediaRational(rate),
                    // `width` / `height` 都是 **uint**。
                    width = (uint)width,
                    height = (uint)height,
                    includeAlpha = false,
                    bitRateMode = VideoBitrateMode.High,
                    targetBitRate = targetBitRate,
                };

                instance.encoder = new MediaEncoder(outputPath, attributes);

                return instance;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                instance.Dispose();
                return null;
            }
        }

        private static VideoEncodingProfile ProfileFromIndex(int index)
        {
            switch (index)
            {
                case 0:
                    return VideoEncodingProfile.H264Baseline;
                case 1:
                    return VideoEncodingProfile.H264Main;
                default:
                    return VideoEncodingProfile.H264High;
            }
        }

        /// <summary>
        /// 写一帧。
        ///
        /// 时间戳按**帧号**算（`frameIndex / frameRate`），而不是取 `Time.time` ——
        /// 离线步进下两者等价，但按帧号算出来的时间轴一定是等间距的，
        /// 不会因为某一帧渲染特别慢而在视频里留下一个空洞。
        /// </summary>
        /// <summary>
        /// 写一帧。
        ///
        /// 直接走 `MediaEncoder.AddFrame(Texture2D, MediaTime)` 这个重载 —— 让引擎自己去读纹理，
        /// 我们**不手工搬像素**。这么做有三个好处：
        ///   · 不用碰 `NativeArray` 的指针（那需要给程序集开 `allowUnsafeCode`，或者跟
        ///     `AddFrame` 那串 `int`/`uint` 混用的参数类型较劲）；
        ///   · 不用自己管 RGBA→编码器格式的转换；
        ///   · 不用手工做上下翻转 —— 这个重载按纹理自己的朝向读。
        ///
        /// 时间戳按**帧号**给（`frameIndex / frameRate`），不是取 `Time.time`：
        /// 离线步进下两者等价，但按帧号算出来的时间轴一定是等间距的，
        /// 不会因为某一帧渲染特别慢而在视频里留下一个空洞。
        /// </summary>
        /// <param name="texture">这一帧的纹理（RGBA32）。</param>
        public bool WriteFrame(Texture2D texture)
        {
            if (closed)
            {
                return false;
            }

            if (encoder == null)
            {
                SetFault("编码器不可用。");
                return false;
            }

            if (texture == null)
            {
                SetFault("帧纹理没了。");
                return false;
            }

            try
            {
                // `MediaTime` 的签名是 `(long count, uint rateNumerator, uint rateDenominator)` ——
                // 帧率那个参数是 **uint**，传 int 编不过。
                encoder.AddFrame(texture, new MediaTime(FramesWritten, (uint)frameRate, 1u));
                FramesWritten++;
                return true;
            }
            catch (Exception exception)
            {
                SetFault("写入第 " + (FramesWritten + 1) + " 帧失败：" + exception.Message);
                return false;
            }
        }
        /// <summary>收尾：把编码器关掉，文件才会封口。</summary>
        public bool Finish(out string error)
        {
            error = null;
            if (closed)
            {
                return string.IsNullOrEmpty(faultMessage);
            }

            closed = true;

            try
            {
                if (encoder != null)
                {
                    encoder.Dispose();
                    encoder = null;
                }
            }
            catch (Exception exception)
            {
                error = "关闭编码器失败：" + exception.Message;
            }



            if (faultMessage != null && error == null)
            {
                error = faultMessage;
            }

            return error == null;
        }

        public void Dispose()
        {
            try
            {
                if (!closed)
                {
                    string ignored;
                    Finish(out ignored);
                }
            }
            catch (Exception)
            {
                // Dispose 不抛
            }


        }

        private void SetFault(string message)
        {
            faultMessage = message;
            Debug.LogWarning("[快速渲染] 视频编码：" + message);
        }
    }
}
