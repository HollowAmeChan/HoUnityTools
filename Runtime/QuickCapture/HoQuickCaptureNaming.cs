// HoQuickCaptureNaming.cs -- 文件名与目录名的规则
//
// 单独拎出来是因为**它是产物的一部分**：帧号补零的位数决定了序列能不能被
// ffmpeg / AE / 达芬奇这类工具按顺序吃进去。所以规则要固定、要有文档、别散落在各处。
//
// 约定：
//   · 帧号一律 **4 位补零**（0000 ~ 9999）。超过 9999 帧不截断、自然变宽
//     —— 宁可排序工具那里宽一位，也不要两个文件撞名。
//   · 单张截图用拍摄时刻当文件名（`前缀_2026-02-05_14h30m12s.png`），
//     所以连拍几张不会互相覆盖，也不会出现 `_0000` 这种让人以为是序列的名字。
//   · 时间戳一律用 **本地时间 + InvariantCulture**：文件名里带冒号在 Windows 上是非法的，
//     所以用 `14h30m12s` 这种写法。
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Hollow.HoUnityTools.Runtime.QuickCapture
{
    /// <summary>产物名字的生成规则。</summary>
    public static class HoQuickCaptureNaming
    {
        /// <summary>
        /// 帧号补零到几位。
        ///
        /// 用 **6 位**（`000000`）而不是常见的 4 位：上限是 100 万帧，
        /// 4 位一到 10000 帧就出现"`_10000.png` 排在 `_9999.png` 前面"的字典序错乱，
        /// 而且 `ffmpeg -i prefix_%04d.png` 只认四位、到 9999 就停。
        /// 6 位在这个上限内**永远不会变宽**。配套的命令是：
        /// <code>ffmpeg -framerate 30 -i prefix_%06d.png ...</code>
        /// </summary>
        public const int FrameDigits = 6;

        /// <summary>图片格式对应的扩展名（小写、不带点）。</summary>
        public static string ExtensionFor(HoQuickCaptureImageFormat format)
        {
            switch (format)
            {
                case HoQuickCaptureImageFormat.Jpg:
                    return "jpg";
                case HoQuickCaptureImageFormat.Exr:
                    return "exr";
                default:
                    return "png";
            }
        }

        /// <summary>视频容器的扩展名。Unity 的编码器只写得了 mp4（H.264）。</summary>
        public static string ExtensionFor(HoQuickCaptureVideoFormat format)
        {
            // 目前只有 MP4 一种容器（另一个选项是 PNG 序列，走图片那条路）。
            // 留这个方法是为了以后加容器时调用点不用改。
            return "mp4";
        }

        /// <summary>序列帧的文件名：<c>前缀_000000.png</c>。</summary>
        public static string BuildFileName(string prefix, int frameIndex, string extension = "png")
        {
            string safePrefix = SanitizeSegment(prefix);
            if (safePrefix.Length == 0)
            {
                safePrefix = "HoCapture";
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}_{1}.{2}",
                safePrefix,
                frameIndex.ToString("D" + FrameDigits, CultureInfo.InvariantCulture),
                extension);
        }

        /// <summary>单张截图的文件名：<c>前缀_2026-02-05_14h30m12s.png</c>。</summary>
        public static string BuildScreenshotFileName(string prefix, DateTime localTime, string extension = "png")
        {
            string safePrefix = SanitizeSegment(prefix);
            if (safePrefix.Length == 0)
            {
                safePrefix = "HoCapture";
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}_{1:yyyy-MM-dd_HH}h{1:mm}m{1:ss}s.{2}",
                safePrefix,
                localTime,
                extension);
        }

        /// <summary>视频文件名：<c>前缀_2026-02-05_14h30m12s.mp4</c>（和截图同一套命名）。</summary>
        public static string BuildVideoFileName(string prefix, DateTime localTime, string extension)
        {
            return BuildScreenshotFileName(prefix, localTime, extension);
        }

        /// <summary>一段录制的子目录名：<c>take_2026-02-05_14h30m12s</c>。</summary>
        public static string BuildTakeFolderName(DateTime localTime, string parentFolder)
        {
            string baseName = string.Format(
                CultureInfo.InvariantCulture,
                "take_{0:yyyy-MM-dd_HH}h{0:mm}m{0:ss}s",
                localTime);

            // 时间戳只精确到秒，而"连点两下开始录制"完全可能落在同一秒里 ——
            // 那样两段会共用同一个目录，后一段把前一段的 `_0000.png` 覆盖掉（看着像丢了帧）。
            // 所以目录名要**实际问一下磁盘**：撞了就在后面加 `_2`、`_3`。
            if (string.IsNullOrEmpty(parentFolder))
            {
                return baseName;
            }

            string candidate = baseName;
            int suffix = 2;
            while (suffix <= 9999 && Directory.Exists(Path.Combine(parentFolder, candidate)))
            {
                candidate = baseName + "_" + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }

            return candidate;
        }

        /// <summary>
        /// 洗掉不能进文件名的字符。
        ///
        /// 除了 <see cref="Path.GetInvalidFileNameChars"/>，还要挡两样：
        ///   · 路径分隔符本身 —— 前缀里写个 <c>a/b</c> 就变成往子目录里写了；
        ///   · 结尾的点与空格 —— Windows 上 <c>abc.</c> 与 <c>abc</c> 是同一个文件，
        ///     会出现"我明明写进去了却找不到"这种怪事。
        /// </summary>
        public static string SanitizeSegment(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                bool bad = c == '/' || c == '\\' || c == ':';
                if (!bad)
                {
                    for (int j = 0; j < invalid.Length; j++)
                    {
                        if (invalid[j] == c)
                        {
                            bad = true;
                            break;
                        }
                    }
                }

                builder.Append(bad ? '_' : c);
            }

            string result = builder.ToString().Trim();
            return result.TrimEnd('.');
        }
    }
}
