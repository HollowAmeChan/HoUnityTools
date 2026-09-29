// HoPlaySpeedWindow.cs -- 播放速度面板（速度条）
//
// 干什么：播放模式下直接改 Time.timeScale —— 慢放看清某一帧、快进跳过没意思的一段，不用改代码重进 Play。
// 形态与图标都仿 dotsquid 的 ChronoHelper（一排速度点 + 非线性滑杆 + 数字格 + 复位 / 自动复位 / 锁定 / 警告闪烁；
// 图标字节直接从它那儿搬 —— 那份是 **CC0 1.0**，见 HoPlaySpeedIcons.cs 的说明），
// 按本仓的规矩重写：中文界面、配置存 EditorPrefs、**只有一行**、菜单里只留一个入口。
//
// 四条硬规矩（别改）：
//   ① **只在播放模式写 Time.timeScale**。编辑模式写它没有意义，还会把"进播放时那个值"这个复位基准弄脏。
//   ② **进播放先记下 Time.timeScale 与期望速度，退出播放（或关窗口）时还原**。面板是"借"时间的：
//      不还就会把工程留在 0.25× 上，下一次 Play 莫名其妙地慢，而这种问题很难联想到这里。
//   ③ **面板永远跟随外部改动，不跟外部抢时间**（2026-09-30 用户定：不要"锁定"那档）。
//      外部改了 Time.timeScale，面板就把值收下来显示出去，并按警告设置闪一下 —— 也就是说
//      **脚本每帧自己设 timeScale 时，面板设的速度会被顶掉**；要压住脚本得用组件，不是编辑器窗口。
//   ④ 速度值一律夹在 [0, 100]。0 是暂停，负数是没定义的，100 以上固定步长追不上。
//
// ⚠️ 已知边界（都不是 bug，是"面板只在自己画的时候才管事"）：
//   · 窗口被折叠 / 切到别的页签时 OnGUI 不跑 ⇒ 锁定在这段时间里没有强制力（外部改了就改成了）。
//     顺带：警告也只有下次重画时才会发现。要 100% 兜住外部改动得用组件而不是编辑器窗口。
//   · 播放中重编译脚本会走一次域重载：窗口重建时把"当前速度"当成原速记下来（拿不到更早的值了）。
//   · 相对时间被改的东西一律跟着变：`Time.time` / 物理 / 动画 / `WaitForSeconds` / 粒子。
//     **不受影响**：`Time.unscaledTime`、`Time.realtimeSinceStartup`、`WaitForSecondsRealtime`、编辑器自己的刷新。
//     慢放到 0× 时画面还在画，只是逻辑停住。
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.PlaySpeed
{
    /// <summary>播放速度条。菜单：<c>HoUnityTools/播放速度</c>（唯一的入口；设置从条上的齿轮按钮进）。</summary>
    internal sealed class HoPlaySpeedWindow : EditorWindow, IHasCustomMenu
    {
        // ── 排版常量（**只有一行**：横着排不下就撑宽，不换行、不叠第二排）──
        private const float BarHeight = 24f;
        private const float MinWidth = 320f;
        private const float MaxWidth = 8192f;
        private const float EdgePadding = 4f;
        private const float ButtonHeight = 20f;
        private const float ButtonMinWidth = 34f;
        private const float FieldWidth = 46f;
        private const float ToggleWidth = 24f;
        private const float ToggleHeight = 16f;
        private const float ResetButtonMinWidth = 28f;
        /// <summary>滑杆那一块要占的宽度（滑杆 60 + 数字格 46 + 两个图标开关 48 + 空隙）。</summary>
        private const float SliderBlockWidth = 176f;

        private const float LinearMax = 2f;
        private const float SpeedEpsilon = 0.0005f;
        private const float RepaintInterval = 0.05f;
        private const double Tau = Math.PI * 2.0;

        /// <summary>背景底色（照抄参考实现：白色 10%，斜纹贴图自己带 alpha）。</summary>
        private static readonly Color NormalBackColor = new Color(1f, 1f, 1f, 0.1f);

        private static readonly GUIContent ResetContent =
            new GUIContent(string.Empty, "复位：回到本次播放开始时的速度（当前速度与期望值一起回去）。不在播放模式时按不动。");

        private static readonly GUIContent AutoResetContent =
            new GUIContent(string.Empty, "播放结束自动复位：退出播放时把期望速度还原成进播放前那个值。");

        private static readonly GUIContent SettingsContent =
            new GUIContent(string.Empty, "设置：速度点、按钮、警告闪烁。");

        private static readonly GUIContent PauseContent =
            new GUIContent(string.Empty, "暂停：Time.timeScale = 0");

        private static readonly GUIContent SpeedFieldTooltip =
            new GUIContent(string.Empty, "期望速度（Time.timeScale 的倍数）。可以直接填数，超过最大速度点也能填。");

        private static readonly GUIContent SliderTooltip =
            new GUIContent(string.Empty, "拖速度。1× 以上是压缩过的（后面越来越快），<1× 是线性的。");

        private static readonly GUIContent MenuSettings = new GUIContent("设置…");
        private static readonly GUIContent MenuReset = new GUIContent("复位到进播放时的速度");
        private static readonly GUIContent MenuAutoReset = new GUIContent("播放结束自动复位");
        private static readonly GUIContent MenuResetAll = new GUIContent("恢复全部默认设置");

        private static readonly GUILayoutOption ButtonHeightOption = GUILayout.Height(ButtonHeight);
        private static readonly GUILayoutOption[] ToggleOptions =
        {
            GUILayout.Width(ToggleWidth),
            GUILayout.Height(ToggleHeight),
            GUILayout.ExpandWidth(false),
        };

        /// <summary>一颗速度点按钮（缓存：文字、量出来的宽度、值）。</summary>
        private sealed class PresetButton
        {
            public HoPlaySpeedPreset Preset;
            public GUIContent Content;
            public float Width;

            public float Value => Preset.value;
        }

        private PresetButton[] buttons = new PresetButton[0];
        private int buttonsRevision = -1;
        private float buttonsWidth;
        private float equalButtonWidth = ButtonMinWidth;
        private float resetButtonWidth = ResetButtonMinWidth;

        /// <summary>面板现在管着 Time.timeScale（进了播放并且已经记下原值）。</summary>
        private bool armed;
        private float originalTimeScale = 1f;
        private float originalSpeed = 1f;

        private double warningStartTime;
        private double lastRepaintTime;
        private readonly HoPlaySpeedIcons icons = new HoPlaySpeedIcons();

        [MenuItem("HoUnityTools/播放速度", false, 8)]
        private static void Open()
        {
            HoPlaySpeedWindow window = GetWindow<HoPlaySpeedWindow>(false, "播放速度", true);
            window.Show();
        }

        /// <summary>让速度条闪一次警告（设置窗口的「测试闪烁」用）。速度条没开着就顺手开出来。</summary>
        internal static void FlashWarning()
        {
            HoPlaySpeedWindow window = GetWindow<HoPlaySpeedWindow>(false, "播放速度", true);
            window.warningStartTime = EditorApplication.timeSinceStartup;
            window.Show();
            window.Repaint();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("播放速度", "播放速度面板：播放模式下直接改 Time.timeScale。");
            icons.Load();
            UpdateContentImages();
            HoPlaySpeedSettings.Changed += OnSettingsChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += OnEditorUpdate;

            // 播放中才打开窗口：拿现在的速度当"原速"（早先那个值已经拿不到了）
            if (EditorApplication.isPlaying)
            {
                EnterPlay();
            }
        }

        private void OnDisable()
        {
            HoPlaySpeedSettings.Changed -= OnSettingsChanged;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.update -= OnEditorUpdate;
            HoPlaySpeedSettings.Save();
        }

        private void OnDestroy()
        {
            ExitPlay();
            icons.Clear();
        }

        /// <summary>
        /// 把图标挂到那几个 GUIContent 上（照参考实现：**深色皮肤用白线图标、浅色皮肤用黑线图标**）。
        /// 图标在 <see cref="HoPlaySpeedIcons.Load"/> 里按当前皮肤挑好，这里只负责贴上去 ——
        /// 于是切主题后只要窗口重开（或下一次 OnEnable）就会跟着换。
        /// </summary>
        private void UpdateContentImages()
        {
            PauseContent.image = icons.pause;
            ResetContent.image = icons.reset;
            SettingsContent.image = icons.settings;
            AutoResetContent.image = HoPlaySpeedSettings.Data.resetOnPlayEnd ? icons.autoResetOn : icons.autoResetOff;
            buttonsRevision = -1;
        }

        private void OnGUI()
        {
            HoPlaySpeedSettingsData data = HoPlaySpeedSettings.Data;

            SyncArmedState();
            RebuildButtonsIfNeeded(data);
            SyncWithExternal(data);

            DrawStrip(data);
            HandleContextClick();
            DrawBar(data);

            ApplySpeed(data);
        }

        // ══════════════════════════════════════════════════════════════
        // 状态：进 / 出播放
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 兜底同步：正常路径是 <see cref="OnPlayModeChanged"/>，这里只处理"事件没收到"的漏网情况。
        /// 判据用 <c>isPlayingOrWillChangePlaymode</c>：切换途中（ExitingEditMode 之后、真正开始播放之前）
        /// 它是 true，所以不会在这一瞬间被误判成"退出播放"而把刚接管的速度还回去。
        /// </summary>
        private void SyncArmedState()
        {
            if (!armed && EditorApplication.isPlaying)
            {
                EnterPlay();
            }
            else if (armed && !EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                ExitPlay();
            }
        }

        private void OnPlayModeChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.ExitingEditMode:
                    // 提前接管：这时还拿得到"编辑模式下的 Time.timeScale"，它就是要还原回去的那个基准
                    EnterPlay();
                    break;

                case PlayModeStateChange.EnteredPlayMode:
                    // 播放真的开始了，再写一次：进播放有可能把 Time.timeScale 冲掉
                    //（各版本行为不一致，不赌它）。少了这一刀，症状是"设好 0.25× 按 Play 却是 1×"，
                    //  而且在「跟随」模式下会被当成外部改动、把用户设的值悄悄改成 1。
                    EnterPlay();
                    ApplySpeed(HoPlaySpeedSettings.Data);
                    break;

                case PlayModeStateChange.ExitingPlayMode:
                case PlayModeStateChange.EnteredEditMode:
                    ExitPlay();
                    break;
            }
        }

        private void EnterPlay()
        {
            if (armed)
            {
                return;
            }

            armed = true;
            HoPlaySpeedSettingsData data = HoPlaySpeedSettings.Data;
            originalTimeScale = Time.timeScale;
            originalSpeed = data.speed;
            ApplySpeed(data);
        }

        private void ExitPlay()
        {
            if (!armed)
            {
                return;
            }

            armed = false;
            HoPlaySpeedSettingsData data = HoPlaySpeedSettings.Data;
            RestoreOriginal(data, data.resetOnPlayEnd);
        }

        /// <summary>
        /// 把时间还回去：<c>Time.timeScale</c> **一定**还原成进播放时那个值；
        /// 期望速度要不要一起还原由调用方定（「复位」按钮 = 要；退出播放 = 看「自动复位」开关）。
        ///
        /// 复位按钮为什么无条件还原期望值：锁定时面板是权威，期望值不跟着回去的话，
        /// 按了复位下一帧又被写回慢速 —— 按钮看起来"坏了"。
        /// </summary>
        private void RestoreOriginal(HoPlaySpeedSettingsData data, bool resetDesiredSpeed)
        {
            if (Math.Abs(Time.timeScale - originalTimeScale) > SpeedEpsilon)
            {
                Time.timeScale = originalTimeScale;
            }

            if (resetDesiredSpeed)
            {
                data.speed = originalSpeed;
            }
        }

        private void ApplySpeed(HoPlaySpeedSettingsData data)
        {
            if (!armed)
            {
                return;
            }

            data.speed = Mathf.Clamp(data.speed, 0f, HoPlaySpeedSettings.MaxSpeed);
            if (Math.Abs(Time.timeScale - data.speed) > SpeedEpsilon)
            {
                Time.timeScale = data.speed;
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 状态：外部改动（跟随 / 警告）
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 谁改的 Time.timeScale？面板自己写完是立刻一致的，所以"不一致"只可能来自外部（脚本、别的工具）。
        /// **面板永远跟随**：把外部的值收下来当期望值（于是滑杆与数字格跟着动），并按警告设置闪一下。
        /// 不跟外部抢时间是有意的 —— 每帧对着写会打架（物理步长与动画都会抖），而且"到底谁说了算"就没法回答了。
        /// </summary>
        private void SyncWithExternal(HoPlaySpeedSettingsData data)
        {
            if (!armed)
            {
                return;
            }

            float actual = Time.timeScale;
            if (Math.Abs(actual - data.speed) <= SpeedEpsilon)
            {
                return;
            }

            data.speed = Mathf.Clamp(actual, 0f, HoPlaySpeedSettings.MaxSpeed);
            RaiseWarning(data);
        }

        private void RaiseWarning(HoPlaySpeedSettingsData data)
        {
            if (data.warning == HoPlaySpeedWarning.Never)
            {
                return;
            }

            warningStartTime = EditorApplication.timeSinceStartup;
        }

        /// <summary>警告背景的亮度（0 = 不闪）。正弦来回，闪够 <c>warningBlinkDuration</c> 秒自己停。</summary>
        private float WarningPhase(HoPlaySpeedSettingsData data)
        {
            if (warningStartTime <= 0.0)
            {
                return 0f;
            }

            double now = EditorApplication.timeSinceStartup;
            double end = warningStartTime + data.warningBlinkDuration;
            if (now >= end)
            {
                warningStartTime = 0.0;
                return 0f;
            }

            if (data.warningBlinkPeriod <= 0.01f)
            {
                return 1f;
            }

            double phase = (end - now) / data.warningBlinkPeriod;
            return 0.5f - (0.5f * (float)Math.Sin(phase * Tau));
        }

        /// <summary>
        /// 播放中 20Hz 重画：既是为了让警告闪起来，也是为了定期看一眼外部有没有改速度
        /// （不开锁定时把它显示出来、开锁定时顶回去）。编辑模式没有任何东西会自己变，就不重画。
        /// </summary>
        private void OnEditorUpdate()
        {
            if (warningStartTime > 0.0 &&
                EditorApplication.timeSinceStartup >= warningStartTime + HoPlaySpeedSettings.Data.warningBlinkDuration)
            {
                warningStartTime = 0.0;
            }

            if (!armed && warningStartTime <= 0.0)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            if (now - lastRepaintTime < RepaintInterval)
            {
                return;
            }

            lastRepaintTime = now;
            Repaint();
        }

        private void OnSettingsChanged()
        {
            buttonsRevision = -1;
            Repaint();
        }

        // ══════════════════════════════════════════════════════════════
        // 画：背景 + 速度条
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 窗口底色：参考实现那张 8×8 斜纹贴图平铺（向内收 1px，留出一道边），警告时整片渐变到警告色。
        /// 深色皮肤用白色 10%（浅色皮肤下几乎看不见 —— 参考实现就是这样，照抄）。
        /// </summary>
        private void DrawStrip(HoPlaySpeedSettingsData data)
        {
            float phase = WarningPhase(data);
            Color tint = phase > 0f ? Color.Lerp(NormalBackColor, data.warningColor, phase) : NormalBackColor;

            Rect rect = new Rect(1f, 1f, Mathf.Max(1f, position.width - 2f), Mathf.Max(1f, position.height - 2f));
            Rect uv = new Rect(0f, 0f, rect.width / HoPlaySpeedIcons.BackSize, rect.height / HoPlaySpeedIcons.BackSize);

            Color previous = GUI.color;
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(rect, icons.back, uv);
            GUI.color = previous;
        }

        /// <summary>一条横排：速度点那排 + 滑杆那块（谁在左看「块顺序」）。**不换行**。</summary>
        private void DrawBar(HoPlaySpeedSettingsData data)
        {
            ApplyWindowSize(Mathf.Max(MinWidth, buttonsWidth + SliderBlockWidth), BarHeight);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (data.blockOrder == HoPlaySpeedBlockOrder.Reversed)
                {
                    DrawSliderBlock(data);
                    DrawButtons(data);
                }
                else
                {
                    DrawButtons(data);
                    DrawSliderBlock(data);
                }
            }
        }

        /// <summary>横排里滑杆那一块要往下挪一点：它比按钮矮，不挪就贴不到一条线上。</summary>
        private void DrawSliderBlock(HoPlaySpeedSettingsData data)
        {
            using (new EditorGUILayout.VerticalScope())
            {
                GUILayout.Space(2f);
                DrawSliderRow(data);
            }
        }

        private void DrawSliderRow(HoPlaySpeedSettingsData data)
        {
            float max = data.MaxPresetValue();

            using (new EditorGUILayout.HorizontalScope())
            {
                // 滑杆：1× 以下线性、1× 以上压到 [1, 最大速度点]。只认"用户真的拖了"这个事实，
                // 否则手填的 4× 会在下一帧被"滑杆夹到上限再换算回来"悄悄改成 2×。
                float linear = SpeedToLinear(data.speed, max);
                float moved = GUILayout.HorizontalSlider(linear, 0f, LinearMax, GUILayout.ExpandWidth(true), GUILayout.MaxWidth(MaxWidth));
                if (!Mathf.Approximately(moved, linear))
                {
                    data.speed = LinearToSpeed(moved, max);
                }

                DrawTooltipOverLastRect(SliderTooltip);

                Rect fieldRect = GUILayoutUtility.GetRect(FieldWidth, FieldWidth, 18f, 18f);
                float rounded = (float)Math.Round(data.speed, 3);
                float typed = EditorGUI.FloatField(fieldRect, rounded);
                if (!Mathf.Approximately(typed, data.speed))
                {
                    data.speed = Mathf.Clamp(typed, 0f, HoPlaySpeedSettings.MaxSpeed);
                }

                DrawTooltipOverRect(fieldRect, SpeedFieldTooltip);
                GUILayout.Space(EdgePadding);

                // 两个开关：自复 / 设置（图标 + tooltip，跟参考实现一样）
                bool autoReset = GUILayout.Toggle(data.resetOnPlayEnd, AutoResetContent, EditorStyles.miniButtonLeft, ToggleOptions);
                if (autoReset != data.resetOnPlayEnd)
                {
                    data.resetOnPlayEnd = autoReset;
                    AutoResetContent.image = autoReset ? icons.autoResetOn : icons.autoResetOff;
                    HoPlaySpeedSettings.NotifyChanged();
                }

                if (GUILayout.Button(SettingsContent, EditorStyles.miniButtonRight, ToggleOptions))
                {
                    HoPlaySpeedSettingsWindow.Open();
                }

                GUILayout.Space(EdgePadding);
            }
        }

        private void DrawButtons(HoPlaySpeedSettingsData data)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EdgePadding);

                if (data.showResetButton)
                {
                    using (new EditorGUI.DisabledScope(!armed))
                    {
                        float width = data.buttonWidth == HoPlaySpeedButtonWidth.AsIs
                            ? resetButtonWidth
                            : Mathf.Max(resetButtonWidth, equalButtonWidth);
                        if (GUILayout.Button(ResetContent, EditorStyles.miniButtonLeft, GUILayout.Width(width), ButtonHeightOption))
                        {
                            RestoreOriginal(data, true);
                        }
                    }
                }

                for (int i = 0; i < buttons.Length; i++)
                {
                    PresetButton button = buttons[i];
                    GUIStyle style;
                    if (i > 0)
                    {
                        style = i == buttons.Length - 1 ? EditorStyles.miniButtonRight : EditorStyles.miniButtonMid;
                    }
                    else
                    {
                        style = data.showResetButton ? EditorStyles.miniButtonMid : EditorStyles.miniButtonLeft;
                    }

                    // 一次性按钮：点一下就是把值设过去，**不留按下状态**
                    //（面板不记"当前选的是哪颗"；现在速度是多少看滑杆与数字格）
                    if (GUILayout.Button(button.Content, style, ButtonWidthOption(data, button), ButtonHeightOption))
                    {
                        data.speed = button.Value;
                    }
                }
            }
        }

        private GUILayoutOption ButtonWidthOption(HoPlaySpeedSettingsData data, PresetButton button)
        {
            float width = data.buttonWidth == HoPlaySpeedButtonWidth.AsIs ? button.Width : equalButtonWidth;
            return GUILayout.Width(width);
        }

        private static float SpeedToLinear(float speed, float max)
        {
            return speed <= 1f ? speed : Mathf.InverseLerp(1f, max, speed) + 1f;
        }

        private static float LinearToSpeed(float linear, float max)
        {
            return linear <= 1f ? linear : Mathf.Lerp(1f, max, linear - 1f);
        }

        private void ApplyWindowSize(float minWidth, float height)
        {
            Vector2 min = new Vector2(minWidth, height);
            Vector2 max = new Vector2(MaxWidth, height);
            if (minSize != min)
            {
                minSize = min;
            }

            if (maxSize != max)
            {
                maxSize = max;
            }
        }

        private static void DrawTooltipOverLastRect(GUIContent content)
        {
            DrawTooltipOverRect(GUILayoutUtility.GetLastRect(), content);
        }

        private static void DrawTooltipOverRect(Rect rect, GUIContent content)
        {
            GUI.Label(rect, content);
        }

        // ══════════════════════════════════════════════════════════════
        // 速度点按钮的缓存
        // ══════════════════════════════════════════════════════════════

        private void RebuildButtonsIfNeeded(HoPlaySpeedSettingsData data)
        {
            if (buttonsRevision == HoPlaySpeedSettings.Revision)
            {
                return;
            }

            buttonsRevision = HoPlaySpeedSettings.Revision;
            List<HoPlaySpeedPreset> presets = data.presets;
            List<PresetButton> rebuilt = new List<PresetButton>(presets.Count);
            equalButtonWidth = ButtonMinWidth;

            for (int i = 0; i < presets.Count; i++)
            {
                HoPlaySpeedPreset preset = presets[i];
                if (preset == null)
                {
                    continue;
                }

                // 0× 那颗画暂停图标（参考实现也是这么干的）；其余画速度值文字
                GUIContent content;
                if (preset.value <= 0f && string.IsNullOrEmpty(preset.label))
                {
                    content = new GUIContent(PauseContent);
                }
                else
                {
                    string display = preset.Display(data.format);
                    string tooltip = "把 Time.timeScale 设成 " + preset.value.ToString("0.###", CultureInfo.InvariantCulture);
                    content = new GUIContent(display, tooltip);
                }

                float width = ButtonMinWidth;
                width = Mathf.Max(width, EditorStyles.miniButtonLeft.CalcSize(content).x);
                width = Mathf.Max(width, EditorStyles.miniButtonRight.CalcSize(content).x);

                rebuilt.Add(new PresetButton
                {
                    Preset = preset,
                    Content = content,
                    Width = width,
                });
                equalButtonWidth = Mathf.Max(equalButtonWidth, width);
            }

            buttons = rebuilt.ToArray();

            resetButtonWidth = Mathf.Max(
                ResetButtonMinWidth,
                EditorStyles.miniButtonLeft.CalcSize(ResetContent).x,
                EditorStyles.miniButtonRight.CalcSize(ResetContent).x);

            buttonsWidth = 0f;
            for (int i = 0; i < buttons.Length; i++)
            {
                buttonsWidth += data.buttonWidth == HoPlaySpeedButtonWidth.AsIs
                    ? buttons[i].Width
                    : equalButtonWidth;
            }

            if (data.showResetButton)
            {
                buttonsWidth += data.buttonWidth == HoPlaySpeedButtonWidth.AsIs
                    ? resetButtonWidth
                    : Mathf.Max(resetButtonWidth, equalButtonWidth);
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 右键菜单
        // ══════════════════════════════════════════════════════════════

        void IHasCustomMenu.AddItemsToMenu(GenericMenu menu)
        {
            HoPlaySpeedSettingsData data = HoPlaySpeedSettings.Data;
            menu.AddItem(MenuSettings, false, HoPlaySpeedSettingsWindow.Open);
            menu.AddSeparator(string.Empty);

            // 没在播放时"复位"没有意义：originalTimeScale 还是字段默认值，点下去只会把期望速度改成 1
            if (armed)
            {
                menu.AddItem(MenuReset, false, () => RestoreOriginal(HoPlaySpeedSettings.Data, true));
            }
            else
            {
                menu.AddDisabledItem(MenuReset);
            }

            menu.AddItem(MenuAutoReset, data.resetOnPlayEnd, () =>
            {
                data.resetOnPlayEnd = !data.resetOnPlayEnd;
                AutoResetContent.image = data.resetOnPlayEnd ? icons.autoResetOn : icons.autoResetOff;
                HoPlaySpeedSettings.NotifyChanged();
            });
            menu.AddSeparator(string.Empty);
            menu.AddItem(MenuResetAll, false, HoPlaySpeedSettings.ResetToDefault);
        }

        /// <summary>窗口体内右键：<see cref="IHasCustomMenu"/> 只管页签与停靠区，画布里的右键得自己接。</summary>
        private void HandleContextClick()
        {
            Event current = Event.current;
            if (current.type != EventType.ContextClick)
            {
                return;
            }

            GenericMenu menu = new GenericMenu();
            ((IHasCustomMenu)this).AddItemsToMenu(menu);
            menu.ShowAsContext();
            current.Use();
        }
    }
}
