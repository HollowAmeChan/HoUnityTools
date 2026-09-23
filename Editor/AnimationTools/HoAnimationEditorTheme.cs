using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.AnimationTools
{
    /// <summary>
    /// 动画预览面板的**设计令牌**：颜色、尺寸、文字样式与程序化图标。
    ///
    /// <para>色值与 `HoConstraintEditorTheme` 同一套（面板底 / 卡片 / 1px 描边 / 三级文字 / 强调蓝），
    /// 所以放在约束面板旁边不会有"两个作者画的"那种割裂感。规矩也一样：
    /// **面板里不许再写死颜色和宽度**，一律从这里取。</para>
    ///
    /// <para>图标是运行时画的 32×32 白色形状（三角形/竖条/竖杠），
    /// 用 <c>GUI.color</c> 染色，所以深浅两种皮肤都清晰，且不引入任何资源文件、不落盘。</para>
    /// </summary>
    internal static class HoAnimationEditorTheme
    {
        // ── 尺寸 ────────────────────────────────────────────────────────
        //
        // 走带分三层，每层独占整宽（标题行已去掉 —— 组件名 Unity 自己在头顶显示）：
        //   读数 13  →  时间轴 18  →  按钮行 22（左边五个键，右边剪辑字段）
        // 71 是紧凑值，给到 76 让行间有呼吸。
        public const float BarHeight = 76.0f;

        /// <summary>时间轴槽高。独占一整行，所以给得比图标还高，一眼就是主体。</summary>
        public const float SeekHeight = 18.0f;

        /// <summary>按钮行高。左边是 16px 内置图标，右边那格是剪辑字段，两者同高。</summary>
        public const float TransportHeight = 22.0f;

        public const float ReadoutHeight = 13.0f;

        /// <summary>走带键的格子边长（内置图标是 16×16）。</summary>
        public const float IconSize = 16.0f;

        /// <summary>走带键之间的间距。</summary>
        public const float Gutter = 6.0f;

        /// <summary>按钮行里，剪辑字段前面留的空档 —— 免得它和最后一个键贴在一起。</summary>
        public const float ClipFieldGap = 12.0f;

        public const float CardPadding = 6.0f;

        /// <summary>
        /// 卡片右边缘留出的空档 —— Unity 在 Inspector 右缘画滚动条，
        /// 不留的话最后一格会被压在滚动条底下。
        /// </summary>
        public const float ScrollbarReserve = 16.0f;

        /// <summary>面板的栅格：行高、标签列、缩进 —— 与约束面板同一套数值。</summary>
        public const float RowHeight = 18.0f;
        public const float LabelWidth = 62.0f;
        public const float Indent = 10.0f;
        public const float FieldWidth = 44.0f;
        public const float CheckSize = 13.0f;

        // ── 颜色（与约束面板同一套令牌）──────────────────────────────────
        public static readonly Color CardColor = Pick(0.247f, 0.945f);
        public static readonly Color WellColor = Pick(0.180f, 0.878f);
        public static readonly Color LineColor = Pick(0.173f, 0.792f);
        public static readonly Color SeparatorColor = Pick(0.290f, 0.831f);

        public static readonly Color TextColor = Pick(0.831f, 0.153f);
        public static readonly Color TextDimColor = Pick(0.627f, 0.420f);
        public static readonly Color TextFaintColor = Pick(0.486f, 0.545f);
        public static readonly Color TextBrightColor = Pick(0.929f, 0.086f);

        public static readonly Color AccentColor = new Color(0.290f, 0.608f, 1.000f);
        public static readonly Color AccentDimColor = new Color(0.290f, 0.608f, 1.000f, 0.20f);
        public static readonly Color ErrorColor = new Color(0.898f, 0.325f, 0.294f);
        public static readonly Color WarningColor = new Color(0.910f, 0.639f, 0.239f);

        /// <summary>时间轴槽底（比输入井更深，让已播放段更跳）。</summary>
        public static readonly Color TrackColor = new Color(0f, 0f, 0f, 0.42f);

        /// <summary>时间轴里的整秒刻度线。</summary>
        public static readonly Color TickColor = new Color(1f, 1f, 1f, 0.13f);

        /// <summary>播放头（小三角 + 竖线）。</summary>
        public static readonly Color PlayheadColor = new Color(0.92f, 0.95f, 1.00f);

        // ── 走带外框 ────────────────────────────────────────────────────
        private static Texture2D cardTexture;
        private static Texture2D cardBorderTexture;

        /// <summary>
        /// 走带卡片的背景贴图（按九宫格拉伸）。**不在 GUIStyle 里做内边距** ——
        /// 内边距会让 GUILayout 自动分配宽度，正是"按钮被压成小点"的成因。
        /// 布局全部由 <see cref="HoAnimationTimelineControl"/> 自己算矩形。
        /// </summary>
        public static Texture2D CardTexture
        {
            get
            {
                return cardTexture != null
                    ? cardTexture
                    : cardTexture = BoxTexture(CardColor, LineColor, CardPadding);
            }
        }

        public static Texture2D CardBorderTexture
        {
            get
            {
                return cardBorderTexture != null
                    ? cardBorderTexture
                    : cardBorderTexture = BoxTexture(LineColor, LineColor, 1f);
            }
        }

        /// <summary>把整张走带画出来（1px 描边 + 卡片底）。</summary>
        public static void Card(Rect rect)
        {
            Fill(rect, LineColor);
            Fill(new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f), CardColor);
        }

        private static Texture2D BoxTexture(Color fill, Color border, float borderWidth)
        {
            const int Size = 8;
            Texture2D texture = NewTexture(Size, Size);
            var pixels = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    bool edge = x < borderWidth || y < borderWidth
                        || x >= Size - borderWidth || y >= Size - borderWidth;
                    pixels[(y * Size) + x] = edge ? border : fill;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>把一个矩形按像素向内收。</summary>
        public static Rect Inset(Rect rect, float left, float top, float right, float bottom)
        {
            return new Rect(
                rect.x + left,
                rect.y + top,
                Mathf.Max(0f, rect.width - left - right),
                Mathf.Max(0f, rect.height - top - bottom));
        }

        // ── 自绘控件（设置区用）─────────────────────────────────────────
        //
        // 设置区不用 EditorGUILayout 的自动布局：GUILayout 按剩余宽度百分比分空间，
        // 结果就是上一版那种"滑条铺满整行、数字框被拉得老长"。
        // 这里每一行自己算矩形，和约束面板同一套规矩。

        /// <summary>
        /// 一行：<c>Label()</c> 开标签列，随后 <c>Field(w)</c> 申请定宽格、
        /// <c>Rest()</c> 吃掉剩余宽度。一行画完自动推进到下一行。
        /// </summary>
        internal sealed class Row : System.IDisposable
        {
            private readonly Rect rect;
            private float cursor;

            internal Row(Rect rect)
            {
                this.rect = rect;
                cursor = rect.x + Indent;
            }

            /// <summary>当前行还剩多少宽度。</summary>
            internal float Remaining
            {
                get { return rect.xMax - cursor; }
            }

            /// <summary>开一个标签列（固定宽度），并推进光标。</summary>
            internal void Label(string text, float width = LabelWidth)
            {
                GUI.Label(new Rect(cursor, rect.y, width, rect.height), text, TextDim);
                cursor += width;
            }

            /// <summary>申请一个定宽格。</summary>
            internal Rect Field(float width)
            {
                var slot = new Rect(cursor, rect.y, width, rect.height);
                cursor += width + 4f;
                return slot;
            }

            /// <summary>吃掉剩余宽度。</summary>
            internal Rect Rest(float minWidth = 20f)
            {
                var slot = new Rect(cursor, rect.y, Mathf.Max(minWidth, rect.xMax - cursor), rect.height);
                cursor = rect.xMax;
                return slot;
            }

            public void Dispose()
            {
            }
        }

        /// <summary>开一行（行高 <see cref="RowHeight"/>，左缩进 <see cref="Indent"/>）。</summary>
        internal static Row BeginRow()
        {
            return new Row(EditorGUILayout.GetControlRect(false, RowHeight));
        }

        /// <summary>
        /// 定宽输入格：自己画底与描边，不像 Unity 的 textField 会吃掉 label 的四成宽度。
        /// </summary>
        public static float NumberField(Rect rect, float value, string unit = null)
        {
            DrawWell(rect, LineColor, TextFaintColor);

            float unitWidth = string.IsNullOrEmpty(unit) ? 0f : 16f;
            var textRect = new Rect(rect.x + 5f, rect.y, rect.width - 10f - unitWidth, rect.height);

            string text = GUI.TextField(textRect, value.ToString("0.###"), NumberStyle);
            float parsed;
            if (float.TryParse(text, out parsed))
                value = parsed;

            if (unitWidth > 0f)
                GUI.Label(new Rect(rect.xMax - unitWidth - 4f, rect.y, unitWidth, rect.height), unit, Caption);

            return value;
        }

        /// <summary>输入井外观（比卡片更深）。</summary>
        public static void DrawWell(Rect rect, Color border, Color fill)
        {
            Fill(rect, border);
            Fill(new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f), fill);
        }

        /// <summary>自绘勾选：13px 方块 + 勾。比 Toggle 矮，且能定宽。</summary>
        public static bool Check(Rect rect, bool value)
        {
            var box = new Rect(rect.x, rect.y + ((rect.height - CheckSize) * 0.5f), CheckSize, CheckSize);

            int id = GUIUtility.GetControlID(FocusType.Passive);
            bool hot = box.Contains(Event.current.mousePosition);
            if (hot)
                EditorGUIUtility.AddCursorRect(box, MouseCursor.Link);

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && hot)
            {
                value = !value;
                GUI.changed = true;
                Event.current.Use();
                GUIUtility.hotControl = id;
            }
            else if (GUIUtility.hotControl == id && Event.current.type == EventType.MouseUp)
            {
                GUIUtility.hotControl = 0;
            }

            DrawWell(box, value ? new Color(0.24f, 0.56f, 0.40f) : LineColor,
                value ? new Color(0.18f, 0.43f, 0.31f) : WellColor);

            if (value)
                GUI.Label(box, "✓", CheckGlyph);

            return value;
        }

        private static GUIStyle numberStyle;
        private static GUIStyle checkGlyph;

        private static GUIStyle NumberStyle
        {
            get
            {
                if (numberStyle == null)
                {
                    numberStyle = new GUIStyle(EditorStyles.label)
                    {
                        fontSize = 11,
                        alignment = TextAnchor.MiddleRight,
                        richText = false,
                        padding = new RectOffset(0, 0, 0, 0)
                    };
                    numberStyle.normal.textColor = TextBrightColor;
                    numberStyle.focused.textColor = TextBrightColor;
                }
                return numberStyle;
            }
        }

        private static GUIStyle CheckGlyph
        {
            get
            {
                if (checkGlyph == null)
                {
                    checkGlyph = new GUIStyle(EditorStyles.miniLabel)
                    {
                        fontSize = 9,
                        alignment = TextAnchor.MiddleCenter,
                        richText = false
                    };
                    checkGlyph.normal.textColor = new Color(0.812f, 0.965f, 0.886f);
                }
                return checkGlyph;
            }
        }

        // ── 文字样式 ────────────────────────────────────────────────────
        private static GUIStyle title;
        private static GUIStyle readout;
        private static GUIStyle readoutDim;
        private static GUIStyle caption;
        private static GUIStyle hint;
        private static GUIStyle textDim;

        /// <summary>面板标题（小一号的粗体）。</summary>
        public static GUIStyle Title => Ready(ref title, () => Text(EditorStyles.boldLabel, TextColor, 11));

        /// <summary>时间读数：等宽数字、右对齐，跳动时不抖。</summary>
        public static GUIStyle Readout => Ready(ref readout, () =>
        {
            GUIStyle style = Text(EditorStyles.label, TextBrightColor, 10);
            style.alignment = TextAnchor.LowerRight;
            style.font = MonoFont;
            return style;
        });

        /// <summary>次要读数（帧号、时长）。</summary>
        public static GUIStyle ReadoutDim => Ready(ref readoutDim, () =>
        {
            GUIStyle style = Text(EditorStyles.label, TextDimColor, 10);
            style.alignment = TextAnchor.LowerRight;
            return style;
        });

        public static GUIStyle Caption => Ready(ref caption, () => Text(EditorStyles.miniLabel, TextFaintColor, 9));

        /// <summary>行标签（次要文字，固定列宽）。</summary>
        public static GUIStyle TextDim => Ready(ref textDim, () => Text(EditorStyles.label, TextDimColor, 11));

        /// <summary>自绘小按钮（倍速档）：定宽、比 miniButton 干净。</summary>
        public static bool Button(Rect rect, string text, bool on)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            bool hot = rect.Contains(Event.current.mousePosition);
            if (hot)
                EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);

            bool clicked = false;
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && hot)
            {
                clicked = true;
                GUI.changed = true;
                GUIUtility.hotControl = id;
                Event.current.Use();
            }
            else if (GUIUtility.hotControl == id && Event.current.type == EventType.MouseUp)
            {
                GUIUtility.hotControl = 0;
            }

            Color border = on ? new Color(0.31f, 0.48f, 0.65f) : LineColor;
            Color fill = on
                ? new Color(0.24f, 0.36f, 0.49f)
                : (hot ? new Color(0.22f, 0.22f, 0.22f) : new Color(0.20f, 0.20f, 0.20f));

            DrawWell(rect, border, fill);
            GUI.Label(rect, text, on ? ButtonOnText : ButtonOffText);
            return clicked;
        }

        private static GUIStyle buttonOnText;
        private static GUIStyle buttonOffText;


        private static GUIStyle ButtonOnText
        {
            get
            {
                if (buttonOnText == null)
                {
                    buttonOnText = Text(EditorStyles.miniLabel, new Color(0.910f, 0.945f, 0.980f), 10);
                    buttonOnText.alignment = TextAnchor.MiddleCenter;
                }
                return buttonOnText;
            }
        }

        private static GUIStyle ButtonOffText
        {
            get
            {
                if (buttonOffText == null)
                {
                    buttonOffText = Text(EditorStyles.miniLabel, TextDimColor, 10);
                    buttonOffText.alignment = TextAnchor.MiddleCenter;
                }
                return buttonOffText;
            }
        }

        /// <summary>空态提示。</summary>
        public static GUIStyle Hint => Ready(ref hint, () =>
        {
            GUIStyle style = Text(EditorStyles.miniLabel, TextFaintColor, 10);
            style.alignment = TextAnchor.MiddleLeft;
            return style;
        });

        private static Font MonoFont
        {
            get
            {
                // Consolas 在 Windows 上必有；取不到就交给 Unity 的默认字体。
                Font font = Font.CreateDynamicFontFromOSFont(
                    new[] { "Consolas", "DejaVu Sans Mono", "Menlo", "Courier New" }, 10);
                return font != null ? font : EditorStyles.label.font;
            }
        }

        // ── 画法 ────────────────────────────────────────────────────────

        /// <summary>纯色矩形。</summary>
        public static void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        /// <summary>1px 描边的卡片，用作整个走带的外框。</summary>
        public static void Box(Rect rect, Color fill, Color border)
        {
            Fill(rect, border);
            Fill(new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f), fill);
        }

        /// <summary>已播放段的横向渐变（左浓右淡），让进度条有个方向感。</summary>
        public static void GradientFill(Rect rect, Color color)
        {
            if (rect.width <= 0f)
                return;

            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, GradientTexture(color));
            GUI.color = previous;
        }

        private static readonly System.Collections.Generic.Dictionary<Color, Texture2D> GradientCache =
            new System.Collections.Generic.Dictionary<Color, Texture2D>();

        private static Texture2D GradientTexture(Color key)
        {
            Texture2D cached;
            if (GradientCache.TryGetValue(key, out cached) && cached != null)
                return cached;

            const int Width = 64;
            Texture2D texture = NewTexture(Width, 1);
            var pixels = new Color[Width];
            for (int x = 0; x < Width; x++)
            {
                float t = x / (float)(Width - 1);
                // 从 key 的完整透明度滑到 0.72，尾部稍淡以免看着"断掉"。
                float alpha = Mathf.Lerp(key.a, key.a * 0.72f, t);
                pixels[x] = new Color(key.r, key.g, key.b, alpha);
            }

            texture.SetPixels(pixels);
            texture.Apply();
            GradientCache[key] = texture;
            return texture;
        }

        public static Texture2D Solid(Color color)
        {
            Texture2D texture = NewTexture(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// 运行时生成的贴图（卡片底、渐变、纯色块）。图标**不在此列** ——
        /// 走带图标一律用 Unity 内置的，见 <see cref="HoEditorIcons"/>。
        /// </summary>
        private static Texture2D NewTexture(int width, int height)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        // ── 工具 ────────────────────────────────────────────────────────

        private static GUIStyle Text(GUIStyle basis, Color color, int fontSize)
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
            float value = EditorGUIUtility.isProSkin ? pro : light;
            return new Color(value, value, value);
        }

        private static Color Pick(Color pro, Color light)
        {
            return EditorGUIUtility.isProSkin ? pro : light;
        }

        private static GUIStyle Ready(ref GUIStyle style, System.Func<GUIStyle> factory)
        {
            return style ??= factory();
        }

        private static Texture2D Ready(ref Texture2D texture, System.Func<Texture2D> factory)
        {
            return texture != null ? texture : texture = factory();
        }
    }
}
