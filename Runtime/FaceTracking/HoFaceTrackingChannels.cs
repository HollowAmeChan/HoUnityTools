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

        public static HoFaceRegion Region(string shape)
        {
            if (shape.StartsWith("eyeLook", StringComparison.Ordinal)) return HoFaceRegion.Gaze;
            if (shape.StartsWith("eye", StringComparison.Ordinal)) return HoFaceRegion.Eyelids;
            if (shape.StartsWith("brow", StringComparison.Ordinal)) return HoFaceRegion.Brows;
            if (shape.StartsWith("cheek", StringComparison.Ordinal) || shape.StartsWith("nose", StringComparison.Ordinal)) return HoFaceRegion.Cheeks;
            return HoFaceRegion.Mouth;
        }

        public static List<HoFaceChannel> CreateDefaults()
        {
            var channels = new List<HoFaceChannel>();
            foreach (string name in Names) channels.Add(new HoFaceChannel { shape = name, parameter = "ARKit/" + name });
            return channels;
        }
    }
}
