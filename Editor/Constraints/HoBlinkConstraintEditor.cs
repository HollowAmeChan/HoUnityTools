using System.Collections.Generic;
using Hollow.HoUnityTools.Constraints;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Constraints
{
    [CustomEditor(typeof(HoBlinkConstraint))]
    internal sealed class HoBlinkConstraintEditor : UnityEditor.Editor
    {
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

        private bool meshExpanded;
        private bool blinkExpanded;
        private bool rulesExpanded;
        private bool debugExpanded;
        private bool manualDrive;

        private readonly List<bool> ruleFoldouts = new List<bool>();
        private readonly List<bool> targetDetails = new List<bool>();
        private readonly List<HoShapeKeySaturation> saturationBuffer = new List<HoShapeKeySaturation>();

        private static readonly Color MeshColor = new Color(0.28f, 0.62f, 1.0f);
        private static readonly Color BlinkColor = new Color(0.24f, 0.86f, 0.58f);
        private static readonly Color RulesColor = new Color(0.78f, 0.48f, 1.0f);
        private static readonly Color DebugColor = new Color(0.70f, 0.72f, 0.76f);

        private static readonly GUIContent RenderersLabel = new GUIContent("目标网格", "支持任意 Renderer，运行时解析出 SkinnedMeshRenderer。");
        private static readonly GUIContent UpdateModeLabel = new GUIContent("更新时机", "写在动画之后才能叠加动画；默认 LateUpdate。");
        private static readonly GUIContent EvaluateInEditModeLabel = new GUIContent("编辑模式求值", "编辑模式也写入形态键（会把场景标脏，一般用调试区的手动驱动预览）。");
        private static readonly GUIContent WriteThresholdLabel = new GUIContent("写入阈值", "变化小于该值就不写，减少 mesh dirty。");
        private static readonly GUIContent WritingEnabledLabel = new GUIContent("允许写入");
        private static readonly GUIContent KeyLabel = new GUIContent("键");
        private static readonly GUIContent GainLabel = new GUIContent("增益", "归一化：1.0 约等于满量程 100 键值，可为负。");
        private static readonly GUIContent RampLabel = new GUIContent("ramp", "这一路的成形方式：预设 + 强度。动态超调由规则弹簧负责。");
        private static readonly GUIContent IntensityLabel = new GUIContent("强度", "按预设语义缩放（倍率 / 时间常数 / 超调）。");
        private static readonly GUIContent SideLabel = new GUIContent("通道", "自动眨眼输出的左右通道；正常眨眼两者相同。");
        private static readonly GUIContent DriverKindLabel = new GUIContent("驱动", "这条规则的信号从哪来。");
        private static readonly GUIContent DriverRangeLabel = new GUIContent("值域", "单极 0..1（上/下这类单键），或双极 −1..1（右−左、上−下这样的合成）。");
        private static readonly GUIContent PositiveKeyLabel = new GUIContent("正", "值 > 0 的一侧读哪个键（单极就是它自己）。");
        private static readonly GUIContent NegativeKeyLabel = new GUIContent("负", "值 < 0 的一侧读哪个键；留空就是单极。");
        private static readonly GUIContent InvertLabel = new GUIContent("反相", "读到的值取负。");
        private static readonly GUIContent ReadWrittenLabel = new GUIContent("读本帧已写值", "默认读基准快照（切断自反馈）；要做链式联动才打开。");
        private static readonly GUIContent JellyLabel = new GUIContent("果冻弹簧", "打开后驱动值过一遍弹簧-阻尼：才有超调与回弹。");
        private static readonly GUIContent FrequencyLabel = new GUIContent("频率 Hz", "跟得上多快；高光跟眼一般 3–6 Hz。");
        private static readonly GUIContent DampingLabel = new GUIContent("阻尼比", "0.2–0.35 有明显果冻感，1 是临界阻尼不超调。");
        private static readonly GUIContent MaxStepLabel = new GUIContent("子步上限 s", "长帧时拆子步，防止弹簧炸掉。");
        private static readonly GUIContent SmoothingLabel = new GUIContent("输入平滑 s", "先把驱动抹平一点再进弹簧，压抖动。");
        private static readonly GUIContent MeshScopeLabel = new GUIContent("范围", "这个目标写到哪些网格。");
        private static readonly GUIContent MeshIndexLabel = new GUIContent("序号", "范围 = 指定网格时用。");
        private static readonly GUIContent BlendModeLabel = new GUIContent("混合", "叠加：在动画/面捕的基准上加；覆盖：直接顶掉基准。");
        private static readonly GUIContent WeightLabel = new GUIContent("权重", "这一路输出的总强度（0..1）。");

        private const string DriverMeterTooltip =
            "实时：白刻度 = 键上的原始值（弹簧/平滑之前），色条 = 真正拿去驱动目标的值。";

        private const string OutputMeterTooltip =
            "最终写进键的值：驱动 → 增益 → ramp 预设 → 强度 → 权重 → 偏移，再钳制到输出范围。";

        private static readonly Color DriverColor = new Color(0.36f, 0.70f, 0.98f);
        private static readonly Color OutputColor = new Color(0.42f, 0.84f, 0.58f);

        private static readonly GUIContent[] RampPresetLabels =
        {
            new GUIContent("直通"),
            new GUIContent("柔跟"),
            new GUIContent("放大"),
            new GUIContent("缓入"),
            new GUIContent("慢放"),
            new GUIContent("阶梯"),
            new GUIContent("自定义")
        };

        private static readonly int[] RampPresetValues = { 0, 1, 2, 3, 4, 5, 6 };

        /// <summary>规则的果冻/映射档位（只影响规则列表，眼睑键归「自动眨眼」区）。</summary>
        private enum HoBlinkRuleTier
        {
            GazeJelly = 0,
            GazeJellyFourWay = 1,
            Full = 2
        }

        private HoBlinkRuleTier ruleTier = HoBlinkRuleTier.GazeJelly;

        private static readonly GUIContent[] RuleTierLabels =
        {
            new GUIContent("跟眼（X / Y 两条）", "果冻 X（往右−往左）、果冻 Y（上−下）两条双极规则，目标键留空由你填。"),
            new GUIContent("跟眼 + 四向", "再加四条单极凝视规则（上/下/左/右），适合每个方向有独立键的模型。"),
            new GUIContent("全套", "再加一条「眨眼压高光」（驱动 = 自动眨眼）。")
        };

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
            DrawTitle();
            EditorGUILayout.Space(4.0f);

            DrawMeshSection(constraint);
            DrawBlinkSection();
            DrawRulesSection();
            DrawDebugSection(constraint);

            serializedObject.ApplyModifiedProperties();

            // 横条要动起来：播放中、或手动驱动预览时，让面板每帧重画一次。
            if (Application.isPlaying || manualDrive)
            {
                Repaint();
            }
        }

        private void DrawTitle()
        {
            EditorGUILayout.LabelField("Ho 眨眼约束", EditorStyles.boldLabel);
        }

        private void DrawMeshSection(HoBlinkConstraint constraint)
        {
            string summary = constraint.MeshCount + " 个蒙皮网格 / " + constraint.BindingCount + " 个绑定";
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref meshExpanded, "目标网格", summary, MeshColor))
            {
                return;
            }

            EditorGUILayout.PropertyField(renderers, RenderersLabel, true);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("收集子级网格"))
            {
                Undo.RecordObject(constraint, "收集眨眼约束网格");
                constraint.CollectChildRenderers();
                EditorUtility.SetDirty(constraint);
                serializedObject.Update();
            }

            if (GUILayout.Button("重新解析键"))
            {
                HoBlinkPresetActions.Rebuild(constraint);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.PropertyField(updateMode, UpdateModeLabel);
            EditorGUILayout.PropertyField(evaluateInEditMode, EvaluateInEditModeLabel);
            EditorGUILayout.PropertyField(writingEnabled, WritingEnabledLabel);
            EditorGUILayout.PropertyField(writeThreshold, WriteThresholdLabel);
            EditorGUILayout.PropertyField(mergeMode, HoConstraintEditorSectionGui.MergeModeLabel);

            if (constraint.MissingKeys.Count > 0)
            {
                string text = "以下键在目标网格上不存在：" + string.Join("、", constraint.MissingKeys);
                EditorGUILayout.HelpBox(text, MessageType.Warning);
            }
        }

        private void DrawBlinkSection()
        {
            string summary = blinkEnabled.boolValue ? "开" : "关";
            if (blinkTargets.arraySize > 0)
            {
                string firstKey = blinkTargets.GetArrayElementAtIndex(0).FindPropertyRelative("keyName").stringValue;
                summary += " · " + (string.IsNullOrEmpty(firstKey) ? "（键未填）" : firstKey)
                    + (blinkTargets.arraySize > 1 ? " 等 " + blinkTargets.arraySize + " 个键" : string.Empty);
            }

            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref blinkExpanded, "自动眨眼", summary, BlinkColor))
            {
                return;
            }

            EditorGUILayout.LabelField("这里配眨眼本身写的键（眼睑）；「规则」区是用信号驱动别的键，不写眼睑。", EditorStyles.miniLabel);
            if (GUILayout.Button("自动匹配眼睑键", GUILayout.Height(20.0f)))
            {
                HoBlinkPresetActions.AutoMatchEyelidKeys((HoBlinkConstraint)target, serializedObject);
                serializedObject.Update();
            }

            EditorGUILayout.PropertyField(blinkEnabled, new GUIContent("启用"));
            using (new EditorGUI.DisabledScope(!blinkEnabled.boolValue))
            {
                EditorGUILayout.PropertyField(intervalDistribution, new GUIContent("间隔分布"));
                EditorGUILayout.PropertyField(intervalMin, new GUIContent("间隔下限（秒）"));
                EditorGUILayout.PropertyField(intervalMax, new GUIContent("间隔上限（秒）"));
                EditorGUILayout.PropertyField(closeDuration, new GUIContent("闭合时长（秒）"));
                EditorGUILayout.PropertyField(holdDuration, new GUIContent("保持时长（秒）"));
                EditorGUILayout.PropertyField(openDuration, new GUIContent("张开时长（秒）"));
                EditorGUILayout.PropertyField(blinkCurve, new GUIContent("眨眼曲线", "横轴为相位，纵轴为闭合量。"));
                EditorGUILayout.PropertyField(strength, new GUIContent("强度"));
                EditorGUILayout.PropertyField(doubleBlinkChance, new GUIContent("双击概率"));
                EditorGUILayout.PropertyField(doubleBlinkGap, new GUIContent("双击间隔（秒）"));
                EditorGUILayout.PropertyField(randomSeed, new GUIContent("随机种子", "0 表示用实例 id。"));
                EditorGUILayout.PropertyField(pauseWhenDriven, new GUIContent("被驱动时暂停", "面捕/动画在眨眼时让位。"));
                using (new EditorGUI.DisabledScope(!pauseWhenDriven.boolValue))
                {
                    EditorGUILayout.PropertyField(pauseThreshold, new GUIContent("接管阈值"));
                    EditorGUILayout.PropertyField(pauseDuration, new GUIContent("暂停时长（秒）"));
                }
            }

            EditorGUILayout.Space(3.0f);
            EditorGUILayout.LabelField("眨眼输出", EditorStyles.boldLabel);
            DrawTargetList(blinkTargets, -1);
        }

        private void DrawRulesSection()
        {
            string summary = rules.arraySize + " 条规则";
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref rulesExpanded, "规则", summary, RulesColor))
            {
                return;
            }

            EditorGUILayout.BeginHorizontal();
            using (HoConstraintEditorSectionGui.NarrowLabels(34.0f))
            {
                ruleTier = (HoBlinkRuleTier)EditorGUILayout.Popup(
                    new GUIContent("档位", "只重建下面的规则列表，不动「自动眨眼」的眼睑键。目标键一律留空。"),
                    (int)ruleTier,
                    RuleTierLabels);
            }

            if (GUILayout.Button("应用档位", GUILayout.Width(72.0f)))
            {
                HoBlinkPresetActions.ApplyRuleTier((HoBlinkConstraint)target, serializedObject, (int)ruleTier);
                serializedObject.Update();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(2.0f);

            EnsureFoldoutCapacity();

            for (int i = 0; i < rules.arraySize; i++)
            {
                DrawRule(i);
            }

            EditorGUILayout.Space(2.0f);
            if (GUILayout.Button("+ 规则"))
            {
                rules.InsertArrayElementAtIndex(rules.arraySize);
                ruleFoldouts.Add(false);
            }
        }

        private void DrawRule(int index)
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

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            ruleFoldouts[index] = EditorGUILayout.Foldout(ruleFoldouts[index], BuildRuleTitle(rule), true);
            enabled.boolValue = EditorGUILayout.ToggleLeft("启用", enabled.boolValue, GUILayout.Width(52.0f));
            if (GUILayout.Button("↑", GUILayout.Width(22.0f)) && index > 0)
            {
                rules.MoveArrayElement(index, index - 1);
            }

            if (GUILayout.Button("↓", GUILayout.Width(22.0f)) && index < rules.arraySize - 1)
            {
                rules.MoveArrayElement(index, index + 1);
            }

            if (GUILayout.Button("✕", GUILayout.Width(22.0f)))
            {
                rules.DeleteArrayElementAtIndex(index);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.EndHorizontal();

            if (!ruleFoldouts[index])
            {
                EditorGUILayout.EndVertical();
                return;
            }

            HoBlinkConstraint constraint = (HoBlinkConstraint)target;
            EditorGUI.indentLevel++;

            // ── 驱动源：一行两个，别把下拉挤成一条缝
            EditorGUILayout.BeginHorizontal();
            using (HoConstraintEditorSectionGui.NarrowLabels(34.0f))
            {
                EditorGUILayout.PropertyField(driverKind, DriverKindLabel);
                EditorGUILayout.PropertyField(driverRange, DriverRangeLabel);
            }

            EditorGUILayout.EndHorizontal();

            // ── 正 / 负键：并排一行（单极只填「正」）
            using (new EditorGUI.DisabledScope((HoBlinkDriverKind)driverKind.enumValueIndex != HoBlinkDriverKind.ShapeKey))
            {
                EditorGUILayout.BeginHorizontal();
                DrawKeyCell(PositiveKeyLabel, positiveKey, constraint);
                DrawKeyCell(NegativeKeyLabel, negativeKey, constraint);
                EditorGUILayout.EndHorizontal();
            }

            // ── 实时横条：白刻度是弹簧前的原始值，色条是真正驱动目标的值
            DrawDriverMeter(constraint, index, driverRange);

            EditorGUILayout.Space(2.0f);
            EditorGUILayout.BeginHorizontal();
            invert.boolValue = EditorGUILayout.ToggleLeft(InvertLabel, invert.boolValue, GUILayout.Width(58.0f));
            readWritten.boolValue = EditorGUILayout.ToggleLeft(ReadWrittenLabel, readWritten.boolValue);
            EditorGUILayout.EndHorizontal();

            HoConstraintMeterGui.DrawSeparator();

            // ── 果冻弹簧
            EditorGUILayout.BeginHorizontal();
            jellyEnabled.boolValue = EditorGUILayout.ToggleLeft(JellyLabel, jellyEnabled.boolValue, GUILayout.Width(84.0f));
            using (new EditorGUI.DisabledScope(!jellyEnabled.boolValue))
            using (HoConstraintEditorSectionGui.NarrowLabels(50.0f))
            {
                frequency.floatValue = EditorGUILayout.FloatField(FrequencyLabel, frequency.floatValue);
            }

            EditorGUILayout.EndHorizontal();

            using (new EditorGUI.DisabledScope(!jellyEnabled.boolValue))
            using (HoConstraintEditorSectionGui.NarrowLabels(50.0f))
            {
                EditorGUILayout.BeginHorizontal();
                dampingRatio.floatValue = EditorGUILayout.FloatField(DampingLabel, dampingRatio.floatValue);
                maxStep.floatValue = EditorGUILayout.FloatField(MaxStepLabel, maxStep.floatValue);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                inputSmoothing.floatValue = EditorGUILayout.FloatField(SmoothingLabel, inputSmoothing.floatValue);
                EditorGUILayout.EndHorizontal();
            }

            HoConstraintMeterGui.DrawSeparator();

            EditorGUILayout.LabelField("目标 · 这条规则写哪些键", EditorStyles.miniBoldLabel);
            DrawTargetList(targets, index);
            EditorGUI.indentLevel--;

            EditorGUILayout.EndVertical();
        }

        /// <summary>规则的实时横条：色条 = 弹簧后（真正驱动目标的值），白刻度 = 弹簧前的原始值。</summary>
        private void DrawDriverMeter(HoBlinkConstraint constraint, int index, SerializedProperty driverRange)
        {
            bool bipolar = (HoBlinkDriverRange)driverRange.enumValueIndex == HoBlinkDriverRange.Bipolar;
            float raw = constraint.GetDriverRawValue(index);
            float value = constraint.GetDriverValue(index);

            HoConstraintMeterGui.DrawRow(
                "驱动",
                value,
                bipolar ? -1.0f : 0.0f,
                1.0f,
                DriverColor,
                value.ToString("0.00"),
                raw,
                DriverMeterTooltip + "\n原始 " + raw.ToString("0.###") + " → 进目标 " + value.ToString("0.###"));
        }

        private string BuildRuleTitle(SerializedProperty rule)
        {
            SerializedProperty label = rule.FindPropertyRelative("label");
            SerializedProperty driverKind = rule.FindPropertyRelative("driverKind");
            SerializedProperty positiveKey = rule.FindPropertyRelative("positiveKey");
            SerializedProperty negativeKey = rule.FindPropertyRelative("negativeKey");

            // 标题只留名字：驱动键在下面那行键名格里看得见，两段拼一起会把标题挤爆。
            if (!string.IsNullOrEmpty(label.stringValue))
            {
                return label.stringValue;
            }

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
            if (string.IsNullOrEmpty(negativeKey.stringValue))
            {
                return positive;
            }

            string negative = string.IsNullOrEmpty(negativeKey.stringValue) ? "（空）" : negativeKey.stringValue;
            return positive + " − " + negative;
        }

        private void DrawTargetList(SerializedProperty list, int ruleIndex)
        {
            for (int i = 0; i < list.arraySize; i++)
            {
                DrawTarget(list, i, ruleIndex);
            }

            if (GUILayout.Button("+ 目标"))
            {
                list.InsertArrayElementAtIndex(list.arraySize);
                targetDetails.Add(false);
            }
        }

        private void DrawTarget(SerializedProperty list, int index, int ruleIndex)
        {
            SerializedProperty target = list.GetArrayElementAtIndex(index);
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

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(KeyLabel, GUILayout.Width(24.0f));
            DrawKeyField(keyName, (HoBlinkConstraint)target.serializedObject.targetObject, 0.0f);
            targetDetails[detailIndex] = EditorGUILayout.Foldout(targetDetails[detailIndex], "细节", true);
            if (GUILayout.Button("✕", GUILayout.Width(22.0f)))
            {
                list.DeleteArrayElementAtIndex(index);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.EndHorizontal();

            // 实时横条：最终写进键的那个值（含增益 / ramp / 强度 / 权重 / 偏移）
            DrawTargetMeter((HoBlinkConstraint)target.serializedObject.targetObject, ruleIndex, index, outputMin, outputMax);

            EditorGUILayout.Space(2.0f);
            EditorGUILayout.BeginHorizontal();
            using (HoConstraintEditorSectionGui.NarrowLabels(34.0f))
            {
                EditorGUILayout.PropertyField(meshScope, MeshScopeLabel);
                using (new EditorGUI.DisabledScope((HoShapeKeyMeshScope)meshScope.enumValueIndex != HoShapeKeyMeshScope.Index))
                {
                    EditorGUILayout.PropertyField(meshIndex, MeshIndexLabel);
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            using (HoConstraintEditorSectionGui.NarrowLabels(34.0f))
            {
                EditorGUILayout.PropertyField(blendMode, BlendModeLabel);
                EditorGUILayout.PropertyField(side, SideLabel);
            }

            EditorGUILayout.EndHorizontal();

            // 一行两个：定宽格子会被 label 吃满 → 数字框变 0 宽，所以并排必须收窄 label。
            EditorGUILayout.BeginHorizontal();
            using (HoConstraintEditorSectionGui.NarrowLabels(34.0f))
            {
                gain.floatValue = EditorGUILayout.FloatField(GainLabel, gain.floatValue);
                rampPreset.enumValueIndex = EditorGUILayout.IntPopup(
                    RampLabel,
                    rampPreset.enumValueIndex,
                    RampPresetLabels,
                    RampPresetValues);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            using (HoConstraintEditorSectionGui.NarrowLabels(34.0f))
            {
                rampIntensity.floatValue = EditorGUILayout.FloatField(IntensityLabel, rampIntensity.floatValue);
                weight.floatValue = EditorGUILayout.Slider(WeightLabel, weight.floatValue, 0.0f, 1.0f);
            }

            EditorGUILayout.EndHorizontal();

            if (targetDetails[detailIndex])
            {
                EditorGUI.indentLevel++;
                HoConstraintMeterGui.DrawSeparator(2.0f, 2.0f);
                using (HoConstraintEditorSectionGui.NarrowLabels(54.0f))
                {
                    EditorGUILayout.BeginHorizontal();
                    offset.floatValue = EditorGUILayout.FloatField(new GUIContent("偏移"), offset.floatValue);
                    clampToRange.boolValue = EditorGUILayout.ToggleLeft(new GUIContent("钳制", "把输出夹在下面的范围里。"), clampToRange.boolValue, GUILayout.Width(58.0f));
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                    outputMin.floatValue = EditorGUILayout.FloatField(new GUIContent("输出下限"), outputMin.floatValue);
                    outputMax.floatValue = EditorGUILayout.FloatField(new GUIContent("输出上限"), outputMax.floatValue);
                    EditorGUILayout.EndHorizontal();
                }

                if ((HoShapeKeyRampPreset)rampPreset.enumValueIndex == HoShapeKeyRampPreset.Custom)
                {
                    EditorGUILayout.Space(2.0f);
                    EditorGUILayout.PropertyField(rampCurve, new GUIContent("自定义曲线"));
                    using (HoConstraintEditorSectionGui.NarrowLabels(58.0f))
                    {
                        EditorGUILayout.BeginHorizontal();
                        rampAttack.floatValue = EditorGUILayout.FloatField(new GUIContent("attack s"), rampAttack.floatValue);
                        rampRelease.floatValue = EditorGUILayout.FloatField(new GUIContent("release s"), rampRelease.floatValue);
                        EditorGUILayout.EndHorizontal();
                    }
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        /// <summary>目标的实时横条：这一路最终写进键的值 / 它的输出范围。</summary>
        private void DrawTargetMeter(HoBlinkConstraint constraint, int ruleIndex, int targetIndex, SerializedProperty outputMin, SerializedProperty outputMax)
        {
            float value = ruleIndex < 0
                ? constraint.GetBlinkTargetOutput(targetIndex)
                : constraint.GetRuleTargetOutput(ruleIndex, targetIndex);
            float min = Mathf.Min(outputMin.floatValue, outputMax.floatValue);
            float max = Mathf.Max(outputMin.floatValue, outputMax.floatValue);
            if (Mathf.Approximately(min, max))
            {
                max = min + 1.0f;
            }

            HoConstraintMeterGui.DrawRow(
                "输出",
                value,
                min,
                max,
                OutputColor,
                value.ToString("0.0"),
                float.NaN,
                OutputMeterTooltip + "\n当前 " + value.ToString("0.##")
                + " / 输出范围 " + min.ToString("0.#") + "–" + max.ToString("0.#"));
        }

        /// <summary>
        /// 键名格 + 下拉 + **解析状态**：右边那个小标写清"这个键落在谁身上"（✓ 绑定数 / ✗ 没找到）。
        /// 用户在面板上就能看出到底在调用谁的键，而不用等缺失清单。
        /// </summary>
        private void DrawKeyField(SerializedProperty keyName, HoBlinkConstraint constraint, float labelWidth)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            if (labelWidth > 0.0f)
            {
                rect.width -= labelWidth + 4.0f;
                rect.x += labelWidth + 4.0f;
            }

            DrawKeyContent(rect, keyName, constraint);
        }

        /// <summary>横排用的键名格：`标签 [文本框] [▾] [✓N]`。两个并排时由布局系统平分剩余宽度。</summary>
        private void DrawKeyCell(GUIContent label, SerializedProperty keyName, HoBlinkConstraint constraint, float labelWidth = 18.0f)
        {
            Rect rect = GUILayoutUtility.GetRect(
                110.0f,
                4000.0f,
                EditorGUIUtility.singleLineHeight,
                EditorGUIUtility.singleLineHeight,
                GUILayout.ExpandWidth(true));

            if (label != null)
            {
                GUI.Label(new Rect(rect.x, rect.y, labelWidth, rect.height), label, EditorStyles.miniBoldLabel);
                rect.x += labelWidth;
                rect.width -= labelWidth;
            }

            DrawKeyContent(rect, keyName, constraint);
        }

        private void DrawKeyContent(Rect rect, SerializedProperty keyName, HoBlinkConstraint constraint)
        {
            const float ButtonWidth = 22.0f;
            const float StatusWidth = 28.0f;
            Rect fieldRect = new Rect(rect.x, rect.y, Mathf.Max(20.0f, rect.width - ButtonWidth - StatusWidth - 4.0f), rect.height);
            Rect buttonRect = new Rect(fieldRect.xMax + 2.0f, rect.y, ButtonWidth, rect.height);
            Rect statusRect = new Rect(buttonRect.xMax + 2.0f, rect.y, StatusWidth, rect.height);

            string key = keyName.stringValue;
            int bindings = string.IsNullOrEmpty(key) ? 0 : constraint.CountKeyBindings(key);
            bool missing = !string.IsNullOrEmpty(key) && bindings == 0;
            Color previous = GUI.color;
            if (missing)
            {
                GUI.color = new Color(1.0f, 0.76f, 0.42f);
            }

            keyName.stringValue = EditorGUI.TextField(fieldRect, keyName.stringValue);
            GUI.color = previous;

            string tooltip = HoBlinkKeyTable.TryGetStandard(keyName.stringValue, out string standard, out _, out _)
                ? "规范：" + standard
                : "内置表未收录（手填或从网格上选）";
            if (GUI.Button(buttonRect, new GUIContent("▾", tooltip)))
            {
                HoKeyNameDropdown.Show(buttonRect, constraint, keyName);
            }

            string status;
            string statusTooltip;
            if (string.IsNullOrEmpty(key))
            {
                status = string.Empty;
                statusTooltip = "还没填键名";
            }
            else if (bindings > 0)
            {
                status = "✓" + bindings;
                statusTooltip = "已写在：" + constraint.DescribeKeyBindings(key);
            }
            else
            {
                status = "✗";
                statusTooltip = "这些网格上没有这个键";
            }

            GUI.Label(statusRect, new GUIContent(status, statusTooltip), EditorStyles.miniLabel);
        }

        private void DrawDebugSection(HoBlinkConstraint constraint)
        {
            string summary = constraint.BindingCount + " 个绑定";
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref debugExpanded, "调试", summary, DebugColor))
            {
                return;
            }

            HoConstraintMeterGui.DrawRow(
                "闭眼",
                constraint.BlinkValue,
                0.0f,
                1.0f,
                BlinkColor,
                constraint.BlinkValue.ToString("0.00"),
                float.NaN,
                "自动眨眼算出来的闭眼量（0 = 睁开，1 = 闭合）。",
                labelWidth: 30.0f);

            HoConstraintMeterGui.DrawCaption(
                BlinkPhaseText(constraint.BlinkPhase)
                + " · 网格 " + constraint.MeshCount
                + " · 绑定 " + constraint.BindingCount
                + " · 规则 " + constraint.RuleCount
                + " · 眨眼输出 " + constraint.BlinkOutputCount);

            constraint.CollectSaturatedKeys(saturationBuffer);
            HoConstraintEditorSectionGui.DrawSaturationReport(saturationBuffer);

            EditorGUILayout.Space(2.0f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("立即眨一次"))
            {
                constraint.TriggerBlink();
            }

            if (GUILayout.Button("wink 左"))
            {
                constraint.TriggerBlink(1.0f, 0.0f, 0.35f);
            }

            if (GUILayout.Button("重置"))
            {
                constraint.ResetState();
            }

            if (GUILayout.Button(new GUIContent("清空", "删掉全部眼睑键与规则。"), GUILayout.Width(56.0f)))
            {
                HoBlinkPresetActions.ClearAll(serializedObject);
                serializedObject.Update();
            }

            EditorGUILayout.EndHorizontal();

            HoConstraintMeterGui.DrawSeparator();

            manualDrive = EditorGUILayout.ToggleLeft(
                new GUIContent("手动驱动", "忽略真实键值，用下面的滑杆直接调这条规则，看输出怎么走。调试用，不进序列化。"),
                manualDrive);
            constraint.ManualDriveEnabled = manualDrive;

            bool live = Application.isPlaying || evaluateInEditMode.boolValue;
            if (!live)
            {
                HoConstraintMeterGui.DrawCaption("编辑模式下要开始播放，或打开「目标网格 · 编辑模式求值」，读数才会动。");
            }

            // 每条规则一行实时读数 + 每个目标一条横条：不用展开规则也能看数值怎么流。
            for (int i = 0; i < constraint.RuleCount; i++)
            {
                HoBlinkRule rule = constraint.GetRule(i);
                if (rule == null)
                {
                    continue;
                }

                EditorGUILayout.Space(2.0f);
                EditorGUILayout.LabelField(BuildRuleTitle(constraint, rule, i), EditorStyles.miniBoldLabel);

                bool bipolar = rule.DriverRange == HoBlinkDriverRange.Bipolar;
                float raw = constraint.GetDriverRawValue(i);
                float value = constraint.GetDriverValue(i);
                HoConstraintMeterGui.DrawRow(
                    "驱动",
                    value,
                    bipolar ? -1.0f : 0.0f,
                    1.0f,
                    DriverColor,
                    value.ToString("0.00"),
                    raw,
                    "白刻度 = 原始 " + raw.ToString("0.###") + "（弹簧前），色条 = 进目标 " + value.ToString("0.###"));

                if (manualDrive)
                {
                    using (HoConstraintEditorSectionGui.NarrowLabels(34.0f))
                    {
                        float slider = EditorGUILayout.Slider(
                            new GUIContent("滑杆", "手动给这条规则的驱动值。"),
                            value,
                            bipolar ? -1.0f : 0.0f,
                            1.0f);
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

                    HoConstraintMeterGui.DrawRow(
                        string.IsNullOrEmpty(target.KeyName) ? "（没填键）" : target.KeyName,
                        output,
                        min,
                        max,
                        OutputColor,
                        output.ToString("0.0"),
                        float.NaN,
                        "这条目标的最终键值（" + min.ToString("0.#") + "–" + max.ToString("0.#") + "）。",
                        labelWidth: 84.0f);
                }
            }

            if (constraint.BlinkOutputCount > 0)
            {
                EditorGUILayout.Space(2.0f);
                EditorGUILayout.LabelField("眨眼输出", EditorStyles.miniBoldLabel);
                for (int i = 0; i < constraint.BlinkOutputCount; i++)
                {
                    float output = constraint.GetBlinkTargetOutput(i);
                    HoConstraintMeterGui.DrawRow(
                        constraint.GetBlinkTargetKeyName(i),
                        output,
                        0.0f,
                        100.0f,
                        BlinkColor,
                        output.ToString("0.0"),
                        float.NaN,
                        "自动眨眼写在眼睑键上的值（0–100）。",
                        labelWidth: 84.0f);
                }
            }

            if (constraint.MissingKeys.Count > 0)
            {
                EditorGUILayout.HelpBox("缺失键 " + constraint.MissingKeys.Count + " 个，见「目标网格」区。", MessageType.Warning);
            }
        }

        /// <summary>调试区的规则标题（不依赖 SerializedProperty）。</summary>
        private static string BuildRuleTitle(HoBlinkConstraint constraint, HoBlinkRule rule, int index)
        {
            if (!string.IsNullOrEmpty(rule.Label))
            {
                return rule.Label;
            }

            switch (rule.DriverKind)
            {
                case HoBlinkDriverKind.AutoBlink:
                    return "规则 " + index + " · 自动眨眼";
                case HoBlinkDriverKind.Manual:
                    return "规则 " + index + " · 手动";
                default:
                    return "规则 " + index + " · " + (string.IsNullOrEmpty(rule.PositiveKey) ? "（没填驱动键）" : rule.PositiveKey)
                        + (string.IsNullOrEmpty(rule.NegativeKey) ? string.Empty : " − " + rule.NegativeKey);
            }
        }

        private void EnsureFoldoutCapacity()
        {
            while (ruleFoldouts.Count < rules.arraySize)
            {
                ruleFoldouts.Add(false);
            }

            while (ruleFoldouts.Count > rules.arraySize)
            {
                ruleFoldouts.RemoveAt(ruleFoldouts.Count - 1);
            }
        }

        private static int DetailIndex(int ruleIndex, int targetIndex)
        {
            return ruleIndex < 0 ? targetIndex : ruleIndex * 1000 + targetIndex;
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
    }
}
