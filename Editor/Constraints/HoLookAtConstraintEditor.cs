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
        private SerializedProperty mouseCamera;
        private SerializedProperty mouseSensitivity;
        private SerializedProperty mouseDeadZone;
        private SerializedProperty animator;
        private SerializedProperty reference;
        private SerializedProperty weight;
        private SerializedProperty evaluateInEditMode;
        private SerializedProperty spineEnabled;
        private SerializedProperty bodyWeight;
        private SerializedProperty spineMinAngle;
        private SerializedProperty headEnabled;
        private SerializedProperty headWeight;
        private SerializedProperty deadZone;
        private SerializedProperty headShare;
        private SerializedProperty headLimitYaw;
        private SerializedProperty headLimitPitch;
        private SerializedProperty aimSmoothing;
        private SerializedProperty aimMaxSpeed;
        private SerializedProperty eyesEnabled;
        private SerializedProperty renderers;
        private SerializedProperty eyeWeight;
        private SerializedProperty eyeSmoothing;
        private SerializedProperty horizontalInner;
        private SerializedProperty horizontalOuter;
        private SerializedProperty verticalUp;
        private SerializedProperty verticalDown;
        private SerializedProperty eyeAngleLimit;
        private SerializedProperty eyeEntries;
        private SerializedProperty eyeBoneWeight;
        private SerializedProperty lostBehavior;
        private SerializedProperty returnDelay;
        private SerializedProperty returnSpeed;
        private SerializedProperty teleportAngleThreshold;
        private SerializedProperty mouseSampleMode;
        private SerializedProperty mouseAngleSpace;
        private SerializedProperty mouseDistance;
        private SerializedProperty mouseRaycastMask;
        private SerializedProperty mouseHoldOffscreen;
        private SerializedProperty writeThreshold;
        private SerializedProperty drawGizmos;
        private SerializedProperty mergeMode;

        private bool targetExpanded = true;
        private bool roleExpanded = true;
        private bool spineExpanded;
        private bool headExpanded = true;
        private bool eyesExpanded = true;
        private bool lostExpanded;
        private bool advancedExpanded;
        private bool debugExpanded = true;
        private readonly List<bool> entryFoldouts = new List<bool>();
        private readonly List<HoShapeKeySaturation> saturationBuffer = new List<HoShapeKeySaturation>();

        private static readonly Color TargetColor = new Color(0.28f, 0.62f, 1.0f);
        private static readonly Color RoleColor = new Color(0.62f, 0.66f, 0.72f);
        private static readonly Color SpineColor = new Color(1.0f, 0.70f, 0.28f);
        private static readonly Color HeadColor = new Color(0.24f, 0.86f, 0.58f);
        private static readonly Color EyeColor = new Color(0.78f, 0.48f, 1.0f);
        private static readonly Color LostColor = new Color(1.0f, 0.45f, 0.38f);
        private static readonly Color DebugColor = new Color(0.70f, 0.72f, 0.76f);
        private static readonly Color BarBackground = new Color(0.0f, 0.0f, 0.0f, 0.35f);
        private static readonly Color BarCenter = new Color(1.0f, 1.0f, 1.0f, 0.35f);
        private static readonly Color TargetMarker = new Color(1.0f, 0.85f, 0.30f);
        private static readonly Color HeadMarker = new Color(0.30f, 0.90f, 0.65f);
        private static readonly Color EyeMarker = new Color(0.80f, 0.55f, 1.0f);

        private static readonly string[] ChannelLabels =
        {
            "看左 LookLeft", "看右 LookRight", "看上 Up", "看下 Down"
        };

        private static readonly string[] ChannelTooltips =
        {
            "往角色左边看。往左 = 左眼的外侧键（Out）+ 右眼的内侧键（In）—— 所以每只眼各填一个键就够了。",
            "往角色右边看。往右 = 左眼的内侧键（In）+ 右眼的外侧键（Out）。",
            "往上看（双眼共用键就两格填同一个名字，只写一次）。",
            "往下看（双眼共用键就两格填同一个名字，只写一次）。"
        };

        private void OnEnable()
        {
            targetMode = Find("targetMode");
            targetProperty = Find("target");
            targetOffset = Find("targetOffset");
            mouseCamera = Find("mouseCamera");
            mouseSensitivity = Find("mouseSensitivity");
            mouseDeadZone = Find("mouseDeadZone");
            animator = Find("animator");
            reference = Find("reference");
            weight = Find("weight");
            evaluateInEditMode = Find("evaluateInEditMode");
            spineEnabled = Find("spineEnabled");
            bodyWeight = Find("bodyWeight");
            spineMinAngle = Find("spineMinAngle");
            headEnabled = Find("headEnabled");
            headWeight = Find("headWeight");
            deadZone = Find("deadZone");
            headShare = Find("headShare");
            headLimitYaw = Find("headLimitYaw");
            headLimitPitch = Find("headLimitPitch");
            aimSmoothing = Find("aimSmoothing");
            aimMaxSpeed = Find("aimMaxSpeed");
            eyesEnabled = Find("eyesEnabled");
            renderers = Find("renderers");
            eyeWeight = Find("eyeWeight");
            eyeSmoothing = Find("eyeSmoothing");
            horizontalInner = Find("horizontalInner");
            horizontalOuter = Find("horizontalOuter");
            verticalUp = Find("verticalUp");
            verticalDown = Find("verticalDown");
            eyeAngleLimit = Find("eyeAngleLimit");
            eyeEntries = Find("eyeEntries");
            eyeBoneWeight = Find("eyeBoneWeight");
            lostBehavior = Find("lostBehavior");
            returnDelay = Find("returnDelay");
            returnSpeed = Find("returnSpeed");
            teleportAngleThreshold = Find("teleportAngleThreshold");
            mouseSampleMode = Find("mouseSampleMode");
            mouseAngleSpace = Find("mouseAngleSpace");
            mouseDistance = Find("mouseDistance");
            mouseRaycastMask = Find("mouseRaycastMask");
            mouseHoldOffscreen = Find("mouseHoldOffscreen");
            writeThreshold = Find("writeThreshold");
            drawGizmos = Find("drawGizmos");
            mergeMode = Find("mergeMode");
        }

        private SerializedProperty Find(string name)
        {
            return serializedObject.FindProperty(name);
        }

        /// <summary>带中文说明的字段 —— 面板上每个参数鼠标悬停都能看到"它是干什么的"。</summary>
        private static GUIContent L(string label, string tooltip)
        {
            return new GUIContent(label, tooltip);
        }

        /// <summary>
        /// 并排两个字段时必须临时收窄 labelWidth：默认值是面板宽度的 40%，
        /// 半宽的一格里 label 就能把数字框挤成 0 宽（症状：只有文字、点不动）。
        /// </summary>
        private static System.IDisposable NarrowLabels(float width)
        {
            return HoConstraintEditorSectionGui.NarrowLabels(width);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            HoLookAtConstraint constraint = (HoLookAtConstraint)target;

            DrawHeader(constraint);
            DrawChecks(constraint);
            DrawTargetSection(constraint);
            DrawRoleSection(constraint);
            DrawSpineSection();
            DrawHeadSection();
            DrawEyesSection(constraint);
            DrawLostSection();
            DrawAdvancedSection(constraint);
            DrawDebugSection(constraint);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawHeader(HoLookAtConstraint constraint)
        {
            EditorGUILayout.LabelField("Ho 注视约束", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("一键装配", "自动找 Animator、收集网格、按模型上的键判断该用「左右族」还是「内外族」，并填好相机。"), GUILayout.Height(22.0f)))
            {
                HoLookAtPresetActions.AutoRig(constraint, serializedObject);
            }

            if (GUILayout.Button(new GUIContent("清空通道", "只清掉眼睛通道的键，不动其他设置。"), GUILayout.Height(22.0f)))
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

        // ── 目标 ────────────────────────────────────────────────────────────

        private void DrawTargetSection(HoLookAtConstraint constraint)
        {
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref targetExpanded, "看哪里（目标）", GetModeSummary(), TargetColor))
            {
                return;
            }

            EditorGUILayout.PropertyField(targetMode, L("目标来源", "跟随物体：盯着场景里的一个物体（可加偏移）。\n跟随鼠标：按鼠标位置看，需要指定一台「观众视角」的相机。"));

            if ((HoLookAtMode)targetMode.enumValueIndex == HoLookAtMode.Transform)
            {
                EditorGUILayout.PropertyField(targetProperty, L("目标物体", "要看的那个物体（空物体也行，眼睛/头会跟它跑）。"));
                EditorGUILayout.PropertyField(targetOffset, L("偏移", "在目标物体自身坐标系里的偏移，用来微调看的位置（比如看头顶上方）。"));
            }
            else
            {
                EditorGUILayout.PropertyField(mouseCamera, L("相机", "观众视角的那台相机，用来把鼠标屏幕位置换算成角度。运行时不会自动猜，必须手动指定。"));
                if (!constraint.MouseCameraAssigned)
                {
                    EditorGUILayout.HelpBox(
                        "鼠标模式必须指定「相机」，否则角度映射会退回角色相对坐标系（正面机位下看起来左右是反的）。",
                        MessageType.Warning);
                    if (GUILayout.Button("填入场景里的相机"))
                    {
                        Camera found = HoMousePointer.FindSceneCamera(null);
                        if (found != null)
                        {
                            mouseCamera.objectReferenceValue = found;
                            serializedObject.ApplyModifiedProperties();
                        }
                        else
                        {
                            Debug.LogWarning("场景里没有找到渲染到屏幕的启用相机。");
                        }
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("使用的相机：" + constraint.ResolvedCameraName, EditorStyles.miniLabel);
                }

                EditorGUILayout.PropertyField(mouseSensitivity, L("灵敏度（度）", "鼠标从画面中心推到边缘时，横向/纵向各转多少度。\n30/20 = 推到画面边缘大约左右 30°、上下 20°。"));
                EditorGUILayout.Slider(mouseDeadZone, 0.0f, 0.45f, L("鼠标死区", "画面正中间这一圈内不转，避免鼠标抖动带着眼睛一直动。0.05 = 中心 5%。"));
                EditorGUILayout.PropertyField(mouseHoldOffscreen, L("离屏保持", "鼠标移出窗口 / 失焦时，保持最后一次的方向，而不是回中立。"));
            }
        }

        // ── 角色 ────────────────────────────────────────────────────────────

        private void DrawRoleSection(HoLookAtConstraint constraint)
        {
            string summary = "强度 " + weight.floatValue.ToString("0.##");
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref roleExpanded, "角色（接口）", summary, RoleColor))
            {
                return;
            }

            EditorGUILayout.Slider(weight, 0.0f, 1.0f, L("总强度", "整个注视的总开关强度：0 = 完全不做注视，1 = 按下面三块的比例全额生效。\n做「看一眼再移开」这类演出时改它就够了。"));

            string animatorInfo = constraint.AnimatorName;
            if (constraint.AnimatorIsHuman && constraint.AnimatorHasAvatar)
            {
                animatorInfo += "（humanoid ✓）";
            }
            else if (Application.isPlaying)
            {
                animatorInfo += "（不是 humanoid ✗）";
            }

            EditorGUILayout.LabelField("Animator：" + animatorInfo, EditorStyles.miniLabel);
            EditorGUILayout.LabelField("参考系：" + GetReferenceInfo(constraint), EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("只看眼睛", "关掉头颈与脊椎，让眼珠自己跟（近距离对话、镜头特写常用）。"), EditorStyles.miniButton))
            {
                HoLookAtPresetActions.EyesOnly(serializedObject);
            }

            if (GUILayout.Button(new GUIContent("头眼并用", "恢复成头 1.0 + 身体 0.3 的默认分工。"), EditorStyles.miniButton))
            {
                HoLookAtPresetActions.HeadAndEyes(serializedObject);
            }

            EditorGUILayout.EndHorizontal();
        }

        private string GetReferenceInfo(HoLookAtConstraint constraint)
        {
            if (reference.objectReferenceValue != null)
            {
                Transform frame = (Transform)reference.objectReferenceValue;
                return frame.name + "（手动指定）";
            }

            Animator resolved = (Animator)animator.objectReferenceValue;
            return resolved != null ? resolved.gameObject.name + "（自动）" : "本物体（自动）";
        }

        // ── 三块 ────────────────────────────────────────────────────────────

        private void DrawSpineSection()
        {
            string summary = spineEnabled.boolValue ? HoConstraintEditorSectionGui.FloatSummary(bodyWeight) : "关";
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref spineExpanded, "① 脊椎跟随", summary, SpineColor))
            {
                return;
            }

            EditorGUILayout.PropertyField(spineEnabled, L("启用", "身体跟着转一点点。想要「只有头在动」就关掉它。"));
            using (new EditorGUI.DisabledScope(!spineEnabled.boolValue))
            {
                EditorGUILayout.Slider(bodyWeight, 0.0f, 1.0f, L("身体强度", "身体参与的比例，Unity 会沿脊椎分摊下去。0.2~0.4 比较自然，越大腰也跟着扭。"));
            }
        }

        private void DrawHeadSection()
        {
            string summary = headEnabled.boolValue ? HoConstraintEditorSectionGui.FloatSummary(headWeight) : "关";
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref headExpanded, "② 头颈跟随", summary, HeadColor))
            {
                return;
            }

            EditorGUILayout.PropertyField(headEnabled, L("启用", "用 Unity 的 LookAt IK 转头颈。需要 humanoid + 图层 IK Pass。"));
            using (new EditorGUI.DisabledScope(!headEnabled.boolValue))
            {
                EditorGUILayout.Slider(headWeight, 0.0f, 1.0f, L("头部强度", "头颈参与的比例。1 = 完全对准（在下面的限位之内），0.5 = 只转一半。"));
                EditorGUILayout.Slider(deadZone, 0.0f, 89.0f, L("起始死区（度）", "目标离正前方这么近的时候头完全不动，只让眼睛动。避免小幅目标让头一直微抖。"));
                EditorGUILayout.Slider(headShare, 0.0f, 1.0f, L("头部承担", "超出死区之后，头承担多少（剩下的留给眼睛）。\n0.7 = 头吃七成、眼睛补三成。"));
                using (NarrowLabels(74.0f))
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PropertyField(headLimitYaw, L("左右限位", "头最多往左右各转多少度。超过的部分全部交给眼睛。"));
                    EditorGUILayout.PropertyField(headLimitPitch, L("上下限位", "头最多往上/往下各转多少度，对称。"));
                    EditorGUILayout.EndHorizontal();
                }
            }
        }

        private void DrawEyesSection(HoLookAtConstraint constraint)
        {
            string summary = eyesEnabled.boolValue ? "开" : "关";
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref eyesExpanded, "③ 眼睛跟随", summary, EyeColor))
            {
                return;
            }

            EditorGUILayout.PropertyField(eyesEnabled, L("启用", "用形态键（辅助骨骼也行）让眼珠跟住目标。不依赖 humanoid。"));
            using (new EditorGUI.DisabledScope(!eyesEnabled.boolValue))
            {
                EditorGUILayout.PropertyField(renderers, L("目标网格", "哪些网格上有眼睛的形态键。点下面的按钮可以一次收集所有子网格。"), true);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("收集子级网格"))
                {
                    Undo.RecordObject(constraint, "收集注视约束网格");
                    constraint.CollectChildRenderers();
                    EditorUtility.SetDirty(constraint);
                    serializedObject.Update();
                }

                if (GUILayout.Button(new GUIContent("重新解析键", "改了键名/换了模型之后点一下，重新在网格上找键。")))
                {
                    constraint.Rebuild();
                }

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Slider(eyeWeight, 0.0f, 1.0f, L("眼球强度", "眼睛参与的比例。头转不到位的部分由眼睛补，这里可以再打个折。"));
                EditorGUILayout.PropertyField(mergeMode, HoConstraintEditorSectionGui.MergeModeLabel);
                EditorGUILayout.PropertyField(eyeAngleLimit, L("四个角度上限 往右/往左/上/下（度）", "在这个角度内眼睛能完全跟上，超过就按曲线开始饱和。\n一般按模型实际能转的范围填（30/30/20/25 是常见值）。"));
                EditorGUILayout.LabelField("四条方向曲线（横轴 = 上面角度上限的比例，纵轴 = 输出）", EditorStyles.miniLabel);
                EditorGUILayout.PropertyField(horizontalInner, L("往右曲线（内）", "往右看这条通道的映射形状，直线 = 线性。"));
                EditorGUILayout.PropertyField(horizontalOuter, L("往左曲线（外）", "往左看这条通道的映射形状。"));
                EditorGUILayout.PropertyField(verticalUp, L("看上曲线", "往上看这条通道的映射形状。"));
                EditorGUILayout.PropertyField(verticalDown, L("看下曲线", "往下看这条通道的映射形状。"));

                EditorGUILayout.Space(2.0f);
                if (DrawChannelHeader())
                {
                    // 刚按了「左右族 / 内外族」：通道列表已经换过一轮，本帧就不要再遍历了
                    return;
                }

                while (entryFoldouts.Count < eyeEntries.arraySize)
                {
                    entryFoldouts.Add(true);
                }

                for (int i = 0; i < eyeEntries.arraySize; i++)
                {
                    DrawEyeEntry(constraint, i);
                }

                if (GUILayout.Button(new GUIContent("+ 通道", "一条通道 = 一种眼动方向（水平内/外、看左/右、看上/下）。"), GUILayout.Width(80.0f)))
                {
                    eyeEntries.InsertArrayElementAtIndex(eyeEntries.arraySize);
                }

                if (constraint.MissingKeys.Count > 0)
                {
                    EditorGUILayout.HelpBox("以下键在目标网格上不存在：" + string.Join("、", constraint.MissingKeys), MessageType.Warning);
                }
            }
        }

        /// <summary>通道列表表头 + 两个族别预设按钮。返回 true 表示这一帧刚按了预设、列表已重建。</summary>
        private bool DrawChannelHeader()
        {
            EditorGUILayout.LabelField("四条通道 → 键名（左右眼各一个；两格填同一个键就只写一次）", EditorStyles.miniLabel);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("按模型上的键自动填：", "会在网格上找内置表里收录的凝视键，找不到就留空。"), EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            bool clickedLeftRight = GUILayout.Button(
                new GUIContent("左右族", "VRM/Meta 那种「看左/看右」的键名（双眼共用一个键，或左右眼各一个）。"),
                EditorStyles.miniButton,
                GUILayout.Width(58.0f));
            bool clickedInnerOuter = GUILayout.Button(
                new GUIContent("内外族", "ARKit/PICO 那种相对眼球的 In/Out 键名 —— 会拆成「往左 = 左眼 Out + 右眼 In、往右 = 左眼 In + 右眼 Out」填进左右眼两格。"),
                EditorStyles.miniButton,
                GUILayout.Width(58.0f));
            EditorGUILayout.EndHorizontal();

            if (clickedLeftRight || clickedInnerOuter)
            {
                HoLookAtPresetActions.ApplyFamily((HoLookAtConstraint)target, serializedObject, clickedInnerOuter);
                entryFoldouts.Clear();
                return true;
            }

            return false;
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
            channel.enumValueIndex = EditorGUILayout.Popup(channel.enumValueIndex, ChannelLabels, GUILayout.Width(120.0f));
            enabled.boolValue = EditorGUILayout.ToggleLeft(new GUIContent("启用", ChannelTooltips[channelIndex]), enabled.boolValue, GUILayout.Width(50.0f));
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
                DrawEyeKey("左眼", leftEye, constraint);
                DrawEyeKey("右眼", rightEye, constraint);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 一只眼的一行：`[✓ 左眼] [键名 ………] [▾] 增益 [ 1.0 ]`。
        /// 全部手摆矩形：并排的小方框不能用带 label 的 *Layout 控件 ——
        /// label 会按 EditorGUIUtility.labelWidth（面板宽度的 40%）吃掉整格，
        /// 数字框被压成 0 宽，看起来就是"只有文字、点不动"。
        /// </summary>
        private void DrawEyeKey(string label, SerializedProperty key, HoLookAtConstraint constraint)
        {
            SerializedProperty enabled = key.FindPropertyRelative("enabled");
            SerializedProperty keyName = key.FindPropertyRelative("keyName");
            SerializedProperty gain = key.FindPropertyRelative("gain");

            const float ToggleWidth = 46.0f;
            const float GainFieldWidth = 56.0f;
            const float GainLabelWidth = 30.0f;
            const float DropdownWidth = 22.0f;

            Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            Rect toggleRect = new Rect(row.x, row.y, ToggleWidth, row.height);
            Rect gainFieldRect = new Rect(row.xMax - GainFieldWidth, row.y, GainFieldWidth, row.height);
            Rect gainLabelRect = new Rect(gainFieldRect.x - GainLabelWidth - 2.0f, row.y, GainLabelWidth, row.height);
            Rect dropdownRect = new Rect(gainLabelRect.x - DropdownWidth - 2.0f, row.y, DropdownWidth, row.height);
            Rect keyRect = new Rect(
                toggleRect.xMax,
                row.y,
                Mathf.Max(40.0f, dropdownRect.x - toggleRect.xMax - 4.0f),
                row.height);

            enabled.boolValue = EditorGUI.ToggleLeft(toggleRect, label, enabled.boolValue);
            using (new EditorGUI.DisabledScope(!enabled.boolValue))
            {
                DrawKeyField(keyRect, keyName, constraint);
                GUI.Label(gainLabelRect, new GUIContent("增益", "通道量乘上它再写出去：1 = 曲线拉满输出 100。个别键太夸张时可以在这里压下去。"), EditorStyles.miniLabel);
                gain.floatValue = EditorGUI.FloatField(gainFieldRect, gain.floatValue);
            }
        }

        private void DrawKeyField(Rect rect, SerializedProperty keyName, HoLookAtConstraint constraint)
        {
            const float ButtonWidth = 22.0f;
            Rect fieldRect = new Rect(rect.x, rect.y, Mathf.Max(20.0f, rect.width - ButtonWidth - 2.0f), rect.height);
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
            string summary = HoConstraintEditorSectionGui.EnumSummary(lostBehavior);
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref lostExpanded, "丢失与瞬移", summary, LostColor))
            {
                return;
            }

            EditorGUILayout.PropertyField(lostBehavior, L("丢失行为", "目标丢了（物体被删/鼠标离屏）怎么办：\n保持 = 停在最后的方向\n回中立 = 慢慢转回正前方\n禁用 = 立刻把头松开（交给动画）"));
        }

        // ── 高级 ────────────────────────────────────────────────────────────

        private void DrawAdvancedSection(HoLookAtConstraint constraint)
        {
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref advancedExpanded, "高级", "少动", new Color(0.55f, 0.58f, 0.62f)))
            {
                return;
            }

            EditorGUILayout.LabelField("跟随手感", EditorStyles.miniBoldLabel);
            using (NarrowLabels(96.0f))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(aimSmoothing, L("方向平滑", "目标方向的一阶平滑时间常数（秒），0 = 立刻对准（会有点硬）。"));
                EditorGUILayout.PropertyField(aimMaxSpeed, L("最大角速度", "转头速度上限（度/秒），防止目标瞬移时头猛地甩过去。0 = 不限。"));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.PropertyField(teleportAngleThreshold, L("瞬移阈值（度）", "目标角度跳变超过它就当瞬移，直接跟上不慢慢转。"));
            using (new EditorGUI.DisabledScope(!spineEnabled.boolValue))
            {
                EditorGUILayout.Slider(spineMinAngle, 0.0f, 90.0f, L("脊椎起始角（度）", "总角度超过它之后身体才开始参与，避免小幅注视也带着上半身动。"));
            }

            EditorGUILayout.Space(2.0f);
            EditorGUILayout.LabelField("丢失之后", EditorStyles.miniBoldLabel);
            using (NarrowLabels(88.0f))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(returnDelay, L("回正延迟", "丢失后先保持这么久（秒）再开始回正。"));
                EditorGUILayout.PropertyField(returnSpeed, L("回正速度", "回正时的角速度（度/秒）。"));
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(2.0f);
            EditorGUILayout.LabelField("眼球骨骼（非形态键方案）", EditorStyles.miniBoldLabel);
            EditorGUILayout.Slider(eyeBoneWeight, 0.0f, 1.0f, L("眼球骨骼权重", "humanoid 有 LeftEye/RightEye 时，直接转这两根骨头。\n0 = 完全不动骨骼，只用形态键。"));

            EditorGUILayout.Space(2.0f);
            EditorGUILayout.LabelField("鼠标细节", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(mouseSampleMode, L("鼠标取法", "角度映射：鼠标位置直接换算成角度（推荐，跟相机距离无关）。\n场景点：从相机沿鼠标打一条射线，用打到的位置当目标点。"));
            using (new EditorGUI.DisabledScope((HoLookAtMouseSampleMode)mouseSampleMode.enumValueIndex != HoLookAtMouseSampleMode.AngleMap))
            {
                EditorGUILayout.PropertyField(mouseAngleSpace, L("角度坐标系", "屏幕相对：鼠标往右 = 角色看向画面右侧（第三人称面对角色的直觉）。\n角色相对：鼠标往右 = 角色看向它自己的右侧（当摇杆用）。"));
            }

            using (new EditorGUI.DisabledScope((HoLookAtMouseSampleMode)mouseSampleMode.enumValueIndex != HoLookAtMouseSampleMode.Raycast))
            {
                EditorGUILayout.PropertyField(mouseRaycastMask, L("射线层", "场景点模式打哪些层。"));
                EditorGUILayout.PropertyField(mouseDistance, L("兜底距离（米）", "射线什么都没打中时，取射线上这个距离的点。"));
            }

            EditorGUILayout.Space(2.0f);
            EditorGUILayout.LabelField("组件", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(animator, L("Animator", "留空 = 自己/父级/子级里自动找。只有自动找错时才需要填。"));
            EditorGUILayout.PropertyField(reference, L("参考系", "算 yaw/pitch 和限位用的朝向。\n留空 = 用 Animator 所在物体（角色根，推荐）。\n自己指定时一定要用角色的根物体，别填骨骼，否则角度全部失准。"));
            EditorGUILayout.Space(2.0f);
            EditorGUILayout.LabelField("写入", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(writeThreshold, L("写入阈值", "形态键变化小于它就不写，减少网格 dirty。调大能省一点性能，代价是细微变化被忽略。"));
            EditorGUILayout.PropertyField(evaluateInEditMode, L("编辑模式求值", "不播放也在编辑器里跑一遍（调试用，会给场景标脏）。"));
        }

        // ── 调试（可视化） ──────────────────────────────────────────────────

        private void DrawDebugSection(HoLookAtConstraint constraint)
        {
            HoLookAtDebug debug = constraint.GetDebug();
            string summary = debug.hasTarget ? "有目标" : "无目标";
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref debugExpanded, "调试", summary, DebugColor))
            {
                return;
            }

            EditorGUILayout.PropertyField(drawGizmos, L("场景 Gizmo", "在 Scene 视图里画出参考朝向、限位框、目标方向、头和眼睛各自的方向（选中本物体即可看到）。"));

            EditorGUILayout.LabelField("左右（yaw，正 = 角色右侧）", EditorStyles.miniBoldLabel);
            DrawAngleBar(constraint.HeadLimitYaw, debug.targetYaw, debug.headYaw, debug.eyeYaw);
            EditorGUILayout.LabelField("上下（pitch，正 = 抬头）", EditorStyles.miniBoldLabel);
            DrawAngleBar(constraint.HeadLimitPitch, debug.targetPitch, debug.headPitch, debug.eyePitch);

            EditorGUILayout.Space(2.0f);
            EditorGUILayout.LabelField("状态：总角度 " + debug.targetYaw.ToString("0.0") + "° / " + debug.targetPitch.ToString("0.0") + "°"
                + "　头 " + debug.headYaw.ToString("0.0") + "° / " + debug.headPitch.ToString("0.0") + "°"
                + "　眼 " + debug.eyeYaw.ToString("0.0") + "° / " + debug.eyePitch.ToString("0.0") + "°",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField("网格 " + constraint.MeshCount + " / 绑定 " + constraint.BindingCount
                + " / OnAnimatorIK " + (constraint.IkRecentlyCalled ? "在跑" : "未调用"), EditorStyles.miniLabel);

            EditorGUILayout.Space(2.0f);
            EditorGUILayout.LabelField("六个通道（条形 = 写入比例，括号 = 左右眼实际写出的形态键值）", EditorStyles.miniBoldLabel);
            for (int c = 0; c < ChannelLabels.Length; c++)
            {
                HoLookAtEyeChannel channel = (HoLookAtEyeChannel)c;
                DrawChannelBar(
                    ChannelLabels[c],
                    GetChannelAmount(debug, channel),
                    constraint.GetChannelTargetOutput(channel, false),
                    constraint.GetChannelTargetOutput(channel, true));
            }

            constraint.CollectSaturatedKeys(saturationBuffer);
            HoConstraintEditorSectionGui.DrawSaturationReport(saturationBuffer);

            EditorGUILayout.Space(2.0f);
            EditorGUILayout.LabelField("图例：黄 = 总角度（目标）　青 = 头承担　紫 = 头 + 眼睛（实际目光）", EditorStyles.miniLabel);
        }

        private static float GetChannelAmount(HoLookAtDebug debug, HoLookAtEyeChannel channel)
        {
            switch (channel)
            {
                case HoLookAtEyeChannel.LookLeft:
                    return debug.lookLeft;

                case HoLookAtEyeChannel.LookRight:
                    return debug.lookRight;

                case HoLookAtEyeChannel.Up:
                    return debug.up;

                default:
                    return debug.down;
            }
        }

        /// <summary>
        /// 角度条：中心是正前方，往右 = 正角度；黄标 = 总角度，青条 = 头承担，紫标 = 眼睛残余。
        /// </summary>
        private static void DrawAngleBar(float limit, float total, float head, float eye)
        {
            Rect row = EditorGUILayout.GetControlRect(false, 20.0f);
            Rect bar = new Rect(row.x + 2.0f, row.y + 4.0f, Mathf.Max(20.0f, row.width - 76.0f), 12.0f);
            EditorGUI.DrawRect(bar, BarBackground);

            float center = bar.x + bar.width * 0.5f;
            float half = bar.width * 0.5f;
            float scale = Mathf.Max(1.0f, limit);

            // 限位范围（浅格子）
            DrawSegment(bar, center - half, center + half, new Color(1.0f, 1.0f, 1.0f, 0.06f));
            EditorGUI.DrawRect(new Rect(center, bar.y, 1.0f, bar.height), BarCenter);
            EditorGUI.DrawRect(new Rect(center - half, bar.y, 1.0f, bar.height), BarCenter);
            EditorGUI.DrawRect(new Rect(center + half - 1.0f, bar.y, 1.0f, bar.height), BarCenter);

            // 头承担
            DrawSegment(bar, center, center + Mathf.Clamp(head / scale, -1.0f, 1.0f) * half, HeadMarker);
            // 眼睛残余（叠在头上，用另一色标出来）
            DrawMarker(bar, center + Mathf.Clamp((head + eye) / scale, -1.0f, 1.0f) * half, EyeMarker, 3.0f);
            // 总角度
            DrawMarker(bar, center + Mathf.Clamp(total / scale, -1.0f, 1.0f) * half, TargetMarker, 2.0f);

            Rect text = new Rect(bar.xMax + 4.0f, row.y + 2.0f, 72.0f, row.height);
            GUI.Label(text, "总 " + total.ToString("0.0") + "° 限 " + limit.ToString("0") + "°", EditorStyles.miniLabel);
        }

        private static void DrawChannelBar(string label, float amount, float leftOutput, float rightOutput)
        {
            Rect row = EditorGUILayout.GetControlRect(false, 18.0f);
            Rect labelRect = new Rect(row.x, row.y + 1.0f, 92.0f, row.height);
            Rect bar = new Rect(row.x + 94.0f, row.y + 3.0f, Mathf.Max(20.0f, row.width - 94.0f - 96.0f), 11.0f);
            GUI.Label(labelRect, label, EditorStyles.miniLabel);

            EditorGUI.DrawRect(bar, BarBackground);
            float value = Mathf.Clamp01(amount);
            EditorGUI.DrawRect(new Rect(bar.x, bar.y, bar.width * value, bar.height), EyeMarker);
            EditorGUI.DrawRect(new Rect(bar.x, bar.y, 1.0f, bar.height), BarCenter);

            Rect text = new Rect(bar.xMax + 4.0f, row.y + 1.0f, 92.0f, row.height);
            GUI.Label(text, "量 " + value.ToString("0.00") + "  (" + leftOutput.ToString("0.#") + " / " + rightOutput.ToString("0.#") + ")", EditorStyles.miniLabel);
        }

        private static void DrawSegment(Rect bar, float from, float to, Color color)
        {
            float x0 = Mathf.Min(from, to);
            float x1 = Mathf.Max(from, to);
            if (x1 - x0 < 1.0f)
            {
                x1 = x0 + 1.0f;
            }

            EditorGUI.DrawRect(new Rect(x0, bar.y, x1 - x0, bar.height), color);
        }

        private static void DrawMarker(Rect bar, float x, Color color, float width)
        {
            float clamped = Mathf.Clamp(x, bar.x, bar.xMax - width);
            EditorGUI.DrawRect(new Rect(clamped, bar.y - 1.0f, width, bar.height + 2.0f), color);
        }

        private string GetModeSummary()
        {
            if ((HoLookAtMode)targetMode.enumValueIndex == HoLookAtMode.Mouse)
            {
                string sample = (HoLookAtMouseSampleMode)mouseSampleMode.enumValueIndex == HoLookAtMouseSampleMode.AngleMap ? "角度" : "场景点";
                return "鼠标 / " + sample;
            }

            return "跟随物体";
        }
    }
}
