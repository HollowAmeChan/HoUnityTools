using System.Collections.Generic;
using Hollow.HoUnityTools.Editor.Constraints;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>
    /// **面捕中间层配置面板**：左边是输出行目录，右边是选中那一行的全部细节。
    ///
    /// 它编辑的数据 = 运行时那份 <see cref="HoFaceMiddleware"/>（`.hoface.json` 文本资产）。
    /// 面板上的顺序就是生效顺序 —— 左边列表的顺序 = 写参数的顺序，修饰符列表的顺序 = 串起来的顺序。
    ///
    /// 三处刻意做成"一眼能看出坏在哪"：表达式解析不过 / 变量不是 ARKit 键 / 用了还没实现的延迟。
    /// 保存后会把场景里的 <see cref="HoFaceTrackingDebugger"/> 的解析缓存打掉（见 <see cref="SaveProfile"/>）。
    /// </summary>
    public sealed class HoFaceProfileWindow : EditorWindow
    {
        private const float LeftWidth = 300.0f;
        private const float CurveHeight = 90.0f;
        private const string NewProfileName = "ho-2d-test1.hoface.json";

        private TextAsset profile;
        private HoFaceMiddleware middleware;
        private int selected = -1;
        private string search = "";
        private string message;
        private bool messageIsError;
        private Vector2 listScroll;
        private bool dirty;

        [MenuItem("HoUnityTools/面捕/面捕配置")]
        private static void Open()
        {
            GetWindow<HoFaceProfileWindow>(false, "面捕配置", true);
        }

        /// <summary>从组件面板跳进来：直接盯着那份配置，不预选任何一行。</summary>
        public static void Open(TextAsset profile)
        {
            HoFaceProfileWindow window = GetWindow<HoFaceProfileWindow>(false, "面捕配置", true);
            window.profile = profile;
            window.selected = -1;
            window.ReloadProfile(true);
            window.Repaint();
        }

        private void OnEnable()
        {
            minSize = new Vector2(720.0f, 420.0f);
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
            using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(LeftWidth), GUILayout.ExpandHeight(true)))
                {
                    DrawLeftColumn();
                }

                HoConstraintEditorControls.Separator(0.0f, 0.0f);

                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
                {
                    if (middleware == null)
                    {
                        HoConstraintEditorControls.Caption("（还没载入配置）", "上面选一份 .hoface.json 文本资产，或点「新建配置…」。");
                    }
                    else if (selected < 0 || selected >= middleware.outputs.Count)
                    {
                        HoConstraintEditorControls.Caption("（左边选一行）", "点左边的参数名看它的表达式、曲线与修饰符。");
                    }
                    else
                    {
                        DrawRightColumn(selected);
                    }
                }
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 顶栏
        // ══════════════════════════════════════════════════════════════

        private void DrawTopBar()
        {
            using (HoConstraintEditorControls.Card())
            {
                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("配置", HoConstraintEditorTheme.LabelWidthSm, "要被编辑的那份 .hoface.json 文本资产。");
                    Rect rect = HoConstraintEditorControls.NextFlexible(140.0f);
                    EditorGUI.BeginChangeCheck();
                    var picked = (TextAsset)EditorGUI.ObjectField(rect, profile, typeof(TextAsset), false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        profile = picked;
                        ReloadProfile(false);
                    }

                    HoConstraintEditorControls.Gap();
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
                    string path = profile == null ? "（未选配置 · 用内置默认）" : AssetDatabase.GetAssetPath(profile);
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
                    search = EditorGUI.TextField(HoConstraintEditorControls.NextFlexible(60.0f), search, HoConstraintEditorTheme.Field);
                }

                using (HoConstraintEditorControls.Row())
                {
                    if (HoConstraintEditorControls.Button("+ 新增输出", "在列表末尾加一行。", false, 90.0f))
                    {
                        AddOutput();
                    }

                    HoConstraintEditorControls.Flex();
                    HoConstraintEditorControls.Caption("顺序 = 写参数顺序", "上面的行先写；重复写同一个参数时后面的覆盖前面的。");
                }

                HoConstraintEditorControls.Separator(3.0f, 3.0f);

                // 只有滚动区吃掉余下的高度，状态行才不会被长列表推出窗口
                if (middleware != null && middleware.outputs != null)
                {
                    using (new EditorGUILayout.VerticalScope(GUILayout.ExpandHeight(true)))
                    {
                        listScroll = EditorGUILayout.BeginScrollView(listScroll);
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

        private void DrawLeftRow(int index)
        {
            HoFaceOutput output = middleware.outputs[index];
            if (output == null)
            {
                return;
            }

            bool bad = !HoFaceExpression.TryParse(output.expression, out var parsed, out _);
            bool delayed = HasDelay(output);
            string name = string.IsNullOrEmpty(output.parameter) ? "(未命名)" : output.parameter;

            using (HoConstraintEditorControls.Card(index == selected))
            {
                using (HoConstraintEditorControls.Row())
                {
                    // 整条名字都是点选区：自己画按钮 + 自己判点击（选中态由卡片底色给出）
                    Rect nameRect = HoConstraintEditorControls.NextFlexible(60.0f);
                    GUIStyle nameStyle = index == selected ? HoConstraintEditorTheme.ButtonPrimary : HoConstraintEditorTheme.Button;
                    Event evt = Event.current;
                    bool hover = nameRect.Contains(evt.mousePosition);
                    if (evt.type == EventType.Repaint)
                    {
                        nameStyle.Draw(nameRect, new GUIContent(name, output.expression), false, hover, false, false);
                    }

                    if (evt.type == EventType.MouseDown && evt.button == 0 && hover)
                    {
                        selected = index;
                        GUI.FocusControl(null);
                        evt.Use();
                    }

                    // ⚠ / 延迟 画在名字按钮上面（同一 IMGUI 趟里后画的在上）
                    if (bad || delayed)
                    {
                        Rect note = new Rect(nameRect.xMax - 74.0f, nameRect.y, 70.0f, nameRect.height);
                        GUIStyle noteStyle = new GUIStyle(HoConstraintEditorTheme.Caption) { alignment = TextAnchor.MiddleRight };
                        noteStyle.normal.textColor = bad ? HoConstraintEditorTheme.ErrorColor : HoConstraintEditorTheme.WarningColor;
                        string glyph = (bad ? "⚠" : "") + (delayed ? " 延迟" : "");
                        GUI.Label(note, new GUIContent(glyph, bad ? "表达式解析不过：" + output.expression : "这行有延迟修饰符（未实现）。"), noteStyle);
                    }

                    HoConstraintEditorControls.Gap(2.0f);
                    using (new EditorGUI.DisabledScope(index == 0))
                    {
                        if (HoConstraintEditorControls.IconButton("↑", "上移一行（往前生效）。")) MoveOutput(index, index - 1);
                    }

                    using (new EditorGUI.DisabledScope(index == middleware.outputs.Count - 1))
                    {
                        if (HoConstraintEditorControls.IconButton("↓", "下移一行（往后生效）。")) MoveOutput(index, index + 1);
                    }

                    if (HoConstraintEditorControls.IconButton("✕", "删掉这一行。"))
                    {
                        middleware.outputs.RemoveAt(index);
                        if (selected > index) selected--;
                        else if (selected == index) selected = -1;
                        dirty = true;
                    }
                }

                // 变量个数只是提示，不参与布局
                if (!bad)
                {
                    var variables = new List<string>();
                    parsed.CollectVariables(variables);
                    using (HoConstraintEditorControls.Row(true))
                    {
                        HoConstraintEditorControls.Caption("曲线(" + variables.Count + " 个变量)", "变量来自表达式；右边会逐个列出并标出不是 ARKit 键的。");
                    }
                }
            }
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

            HoConstraintEditorControls.Separator(3.0f, 4.0f);
            using (HoConstraintEditorControls.Row())
            {
                using (new EditorGUI.DisabledScope(index == 0))
                {
                    if (HoConstraintEditorControls.Button("↑ 上移")) MoveOutput(index, index - 1);
                }

                HoConstraintEditorControls.Gap();
                using (new EditorGUI.DisabledScope(index == middleware.outputs.Count - 1))
                {
                    if (HoConstraintEditorControls.Button("↓ 下移")) MoveOutput(index, index + 1);
                }

                HoConstraintEditorControls.Flex();
                if (HoConstraintEditorControls.Button("✕ 删除这一行", null, false))
                {
                    middleware.outputs.RemoveAt(index);
                    selected = -1;
                    dirty = true;
                }
            }
        }

        private Vector2 rightScroll;

        private void DrawRightScroll(HoFaceOutput output)
        {
            rightScroll = EditorGUILayout.BeginScrollView(rightScroll);

            using (HoConstraintEditorControls.Row())
            {
                HoConstraintEditorControls.Label("参数名", HoConstraintEditorTheme.LabelWidth);
                output.parameter = EditorGUI.TextField(
                    HoConstraintEditorControls.NextFlexible(120.0f),
                    output.parameter,
                    HoConstraintEditorTheme.Field);
                HoConstraintEditorControls.Flex();
                HoConstraintEditorControls.Caption("写进控制器的参数；控制器里没有这个名字时这行被跳过。");
            }

            bool parsedOk = HoFaceExpression.TryParse(output.expression, out var parsed, out string expressionError);
            using (HoConstraintEditorControls.Row())
            {
                HoConstraintEditorControls.Label("表达式", HoConstraintEditorTheme.LabelWidth);
                output.expression = EditorGUI.TextField(
                    HoConstraintEditorControls.NextFlexible(120.0f),
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
                    GUI.Label(HoConstraintEditorControls.NextFlexible(80.0f), new GUIContent(expressionError), errorStyle);
                }
            }

            DrawVariableList(parsed);

            CurveField(output);

            HoConstraintEditorControls.Separator(4.0f, 2.0f);
            using (HoConstraintEditorControls.Row(true))
            {
                HoConstraintEditorControls.Label("修饰符", HoConstraintEditorTheme.LabelWidth);
                HoConstraintEditorControls.Caption("按列出顺序串在曲线后面。", "平滑 / 分档 / 延迟，从上到下依次作用。");
            }

            DrawModifiers(output);

            using (HoConstraintEditorControls.Row())
            {
                if (HoConstraintEditorControls.Button("+ 添加修饰符")) AddModifier(output);
            }

            EditorGUILayout.EndScrollView();
        }

        /// <summary>表达式用到的变量逐个列出；不是 ARKit 键的点名（只是提醒，不拦保存）。</summary>
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
                HoConstraintEditorControls.Label("变量", HoConstraintEditorTheme.LabelWidth, "表达式里的变量 = 源形态键名。");
                HoConstraintEditorControls.Caption(variables.Count == 0 ? "（表达式里没有变量）" : variables.Count + " 个");
            }

            for (int i = 0; i < variables.Count; i++)
            {
                string name = variables[i];
                bool known = HoFaceTrackingChannels.IndexOf(name) >= 0;
                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Label("", HoConstraintEditorTheme.LabelWidth);
                    GUIStyle style = new GUIStyle(known ? HoConstraintEditorTheme.Value : HoConstraintEditorTheme.Caption);
                    if (!known) style.normal.textColor = HoConstraintEditorTheme.WarningColor;
                    GUI.Label(HoConstraintEditorControls.NextAuto(name + (known ? "" : "  · 不是标准 ARKit 键"), style), new GUIContent(name + (known ? "" : "  · 不是标准 ARKit 键")), style);
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
                HoConstraintEditorControls.Caption("横轴 = 表达式的值，纵轴 = 写出去的值；关键点范围之外按端点算（不外推）。");
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
                            HoConstraintEditorControls.Caption("（这个修饰符现在不起作用）", "平滑/延迟的时长是 0，或分档里一步都没有。");
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

            // 组件按"资产实例 + 文本长度"缓存解析结果，长度没变时会继续用旧的那份 —— 必须敲一下
            TextAsset saved = profile;
            EditorApplication.delayCall += () =>
            {
                foreach (HoFaceTrackingDebugger rig in Object.FindObjectsByType<HoFaceTrackingDebugger>(FindObjectsSortMode.None))
                {
                    if (rig != null && rig.profile == saved)
                    {
                        rig.ReloadProfile();
                    }
                }
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
