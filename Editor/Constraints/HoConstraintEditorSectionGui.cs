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
