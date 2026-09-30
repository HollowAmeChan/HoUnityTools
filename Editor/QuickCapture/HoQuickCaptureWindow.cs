// HoQuickCaptureWindow.cs -- 快速渲染面板：拍一张 / 录一段
//
// 形态仿 Unity 官方的 Recorder 包（`com.unity.recorder`），但只留两件事、且**不依赖它**：
//   · 拍一张当前游戏视图 → 输出目录；
//   · 按"多少帧 / 多少秒"离线录一段 → 输出目录（PNG 序列）。
// 没有动画录制、音频、AOV、在线编码、时间轴片段那些东西，也没有 .asset 配置资产：
// 这个面板的定位是"手边快捷键"，不是"工程化的录制管线"。
//
// **不复制 Recorder 的代码**：面板、数据、帧泵全部按本仓规矩重写（中文界面、配置进 EditorPrefs、
// 贴图自己生成）。从它那里借鉴的只有**机制**，且都在源码注释里点名了出处：
//   · 抓游戏视图 = `ScreenCapture.CaptureScreenshotIntoRenderTexture`（Recorder 的 GameViewInput 用的同一个调用）；
//   · 离线步进 = `Time.captureDeltaTime`（Recorder 从不用已过时的 `Time.captureFramerate`，见 Recorder.cs）；
//   · 帧泵 = 播放模式 + 协程 `WaitForEndOfFrame`（Recorder 的 _FrameRequestComponent 是同一套）。
//
// ══════════════════════════════════════════════════════════════════
// 界面规矩（2026-09-30 用户定，**别加回去**）
// ══════════════════════════════════════════════════════════════════
// 这个面板**刻意不套用约束面板那套栅格/卡片/分区标题**：那套是给"参数很多、需要讲解"的
// Inspector 用的，而这里要的是**两行就干完活**。形态向隔壁播放速度条看齐：
//   · 第一行：`[截帧]  <分辨率>  <输出目录>`
//   · 第二行：`[录制]  <时长>`  /  录制中 = `[暂停] [停止]  ▇▇▇▇ 32%  4.2s`
//   · 第三行：一个折叠「高级」，默认收起。所有其余设置（分辨率模式、帧率、前缀、
//     每次录制的子目录、完成后打开目录、PNG 质量、还原默认）都在里面。
// **正文里不写说明文字** —— 该解释的一律进悬浮工具提示（`GUIContent` 的第二参）。
// 例外只有两条：报错、录制进度，因为那两样是"现在发生了什么"，藏起来等于没说。
//
// ⚠️ 枚举控件只能用 `Segmented` / `EnumControl(..., segmented: true)`：`Dropdown` 走异步菜单，
// **拿不到用户的选择**（它永远返回入参）。这条踩过，见 HoConstraintEditorControls.Dropdown 的注释。
using System;
using System.Globalization;
using Hollow.HoUnityTools.Editor.Constraints;
using Hollow.HoUnityTools.Runtime.QuickCapture;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.QuickCapture
{
    /// <summary>快速渲染面板。菜单：<c>HoUnityTools/快速渲染</c>。</summary>
    internal sealed class HoQuickCaptureWindow : EditorWindow
    {
        // ── 排版（照播放速度条那套紧凑尺寸）──
        private const float RowHeight = 20f;
        private const float ControlHeight = 18f;
        private const float Pad = 4f;
        private const float Gap = 6f;

        private const float ActionButtonWidth = 54f;
        private const float FoldFieldWidth = 52f;
        private const float PctWidth = 34f;

        /// <summary>窗口最小宽度：第一行要装下 截帧 + 格式 + 分辨率 + 目录框。</summary>
        private const float MinWindowWidth = 400f;

        private static readonly string[] UnitOptions = { "s", "帧" };
        private static readonly string[] ResolutionOptions = { "跟随视图", "自定义" };
        private static readonly string[] ImageFormatOptions = { "png", "jpg", "exr" };
        private static readonly string[] VideoFormatOptions = { "mp4", "PNG 序列" };
        private static readonly string[] ProfileOptions = { "Baseline", "Main", "High" };
        private static readonly string[] SourceOptions = { "游戏视图", "指定相机" };

        private static readonly GUIContent ShotContent = new GUIContent(
            "截帧",
            "把当前游戏视图存成一张图。分辨率 = 游戏视图当前的渲染分辨率。\n\n"
            + "**只在播放模式下可用**：编辑模式里没有正在推进的游戏帧，\n"
            + "抓到的画面不可靠（会抓到编辑器界面，或者只是「某一时刻碰巧画完」的那一帧）。\n"
            + "先按 Play。\n\n"
            + "播放中会等这一帧画完再拍，拿到的是完整的合成结果。");

        private static readonly GUIContent RecordContent = new GUIContent(
            "录制",
            "离线录一段到输出目录。录制不走真实时间：游戏时间按帧率一步一步走，机器慢也不会丢帧。\n\n"
            + "同样只在播放模式下可用 —— 编辑模式里时间不前进，录不出会动的画面。");

        private static readonly GUIContent PauseContent = new GUIContent(
            "暂停",
            "冻结模拟（Time.timeScale = 0），画面继续画。可反复暂停/继续，帧不会被吃掉。");

        private static readonly GUIContent ResumeContent = new GUIContent(
            "继续",
            "解除冻结，游戏时间从暂停处接着走。");

        private static readonly GUIContent StopContent = new GUIContent(
            "停止",
            "停下并收好已经录到的帧（不删文件）。");

        private static readonly GUIContent AdvancedContent = new GUIContent(
            "高级",
            "分辨率模式、帧率、文件名、目录结构、PNG 质量、恢复默认。");

        /// <summary>「高级」展开状态。存 EditorPrefs：不放进 settings —— 它是"界面折叠"不是"参数"。</summary>
        private const string AdvancedPrefsKey = "com.hollow.hounitytools.quickcapture.advanced";

        private bool advancedExpanded;

        [MenuItem("HoUnityTools/快速渲染", false, 7)]
        internal static void Open()
        {
            GetWindow<HoQuickCaptureWindow>(false, "快速渲染", true);
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("快速渲染", "快速保存当前游戏视图 / 离线录制一段。");
            HoQuickCaptureEngine.Changed += Repaint;
            minSize = new Vector2(MinWindowWidth, 74f);
            advancedExpanded = EditorPrefs.GetBool(AdvancedPrefsKey, false);
        }

        private void OnDisable()
        {
            HoQuickCaptureEngine.Changed -= Repaint;
            HoQuickCaptureSettings.Save();
            EditorPrefs.SetBool(AdvancedPrefsKey, advancedExpanded);
        }

        private void OnDestroy()
        {
            HoQuickCaptureEngine.ReleasePreview();
        }

        /// <summary>录制中要持续重画（进度在动）。20Hz 足够。</summary>
        private void OnInspectorUpdate()
        {
            if (HoQuickCaptureEngine.IsBusy)
            {
                Repaint();
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 画
        // ══════════════════════════════════════════════════════════════

        private void OnGUI()
        {
            HoQuickCaptureSettingsData data = HoQuickCaptureSettings.Data;
            HoQuickCaptureStatus status = HoQuickCaptureEngine.GetStatus();

            // 一次 OnGUI 里只快照一次：IMGUI 要求 Layout 与 Repaint 两个 pass 的布局调用
            // 数量与顺序完全一致，而引擎状态是外部协程改的。统一在这里读，绘制路径只读快照。
            Texture2D preview = HoQuickCaptureEngine.Preview;

            // 不套 ScrollView：面板大部分时间是两行（~80px），展开「高级」也就多两百来像素，
            // 直接让窗口自己滚就行 —— 套一层反而会和 minSize 打架。内容本来也不宽，不需要横向滚动。
            GUILayout.Space(Pad);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(Pad);
                DrawShotRow(data, status);
                GUILayout.Space(Pad);
            }

            GUILayout.Space(2f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(Pad);
                DrawRecordRow(data, status);
                GUILayout.Space(Pad);
            }

            if (!string.IsNullOrEmpty(status.Error) || !string.IsNullOrEmpty(status.Warning))
            {
                GUILayout.Space(2f);
                DrawNotice(status);
            }

            GUILayout.Space(2f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(Pad);
                DrawAdvancedToggle();
                GUILayout.FlexibleSpace();
                GUILayout.Space(Pad);
            }

            if (advancedExpanded)
            {
                DrawAdvanced(data, status, preview);
            }

            GUILayout.Space(Pad);
        }

        // ── 第一行：截帧 · 分辨率 · 输出目录 ────────────────────────

        private void DrawShotRow(HoQuickCaptureSettingsData data, HoQuickCaptureStatus status)
        {
            // 只问一次：`CheckCanStart` 每次都要读编辑器状态，而两个 pass 都得给出同一个答案。
            string blocked = status.IsBusy ? "正在忙，等它结束。" : HoQuickCaptureEngine.CheckCanStart();
            GUIContent content = blocked == null ? ShotContent : new GUIContent(ShotContent.text, blocked);

            using (new EditorGUI.DisabledScope(blocked != null))
            {
                if (GUILayout.Button(content, EditorStyles.miniButton, GUILayout.Width(ActionButtonWidth), GUILayout.Height(ControlHeight)))
                {
                    HoQuickCaptureEngine.TakeScreenshot();
                }
            }

            GUILayout.Space(Gap);

            // 格式（枚举）：png / jpg / exr。默认 png。
            Rect formatRect = GUILayoutUtility.GetRect(
                HoConstraintEditorControls.SegmentedWidth(ImageFormatOptions),
                HoConstraintEditorControls.SegmentedWidth(ImageFormatOptions),
                ControlHeight,
                ControlHeight);
            int imageFormat = HoConstraintEditorControls.Segmented(
                formatRect,
                (int)data.imageFormat,
                ImageFormatOptions,
                "存成什么格式：\npng —— 无损，体积大（默认）\njpg —— 有损，体积小\nexr —— 浮点，给后期合成用");
            if (imageFormat != (int)data.imageFormat)
            {
                data.imageFormat = (HoQuickCaptureImageFormat)imageFormat;
                HoQuickCaptureSettings.NotifyChanged();
            }

            GUILayout.Space(Gap);

            // 分辨率读数：出图就是多大。录制中显示本次会话锁定的尺寸。
            string resolution = status.IsBusy && status.Width > 0
                ? status.Width + "x" + status.Height
                : CurrentResolutionText(data);
            GUILayout.Label(
                new GUIContent(resolution, ResolutionTooltip(data)),
                EditorStyles.miniLabel,
                GUILayout.Width(ResolveLabelWidth(resolution)));

            GUILayout.Space(Gap);

            // 输出目录：编辑框直接放在外面（"要输入一个输出文件夹路径"这件事一眼可见）。
            //
            // ⚠️ 这里以前右边还跟两颗按钮（「选目录」…、「打开」↗）。
            // 用户问「界面上乱七八糟按钮好多能不能简化」，那两颗是**每行都占位、但一天用不到一次**的
            // 典型，所以撤掉了 —— 功能挪到**右键菜单**里（见下面 `HandleFolderContextMenu`）。
            // 面板上少两颗常驻按钮，功能一个没少。
            Rect fieldRect = GUILayoutUtility.GetRect(120f, 10000f, ControlHeight, ControlHeight, GUILayout.ExpandWidth(true));
            string edited = EditorGUI.TextField(fieldRect, data.outputFolder ?? string.Empty);
            // 提示要画在输入框**之后**：`GUI.Label` 是后画的盖在上面，画在前面会被输入框盖掉。
            GUI.Label(fieldRect, new GUIContent(string.Empty, "输出目录。\n留空 = 工程根目录下的 "
                + HoQuickCaptureSettings.DefaultFolderName + "。\n相对路径按工程根目录解析。\n\n"
                + "**右键**：选目录 / 在文件管理器里打开。\n\n"
                + "解析后：" + HoQuickCaptureSettings.PreviewOutputFolder()));
            if (edited != data.outputFolder)
            {
                data.outputFolder = edited;
                HoQuickCaptureSettings.NotifyChanged();
            }

            HandleFolderContextMenu(fieldRect, data);
        }

        /// <summary>
        /// 输出目录那格的右键菜单。原来的两颗图标按钮（选目录、打开）搬到了这里。
        /// </summary>
        private static void HandleFolderContextMenu(Rect fieldRect, HoQuickCaptureSettingsData data)
        {
            Event current = Event.current;
            if (current == null
                || current.type != EventType.ContextClick
                || !fieldRect.Contains(current.mousePosition))
            {
                return;
            }

            current.Use();

            var menu = new GenericMenu();

            string folderError;
            string resolved = HoQuickCaptureSettings.ResolveOutputFolder(out folderError);

            menu.AddItem(new GUIContent("选目录…"), false, () =>
            {
                string picked = EditorUtility.OpenFolderPanel(
                    "选择快速渲染的输出目录",
                    HoQuickCaptureSettings.PreviewOutputFolder(),
                    string.Empty);
                if (!string.IsNullOrEmpty(picked))
                {
                    data.outputFolder = picked;
                    HoQuickCaptureSettings.NotifyChanged();
                }
            });

            if (resolved == null)
            {
                menu.AddDisabledItem(new GUIContent("在文件管理器里打开"));
            }
            else
            {
                menu.AddItem(new GUIContent("在文件管理器里打开"), false,
                    () => EditorUtility.RevealInFinder(resolved));
            }

            menu.AddSeparator(string.Empty);

            if (string.IsNullOrEmpty(data.outputFolder))
            {
                menu.AddDisabledItem(new GUIContent("清空（已经是默认目录）"));
            }
            else
            {
                menu.AddItem(new GUIContent("清空（回到默认目录）"), false, () =>
                {
                    data.outputFolder = string.Empty;
                    HoQuickCaptureSettings.NotifyChanged();
                });
            }

            menu.DropDown(fieldRect);
        }

        // ── 第二行：录制 · 时长（录制中换成 暂停/停止 + 进度）─────────

        private void DrawRecordRow(HoQuickCaptureSettingsData data, HoQuickCaptureStatus status)
        {
            // TAA 预热优先占这一行 —— 它发生在"截帧"按下之后、真正出图之前，
            // 一等等 16 帧。不给进度的话从面板上看和卡死没区别（用户报过）。
            if (status.WarmupActive)
            {
                DrawWarmupRow(status);
                return;
            }

            if (status.IsBusy)
            {
                DrawActiveSession(status);
                return;
            }

            // 录制比截帧多一条：**必须播放模式**（编辑模式里时间不前进，录不出会动的画面）。
            string blocked = HoQuickCaptureEngine.CheckCanRecord();
            GUIContent content = blocked == null ? RecordContent : new GUIContent(RecordContent.text, blocked);

            using (new EditorGUI.DisabledScope(blocked != null))
            {
                if (GUILayout.Button(content, EditorStyles.miniButton, GUILayout.Width(ActionButtonWidth), GUILayout.Height(ControlHeight)))
                {
                    HoQuickCaptureEngine.StartRecording();
                }
            }

            GUILayout.Space(Gap);

            // 格式（枚举）：mp4 / mov / 序列。默认 mp4。
            Rect formatRect = GUILayoutUtility.GetRect(
                HoConstraintEditorControls.SegmentedWidth(VideoFormatOptions),
                HoConstraintEditorControls.SegmentedWidth(VideoFormatOptions),
                ControlHeight,
                ControlHeight);
            int format = HoConstraintEditorControls.Segmented(
                formatRect,
                (int)data.videoFormat,
                VideoFormatOptions,
                VideoFormatTooltip());
            if (format != (int)data.videoFormat)
            {
                data.videoFormat = (HoQuickCaptureVideoFormat)format;
                HoQuickCaptureSettings.NotifyChanged();
            }

            GUILayout.Space(Gap);

            // 时长：数字格 + 单位。默认单位是秒，所以出来就是"录几秒"。
            int durationWidth = Mathf.RoundToInt(HoConstraintEditorControls.SegmentedWidth(UnitOptions));
            float fieldWidth = data.targetUnit == HoQuickCaptureTargetUnit.Seconds ? 46f : 56f;

            if (data.targetUnit == HoQuickCaptureTargetUnit.Frames)
            {
                int frames = EditorGUI.IntField(
                    GUILayoutUtility.GetRect(fieldWidth, fieldWidth, ControlHeight, ControlHeight),
                    data.targetFrames);
                frames = Mathf.Clamp(frames, 1, HoQuickCaptureSettings.MaxFrames);
                if (frames != data.targetFrames)
                {
                    data.targetFrames = frames;
                    HoQuickCaptureSettings.NotifyChanged();
                }
            }
            else
            {
                float seconds = EditorGUI.FloatField(
                    GUILayoutUtility.GetRect(fieldWidth, fieldWidth, ControlHeight, ControlHeight),
                    (float)Math.Round(data.targetSeconds, 2));
                seconds = Mathf.Clamp(seconds, 0.01f, HoQuickCaptureSettings.MaxSeconds);
                if (!Mathf.Approximately(seconds, data.targetSeconds))
                {
                    data.targetSeconds = seconds;
                    HoQuickCaptureSettings.NotifyChanged();
                }
            }

            GUILayout.Space(2f);

            Rect unitRect = GUILayoutUtility.GetRect(durationWidth, durationWidth, ControlHeight, ControlHeight);
            int unit = HoConstraintEditorControls.Segmented(
                unitRect,
                (int)data.targetUnit,
                UnitOptions,
                "录多久：秒数 / 帧数。\n秒数按帧率换算成帧 —— 换算结果一定是整数帧。");
            if (unit != (int)data.targetUnit)
            {
                data.targetUnit = (HoQuickCaptureTargetUnit)unit;
                HoQuickCaptureSettings.NotifyChanged();
            }

            GUILayout.FlexibleSpace();

            // 换算结果就写在行尾 —— 这是"我到底会得到多少帧"，属于必须看见的读数。
            GUILayout.Label(
                new GUIContent(
                    data.ResolveTargetFrames() + " 帧",
                    "按当前帧率与时长换算出来的实际帧数。\n当前帧率："
                    + FormatFrameRate(data.frameRate) + " fps（在「高级」里改）。"),
                EditorStyles.miniLabel);
        }

        /// <summary>
        /// TAA 预热：占第二行的位置，画一条进度。
        ///
        /// 为什么非要画：TAA 靠**连续多帧**累积抖动才收敛，所以要真的喂够 16 个引擎帧。
        /// 这段时间里面板之前是**完全静止**的 —— 用户描述得很准：「搞得我以为卡了」。
        /// 有进度条 + "N / 16 帧"这个读数，等待就变成可解释的了。
        /// </summary>
        private void DrawWarmupRow(HoQuickCaptureStatus status)
        {
            GUILayout.Label(
                new GUIContent("TAA 预热", "抗锯齿是 TAA 时必须先攒够若干连续帧才能出图。\n"
                    + "这段时间画面不会变，是正常的，不是在卡。\n"
                    + "想避免等待可以把相机的 Anti-aliasing 换成 SMAA（单帧就有）。"),
                EditorStyles.miniLabel,
                GUILayout.Width(56f));

            Rect bar = GUILayoutUtility.GetRect(40f, 10000f, 10f, 10f, GUILayout.ExpandWidth(true));
            int total = Mathf.Max(1, status.WarmupTotal);
            HoConstraintEditorControls.Meter(
                new Rect(bar.x, bar.y + 4f, bar.width, 10f),
                status.WarmupDone,
                0f,
                total,
                HoConstraintEditorTheme.AccentOutput,
                float.NaN,
                "TAA 预热进度");

            GUILayout.Space(Gap);

            int percent = Mathf.Clamp(Mathf.RoundToInt(status.WarmupDone * 100f / total), 0, 100);
            GUILayout.Label(
                new GUIContent(percent + "%", status.WarmupDone + " / " + total + " 帧"),
                EditorStyles.miniLabel,
                GUILayout.Width(PctWidth));
        }

        /// <summary>录制中：暂停/继续 + 停止 + 进度 + 已录时长。占第二行的位置。</summary>
        private void DrawActiveSession(HoQuickCaptureStatus status)
        {
            if (status.CanPause)
            {
                GUIContent pauseContent = status.State == HoQuickCaptureState.Paused ? ResumeContent : PauseContent;
                if (GUILayout.Button(pauseContent, EditorStyles.miniButton, GUILayout.Width(ActionButtonWidth), GUILayout.Height(ControlHeight)))
                {
                    HoQuickCaptureEngine.SetPaused(status.State != HoQuickCaptureState.Paused);
                }

                GUILayout.Space(2f);
            }

            if (GUILayout.Button(StopContent, EditorStyles.miniButton, GUILayout.Width(ActionButtonWidth), GUILayout.Height(ControlHeight)))
            {
                HoQuickCaptureEngine.Stop();
            }

            GUILayout.Space(Gap);

            Rect bar = GUILayoutUtility.GetRect(40f, 10000f, 10f, 10f, GUILayout.ExpandWidth(true));
            HoConstraintEditorControls.Meter(
                new Rect(bar.x, bar.y + 4f, bar.width, 10f),
                status.WrittenFrames,
                0f,
                Mathf.Max(1, status.TargetFrames),
                status.State == HoQuickCaptureState.Paused
                    ? HoConstraintEditorTheme.WarningColor
                    : HoConstraintEditorTheme.AccentOutput,
                float.NaN,
                "写盘进度");

            GUILayout.Space(Gap);
            GUILayout.Label(
                new GUIContent(
                    Mathf.RoundToInt(status.Progress * 100f) + "%",
                    status.WrittenFrames + " / " + status.TargetFrames + " 帧已写盘"),
                EditorStyles.miniLabel,
                GUILayout.Width(PctWidth));

            GUILayout.Label(
                new GUIContent(
                    FormatSeconds(status.GameTime),
                    "已录的**游戏时间**，不是墙上时间：\n离线步进下每帧前进 1/帧率 秒，跟这段实际录了多久无关。"),
                EditorStyles.miniLabel,
                GUILayout.Width(52f));
        }

        /// <summary>出错 / 提醒那一行。只在真有事的时候出现，且尽量短。</summary>
        private void DrawNotice(HoQuickCaptureStatus status)
        {
            string text = !string.IsNullOrEmpty(status.Error) ? status.Error : status.Warning;
            bool isError = !string.IsNullOrEmpty(status.Error);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(Pad);
                GUIStyle style = new GUIStyle(EditorStyles.miniLabel);
                style.normal.textColor = isError
                    ? HoConstraintEditorTheme.ErrorColor
                    : HoConstraintEditorTheme.WarningColor;
                style.wordWrap = true;

                GUILayout.Label(
                    new GUIContent((isError ? "✕ " : "· ") + text, text),
                    style,
                    GUILayout.ExpandWidth(true));

                if (GUILayout.Button(new GUIContent("✕", "清掉这条提示"), EditorStyles.miniLabel, GUILayout.Width(16f)))
                {
                    HoQuickCaptureEngine.ClearNotice();
                }

                GUILayout.Space(Pad);
            }
        }

        private void DrawAdvancedToggle()
        {
            bool expanded = EditorGUILayout.Foldout(advancedExpanded, AdvancedContent, true, EditorStyles.foldout);
            if (expanded != advancedExpanded)
            {
                advancedExpanded = expanded;
                EditorPrefs.SetBool(AdvancedPrefsKey, advancedExpanded);
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 高级（默认收起）
        // ══════════════════════════════════════════════════════════════

        /// <summary>画面来源：游戏视图 / 指定相机（可透明背景）。</summary>
        private void DrawSourceRow(HoQuickCaptureSettingsData data)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(
                    new GUIContent("来源", "画面从哪来。\n\n"
                        + "游戏视图 —— 抓游戏视图的最终合成结果：多相机、后处理、UI 都在里面，"
                        + "但它是画到屏幕上的，**拿不到透明背景**。\n\n"
                        + "指定相机 —— 把那台相机渲进一张带 alpha 的 RT。**要透明背景就用这个。**\n"
                        + "包含指定 Base 相机的 URP 相机栈，不包含独立相机和 Screen Space-Overlay UI。"),
                    EditorStyles.miniLabel,
                    GUILayout.Width(52f));

                Rect sourceRect = GUILayoutUtility.GetRect(
                    HoConstraintEditorControls.SegmentedWidth(SourceOptions),
                    HoConstraintEditorControls.SegmentedWidth(SourceOptions),
                    ControlHeight,
                    ControlHeight);
                int source = HoConstraintEditorControls.Segmented(
                    sourceRect,
                    (int)data.renderSource,
                    SourceOptions,
                    "游戏视图 = 合成结果（不透明）；指定相机 = 渲进 RT（可透明背景）");
                if (source != (int)data.renderSource)
                {
                    data.renderSource = (HoQuickCaptureRenderSource)source;
                    HoQuickCaptureSettings.NotifyChanged();
                }

                if (data.renderSource == HoQuickCaptureRenderSource.Camera)
                {
                    GUILayout.Space(Gap);

                    var camRect = (Rect)GUILayoutUtility.GetRect(140f, 260f, ControlHeight, ControlHeight);
                    var edited = (Camera)EditorGUI.ObjectField(
                        camRect,
                        data.sourceCamera,
                        typeof(Camera),
                        true);
                    if (edited != data.sourceCamera)
                    {
                        data.sourceCamera = edited;
                        // 要存：`NotifyChanged()` 会走到 `HoQuickCaptureSettings.SaveCameraRef()`，
                        // 那边把相机存成 `GlobalObjectId` 字符串，所以换个场景 / 域重载之后
                        // 这个槽能自己还原回来（用户报过「指定相机这个槽老是被清空」）。
                        HoQuickCaptureSettings.NotifyChanged();
                    }

                    if (data.sourceCamera == null)
                    {
                        GUILayout.Label(
                            new GUIContent("(空)", "留空 = 自动用 Camera.main，面板会在结果里说明。"),
                            EditorStyles.miniLabel,
                            GUILayout.Width(34f));
                    }

                    GUILayout.Space(Gap);
                    bool transparent = GUILayout.Toggle(
                        data.transparentBackground,
                        new GUIContent(
                            "透明背景",
                            "临时让相机以透明黑清屏并跳过天空盒绘制，保留场景环境光与反射；截帧后还原。\n\n"
                            + "URP 后处理的 alpha 输出会自动临时开启。请使用 PNG 或 EXR，JPG 不支持透明。\n"
                            + "TAA 会先积累 16 帧再保存；预热期间相机暂时用于截帧。\n"
                            + "场景中的背景物体仍会被渲染，可通过相机 Culling Mask 排除。"),
                        EditorStyles.miniButton,
                        GUILayout.Width(84f));
                    if (transparent != data.transparentBackground)
                    {
                        data.transparentBackground = transparent;
                        HoQuickCaptureSettings.NotifyChanged();
                    }
                }

                GUILayout.FlexibleSpace();
            }
        }

        /// <summary>MP4 参数：码率、档次、关键帧间隔。</summary>
        private void DrawVideoRow(HoQuickCaptureSettingsData data)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(
                    new GUIContent("码率", "H.264 的目标码率（Mbps）。\n填 0 = 按分辨率自动估一个（每像素每帧 0.1 bit，1080p30 约 6 Mbps）。\n\n"
                        + "Unity 的编码器没有 CRF —— 那是不属于它的概念。"),
                    EditorStyles.miniLabel,
                    GUILayout.Width(52f));

                float bitrate = EditorGUI.FloatField(
                    GUILayoutUtility.GetRect(FoldFieldWidth, FoldFieldWidth, ControlHeight, ControlHeight),
                    (float)Math.Round(data.videoBitrateMbps, 1));
                bitrate = Mathf.Clamp(bitrate, 0f, 400f);
                if (!Mathf.Approximately(bitrate, data.videoBitrateMbps))
                {
                    data.videoBitrateMbps = bitrate;
                    HoQuickCaptureSettings.NotifyChanged();
                }

                GUILayout.Label(new GUIContent("Mbps", "0 = 自动"), EditorStyles.miniLabel);
                GUILayout.Space(Gap);

                GUILayout.Label(
                    new GUIContent("档次", "H.264 profile。\nHigh（默认）压缩率最好；老设备不认就退 Main / Baseline。"),
                    EditorStyles.miniLabel);
                Rect profileRect = GUILayoutUtility.GetRect(
                    HoConstraintEditorControls.SegmentedWidth(ProfileOptions),
                    HoConstraintEditorControls.SegmentedWidth(ProfileOptions),
                    ControlHeight,
                    ControlHeight);
                int profile = HoConstraintEditorControls.Segmented(
                    profileRect,
                    Mathf.Clamp(data.h264Profile, 0, 2),
                    ProfileOptions,
                    "H.264 profile：Baseline / Main / High");
                if (profile != data.h264Profile)
                {
                    data.h264Profile = profile;
                    HoQuickCaptureSettings.NotifyChanged();
                }

                GUILayout.Space(Gap);

                GUILayout.Label(
                    new GUIContent("关键帧", "关键帧间隔（秒）。\n小 = 拖动定位灵、文件大；大 = 反过来。"),
                    EditorStyles.miniLabel);
                float keyframe = EditorGUI.FloatField(
                    GUILayoutUtility.GetRect(FoldFieldWidth, FoldFieldWidth, ControlHeight, ControlHeight),
                    (float)Math.Round(data.keyframeIntervalSeconds, 2));
                keyframe = Mathf.Clamp(keyframe, 0.25f, 30f);
                if (!Mathf.Approximately(keyframe, data.keyframeIntervalSeconds))
                {
                    data.keyframeIntervalSeconds = keyframe;
                    HoQuickCaptureSettings.NotifyChanged();
                }

                GUILayout.Label(new GUIContent("秒", "关键帧间隔（秒）"), EditorStyles.miniLabel);

                GUILayout.FlexibleSpace();
            }
        }

        private void DrawAdvanced(HoQuickCaptureSettingsData data, HoQuickCaptureStatus status, Texture2D preview)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawVideoRow(data);
                DrawSourceRow(data);

                // 分辨率
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(new GUIContent("分辨率", "跟随视图 = 和游戏视图当前的渲染分辨率一致；自定义 = 自己填宽高。"), EditorStyles.miniLabel, GUILayout.Width(52f));

                    Rect modeRect = GUILayoutUtility.GetRect(
                        HoConstraintEditorControls.SegmentedWidth(ResolutionOptions),
                        HoConstraintEditorControls.SegmentedWidth(ResolutionOptions),
                        ControlHeight,
                        ControlHeight);
                    int mode = HoConstraintEditorControls.Segmented(
                        modeRect,
                        (int)data.resolutionMode,
                        ResolutionOptions,
                        "跟随视图 = 和游戏视图当前的渲染分辨率一致；自定义 = 自己填宽高。");
                    if (mode != (int)data.resolutionMode)
                    {
                        data.resolutionMode = (HoQuickCaptureResolutionMode)mode;
                        HoQuickCaptureSettings.NotifyChanged();
                    }

                    GUILayout.Space(Gap);

                    if (data.resolutionMode == HoQuickCaptureResolutionMode.Custom)
                    {
                        int width = EditorGUI.IntField(GUILayoutUtility.GetRect(FoldFieldWidth, FoldFieldWidth, ControlHeight, ControlHeight), data.customWidth);
                        GUILayout.Label("x", EditorStyles.miniLabel);
                        int height = EditorGUI.IntField(GUILayoutUtility.GetRect(FoldFieldWidth, FoldFieldWidth, ControlHeight, ControlHeight), data.customHeight);
                        width = Mathf.Clamp(width, 1, 16384);
                        height = Mathf.Clamp(height, 1, 16384);
                        if (width != data.customWidth || height != data.customHeight)
                        {
                            data.customWidth = width;
                            data.customHeight = height;
                            HoQuickCaptureSettings.NotifyChanged();
                        }
                    }
                    else
                    {
                        HoQuickCaptureGameView.GetRenderSize(out int liveWidth, out int liveHeight);
                        GUILayout.Label(
                            new GUIContent(liveWidth + " x " + liveHeight, "游戏视图现在这么大。\n想改就在游戏视图左上角那个分辨率下拉里改。"),
                            EditorStyles.miniLabel);
                    }

                    GUILayout.FlexibleSpace();
                }

                // 帧率 + 每次录制建子目录
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(new GUIContent("帧率", "出图序列的帧率。\n同时决定游戏时间每帧前进多少（Time.captureDeltaTime = 1/帧率）。"), EditorStyles.miniLabel, GUILayout.Width(52f));

                    float rate = EditorGUI.FloatField(
                        GUILayoutUtility.GetRect(FoldFieldWidth, FoldFieldWidth, ControlHeight, ControlHeight),
                        (float)Math.Round(data.frameRate, 3));
                    rate = Mathf.Clamp(rate, HoQuickCaptureSettings.MinFrameRate, HoQuickCaptureSettings.MaxFrameRate);
                    if (!Mathf.Approximately(rate, data.frameRate))
                    {
                        data.frameRate = rate;
                        HoQuickCaptureSettings.NotifyChanged();
                    }

                    if (GUILayout.Button(new GUIContent("24", "24 fps"), EditorStyles.miniButton, GUILayout.Width(24f)))
                    {
                        SetFrameRate(data, 24f);
                    }

                    if (GUILayout.Button(new GUIContent("30", "30 fps"), EditorStyles.miniButton, GUILayout.Width(24f)))
                    {
                        SetFrameRate(data, 30f);
                    }

                    if (GUILayout.Button(new GUIContent("60", "60 fps"), EditorStyles.miniButton, GUILayout.Width(24f)))
                    {
                        SetFrameRate(data, 60f);
                    }

                    GUILayout.Space(Gap);
                    GUILayout.FlexibleSpace();
                }

                // 目录结构 + 完成后打开 + 自定义分辨率时是否连带改游戏视图
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(new GUIContent("目录", "每次录制建子目录 = 每段录到 take_日期_时间 子目录里，上一段不会被覆盖。"), EditorStyles.miniLabel, GUILayout.Width(52f));
                    bool perTake = GUILayout.Toggle(
                        data.perTakeSubfolder,
                        new GUIContent("每段一个子目录", "每段录到独立的 take_日期_时间 子目录里，上一段不会被覆盖。"),
                        EditorStyles.miniButton);
                    if (perTake != data.perTakeSubfolder)
                    {
                        data.perTakeSubfolder = perTake;
                        HoQuickCaptureSettings.NotifyChanged();
                    }

                    GUILayout.Space(Gap);
                    bool reveal = GUILayout.Toggle(
                        data.revealWhenDone,
                        new GUIContent("完成后打开", "录完 / 拍完在文件管理器里定位到产物。"),
                        EditorStyles.miniButton);
                    if (reveal != data.revealWhenDone)
                    {
                        data.revealWhenDone = reveal;
                        HoQuickCaptureSettings.NotifyChanged();
                    }

                    GUILayout.Space(Gap);
                    using (new EditorGUI.DisabledScope(data.resolutionMode != HoQuickCaptureResolutionMode.Custom))
                    {
                        bool lockSize = GUILayout.Toggle(
                            data.lockGameViewResolution,
                            new GUIContent("顺带改视图", "自定义分辨率时，录制前临时把游戏视图也切成那个尺寸（录完还原）。\n不开的话出图会被拉伸。"),
                            EditorStyles.miniButton);
                        if (lockSize != data.lockGameViewResolution)
                        {
                            data.lockGameViewResolution = lockSize;
                            HoQuickCaptureSettings.NotifyChanged();
                        }
                    }

                    GUILayout.FlexibleSpace();
                }

                // 文件名前缀 + PNG 质量
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(new GUIContent("文件名", "文件名前缀。\n序列帧：前缀_000000.png\n单张截图：前缀_日期_时间.png"), EditorStyles.miniLabel, GUILayout.Width(52f));

                    Rect prefixRect = GUILayoutUtility.GetRect(64f, 200f, ControlHeight, ControlHeight);
                    string prefix = EditorGUI.TextField(prefixRect, data.filePrefix ?? string.Empty);
                    if (prefix != data.filePrefix)
                    {
                        data.filePrefix = prefix;
                        HoQuickCaptureSettings.NotifyChanged();
                    }

                    GUILayout.Label(
                        new GUIContent(FileNamePreview(data), "产物长这样。\n序列帧的帧号补零到 6 位。"),
                        EditorStyles.miniLabel);

                    GUILayout.Space(Gap);
                    GUILayout.Label(new GUIContent("质量", "PNG 压缩力度：0 = 编得最快、文件最大；100 = 编得最慢、文件最小。\n图本身始终无损，这里只换压缩力度。\n（Unity 2022.1 起可调；2021.3 上固定用默认压缩。）"), EditorStyles.miniLabel);
                    float quality = GUILayout.HorizontalSlider(data.pngQuality, 0f, 100f, GUILayout.Width(70f));
                    int rounded = Mathf.Clamp(Mathf.RoundToInt(quality), 0, 100);
                    if (rounded != data.pngQuality)
                    {
                        data.pngQuality = rounded;
                        HoQuickCaptureSettings.NotifyChanged();
                    }

                    GUILayout.Label(data.pngQuality.ToString(CultureInfo.InvariantCulture), EditorStyles.miniLabel, GUILayout.Width(22f));

                    GUILayout.FlexibleSpace();
                }

                if (preview != null)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(new GUIContent("最近", "最近一次拍到的画面（缩略图）。落盘的是原始分辨率。"), EditorStyles.miniLabel, GUILayout.Width(52f));
                        float aspect = preview.height <= 0 ? 16f / 9f : (float)preview.width / preview.height;
                        float height = Mathf.Min(90f, preview.height);
                        Rect rect = GUILayoutUtility.GetRect(height * aspect, height * aspect, height, height);
                        if (Event.current.type == EventType.Repaint)
                        {
                            EditorGUI.DrawRect(rect, HoConstraintEditorTheme.WellColor);
                            GUI.DrawTexture(rect, preview, ScaleMode.ScaleToFit, false);
                        }

                        GUILayout.FlexibleSpace();
                    }
                }

                // 最近产物的路径以前是"定位最近产物"「复制路径」两颗按钮。
                // 现在只留一行文字（路径本身有信息量，但不需要两个常驻按钮），
                // 想打开就在「输出目录」那格上右键 →「在文件管理器里打开」。
                if (!string.IsNullOrEmpty(status.LastFile))
                {
                    GUILayout.Label(
                        new GUIContent("最近产物：" + status.LastFile, status.LastFile),
                        EditorStyles.miniLabel);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(new GUIContent("恢复默认", "截图/录制的全部设置回到出厂值（会先问你一次）。"), EditorStyles.miniButton))
                    {
                        if (EditorUtility.DisplayDialog(
                                "恢复快速渲染的默认设置",
                                "分辨率模式、帧率、时长、文件名、目录结构、PNG 质量都会回到出厂值。确定？",
                                "恢复",
                                "算了"))
                        {
                            HoQuickCaptureSettings.ResetToDefault();
                        }
                    }
                }
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 小块
        // ══════════════════════════════════════════════════════════════

        private static void SetFrameRate(HoQuickCaptureSettingsData data, float rate)
        {
            if (Mathf.Approximately(data.frameRate, rate))
            {
                return;
            }

            data.frameRate = rate;
            HoQuickCaptureSettings.NotifyChanged();
        }

        /// <summary>「高级」里那个"产物长这样"的预览：跟着当前格式走。</summary>
        private static string FileNamePreview(HoQuickCaptureSettingsData data)
        {
            if (data.videoFormat != HoQuickCaptureVideoFormat.PngSequence)
            {
                return HoQuickCaptureNaming.BuildVideoFileName(
                    data.filePrefix,
                    DateTime.Now,
                    HoQuickCaptureNaming.ExtensionFor(data.videoFormat));
            }

            return HoQuickCaptureNaming.BuildFileName(
                data.filePrefix,
                0,
                HoQuickCaptureNaming.ExtensionFor(data.imageFormat));
        }

        /// <summary>第一行那个分辨率读数该显示什么。</summary>
        private static string CurrentResolutionText(HoQuickCaptureSettingsData data)
        {
            if (data.resolutionMode == HoQuickCaptureResolutionMode.Custom)
            {
                return data.customWidth + "x" + data.customHeight;
            }

            if (HoQuickCaptureGameView.GetRenderSize(out int width, out int height))
            {
                return width + "x" + height;
            }

            return "—";
        }

        private static string ResolutionTooltip(HoQuickCaptureSettingsData data)
        {
            if (data.resolutionMode == HoQuickCaptureResolutionMode.Custom)
            {
                return "出图分辨率：自定义 " + data.customWidth + "x" + data.customHeight + "。\n在「高级」里改。";
            }

            return "出图分辨率 = 游戏视图当前的渲染分辨率。\n"
                + "拿不到游戏视图尺寸时不能开拍（会显示 —）。\n"
                + "要固定尺寸可以在「高级」里切「自定义」。";
        }

        /// <summary>视频格式那个枚举的提示。</summary>
        private static string VideoFormatTooltip()
        {
            return "录成什么：\n"
                + "mp4 —— H.264，通用（默认）。用 Unity 自带的编码器，零依赖。\n"
                + "PNG 序列 —— 每帧一张图，不需要任何编码器。\n\n"
                + "mp4 要求宽高都是偶数（H.264 的限制），面板会在开录前挡住奇数分辨率。";
        }

        /// <summary>分辨率读数要留多宽（免得 1920x1080 被截成 1920x1…）。</summary>
        private static float ResolveLabelWidth(string text)
        {
            return Mathf.Max(48f, EditorStyles.miniLabel.CalcSize(new GUIContent(text)).x + 4f);
        }

        private static string FormatSeconds(float seconds)
        {
            if (seconds >= 60f)
            {
                int minutes = Mathf.FloorToInt(seconds / 60f);
                return minutes + "m" + (seconds - minutes * 60f).ToString("00.0", CultureInfo.InvariantCulture) + "s";
            }

            return seconds.ToString("0.0", CultureInfo.InvariantCulture) + "s";
        }

        private static string FormatFrameRate(float rate)
        {
            return Mathf.Approximately(rate, Mathf.Round(rate))
                ? Mathf.RoundToInt(rate).ToString(CultureInfo.InvariantCulture)
                : rate.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
