using System;
using System.Collections.Generic;
using Hollow.HoUnityTools.Editor.Constraints;
using Hollow.HoUnityTools.Editor.FaceTracking;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>
    /// **批量改名**：对某一类行的参数名（左边那个 `parameter`）做查找替换 / 加前后缀。
    ///
    /// 铁律：**先出预览，确认了才应用**。理由不是谨慎过头 —— 改名会连带作废下游
    /// （控制器里的参数名对不上那一行就永远不写），而这种破坏**不报错**、只是不动。
    /// 所以这里把"旧名 → 新名"整张表摊出来给你核，按「应用」之前一个字节都不改。
    ///
    /// 「只改选中行」是可选项：默认改全部。
    /// </summary>
    public sealed class HoFaceBatchRenameWindow : EditorWindow
    {
        private List<HoFaceOutput> rows;
        private Action onChanged;

        private string find = "";
        private string replace = "";
        private string prefix = "";
        private string suffix = "";
        private bool onlySelected;
        private int selectedIndex = -1;

        private Vector2 scroll;
        private readonly List<(string From, string To, bool Changed)> preview =
            new List<(string, string, bool)>();

        public static void Open(List<HoFaceOutput> rows, Action onChanged, int selectedIndex)
        {
            HoFaceBatchRenameWindow window = GetWindow<HoFaceBatchRenameWindow>(true, "批量改名", true);
            window.rows = rows;
            window.onChanged = onChanged;
            window.selectedIndex = selectedIndex;
            window.minSize = new Vector2(420.0f, 380.0f);
            window.RebuildPreview();
            window.Repaint();
        }

        /// <summary>给"只改选中行"用的：告诉它当前选的是第几行。</summary>
        public void SetSelection(int index)
        {
            selectedIndex = index;
            RebuildPreview();
            Repaint();
        }

        private void OnEnable()
        {
            minSize = new Vector2(420.0f, 380.0f);
        }

        private string Rename(string name)
        {
            string result = name ?? "";
            if (!string.IsNullOrEmpty(find))
            {
                result = result.Replace(find, replace ?? "");
            }
            return (prefix ?? "") + result + (suffix ?? "");
        }

        private void RebuildPreview()
        {
            preview.Clear();
            if (rows == null) return;

            for (int i = 0; i < rows.Count; i++)
            {
                if (onlySelected && i != selectedIndex) continue;
                HoFaceOutput row = rows[i];
                if (row == null) continue;

                string from = row.parameter ?? "";
                string to = Rename(from);
                preview.Add((from, to, !string.Equals(from, to, StringComparison.Ordinal)));
            }
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                HoConstraintEditorControls.Caption(
                    "改的是每行左边那个 **参数名**（输入行 = 规范名，输出行 = 控制器参数名）。",
                    "表达式、曲线、修饰符都不动。");

                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("查找", HoConstraintEditorTheme.LabelWidthSm);
                    string next = EditorGUI.TextField(HoConstraintEditorControls.NextFlexible(60.0f), find,
                        HoConstraintEditorTheme.Field);
                    if (next != find) { find = next; RebuildPreview(); }
                }

                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("替换", HoConstraintEditorTheme.LabelWidthSm);
                    string next = EditorGUI.TextField(HoConstraintEditorControls.NextFlexible(60.0f), replace,
                        HoConstraintEditorTheme.Field);
                    if (next != replace) { replace = next; RebuildPreview(); }
                }

                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("前缀", HoConstraintEditorTheme.LabelWidthSm);
                    string next = EditorGUI.TextField(HoConstraintEditorControls.NextFlexible(40.0f), prefix,
                        HoConstraintEditorTheme.Field);
                    if (next != prefix) { prefix = next; RebuildPreview(); }

                    HoConstraintEditorControls.Gap();
                    HoConstraintEditorControls.Label("后缀", HoConstraintEditorTheme.LabelWidthSm);
                    string tail = EditorGUI.TextField(HoConstraintEditorControls.NextFlexible(40.0f), suffix,
                        HoConstraintEditorTheme.Field);
                    if (tail != suffix) { suffix = tail; RebuildPreview(); }
                }

                using (HoConstraintEditorControls.Row())
                {
                    int mode = HoConstraintEditorControls.Segmented(
                        HoConstraintEditorControls.Next(160.0f), onlySelected ? 1 : 0,
                        new[] { "改全部", "只改选中行" }, "只改选中行需要先在列表里选中一行。");
                    bool next = mode == 1;
                    if (next != onlySelected)
                    {
                        onlySelected = next;
                        RebuildPreview();
                    }

                    HoConstraintEditorControls.Flex();
                    int changed = 0;
                    foreach (var item in preview) if (item.Changed) changed++;
                    HoConstraintEditorControls.Caption("· " + preview.Count + " 行里 " + changed + " 行会变");
                }

                HoConstraintEditorControls.Separator(3.0f, 3.0f);

                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandHeight(true)))
                {
                    scroll = EditorGUILayout.BeginScrollView(scroll, false, true);
                    foreach (var item in preview)
                    {
                        using (HoConstraintEditorControls.Row(true))
                        {
                            var fromStyle = new GUIStyle(HoConstraintEditorTheme.Caption) { clipping = TextClipping.Clip };
                            GUI.Label(HoConstraintEditorControls.NextFlexible(60.0f),
                                new GUIContent(item.From, item.From), fromStyle);

                            var arrowStyle = new GUIStyle(HoConstraintEditorTheme.Caption) { alignment = TextAnchor.MiddleCenter };
                            GUI.Label(HoConstraintEditorControls.Next(14.0f), new GUIContent("→"), arrowStyle);

                            var toStyle = new GUIStyle(item.Changed ? HoConstraintEditorTheme.Value : HoConstraintEditorTheme.Caption)
                            {
                                clipping = TextClipping.Clip
                            };
                            GUI.Label(HoConstraintEditorControls.NextFlexible(60.0f),
                                new GUIContent(item.To, item.To), toStyle);

                            if (HoConstraintEditorControls.IconButton("⧉", "只复制新名字。"))
                            {
                                EditorGUIUtility.systemCopyBuffer = item.To;
                            }
                        }
                    }
                    EditorGUILayout.EndScrollView();
                }

                HoConstraintEditorControls.Separator(3.0f, 2.0f);
                using (HoConstraintEditorControls.Row())
                {
                    bool any = false;
                    foreach (var item in preview) if (item.Changed) { any = true; break; }

                    using (new EditorGUI.DisabledScope(!any))
                    {
                        if (HoConstraintEditorControls.Button("应用（会改内存，保存才落盘）", null, true))
                        {
                            Apply();
                        }
                    }

                    HoConstraintEditorControls.Flex();
                    if (HoConstraintEditorControls.Button("关闭"))
                    {
                        Close();
                    }
                }
            }
        }

        private void Apply()
        {
            if (rows == null) return;
            for (int i = 0; i < rows.Count; i++)
            {
                if (onlySelected && i != selectedIndex) continue;
                HoFaceOutput row = rows[i];
                if (row == null) continue;
                row.parameter = Rename(row.parameter);
            }

            onChanged?.Invoke();
            RebuildPreview();
        }
    }
}
