using Hollow.HoUnityTools.Constraints;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Constraints
{
    [CustomEditor(typeof(HoFollowConstraint))]
    internal sealed class HoFollowConstraintEditor : UnityEditor.Editor
    {
        private SerializedProperty targetProperty;
        private SerializedProperty updateMode;
        private SerializedProperty evaluateInEditMode;
        private SerializedProperty initializeOnEnable;
        private SerializedProperty hasInitialTransform;
        private SerializedProperty initialLocalPosition;
        private SerializedProperty initialLocalRotation;
        private SerializedProperty initialLocalScale;
        private SerializedProperty positionFollow;
        private SerializedProperty rotationFollow;
        private SerializedProperty response;
        private SerializedProperty overshoot;
        private SerializedProperty maxVelocity;
        private SerializedProperty maxAngularVelocity;
        private SerializedProperty lockX;
        private SerializedProperty lockY;
        private SerializedProperty lockZ;
        private SerializedProperty lockPitch;
        private SerializedProperty lockYaw;
        private SerializedProperty lockRoll;
        private SerializedProperty rotationMode;
        private SerializedProperty keepHorizon;
        private SerializedProperty followYaw;
        private SerializedProperty followPitch;
        private SerializedProperty followRoll;
        private SerializedProperty oscillationEnabled;
        private SerializedProperty oscillationMultiplier;
        private SerializedProperty oscillationWaveform;
        private SerializedProperty oscillationCurve;
        private SerializedProperty oscillationFrequency;
        private SerializedProperty oscillationPhase;
        private SerializedProperty oscillationPositionAmplitude;
        private SerializedProperty oscillationRotationAmplitude;
        private SerializedProperty oscillationScaleAmplitude;
        private SerializedProperty oscillationAxisWeight;
        private SerializedProperty noiseEnabled;
        private SerializedProperty noiseMultiplier;
        private SerializedProperty noiseSpace;
        private SerializedProperty noiseFrequency;
        private SerializedProperty noiseSeed;
        private SerializedProperty noisePositionAmplitude;
        private SerializedProperty noiseRotationAmplitude;
        private SerializedProperty noiseScaleAmplitude;
        private SerializedProperty limitEnabled;
        private SerializedProperty limitShape;
        private SerializedProperty limitRadius;
        private SerializedProperty limitBoxSize;
        private SerializedProperty limitCylinderHeight;
        private SerializedProperty limitSoftness;
        private SerializedProperty limitClamp;
        private SerializedProperty offsetMode;
        private SerializedProperty positionOffset;
        private SerializedProperty rotationOffset;
        private SerializedProperty drawGizmos;
        private SerializedProperty drawMotionTrail;
        private SerializedProperty motionTrailLength;
        private SerializedProperty gizmoColor;

        private bool targetExpanded = true;
        private bool followExpanded = true;
        private bool axisExpanded = true;
        private bool rotationExpanded = true;
        private bool oscillationExpanded;
        private bool noiseExpanded;
        private bool limitExpanded;
        private bool offsetExpanded = true;
        private bool debugExpanded;

        private static readonly Color TargetColor = new Color(0.28f, 0.62f, 1.0f);
        private static readonly Color FollowColor = new Color(0.24f, 0.86f, 0.58f);
        private static readonly Color AxisColor = new Color(1.0f, 0.70f, 0.28f);
        private static readonly Color RotationColor = new Color(0.78f, 0.48f, 1.0f);
        private static readonly Color MotionColor = new Color(0.32f, 0.86f, 0.92f);
        private static readonly Color LimitColor = new Color(1.0f, 0.45f, 0.38f);
        private static readonly Color DebugColor = new Color(0.70f, 0.72f, 0.76f);

        private static readonly GUIContent TargetLabel = new GUIContent("目标", "被跟随的 Transform。");
        private static readonly GUIContent UpdateModeLabel = new GUIContent("更新时机", "约束求值发生在哪个 Unity 更新阶段。");
        private static readonly GUIContent EvaluateInEditModeLabel = new GUIContent("编辑模式求值", "未进入播放模式时也持续更新。");
        private static readonly GUIContent InitializeOnEnableLabel = new GUIContent("启用时重置锚点", "组件启用时用当前 Transform 作为锁定与跟随的初始锚点。");
        private static readonly GUIContent InitialLocalPositionLabel = new GUIContent("初始位置", "保存的本地初始位置。");
        private static readonly GUIContent InitialLocalRotationLabel = new GUIContent("初始旋转", "保存的本地初始旋转。");
        private static readonly GUIContent InitialLocalScaleLabel = new GUIContent("初始缩放", "保存的本地初始缩放。");
        private static readonly GUIContent PositionFollowLabel = new GUIContent("位置跟随", "0 为保持锚点，1 为完全贴近目标位置。");
        private static readonly GUIContent RotationFollowLabel = new GUIContent("旋转跟随", "0 为保持锚点，1 为完全贴近目标旋转。");
        private static readonly GUIContent ResponseLabel = new GUIContent("响应", "收敛速度。值越大，越快追上目标。");
        private static readonly GUIContent OvershootLabel = new GUIContent("超调", "增加甩过头再回弹的风格化运动。");
        private static readonly GUIContent MaxVelocityLabel = new GUIContent("最大速度", "0 表示不限制线速度。");
        private static readonly GUIContent MaxAngularVelocityLabel = new GUIContent("最大角速度", "0 表示不限制角速度，单位为度/秒。");
        private static readonly GUIContent RotationModeLabel = new GUIContent("旋转模式", "目标旋转的解释方式。");
        private static readonly GUIContent KeepHorizonLabel = new GUIContent("保持水平", "忽略目标俯仰和翻滚，适合光环、头顶特效等。");
        private static readonly GUIContent FollowPitchLabel = new GUIContent("跟随俯仰");
        private static readonly GUIContent FollowYawLabel = new GUIContent("跟随偏航");
        private static readonly GUIContent FollowRollLabel = new GUIContent("跟随翻滚");
        private static readonly GUIContent OscillationEnabledLabel = new GUIContent("启用呼吸");
        private static readonly GUIContent OscillationMultiplierLabel = new GUIContent("整体倍率", "统一缩放呼吸的位置、旋转和缩放振幅。");
        private static readonly GUIContent OscillationWaveformLabel = new GUIContent("波形");
        private static readonly GUIContent OscillationCurveLabel = new GUIContent("自定义曲线");
        private static readonly GUIContent OscillationFrequencyLabel = new GUIContent("频率");
        private static readonly GUIContent OscillationPhaseLabel = new GUIContent("相位");
        private static readonly GUIContent OscillationAxisWeightLabel = new GUIContent("轴权重");
        private static readonly GUIContent OscillationPositionLabel = new GUIContent("位置振幅");
        private static readonly GUIContent OscillationRotationLabel = new GUIContent("旋转振幅");
        private static readonly GUIContent OscillationScaleLabel = new GUIContent("缩放振幅");
        private static readonly GUIContent NoiseEnabledLabel = new GUIContent("启用噪声");
        private static readonly GUIContent NoiseMultiplierLabel = new GUIContent("整体倍率", "统一缩放噪声的位置、旋转和缩放振幅。");
        private static readonly GUIContent NoiseSpaceLabel = new GUIContent("噪声空间");
        private static readonly GUIContent NoiseFrequencyLabel = new GUIContent("频率");
        private static readonly GUIContent NoiseSeedLabel = new GUIContent("种子");
        private static readonly GUIContent NoisePositionLabel = new GUIContent("位置噪声");
        private static readonly GUIContent NoiseRotationLabel = new GUIContent("旋转噪声");
        private static readonly GUIContent NoiseScaleLabel = new GUIContent("缩放噪声");
        private static readonly GUIContent LimitEnabledLabel = new GUIContent("启用限制");
        private static readonly GUIContent LimitShapeLabel = new GUIContent("限制形状");
        private static readonly GUIContent LimitRadiusLabel = new GUIContent("半径");
        private static readonly GUIContent LimitBoxSizeLabel = new GUIContent("盒体尺寸");
        private static readonly GUIContent LimitCylinderHeightLabel = new GUIContent("圆柱高度");
        private static readonly GUIContent LimitSoftnessLabel = new GUIContent("柔和度");
        private static readonly GUIContent LimitClampLabel = new GUIContent("硬夹取");
        private static readonly GUIContent OffsetModeLabel = new GUIContent("偏移空间");
        private static readonly GUIContent PositionOffsetLabel = new GUIContent("位置偏移");
        private static readonly GUIContent RotationOffsetLabel = new GUIContent("旋转偏移");
        private static readonly GUIContent DrawGizmosLabel = new GUIContent("显示 Gizmo");
        private static readonly GUIContent GizmoColorLabel = new GUIContent("Gizmo 颜色");
        private static readonly GUIContent DrawMotionTrailLabel = new GUIContent("显示运动轨迹");
        private static readonly GUIContent MotionTrailLengthLabel = new GUIContent("轨迹长度");

        private static readonly GUIContent[] UpdateModeLabels =
        {
            new GUIContent("LateUpdate"),
            new GUIContent("Update"),
            new GUIContent("FixedUpdate"),
            new GUIContent("手动")
        };

        private static readonly GUIContent[] RotationModeLabels =
        {
            new GUIContent("世界"),
            new GUIContent("本地"),
            new GUIContent("目标相对")
        };

        private static readonly GUIContent[] WaveformLabels =
        {
            new GUIContent("正弦"),
            new GUIContent("三角"),
            new GUIContent("曲线")
        };

        private static readonly GUIContent[] SpaceLabels =
        {
            new GUIContent("世界"),
            new GUIContent("本地")
        };

        private static readonly GUIContent[] LimitShapeLabels =
        {
            new GUIContent("球体"),
            new GUIContent("盒体"),
            new GUIContent("圆柱")
        };

        private static readonly int[] FourEnumValues = { 0, 1, 2, 3 };
        private static readonly int[] ThreeEnumValues = { 0, 1, 2 };
        private static readonly int[] TwoEnumValues = { 0, 1 };

        private void OnEnable()
        {
            targetProperty = Find("target");
            updateMode = Find("updateMode");
            evaluateInEditMode = Find("evaluateInEditMode");
            initializeOnEnable = Find("initializeOnEnable");
            hasInitialTransform = Find("hasInitialTransform");
            initialLocalPosition = Find("initialLocalPosition");
            initialLocalRotation = Find("initialLocalRotation");
            initialLocalScale = Find("initialLocalScale");
            positionFollow = Find("positionFollow");
            rotationFollow = Find("rotationFollow");
            response = Find("response");
            overshoot = Find("overshoot");
            maxVelocity = Find("maxVelocity");
            maxAngularVelocity = Find("maxAngularVelocity");
            lockX = Find("lockX");
            lockY = Find("lockY");
            lockZ = Find("lockZ");
            lockPitch = Find("lockPitch");
            lockYaw = Find("lockYaw");
            lockRoll = Find("lockRoll");
            rotationMode = Find("rotationMode");
            keepHorizon = Find("keepHorizon");
            followYaw = Find("followYaw");
            followPitch = Find("followPitch");
            followRoll = Find("followRoll");
            oscillationEnabled = Find("oscillationEnabled");
            oscillationMultiplier = Find("oscillationMultiplier");
            oscillationWaveform = Find("oscillationWaveform");
            oscillationCurve = Find("oscillationCurve");
            oscillationFrequency = Find("oscillationFrequency");
            oscillationPhase = Find("oscillationPhase");
            oscillationPositionAmplitude = Find("oscillationPositionAmplitude");
            oscillationRotationAmplitude = Find("oscillationRotationAmplitude");
            oscillationScaleAmplitude = Find("oscillationScaleAmplitude");
            oscillationAxisWeight = Find("oscillationAxisWeight");
            noiseEnabled = Find("noiseEnabled");
            noiseMultiplier = Find("noiseMultiplier");
            noiseSpace = Find("noiseSpace");
            noiseFrequency = Find("noiseFrequency");
            noiseSeed = Find("noiseSeed");
            noisePositionAmplitude = Find("noisePositionAmplitude");
            noiseRotationAmplitude = Find("noiseRotationAmplitude");
            noiseScaleAmplitude = Find("noiseScaleAmplitude");
            limitEnabled = Find("limitEnabled");
            limitShape = Find("limitShape");
            limitRadius = Find("limitRadius");
            limitBoxSize = Find("limitBoxSize");
            limitCylinderHeight = Find("limitCylinderHeight");
            limitSoftness = Find("limitSoftness");
            limitClamp = Find("limitClamp");
            offsetMode = Find("offsetMode");
            positionOffset = Find("positionOffset");
            rotationOffset = Find("rotationOffset");
            drawGizmos = Find("drawGizmos");
            drawMotionTrail = Find("drawMotionTrail");
            motionTrailLength = Find("motionTrailLength");
            gizmoColor = Find("gizmoColor");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawPresetToolbar();
            EditorGUILayout.Space(4.0f);

            DrawTargetSection();
            DrawFollowSection();
            DrawAxisSection();
            DrawRotationSection();
            DrawOscillationSection();
            DrawNoiseSection();
            DrawLimitSection();
            DrawOffsetSection();
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
            EditorGUILayout.LabelField("Ho 跟随约束", EditorStyles.boldLabel);
            Rect rect = EditorGUILayout.GetControlRect(false, 22.0f);
            float width = rect.width / 5.0f;
            if (GUI.Button(new Rect(rect.x, rect.y, width - 2.0f, rect.height), "清空"))
            {
                ApplyPreset(-1);
            }

            if (GUI.Button(new Rect(rect.x + width, rect.y, width - 2.0f, rect.height), "光环"))
            {
                ApplyPreset(0);
            }

            if (GUI.Button(new Rect(rect.x + width * 2.0f, rect.y, width - 2.0f, rect.height), "武器"))
            {
                ApplyPreset(1);
            }

            if (GUI.Button(new Rect(rect.x + width * 3.0f, rect.y, width - 2.0f, rect.height), "背包"))
            {
                ApplyPreset(2);
            }

            if (GUI.Button(new Rect(rect.x + width * 4.0f, rect.y, width, rect.height), "无人机"))
            {
                ApplyPreset(3);
            }
        }

        private void DrawTargetSection()
        {
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref targetExpanded, "目标", GetUpdateModeSummary(), TargetColor))
            {
                return;
            }

            EditorGUILayout.PropertyField(targetProperty, TargetLabel);
            DrawEnumPopup(updateMode, UpdateModeLabel, UpdateModeLabels, FourEnumValues);
            EditorGUILayout.PropertyField(evaluateInEditMode, EvaluateInEditModeLabel);
            EditorGUILayout.PropertyField(initializeOnEnable, InitializeOnEnableLabel);
            DrawInitialTransformControls();
            DrawMissingTargetHelp();
        }

        private void DrawInitialTransformControls()
        {
            EditorGUILayout.Space(3.0f);
            EditorGUILayout.LabelField("初始变换缓存", EditorStyles.boldLabel);

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

        private void DrawFollowSection()
        {
            string summary = "位置 " + HoConstraintEditorSectionGui.FloatSummary(positionFollow) + " / 旋转 " + HoConstraintEditorSectionGui.FloatSummary(rotationFollow);
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref followExpanded, "跟随", summary, FollowColor))
            {
                return;
            }

            EditorGUILayout.Slider(positionFollow, 0.0f, 1.0f, PositionFollowLabel);
            EditorGUILayout.Slider(rotationFollow, 0.0f, 1.0f, RotationFollowLabel);
            EditorGUILayout.Slider(response, 0.0f, 10.0f, ResponseLabel);
            EditorGUILayout.Slider(overshoot, 0.0f, 1.0f, OvershootLabel);
            EditorGUILayout.PropertyField(maxVelocity, MaxVelocityLabel);
            EditorGUILayout.PropertyField(maxAngularVelocity, MaxAngularVelocityLabel);
        }

        private void DrawAxisSection()
        {
            string summary = string.Format("XYZ {0:0.#}/{1:0.#}/{2:0.#}", lockX.floatValue, lockY.floatValue, lockZ.floatValue);
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref axisExpanded, "轴约束", summary, AxisColor))
            {
                return;
            }

            EditorGUILayout.LabelField("位置锁定", EditorStyles.boldLabel);
            EditorGUILayout.Slider(lockX, 0.0f, 1.0f, new GUIContent("锁定 X"));
            EditorGUILayout.Slider(lockY, 0.0f, 1.0f, new GUIContent("锁定 Y"));
            EditorGUILayout.Slider(lockZ, 0.0f, 1.0f, new GUIContent("锁定 Z"));

            EditorGUILayout.Space(3.0f);
            EditorGUILayout.LabelField("旋转锁定", EditorStyles.boldLabel);
            EditorGUILayout.Slider(lockPitch, 0.0f, 1.0f, new GUIContent("俯仰"));
            EditorGUILayout.Slider(lockYaw, 0.0f, 1.0f, new GUIContent("偏航"));
            EditorGUILayout.Slider(lockRoll, 0.0f, 1.0f, new GUIContent("翻滚"));
        }

        private void DrawRotationSection()
        {
            string summary = GetRotationModeSummary();
            if (keepHorizon.boolValue)
            {
                summary += " / 水平";
            }

            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref rotationExpanded, "旋转", summary, RotationColor))
            {
                return;
            }

            DrawEnumPopup(rotationMode, RotationModeLabel, RotationModeLabels, ThreeEnumValues);
            EditorGUILayout.PropertyField(keepHorizon, KeepHorizonLabel);
            using (new EditorGUI.DisabledScope(keepHorizon.boolValue))
            {
                EditorGUILayout.PropertyField(followPitch, FollowPitchLabel);
                EditorGUILayout.PropertyField(followRoll, FollowRollLabel);
            }

            EditorGUILayout.PropertyField(followYaw, FollowYawLabel);
        }

        private void DrawOscillationSection()
        {
            string summary = HoConstraintEditorSectionGui.BoolSummary(oscillationEnabled);
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref oscillationExpanded, "呼吸", summary, MotionColor))
            {
                return;
            }

            EditorGUILayout.PropertyField(oscillationEnabled, OscillationEnabledLabel);
            using (new EditorGUI.DisabledScope(!oscillationEnabled.boolValue))
            {
                EditorGUILayout.PropertyField(oscillationMultiplier, OscillationMultiplierLabel);
                DrawEnumPopup(oscillationWaveform, OscillationWaveformLabel, WaveformLabels, ThreeEnumValues);
                if (oscillationWaveform.enumValueIndex == (int)HoFollowConstraintWaveform.Curve)
                {
                    EditorGUILayout.PropertyField(oscillationCurve, OscillationCurveLabel);
                }

                EditorGUILayout.PropertyField(oscillationFrequency, OscillationFrequencyLabel);
                EditorGUILayout.PropertyField(oscillationPhase, OscillationPhaseLabel);
                EditorGUILayout.PropertyField(oscillationAxisWeight, OscillationAxisWeightLabel);
                EditorGUILayout.PropertyField(oscillationPositionAmplitude, OscillationPositionLabel);
                EditorGUILayout.PropertyField(oscillationRotationAmplitude, OscillationRotationLabel);
                EditorGUILayout.PropertyField(oscillationScaleAmplitude, OscillationScaleLabel);
            }
        }

        private void DrawNoiseSection()
        {
            string summary = HoConstraintEditorSectionGui.BoolSummary(noiseEnabled);
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref noiseExpanded, "噪声", summary, MotionColor))
            {
                return;
            }

            EditorGUILayout.PropertyField(noiseEnabled, NoiseEnabledLabel);
            using (new EditorGUI.DisabledScope(!noiseEnabled.boolValue))
            {
                EditorGUILayout.PropertyField(noiseMultiplier, NoiseMultiplierLabel);
                DrawEnumPopup(noiseSpace, NoiseSpaceLabel, SpaceLabels, TwoEnumValues);
                EditorGUILayout.PropertyField(noiseFrequency, NoiseFrequencyLabel);
                EditorGUILayout.PropertyField(noiseSeed, NoiseSeedLabel);
                EditorGUILayout.PropertyField(noisePositionAmplitude, NoisePositionLabel);
                EditorGUILayout.PropertyField(noiseRotationAmplitude, NoiseRotationLabel);
                EditorGUILayout.PropertyField(noiseScaleAmplitude, NoiseScaleLabel);
            }
        }

        private void DrawLimitSection()
        {
            string summary = limitEnabled.boolValue ? GetLimitShapeSummary() : "关";
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref limitExpanded, "限制", summary, LimitColor))
            {
                return;
            }

            EditorGUILayout.PropertyField(limitEnabled, LimitEnabledLabel);
            using (new EditorGUI.DisabledScope(!limitEnabled.boolValue))
            {
                DrawEnumPopup(limitShape, LimitShapeLabel, LimitShapeLabels, ThreeEnumValues);
                switch ((HoFollowConstraintLimitShape)limitShape.enumValueIndex)
                {
                    case HoFollowConstraintLimitShape.Box:
                        EditorGUILayout.PropertyField(limitBoxSize, LimitBoxSizeLabel);
                        break;
                    case HoFollowConstraintLimitShape.Cylinder:
                        EditorGUILayout.PropertyField(limitRadius, LimitRadiusLabel);
                        EditorGUILayout.PropertyField(limitCylinderHeight, LimitCylinderHeightLabel);
                        break;
                    case HoFollowConstraintLimitShape.Sphere:
                    default:
                        EditorGUILayout.PropertyField(limitRadius, LimitRadiusLabel);
                        break;
                }

                EditorGUILayout.PropertyField(limitSoftness, LimitSoftnessLabel);
                EditorGUILayout.PropertyField(limitClamp, LimitClampLabel);
            }
        }

        private void DrawOffsetSection()
        {
            string summary = GetOffsetModeSummary();
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref offsetExpanded, "偏移", summary, TargetColor))
            {
                return;
            }

            DrawEnumPopup(offsetMode, OffsetModeLabel, SpaceLabels, TwoEnumValues);
            EditorGUILayout.PropertyField(positionOffset, PositionOffsetLabel);
            EditorGUILayout.PropertyField(rotationOffset, RotationOffsetLabel);
        }

        private void DrawDebugSection()
        {
            string summary = HoConstraintEditorSectionGui.BoolSummary(drawGizmos);
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref debugExpanded, "调试", summary, DebugColor))
            {
                return;
            }

            EditorGUILayout.PropertyField(drawGizmos, DrawGizmosLabel);
            using (new EditorGUI.DisabledScope(!drawGizmos.boolValue))
            {
                EditorGUILayout.PropertyField(gizmoColor, GizmoColorLabel);
                EditorGUILayout.PropertyField(drawMotionTrail, DrawMotionTrailLabel);
                using (new EditorGUI.DisabledScope(!drawMotionTrail.boolValue))
                {
                    EditorGUILayout.PropertyField(motionTrailLength, MotionTrailLengthLabel);
                }
            }

            DrawRuntimeReadout();
        }

        private void DrawMissingTargetHelp()
        {
            if (targetProperty.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("未指定目标时，组件会以当前锚点为基准应用偏移、呼吸与噪声。指定目标后会额外进行跟随。", MessageType.Info);
            }
        }

        private void DrawRuntimeReadout()
        {
            if (targets.Length != 1 || !(serializedObject.targetObject is HoFollowConstraint constraint))
            {
                return;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Vector3Field("当前位置", constraint.CurrentPosition);
                EditorGUILayout.Vector3Field("速度", constraint.Velocity);
                EditorGUILayout.Vector3Field("角速度", constraint.AngularVelocity);
            }
        }

        private void DrawActionButtons()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("重置锚点"))
            {
                foreach (Object selectedTarget in targets)
                {
                    if (selectedTarget is HoFollowConstraint constraint)
                    {
                        Undo.RecordObject(constraint.transform, "重置跟随约束锚点");
                        constraint.ResetState();
                        EditorUtility.SetDirty(constraint);
                    }
                }
            }

            if (GUILayout.Button("吸附到目标"))
            {
                foreach (Object selectedTarget in targets)
                {
                    if (selectedTarget is HoFollowConstraint constraint)
                    {
                        Undo.RecordObject(constraint.transform, "跟随约束吸附到目标");
                        constraint.SnapToTarget();
                        EditorUtility.SetDirty(constraint);
                    }
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void ApplyPreset(int preset)
        {
            serializedObject.Update();

            SaveInitialTransformForTargets();
            serializedObject.Update();

            SetDefaultsForAllPresets();
            if (preset < 0)
            {
                serializedObject.ApplyModifiedProperties();
                return;
            }

            switch (preset)
            {
                case 0:
                    SetFloat(positionFollow, 1.0f);
                    SetFloat(rotationFollow, 1.0f);
                    SetFloat(lockY, 0.8f);
                    SetBool(keepHorizon, true);
                    SetBool(oscillationEnabled, true);
                    SetVector3(oscillationPositionAmplitude, new Vector3(0.0f, 0.025f, 0.0f));
                    SetVector3(oscillationRotationAmplitude, new Vector3(0.0f, 0.3f, 0.0f));
                    SetFloat(oscillationFrequency, 0.35f);
                    break;
                case 1:
                    SetFloat(positionFollow, 1.0f);
                    SetFloat(rotationFollow, 1.0f);
                    SetBool(noiseEnabled, true);
                    SetVector3(noisePositionAmplitude, Vector3.one * 0.012f);
                    SetVector3(noiseRotationAmplitude, Vector3.one * 0.35f);
                    break;
                case 2:
                    SetFloat(positionFollow, 1.0f);
                    SetFloat(rotationFollow, 1.0f);
                    SetBool(noiseEnabled, true);
                    SetVector3(noisePositionAmplitude, Vector3.one * 0.018f);
                    SetVector3(noiseRotationAmplitude, Vector3.one * 0.25f);
                    break;
                case 3:
                    SetFloat(positionFollow, 1.0f);
                    SetFloat(rotationFollow, 1.0f);
                    SetBool(followYaw, true);
                    SetBool(followPitch, false);
                    SetBool(followRoll, false);
                    SetBool(noiseEnabled, true);
                    SetVector3(noisePositionAmplitude, Vector3.one * 0.02f);
                    SetVector3(noiseRotationAmplitude, new Vector3(0.0f, 0.45f, 0.0f));
                    break;
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void SaveInitialTransformForTargets()
        {
            serializedObject.ApplyModifiedProperties();
            foreach (Object selectedTarget in targets)
            {
                if (selectedTarget is HoFollowConstraint constraint)
                {
                    Undo.RecordObject(constraint, "保存跟随约束初始变换");
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
                if (selectedTarget is HoFollowConstraint constraint)
                {
                    Undo.RecordObjects(new Object[] { constraint, constraint.transform }, "恢复跟随约束初始变换");
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
                if (selectedTarget is HoFollowConstraint constraint)
                {
                    Undo.RecordObject(constraint, "清除跟随约束初始变换");
                    constraint.ClearInitialTransform();
                    EditorUtility.SetDirty(constraint);
                }
            }

            serializedObject.Update();
        }

        private void SetDefaultsForAllPresets()
        {
            SetFloat(positionFollow, 1.0f);
            SetFloat(rotationFollow, 1.0f);
            SetFloat(response, 10.0f);
            SetFloat(overshoot, 0.0f);
            SetFloat(maxVelocity, 0.0f);
            SetFloat(maxAngularVelocity, 0.0f);
            SetFloat(lockX, 0.0f);
            SetFloat(lockY, 0.0f);
            SetFloat(lockZ, 0.0f);
            SetFloat(lockPitch, 0.0f);
            SetFloat(lockYaw, 0.0f);
            SetFloat(lockRoll, 0.0f);
            SetEnum(rotationMode, HoFollowConstraintRotationMode.World);
            SetBool(keepHorizon, false);
            SetBool(followYaw, true);
            SetBool(followPitch, true);
            SetBool(followRoll, true);
            SetBool(oscillationEnabled, false);
            SetEnum(oscillationWaveform, HoFollowConstraintWaveform.Sin);
            SetFloat(oscillationMultiplier, 1.0f);
            SetFloat(oscillationFrequency, 1.0f);
            SetFloat(oscillationPhase, 0.0f);
            SetVector3(oscillationAxisWeight, Vector3.up);
            SetVector3(oscillationPositionAmplitude, Vector3.zero);
            SetVector3(oscillationRotationAmplitude, Vector3.zero);
            SetVector3(oscillationScaleAmplitude, Vector3.zero);
            SetBool(noiseEnabled, false);
            SetFloat(noiseMultiplier, 1.0f);
            SetEnum(noiseSpace, HoFollowConstraintNoiseSpace.Local);
            SetFloat(noiseFrequency, 1.0f);
            SetVector3(noisePositionAmplitude, Vector3.zero);
            SetVector3(noiseRotationAmplitude, Vector3.zero);
            SetVector3(noiseScaleAmplitude, Vector3.zero);
            SetBool(limitEnabled, false);
            SetEnum(limitShape, HoFollowConstraintLimitShape.Sphere);
            SetFloat(limitRadius, 1.0f);
            SetVector3(limitBoxSize, Vector3.one);
            SetFloat(limitCylinderHeight, 1.0f);
            SetFloat(limitSoftness, 0.2f);
            SetBool(limitClamp, true);
            SetEnum(offsetMode, HoFollowConstraintOffsetMode.Local);
            SetVector3(positionOffset, Vector3.zero);
            SetVector3(rotationOffset, Vector3.zero);
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

        private string GetUpdateModeSummary()
        {
            return GetPopupSummary(updateMode, UpdateModeLabels);
        }

        private string GetRotationModeSummary()
        {
            return GetPopupSummary(rotationMode, RotationModeLabels);
        }

        private string GetLimitShapeSummary()
        {
            return GetPopupSummary(limitShape, LimitShapeLabels);
        }

        private string GetOffsetModeSummary()
        {
            return GetPopupSummary(offsetMode, SpaceLabels);
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
    }
}
