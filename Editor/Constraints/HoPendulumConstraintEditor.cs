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
        /// 水瓶液面预设的斜率缩放。缩放 1 表示通道值就是液面真实斜率 tanθ；
        /// 这里按 60fps 下旧 Wobble 脚本在代表手持运动上的 RMS 标定（旧/新 ≈ 2.42）。
        /// </summary>
        private const float BottleTiltScale = 2.4f;

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
        private SerializedProperty equilibriumSmoothing;
        private SerializedProperty manualAcceleration;
        private SerializedProperty inputValue;
        private SerializedProperty sourceValueMin;
        private SerializedProperty sourceValueMax;
        private SerializedProperty inputAcceleration;
        private SerializedProperty inputAxis;
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

        private bool updateExpanded = true;
        private bool initialExpanded = true;
        private bool anchorExpanded = true;
        private bool pendulumExpanded = true;
        private bool stretchExpanded = true;
        private bool samplingExpanded;
        private bool inputExpanded;
        private bool outputExpanded = true;
        private bool debugExpanded;

        private readonly List<bool> bindingFoldouts = new List<bool>();
        private int pendingRemoveIndex = -1;

        private static readonly Color UpdateColor = new Color(0.28f, 0.62f, 1.0f);
        private static readonly Color InitialColor = new Color(0.24f, 0.86f, 0.58f);
        private static readonly Color AnchorColor = new Color(0.35f, 0.72f, 1.0f);
        private static readonly Color PendulumColor = new Color(0.32f, 0.86f, 0.92f);
        private static readonly Color StretchColor = new Color(0.42f, 0.78f, 1.0f);
        private static readonly Color SamplingColor = new Color(0.62f, 0.66f, 0.92f);
        private static readonly Color InputColor = new Color(1.0f, 0.70f, 0.28f);
        private static readonly Color OutputColor = new Color(0.78f, 0.48f, 1.0f);
        private static readonly Color DebugColor = new Color(0.70f, 0.72f, 0.76f);

        private static readonly GUIContent UpdateModeLabel = new GUIContent("更新时机", "摆锤约束在哪个 Unity 更新阶段求值。");
        private static readonly GUIContent EvaluateInEditModeLabel = new GUIContent("编辑模式求值", "未进入播放模式时也持续更新。");
        private static readonly GUIContent InitializeOnEnableLabel = new GUIContent("启用时重置摆锤", "组件启用时把采样基准复位到当前位姿，避免启用瞬间抖动。");
        private static readonly GUIContent InitialLocalPositionLabel = new GUIContent("初始位置", "保存的本地初始位置。");
        private static readonly GUIContent InitialLocalRotationLabel = new GUIContent("初始旋转", "保存的本地初始旋转。");
        private static readonly GUIContent InitialLocalScaleLabel = new GUIContent("初始缩放", "保存的本地初始缩放。");
        private static readonly GUIContent DriveSourceLabel = new GUIContent("驱动源", "采样哪个 Transform 的运动。液面用自身运动，被驱动的挂件用父级运动。");
        private static readonly GUIContent AnchorLabel = new GUIContent("锚点", "驱动源为「指定锚点」时采样的 Transform。");
        private static readonly GUIContent LengthLabel = new GUIContent("摆长", "摆锤末端到锚点的静止距离，同时是离心项半径。默认不再决定晃动频率。");
        private static readonly GUIContent FrequencyFromLengthLabel = new GUIContent("摆长决定频率", "勾选后改用物理单摆频率 √(g/L)/2π，摆长越长晃得越慢。");
        private static readonly GUIContent FrequencyLabel = new GUIContent("液面响应频率", "单位 Hz。液面晃动快慢由容器与液体决定，手持运动一般在 0.5~1.5Hz，建议把响应频率放在 2.5Hz 以上，避免与手的动作共振导致相位滞后。");
        private static readonly GUIContent DampingRatioLabel = new GUIContent("阻尼比", "1 为临界阻尼不再晃动，0 为无阻尼。液面建议 0.15~0.3。");
        private static readonly GUIContent MaxAngleLabel = new GUIContent("最大倾斜角", "合成倾角上限（度）。按幅值夹取，对角晃动不会超过这个值。");
        private static readonly GUIContent SaturationSoftnessLabel = new GUIContent("饱和柔和度", "0 为硬夹取（到顶就停住），1 为完全 tanh 软饱和。软饱和的大幅晃动更像液体而不是撞墙。");
        private static readonly GUIContent SensitivityLabel = new GUIContent("加速度灵敏度", "1 为物理值：恒加速度下 tanθ = a/g。");
        private static readonly GUIContent GravityInfluenceLabel = new GUIContent("朝向跟随", "1 表示液面始终与 世界水平面 平行，0 表示始终垂直于物体自身轴。");
        private static readonly GUIContent CentrifugalInfluenceLabel = new GUIContent("离心影响", "旋转产生的离心加速度对液面的影响强度。");
        private static readonly GUIContent ReferenceGravityLabel = new GUIContent("参考重力", "用于换算平衡角与径向弹簧的参考重力加速度。");
        private static readonly GUIContent RadialEnabledLabel = new GUIContent("启用竖直拉伸", "竖直方向的伸缩由一维弹簧描述：静止时在摆长上，过载拉长，失重收缩。");
        private static readonly GUIContent RestElongationLabel = new GUIContent("静止伸长", "1g 静止时的伸长量（米）。同时决定径向弹簧固有频率 √(g/ΔL)：0.02m 约 3.5Hz。0 表示刚性。");
        private static readonly GUIContent RadialDampingRatioLabel = new GUIContent("径向阻尼比", "竖直伸缩的阻尼比。");
        private static readonly GUIContent MaxStepLabel = new GUIContent("最大子步", "单步积分上限。越小越精确，代价是子步更多。");
        private static readonly GUIContent EstimationWindowLabel = new GUIContent("估计窗口", "帧。对最近这么多帧的位置做二次最小二乘拟合来求速度与加速度。窗口越大越抗抖动，但加速度变化时会多出约 (窗口-1)/2 帧的滞后。逐帧二阶差分在 20Hz 采样、位置量化、编辑器拖拽下会产生几十 m/s² 的噪声，所以不建议调到 3。");
        private static readonly GUIContent EquilibriumSmoothingLabel = new GUIContent("平衡角平滑", "毫秒。对平衡角做一阶低通，直流增益为 1，因此不会影响稳态倾角，只增加这一点延迟。");
        private static readonly GUIContent ManualAccelerationLabel = new GUIContent("手动加速度", "驱动源为「手动输入」时直接注入的加速度（锚点本地坐标系，m/s²）。");
        private static readonly GUIContent InputValueLabel = new GUIContent("输入值", "外部脚本写入的驱动值，会按区间归一化后乘到输入加速度上。");
        private static readonly GUIContent SourceValueMinLabel = new GUIContent("输入下限");
        private static readonly GUIContent SourceValueMaxLabel = new GUIContent("输入上限");
        private static readonly GUIContent InputAccelerationLabel = new GUIContent("输入加速度", "输入值取满时沿输入轴注入的加速度（m/s²）。");
        private static readonly GUIContent InputAxisLabel = new GUIContent("输入轴", "输入加速度在锚点本地坐标系中的方向。");
        private static readonly GUIContent WriteToSharedMaterialLabel = new GUIContent("同时写材质资产", "默认只写 MaterialPropertyBlock；勾选后会额外写入共享材质，会破坏合批，仅在需要兼容旧行为时使用。");
        private static readonly GUIContent SharedMaterialLabel = new GUIContent("共享材质");
        private static readonly GUIContent DrawGizmosLabel = new GUIContent("显示 Gizmo");
        private static readonly GUIContent DrawSwingPlaneLabel = new GUIContent("显示当前液面", "按当前摆角画出倾斜的液面参考圆。");
        private static readonly GUIContent DrawEquilibriumLabel = new GUIContent("显示平衡液面", "画出振荡器正在追赶的目标液面。和当前液面一起看就能判断滞后与振铃，而不是靠猜。");
        private static readonly GUIContent DrawAccelerationLabel = new GUIContent("显示加速度箭头", "画出驱动整个系统的锚点加速度：这是因，液面倾斜是果。");
        private static readonly GUIContent GizmoSizeFromBoundsLabel = new GUIContent("尺寸按物体包围盒", "自动用驱动源（含子物体）渲染器包围盒的最长边作为参考尺寸，物体缩放多大都不会看不见。关掉则用下面的手动尺寸。");
        private static readonly GUIContent GizmoSizeLabel = new GUIContent("参考尺寸", "米。所有调试绘制尺寸都从这里派生：锚点球 5%、摆锤球 4.5%、液面盘半径 40%、法线长度 50%。与摆长无关。");
        private static readonly GUIContent GizmoScaleLabel = new GUIContent("整体缩放", "在参考尺寸之上再整体放大缩小。");
        private static readonly GUIContent AccelerationGizmoScaleLabel = new GUIContent("箭头 1g 长度", "1g 加速度对应的箭头长度，单位为参考尺寸的倍数。箭头上会画一道 1g 刻度，按刻度数就能读出几倍重力。");
        private static readonly GUIContent AccelerationGizmoMaxScaleLabel = new GUIContent("箭头最大长度", "参考尺寸的倍数。拖拽与急停时加速度本来就有几十 g，箭头会顶到这个长度不再增长；调小可以避免它占满场景。");
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
            new GUIContent("斜率向量"),
            new GUIContent("倾角 X"),
            new GUIContent("倾角 Z"),
            new GUIContent("合倾角"),
            new GUIContent("旋转欧拉角", "把局部 up 转到液面法线的欧拉角 (AngleZ, 0, -AngleX)，适合让物体随液面倾斜。"),
            new GUIContent("摆向"),
            new GUIContent("末端偏移"),
            new GUIContent("末端位置"),
            new GUIContent("振幅"),
            new GUIContent("相位"),
            new GUIContent("归一化读数"),
            new GUIContent("有效重力", "相对参考重力的倍率：静止 1，自由落体趋近 0，过载大于 1。"),
            new GUIContent("伸长量", "径向弹簧伸长（米）：静止 0，过载为正，失重为负。"),
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
            equilibriumSmoothing = Find("equilibriumSmoothing");
            manualAcceleration = Find("manualAcceleration");
            inputValue = Find("inputValue");
            sourceValueMin = Find("sourceValueMin");
            sourceValueMax = Find("sourceValueMax");
            inputAcceleration = Find("inputAcceleration");
            inputAxis = Find("inputAxis");
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
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawPresetToolbar();
            EditorGUILayout.Space(4.0f);

            DrawUpdateSection();
            DrawInitialTransformSection();
            DrawAnchorSection();
            DrawPendulumSection();
            DrawStretchSection();
            DrawSamplingSection();
            DrawInputSection();
            DrawOutputSection();
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

        private void DrawUpdateSection()
        {
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref updateExpanded, "更新", GetPopupSummary(updateMode, UpdateModeLabels), UpdateColor))
            {
                return;
            }

            DrawEnumPopup(updateMode, UpdateModeLabel, UpdateModeLabels, FourEnumValues);
            EditorGUILayout.PropertyField(evaluateInEditMode, EvaluateInEditModeLabel);
            EditorGUILayout.PropertyField(initializeOnEnable, InitializeOnEnableLabel);
        }

        private void DrawInitialTransformSection()
        {
            string summary = hasInitialTransform.boolValue ? "已保存" : "未保存";
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref initialExpanded, "初始变换", summary, InitialColor))
            {
                return;
            }

            EditorGUILayout.HelpBox("摆锤约束不写自身 Transform。这里保存的位姿只用于「恢复初始变换」，以及给采样提供静止参考。", MessageType.Info);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Toggle("已保存", hasInitialTransform.boolValue);
            }

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

        private void DrawAnchorSection()
        {
            string summary = GetPopupSummary(driveSource, DriveSourceLabels);
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref anchorExpanded, "锚点", summary, AnchorColor))
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
        }

        private void DrawPendulumSection()
        {
            string summary = string.Format(
                "L {0:0.##}m / {1:0.#}°",
                length.floatValue,
                maxAngle.floatValue);
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref pendulumExpanded, "摆锤", summary, PendulumColor))
            {
                return;
            }

            EditorGUILayout.PropertyField(length, LengthLabel);
            EditorGUILayout.PropertyField(frequencyFromLength, FrequencyFromLengthLabel);

            if (frequencyFromLength.boolValue)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.FloatField(FrequencyLabel, ResolveFrequency());
                }

                if (length.floatValue > 0.0f)
                {
                    EditorGUILayout.LabelField(
                        "  ",
                        "摆长 " + length.floatValue.ToString("0.###") + "m 的物理单摆周期 " +
                        (1.0f / Mathf.Max(0.0001f, ResolveFrequency())).ToString("0.###") + " s",
                        EditorStyles.miniLabel);
                }
            }
            else
            {
                EditorGUILayout.PropertyField(frequency, FrequencyLabel);
            }

            EditorGUILayout.PropertyField(dampingRatio, DampingRatioLabel);
            EditorGUILayout.PropertyField(maxAngle, MaxAngleLabel);
            EditorGUILayout.PropertyField(saturationSoftness, SaturationSoftnessLabel);
            EditorGUILayout.PropertyField(sensitivity, SensitivityLabel);
            EditorGUILayout.PropertyField(gravityInfluence, GravityInfluenceLabel);
            EditorGUILayout.PropertyField(centrifugalInfluence, CentrifugalInfluenceLabel);
            EditorGUILayout.PropertyField(referenceGravity, ReferenceGravityLabel);
        }

        private void DrawStretchSection()
        {
            string summary = radialEnabled.boolValue
                ? "ΔL " + restElongation.floatValue.ToString("0.###") + "m / " + ResolveRadialFrequency().ToString("0.#") + "Hz"
                : "关";
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref stretchExpanded, "竖直拉伸", summary, StretchColor))
            {
                return;
            }

            EditorGUILayout.PropertyField(radialEnabled, RadialEnabledLabel);
            using (new EditorGUI.DisabledScope(!radialEnabled.boolValue))
            {
                EditorGUILayout.PropertyField(restElongation, RestElongationLabel);
                EditorGUILayout.PropertyField(radialDampingRatio, RadialDampingRatioLabel);

                if (restElongation.floatValue > 0.0f)
                {
                    EditorGUILayout.LabelField(
                        "  ",
                        "径向弹簧频率 " + ResolveRadialFrequency().ToString("0.###") + " Hz（周期 " +
                        (1.0f / Mathf.Max(0.0001f, ResolveRadialFrequency())).ToString("0.###") + " s）",
                        EditorStyles.miniLabel);
                }
                else
                {
                    EditorGUILayout.HelpBox("静止伸长为 0 表示径向刚性，竖直方向不会有伸缩。", MessageType.Info);
                }
            }
        }

        private void DrawSamplingSection()
        {
            string summary = estimationWindow.intValue + " 帧 + " + equilibriumSmoothing.floatValue.ToString("0") + "ms";
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref samplingExpanded, "采样", summary, SamplingColor))
            {
                return;
            }

            EditorGUILayout.HelpBox(
                "加速度不是逐帧二阶差分算出来的，而是对最近「估计窗口」帧的位置做二次最小二乘拟合：" +
                "对匀速与恒加速都精确，但对位置抖动/量化/低采样率的放大远小于二阶差分（实测 20Hz 采样台阶下相差约 28 倍）。\n" +
                "总响应延迟 ≈ (估计窗口-1)/2 帧 + 平衡角平滑。",
                MessageType.Info);

            EditorGUILayout.PropertyField(estimationWindow, EstimationWindowLabel);
            EditorGUILayout.PropertyField(equilibriumSmoothing, EquilibriumSmoothingLabel);
            EditorGUILayout.PropertyField(maxStep, MaxStepLabel);
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

        private void DrawInputSection()
        {
            string summary = driveSource.enumValueIndex == (int)HoPendulumDriveSource.Manual ? "生效中" : "未启用";
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref inputExpanded, "手动输入", summary, InputColor))
            {
                return;
            }

            if (driveSource.enumValueIndex != (int)HoPendulumDriveSource.Manual)
            {
                EditorGUILayout.HelpBox("驱动源不是「手动输入」时，本节参数不参与求解，可改由运动采样驱动。", MessageType.Info);
            }

            EditorGUILayout.PropertyField(manualAcceleration, ManualAccelerationLabel);
            EditorGUILayout.PropertyField(inputValue, InputValueLabel);
            EditorGUILayout.PropertyField(sourceValueMin, SourceValueMinLabel);
            EditorGUILayout.PropertyField(sourceValueMax, SourceValueMaxLabel);
            EditorGUILayout.PropertyField(inputAcceleration, InputAccelerationLabel);
            EditorGUILayout.PropertyField(inputAxis, InputAxisLabel);
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
                    "还没有绑定。点「+ 添加绑定」或工具栏的「水瓶液面」预设来创建通道到材质属性的连接。",
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
        }

        private void DrawBindingElement(SerializedProperty element, int index)
        {
            SerializedProperty enabled = element.FindPropertyRelative("enabled");
            SerializedProperty label = element.FindPropertyRelative("label");
            SerializedProperty channel = element.FindPropertyRelative("channel");
            SerializedProperty target = element.FindPropertyRelative("target");

            while (bindingFoldouts.Count <= index)
            {
                bindingFoldouts.Add(index == 0);
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
                            new GUIContent("包含子物体", "渲染器列表为空时，是否连同子物体的渲染器一起写入。"));
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
            element.FindPropertyRelative("propertyName").stringValue = "_WobbleX";
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
                        "  包围盒测得",
                        probe.GizmoReferenceSize.ToString("0.###") + " m（含整体缩放）",
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
                EditorGUILayout.FloatField("倾角 X", constraint.AngleX);
                EditorGUILayout.FloatField("倾角 Z", constraint.AngleZ);
                EditorGUILayout.FloatField("合倾角", constraint.Angle);
                EditorGUILayout.FloatField("平衡倾角 X", constraint.EquilibriumAngleX);
                EditorGUILayout.FloatField("平衡倾角 Z", constraint.EquilibriumAngleZ);
                EditorGUILayout.FloatField("液面斜率 X", constraint.TiltX);
                EditorGUILayout.FloatField("液面斜率 Z", constraint.TiltZ);
                EditorGUILayout.FloatField("振幅", constraint.Amplitude);
                EditorGUILayout.FloatField("相位", constraint.Phase);
                EditorGUILayout.FloatField("有效重力 (g)", constraint.EffectiveGravity);
                EditorGUILayout.FloatField("伸长量 (m)", constraint.Stretch);
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
                constraint.AddRendererPropertyBinding(
                    HoPendulumChannel.TiltX,
                    "_WobbleX",
                    BottleTiltScale,
                    Vector3.zero,
                    "液面摆动 X");
                constraint.AddRendererPropertyBinding(
                    HoPendulumChannel.TiltZ,
                    "_WobbleZ",
                    BottleTiltScale,
                    Vector3.zero,
                    "液面摆动 Z");
                EditorUtility.SetDirty(constraint);
            }

            bindingFoldouts.Clear();
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
            SetFloat(equilibriumSmoothing, 20.0f);
            SetVector3(manualAcceleration, Vector3.zero);
            SetFloat(inputValue, 0.0f);
            SetFloat(sourceValueMin, 0.0f);
            SetFloat(sourceValueMax, 1.0f);
            SetFloat(inputAcceleration, 9.81f);
            SetVector3(inputAxis, Vector3.right);
            SetBool(gizmoSizeFromBounds, true);
            SetFloat(gizmoSize, 0.5f);
            SetFloat(gizmoScale, 1.0f);
            SetFloat(accelerationGizmoScale, 0.5f);
            SetFloat(accelerationGizmoMaxScale, 2.0f);
        }

        private void SetBottlePresetDefaults()
        {
            SetDefaultsForAllPresets();

            // 液面晃动由容器与液体决定，和摆长无关：默认给 3Hz，
            // 手持运动频段在 0.5~1.5Hz，放在 3Hz 可以把相位滞后压到几度，
            // 也避免手抖落在谐振点上被 Q=1/(2ζ) 放大成来回翻。
            SetBool(frequencyFromLength, false);
            SetFloat(frequency, 3.0f);
            SetFloat(length, 0.25f);
            SetFloat(dampingRatio, 0.22f);
            SetFloat(maxAngle, 20.0f);
            SetFloat(saturationSoftness, 0.65f);
            // 灵敏度 1 是物理值（tanθ = a/g），但 a 只要 3.6m/s² 就会顶到 20° 上限，
            // 场景里拖动很容易超过，结果是碟子长期贴在极限处翻。
            // 0.5 把常见手持运动放到量程中段：a=1 约 2.9°、a=3 约 8.7°、a=7 才接近上限。
            SetFloat(sensitivity, 0.5f);
            SetFloat(restElongation, 0.015f);
            SetInt(estimationWindow, HoMotionEstimator.DefaultWindow);
            SetFloat(equilibriumSmoothing, 20.0f);
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
