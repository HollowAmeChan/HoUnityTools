#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.PhysBoneTuner
{
    /// <summary>
    /// PhysBone 快速调参面板。
    ///
    /// 两条设计前提，决定了整个面板的形状：
    ///
    /// 1. **目标是一份用户自己维护的列表**，不跟 Selection 动态联动。
    ///    「加入选中 / 移除选中 / 清空」是显式动作，列表可序列化、跨域重载保留。
    ///    动态联动看起来省事，但选择一变目标就变，调参时很容易"改到了不是想改的东西"。
    ///
    /// 2. **滑杆直接实时写组件**，没有草稿、没有"应用"。
    ///    播放模式下 pull / spring / stiffness 是每帧现算的
    ///    （CalcPull(t) = pullCurve.Evaluate(t) × pull），所以拖的那一刻就生效。
    ///
    /// 范围刻意收窄：只调 pull / spring / stiffness / gravity / radius 五项，
    /// 其余 PhysBone 字段（碰撞、抓取、限制、动画…）一律不碰。
    ///
    /// 曲线：标量 × 曲线 = 最终作用值（曲线为空时 SDK 取常数 1，即均匀）。
    /// 面板不内嵌曲线编辑器 —— 曲线由预设 json 带进来。
    /// </summary>
    internal sealed class HoPhysBoneTunerWindow : EditorWindow
    {
        private const string MenuPath = "HoUnityTools/PhysBone 快速调参";

        // ---- 参数表：名字、SDK 字段、SDK 真值范围 ----
        //
        // 范围是从 SDK DLL 的 RangeAttribute 上读出来的，不是估的：
        //   pull/spring/stiffness Range(0,1)、gravity Range(-1,1)、radius 无范围。
        // gravity 允许负值 = 往上飘，做飘带 / 裙摆要用，所以下限是 -1 不是 0。

        private sealed class ParameterRow
        {
            public string Label;
            public string FieldName;
            public float Min;
            public float Max;
            public string Tooltip;
        }

        private static readonly ParameterRow[] Parameters =
        {
            new ParameterRow
            {
                Label = "Pull", FieldName = HoPhysBoneAccess.PullField,
                Min = HoPhysBoneAccess.PullMin, Max = HoPhysBoneAccess.PullMax,
                Tooltip = "骨骼回到静止位置的力度。SDK 范围 0–1，默认 0.2。\n" +
                          "越大越「跟手」、越不容易被甩开。",
            },
            new ParameterRow
            {
                Label = "Spring", FieldName = HoPhysBoneAccess.SpringField,
                Min = HoPhysBoneAccess.SpringMin, Max = HoPhysBoneAccess.SpringMax,
                Tooltip = "弹簧强度。SDK 范围 0–1，默认 0.2。\n" +
                          "越大回弹越快、抖动越少；越小越飘、惯性越明显。",
            },
            new ParameterRow
            {
                Label = "Stiffness", FieldName = HoPhysBoneAccess.StiffnessField,
                Min = HoPhysBoneAccess.StiffnessMin, Max = HoPhysBoneAccess.StiffnessMax,
                Tooltip = "抗弯曲刚度。SDK 范围 0–1，默认 0.2。\n" +
                          "越大越「硬」，形状保持得越好（耳朵、角常用高值）。",
            },
            new ParameterRow
            {
                Label = "Gravity", FieldName = HoPhysBoneAccess.GravityField,
                Min = HoPhysBoneAccess.GravityMin, Max = HoPhysBoneAccess.GravityMax,
                Tooltip = "重力。SDK 范围 −1–1，默认 0。\n" +
                          "正 = 往下坠；负 = 往上飘（飘带、裙摆会用）。",
            },
            new ParameterRow
            {
                Label = "Radius", FieldName = HoPhysBoneAccess.RadiusField,
                Min = 0.0f, Max = 0.5f,
                Tooltip = "模拟圆柱半径（米）。SDK 没有范围限制，这里给 0–0.5 的实用区间。\n" +
                          "经验值 ≈ 相邻骨长 × 0.25：约骨段长的四分之一，既有体积感又不捅穿网格。\n" +
                          "注意：radius 属于初始化期快照，改了要重建仿真才生效（见底部提示）。",
            },
        };

        // ---- 状态 ----

        /// <summary>
        /// 目标列表。**用户自己维护**，不跟 Selection 联动。
        ///
        /// 存 Component 而不是 GameObject：要写的就是组件本身，
        /// 而且列表里混着"骨骼上的 PhysBone"和"空物体上的 PhysBone"时，
        /// 存组件才能准确定位到要改的那一个。
        /// </summary>
        [SerializeField] private List<Component> targets = new List<Component>();

        [SerializeField] private string selectedPresetId = string.Empty;
        [SerializeField] private bool targetsExpanded = true;
        [SerializeField] private Vector2 scroll;

        /// <summary>内置库里选中的那条（只读；「填入」时才被读）。</summary>
        private HoPhysBonePreset libraryPreset;

        /// <summary>从磁盘导入的预设。与内置库分开存，免得被误当内置。</summary>
        private HoPhysBonePreset importedPreset;

        private static GUIStyle panelTitleStyle;
        private static GUIStyle panelStatusStyle;
        private static GUIStyle primaryButtonStyle;
        private static bool stylesBuiltForProSkin;

        // ---- 入口 ----

        [MenuItem(MenuPath, false, 30)]
        internal static void Open()
        {
            var window = GetWindow<HoPhysBoneTunerWindow>("PhysBone 调参");
            window.minSize = new Vector2(340f, 420f);
            window.Show();
        }

        [MenuItem("GameObject/HoUnityTools/PhysBone 快速调参", false, 30)]
        private static void OpenFromSelection()
        {
            var window = GetWindow<HoPhysBoneTunerWindow>("PhysBone 调参");
            window.minSize = new Vector2(340f, 420f);
            window.Show();
            // 从右键菜单进来时，顺手把选中的加进列表 —— 这是最常用的那一下。
            window.AddSelection();
        }

        private void OnEnable()
        {
            PruneTargets();
        }

        /// <summary>清掉列表里被销毁 / 丢失的引用。反序列化之后先跑一次。</summary>
        private void PruneTargets()
        {
            if (targets == null)
            {
                targets = new List<Component>();
                return;
            }

            var seen = new HashSet<int>();
            for (int index = targets.Count - 1; index >= 0; index--)
            {
                Component target = targets[index];
                if (target == null || !seen.Add(target.GetInstanceID()))
                    targets.RemoveAt(index);
            }
        }

        // ================= 界面 =================
        //
        // 风格对齐隔壁面板（HoFbxImportProcessingWindow / HoFastBuildWarudoModWindow）：
        // 每个区块一个 28px 标题条（左侧色条 + 标题 + 右对齐状态），
        // 正文只放控件，说明收进 tooltip。

        private void OnGUI()
        {
            EnsureStyles();

            scroll = EditorGUILayout.BeginScrollView(scroll);

            DrawPresetSection();
            GUILayout.Space(8f);
            DrawTargetSection();
            GUILayout.Space(8f);
            DrawParameterSection();
            GUILayout.Space(8f);
            DrawIoSection();

            EditorGUILayout.EndScrollView();

            GUILayout.Space(4f);
            DrawFooter();
        }

        private static void EnsureStyles()
        {
            bool proSkin = EditorGUIUtility.isProSkin;
            if (stylesBuiltForProSkin != proSkin ||
                !HoEditorStyles.MatchesSource(panelTitleStyle, EditorStyles.boldLabel) ||
                !HoEditorStyles.MatchesSource(panelStatusStyle, EditorStyles.miniLabel) ||
                !HoEditorStyles.MatchesSource(primaryButtonStyle, GUI.skin.button))
            {
                stylesBuiltForProSkin = proSkin;

                panelTitleStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(0, 0, 0, 0),
                };
                panelStatusStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleRight,
                    padding = new RectOffset(0, 0, 0, 0),
                };
                primaryButtonStyle = new GUIStyle(GUI.skin.button)
                {
                    fontStyle = FontStyle.Bold,
                };
            }
        }

        /// <summary>区块标题条：左侧色条 + 标题 + 右对齐状态。全窗统一用它。</summary>
        private void DrawPanelHeader(string title, string status, Color accent, string tooltip)
        {
            Rect rect = GUILayoutUtility.GetRect(0f, 28f, GUILayout.ExpandWidth(true));
            Color background = EditorGUIUtility.isProSkin
                ? new Color(0.17f, 0.18f, 0.20f)
                : new Color(0.82f, 0.83f, 0.85f);
            EditorGUI.DrawRect(rect, background);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4f, rect.height), accent);

            Rect titleRect = new Rect(rect.x + 12f, rect.y, rect.width - 130f, rect.height);
            Rect statusRect = new Rect(rect.xMax - 120f, rect.y, 112f, rect.height);
            GUI.Label(titleRect, new GUIContent(title, tooltip), panelTitleStyle);
            GUI.Label(statusRect, status, panelStatusStyle);
        }

        private void DrawFooter()
        {
            string hint;
            if (!HoPhysBoneAccess.IsAvailable)
            {
                hint = "未找到 VRCPhysBone —— 装好 VRChat SDK 后自动识别。";
            }
            else if (targets.Count == 0)
            {
                hint = "列表为空：在层级里选中骨骼，点「加入选中」。";
            }
            else
            {
                hint = $"实时写入中 · 目标 {targets.Count} 个";
            }

            EditorGUILayout.LabelField(
                new GUIContent(hint, RuntimeHintTooltip),
                EditorStyles.centeredGreyMiniLabel);
        }

        private const string RuntimeHintTooltip =
            "求值时机（SDK 行为）：\n" +
            "· PhysBone 的模拟器只在播放模式创建，编辑模式不跑模拟 —— 编辑模式拖滑杆只有数字在动。\n" +
            "· 播放模式下 pull / spring / stiffness 是每帧现算的（CalcPull(t) = pullCurve.Evaluate(t) × pull），\n" +
            "  拖动那一刻就生效，不需要任何 refresh 调用。\n" +
            "· radius 与 colliders 属于初始化期快照，改了要重建仿真才生效。";

        // ---- 预设 ----

        private void DrawPresetSection()
        {
            IList<HoPhysBonePreset> builtIn = HoPhysBonePresetLibrary.BuiltIn;
            string status = importedPreset != null
                ? "已导入 " + importedPreset.name
                : builtIn.Count + " 个内置";

            DrawPanelHeader("预设", status, new Color(0.62f, 0.45f, 0.84f), PresetSectionTooltip);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Space(4f);

                if (builtIn.Count == 0)
                {
                    EditorGUILayout.LabelField(
                        new GUIContent("读不到内置预设库", "包的 HoPhysBonePresets.json 缺失或被裁剪。不影响手动调参。"),
                        EditorStyles.miniLabel);
                }
                else
                {
                    var labels = new GUIContent[builtIn.Count];
                    int current = -1;
                    for (int index = 0; index < builtIn.Count; index++)
                    {
                        HoPhysBonePreset preset = builtIn[index];
                        labels[index] = new GUIContent(preset.name, preset.description);
                        if (string.Equals(preset.id, selectedPresetId, StringComparison.Ordinal))
                            current = index;
                    }

                    EditorGUI.BeginChangeCheck();
                    int picked = EditorGUILayout.Popup(
                        new GUIContent("预设", "只读，来自包内的 HoPhysBonePresets.json。"),
                        Mathf.Max(0, current),
                        labels);
                    if (EditorGUI.EndChangeCheck() && picked >= 0 && picked < builtIn.Count)
                    {
                        libraryPreset = builtIn[picked];
                        selectedPresetId = libraryPreset.id;
                        importedPreset = null;
                    }
                    else if (libraryPreset == null && current >= 0)
                    {
                        libraryPreset = builtIn[current];
                    }
                }

                HoPhysBonePreset active = ActivePreset();
                if (active != null)
                {
                    EditorGUILayout.LabelField(
                        new GUIContent(Summarize(active), PresetSummaryTooltip),
                        EditorStyles.miniLabel);
                }

                GUILayout.Space(4f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(active == null || targets.Count == 0))
                    {
                        if (GUILayout.Button(
                                new GUIContent("填入目标", "把预设的五个值（含曲线）写进列表里全部 PhysBone。\n" +
                                                             "只写这五项，碰撞 / 抓取 / 限制等一律不动。可 Ctrl+Z 撤销。"),
                                primaryButtonStyle,
                                GUILayout.Height(24f)))
                        {
                            ApplyPreset(active);
                        }
                    }

                    using (new EditorGUI.DisabledScope(targets.Count == 0))
                    {
                        if (GUILayout.Button(
                                new GUIContent("从目标抓取", "把列表里第一个 PhysBone 的当前值读成一个临时预设（含曲线）。\n" +
                                                             "抓完可以直接「导出草稿…」存成 json。"),
                                GUILayout.Height(24f)))
                        {
                            CaptureFromTargets();
                        }
                    }
                }

                GUILayout.Space(2f);
            }
        }

        private const string PresetSectionTooltip =
            "预设随包提供、只读；用户预设走「导入 json…」显式读盘，面板不扫工程目录。\n\n" +
            "「填入目标」是一次性写入 —— 填完之后仍然可以继续用下面的滑杆实时微调。";

        private const string PresetSummaryTooltip =
            "PhysBone 的最终作用值 = 标量 × 曲线在该骨骼处的取值（t = 骨骼在链内的位置 0–1）。\n" +
            "曲线为空时 SDK 取常数 1，也就是均匀。\n" +
            "曲线由预设 json 带进来，面板不内嵌曲线编辑器。";

        // ---- 目标列表 ----

        private void DrawTargetSection()
        {
            string status = targets.Count == 0 ? "空" : targets.Count + " 个";
            DrawPanelHeader("目标列表", status, new Color(0.24f, 0.54f, 0.88f), TargetSectionTooltip);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Space(4f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(
                            new GUIContent("加入选中", "把层级里选中的物体（含子层）里的 PhysBone 加进列表。\n" +
                                                       "重复的会自动跳过。"),
                            GUILayout.Height(22f)))
                    {
                        AddSelection();
                    }

                    if (GUILayout.Button(
                            new GUIContent("移除选中", "把层级里选中的 PhysBone 从列表里去掉。"),
                            GUILayout.Height(22f)))
                    {
                        RemoveSelection();
                    }

                    using (new EditorGUI.DisabledScope(targets.Count == 0))
                    {
                        if (GUILayout.Button(
                                new GUIContent("清空", "清空整个列表。不影响场景里的组件。"),
                                GUILayout.Height(22f)))
                        {
                            targets.Clear();
                        }
                    }
                }

                GUILayout.Space(4f);

                if (targets.Count == 0)
                {
                    EditorGUILayout.LabelField(
                        new GUIContent(
                            HoPhysBoneAccess.IsAvailable ? "列表为空" : "未找到 VRCPhysBone",
                            HoPhysBoneAccess.IsAvailable
                                ? "在层级里选中带 PhysBone 的骨骼（或它的父级），点「加入选中」。"
                                : "装好 VRChat SDK 后这里会列出 PhysBone。"),
                        EditorStyles.miniLabel);
                }
                else
                {
                    targetsExpanded = EditorGUILayout.Foldout(
                        targetsExpanded,
                        new GUIContent("列表内容（" + targets.Count + "）", "展开查看 / 逐个移除。"),
                        true);

                    if (targetsExpanded)
                    {
                        const int MaxRows = 12;
                        int shown = Mathf.Min(targets.Count, MaxRows);
                        for (int index = 0; index < shown; index++)
                        {
                            DrawTargetRow(index);
                        }
                        if (targets.Count > shown)
                        {
                            EditorGUILayout.LabelField(
                                new GUIContent($"… 另外 {targets.Count - shown} 个", "折叠只是显示限制，全部都会被写入。"),
                                EditorStyles.miniLabel);
                        }
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(
                                new GUIContent("在层级里选中它们", "把这批 PhysBone 所属物体一起选进层级，便于整体查看。"),
                                GUILayout.Height(20f)))
                        {
                            SelectTargetsInHierarchy();
                        }
                    }
                }

                GUILayout.Space(2f);
            }
        }

        private const string TargetSectionTooltip =
            "这份列表是**手动维护**的，不跟层级选择联动 —— 这样调参时不会因为顺手点了一下别处\n" +
            "就把改动写到别的骨骼上。\n" +
            "列表会随窗口一起序列化保留（含域重载）。";

        private void DrawTargetRow(int index)
        {
            Component target = targets[index];
            if (target == null)
                return;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    new GUIContent("· " + target.gameObject.name, HoPhysBoneAccess.GetPath(target)),
                    EditorStyles.miniLabel);

                if (GUILayout.Button(
                        new GUIContent("×", "从列表里移除这一条（不影响场景）。"),
                        EditorStyles.miniButton,
                        GUILayout.Width(20f)))
                {
                    targets.RemoveAt(index);
                }
            }
        }

        // ---- 参数（实时写入）----

        private void DrawParameterSection()
        {
            DrawPanelHeader("参数", targets.Count == 0 ? "无目标" : "实时", new Color(0.20f, 0.68f, 0.57f), ParameterSectionTooltip);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Space(4f);

                bool enabled = HoPhysBoneAccess.IsAvailable && targets.Count > 0;
                using (new EditorGUI.DisabledScope(!enabled))
                {
                    for (int index = 0; index < Parameters.Length; index++)
                        DrawParameterRow(Parameters[index], enabled);
                }

                GUILayout.Space(2f);
            }
        }

        private const string ParameterSectionTooltip =
            "拖动即写入列表里全部 PhysBone（可 Ctrl+Z 撤销）。没有草稿、没有「应用」这一步。\n" +
            "多个目标的值不一致时显示为混合态；拖一下就把它们统一。\n\n" +
            "播放模式下 pull / spring / stiffness 立刻生效；编辑模式看不到物理变化。";

        private void DrawParameterRow(ParameterRow row, bool enabled)
        {
            float value;
            bool mixed = TryReadCommonValue(row.FieldName, out value);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(row.Label, row.Tooltip), GUILayout.Width(66f));

                using (new EditorGUI.DisabledScope(!enabled))
                {
                    float edited;
                    bool changed;
                    // showMixedValue 必须成对还原，否则会漏到后续控件上，
                    // 让整个面板的数值字段看起来都是"多值"。
                    try
                    {
                        EditorGUI.showMixedValue = mixed;
                        EditorGUI.BeginChangeCheck();
                        edited = EditorGUILayout.Slider(value, row.Min, row.Max);
                        changed = EditorGUI.EndChangeCheck();
                    }
                    finally
                    {
                        EditorGUI.showMixedValue = false;
                    }
                    if (changed)
                        WriteParameter(row, edited);
                }

                using (new EditorGUI.DisabledScope(!enabled))
                {
                    float typed;
                    bool changed;
                    try
                    {
                        EditorGUI.showMixedValue = mixed;
                        EditorGUI.BeginChangeCheck();
                        typed = EditorGUILayout.FloatField(value, GUILayout.Width(58f));
                        changed = EditorGUI.EndChangeCheck();
                    }
                    finally
                    {
                        EditorGUI.showMixedValue = false;
                    }
                    if (changed)
                        WriteParameter(row, Mathf.Clamp(typed, row.Min, row.Max));
                }

                // 曲线状态收成行内小标记，不单独占一行。
                AnimationCurve curve = ReadCurve(targets.Count > 0 ? targets[0] : null, row.FieldName + "Curve");
                bool hasCurve = HasKeys(curve);
                EditorGUILayout.LabelField(
                    new GUIContent(
                        hasCurve ? "〰 " + curve.length : "〰 –",
                        hasCurve
                            ? "这条参数带曲线（" + curve.length + " 键），沿链分布。\n" +
                              "最终作用值 = 标量 × 曲线取值，t = 骨骼在链内的位置 0–1。"
                            : "这条参数没有曲线：SDK 按常数 1 处理，标量均匀作用在整条链上。"),
                    EditorStyles.miniLabel,
                    GUILayout.Width(38f));
            }
        }

        /// <summary>读列表里这个参数的公共值；不一致时返回 true（混合态）。</summary>
        private bool TryReadCommonValue(string fieldName, out float value)
        {
            value = 0f;
            bool any = false;
            for (int index = 0; index < targets.Count; index++)
            {
                if (!HoPhysBoneAccess.TryReadFloat(targets[index], fieldName, out float read))
                    continue;
                if (!any)
                {
                    value = read;
                    any = true;
                    continue;
                }
                if (!Mathf.Approximately(value, read))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 实时写入：一次撤销步记录全部目标，然后逐个写。
        /// 拖动期间 Unity 会把连续改动并进同一组，所以一次拖动 = 一次 Ctrl+Z。
        /// </summary>
        private void WriteParameter(ParameterRow row, float value)
        {
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("PhysBone 调参：" + row.Label);

            var objects = new List<UnityEngine.Object>();
            for (int index = 0; index < targets.Count; index++)
            {
                if (targets[index] != null)
                    objects.Add(targets[index]);
            }
            if (objects.Count == 0)
                return;
            Undo.RecordObjects(objects.ToArray(), "PhysBone 调参：" + row.Label);

            int failed = 0;
            for (int index = 0; index < targets.Count; index++)
            {
                Component target = targets[index];
                if (target == null)
                    continue;
                if (HoPhysBoneAccess.TryWriteFloat(target, row.FieldName, value))
                    EditorUtility.SetDirty(target);
                else
                    failed++;
            }

            Undo.CollapseUndoOperations(group);

            if (failed > 0)
            {
                Debug.LogWarning(
                    "HoPhysBoneTuner: 有 " + failed + " 个目标写不进 " + row.FieldName +
                    "，可能是 SDK 版本差异导致字段名变了。");
            }
        }

        // ---- 预设文件 ----

        private void DrawIoSection()
        {
            string builtInPath = HoPhysBonePresetLibrary.BuiltInPath;
            DrawPanelHeader(
                "预设文件",
                builtInPath == null ? "内置库未找到" : "json",
                new Color(0.91f, 0.65f, 0.25f),
                IoSectionTooltip);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Space(4f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(
                            new GUIContent("导入 json…", "从磁盘读一个预设 json 进面板（不写场景）。\n" +
                                                         "两种写法都接受：带 preset 包封的，或裸预设。"),
                            GUILayout.Height(22f)))
                    {
                        ImportPreset();
                    }

                    using (new EditorGUI.DisabledScope(ActivePreset() == null))
                    {
                        if (GUILayout.Button(
                                new GUIContent("导出 json…", "把当前载入的预设（含曲线）存成 json。"),
                                GUILayout.Height(22f)))
                        {
                            ExportActivePreset();
                        }
                    }
                }

                EditorGUILayout.LabelField(
                    new GUIContent(
                        builtInPath == null ? "内置库：未找到" : "内置库：" + builtInPath,
                        "内置预设的 json 就在这里，只读。要改内置预设就改这个文件。"),
                    EditorStyles.miniLabel);
                GUILayout.Space(2f);
            }
        }

        private const string IoSectionTooltip =
            "内置预设随包走、只读。\n" +
            "你自己的预设存成 json 或导入别人给的 json —— 都是显式点按钮，面板不会去扫工程目录。";

        // ---- 列表维护 ----

        /// <summary>把层级里选中的物体（含子层）里的 PhysBone 加进列表，重复的跳过。</summary>
        private void AddSelection()
        {
            List<Component> found = CollectFromSelection();
            if (found.Count == 0)
                return;

            int added = 0;
            for (int index = 0; index < found.Count; index++)
            {
                if (!targets.Contains(found[index]))
                {
                    targets.Add(found[index]);
                    added++;
                }
            }
            Repaint();

            if (added == 0)
                Debug.Log("HoPhysBoneTuner: 选中的 PhysBone 已经在列表里了。");
        }

        private void RemoveSelection()
        {
            List<Component> selected = CollectFromSelection();
            if (selected.Count == 0)
                return;

            for (int index = targets.Count - 1; index >= 0; index--)
            {
                if (selected.Contains(targets[index]))
                    targets.RemoveAt(index);
            }
            Repaint();
        }

        private static List<Component> CollectFromSelection()
        {
            var roots = new List<Transform>();
            Transform[] selected = Selection.transforms;
            for (int index = 0; index < selected.Length; index++)
            {
                if (selected[index] != null)
                    roots.Add(selected[index]);
            }
            return HoPhysBoneAccess.CollectPhysBones(roots);
        }

        private void SelectTargetsInHierarchy()
        {
            var objects = new List<UnityEngine.Object>();
            for (int index = 0; index < targets.Count; index++)
            {
                if (targets[index] != null)
                    objects.Add(targets[index].gameObject);
            }
            if (objects.Count > 0)
                Selection.objects = objects.ToArray();
        }

        // ---- 预设读写 ----

        private HoPhysBonePreset ActivePreset()
        {
            return importedPreset != null ? importedPreset : libraryPreset;
        }

        /// <summary>
        /// 把预设写进列表里全部目标。
        /// 自适应项（半径按骨长、重力按垂直度）在这里按每个目标现场推导 ——
        /// 这是预设能"同一条适配不同角色/不同朝向"的关键。
        /// </summary>
        private void ApplyPreset(HoPhysBonePreset preset)
        {
            if (preset == null || targets.Count == 0)
                return;
            if (!EditorUtility.DisplayDialog(
                    "填入目标",
                    $"把「{preset.name}」写进列表里的 {targets.Count} 个 PhysBone：\n\n" +
                    $"pull {preset.pull:0.###}　spring {preset.spring:0.###}　stiffness {preset.stiffness:0.###}\n" +
                    $"gravity {DescribeAdaptive(preset.gravity, preset.gravityScaleWithVerticality, preset.gravityVertical)}　" +
                    $"radius {DescribeAdaptive(preset.radius, preset.radiusFromBoneSize, preset.radiusBoneRatio)}\n" +
                    CurveSummary(preset) + "\n\n" +
                    "只写这五项（及其曲线），碰撞 / 抓取 / 限制等其它设置一律不动。可 Ctrl+Z 撤销。",
                    "写入",
                    "取消"))
            {
                return;
            }

            var objects = new List<UnityEngine.Object>();
            for (int index = 0; index < targets.Count; index++)
            {
                if (targets[index] != null)
                    objects.Add(targets[index]);
            }
            if (objects.Count == 0)
                return;

            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("PhysBone 调参：填入 " + preset.name);
            Undo.RecordObjects(objects.ToArray(), "PhysBone 调参：填入预设");

            for (int index = 0; index < targets.Count; index++)
            {
                Component target = targets[index];
                if (target == null)
                    continue;

                float gravity = preset.gravityScaleWithVerticality
                    ? Mathf.Lerp(preset.gravity, preset.gravityVertical,
                        HoPhysBoneAccess.MeasureVerticality(target))
                    : preset.gravity;

                float radius = preset.radius;
                if (preset.radiusFromBoneSize)
                {
                    float boneSize = HoPhysBoneAccess.MeasureBoneSize(target);
                    radius = boneSize > 0f
                        ? Mathf.Clamp(boneSize * preset.radiusBoneRatio, preset.radiusMin, preset.radiusMax)
                        : preset.radiusFallback;
                }

                HoPhysBoneAccess.TryWriteFloat(target, HoPhysBoneAccess.PullField, preset.pull);
                HoPhysBoneAccess.TryWriteFloat(target, HoPhysBoneAccess.SpringField, preset.spring);
                HoPhysBoneAccess.TryWriteFloat(target, HoPhysBoneAccess.StiffnessField, preset.stiffness);
                HoPhysBoneAccess.TryWriteFloat(target, HoPhysBoneAccess.GravityField, gravity);
                HoPhysBoneAccess.TryWriteFloat(target, HoPhysBoneAccess.RadiusField, radius);

                ApplyCurve(target, HoPhysBoneAccess.PullField + "Curve",
                    preset.HasPullCurveField, preset.usePullCurve, preset.pullCurve);
                ApplyCurve(target, HoPhysBoneAccess.SpringField + "Curve",
                    preset.HasSpringCurveField, preset.useSpringCurve, preset.springCurve);
                ApplyCurve(target, HoPhysBoneAccess.StiffnessField + "Curve",
                    preset.HasStiffnessCurveField, preset.useStiffnessCurve, preset.stiffnessCurve);
                ApplyCurve(target, HoPhysBoneAccess.GravityField + "Curve",
                    preset.HasGravityCurveField, preset.useGravityCurve, preset.gravityCurve);
                ApplyCurve(target, HoPhysBoneAccess.RadiusField + "Curve",
                    preset.HasRadiusCurveField, preset.useRadiusCurve, preset.radiusCurve);

                EditorUtility.SetDirty(target);
            }

            Undo.CollapseUndoOperations(group);
            Repaint();
        }

        /// <summary>把列表里第一个目标的当前值抓成一个临时预设，供导出。</summary>
        private void CaptureFromTargets()
        {
            if (targets.Count == 0 || targets[0] == null)
                return;

            Component first = targets[0];
            var captured = new HoPhysBonePreset
            {
                id = "captured_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                name = first.gameObject.name,
                description = "从面板目标列表的第一个 PhysBone 抓取。",
            };

            HoPhysBoneAccess.TryReadFloat(first, HoPhysBoneAccess.PullField, out captured.pull);
            HoPhysBoneAccess.TryReadFloat(first, HoPhysBoneAccess.SpringField, out captured.spring);
            HoPhysBoneAccess.TryReadFloat(first, HoPhysBoneAccess.StiffnessField, out captured.stiffness);
            HoPhysBoneAccess.TryReadFloat(first, HoPhysBoneAccess.GravityField, out captured.gravity);
            HoPhysBoneAccess.TryReadFloat(first, HoPhysBoneAccess.RadiusField, out captured.radius);

            captured.pullCurve = ReadCurve(first, HoPhysBoneAccess.PullField + "Curve");
            captured.springCurve = ReadCurve(first, HoPhysBoneAccess.SpringField + "Curve");
            captured.stiffnessCurve = ReadCurve(first, HoPhysBoneAccess.StiffnessField + "Curve");
            captured.gravityCurve = ReadCurve(first, HoPhysBoneAccess.GravityField + "Curve");
            captured.radiusCurve = ReadCurve(first, HoPhysBoneAccess.RadiusField + "Curve");

            captured.usePullCurve = HasKeys(captured.pullCurve);
            captured.useSpringCurve = HasKeys(captured.springCurve);
            captured.useStiffnessCurve = HasKeys(captured.stiffnessCurve);
            captured.useGravityCurve = HasKeys(captured.gravityCurve);
            captured.useRadiusCurve = HasKeys(captured.radiusCurve);

            captured.HasPullCurveField = true;
            captured.HasSpringCurveField = true;
            captured.HasStiffnessCurveField = true;
            captured.HasGravityCurveField = true;
            captured.HasRadiusCurveField = true;

            importedPreset = captured;
            libraryPreset = null;
            selectedPresetId = captured.id;
            Repaint();
        }

        private void ImportPreset()
        {
            HoPhysBonePreset preset = HoPhysBonePresetLibrary.ImportFromDisk(out string error);
            if (preset == null)
            {
                if (!string.IsNullOrEmpty(error))
                    EditorUtility.DisplayDialog("导入预设", error, "确定");
                return;
            }

            importedPreset = preset;
            selectedPresetId = preset.id;
            Repaint();
        }

        private void ExportActivePreset()
        {
            HoPhysBonePreset active = ActivePreset();
            if (active == null)
                return;

            string path = HoPhysBonePresetLibrary.ExportToDisk(active, out string error);
            if (!string.IsNullOrEmpty(error))
                EditorUtility.DisplayDialog("导出预设", error, "确定");
            else if (!string.IsNullOrEmpty(path))
                Debug.Log("HoPhysBoneTuner: 预设已写入 " + path);
        }

        // ---- 曲线辅助 ----

        private static bool HasKeys(AnimationCurve curve)
        {
            return curve != null && curve.length > 0;
        }

        /// <summary>
        /// 按预设的意图处理一条曲线：
        ///   键没出现过 → 不动（保留用户已有的曲线）
        ///   出现过且要清空 → 写 null（回到均匀）
        ///   出现过且带曲线 → 写入
        /// </summary>
        private static void ApplyCurve(
            Component target,
            string fieldName,
            bool hasField,
            bool useCurve,
            AnimationCurve curve)
        {
            if (!hasField)
                return;

            if (useCurve && HasKeys(curve))
                WriteCurve(target, fieldName, curve);
            else
                WriteCurve(target, fieldName, null);
        }

        private static AnimationCurve ReadCurve(Component target, string fieldName)
        {
            if (target == null)
                return null;
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(
                    fieldName,
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.DeclaredOnly);
                if (field == null)
                    continue;
                try
                {
                    return field.GetValue(target) as AnimationCurve;
                }
                catch (Exception)
                {
                    return null;
                }
            }
            return null;
        }

        private static bool WriteCurve(Component target, string fieldName, AnimationCurve curve)
        {
            if (target == null)
                return false;
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(
                    fieldName,
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.DeclaredOnly);
                if (field == null)
                    continue;
                try
                {
                    field.SetValue(target, curve);
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }
            return false;
        }

        private static string CurveSummary(HoPhysBonePreset preset)
        {
            var names = new List<string>();
            if (preset.HasPullCurveField && preset.usePullCurve && HasKeys(preset.pullCurve)) names.Add("pull");
            if (preset.HasSpringCurveField && preset.useSpringCurve && HasKeys(preset.springCurve)) names.Add("spring");
            if (preset.HasStiffnessCurveField && preset.useStiffnessCurve && HasKeys(preset.stiffnessCurve)) names.Add("stiffness");
            if (preset.HasGravityCurveField && preset.useGravityCurve && HasKeys(preset.gravityCurve)) names.Add("gravity");
            if (preset.HasRadiusCurveField && preset.useRadiusCurve && HasKeys(preset.radiusCurve)) names.Add("radius");

            if (names.Count == 0)
                return "曲线：无（标量均匀作用在整条链上）";
            return "曲线：" + string.Join(" / ", names.ToArray()) + "（沿链分布，标量是整体强度）";
        }

        /// <summary>预设概览：五个值 + 自适应标记。</summary>
        private static string Summarize(HoPhysBonePreset preset)
        {
            return $"pull {preset.pull:0.##} · spring {preset.spring:0.##} · stiffness {preset.stiffness:0.##} · " +
                   $"gravity {DescribeAdaptive(preset.gravity, preset.gravityScaleWithVerticality, preset.gravityVertical)} · " +
                   $"radius {DescribeAdaptive(preset.radius, preset.radiusFromBoneSize, preset.radiusBoneRatio)}";
        }

        private static string DescribeAdaptive(float value, bool adaptive, float adaptiveMax)
        {
            return adaptive
                ? value.ToString("0.###") + "→" + adaptiveMax.ToString("0.###")
                : value.ToString("0.###");
        }
    }
}
#endif
