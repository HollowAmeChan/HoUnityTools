using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Constraints
{
    /// <summary>
    /// Ho 约束面板的**设计令牌**：尺寸、颜色、文字样式、生成出来的贴图。
    ///
    /// 规矩只有一条：**面板里不许再写死颜色和宽度**，一律从这里取。
    /// 之前每个 Editor 各自调 `EditorGUILayout.FloatField`、各写各的 `GUILayout.Width`，
    /// 结果就是左右不对齐、数字框被 label 吃掉、一个字占半行 —— 那不是"没调好"，是没有栅格。
    ///
    /// 贴图全是运行时生成的 4×4（描边）与 64×1（渐变），九宫格拉伸，不引入任何资源文件、不落盘。
    /// </summary>
    internal static class HoConstraintEditorTheme
    {
        // ── 尺寸（所有面板共用同一个栅格）────────────────────────────────
        public const float RowHeight = 20.0f;
        public const float RowHeightTight = 18.0f;
        public const float ControlHeight = 18.0f;
        public const float Gutter = 6.0f;
        public const float Indent = 12.0f;
        public const float SectionHeight = 28.0f;
        public const float AccentBarWidth = 3.0f;

        /// <summary>标签列三档：常规 / 两字 / 单字。</summary>
        public const float LabelWidth = 56.0f;
        public const float LabelWidthSm = 30.0f;
        public const float LabelWidthXs = 18.0f;

        public const float FieldWidth = 42.0f;
        public const float FieldWidthWide = 52.0f;
        public const float MeterBarWidth = 54.0f;
        public const float MeterValueWidth = 38.0f;

        // ── 颜色 ────────────────────────────────────────────────────────
        public static readonly Color CardColor = Pick(0.247f, 0.945f);
        public static readonly Color CardAltColor = Pick(0.271f, 0.918f);
        public static readonly Color LineColor = Pick(0.173f, 0.792f);
        public static readonly Color SeparatorColor = Pick(0.290f, 0.831f);
        public static readonly Color WellColor = Pick(0.180f, 0.878f);

        public static readonly Color TextColor = Pick(0.831f, 0.153f);
        public static readonly Color TextDimColor = Pick(0.627f, 0.420f);
        public static readonly Color TextFaintColor = Pick(0.486f, 0.545f);
        public static readonly Color TextBrightColor = Pick(0.929f, 0.086f);

        public static readonly Color AccentMesh = new Color(0.290f, 0.608f, 1.000f);
        public static readonly Color AccentBlink = new Color(0.247f, 0.816f, 0.549f);
        public static readonly Color AccentRules = new Color(0.773f, 0.545f, 1.000f);
        public static readonly Color AccentDebug = new Color(0.604f, 0.627f, 0.659f);
        public static readonly Color AccentDriver = new Color(0.361f, 0.706f, 0.961f);
        public static readonly Color AccentOutput = new Color(0.420f, 0.839f, 0.561f);

        public static readonly Color WarningColor = new Color(0.910f, 0.639f, 0.239f);
        public static readonly Color ErrorColor = new Color(0.898f, 0.325f, 0.294f);

        // ── 文字样式 ────────────────────────────────────────────────────
        private static GUIStyle label;
        private static GUIStyle labelDim;
        private static GUIStyle value;
        private static GUIStyle caption;
        private static GUIStyle bold;
        private static GUIStyle sectionTitle;
        private static GUIStyle sectionSummary;
        private static GUIStyle field;
        private static GUIStyle fieldMissing;
        private static GUIStyle numberField;
        private static GUIStyle segmentOn;
        private static GUIStyle segmentOff;
        private static GUIStyle button;
        private static GUIStyle buttonPrimary;
        private static GUIStyle buttonDanger;
        private static GUIStyle card;
        private static GUIStyle cardAlt;
        private static GUIStyle foldout;
        private static GUIStyle iconButton;
        private static GUIStyle checkGlyph;

        public static GUIStyle Label => Ready(ref label, () => TextStyle(EditorStyles.label, TextDimColor, 11));
        public static GUIStyle Value => Ready(ref value, () => TextStyle(EditorStyles.label, TextBrightColor, 11));
        public static GUIStyle Caption => Ready(ref caption, () => TextStyle(EditorStyles.miniLabel, TextFaintColor, 10));
        public static GUIStyle Bold => Ready(ref bold, () => TextStyle(EditorStyles.boldLabel, TextColor, 12));
        public static GUIStyle Foldout => Ready(ref foldout, () => TextStyle(EditorStyles.foldout, TextColor, 11));

        public static GUIStyle LabelDim => Ready(ref labelDim, () => TextStyle(EditorStyles.miniLabel, TextFaintColor, 11));

        public static GUIStyle SectionTitle => Ready(ref sectionTitle, () =>
        {
            GUIStyle style = TextStyle(EditorStyles.boldLabel, TextColor, 12);
            style.alignment = TextAnchor.MiddleLeft;
            style.clipping = TextClipping.Clip;
            return style;
        });

        public static GUIStyle SectionSummary => Ready(ref sectionSummary, () =>
        {
            GUIStyle style = TextStyle(EditorStyles.miniLabel, TextDimColor, 10);
            style.alignment = TextAnchor.MiddleRight;
            style.clipping = TextClipping.Clip;
            return style;
        });

        public static GUIStyle Field => Ready(ref field, () =>
        {
            GUIStyle style = new GUIStyle(EditorStyles.textField)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(5, 4, 0, 0),
                border = new RectOffset(1, 1, 1, 1),
                normal = { background = Box(WellColor, LineColor) }
            };
            style.normal.textColor = TextColor;
            return style;
        });

        /// <summary>键名没解析到任何绑定时的输入框样式（描边变提醒色）。</summary>
        public static GUIStyle FieldMissing => Ready(ref fieldMissing, () =>
        {
            GUIStyle style = new GUIStyle(Field)
            {
                normal = { background = Box(WellColor, new Color(0.42f, 0.29f, 0.12f)) }
            };
            style.normal.textColor = new Color(0.941f, 0.698f, 0.369f);
            return style;
        });

        public static GUIStyle NumberField => Ready(ref numberField, () =>
        {
            GUIStyle style = new GUIStyle(Field)
            {
                alignment = TextAnchor.MiddleRight,
                padding = new RectOffset(3, 4, 0, 0)
            };
            return style;
        });

        public static GUIStyle SegmentOn => Ready(ref segmentOn, () => SegmentStyle(true));
        public static GUIStyle SegmentOff => Ready(ref segmentOff, () => SegmentStyle(false));

        public static GUIStyle Button => Ready(ref button, () => ButtonStyle(false, false));
        public static GUIStyle ButtonPrimary => Ready(ref buttonPrimary, () => ButtonStyle(true, false));
        public static GUIStyle ButtonDanger => Ready(ref buttonDanger, () => ButtonStyle(false, true));

        /// <summary>18×18 的图标按钮（▾ / ✕ / ⋮）：内边距小，居中画符号。</summary>
        public static GUIStyle IconButton => Ready(ref iconButton, () =>
        {
            GUIStyle style = ButtonStyle(false, false);
            style.padding = new RectOffset(1, 1, 0, 0);
            style.alignment = TextAnchor.MiddleCenter;
            return style;
        });

        public static GUIStyle Card => Ready(ref card, () => CardStyle(CardColor));
        public static GUIStyle CardAlt => Ready(ref cardAlt, () => CardStyle(CardAltColor));

        /// <summary>勾选框里的那个勾。</summary>
        public static GUIStyle CheckGlyph => Ready(ref checkGlyph, () =>
        {
            GUIStyle style = TextStyle(EditorStyles.miniLabel, new Color(0.812f, 0.965f, 0.886f), 10);
            style.alignment = TextAnchor.MiddleCenter;
            return style;
        });

        // ── 分区头背景：按强调色生成的横向渐变（缓存）─────────────────────
        private static readonly Dictionary<Color, Texture2D> sectionTextures = new Dictionary<Color, Texture2D>();

        public static Texture2D SectionTexture(Color accent)
        {
            Color key = accent;
            if (sectionTextures.TryGetValue(key, out Texture2D cached) && cached != null)
            {
                return cached;
            }

            float strength = EditorGUIUtility.isProSkin ? 0.30f : 0.22f;
            Color left = new Color(accent.r, accent.g, accent.b, strength);
            Color mid = new Color(accent.r, accent.g, accent.b, strength * 0.26f);
            Color right = new Color(accent.r, accent.g, accent.b, 0.0f);
            Texture2D texture = Gradient(left, mid, right);
            sectionTextures[key] = texture;
            return texture;
        }

        // ── 生成贴图 ────────────────────────────────────────────────────
        private static Texture2D boxTexture;
        private static Texture2D boxAltTexture;
        private static Texture2D segmentOnTexture;
        private static Texture2D segmentOffTexture;
        private static Texture2D buttonTexture;
        private static Texture2D buttonPrimaryTexture;
        private static Texture2D buttonDangerTexture;

        private static Texture2D Box(Color fill, Color border)
        {
            const int Size = 4;
            Texture2D texture = NewTexture(Size, Size);
            Color[] pixels = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    bool edge = x == 0 || y == 0 || x == Size - 1 || y == Size - 1;
                    pixels[(y * Size) + x] = edge ? border : fill;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static Texture2D Gradient(Color left, Color mid, Color right)
        {
            const int Width = 64;
            Texture2D texture = NewTexture(Width, 1);
            Color[] pixels = new Color[Width];
            for (int x = 0; x < Width; x++)
            {
                float t = x / (float)(Width - 1);
                pixels[x] = t < 0.6f
                    ? Color.Lerp(left, mid, t / 0.6f)
                    : Color.Lerp(mid, right, (t - 0.6f) / 0.4f);
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static Texture2D NewTexture(int width, int height)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        private static GUIStyle CardStyle(Color fill)
        {
            Texture2D texture = fill == CardColor
                ? boxTexture ??= Box(CardColor, LineColor)
                : boxAltTexture ??= Box(CardAltColor, LineColor);

            return new GUIStyle
            {
                normal = { background = texture },
                border = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(7, 7, 6, 6),
                margin = new RectOffset(0, 0, 4, 0),
                stretchWidth = true
            };
        }

        private static GUIStyle SegmentStyle(bool on)
        {
            Texture2D texture = on
                ? segmentOnTexture ??= Box(
                    Pick(new Color(0.243f, 0.361f, 0.494f), new Color(0.741f, 0.831f, 0.925f)),
                    Pick(new Color(0.306f, 0.482f, 0.651f), new Color(0.588f, 0.686f, 0.792f)))
                : segmentOffTexture ??= Box(Pick(0.200f, 0.878f), LineColor);

            GUIStyle style = new GUIStyle
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(6, 6, 0, 0),
                normal = { background = texture },
                stretchWidth = false,
                stretchHeight = false
            };
            style.normal.textColor = on
                ? Pick(new Color(0.910f, 0.945f, 0.980f), new Color(0.086f, 0.145f, 0.204f))
                : TextDimColor;
            return style;
        }

        private static GUIStyle ButtonStyle(bool primary, bool danger)
        {
            Texture2D texture;
            Color text;
            if (primary)
            {
                texture = buttonPrimaryTexture ??= Box(
                    Pick(new Color(0.216f, 0.325f, 0.435f), new Color(0.855f, 0.898f, 0.949f)),
                    Pick(new Color(0.306f, 0.482f, 0.651f), new Color(0.588f, 0.686f, 0.792f)));
                text = Pick(new Color(0.914f, 0.949f, 0.980f), new Color(0.086f, 0.145f, 0.204f));
            }
            else if (danger)
            {
                texture = buttonDangerTexture ??= Box(
                    Pick(0.227f, 0.929f),
                    Pick(new Color(0.416f, 0.231f, 0.220f), new Color(0.718f, 0.545f, 0.533f)));
                text = Pick(new Color(0.922f, 0.690f, 0.675f), new Color(0.552f, 0.157f, 0.137f));
            }
            else
            {
                texture = buttonTexture ??= Box(Pick(0.235f, 0.898f), Pick(0.333f, 0.741f));
                text = TextColor;
            }

            GUIStyle style = new GUIStyle
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(9, 9, 0, 0),
                normal = { background = texture },
                stretchWidth = false,
                stretchHeight = false
            };
            style.normal.textColor = text;
            style.hover.textColor = Pick(1.0f, 0.0f);
            return style;
        }

        private static GUIStyle TextStyle(GUIStyle basis, Color color, int fontSize)
        {
            GUIStyle style = new GUIStyle(basis)
            {
                fontSize = fontSize,
                richText = false,
                clipping = TextClipping.Clip,
                wordWrap = false,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
            style.normal.textColor = color;
            style.hover.textColor = color;
            style.active.textColor = color;
            return style;
        }

        private static Color Pick(float pro, float light)
        {
            return Pick(new Color(pro, pro, pro), new Color(light, light, light));
        }

        private static Color Pick(Color pro, Color light)
        {
            return EditorGUIUtility.isProSkin ? pro : light;
        }

        private static GUIStyle Ready(ref GUIStyle style, System.Func<GUIStyle> factory)
        {
            return style ??= factory();
        }

        /// <summary>量一段文字要多宽（分段胶囊、勾选这些自绘控件算宽度用）。</summary>
        public static float Measure(GUIStyle style, string text)
        {
            return style.CalcSize(new GUIContent(text)).x;
        }
    }
}
