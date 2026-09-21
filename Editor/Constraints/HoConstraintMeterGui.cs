using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Constraints
{
    /// <summary>
    /// 实时横条：把"这一路现在到哪了"画成一条会动的条，而不是一串小数点。
    ///
    /// - 单极量（0..1）：从左往右长；
    /// - 双极量（−1..1）：从中轴往两边长，一眼看出正负；
    /// - `ghost`：画一根细刻度表示**弹簧/平滑之前**的原始值，能直接看出弹簧把输入拖了多少、超调甩到哪。
    ///
    /// 全部用 `EditorGUI.DrawRect` 自绘（IMGUI），不建纹理、不进布局系统，面板窄也不会串行。
    /// </summary>
    internal static class HoConstraintMeterGui
    {
        public const float DefaultHeight = 13.0f;

        /// <summary>紧凑档：短标签 + 小条 + 短数值，整块只占 ~130px，能直接塞进别的行。</summary>
        public const float CompactLabelWidth = 30.0f;
        public const float CompactBarWidth = 54.0f;
        public const float CompactValueWidth = 40.0f;

        private static GUIStyle labelStyle;
        private static GUIStyle valueStyle;
        private static GUIStyle captionStyle;

        /// <summary>
        /// 紧凑读数：`标签 [小条] 值`，只占固定宽度（默认 ~128px），**不铺满整行**。
        /// 放进 `BeginHorizontal` 里给一行参数收尾用；单独一行也行，但不会浪费横向空间。
        /// </summary>
        public static void DrawCompact(
            string label,
            float value,
            float min,
            float max,
            Color color,
            string valueText = null,
            float ghost = float.NaN,
            string tooltip = null,
            float labelWidth = CompactLabelWidth,
            float barWidth = CompactBarWidth,
            float valueWidth = CompactValueWidth,
            float height = DefaultHeight)
        {
            float width = (string.IsNullOrEmpty(label) ? 0.0f : labelWidth) + barWidth + valueWidth;
            Rect rect = GUILayoutUtility.GetRect(width, width, height, height);
            DrawCompactInto(rect, label, value, min, max, color, valueText, ghost, tooltip, labelWidth, barWidth, valueWidth);
        }

        /// <summary>紧凑读数的绘制本体（自己给 rect，比如塞在一行的尾巴上）。</summary>
        public static void DrawCompactInto(
            Rect rect,
            string label,
            float value,
            float min,
            float max,
            Color color,
            string valueText = null,
            float ghost = float.NaN,
            string tooltip = null,
            float labelWidth = CompactLabelWidth,
            float barWidth = CompactBarWidth,
            float valueWidth = CompactValueWidth)
        {
            bool hasLabel = !string.IsNullOrEmpty(label);
            if (hasLabel)
            {
                GUI.Label(new Rect(rect.x, rect.y, labelWidth, rect.height), new GUIContent(label, tooltip), LabelStyle);
            }
            else
            {
                // 没标签就别给它留位置，条直接贴左边
                labelWidth = 0.0f;
            }

            DrawBar(
                new Rect(rect.x + labelWidth, rect.y + 1.0f, barWidth, rect.height - 2.0f),
                value,
                min,
                max,
                color,
                ghost);

            if (!string.IsNullOrEmpty(valueText))
            {
                GUI.Label(new Rect(rect.x + labelWidth + barWidth, rect.y, valueWidth, rect.height), valueText, ValueStyle);
            }

            if (!hasLabel && !string.IsNullOrEmpty(tooltip))
            {
                // 没有文字可以挂 tooltip，就给条本身盖一层透明标签，鼠标停上去才看得到说明
                GUI.Label(new Rect(rect.x, rect.y, barWidth + valueWidth, rect.height), new GUIContent(string.Empty, tooltip));
            }
        }

        /// <summary>只画条（自己管布局时用）。`ghost` 传 NaN 表示不画原始刻度。</summary>
        public static void DrawBar(Rect rect, float value, float min, float max, Color color, float ghost = float.NaN)
        {
            if (rect.width <= 1.0f || rect.height <= 1.0f || Event.current.type != EventType.Repaint)
            {
                return;
            }

            float range = max - min;
            if (range <= 1e-6f)
            {
                range = 1.0f;
            }

            bool pro = EditorGUIUtility.isProSkin;
            EditorGUI.DrawRect(rect, pro ? new Color(0.0f, 0.0f, 0.0f, 0.42f) : new Color(0.0f, 0.0f, 0.0f, 0.10f));

            float value01 = Mathf.Clamp01((value - min) / range);
            bool bipolar = min < 0.0f && max > 0.0f;
            float zero01 = bipolar ? Mathf.Clamp01((0.0f - min) / range) : 0.0f;
            float from = Mathf.Min(zero01, value01);
            float to = Mathf.Max(zero01, value01);

            if (to - from > 0.0005f)
            {
                Rect fill = new Rect(
                    rect.x + (from * rect.width),
                    rect.y + 1.0f,
                    (to - from) * rect.width,
                    rect.height - 2.0f);
                color.a = 0.92f;
                EditorGUI.DrawRect(fill, color);

                // 顶部一道更亮的边，让条看起来有厚度
                Color top = Color.Lerp(color, Color.white, 0.45f);
                top.a = 0.85f;
                EditorGUI.DrawRect(new Rect(fill.x, fill.y, fill.width, 1.0f), top);
            }

            if (bipolar)
            {
                float centerX = rect.x + (zero01 * rect.width);
                EditorGUI.DrawRect(
                    new Rect(centerX - 0.5f, rect.y, 1.0f, rect.height),
                    pro ? new Color(1.0f, 1.0f, 1.0f, 0.34f) : new Color(0.0f, 0.0f, 0.0f, 0.30f));
            }

            Color frame = pro ? new Color(1.0f, 1.0f, 1.0f, 0.10f) : new Color(0.0f, 0.0f, 0.0f, 0.18f);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1.0f), frame);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1.0f, rect.width, 1.0f), frame);

            if (!float.IsNaN(ghost))
            {
                float ghost01 = Mathf.Clamp01((ghost - min) / range);
                float ghostX = rect.x + (ghost01 * rect.width);
                EditorGUI.DrawRect(
                    new Rect(ghostX - 1.0f, rect.y + 2.0f, 2.0f, rect.height - 4.0f),
                    pro ? new Color(1.0f, 1.0f, 1.0f, 0.75f) : new Color(0.0f, 0.0f, 0.0f, 0.65f));
            }
        }

        /// <summary>一行小字说明（比 `LabelField` 松一点，不跟别的控件贴在一起）。</summary>
        public static void DrawCaption(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            Rect rect = EditorGUILayout.GetControlRect(false, 14.0f);
            GUI.Label(rect, text, CaptionStyle);
        }

        /// <summary>一条细分隔线：把"参数"和"调试读数"分开，不用再加标题占一行。</summary>
        public static void DrawSeparator(float topPadding = 4.0f, float bottomPadding = 2.0f)
        {
            EditorGUILayout.Space(topPadding);
            Rect rect = EditorGUILayout.GetControlRect(false, 1.0f);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(
                    rect,
                    EditorGUIUtility.isProSkin ? new Color(1.0f, 1.0f, 1.0f, 0.08f) : new Color(0.0f, 0.0f, 0.0f, 0.12f));
            }

            EditorGUILayout.Space(bottomPadding);
        }

        private static GUIStyle LabelStyle
        {
            get
            {
                if (labelStyle == null)
                {
                    labelStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleLeft,
                        clipping = TextClipping.Clip
                    };
                }

                labelStyle.normal.textColor = EditorGUIUtility.isProSkin
                    ? new Color(0.76f, 0.79f, 0.83f)
                    : new Color(0.28f, 0.28f, 0.28f);
                return labelStyle;
            }
        }

        private static GUIStyle ValueStyle
        {
            get
            {
                if (valueStyle == null)
                {
                    valueStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleRight,
                        clipping = TextClipping.Clip
                    };
                }

                valueStyle.normal.textColor = EditorGUIUtility.isProSkin
                    ? new Color(0.94f, 0.95f, 0.97f)
                    : new Color(0.10f, 0.10f, 0.10f);
                return valueStyle;
            }
        }

        private static GUIStyle CaptionStyle
        {
            get
            {
                if (captionStyle == null)
                {
                    captionStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleLeft,
                        clipping = TextClipping.Clip,
                        wordWrap = false
                    };
                }

                captionStyle.normal.textColor = EditorGUIUtility.isProSkin
                    ? new Color(0.62f, 0.65f, 0.69f)
                    : new Color(0.38f, 0.38f, 0.38f);
                return captionStyle;
            }
        }
    }
}
