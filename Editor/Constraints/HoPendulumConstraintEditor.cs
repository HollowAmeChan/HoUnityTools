using System.Collections.Generic;
using Hollow.HoUnityTools.Constraints;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Constraints
{
    [CustomEditor(typeof(HoPendulumConstraint))]
    internal sealed class HoPendulumConstraintEditor : UnityEditor.Editor
    {
        /// <summary>
        /// 水瓶液面预设的斜率缩放。1 表示通道值就是液面真实斜率 tanθ。
        /// 晃动强度 ≈ 灵敏度 × 这个缩放，所以它和「加速度灵敏度」一起决定观感强度；
        /// 上限则由「最大倾斜角」压住：输出斜率不会超过 tan(最大倾斜角) × 缩放。
        /// </summary>
        private const float BottleTiltScale = 1.0f;

        /// <summary>
        /// 液面惯性浮动（±1 斜坡）写进 `_LiquidOffset` 的量级。
        /// `_LiquidOffset` 是**网格本地单位**（与 `_LiquidLevelY/H` 同量纲），
        /// 而斜坡是 ±1，所以这里要缩到网格单位：0.01 = ±1%。
        /// 或者把 shader 的 `_LiquidOffsetMode` 设成 1（按量程百分比），这个值就直接是百分比。
        /// </summary>
        private const float LiquidOffsetScale = 0.01f;

        /// <summary>
        /// 「振幅」通道写进 `_LiquidWaveAmpMul` 的缩放。
        /// shader 侧最终振幅 = `_LiquidWaveAmp`（美术基准，面板可调）× `_LiquidWaveAmpMul`（脚本每帧写），
        /// 通道是 0~1 的摆动幅度（静止 0），所以缩放 1 的含义是"晃得最厉害时波纹等于美术给的基准"。
        /// 实测手持场景该通道峰值 0.53~0.66、RMS 0.41~0.55，不会长期贴着 1，留了调节余量。
        /// </summary>
        private const float LiquidWaveAmpMultiplierScale = 1.0f;

        private SerializedProperty updateMode;
        private SerializedProperty evaluateInEditMode;
        private SerializedProperty initializeOnEnable;
        private SerializedProperty hasInitialTransform;
        private SerializedProperty initialLocalPosition;
        private SerializedProperty initialLocalRotation;
        private SerializedProperty initialLocalScale;
        private SerializedProperty driveSource;
        private SerializedProperty anchor;
        private SerializedProperty length;
        private SerializedProperty frequencyFromLength;
        private SerializedProperty frequency;
        private SerializedProperty dampingRatio;
        private SerializedProperty maxAngle;
        private SerializedProperty saturationSoftness;
        private SerializedProperty sensitivity;
        private SerializedProperty gravityInfluence;
        private SerializedProperty centrifugalInfluence;
        private SerializedProperty referenceGravity;
        private SerializedProperty radialEnabled;
        private SerializedProperty restElongation;
        private SerializedProperty radialDampingRatio;
        private SerializedProperty maxStep;
        private SerializedProperty estimationWindow;
        private SerializedProperty accelerationSmoothing;
        private SerializedProperty equilibriumSmoothing;
        private SerializedProperty manualAcceleration;
        private SerializedProperty inputValue;
        private SerializedProperty sourceValueMin;
        private SerializedProperty sourceValueMax;
        private SerializedProperty inputAcceleration;
        private SerializedProperty inputAxis;
        private SerializedProperty fillAmount;
        private SerializedProperty sharedMaterial;
        private SerializedProperty writeToSharedMaterial;
        private SerializedProperty bindings;
        private SerializedProperty drawGizmos;
        private SerializedProperty drawSwingPlane;
        private SerializedProperty drawEquilibrium;
        private SerializedProperty drawAcceleration;
        private SerializedProperty drawMotionTrail;
        private SerializedProperty motionTrailLength;
        private SerializedProperty gizmoSizeFromBounds;
        private SerializedProperty gizmoSize;
        private SerializedProperty gizmoScale;
        private SerializedProperty accelerationGizmoScale;
        private SerializedProperty accelerationGizmoMaxScale;
        private SerializedProperty gizmoColor;
        private SerializedProperty swingColor;
        private SerializedProperty equilibriumColor;
        private SerializedProperty accelerationColor;

        private const string SectionStatePrefix = "HoPendulumConstraint.";

        private bool basicExpanded;
        private bool outputExpanded;
        private bool advancedExpanded;
        private bool debugExpanded;

        private readonly List<bool> bindingFoldouts = new List<bool>();
        private int pendingRemoveIndex = -1;

        private static readonly Color BasicColor = new Color(0.32f, 0.86f, 0.92f);
        private static readonly Color AdvancedColor = new Color(0.62f, 0.66f, 0.92f);
        private static readonly Color OutputColor = new Color(0.78f, 0.48f, 1.0f);
        private static readonly Color DebugColor = new Color(0.70f, 0.72f, 0.76f);

        // 工具提示只保留「名字看不出来」的那一句；含义、调参区间、实测量级与推导全部在
        // docs/PENDULUM_CONSTRAINT.md（参数 / 惯性项为什么要单独滤波 / 水瓶液面预设的其余参数）。
        private static readonly GUIContent UpdateModeLabel = new GUIContent("更新时机");
        private static readonly GUIContent EvaluateInEditModeLabel = new GUIContent("编辑模式求值");
        private static readonly GUIContent InitializeOnEnableLabel = new GUIContent("启用时重置摆锤", "复位采样基准，避免启用瞬间抖动。");
        private static readonly GUIContent InitialLocalPositionLabel = new GUIContent("初始位置");
        private static readonly GUIContent InitialLocalRotationLabel = new GUIContent("初始旋转");
        private static readonly GUIContent InitialLocalScaleLabel = new GUIContent("初始缩放");
        private static readonly GUIContent DriveSourceLabel = new GUIContent("驱动源", "液面用自身运动，挂件用父级运动。");
        private static readonly GUIContent AnchorLabel = new GUIContent("锚点");
        private static readonly GUIContent LengthLabel = new GUIContent("摆长", "末端到锚点的静止距离，也是离心半径。");
        private static readonly GUIContent FrequencyFromLengthLabel = new GUIContent("摆长决定频率", "改用物理单摆频率 √(g/L)/2π。");
        private static readonly GUIContent FrequencyLabel = new GUIContent("液面响应频率", "Hz。手持运动在 0.5~2Hz，建议放 2~3Hz。");
        private static readonly GUIContent DampingRatioLabel = new GUIContent("阻尼比", "1 为临界阻尼。液面建议 0.5~0.8。");
        private static readonly GUIContent MaxAngleLabel = new GUIContent("最大倾斜角", "**相对平衡面**的摆动幅度上限（度），不是液面绝对倾角。");
        private static readonly GUIContent SaturationSoftnessLabel = new GUIContent("饱和柔和度", "0 硬夹取，1 最软。");
        private static readonly GUIContent SensitivityLabel = new GUIContent("加速度灵敏度", "1 为物理值；只缩放惯性项。");
        private static readonly GUIContent GravityInfluenceLabel = new GUIContent("朝向跟随", "1 平行世界水平面，0 垂直物体轴。");
        private static readonly GUIContent CentrifugalInfluenceLabel = new GUIContent("离心影响");
        private static readonly GUIContent ReferenceGravityLabel = new GUIContent("参考重力");
        private static readonly GUIContent RadialEnabledLabel = new GUIContent("启用竖直拉伸", "过载拉长，失重收缩。");
        private static readonly GUIContent RestElongationLabel = new GUIContent("静止伸长", "米。决定径向频率 √(g/ΔL)，0 为刚性。");
        private static readonly GUIContent RadialDampingRatioLabel = new GUIContent("径向阻尼比");
        private static readonly GUIContent MaxStepLabel = new GUIContent("最大子步", "越小越精确，代价是子步更多。");
        private static readonly GUIContent EstimationWindowLabel = new GUIContent("估计窗口", "帧。越大越抗抖动，滞后约 (窗口-1)/2 帧。");
        private static readonly GUIContent AccelerationSmoothingLabel = new GUIContent("加速度平滑", "毫秒。只滤加速度、不滤朝向。颤抖的主开关。");
        private static readonly GUIContent EquilibriumSmoothingLabel = new GUIContent("平衡角平滑", "毫秒。同时会延迟倾斜容器的响应。");
        private static readonly GUIContent ManualAccelerationLabel = new GUIContent("手动加速度", "本地坐标系，m/s²。");
        private static readonly GUIContent InputValueLabel = new GUIContent("输入值");
        private static readonly GUIContent SourceValueMinLabel = new GUIContent("输入下限");
        private static readonly GUIContent SourceValueMaxLabel = new GUIContent("输入上限");
        private static readonly GUIContent InputAccelerationLabel = new GUIContent("输入加速度", "输入值取满时的加速度（m/s²）。");
        private static readonly GUIContent InputAxisLabel = new GUIContent("输入轴");
        private static readonly GUIContent WriteToSharedMaterialLabel = new GUIContent("同时写材质资产", "会破坏合批，仅兼容旧行为时用。");
        private static readonly GUIContent SharedMaterialLabel = new GUIContent("共享材质");
        private static readonly GUIContent FillAmountInputLabel = new GUIContent("液面高度输入", "0~1，倒水逻辑写；输出时按倒置自动翻转。");

        private static readonly GUIContent DrawGizmosLabel = new GUIContent("显示 Gizmo");
        private static readonly GUIContent DrawSwingPlaneLabel = new GUIContent("显示当前液面");
        private static readonly GUIContent DrawEquilibriumLabel = new GUIContent("显示平衡液面", "振荡器正在追赶的目标液面。");
        private static readonly GUIContent DrawAccelerationLabel = new GUIContent("显示加速度箭头");
        private static readonly GUIContent GizmoSizeFromBoundsLabel = new GUIContent("尺寸按物体包围盒", "取驱动源包围盒最长边作为参考尺寸。");
        private static readonly GUIContent GizmoSizeLabel = new GUIContent("参考尺寸", "米。所有调试尺寸都由它派生。");
        private static readonly GUIContent GizmoScaleLabel = new GUIContent("整体缩放");
        private static readonly GUIContent AccelerationGizmoScaleLabel = new GUIContent("箭头 1g 长度", "参考尺寸的倍数，箭头上有 1g 刻度。");
        private static readonly GUIContent AccelerationGizmoMaxScaleLabel = new GUIContent("箭头最大长度", "参考尺寸的倍数。");
        private static readonly GUIContent DrawMotionTrailLabel = new GUIContent("显示运动轨迹");
        private static readonly GUIContent MotionTrailLengthLabel = new GUIContent("轨迹长度");
        private static readonly GUIContent GizmoColorLabel = new GUIContent("Gizmo 颜色");
        private static readonly GUIContent SwingColorLabel = new GUIContent("当前液面颜色");
        private static readonly GUIContent EquilibriumColorLabel = new GUIContent("平衡液面颜色");
        private static readonly GUIContent AccelerationColorLabel = new GUIContent("加速度颜色");

        private static readonly GUIContent[] UpdateModeLabels =
        {
            new GUIContent("LateUpdate"),
            new GUIContent("Update"),
            new GUIContent("FixedUpdate"),
            new GUIContent("手动")
        };

        private static readonly GUIContent[] DriveSourceLabels =
        {
            new GUIContent("自身运动"),
            new GUIContent("父级运动"),
            new GUIContent("指定锚点"),
            new GUIContent("手动输入")
        };

        private static readonly GUIContent[] ChannelLabels =
        {
            new GUIContent("液面斜率 X"),
            new GUIContent("液面斜率 Z"),
            new GUIContent("世界斜率 X", "静止时对任何朝向恒为 0。"),
            new GUIContent("世界斜率 Z", "静止时对任何朝向恒为 0。"),
            new GUIContent("竖直对齐", "+1 正立 / 0 放平 / −1 倒置。"),
            new GUIContent("液面倾斜 X°", "度。对应 _LiquidTiltX。"),
            new GUIContent("液面倾斜 Z°", "度。对应 _LiquidTiltZ。"),
            new GUIContent("是否倒置", "1 = 已翻过 90°，用来翻转 _LiquidFill。"),
            new GUIContent("液面高度", "已按倒置翻转，对应 _LiquidFill。"),
            new GUIContent("斜率向量"),
            new GUIContent("倾角 X"),
            new GUIContent("倾角 Z"),
            new GUIContent("合倾角"),
            new GUIContent("旋转欧拉角", "让物体随液面倾斜用。"),
            new GUIContent("摆向"),
            new GUIContent("末端偏移"),
            new GUIContent("末端位置"),
            new GUIContent("振幅", "相对平衡面的摆动幅度 / 最大倾斜角。"),
            new GUIContent("相位"),
            new GUIContent("归一化读数"),
            new GUIContent("有效重力", "相对参考重力的倍率：静止 1，失重趋近 0。"),
            new GUIContent("伸长量", "米。静止 0，过载正、失重负。"),
            new GUIContent("伸长量 ±1", "静止 0、过载 +1、失重 −1。"),
            new GUIContent("锚点速度"),
            new GUIContent("锚点角速度"),
            new GUIContent("锚点加速度")
        };

        private static readonly GUIContent[] TargetLabels =
        {
            new GUIContent("渲染器属性"),
            new GUIContent("Shader 全局"),
            new GUIContent("UnityEvent"),
            new GUIContent("Transform")
        };

        private static readonly GUIContent[] TransformModeLabels =
        {
            new GUIContent("旋转"),
            new GUIContent("平移")
        };

        private static readonly int[] FourEnumValues = { 0, 1, 2, 3 };
        private static readonly int[] TwoEnumValues = { 0, 1 };
        private static readonly int[] ChannelEnumValues = BuildEnumValues(ChannelLabels.Length);

        private void OnEnable()
        {
            updateMode = Find("updateMode");
            evaluateInEditMode = Find("evaluateInEditMode");
            initializeOnEnable = Find("initializeOnEnable");
            hasInitialTransform = Find("hasInitialTransform");
            initialLocalPosition = Find("initialLocalPosition");
            initialLocalRotation = Find("initialLocalRotation");
            initialLocalScale = Find("initialLocalScale");
            driveSource = Find("driveSource");
            anchor = Find("anchor");
            length = Find("length");
            frequencyFromLength = Find("frequencyFromLength");
            frequency = Find("frequency");
            dampingRatio = Find("dampingRatio");
            maxAngle = Find("maxAngle");
            saturationSoftness = Find("saturationSoftness");
            sensitivity = Find("sensitivity");
            gravityInfluence = Find("gravityInfluence");
            centrifugalInfluence = Find("centrifugalInfluence");
            referenceGravity = Find("referenceGravity");
            radialEnabled = Find("radialEnabled");
            restElongation = Find("restElongation");
            radialDampingRatio = Find("radialDampingRatio");
            maxStep = Find("maxStep");
            estimationWindow = Find("estimationWindow");
            accelerationSmoothing = Find("accelerationSmoothing");
            equilibriumSmoothing = Find("equilibriumSmoothing");
            manualAcceleration = Find("manualAcceleration");
            inputValue = Find("inputValue");
            sourceValueMin = Find("sourceValueMin");
            sourceValueMax = Find("sourceValueMax");
            inputAcceleration = Find("inputAcceleration");
            inputAxis = Find("inputAxis");
            fillAmount = Find("fillAmount");
            sharedMaterial = Find("sharedMaterial");
            writeToSharedMaterial = Find("writeToSharedMaterial");
            bindings = Find("bindings");
            drawGizmos = Find("drawGizmos");
            drawSwingPlane = Find("drawSwingPlane");
            drawEquilibrium = Find("drawEquilibrium");
            drawAcceleration = Find("drawAcceleration");
            drawMotionTrail = Find("drawMotionTrail");
            motionTrailLength = Find("motionTrailLength");
            gizmoSizeFromBounds = Find("gizmoSizeFromBounds");
            gizmoSize = Find("gizmoSize");
            gizmoScale = Find("gizmoScale");
            accelerationGizmoScale = Find("accelerationGizmoScale");
            accelerationGizmoMaxScale = Find("accelerationGizmoMaxScale");
            gizmoColor = Find("gizmoColor");
            swingColor = Find("swingColor");
            equilibriumColor = Find("equilibriumColor");
            accelerationColor = Find("accelerationColor");

            LoadSectionStates();
        }

        private void OnDisable()
        {
            SaveSectionStates();
        }

        // 折叠状态存进 SessionState：默认全部折叠，但展开过的分区在脚本重编译后仍然保持展开。
        private void LoadSectionStates()
        {
            basicExpanded = GetSectionState("basic");
            outputExpanded = GetSectionState("output");
            advancedExpanded = GetSectionState("advanced");
            debugExpanded = GetSectionState("debug");
        }

        private void SaveSectionStates()
        {
            SetSectionState("basic", basicExpanded);
            SetSectionState("output", outputExpanded);
            SetSectionState("advanced", advancedExpanded);
            SetSectionState("debug", debugExpanded);
        }

        private static bool GetSectionState(string key)
        {
            return SessionState.GetBool(SectionStatePrefix + key, false);
        }

        private static void SetSectionState(string key, bool value)
        {
            SessionState.SetBool(SectionStatePrefix + key, value);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawPresetToolbar();
            EditorGUILayout.Space(4.0f);

            DrawBasicSection();
            DrawOutputSection();
            DrawAdvancedSection();
            DrawDebugSection();

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(4.0f);
            DrawActionButtons();
        }

        private SerializedProperty Find(string propertyName)
        {
            return serializedObject.FindProperty(propertyName);
        }

        private void DrawPresetToolbar()
        {
            EditorGUILayout.LabelField("Ho 摆锤约束", EditorStyles.boldLabel);
            Rect rect = EditorGUILayout.GetControlRect(false, 22.0f);
            float width = rect.width * 0.5f;

            if (GUI.Button(new Rect(rect.x, rect.y, width - 1.0f, rect.height), "清空"))
            {
                ApplyEmptyPreset();
            }

            if (GUI.Button(new Rect(rect.x + width, rect.y, width, rect.height), "水瓶液面"))
            {
                ApplyBottlePreset();
            }
        }

        private void DrawBasicSection()
        {
            string summary = GetPopupSummary(driveSource, DriveSourceLabels) + " · " +
                             ResolveFrequency().ToString("0.##") + "Hz · " +
                             maxAngle.floatValue.ToString("0.#") + "°";
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref basicExpanded, "基本", summary, BasicColor))
            {
                return;
            }

            DrawEnumPopup(driveSource, DriveSourceLabel, DriveSourceLabels, FourEnumValues);
            if (driveSource.enumValueIndex == (int)HoPendulumDriveSource.AnchorTransform)
            {
                EditorGUILayout.PropertyField(anchor, AnchorLabel);
                if (anchor.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox("未指定锚点时回退到自身 Transform。", MessageType.Info);
                }
            }

            EditorGUILayout.PropertyField(frequencyFromLength, FrequencyFromLengthLabel);
            if (frequencyFromLength.boolValue)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.FloatField(FrequencyLabel, ResolveFrequency());
                }
            }
            else
            {
                EditorGUILayout.PropertyField(frequency, FrequencyLabel);
            }

            EditorGUILayout.PropertyField(dampingRatio, DampingRatioLabel);
            EditorGUILayout.PropertyField(maxAngle, MaxAngleLabel);
            EditorGUILayout.PropertyField(sensitivity, SensitivityLabel);
        }

        private void DrawAdvancedSection()
        {
            string summary = GetPopupSummary(updateMode, UpdateModeLabels) +
                             " · 拉伸" + HoConstraintEditorSectionGui.BoolSummary(radialEnabled) +
                             " · " + estimationWindow.intValue + "帧";
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref advancedExpanded, "高级", summary, AdvancedColor))
            {
                return;
            }

            EditorGUILayout.LabelField("更新", EditorStyles.boldLabel);
            DrawEnumPopup(updateMode, UpdateModeLabel, UpdateModeLabels, FourEnumValues);
            EditorGUILayout.PropertyField(evaluateInEditMode, EvaluateInEditModeLabel);
            EditorGUILayout.PropertyField(initializeOnEnable, InitializeOnEnableLabel);

            EditorGUILayout.Space(5.0f);
            EditorGUILayout.LabelField("摆长与朝向", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(length, LengthLabel);
            EditorGUILayout.PropertyField(gravityInfluence, GravityInfluenceLabel);
            EditorGUILayout.PropertyField(centrifugalInfluence, CentrifugalInfluenceLabel);
            EditorGUILayout.PropertyField(referenceGravity, ReferenceGravityLabel);

            EditorGUILayout.Space(5.0f);
            EditorGUILayout.LabelField("饱和", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(saturationSoftness, SaturationSoftnessLabel);

            EditorGUILayout.Space(5.0f);
            EditorGUILayout.LabelField("竖直拉伸", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(radialEnabled, RadialEnabledLabel);
            using (new EditorGUI.DisabledScope(!radialEnabled.boolValue))
            {
                EditorGUILayout.PropertyField(restElongation, RestElongationLabel);
                EditorGUILayout.PropertyField(radialDampingRatio, RadialDampingRatioLabel);

                if (restElongation.floatValue > 0.0f)
                {
                    EditorGUILayout.LabelField(
                        "径向频率 " + ResolveRadialFrequency().ToString("0.##") + " Hz",
                        EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.Space(5.0f);
            EditorGUILayout.LabelField("采样", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(estimationWindow, EstimationWindowLabel);
            EditorGUILayout.PropertyField(accelerationSmoothing, AccelerationSmoothingLabel);
            EditorGUILayout.PropertyField(equilibriumSmoothing, EquilibriumSmoothingLabel);
            EditorGUILayout.PropertyField(maxStep, MaxStepLabel);

            if (driveSource.enumValueIndex == (int)HoPendulumDriveSource.Manual)
            {
                EditorGUILayout.Space(5.0f);
                EditorGUILayout.LabelField("手动输入", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(manualAcceleration, ManualAccelerationLabel);
                EditorGUILayout.PropertyField(inputValue, InputValueLabel);
                EditorGUILayout.PropertyField(sourceValueMin, SourceValueMinLabel);
                EditorGUILayout.PropertyField(sourceValueMax, SourceValueMaxLabel);
                EditorGUILayout.PropertyField(inputAcceleration, InputAccelerationLabel);
                EditorGUILayout.PropertyField(inputAxis, InputAxisLabel);
            }

            EditorGUILayout.Space(5.0f);
            EditorGUILayout.LabelField("液面高度", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(fillAmount, FillAmountInputLabel);

            EditorGUILayout.Space(5.0f);
            EditorGUILayout.LabelField("初始变换", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(!hasInitialTransform.boolValue))
            {
                EditorGUILayout.PropertyField(initialLocalPosition, InitialLocalPositionLabel);
                EditorGUILayout.PropertyField(initialLocalRotation, InitialLocalRotationLabel);
                EditorGUILayout.PropertyField(initialLocalScale, InitialLocalScaleLabel);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("保存初始变换"))
            {
                SaveInitialTransformForTargets();
            }

            using (new EditorGUI.DisabledScope(!hasInitialTransform.boolValue))
            {
                if (GUILayout.Button("恢复初始变换"))
                {
                    RestoreInitialTransformForTargets();
                }

                if (GUILayout.Button("清除缓存"))
                {
                    ClearInitialTransformForTargets();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private float ResolveFrequency()
        {
            if (!frequencyFromLength.boolValue)
            {
                return frequency.floatValue;
            }

            float derived = HoPendulumSolver.FrequencyFromLength(length.floatValue, referenceGravity.floatValue);
            return derived > 0.0f ? derived : frequency.floatValue;
        }

        private float ResolveRadialFrequency()
        {
            if (!radialEnabled.boolValue)
            {
                return 0.0f;
            }

            return HoPendulumSolver.RadialFrequencyFromElongation(restElongation.floatValue, referenceGravity.floatValue);
        }

        private void DrawOutputSection()
        {
            int count = bindings != null && bindings.isArray ? bindings.arraySize : 0;
            string summary = count + " 条绑定";
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref outputExpanded, "输出绑定", summary, OutputColor))
            {
                return;
            }

            if (count == 0)
            {
                EditorGUILayout.HelpBox(
                    "还没有绑定。用「+ 添加绑定」或工具栏的「水瓶液面」预设创建。",
                    MessageType.Info);
            }

            pendingRemoveIndex = -1;
            for (int i = 0; i < count; i++)
            {
                DrawBindingElement(bindings.GetArrayElementAtIndex(i), i);
            }

            if (pendingRemoveIndex >= 0)
            {
                bindings.DeleteArrayElementAtIndex(pendingRemoveIndex);
                if (pendingRemoveIndex < bindingFoldouts.Count)
                {
                    bindingFoldouts.RemoveAt(pendingRemoveIndex);
                }
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ 添加绑定"))
            {
                bindings.arraySize++;
                SerializedProperty added = bindings.GetArrayElementAtIndex(bindings.arraySize - 1);
                ResetBindingElement(added);
                bindingFoldouts.Add(true);
            }

            using (new EditorGUI.DisabledScope(count == 0))
            {
                if (GUILayout.Button("全部清空"))
                {
                    bindings.arraySize = 0;
                    bindingFoldouts.Clear();
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(3.0f);
            EditorGUILayout.PropertyField(writeToSharedMaterial, WriteToSharedMaterialLabel);
            using (new EditorGUI.DisabledScope(!writeToSharedMaterial.boolValue))
            {
                EditorGUILayout.PropertyField(sharedMaterial, SharedMaterialLabel);
            }

            EditorGUILayout.Space(3.0f);
            EditorGUILayout.LabelField("写入 MaterialPropertyBlock，不改材质资产；禁用/移除时自动还原。", EditorStyles.miniLabel);

            if (GUILayout.Button("清除已写入的材质属性"))
            {
                foreach (Object selectedTarget in targets)
                {
                    if (selectedTarget is HoPendulumConstraint constraint)
                    {
                        Undo.RecordObject(constraint, "清除摆锤约束写入的材质属性");
                        constraint.RestoreRendererBlocks();
                        EditorUtility.SetDirty(constraint);
                    }
                }
            }
        }

        private void DrawBindingElement(SerializedProperty element, int index)
        {
            SerializedProperty enabled = element.FindPropertyRelative("enabled");
            SerializedProperty label = element.FindPropertyRelative("label");
            SerializedProperty channel = element.FindPropertyRelative("channel");
            SerializedProperty target = element.FindPropertyRelative("target");

            while (bindingFoldouts.Count <= index)
            {
                // 默认折叠，只显示「通道 → 目标」标题；新增的那条才自动展开。
                bindingFoldouts.Add(false);
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            enabled.boolValue = EditorGUILayout.Toggle(enabled.boolValue, GUILayout.Width(18.0f));
            string title = string.IsNullOrEmpty(label.stringValue)
                ? GetPopupSummary(channel, ChannelLabels) + " → " + GetPopupSummary(target, TargetLabels)
                : label.stringValue;
            bindingFoldouts[index] = EditorGUILayout.Foldout(bindingFoldouts[index], title, true);
            if (GUILayout.Button("移除", GUILayout.Width(44.0f)))
            {
                pendingRemoveIndex = index;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.EndHorizontal();

            if (bindingFoldouts[index])
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(label, new GUIContent("备注名"));
                DrawEnumPopup(channel, new GUIContent("通道"), ChannelLabels, ChannelEnumValues);
                DrawEnumPopup(target, new GUIContent("目标"), TargetLabels, FourEnumValues);
                EditorGUILayout.PropertyField(element.FindPropertyRelative("scale"), new GUIContent("缩放"));
                EditorGUILayout.PropertyField(element.FindPropertyRelative("bias"), new GUIContent("偏置"));

                switch ((HoPendulumBindingTarget)target.enumValueIndex)
                {
                    case HoPendulumBindingTarget.RendererProperty:
                        EditorGUILayout.PropertyField(element.FindPropertyRelative("propertyName"), new GUIContent("属性名"));
                        EditorGUILayout.PropertyField(element.FindPropertyRelative("renderers"), new GUIContent("渲染器"), true);
                        EditorGUILayout.PropertyField(
                            element.FindPropertyRelative("includeChildRenderers"),
                            new GUIContent("包含子物体", "渲染器列表为空时生效。"));
                        break;
                    case HoPendulumBindingTarget.ShaderGlobal:
                        EditorGUILayout.PropertyField(element.FindPropertyRelative("globalName"), new GUIContent("全局名"));
                        break;
                    case HoPendulumBindingTarget.Event:
                        EditorGUILayout.PropertyField(
                            HoPendulumChannels.IsVector((HoPendulumChannel)channel.enumValueIndex)
                                ? element.FindPropertyRelative("vectorEvent")
                                : element.FindPropertyRelative("floatEvent"),
                            new GUIContent("回调"));
                        break;
                    case HoPendulumBindingTarget.Transform:
                        EditorGUILayout.PropertyField(element.FindPropertyRelative("transformTarget"), new GUIContent("目标 Transform"));
                        DrawEnumPopup(
                            element.FindPropertyRelative("transformMode"),
                            new GUIContent("驱动方式"),
                            TransformModeLabels,
                            TwoEnumValues);
                        EditorGUILayout.PropertyField(element.FindPropertyRelative("transformWeight"), new GUIContent("权重"));
                        break;
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private static void ResetBindingElement(SerializedProperty element)
        {
            element.FindPropertyRelative("enabled").boolValue = true;
            element.FindPropertyRelative("label").stringValue = string.Empty;
            element.FindPropertyRelative("channel").enumValueIndex = (int)HoPendulumChannel.TiltX;
            element.FindPropertyRelative("target").enumValueIndex = (int)HoPendulumBindingTarget.RendererProperty;
            element.FindPropertyRelative("scale").floatValue = 1.0f;
            element.FindPropertyRelative("bias").vector3Value = Vector3.zero;
            element.FindPropertyRelative("propertyName").stringValue = "_LiquidTiltX";
            element.FindPropertyRelative("includeChildRenderers").boolValue = true;
            element.FindPropertyRelative("renderers").arraySize = 0;
        }

        private void DrawDebugSection()
        {
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref debugExpanded, "调试", HoConstraintEditorSectionGui.BoolSummary(drawGizmos), DebugColor))
            {
                return;
            }

            EditorGUILayout.PropertyField(drawGizmos, DrawGizmosLabel);
            using (new EditorGUI.DisabledScope(!drawGizmos.boolValue))
            {
                EditorGUILayout.PropertyField(drawSwingPlane, DrawSwingPlaneLabel);
                EditorGUILayout.PropertyField(drawEquilibrium, DrawEquilibriumLabel);
                EditorGUILayout.PropertyField(drawAcceleration, DrawAccelerationLabel);
                using (new EditorGUI.DisabledScope(!drawAcceleration.boolValue))
                {
                    EditorGUILayout.PropertyField(accelerationGizmoScale, AccelerationGizmoScaleLabel);
                    EditorGUILayout.PropertyField(accelerationGizmoMaxScale, AccelerationGizmoMaxScaleLabel);
                }

                EditorGUILayout.PropertyField(gizmoColor, GizmoColorLabel);
                EditorGUILayout.PropertyField(swingColor, SwingColorLabel);
                EditorGUILayout.PropertyField(equilibriumColor, EquilibriumColorLabel);
                EditorGUILayout.PropertyField(accelerationColor, AccelerationColorLabel);

                EditorGUILayout.Space(3.0f);
                EditorGUILayout.LabelField("绘制尺寸", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(gizmoSizeFromBounds, GizmoSizeFromBoundsLabel);
                using (new EditorGUI.DisabledScope(gizmoSizeFromBounds.boolValue))
                {
                    EditorGUILayout.PropertyField(gizmoSize, GizmoSizeLabel);
                }

                if (gizmoSizeFromBounds.boolValue && serializedObject.targetObject is HoPendulumConstraint probe)
                {
                    EditorGUILayout.LabelField(
                        "测得",
                        probe.GizmoReferenceSize.ToString("0.###") + " m",
                        EditorStyles.miniLabel);
                }

                EditorGUILayout.PropertyField(gizmoScale, GizmoScaleLabel);
                EditorGUILayout.PropertyField(drawMotionTrail, DrawMotionTrailLabel);
                using (new EditorGUI.DisabledScope(!drawMotionTrail.boolValue))
                {
                    EditorGUILayout.PropertyField(motionTrailLength, MotionTrailLengthLabel);
                }
            }

            DrawRuntimeReadout();
        }

        private void DrawRuntimeReadout()
        {
            if (targets.Length != 1 || !(serializedObject.targetObject is HoPendulumConstraint constraint))
            {
                return;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.LabelField("运行时读数", EditorStyles.boldLabel);
                EditorGUILayout.Vector3Field("锚点位置", constraint.AnchorPosition);
                EditorGUILayout.Vector3Field("末端位置", constraint.BobPosition);
                EditorGUILayout.Vector3Field("锚点加速度", constraint.AnchorAcceleration);
                // 两个都看：原始值暴露手抖，进入解算的值才是液面真正跟随的驱动量
                EditorGUILayout.Vector3Field("进入解算的加速度", constraint.EffectiveAcceleration);
                EditorGUILayout.FloatField("倾角 X", constraint.AngleX);
                EditorGUILayout.FloatField("倾角 Z", constraint.AngleZ);
                EditorGUILayout.FloatField("合倾角", constraint.Angle);
                EditorGUILayout.FloatField("平衡倾角 X", constraint.EquilibriumAngleX);
                EditorGUILayout.FloatField("平衡倾角 Z", constraint.EquilibriumAngleZ);
                EditorGUILayout.FloatField("液面斜率 X", constraint.TiltX);
                EditorGUILayout.FloatField("液面斜率 Z", constraint.TiltZ);
                EditorGUILayout.FloatField("世界斜率 X", constraint.WorldTiltX);
                EditorGUILayout.FloatField("世界斜率 Z", constraint.WorldTiltZ);
                EditorGUILayout.FloatField("竖直对齐", constraint.VerticalAlignment);
                EditorGUILayout.FloatField("液面倾斜 X°", constraint.LiquidTiltX);
                EditorGUILayout.FloatField("液面倾斜 Z°", constraint.LiquidTiltZ);
                EditorGUILayout.FloatField("是否倒置", constraint.Inverted);
                EditorGUILayout.Slider("液面高度输入", constraint.FillAmount, 0.0f, 1.0f);
                EditorGUILayout.FloatField("液面高度输出", constraint.FillOutput);
                EditorGUILayout.FloatField("振幅", constraint.Amplitude);
                EditorGUILayout.FloatField("相位", constraint.Phase);
                EditorGUILayout.FloatField("有效重力 (g)", constraint.EffectiveGravity);
                EditorGUILayout.FloatField("伸长量 (m)", constraint.Stretch);
                EditorGUILayout.FloatField("伸长量 ±1", constraint.NormalizedStretch);
                EditorGUILayout.FloatField("摆锤距离 (m)", constraint.BobDistance);
                EditorGUILayout.Toggle("已夹取", constraint.Saturated);
            }
        }

        private void DrawActionButtons()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("重置摆锤"))
            {
                foreach (Object selectedTarget in targets)
                {
                    if (selectedTarget is HoPendulumConstraint constraint)
                    {
                        Undo.RecordObject(constraint, "重置摆锤约束");
                        constraint.ResetState();
                        EditorUtility.SetDirty(constraint);
                    }
                }
            }

            if (GUILayout.Button("立即求值"))
            {
                foreach (Object selectedTarget in targets)
                {
                    if (selectedTarget is HoPendulumConstraint constraint)
                    {
                        Undo.RecordObject(constraint, "摆锤约束立即求值");
                        constraint.Evaluate(0.0f);
                        EditorUtility.SetDirty(constraint);
                    }
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void ApplyEmptyPreset()
        {
            serializedObject.ApplyModifiedProperties();
            foreach (Object selectedTarget in targets)
            {
                if (!(selectedTarget is HoPendulumConstraint constraint))
                {
                    continue;
                }

                Undo.RecordObject(constraint, "清空摆锤约束参数");
                constraint.ClearBindings();
                EditorUtility.SetDirty(constraint);
            }

            bindingFoldouts.Clear();
            serializedObject.Update();
            SetDefaultsForAllPresets();
            serializedObject.ApplyModifiedProperties();
        }

        private void ApplyBottlePreset()
        {
            serializedObject.ApplyModifiedProperties();
            foreach (Object selectedTarget in targets)
            {
                if (!(selectedTarget is HoPendulumConstraint constraint))
                {
                    continue;
                }

                Undo.RecordObject(constraint, "摆锤约束水瓶液面预设");
                constraint.ClearBindings();
                // 按 lilToon 液体 shader 的驱动契约（lil_liquid_level.hlsl + 设计文档 §4.5 / §4.6）：
                //   _LiquidTiltX / _LiquidTiltZ = 液面倾斜角（**度**）
                //   _LiquidOffset              = 液面垂直偏移（**网格本地单位**，晃动惯性）
                //   _LiquidWaveAmpMul          = 波纹振幅的**乘数**（0~4，静止给 0）
                // 倒转不在倾斜角里：平面本身对 ±n 对称（tan(180°)=0），所以倒置时倾斜角自动归零。
                // 需要驱动端配合的只有 _LiquidFill（满 → 0，空 → 1）——
                // 「液体挂在哪一侧」是半空间属性，shader 会按物体朝向自己翻转内部，
                // 但那一步必须配合 Fill 反过来量，否则液体仍然从原来那一端开始灌。
                constraint.AddRendererPropertyBinding(
                    HoPendulumChannel.TiltXDegrees,
                    "_LiquidTiltX",
                    BottleTiltScale,
                    Vector3.zero,
                    "液面倾斜 X");
                constraint.AddRendererPropertyBinding(
                    HoPendulumChannel.TiltZDegrees,
                    "_LiquidTiltZ",
                    BottleTiltScale,
                    Vector3.zero,
                    "液面倾斜 Z");
                constraint.AddRendererPropertyBinding(
                    HoPendulumChannel.StretchNormalized,
                    "_LiquidOffset",
                    LiquidOffsetScale,
                    Vector3.zero,
                    "液面惯性浮动");
                // 液面高度：倒水逻辑把 0~1 写进组件的 FillAmount，组件在翻过 90° 时自动输出 1-输入。
                // 不接这条的话，容器倒过来时液体还留在原来那一半（"液面是倒的"就是这么来的）。
                constraint.AddRendererPropertyBinding(
                    HoPendulumChannel.FillAmount,
                    "_LiquidFill",
                    1.0f,
                    Vector3.zero,
                    "液面高度");
                // 波纹：shader 侧是「手动基准 × 脚本乘数」（_LiquidWaveAmp * _LiquidWaveAmpMul），
                // 脚本只写乘数那一路 —— 材质上的 _LiquidWaveAmp 保持是美术的闸门（给 0 就整条关掉）。
                // 「振幅」通道 = 相对平衡面的摆动幅度 / 最大倾斜角（静止 0），实测手持场景峰值 0.53~0.66、
                // RMS 0.41~0.55，所以缩放 1 的含义是"晃得最厉害时波纹正好等于美术给的基准幅度"。
                // 想让晃动时波纹更显眼，把这条绑定的缩放调到 1.5~2（量程 0~4 够用）。
                constraint.AddRendererPropertyBinding(
                    HoPendulumChannel.Amplitude,
                    "_LiquidWaveAmpMul",
                    LiquidWaveAmpMultiplierScale,
                    Vector3.zero,
                    "液面波纹");
                EditorUtility.SetDirty(constraint);
            }

            bindingFoldouts.Clear();
            bindingFoldouts.Add(false);
            bindingFoldouts.Add(false);
            bindingFoldouts.Add(false);
            bindingFoldouts.Add(false);
            bindingFoldouts.Add(false);
            serializedObject.Update();
            SetBottlePresetDefaults();
            serializedObject.ApplyModifiedProperties();
        }

        private void SetDefaultsForAllPresets()
        {
            SetEnum(updateMode, HoPendulumConstraintUpdateMode.LateUpdate);
            SetBool(evaluateInEditMode, true);
            SetBool(initializeOnEnable, true);
            SetEnum(driveSource, HoPendulumDriveSource.SelfMotion);
            SetFloat(length, 0.25f);
            SetBool(frequencyFromLength, false);
            SetFloat(frequency, 3.0f);
            SetFloat(dampingRatio, 0.18f);
            SetFloat(maxAngle, 20.0f);
            SetFloat(saturationSoftness, 0.65f);
            SetFloat(sensitivity, 1.0f);
            SetFloat(gravityInfluence, 1.0f);
            SetFloat(centrifugalInfluence, 1.0f);
            SetFloat(referenceGravity, 9.81f);
            SetBool(radialEnabled, true);
            SetFloat(restElongation, 0.02f);
            SetFloat(radialDampingRatio, 0.25f);
            SetFloat(maxStep, 1.0f / 240.0f);
            SetInt(estimationWindow, HoMotionEstimator.DefaultWindow);
            SetFloat(accelerationSmoothing, 60.0f);
            SetFloat(equilibriumSmoothing, 20.0f);
            SetVector3(manualAcceleration, Vector3.zero);
            SetFloat(inputValue, 0.0f);
            SetFloat(sourceValueMin, 0.0f);
            SetFloat(sourceValueMax, 1.0f);
            SetFloat(inputAcceleration, 9.81f);
            SetVector3(inputAxis, Vector3.right);
            SetFloat(fillAmount, 0.5f);
            SetBool(gizmoSizeFromBounds, true);
            SetFloat(gizmoSize, 0.5f);
            SetFloat(gizmoScale, 1.0f);
            SetFloat(accelerationGizmoScale, 0.5f);
            SetFloat(accelerationGizmoMaxScale, 2.0f);
        }

        private void SetBottlePresetDefaults()
        {
            SetDefaultsForAllPresets();

            // 数值来自实测（harness 的 liquid 模式：把「手拿瓶子左右移动」的轨迹喂进
            // 运动估计器 + 求解器，量液面倾角的峰峰值与 >6Hz 的抖动 RMS）：
            //   旧预设（灵敏度1 / 最大角10° / 3Hz / ζ0.3 / 40ms）：
            //     慢速搬运 峰峰 49.8° 抖动 2.49°；走路 48.5° / 3.84°；摇动 98.7° / 12.42°
            //   新预设：慢速搬运 16.7° / 0.87°；走路 10.3° / 0.88°；摇动 22.9° / 2.69°
            // 也就是说旧预设的「液面倾角」在左右移动时会摆 ±25°（半个液面翻过去），
            // 现在的 ±5~11° 才是「瓶子里的水」该有的样子。
            SetBool(frequencyFromLength, false);
            SetFloat(length, 0.25f);
            SetFloat(frequency, 2.2f);
            // 阻尼 0.7：接近临界阻尼。液体的黏性本来就大，ζ=0.3 那种来回振铃像果冻。
            SetFloat(dampingRatio, 0.7f);
            // 灵敏度 0.5：1.0 是物理值，但手的加速度尖峰能到几十 m/s²，1:1 跟随就是 ±25° 的大幅摆动。
            // 它只缩放**惯性项**，不影响「倾斜容器时液面屈服重力」（那是朝向项，永远满值跟随）。
            SetFloat(sensitivity, 0.5f);
            // 最大倾斜角限制的是**相对平衡面的摆动幅度**（不是液面倾角上限）：
            // 6° 是一眼能看出晃动、又不至于把液面甩出瓶口的上限。
            SetFloat(maxAngle, 6.0f);
            SetFloat(saturationSoftness, 0.7f);
            SetFloat(restElongation, 0.015f);
            // 防抖三条：估计窗口 8 帧（拟合，不放大噪声）→ 加速度平滑 120ms（只滤惯性项）
            // → 平衡角平滑 30ms（兜住剩下的一阶噪声，再大就会让倾斜容器的响应发肉）
            SetInt(estimationWindow, 8);
            SetFloat(accelerationSmoothing, 120.0f);
            SetFloat(equilibriumSmoothing, 30.0f);
        }

        private void SaveInitialTransformForTargets()
        {
            serializedObject.ApplyModifiedProperties();
            foreach (Object selectedTarget in targets)
            {
                if (selectedTarget is HoPendulumConstraint constraint)
                {
                    Undo.RecordObject(constraint, "保存摆锤约束初始变换");
                    constraint.SaveInitialTransform();
                    EditorUtility.SetDirty(constraint);
                }
            }

            serializedObject.Update();
        }

        private void RestoreInitialTransformForTargets()
        {
            serializedObject.ApplyModifiedProperties();
            foreach (Object selectedTarget in targets)
            {
                if (selectedTarget is HoPendulumConstraint constraint)
                {
                    Undo.RecordObjects(new Object[] { constraint, constraint.transform }, "恢复摆锤约束初始变换");
                    constraint.RestoreInitialTransform();
                    EditorUtility.SetDirty(constraint);
                    EditorUtility.SetDirty(constraint.transform);
                }
            }

            serializedObject.Update();
        }

        private void ClearInitialTransformForTargets()
        {
            serializedObject.ApplyModifiedProperties();
            foreach (Object selectedTarget in targets)
            {
                if (selectedTarget is HoPendulumConstraint constraint)
                {
                    Undo.RecordObject(constraint, "清除摆锤约束初始变换");
                    constraint.ClearInitialTransform();
                    EditorUtility.SetDirty(constraint);
                }
            }

            serializedObject.Update();
        }

        private static void SetBool(SerializedProperty property, bool value)
        {
            if (property != null)
            {
                property.boolValue = value;
            }
        }

        private static void SetFloat(SerializedProperty property, float value)
        {
            if (property != null)
            {
                property.floatValue = value;
            }
        }

        private static void SetInt(SerializedProperty property, int value)
        {
            if (property != null)
            {
                property.intValue = value;
            }
        }

        private static void SetVector3(SerializedProperty property, Vector3 value)
        {
            if (property != null)
            {
                property.vector3Value = value;
            }
        }

        private static void SetEnum<TEnum>(SerializedProperty property, TEnum value)
            where TEnum : System.Enum
        {
            if (property != null)
            {
                property.enumValueIndex = System.Convert.ToInt32(value);
            }
        }

        private static void DrawEnumPopup(SerializedProperty property, GUIContent label, GUIContent[] labels, int[] values)
        {
            if (property == null)
            {
                return;
            }

            property.enumValueIndex = EditorGUILayout.IntPopup(label, property.enumValueIndex, labels, values);
        }

        private static string GetPopupSummary(SerializedProperty property, GUIContent[] labels)
        {
            if (property == null)
            {
                return "-";
            }

            int index = Mathf.Clamp(property.enumValueIndex, 0, labels.Length - 1);
            return labels[index].text;
        }

        private static int[] BuildEnumValues(int count)
        {
            int[] values = new int[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = i;
            }

            return values;
        }
    }
}
