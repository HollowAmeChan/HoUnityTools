using System.Collections.Generic;
using Hollow.HoUnityTools.Constraints;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Constraints
{
    /// <summary>
    /// 眨眼面板。排版全部走 <see cref="HoConstraintEditorControls"/> 的栅格与自绘控件，
    /// 这里只负责"哪一行放什么、点了做什么"。设计稿见 `.design/blink-panel.html`。
    /// </summary>
    [CustomEditor(typeof(HoBlinkConstraint))]
    internal sealed class HoBlinkConstraintEditor : UnityEditor.Editor
    {
        // ── 序列化字段 ──────────────────────────────────────────────────
        private SerializedProperty updateMode;
        private SerializedProperty evaluateInEditMode;
        private SerializedProperty renderers;
        private SerializedProperty blinkEnabled;
        private SerializedProperty intervalDistribution;
        private SerializedProperty intervalMin;
        private SerializedProperty intervalMax;
        private SerializedProperty closeDuration;
        private SerializedProperty holdDuration;
        private SerializedProperty openDuration;
        private SerializedProperty blinkCurve;
        private SerializedProperty strength;
        private SerializedProperty doubleBlinkChance;
        private SerializedProperty doubleBlinkGap;
        private SerializedProperty randomSeed;
        private SerializedProperty pauseWhenDriven;
        private SerializedProperty pauseThreshold;
        private SerializedProperty pauseDuration;
        private SerializedProperty blinkTargets;
        private SerializedProperty rules;
        private SerializedProperty writeThreshold;
        private SerializedProperty writingEnabled;
        private SerializedProperty mergeMode;

        // ── 展开状态 ────────────────────────────────────────────────────
        private bool meshExpanded;
        private bool blinkExpanded;
        private bool blinkDetailExpanded;
        private bool rulesExpanded;
        private bool debugExpanded;
        private bool manualDrive;

        private readonly List<bool> ruleFoldouts = new List<bool>();
        private readonly List<bool> ruleDetails = new List<bool>();
        private readonly List<bool> targetDetails = new List<bool>();
        private readonly List<HoShapeKeySaturation> saturationBuffer = new List<HoShapeKeySaturation>();

        // ── 文案（短标签 + tooltip 里说全）───────────────────────────────
        private static readonly string[] RampPresetNames = { "直通", "柔跟", "放大", "缓入", "慢放", "阶梯", "自定义" };
        private static readonly int[] RampPresetValues = { 0, 1, 2, 3, 4, 5, 6 };

        private static readonly GUIContent RenderersLabel = new GUIContent("网格", "眨眼写这些网格上的形态键；支持任意 Renderer，运行时解析出 SkinnedMeshRenderer。");
        private static readonly GUIContent EnabledLabel = new GUIContent("启用", "关掉就不自动眨眼（规则仍然可以读形态键做果冻）。");
        private static readonly GUIContent KeyLabel = new GUIContent("键", "这个眼睑输出写到哪个形态键上。");
        private static readonly GUIContent DetailLabel = new GUIContent("细节", "不常动的项。");
        private const string DriverMeterTooltip = "白刻度 = 键上的原始值（弹簧/平滑之前），色条 = 真正拿去驱动目标的值。";
        private const string OutputMeterTooltip = "最终写进键的值：驱动 → 增益 → ramp 预设 → 强度 → 权重 → 偏移，再钳制到输出范围。";

        private void OnEnable()
        {
            updateMode = Find("updateMode");
            evaluateInEditMode = Find("evaluateInEditMode");
            renderers = Find("renderers");
            blinkEnabled = Find("blinkEnabled");
            intervalDistribution = Find("intervalDistribution");
            intervalMin = Find("intervalMin");
            intervalMax = Find("intervalMax");
            closeDuration = Find("closeDuration");
            holdDuration = Find("holdDuration");
            openDuration = Find("openDuration");
            blinkCurve = Find("blinkCurve");
            strength = Find("strength");
            doubleBlinkChance = Find("doubleBlinkChance");
            doubleBlinkGap = Find("doubleBlinkGap");
            randomSeed = Find("randomSeed");
            pauseWhenDriven = Find("pauseWhenDriven");
            pauseThreshold = Find("pauseThreshold");
            pauseDuration = Find("pauseDuration");
            blinkTargets = Find("blinkTargets");
            rules = Find("rules");
            writeThreshold = Find("writeThreshold");
            writingEnabled = Find("writingEnabled");
            mergeMode = Find("mergeMode");
            ruleFoldouts.Clear();
            ruleDetails.Clear();
            targetDetails.Clear();
        }

        private SerializedProperty Find(string name)
        {
            return serializedObject.FindProperty(name);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            HoBlinkConstraint constraint = (HoBlinkConstraint)target;
            HoConstraintEditorControls.Title(
                "Ho 眨眼约束",
                constraint.MeshCount + " 网格 · " + constraint.BindingCount + " 绑定 · " + constraint.RuleCount + " 规则",
                ("写入 " + (writingEnabled.boolValue ? "开" : "关"), writingEnabled.boolValue),
                ("眨眼 " + (blinkEnabled.boolValue ? "开" : "关"), blinkEnabled.boolValue));

            DrawMeshSection(constraint);
            DrawBlinkSection(constraint);
            DrawRulesSection(constraint);
            DrawDebugSection(constraint);

            serializedObject.ApplyModifiedProperties();

            if (Application.isPlaying || manualDrive)
            {
                Repaint();
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 目标网格
        // ══════════════════════════════════════════════════════════════
        private void DrawMeshSection(HoBlinkConstraint constraint)
        {
            string summary = constraint.MeshCount + " 网格 · " + constraint.BindingCount + " 绑定";
            if (!HoConstraintEditorControls.Section(ref meshExpanded, "目标网格", summary, HoConstraintEditorTheme.AccentMesh))
            {
                return;
            }

            using (HoConstraintEditorControls.Card())
            {
                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("网格", HoConstraintEditorTheme.LabelWidthSm, RenderersLabel.tooltip);
                    if (HoConstraintEditorControls.Button("收集子级"))
                    {
                        Undo.RecordObject(constraint, "收集眨眼约束网格");
                        constraint.CollectChildRenderers();
                        EditorUtility.SetDirty(constraint);
                        serializedObject.Update();
                    }

                    HoConstraintEditorControls.Gap();
                    if (HoConstraintEditorControls.Button("重新解析"))
                    {
                        HoBlinkPresetActions.Rebuild(constraint);
                    }
                }

                float listHeight = EditorGUI.GetPropertyHeight(renderers, true);
                Rect listRect = GUILayoutUtility.GetRect(0.0f, 4000.0f, listHeight, listHeight, GUILayout.ExpandWidth(true));
                EditorGUI.PropertyField(listRect, renderers, GUIContent.none, true);

                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Label("更新时机", HoConstraintEditorTheme.LabelWidth, "写在动画之后才能叠加动画；默认 LateUpdate。");
                    updateMode.enumValueIndex = Segment(updateMode, "写在动画之后才能叠加动画。");
                }

                using (HoConstraintEditorControls.Row(true))
                {
                    writingEnabled.boolValue = HoConstraintEditorControls.Toggle("允许写入", writingEnabled.boolValue, "关掉就只算不写（调试用）。");
                    HoConstraintEditorControls.Gap();
                    HoConstraintEditorControls.Label("阈值", HoConstraintEditorTheme.LabelWidthSm, "变化小于该值就不写，减少 mesh dirty。");
                    writeThreshold.floatValue = HoConstraintEditorControls.NumberField(writeThreshold.floatValue, null, "变化小于该值就不写。");
                    HoConstraintEditorControls.Gap();
                    HoConstraintEditorControls.Label("合并", HoConstraintEditorTheme.LabelWidthSm, HoConstraintEditorSectionGui.MergeModeLabel.tooltip);
                    mergeMode.enumValueIndex = Segment(mergeMode, HoConstraintEditorSectionGui.MergeModeLabel.tooltip);
                }

                using (HoConstraintEditorControls.Row(true))
                {
                    evaluateInEditMode.boolValue = HoConstraintEditorControls.Toggle(
                        "编辑模式求值",
                        evaluateInEditMode.boolValue,
                        "编辑模式也写入形态键（会把场景标脏）；只想看数值就用调试区的手动驱动。");
                    HoConstraintEditorControls.Gap();
                    HoConstraintEditorControls.Caption("（不播放时读数不动，除非打开它）");
                }

                if (constraint.MissingKeys.Count > 0)
                {
                    EditorGUILayout.HelpBox("以下键在目标网格上不存在：" + string.Join("、", constraint.MissingKeys), MessageType.Warning);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 眨眼（自动眨眼本身写的眼睑键）
        // ══════════════════════════════════════════════════════════════
        private void DrawBlinkSection(HoBlinkConstraint constraint)
        {
            string summary = blinkEnabled.boolValue ? "开" : "关";
            if (blinkTargets.arraySize > 0)
            {
                string firstKey = blinkTargets.GetArrayElementAtIndex(0).FindPropertyRelative("keyName").stringValue;
                summary += " · " + (string.IsNullOrEmpty(firstKey) ? "键未填" : firstKey)
                    + (blinkTargets.arraySize > 1 ? " 等 " + blinkTargets.arraySize + " 个" : string.Empty);
            }

            if (!HoConstraintEditorControls.Section(ref blinkExpanded, "眨眼", summary, HoConstraintEditorTheme.AccentBlink))
            {
                return;
            }

            using (HoConstraintEditorControls.Card())
            {
                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Flex();
                    if (HoConstraintEditorControls.Button("自动匹配眼睑键", "按模型上实际存在的键选：有双眼闭眼键就用一个，否则用左右两个。", true))
                    {
                        HoBlinkPresetActions.AutoMatchEyelidKeys(constraint, serializedObject);
                        serializedObject.Update();
                    }

                    HoConstraintEditorControls.Gap();
                    if (HoConstraintEditorControls.Button("＋ 键"))
                    {
                        AddTarget(blinkTargets);
                    }
                }

                using (HoConstraintEditorControls.Row(true))
                {
                    blinkEnabled.boolValue = HoConstraintEditorControls.Toggle("自动眨眼", blinkEnabled.boolValue, EnabledLabel.tooltip);
                    HoConstraintEditorControls.Flex();
                    HoConstraintEditorControls.MeterRow(
                        constraint.BlinkValue,
                        0.0f,
                        1.0f,
                        HoConstraintEditorTheme.AccentBlink,
                        constraint.BlinkValue.ToString("0.00"),
                        "闭眼",
                        float.NaN,
                        "自动眨眼算出来的闭眼量（0 = 睁开，1 = 闭合）。");
                }

                for (int i = 0; i < blinkTargets.arraySize; i++)
                {
                    DrawTarget(blinkTargets, i, -1);
                }

                using (new EditorGUI.DisabledScope(!blinkEnabled.boolValue))
                {
                    using (HoConstraintEditorControls.Row(true))
                    {
                        HoConstraintEditorControls.Label("间隔", HoConstraintEditorTheme.LabelWidthSm, "两次眨眼的间隔（秒）。指数分布更接近真人。");
                        intervalDistribution.enumValueIndex = Segment(intervalDistribution, "指数分布更接近真人，均匀分布更机械。");
                        HoConstraintEditorControls.Gap();
                        intervalMin.floatValue = HoConstraintEditorControls.NumberField(intervalMin.floatValue, null, "间隔下限（秒）。");
                        HoConstraintEditorControls.Caption("–");
                        intervalMax.floatValue = HoConstraintEditorControls.NumberField(intervalMax.floatValue, "s", "间隔上限（秒）。");
                    }

                    using (HoConstraintEditorControls.Row(true))
                    {
                        HoConstraintEditorControls.Label("闭合", HoConstraintEditorTheme.LabelWidthSm, "闭合时长（秒）。");
                        closeDuration.floatValue = HoConstraintEditorControls.NumberField(closeDuration.floatValue);
                        HoConstraintEditorControls.Gap();
                        HoConstraintEditorControls.Label("保持", HoConstraintEditorTheme.LabelWidthSm, "保持时长（秒）。");
                        holdDuration.floatValue = HoConstraintEditorControls.NumberField(holdDuration.floatValue);
                        HoConstraintEditorControls.Gap();
                        HoConstraintEditorControls.Label("张开", HoConstraintEditorTheme.LabelWidthSm, "张开时长（秒）。");
                        openDuration.floatValue = HoConstraintEditorControls.NumberField(openDuration.floatValue);
                    }
                }

                using (HoConstraintEditorControls.Row(true))
                {
                    blinkDetailExpanded = HoConstraintEditorControls.InlineFoldout(blinkDetailExpanded, "细节", DetailLabel.tooltip);
                    HoConstraintEditorControls.Gap();
                    HoConstraintEditorControls.Caption("曲线 · 强度 · 双击 · 随机种子 · 外部接管");
                }

                if (!blinkDetailExpanded)
                {
                    return;
                }

                using (HoConstraintEditorControls.Indent())
                using (new EditorGUI.DisabledScope(!blinkEnabled.boolValue))
                {
                    EditorGUILayout.PropertyField(blinkCurve, new GUIContent("曲线", "眨眼曲线：横轴为相位，纵轴为闭合量。"));

                    using (HoConstraintEditorControls.Row(true))
                    {
                        HoConstraintEditorControls.Label("强度", HoConstraintEditorTheme.LabelWidthSm, "闭眼量整体缩放。");
                        strength.floatValue = HoConstraintEditorControls.NumberField(strength.floatValue);
                        HoConstraintEditorControls.Gap();
                        HoConstraintEditorControls.Label("双击", HoConstraintEditorTheme.LabelWidthSm, "双击概率（0–1）。");
                        doubleBlinkChance.floatValue = HoConstraintEditorControls.NumberField(doubleBlinkChance.floatValue);
                        HoConstraintEditorControls.Gap();
                        HoConstraintEditorControls.Label("双击隔", 40.0f, "双击之间的间隔（秒）。");
                        doubleBlinkGap.floatValue = HoConstraintEditorControls.NumberField(doubleBlinkGap.floatValue);
                        HoConstraintEditorControls.Gap();
                        HoConstraintEditorControls.Label("种子", HoConstraintEditorTheme.LabelWidthSm, "随机种子，0 表示用实例 id。");
                        randomSeed.intValue = HoConstraintEditorControls.IntField(
                            HoConstraintEditorControls.Next(HoConstraintEditorTheme.FieldWidth),
                            randomSeed.intValue,
                            "随机种子，0 表示用实例 id。");
                    }

                    using (HoConstraintEditorControls.Row(true))
                    {
                        pauseWhenDriven.boolValue = HoConstraintEditorControls.Toggle(
                            "被驱动时暂停",
                            pauseWhenDriven.boolValue,
                            "面捕/动画已经在眨这个眼时，程序化眨眼让位。");
                        HoConstraintEditorControls.Gap();
                        using (new EditorGUI.DisabledScope(!pauseWhenDriven.boolValue))
                        {
                            HoConstraintEditorControls.Label("阈值", HoConstraintEditorTheme.LabelWidthSm, "基准值高于它就算被接管（键值 0–100）。");
                            pauseThreshold.floatValue = HoConstraintEditorControls.NumberField(pauseThreshold.floatValue);
                            HoConstraintEditorControls.Gap();
                            HoConstraintEditorControls.Label("暂停", HoConstraintEditorTheme.LabelWidthSm, "让位时长（秒）。");
                            pauseDuration.floatValue = HoConstraintEditorControls.NumberField(pauseDuration.floatValue);
                        }
                    }
                }
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 规则
        // ══════════════════════════════════════════════════════════════
        private void DrawRulesSection(HoBlinkConstraint constraint)
        {
            string summary = rules.arraySize + " 条 · 驱动别的键";
            if (!HoConstraintEditorControls.Section(ref rulesExpanded, "规则", summary, HoConstraintEditorTheme.AccentRules))
            {
                return;
            }

            using (HoConstraintEditorControls.Card())
            {
                // 一排按钮 = 一排预设：点一下就**追加**一份规则，不重建、不清空。
                using (HoConstraintEditorControls.Row())
                {
                    if (HoConstraintEditorControls.Button(
                        "＋ 跟眼",
                        "追加「果冻 X（往右 − 往左）」+「果冻 Y（上 − 下）」两条双极规则，驱动键从网格上自动抓。\n"
                        + "这是果冻眼的主路：高光 / 眼仁跟着视线甩。再点一次会再来一份。"))
                    {
                        HoBlinkPresetActions.ApplyGazeJelly(constraint, serializedObject);
                        serializedObject.Update();
                    }

                    HoConstraintEditorControls.Gap(4.0f);
                    if (HoConstraintEditorControls.Button(
                        "＋ 四向凝视",
                        "追加「凝视 上 / 下 / 左 / 右」四条单极规则：每个方向有独立键的模型用这个。目标键留空。"))
                    {
                        HoBlinkPresetActions.ApplyGazeRules(constraint, serializedObject, false);
                        serializedObject.Update();
                    }

                    HoConstraintEditorControls.Gap(4.0f);
                    if (HoConstraintEditorControls.Button(
                        "＋ 眨眼",
                        "追加眨眼路径：「眨眼压高光」（闭眼量驱动：压扁 / 拉宽 / 下移）+「眨眼速度弹」（眼皮速度驱动：眼仁弹一下）。\n"
                        + "不需要任何驱动键，果冻眼里最出效果的一路。"))
                    {
                        HoBlinkPresetActions.ApplyBlinkJelly(constraint, serializedObject);
                        serializedObject.Update();
                    }
                }

                using (HoConstraintEditorControls.Row())
                {
                    if (HoConstraintEditorControls.Button(
                        "＋ 左右眼凝视",
                        "追加「凝视 X（往右 − 往左）」+「凝视 Y（上 − 下）」两条双极规则，用分左右眼的凝视键。\n"
                        + "按模型上的族自动选：有 In/Out（ARKit / PICO）就用内外族，否则用相对头的 Left/Right。"))
                    {
                        HoBlinkPresetActions.ApplyEyeGazeRules(constraint, serializedObject);
                        serializedObject.Update();
                    }

                    HoConstraintEditorControls.Flex();
                    if (HoConstraintEditorControls.Button(
                        "按名字接目标键",
                        "把**键名还空着**的目标，按它自己的角色标签（如「高光 · 压扁」）去网格上找最像的键：\n"
                        + "高光/眼仁/眼皮 → 主体；压扁/拉宽 → 压缩类；位移/下移 → 移动类 + 方向。\n"
                        + "找不到就留空并在 Console 里说明，绝不硬填。"))
                    {
                        HoBlinkPresetActions.AutoMatchTargetKeys(constraint, serializedObject);
                        serializedObject.Update();
                    }
                }

                HoConstraintEditorControls.Separator();
                EnsureFoldoutCapacity();

                for (int i = 0; i < rules.arraySize; i++)
                {
                    DrawRule(constraint, i);
                }

                using (HoConstraintEditorControls.Row())
                {
                    HoConstraintEditorControls.Flex();
                    if (HoConstraintEditorControls.Button("＋ 规则"))
                    {
                        rules.InsertArrayElementAtIndex(rules.arraySize);
                        ruleFoldouts.Add(true);
                        ruleDetails.Add(false);
                    }
                }
            }
        }

        private void DrawRule(HoBlinkConstraint constraint, int index)
        {
            SerializedProperty rule = rules.GetArrayElementAtIndex(index);
            SerializedProperty label = rule.FindPropertyRelative("label");
            SerializedProperty enabled = rule.FindPropertyRelative("enabled");
            SerializedProperty driverKind = rule.FindPropertyRelative("driverKind");
            SerializedProperty positiveKey = rule.FindPropertyRelative("positiveKey");
            SerializedProperty negativeKey = rule.FindPropertyRelative("negativeKey");
            SerializedProperty driverRange = rule.FindPropertyRelative("driverRange");
            SerializedProperty invert = rule.FindPropertyRelative("invert");
            SerializedProperty readWritten = rule.FindPropertyRelative("readWrittenThisFrame");
            SerializedProperty jellyEnabled = rule.FindPropertyRelative("jellyEnabled");
            SerializedProperty frequency = rule.FindPropertyRelative("frequency");
            SerializedProperty dampingRatio = rule.FindPropertyRelative("dampingRatio");
            SerializedProperty inputSmoothing = rule.FindPropertyRelative("inputSmoothing");
            SerializedProperty maxStep = rule.FindPropertyRelative("maxStep");
            SerializedProperty targets = rule.FindPropertyRelative("targets");

            using (HoConstraintEditorControls.Card(true))
            {
                using (HoConstraintEditorControls.Row())
                {
                    ruleFoldouts[index] = HoConstraintEditorControls.InlineFoldout(ruleFoldouts[index], BuildRuleTitle(rule));
                    HoConstraintEditorControls.Flex();
                    if (HoConstraintEditorControls.IconButton("⋮", "上移 / 下移 / 删除"))
                    {
                        ShowRuleMenu(index);
                    }

                    HoConstraintEditorControls.Gap(3.0f);
                    enabled.boolValue = HoConstraintEditorControls.Toggle(
                        "启用",
                        enabled.boolValue,
                        "关掉这条规则：驱动归零，不再写它的目标。");
                }

                if (ruleFoldouts[index])
                {
                    using (HoConstraintEditorControls.Indent())
                    {
                        DrawRuleBody(constraint, index, driverKind, positiveKey, negativeKey, driverRange, invert, readWritten, jellyEnabled, frequency, dampingRatio, inputSmoothing, maxStep, targets);
                    }
                }
            }
        }

        private void DrawRuleBody(
            HoBlinkConstraint constraint,
            int index,
            SerializedProperty driverKind,
            SerializedProperty positiveKey,
            SerializedProperty negativeKey,
            SerializedProperty driverRange,
            SerializedProperty invert,
            SerializedProperty readWritten,
            SerializedProperty jellyEnabled,
            SerializedProperty frequency,
            SerializedProperty dampingRatio,
            SerializedProperty inputSmoothing,
            SerializedProperty maxStep,
            SerializedProperty targets)
        {
            using (HoConstraintEditorControls.Row())
            {
                HoConstraintEditorControls.Label("驱动", HoConstraintEditorTheme.LabelWidth, "这条规则的信号从哪来。");
                driverKind.enumValueIndex = Segment(driverKind, "形态键：读键值；眨眼：用自动眨眼当信号；手动：调试滑杆。");
                HoConstraintEditorControls.Gap();
                HoConstraintEditorControls.Label("值域", HoConstraintEditorTheme.LabelWidthSm, "单极 0..1，或双极 −1..1（右−左 这样的合成）。");
                driverRange.enumValueIndex = Segment(driverRange, "单极 0..1（上/下这类单键），双极 −1..1（右−左、上−下）。");
            }

            using (new EditorGUI.DisabledScope((HoBlinkDriverKind)driverKind.enumValueIndex != HoBlinkDriverKind.ShapeKey))
            using (HoConstraintEditorControls.Row())
            {
                HoConstraintEditorControls.Label("正", HoConstraintEditorTheme.LabelWidthXs, "值 > 0 的一侧读哪个键（单极就是它自己）。");
                DrawKeyField(constraint, positiveKey);

                HoConstraintEditorControls.Gap(4.0f);
                HoConstraintEditorControls.Label("负", HoConstraintEditorTheme.LabelWidthXs, "值 < 0 的一侧读哪个键；留空就是单极。");
                DrawKeyField(constraint, negativeKey);

                HoConstraintEditorControls.Gap(4.0f);
                HoConstraintEditorControls.MeterRow(
                    constraint.GetDriverValue(index),
                    (HoBlinkDriverRange)driverRange.enumValueIndex == HoBlinkDriverRange.Bipolar ? -1.0f : 0.0f,
                    1.0f,
                    HoConstraintEditorTheme.AccentDriver,
                    constraint.GetDriverValue(index).ToString("0.00"),
                    null,
                    constraint.GetDriverRawValue(index),
                    DriverMeterTooltip);
            }

            using (HoConstraintEditorControls.Row(true))
            {
                jellyEnabled.boolValue = HoConstraintEditorControls.Toggle(
                    "果冻",
                    jellyEnabled.boolValue,
                    "打开后驱动值过一遍弹簧-阻尼：才有超调与回弹。");
                HoConstraintEditorControls.Gap();
                using (new EditorGUI.DisabledScope(!jellyEnabled.boolValue))
                {
                    HoConstraintEditorControls.Label("频率", HoConstraintEditorTheme.LabelWidthSm, "弹簧频率 Hz：高光跟眼一般 3–6 Hz。");
                    frequency.floatValue = HoConstraintEditorControls.NumberField(frequency.floatValue);
                    HoConstraintEditorControls.Gap();
                    HoConstraintEditorControls.Label("阻尼", HoConstraintEditorTheme.LabelWidthSm, "0.2–0.35 有明显果冻感，1 是临界阻尼不超调。");
                    dampingRatio.floatValue = HoConstraintEditorControls.NumberField(dampingRatio.floatValue);
                    HoConstraintEditorControls.Gap();
                    HoConstraintEditorControls.Label("平滑", HoConstraintEditorTheme.LabelWidthSm, "输入平滑（秒）：压抖动。");
                    inputSmoothing.floatValue = HoConstraintEditorControls.NumberField(inputSmoothing.floatValue);
                }
            }

            using (HoConstraintEditorControls.Row(true))
            {
                ruleDetails[index] = HoConstraintEditorControls.InlineFoldout(ruleDetails[index], "细节", DetailLabel.tooltip);
            }

            if (ruleDetails[index])
            {
                using (HoConstraintEditorControls.Indent())
                using (HoConstraintEditorControls.Row(true))
                {
                    invert.boolValue = HoConstraintEditorControls.Toggle("反相", invert.boolValue, "读到的值取负。");
                    HoConstraintEditorControls.Gap();
                    readWritten.boolValue = HoConstraintEditorControls.Toggle(
                        "读本帧已写值",
                        readWritten.boolValue,
                        "默认读基准快照（切断自反馈）；要做链式联动才打开。");
                    HoConstraintEditorControls.Gap();
                    using (new EditorGUI.DisabledScope(!jellyEnabled.boolValue))
                    {
                        HoConstraintEditorControls.Label("子步", HoConstraintEditorTheme.LabelWidthSm, "子步上限（秒）：长帧时拆子步，防止弹簧炸掉。");
                        maxStep.floatValue = HoConstraintEditorControls.NumberField(maxStep.floatValue);
                    }
                }
            }

            using (HoConstraintEditorControls.Row())
            {
                HoConstraintEditorControls.Caption("目标 · 这条规则写哪些键");
                HoConstraintEditorControls.Flex();
                if (HoConstraintEditorControls.Button("＋ 目标"))
                {
                    AddTarget(targets);
                }
            }

            for (int t = 0; t < targets.arraySize; t++)
            {
                DrawTarget(targets, t, index);
            }
        }

        private void ShowRuleMenu(int index)
        {
            GenericMenu menu = new GenericMenu();
            if (index > 0)
            {
                menu.AddItem(new GUIContent("上移"), false, () => MoveRule(index, index - 1));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("上移"));
            }

            if (index < rules.arraySize - 1)
            {
                menu.AddItem(new GUIContent("下移"), false, () => MoveRule(index, index + 1));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("下移"));
            }

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("删除"), false, () =>
            {
                rules.DeleteArrayElementAtIndex(index);
                serializedObject.ApplyModifiedProperties();
                EnsureFoldoutCapacity();
                Repaint();
            });
            menu.ShowAsContext();
        }

        private void MoveRule(int from, int to)
        {
            rules.MoveArrayElement(from, to);
            serializedObject.ApplyModifiedProperties();
            Repaint();
        }

        private string BuildRuleTitle(SerializedProperty rule)
        {
            SerializedProperty label = rule.FindPropertyRelative("label");
            if (!string.IsNullOrEmpty(label.stringValue))
            {
                return label.stringValue;
            }

            SerializedProperty driverKind = rule.FindPropertyRelative("driverKind");
            SerializedProperty positiveKey = rule.FindPropertyRelative("positiveKey");
            SerializedProperty negativeKey = rule.FindPropertyRelative("negativeKey");

            HoBlinkDriverKind kind = (HoBlinkDriverKind)driverKind.enumValueIndex;
            if (kind == HoBlinkDriverKind.AutoBlink)
            {
                return "自动眨眼";
            }

            if (kind == HoBlinkDriverKind.Manual)
            {
                return "手动驱动";
            }

            string positive = string.IsNullOrEmpty(positiveKey.stringValue) ? "（没填驱动键）" : positiveKey.stringValue;
            return string.IsNullOrEmpty(negativeKey.stringValue) ? positive : positive + " − " + negativeKey.stringValue;
        }

        // ══════════════════════════════════════════════════════════════
        // 目标（眼睑输出 与 规则目标 共用）
        // ══════════════════════════════════════════════════════════════
        private void DrawTarget(SerializedProperty list, int index, int ruleIndex)
        {
            SerializedProperty target = list.GetArrayElementAtIndex(index);
            SerializedProperty targetLabel = target.FindPropertyRelative("label");
            SerializedProperty meshScope = target.FindPropertyRelative("meshScope");
            SerializedProperty meshIndex = target.FindPropertyRelative("meshIndex");
            SerializedProperty keyName = target.FindPropertyRelative("keyName");
            SerializedProperty side = target.FindPropertyRelative("side");
            SerializedProperty blendMode = target.FindPropertyRelative("blendMode");
            SerializedProperty weight = target.FindPropertyRelative("weight");
            SerializedProperty gain = target.FindPropertyRelative("gain");
            SerializedProperty offset = target.FindPropertyRelative("offset");
            SerializedProperty outputMin = target.FindPropertyRelative("outputMin");
            SerializedProperty outputMax = target.FindPropertyRelative("outputMax");
            SerializedProperty clampToRange = target.FindPropertyRelative("clampToRange");
            SerializedProperty rampPreset = target.FindPropertyRelative("rampPreset");
            SerializedProperty rampIntensity = target.FindPropertyRelative("rampIntensity");
            SerializedProperty rampCurve = target.FindPropertyRelative("rampCurve");
            SerializedProperty rampAttack = target.FindPropertyRelative("rampAttack");
            SerializedProperty rampRelease = target.FindPropertyRelative("rampRelease");

            int detailIndex = DetailIndex(ruleIndex, index);
            while (targetDetails.Count <= detailIndex)
            {
                targetDetails.Add(false);
            }

            HoBlinkConstraint constraint = (HoBlinkConstraint)target.serializedObject.targetObject;
            float value = ruleIndex < 0
                ? constraint.GetBlinkTargetOutput(index)
                : constraint.GetRuleTargetOutput(ruleIndex, index);
            float min = Mathf.Min(outputMin.floatValue, outputMax.floatValue);
            float max = Mathf.Max(outputMin.floatValue, outputMax.floatValue);
            if (Mathf.Approximately(min, max))
            {
                max = min + 1.0f;
            }

            using (HoConstraintEditorControls.Card())
            {
                using (HoConstraintEditorControls.Row())
                {
                    // 有角色标签就用它当这一行的标签（「高光 · 压扁 [键] …」），省掉一行说明
                    bool hasRole = !string.IsNullOrEmpty(targetLabel.stringValue);
                    HoConstraintEditorControls.Label(
                        hasRole ? targetLabel.stringValue : "键",
                        hasRole ? 82.0f : HoConstraintEditorTheme.LabelWidthXs,
                        hasRole ? "这一路的作用（在「细节」里可以改）；右边是它写的键。" : KeyLabel.tooltip);
                    DrawKeyField(constraint, keyName);
                    HoConstraintEditorControls.Gap(4.0f);
                    HoConstraintEditorControls.MeterRow(
                        value,
                        min,
                        max,
                        HoConstraintEditorTheme.AccentOutput,
                        value.ToString("0.0"),
                        null,
                        float.NaN,
                        OutputMeterTooltip + "\n当前 " + value.ToString("0.##") + " / 范围 " + min.ToString("0.#") + "–" + max.ToString("0.#"));

                    HoConstraintEditorControls.Gap(2.0f);
                    targetDetails[detailIndex] = HoConstraintEditorControls.InlineFoldout(targetDetails[detailIndex], "细节", DetailLabel.tooltip);
                    if (HoConstraintEditorControls.IconButton("✕", "删掉这个目标"))
                    {
                        list.DeleteArrayElementAtIndex(index);
                        return;
                    }
                }

                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Label("范围", HoConstraintEditorTheme.LabelWidthSm, "这个目标写到哪些网格。");
                    meshScope.enumValueIndex = Segment(meshScope, "全部网格，或只写指定的那一个。");
                    using (new EditorGUI.DisabledScope((HoShapeKeyMeshScope)meshScope.enumValueIndex != HoShapeKeyMeshScope.Index))
                    {
                        HoConstraintEditorControls.Gap();
                        HoConstraintEditorControls.Label("序号", HoConstraintEditorTheme.LabelWidthSm, "范围 = 指定网格时用。");
                        meshIndex.intValue = HoConstraintEditorControls.IntField(
                            HoConstraintEditorControls.Next(HoConstraintEditorTheme.FieldWidth),
                            meshIndex.intValue);
                    }

                    HoConstraintEditorControls.Gap();
                    HoConstraintEditorControls.Label("混合", HoConstraintEditorTheme.LabelWidthSm, "叠加：在动画/面捕的基准上加；覆盖：直接顶掉基准。");
                    blendMode.enumValueIndex = Segment(blendMode, "叠加：在动画/面捕的基准上加；覆盖：直接顶掉基准。");

                    HoConstraintEditorControls.Gap();
                    HoConstraintEditorControls.Label("增益", HoConstraintEditorTheme.LabelWidthSm, "归一化：1.0 约等于满量程 100 键值，可为负。");
                    gain.floatValue = HoConstraintEditorControls.NumberField(gain.floatValue);
                }

                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Label("ramp", HoConstraintEditorTheme.LabelWidthSm, "这一路的成形方式：预设 + 强度；动态超调由规则弹簧负责。");
                    rampPreset.enumValueIndex = RampDropdown(rampPreset.enumValueIndex);
                    HoConstraintEditorControls.Gap();
                    HoConstraintEditorControls.Label("强度", HoConstraintEditorTheme.LabelWidthSm, "按预设语义缩放（倍率 / 时间常数 / 超调）。");
                    rampIntensity.floatValue = HoConstraintEditorControls.NumberField(rampIntensity.floatValue);
                    HoConstraintEditorControls.Gap();
                    HoConstraintEditorControls.Label("权重", HoConstraintEditorTheme.LabelWidthSm, "这一路输出的总强度（0..1），双击回 1。");
                    weight.floatValue = HoConstraintEditorControls.MiniSlider(weight.floatValue, 0.0f, 1.0f, 1.0f, "这一路输出的总强度（0..1）；双击回 1。");
                }

                if (targetDetails[detailIndex])
                {
                    DrawTargetDetails(targetLabel, rampPreset, rampCurve, rampAttack, rampRelease, side, offset, clampToRange, outputMin, outputMax);
                }
            }
        }

        /// <summary>目标的「细节」区（不常动的项）。</summary>
        private void DrawTargetDetails(
            SerializedProperty targetLabel,
            SerializedProperty rampPreset,
            SerializedProperty rampCurve,
            SerializedProperty rampAttack,
            SerializedProperty rampRelease,
            SerializedProperty side,
            SerializedProperty offset,
            SerializedProperty clampToRange,
            SerializedProperty outputMin,
            SerializedProperty outputMax)
        {
            using (HoConstraintEditorControls.Indent())
            {
                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Label("角色", HoConstraintEditorTheme.LabelWidthSm, "这一路是干什么的：显示成目标行的标签，也决定「按名字接目标键」去找哪种键。");
                    targetLabel.stringValue = EditorGUI.TextField(
                        HoConstraintEditorControls.NextFlexible(80.0f),
                        targetLabel.stringValue,
                        HoConstraintEditorTheme.Field);
                }

                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Label("通道", HoConstraintEditorTheme.LabelWidthSm, "自动眨眼输出的左右通道；正常眨眼两者相同。");
                    side.enumValueIndex = Segment(side, "左右通道；正常眨眼两者相同。");
                    HoConstraintEditorControls.Gap();
                    HoConstraintEditorControls.Label("偏移", HoConstraintEditorTheme.LabelWidthSm, "输出加一个常量偏置（键值 0–100）。");
                    offset.floatValue = HoConstraintEditorControls.NumberField(offset.floatValue);
                    HoConstraintEditorControls.Gap();
                    clampToRange.boolValue = HoConstraintEditorControls.Toggle("钳制", clampToRange.boolValue, "把输出夹在下面的范围里。");
                }

                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Label("输出下限", 56.0f, "输出下限（键值 0–100）。");
                    outputMin.floatValue = HoConstraintEditorControls.NumberField(outputMin.floatValue);
                    HoConstraintEditorControls.Gap();
                    HoConstraintEditorControls.Label("上限", HoConstraintEditorTheme.LabelWidthSm, "输出上限（键值 0–100）。");
                    outputMax.floatValue = HoConstraintEditorControls.NumberField(outputMax.floatValue);
                }

                if ((HoShapeKeyRampPreset)rampPreset.enumValueIndex == HoShapeKeyRampPreset.Custom)
                {
                    EditorGUILayout.PropertyField(rampCurve, new GUIContent("曲线", "自定义 ramp：驱动值 → 输出值（允许 > 1）。"));
                    using (HoConstraintEditorControls.Row(true))
                    {
                        HoConstraintEditorControls.Label("attack", 40.0f, "上升时间常数（秒）。");
                        rampAttack.floatValue = HoConstraintEditorControls.NumberField(rampAttack.floatValue);
                        HoConstraintEditorControls.Gap();
                        HoConstraintEditorControls.Label("release", 46.0f, "回落时间常数（秒）。");
                        rampRelease.floatValue = HoConstraintEditorControls.NumberField(rampRelease.floatValue);
                    }
                }
            }
        }

        /// <summary>
        /// 加一个空目标。`InsertArrayElementAtIndex` 会**复制上一个元素**，
        /// 所以角色标签和键名必须显式清掉，否则新目标会悄悄接着写上一个键。
        /// </summary>
        private void AddTarget(SerializedProperty list)
        {
            list.InsertArrayElementAtIndex(list.arraySize);
            SerializedProperty target = list.GetArrayElementAtIndex(list.arraySize - 1);
            target.FindPropertyRelative("label").stringValue = string.Empty;
            target.FindPropertyRelative("keyName").stringValue = string.Empty;
            targetDetails.Add(false);
        }

        /// <summary>键名格：文本 + ▾ 菜单 + 解析状态点。</summary>
        private void DrawKeyField(HoBlinkConstraint constraint, SerializedProperty keyName)
        {
            string edited = HoConstraintEditorControls.KeyField(
                keyName.stringValue,
                string.IsNullOrEmpty(keyName.stringValue) ? 0 : constraint.CountKeyBindings(keyName.stringValue),
                string.IsNullOrEmpty(keyName.stringValue)
                    ? "还没填键名"
                    : "已写在：" + constraint.DescribeKeyBindings(keyName.stringValue),
                "从内置键名表或当前网格上实际存在的键里选",
                out Rect dropdownRect,
                out bool clicked);

            if (edited != keyName.stringValue)
            {
                keyName.stringValue = edited;
            }

            if (clicked)
            {
                HoKeyNameDropdown.Show(dropdownRect, constraint, keyName);
            }
        }

        private int RampDropdown(int enumValueIndex)
        {
            int current = 0;
            for (int i = 0; i < RampPresetValues.Length; i++)
            {
                if (RampPresetValues[i] == enumValueIndex)
                {
                    current = i;
                    break;
                }
            }

            int selected = HoConstraintEditorControls.Dropdown(
                HoConstraintEditorControls.Next(78.0f),
                current,
                RampPresetNames,
                "这一路的成形方式；动态超调由规则弹簧负责。");
            return RampPresetValues[selected];
        }

        /// <summary>枚举用分段胶囊（选项 ≤ 4 时）或下拉；返回写入 enumValueIndex 的值。</summary>
        private static int Segment(SerializedProperty property, string tooltip = null)
        {
            string[] options = property.enumDisplayNames;
            float width = HoConstraintEditorControls.SegmentedWidth(options);
            return HoConstraintEditorControls.EnumControl(
                HoConstraintEditorControls.Next(width),
                property.enumValueIndex,
                options,
                true,
                tooltip);
        }

        // ══════════════════════════════════════════════════════════════
        // 调试
        // ══════════════════════════════════════════════════════════════
        private void DrawDebugSection(HoBlinkConstraint constraint)
        {
            string summary = "网格 " + constraint.MeshCount + " · 规则 " + constraint.RuleCount;
            if (!HoConstraintEditorControls.Section(ref debugExpanded, "调试", summary, HoConstraintEditorTheme.AccentDebug))
            {
                return;
            }

            using (HoConstraintEditorControls.Card())
            {
                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.MeterRow(
                        constraint.BlinkValue,
                        0.0f,
                        1.0f,
                        HoConstraintEditorTheme.AccentBlink,
                        constraint.BlinkValue.ToString("0.00"),
                        "闭眼",
                        float.NaN,
                        "自动眨眼算出来的闭眼量（0 = 睁开，1 = 闭合）。");
                    HoConstraintEditorControls.Gap(4.0f);
                    HoConstraintEditorControls.MeterRow(
                        constraint.BlinkSpeed,
                        0.0f,
                        1.0f,
                        HoConstraintEditorTheme.AccentDriver,
                        constraint.BlinkSpeed.ToString("0.00"),
                        "速度",
                        float.NaN,
                        "眼皮动的速度：只在闭合/张开那几帧有值，停住时为 0。");
                    HoConstraintEditorControls.Gap();
                    HoConstraintEditorControls.Caption(BlinkPhaseText(constraint.BlinkPhase));
                }

                using (HoConstraintEditorControls.Row(true))
                {
                    HoConstraintEditorControls.Flex();
                    if (HoConstraintEditorControls.Button("眨一次"))
                    {
                        constraint.TriggerBlink();
                    }

                    HoConstraintEditorControls.Gap(4.0f);
                    if (HoConstraintEditorControls.Button("wink 左"))
                    {
                        constraint.TriggerBlink(1.0f, 0.0f, 0.35f);
                    }

                    HoConstraintEditorControls.Gap(4.0f);
                    if (HoConstraintEditorControls.Button("重置"))
                    {
                        constraint.ResetState();
                    }

                    HoConstraintEditorControls.Gap(4.0f);
                    if (HoConstraintEditorControls.Button("清空", "删掉全部眼睑键与规则。"))
                    {
                        HoBlinkPresetActions.ClearAll(serializedObject);
                        serializedObject.Update();
                    }
                }

                HoConstraintEditorControls.Separator(3.0f, 3.0f);

                using (HoConstraintEditorControls.Row(true))
                {
                    manualDrive = HoConstraintEditorControls.Toggle(
                        "手动驱动",
                        manualDrive,
                        "忽略真实键值，用滑杆直接调每条规则，看输出怎么走。调试用，不进序列化。");
                    constraint.ManualDriveEnabled = manualDrive;
                    HoConstraintEditorControls.Gap();
                    if (!Application.isPlaying && !evaluateInEditMode.boolValue)
                    {
                        HoConstraintEditorControls.Caption("不播放时读数不动，除非打开「编辑模式求值」");
                    }
                }

                for (int i = 0; i < constraint.RuleCount; i++)
                {
                    HoBlinkRule rule = constraint.GetRule(i);
                    if (rule == null)
                    {
                        continue;
                    }

                    bool bipolar = rule.DriverRange == HoBlinkDriverRange.Bipolar;
                    float raw = constraint.GetDriverRawValue(i);
                    float value = constraint.GetDriverValue(i);

                    using (HoConstraintEditorControls.Row(true))
                    {
                        HoConstraintEditorControls.Label(Trim(rule.Label, "规则 " + i), HoConstraintEditorTheme.LabelWidth, "驱动值：白刻度 = 原始，色条 = 进目标。");
                        HoConstraintEditorControls.MeterRow(
                            value,
                            bipolar ? -1.0f : 0.0f,
                            1.0f,
                            HoConstraintEditorTheme.AccentDriver,
                            value.ToString("0.00"),
                            null,
                            raw,
                            "原始 " + raw.ToString("0.###") + " → 进目标 " + value.ToString("0.###"));

                        if (manualDrive)
                        {
                            HoConstraintEditorControls.Gap();
                            float slider = HoConstraintEditorControls.MiniSlider(
                                value,
                                bipolar ? -1.0f : 0.0f,
                                1.0f,
                                0.0f,
                                "手动给这条规则一个驱动值。");
                            if (!Mathf.Approximately(slider, value))
                            {
                                constraint.SetManualDriverValue(i, slider);
                            }
                        }
                    }

                    for (int t = 0; t < constraint.GetRuleTargetCount(i) && rule.Targets != null && t < rule.Targets.Count; t++)
                    {
                        HoShapeKeyTarget target = rule.Targets[t];
                        if (target == null)
                        {
                            continue;
                        }

                        float output = constraint.GetRuleTargetOutput(i, t);
                        float min = Mathf.Min(target.OutputMin, target.OutputMax);
                        float max = Mathf.Max(target.OutputMin, target.OutputMax);
                        if (Mathf.Approximately(min, max))
                        {
                            max = min + 1.0f;
                        }

                        using (HoConstraintEditorControls.Row(true))
                        {
                            HoConstraintEditorControls.Label(
                                string.IsNullOrEmpty(target.KeyName) ? "（没填键）" : target.KeyName,
                                84.0f,
                                "这条目标的最终键值。");
                            HoConstraintEditorControls.MeterRow(
                                output,
                                min,
                                max,
                                HoConstraintEditorTheme.AccentOutput,
                                output.ToString("0.0"),
                                null,
                                float.NaN,
                                "最终键值（" + min.ToString("0.#") + "–" + max.ToString("0.#") + "）。");
                        }
                    }
                }

                if (constraint.BlinkOutputCount > 0)
                {
                    HoConstraintEditorControls.Separator(3.0f, 3.0f);
                    for (int i = 0; i < constraint.BlinkOutputCount; i++)
                    {
                        float output = constraint.GetBlinkTargetOutput(i);
                        using (HoConstraintEditorControls.Row(true))
                        {
                            HoConstraintEditorControls.Label(constraint.GetBlinkTargetKeyName(i), 84.0f, "自动眨眼写在眼睑键上的值（0–100）。");
                            HoConstraintEditorControls.MeterRow(
                                output,
                                0.0f,
                                100.0f,
                                HoConstraintEditorTheme.AccentBlink,
                                output.ToString("0.0"),
                                null,
                                float.NaN,
                                "自动眨眼写在眼睑键上的值（0–100）。");
                        }
                    }
                }

                constraint.CollectSaturatedKeys(saturationBuffer);
                if (saturationBuffer.Count > 0)
                {
                    HoConstraintEditorControls.Separator(3.0f, 3.0f);
                    HoConstraintEditorSectionGui.DrawSaturationReport(saturationBuffer);
                }

                if (constraint.MissingKeys.Count > 0)
                {
                    EditorGUILayout.HelpBox("缺失键 " + constraint.MissingKeys.Count + " 个，见「目标网格」区。", MessageType.Warning);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════
        private void EnsureFoldoutCapacity()
        {
            while (ruleFoldouts.Count < rules.arraySize)
            {
                ruleFoldouts.Add(true);
            }

            while (ruleDetails.Count < rules.arraySize)
            {
                ruleDetails.Add(false);
            }

            while (ruleFoldouts.Count > rules.arraySize)
            {
                ruleFoldouts.RemoveAt(ruleFoldouts.Count - 1);
            }

            while (ruleDetails.Count > rules.arraySize)
            {
                ruleDetails.RemoveAt(ruleDetails.Count - 1);
            }
        }

        private static int DetailIndex(int ruleIndex, int targetIndex)
        {
            // 眼睑输出（ruleIndex < 0）与规则目标分开占号段，免得折叠状态互相串
            return ruleIndex < 0 ? 100000 + targetIndex : (ruleIndex * 1000) + targetIndex;
        }

        private static string BlinkPhaseText(HoBlinkPhase phase)
        {
            switch (phase)
            {
                case HoBlinkPhase.Closing:
                    return "相位：闭合中";
                case HoBlinkPhase.Hold:
                    return "相位：保持";
                case HoBlinkPhase.Opening:
                    return "相位：张开中";
                default:
                    return "相位：等待下一次眨眼";
            }
        }

        private static string Trim(string text, string fallback)
        {
            if (string.IsNullOrEmpty(text))
            {
                return fallback;
            }

            return text.Length <= 8 ? text : text.Substring(0, 8) + "…";
        }
    }
}
