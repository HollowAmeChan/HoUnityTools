using UnityEngine;

namespace Hollow.HoUnityTools.Editor.QuickCapture
{
    /// <summary>Output-size presets only; never reduce the artist's renderer or Volume effects.</summary>
    internal static class HoQuickCaptureQualityPresets
    {
        internal static readonly string[] QualityNames = { "验证 360p", "预览 720p", "精查 1080p", "高分 2160p" };
        internal static readonly string[] AspectNames = { "横屏 16:9", "竖屏 9:16", "4:3", "正方形 1:1" };
        static readonly int[] Edges = { 360, 720, 1080, 2160 };
        static readonly Vector2Int[] Ratios = {
            new Vector2Int(16, 9), new Vector2Int(9, 16), new Vector2Int(4, 3), new Vector2Int(1, 1)
        };

        internal static Vector2Int Resolve(int quality, int aspect)
        {
            int edge = Edges[Mathf.Clamp(quality, 0, Edges.Length - 1)];
            var ratio = Ratios[Mathf.Clamp(aspect, 0, Ratios.Length - 1)];
            int divisor = Mathf.Min(ratio.x, ratio.y);
            return new Vector2Int(edge * ratio.x / divisor, edge * ratio.y / divisor);
        }

        internal static int FindAspect(int width, int height)
        {
            for (int i = 0; i < Ratios.Length; ++i)
                if ((long)width * Ratios[i].y == (long)height * Ratios[i].x) return i;
            return -1;
        }

        internal static int FindQuality(int width, int height)
        {
            int aspect = FindAspect(width, height);
            if (aspect < 0) return -1;
            for (int i = 0; i < Edges.Length; ++i)
                if (Resolve(i, aspect) == new Vector2Int(width, height)) return i;
            return -1;
        }

        internal static void Apply(HoQuickCaptureSettingsData data, int quality, int aspect)
        {
            var size = Resolve(quality, aspect);
            data.resolutionMode = HoQuickCaptureResolutionMode.Custom;
            data.customWidth = size.x;
            data.customHeight = size.y;
            data.lockGameViewResolution = true;
        }
    }
}
