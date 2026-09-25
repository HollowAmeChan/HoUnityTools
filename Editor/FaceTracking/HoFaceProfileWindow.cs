using System.Collections.Generic;
using Hollow.HoUnityTools.Editor.Constraints;
using Hollow.HoUnityTools.Editor.FaceTracking;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>
    /// **面捕中间层配置窗口**：左边是**输入行 / 输出行**的目录，右边是选中那一行的全部细节。
    ///
    /// 它编辑的数据 = 运行时那份 <see cref="HoFaceMiddleware"/>（磁盘上的 `.hoface.json`）。
    /// 面板上的顺序就是生效顺序 —— 左边列表的顺序 = 写参数的顺序，修饰符列表的顺序 = 串起来的顺序。
    ///
    /// **中线可以左右拖**（双击复位）：右边那一列在窄窗口下装不下时，把左边收窄比左右滚要顺手。
    ///
    /// 只有一处"一眼看出坏在哪"：**表达式解析不过**。这里**不检查变量是不是 ARKit 键** ——
    /// 映射是自由的，输入行的左值是规范名、输出行的变量可以是线名或上一行的结果，
    /// 而且"某个名字这一帧有没有来源"只有跑起来才知道（缺键的行冻结，不是报错）。见 <see cref="DrawVariableList"/>。
    ///
    /// 保存后会让宿主重新读一遍这份配置（见 <see cref="SaveProfile"/>）。
    /// </summary>
    public sealed class HoFaceProfileWindow : EditorWindow
    {
        private const float LeftWidthMin = 180.0f;
        private const float LeftWidthMax = 560.0f;
        private const float SplitterWidth = 6.0f;
        private const float CurveHeight = 90.0f;
        private const string NewProfileName = "ho-2d-test1.hoface.json";
        private const string LeftWidthPref = "HoUnityTools.FaceProfile.LeftWidth";

        private TextAsset profile;
        private HoFaceMiddleware middleware;
        private int selected = -1;
        private string search = "";
        private string message;
        private bool messageIsError;
        private Vector2 listScroll;
        private bool dirty;

        /// <summary>左边目录的宽度；**可以拖中线改**，存在 EditorPrefs 里跨窗口记住。</summary>
        private float leftWidth = 300.0f;
        private bool draggingSplitter;

        [MenuItem("HoUnityTools/面捕/配置文件", false, 30)]
        private static void Open()
        {
            GetWindow<HoFaceProfileWindow>(false, "配置文件", true);
        }

        /// <summary>从主面板跳进来：直接盯着那份配置，不预选任何一行。</summary>
        public static void Open(TextAsset profile)
        {
            HoFaceProfileWindow window = GetWindow<HoFaceProfileWindow>(false, "配置文件", true);
            window.profile = profile;
            window.selected = -1;
            window.ReloadProfile(true);
            window.Repaint();
        }

        private void OnEnable()
        {
            minSize = new Vector2(560.0f, 420.0f);
            leftWidth = Mathf.Clamp(EditorPrefs.GetFloat(LeftWidthPref, leftWidth), LeftWidthMin, LeftWidthMax);
            if (middleware == null && profile != null) ReloadProfile(true);
        }

        // ══════════════════════════════════════════════════════════════
        // 骨架
        // ══════════════════════════════════════════════════════════════

        private void OnGUI()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                DrawTopBar();
                DrawColumnSplit();

                // 面板上的字段真的被改过（自绘按钮自己设 GUI.changed）——统一在这里记脏
                if (GUI.changed)
                {
                    dirty = true;
                }
            }
        }

        private void DrawColumnSplit()
        {
            // 左列最多占窗口的一半 —— 拖到头也不该把右边挤没（右边还要放曲线编辑器）。
            float limit = Mathf.Max(LeftWidthMin, position.width * 0.5f);
            float width = Mathf.Clamp(leftWidth, LeftWidthMin, Mathf.Min(LeftWidthMax, limit));

            using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(width), GUILayout.ExpandHeight(true)))
                {
                    DrawLeftColumn();
                }

                DrawSplitter(ref leftWidth, LeftWidthMin, Mathf.Min(LeftWidthMax, limit));

                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
                {
                    if (middleware == null)
                    {
                        HoConstraintEditorControls.CaptionTrim("（还没载入配置）", 190.0f,
                            "上面选一份 .hoface.json 文本资产，或点「新建配置…」。");
                    }
                    else if (selected < 0 || selected >= middleware.outputs.Count)
                    {
                        HoConstraintEditorControls.CaptionTrim("（左边选一行）", 190.0f,
                            "点左边的参数名看它的表达式、曲线与修饰符。");
                    }
                    else
                    {
                        DrawRightColumn(selected);
                    }
                }
            }
        }

        /// <summary>
        /// **可拖的中线**：左键按住左右拖，改 <paramref name="width"/>。
        /// 双击回到默认宽度。松手时记住（<see cref="EditorPrefs"/>）。
        ///
        /// 为什么要有它：右边那一列在窄窗口下会装不下，原来只能靠**横向滚动条**看全；
        /// 让用户自己把左边收窄，比让他左右滚要顺手得多。
        /// </summary>
        private void DrawSplitter(ref float width, float min, float max)
        {
            Rect rect = GUILayoutUtility.GetRect(SplitterWidth, SplitterWidth, 0.0f, 4000.0f, GUILayout.ExpandHeight(true));
            int control = GUIUtility.GetControlID(FocusType.Passive);

            switch (Event.current.type)
            {
                case EventType.Repaint:
                    EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);
                    Color line = HoConstraintEditorTheme.SeparatorColor;
                    if (draggingSplitter) line = new Color(line.r, line.g, line.b, 1.0f);
                    EditorGUI.DrawRect(new Rect(rect.x + (rect.width - 1.0f) * 0.5f, rect.y, 1.0f, rect.height), line);
                    break;

                case EventType.MouseDown:
                    if (rect.Contains(Event.current.mousePosition))
                    {
                        if (Event.current.clickCount == 2)
                        {
                            // 双击复位
                            width = 300.0f;
                            EditorPrefs.SetFloat(LeftWidthPref, width);
                            Event.current.Use();
                            Repaint();
                        }
                        else
                        {
                            draggingSplitter = true;
                            GUIUtility.hotControl = control;
                            Event.current.Use();
                        }
                    }
                    break;

                case EventType.MouseDrag:
                    if (draggingSplitter)
                    {
                        width = Mathf.Clamp(width + Event.current.delta.x, min, max);
                        Event.current.Use();
                        Repaint();
                    }
                    break;

                case EventType.MouseUp:
                    if (draggingSplitter)
                    {
                        draggingSplitter = false;
                        GUIUtility.hotControl = 0;
                        EditorPrefs.SetFloat(LeftWidthPref, width);
                        Event.current.Use();
                    }
                    break;
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 顶栏
        // ══════════════════════════════════════════════════════════════

        private void DrawTopBar()
        {
            using (HoConstraintEditorControls.Card())
            {
                // ⚠️ 顶栏**不在任何 ScrollView 里**（见 OnGUI），所以它一旦撑宽就是整个窗口出滚动条。
                // 原来这一行是 `配置 + 路径框 + 新建配置… + 保存 + 重新载入` 五个东西排一行，
                // 最小宽度约 420px —— 窗口一窄就爆。现在拆成两行：第一行只放路径，第二行放按钮，
                // 最小宽度掉到约 200px。
                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("配置", HoConstraintEditorTheme.LabelWidthSm, "要被编辑的那份 .hoface.json 文本资产。");
                    Rect rect = HoConstraintEditorControls.NextFlexible(80.0f);
                    EditorGUI.BeginChangeCheck();
                    var picked = (TextAsset)EditorGUI.ObjectField(rect, profile, typeof(TextAsset), false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        profile = picked;
                        ReloadProfile(false);
                    }
                }

                using (HoConstraintEditorControls.Row())
                {
                    if (HoConstraintEditorControls.Button("新建配置…", "在工程里新建一份内置默认表。「保存」写文件。「重新载入」从磁盘重读。"))
                        NewProfile();
                    HoConstraintEditorControls.Gap();
                    using (new EditorGUI.DisabledScope(profile == null))
                    {
                        if (HoConstraintEditorControls.Button("保存", "写回这份文本资产（无 BOM）。同一条参数在场景里会被通知重新解析。", true))
                            SaveProfile();
                        HoConstraintEditorControls.Gap();
                        if (HoConstraintEditorControls.Button("重新载入", "丢掉内存里的改动，从磁盘重读。"))
                            ReloadProfile(false);
                    }

                    HoConstraintEditorControls.Flex();
                    if (dirty)
                    {
                        HoConstraintEditorControls.Pill("有未保存改动", true);
                    }
                }

                using (HoConstraintEditorControls.Row(true))
                {
                    // ⚠️ 面板以前在这里写"用内置默认" —— 那条兜底已经删了（空 = 空表，见 HoFaceDebugSettings.Outputs()），
                    // 对用户说错话比不说更糟：他会以为没选配置也在跑。
                    string path = profile == null ? "（未选配置 · 这一层不做事）" : AssetDatabase.GetAssetPath(profile);
                    HoConstraintEditorControls.Caption(path, "配置文件在工程里的路径。");
                    if (middleware != null && middleware.outputs != null)
                    {
                        HoConstraintEditorControls.Gap();
                        HoConstraintEditorControls.Caption("· " + middleware.outputs.Count + " 行");
                    }
                }
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 左列：搜索 + 行目录
        // ══════════════════════════════════════════════════════════════

        private void DrawLeftColumn()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandHeight(true)))
            {
                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("搜索", HoConstraintEditorTheme.LabelWidthSm, "按参数名过滤（不区分大小写）。");
                    // 最小宽度取小：这一行也在左列那个 ScrollView 里，撑宽了就出横向滚动条。
                    search = EditorGUI.TextField(HoConstraintEditorControls.NextFlexible(40.0f), search, HoConstraintEditorTheme.Field);
                }

                using (HoConstraintEditorControls.Row())
                {
                    if (HoConstraintEditorControls.Button("+ 新增", "在列表末尾加一行。", false, 64.0f))
                    {
                        AddOutput();
                    }

                    HoConstraintEditorControls.Flex();
                    HoConstraintEditorControls.CaptionTrim("顺序 = 写参数顺序", 130.0f,
                        "上面的行先写；重复写同一个参数时后面的覆盖前面的。");
                }

                HoConstraintEditorControls.Separator(3.0f, 3.0f);

                // 只有滚动区吃掉余下的高度，状态行才不会被长列表推出窗口。
                // ⚠️ `alwaysShowHorizontal: false` —— 左列的每一行都自己算好宽度（见 DrawLeftRow），
                // 内容永远不该比列宽还宽；出现横向滚动条一定是宽度算错了，不该让用户滚着看。
                if (middleware != null && middleware.outputs != null)
                {
                    using (new EditorGUILayout.VerticalScope(GUILayout.ExpandHeight(true)))
                    {
                        listScroll = EditorGUILayout.BeginScrollView(listScroll, false, true);
                        for (int i = 0; i < middleware.outputs.Count; i++)
                        {
                            if (Matches(middleware.outputs[i], search)) DrawLeftRow(i);
                        }

                        EditorGUILayout.EndScrollView();
                    }
                }

                DrawStatusLine();
            }
        }

        /// <summary>
        /// 左列的一行：**整行显式绘制**，行内带 ↑ ↓ ✕ 三个按钮，**按钮以外整行可点**。
        ///
        /// 为什么不用 `Card()` / `Row()` / `IconButton()` 那套布局组：
        /// 那套要靠 `GUILayoutUtility.GetLastRect()` **反推**矩形，而反推出来的东西是脆的 ——
        /// 我连踩三次（读到最后一行而不是卡片、被卡片盖住、按钮与热区不在同一坐标系），
        /// 表现为"曲线不画了""整行点不动"。现在**自己申请整行的 Rect，里面每个位置都算出来**，
        /// 背景、文字、按钮、热区全都对着同一个已知矩形画，不再依赖任何"最后画的矩形"。
        ///
        /// 行内保留那三个按钮（右面板**不再**重复一份）：`↑ ↓ ✕` 走显式矩形 + 自判点击，
        /// 命中就 `Event.Use()` 掉，所以"整行可点"不会跟它们抢。
        ///
        /// 布局（{@link RowHeight} 高）：
        ///     ╔══════════════════════════════════════════════╗
        ///     ║ 名字（左对齐）                    ↑   ↓   ✕  ║  ← 第一行
        ///     ║              表达式（居中）                    ║  ← 第二行
        ///     ╚══════════════════════════════════════════════╝
        ///     曲线画在整块的淡色底上；点按钮以外的任何地方都选中这一行。
        /// </summary>
        private void DrawLeftRow(int index)
        {
            HoFaceOutput output = middleware.outputs[index];
            if (output == null)
            {
                return;
            }

            bool bad = !HoFaceExpression.TryParse(output.expression, out _, out _);
            bool delayed = HasDelay(output);
            string name = string.IsNullOrEmpty(output.parameter) ? "(未命名)" : output.parameter;

            // ⚠️ 横向滚动条的**真正来源**就在这一行。
            // `GUILayout.ExpandWidth(true)` 在 ScrollView 里会涨到**内容宽度**，而内容宽度可以
            // 大于可见宽度 ⇒ 于是长出横向滚动条，而且下一帧更宽（内容被上一次的宽度喂大）。
            // `ExpandWidth(false)` 只占**视口**宽、不参与内容测量，两趟拿到的是同一个矩形，
            // 所以行永远贴合列宽。
            Rect row = GUILayoutUtility.GetRect(0.0f, 4000.0f, RowHeight, RowHeight, GUILayout.ExpandWidth(false));
            if (row.width < 24.0f) return;

            // ── 三个按钮的位置（右端）────────────────────────────────────────────
            const float button = 18.0f;
            const float gap = 2.0f;
            Rect removeRect = new Rect(row.xMax - button - 1.0f, row.y + 3.0f, button, button);
            Rect downRect = new Rect(removeRect.x - button - gap, removeRect.y, button, button);
            Rect upRect = new Rect(downRect.x - button - gap, downRect.y, button, button);
            Rect content = new Rect(row.x + 1.0f, row.y + 1.0f,
                Mathf.Max(0.0f, upRect.x - row.x - 4.0f), row.height - 2.0f);

            // ── 背景 + 曲线 ──────────────────────────────────────────────────────
            if (Event.current.type == EventType.Repaint)
            {
                Color accent = bad ? HoConstraintEditorTheme.ErrorColor : AccentForRow(output);
                bool isSelected = index == selected;

                EditorGUI.DrawRect(row, isSelected ? SelectedRowTint : RowTint);
                DrawRowCurveBackground(content, output, bad, isSelected, accent);
            }

            // ── 第一行：名字**左对齐**，右端接 ⚠ / 延迟 标记 ──────────────────────
            Rect nameRect = new Rect(content.x + 4.0f, content.y, content.width - 8.0f, RowHeaderHeight);
            if (Event.current.type == EventType.Repaint)
            {
                var nameStyle = new GUIStyle(index == selected ? HoConstraintEditorTheme.Bold : HoConstraintEditorTheme.Label)
                {
                    alignment = TextAnchor.MiddleLeft,
                    clipping = TextClipping.Clip
                };
                nameStyle.normal.textColor = bad
                    ? HoConstraintEditorTheme.ErrorColor
                    : (index == selected ? HoConstraintEditorTheme.TextBrightColor : HoConstraintEditorTheme.TextColor);
                GUI.Label(nameRect, new GUIContent(name, output.expression), nameStyle);

                if (bad || delayed)
                {
                    var noteStyle = new GUIStyle(HoConstraintEditorTheme.Caption) { alignment = TextAnchor.MiddleRight };
                    noteStyle.normal.textColor = bad ? HoConstraintEditorTheme.ErrorColor : HoConstraintEditorTheme.WarningColor;
                    string glyph = (bad ? "⚠" : "") + (delayed ? " 延迟" : "");
                    GUI.Label(nameRect, new GUIContent(glyph, bad ? "表达式解析不过：" + output.expression : "这行有延迟修饰符（未实现）。"), noteStyle);
                }
            }

            // ── 第二行：表达式**居中** ───────────────────────────────────────────
            Rect expressionRect = new Rect(content.x, nameRect.yMax, content.width, RowExpressionHeight);
            if (Event.current.type == EventType.Repaint)
            {
                var style = new GUIStyle(HoConstraintEditorTheme.Caption)
                {
                    alignment = TextAnchor.MiddleCenter,
                    clipping = TextClipping.Clip
                };
                string text = output.expression;
                string tip = output.expression;
                if (bad)
                {
                    style.normal.textColor = HoConstraintEditorTheme.ErrorColor;
                    text = "⚠ " + output.expression;
                    tip = "表达式解析不过。";
                }
                GUI.Label(expressionRect, new GUIContent(text, tip), style);
            }

            // ── 三个按钮 + 整行可点 ──────────────────────────────────────────────
            // 顺序很关键：先给按钮机会（命中就 `Event.Use()` 掉），被按钮吃掉的鼠标事件
            // 就不会走到下面那次"选中整行"。
            bool handled = false;
            if (DrawRowButton(upRect, "↑", "上移一行（往前生效）。", index > 0))
            {
                MoveOutput(index, index - 1);
                handled = true;
            }
            if (DrawRowButton(downRect, "↓", "下移一行（往后生效）。", index < middleware.outputs.Count - 1))
            {
                MoveOutput(index, index + 1);
                handled = true;
            }
            if (DrawRowButton(removeRect, "✕", "删掉这一行。", true))
            {
                middleware.outputs.RemoveAt(index);
                if (selected > index) selected--;
                else if (selected == index) selected = -1;
                dirty = true;
                handled = true;
            }

            if (!handled && Event.current.type == EventType.MouseDown && Event.current.button == 0
                && content.Contains(Event.current.mousePosition))
            {
                selected = index;
                GUI.FocusControl(null);
                Event.current.Use();
            }
        }

        /// <summary>行内按钮：显式矩形、自画、自判点击。返回 true = 这一趟被点了。</summary>
        private static bool DrawRowButton(Rect rect, string glyph, string tooltip, bool enabled)
        {
            Event evt = Event.current;
            bool hover = enabled && rect.Contains(evt.mousePosition);

            if (evt.type == EventType.Repaint)
            {
                HoConstraintEditorTheme.IconButton.Draw(rect, new GUIContent(glyph, tooltip),
                    false, hover, false, false);
                if (!enabled)
                {
                    // 禁用态：压一层暗色，形状还在但看得出不能点。
                    EditorGUI.DrawRect(rect, new Color(0.0f, 0.0f, 0.0f, 0.35f));
                }
            }

            if (!enabled) return false;
            if (evt.type == EventType.MouseDown && evt.button == 0 && hover)
            {
                evt.Use();
                return true;
            }
            return false;
        }

        /// <summary>一行的高度：第一行 19（名字 + 标记）+ 第二行 17（表达式）+ 上下留白。</summary>
        private const float RowHeight = 40.0f;
        private const float RowHeaderHeight = 19.0f;
        private const float RowExpressionHeight = 17.0f;

        /// <summary>行底色 / 选中底色。主题里没有"选中行"这一档，就近取强调色的低透明度。</summary>
        private static readonly Color RowTint = new Color(1.0f, 1.0f, 1.0f, 0.02f);
        private static readonly Color SelectedRowTint = new Color(0.290f, 0.608f, 1.000f, 0.22f);

        /// <summary>
        /// **整行背景直接画曲线**：一条横贯整行的曲线 + 一层极淡的底色。
        ///
        /// 为什么这么画：左边一列原来是"名字 + 一行小字"，看不出任何形状。而"这一行是恒等直线、
        /// 是宽范围、还是 0..1 默认"这件事，**形状差别一眼就看得出来**，比读数有用。
        /// 底色按语义分：**原始量绿**（那是"没被动过的源"）、**合成量蓝**。
        ///
        /// 用 `Handles.DrawAAPolyLine` 直接画折线 —— **不生成贴图、不缓存**。
        /// 这条窗口本来就是 IMGUI（`EditorGUILayout` + 自定义 `GUIStyle`），
        /// 曲线也该走同一条路：采样若干点、连成线，抗锯齿由 `Handles` 负责。
        /// （第一版我用 `Texture2D` + 签名缓存，那是"程序化贴图"的做法，
        ///  平白多了一整套缓存、销毁、泄漏要考虑，见 git 历史。）
        /// </summary>
        /// <param name="area">已经让开右侧按钮区的可用矩形（调用方算好）。</param>
        private void DrawRowCurveBackground(Rect area, HoFaceOutput output, bool bad, bool selected, Color accent)
        {
            if (area.width < 16.0f || area.height < 8.0f) return;

            // 极淡的底色：给"这一行属于哪一堆"一个氛围，不影响读字。
            // 选中的那行多压一点，让"整行可点"在视觉上说得通。
            Color tint = accent;
            tint.a = bad ? 0.18f : (selected ? 0.14f : 0.08f);
            EditorGUI.DrawRect(area, tint);

            AnimationCurve curve = output.curve;
            if (curve == null || curve.length == 0) return;

            Keyframe[] keys = curve.keys;
            float lo = keys[0].time;
            float hi = keys[keys.Length - 1].time;
            if (hi <= lo) return;

            float min = float.MaxValue, max = float.MinValue;
            foreach (Keyframe key in keys)
            {
                if (key.value < min) min = key.value;
                if (key.value > max) max = key.value;
            }

            // ⚠️ **不跳过平线**：恒等曲线（`0..1 → 0..1`）恰恰是大多数行，它画出来是一条对角线，
            // 正是"这行是直通"的形状。真正没信息的是**值域为零**（min == max）的曲线，那时没得画。
            if (max - min < 1e-4f) return;

            // 采样密度：整行一个点太糙、每像素一个点太费；约 3px 一个点对"看形状"足够。
            int samples = Mathf.Clamp(Mathf.RoundToInt(area.width / 3.0f), 8, 200);
            var points = new Vector3[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = lo + (hi - lo) * (i / (float)(samples - 1));
                float v = curve.Evaluate(t);
                // 上下各留 1px，免得线贴着行边缘看成一整块。
                float inner = Mathf.Max(1.0f, area.height - 2.0f);
                float y = area.yMax - 1.0f - (v - min) / (max - min) * inner;
                points[i] = new Vector3(area.x + area.width * (i / (float)(samples - 1)), y, 0.0f);
            }

            // 曲线是**背景**，所以墨色要淡到不跟文字抢；选中的那行略亮一点。
            Color line = accent;
            line.a = bad ? 0.55f : (selected ? 0.45f : 0.30f);
            Color previous = Handles.color;
            Handles.color = line;
            Handles.DrawAAPolyLine(1.5f, points);
            Handles.color = previous;
        }

        /// <summary>行颜色：**原始量绿**（没被动过的源）、**合成量蓝**，选自主题强调色。</summary>
        private static Color AccentForRow(HoFaceOutput output)
        {
            if (output == null) return HoConstraintEditorTheme.AccentOutput;
            bool raw = !string.IsNullOrEmpty(output.parameter) && output.parameter == output.expression;
            return raw ? HoConstraintEditorTheme.AccentOutput : HoConstraintEditorTheme.AccentDriver;
        }

        private void DrawStatusLine()
        {
            int rows = middleware != null && middleware.outputs != null ? middleware.outputs.Count : 0;
            int bad = 0;
            var duplicates = new HashSet<string>();
            if (middleware != null && middleware.outputs != null)
            {
                var seen = new HashSet<string>();
                for (int i = 0; i < middleware.outputs.Count; i++)
                {
                    HoFaceOutput output = middleware.outputs[i];
                    if (output == null) continue;
                    if (!HoFaceExpression.TryParse(output.expression, out _, out _)) bad++;
                    if (!string.IsNullOrEmpty(output.parameter) && !seen.Add(output.parameter)) duplicates.Add(output.parameter);
                }
            }

            using (HoConstraintEditorControls.Row(true))
            {
                string text = rows + " 行 · " + bad + " 行表达式有错 · " + duplicates.Count + " 个重复参数名";
                GUIStyle style = new GUIStyle(HoConstraintEditorTheme.Caption);
                if (bad > 0 || duplicates.Count > 0) style.normal.textColor = HoConstraintEditorTheme.ErrorColor;
                GUI.Label(HoConstraintEditorControls.NextAuto(text, style), text, style);

                HoConstraintEditorControls.Flex();
                if (!string.IsNullOrEmpty(message))
                {
                    GUIStyle messageStyle = new GUIStyle(HoConstraintEditorTheme.Caption) { alignment = TextAnchor.MiddleRight };
                    messageStyle.normal.textColor = messageIsError ? HoConstraintEditorTheme.ErrorColor : HoConstraintEditorTheme.TextDimColor;
                    GUI.Label(HoConstraintEditorControls.NextFlexible(60.0f), new GUIContent(message), messageStyle);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 右列：选中那一行
        // ══════════════════════════════════════════════════════════════

        private void DrawRightColumn(int index)
        {
            HoFaceOutput output = middleware.outputs[index];
            if (output == null)
            {
                return;
            }

            HoConstraintEditorControls.Title(
                string.IsNullOrEmpty(output.parameter) ? "(未命名)" : output.parameter,
                "第 " + (index + 1) + " / " + middleware.outputs.Count + " 行",
                new (string, bool)[] { ("表达式", !string.IsNullOrEmpty(output.expression)) });

            DrawRightScroll(output);
        }

        private Vector2 rightScroll;

        private void DrawRightScroll(HoFaceOutput output)
        {
            // `alwaysShowHorizontal: false` —— 右列的内容宽度都由 `NextFlexible` 自己让开，
            // 没理由需要横向滚动。
            rightScroll = EditorGUILayout.BeginScrollView(rightScroll, false, true);

            using (HoConstraintEditorControls.Row())
            {
                HoConstraintEditorControls.Label("参数名", HoConstraintEditorTheme.LabelWidth);
                // ⚠️ 最小宽度取小（60 而不是 120）：这一行是 `标签 + 输入框 + 说明` 三段，
                // 最小宽度就是三段的**最小和**。以前 120 + 130 让它在窄窗口下要 300px 才装得下，
                // 右列一到 300px 以下就长出**横向滚动条**。
                output.parameter = EditorGUI.TextField(
                    HoConstraintEditorControls.NextFlexible(60.0f),
                    output.parameter,
                    HoConstraintEditorTheme.Field);
                HoConstraintEditorControls.Flex();
                HoConstraintEditorControls.CaptionTrim("控制器里没有就跳过", 96.0f,
                    "写进控制器的参数；控制器里没有这个名字时这行被跳过（不猜也不补）。");
            }

            bool parsedOk = HoFaceExpression.TryParse(output.expression, out var parsed, out string expressionError);
            using (HoConstraintEditorControls.Row())
            {
                HoConstraintEditorControls.Label("表达式", HoConstraintEditorTheme.LabelWidth);
                output.expression = EditorGUI.TextField(
                    HoConstraintEditorControls.NextFlexible(60.0f),
                    output.expression,
                    parsedOk ? HoConstraintEditorTheme.Field : HoConstraintEditorTheme.FieldMissing);
            }

            if (!parsedOk)
            {
                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Label("", HoConstraintEditorTheme.LabelWidth);
                    GUIStyle errorStyle = new GUIStyle(HoConstraintEditorTheme.Caption);
                    errorStyle.normal.textColor = HoConstraintEditorTheme.ErrorColor;
                    GUI.Label(HoConstraintEditorControls.NextFlexible(40.0f), new GUIContent(expressionError), errorStyle);
                }
            }

            DrawVariableList(parsed);

            CurveField(output);

            HoConstraintEditorControls.Separator(4.0f, 2.0f);
            using (HoConstraintEditorControls.Row(true))
            {
                HoConstraintEditorControls.Label("修饰符", HoConstraintEditorTheme.LabelWidth);
                HoConstraintEditorControls.CaptionTrim("按列出顺序串在曲线后面", 200.0f,
                    "按列出顺序串在曲线后面。平滑 / 分档 / 延迟，从上到下依次作用。");
            }

            DrawModifiers(output);

            using (HoConstraintEditorControls.Row())
            {
                if (HoConstraintEditorControls.Button("+ 添加修饰符")) AddModifier(output);
            }

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// 把表达式里用到的变量逐个列出来 —— **只列，不判定**。
        ///
        /// 【为什么不检查 ARKit】这一层的映射是**自由**的：表达式读的是"上一层给的名字"，
        /// 它可以是规范名（`jawOpen`）、可以是线名（`Rotation_x`）、也可以是一个**上一行算出来的**名字。
        /// 判定"它是不是标准 ARKit 键"在**任何一行上都是错的**：
        ///   · 输入行上，规范名本来就不等于设备线名（`browDown_L` 那种 `_L/_R` 别名根本不在 ARKit 52 里）；
        ///   · 输出行上，变量允许是原始线名（头/眼那 15 个就都不是通道名）。
        /// 而且"这个变量到底有没有来源"这件事**只有跑起来才知道**（缺键的行会冻结，不是报错）。
        /// 所以这里只做**枚举**：作者用眼睛核，机器不猜。
        /// </summary>
        private void DrawVariableList(HoFaceExpression parsed)
        {
            if (parsed == null)
            {
                return;
            }

            var variables = new List<string>();
            parsed.CollectVariables(variables);

            using (HoConstraintEditorControls.Row(true))
            {
                HoConstraintEditorControls.Label("变量", HoConstraintEditorTheme.LabelWidth, "表达式里的变量 = 上一层给的名字（规范名 / 线名 / 上一行的结果）。");
                HoConstraintEditorControls.Caption(variables.Count == 0 ? "（表达式里没有变量）" : variables.Count + " 个");
            }

            for (int i = 0; i < variables.Count; i++)
            {
                string name = variables[i];
                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Label("", HoConstraintEditorTheme.LabelWidth);
                    GUI.Label(HoConstraintEditorControls.NextAuto(name, HoConstraintEditorTheme.Value),
                        new GUIContent(name), HoConstraintEditorTheme.Value);
                }
            }
        }

        /// <summary>曲线整宽一条 + 底下一条关键点读数（只读，想改值就拖曲线）。</summary>
        private void CurveField(HoFaceOutput output)
        {
            using (HoConstraintEditorControls.Row())
            {
                HoConstraintEditorControls.Label("曲线", HoConstraintEditorTheme.LabelWidth, "横轴 = 表达式值，纵轴 = 写出去的值。");
                Rect rect = GUILayoutUtility.GetRect(120.0f, 4000.0f, CurveHeight, CurveHeight, GUILayout.ExpandWidth(true));
                output.curve = EditorGUI.CurveField(rect, output.curve);
            }

            using (HoConstraintEditorControls.Row(true))
            {
                HoConstraintEditorControls.Label("", HoConstraintEditorTheme.LabelWidth);
                HoConstraintEditorControls.CaptionTrim("横轴 = 表达式值，纵轴 = 写出的值；范围外按端点算", 320.0f,
                    "横轴 = 表达式的值，纵轴 = 写出去的值；关键点范围之外按端点算（不外推）。");
            }

            using (HoConstraintEditorControls.Row(true))
            {
                HoConstraintEditorControls.Label("", HoConstraintEditorTheme.LabelWidth);
                Rect strip = HoConstraintEditorControls.NextFlexible(80.0f);
                string summary = CurveSummary(output.curve);
                EditorGUI.LabelField(strip, summary, HoConstraintEditorTheme.Caption);
                if (Event.current.type == EventType.Repaint && !string.IsNullOrEmpty(summary))
                {
                    EditorGUI.DrawRect(new Rect(strip.x, strip.yMax - 1.0f, strip.width, 1.0f), HoConstraintEditorTheme.SeparatorColor);
                }
            }
        }

        private static string CurveSummary(AnimationCurve curve)
        {
            if (curve == null || curve.length == 0)
            {
                return "（空曲线：输出 0）";
            }

            Keyframe first = curve.keys[0];
            Keyframe last = curve.keys[curve.length - 1];
            return curve.length + " 个关键点 · x " + first.time.ToString("0.###") + "→" + last.time.ToString("0.###")
                + " · y " + first.value.ToString("0.###") + "→" + last.value.ToString("0.###");
        }

        // ══════════════════════════════════════════════════════════════
        // 修饰符
        // ══════════════════════════════════════════════════════════════

        private void DrawModifiers(HoFaceOutput output)
        {
            if (output.modifiers == null)
            {
                output.modifiers = new List<HoFaceModifier>();
            }

            for (int i = 0; i < output.modifiers.Count; i++)
            {
                HoFaceModifier modifier = output.modifiers[i];
                if (modifier == null)
                {
                    continue;
                }

                if (modifier.steps == null)
                {
                    modifier.steps = new List<HoFaceStep>();
                }

                using (HoConstraintEditorControls.Card(i % 2 == 1))
                {
                    using (HoConstraintEditorControls.Row())
                    {
                        HoConstraintEditorControls.Label((i + 1) + ".", HoConstraintEditorTheme.LabelWidthXs);
                        int kind = HoConstraintEditorControls.EnumControl(
                            HoConstraintEditorControls.Next(150.0f),
                            (int)modifier.kind,
                            ModifierKindNames,
                            false,
                            "平滑 / 延迟 / 分档。按从上到下的顺序生效。");
                        modifier.kind = (HoFaceModifierKind)kind;

                        if (!modifier.Active)
                        {
                            HoConstraintEditorControls.CaptionTrim("（不起作用）", 80.0f,
                                "这个修饰符现在不起作用：平滑/延迟的时长是 0，或分档里一步都没有。");
                        }

                        HoConstraintEditorControls.Flex();
                        using (new EditorGUI.DisabledScope(i == 0))
                        {
                            if (HoConstraintEditorControls.IconButton("↑", "上移（提前生效）。")) MoveModifier(output, i, i - 1);
                        }

                        using (new EditorGUI.DisabledScope(i == output.modifiers.Count - 1))
                        {
                            if (HoConstraintEditorControls.IconButton("↓", "下移（推后生效）。")) MoveModifier(output, i, i + 1);
                        }

                        if (HoConstraintEditorControls.IconButton("✕", "删掉这个修饰符。"))
                        {
                            output.modifiers.RemoveAt(i);
                            dirty = true;
                            break;
                        }
                    }

                    if (modifier.kind == HoFaceModifierKind.Steps)
                    {
                        DrawSteps(modifier);
                    }
                    else
                    {
                        using (HoConstraintEditorControls.Row())
                        {
                            HoConstraintEditorControls.Label("时长", HoConstraintEditorTheme.LabelWidthSm, "秒。");
                            modifier.seconds = HoConstraintEditorControls.NumberField(modifier.seconds, "s", "平滑时间常数 / 延迟时长。");
                        }

                        if (modifier.kind == HoFaceModifierKind.Delay)
                        {
                            using (HoConstraintEditorControls.Row(true))
                            {
                                HoConstraintEditorControls.Label("", HoConstraintEditorTheme.LabelWidthSm);
                                GUIStyle style = new GUIStyle(HoConstraintEditorTheme.Caption);
                                style.normal.textColor = HoConstraintEditorTheme.WarningColor;
                                GUI.Label(HoConstraintEditorControls.NextAuto("还没实现，装配时会跳过", style), new GUIContent("还没实现，装配时会跳过"), style);
                            }
                        }
                    }
                }
            }
        }

        private void DrawSteps(HoFaceModifier modifier)
        {
            using (HoConstraintEditorControls.Row(true))
            {
                HoConstraintEditorControls.Label("分档", HoConstraintEditorTheme.LabelWidthSm, "参数过触发值就跳到目标值；掉回阈值以下再退回去。");
                HoConstraintEditorControls.Caption("按触发值从小到大排列", "顺序乱了也能用，但按顺序读更好核对。");
            }

            for (int s = 0; s < modifier.steps.Count; s++)
            {
                HoFaceStep step = modifier.steps[s];
                if (step == null)
                {
                    continue;
                }

                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("触发", HoConstraintEditorTheme.LabelWidthSm);
                    step.trigger = HoConstraintEditorControls.NumberField(step.trigger);
                    HoConstraintEditorControls.Label("目标", HoConstraintEditorTheme.LabelWidthSm);
                    step.target = HoConstraintEditorControls.NumberField(step.target);
                    HoConstraintEditorControls.Label("保持", HoConstraintEditorTheme.LabelWidthSm, "触发后的最短保持时间（秒）。");
                    step.hold = HoConstraintEditorControls.NumberField(step.hold);
                    HoConstraintEditorControls.Label("回退", HoConstraintEditorTheme.LabelWidthSm, "要从触发点往下掉这么多才退回去（迟滞）。");
                    step.threshold = HoConstraintEditorControls.NumberField(step.threshold);

                    HoConstraintEditorControls.Flex();
                    if (HoConstraintEditorControls.IconButton("✕", "删掉这一档。"))
                    {
                        modifier.steps.RemoveAt(s);
                        dirty = true;
                        break;
                    }
                }
            }

            using (HoConstraintEditorControls.Row())
            {
                HoConstraintEditorControls.Label("", HoConstraintEditorTheme.LabelWidthSm);
                if (HoConstraintEditorControls.Button("+ 添加一档"))
                {
                    modifier.steps.Add(new HoFaceStep());
                    dirty = true;
                }
            }
        }

        private static readonly string[] ModifierKindNames = { "平滑", "延迟（未实现）", "分档" };

        // ══════════════════════════════════════════════════════════════
        // 数据操作
        // ══════════════════════════════════════════════════════════════

        private bool Matches(HoFaceOutput output, string needle)
        {
            if (output == null || string.IsNullOrEmpty(needle))
            {
                return true;
            }

            return !string.IsNullOrEmpty(output.parameter)
                && output.parameter.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasDelay(HoFaceOutput output)
        {
            if (output.modifiers == null)
            {
                return false;
            }

            for (int i = 0; i < output.modifiers.Count; i++)
            {
                if (output.modifiers[i] != null && output.modifiers[i].kind == HoFaceModifierKind.Delay)
                {
                    return true;
                }
            }

            return false;
        }

        private void AddOutput()
        {
            EnsureMiddleware();
            middleware.outputs.Add(new HoFaceOutput { parameter = "", expression = "" });
            selected = middleware.outputs.Count - 1;
            dirty = true;
        }

        private static void AddModifier(HoFaceOutput output)
        {
            if (output.modifiers == null)
            {
                output.modifiers = new List<HoFaceModifier>();
            }

            output.modifiers.Add(new HoFaceModifier { kind = HoFaceModifierKind.Smooth, seconds = 0.03f });
        }

        private void MoveOutput(int from, int to)
        {
            if (to < 0 || to >= middleware.outputs.Count)
            {
                return;
            }

            HoFaceOutput moved = middleware.outputs[from];
            middleware.outputs.RemoveAt(from);
            middleware.outputs.Insert(to, moved);
            selected = to;
            dirty = true;
        }

        private static void MoveModifier(HoFaceOutput output, int from, int to)
        {
            if (to < 0 || to >= output.modifiers.Count)
            {
                return;
            }

            HoFaceModifier moved = output.modifiers[from];
            output.modifiers.RemoveAt(from);
            output.modifiers.Insert(to, moved);
        }

        private void EnsureMiddleware()
        {
            if (middleware == null)
            {
                middleware = HoFaceMiddlewareDefaults.Create();
            }

            if (middleware.outputs == null)
            {
                middleware.outputs = new List<HoFaceOutput>();
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 新建 / 保存 / 重载
        // ══════════════════════════════════════════════════════════════

        private void NewProfile()
        {
            string path = EditorUtility.SaveFilePanelInProject("新建面捕配置", NewProfileName, "hoface.json", "新建一份中间层配置（内容 = 内置默认表）。");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            WriteText(path, HoFaceProfile.WriteDefaults());
            AssetDatabase.ImportAsset(path);

            // 直接把新文件接上来，免得"建好了还要再选一次"
            profile = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            ReloadProfile(true);
        }

        private void SaveProfile()
        {
            if (profile == null)
            {
                SetMessage("没有选中配置文件。", true);
                return;
            }

            EnsureMiddleware();
            string path = AssetDatabase.GetAssetPath(profile);
            if (string.IsNullOrEmpty(path))
            {
                SetMessage("这份配置在工程里没有路径（不是磁盘上的资产？）。", true);
                return;
            }

            WriteText(path, HoFaceProfile.Write(middleware));
            AssetDatabase.ImportAsset(path);
            dirty = false;

            // 设置对象按"路径 + 文件写盘时间戳"缓存解析结果 —— 存完盘时间戳一定变，
            // 但为了让面板立刻看到新内容（而不是等下一次 tick），这里主动敲一下。
            EditorApplication.delayCall += () =>
            {
                HoFaceDebugHost.Settings.ReloadProfile();
                HoFaceDebugHost.Save();
            };

            SetMessage("已保存 " + path, false);
        }

        private void ReloadProfile(bool silent)
        {
            if (profile == null)
            {
                middleware = null;
                selected = -1;
                dirty = false;
                if (!silent) SetMessage("没有选中配置文件。", true);
                return;
            }

            if (HoFaceProfile.TryParse(profile.text, out var parsed, out string error))
            {
                middleware = parsed;
                selected = -1;
                dirty = false;
                if (!silent) SetMessage("已重新载入 " + AssetDatabase.GetAssetPath(profile), false);
            }
            else
            {
                middleware = null;
                selected = -1;
                dirty = false;
                SetMessage("读不了这份配置：" + error, true);
            }
        }

        private static void WriteText(string path, string text)
        {
            System.IO.File.WriteAllText(path, text, new System.Text.UTF8Encoding(false));
        }

        private void SetMessage(string text, bool isError)
        {
            message = text;
            messageIsError = isError;
            Repaint();
        }
    }
}
