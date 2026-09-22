using System;
using System.Collections.Generic;

namespace Hollow.HoUnityTools.FaceTracking
{
    [Flags]
    public enum HoFaceRegion
    {
        Mouth = 1, Brows = 2, Cheeks = 4, Eyelids = 8, Gaze = 16,
        Expression = Mouth | Brows | Cheeks | Eyelids, All = Expression | Gaze
    }

    public enum HoFaceInputMode { Live, Manual, Hold, Neutral, Release }

    /// <summary>
    /// 驱动层的两个区域子树。参考实现里眉挂 `EyeTrackingActive`、颊鼻挂 `LipTrackingActive`，
    /// 即 **眼区 = 眼睑 + 眼球 + 眉**、**唇区 = 嘴 + 颊鼻** —— 眉毛跟着眼神走，不是跟着嘴走。
    /// </summary>
    public enum HoFaceGate { Eye, Lip }

    /// <summary>
    /// 平滑分组。对齐参考实现的 <c>OSCm/Sensitivity</c> 分组手感：眼睑、眼球、嘴各自一组，其余归一组 ——
    /// 而不是 52 个键共用一个平滑系数。实践中那不够用：眼球要跟得紧、眼睑要稳、嘴要更黏，
    /// 三者的合适时长能差一个数量级。
    /// </summary>
    public enum HoFaceSmoothGroup { Eyelids, Gaze, Mouth, Other }

    [Serializable]
    public sealed class HoFaceChannel
    {
        public string shape;
        public string parameter;
        public HoFaceInputMode mode;
        public float manual;
        public float neutral;
        public float gain = 1f;
    }

    /// <summary>ARKit names, independent of the iFacialMocap wire spelling.</summary>
    public static class HoFaceTrackingChannels
    {
        public static readonly string[] Names =
        {
            "eyeBlinkLeft", "eyeLookDownLeft", "eyeLookInLeft", "eyeLookOutLeft", "eyeLookUpLeft", "eyeSquintLeft", "eyeWideLeft",
            "eyeBlinkRight", "eyeLookDownRight", "eyeLookInRight", "eyeLookOutRight", "eyeLookUpRight", "eyeSquintRight", "eyeWideRight",
            "jawForward", "jawLeft", "jawRight", "jawOpen", "mouthClose", "mouthFunnel", "mouthPucker", "mouthLeft", "mouthRight",
            "mouthSmileLeft", "mouthSmileRight", "mouthFrownLeft", "mouthFrownRight", "mouthDimpleLeft", "mouthDimpleRight",
            "mouthStretchLeft", "mouthStretchRight", "mouthRollLower", "mouthRollUpper", "mouthShrugLower", "mouthShrugUpper",
            "mouthPressLeft", "mouthPressRight", "mouthLowerDownLeft", "mouthLowerDownRight", "mouthUpperUpLeft", "mouthUpperUpRight",
            "browDownLeft", "browDownRight", "browInnerUp", "browOuterUpLeft", "browOuterUpRight", "cheekPuff", "cheekSquintLeft",
            "cheekSquintRight", "noseSneerLeft", "noseSneerRight", "tongueOut"
        };

        private static readonly Dictionary<string, int> Indices = BuildIndices();

        private static Dictionary<string, int> BuildIndices()
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < Names.Length; i++)
            {
                string name = Names[i];
                result[name] = i;
                // mouthLeft/Right and jawLeft/Right are unsuffixed wire fields.
                if (name != "mouthLeft" && name != "mouthRight" && name != "jawLeft" && name != "jawRight")
                {
                    if (name.EndsWith("Left", StringComparison.Ordinal)) result[name.Substring(0, name.Length - 4) + "_L"] = i;
                    if (name.EndsWith("Right", StringComparison.Ordinal)) result[name.Substring(0, name.Length - 5) + "_R"] = i;
                }
            }
            return result;
        }

        public static int IndexOf(string name) => name != null && Indices.TryGetValue(name, out int index) ? index : -1;

        /// <summary>眼区的区域位（眼睑 + 眼球 + 眉）。</summary>
        public const HoFaceRegion EyeRegion = HoFaceRegion.Eyelids | HoFaceRegion.Gaze | HoFaceRegion.Brows;

        /// <summary>唇区的区域位（嘴 + 颊鼻）。</summary>
        public const HoFaceRegion LipRegion = HoFaceRegion.Mouth | HoFaceRegion.Cheeks;

        /// <summary>这个键属于哪个区域子树。</summary>
        public static HoFaceGate Gate(string shape) =>
            (Region(shape) & EyeRegion) != 0 ? HoFaceGate.Eye : HoFaceGate.Lip;

        public static HoFaceRegion Region(string shape)
        {
            if (shape.StartsWith("eyeLook", StringComparison.Ordinal)) return HoFaceRegion.Gaze;
            if (shape.StartsWith("eye", StringComparison.Ordinal)) return HoFaceRegion.Eyelids;
            if (shape.StartsWith("brow", StringComparison.Ordinal)) return HoFaceRegion.Brows;
            if (shape.StartsWith("cheek", StringComparison.Ordinal) || shape.StartsWith("nose", StringComparison.Ordinal)) return HoFaceRegion.Cheeks;
            return HoFaceRegion.Mouth;
        }

        /// <summary>平滑分组：眼球 / 眼睑 / 嘴各自一组，眉、脸颊、鼻子归到「其它」。</summary>
        public static HoFaceSmoothGroup SmoothGroup(string shape)
        {
            switch (Region(shape))
            {
                case HoFaceRegion.Gaze: return HoFaceSmoothGroup.Gaze;
                case HoFaceRegion.Eyelids: return HoFaceSmoothGroup.Eyelids;
                case HoFaceRegion.Mouth: return HoFaceSmoothGroup.Mouth;
                default: return HoFaceSmoothGroup.Other;
            }
        }

        public static List<HoFaceChannel> CreateDefaults()
        {
            var channels = new List<HoFaceChannel>();
            foreach (string name in Names) channels.Add(new HoFaceChannel { shape = name, parameter = "ARKit/" + name });
            return channels;
        }
    }
}
