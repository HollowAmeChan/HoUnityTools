namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>
    /// 面捕参数的**命名规则 —— 唯一出处**。会话、面板、用例、文档都从这里取词，不许各自拼字符串。
    ///
    /// 这里只剩"要有哪些参数"这件事。**控制器里那些树的形状、每格写什么键，都不再由代码规定** ——
    /// 控制器是搬来的作品（见 Editor/FaceTracking/HoFaceAnimationAssets.cs 的装配说明），
    /// 它的格子名、轴名、树形都由作者在混合树编辑器里定。作者约定见
    /// docs/FACE_TRACKING_CONTROLLER_STRUCTURE.md §5.2。
    ///
    /// **参数名一律 ASCII**（以后 VRChat 参数名有字符限制），并且统一收在 <see cref="ParameterRoot"/> 下。
    /// 会话只在参数真的存在时才写：搬来一份别的血统的控制器时，这些名字写不进去就算了（不猜、不补）。
    /// </summary>
    public static class HoFaceNaming
    {
        /// <summary>驱动层产出的所有参数的根。</summary>
        public const string ParameterRoot = "Ho/Drive";

        // ── 轴语义（正端在前）──────────────────────────────────────────────────
        /// <summary>眼睑开合：<c>+1</c> 闭 / <c>-1</c> 睁大 / <c>0</c> 中性。</summary>
        public const string LidOpenAxis = "BlinkWide";
        /// <summary>眼睑眯眼：<c>+1</c> 眯 / <c>0</c> 不眯（单端）。</summary>
        public const string LidSquintAxis = "Squint";

        /// <summary>左右侧的英文名，用于参数名（ASCII）。</summary>
        public static string Side(int side) => side == 0 ? "Left" : "Right";

        /// <summary>区域门控参数：<c>Ho/Drive/Gate/Eye</c> / <c>Ho/Drive/Gate/Lip</c>。</summary>
        public static string Gate(HoFaceGate gate) =>
            ParameterRoot + "/Gate/" + (gate == HoFaceGate.Eye ? "Eye" : "Lip");

        /// <summary>眼睑的两根轴参数：<c>Ho/Drive/Lid/Left/BlinkWide</c> 与 <c>…/Squint</c>。</summary>
        public static string LidAxis(int side, bool horizontal) =>
            ParameterRoot + "/Lid/" + Side(side) + "/" + (horizontal ? LidOpenAxis : LidSquintAxis);
    }
}
