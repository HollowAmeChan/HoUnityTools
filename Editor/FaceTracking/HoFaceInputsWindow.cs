using System.Collections.Generic;
using Hollow.HoUnityTools.Editor.Constraints;
using Hollow.HoUnityTools.Editor.FaceTracking;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>
    /// **这份配置需要哪些输入值** —— 扫全部规则（输入行 + 输出行）的表达式，把用到的变量
    /// 汇成一张表。
    ///
    /// 为什么需要它：这一层的映射是自由的，表达式里可以写规范名、可以写设备线名、
    /// 也可以写"上一行算出来的结果"。所以"**这份配置到底吃哪些键**"从文件里一眼看不出来 ——
    /// 以前得靠人肉把每行的变量加一遍。这就是那个活的答案。
    ///
    /// 只**列**，不判定对错：某个名字是不是标准键、这一帧有没有来源，只有跑起来才知道
    /// （缺键的行会冻结，不是报错）。表里只区分"它出现在哪一类行里"。
    /// </summary>
    public sealed class HoFaceInputsWindow : EditorWindow
    {
        private sealed class Entry
        {
            public string Name;
            public int InputUses;
            public int OutputUses;
        }

        private HoFaceProfileWindow owner;
        private Vector2 scroll;
        private readonly List<Entry> entries = new List<Entry>();
        private string filter = "";
        private int totalUses;

        public static void Open(HoFaceProfileWindow owner)
        {
            HoFaceInputsWindow window = GetWindow<HoFaceInputsWindow>(false, "需要的输入值", true);
            window.owner = owner;
            window.Rebuild();
            window.Repaint();
        }

        private void OnEnable()
        {
            minSize = new Vector2(320.0f, 260.0f);
            Rebuild();
        }

        private void Rebuild()
        {
            entries.Clear();
            totalUses = 0;

            HoFaceMiddleware middleware = owner != null ? owner.Middleware : null;
            if (middleware == null) return;

            var map = new Dictionary<string, Entry>(System.StringComparer.Ordinal);
            Collect(middleware.inputs, map, true);
            Collect(middleware.outputs, map, false);

            entries.AddRange(map.Values);
            entries.Sort((a, b) =>
            {
                int byTotal = (b.InputUses + b.OutputUses).CompareTo(a.InputUses + a.OutputUses);
                return byTotal != 0 ? byTotal : string.CompareOrdinal(a.Name, b.Name);
            });
        }

        private void Collect(List<HoFaceOutput> rows, Dictionary<string, Entry> map, bool inputs)
        {
            if (rows == null) return;
            var variables = new List<string>();
            foreach (HoFaceOutput row in rows)
            {
                if (row == null || string.IsNullOrEmpty(row.expression)) continue;
                if (!HoFaceExpression.TryParse(row.expression, out var parsed, out _)) continue;

                variables.Clear();
                parsed.CollectVariables(variables);
                foreach (string name in variables)
                {
                    if (string.IsNullOrEmpty(name)) continue;
                    Entry entry;
                    if (!map.TryGetValue(name, out entry))
                    {
                        entry = new Entry { Name = name };
                        map[name] = entry;
                    }

                    if (inputs) entry.InputUses++;
                    else entry.OutputUses++;
                    totalUses++;
                }
            }
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("过滤", HoConstraintEditorTheme.LabelWidthSm, "按名字过滤。");
                    filter = EditorGUI.TextField(HoConstraintEditorControls.NextFlexible(60.0f), filter, HoConstraintEditorTheme.Field);
                }

                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Caption(entries.Count + " 个不同的键 · 共 " + totalUses + " 处引用");
                    HoConstraintEditorControls.Flex();
                    if (HoConstraintEditorControls.Button("重新扫描", "配置改过之后点这个。"))
                    {
                        Rebuild();
                    }

                    HoConstraintEditorControls.Gap();
                    if (HoConstraintEditorControls.Button("全部复制", "把所有键名按行复制到剪贴板。"))
                    {
                        var text = new System.Text.StringBuilder();
                        foreach (Entry entry in entries)
                        {
                            if (!Visible(entry)) continue;
                            text.AppendLine(entry.Name);
                        }
                        EditorGUIUtility.systemCopyBuffer = text.ToString();
                    }
                }

                HoConstraintEditorControls.Separator(3.0f, 3.0f);

                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandHeight(true)))
                {
                    scroll = EditorGUILayout.BeginScrollView(scroll, false, true);
                    foreach (Entry entry in entries)
                    {
                        if (!Visible(entry)) continue;
                        DrawEntry(entry);
                    }
                    EditorGUILayout.EndScrollView();
                }
            }
        }

        private bool Visible(Entry entry)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            return entry.Name != null
                && entry.Name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void DrawEntry(Entry entry)
        {
            Rect row = GUILayoutUtility.GetRect(0.0f, 4000.0f, 20.0f, 20.0f, GUILayout.ExpandWidth(false));
            if (row.width < 24.0f) return;

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(row, new Color(1.0f, 1.0f, 1.0f, 0.02f));

                var nameStyle = new GUIStyle(HoConstraintEditorTheme.Label)
                {
                    alignment = TextAnchor.MiddleLeft,
                    clipping = TextClipping.Clip
                };
                GUI.Label(new Rect(row.x + 6.0f, row.y, row.width * 0.55f, row.height), entry.Name, nameStyle);

                string uses = (entry.InputUses > 0 ? "输入行×" + entry.InputUses + "  " : "")
                    + (entry.OutputUses > 0 ? "输出行×" + entry.OutputUses : "");
                var useStyle = new GUIStyle(HoConstraintEditorTheme.Caption) { alignment = TextAnchor.MiddleRight };
                GUI.Label(new Rect(row.x, row.y, row.width - 6.0f, row.height), uses, useStyle);
            }

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0
                && row.Contains(Event.current.mousePosition))
            {
                EditorGUIUtility.systemCopyBuffer = entry.Name;
                Event.current.Use();
            }
        }
    }
}
