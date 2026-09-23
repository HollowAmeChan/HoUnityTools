using Hollow.HoUnityTools.Animations;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.AnimationTools
{
    /// <summary>
    /// 预览走带。分三层，每层独占高度：
    ///
    /// <code>
    ///   ⟳  1.24 / 3.00s                              37/90      读数 13
    ///   ▓▓▓▓▓▓▓▓▓▓▓░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░       时间轴 18
    ///   (▶)   ⏮   ◀   ▶   ⏭   ←——  摘要  ———→                按钮 20
    /// </code>
    ///
    /// <para>早先这些东西挤在一行里分 280px，怎么调间距都挤 —— 问题不在间距，在"四个东西抢一行"。
    /// 现在时间轴独占整宽，一眼就是主体。</para>
    ///
    /// <para>**布局全部自己算矩形**，不交给 GUILayout 分配宽度：它按剩余宽度百分比分空间，
    /// 不给宽度的按钮会被压成几个像素的小点。见 `docs/EDITOR_UI_SYSTEM.md`。</para>
    /// </summary>
    internal static class HoAnimationPreviewTimeline
    {
        /// <summary>正在拖动时间轴。拖动期间不要推进播放头。</summary>
        internal static bool Dragging { get; private set; }

        private static int seekControlId;

        private const float FrameWidth = 42f;

        /// <summary>画整个走带。</summary>
        internal static void Draw(
            HoAnimationPreviewer previewer,
            bool loop,
            bool hasError,
            bool hasWarning,
            SerializedProperty clipProperty)
        {
            // 拖动中途被外部改了剪辑（时长变了）就把抓取状态收掉，免得播放头卡在旧比例上。
            if (Dragging && (previewer == null || SeekControlChanged()))
                Dragging = false;

            Rect row = EditorGUILayout.GetControlRect(
                false,
                HoAnimationPreviewTheme.BarHeight,
                GUIStyle.none);
            HoAnimationPreviewTheme.Card(row);

            // 卡片内边距自己收，不靠 GUIStyle.padding（那会让 GUILayout 重分配宽度）。
            const float Pad = HoAnimationPreviewTheme.CardPadding;
            Rect content = HoAnimationPreviewTheme.Inset(row, Pad, Pad, Pad, Pad);
            content.width = Mathf.Max(40f, content.width - HoAnimationPreviewTheme.ScrollbarReserve);

            float readoutHeight = HoAnimationPreviewTheme.ReadoutHeight;
            float seekHeight = HoAnimationPreviewTheme.SeekHeight;
            float transportHeight = HoAnimationPreviewTheme.TransportHeight;

            var readout = new Rect(content.x, content.y, content.width, readoutHeight);
            var seek = new Rect(content.x, readout.yMax + 3f, content.width, seekHeight);
            var transport = new Rect(content.x, seek.yMax + 3f, content.width, transportHeight);

            DrawReadout(readout, previewer, loop, hasError, hasWarning);
            // 组件禁用时走带只读：接口那边已经会静默返回，这里让"点了没反应"变成看得见。
            DrawSeekBar(seek, previewer, previewer != null && previewer.enabled);
            DrawTransport(transport, previewer, clipProperty);
        }

        // ── 读数 ────────────────────────────────────────────────────────

        private static void DrawReadout(
            Rect row,
            HoAnimationPreviewer previewer,
            bool loop,
            bool hasError,
            bool hasWarning)
        {
            bool ready = previewer != null && previewer.IsPlayableReady;

            // 右端：帧号。
            if (ready)
            {
                var frameRect = new Rect(row.xMax - FrameWidth, row.y, FrameWidth, row.height);
                GUI.Label(frameRect,
                    previewer.CurrentFrame + " / " + (previewer.FrameCount - 1),
                    HoAnimationPreviewTheme.ReadoutDim);
            }

            // 左端：循环标记（只在循环时出现）。纯文字，不花图标。
            float left = row.x;
            if (loop)
            {
                float loopWidth = HoAnimationPreviewTheme.Caption.CalcSize(new GUIContent("循环")).x;
                GUI.Label(new Rect(left, row.y, loopWidth, row.height), "循环",
                    HoAnimationPreviewTheme.Caption);
                left += loopWidth + 8f;
            }

            // 时间 / 时长。
            string time = ready
                ? previewer.CurrentTime.ToString("F2") + " / " + previewer.Duration.ToString("F2") + "s"
                : "—";
            float timeWidth = HoAnimationPreviewTheme.Readout.CalcSize(new GUIContent(time)).x;
            GUI.Label(new Rect(left, row.y, timeWidth, row.height), time, HoAnimationPreviewTheme.Readout);

            // 异常挤在时间后面。
            if (hasError)
                GUI.Label(new Rect(left + timeWidth + 8f, row.y, 58f, row.height), "预览失败", ErrorStyle);
            else if (hasWarning)
                GUI.Label(new Rect(left + timeWidth + 8f, row.y, 58f, row.height), "无法预览", WarningStyle);
        }

        private static GUIStyle errorStyle;
        private static GUIStyle warningStyle;

        private static GUIStyle ErrorStyle
        {
            get
            {
                if (errorStyle == null)
                {
                    errorStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 };
                    errorStyle.normal.textColor = HoAnimationPreviewTheme.ErrorColor;
                }
                return errorStyle;
            }
        }

        private static GUIStyle WarningStyle
        {
            get
            {
                if (warningStyle == null)
                {
                    warningStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 };
                    warningStyle.normal.textColor = HoAnimationPreviewTheme.WarningColor;
                }
                return warningStyle;
            }
        }

        // ── 按钮行 ──────────────────────────────────────────────────────

        private static void DrawTransport(Rect row, HoAnimationPreviewer previewer, SerializedProperty clipProperty)
        {
            // 用 CanPreview 而不是 IsPlayableReady 当闸门：编辑器里组件不自动接管，
            // 没点过「预览」时图还没建 —— 那时候按钮必须能点（点了才发起预览）。
            // 但**组件禁用时全部只读**：接口那边会静默返回，这里让"点了没反应"看得见。
            bool usable = previewer != null && previewer.enabled
                && previewer.CanPreview && previewer.Duration > 0f;

            float size = HoAnimationPreviewTheme.IconSize;
            float gutter = HoAnimationPreviewTheme.Gutter;
            // 图标格与剪辑字段同高、垂直居中。
            float y = row.y + ((row.height - size) * 0.5f);
            float x = row.x;

            // 播放 / 暂停：同一个键，按状态换图标。
            bool playing = usable && previewer.IsPlaying;
            if (HoAnimationPreviewIcons.Button(
                    new Rect(x, y, size, size),
                    playing ? HoAnimationPreviewIcons.Pause : HoAnimationPreviewIcons.Play,
                    playing, usable) && previewer != null)
            {
                previewer.TogglePlay();
            }
            x += size + gutter;

            if (HoAnimationPreviewIcons.Button(new Rect(x, y, size, size), HoAnimationPreviewIcons.Start, false, usable)
                && previewer != null)
            {
                previewer.SetTime(0f, previewer.IsPlaying);
            }
            x += size + gutter;

            if (HoAnimationPreviewIcons.Button(new Rect(x, y, size, size), HoAnimationPreviewIcons.StepBack, false, usable)
                && previewer != null)
            {
                previewer.StepFrames(-1);
            }
            x += size + gutter;

            if (HoAnimationPreviewIcons.Button(new Rect(x, y, size, size), HoAnimationPreviewIcons.StepForward, false, usable)
                && previewer != null)
            {
                previewer.StepFrames(1);
            }
            x += size + gutter;

            if (HoAnimationPreviewIcons.Button(new Rect(x, y, size, size), HoAnimationPreviewIcons.End, false, usable)
                && previewer != null)
            {
                previewer.SetFrame(previewer.FrameCount - 1, false);
            }
            x += size;

            // 右边剩下的全给剪辑字段 —— 挂载即用，不用再往下找一行。
            if (clipProperty != null)
            {
                float fieldX = x + HoAnimationPreviewTheme.ClipFieldGap;
                var fieldRect = new Rect(fieldX, row.y, Mathf.Max(40f, row.xMax - fieldX), row.height);

                EditorGUI.BeginChangeCheck();
                EditorGUI.ObjectField(fieldRect, clipProperty, GUIContent.none);
                if (EditorGUI.EndChangeCheck())
                {
                    // 只管落地；重建由组件的 OnValidate 兜底（它会推迟到下一次编辑器 update）。
                    clipProperty.serializedObject.ApplyModifiedProperties();
                }
            }
        }

        // ── 时间轴（独占整宽）──────────────────────────────────────────

        private static void DrawSeekBar(Rect rect, HoAnimationPreviewer previewer, bool interactable)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);

            float duration = previewer != null ? previewer.Duration : 0f;
            bool hasTime = interactable && previewer.CanPreview && duration > 0f;

            if (!hasTime)
            {
                HoAnimationPreviewTheme.Box(rect, HoAnimationPreviewTheme.TrackColor, HoAnimationPreviewTheme.LineColor);
                GUI.Label(new Rect(rect.x + 7f, rect.y, rect.width - 14f, rect.height),
                    "—", HoAnimationPreviewTheme.Hint);
                return;
            }

            EditorGUIUtility.AddCursorRect(rect, MouseCursor.SlideArrow);

            Event current = Event.current;
            bool inside = rect.Contains(current.mousePosition);

            if (current.type == EventType.MouseDown && current.button == 0 && inside)
            {
                GUIUtility.hotControl = id;
                seekControlId = id;
                Dragging = true;
                ApplyHead(previewer, rect, current.mousePosition.x, duration);
                current.Use();
            }
            else if (Dragging && seekControlId == id)
            {
                if (current.type == EventType.MouseDrag || current.type == EventType.MouseMove)
                {
                    ApplyHead(previewer, rect, current.mousePosition.x, duration);
                    current.Use();
                }
                else if (current.type == EventType.MouseUp || current.type == EventType.Ignore)
                {
                    GUIUtility.hotControl = 0;
                    Dragging = false;
                    current.Use();
                }
            }

            DrawTrack(rect, Mathf.Clamp01(previewer.NormalizedTime), duration);
        }

        /// <summary>把槽画出来：底、已播放段、整秒刻度、播放头。</summary>
        private static void DrawTrack(Rect rect, float normalized, float duration)
        {
            HoAnimationPreviewTheme.Box(rect, HoAnimationPreviewTheme.TrackColor, HoAnimationPreviewTheme.LineColor);

            Rect inner = HoAnimationPreviewTheme.Inset(rect, 1f, 1f, 1f, 1f);
            if (inner.width <= 0f || inner.height <= 0f)
                return;

            // 已播放段。
            if (normalized > 0f)
            {
                var fill = new Rect(inner.x, inner.y, inner.width * normalized, inner.height);
                HoAnimationPreviewTheme.GradientFill(fill, new Color(
                    HoAnimationPreviewTheme.AccentColor.r,
                    HoAnimationPreviewTheme.AccentColor.g,
                    HoAnimationPreviewTheme.AccentColor.b,
                    0.58f));
            }

            // 整秒刻度：只在读得清的时候画。
            int seconds = Mathf.FloorToInt(duration);
            if (seconds >= 1 && seconds <= 60)
            {
                for (int s = 1; s < seconds; s++)
                {
                    float t = s / duration;
                    if (t <= 0f || t >= 1f)
                        continue;
                    float px = inner.x + (inner.width * t);
                    HoAnimationPreviewTheme.Fill(
                        new Rect(Mathf.Round(px), inner.y + 3f, 1f, Mathf.Max(1f, inner.height - 6f)),
                        HoAnimationPreviewTheme.TickColor);
                }
            }

            // 播放头：就一条竖线，压在所有东西上面。不画三角头 —— 纯线条更干净，
            // 而且那点小三角在 1px 细线旁边反而像噪点。
            float headX = inner.x + (inner.width * normalized);
            HoAnimationPreviewTheme.Fill(
                new Rect(Mathf.Round(headX), rect.y, 1f, rect.height),
                HoAnimationPreviewTheme.PlayheadColor);
        }

        private static void ApplyHead(
            HoAnimationPreviewer previewer,
            Rect rect,
            float mouseX,
            float duration)
        {
            if (previewer == null || duration <= 0f)
                return;

            float t = Mathf.Clamp01((mouseX - rect.x) / Mathf.Max(1f, rect.width));
            previewer.SetTime(t * duration, false);
            GUI.changed = true;
        }

        /// <summary>拖动过程中控件 id 是否还归我们（防止别的控件抢走 hotControl）。</summary>
        private static bool SeekControlChanged()
        {
            return GUIUtility.hotControl != 0 && GUIUtility.hotControl != seekControlId;
        }
    }
}
