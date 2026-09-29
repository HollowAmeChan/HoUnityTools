// HoPlaySpeedSettingsWindow.cs -- 播放速度面板的设置窗口
//
// 这里改的全是**面板自己的样子与规则**：速度点那排按钮、布局、外部改动怎么警告、锁定与自动复位。
// 「现在速度是多少」不在这里改（那是速度条上的活），但这里会显示它。
//
// 排版一律走约束面板那套设计令牌与控件（`HoConstraintEditorTheme` / `HoConstraintEditorControls`）：
// 本仓的规矩是"新增面板不许各写各的颜色和宽度"。别在这个文件里出现 `EditorGUILayout.FloatField` 那种
// 按剩余宽度百分比分配的控件 —— 后果就是同一列在不同行宽度不同（见 docs/完善的功能/EDITOR_UI_SYSTEM.md）。
//
// ⚠️ 枚举控件只能用 `EnumControl(..., segmented: true)` 或 `Segmented`：`Dropdown` 走的是异步菜单，
// **拿不到用户的选择**（它永远返回入参）。这条踩过，见 HoConstraintEditorControls.Dropdown 的注释。
using UnityEditor;
using UnityEngine;
using Hollow.HoUnityTools.Editor.Constraints;

namespace Hollow.HoUnityTools.Editor.PlaySpeed
{
    /// <summary>播放速度面板的设置。菜单：<c>HoUnityTools/播放速度设置</c>。</summary>
    internal sealed class HoPlaySpeedSettingsWindow : EditorWindow
    {
        private static readonly string[] OrderOptions = { "正常", "反转" };
        private static readonly string[] ButtonWidthOptions = { "等宽", "自适应" };
        private static readonly string[] FormatOptions = { "原样", "短", "紧凑" };
        private static readonly string[] WarningOptions = { "从不", "总是" };

        private const string ResetAllLabel = "恢复全部默认";
        private const string ResetAllTooltip = "速度点、布局、警告、锁定、自动复位全部回到出厂值（会先问你一次）。";

        private const string SortLabel = "按值排序";
        private const string SortTooltip = "把速度点从小到大排好。编辑数值时不会自动排 —— 那样行会在你手底下跳。";

        private const string ResetPresetsLabel = "恢复默认速度点";
        private const string ResetPresetsTooltip = "换回出厂那七颗：暂停 / ⅛ / ¼ / ½ / 常速 / 1.5 / 2（其余设置不动）。";

        private const string AddPresetLabel = "＋ 添加速度点";
        private const string AddPresetTooltip = "在列表末尾加一个（值 1×）。快速连点几下就加几个，之后逐个改值。";

        private const string TestWarningLabel = "测试闪烁";
        private const string TestWarningTooltip = "让速度条现在闪一次警告（只是看效果，不代表真的有人在改速度）。";

        private Vector2 scroll;
        private bool presetsExpanded = true;
        private bool panelExpanded = true;
        private bool warningExpanded = true;
        private bool behaviorExpanded = true;

        /// <summary>
        /// 开设置窗口。**故意没有菜单项**（2026-09-30 用户定：菜单里只留 <c>HoUnityTools/播放速度</c> 一个入口，
        /// 设置从速度条上的齿轮按钮 / 右键菜单进）—— 别顺手给它加 <c>[MenuItem]</c>。
        /// </summary>
        internal static void Open()
        {
            GetWindow<HoPlaySpeedSettingsWindow>(false, "播放速度设置", true);
        }
        private void OnEnable()
        {
            titleContent = new GUIContent("播放速度设置", "播放速度面板的设置：速度点、布局、警告。");
        }

        private void OnDisable()
        {
            HoPlaySpeedSettings.Save();
        }

        /// <summary>播放中要重画：标题行显示的是实时的 Time.timeScale。</summary>
        private void OnInspectorUpdate()
        {
            if (EditorApplication.isPlaying)
            {
                Repaint();
            }
        }

        private void OnGUI()
        {
            HoPlaySpeedSettingsData data = HoPlaySpeedSettings.Data;

            using (EditorGUILayout.ScrollViewScope scrollView = new EditorGUILayout.ScrollViewScope(scroll))
            {
                scroll = scrollView.scrollPosition;

                using (HoConstraintEditorControls.Card())
                {
                    DrawTitle(data);
                }

                DrawPresetsSection(data);
                DrawPanelSection(data);
                DrawWarningSection(data);
                DrawBehaviorSection(data);
                DrawFooter(data);
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 标题
        // ══════════════════════════════════════════════════════════════

        private void DrawTitle(HoPlaySpeedSettingsData data)
        {
            string state = EditorApplication.isPlaying
                ? "播放中 · 现在 " + HoPlaySpeedFormatting.Format(Time.timeScale, data.format)
                : "编辑模式（按 Play 才生效）";

            HoConstraintEditorControls.Title(
                "播放速度",
                state,
                ("跟随外部", true),
                ("自动复位", data.resetOnPlayEnd));
        }

        // ══════════════════════════════════════════════════════════════
        // 速度点
        // ══════════════════════════════════════════════════════════════

        private void DrawPresetsSection(HoPlaySpeedSettingsData data)
        {
            string summary = data.presets.Count + " 个 · 格式 " + FormatOptions[(int)data.format];
            HoConstraintEditorControls.Section(ref presetsExpanded, "速度点", summary, HoConstraintEditorTheme.AccentDriver);
            if (!presetsExpanded)
            {
                return;
            }

            using (HoConstraintEditorControls.Card())
            {
                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("值", HoConstraintEditorTheme.LabelWidthXs, "速度值（Time.timeScale 的倍数）");
                    HoConstraintEditorControls.Caption("按钮文字（留空 = 按格式自动生成）");
                }

                for (int i = 0; i < data.presets.Count; i++)
                {
                    DrawPresetRow(data, i);
                }

                HoConstraintEditorControls.Separator();

                using (HoConstraintEditorControls.Row())
                {
                    if (HoConstraintEditorControls.Button(AddPresetLabel, AddPresetTooltip))
                    {
                        data.presets.Add(new HoPlaySpeedPreset(1f));
                        HoPlaySpeedSettings.NotifyChanged();
                    }

                    HoConstraintEditorControls.Gap();

                    if (HoConstraintEditorControls.Button(SortLabel, SortTooltip))
                    {
                        data.SortPresets();
                        HoPlaySpeedSettings.NotifyChanged();
                    }

                    HoConstraintEditorControls.Gap();

                    if (HoConstraintEditorControls.Button(ResetPresetsLabel, ResetPresetsTooltip))
                    {
                        data.presets = HoPlaySpeedPresets.CreateDefault();
                        HoPlaySpeedSettings.NotifyChanged();
                    }

                    HoConstraintEditorControls.Flex();
                }

                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Caption(
                        "0× 与 1× 是保底速度点（暂停与常速）：删掉之后下次打开会自己补回来，值 0 与 1 也补。",
                        "面板上的「固定」标记只影响能不能删；速度点列表在读取时一定会补齐 0× 与 1×。");
                }
            }
        }

        private void DrawPresetRow(HoPlaySpeedSettingsData data, int index)
        {
            HoPlaySpeedPreset preset = data.presets[index];

            using (HoConstraintEditorControls.Row())
            {
                HoConstraintEditorControls.Label(index.ToString(), HoConstraintEditorTheme.LabelWidthXs, "第几个（面板上从左到右 / 从上到下按这个顺序）");

                float edited = HoConstraintEditorControls.NumberField(
                    preset.value,
                    null,
                    "速度值（Time.timeScale 的倍数）：0 = 暂停",
                    HoConstraintEditorTheme.FieldWidth);
                edited = Mathf.Clamp(edited, 0f, HoPlaySpeedSettings.MaxSpeed);
                if (!Mathf.Approximately(edited, preset.value))
                {
                    preset.value = edited;
                    HoPlaySpeedSettings.NotifyChanged();
                }

                HoConstraintEditorControls.Gap(2f);

                Rect nameRect = HoConstraintEditorControls.NextFlexible(56f);
                string editedLabel = EditorGUI.TextField(nameRect, preset.label ?? string.Empty, HoConstraintEditorTheme.Field);
                GUI.Label(nameRect, new GUIContent(string.Empty, "按钮文字，留空则按「按钮格式」自动生成"));
                if (editedLabel != preset.label)
                {
                    preset.label = editedLabel ?? string.Empty;
                    HoPlaySpeedSettings.NotifyChanged();
                }

                HoConstraintEditorControls.Gap(2f);
                HoConstraintEditorControls.CaptionTrim("→ " + preset.Display(data.format), 76f, "按钮上最终显示成什么");

                bool pinned = HoConstraintEditorControls.Toggle("固定", preset.pinned, "固定点：不能被 ✕ 删掉", 50f);
                if (pinned != preset.pinned)
                {
                    preset.pinned = pinned;
                    HoPlaySpeedSettings.NotifyChanged();
                }

                if (HoConstraintEditorControls.IconButton("▲", "上移（面板上靠前）"))
                {
                    MovePreset(data, index, -1);
                }

                if (HoConstraintEditorControls.IconButton("▼", "下移（面板上靠后）"))
                {
                    MovePreset(data, index, 1);
                }

                if (preset.pinned)
                {
                    HoConstraintEditorControls.Caption("固定", "固定点删不掉：先把它上面的「固定」关掉。");
                }
                else if (HoConstraintEditorControls.IconButton("✕", "删掉这个速度点"))
                {
                    data.presets.RemoveAt(index);
                    HoPlaySpeedSettings.NotifyChanged();
                }

                HoConstraintEditorControls.Flex();
            }
        }

        private static void MovePreset(HoPlaySpeedSettingsData data, int index, int delta)
        {
            int target = index + delta;
            if (target < 0 || target >= data.presets.Count)
            {
                return;
            }

            HoPlaySpeedPreset swap = data.presets[target];
            data.presets[target] = data.presets[index];
            data.presets[index] = swap;
            HoPlaySpeedSettings.NotifyChanged();
        }

        // ══════════════════════════════════════════════════════════════
        // 面板
        // ══════════════════════════════════════════════════════════════

        private void DrawPanelSection(HoPlaySpeedSettingsData data)
        {
            string summary = "一行 · " + ButtonWidthOptions[(int)data.buttonWidth];
            HoConstraintEditorControls.Section(ref panelExpanded, "面板", summary, HoConstraintEditorTheme.AccentMesh);
            if (!panelExpanded)
            {
                return;
            }

            using (HoConstraintEditorControls.Card())
            {
                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("复位按钮", HoConstraintEditorTheme.LabelWidth, "速度条上要不要那颗「复位」");
                    bool show = HoConstraintEditorControls.Toggle("显示", data.showResetButton, "复位 = 回到本次播放开始时的速度");
                    if (show != data.showResetButton)
                    {
                        data.showResetButton = show;
                        HoPlaySpeedSettings.NotifyChanged();
                    }

                    HoConstraintEditorControls.Flex();
                }

                int order = DrawEnumRow("块顺序", "速度点与滑杆谁在左", (int)data.blockOrder, OrderOptions);
                if (order != (int)data.blockOrder)
                {
                    data.blockOrder = (HoPlaySpeedBlockOrder)order;
                    HoPlaySpeedSettings.NotifyChanged();
                }

                int width = DrawEnumRow("按钮宽度", "等宽 = 一排按钮一样宽（对齐好看）；自适应 = 按文字宽", (int)data.buttonWidth, ButtonWidthOptions);
                if (width != (int)data.buttonWidth)
                {
                    data.buttonWidth = (HoPlaySpeedButtonWidth)width;
                    HoPlaySpeedSettings.NotifyChanged();
                }

                int format = DrawEnumRow("按钮格式", "×0.125 / ×0.125 / ×⅛", (int)data.format, FormatOptions);
                if (format != (int)data.format)
                {
                    data.format = (HoPlaySpeedFormat)format;
                    HoPlaySpeedSettings.NotifyChanged();
                }

                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Caption(
                        "面板**只有一行**（24px 高）：排不下就把窗口撑宽，不换行、不叠第二排。",
                        "最小宽度 = 速度点那排 + 滑杆那块。停靠在窄缝里放不下时，先把「按钮宽度」切自适应、或删掉几个速度点。");
                }
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 外部改动警告
        // ══════════════════════════════════════════════════════════════

        private void DrawWarningSection(HoPlaySpeedSettingsData data)
        {
            string summary = WarningOptions[(int)data.warning] + " · " + data.warningBlinkDuration.ToString("0.#") + " 秒";
            HoConstraintEditorControls.Section(ref warningExpanded, "外部改动警告", summary, HoConstraintEditorTheme.WarningColor);
            if (!warningExpanded)
            {
                return;
            }

            using (HoConstraintEditorControls.Card())
            {
                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Caption(
                        "有人（脚本 / 别的工具）改了 Time.timeScale 时，速度条底色闪一下。",
                        "面板自己写进去的值不算；只有「对不上」才会闪 —— 面板永远跟随外部，对不上就等于有人在改。");
                }

                int warning = DrawEnumRow("警告", "从不 / 总是", (int)data.warning, WarningOptions);
                if (warning != (int)data.warning)
                {
                    data.warning = (HoPlaySpeedWarning)warning;
                    HoPlaySpeedSettings.NotifyChanged();
                }

                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("警告色", HoConstraintEditorTheme.LabelWidth, "闪烁用的底色");
                    Rect colorRect = HoConstraintEditorControls.Next(HoConstraintEditorTheme.FieldWidth + 30f);
                    Color rgb = new Color(data.warningColor.r, data.warningColor.g, data.warningColor.b, 1f);
                    Color editedRgb = EditorGUI.ColorField(colorRect, rgb);
                    if (editedRgb != rgb)
                    {
                        data.warningColor = new Color(editedRgb.r, editedRgb.g, editedRgb.b, data.warningColor.a);
                        HoPlaySpeedSettings.NotifyChanged();
                    }

                    HoConstraintEditorControls.Gap();
                    HoConstraintEditorControls.Label("浓", HoConstraintEditorTheme.LabelWidthXs, "透明度（alpha）：警告底色有多显眼，0 = 看不见");
                    float alpha = HoConstraintEditorControls.MiniSlider(
                        data.warningColor.a,
                        0f,
                        1f,
                        0.75f,
                        "拖动改透明度；双击回默认",
                        70f);
                    if (!Mathf.Approximately(alpha, data.warningColor.a))
                    {
                        data.warningColor.a = alpha;
                        HoPlaySpeedSettings.NotifyChanged();
                    }

                    HoConstraintEditorControls.Gap();
                    if (HoConstraintEditorControls.Button(TestWarningLabel, TestWarningTooltip))
                    {
                        HoPlaySpeedWindow.FlashWarning();
                    }

                    HoConstraintEditorControls.Flex();
                }

                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("闪烁周期", HoConstraintEditorTheme.LabelWidth, "一次明暗来回几秒");
                    float period = HoConstraintEditorControls.NumberField(data.warningBlinkPeriod, "秒", "0.1 ~ 10 秒");
                    period = Mathf.Clamp(period, 0.1f, 10f);
                    if (!Mathf.Approximately(period, data.warningBlinkPeriod))
                    {
                        data.warningBlinkPeriod = period;
                        HoPlaySpeedSettings.NotifyChanged();
                    }

                    HoConstraintEditorControls.Gap();
                    HoConstraintEditorControls.Label("闪多久", HoConstraintEditorTheme.LabelWidth, "闪够这么久就自己停");
                    float duration = HoConstraintEditorControls.NumberField(data.warningBlinkDuration, "秒", "0.5 ~ 20 秒");
                    duration = Mathf.Clamp(duration, 0.5f, 20f);
                    if (!Mathf.Approximately(duration, data.warningBlinkDuration))
                    {
                        data.warningBlinkDuration = duration;
                        HoPlaySpeedSettings.NotifyChanged();
                    }

                    HoConstraintEditorControls.Flex();
                }
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 行为
        // ══════════════════════════════════════════════════════════════

        private void DrawBehaviorSection(HoPlaySpeedSettingsData data)
        {
            string summary = data.resetOnPlayEnd ? "退出播放自动复位" : "退出播放保留";
            HoConstraintEditorControls.Section(ref behaviorExpanded, "行为", summary, HoConstraintEditorTheme.AccentOutput);
            if (!behaviorExpanded)
            {
                return;
            }

            using (HoConstraintEditorControls.Card())
            {
                using (HoConstraintEditorControls.Row())
                {
                    bool autoReset = HoConstraintEditorControls.Toggle("播放结束自动复位", data.resetOnPlayEnd, "退出播放时把期望速度还原成进播放前那个值", 132f);
                    if (autoReset != data.resetOnPlayEnd)
                    {
                        data.resetOnPlayEnd = autoReset;
                        HoPlaySpeedSettings.NotifyChanged();
                    }

                    HoConstraintEditorControls.Flex();
                }

                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Caption(
                        "面板**永远跟随**外部改动（2026-09-30 用户定：不要「锁定」那档）：",
                        "外部改了 Time.timeScale，面板就把值收下来显示出去，并按警告设置闪一下。");
                }

                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Caption(
                        "所以脚本每帧自己设 timeScale 时，面板设的速度会被顶掉 —— 要压住脚本得用组件，不是编辑器窗口。",
                        "面板不跟外部抢时间是有意的：每帧对着写会打架，物理步长与动画都会抖。");
                }

                HoConstraintEditorControls.Separator();

                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Caption(
                        "退出播放或关掉速度条窗口时，Time.timeScale 一定会还原成进播放时那个值。",
                        "这是硬保证：面板是借时间的，不还就会把工程留在慢速上。");
                }
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 页脚
        // ══════════════════════════════════════════════════════════════

        private void DrawFooter(HoPlaySpeedSettingsData data)
        {
            using (HoConstraintEditorControls.Card())
            {
                using (HoConstraintEditorControls.Row())
                {
                    if (HoConstraintEditorControls.Button(ResetAllLabel, ResetAllTooltip))
                    {
                        if (EditorUtility.DisplayDialog(
                                "恢复播放速度的默认设置",
                                "速度点、布局、警告、锁定、自动复位都会回到出厂值。确定？",
                                "恢复",
                                "算了"))
                        {
                            HoPlaySpeedSettings.ResetToDefault();
                        }
                    }

                    HoConstraintEditorControls.Flex();
                }

                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Caption(
                        "配置存在 EditorPrefs（每台机器一份，不进仓库）：" + HoPlaySpeedSettings.PrefsKey,
                        "所以换机器 / 换 Unity 账号就是另一套设置；想清干净就把这个键删掉。");
                }
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 共用
        // ══════════════════════════════════════════════════════════════

        /// <summary>一行枚举：标签 + 分段胶囊（当场返回下标，不用异步菜单）。</summary>
        private static int DrawEnumRow(string label, string tooltip, int index, string[] options)
        {
            using (HoConstraintEditorControls.Row())
            {
                HoConstraintEditorControls.Label(label, HoConstraintEditorTheme.LabelWidth, tooltip);
                Rect rect = HoConstraintEditorControls.Next(HoConstraintEditorControls.SegmentedWidth(options));
                int result = HoConstraintEditorControls.EnumControl(rect, index, options);
                HoConstraintEditorControls.Flex();
                return result;
            }
        }
    }
}
