using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>
    /// 面捕生成物的**命名规则 —— 唯一出处**。生成器、会话、用例、文档都从这里取词，
    /// 不许各自拼字符串：名字是唯一能把「混合树里的格子」和「去 DCC 做形态键时的那张清单」
    /// 对上的东西，散着写迟早会漂。
    ///
    /// 格子名格式：
    /// <code>
    /// &lt;树&gt;_&lt;方阵&gt;X&lt;x刻度&gt;Y&lt;y刻度&gt;        例：LidL_A3X2Y2
    /// </code>
    ///
    /// 规则：
    /// <list type="bullet">
    /// <item><b>方阵</b>：<c>A</c> + 每轴刻度数，而且**必定方形** —— 两根轴用同一套刻度。
    /// <c>A3</c> = 每轴 3 个刻度（0/1/2）。用方阵而不是"3x2"是为了**天然消掉"中轴在哪"的歧义**：
    /// 奇数刻度的中间刻度就是中线，两轴刻度数一致，不会再被误读成"关于 0 对称的 0.5 坐标"。</item>
    /// <item><b>坐标从 0 开始、左下为原点、永远不出现负号</b>。刻度线性铺满该轴的值域：
    /// <c>A3</c> 时 X（双边 −1..1）→ 0/1/2 即 −1/0/+1，中间刻度就是中线；
    /// Y（单边 0..1）→ 0/1/2 即 0/0.5/+1。</item>
    /// <item>**允许小数刻度**（如 <c>X0.5</c>）：坐标是"网格位置"而不是"第几格"，所以轴的密度
    /// 变了也不用改字段格式 —— 这是方阵相对"格数 + 序号"最实际的好处。</item>
    /// <item>名字里**不带轴语义、格语义、驱动键名**。那三样分别在网格定义表与
    /// docs/FACE_TRACKING_CONTROLLER_STRUCTURE.md §5.2 的对照表里 ——
    /// 名字里只留**一个严格可解析的字段**（<c>&lt;方阵&gt;X&lt;数&gt;Y&lt;数&gt;</c>），其余交给文档。</item>
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
        // 只用于**参数名**与文档：片段名里不再出现轴语义（见 <see cref="Cell"/>）。
        /// <summary>眼睑开合：<c>+1</c> 闭 / <c>-1</c> 睁大 / <c>0</c> 中性。</summary>
        public const string LidOpenAxis = "BlinkWide";
        /// <summary>眼睑眯眼：<c>+1</c> 眯 / <c>0</c> 不眯（单端）。</summary>
        public const string LidSquintAxis = "Squint";

        /// <summary>眼睑的网格（方阵）：<c>A3</c> = 每轴 3 刻度，原点在左下。</summary>
        public const string LidGrid = "A3";
        /// <summary>该方阵每轴的刻度数（就是 <see cref="LidGrid"/> 里那个 3）。</summary>
        public const int LidSteps = 3;

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

        /// <summary>网格代号：<c>A</c> + 每轴刻度数（<c>A3</c> = 3×3 方阵）。</summary>
        public static string Grid(int steps) => "A" + steps;

        /// <summary>刻度写法：整数不带小数点，其余用最短写法（<c>0.5</c>）。</summary>
        public static string Number(float step) =>
            Mathf.Approximately(step, Mathf.Round(step))
                ? Mathf.RoundToInt(step).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : step.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>格子名：<c>LidL_A3X2Y2</c>。</summary>
        public static string Cell(string treeName, string grid, float x, float y) =>
            treeName + "_" + grid + "X" + Number(x) + "Y" + Number(y);

        /// <summary>
        /// 刻度 → 轴值：双边轴（有正负两端）铺满 −1..1，单边轴铺满 0..1。
        /// **刻度从 0 开始、顺序即从负端到正端**，所以 <c>A3</c> 的中间刻度落在轴值 0 / 0.5。
        /// </summary>
        public static float LatticeValue(float step, int steps, bool signedAxis)
        {
            float t = steps <= 1 ? 0f : Mathf.Clamp01(step / (steps - 1));
            return signedAxis ? t * 2f - 1f : t;
        }

        /// <summary>
        /// 眼睑两轴在 <see cref="LidGrid"/> 里的格点值（喂给树的 <c>Pos X / Pos Y</c>）：
        /// X 双边 0/1/2 → −1/0/+1（所以 <c>X1</c> 是中线），Y 单边 0/1/2 → 0/0.5/+1
        /// （所以我们只在 <c>Y0</c> 不眯与 <c>Y2</c> 眯满两行摆了姿势，<c>Y1</c> 半眯空着）。
        /// </summary>
        public static Vector2 LidPosition(float xStep, float yStep) =>
            new Vector2(LatticeValue(xStep, LidSteps, true), LatticeValue(yStep, LidSteps, false));

        /// <summary>眼睑格子名：<c>LidL_A3X1Y2</c>。</summary>
        public static string LidCell(int side, float xStep, float yStep) =>
            Cell(LidTree(side), LidGrid, xStep, yStep);
    }
}
