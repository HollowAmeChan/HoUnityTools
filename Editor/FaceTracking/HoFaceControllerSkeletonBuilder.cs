using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    /// <summary>
    /// **生成「VTS 原生语义」控制器的骨架**（2026-09-27）：43 个参数 + 27 棵树 + 89 个**空槽位**，
    /// ⚠️ **2026-09-28：`MouthWidth`（嘴宽/偏嘴残差，9 槽位 + 轴 `Mouth/Pucker` × `Mouth/X`）整棵删掉**（用户定）。
    ///    单根左右平移轴要拆成"左右两半的嘴角"，新的"整嘴平移 3×3 + 嘴角 2×2"等实测回来再建 ——
    ///    见 `docs/VTS_HQ_CONTROLLER.md` §5.7.22 与 `.research/结论-2026-09-28-嘴综合移动.md`。
    /// 一层 `Ho/00 Drive`、一个状态 `Drive`（Write Defaults 开）、无 Behaviour、无 clip —— 动画由作者后面填。
    ///
    /// 形状与"现在落到哪"记在 `docs/VTS_HQ_CONTROLLER.md`：**§2.1 结构、§2.2 每格叶子的语义**
    /// （根 Direct → 4 个区域 Direct → 2D 表 / 1D 副本树 / 变体 / 形态子树），槽位名按 `docs/FACE_TRACKING_NAMING.md` §5。
    ///
    /// ⚠️ **中间层的两条「形态契约行」**（`Ho/Drive/Gate/MouthStyle` = 形态门、`Ho/Drive/Style/InvertedV`
    /// = 倒V 权重）也在这里建：控制器看不见中间层的内部行 `Ho/Style/*`，中间层只能把值转发成控制器参数。
    /// 形态门压在 `MouthRegion` 的三个"张嘴 × 笑"孩子上（`@Style` 后缀），倒V 权重喂 `InvertedV` 那棵 1D 表。
    ///
    /// **为什么有这个菜单项**：
    /// ① 手搭 27 棵树不现实、也不可复现；
    /// ② 2026-09-27 那份 `PTP_CTR_Face_VTS.controller` 是用文本生成器写出来的（抄老控制器的字段集），
    ///    当时**没能在 Unity 里打开验证**（本机编辑器占着授权互斥量、跑不了 batchmode）——
    ///    这个菜单项就是"用 Unity 自己的 API 重新建一份"的兜底；
    /// ③ 设计改动（加树、改轴、加槽位）之后从这里重新生成，而不是去手改资产。
    ///    ⚠️ 但**用户是直接在 Animator 窗口里拉点的**（`MouthCore` 8 点 / `MouthJaw` 6 点）——
    ///    那些坐标在下面那些 `Override` 表里；**先读资产、再改这里**，顺序反了就把他的手改冲掉。
    ///
    /// ⚠️ 形状表（哪棵树混哪两根轴、哪些槽位、谁挂哪个门）在这里是**硬编码**的，跟着设计稿走；
    /// 生成出来的资产用 `Tools~/FaceTracking/check-controller.ps1` 对着**发货 profile** 反推的期望集核一遍。
    /// </summary>
    public static class HoFaceControllerSkeletonBuilder
    {
        private const string RootTreeName = "Ho/00 Drive Tree";
        private const string LayerName = "Ho/00 Drive";
        private const string StateName = "Drive";
        private const string OneWeight = "Ho/Drive/W/One";
        /// <summary>形态门（中间层的契约行 `Ho/Drive/Gate/MouthStyle`，默认 1 = 门开着）。</summary>
        private const string StyleGate = "Ho/Drive/Gate/MouthStyle";
        /// <summary>倒V 的形态权重（中间层的契约行 `Ho/Drive/Style/InvertedV`，默认 0 = 不是倒V）。</summary>
        private const string InvertedVWeight = "Ho/Drive/Style/InvertedV";

        private static readonly string[] RegionGates = { "Mouth", "Eye", "Brow", "Nose" };

        /// <summary>
        /// 轴参数（部位/轴 → 名字）：30 根，见设计稿 §3.1。
        /// ⚠️ 2026-09-28 当天三进三出：上午 `Mouth/X` 随 `MouthWidth` 一起退役；下午按「整嘴平移」重建
        /// （`Mouth/X` + `Mouth/Y`）并加过嘴角 3×3 的两根轴；傍晚用户判定「收缩舒张不必拆左右、一根轴
        /// 3 状态就够，左右交给整嘴平移」⇒ **删掉嘴角那两根**，`MouthWidth` 以 **1D 3 格**回来
        /// （轴就是既有的 `Ho/Drive/Mouth/Pucker`，不新增轴）。
        /// ⚠️ 有些轴**没有树消费**（注视 4 根、`Cheek/*` 4 根、`Funnel` / `Press`）—— 它们照旧发布当出口，
        /// 谁要用谁取（注视那 4 根就是给 Warudo 的 LookAt / 别的消费者留的）。
        /// </summary>
        private static readonly string[] Axes =
        {
            "Ho/Drive/Mouth/Form", "Ho/Drive/Mouth/Open", "Ho/Drive/Mouth/Funnel", "Ho/Drive/Mouth/Press",
            "Ho/Drive/Mouth/Jaw", "Ho/Drive/Mouth/JawSide", "Ho/Drive/Mouth/Forward", "Ho/Drive/Mouth/Pucker",
            // 整嘴平移（2026-09-28 下午重建；傍晚确认只有 6 个可达状态 ⇒ 树是 3×2）
            "Ho/Drive/Mouth/X", "Ho/Drive/Mouth/Y",
            "Ho/Drive/Mouth/TongueL", "Ho/Drive/Mouth/TongueR",
            // 嘴宽（2026-09-28 傍晚落地）：`MouthWidth` = **1D 3 格**（窄 收嘴不撅 / 中 静态 / 宽 抿嘴嘴宽），
            // 轴 = 既有的 `Ho/Drive/Mouth/Pucker`（不新增轴）；左右那一维交给 `MouthShift`（整嘴平移 3×2）。
            "Ho/Drive/Mouth/CornerL", "Ho/Drive/Mouth/CornerR",
            // 卷唇（2026-09-27）：上下两根线**一起增减** ⇒ 中间层平均成一根 `Mouth/Roll`，表也跟着变成 1D。
            "Ho/Drive/Style/CatMouth",
            "Ho/Drive/Lid/Left/BlinkWide", "Ho/Drive/Lid/Left/Form",
            "Ho/Drive/Lid/Left/Evidence/Happy", "Ho/Drive/Lid/Left/Evidence/Anger", "Ho/Drive/Lid/Left/Evidence/Sad",
            "Ho/Drive/Lid/Left/Weight/Neutral", "Ho/Drive/Lid/Left/Weight/Happy", "Ho/Drive/Lid/Left/Weight/Anger", "Ho/Drive/Lid/Left/Weight/Sad",
            "Ho/Drive/Lid/Left/Gate/NeutralClosed", "Ho/Drive/Lid/Left/Gate/NeutralOpen", "Ho/Drive/Lid/Left/Gate/NeutralWide",
            "Ho/Drive/Lid/Left/Gate/HappyClosed", "Ho/Drive/Lid/Left/Gate/HappyOpen", "Ho/Drive/Lid/Left/Gate/AngerClosed",
            "Ho/Drive/Lid/Left/Gate/AngerOpen", "Ho/Drive/Lid/Left/Gate/AngerWide", "Ho/Drive/Lid/Left/Gate/SadClosed",
            "Ho/Drive/Lid/Left/Gate/SadOpen", "Ho/Drive/Lid/Left/Gate/SadWide",
            "Ho/Drive/Lid/Right/BlinkWide", "Ho/Drive/Lid/Right/Form",
            "Ho/Drive/Lid/Right/Evidence/Happy", "Ho/Drive/Lid/Right/Evidence/Anger", "Ho/Drive/Lid/Right/Evidence/Sad",
            "Ho/Drive/Lid/Right/Weight/Neutral", "Ho/Drive/Lid/Right/Weight/Happy", "Ho/Drive/Lid/Right/Weight/Anger", "Ho/Drive/Lid/Right/Weight/Sad",
            "Ho/Drive/Lid/Right/Gate/NeutralClosed", "Ho/Drive/Lid/Right/Gate/NeutralOpen", "Ho/Drive/Lid/Right/Gate/NeutralWide",
            "Ho/Drive/Lid/Right/Gate/HappyClosed", "Ho/Drive/Lid/Right/Gate/HappyOpen", "Ho/Drive/Lid/Right/Gate/AngerClosed",
            "Ho/Drive/Lid/Right/Gate/AngerOpen", "Ho/Drive/Lid/Right/Gate/AngerWide", "Ho/Drive/Lid/Right/Gate/SadClosed",
            "Ho/Drive/Lid/Right/Gate/SadOpen", "Ho/Drive/Lid/Right/Gate/SadWide",
            // 注视（2026-09-27）：**两棵树删了**（朝向交给 Warudo 的 LookAt + IK），这 4 根轴照旧发布当出口
            "Ho/Drive/Gaze/Left/X", "Ho/Drive/Gaze/Left/Y", "Ho/Drive/Gaze/Right/X", "Ho/Drive/Gaze/Right/Y",
            "Ho/Drive/Brow/Left/Y", "Ho/Drive/Brow/Left/InnerUp", "Ho/Drive/Brow/Right/Y", "Ho/Drive/Brow/Right/InnerUp",
            // 颊（2026-09-27 删树）：二次元角色表现不了这两个形变；轴照旧发布当出口
            "Ho/Drive/Cheek/Left/Squint", "Ho/Drive/Cheek/Right/Squint",
            "Ho/Drive/Cheek/Left/Puff", "Ho/Drive/Cheek/Right/Puff",
            // 鼻（2026-09-27 收成**一个状态**：鼻子上顶）
            "Ho/Drive/Nose/Up"
        };

        /// <summary>MouthCore 的条件切片权重（Funnel × Press 双线性；骨架里还没有切片表，参数先建好）。</summary>
        private static readonly string[] MouthCoreSlices =
        {
            "Ho/Drive/Slice/MouthCore/Funnel0Press0", "Ho/Drive/Slice/MouthCore/Funnel1Press0",
            "Ho/Drive/Slice/MouthCore/Funnel0Press1", "Ho/Drive/Slice/MouthCore/Funnel1Press1"
        };

        /// <summary>表情门（默认 0，**中间层不写**：留给驱动"按键"的那一方）。</summary>
        private static readonly string[] ExpressionGates = { };

        /// <summary>一张 2D 表的定义：X/Y 参数 + 每个刻度的轴值 + 槽位名里那两段词 + 挖掉的格子。</summary>
        private sealed class TableSpec
        {
            public string Name;
            public string X, Y, XToken, YToken;
            public float[] XValues, YValues;
            /// <summary>稀疏表：这些 (i,j) 格子**物理上到不了**，不建（圈跟着斜切）。null = 摆满。</summary>
            public Vector2Int[] Skip;
            /// <summary>逐格坐标覆盖：这些 (i,j) 格**不摆在刻度值上**（实测定），坐标单独给。</summary>
            public CellPos[] Override;
        }

        /// <summary>一格的独立坐标（槽位名仍按索引编 ⇒ 挪坐标不改名、不用重烘）。</summary>
        private sealed class CellPos
        {
            public int I, J;
            public float X, Y;
        }

        /// <summary>轴驱动的变体开关：一张 1D 树，两个孩子都是**整张表**。</summary>
        private sealed class VariantSwitchSpec
        {
            public string Name;
            public string Main, Variant, Parameter;
            public float[] Thresholds;
        }

        /// <summary>一张 1D 表的定义：**只有一根轴**，刻度值就是阈值本身。</summary>
        private sealed class Simple1DSpec
        {
            public string Name;
            public string Parameter, Token;
            public float[] Values;
        }

        private static readonly float[] Two = { -1f, 0f, 1f };     // 双向轴：负端 / 中性 / 正端

        /// <summary>眼睑的 11 个状态点 = 4 情绪（中性/喜/怒/悲）× 3 闭合（闭/睁/睁大）**减去 `睁大×喜`**。
        /// 那一格不建，因为喜证据里有一道「不许睁大」的门 ⇒ `w喜 × w睁大 ≡ 0`（§5.7.36）。
        /// ⚠️ 顺序 = 控制器里的孩子顺序；槽位名 `LidL__Gate__HappyOpen` 就是语义。</summary>
        private static readonly string[] LidCells =
        {
            "NeutralClosed", "NeutralOpen", "NeutralWide",
            "HappyClosed", "HappyOpen",
            "AngerClosed", "AngerOpen", "AngerWide",
            "SadClosed", "SadOpen", "SadWide",
        };

        /// <summary>
        /// **Direct 张量积表**：每个孩子挂一个**门参数**（不是轴）——
        /// `Ho/Drive/Lid/&lt;侧&gt;/Gate/&lt;情绪&gt;&lt;闭合&gt;` = `w情绪 × w闭合`，11 个相加恒 = 1
        /// ⇒ 输出是这 11 张姿势的凸组合，`default` 不参与（§5.7.38 起眼睑就是这张表）。
        /// </summary>
        private sealed class DirectTableSpec
        {
            public string Name, Side;
            public string[] Cells;
        }

        private static readonly DirectTableSpec[] DirectTables =
        {
            new DirectTableSpec { Name = "LidL", Side = "Left",  Cells = LidCells },
            new DirectTableSpec { Name = "LidR", Side = "Right", Cells = LidCells },
        };

        /// <summary>叶子 = 一棵 Direct 树（名字同槽位名），里面暂时只有「原片段」一个孩子、权重恒 1。
        /// 形状占位：作者/工具后面往这棵树里插预制部件孩子，权重自己接。</summary>
        private static Motion LeafParts(AnimatorController controller, string slot, Motion clip)
        {
            var leaf = NewTree(controller, slot, BlendTreeType.Direct);
            AttachDirect(leaf, clip, OneWeight);
            return leaf;
        }

        private static readonly float[] Unit = { 0f, 0.5f, 1f };   // 单端轴：0 / 半 / 满
        /// <summary>
        /// **舌头的 1D 四档**（2026-09-28 深夜用户定：「默认态在左下角，的四点」→ 随后「你改少点吧，四个状态差不多」）：
        /// **默认（不出舌，左下角）+ 沿对角线 3 步** —— 沿 (伸出量, 张开量) 的对角线走，
        /// 所以在一根轴上就是 4 个状态；"张嘴"由作者烘进每段片段里（舌出来多少、嘴就张多少 = 不穿模）。
        /// ⚠️ 刻度是**占位**（等分）：`tongueOut` 从来没实测过（126 段那批里没有一个舌头动作）⇒ 先等分，
        /// 第一轮「舌头伸出·居中」录完再按实测收圈（同 `Mouth/Open` 的做法）。
        /// </summary>
        private static readonly float[] TongueTicks = { 0f, 0.3333f, 0.6667f, 1f };
        private static readonly float[] Ends = { -1f, 1f };        // 两档
        private static readonly float[] ZeroOne = { 0f, 1f };

        /// <summary>
        /// **`Mouth/Open` 专用刻度：0 / 0.4 / 0.75**（2026-09-27 用户实测定）。
        /// 手机实测"张满"只到 0.75、"半张"在 0.4 附近 ⇒ 把这根轴的圈收进实测范围
        /// （0.75 就是张满，0.9 / 1.0 会被投影到这一行，实测逐位相同）。
        /// ⚠️ **负侧不要**（用户定）：`Open` 的负值（咬唇 −0.14 / 内卷 −0.07）只由"卷唇"驱动，
        /// 而那整族已经搬去 `MouthLipRoll` ⇒ 这里只留正值，负值一律钳到 Y0（= 闭）。
        /// ⚠️ 只给 `MouthCore` 的 Y 用；`Mouth/Jaw`（= 裸 `jawOpen`）**没有实测数据**，仍用 `Unit`。
        /// </summary>
        private static readonly float[] OpenMeasured = { 0f, 0.4f, 0.75f };

        /// <summary>
        /// **`Mouth/Form` 专用刻度：−1 / 0 / 0.75 / 1**（2026-09-27 实测两端 + 2026-09-28 傍晚补的 sad 列）。
        /// ⚠️ **−1 那一列 = 苦**（sad 折进 `Form` 负半轴之后，负侧终于有语义了；用户定「四列，补 3 格」）：
        /// 要画的是「苦 × 闭 / 半张 / 张满」三格（跟正侧的笑那三格对应）。
        /// 「**常态笑**」在 0.75 左右、「**大笑**」才到 1 —— 两个都是真实状态，各要一个采样点。
        /// ⚠️ **负侧现在的归属**（2026-09-28 傍晚重排）：**苦 = 这张表的 −1 列本身**（sad 折进 `Form` 负半轴之后
        /// 负侧终于有语义了 ⇒ 用户定「四列，补 3 格」）；噘 → 倒V 形态（加了 funnel 门，只认"真撅"）；
        /// 卷唇/咬唇 → `MouthCoreRoll` 变体；嘴宽 → `MouthWidth`（1D 3 格）。


        /// 回到干净的 3×3（没有负行/负列，也就没有要挖的死角）。
        /// ⚠️ 只给 `MouthCore` 的 X 用（`MouthWidth` 2026-09-28 已删 ⇒ 现在没有别的表吃 `Form`）。
        /// </summary>
        private static readonly float[] FormSmile = { -1f, 0f, 0.75f, 1f };

        /// <summary>
        /// 下巴**左右**轴（`JawSide`）自己的三档（2026-09-27 用户定「还是六个动画，只不过把最大值弄到
        /// 0.65 左右」）。⚠️ 原来的 `Two`（±1）是**没实测就占位**的：实测「微张、解放咬颌」那个状态下
        /// 单侧只到 **0.52**（咬紧时只有 0.02–0.04，而"只挤嘴角"的伪影反而有 0.05–0.2）⇒ 用 ±1 的话
        /// 真动作只把 X 推到 52%，作者画的左右那两格永远吃不饱。改成 ±0.65：0.52 ⇒ 80%。
        /// </summary>
        private static readonly float[] JawSide = { -0.65f, 0f, 0.65f };

        /// <summary>
        /// 下巴竖直轴（`Mouth/Jaw`）的两档：**0 = 闭 / 咬合**、**+0.75 = 张满**（待标定）。
        /// ⚠️ 这两个数组必须声明在 `Tables` **之前** —— C# 静态字段按声明顺序初始化，
        /// 放在后面的话 `Tables` 构造时读到的还是 `null`（真栽过一次：探针报 NullReference）。
        /// </summary>
        private static readonly float[] JawOpen = { 0f, 0.75f };

        /// <summary>
        /// **2026-09-28 当天三版之后的刻度** —— 上午删 2D 的 `MouthWidth`（§5.7.22）、下午建整嘴平移 +
        /// 嘴角 3×3（§5.7.23）、傍晚按用户判定收成「**整嘴平移 3×2 + 嘴宽 1D 3 格**」（§5.7.24）。
        /// · `Mouth/X`（整嘴左右平移）：刻度 **±0.95**（整嘴右移 `mouthLeft` 0.963~0.967、左移 `mouthRight`
        ///   0.967~0.976 ⇒ 满档 0.96）；曲线里带 **±0.20 死区**（撇嘴只有 0.047~0.136）。
        /// · `Mouth/Y`（整嘴上下）：**只有两档 0 / +1** —— 负侧（闭唇下颌下拉）在真机上到不了 / 被咀嚼抢，
        ///   用户实测「shift 根本不会往下移动，只有 6 个点的状态」⇒ 跟 `MouthJaw` 的 Y 一样钳到 0。
        /// · `MouthWidth`（1D 3 格，轴 = **既有的** `Mouth/Pucker`）：**−0.95 收嘴不撅（窄）/ −0.16 静态（中）/
        ///   +0.95 抿嘴嘴宽（宽）**（9 段补录实测；另一根候选 `(press+shrug)/2` 是反的、弃用）。
        /// ⚠️ 槽位名按**索引**编（`A3X&lt;i&gt;Y&lt;j&gt;`）⇒ 挪刻度**不改名、不新增片段**（命名权威 §5）；
        /// 但**换轴（token）会换槽位名** ⇒ 旧片段变孤儿、由生成器的孤儿清理删掉。
        /// ⚠️ `MouthCore` 现在是**四列**（Form = −1 / 0 / 0.75 / 1）：−1 那一列 = 苦（要画 苦×闭/半张/张满），
        /// 挖掉的那格索引从 `(1,2)` 变成 **`(2,2)`**。
        /// </summary>
        private static readonly float[] ShiftXMeasured = { -0.6f, 0f, 0.6f };
        /// <summary>
        /// **整嘴平移的 Y 只有两档**（2026-09-28 傍晚用户实测「shift 根本不会往下移动，只有 6 个点的状态」）：
        /// 下移那一档**不存在**（`Mouth/Y` 的负半边 = 闭唇下颌下拉，实测在真机上到不了 / 被咀嚼抢），
        /// 所以跟 `MouthJaw` 的 Y 一个处理：**负侧一律钳到 0 那一档** ⇒ 3×2 = 6 格。
        /// </summary>
        private static readonly float[] ShiftYMeasured = { 0f, 1f };

        /// <summary>
        /// **`MouthShift` 挖掉的 2 格**（2026-09-28 深夜，用户问「同理 shift 的两个角点是不是也能删了」⇒ 对）：
        /// 删的是 **`(0,1) 左上` / `(2,1) 右上`**（"上移 × 侧移"），**不是**下面那两个。
        /// 理由（跟 jaw 的「咬合 × 侧偏」同一类 —— 次要维的信号只存在于主要的某一档）：
        /// `Mouth/Y` 的正侧是**噘嘴判据**（要 `mouthPucker ≥ 0.45`），而「整嘴平移」那几段实测
        /// `mouthPucker` 只有 **0.22~0.37** ⇒ Y = 0；反过来噘嘴时嘴是**居中**的（`dimple` 0.072 ⇒ `Mouth/X` ≈ 0）
        /// ⇒「上移 × 侧移」这个组合在设备上到不了 ✓
        /// ⇒ 剩 **4 格** = 下排三点（左 / 中 / 右）+ 上中一点（T 形）；"上移+侧移"的输入会被投影到 T 的两条斜边
        /// ⇒ 得到"一半侧移 + 一半上移"的插值（圈外点投影到圈边做凸组合，见 BLEND_TREE_LIMITS §9）。
        /// ⚠️ **不能删下面那两个**：下排左/右就是「整嘴左右移动」本体（实测 `mouthLeft/Right` 0.963~0.976）。
        /// </summary>
        private static readonly Vector2Int[] MouthShiftSkip = { new Vector2Int(0, 1), new Vector2Int(2, 1) };
        /// <summary>
        /// **`MouthWidth`（1D 3 格，2026-09-28 傍晚重返）** 的刻度 —— 轴就是既有的 `Mouth/Pucker`
        /// （= `2×dimple − pucker`）。9 段补录实测的**原值**：`−0.93 收嘴不撅（窄）· −0.09 静态（中）· +2.0 抿嘴嘴宽（宽）`。
        /// ⭐ **2026-09-29 归一化到 0…1**（用户定：轴不能为负、满端填 1）：中间层那条曲线把 −0.93…+2.0
        /// **分段线性**映到 0…1（拐点仍落在中刻度上）⇒ 形态行为逐段等价，只是刻度数字变成 `0 / 0.5 / 1`。
        /// ⚠️ 原轴是 `2×(dimpleL + dimpleR) − pucker`（**2×和**，不是 2×均值）⇒ 归一化前宽端量纲是 2
        /// （抿嘴嘴宽 4×0.525 − 0.104 = 1.996）。笑 0.78 / 撇嘴 0.815 落在中偏宽。
        /// ⚠️ 另一根候选 `(press + shrug) / 2` 把「收嘴不撅」排在静态**之下**（0.09 &lt; 0.17）⇒ 反的、弃用。
        /// </summary>
        private static readonly float[] MouthWidthMeasured = { 0f, 0.5f, 1f };

        /// <summary>鼻子上顶的两档：不顶 / 顶。⭐ 顶 = **1**（2026-09-29 用户定；实测 0.70/0.75 只是"平均/最大"，刻度取 1 让满档干净）。</summary>
        private static readonly float[] NoseUpTicks = { 0f, 1f };

        /// <summary>
        /// **`MouthCore` 挖掉的一格**（2026-09-27 用户定）：顶行中间 `(Form 0.75 × Open 0.75)` —— 现在 Form 是
        /// 四档（−1 / 0 / 0.75 / 1）⇒ 索引相应地是 **`(2, 2)`**（不是旧的三档时的 `(1, 2)`）。
        /// 嘴张到最大时**半笑与全笑分辨不出来** ⇒ 这一格没有独立语义。
        /// 顶行左右两个角**留着**（"张嘴不笑" 与 "大笑张嘴" 都是真实状态），
        /// 而且挖掉的是**矩形内部**的一点、不是圈上的角 ⇒ 表仍是完整矩形，出界照旧是干净的钳制。
        /// </summary>
        private static readonly Vector2Int[] MouthCoreSkip = { new Vector2Int(2, 2) };

        /// <summary>
        /// **`MouthCore` 的 8 个点：手工拉的自由点集**（2026-09-27 用户第二次改，从资产读回来的真值）。
        /// 他改的是 X1 / X2 那两列（笑那侧），X0 那列没动：
        /// <code>
        ///   A3X0Y0 (0,0)          A3X1Y0 (0.482,-0.017)   A3X2Y0 (0.907,-0.024)
        ///   A3X0Y1 (0,0.4)        A3X1Y1 (0.47,0.336)     A3X2Y1 (0.968,0.267)
        ///   A3X0Y2 (0,0.75)       （A3X1Y2 挖掉）          A3X2Y2 (0.926,0.596)
        /// </code>
        /// ⇒ `FormSmile` / `OpenMeasured` 降级成**索引骨架**（槽位名里只带 X 的档数）。
        /// ⚠️ **变体表 `MouthCoreRoll` 用的是同一份 Override** ⇒ 两个生成器天然同步；
        ///    但**资产是用户手动改的**，所以每次同步完都要把主版那 8 个坐标**镜像到猫嘴版**
        ///    （检查器有一条"变体 / 副本必须与主版逐点一致"的硬检查）。
        /// ⚠️ 他以后再拉点：**先读资产、再改这里**，不要反过来。
        /// </summary>
        private static readonly CellPos[] MouthCoreOverride =
        {
            // 苦那一列（Form −1）：2026-09-28 傍晚新增，坐标与 X0 列对称（作者可再拉）
            new CellPos { I = 0, J = 0, X = -0.5f, Y = 0f },
            new CellPos { I = 0, J = 1, X = -0.5f, Y = 0.4f },
            new CellPos { I = 0, J = 2, X = -0.5f, Y = 0.75f },
            // 原来那 8 个（用户手拉的真值；索引整体 +1，因为左边多了一列苦）
            new CellPos { I = 1, J = 0, X = 0f, Y = 0f },
            new CellPos { I = 2, J = 0, X = 0.482f, Y = -0.017f },
            new CellPos { I = 3, J = 0, X = 0.907f, Y = -0.024f },
            new CellPos { I = 1, J = 1, X = 0f, Y = 0.4f },
            new CellPos { I = 2, J = 1, X = 0.47f, Y = 0.336f },
            new CellPos { I = 3, J = 1, X = 0.968f, Y = 0.267f },
            new CellPos { I = 1, J = 2, X = 0f, Y = 0.75f },
            new CellPos { I = 3, J = 2, X = 0.926f, Y = 0.596f }
        };

        // ⚠️ **`MouthJaw` 的"6 个手拉自由点"已退役**（2026-09-28 深夜用户判定：「左右两个点完全没必要」）。
        //    那些点是 2026-09-27 用户在 Animator 窗口里一个一个拉的（从资产读回来的真值，留档）：
        //      A3X0Y0 (-0.342,-0.015)   A3X1Y0 (0,0)           A3X2Y0 (0.34,-0.01)
        //      A3X0Y1 (-0.541, 0.374)   A3X1Y1 (0.006, 0.902)  A3X2Y1 (0.534, 0.354)
        //    为什么删得掉：① 下巴左右**只在"微张 / 解放咬颌"时才到 0.52**，咬紧时只有 0.02~0.04
        //    （"只挤嘴角"的伪影反而 0.05~0.2）⇒「咬合 × 侧偏」那两格物理上到不了；② 剩下的左右两点
        //    意味着"张满·中"要靠左右 50/50 混，或者牺牲 Y 的分辨率 —— 用户判定不值 ⇒ **下巴只留上下两档**。
        //    `Ho/Drive/Mouth/JawSide` 照旧发布（出口；以后想要回来再建一张 2D 表 + 手拉点）。

        private static readonly TableSpec[] Tables =
        {
            new TableSpec { Name = "MouthCore", X = "Ho/Drive/Mouth/Form", Y = "Ho/Drive/Mouth/Open", XToken = "Form", YToken = "Open", XValues = FormSmile, YValues = OpenMeasured, Skip = MouthCoreSkip, Override = MouthCoreOverride },
            // ⚠️ **下巴 2026-09-28 深夜从 2D 6 格降成 1D 2 格**（只留上下）—— 见下面的 `Simple1DTables`。

            // 整嘴平移（2026-09-28 下午新建，当晚从 3×2 收成 **4 格 T 形**）：X = 左右（±0.95）·
            // Y = 上下（0 / +1，负侧钳到 0）；**上移的左右两角挖掉**（`MouthShiftSkip`，见其注释）。
            new TableSpec { Name = "MouthShift", X = "Ho/Drive/Mouth/X", Y = "Ho/Drive/Mouth/Y", XToken = "LeftRight", YToken = "UpDown", XValues = ShiftXMeasured, YValues = ShiftYMeasured, Skip = MouthShiftSkip },
            // ⭐ **舌头是 1D 4 格，不是 2D 表** —— 见下面的 `Simple1DTables`。
            //    用户 2026-09-28 深夜四句话定形：①「其实也只需要一个 1d 树分三段就行了吧」→
            //    ②「不行我感觉还是要五点」→ ③「5 点不是你想的那样，我想的是**默认态在左下角**，的四点」→
            //    ④「你改少点吧，四个状态差不多」= **默认（左下角）+ 沿对角线 3 步**：姿势序列沿
            //    (伸出量, 张开量) 的对角线走（舌出来多少、嘴就张多少 ⇒ 不穿模），所以在树上就是
            //    **一根轴（伸出量）上的 4 个状态**，"张嘴"由作者**烘进每一段片段**里。
            //    ⚠️ 别建成 2D 表：① 那些点在 2D 里**共线**（实测共线/退化点集不可预测、会出负权重）；
            //       ② 把 `jawOpen` 当第二根轴会让"只张嘴不伸舌"把舌头也带出来 ✗。
            // ⚠️ 注视两棵树删了（2026-09-27：朝向交给 Warudo 的 LookAt + IK）；4 根轴照旧发布当出口
            // ⚠️ 颊那两棵 **ARKit** 树 + 鼻那棵 2D 表都删了（2026-09-27）：颊（`cheekSquint`）在二次元角色上表现不了；
            //    鼻收成**一个状态**（鼻子上顶）⇒ 挪到下面的 1D 片段表（`NoseUp`）。
            // 颊轴（2026-09-27 加）：**鼓嘴**形态用的 2D 表 —— X = 左颊 / Y = 右颊，各两档 = 4 格。
            // ⚠️ 与上面删掉的那两棵**不是一回事**：它们吃 ARKit 的 `cheekSquint`，这张吃
            //    `Ho/Drive/Cheek/Left|Right/Puff`（两条**鼓嘴颊轴**）—— 单边鼓时嘴唇被推过去（`mouthLeft/Right`
            //    0.67~0.87）把左右分开，双鼓时它们 ≈0.03、靠 `cheekPuff` 同时点亮两侧。姿势还没画（空片段）。
            new TableSpec { Name = "Cheek", X = "Ho/Drive/Cheek/Left/Puff", Y = "Ho/Drive/Cheek/Right/Puff", XToken = "PuffL", YToken = "PuffR", XValues = ZeroOne, YValues = ZeroOne }
        };

        /// <summary>
        /// **`Mouth/Roll` 的两个阈值：0.15 / 0.30**（2026-09-28 实测重定，A 批 24 段 + B 批 12 段）。
        /// 这根轴的判据那一次也一起换了（见 profile 里 `Ho/Drive/Style/CatMouth` 那一行）：
        /// `下唇卷 × clamp((嘴角方向 − 0.12)/0.10, 0, 1)`，所以**读数的量纲也变了** ——
        /// 猫嘴三段实测 **0.434~0.460**（全过满档），最坏的非目标（常态笑：嘴角门开着但下唇几乎不卷）
        /// **0.075**（离起点还有 2 倍）⇒ `0.15` 是起点、`0.30` 是满档。
        /// ⚠️ **膝 0.20 → 0.12**（2026-09-28）：用户实机报数 —— 做住 / 张嘴 / 大张 的嘴角方向
        /// 0.50 / 0.25 / 0.17，膝 0.20 时后两档只拿到门 0.5 / 0 ⇒ 读数 0.05 / 0，擦着释放线 0.02
        /// ⇒「张嘴立刻就掉回去」；膝 0.12 后三档是 0.40 / 0.10 / 0.044，而最坏非目标（用力说话 0.096）仍在膝下。
        /// ⚠️ 旧的 `0.02 / 0.12` 是给"√(上×下) × 死区 × 下颌增益"那套旧读数配的，**不能再用**。
        /// ⚠️ 1D 的阈值必须**显式写死**（`m_UseAutomaticThresholds: 0`）：自动模式会忽略存下来的值、
        /// 在 `[m_MinThreshold, m_MaxThreshold] = [0,1]` 上把两档平摊成 0 与 1 ⇒ 静默错位。
        /// </summary>
        private static readonly float[] RollTicks = { 0.15f, 0.30f };

        /// <summary>
        /// 1D 片段表（一根轴、孩子是动画片段）。**这是那套机制的第一个正式用户**（2026-09-27）。
        /// ⚠️ 2026-09-27 起**只乘 `NoseUp` 一张**：下巴前伸那棵按用户决定删掉了
        /// （「把这个 arkit 输入贬成只用来辅助的变量，他还是参与 arkit 直通就行，我们直接不做这个轴了」）
        /// —— `Ho/Drive/Mouth/Forward` 那一行照样发布（辅助变量/出口），只是没有树消费它。
        /// </summary>
        private static readonly Simple1DSpec[] Simple1DTables =
        {
            // 鼻子上顶（用户定：颊不要、鼻只留这一个状态）—— 只要"不顶 / 顶"两格。
            new Simple1DSpec { Name = "NoseUp", Parameter = "Ho/Drive/Nose/Up", Token = "Up", Values = NoseUpTicks },
            // 倒V（2026-09-27 加）：**一个固定姿势** —— 形态门在中间层是"维持"（迟滞）出来的 0/1
            // ⇒ 两档阈值就是 0 / 1。⚠️ 它吃的是**契约行** `Ho/Drive/Style/InvertedV`：
            // 控制器看不见中间层的内部行 `Ho/Style/InvertedV`，中间层把它的值转发成这个控制器参数。
            new Simple1DSpec { Name = "InvertedV", Parameter = InvertedVWeight, Token = "InvertedV", Values = ZeroOne },
            // 嘴宽（2026-09-28 傍晚：`MouthWidth` 以 **1D 3 格**回来）—— 轴就是**既有的**
            // `Ho/Drive/Mouth/Pucker`（= `2×dimple − pucker`，本来就在发布、之前没有树消费）。
            // 三档实测：窄 收嘴不撅 / 中 静态 / 宽 抿嘴嘴宽。
            new Simple1DSpec { Name = "MouthWidth", Parameter = "Ho/Drive/Mouth/Pucker", Token = "Pucker", Values = MouthWidthMeasured },
            // 舌头（2026-09-28 深夜）：**1D 4 格** —— 默认（不出舌，左下角）→ 3 步 → 舌伸满；
            // 每格自己把该有的张嘴量烘进去（穿模由作者那 4 段负责）。见 `TongueTicks` 的注释。
            // 轴借 `Ho/Drive/Mouth/TongueL`（两根 TongueL/R 本来就是 tongueOut 的占位；做「歪舌头」时
            // 量轴换新的 `TongueOut`，这 5 格的名字与位置都不用动）。
            new Simple1DSpec { Name = "MouthTongue", Parameter = "Ho/Drive/Mouth/TongueL", Token = "Tongue", Values = TongueTicks },
            // 下巴（2026-09-28 深夜：**1D 2 格** —— 咬合/闭 ↔ 张开）。用户判定「左右两个点完全没必要」：
            // 下巴左右只在"微张/解放咬颌"时才到 0.52、咬紧时 0.02~0.04 ⇒ 侧偏那两维不值得占格子；
            // 咀嚼走"咬合"这一档（`Mouth/Jaw` 的 0 档）。`JawOpen` = {0, 0.75} 直接当两档刻度。
            // ⚠️ `Ho/Drive/Mouth/JawSide` 照旧发布（出口，没有树消费）。
            new Simple1DSpec { Name = "MouthJaw", Parameter = "Ho/Drive/Mouth/Jaw", Token = "Jaw", Values = JawOpen }
        };

        /// <summary>
        /// **轴驱动的变体表**（不是按键表情副本）：与主版**同轴、同刻度、同稀疏格**，整套姿势换成变体版。
        /// 数组元素 = { 变体树名, 主版树名 }。名字规则见命名权威 §3：变体 = `<树名><驱动它的轴段词>`。
        /// 2026-09-27：卷唇不再当"残差车道"，改成在两张"笑 × 张"2D 表之间**分叉** ——
        /// 静息嘴（`MouthCore`）↔ 猫嘴版（`MouthCoreRoll`）。这样那个时刻的嘴是**作者画过的两张整嘴表**
        /// 按权重淡入 ⇒ 混态有人负责，而不是几条残差相加。
        /// ⚠️ 代价（用户已认）：变体表被选中时必须是**完整嘴姿势**（WD 开着，没烘的格会让那几根键回默认、嘴塌），
        /// 所以"作者要画整张嘴"，我们不替他拷贝。
        /// </summary>
        private static readonly string[,] Variants =
        {
            { "MouthCoreRoll", "MouthCore" }
        };

        // ⚠️ **2026-09-28 砍过 3 格又撤回了 —— 变体表与主版一样保持 8 格**（用户定）。
        //    当时看图觉得"猫嘴开着时 `Open` 顶行（0.6 / 0.75）与『中性形 + 张嘴』到不了"
        //    （`X0Y1` / `X0Y2` / `X2Y2`），但**常开**（直接写 `Ho/Drive/Style/CatMouth` ≥ 0.30，见
        //    `docs/VTS_HQ_CONTROLLER.md` §2.2）会把嘴钉在猫嘴版上 —— 那时任意 Form × Open 都会被采到，
        //    少一格就是"没烘的那几根键回默认、嘴塌"（WD 开）。用户原话：
        //    「还是要留着，因为如果用户加了常开，那些范围还是会采到的」。
        //    ⚠️ 教训：**"轴上到不了" ≠ "不会被采到"** —— 变体表的钥匙是那根轴，而常开能绕过轴直接把钥匙转到底。

        /// <summary>
        /// 轴驱动的变体开关（1D 树，两个孩子都是整表，阈值显式写死）：
        /// 名字 / 主版 / 变体 / 驱动它的轴 / 两个阈值。
        /// </summary>
        private static readonly VariantSwitchSpec[] VariantSwitches =
        {
            new VariantSwitchSpec { Name = "MouthCoreRollSwitch", Main = "MouthCore", Variant = "MouthCoreRoll", Parameter = "Ho/Drive/Style/CatMouth", Thresholds = RollTicks }
        };

        /// <summary>
        /// 平行副本（按键表情）：主版 → 副本名。
        /// ⚠️ **嘴没有副本**（2026-09-27 用户定）：「按键表情版本身对于嘴张嘴笑没有意义」——
        /// 夸张的笑嘴 = `Form` 更大，轴上已经够得到 ⇒ `MouthCoreExpr` 与 `MouthCoreSwitch` 整个删掉。
        /// 眼/眉的副本照留：它们表达的是轴上到不了的"性质"（笑眼、怒眉）。
        /// ⚠️ 嘴现在的第二张表是**变体**（`MouthCoreRoll`，由轴选），不是副本（由表情门选）——
        /// 两者机制相同但语义不同，别混。
        /// </summary>
        private static readonly string[,] Copies =
        {
        };

        /// <summary>1D 开关：名字 / 主版 / 副本 / 用哪个表情门。</summary>
        private static readonly string[,] Switches =
        {
        };

        /// <summary>
        /// 区域 → 直接挂在它下面的子节点（表名或开关名）。
        /// ⚠️ 嘴这一格挂的是**开关** `MouthCoreRollSwitch`（它下面才是两张整嘴表）。
        /// 这类"删了开关 / 换了层级忘了重挂"会让整张表变孤儿树（树在、槽位名也对，但状态走不到它）
        /// —— 真栽过一次，是片段探针先撞出来的，`Tools~/FaceTracking/check-controller.ps1` 现在也会核可达性。
        /// </summary>
        private static readonly string[,] Regions =
        {
            // ⚠️ `@Style` 后缀 = 这个孩子挂**形态门** `Ho/Drive/Gate/MouthStyle`（默认恒 1 = `Ho/Drive/W/One`）：
            //    倒V / 鼓嘴亮起来时，中间层把门压到 0，于是**整块"张嘴 × 笑"**让位给形态。
            //    `MouthJaw` / `MouthTongue` 与两条形态子树保持恒 1（下巴/舌头跟风格化不冲突；
            //    形态子树本身就是"被门放行的东西"，再挂门就套娃了）。
            { "MouthRegion", "Mouth", "MouthCoreRollSwitch@Style,MouthJaw,MouthShift@Style,MouthWidth@Style,MouthTongue,InvertedV,Cheek" },
            // 2026-09-27：左右眼并成一个区域（注视两棵树没了，每边只剩眼睑开关）；颊 → 鼻（只剩"鼻子上顶"一个状态）
            { "EyeRegion", "Eye", "LidL,LidR" },
            { "NoseRegion", "Nose", "NoseUp" }
        };

        [MenuItem("HoUnityTools/面捕/生成控制器骨架（VTS 原生语义）")]
        private static void Generate()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "生成控制器骨架", "PTP_CTR_Face_VTS", "controller",
                "选一个路径 —— 已存在的同名资产会被覆盖（生成的是骨架，动画要自己填）");
            if (string.IsNullOrEmpty(path)) return;

            string report;
            try
            {
                report = Build(path);
            }
            catch (Exception error)
            {
                Debug.LogError("[Ho 面捕] 生成控制器骨架失败：" + error);
                EditorUtility.DisplayDialog("生成失败", error.Message + "\n\n（详细堆栈见 Console）", "好");
                return;
            }

            Debug.Log("[Ho 面捕] 控制器骨架已生成：\n" + report);
            EditorUtility.DisplayDialog("控制器骨架", report, "好");
        }

        /// <summary>
        /// 在 <paramref name="path"/> 建一份骨架（会删掉同路径的旧资产）并返回摘要。
        /// 单独抽出来是为了让脚本/用例能直接调。
        /// </summary>
        public static string Build(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("没有给输出路径");
            if (!path.EndsWith(".controller", StringComparison.OrdinalIgnoreCase)) path += ".controller";

            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);

            // ── 参数（43 个 = 4 区域门 + 1 形态门 + W/One + 2 表情门 + 30 轴 + 4 切片 + 1 形态权重）──


            var parameters = new List<AnimatorControllerParameter>();
            foreach (string gate in RegionGates) parameters.Add(Float("Ho/Drive/Gate/" + gate, 1f));
            parameters.Add(Float(StyleGate, 1f));                                  // 形态门（默认 1 = 门开着）
            parameters.Add(Float(OneWeight, 1f));                                  // Direct 子节点都要挂权重
            foreach (string gate in ExpressionGates) parameters.Add(Float("Ho/Drive/Gate/Expr/" + gate, 0f));
            parameters.Add(Float(InvertedVWeight, 0f));                            // 倒V 权重（默认 0 = 不是倒V）
            foreach (string axis in Axes) parameters.Add(Float(axis, 0f));
            foreach (string slice in MouthCoreSlices) parameters.Add(Float(slice, 0f));
            foreach (var parameter in parameters) controller.AddParameter(parameter);

            // ── 树 ────────────────────────────────────────────────────────────
            var trees = new Dictionary<string, BlendTree>(StringComparer.Ordinal);
            var slotCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            int clipsCreated = 0, clipsKept = 0;
            // 槽位片段：每个格子一份**以槽位名命名的空 `.anim`**，放在控制器旁边的 `Animations/`。
            // 于是混合树里每格都显示语义名字（不再是 None），"槽位名 = 片段名"从第一天就成立。
            // ⚠️ **已存在的片段绝不覆盖**：作者可能已经把姿势烘进去了；要重铺得手动删文件。
            string clipFolder = ClipFolderFor(path);
            if (!AssetDatabase.IsValidFolder(clipFolder))
            {
                string parent = Path.GetDirectoryName(clipFolder).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(clipFolder));
            }

            foreach (var spec in Tables)
            {
                var tree = NewTree(controller, spec.Name, BlendTreeType.FreeformCartesian2D);
                tree.blendParameter = spec.X;
                tree.blendParameterY = spec.Y;
                tree.useAutomaticThresholds = false;
                int filled = 0;
                for (int j = 0; j < spec.YValues.Length; j++)
                {
                    for (int i = 0; i < spec.XValues.Length; i++)
                    {
                        if (Skipped(spec, i, j)) continue;
                        string slot = SlotName(spec.Name, spec.XToken, spec.YToken, spec.XValues.Length, i, j);
                        tree.AddChild(LeafParts(controller, slot, SlotClip(clipFolder, slot, ref clipsCreated, ref clipsKept)), CellPosition(spec, i, j));
                        filled++;
                    }
                }
                slotCounts[spec.Name] = filled;
                trees[spec.Name] = tree;
            }

            foreach (var copy in AllCopies())
            {
                TableSpec spec = FindTable(copy.Key);
                var tree = NewTree(controller, copy.Value, BlendTreeType.FreeformCartesian2D);
                tree.blendParameter = spec.X;
                tree.blendParameterY = spec.Y;
                tree.useAutomaticThresholds = false;
                for (int j = 0; j < spec.YValues.Length; j++)
                    for (int i = 0; i < spec.XValues.Length; i++)
                    {
                        if (Skipped(spec, i, j)) continue;
                        string slot = SlotName(copy.Value, spec.XToken, spec.YToken, spec.XValues.Length, i, j);
                        tree.AddChild(LeafParts(controller, slot, SlotClip(clipFolder, slot, ref clipsCreated, ref clipsKept)), CellPosition(spec, i, j));
                    }
                slotCounts[copy.Value] = CountCells(spec);
                trees[copy.Value] = tree;
            }

            // 变体表：与主版**同轴、同刻度、同稀疏格**（整套姿势换成变体版），所以直接复用主版的 spec
            for (int v = 0; v < Variants.GetLength(0); v++)
            {
                TableSpec spec = FindTable(Variants[v, 1]);
                var tree = NewTree(controller, Variants[v, 0], BlendTreeType.FreeformCartesian2D);
                tree.blendParameter = spec.X;
                tree.blendParameterY = spec.Y;
                tree.useAutomaticThresholds = false;
                for (int j = 0; j < spec.YValues.Length; j++)
                    for (int i = 0; i < spec.XValues.Length; i++)
                    {
                        if (Skipped(spec, i, j)) continue;
                        string slot = SlotName(Variants[v, 0], spec.XToken, spec.YToken, spec.XValues.Length, i, j);
                        tree.AddChild(LeafParts(controller, slot, SlotClip(clipFolder, slot, ref clipsCreated, ref clipsKept)), CellPosition(spec, i, j));
                    }
                slotCounts[Variants[v, 0]] = CountCells(spec);
                trees[Variants[v, 0]] = tree;
            }

            foreach (var spec in Simple1DTables)
            {
                var tree = NewTree(controller, spec.Name, BlendTreeType.Simple1D);
                tree.blendParameter = spec.Parameter;
                tree.useAutomaticThresholds = false;
                for (int i = 0; i < spec.Values.Length; i++)
                {
                    string slot = SlotName1D(spec.Name, spec.Token, spec.Values.Length, i);
                    tree.AddChild(LeafParts(controller, slot, SlotClip(clipFolder, slot, ref clipsCreated, ref clipsKept)), spec.Values[i]);
                }
                slotCounts[spec.Name] = spec.Values.Length;
                trees[spec.Name] = tree;
            }

            // Direct 张量积表：孩子 = 具名状态点，各自挂一个门参数（`AttachDirect` 跟区域那套是同一个 API）
            for (int d = 0; d < DirectTables.Length; d++)
            {
                DirectTableSpec spec = DirectTables[d];
                var tree = NewTree(controller, spec.Name, BlendTreeType.Direct);
                for (int i = 0; i < spec.Cells.Length; i++)
                {
                    string slot = spec.Name + "__Gate__" + spec.Cells[i];
                    AttachDirect(tree, LeafParts(controller, slot, SlotClip(clipFolder, slot, ref clipsCreated, ref clipsKept)),
                                 "Ho/Drive/Lid/" + spec.Side + "/Gate/" + spec.Cells[i]);
                }
                slotCounts[spec.Name] = spec.Cells.Length;
                trees[spec.Name] = tree;
            }



            for (int s = 0; s < Switches.GetLength(0); s++)
            {
                var tree = NewTree(controller, Switches[s, 0], BlendTreeType.Simple1D);
                tree.blendParameter = "Ho/Drive/Gate/Expr/" + Switches[s, 3];
                tree.useAutomaticThresholds = false;
                tree.AddChild(trees[Switches[s, 1]], 0f);   // 阈值 0 = 普通版
                tree.AddChild(trees[Switches[s, 2]], 1f);   // 阈值 1 = 按键表情版
                trees[Switches[s, 0]] = tree;
            }

            // 轴驱动的变体开关：两个孩子都是整表，阈值 = 该轴上的实测档位（显式写死，别让 Unity 平摊）
            foreach (var spec in VariantSwitches)
            {
                var tree = NewTree(controller, spec.Name, BlendTreeType.Simple1D);
                tree.blendParameter = spec.Parameter;
                tree.useAutomaticThresholds = false;
                tree.AddChild(trees[spec.Main], spec.Thresholds[0]);
                tree.AddChild(trees[spec.Variant], spec.Thresholds[1]);
                trees[spec.Name] = tree;
            }

            for (int r = 0; r < Regions.GetLength(0); r++)
            {
                var tree = NewTree(controller, Regions[r, 0], BlendTreeType.Direct);
                foreach (string entry in Regions[r, 2].Split(','))
                {
                    // `名字` = 恒 1；`名字@Style` = 挂形态门（见 Regions 那段注释）
                    string child = entry, weight = OneWeight;
                    int at = entry.IndexOf('@');
                    if (at >= 0)
                    {
                        child = entry.Substring(0, at);
                        string tag = entry.Substring(at + 1);
                        if (tag != "Style") throw new InvalidOperationException("不认识的区域权重标记：" + tag);
                        weight = StyleGate;
                    }
                    AttachDirect(tree, trees[child], weight);
                }
                trees[Regions[r, 0]] = tree;
            }

            var root = NewTree(controller, RootTreeName, BlendTreeType.Direct);
            for (int r = 0; r < Regions.GetLength(0); r++)
                AttachDirect(root, trees[Regions[r, 0]], "Ho/Drive/Gate/" + Regions[r, 1]);
            trees[RootTreeName] = root;

            // ── 层与状态（一层、一个状态、WD 开）─────────────────────────────────
            var layers = controller.layers;
            layers[0].name = LayerName;
            controller.layers = layers;
            var state = controller.layers[0].stateMachine.AddState(StateName);
            state.writeDefaultValues = true;
            state.motion = root;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            int slots = 0;
            var perTree = new StringBuilder();
            foreach (var pair in slotCounts)
            {
                slots += pair.Value;
                perTree.Append("  ").Append(pair.Key).Append(" ").Append(pair.Value).Append(" 格\n");
            }

            return "路径：" + path + "\n"
                + "参数 " + parameters.Count + " 个（区域门 " + RegionGates.Length + " + 形态门 1 + W/One 1 + 表情门 "
                + ExpressionGates.Length + " + 轴 " + Axes.Length + " + 切片 " + MouthCoreSlices.Length + " + 形态权重 1）\n"
                + "树 " + trees.Count + " 棵（根 1 + 区域 " + Regions.GetLength(0) + " + 表 " + Tables.Length + " + 1D 表 " + Simple1DTables.Length + " + 变体 " + Variants.GetLength(0)
                + " + 副本 " + Copies.GetLength(0) + " + 开关 " + (Switches.GetLength(0) + VariantSwitches.Length) + "）\n"
                + "槽位 " + slots + " 个（片段：" + clipFolder + "，新建 " + clipsCreated + " · 保留已有 " + clipsKept + "）：\n" + perTree
                + "核对：`Tools~/FaceTracking/check-controller.ps1 -Path <这份>`（参数名/默认值/树形/槽位名与坐标/门控接线一次核完）";
        }

        /// <summary>槽位片段放哪：控制器同级的 `Animations/`。</summary>
        private static string ClipFolderFor(string controllerPath)
        {
            return Path.GetDirectoryName(controllerPath).Replace('\\', '/') + "/Animations";
        }

        /// <summary>这张表挖掉这个格子吗（稀疏表）。</summary>
        private static bool Skipped(TableSpec spec, int i, int j)
        {
            if (spec.Skip == null) return false;
            foreach (var cell in spec.Skip) if (cell.x == i && cell.y == j) return true;
            return false;
        }

        /// <summary>这张表实际有多少格。</summary>
        private static int CountCells(TableSpec spec)
        {
            int count = 0;
            for (int j = 0; j < spec.YValues.Length; j++)
                for (int i = 0; i < spec.XValues.Length; i++)
                    if (!Skipped(spec, i, j)) count++;
            return count;
        }

        /// <summary>槽位名 = `<树名>__<X段词>__<Y段词>__A<X刻度数>X<i>Y<j>`（见命名权威 §5）。</summary>
        private static string SlotName(string tree, string xToken, string yToken, int xCount, int i, int j)
        {
            return tree + "__" + xToken + "__" + yToken + "__A" + xCount + "X" + i + "Y" + j;
        }

        /// <summary>
        /// 这一格摆在哪个坐标：默认 = 该列的 X 刻度 × 该行的 Y 刻度；
        /// 有逐格覆盖（实测定"这一格不在刻度值上"）时用覆盖值 —— 槽位名仍是按索引编的，所以挪坐标不改名。
        /// </summary>
        private static Vector2 CellPosition(TableSpec spec, int i, int j)
        {
            if (spec.Override != null)
                foreach (var cell in spec.Override)
                    if (cell.I == i && cell.J == j) return new Vector2(cell.X, cell.Y);
            return new Vector2(spec.XValues[i], spec.YValues[j]);
        }

        /// <summary>1D 槽位名 = `<树名>__<轴段词>__A<刻度数>X<i>`（见命名权威 §5 的一维写法）。</summary>
        private static string SlotName1D(string tree, string token, int count, int i)
        {
            return tree + "__" + token + "__A" + count + "X" + i;
        }

        /// <summary>
        /// 取槽位片段：没有就建一份**空**的（名字即语义），已有就原样用 —— **绝不覆盖**已烘好的姿势。
        /// </summary>
        private static AnimationClip SlotClip(string folder, string slot, ref int created, ref int kept)
        {
            string clipPath = folder + "/" + slot + ".anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (existing != null) { kept++; return existing; }

            var clip = new AnimationClip { name = slot };
            AssetDatabase.CreateAsset(clip, clipPath);
            created++;
            return clip;
        }

        private static AnimatorControllerParameter Float(string name, float value)
        {
            return new AnimatorControllerParameter
            {
                name = name,
                type = AnimatorControllerParameterType.Float,
                defaultFloat = value
            };
        }

        private static BlendTree NewTree(AnimatorController controller, string name, BlendTreeType type)
        {
            var tree = new BlendTree { name = name, blendType = type, useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(tree, controller);   // 树是控制器的子资产，不加进去存不下来
            return tree;
        }

        private static void AttachDirect(BlendTree parent, Motion child, string parameter)
        {
            parent.AddChild(child);
            var children = parent.children;
            children[children.Length - 1].directBlendParameter = parameter;
            parent.children = children;
        }

        private static TableSpec FindTable(string name)
        {
            foreach (var spec in Tables) if (spec.Name == name) return spec;
            throw new InvalidOperationException("没有这张表的定义：" + name);
        }

        private static IEnumerable<KeyValuePair<string, string>> AllCopies()
        {
            for (int i = 0; i < Copies.GetLength(0); i++)
                yield return new KeyValuePair<string, string>(Copies[i, 0], Copies[i, 1]);
        }
    }
}
