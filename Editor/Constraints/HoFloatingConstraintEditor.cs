using Hollow.HoUnityTools.Constraints;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Constraints
{
    [CustomEditor(typeof(HoFloatingConstraint))]
    internal sealed class HoFloatingConstraintEditor : UnityEditor.Editor
    {
        private SerializedProperty updateMode;
        private SerializedProperty evaluateInEditMode;
        private SerializedProperty initializeOnEnable;
        private SerializedProperty hasInitialTransform;
        private SerializedProperty initialLocalPosition;
        private SerializedProperty initialLocalRotation;
        private SerializedProperty initialLocalScale;
        private SerializedProperty offsetSpace;
        private SerializedProperty positionOffset;
        private SerializedProperty rotationOffset;
        private SerializedProperty scaleOffset;
        private SerializedProperty oscillationEnabled;
        private SerializedProperty oscillationMultiplier;
        private SerializedProperty oscillationSpace;
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
        private SerializedProperty drawGizmos;
        private SerializedProperty gizmoColor;

        private bool updateExpanded = true;
        private bool initialExpanded = true;
        private bool offsetExpanded = true;
        private bool oscillationExpanded = true;
        private bool noiseExpanded;
        private bool debugExpanded;

        private static readonly Color UpdateColor = new Color(0.28f, 0.62f, 1.0f);
        private static readonly Color InitialColor = new Color(0.24f, 0.86f, 0.58f);
        private static readonly Color OffsetColor = new Color(0.78f, 0.48f, 1.0f);
        private static readonly Color MotionColor = new Color(0.32f, 0.86f, 0.92f);
        private static readonly Color DebugColor = new Color(0.70f, 0.72f, 0.76f);

        private static readonly GUIContent UpdateModeLabel = new GUIContent("更新时机", "漂浮约束在哪个 Unity 更新阶段求值。");
        private static readonly GUIContent EvaluateInEditModeLabel = new GUIContent("编辑模式求值", "未进入播放模式时也持续更新。");
        private static readonly GUIContent InitializeOnEnableLabel = new GUIContent("启用时重置锚点", "组件启用时用当前 Transform 作为漂浮基准。");
        private static readonly GUIContent InitialLocalPositionLabel = new GUIContent("初始位置", "保存的本地初始位置。");
        private static readonly GUIContent InitialLocalRotationLabel = new GUIContent("初始旋转", "保存的本地初始旋转。");
        private static readonly GUIContent InitialLocalScaleLabel = new GUIContent("初始缩放", "保存的本地初始缩放。");
        private static readonly GUIContent OffsetSpaceLabel = new GUIContent("偏移空间");
        private static readonly GUIContent PositionOffsetLabel = new GUIContent("位置偏移");
        private static readonly GUIContent RotationOffsetLabel = new GUIContent("旋转偏移");
        private static readonly GUIContent ScaleOffsetLabel = new GUIContent("缩放偏移");
        private static readonly GUIContent OscillationEnabledLabel = new GUIContent("启用呼吸");
        private static readonly GUIContent OscillationMultiplierLabel = new GUIContent("整体倍率", "统一缩放呼吸的位置、旋转和缩放振幅。");
        private static readonly GUIContent OscillationSpaceLabel = new GUIContent("呼吸空间");
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
        private static readonly GUIContent DrawGizmosLabel = new GUIContent("显示 Gizmo");
        private static readonly GUIContent GizmoColorLabel = new GUIContent("Gizmo 颜色");

        private static readonly GUIContent[] UpdateModeLabels =
        {
            new GUIContent("LateUpdate"),
            new GUIContent("Update"),
            new GUIContent("FixedUpdate"),
            new GUIContent("手动")
        };

        private static readonly GUIContent[] SpaceLabels =
        {
            new GUIContent("世界"),
            new GUIContent("本地")
        };

        private static readonly GUIContent[] WaveformLabels =
        {
            new GUIContent("正弦"),
            new GUIContent("三角"),
            new GUIContent("曲线")
        };

        private static readonly int[] FourEnumValues = { 0, 1, 2, 3 };
        private static readonly int[] ThreeEnumValues = { 0, 1, 2 };
        private static readonly int[] TwoEnumValues = { 0, 1 };

        private void OnEnable()
        {
            updateMode = Find("updateMode");
            evaluateInEditMode = Find("evaluateInEditMode");
            initializeOnEnable = Find("initializeOnEnable");
            hasInitialTransform = Find("hasInitialTransform");
            initialLocalPosition = Find("initialLocalPosition");
            initialLocalRotation = Find("initialLocalRotation");
            initialLocalScale = Find("initialLocalScale");
            offsetSpace = Find("offsetSpace");
            positionOffset = Find("positionOffset");
            rotationOffset = Find("rotationOffset");
            scaleOffset = Find("scaleOffset");
            oscillationEnabled = Find("oscillationEnabled");
            oscillationMultiplier = Find("oscillationMultiplier");
            oscillationSpace = Find("oscillationSpace");
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
            drawGizmos = Find("drawGizmos");
            gizmoColor = Find("gizmoColor");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawPresetToolbar();
            EditorGUILayout.Space(4.0f);

            DrawUpdateSection();
            DrawInitialTransformSection();
            DrawOffsetSection();
            DrawOscillationSection();
            DrawNoiseSection();
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
            EditorGUILayout.LabelField("Ho 漂浮约束", EditorStyles.boldLabel);
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

        private void DrawUpdateSection()
        {
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref updateExpanded, "更新", GetUpdateModeSummary(), UpdateColor))
            {
                return;
            }

            DrawEnumPopup(updateMode, UpdateModeLabel, UpdateModeLabels, FourEnumValues);
            EditorGUILayout.PropertyField(evaluateInEditMode, EvaluateInEditModeLabel);
            EditorGUILayout.PropertyField(initializeOnEnable, InitializeOnEnableLabel);
        }

        private void DrawInitialTransformSection()
        {
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref initialExpanded, "初始变换", hasInitialTransform.boolValue ? "已保存" : "未保存", InitialColor))
            {
                return;
            }

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

        private void DrawOffsetSection()
        {
            string summary = GetSpaceSummary(offsetSpace);
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref offsetExpanded, "偏移", summary, OffsetColor))
            {
                return;
            }

            DrawEnumPopup(offsetSpace, OffsetSpaceLabel, SpaceLabels, TwoEnumValues);
            EditorGUILayout.PropertyField(positionOffset, PositionOffsetLabel);
            EditorGUILayout.PropertyField(rotationOffset, RotationOffsetLabel);
            EditorGUILayout.PropertyField(scaleOffset, ScaleOffsetLabel);
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
                DrawEnumPopup(oscillationSpace, OscillationSpaceLabel, SpaceLabels, TwoEnumValues);
                DrawEnumPopup(oscillationWaveform, OscillationWaveformLabel, WaveformLabels, ThreeEnumValues);
                if (oscillationWaveform.enumValueIndex == (int)HoFloatingConstraintWaveform.Curve)
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
            }

            DrawRuntimeReadout();
        }

        private void DrawRuntimeReadout()
        {
            if (targets.Length != 1 || !(serializedObject.targetObject is HoFloatingConstraint constraint))
            {
                return;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Vector3Field("锚点位置", constraint.AnchorPosition);
                EditorGUILayout.Vector3Field("当前位置", constraint.CurrentPosition);
                EditorGUILayout.Vector3Field("当前缩放", constraint.CurrentScale);
            }
        }

        private void DrawActionButtons()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("重置锚点"))
            {
                foreach (Object selectedTarget in targets)
                {
                    if (selectedTarget is HoFloatingConstraint constraint)
                    {
                        Undo.RecordObject(constraint.transform, "重置漂浮约束锚点");
                        constraint.ResetState();
                        EditorUtility.SetDirty(constraint);
                    }
                }
            }

            if (GUILayout.Button("立即求值"))
            {
                foreach (Object selectedTarget in targets)
                {
                    if (selectedTarget is HoFloatingConstraint constraint)
                    {
                        Undo.RecordObject(constraint.transform, "漂浮约束立即求值");
                        constraint.Evaluate(0.0f);
                        EditorUtility.SetDirty(constraint);
                        EditorUtility.SetDirty(constraint.transform);
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
                    SetBool(oscillationEnabled, true);
                    SetFloat(oscillationFrequency, 0.35f);
                    SetVector3(oscillationPositionAmplitude, new Vector3(0.0f, 0.025f, 0.0f));
                    SetVector3(oscillationRotationAmplitude, new Vector3(0.0f, 0.3f, 0.0f));
                    break;
                case 1:
                    SetBool(noiseEnabled, true);
                    SetVector3(noisePositionAmplitude, Vector3.one * 0.012f);
                    SetVector3(noiseRotationAmplitude, Vector3.one * 0.35f);
                    break;
                case 2:
                    SetBool(noiseEnabled, true);
                    SetVector3(noisePositionAmplitude, Vector3.one * 0.018f);
                    SetVector3(noiseRotationAmplitude, Vector3.one * 0.25f);
                    break;
                case 3:
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
                if (selectedTarget is HoFloatingConstraint constraint)
                {
                    Undo.RecordObject(constraint, "保存漂浮约束初始变换");
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
                if (selectedTarget is HoFloatingConstraint constraint)
                {
                    Undo.RecordObjects(new Object[] { constraint, constraint.transform }, "恢复漂浮约束初始变换");
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
                if (selectedTarget is HoFloatingConstraint constraint)
                {
                    Undo.RecordObject(constraint, "清除漂浮约束初始变换");
                    constraint.ClearInitialTransform();
                    EditorUtility.SetDirty(constraint);
                }
            }

            serializedObject.Update();
        }

        private void SetDefaultsForAllPresets()
        {
            SetEnum(offsetSpace, HoFloatingConstraintSpace.World);
            SetVector3(positionOffset, Vector3.zero);
            SetVector3(rotationOffset, Vector3.zero);
            SetVector3(scaleOffset, Vector3.zero);
            SetBool(oscillationEnabled, false);
            SetFloat(oscillationMultiplier, 1.0f);
            SetEnum(oscillationSpace, HoFloatingConstraintSpace.World);
            SetEnum(oscillationWaveform, HoFloatingConstraintWaveform.Sin);
            SetFloat(oscillationFrequency, 0.35f);
            SetFloat(oscillationPhase, 0.0f);
            SetVector3(oscillationAxisWeight, Vector3.one);
            SetVector3(oscillationPositionAmplitude, Vector3.zero);
            SetVector3(oscillationRotationAmplitude, Vector3.zero);
            SetVector3(oscillationScaleAmplitude, Vector3.zero);
            SetBool(noiseEnabled, false);
            SetFloat(noiseMultiplier, 1.0f);
            SetEnum(noiseSpace, HoFloatingConstraintSpace.World);
            SetFloat(noiseFrequency, 0.75f);
            SetVector3(noisePositionAmplitude, Vector3.zero);
            SetVector3(noiseRotationAmplitude, Vector3.zero);
            SetVector3(noiseScaleAmplitude, Vector3.zero);
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

        private void DrawEnumPopup(SerializedProperty property, GUIContent label, GUIContent[] labels, int[] values)
        {
            if (property == null)
            {
                return;
            }

            int current = Mathf.Clamp(System.Array.IndexOf(values, property.enumValueIndex), 0, values.Length - 1);
            EditorGUI.BeginChangeCheck();
            int next = EditorGUILayout.IntPopup(label, values[current], labels, values);
            if (EditorGUI.EndChangeCheck())
            {
                property.enumValueIndex = next;
            }
        }

        private string GetUpdateModeSummary()
        {
            switch ((HoFloatingConstraintUpdateMode)updateMode.enumValueIndex)
            {
                case HoFloatingConstraintUpdateMode.Update:
                    return "Update";
                case HoFloatingConstraintUpdateMode.FixedUpdate:
                    return "FixedUpdate";
                case HoFloatingConstraintUpdateMode.Manual:
                    return "手动";
                case HoFloatingConstraintUpdateMode.LateUpdate:
                default:
                    return "LateUpdate";
            }
        }

        private static string GetSpaceSummary(SerializedProperty property)
        {
            if (property == null)
            {
                return "-";
            }

            return property.enumValueIndex == (int)HoFloatingConstraintSpace.Local ? "本地" : "世界";
        }
    }
}
