using System;
using System.Collections.Generic;
using UnityEngine;

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
    /// 平滑分组。对齐参考实现的 <c>OSCm/Sensitivity</c> 分组手感：眼睑、眼球、嘴各自一组，其余归一组 ——
    /// 而不是 52 个键共用一个平滑系数。实践中那不够用：眼球要跟得紧、眼睑要稳、嘴要更黏，
    /// 三者的合适时长能差一个数量级。
    /// </summary>
    public enum HoFaceSmoothGroup { Eyelids, Gaze, Mouth, Other }

    /// <summary>
    /// 一路**输入**：源键 + 它怎么被整形。
    ///
    /// 注意这里**没有参数名、也没有增益标量**：参数名归中间层资产（输出行里那个 `parameter`），
    /// 增益归下面这条**输入曲线** —— 这正是 VBridger 的 Input Curve："这张脸打不满 1，
    /// 那就把 0.6 抬成 1"。它是**这台设备/这张脸**的校准，跟用哪份控制器模板无关，
    /// 所以留在组件上、跨中间层保留。
    /// </summary>
    [Serializable]
    public sealed class HoFaceChannel
    {
        public string shape;
        public HoFaceInputMode mode;
        public float manual;
        public float neutral;
        [Tooltip("输入曲线：横轴 = 原始输入（0..1），纵轴 = 整形后的输入（0..1）。\n"
            + "打不满就把它抬起来；想压掉小抖动就在开头压平。范围之外按端点算。")]
        public AnimationCurve inputCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
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

        /// <summary>
        /// **只认规范名**（不接受 `_L/_R` 线名别名）。表达式取变量时用它：
        /// `eyeBlink_L` 应该解析成"手机发来的线名原值"，而不是通道 `eyeBlinkLeft`。
        /// </summary>
        public static int CanonicalIndexOf(string name) => Array.IndexOf(Names, name);

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

        /// <summary>
        /// 组件上默认建哪些通道：52 个形态键（**它们是"规范化后的形态键名"**）。
        /// 手机发来的线名（`eyeBlink_L` / `EyeBlinkLeft`）不在这里 —— 那是中间层配置里**输入行**的活儿。
        /// </summary>
        public static List<HoFaceChannel> CreateDefaults()
        {
            var channels = new List<HoFaceChannel>();
            foreach (string name in Names) channels.Add(new HoFaceChannel { shape = name });
            return channels;
        }
    }
}
