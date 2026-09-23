using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.AnimationTools
{
    /// <summary>
    /// 走带图标：**一律用 Unity 内置图标**，拿不到就退回文字。
    ///
    /// <para>自己画位图这条路是错的 —— 在 14~16px 上程序化画三角/竖条，
    /// 无论怎么调点位都比不过 Unity 自己按当前 DPI 出的图标，而且每个形状都要单独调。
    /// 这个模块照 <c>HoFastBuildWarudoModWindow.DrawIconButton</c> 的既有做法：
    /// 有图标用图标，没有就用文字，**任何情况下都不会出现空白键**。</para>
    ///
    /// <para>图标名不写死一个：不同 Unity 版本内置资源的命名有差异（`d_` 前缀是深色皮肤版），
    /// 所以每个动作给一串候选，探测到哪个用哪个。探测结果在 <see cref="ResolvedReport"/> 里，
    /// 面板的「设置」里可以展开看 —— 这样"哪个名字在你这个版本上有效"是查出来的，不是猜的。</para>
    /// </summary>
    internal static class HoAnimationPreviewIcons
    {
        /// <summary>一个动作的图标：候选名 + 文字兜底。</summary>
        internal sealed class Entry
        {
            internal readonly string[] Candidates;
            internal readonly string FallbackText;
            internal readonly string Tooltip;

            private Texture2D texture;
            private bool resolved;

            internal Entry(string tooltip, string fallbackText, params string[] candidates)
            {
                Tooltip = tooltip;
                FallbackText = fallbackText;
                Candidates = candidates;
            }

            /// <summary>探测到的图标；没有则为 null（这时用 <see cref="FallbackText"/>）。</summary>
            internal Texture2D Texture
            {
                get
                {
                    if (!resolved)
                    {
                        resolved = true;
                        texture = Resolve(Candidates);
                    }
                    return texture;
                }
            }

            /// <summary>实际生效的是哪个候选名（没探到则为 null）。</summary>
            internal string ResolvedName { get; private set; }

            private Texture2D Resolve(string[] candidates)
            {
                for (int i = 0; i < candidates.Length; i++)
                {
                    Texture2D found = Find(candidates[i]);
                    if (found != null)
                    {
                        ResolvedName = candidates[i];
                        return found;
                    }
                }
                return null;
            }

            /// <summary>把探测结果写进日志（一次）。</summary>
            internal void LogResolution()
            {
                if (Texture == null)
                {
                    Debug.LogWarning(
                        "Ho Animation Clip Previewer：走带图标「" + Tooltip
                        + "」在本版本 Unity 里没探到（试过：" + string.Join(", ", Candidates)
                        + "），已退回文字「" + FallbackText + "」。", null);
                }
            }
        }

        // 深色版（d_）在前，浅色版在后 —— 皮肤不对时 IconContent 会返回 null，自动落到下一个。
        internal static readonly Entry Play = new Entry(
            "播放", "▶", "d_PlayButton", "PlayButton", "d_Animation.Play", "Animation.Play");

        internal static readonly Entry Pause = new Entry(
            "暂停", "❚❚", "d_PauseButton", "PauseButton", "d_Animation.Pause", "Animation.Pause");

        internal static readonly Entry StepBack = new Entry(
            "上一帧", "◀", "d_Animation.PrevKey", "Animation.PrevKey", "d_StepButton", "StepButton");

        internal static readonly Entry StepForward = new Entry(
            "下一帧", "▶", "d_Animation.NextKey", "Animation.NextKey", "d_StepButton", "StepButton");

        internal static readonly Entry Start = new Entry(
            "回到开头", "|◀", "d_Animation.FirstKey", "Animation.FirstKey", "d_PreMatCube", "PreMatCube");

        internal static readonly Entry End = new Entry(
            "到末尾", "▶|", "d_Animation.LastKey", "Animation.LastKey", "d_PreMatSphere", "PreMatSphere");

        private static readonly Entry[] All = { Play, Pause, StepBack, StepForward, Start, End };

        /// <summary>把每个动作的探测结果汇总成一段文字，用于面板上显示 / 排查。</summary>
        internal static string ResolvedReport()
        {
            var lines = new List<string>();
            for (int i = 0; i < All.Length; i++)
            {
                Entry entry = All[i];
                bool has = entry.Texture != null;
                lines.Add(entry.Tooltip + "：" + (has ? entry.ResolvedName : "无（用文字「" + entry.FallbackText + "」）"));
            }
            return string.Join("\n", lines.ToArray());
        }

        /// <summary>把探测失败的项写进 Console（每个只写一次）。</summary>
        internal static void LogUnresolved()
        {
            for (int i = 0; i < All.Length; i++)
                All[i].LogResolution();
        }

        /// <summary>
        /// 找内置图标。<c>IconContent</c> 对不存在的名字返回的是**空 GUIContent 而不是 null**，
        /// 所以判据必须是 <c>image == null</c>。
        /// </summary>
        private static Texture2D Find(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            try
            {
                GUIContent content = EditorGUIUtility.IconContent(name);
                if (content == null || content.image == null)
                    return null;
                return content.image as Texture2D;
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 画一个走带键：有图标画图标，没有画文字。
        /// 返回是否点击。
        /// </summary>
        internal static bool Button(Rect rect, Entry entry, bool on, bool enabled)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            Event current = Event.current;
            bool hot = enabled && rect.Contains(current.mousePosition);
            if (hot)
                EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);

            bool clicked = false;
            if (hot && current.type == EventType.MouseDown && current.button == 0)
            {
                clicked = true;
                GUI.changed = true;
                GUIUtility.hotControl = id;
                current.Use();
            }
            else if (GUIUtility.hotControl == id && current.type == EventType.MouseUp)
            {
                GUIUtility.hotControl = 0;
            }

            // 悬停 / 按下底：内置图标本身没有底，不给底就分不出可点区域。
            if (hot)
            {
                Color previous = GUI.color;
                GUI.color = GUIUtility.hotControl == id
                    ? new Color(1f, 1f, 1f, 0.14f)
                    : new Color(1f, 1f, 1f, 0.08f);
                GUI.DrawTexture(rect, Texture2D.whiteTexture);
                GUI.color = previous;
            }

            Texture2D texture = entry.Texture;
            if (texture != null)
            {
                // 内置图标是 16×16 的多分辨率贴图，按原尺寸居中画，不缩放。
                float size = Mathf.Min(rect.width, rect.height, 16f);
                var iconRect = new Rect(
                    Mathf.Round(rect.center.x - (size * 0.5f)),
                    Mathf.Round(rect.center.y - (size * 0.5f)),
                    size,
                    size);

                Color previous = GUI.color;
                GUI.color = enabled ? Color.white : new Color(1f, 1f, 1f, 0.4f);
                GUI.DrawTexture(iconRect, texture, ScaleMode.StretchToFill, true);
                GUI.color = previous;
            }
            else
            {
                GUI.Label(rect, entry.FallbackText, on ? FallbackOn : FallbackOff);
            }

            return clicked;
        }

        private static GUIStyle fallbackOn;
        private static GUIStyle fallbackOff;

        private static GUIStyle FallbackOn
        {
            get
            {
                if (fallbackOn == null)
                {
                    fallbackOn = new GUIStyle(EditorStyles.miniLabel)
                    {
                        fontSize = 11,
                        alignment = TextAnchor.MiddleCenter
                    };
                    fallbackOn.normal.textColor = HoAnimationPreviewTheme.TextBrightColor;
                }
                return fallbackOn;
            }
        }

        private static GUIStyle FallbackOff
        {
            get
            {
                if (fallbackOff == null)
                {
                    fallbackOff = new GUIStyle(EditorStyles.miniLabel)
                    {
                        fontSize = 11,
                        alignment = TextAnchor.MiddleCenter
                    };
                    fallbackOff.normal.textColor = HoAnimationPreviewTheme.TextDimColor;
                }
                return fallbackOff;
            }
        }
    }
}
