using Hollow.HoUnityTools.Animations;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.AnimationTools
{
    /// <summary>
    /// <see cref="HoAnimationPreviewer"/> 的面板。
    ///
    /// <para>**外面只露一个走带**：标题、一条时间读数、一条自绘时间轴加五个图标按钮。
    /// 所有设置都在默认折叠的「设置」里 —— 这个面板是拿来"看一眼 clip"的，不是拿来调参的。</para>
    ///
    /// <para>编辑器里没有游戏循环，所以播放头由本面板的 <c>EditorApplication.update</c> 回调喂时间；
    /// 播放模式里则由组件自己的 <c>Update</c> 推进，两边共用同一套接口，不会重复推进。
    /// 用户抓时间轴时，这里的自动推进让位（`HoAnimationPreviewTimeline.Dragging`）。</para>
    /// </summary>
    [CustomEditor(typeof(HoAnimationPreviewer))]
    internal sealed class HoAnimationPreviewerEditor : UnityEditor.Editor
    {
        private HoAnimationPreviewer previewer;
        private SerializedProperty clipProperty;
        private SerializedProperty animatorProperty;
        private SerializedProperty playOnEnableProperty;
        private SerializedProperty loopProperty;
        private SerializedProperty rootMotionProperty;

        /// <summary>「设置」折叠栏的开关。默认关 —— 面板上只留走带。</summary>
        private bool settingsExpanded;

        /// <summary>「走带图标」诊断栏的开关。默认关。</summary>
        private bool iconDiagExpanded;

        /// <summary>编辑器里的播放时钟。</summary>
        private double lastEditorTime;
        private bool drivingFromEditor;

        private static readonly float[] SpeedPresets = { 0.1f, 0.25f, 0.5f, 1f, 2f, 4f };

        private static readonly GUIContent AnimatorLabel = new GUIContent("Animator", "留空则从本物体向父级查找。Humanoid 剪辑需要它上面有人形 Avatar。");

        private void OnEnable()
        {
            previewer = target as HoAnimationPreviewer;
            clipProperty = serializedObject.FindProperty("clip");
            animatorProperty = serializedObject.FindProperty("targetAnimator");
            playOnEnableProperty = serializedObject.FindProperty("playOnEnable");
            loopProperty = serializedObject.FindProperty("loop");
            rootMotionProperty = serializedObject.FindProperty("applyRootMotion");

            lastEditorTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            drivingFromEditor = false;
        }

        /// <summary>
        /// 编辑器里的推进源。只在**未进入播放模式**时工作 —— 播放模式里组件的
        /// <c>Update</c> 已经在推同一个播放头，两边都推会变成两倍速。
        /// </summary>
        private void OnEditorUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            double delta = now - lastEditorTime;
            lastEditorTime = now;

            if (previewer == null || Application.isPlaying)
            {
                drivingFromEditor = false;
                return;
            }

            // OnValidate 期间不能重建（禁止 SendMessage），推迟到这里做。
            previewer.FlushPendingRebuilds();

            // 断掉的预览（Animator 被禁用、被别的预览器抢走…）也在这里收掉。
            previewer.MaintainPreviewState();

            if (!previewer.IsPlaying)
            {
                drivingFromEditor = false;
                return;
            }

            // 用户正在抓时间轴：播放头归鼠标，这里让位。
            if (HoAnimationPreviewTimeline.Dragging)
                return;

            // 从暂停切到播放的头一帧，lastEditorTime 可能停在很久以前，夹一下不让它跳帧。
            if (!drivingFromEditor)
            {
                drivingFromEditor = true;
                return;
            }

            if (!previewer.IsPlayableReady)
                return;

            previewer.AdvanceBy((float)System.Math.Min(delta, 0.1));
            Repaint();
            SceneView.RepaintAll();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawTimelineRow();
            DrawSettings();
            DrawFooter();

            serializedObject.ApplyModifiedProperties();
        }

        // ── 走带 ────────────────────────────────────────────────────────

        private void DrawTimelineRow()
        {
            bool hasError = previewer != null && !string.IsNullOrEmpty(previewer.LastError);
            bool hasWarning = previewer != null
                && (previewer.Clip == null || !previewer.CanPreview || previewer.MissingHumanoidAvatar);
            bool loop = loopProperty != null && loopProperty.boolValue;

            // 剪辑字段就在按钮行的右侧 —— 挂载即用，不用再往下找一行。
            HoAnimationPreviewTimeline.Draw(previewer, loop, hasError, hasWarning, clipProperty);
        }

        // ── 设置（默认折叠，自绘控件）───────────────────────────────────

        private void DrawSettings()
        {
            EditorGUILayout.Space(2f);

            settingsExpanded = EditorGUILayout.Foldout(settingsExpanded, Summary(), true, HoAnimationPreviewTheme.TextDim);

            if (!settingsExpanded)
                return;

            EditorGUILayout.Space(3f);

            // 设置区不用 EditorGUILayout 的自动布局 —— 那会把滑条铺满整行、数字框拉得老长。
            // 每一行自己算矩形，和约束面板同一套规矩。
            DrawAnimatorRow();
            DrawSpeedRow();
            DrawToggleRow("循环", loopProperty);
            DrawToggleRow("根运动", rootMotionProperty);
            DrawToggleRow("播放模式启用即播", playOnEnableProperty);

            DrawIconDiag();
            EditorGUILayout.Space(2f);
        }

        /// <summary>
        /// 走带图标用的是 Unity 内置图标，名字在不同版本未必一样。
        /// 这里把"实际探测到了哪个"摆出来 —— 图标看着不对时不用猜，展开看一行就知道。
        /// </summary>
        private void DrawIconDiag()
        {
            iconDiagExpanded = EditorGUILayout.Foldout(
                iconDiagExpanded, "走带图标", true, HoAnimationPreviewTheme.Caption);

            if (!iconDiagExpanded)
                return;

            GUIStyle style = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9, wordWrap = true };
            style.normal.textColor = HoAnimationPreviewTheme.TextFaintColor;
            EditorGUILayout.LabelField(HoAnimationPreviewIcons.ResolvedReport(), style);
        }

        private void DrawAnimatorRow()
        {
            if (animatorProperty == null)
                return;

            using (HoAnimationPreviewTheme.Row row = HoAnimationPreviewTheme.BeginRow())
            {
                row.Label("Animator");
                // ObjectField 也是 IMGUI，只是矩形由我们给 —— 这样才定得住宽。
                EditorGUI.BeginChangeCheck();
                EditorGUI.ObjectField(row.Rest(), animatorProperty, GUIContent.none);
                if (EditorGUI.EndChangeCheck())
                    serializedObject.ApplyModifiedProperties();
            }
        }

        private void DrawSpeedRow()
        {
            using (HoAnimationPreviewTheme.Row row = HoAnimationPreviewTheme.BeginRow())
            {
                row.Label("倍速");

                float speed = HoAnimationPreviewTheme.NumberField(
                    row.Field(HoAnimationPreviewTheme.FieldWidth + 8f),
                    previewer != null ? previewer.PlaybackSpeed : 1f,
                    "x");

                if (previewer != null && !Mathf.Approximately(speed, previewer.PlaybackSpeed))
                    SetSpeed(speed);

                // 档位跟在数字后面，一行放得下就画。
                float buttonWidth = 32f;
                float needed = (SpeedPresets.Length * buttonWidth) + ((SpeedPresets.Length - 1) * 2f);
                if (needed <= row.Remaining)
                {
                    for (int i = 0; i < SpeedPresets.Length; i++)
                    {
                        float preset = SpeedPresets[i];
                        bool on = previewer != null && Mathf.Approximately(previewer.PlaybackSpeed, preset);
                        Rect slot = row.Field(buttonWidth);
                        slot.width = buttonWidth;
                        if (HoAnimationPreviewTheme.Button(slot, preset.ToString("0.##") + "x", on))
                            SetSpeed(preset);
                    }
                }
            }
        }

        private void SetSpeed(float speed)
        {
            if (previewer == null)
                return;

            Undo.RecordObject(previewer, "Set Preview Speed");
            previewer.PlaybackSpeed = speed;
            EditorUtility.SetDirty(previewer);
        }

        private void DrawToggleRow(string label, SerializedProperty property)
        {
            if (property == null)
                return;

            using (HoAnimationPreviewTheme.Row row = HoAnimationPreviewTheme.BeginRow())
            {
                row.Label(label);
                bool value = HoAnimationPreviewTheme.Check(
                    row.Field(HoAnimationPreviewTheme.CheckSize), property.boolValue);

                if (value != property.boolValue)
                {
                    property.boolValue = value;
                    serializedObject.ApplyModifiedProperties();
                }
            }
        }

        /// <summary>折叠栏摘要：不展开也知道现在是什么状态。</summary>
        private string Summary()
        {
            if (previewer == null)
                return "设置";

            string speed = previewer.PlaybackSpeed.ToString("0.##") + "x";
            string loop = loopProperty != null && loopProperty.boolValue ? "循环" : "单次";
            string root = rootMotionProperty != null && rootMotionProperty.boolValue ? "根运动开" : "根运动关";

            float fps = previewer.FrameRate;
            string rate = fps > 0f ? " · " + fps.ToString("0.##") + "fps" : string.Empty;

            return "设置 · " + speed + " · " + loop + " · " + root + rate;
        }

        // ── 底部说明（只在有问题时出现一行）─────────────────────────────

        private void DrawFooter()
        {
            string message = FooterMessage();
            if (string.IsNullOrEmpty(message))
                return;

            Rect rect = EditorGUILayout.GetControlRect(false, 13f);

            GUIStyle style = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 9,
                clipping = TextClipping.Clip,
                wordWrap = false
            };
            style.normal.textColor = HoAnimationPreviewTheme.TextFaintColor;

            // 一行截断，完整原因进 tooltip —— 不为了放长句子撑高面板。
            GUI.Label(rect, new GUIContent(message, message), style);
        }

        private string FooterMessage()
        {
            if (previewer == null)
                return string.Empty;

            if (!string.IsNullOrEmpty(previewer.LastError))
                return previewer.LastError;

            if (previewer.MissingHumanoidAvatar)
                return "这条是 Humanoid 剪辑，但目标 Animator 上没有合法人形 Avatar，Unity 无法重定向到骨骼。";

            if (previewer.Clip != null && !previewer.CanPreview)
                return "没找到 Animator。Humanoid 剪辑必须由带人形 Avatar 的 Animator 求值。";

            if (previewer.Clip == null)
                return "把一条 AnimationClip 拖进来的「剪辑」即可 —— 不需要任何 AnimatorController。";

            return string.Empty;
        }
    }
}
