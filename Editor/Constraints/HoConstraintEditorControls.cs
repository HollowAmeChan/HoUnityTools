using System;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Constraints
{
    /// <summary>
    /// Ho 约束面板的**控件库**：全部自己算矩形、自己画。
    ///
    /// 为什么不用 `EditorGUILayout.*`：它按"剩余宽度百分比"分配空间，
    /// 于是同一列在不同行里宽度不同、数字框被 label 吃掉、一个 `0.35` 占半行。
    /// 这里每个控件都要一个明确的 `Rect`，所以行与行之间天然对齐。
    ///
    /// 用法（一行 = 一个 Row，行内自己排）：
    /// <code>
    /// using (HoConstraintEditorControls.Row())
    /// {
    ///     HoConstraintEditorControls.Label("频率", HoConstraintEditorTheme.LabelWidthSm);
    ///     value = HoConstraintEditorControls.NumberField(HoConstraintEditorControls.Next(42f), value, "Hz");
    ///     HoConstraintEditorControls.Flex();
    ///     HoConstraintEditorControls.Meter(HoConstraintEditorControls.Next(MeterWidth), v, 0, 1, color);
    /// }
    /// </code>
    /// </summary>
    internal static class HoConstraintEditorControls
    {
        // ══════════════════════════════════════════════════════════════
        // 布局骨架
        // ══════════════════════════════════════════════════════════════

        /// <summary>一行（默认 20px 高，控件垂直居中）。</summary>
        public static IDisposable Row(bool tight = false)
        {
            return new RowScope(tight ? HoConstraintEditorTheme.RowHeightTight : HoConstraintEditorTheme.RowHeight);
        }

        /// <summary>缩进块：里面所有行整体右移。</summary>
        public static IDisposable Indent(float width = HoConstraintEditorTheme.Indent)
        {
            return new IndentScope(width);
        }

        /// <summary>卡片：1px 描边 + 内边距，里面通常放若干行。</summary>
        public static IDisposable Card(bool alt = false)
        {
            return new CardScope(alt);
        }

        /// <summary>在行内申请一段固定宽度的位置（控件高 18，行高 20 时自动垂直居中）。</summary>
        public static Rect Next(float width)
        {
            Rect rect = GUILayoutUtility.GetRect(width, width, HoConstraintEditorTheme.ControlHeight, HoConstraintEditorTheme.ControlHeight);
            return rect;
        }

        /// <summary>在行内申请一段可伸缩的位置（占满剩余，最小的给 minWidth）。</summary>
        public static Rect NextFlexible(float minWidth = 40.0f)
        {
            return GUILayoutUtility.GetRect(minWidth, 4000.0f, HoConstraintEditorTheme.ControlHeight, HoConstraintEditorTheme.ControlHeight, GUILayout.ExpandWidth(true));
        }

        /// <summary>按内容宽度申请位置（文字量出来多宽就多宽）。</summary>
        public static Rect NextAuto(string text, GUIStyle style, float extra = 0.0f, float minWidth = 0.0f)
        {
            float width = Mathf.Max(minWidth, HoConstraintEditorTheme.Measure(style, text) + extra);
            return Next(width);
        }

        public static void Flex()
        {
            GUILayout.FlexibleSpace();
        }

        public static void Gap(float width = HoConstraintEditorTheme.Gutter)
        {
            GUILayout.Space(width);
        }

        /// <summary>一条分隔线（横向铺满，带上下留白）。</summary>
        public static void Separator(float top = 5.0f, float bottom = 5.0f)
        {
            GUILayout.Space(top);
            Rect rect = GUILayoutUtility.GetRect(0.0f, 4000.0f, 1.0f, 1.0f, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, HoConstraintEditorTheme.SeparatorColor);
            }

            GUILayout.Space(bottom);
        }

        // ══════════════════════════════════════════════════════════════
        // 分区头 / 标题
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 分区头：28px，主色淡染渐变 + 左侧 3px 色条，整行可点折叠，右侧显示摘要。
        /// </summary>
        public static bool Section(ref bool expanded, string title, string summary, Color accent)
        {
            Rect rect = GUILayoutUtility.GetRect(0.0f, 4000.0f, HoConstraintEditorTheme.SectionHeight, HoConstraintEditorTheme.SectionHeight, GUILayout.ExpandWidth(true));
            Event evt = Event.current;
            bool hover = rect.Contains(evt.mousePosition);

            if (evt.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, HoConstraintEditorTheme.CardColor);
                GUI.DrawTexture(rect, HoConstraintEditorTheme.SectionTexture(accent), ScaleMode.StretchToFill, true);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, HoConstraintEditorTheme.AccentBarWidth, rect.height), accent);
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1.0f, rect.width, 1.0f), HoConstraintEditorTheme.LineColor);
                if (hover)
                {
                    EditorGUI.DrawRect(rect, new Color(1.0f, 1.0f, 1.0f, 0.03f));
                }
            }

            Rect caretRect = new Rect(rect.x + 8.0f, rect.y + 6.0f, 12.0f, 16.0f);
            expanded = EditorGUI.Foldout(caretRect, expanded, GUIContent.none, true);

            Rect summaryRect = new Rect(rect.xMax - 10.0f - 200.0f, rect.y + 6.0f, 200.0f, 16.0f);
            float titleWidth = Mathf.Max(60.0f, summaryRect.x - caretRect.xMax - 8.0f);
            GUI.Label(new Rect(caretRect.xMax + 4.0f, rect.y, titleWidth, rect.height), title, HoConstraintEditorTheme.SectionTitle);
            GUI.Label(summaryRect, summary, HoConstraintEditorTheme.SectionSummary);

            // 整行都能点（点在箭头上时 Foldout 已经处理并把事件标记成 Used）
            if (evt.type == EventType.MouseDown && rect.Contains(evt.mousePosition))
            {
                expanded = !expanded;
                evt.Use();
            }

            return expanded;
        }

        /// <summary>面板标题行：名字 + 状态胶囊 + 右侧信息。</summary>
        public static void Title(string title, string rightText, params (string Text, bool On)[] pills)
        {
            using (Row())
            {
                GUI.Label(NextAuto(title, HoConstraintEditorTheme.Bold, 6.0f, 80.0f), title, HoConstraintEditorTheme.Bold);
                for (int i = 0; i < pills.Length; i++)
                {
                    Pill(pills[i].Text, pills[i].On);
                }

                Flex();
                if (!string.IsNullOrEmpty(rightText))
                {
                    GUI.Label(NextAuto(rightText, HoConstraintEditorTheme.Caption, 0.0f, 40.0f), rightText, HoConstraintEditorTheme.Caption);
                }
            }
        }

        /// <summary>小状态胶囊（如「写入 开」）。`GUIStyle.CalcSize` 已含内边距，所以只补一点点余量。</summary>
        public static void Pill(string text, bool on)
        {
            GUIStyle style = on ? HoConstraintEditorTheme.SegmentOn : HoConstraintEditorTheme.SegmentOff;
            Rect rect = Next(HoConstraintEditorTheme.Measure(style, text) + 4.0f);
            if (Event.current.type == EventType.Repaint)
            {
                style.Draw(rect, new GUIContent(text), false, false, false, false);
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 基础控件
        // ══════════════════════════════════════════════════════════════

        /// <summary>参数标签（固定宽度，右对齐不可，左对齐）；带 tooltip。</summary>
        public static void Label(string text, float width = HoConstraintEditorTheme.LabelWidth, string tooltip = null)
        {
            GUI.Label(Next(width), new GUIContent(text, tooltip), HoConstraintEditorTheme.Label);
        }

        /// <summary>读数/说明小字。</summary>
        public static void Caption(string text, string tooltip = null)
        {
            GUI.Label(NextAuto(text, HoConstraintEditorTheme.Caption), new GUIContent(text, tooltip), HoConstraintEditorTheme.Caption);
        }

        /// <summary>
        /// 说明小字，但**允许被压窄**（超出就截断）。
        ///
        /// 为什么要它：<see cref="Caption"/> 的宽度是"量出来多宽就多宽"，放在一行里会把整行
        /// 撑到超过视口宽，于是右边的 <c>ScrollView</c> 长出**横向滚动条**。
        /// 窗口里那些"（不起作用）""控制器里没有就跳过"正是这种长文案。
        /// 换成本方法之后，窄窗口下它会缩、会截断，而**行本身仍然装得下**。
        /// </summary>
        public static void CaptionTrim(string text, float maxWidth = 160.0f, string tooltip = null)
        {
            float width = HoConstraintEditorTheme.Measure(HoConstraintEditorTheme.Caption, text);
            GUILayout.Label(
                new GUIContent(text, tooltip ?? text),
                HoConstraintEditorTheme.Caption,
                GUILayout.Width(Mathf.Min(width, maxWidth)));
        }

        public static void ValueText(string text, float width = 38.0f)
        {
            GUI.Label(Next(width), text, HoConstraintEditorTheme.Value);
        }

        /// <summary>数字格：定宽 + 可选单位后缀（单位只是画在后面，不参与输入）。</summary>
        public static float NumberField(Rect rect, float value, string unit = null, string tooltip = null)
        {
            float unitWidth = string.IsNullOrEmpty(unit) ? 0.0f : HoConstraintEditorTheme.Measure(HoConstraintEditorTheme.Caption, unit) + 2.0f;
            Rect fieldRect = new Rect(rect.x, rect.y, rect.width - unitWidth, rect.height);
            float result = EditorGUI.FloatField(fieldRect, value, HoConstraintEditorTheme.NumberField);
            if (!string.IsNullOrEmpty(unit))
            {
                GUI.Label(new Rect(fieldRect.xMax, rect.y, unitWidth, rect.height), new GUIContent(unit, tooltip), HoConstraintEditorTheme.Caption);
            }
            else if (!string.IsNullOrEmpty(tooltip))
            {
                GUI.Label(rect, new GUIContent(string.Empty, tooltip));
            }

            return result;
        }

        public static float NumberField(float value, string unit = null, string tooltip = null, float width = HoConstraintEditorTheme.FieldWidth)
        {
            return NumberField(Next(width), value, unit, tooltip);
        }

        public static int IntField(Rect rect, int value, string tooltip = null)
        {
            int result = EditorGUI.IntField(rect, value, HoConstraintEditorTheme.NumberField);
            if (!string.IsNullOrEmpty(tooltip))
            {
                GUI.Label(rect, new GUIContent(string.Empty, tooltip));
            }

            return result;
        }

        /// <summary>自绘勾选框：14px 方块 + 勾 + 文字。</summary>
        public static bool Toggle(string text, bool value, string tooltip = null, float width = 0.0f)
        {
            float boxSize = 14.0f;
            float textWidth = HoConstraintEditorTheme.Measure(HoConstraintEditorTheme.Value, text);
            float total = width > 0.0f ? width : boxSize + 5.0f + textWidth;
            Rect rect = Next(total);

            Rect boxRect = new Rect(rect.x, rect.y + 2.0f, boxSize, boxSize);
            Rect textRect = new Rect(boxRect.xMax + 5.0f, rect.y, total - boxSize - 5.0f, rect.height);

            Event evt = Event.current;
            bool hover = rect.Contains(evt.mousePosition);
            if (evt.type == EventType.Repaint)
            {
                Color fill = value
                    ? new Color(0.184f, 0.431f, 0.310f)
                    : new Color(0.180f, 0.180f, 0.180f);
                Color border = value
                    ? new Color(0.243f, 0.561f, 0.400f)
                    : new Color(0.333f, 0.333f, 0.333f);
                if (hover)
                {
                    fill = Color.Lerp(fill, Color.white, 0.06f);
                }

                EditorGUI.DrawRect(boxRect, border);
                EditorGUI.DrawRect(new Rect(boxRect.x + 1.0f, boxRect.y + 1.0f, boxRect.width - 2.0f, boxRect.height - 2.0f), fill);
                if (value)
                {
                    GUI.Label(boxRect, "✓", HoConstraintEditorTheme.CheckGlyph);
                }

                GUI.Label(textRect, new GUIContent(text, tooltip), HoConstraintEditorTheme.Value);
            }

            if (evt.type == EventType.MouseDown && evt.button == 0 && rect.Contains(evt.mousePosition))
            {
                value = !value;
                GUI.changed = true;
                evt.Use();
            }

            return value;
        }

        /// <summary>
        /// 分段胶囊：2~4 个选项直接点，不开下拉（比 popup 少一次点击、也更好看）。
        /// 宽度按文字量出来，返回选中的下标。
        /// </summary>
        public static int Segmented(Rect rect, int index, string[] options, string tooltip = null)
        {
            float x = rect.x;
            int result = index;

            for (int i = 0; i < options.Length; i++)
            {
                bool on = i == index;
                GUIStyle style = on ? HoConstraintEditorTheme.SegmentOn : HoConstraintEditorTheme.SegmentOff;
                float width = HoConstraintEditorTheme.Measure(style, options[i]) + 4.0f;
                Rect cell = new Rect(x, rect.y, width, rect.height);
                x += width;

                Event evt = Event.current;
                if (evt.type == EventType.Repaint)
                {
                    style.Draw(cell, new GUIContent(options[i]), false, cell.Contains(evt.mousePosition), false, false);
                }

                if (evt.type == EventType.MouseDown && evt.button == 0 && cell.Contains(evt.mousePosition))
                {
                    result = i;
                    GUI.changed = true;
                    evt.Use();
                }
            }

            if (!string.IsNullOrEmpty(tooltip))
            {
                GUI.Label(new Rect(rect.x, rect.y, Mathf.Max(1.0f, x - rect.x), rect.height), new GUIContent(string.Empty, tooltip));
            }

            return result;
        }

        /// <summary>分段胶囊占多宽（布局前先算）。</summary>
        public static float SegmentedWidth(string[] options)
        {
            float width = 0.0f;
            for (int i = 0; i < options.Length; i++)
            {
                GUIStyle style = i == 0 ? HoConstraintEditorTheme.SegmentOn : HoConstraintEditorTheme.SegmentOff;
                width += HoConstraintEditorTheme.Measure(style, options[i]) + 4.0f;
            }

            return width;
        }

        /// <summary>枚举下拉：选项少就用分段胶囊，多了才用菜单。</summary>
        public static int EnumControl(Rect rect, int index, string[] options, bool segmented = true, string tooltip = null)
        {
            if (segmented && options.Length <= 4)
            {
                return Segmented(rect, index, options, tooltip);
            }

            return Dropdown(rect, index, options, tooltip);
        }

        /// <summary>
        /// 下拉菜单（选项很多时用，比如 ramp 预设）。
        ///
        /// ⚠️⚠️ **本方法拿不到用户的选择**，不要用它表达"值可以被改"的控件。
        /// `EditorUtility.DisplayCustomMenu` 是**异步**的：它把菜单弹出去就返回，
        /// 回调在**下一帧甚至更晚**才跑，而这里的 `result` 是局部变量、函数早就返回了 ——
        /// 闭包里那句 `result = selected` 改的是一个没人再读的变量。所以本方法**永远返回入参**。
        ///
        /// 要"能改"的枚举控件用 <see cref="Segmented"/>（同步、当场返回 index），
        /// 或把 `EnumControl` 的 `segmented` 传 `true`。这个坑真踩过：
        /// 修饰符类型切不动，就是因为调用处传了 `segmented: false`。
        /// </summary>
        public static int Dropdown(Rect rect, int index, string[] options, string tooltip = null)
        {
            int result = index;
            string current = index >= 0 && index < options.Length ? options[index] : "—";
            if (ButtonInternal(rect, current + "  ▾", HoConstraintEditorTheme.Button, tooltip))
            {
                GUIContent[] items = new GUIContent[options.Length];
                for (int i = 0; i < options.Length; i++)
                {
                    items[i] = new GUIContent(options[i]);
                }

                EditorUtility.DisplayCustomMenu(
                    rect,
                    items,
                    index,
                    (userData, contents, selected) => { result = selected; },
                    null);
            }

            return result;
        }

        /// <summary>普通按钮。</summary>
        public static bool Button(string text, string tooltip = null, bool primary = false, float width = 0.0f)
        {
            GUIStyle style = primary ? HoConstraintEditorTheme.ButtonPrimary : HoConstraintEditorTheme.Button;
            Rect rect = Next(width > 0.0f ? width : HoConstraintEditorTheme.Measure(style, text) + 4.0f);
            return ButtonInternal(rect, text, style, tooltip);
        }

        private static bool ButtonInternal(Rect rect, string text, GUIStyle style, string tooltip)
        {
            Event evt = Event.current;
            bool hover = rect.Contains(evt.mousePosition);
            bool clicked = false;
            if (evt.type == EventType.Repaint)
            {
                style.Draw(rect, new GUIContent(text, tooltip), false, hover, false, false);
            }

            if (evt.type == EventType.MouseDown && evt.button == 0 && hover)
            {
                clicked = true;
                evt.Use();
            }

            return clicked;
        }

        /// <summary>小图标按钮（▾ / ✕ / ⋮ 这种）。</summary>
        public static bool IconButton(string glyph, string tooltip, float width = 18.0f)
        {
            return ButtonInternal(Next(width), glyph, HoConstraintEditorTheme.IconButton, tooltip);
        }

        /// <summary>行内的「▸ 细节」折叠头（自带宽度，返回新状态）。</summary>
        public static bool InlineFoldout(bool expanded, string text, string tooltip = null)
        {
            Rect rect = NextAuto("▸ " + text, HoConstraintEditorTheme.Foldout, 6.0f);
            return EditorGUI.Foldout(rect, expanded, new GUIContent(text, tooltip), true, HoConstraintEditorTheme.Foldout);
        }

        // ══════════════════════════════════════════════════════════════
        // 读数：迷你滑杆 + 横条
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 迷你滑杆：10px 矮条，拖动改值，双击回默认值。
        /// 比 Unity 自带 Slider 矮一半，也不会把一行撑开。
        /// </summary>
        public static float MiniSlider(float value, float min, float max, float defaultValue, string tooltip = null, float width = 0.0f)
        {
            Rect rect = width > 0.0f ? Next(width) : NextFlexible(60.0f);
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            Event evt = Event.current;

            switch (evt.GetTypeForControl(controlId))
            {
                case EventType.MouseDown:
                    if (evt.button == 0 && rect.Contains(evt.mousePosition))
                    {
                        if (evt.clickCount == 2)
                        {
                            value = defaultValue;
                            GUI.changed = true;
                            evt.Use();
                        }
                        else
                        {
                            GUIUtility.hotControl = controlId;
                            value = ValueFromMouse(rect, evt.mousePosition.x, min, max);
                            GUI.changed = true;
                            evt.Use();
                        }
                    }

                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == controlId)
                    {
                        value = ValueFromMouse(rect, evt.mousePosition.x, min, max);
                        GUI.changed = true;
                        evt.Use();
                    }

                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == controlId)
                    {
                        GUIUtility.hotControl = 0;
                        evt.Use();
                    }

                    break;

                case EventType.Repaint:
                    DrawBarBackground(rect);
                    float t = Mathf.InverseLerp(min, max, Mathf.Clamp(value, min, max));
                    EditorGUI.DrawRect(
                        new Rect(rect.x, rect.y + 1.0f, Mathf.Max(0.0f, t * rect.width), rect.height - 2.0f),
                        new Color(0.298f, 0.498f, 0.722f));
                    EditorGUI.DrawRect(
                        new Rect(rect.x + (t * rect.width) - 1.0f, rect.y - 1.0f, 2.0f, rect.height + 2.0f),
                        new Color(0.867f, 0.902f, 0.949f));
                    if (!string.IsNullOrEmpty(tooltip))
                    {
                        GUI.Label(rect, new GUIContent(string.Empty, tooltip));
                    }

                    break;
            }

            return value;
        }

        /// <summary>实时横条：单极从左往右长、双极从中轴往两边长；`ghost` 画一根白刻度表示弹簧前的原始值。</summary>
        public static void Meter(Rect rect, float value, float min, float max, Color color, float ghost = float.NaN, string tooltip = null)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            float range = max - min;
            if (range <= 1e-6f)
            {
                range = 1.0f;
            }

            DrawBarBackground(rect);

            float value01 = Mathf.Clamp01((value - min) / range);
            bool bipolar = min < 0.0f && max > 0.0f;
            float zero01 = bipolar ? Mathf.Clamp01((0.0f - min) / range) : 0.0f;
            float from = Mathf.Min(zero01, value01);
            float to = Mathf.Max(zero01, value01);

            if (to - from > 0.0005f)
            {
                Rect fill = new Rect(rect.x + (from * rect.width), rect.y + 1.0f, (to - from) * rect.width, rect.height - 2.0f);
                EditorGUI.DrawRect(fill, color);
                EditorGUI.DrawRect(new Rect(fill.x, fill.y, fill.width, 1.0f), Color.Lerp(color, Color.white, 0.45f));
            }

            if (bipolar)
            {
                EditorGUI.DrawRect(
                    new Rect(rect.x + (zero01 * rect.width) - 0.5f, rect.y, 1.0f, rect.height),
                    new Color(1.0f, 1.0f, 1.0f, 0.34f));
            }

            if (!float.IsNaN(ghost))
            {
                float ghost01 = Mathf.Clamp01((ghost - min) / range);
                EditorGUI.DrawRect(
                    new Rect(rect.x + (ghost01 * rect.width) - 1.0f, rect.y + 2.0f, 2.0f, rect.height - 4.0f),
                    new Color(1.0f, 1.0f, 1.0f, 0.78f));
            }

            if (!string.IsNullOrEmpty(tooltip))
            {
                GUI.Label(rect, new GUIContent(string.Empty, tooltip));
            }
        }

        /// <summary>读数行：`标签 [横条] 数值`，整块固定宽度，可塞在别的行尾巴上。</summary>
        public static void MeterRow(
            float value,
            float min,
            float max,
            Color color,
            string valueText,
            string label = null,
            float ghost = float.NaN,
            string tooltip = null,
            float labelWidth = HoConstraintEditorTheme.LabelWidthSm,
            float barWidth = HoConstraintEditorTheme.MeterBarWidth,
            float valueWidth = HoConstraintEditorTheme.MeterValueWidth)
        {
            if (!string.IsNullOrEmpty(label))
            {
                GUI.Label(Next(labelWidth), new GUIContent(label, tooltip), HoConstraintEditorTheme.Label);
            }

            Meter(Next(barWidth), value, min, max, color, ghost, tooltip);
            if (!string.IsNullOrEmpty(valueText))
            {
                GUI.Label(Next(valueWidth), valueText, HoConstraintEditorTheme.Value);
            }
        }

        public static float MeterRowWidth(string label, float labelWidth = HoConstraintEditorTheme.LabelWidthSm)
        {
            return (string.IsNullOrEmpty(label) ? 0.0f : labelWidth)
                + HoConstraintEditorTheme.MeterBarWidth
                + HoConstraintEditorTheme.MeterValueWidth;
        }

        private static void DrawBarBackground(Rect rect)
        {
            EditorGUI.DrawRect(rect, HoConstraintEditorTheme.WellColor);
            Color frame = new Color(1.0f, 1.0f, 1.0f, 0.10f);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1.0f), frame);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1.0f, rect.width, 1.0f), frame);
        }

        private static float ValueFromMouse(Rect rect, float mouseX, float min, float max)
        {
            float t = Mathf.Clamp01((mouseX - rect.x) / Mathf.Max(1.0f, rect.width));
            return Mathf.Lerp(min, max, t);
        }

        // ══════════════════════════════════════════════════════════════
        // 键名格
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 键名格：`[键名输入] [▾] [状态点]`。
        /// 空着的时候显示「选键…」占位符，并把 ▾ 画成主色按钮 —— 面板上最该点的地方一眼就看出来。
        /// 状态点用颜色说话：绿点 + 绑定数 = 解析到了，红叉 = 这些网格上没这个键。
        /// </summary>
        public static string KeyField(string key, int bindings, string statusTooltip, string dropdownTooltip, out Rect dropdownRect, out bool dropdownClicked)
        {
            Rect rect = NextFlexible(70.0f);
            const float ButtonWidth = 18.0f;
            const float StatusWidth = 20.0f;

            bool empty = string.IsNullOrEmpty(key);
            Rect fieldRect = new Rect(rect.x, rect.y, Mathf.Max(24.0f, rect.width - ButtonWidth - StatusWidth - 4.0f), rect.height);
            Rect buttonRect = new Rect(fieldRect.xMax + 2.0f, rect.y, ButtonWidth, rect.height);
            Rect statusRect = new Rect(buttonRect.xMax + 2.0f, rect.y, StatusWidth, rect.height);

            bool missing = !empty && bindings == 0;
            key = EditorGUI.TextField(fieldRect, key, missing ? HoConstraintEditorTheme.FieldMissing : HoConstraintEditorTheme.Field);

            if (empty && Event.current.type == EventType.Repaint)
            {
                GUI.Label(
                    new Rect(fieldRect.x + 5.0f, fieldRect.y, fieldRect.width - 8.0f, fieldRect.height),
                    new GUIContent("选键…", "还没有选键：点右边的 ▾ 从网格上真实存在的形态键里选。"),
                    HoConstraintEditorTheme.Caption);
            }

            dropdownRect = buttonRect;
            dropdownClicked = ButtonInternal(
                buttonRect,
                "▾",
                empty ? HoConstraintEditorTheme.ButtonPrimary : HoConstraintEditorTheme.IconButton,
                dropdownTooltip);

            if (Event.current.type == EventType.Repaint)
            {
                string glyph;
                Color color;
                if (empty)
                {
                    glyph = "○";
                    color = HoConstraintEditorTheme.WarningColor;
                }
                else if (missing)
                {
                    glyph = "✕";
                    color = HoConstraintEditorTheme.ErrorColor;
                }
                else
                {
                    glyph = "●" + bindings;
                    color = HoConstraintEditorTheme.AccentOutput;
                }

                GUIStyle style = new GUIStyle(HoConstraintEditorTheme.Caption) { alignment = TextAnchor.MiddleCenter };
                style.normal.textColor = color;
                GUI.Label(statusRect, new GUIContent(glyph, statusTooltip), style);
            }

            return key;
        }

        // ══════════════════════════════════════════════════════════════
        // 行 / 缩进 / 卡片的 scope
        // ══════════════════════════════════════════════════════════════

        private sealed class RowScope : IDisposable
        {
            public RowScope(float height)
            {
                EditorGUILayout.BeginHorizontal(GUILayout.Height(height));
            }

            public void Dispose()
            {
                EditorGUILayout.EndHorizontal();
            }
        }

        private sealed class IndentScope : IDisposable
        {
            public IndentScope(float width)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(width);
                GUILayout.BeginVertical();
            }

            public void Dispose()
            {
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }
        }

        private sealed class CardScope : IDisposable
        {
            public CardScope(bool alt)
            {
                EditorGUILayout.BeginVertical(alt ? HoConstraintEditorTheme.CardAlt : HoConstraintEditorTheme.Card);
            }

            public void Dispose()
            {
                EditorGUILayout.EndVertical();
                GUILayout.Space(2.0f);
            }
        }
    }
}
