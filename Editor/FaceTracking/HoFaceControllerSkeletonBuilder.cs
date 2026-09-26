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

        /// <summary>轴参数（部位/轴 → 名字）：30 根，见设计稿 §3.1。</summary>
        private static readonly string[] Axes =
        {
            "Ho/Drive/Mouth/Form", "Ho/Drive/Mouth/Open", "Ho/Drive/Mouth/Funnel", "Ho/Drive/Mouth/Press",
            "Ho/Drive/Mouth/Jaw", "Ho/Drive/Mouth/Forward", "Ho/Drive/Mouth/Pucker", "Ho/Drive/Mouth/X",
            "Ho/Drive/Mouth/TongueL", "Ho/Drive/Mouth/TongueR",
            // 嘴角（选项 C）：把"嘴角笑/苦"从 Form 的负侧分出来，专供 MouthCorner 表（合同时 HQSmileFrownLeft/Right）
            "Ho/Drive/Mouth/CornerL", "Ho/Drive/Mouth/CornerR",
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

        /// <summary>一张 2D 表的定义：X/Y 参数 + 每个刻度的轴值 + 槽位名里那两段词。</summary>
        private sealed class TableSpec
        {
            public string Name;
            public string X, Y, XToken, YToken;
            public float[] XValues, YValues;
        }

        private static readonly float[] Two = { -1f, 0f, 1f };     // 双向轴：负端 / 中性 / 正端
        private static readonly float[] Unit = { 0f, 0.5f, 1f };   // 单端轴：0 / 半 / 满
        private static readonly float[] Ends = { -1f, 1f };        // 两档
        private static readonly float[] ZeroOne = { 0f, 1f };

        /// <summary>
        /// **`Mouth/Open` 专用刻度：−0.14 / 0 / 0.4 / 0.75**（2026-09-27 用户实测定）。
        ///
        /// 正侧：手机实测"张满"只到 0.75、"半张"在 0.4 附近 ⇒ 把这根轴的圈收进实测范围
        /// （0.75 就是张满，0.9 / 1.0 会被投影到这一行，实测逐位相同）。
        /// 负侧：这根轴**不是"下颌开度"而是"嘴唇张开量"** —— 闭紧与卷唇都算"比中性更闭合"。
        /// 实测：不咬唇上下内卷 = **−0.07**、咬紧内卷 = **−0.14** ⇒ 负侧给 **−0.14** 一格；
        /// −0.07 落在它一半处即可（用户定"可以不加太多动画"）。
        /// 曲线把 ±0.02 的死区抹平（撇嘴只到 −0.01/0）。
        /// ⚠️ 只给 `MouthCore` 的 Y 用；`Mouth/Jaw`（= 裸 `jawOpen`）**没有实测数据**，仍用 `Unit`。
        /// </summary>
        private static readonly float[] OpenMeasured = { -0.14f, 0f, 0.4f, 0.75f };

        /// <summary>
        /// **`Mouth/Form` 专用刻度：−1 / 0 / 0.75 / 1**（2026-09-27 用户实测定）。
        /// 正侧不是"0 → 1"两档：「**常态笑**」在 0.75 左右、「**大笑**」才到 1 —— 两个都是真实状态，
        /// 各要一个采样点，所以 X 是四档（12 格）。负侧仍停在 −1：那里是曲线的钳制端，
        /// 而噘嘴实测只到 −0.5（按选项 C 只该把唇形往苦的方向带一点，不该到极端苦相）。
        /// ⚠️ 只给 `MouthCore` / `MouthCoreExpr` 的 X 用；`MouthWidth` 的 X/Y 仍是 `Two`（没有实测数据）。
        /// </summary>
        private static readonly float[] FormSmile = { -1f, 0f, 0.75f, 1f };

        private static readonly TableSpec[] Tables =
        {
            new TableSpec { Name = "MouthCore", X = "Ho/Drive/Mouth/Form", Y = "Ho/Drive/Mouth/Open", XToken = "Form", YToken = "Open", XValues = FormSmile, YValues = OpenMeasured },
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

        /// <summary>平行副本（按键表情）：主版 → 副本名。</summary>
        private static readonly string[,] Copies =
        {
            { "MouthCore", "MouthCoreExpr" }, { "LidL", "LidLExpr" }, { "LidR", "LidRExpr" },
            { "BrowCoreL", "BrowCoreLExpr" }, { "BrowCoreR", "BrowCoreRExpr" }
        };

        /// <summary>1D 开关：名字 / 主版 / 副本 / 用哪个表情门。</summary>
        private static readonly string[,] Switches =
        {
            { "MouthCoreSwitch", "MouthCore", "MouthCoreExpr", "Smile" },
            { "LidLSwitch", "LidL", "LidLExpr", "Smile" },
            { "LidRSwitch", "LidR", "LidRExpr", "Smile" },
            { "BrowCoreLSwitch", "BrowCoreL", "BrowCoreLExpr", "Angry" },
            { "BrowCoreRSwitch", "BrowCoreR", "BrowCoreRExpr", "Angry" }
        };

        /// <summary>区域 → 直接挂在它下面的子节点（表名或开关名）。</summary>
        private static readonly string[,] Regions =
        {
            { "MouthRegion", "Mouth", "MouthCoreSwitch,MouthJaw,MouthWidth,MouthCorner,MouthTongue" },
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

            // ── 参数（40 个）───────────────────────────────────────────────────
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
                        string slot = SlotName(spec.Name, spec.XToken, spec.YToken, spec.XValues.Length, i, j);
                        tree.AddChild(SlotClip(clipFolder, slot, ref clipsCreated, ref clipsKept),
                            new Vector2(spec.XValues[i], spec.YValues[j]));
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
                        string slot = SlotName(copy.Value, spec.XToken, spec.YToken, spec.XValues.Length, i, j);
                        tree.AddChild(SlotClip(clipFolder, slot, ref clipsCreated, ref clipsKept),
                            new Vector2(spec.XValues[i], spec.YValues[j]));
                    }
                slotCounts[copy.Value] = spec.XValues.Length * spec.YValues.Length;
                trees[copy.Value] = tree;
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
                + "树 " + trees.Count + " 棵（根 1 + 区域 5 + 表 " + Tables.Length + " + 副本 " + Copies.GetLength(0) + " + 开关 " + Switches.GetLength(0) + "）\n"
                + "槽位 " + slots + " 个（片段：" + clipFolder + "，新建 " + clipsCreated + " · 保留已有 " + clipsKept + "）：\n" + perTree
                + "核对：`.research/check-controller.ps1 -Path <这份>`（参数名/默认值/树形/槽位名与坐标/门控接线一次核完）";
        }

        /// <summary>槽位片段放哪：控制器同级的 `Animations/`。</summary>
        private static string ClipFolderFor(string controllerPath)
        {
            return Path.GetDirectoryName(controllerPath).Replace('\\', '/') + "/Animations";
        }

        /// <summary>槽位名 = `<树名>__<X段词>__<Y段词>__A<X刻度数>X<i>Y<j>`（见命名权威 §5）。</summary>
        private static string SlotName(string tree, string xToken, string yToken, int xCount, int i, int j)
        {
            return tree + "__" + xToken + "__" + yToken + "__A" + xCount + "X" + i + "Y" + j;
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
