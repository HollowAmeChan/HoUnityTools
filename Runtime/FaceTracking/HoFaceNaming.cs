using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>
    /// 面捕生成物的**命名规则 —— 唯一出处**。生成器、会话、用例、文档都从这里取词，
    /// 不许各自拼字符串：名字是唯一能把「混合树里的格子」和「去 DCC 做形态键时的那张清单」
    /// 对上的东西，散着写迟早会漂。
    ///
    /// **2D 格子**（一根树一根网格）：
    /// <code>
    /// &lt;树&gt;_&lt;x语义&gt;_&lt;y语义&gt;__&lt;格语义&gt;__&lt;x格数&gt;x&lt;y格数&gt;_&lt;x索引&gt;_&lt;y索引&gt;
    /// </code>
    /// **1D 格子**去掉 y 的全部字段：
    /// <code>
    /// &lt;树&gt;_&lt;x语义&gt;__&lt;格语义&gt;__&lt;x格数&gt;_&lt;x索引&gt;
    /// </code>
    ///
    /// 规则：
    /// <list type="bullet">
    /// <item>轴名 = **正端语义 + 负端语义**，+1 的那端在前（眼睑开合是「闭 / 睁大」→ <c>BlinkWide</c>）；
    /// 单端轴只写正端（眯眼 → <c>Squint</c>）。</item>
    /// <item>索引**从 0 开始**；尺寸字段写的是**格子数**（3 列写 <c>3</c>，于是索引是 0..2），不是最大索引。</item>
    /// <item>格语义 = 各轴的**非中性端**按 x→y 顺序直接拼接（<c>WideSquint</c> / <c>BlinkSquint</c>）；
    /// 两端都中性写 <c>Neutral</c>。</item>
    /// <item>双下划线 <c>__</c> 是**唯一的结构分隔符**，所以轴语义里不许出现它。</item>
    /// <item>片段名里**不带具体键名**（格式没给键名留位置）。具体这一格写哪几个键、各多少值，
    /// 看 docs/FACE_TRACKING_CONTROLLER_STRUCTURE.md 的对照表。</item>
    /// </list>
    ///
    /// **参数名一律 ASCII**（它是给后端吃的，以后 VRChat 参数名有字符限制），
    /// 并且统一收在 <see cref="ParameterRoot"/> 下；只有片段名与树名是给人看的。
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

        /// <summary>眼睑 2D 网格的列数（开合：睁大 / 中性 / 闭）。</summary>
        public const int LidColumns = 3;
        /// <summary>眼睑 2D 网格的行数（眯眼：不眯 / 眯）。</summary>
        public const int LidRows = 2;

        /// <summary>两端都中性时的格语义。</summary>
        public const string Neutral = "Neutral";

        /// <summary>左右侧的英文名，用于参数名（ASCII）。</summary>
        public static string Side(int side) => side == 0 ? "Left" : "Right";

        /// <summary>眼睑 2D 树名：<c>LidL</c> / <c>LidR</c>。</summary>
        public static string LidTree(int side) => "Lid" + (side == 0 ? "L" : "R");

        /// <summary>区域 Direct 子树名：<c>EyeRegion</c> / <c>LipRegion</c>。</summary>
        public static string RegionTree(HoFaceGate gate) => gate == HoFaceGate.Eye ? "EyeRegion" : "LipRegion";

        /// <summary>区域门控参数：<c>Ho/Drive/Gate/Eye</c> / <c>Ho/Drive/Gate/Lip</c>。</summary>
        public static string Gate(HoFaceGate gate) =>
            ParameterRoot + "/Gate/" + (gate == HoFaceGate.Eye ? "Eye" : "Lip");

        /// <summary>眼睑的两根轴参数：<c>Ho/Drive/Lid/Left/BlinkWide</c> 与 <c>…/Squint</c>。</summary>
        public static string LidAxis(int side, bool horizontal) =>
            ParameterRoot + "/Lid/" + Side(side) + "/" + (horizontal ? LidOpenAxis : LidSquintAxis);

        /// <summary>
        /// 网格索引 → 轴值。X 轴三档：索引 0/1/2 = 睁大(-1) / 中性(0) / 闭(+1)；
        /// Y 轴两档：索引 0/1 = 不眯(0) / 眯(+1)。**闭在最后一列**就是轴名「正端在前」的来源。
        /// </summary>
        public static Vector2 LidPosition(int x, int y) =>
            new Vector2(Mathf.Clamp(x - 1, -1, 1), Mathf.Clamp(y, 0, 1));

        /// <summary>各轴非中性端按 x→y 顺序拼接；两端都中性 = <see cref="Neutral"/>。</summary>
        public static string Art(string xEnd, string yEnd)
        {
            string art = (xEnd ?? string.Empty) + (yEnd ?? string.Empty);
            return art.Length == 0 ? Neutral : art;
        }

        /// <summary>2D 格子名。</summary>
        public static string Cell(string treeName, string xSemantic, string ySemantic, string art,
            int xCount, int yCount, int x, int y) =>
            treeName + "_" + xSemantic + "_" + ySemantic + "__" + art + "__"
            + xCount + "x" + yCount + "_" + x + "_" + y;

        /// <summary>1D 格子名（只有一根轴）。</summary>
        public static string Cell(string treeName, string xSemantic, string art, int xCount, int x) =>
            treeName + "_" + xSemantic + "__" + art + "__" + xCount + "_" + x;

        /// <summary>眼睑 2D 的格子名：<c>LidL_BlinkWide_Squint__BlinkSquint__3x2_2_1</c>。</summary>
        public static string LidCell(int side, string xEnd, string yEnd, int x, int y) =>
            Cell(LidTree(side), LidOpenAxis, LidSquintAxis, Art(xEnd, yEnd), LidColumns, LidRows, x, y);
    }
}
