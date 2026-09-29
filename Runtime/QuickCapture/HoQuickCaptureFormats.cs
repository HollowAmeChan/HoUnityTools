// HoQuickCaptureFormats.cs -- 抓帧/录制用到的**运行时**枚举
//
// 为什么这两个枚举在 Runtime 而不是跟设置放一起：
//   帧泵 `HoQuickCaptureDriver` 是真正的 MonoBehaviour，**必须**待在运行时程序集里
//   （见 HoQuickCaptureDriver.cs 顶部）。而运行时程序集**不能**引用 Editor 程序集
//   （`HoUnityTools.Editor` 的 `includePlatforms` 是 `["Editor"]`，方向只能是 Editor → Runtime）。
//   所以帧泵要用的类型就得在 Runtime 这一侧。
//
// 这条是踩出来的：一开始帧泵写在 `Editor/QuickCapture/` 下，`AddComponent` 直接报
//   "Can't add script behaviour 'HoQuickCaptureDriver' because it is an editor script.
//    To attach a script it needs to be outside the 'Editor' folder."
// —— 编辑器程序集里的 MonoBehaviour 是挂不到物体上的，不管它名字里有没有 Editor。
using UnityEngine;

namespace Hollow.HoUnityTools.Runtime.QuickCapture
{
    /// <summary>单张截图存成什么格式。</summary>
    public enum HoQuickCaptureImageFormat
    {
        /// <summary>PNG（默认）。无损，体积大。</summary>
        Png,

        /// <summary>JPG。有损、体积小，适合快速看一眼构图。</summary>
        Jpg,

        /// <summary>EXR。浮点，给后期合成用（alpha 会原样保留）。</summary>
        Exr,
    }

    /// <summary>
    /// 录制输出成什么。
    ///
    /// MP4 走 **Unity 自带的编码器**（`UnityEditor.Media.MediaEncoder`，Windows 上是
    /// Media Foundation）：零依赖、不用装任何东西、跨版本一致。
    /// PNG 序列是零依赖的另一种形态 —— 每帧一张图，方便自己再合成。
    ///
    /// **没有 MOV**：Unity 的编码器认容器只认扩展名，而它能写的是
    /// `.mp4`（H.264/H.265）与 `.webm`（VP8）—— 它根本写不了 `.mov`
    ///（Unity 原生代码里写着 "H264 or H265 codec must be used for a .mp4 container"，
    ///  而 ProRes/MOV 是 Recorder 包自带的一个原生插件，不在引擎里）。
    /// 与其放一个必然失败的选项，不如不给。
    /// </summary>
    public enum HoQuickCaptureVideoFormat
    {
        /// <summary>MP4（H.264）。默认。</summary>
        Mp4,

        /// <summary>PNG 序列。每帧一张图，不需要任何编码器。</summary>
        PngSequence,
    }
}
