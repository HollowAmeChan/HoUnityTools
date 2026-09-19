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
        private SerializedProperty mouseAimGain;
        private SerializedProperty mouseAimFromCharacter;
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
        private SerializedProperty headDirectionTrim;
        private SerializedProperty aimSmoothing;
        private SerializedProperty aimMaxSpeed;
        private SerializedProperty eyesEnabled;
        private SerializedProperty eyeDriver;
        private SerializedProperty renderers;
        private SerializedProperty eyeWeight;
        private SerializedProperty eyeSmoothing;
        private SerializedProperty eyeBoneLimitYaw;
        private SerializedProperty eyeBoneLimitPitch;
        private SerializedProperty horizontalInner;
        private SerializedProperty horizontalOuter;
        private SerializedProperty verticalUp;
        private SerializedProperty verticalDown;
        private SerializedProperty eyeAngleLimit;
        private SerializedProperty eyeEntries;
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
        private SerializedProperty drawOverlay;
        private SerializedProperty useSceneViewMouse;
        private SerializedProperty mergeMode;

        // 全部默认折叠：面板一打开就是一张"目录"，要看哪块点哪块
        private bool targetExpanded;
        private bool roleExpanded;
        private bool spineExpanded;
        private bool headExpanded;
        private bool eyesExpanded;
        private bool lostExpanded;
        private bool advancedExpanded;
        private bool debugExpanded;
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
            "看左", "看右", "看上", "看下"
        };

        private static readonly string[] ChannelTooltips =
        {
            "往角色左边看：左眼填 Out、右眼填 In。",
            "往角色右边看：左眼填 In、右眼填 Out。",
            "往上看（双眼共用一个键时两格填同名，只写一次）。",
            "往下看（双眼共用一个键时两格填同名，只写一次）。"
        };

        private void OnEnable()
        {
            targetMode = Find("targetMode");
            targetProperty = Find("target");
            targetOffset = Find("targetOffset");
            mouseCamera = Find("mouseCamera");
            mouseSensitivity = Find("mouseSensitivity");
            mouseAimGain = Find("mouseAimGain");
            mouseAimFromCharacter = Find("mouseAimFromCharacter");
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
            headDirectionTrim = Find("headDirectionTrim");
            aimSmoothing = Find("aimSmoothing");
            aimMaxSpeed = Find("aimMaxSpeed");
            eyesEnabled = Find("eyesEnabled");
            eyeDriver = Find("eyeDriver");
            renderers = Find("renderers");
            eyeWeight = Find("eyeWeight");
            eyeSmoothing = Find("eyeSmoothing");
            eyeBoneLimitYaw = Find("eyeBoneLimitYaw");
            eyeBoneLimitPitch = Find("eyeBoneLimitPitch");
            horizontalInner = Find("horizontalInner");
            horizontalOuter = Find("horizontalOuter");
            verticalUp = Find("verticalUp");
            verticalDown = Find("verticalDown");
            eyeAngleLimit = Find("eyeAngleLimit");
            eyeEntries = Find("eyeEntries");
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
            drawOverlay = Find("drawOverlay");
            useSceneViewMouse = Find("useSceneViewMouse");

            // 一次性迁移：老场景里是"角度摇杆"（跟鼠标位置没有几何关系，视线永远对不上鼠标），
            // 打开面板时自动切到"准星"并落到场景里；想用摇杆手感再切回来即可（标记已置位）。
            SerializedProperty migrated = Find("mouseModeMigrated");
            if (migrated != null && !migrated.boolValue)
            {
                migrated.boolValue = true;
                if ((HoLookAtMouseSampleMode)mouseSampleMode.enumValueIndex == HoLookAtMouseSampleMode.AngleMap)
                {
                    mouseSampleMode.enumValueIndex = (int)HoLookAtMouseSampleMode.CursorPoint;
                }

                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(serializedObject.targetObject);
            }
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
            if (GUILayout.Button(new GUIContent("一键装配", "自动找 Animator、清参考系、收集网格、填相机；有眼球骨骼就用骨骼模式，否则按模型上的键填好形态键通道。"), GUILayout.Height(22.0f)))
            {
                HoLookAtPresetActions.AutoRig(constraint, serializedObject);
            }

            if (GUILayout.Button(new GUIContent("清空通道", "只清掉形态键模式的四条通道，其他设置不动。"), GUILayout.Height(22.0f)))
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
                EditorGUILayout.PropertyField(mouseCamera, L("相机", "观众视角那台相机，用来把鼠标位置换算成世界方向。必须手动指定（运行时不会自动猜）。"));
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

                EditorGUILayout.PropertyField(mouseSampleMode, L("鼠标取法", "跟随鼠标（推荐）：鼠标相对角色屏幕位置的偏移 × 相机 FOV × 灵敏度 = 视线角度，鼠标指着角色就是看镜头。跟 VRM 生态的做法一致（three-vrm 的 LookAtRangeMap），屏幕边缘 ≈ 视角边缘，跟手且不会乱摆。\n角度摇杆：老式做法，鼠标位置线性换算成角度（灵敏度人为定），跟相机 FOV 无关。\n射线命中：用鼠标射线打到的场景物体当目标（相机在角色身后的第三人称瞄准式）。"));

                bool cursorPoint = (HoLookAtMouseSampleMode)mouseSampleMode.enumValueIndex == HoLookAtMouseSampleMode.CursorPoint;
                bool angleMap = (HoLookAtMouseSampleMode)mouseSampleMode.enumValueIndex == HoLookAtMouseSampleMode.AngleMap;
                bool raycast = (HoLookAtMouseSampleMode)mouseSampleMode.enumValueIndex == HoLookAtMouseSampleMode.Raycast;

                using (new EditorGUI.DisabledScope(!cursorPoint))
                {
                    EditorGUILayout.Slider(mouseAimGain, 0.1f, 3.0f, L("指向灵敏度", "1 = 鼠标推到画面边缘时，视线转到相机视角的边缘（屏幕上大致 1:1 跟手）。\n调大更\u201c甩\u201d、调小更\u201c稳\u201d。角度上限由相机 FOV 决定，所以不会在角色附近失控。"));
                    EditorGUILayout.PropertyField(mouseAimFromCharacter, L("以角色为原点", "开：鼠标指着角色在屏幕上的位置 = 看镜头，偏移从那里算（推荐）。\n关：偏移从屏幕中心算，适合相机永远盯着角色正中的机位。"));

                    Vector2 half = constraint.MouseAimHalfAngles;
                    EditorGUILayout.LabelField(
                        "当前：鼠标到画面边缘 ≈ 左右 " + half.x.ToString("0") + "° / 上下 " + half.y.ToString("0") + "°"
                        + (constraint.MouseCameraAssigned ? string.Empty : "（没指定相机，先按默认视角估算）"),
                        EditorStyles.miniLabel);
                }

                using (new EditorGUI.DisabledScope(!angleMap))
                {
                    EditorGUILayout.PropertyField(mouseSensitivity, L("灵敏度（度）", "摇杆模式：鼠标从画面中心推到边缘，左右/上下各转多少度。"));
                    EditorGUILayout.Slider(mouseDeadZone, 0.0f, 0.45f, L("鼠标死区", "摇杆模式：画面中心这一圈内不转，避免鼠标微抖带着眼睛动。"));
                    EditorGUILayout.PropertyField(mouseAngleSpace, L("角度坐标系", "摇杆模式：屏幕相对 = 往右看向画面右侧；角色相对 = 往右看向角色自己的右侧。"));
                }

                using (new EditorGUI.DisabledScope(!raycast))
                {
                    EditorGUILayout.PropertyField(mouseRaycastMask, L("射线层", "射线命中模式打哪些层。"));
                    EditorGUILayout.PropertyField(mouseDistance, L("兜底距离（米）", "射线没打中时，取射线上这个距离的点。"));
                }

                EditorGUILayout.PropertyField(mouseHoldOffscreen, L("离屏保持", "鼠标移出窗口/失焦时保持最后一次方向，而不是回中立。"));
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

            EditorGUILayout.Slider(weight, 0.0f, 1.0f, L("总强度", "整个注视的总强度：0 = 不注视，1 = 按三块的比例全额生效。\n做「看一眼再移开」这类演出改它就够。"));

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
            if (GUILayout.Button(new GUIContent("只看眼睛", "关掉头颈与脊椎，只让眼珠跟（对话、特写常用）。"), EditorStyles.miniButton))
            {
                HoLookAtPresetActions.EyesOnly(serializedObject);
            }

            if (GUILayout.Button(new GUIContent("头眼并用", "恢复默认分工：头 1.0 + 身体 0.3。"), EditorStyles.miniButton))
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
            string summary = spineEnabled.boolValue ? "身体 " + bodyWeight.floatValue.ToString("0.##") : "关";
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref spineExpanded, "脊椎跟随", summary, SpineColor))
            {
                return;
            }

            EditorGUILayout.PropertyField(spineEnabled, L("启用", "身体跟着转一点。只想让头动就关掉。"));
            using (new EditorGUI.DisabledScope(!spineEnabled.boolValue))
            {
                EditorGUILayout.Slider(bodyWeight, 0.0f, 1.0f, L("身体强度", "身体参与比例，Unity 沿脊椎分摊。0.2~0.4 自然，越大腰跟着扭。"));
            }
        }

        private void DrawHeadSection()
        {
            string summary = headEnabled.boolValue ? "头部 " + headWeight.floatValue.ToString("0.##") : "关";
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref headExpanded, "头颈跟随", summary, HeadColor))
            {
                return;
            }

            EditorGUILayout.PropertyField(headEnabled, L("启用", "用 Unity 的 LookAt IK 转头颈。需要 humanoid + 图层勾 IK Pass。"));
            using (new EditorGUI.DisabledScope(!headEnabled.boolValue))
            {
                EditorGUILayout.Slider(headWeight, 0.0f, 1.0f, L("头部强度", "头颈参与比例：1 = 完全对准（限位内），0.5 = 只转一半。没转到的部分由眼睛补。"));
                EditorGUILayout.Slider(deadZone, 0.0f, 89.0f, L("起始死区（度）", "目标离正前方这么近时头不动、只让眼睛动，避免小幅目标让头一直微抖。"));
                EditorGUILayout.Slider(headShare, 0.0f, 1.0f, L("头部承担", "超出死区后头承担的比例，剩下给眼睛（头吃七成 = 0.7）。"));
                EditorGUILayout.PropertyField(headDirectionTrim, L("头朝向偏差（度）", "模型静止姿势的头不朝正前方时，那个固定差值（左右/上下）。\n症状：目标怎么动，头和眼睛都固定偏同一个方向 → 对着 Gizmo 调这里，眼睛会一起补正。\n正常模型填 0。"));
                using (NarrowLabels(74.0f))
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PropertyField(headLimitYaw, L("左右限位", "头最多往左右各转多少度，超出部分交给眼睛。"));
                    EditorGUILayout.PropertyField(headLimitPitch, L("上下限位", "头最多往上/下各转多少度（对称）。"));
                    EditorGUILayout.EndHorizontal();
                }
            }
        }

        private void DrawEyesSection(HoLookAtConstraint constraint)
        {
            bool bones = (HoLookAtEyeDriver)eyeDriver.enumValueIndex == HoLookAtEyeDriver.EyeBones;
            string summary = !eyesEnabled.boolValue ? "关" : (bones ? "眼球骨骼" : "形态键");
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref eyesExpanded, "眼睛跟随", summary, EyeColor))
            {
                return;
            }

            EditorGUILayout.PropertyField(eyesEnabled, L("启用", "眼睛跟着目标。骨骼模式转 humanoid 的 LeftEye/RightEye，形态键模式写凝视键。"));
            using (new EditorGUI.DisabledScope(!eyesEnabled.boolValue))
            {
                EditorGUILayout.PropertyField(eyeDriver, L("驱动方式", "眼球骨骼（默认）：残余角直接转到 LeftEye/RightEye，指哪看哪、不用标定。\n形态键：残余角过四条曲线写成凝视键，给没有眼球骨骼的模型。\n切换时旧的形态键会自动交还回基准值。"));

                EditorGUILayout.Slider(eyeWeight, 0.0f, 1.0f, L("眼球强度", "眼睛参与比例，可再打个折。头没转到的部分由眼睛补。"));
                EditorGUILayout.PropertyField(eyeSmoothing, L("平滑（秒）", "眼睛角度的一阶平滑（比头部快，0.04 左右）。0 = 不平滑、最跟手。"));

                if (bones)
                {
                    if (!constraint.EyeBonesAvailable)
                    {
                        EditorGUILayout.HelpBox(
                            "找不到眼球骨骼：humanoid 的 LeftEye / RightEye 至少要有一根（Avatar 里配好），否则眼睛不会动。"
                            + "没有眼球骨骼的模型请把「驱动方式」切成「形态键」。",
                            MessageType.Warning);
                    }

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PropertyField(eyeBoneLimitYaw, L("左右限位（度）", "眼球最多往左右各转多少度（默认 15）。超出就夹住，避免翻白眼。"));
                    EditorGUILayout.PropertyField(eyeBoneLimitPitch, L("上下限位（度）", "眼球最多往上/下各转多少度（默认 10，对称）。"));
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.LabelField(
                        "眼球转动 左右 " + constraint.AppliedEyeYaw.ToString("0.0") + "°　上下 " + constraint.AppliedEyePitch.ToString("0.0") + "°",
                        EditorStyles.miniLabel);
                }
                else
                {
                    EditorGUILayout.PropertyField(renderers, L("目标网格", "哪些网格上有凝视形态键。点下面的按钮一次收集所有子网格。"), true);
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("收集子级网格"))
                    {
                        Undo.RecordObject(constraint, "收集注视约束网格");
                        constraint.CollectChildRenderers();
                        EditorUtility.SetDirty(constraint);
                        serializedObject.Update();
                    }

                    if (GUILayout.Button(new GUIContent("重新解析键", "改了键名或换了模型后点一下，重新在网格上找键。")))
                    {
                        constraint.Rebuild();
                    }

                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.PropertyField(mergeMode, HoConstraintEditorSectionGui.MergeModeLabel);
                    EditorGUILayout.PropertyField(eyeAngleLimit, L("角度上限 往右/往左/上/下（度）", "这个角度内眼睛完全跟上，超过就按曲线饱和。\n按模型实际可动范围标定（默认 15/15/10/10）。"));
                    EditorGUILayout.LabelField("四条方向曲线（横轴 = 上面角度上限的比例，纵轴 = 输出）", EditorStyles.miniLabel);
                    EditorGUILayout.PropertyField(horizontalInner, L("往右曲线", "往右看这条通道的映射形状，直线 = 线性。"));
                    EditorGUILayout.PropertyField(horizontalOuter, L("往左曲线", "往左看这条通道的映射形状。"));
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
                        entryFoldouts.Add(false);
                    }

                    for (int i = 0; i < eyeEntries.arraySize; i++)
                    {
                        DrawEyeEntry(constraint, i);
                    }

                    if (GUILayout.Button(new GUIContent("+ 通道", "一条通道 = 一种眼动方向（看左/看右/看上/看下）。"), GUILayout.Width(80.0f)))
                    {
                        eyeEntries.InsertArrayElementAtIndex(eyeEntries.arraySize);
                    }

                    if (constraint.MissingKeys.Count > 0)
                    {
                        EditorGUILayout.HelpBox("以下键在目标网格上不存在：" + string.Join("、", constraint.MissingKeys), MessageType.Warning);
                    }
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
                new GUIContent("左右族", "VRM/Meta 的「看左/看右」键名：双眼共用一个键，或左右眼各一个。"),
                EditorStyles.miniButton,
                GUILayout.Width(58.0f));
            bool clickedInnerOuter = GUILayout.Button(
                new GUIContent("内外族", "ARKit/PICO 的 In/Out 键名，会拆成：往右 = 左眼 In + 右眼 Out，往左 = 左眼 Out + 右眼 In。"),
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
                GUI.Label(gainLabelRect, new GUIContent("增益", "通道量乘上它再写出去：1 = 曲线拉满输出 100。个别键太夸张就压这里。"), EditorStyles.miniLabel);
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

            EditorGUILayout.PropertyField(lostBehavior, L("丢失行为", "目标丢了（物体被删/鼠标离屏）怎么办：\n停在最后方向 / 慢慢回正前方 / 立刻松开交给动画。"));
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
                EditorGUILayout.PropertyField(aimSmoothing, L("方向平滑", "目标方向的一阶平滑时间常数（秒）。0 = 立刻对准，会有点硬。"));
                EditorGUILayout.PropertyField(aimMaxSpeed, L("最大角速度", "转头速度上限（度/秒），防止目标瞬移时头猛甩。0 = 不限。"));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.PropertyField(teleportAngleThreshold, L("瞬移阈值（度）", "角度跳变超过它就当瞬移，直接跟上、不慢慢转。"));
            using (new EditorGUI.DisabledScope(!spineEnabled.boolValue))
            {
                EditorGUILayout.Slider(spineMinAngle, 0.0f, 90.0f, L("脊椎起始角（度）", "总角度超过它身体才参与，避免小幅注视也带着上半身动。"));
            }

            EditorGUILayout.Space(2.0f);
            EditorGUILayout.LabelField("丢失之后", EditorStyles.miniBoldLabel);
            using (NarrowLabels(88.0f))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(returnDelay, L("回正延迟", "丢失后先保持这么久（秒）再回正。"));
                EditorGUILayout.PropertyField(returnSpeed, L("回正速度", "回正时的角速度（度/秒）。"));
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(2.0f);
            EditorGUILayout.LabelField("组件", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(animator, L("Animator", "留空 = 自己/父级/子级里自动找；只有自动找错时才需要填。"));
            EditorGUILayout.PropertyField(reference, L("参考系", "算角度和限位用的朝向。留空 = Animator 所在物体（角色根，推荐）。\n要手动指定时一定用角色根物体，别填骨骼，否则角度全部失准。"));
            EditorGUILayout.Space(2.0f);
            EditorGUILayout.LabelField("写入", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(writeThreshold, L("写入阈值", "形态键变化小于它就不写，减少网格 dirty。调大省性能，细微变化会被忽略。"));
            EditorGUILayout.PropertyField(evaluateInEditMode, L("编辑模式求值", "不播放也在编辑器里跑一遍（调试用，会把场景标脏）。"));
        }

        // ── 调试（可视化） ──────────────────────────────────────────────────

        private void DrawDebugSection(HoLookAtConstraint constraint)
        {
            HoLookAtDebug debug = constraint.GetDebug();
            // 折叠状态下摘要就是唯一可见的信息，所以这里直接把"目光误差"报出来
            float errorSum = Mathf.Abs(constraint.GazeErrorYaw) + Mathf.Abs(constraint.GazeErrorPitch);
            string summary = !debug.hasTarget
                ? "无目标"
                : (errorSum < 1.0f ? "误差 " + errorSum.ToString("0.0") + "°（精确）" : "误差 " + errorSum.ToString("0.0") + "°");
            if (!HoConstraintEditorSectionGui.DrawSectionHeader(ref debugExpanded, "调试", summary, DebugColor))
            {
                return;
            }

            EditorGUILayout.PropertyField(drawGizmos, L("场景 Gizmo", "在 Scene 视图画参考朝向、限位框、目标方向、头部朝向与目光（选中本物体可见）。"));
            EditorGUILayout.PropertyField(drawOverlay, L("Game 视图叠加", "Game 视图里叠读数与三个标记：青十字 = 鼠标，黄点 = 目标，紫点 = 目光落点；连线 = 偏差。\n只在编辑器里生效（构建里没有这段代码），可以一直开着。"));
            EditorGUILayout.PropertyField(useSceneViewMouse, L("Scene 视图鼠标", "鼠标放在 Scene 视图上时，临时用 Scene 视图相机当观众视角：一边看 Gizmo 一边用鼠标调。\n鼠标回到 Game 视图自动切回真实输入。"));

            EditorGUILayout.LabelField("左右（yaw，正 = 角色右侧）", EditorStyles.miniBoldLabel);
            DrawAngleBar(constraint.HeadLimitYaw, debug.targetYaw, debug.headEstimateYaw, debug.eyeYaw);
            EditorGUILayout.LabelField("上下（pitch，正 = 抬头）", EditorStyles.miniBoldLabel);
            DrawAngleBar(constraint.HeadLimitPitch, debug.targetPitch, debug.headEstimatePitch, debug.eyePitch);

            EditorGUILayout.Space(2.0f);
            EditorGUILayout.LabelField("状态：总角度 " + debug.targetYaw.ToString("0.0") + "° / " + debug.targetPitch.ToString("0.0") + "°"
                + "　头（估计） " + debug.headEstimateYaw.ToString("0.0") + "° / " + debug.headEstimatePitch.ToString("0.0") + "°"
                + "　头增量 " + debug.headDeltaYaw.ToString("0.0") + "° / " + debug.headDeltaPitch.ToString("0.0") + "°",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField("目标 " + debug.targetYaw.ToString("0.0") + "° / " + debug.targetPitch.ToString("0.0") + "°"
                + "　眼睛残余 " + debug.eyeYaw.ToString("0.0") + "° / " + debug.eyePitch.ToString("0.0") + "°"
                + "　目光误差 " + constraint.GazeErrorYaw.ToString("0.0") + "° / " + constraint.GazeErrorPitch.ToString("0.0") + "°"
                + (Mathf.Abs(constraint.GazeErrorYaw) + Mathf.Abs(constraint.GazeErrorPitch) < 1.0f ? "（精确）" : "（有偏差，见下）")
                + (Mathf.Abs(debug.eyeOverflowYaw) + Mathf.Abs(debug.eyeOverflowPitch) > 0.5f
                    ? "　眼球超范围 " + debug.eyeOverflowYaw.ToString("0.0") + "° / " + debug.eyeOverflowPitch.ToString("0.0") + "°（已还给头）"
                    : string.Empty),
                EditorStyles.miniLabel);

            bool bones = constraint.EyeDriver == HoLookAtEyeDriver.EyeBones;
            EditorGUILayout.LabelField(
                bones
                    ? "驱动：眼球骨骼" + (constraint.EyeBonesAvailable ? "（LeftEye/RightEye ✓）" : "（找不到眼球骨骼 ✗）")
                      + "　眼球骨骼实际转 " + constraint.AppliedEyeYaw.ToString("0.0") + "° / " + constraint.AppliedEyePitch.ToString("0.0") + "°"
                      + "　限位 " + constraint.EyeBoneLimitYaw.ToString("0") + "° / " + constraint.EyeBoneLimitPitch.ToString("0") + "°"
                    : "驱动：形态键　网格 " + constraint.MeshCount + " / 绑定 " + constraint.BindingCount,
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField("OnAnimatorIK " + (constraint.IkRecentlyCalled ? "在跑" : "未调用"), EditorStyles.miniLabel);

            if (bones)
            {
                EditorGUILayout.Space(2.0f);
                EditorGUILayout.LabelField("骨骼模式不写形态键，所以没有通道读数；要看通道条形请把「驱动方式」切成「形态键」。", EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.Space(2.0f);
                EditorGUILayout.LabelField("四条通道（条形 = 写入比例，括号 = 左右眼实际写出的形态键值）", EditorStyles.miniBoldLabel);
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
            }

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
                int index = Mathf.Clamp(mouseSampleMode.enumValueIndex, 0, mouseSampleMode.enumDisplayNames.Length - 1);
                return "鼠标 / " + mouseSampleMode.enumDisplayNames[index];
            }

            return "跟随物体";
        }
    }
}
