#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor
{
    /// <summary>
    /// 缓存的 GUIStyle 副本与当前编辑器皮肤的同步工具。
    /// <para>
    /// <c>new GUIStyle(EditorStyles.xxx)</c> 得到的副本会把创建那一刻的皮肤整个固定下来：
    /// 文字颜色、八个交互状态的背景贴图都在拷贝里。编辑器启动、域重载后恢复窗口布局，
    /// 或者直接切换明暗主题时，Unity 会重建 EditorStyles 并换掉 GUI.skin，副本却不会自愈——
    /// 于是面板标题、状态文字画成浅色皮肤的黑字（深色主题下几乎看不见），按钮背景也会整个丢掉。
    /// 因此窗口每次 OnGUI 都要核对副本是否还对得当前皮肤，不对就整体重建。
    /// </para>
    /// </summary>
    internal static class HoEditorStyles
    {
        /// <summary>
        /// 副本是否仍然对应当前皮肤来源。
        /// 文字颜色是主判据：皮肤变化必然带来文字颜色变化。
        /// 另外单独看一次背景：皮肤资源还没加载出来时复制到的副本会丢掉背景贴图
        /// （按钮就变成一块没有底、只剩黑字的透明区域），来源后来拿到背景贴图就该重建。
        /// 只做“来源有、副本没有”这一侧的判断，避免个别皮肤状态下每帧重建。
        /// </summary>
        public static bool MatchesSource(GUIStyle cached, GUIStyle source)
        {
            if (cached == null || source == null)
                return false;

            if (cached.normal.textColor != source.normal.textColor)
                return false;

            if (cached.normal.background == null && source.normal.background != null)
                return false;

            return true;
        }

        /// <summary>
        /// 把副本的文字颜色对齐到来源样式。用于来源实例没换、只是颜色被重建过的场景。
        /// </summary>
        public static void SyncTextColor(GUIStyle target, GUIStyle source)
        {
            if (target == null || source == null)
                return;

            Color color = source.normal.textColor;
            target.normal.textColor = color;
            target.hover.textColor = color;
            target.active.textColor = color;
            target.focused.textColor = color;
            target.onNormal.textColor = color;
            target.onHover.textColor = color;
            target.onActive.textColor = color;
            target.onFocused.textColor = color;
        }
    }
}
#endif
