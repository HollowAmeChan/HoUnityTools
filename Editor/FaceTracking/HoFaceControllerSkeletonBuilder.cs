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
    /// **生成「VTS 原生语义」控制器的骨架**（2026-09-27）：40 个参数 + 29 棵树 + 104 个**空槽位**，
    /// 一层 `Ho/00 Drive`、一个状态 `Drive`（Write Defaults 开）、无 Behaviour、无 clip —— 动画由作者后面填。
    ///
    /// 形状与"现在落到哪"记在 `docs/VTS_HQ_CONTROLLER.md`：**§2.1 结构、§2.2 每格叶子的语义**
    /// （根 Direct → 5 个区域 Direct → 2D 表 / 1D 副本树），槽位名按 `docs/FACE_TRACKING_NAMING.md` §5。
    ///
    /// **为什么有这个菜单项**：
    /// ① 手搭 29 棵树不现实、也不可复现；
    /// ② 2026-09-27 那份 `PTP_CTR_Face_VTS.controller` 是用文本生成器写出来的（抄老控制器的字段集），
    ///    当时**没能在 Unity 里打开验证**（本机编辑器占着授权互斥量、跑不了 batchmode）——
    ///    这个菜单项就是"用 Unity 自己的 API 重新建一份"的兜底；
    /// ③ 设计改动（加树、改轴、加槽位）之后从这里重新生成，而不是去手改资产。
    ///
    /// ⚠️ 形状表（哪棵树混哪两根轴、哪些槽位、谁挂哪个门）在这里是**硬编码**的，跟着设计稿走；
    /// 生成出来的资产用 `.research/check-controller.ps1` 对着**发货 profile** 反推的期望集核一遍。
    /// </summary>
    public static class HoFaceControllerSkeletonBuilder
    {
        private const string RootTreeName = "Ho/00 Drive Tree";
        private const string LayerName = "Ho/00 Drive";
        private const string StateName = "Drive";
        private const string OneWeight = "Ho/Drive/W/One";

        private static readonly string[] RegionGates = { "Mouth", "EyeLeft", "EyeRight", "Brow", "Cheek" };

        /// <summary>轴参数（部位/轴 → 名字）：31 根，见设计稿 §3.1。</summary>
        private static readonly string[] Axes =
        {
            "Ho/Drive/Mouth/Form", "Ho/Drive/Mouth/Open", "Ho/Drive/Mouth/Funnel", "Ho/Drive/Mouth/Press",
            "Ho/Drive/Mouth/Jaw", "Ho/Drive/Mouth/Forward", "Ho/Drive/Mouth/Pucker", "Ho/Drive/Mouth/X",
            "Ho/Drive/Mouth/TongueL", "Ho/Drive/Mouth/TongueR",
            // 嘴角（选项 C）：把"嘴角笑/苦"从 Form 的负侧分出来，专供 MouthCorner 表（合同时 HQSmileFrownLeft/Right）
            "Ho/Drive/Mouth/CornerL", "Ho/Drive/Mouth/CornerR",
            // 卷唇（2026-09-27）：上下两根线**一起增减** ⇒ 中间层平均成一根 `Mouth/Roll`，表也跟着变成 1D。
            "Ho/Drive/Mouth/Roll",
            "Ho/Drive/Lid/Left/BlinkWide", "Ho/Drive/Lid/Left/Squint",
            "Ho/Drive/Lid/Right/BlinkWide", "Ho/Drive/Lid/Right/Squint",
            "Ho/Drive/Gaze/Left/X", "Ho/Drive/Gaze/Left/Y", "Ho/Drive/Gaze/Right/X", "Ho/Drive/Gaze/Right/Y",
            "Ho/Drive/Brow/Left/Y", "Ho/Drive/Brow/Left/InnerUp", "Ho/Drive/Brow/Right/Y", "Ho/Drive/Brow/Right/InnerUp",
            "Ho/Drive/Cheek/Left/Squint", "Ho/Drive/Cheek/Right/Squint",
            "Ho/Drive/Cheek/Left/Puff", "Ho/Drive/Cheek/Right/Puff",
            "Ho/Drive/Nose/Left/Sneer", "Ho/Drive/Nose/Right/Sneer"
        };

        /// <summary>MouthCore 的条件切片权重（Funnel × Press 双线性；骨架里还没有切片表，参数先建好）。</summary>
        private static readonly string[] MouthCoreSlices =
        {
            "Ho/Drive/Slice/MouthCore/Funnel0Press0", "Ho/Drive/Slice/MouthCore/Funnel1Press0",
            "Ho/Drive/Slice/MouthCore/Funnel0Press1", "Ho/Drive/Slice/MouthCore/Funnel1Press1"
        };

        /// <summary>表情门（默认 0，**中间层不写**：留给驱动"按键"的那一方）。</summary>
        private static readonly string[] ExpressionGates = { "Smile", "Angry" };

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
        private static readonly float[] Unit = { 0f, 0.5f, 1f };   // 单端轴：0 / 半 / 满
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
        /// **`Mouth/Form` 专用刻度：0 / 0.75 / 1**（2026-09-27 用户实测定）。
        /// 「**常态笑**」在 0.75 左右、「**大笑**」才到 1 —— 两个都是真实状态，各要一个采样点。
        /// ⚠️ **负侧不要**（用户定）：`Form` 的负侧混了三件事，全部搬走 —— 苦 → `MouthCorner`（嘴角）、
        /// 噘 → `MouthWidth`、卷唇/咬唇 → `MouthLipRoll`。于是这张表**只管"笑 × 张嘴"两块正值**，
        /// 回到干净的 3×3（没有负行/负列，也就没有要挖的死角）。
        /// ⚠️ 只给 `MouthCore` 的 X 用；`MouthWidth` 的 X/Y 仍是 `Two`（没有实测数据）。
        /// </summary>
        private static readonly float[] FormSmile = { 0f, 0.75f, 1f };

        /// <summary>
        /// **`MouthCore` 挖掉的一格**（2026-09-27 用户定）：顶行中间 `(Form 0.75 × Open 0.75)`。
        /// 嘴张到最大时**半笑与全笑分辨不出来** ⇒ 这一格没有独立语义。
        /// 顶行左右两个角**留着**（"张嘴不笑" 与 "大笑张嘴" 都是真实状态），
        /// 而且挖掉的是**矩形内部**的一点、不是圈上的角 ⇒ 表仍是完整矩形，出界照旧是干净的钳制。
        /// </summary>
        private static readonly Vector2Int[] MouthCoreSkip = { new Vector2Int(1, 2) };

        /// <summary>
        /// **`MouthCore` 挪过位的那一格**（2026-09-27 用户实测定）：右上角 `(大笑 × 张满)`。
        /// 实测"**大笑张嘴时嘴会收缩**"⇒ `Open` 到不了 0.75，摆 **0.6** 才是那个状态的真实位置。
        /// ⚠️ 只挪**这一个点**（不是整行）：静息张嘴仍是 0.75、常态笑张嘴那格已挖掉。
        /// 于是顶边从平线变成 **0.6 ↔ 0.75 的斜线** —— 圈仍是凸的，出界照旧投影到圈边（不外推），
        /// 代价是 `(Form 0.75…1, Open > 0.6)` 那块会落到这条斜边上（越靠右越接近"大笑张嘴"）。
        /// ⚠️ 槽位名按索引编（`A3X<i>Y<j>`），所以挪坐标**不改名、不用重烘**。
        /// </summary>
        private static readonly CellPos[] MouthCoreOverride =
        {
            new CellPos { I = 2, J = 2, X = 1f, Y = 0.6f }
        };

        private static readonly TableSpec[] Tables =
        {
            new TableSpec { Name = "MouthCore", X = "Ho/Drive/Mouth/Form", Y = "Ho/Drive/Mouth/Open", XToken = "Form", YToken = "Open", XValues = FormSmile, YValues = OpenMeasured, Skip = MouthCoreSkip, Override = MouthCoreOverride },
            new TableSpec { Name = "MouthJaw", X = "Ho/Drive/Mouth/Jaw", Y = "Ho/Drive/Mouth/Forward", XToken = "Jaw", YToken = "Forward", XValues = Unit, YValues = new[] { 0f } },
            new TableSpec { Name = "MouthWidth", X = "Ho/Drive/Mouth/Pucker", Y = "Ho/Drive/Mouth/X", XToken = "Pucker", YToken = "LeftRight", XValues = Two, YValues = Two },
            // 嘴角（2026-09-27 选项 C）：**残差表** —— 中间那格 = 零修正，所以两轴都用 3 刻度（0 = 静息）
            new TableSpec { Name = "MouthCorner", X = "Ho/Drive/Mouth/CornerL", Y = "Ho/Drive/Mouth/CornerR", XToken = "CornerL", YToken = "CornerR", XValues = Two, YValues = Two },
            new TableSpec { Name = "MouthTongue", X = "Ho/Drive/Mouth/TongueL", Y = "Ho/Drive/Mouth/TongueR", XToken = "TongueL", YToken = "TongueR", XValues = ZeroOne, YValues = ZeroOne },
            new TableSpec { Name = "LidL", X = "Ho/Drive/Lid/Left/BlinkWide", Y = "Ho/Drive/Lid/Left/Squint", XToken = "BlinkWide", YToken = "Squint", XValues = Two, YValues = ZeroOne },
            new TableSpec { Name = "LidR", X = "Ho/Drive/Lid/Right/BlinkWide", Y = "Ho/Drive/Lid/Right/Squint", XToken = "BlinkWide", YToken = "Squint", XValues = Two, YValues = ZeroOne },
            new TableSpec { Name = "GazeL", X = "Ho/Drive/Gaze/Left/X", Y = "Ho/Drive/Gaze/Left/Y", XToken = "InOut", YToken = "UpDown", XValues = Two, YValues = Two },
            new TableSpec { Name = "GazeR", X = "Ho/Drive/Gaze/Right/X", Y = "Ho/Drive/Gaze/Right/Y", XToken = "InOut", YToken = "UpDown", XValues = Two, YValues = Two },
            new TableSpec { Name = "BrowCoreL", X = "Ho/Drive/Brow/Left/Y", Y = "Ho/Drive/Brow/Left/InnerUp", XToken = "Height", YToken = "InnerUp", XValues = Ends, YValues = ZeroOne },
            new TableSpec { Name = "BrowCoreR", X = "Ho/Drive/Brow/Right/Y", Y = "Ho/Drive/Brow/Right/InnerUp", XToken = "Height", YToken = "InnerUp", XValues = Ends, YValues = ZeroOne },
            new TableSpec { Name = "CheekSquint", X = "Ho/Drive/Cheek/Left/Squint", Y = "Ho/Drive/Cheek/Right/Squint", XToken = "CheekL", YToken = "CheekR", XValues = ZeroOne, YValues = ZeroOne },
            new TableSpec { Name = "CheekPuff", X = "Ho/Drive/Cheek/Left/Puff", Y = "Ho/Drive/Cheek/Right/Puff", XToken = "PuffL", YToken = "PuffR", XValues = ZeroOne, YValues = ZeroOne },
            new TableSpec { Name = "NoseSneer", X = "Ho/Drive/Nose/Left/Sneer", Y = "Ho/Drive/Nose/Right/Sneer", XToken = "SneerL", YToken = "SneerR", XValues = ZeroOne, YValues = ZeroOne }
        };

        /// <summary>
        /// **`Mouth/Roll` 的两个阈值：0.02 / 0.18**（2026-09-27 用户实测定）。
        /// `0.18` = 只卷嘴（= 二次元的"猫嘴"）**满档**；实测的咬唇最强 0.45 也停在这里
        /// （用户定「不需要区分两段卷嘴」）。`0.02` = 死区出口。
        /// ⚠️ 这根轴**到不了 1**（实测最强 0.45）⇒ 满档阈值就是 0.18，没有够不着的档。
        /// ⚠️ 1D 的阈值必须**显式写死**（`m_UseAutomaticThresholds: 0`）：自动模式会忽略存下来的值、
        /// 在 `[m_MinThreshold, m_MaxThreshold] = [0,1]` 上把两档平摊成 0 与 1 ⇒ 静默错位。
        /// </summary>
        private static readonly float[] RollTicks = { 0.02f, 0.18f };

        /// <summary>
        /// 1D 片段表（一根轴、孩子是动画片段）：**现在没有表用它**（卷唇改成"变体开关"之后空着）。
        /// 留着是因为一维形状还多（命名权威里的例子 `MouthShrugBase__Shrug__A3X1`），回来时不用重写。
        /// </summary>
        private static readonly Simple1DSpec[] Simple1DTables = { };

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

        /// <summary>
        /// 轴驱动的变体开关（1D 树，两个孩子都是整表，阈值显式写死）：
        /// 名字 / 主版 / 变体 / 驱动它的轴 / 两个阈值。
        /// </summary>
        private static readonly VariantSwitchSpec[] VariantSwitches =
        {
            new VariantSwitchSpec { Name = "MouthCoreRollSwitch", Main = "MouthCore", Variant = "MouthCoreRoll", Parameter = "Ho/Drive/Mouth/Roll", Thresholds = RollTicks }
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
            { "LidL", "LidLExpr" }, { "LidR", "LidRExpr" },
            { "BrowCoreL", "BrowCoreLExpr" }, { "BrowCoreR", "BrowCoreRExpr" }
        };

        /// <summary>1D 开关：名字 / 主版 / 副本 / 用哪个表情门。</summary>
        private static readonly string[,] Switches =
        {
            { "LidLSwitch", "LidL", "LidLExpr", "Smile" },
            { "LidRSwitch", "LidR", "LidRExpr", "Smile" },
            { "BrowCoreLSwitch", "BrowCoreL", "BrowCoreLExpr", "Angry" },
            { "BrowCoreRSwitch", "BrowCoreR", "BrowCoreRExpr", "Angry" }
        };

        /// <summary>
        /// 区域 → 直接挂在它下面的子节点（表名或开关名）。
        /// ⚠️ 嘴这一格挂的是**开关** `MouthCoreRollSwitch`（它下面才是两张整嘴表）。
        /// 这类"删了开关 / 换了层级忘了重挂"会让整张表变孤儿树（树在、槽位名也对，但状态走不到它）
        /// —— 真栽过一次，是片段探针先撞出来的，`.research/check-controller.ps1` 现在也会核可达性。
        /// </summary>
        private static readonly string[,] Regions =
        {
            { "MouthRegion", "Mouth", "MouthCoreRollSwitch,MouthJaw,MouthWidth,MouthCorner,MouthTongue" },
            { "EyeLeftRegion", "EyeLeft", "LidLSwitch,GazeL" },
            { "EyeRightRegion", "EyeRight", "LidRSwitch,GazeR" },
            { "BrowRegion", "Brow", "BrowCoreLSwitch,BrowCoreRSwitch" },
            { "CheekRegion", "Cheek", "CheekSquint,CheekPuff,NoseSneer" }
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

            // ── 参数（43 个）───────────────────────────────────────────────────
            var parameters = new List<AnimatorControllerParameter>();
            foreach (string gate in RegionGates) parameters.Add(Float("Ho/Drive/Gate/" + gate, 1f));
            parameters.Add(Float(OneWeight, 1f));                                  // Direct 子节点都要挂权重
            foreach (string gate in ExpressionGates) parameters.Add(Float("Ho/Drive/Gate/Expr/" + gate, 0f));
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
                        tree.AddChild(SlotClip(clipFolder, slot, ref clipsCreated, ref clipsKept), CellPosition(spec, i, j));
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
                        tree.AddChild(SlotClip(clipFolder, slot, ref clipsCreated, ref clipsKept), CellPosition(spec, i, j));
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
                        tree.AddChild(SlotClip(clipFolder, slot, ref clipsCreated, ref clipsKept), CellPosition(spec, i, j));
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
                    tree.AddChild(SlotClip(clipFolder, slot, ref clipsCreated, ref clipsKept), spec.Values[i]);
                }
                slotCounts[spec.Name] = spec.Values.Length;
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
                foreach (string child in Regions[r, 2].Split(','))
                {
                    AttachDirect(tree, trees[child], OneWeight);
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
                + "参数 " + parameters.Count + " 个（区域门 5 + W/One 1 + 表情门 2 + 轴 " + Axes.Length + " + 切片 " + MouthCoreSlices.Length + "）\n"
                + "树 " + trees.Count + " 棵（根 1 + 区域 5 + 表 " + Tables.Length + " + 变体 " + Variants.GetLength(0)
                + " + 副本 " + Copies.GetLength(0) + " + 开关 " + (Switches.GetLength(0) + VariantSwitches.Length) + "）\n"
                + "槽位 " + slots + " 个（片段：" + clipFolder + "，新建 " + clipsCreated + " · 保留已有 " + clipsKept + "）：\n" + perTree
                + "核对：`.research/check-controller.ps1 -Path <这份>`（参数名/默认值/树形/槽位名与坐标/门控接线一次核完）";
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
