using System;
using System.Collections.Generic;
using Hollow.HoUnityTools.Constraints;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Constraints
{
    internal static class HoConstraintEditorSectionGui
    {
        private const float SectionHeaderHeight = 30.0f;

        private static GUIStyle sectionTitleStyle;
        private static GUIStyle sectionSummaryStyle;

        private static GUIStyle SectionTitleStyle
        {
            get
            {
                if (sectionTitleStyle == null)
                {
                    sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        alignment = TextAnchor.MiddleLeft,
                        clipping = TextClipping.Clip
                    };
                }

                sectionTitleStyle.normal.textColor = EditorGUIUtility.isProSkin ? Color.white : new Color(0.12f, 0.12f, 0.12f);
                return sectionTitleStyle;
            }
        }

        private static GUIStyle SectionSummaryStyle
        {
            get
            {
                if (sectionSummaryStyle == null)
                {
                    sectionSummaryStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleRight,
                        clipping = TextClipping.Clip
                    };
                }

                sectionSummaryStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.86f, 0.88f, 0.90f) : new Color(0.22f, 0.22f, 0.22f);
                return sectionSummaryStyle;
            }
        }

        public static bool DrawSectionHeader(ref bool expanded, string title, string summary, Color color)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, SectionHeaderHeight);
            Event evt = Event.current;
            bool hover = rect.Contains(evt.mousePosition);

            EditorGUI.DrawRect(rect, GetSectionColor(color, hover));

            Rect foldoutRect = new Rect(rect.x + 6.0f, rect.y + 7.0f, 16.0f, EditorGUIUtility.singleLineHeight);
            expanded = EditorGUI.Foldout(foldoutRect, expanded, GUIContent.none, true);

            Rect summaryRect = new Rect(rect.x + rect.width * 0.48f, rect.y + 7.0f, rect.width * 0.52f - 10.0f, 18.0f);
            Rect titleRect = new Rect(rect.x + 26.0f, rect.y + 6.0f, Mathf.Max(90.0f, summaryRect.x - rect.x - 32.0f), 20.0f);
            GUI.Label(titleRect, title, SectionTitleStyle);
            GUI.Label(summaryRect, summary, SectionSummaryStyle);

            if (evt.type == EventType.MouseDown && rect.Contains(evt.mousePosition) && !foldoutRect.Contains(evt.mousePosition))
            {
                expanded = !expanded;
                evt.Use();
            }

            return expanded;
        }

        public static string BoolSummary(SerializedProperty property)
        {
            return property != null && property.boolValue ? "开" : "关";
        }

        public static string FloatSummary(SerializedProperty property, string suffix = "")
        {
            return property != null ? property.floatValue.ToString("0.##") + suffix : "-";
        }

        public static string EnumSummary(SerializedProperty property)
        {
            if (property == null || property.propertyType != SerializedPropertyType.Enum)
            {
                return "-";
            }

            int index = Mathf.Clamp(property.enumValueIndex, 0, property.enumDisplayNames.Length - 1);
            return property.enumDisplayNames[index];
        }

        /// <summary>
        /// 临时收窄 label 宽度。**并排或者固定小宽度的字段必须套这个**：
        /// `EditorGUIUtility.labelWidth` 默认是面板宽度的 40%，一个 `FloatField(label, value, Width(160))`
        /// 在宽面板下 160px 会被 label 全部吃掉，数字框变成 0 宽 ——
        /// 表现就是"只有文字、点不动也调不了"。
        /// </summary>
        public static IDisposable NarrowLabels(float width)
        {
            return new LabelWidthScope(width);
        }

        /// <summary>合并方式下拉的标签（眨眼与注视共用同一套文案）。</summary>
        public static readonly GUIContent MergeModeLabel = new GUIContent(
            "合并方式",
            "一个键上同时有外部写者（动画/面捕/表情）和我们多路目标时，求和超过 100 怎么处理：\n"
            + "· 夹断（默认）：到 100 就停 —— 单路到顶就该是满值时用这个\n"
            + "· 软饱和：80 以上软压缩，永远到不了 100（请求 100 时约 93）—— 果冻超调、多路叠加的差别保留下来\n"
            + "· 按比例分配：超了就各路一起缩、比例不变，谁也不会把谁挤掉\n"
            + "只影响我们这一路，不会改写动画/面捕写在键上的基准值。");

        /// <summary>
        /// 列出"已经写满 / 被削过"的键。这是回答"为什么形态键老是 100"的地方：
        /// 把基准、我们的请求、最终值三个数摆出来，就能看出是外部占满了、还是自己拉满、还是叠加被夹断。
        /// </summary>
        public static void DrawSaturationReport(List<HoShapeKeySaturation> buffer, int maxLines = 6)
        {
            if (buffer == null || buffer.Count == 0)
            {
                return;
            }

            System.Text.StringBuilder text = new System.Text.StringBuilder();
            text.Append("以下形态键已经写满（或我们这一路被合并策略削过）：");
            int lines = Mathf.Min(buffer.Count, maxLines);
            for (int i = 0; i < lines; i++)
            {
                HoShapeKeySaturation item = buffer[i];
                text.Append("\n· ").Append(item.KeyName);
                text.Append("　基准 ").Append(item.Base.ToString("0.#"));
                text.Append(" + 我们 ").Append((item.Request - item.Base).ToString("0.#"));
                text.Append(" → ").Append(item.Final.ToString("0.#"));
                if (item.Clipped)
                {
                    text.Append("（被削）");
                }
            }

            if (buffer.Count > lines)
            {
                text.Append("\n… 还有 ").Append(buffer.Count - lines).Append(" 个");
            }

            text.Append("\n\n想留出余量：把「合并方式」换成软饱和/按比例分配，或调小对应那一路的「增益 / 强度 / 输出上限」。");
            EditorGUILayout.HelpBox(text.ToString(), MessageType.Warning);
        }

        private sealed class LabelWidthScope : IDisposable
        {
            private readonly float previous;

            public LabelWidthScope(float width)
            {
                previous = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = width;
            }

            public void Dispose()
            {
                EditorGUIUtility.labelWidth = previous;
            }
        }

        private static Color GetSectionColor(Color baseColor, bool hover)
        {
            Color neutral = EditorGUIUtility.isProSkin
                ? new Color(0.16f, 0.17f, 0.18f)
                : new Color(0.93f, 0.93f, 0.93f);
            float strength = hover ? 0.42f : 0.34f;
            Color result = Color.Lerp(neutral, baseColor, strength);
            result.a = 1.0f;
            return result;
        }
    }
}
