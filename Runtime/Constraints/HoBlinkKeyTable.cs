using System.Collections.Generic;

namespace Hollow.HoUnityTools.Constraints
{
    public enum HoBlinkKeySemantic
    {
        EyelidClosed,
        EyelidSquint,
        EyelidWide,
        GazeUp,
        GazeDown,
        GazeLeft,
        GazeRight,
        GazeIn,
        GazeOut
    }

    /// <summary>内置键名表的一条：键名 + 命名规范标签 + 语义 + 侧别。</summary>
    public readonly struct HoBlinkKeyEntry
    {
        public readonly string Name;
        public readonly string Standard;
        public readonly HoBlinkKeySemantic Semantic;
        public readonly HoBlinkSide Side;

        public HoBlinkKeyEntry(string name, string standard, HoBlinkKeySemantic semantic, HoBlinkSide side)
        {
            Name = name;
            Standard = standard;
            Semantic = semantic;
            Side = side;
        }

        /// <summary>下拉里的显示文本：`Blink (VRM1)` / `Blink_L (VRM, 左)`。</summary>
        public string Display
        {
            get
            {
                switch (Side)
                {
                    case HoBlinkSide.Left:
                        return Name + " (" + Standard + ", 左)";
                    case HoBlinkSide.Right:
                        return Name + " (" + Standard + ", 右)";
                    default:
                        return Name + " (" + Standard + ")";
                }
            }
        }
    }

    /// <summary>
    /// 写死的形态键名表，只覆盖眼睑与凝视。
    /// 不做目录查询、不做语义推断：这张表只用来给下拉提供候选项和规范标签，
    /// 键名本身在组件里以 string 存储，用户随时可以手填别的名字。
    /// </summary>
    public static class HoBlinkKeyTable
    {
        private const string Arkit = "ARKit";
        private const string Vrm = "VRM";
        private const string Vrm1 = "VRM1";
        private const string Vrchat = "VRChat";
        private const string Mmd = "MMD";
        private const string Meta = "Meta Movement";
        private const string Pico = "PICO";
        private const string Sranipal = "VIVE SRanipal";
        private const string OpenXr = "VIVE OpenXR";
        private const string UnifiedBase = "UnifiedExpressions Base";
        private const string UnifiedBlend = "UnifiedExpressions Blend";

        public static readonly HoBlinkKeyEntry[] Entries =
        {
            // ── 眼睑：闭合 ─────────────────────────────────────────────
            new HoBlinkKeyEntry("Blink", Vrm, HoBlinkKeySemantic.EyelidClosed, HoBlinkSide.Both),
            new HoBlinkKeyEntry("Blink", Vrm1, HoBlinkKeySemantic.EyelidClosed, HoBlinkSide.Both),
            new HoBlinkKeyEntry("vrc.blink", Vrchat, HoBlinkKeySemantic.EyelidClosed, HoBlinkSide.Both),
            new HoBlinkKeyEntry("まばたき", Mmd, HoBlinkKeySemantic.EyelidClosed, HoBlinkSide.Both),
            new HoBlinkKeyEntry("EyeClosed", UnifiedBlend, HoBlinkKeySemantic.EyelidClosed, HoBlinkSide.Both),

            new HoBlinkKeyEntry("Blink_L", Vrm, HoBlinkKeySemantic.EyelidClosed, HoBlinkSide.Left),
            new HoBlinkKeyEntry("BlinkLeft", Vrm1, HoBlinkKeySemantic.EyelidClosed, HoBlinkSide.Left),
            new HoBlinkKeyEntry("eyeBlinkLeft", Arkit, HoBlinkKeySemantic.EyelidClosed, HoBlinkSide.Left),
            new HoBlinkKeyEntry("EYES_CLOSED_L", Meta, HoBlinkKeySemantic.EyelidClosed, HoBlinkSide.Left),
            new HoBlinkKeyEntry("EyeBlink_L", Pico, HoBlinkKeySemantic.EyelidClosed, HoBlinkSide.Left),
            new HoBlinkKeyEntry("Eye_Left_Blink", Sranipal, HoBlinkKeySemantic.EyelidClosed, HoBlinkSide.Left),
            new HoBlinkKeyEntry("XR_EYE_EXPRESSION_LEFT_BLINK_HTC", OpenXr, HoBlinkKeySemantic.EyelidClosed, HoBlinkSide.Left),
            new HoBlinkKeyEntry("EyeClosedLeft", UnifiedBase, HoBlinkKeySemantic.EyelidClosed, HoBlinkSide.Left),
            new HoBlinkKeyEntry("ウィンク", Mmd, HoBlinkKeySemantic.EyelidClosed, HoBlinkSide.Left),

            new HoBlinkKeyEntry("Blink_R", Vrm, HoBlinkKeySemantic.EyelidClosed, HoBlinkSide.Right),
            new HoBlinkKeyEntry("BlinkRight", Vrm1, HoBlinkKeySemantic.EyelidClosed, HoBlinkSide.Right),
            new HoBlinkKeyEntry("eyeBlinkRight", Arkit, HoBlinkKeySemantic.EyelidClosed, HoBlinkSide.Right),
            new HoBlinkKeyEntry("EYES_CLOSED_R", Meta, HoBlinkKeySemantic.EyelidClosed, HoBlinkSide.Right),
            new HoBlinkKeyEntry("EyeBlink_R", Pico, HoBlinkKeySemantic.EyelidClosed, HoBlinkSide.Right),
            new HoBlinkKeyEntry("Eye_Right_Blink", Sranipal, HoBlinkKeySemantic.EyelidClosed, HoBlinkSide.Right),
            new HoBlinkKeyEntry("XR_EYE_EXPRESSION_RIGHT_BLINK_HTC", OpenXr, HoBlinkKeySemantic.EyelidClosed, HoBlinkSide.Right),
            new HoBlinkKeyEntry("EyeClosedRight", UnifiedBase, HoBlinkKeySemantic.EyelidClosed, HoBlinkSide.Right),
            new HoBlinkKeyEntry("ウィンク右", Mmd, HoBlinkKeySemantic.EyelidClosed, HoBlinkSide.Right),

            // ── 眼睑：眯眼 ─────────────────────────────────────────────
            new HoBlinkKeyEntry("EyeSquint", UnifiedBlend, HoBlinkKeySemantic.EyelidSquint, HoBlinkSide.Both),
            new HoBlinkKeyEntry("eyeSquintLeft", Arkit, HoBlinkKeySemantic.EyelidSquint, HoBlinkSide.Left),
            new HoBlinkKeyEntry("eyeSquintRight", Arkit, HoBlinkKeySemantic.EyelidSquint, HoBlinkSide.Right),
            new HoBlinkKeyEntry("EyeSquint_L", Pico, HoBlinkKeySemantic.EyelidSquint, HoBlinkSide.Left),
            new HoBlinkKeyEntry("EyeSquint_R", Pico, HoBlinkKeySemantic.EyelidSquint, HoBlinkSide.Right),
            new HoBlinkKeyEntry("Eye_Left_Squeeze", Sranipal, HoBlinkKeySemantic.EyelidSquint, HoBlinkSide.Left),
            new HoBlinkKeyEntry("Eye_Right_Squeeze", Sranipal, HoBlinkKeySemantic.EyelidSquint, HoBlinkSide.Right),
            new HoBlinkKeyEntry("XR_EYE_EXPRESSION_LEFT_SQUEEZE_HTC", OpenXr, HoBlinkKeySemantic.EyelidSquint, HoBlinkSide.Left),
            new HoBlinkKeyEntry("XR_EYE_EXPRESSION_RIGHT_SQUEEZE_HTC", OpenXr, HoBlinkKeySemantic.EyelidSquint, HoBlinkSide.Right),
            new HoBlinkKeyEntry("LID_TIGHTENER_L", Meta, HoBlinkKeySemantic.EyelidSquint, HoBlinkSide.Left),
            new HoBlinkKeyEntry("LID_TIGHTENER_R", Meta, HoBlinkKeySemantic.EyelidSquint, HoBlinkSide.Right),
            new HoBlinkKeyEntry("EyeSquintLeft", UnifiedBase, HoBlinkKeySemantic.EyelidSquint, HoBlinkSide.Left),
            new HoBlinkKeyEntry("EyeSquintRight", UnifiedBase, HoBlinkKeySemantic.EyelidSquint, HoBlinkSide.Right),

            // ── 眼睑：睁大 ─────────────────────────────────────────────
            new HoBlinkKeyEntry("EyeWide", UnifiedBlend, HoBlinkKeySemantic.EyelidWide, HoBlinkSide.Both),
            new HoBlinkKeyEntry("eyeWideLeft", Arkit, HoBlinkKeySemantic.EyelidWide, HoBlinkSide.Left),
            new HoBlinkKeyEntry("eyeWideRight", Arkit, HoBlinkKeySemantic.EyelidWide, HoBlinkSide.Right),
            new HoBlinkKeyEntry("EyeWide_L", Pico, HoBlinkKeySemantic.EyelidWide, HoBlinkSide.Left),
            new HoBlinkKeyEntry("EyeWide_R", Pico, HoBlinkKeySemantic.EyelidWide, HoBlinkSide.Right),
            new HoBlinkKeyEntry("Eye_Left_Wide", Sranipal, HoBlinkKeySemantic.EyelidWide, HoBlinkSide.Left),
            new HoBlinkKeyEntry("Eye_Right_Wide", Sranipal, HoBlinkKeySemantic.EyelidWide, HoBlinkSide.Right),
            new HoBlinkKeyEntry("XR_EYE_EXPRESSION_LEFT_WIDE_HTC", OpenXr, HoBlinkKeySemantic.EyelidWide, HoBlinkSide.Left),
            new HoBlinkKeyEntry("XR_EYE_EXPRESSION_RIGHT_WIDE_HTC", OpenXr, HoBlinkKeySemantic.EyelidWide, HoBlinkSide.Right),
            new HoBlinkKeyEntry("UPPER_LID_RAISER_L", Meta, HoBlinkKeySemantic.EyelidWide, HoBlinkSide.Left),
            new HoBlinkKeyEntry("UPPER_LID_RAISER_R", Meta, HoBlinkKeySemantic.EyelidWide, HoBlinkSide.Right),
            new HoBlinkKeyEntry("EyeWideLeft", UnifiedBase, HoBlinkKeySemantic.EyelidWide, HoBlinkSide.Left),
            new HoBlinkKeyEntry("EyeWideRight", UnifiedBase, HoBlinkKeySemantic.EyelidWide, HoBlinkSide.Right),

            // ── 凝视：双眼 ─────────────────────────────────────────────
            new HoBlinkKeyEntry("LookUp", Vrm, HoBlinkKeySemantic.GazeUp, HoBlinkSide.Both),
            new HoBlinkKeyEntry("LookUp", Vrm1, HoBlinkKeySemantic.GazeUp, HoBlinkSide.Both),
            new HoBlinkKeyEntry("vrc.looking_up", Vrchat, HoBlinkKeySemantic.GazeUp, HoBlinkSide.Both),
            new HoBlinkKeyEntry("LookDown", Vrm, HoBlinkKeySemantic.GazeDown, HoBlinkSide.Both),
            new HoBlinkKeyEntry("LookDown", Vrm1, HoBlinkKeySemantic.GazeDown, HoBlinkSide.Both),
            new HoBlinkKeyEntry("vrc.looking_down", Vrchat, HoBlinkKeySemantic.GazeDown, HoBlinkSide.Both),
            new HoBlinkKeyEntry("LookLeft", Vrm, HoBlinkKeySemantic.GazeLeft, HoBlinkSide.Both),
            new HoBlinkKeyEntry("LookLeft", Vrm1, HoBlinkKeySemantic.GazeLeft, HoBlinkSide.Both),
            new HoBlinkKeyEntry("LookRight", Vrm, HoBlinkKeySemantic.GazeRight, HoBlinkSide.Both),
            new HoBlinkKeyEntry("LookRight", Vrm1, HoBlinkKeySemantic.GazeRight, HoBlinkSide.Both),

            // ── 凝视：上 / 下（左右眼） ─────────────────────────────────
            new HoBlinkKeyEntry("eyeLookUpLeft", Arkit, HoBlinkKeySemantic.GazeUp, HoBlinkSide.Left),
            new HoBlinkKeyEntry("eyeLookUpRight", Arkit, HoBlinkKeySemantic.GazeUp, HoBlinkSide.Right),
            new HoBlinkKeyEntry("EYES_LOOK_UP_L", Meta, HoBlinkKeySemantic.GazeUp, HoBlinkSide.Left),
            new HoBlinkKeyEntry("EYES_LOOK_UP_R", Meta, HoBlinkKeySemantic.GazeUp, HoBlinkSide.Right),
            new HoBlinkKeyEntry("EyeLookUp_L", Pico, HoBlinkKeySemantic.GazeUp, HoBlinkSide.Left),
            new HoBlinkKeyEntry("EyeLookUp_R", Pico, HoBlinkKeySemantic.GazeUp, HoBlinkSide.Right),
            new HoBlinkKeyEntry("Eye_Left_Up", Sranipal, HoBlinkKeySemantic.GazeUp, HoBlinkSide.Left),
            new HoBlinkKeyEntry("Eye_Right_Up", Sranipal, HoBlinkKeySemantic.GazeUp, HoBlinkSide.Right),
            new HoBlinkKeyEntry("XR_EYE_EXPRESSION_LEFT_UP_HTC", OpenXr, HoBlinkKeySemantic.GazeUp, HoBlinkSide.Left),
            new HoBlinkKeyEntry("XR_EYE_EXPRESSION_RIGHT_UP_HTC", OpenXr, HoBlinkKeySemantic.GazeUp, HoBlinkSide.Right),
            new HoBlinkKeyEntry("EyeLookUpLeft", UnifiedBase, HoBlinkKeySemantic.GazeUp, HoBlinkSide.Left),
            new HoBlinkKeyEntry("EyeLookUpRight", UnifiedBase, HoBlinkKeySemantic.GazeUp, HoBlinkSide.Right),

            new HoBlinkKeyEntry("eyeLookDownLeft", Arkit, HoBlinkKeySemantic.GazeDown, HoBlinkSide.Left),
            new HoBlinkKeyEntry("eyeLookDownRight", Arkit, HoBlinkKeySemantic.GazeDown, HoBlinkSide.Right),
            new HoBlinkKeyEntry("EYES_LOOK_DOWN_L", Meta, HoBlinkKeySemantic.GazeDown, HoBlinkSide.Left),
            new HoBlinkKeyEntry("EYES_LOOK_DOWN_R", Meta, HoBlinkKeySemantic.GazeDown, HoBlinkSide.Right),
            new HoBlinkKeyEntry("EyeLookDown_L", Pico, HoBlinkKeySemantic.GazeDown, HoBlinkSide.Left),
            new HoBlinkKeyEntry("EyeLookDown_R", Pico, HoBlinkKeySemantic.GazeDown, HoBlinkSide.Right),
            new HoBlinkKeyEntry("Eye_Left_Down", Sranipal, HoBlinkKeySemantic.GazeDown, HoBlinkSide.Left),
            new HoBlinkKeyEntry("Eye_Right_Down", Sranipal, HoBlinkKeySemantic.GazeDown, HoBlinkSide.Right),
            new HoBlinkKeyEntry("XR_EYE_EXPRESSION_LEFT_DOWN_HTC", OpenXr, HoBlinkKeySemantic.GazeDown, HoBlinkSide.Left),
            new HoBlinkKeyEntry("XR_EYE_EXPRESSION_RIGHT_DOWN_HTC", OpenXr, HoBlinkKeySemantic.GazeDown, HoBlinkSide.Right),
            new HoBlinkKeyEntry("EyeLookDownLeft", UnifiedBase, HoBlinkKeySemantic.GazeDown, HoBlinkSide.Left),
            new HoBlinkKeyEntry("EyeLookDownRight", UnifiedBase, HoBlinkKeySemantic.GazeDown, HoBlinkSide.Right),

            // ── 凝视：左右族 ───────────────────────────────────────────
            new HoBlinkKeyEntry("EYES_LOOK_LEFT_L", Meta, HoBlinkKeySemantic.GazeLeft, HoBlinkSide.Left),
            new HoBlinkKeyEntry("EYES_LOOK_LEFT_R", Meta, HoBlinkKeySemantic.GazeLeft, HoBlinkSide.Right),
            new HoBlinkKeyEntry("EYES_LOOK_RIGHT_L", Meta, HoBlinkKeySemantic.GazeRight, HoBlinkSide.Left),
            new HoBlinkKeyEntry("EYES_LOOK_RIGHT_R", Meta, HoBlinkKeySemantic.GazeRight, HoBlinkSide.Right),
            new HoBlinkKeyEntry("Eye_Left_Left", Sranipal, HoBlinkKeySemantic.GazeLeft, HoBlinkSide.Left),
            new HoBlinkKeyEntry("Eye_Right_Left", Sranipal, HoBlinkKeySemantic.GazeLeft, HoBlinkSide.Right),
            new HoBlinkKeyEntry("Eye_Left_Right", Sranipal, HoBlinkKeySemantic.GazeRight, HoBlinkSide.Left),
            new HoBlinkKeyEntry("Eye_Right_Right", Sranipal, HoBlinkKeySemantic.GazeRight, HoBlinkSide.Right),

            // ── 凝视：内外族（相对眼球，别和左右族混用） ────────────────
            new HoBlinkKeyEntry("eyeLookInLeft", Arkit, HoBlinkKeySemantic.GazeIn, HoBlinkSide.Left),
            new HoBlinkKeyEntry("eyeLookInRight", Arkit, HoBlinkKeySemantic.GazeIn, HoBlinkSide.Right),
            new HoBlinkKeyEntry("eyeLookOutLeft", Arkit, HoBlinkKeySemantic.GazeOut, HoBlinkSide.Left),
            new HoBlinkKeyEntry("eyeLookOutRight", Arkit, HoBlinkKeySemantic.GazeOut, HoBlinkSide.Right),
            new HoBlinkKeyEntry("EyeLookIn_L", Pico, HoBlinkKeySemantic.GazeIn, HoBlinkSide.Left),
            new HoBlinkKeyEntry("EyeLookIn_R", Pico, HoBlinkKeySemantic.GazeIn, HoBlinkSide.Right),
            new HoBlinkKeyEntry("EyeLookOut_L", Pico, HoBlinkKeySemantic.GazeOut, HoBlinkSide.Left),
            new HoBlinkKeyEntry("EyeLookOut_R", Pico, HoBlinkKeySemantic.GazeOut, HoBlinkSide.Right),
            new HoBlinkKeyEntry("XR_EYE_EXPRESSION_LEFT_IN_HTC", OpenXr, HoBlinkKeySemantic.GazeIn, HoBlinkSide.Left),
            new HoBlinkKeyEntry("XR_EYE_EXPRESSION_RIGHT_IN_HTC", OpenXr, HoBlinkKeySemantic.GazeIn, HoBlinkSide.Right),
            new HoBlinkKeyEntry("XR_EYE_EXPRESSION_LEFT_OUT_HTC", OpenXr, HoBlinkKeySemantic.GazeOut, HoBlinkSide.Left),
            new HoBlinkKeyEntry("XR_EYE_EXPRESSION_RIGHT_OUT_HTC", OpenXr, HoBlinkKeySemantic.GazeOut, HoBlinkSide.Right),
            new HoBlinkKeyEntry("EyeLookInLeft", UnifiedBase, HoBlinkKeySemantic.GazeIn, HoBlinkSide.Left),
            new HoBlinkKeyEntry("EyeLookInRight", UnifiedBase, HoBlinkKeySemantic.GazeIn, HoBlinkSide.Right),
            new HoBlinkKeyEntry("EyeLookOutLeft", UnifiedBase, HoBlinkKeySemantic.GazeOut, HoBlinkSide.Left),
            new HoBlinkKeyEntry("EyeLookOutRight", UnifiedBase, HoBlinkKeySemantic.GazeOut, HoBlinkSide.Right)
        };

        /// <summary>按语义取条目（用于预设与下拉分组）。</summary>
        public static List<HoBlinkKeyEntry> BySemantic(HoBlinkKeySemantic semantic, HoBlinkSide side = HoBlinkSide.Both)
        {
            List<HoBlinkKeyEntry> result = new List<HoBlinkKeyEntry>();
            for (int i = 0; i < Entries.Length; i++)
            {
                if (Entries[i].Semantic != semantic)
                {
                    continue;
                }

                if (side != HoBlinkSide.Both && Entries[i].Side != side)
                {
                    continue;
                }

                result.Add(Entries[i]);
            }

            return result;
        }

        /// <summary>查一个键名在表里的规范标签（手填的键名也能显示标签）。</summary>
        public static bool TryGetStandard(string keyName, out string standard, out HoBlinkKeySemantic semantic, out HoBlinkSide side)
        {
            standard = string.Empty;
            semantic = HoBlinkKeySemantic.EyelidClosed;
            side = HoBlinkSide.Both;
            if (string.IsNullOrEmpty(keyName))
            {
                return false;
            }

            for (int i = 0; i < Entries.Length; i++)
            {
                if (string.Equals(Entries[i].Name, keyName, System.StringComparison.OrdinalIgnoreCase))
                {
                    standard = Entries[i].Standard;
                    semantic = Entries[i].Semantic;
                    side = Entries[i].Side;
                    return true;
                }
            }

            return false;
        }
    }
}
