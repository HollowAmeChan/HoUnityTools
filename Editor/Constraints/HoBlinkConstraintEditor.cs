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

        private bool meshExpanded = true;
        private bool blinkExpanded = true;
        private bool rulesExpanded;
        private bool debugExpanded;
        private bool manualDrive;

        private readonly List<bool> ruleFoldouts = new List<bool>();
        private readonly List<bool> targetDetails = new List<bool>();

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
            DrawToolbar(constraint);
            EditorGUILayout.Space(4.0f);

            DrawMeshSection(constraint);
            DrawBlinkSection();
            DrawRulesSection();
            DrawDebugSection(constraint);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawToolbar(HoBlinkConstraint constraint)
        {
            EditorGUILayout.LabelField("Ho 眨眼约束", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("眨眼输出: 双眼键版", GUILayout.Height(22.0f)))
            {
                HoBlinkPresetActions.ApplyBlinkOutput(constraint, serializedObject, false);
            }

            if (GUILayout.Button("左右键版", GUILayout.Height(22.0f)))
            {
                HoBlinkPresetActions.ApplyBlinkOutput(constraint, serializedObject, true);
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("凝视: 双眼四向"))
            {
                HoBlinkPresetActions.ApplyGazeRules(constraint, serializedObject, false);
            }

            if (GUILayout.Button("左右眼 · 左右族"))
            {
                HoBlinkPresetActions.ApplyGazeRules(constraint, serializedObject, true);
            }

            if (GUILayout.Button("左右眼 · 内外族"))
            {
                HoBlinkPresetActions.ApplyGazeRules(constraint, serializedObject, true, true);
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("高光跟眼"))
            {
                HoBlinkPresetActions.ApplyGazeJelly(constraint, serializedObject);
            }

            if (GUILayout.Button("眼仁形变"))
            {
                HoBlinkPresetActions.ApplyPupilJelly(constraint, serializedObject);
            }

            if (GUILayout.Button("眨眼压高光"))
            {
                HoBlinkPresetActions.ApplyBlinkJelly(constraint, serializedObject);
            }

            if (GUILayout.Button("清空"))
            {
                HoBlinkPresetActions.ClearAll(serializedObject);
            }

            EditorGUILayout.EndHorizontal();
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

            if (constraint.MissingKeys.Count > 0)
            {
                string text = "以下键在目标网格上不存在：" + string.Join("、", constraint.MissingKeys);
                EditorGUILayout.HelpBox(text, MessageType.Warning);
            }
        }

        private void DrawBlinkSection()
        {
            string summary = blinkEnabled.boolValue ? "开" : "关";
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref blinkExpanded, "自动眨眼", summary, BlinkColor))
            {
                return;
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

            EnsureFoldoutCapacity();

            for (int i = 0; i < rules.arraySize; i++)
            {
                DrawRule(i);
            }

            EditorGUILayout.Space(2.0f);
            if (GUILayout.Button("+ 规则"))
            {
                rules.InsertArrayElementAtIndex(rules.arraySize);
                ruleFoldouts.Add(true);
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

            if (ruleFoldouts[index])
            {
                EditorGUI.indentLevel++;
                HoBlinkConstraint constraint = (HoBlinkConstraint)target;

                EditorGUILayout.BeginHorizontal();
                using (HoConstraintEditorSectionGui.NarrowLabels(34.0f))
                {
                    EditorGUILayout.PropertyField(driverKind, new GUIContent("驱动"));
                    EditorGUILayout.PropertyField(driverRange, new GUIContent("值域"));
                }

                invert.boolValue = EditorGUILayout.ToggleLeft("反相", invert.boolValue, GUILayout.Width(52.0f));
                EditorGUILayout.EndHorizontal();

                using (new EditorGUI.DisabledScope((HoBlinkDriverKind)driverKind.enumValueIndex != HoBlinkDriverKind.ShapeKey))
                {
                    DrawKeyRow("正向键", positiveKey, constraint);
                    DrawKeyRow("负向键", negativeKey, constraint);
                }

                EditorGUILayout.BeginHorizontal();
                jellyEnabled.boolValue = EditorGUILayout.ToggleLeft("果冻", jellyEnabled.boolValue, GUILayout.Width(52.0f));
                using (new EditorGUI.DisabledScope(!jellyEnabled.boolValue))
                using (HoConstraintEditorSectionGui.NarrowLabels(52.0f))
                {
                    frequency.floatValue = EditorGUILayout.FloatField(new GUIContent("频率 Hz"), frequency.floatValue);
                    dampingRatio.floatValue = EditorGUILayout.FloatField(new GUIContent("阻尼比"), dampingRatio.floatValue);
                }

                EditorGUILayout.EndHorizontal();
                using (new EditorGUI.DisabledScope(!jellyEnabled.boolValue))
                using (HoConstraintEditorSectionGui.NarrowLabels(62.0f))
                {
                    EditorGUILayout.BeginHorizontal();
                    inputSmoothing.floatValue = EditorGUILayout.FloatField(new GUIContent("输入平滑 s"), inputSmoothing.floatValue);
                    maxStep.floatValue = EditorGUILayout.FloatField(new GUIContent("子步上限 s"), maxStep.floatValue);
                    EditorGUILayout.EndHorizontal();
                }

                readWritten.boolValue = EditorGUILayout.ToggleLeft(
                    new GUIContent("读本帧已写值（链式联动，注意自反馈）", "默认读基准快照，切断自反馈。"),
                    readWritten.boolValue);

                EditorGUILayout.Space(2.0f);
                EditorGUILayout.LabelField("目标", EditorStyles.boldLabel);
                DrawTargetList(targets, index);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private string BuildRuleTitle(SerializedProperty rule)
        {
            SerializedProperty label = rule.FindPropertyRelative("label");
            SerializedProperty driverKind = rule.FindPropertyRelative("driverKind");
            SerializedProperty positiveKey = rule.FindPropertyRelative("positiveKey");
            SerializedProperty negativeKey = rule.FindPropertyRelative("negativeKey");

            string summary;
            HoBlinkDriverKind kind = (HoBlinkDriverKind)driverKind.enumValueIndex;
            if (kind == HoBlinkDriverKind.AutoBlink)
            {
                summary = "自动眨眼 (AutoBlink)";
            }
            else if (kind == HoBlinkDriverKind.Manual)
            {
                summary = "手动驱动";
            }
            else
            {
                string positive = string.IsNullOrEmpty(positiveKey.stringValue) ? "（未填驱动键）" : positiveKey.stringValue;
                string standard = HoBlinkKeyTable.TryGetStandard(positiveKey.stringValue, out string tag, out _, out _)
                    ? " (" + tag + ")"
                    : string.Empty;
                summary = string.IsNullOrEmpty(negativeKey.stringValue)
                    ? positive + standard
                    : positive + standard + " − " + negativeKey.stringValue;
            }

            return string.IsNullOrEmpty(label.stringValue) ? summary : label.stringValue + " · " + summary;
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

            EditorGUILayout.BeginHorizontal();
            using (HoConstraintEditorSectionGui.NarrowLabels(34.0f))
            {
                EditorGUILayout.PropertyField(meshScope, new GUIContent("范围"));
                using (new EditorGUI.DisabledScope((HoShapeKeyMeshScope)meshScope.enumValueIndex != HoShapeKeyMeshScope.Index))
                {
                    EditorGUILayout.PropertyField(meshIndex, new GUIContent("序号"));
                }

                EditorGUILayout.PropertyField(blendMode, new GUIContent("混合"));
                EditorGUILayout.PropertyField(side, SideLabel);
            }

            EditorGUILayout.EndHorizontal();

            // 增益 / ramp 预设 / 强度：原来一行三个定宽（160+150+140=450px）在窄面板会被裁掉，
            // 而且 label 会按默认 labelWidth 把定宽格子吃满 → 数字框 0 宽、调不动。现在三等分 + 收窄 label。
            EditorGUILayout.BeginHorizontal();
            using (HoConstraintEditorSectionGui.NarrowLabels(32.0f))
            {
                gain.floatValue = EditorGUILayout.FloatField(GainLabel, gain.floatValue);
                rampPreset.enumValueIndex = EditorGUILayout.IntPopup(
                    RampLabel,
                    rampPreset.enumValueIndex,
                    RampPresetLabels,
                    RampPresetValues);
                rampIntensity.floatValue = EditorGUILayout.FloatField(IntensityLabel, rampIntensity.floatValue);
            }

            EditorGUILayout.EndHorizontal();

            if (targetDetails[detailIndex])
            {
                EditorGUI.indentLevel++;
                using (HoConstraintEditorSectionGui.NarrowLabels(48.0f))
                {
                    EditorGUILayout.BeginHorizontal();
                    offset.floatValue = EditorGUILayout.FloatField(new GUIContent("偏移"), offset.floatValue);
                    weight.floatValue = EditorGUILayout.Slider(new GUIContent("权重"), weight.floatValue, 0.0f, 1.0f);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                    outputMin.floatValue = EditorGUILayout.FloatField(new GUIContent("输出下限"), outputMin.floatValue);
                    outputMax.floatValue = EditorGUILayout.FloatField(new GUIContent("输出上限"), outputMax.floatValue);
                    clampToRange.boolValue = EditorGUILayout.ToggleLeft("钳制", clampToRange.boolValue, GUILayout.Width(52.0f));
                    EditorGUILayout.EndHorizontal();
                }

                if ((HoShapeKeyRampPreset)rampPreset.enumValueIndex == HoShapeKeyRampPreset.Custom)
                {
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

        private void DrawKeyRow(string label, SerializedProperty keyName, HoBlinkConstraint constraint)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(52.0f));
            DrawKeyField(keyName, constraint, 0.0f);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawKeyField(SerializedProperty keyName, HoBlinkConstraint constraint, float labelWidth)
        {
            const float ButtonWidth = 24.0f;
            Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            if (labelWidth > 0.0f)
            {
                rect.width -= labelWidth + 4.0f;
                rect.x += labelWidth + 4.0f;
            }

            Rect fieldRect = new Rect(rect.x, rect.y, rect.width - ButtonWidth - 2.0f, rect.height);
            Rect buttonRect = new Rect(fieldRect.xMax + 2.0f, rect.y, ButtonWidth, rect.height);

            bool missing = !string.IsNullOrEmpty(keyName.stringValue) && !constraint.KeyExists(keyName.stringValue);
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
        }

        private void DrawDebugSection(HoBlinkConstraint constraint)
        {
            string summary = constraint.BindingCount + " 个绑定";
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref debugExpanded, "调试", summary, DebugColor))
            {
                return;
            }

            EditorGUILayout.LabelField(
                "网格 " + constraint.MeshCount + " / 绑定 " + constraint.BindingCount + " / 规则 " + constraint.RuleCount,
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField(
                "眨眼相位 " + constraint.BlinkPhase + "   输出 " + constraint.BlinkValue.ToString("0.###"),
                EditorStyles.miniLabel);

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

            EditorGUILayout.EndHorizontal();

            manualDrive = EditorGUILayout.ToggleLeft(
                new GUIContent("手动驱动（忽略真实键值，用滑杆调参）", "调试用，不进序列化。"),
                manualDrive);
            constraint.ManualDriveEnabled = manualDrive;

            for (int i = 0; i < constraint.RuleCount; i++)
            {
                HoBlinkRule rule = constraint.GetRule(i);
                if (rule == null)
                {
                    continue;
                }

                string title = string.IsNullOrEmpty(rule.PositiveKey) ? "规则 " + i : rule.PositiveKey;
                EditorGUILayout.LabelField(
                    title + "   驱动 " + constraint.GetDriverValue(i).ToString("0.###")
                    + "   弹簧 " + constraint.GetJellyValue(i).ToString("0.###"),
                    EditorStyles.miniLabel);

                if (manualDrive)
                {
                    float value = constraint.GetDriverValue(i);
                    float slider = EditorGUILayout.Slider("滑杆 " + i, value, -1.0f, 1.0f);
                    if (!Mathf.Approximately(slider, value))
                    {
                        constraint.SetManualDriverValue(i, slider);
                    }
                }
            }

            if (constraint.MissingKeys.Count > 0)
            {
                EditorGUILayout.HelpBox("缺失键 " + constraint.MissingKeys.Count + " 个，见「目标网格」区。", MessageType.Warning);
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
    }
}
