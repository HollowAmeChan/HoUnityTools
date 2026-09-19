using System.Collections.Generic;
using Hollow.HoUnityTools.Constraints;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Constraints
{
    [CustomEditor(typeof(HoLookAtConstraint))]
    internal sealed class HoLookAtConstraintEditor : UnityEditor.Editor
    {
        private SerializedProperty targetMode;
        private SerializedProperty targetProperty;
        private SerializedProperty targetOffset;
        private SerializedProperty inputSource;
        private SerializedProperty mouseCamera;
        private SerializedProperty mouseSampleMode;
        private SerializedProperty mouseSensitivity;
        private SerializedProperty mouseDeadZone;
        private SerializedProperty mouseDistance;
        private SerializedProperty mouseProjectToPlane;
        private SerializedProperty mousePlaneHeight;
        private SerializedProperty mouseRaycastMask;
        private SerializedProperty mouseHoldOffscreen;
        private SerializedProperty animator;
        private SerializedProperty reference;
        private SerializedProperty weight;
        private SerializedProperty evaluateInEditMode;
        private SerializedProperty spineEnabled;
        private SerializedProperty bodyWeight;
        private SerializedProperty spineMinAngle;
        private SerializedProperty headEnabled;
        private SerializedProperty headWeight;
        private SerializedProperty clampWeight;
        private SerializedProperty deadZone;
        private SerializedProperty headShare;
        private SerializedProperty yawLimit;
        private SerializedProperty pitchLimitUp;
        private SerializedProperty pitchLimitDown;
        private SerializedProperty aimSmoothing;
        private SerializedProperty aimMaxSpeed;
        private SerializedProperty eyesEnabled;
        private SerializedProperty renderers;
        private SerializedProperty eyeWeight;
        private SerializedProperty eyeSmoothing;
        private SerializedProperty eyeEllipseClamp;
        private SerializedProperty eyesWeightToUnity;
        private SerializedProperty horizontalInner;
        private SerializedProperty horizontalOuter;
        private SerializedProperty verticalUp;
        private SerializedProperty verticalDown;
        private SerializedProperty eyeAngleLimit;
        private SerializedProperty eyeEntries;
        private SerializedProperty driveEyeBones;
        private SerializedProperty eyeBoneWeight;
        private SerializedProperty eyeBoneUseCurve;
        private SerializedProperty lostBehavior;
        private SerializedProperty returnDelay;
        private SerializedProperty returnSpeed;
        private SerializedProperty teleportAngleThreshold;
        private SerializedProperty writeThreshold;

        private bool targetExpanded = true;
        private bool spineExpanded;
        private bool headExpanded = true;
        private bool eyesExpanded = true;
        private bool lostExpanded;
        private bool debugExpanded;
        private readonly List<bool> entryFoldouts = new List<bool>();

        private static readonly Color TargetColor = new Color(0.28f, 0.62f, 1.0f);
        private static readonly Color SpineColor = new Color(1.0f, 0.70f, 0.28f);
        private static readonly Color HeadColor = new Color(0.24f, 0.86f, 0.58f);
        private static readonly Color EyeColor = new Color(0.78f, 0.48f, 1.0f);
        private static readonly Color LostColor = new Color(1.0f, 0.45f, 0.38f);
        private static readonly Color DebugColor = new Color(0.70f, 0.72f, 0.76f);

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

        private static readonly string[] ChannelLabels =
        {
            "水平内 (In)", "水平外 (Out)", "看左 (LookLeft)", "看右 (LookRight)", "垂直上 (Up)", "垂直下 (Down)"
        };

        private void OnEnable()
        {
            targetMode = Find("targetMode");
            targetProperty = Find("target");
            targetOffset = Find("targetOffset");
            inputSource = Find("inputSource");
            mouseCamera = Find("mouseCamera");
            mouseSampleMode = Find("mouseSampleMode");
            mouseSensitivity = Find("mouseSensitivity");
            mouseDeadZone = Find("mouseDeadZone");
            mouseDistance = Find("mouseDistance");
            mouseProjectToPlane = Find("mouseProjectToPlane");
            mousePlaneHeight = Find("mousePlaneHeight");
            mouseRaycastMask = Find("mouseRaycastMask");
            mouseHoldOffscreen = Find("mouseHoldOffscreen");
            animator = Find("animator");
            reference = Find("reference");
            weight = Find("weight");
            evaluateInEditMode = Find("evaluateInEditMode");
            spineEnabled = Find("spineEnabled");
            bodyWeight = Find("bodyWeight");
            spineMinAngle = Find("spineMinAngle");
            headEnabled = Find("headEnabled");
            headWeight = Find("headWeight");
            clampWeight = Find("clampWeight");
            deadZone = Find("deadZone");
            headShare = Find("headShare");
            yawLimit = Find("yawLimit");
            pitchLimitUp = Find("pitchLimitUp");
            pitchLimitDown = Find("pitchLimitDown");
            aimSmoothing = Find("aimSmoothing");
            aimMaxSpeed = Find("aimMaxSpeed");
            eyesEnabled = Find("eyesEnabled");
            renderers = Find("renderers");
            eyeWeight = Find("eyeWeight");
            eyeSmoothing = Find("eyeSmoothing");
            eyeEllipseClamp = Find("eyeEllipseClamp");
            eyesWeightToUnity = Find("eyesWeightToUnity");
            horizontalInner = Find("horizontalInner");
            horizontalOuter = Find("horizontalOuter");
            verticalUp = Find("verticalUp");
            verticalDown = Find("verticalDown");
            eyeAngleLimit = Find("eyeAngleLimit");
            eyeEntries = Find("eyeEntries");
            driveEyeBones = Find("driveEyeBones");
            eyeBoneWeight = Find("eyeBoneWeight");
            eyeBoneUseCurve = Find("eyeBoneUseCurve");
            lostBehavior = Find("lostBehavior");
            returnDelay = Find("returnDelay");
            returnSpeed = Find("returnSpeed");
            teleportAngleThreshold = Find("teleportAngleThreshold");
            writeThreshold = Find("writeThreshold");
        }

        private SerializedProperty Find(string name)
        {
            return serializedObject.FindProperty(name);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            HoLookAtConstraint constraint = (HoLookAtConstraint)target;

            DrawToolbar(constraint);
            EditorGUILayout.Space(4.0f);
            DrawChecks(constraint);
            DrawTargetSection();
            DrawSpineSection();
            DrawHeadSection();
            DrawEyesSection(constraint);
            DrawLostSection();
            DrawDebugSection(constraint);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawToolbar(HoLookAtConstraint constraint)
        {
            EditorGUILayout.LabelField("Ho 注视约束", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("一键装配", GUILayout.Height(22.0f)))
            {
                HoLookAtPresetActions.AutoRig(constraint, serializedObject);
            }

            if (GUILayout.Button("左右族"))
            {
                HoLookAtPresetActions.ApplyFamily(constraint, serializedObject, false);
            }

            if (GUILayout.Button("内外族"))
            {
                HoLookAtPresetActions.ApplyFamily(constraint, serializedObject, true);
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("只看眼睛"))
            {
                HoLookAtPresetActions.EyesOnly(serializedObject);
            }

            if (GUILayout.Button("头眼并用"))
            {
                HoLookAtPresetActions.HeadAndEyes(serializedObject);
            }

            if (GUILayout.Button("清空"))
            {
                HoLookAtPresetActions.Clear(serializedObject);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawChecks(HoLookAtConstraint constraint)
        {
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "播放后检查：Animator 是 humanoid + 动画图层勾了 IK Pass。组件不必放在 Animator 物体上（播放时会自动在 Animator 物体上挂转发器）。",
                    MessageType.Info);
                return;
            }

            if (!constraint.AnimatorIsHuman || !constraint.AnimatorHasAvatar)
            {
                EditorGUILayout.HelpBox(
                    "头部与脊椎不生效：Animator（" + constraint.AnimatorName + "）必须有 Avatar 且是 humanoid。眼睛形态键不受影响。",
                    MessageType.Error);
                return;
            }

            if (constraint.IkRecentlyCalled)
            {
                string via = constraint.AnimatorOnSameObject ? "同物体" : "转发器";
                EditorGUILayout.HelpBox("OnAnimatorIK 正常（" + via + "）。", MessageType.Info);
                return;
            }

            string detail = constraint.AnimatorOnSameObject
                ? "组件就在 Animator 物体上，回调仍然没来。"
                : "组件在别的物体上，转发器" + (constraint.IkRelayActive ? "已挂上" : "没挂上") + "。";
            EditorGUILayout.HelpBox(
                "OnAnimatorIK 没有被调用。" + detail
                + "\n① Animator 窗口 → Layers → 图层行右侧齿轮 ⚙ → 勾 IK Pass（Unity 6 里选中图层后 Inspector 也会显示这个勾）。"
                + "\n② Animator 组件的 Culling Mode 若是 Cull Update Transforms / Cull Completely，角色离屏时 IK 不更新。",
                MessageType.Warning);
        }

        private void DrawTargetSection()
        {
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref targetExpanded, "目标", GetModeSummary(), TargetColor))
            {
                return;
            }

            EditorGUILayout.PropertyField(targetMode, new GUIContent("模式"));
            using (new EditorGUI.DisabledScope((HoLookAtMode)targetMode.enumValueIndex != HoLookAtMode.Transform))
            {
                EditorGUILayout.PropertyField(targetProperty, new GUIContent("目标"));
                EditorGUILayout.PropertyField(targetOffset, new GUIContent("偏移"));
            }

            if ((HoLookAtMode)targetMode.enumValueIndex == HoLookAtMode.Mouse)
            {
                EditorGUILayout.PropertyField(mouseSampleMode, new GUIContent("取法"));
                using (new EditorGUI.DisabledScope((HoLookAtMouseSampleMode)mouseSampleMode.enumValueIndex == HoLookAtMouseSampleMode.AngleMap))
                {
                    EditorGUILayout.PropertyField(mouseCamera, new GUIContent("相机", "空 = Camera.main"));
                    EditorGUILayout.PropertyField(mouseDistance, new GUIContent("距离"));
                    EditorGUILayout.PropertyField(mouseProjectToPlane, new GUIContent("投到水平面"));
                    using (new EditorGUI.DisabledScope(!mouseProjectToPlane.boolValue))
                    {
                        EditorGUILayout.PropertyField(mousePlaneHeight, new GUIContent("平面高度"));
                    }
                }

                using (new EditorGUI.DisabledScope((HoLookAtMouseSampleMode)mouseSampleMode.enumValueIndex != HoLookAtMouseSampleMode.Raycast))
                {
                    EditorGUILayout.PropertyField(mouseRaycastMask, new GUIContent("射线层"));
                }

                EditorGUILayout.PropertyField(inputSource, new GUIContent("输入来源"));
                EditorGUILayout.PropertyField(mouseSensitivity, new GUIContent("灵敏度（度）"));
                EditorGUILayout.PropertyField(mouseDeadZone, new GUIContent("鼠标死区"));
                EditorGUILayout.PropertyField(mouseHoldOffscreen, new GUIContent("离屏保持"));
            }

            EditorGUILayout.Space(2.0f);
            EditorGUILayout.PropertyField(animator, new GUIContent("Animator", "空 = 自己/父级/子级里找"));
            EditorGUILayout.PropertyField(reference, new GUIContent("参考系", "算 yaw/pitch 用；空 = 本物体"));
            EditorGUILayout.Slider(weight, 0.0f, 1.0f, new GUIContent("总权重 weight"));
            EditorGUILayout.PropertyField(evaluateInEditMode, new GUIContent("编辑模式求值"));
        }

        private void DrawSpineSection()
        {
            string summary = spineEnabled.boolValue ? HoConstraintEditorSectionGui.FloatSummary(bodyWeight) : "关";
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref spineExpanded, "① 脊椎跟随", summary, SpineColor))
            {
                return;
            }

            EditorGUILayout.PropertyField(spineEnabled, new GUIContent("启用"));
            using (new EditorGUI.DisabledScope(!spineEnabled.boolValue))
            {
                EditorGUILayout.Slider(bodyWeight, 0.0f, 1.0f, new GUIContent("身体权重 bodyWeight", "Unity 沿脊柱分摊；0.3 左右比较自然。"));
                EditorGUILayout.PropertyField(spineMinAngle, new GUIContent("起始角", "总角度超过它之后身体才参与。"));
            }
        }

        private void DrawHeadSection()
        {
            string summary = headEnabled.boolValue ? HoConstraintEditorSectionGui.FloatSummary(headWeight) : "关";
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref headExpanded, "② 头颈跟随", summary, HeadColor))
            {
                return;
            }

            EditorGUILayout.PropertyField(headEnabled, new GUIContent("启用"));
            using (new EditorGUI.DisabledScope(!headEnabled.boolValue))
            {
                EditorGUILayout.Slider(headWeight, 0.0f, 1.0f, new GUIContent("头部权重 headWeight"));
                EditorGUILayout.Slider(clampWeight, 0.0f, 1.0f, new GUIContent("clampWeight", "0 不受限、1 完全夹死、0.5 只能在可能范围的一半内转。"));
                EditorGUILayout.Slider(deadZone, 0.0f, 89.0f, new GUIContent("死区（度）", "这么小的角度只用眼睛。"));
                EditorGUILayout.Slider(headShare, 0.0f, 1.0f, new GUIContent("头部承担", "超过死区之后头部承担的比例，剩下的给眼睛。"));
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(yawLimit, new GUIContent("偏航上限"));
                EditorGUILayout.PropertyField(pitchLimitUp, new GUIContent("抬头上限"));
                EditorGUILayout.PropertyField(pitchLimitDown, new GUIContent("低头上限"));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(aimSmoothing, new GUIContent("方向平滑（秒）"));
                EditorGUILayout.PropertyField(aimMaxSpeed, new GUIContent("方向角速度（度/秒）"));
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawEyesSection(HoLookAtConstraint constraint)
        {
            string summary = eyesEnabled.boolValue ? "开" : "关";
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref eyesExpanded, "③ 眼睛跟随", summary, EyeColor))
            {
                return;
            }

            EditorGUILayout.PropertyField(eyesEnabled, new GUIContent("启用"));
            using (new EditorGUI.DisabledScope(!eyesEnabled.boolValue))
            {
                EditorGUILayout.PropertyField(renderers, new GUIContent("目标网格"), true);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("收集子级网格"))
                {
                    Undo.RecordObject(constraint, "收集注视约束网格");
                    constraint.CollectChildRenderers();
                    EditorUtility.SetDirty(constraint);
                    serializedObject.Update();
                }

                if (GUILayout.Button("重新解析键"))
                {
                    constraint.Rebuild();
                }

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Slider(eyeWeight, 0.0f, 1.0f, new GUIContent("眼球权重 eyeWeight"));
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(eyeSmoothing, new GUIContent("平滑（秒）"));
                EditorGUILayout.PropertyField(eyeEllipseClamp, new GUIContent("椭圆夹紧"));
                EditorGUILayout.Slider(eyesWeightToUnity, 0.0f, 1.0f, new GUIContent("交给 Unity 的 eyesWeight", "默认 0：眼球由下面的曲线驱动。"));
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField("四条方向曲线（横轴 = 角度上限，纵轴 = 输出比例）", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(eyeAngleLimit, new GUIContent("角度上限 内/外/上/下"));
                EditorGUILayout.PropertyField(horizontalInner, new GUIContent("水平内"));
                EditorGUILayout.PropertyField(horizontalOuter, new GUIContent("水平外"));
                EditorGUILayout.PropertyField(verticalUp, new GUIContent("垂直上"));
                EditorGUILayout.PropertyField(verticalDown, new GUIContent("垂直下"));

                EditorGUILayout.Space(2.0f);
                EditorGUILayout.LabelField("通道 → 键（左右眼各一个）", EditorStyles.boldLabel);
                while (entryFoldouts.Count < eyeEntries.arraySize)
                {
                    entryFoldouts.Add(true);
                }

                for (int i = 0; i < eyeEntries.arraySize; i++)
                {
                    DrawEyeEntry(constraint, i);
                }

                if (GUILayout.Button("+ 通道"))
                {
                    eyeEntries.InsertArrayElementAtIndex(eyeEntries.arraySize);
                }

                EditorGUILayout.Space(2.0f);
                EditorGUILayout.PropertyField(driveEyeBones, new GUIContent("驱动眼球骨骼"));
                using (new EditorGUI.DisabledScope(!driveEyeBones.boolValue))
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.Slider(eyeBoneWeight, 0.0f, 1.0f, new GUIContent("眼球骨骼权重"));
                    EditorGUILayout.PropertyField(eyeBoneUseCurve, new GUIContent("过曲线"));
                    EditorGUILayout.EndHorizontal();
                }

                if (constraint.MissingKeys.Count > 0)
                {
                    EditorGUILayout.HelpBox("以下键在目标网格上不存在：" + string.Join("、", constraint.MissingKeys), MessageType.Warning);
                }
            }
        }

        private void DrawEyeEntry(HoLookAtConstraint constraint, int index)
        {
            SerializedProperty entry = eyeEntries.GetArrayElementAtIndex(index);
            SerializedProperty enabled = entry.FindPropertyRelative("enabled");
            SerializedProperty channel = entry.FindPropertyRelative("channel");
            SerializedProperty leftEye = entry.FindPropertyRelative("leftEye");
            SerializedProperty rightEye = entry.FindPropertyRelative("rightEye");

            int channelIndex = Mathf.Clamp(channel.enumValueIndex, 0, ChannelLabels.Length - 1);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            entryFoldouts[index] = EditorGUILayout.Foldout(entryFoldouts[index], ChannelLabels[channelIndex], true);
            channel.enumValueIndex = EditorGUILayout.Popup(channel.enumValueIndex, ChannelLabels, GUILayout.Width(140.0f));
            enabled.boolValue = EditorGUILayout.ToggleLeft("启用", enabled.boolValue, GUILayout.Width(52.0f));
            if (GUILayout.Button("✕", GUILayout.Width(22.0f)))
            {
                eyeEntries.DeleteArrayElementAtIndex(index);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.EndHorizontal();

            if (entryFoldouts[index])
            {
                EditorGUI.indentLevel++;
                DrawEyeMapping("左眼", leftEye, constraint);
                DrawEyeMapping("右眼", rightEye, constraint);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawEyeMapping(string label, SerializedProperty mapping, HoLookAtConstraint constraint)
        {
            SerializedProperty keyName = mapping.FindPropertyRelative("keyName");
            SerializedProperty gain = mapping.FindPropertyRelative("gain");
            SerializedProperty rampPreset = mapping.FindPropertyRelative("rampPreset");
            SerializedProperty rampIntensity = mapping.FindPropertyRelative("rampIntensity");

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(30.0f));
            DrawKeyField(keyName, constraint);
            rampPreset.enumValueIndex = EditorGUILayout.IntPopup(
                GUIContent.none,
                rampPreset.enumValueIndex,
                RampPresetLabels,
                RampPresetValues,
                GUILayout.Width(80.0f));
            rampIntensity.floatValue = EditorGUILayout.FloatField(rampIntensity.floatValue, GUILayout.Width(40.0f));
            EditorGUILayout.EndHorizontal();
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.BeginHorizontal();
                gain.floatValue = EditorGUILayout.FloatField(new GUIContent("增益"), gain.floatValue);
                EditorGUILayout.PropertyField(mapping.FindPropertyRelative("outputMax"), new GUIContent("输出上限"));
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawKeyField(SerializedProperty keyName, HoLookAtConstraint constraint)
        {
            const float ButtonWidth = 24.0f;
            Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
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

        private void DrawLostSection()
        {
            string summary = lostBehavior.enumValueIndex == (int)HoLookAtLostBehavior.Hold ? "保持" : "回中立";
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref lostExpanded, "丢失与瞬移", summary, LostColor))
            {
                return;
            }

            EditorGUILayout.PropertyField(lostBehavior, new GUIContent("丢失行为"));
            EditorGUILayout.PropertyField(returnDelay, new GUIContent("回正延迟（秒）"));
            EditorGUILayout.PropertyField(returnSpeed, new GUIContent("回正速度（度/秒）"));
            EditorGUILayout.PropertyField(teleportAngleThreshold, new GUIContent("瞬移阈值（度）", "目标角度跳变超过它就瞬移跟上，不慢慢转。"));
            EditorGUILayout.PropertyField(writeThreshold, new GUIContent("写入阈值"));
        }

        private void DrawDebugSection(HoLookAtConstraint constraint)
        {
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref debugExpanded, "调试", constraint.HasTarget ? "有目标" : "无目标", DebugColor))
            {
                return;
            }

            EditorGUILayout.LabelField(
                "总角度 yaw " + constraint.TotalAngles.yaw.ToString("0.0") + "°  pitch " + constraint.TotalAngles.pitch.ToString("0.0") + "°",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField(
                "头部承担 yaw " + constraint.HeadAngles.yaw.ToString("0.0") + "°  pitch " + constraint.HeadAngles.pitch.ToString("0.0") + "°",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField(
                "眼睛残余 yaw " + constraint.EyeYaw.ToString("0.0") + "°  pitch " + constraint.EyePitch.ToString("0.0") + "°",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField(
                "网格 " + constraint.MeshCount + " / 绑定 " + constraint.BindingCount + " / IK " + (constraint.IkRecentlyCalled ? "在跑" : "未调用"),
                EditorStyles.miniLabel);

            for (int c = 0; c < ChannelLabels.Length; c++)
            {
                HoLookAtEyeChannel channel = (HoLookAtEyeChannel)c;
                float amount = constraint.GetChannelAmount(channel);
                EditorGUILayout.LabelField(
                    ChannelLabels[c] + "  量 " + amount.ToString("0.###")
                    + "   左 " + constraint.GetChannelTargetOutput(channel, false).ToString("0.#")
                    + "   右 " + constraint.GetChannelTargetOutput(channel, true).ToString("0.#"),
                    EditorStyles.miniLabel);
            }
        }

        private string GetModeSummary()
        {
            if ((HoLookAtMode)targetMode.enumValueIndex == HoLookAtMode.Mouse)
            {
                return "鼠标 / " + (HoLookAtMouseSampleMode)mouseSampleMode.enumValueIndex;
            }

            return "跟随物体";
        }
    }
}
