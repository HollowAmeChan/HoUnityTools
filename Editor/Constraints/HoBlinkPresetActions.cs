using System.Collections.Generic;
using System.Text;
using Hollow.HoUnityTools.Constraints;
using UnityEditor;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.Constraints
{
    /// <summary>
    /// 预设：只负责把键名与起点参数写进序列化数据，用户自建的高光/眼仁键始终留空由用户填。
    /// 凝视与果冻预设是"追加"规则，不会清掉已有规则。
    /// </summary>
    internal static class HoBlinkPresetActions
    {
        public static void Rebuild(HoBlinkConstraint constraint)
        {
            constraint.Rebuild();
            EditorUtility.SetDirty(constraint);
        }

        public static void ClearAll(SerializedObject serializedObject)
        {
            serializedObject.FindProperty("blinkTargets").ClearArray();
            serializedObject.FindProperty("rules").ClearArray();
            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>眼睑键自动匹配：模型上有"双眼闭合键"就用一个双眼键，否则用左右两个键。</summary>
        public static void AutoMatchEyelidKeys(HoBlinkConstraint constraint, SerializedObject serializedObject)
        {
            bool splitLeftRight = string.IsNullOrEmpty(FindKey(constraint, HoBlinkKeySemantic.EyelidClosed, HoBlinkSide.Both));
            ApplyBlinkOutput(constraint, serializedObject, splitLeftRight);
        }

        /// <summary>
        /// 左右眼凝视（追加）：两条双极规则（X = 往右 − 往左，Y = 上 − 下），用**分左右眼**的凝视键。
        /// 按模型上实际存在的族自动选：有 In/Out（ARKit / PICO）就用内外族，否则用相对头的 Left/Right 族。
        /// </summary>
        public static void ApplyEyeGazeRules(HoBlinkConstraint constraint, SerializedObject serializedObject)
        {
            bool inOutFamily =
                !string.IsNullOrEmpty(FindKey(constraint, HoBlinkKeySemantic.GazeIn, HoBlinkSide.Left))
                || !string.IsNullOrEmpty(FindKey(constraint, HoBlinkKeySemantic.GazeOut, HoBlinkSide.Left));

            ApplyGazeRules(constraint, serializedObject, true, inOutFamily);
            Debug.Log(
                "[HoBlinkConstraint] 左右眼凝视：驱动族 = " + (inOutFamily ? "内外族（In/Out）" : "左右族（Left/Right）")
                + "；目标键按方向自动接。",
                constraint);
        }

        /// <summary>眨眼输出：双眼键版（一个双眼键）或左右键版（左右两个键，值相同）。</summary>
        public static void ApplyBlinkOutput(HoBlinkConstraint constraint, SerializedObject serializedObject, bool splitLeftRight)
        {
            SerializedProperty list = serializedObject.FindProperty("blinkTargets");
            list.ClearArray();

            string bothKey = FindKey(constraint, HoBlinkKeySemantic.EyelidClosed, HoBlinkSide.Both);
            string leftKey = FindKey(constraint, HoBlinkKeySemantic.EyelidClosed, HoBlinkSide.Left);
            string rightKey = FindKey(constraint, HoBlinkKeySemantic.EyelidClosed, HoBlinkSide.Right);

            if (!splitLeftRight && !string.IsNullOrEmpty(bothKey))
            {
                AddTarget(list, bothKey, HoBlinkSide.Both);
            }
            else
            {
                AddTarget(list, FirstNotEmpty(leftKey, bothKey), HoBlinkSide.Left);
                AddTarget(list, FirstNotEmpty(rightKey, bothKey), HoBlinkSide.Right);
            }

            serializedObject.ApplyModifiedProperties();
            Rebuild(constraint);

            string summary = string.Empty;
            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty target = list.GetArrayElementAtIndex(i);
                string key = target.FindPropertyRelative("keyName").stringValue;
                string mesh = constraint.DescribeKeyBindings(key);
                summary += (summary.Length == 0 ? string.Empty : "\n") + "· "
                    + key + "（" + target.FindPropertyRelative("side").enumDisplayNames[target.FindPropertyRelative("side").enumValueIndex] + "）"
                    + (string.IsNullOrEmpty(mesh) ? " ← 这些网格上没有这个键！" : " → " + mesh);
            }

            Debug.Log(
                "[HoBlinkConstraint] 眨眼输出：" + (splitLeftRight ? "左右键版" : "双眼键版") + "\n" + summary,
                constraint);
        }

        /// <summary>凝视驱动：双眼四向（单极 4 条）或左右眼四向（双极 2 条，可切内外族）。</summary>
        public static void ApplyGazeRules(HoBlinkConstraint constraint, SerializedObject serializedObject, bool splitEyes, bool inOutFamily = false)
        {
            SerializedProperty rules = serializedObject.FindProperty("rules");

            if (!splitEyes)
            {
                AddRule(rules, "凝视 上", HoBlinkDriverKind.ShapeKey, FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeUp), string.Empty, HoBlinkDriverRange.Unipolar, false, 4.0f, 0.35f, 0.03f);
                AddLabeledTarget(rules, "往上", HoShapeKeyRampPreset.Direct, 1.0f, HoShapeKeyBlendMode.Additive, 1.0f, 1.0f);
                AddRule(rules, "凝视 下", HoBlinkDriverKind.ShapeKey, FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeDown), string.Empty, HoBlinkDriverRange.Unipolar, false, 4.0f, 0.35f, 0.03f);
                AddLabeledTarget(rules, "往下", HoShapeKeyRampPreset.Direct, 1.0f, HoShapeKeyBlendMode.Additive, 1.0f, 1.0f);
                AddRule(rules, "凝视 左", HoBlinkDriverKind.ShapeKey, FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeLeft), string.Empty, HoBlinkDriverRange.Unipolar, false, 4.0f, 0.35f, 0.03f);
                AddLabeledTarget(rules, "往左", HoShapeKeyRampPreset.Direct, 1.0f, HoShapeKeyBlendMode.Additive, 1.0f, 1.0f);
                AddRule(rules, "凝视 右", HoBlinkDriverKind.ShapeKey, FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeRight), string.Empty, HoBlinkDriverRange.Unipolar, false, 4.0f, 0.35f, 0.03f);
                AddLabeledTarget(rules, "往右", HoShapeKeyRampPreset.Direct, 1.0f, HoShapeKeyBlendMode.Additive, 1.0f, 1.0f);
                serializedObject.ApplyModifiedProperties();
                Rebuild(constraint);
                Debug.Log("[HoBlinkConstraint] 四向凝视：加了 4 条单极规则（上 / 下 / 左 / 右），目标键按方向自动接。", constraint);
                AutoMatchTargetKeys(constraint, serializedObject, -1, "追加「四向凝视」后");
                return;
            }

            string positiveX;
            string negativeX;
            if (inOutFamily)
            {
                // 内/外族（ARKit、PICO）：左眼的 "内" 是往右看、"外" 是往左看
                positiveX = FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeIn, HoBlinkSide.Left);
                negativeX = FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeOut, HoBlinkSide.Left);
            }
            else
            {
                positiveX = FirstNotEmpty(
                    FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeRight, HoBlinkSide.Both, false),
                    FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeRight, HoBlinkSide.Left));
                negativeX = FirstNotEmpty(
                    FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeLeft, HoBlinkSide.Both, false),
                    FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeLeft, HoBlinkSide.Left));
            }

            string positiveY = FirstNotEmpty(
                FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeUp, HoBlinkSide.Both, false),
                FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeUp, HoBlinkSide.Left));
            string negativeY = FirstNotEmpty(
                FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeDown, HoBlinkSide.Both, false),
                FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeDown, HoBlinkSide.Left));

            AddRule(rules, "凝视 X（右 − 左）", HoBlinkDriverKind.ShapeKey, positiveX, negativeX, HoBlinkDriverRange.Bipolar, false, 4.0f, 0.35f, 0.03f);
            AddLabeledTarget(rules, "往右", HoShapeKeyRampPreset.Direct, 1.0f, HoShapeKeyBlendMode.Additive, 1.0f, 1.0f);
            AddLabeledTarget(rules, "往左", HoShapeKeyRampPreset.Direct, 1.0f, HoShapeKeyBlendMode.Additive, -1.0f, 1.0f);
            AddRule(rules, "凝视 Y（上 − 下）", HoBlinkDriverKind.ShapeKey, positiveY, negativeY, HoBlinkDriverRange.Bipolar, false, 4.0f, 0.35f, 0.03f);
            AddLabeledTarget(rules, "往上", HoShapeKeyRampPreset.Direct, 1.0f, HoShapeKeyBlendMode.Additive, 1.0f, 1.0f);
            AddLabeledTarget(rules, "往下", HoShapeKeyRampPreset.Direct, 1.0f, HoShapeKeyBlendMode.Additive, -1.0f, 1.0f);

            serializedObject.ApplyModifiedProperties();
            Rebuild(constraint);
            Debug.Log(
                "[HoBlinkConstraint] 左右眼凝视：加了 2 条双极规则（X / Y），每个方向一个目标（反向用 −增益，负值会被钳成 0）。",
                constraint);
            AutoMatchTargetKeys(constraint, serializedObject, -1, "追加「左右眼」后");
        }

        /// <summary>
        /// 高光跟眼：只加 **两条** 双极规则（X：往右 − 往左，Y：上 − 下），驱动键取模型上真实存在的凝视键，
        /// 目标键**留空**由用户填高光键。找不到凝视键时驱动键留空、不硬填不存在的名字。
        /// </summary>
        public static void ApplyGazeJelly(HoBlinkConstraint constraint, SerializedObject serializedObject)
        {
            AddJellyGazeRules(
                constraint,
                serializedObject,
                "高光跟眼",
                HoShapeKeyRampPreset.Direct,
                1.0f,
                4.0f,
                0.35f,
                0.03f);
        }

        /// <summary>
        /// 眨眼路径（果冻眼最好用的就是这一路）。两条规则、四个带角色的目标：
        ///   1「眨眼压高光」= 闭眼量驱动：高光**压扁**（放大 + 覆盖，最显眼的那一下）、
        ///     **拉宽**（同一条驱动、负增益，做出体积感）、**往下带一点**（叠加，位移）；
        ///   2「眨眼速度弹」= 眼皮速度驱动：只在眼皮动的那几帧有值，**眼仁压一下**就回弹，停住时完全不动。
        /// 目标键由「自动接键」按角色从网格上挑（找不到就留空）。
        /// </summary>
        public static void ApplyBlinkJelly(HoBlinkConstraint constraint, SerializedObject serializedObject)
        {
            SerializedProperty rules = serializedObject.FindProperty("rules");

            // 1) 闭眼量 → 压扁 / 拉宽 / 位移（3 个目标共用一条驱动，弹簧只搓一次）
            AddRule(rules, "眨眼压高光", HoBlinkDriverKind.AutoBlink, string.Empty, string.Empty, HoBlinkDriverRange.Unipolar, false, 8.0f, 0.26f, 0.015f);
            AddLabeledTarget(rules, "高光 · 压扁", HoShapeKeyRampPreset.Amplify, 1.35f, HoShapeKeyBlendMode.Override, 1.0f, 1.0f);
            AddLabeledTarget(rules, "高光 · 拉宽", HoShapeKeyRampPreset.Amplify, 1.0f, HoShapeKeyBlendMode.Override, -0.5f, 1.0f);
            AddLabeledTarget(rules, "高光 · 下移", HoShapeKeyRampPreset.Direct, 1.0f, HoShapeKeyBlendMode.Additive, -0.45f, 0.9f);

            // 2) 眼皮速度 → 眼仁弹一下（速度信号只在动的那一下有值，不需要额外的包络）
            AddRule(rules, "眨眼速度弹", HoBlinkDriverKind.BlinkSpeed, string.Empty, string.Empty, HoBlinkDriverRange.Unipolar, false, 10.0f, 0.20f, 0.008f);
            AddLabeledTarget(rules, "眼仁 · 压一下", HoShapeKeyRampPreset.EaseIn, 0.9f, HoShapeKeyBlendMode.Additive, 1.0f, 1.0f);

            serializedObject.ApplyModifiedProperties();
            Rebuild(constraint);
            Debug.Log(
                "[HoBlinkConstraint] 眨眼路径：加了 2 条规则、4 个目标。\n"
                + "· 眨眼压高光（闭眼量 0..1，8 Hz / ζ0.26）：高光 · 压扁（放大 1.35 + 覆盖）/ 拉宽（增益 −0.5）/ 下移（增益 −0.45）\n"
                + "· 眨眼速度弹（眼皮速度 0..1，10 Hz / ζ0.20）：眼仁 · 压一下",
                constraint);
            AutoMatchTargetKeys(constraint, serializedObject, -1, "追加「眨眼」后");
        }

        /// <summary>
        /// 按角色自动接目标键：把**键名还空着**的目标按它自己的「角色」标签去网格上找最像的形态键。
        /// 预设追加完会自动调它（见各 `Apply*`），所以正常用不到手动触发；
        /// 手动加了目标之后想重接，走规则右上的 `⋮ → 按名字接目标键`。
        /// 完全不猜语义之外的键名：找不到就留空并回报，绝不硬填一个不存在的名字。
        /// </summary>
        /// <param name="onlyRuleIndex">只处理这一条规则（-1 = 全部）。</param>
        /// <param name="reason">日志里的来源说明，例如"追加『跟眼』后"。</param>
        public static void AutoMatchTargetKeys(HoBlinkConstraint constraint, SerializedObject serializedObject, int onlyRuleIndex = -1, string reason = null)
        {
            List<string> names = CollectKeyNames(constraint);
            if (names.Count == 0)
            {
                Debug.LogWarning("[HoBlinkConstraint] 自动接键：没有可用的形态键 —— 先确认「目标网格」里加了对的网格。", constraint);
                return;
            }

            serializedObject.ApplyModifiedProperties();
            SerializedProperty rules = serializedObject.FindProperty("rules");
            StringBuilder report = new StringBuilder();
            int filled = 0;
            int skipped = 0;

            for (int r = 0; r < rules.arraySize; r++)
            {
                if (onlyRuleIndex >= 0 && r != onlyRuleIndex)
                {
                    continue;
                }

                SerializedProperty rule = rules.GetArrayElementAtIndex(r);
                SerializedProperty targets = rule.FindPropertyRelative("targets");
                string ruleName = rule.FindPropertyRelative("label").stringValue;

                for (int t = 0; t < targets.arraySize; t++)
                {
                    SerializedProperty target = targets.GetArrayElementAtIndex(t);
                    SerializedProperty keyName = target.FindPropertyRelative("keyName");
                    if (!string.IsNullOrEmpty(keyName.stringValue))
                    {
                        continue;
                    }

                    string role = target.FindPropertyRelative("label").stringValue;
                    if (string.IsNullOrEmpty(role))
                    {
                        role = ruleName;
                    }

                    string match = FindKeyByRole(names, role);
                    if (string.IsNullOrEmpty(match))
                    {
                        skipped++;
                        if (filled == 0 && skipped <= 4)
                        {
                            report.Append("\n· 没找到：").Append(Describe(role));
                        }

                        continue;
                    }

                    keyName.stringValue = match;
                    filled++;
                    report.Append("\n· ").Append(Describe(role)).Append(" → ").Append(match);
                }
            }

            serializedObject.ApplyModifiedProperties();
            Rebuild(constraint);

            string head = "[HoBlinkConstraint] 自动接键" + (string.IsNullOrEmpty(reason) ? string.Empty : "（" + reason + "）") + "：";
            if (filled == 0 && skipped == 0)
            {
                Debug.Log(head + "没有空着键名的目标，没动。", constraint);
                return;
            }

            head += filled > 0 ? "接了 " + filled + " 个" : "一个都没配上";
            if (skipped > 0)
            {
                head += "，另有 " + skipped + " 个角色在网格上找不到对应的键（可以手动用 ▾ 选，或改目标的「角色」）";
            }

            Debug.Log(head + report, constraint);
        }

        /// <summary>网格上出现过的所有形态键名（多网格取并集，去重）。</summary>
        private static List<string> CollectKeyNames(HoBlinkConstraint constraint)
        {
            List<string> names = new List<string>();
            HashSet<string> seen = new HashSet<string>();
            for (int m = 0; m < constraint.MeshCount; m++)
            {
                SkinnedMeshRenderer renderer = constraint.GetMesh(m);
                Mesh mesh = renderer != null ? renderer.sharedMesh : null;
                if (mesh == null)
                {
                    continue;
                }

                int count = mesh.blendShapeCount;
                for (int i = 0; i < count; i++)
                {
                    string name = mesh.GetBlendShapeName(i);
                    if (!string.IsNullOrEmpty(name) && seen.Add(name))
                    {
                        names.Add(name);
                    }
                }
            }

            return names;
        }

        /// <summary>
        /// 角色标签 → 关键词打分。角色里写的三件事决定打分：
        ///   **主体**（高光 / 眼仁 / 眼皮，不写就优先高光）；
        ///   **动作**（压扁 / 拉宽 / 位移，或不写动词只写方向）；
        ///   **方向**（往右 / 往左 / 往上 / 往下，用来认 `X+` / `X-` / `Z+` / `Z-` 这种单方向键）。
        /// 只做名字匹配，**不做语义推断**：拿不准就返回空串，让用户手选。
        /// </summary>
        private static string FindKeyByRole(List<string> names, string role)
        {
            if (string.IsNullOrEmpty(role))
            {
                return string.Empty;
            }

            string lower = role.ToLowerInvariant();
            bool highlight = Contains(lower, "高光", "highlight", "spec", "light");
            bool eyeball = Contains(lower, "眼仁", "瞳孔", "eyeball", "pupil", "iris");
            bool lid = Contains(lower, "眼皮", "眼睑", "lid", "lash");

            bool squash = Contains(lower, "压扁", "压", "扁", "squash", "shrink", "press", "compress");
            bool widen = Contains(lower, "拉宽", "宽", "widen", "stretch");
            bool shift = Contains(lower, "位移", "移动", "move", "shift", "offset", "slide");

            bool right = Contains(lower, "往右", "向右", "右移", "right");
            bool left = Contains(lower, "往左", "向左", "左移", "left");
            bool up = Contains(lower, "往上", "向上", "上移", "up");
            bool down = Contains(lower, "往下", "向下", "下移", "down");
            bool directional = right || left || up || down;

            bool verticalDir = up || down;
            bool horizontalDir = right || left;

            // 拉宽 = 同一类"压缩键"的负向用法（压扁走垂直、拉宽走水平）
            bool shrinkFamily = squash || widen;
            bool moveFamily = shift || directional;
            bool noSubject = !highlight && !eyeball && !lid;

            string best = string.Empty;
            int bestScore = 0;
            for (int i = 0; i < names.Count; i++)
            {
                string candidate = names[i].ToLowerInvariant();
                int score = 0;

                if (highlight)
                {
                    score += Contains(candidate, "highlight", "high_light", "spec", "light") ? 4 : -2;
                }
                else if (eyeball)
                {
                    score += Contains(candidate, "eyeball", "eye_ball", "pupil", "iris") ? 4 : -2;
                }
                else if (lid)
                {
                    score += Contains(candidate, "lid", "lash", "eyelid") ? 3 : -2;
                }
                else
                {
                    // 没写主体：高光优先（果冻眼最常压的就是高光），但不排除别的
                    score += Contains(candidate, "highlight", "high_light", "spec") ? 1 : 0;
                }

                if (shrinkFamily)
                {
                    score += Contains(candidate, "shrink", "squash", "press", "flat", "compress") ? 4 : -1;
                    if (squash && !horizontalDir)
                    {
                        score += Contains(candidate, "z", "y", "v", "vertical") ? 2 : 0;
                        score -= Contains(candidate, "x+", "x-", "x_") ? 2 : 0;
                    }
                    else if (widen || horizontalDir)
                    {
                        score += Contains(candidate, "x+", "x-", "x_", "x") ? 2 : 0;
                        score -= Contains(candidate, "z+", "z-", "z_") ? 2 : 0;
                    }
                }
                else if (moveFamily)
                {
                    score += Contains(candidate, "move", "shift", "offset", "slide") ? 4 : -1;
                    if (horizontalDir)
                    {
                        score += Contains(candidate, "x+", "x-", "x_", "x") ? 3 : -1;
                        score -= Contains(candidate, "z+", "z-", "z_") ? 3 : 0;
                    }
                    else if (verticalDir)
                    {
                        score += Contains(candidate, "z+", "z-", "z_", "y+", "y-", "y_") ? 3 : -1;
                        score -= Contains(candidate, "x+", "x-", "x_") ? 3 : 0;
                    }
                }
                else
                {
                    continue;
                }

                if (directional)
                {
                    bool positive = Contains(candidate, "+", "pos", "up");
                    bool negative = Contains(candidate, "-", "neg", "down");
                    if (right || up)
                    {
                        score += positive ? 3 : 0;
                        score -= negative ? 3 : 0;
                    }
                    else
                    {
                        score += negative ? 3 : 0;
                        score -= positive ? 3 : 0;
                    }
                }

                if (noSubject && score <= 0)
                {
                    continue;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = names[i];
                }
            }

            // 分数太低说明只是碰巧命中个别字，宁可不填
            return bestScore >= 4 ? best : string.Empty;
        }

        private static bool Contains(string text, params string[] needles)
        {
            for (int i = 0; i < needles.Length; i++)
            {
                if (text.Contains(needles[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static string Describe(string role)
        {
            return string.IsNullOrEmpty(role) ? "（没写角色）" : role;
        }

        /// <summary>
        /// 果冻用的两条双极规则。驱动键优先"相对头"的看左/看右，其次"相对眼球"的 In/Out；
        /// **找不到就留空**（不硬填内置表里的名字），由用户在规则里手填。
        /// </summary>
        private static void AddJellyGazeRules(
            HoBlinkConstraint constraint,
            SerializedObject serializedObject,
            string presetName,
            HoShapeKeyRampPreset rampPreset,
            float intensity,
            float frequency,
            float dampingRatio,
            float smoothing)
        {
            serializedObject.ApplyModifiedProperties();
            SerializedProperty rules = serializedObject.FindProperty("rules");

            string positiveX = FirstNotEmpty(
                FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeRight, HoBlinkSide.Both, false),
                FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeIn, HoBlinkSide.Left, false));
            string negativeX = FirstNotEmpty(
                FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeLeft, HoBlinkSide.Both, false),
                FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeOut, HoBlinkSide.Left, false));
            string positiveY = FirstNotEmpty(
                FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeUp, HoBlinkSide.Both, false),
                FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeUp, HoBlinkSide.Left, false));
            string negativeY = FirstNotEmpty(
                FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeDown, HoBlinkSide.Both, false),
                FindKeyOrPreferred(constraint, HoBlinkKeySemantic.GazeDown, HoBlinkSide.Left, false));

            AddRule(rules, "果冻 X（往右 − 往左）", HoBlinkDriverKind.ShapeKey, positiveX, negativeX, HoBlinkDriverRange.Bipolar, false, frequency, dampingRatio, smoothing);
            AddLabeledTarget(rules, "往右", rampPreset, intensity, HoShapeKeyBlendMode.Additive, 1.0f, 1.0f);
            AddLabeledTarget(rules, "往左", rampPreset, intensity, HoShapeKeyBlendMode.Additive, -1.0f, 1.0f);

            AddRule(rules, "果冻 Y（上 − 下）", HoBlinkDriverKind.ShapeKey, positiveY, negativeY, HoBlinkDriverRange.Bipolar, false, frequency, dampingRatio, smoothing);
            AddLabeledTarget(rules, "往上", rampPreset, intensity, HoShapeKeyBlendMode.Additive, 1.0f, 1.0f);
            AddLabeledTarget(rules, "往下", rampPreset, intensity, HoShapeKeyBlendMode.Additive, -1.0f, 1.0f);

            serializedObject.ApplyModifiedProperties();
            Rebuild(constraint);

            Debug.Log(
                "[HoBlinkConstraint] " + presetName + "：加了 2 条规则（果冻 X / 果冻 Y），每条两个方向的目标（反向用 −增益，负值会被钳成 0）。\n"
                + "· 驱动键：往右 = " + Describe(positiveX) + "，往左 = " + Describe(negativeX)
                + "，上 = " + Describe(positiveY) + "，下 = " + Describe(negativeY),
                constraint);
            AutoMatchTargetKeys(constraint, serializedObject, -1, "追加「" + presetName + "」后");

            static string Describe(string keyName)
            {
                return string.IsNullOrEmpty(keyName) ? "（模型上没有这类键，留空待填）" : keyName;
            }
        }

        /// <summary>给最后一条规则加一个"带角色"的空目标：标签说明它是干什么的，键名由自动接键填。</summary>
        private static void AddLabeledTarget(
            SerializedProperty rules,
            string label,
            HoShapeKeyRampPreset rampPreset,
            float intensity,
            HoShapeKeyBlendMode blendMode,
            float gain,
            float weight)
        {
            SerializedProperty targets = rules.GetArrayElementAtIndex(rules.arraySize - 1).FindPropertyRelative("targets");
            SerializedProperty target = AddTarget(targets, string.Empty, HoBlinkSide.Both);
            target.FindPropertyRelative("label").stringValue = label;
            target.FindPropertyRelative("rampPreset").enumValueIndex = (int)rampPreset;
            target.FindPropertyRelative("rampIntensity").floatValue = intensity;
            target.FindPropertyRelative("blendMode").enumValueIndex = (int)blendMode;
            target.FindPropertyRelative("gain").floatValue = gain;
            target.FindPropertyRelative("weight").floatValue = weight;
        }

        private static SerializedProperty AddRule(
            SerializedProperty rules,
            string label,
            HoBlinkDriverKind driverKind,
            string positiveKey,
            string negativeKey,
            HoBlinkDriverRange driverRange,
            bool invert,
            float frequency,
            float dampingRatio,
            float inputSmoothing)
        {
            rules.InsertArrayElementAtIndex(rules.arraySize);
            SerializedProperty rule = rules.GetArrayElementAtIndex(rules.arraySize - 1);
            rule.FindPropertyRelative("label").stringValue = label;
            rule.FindPropertyRelative("enabled").boolValue = true;
            rule.FindPropertyRelative("driverKind").enumValueIndex = (int)driverKind;
            rule.FindPropertyRelative("positiveKey").stringValue = positiveKey ?? string.Empty;
            rule.FindPropertyRelative("negativeKey").stringValue = negativeKey ?? string.Empty;
            rule.FindPropertyRelative("driverRange").enumValueIndex = (int)driverRange;
            rule.FindPropertyRelative("invert").boolValue = invert;
            rule.FindPropertyRelative("readWrittenThisFrame").boolValue = false;
            rule.FindPropertyRelative("missingPolicy").enumValueIndex = (int)HoBlinkMissingPolicy.Skip;
            rule.FindPropertyRelative("jellyEnabled").boolValue = true;
            rule.FindPropertyRelative("frequency").floatValue = frequency;
            rule.FindPropertyRelative("dampingRatio").floatValue = dampingRatio;
            rule.FindPropertyRelative("inputSmoothing").floatValue = inputSmoothing;
            rule.FindPropertyRelative("maxStep").floatValue = 0.016f;
            rule.FindPropertyRelative("resetOnEnable").boolValue = true;
            rule.FindPropertyRelative("targets").ClearArray();
            return rule;
        }

        private static SerializedProperty AddTarget(SerializedProperty list, string keyName, HoBlinkSide side)
        {
            list.InsertArrayElementAtIndex(list.arraySize);
            SerializedProperty target = list.GetArrayElementAtIndex(list.arraySize - 1);
            target.FindPropertyRelative("label").stringValue = string.Empty;   // 插入会复制上一个元素，标签必须显式清掉
            target.FindPropertyRelative("meshScope").enumValueIndex = (int)HoShapeKeyMeshScope.All;
            target.FindPropertyRelative("meshIndex").intValue = 0;
            target.FindPropertyRelative("keyName").stringValue = keyName ?? string.Empty;
            target.FindPropertyRelative("side").enumValueIndex = (int)side;
            target.FindPropertyRelative("blendMode").enumValueIndex = (int)HoShapeKeyBlendMode.Additive;
            target.FindPropertyRelative("weight").floatValue = 1.0f;
            target.FindPropertyRelative("gain").floatValue = 1.0f;
            target.FindPropertyRelative("offset").floatValue = 0.0f;
            target.FindPropertyRelative("outputMin").floatValue = 0.0f;
            target.FindPropertyRelative("outputMax").floatValue = 100.0f;
            target.FindPropertyRelative("clampToRange").boolValue = true;
            target.FindPropertyRelative("rampPreset").enumValueIndex = (int)HoShapeKeyRampPreset.Direct;
            target.FindPropertyRelative("rampIntensity").floatValue = 1.0f;
            target.FindPropertyRelative("rampAttack").floatValue = 0.0f;
            target.FindPropertyRelative("rampRelease").floatValue = 0.0f;
            return target;
        }

        private static string FindKey(HoBlinkConstraint constraint, HoBlinkKeySemantic semantic, HoBlinkSide side)
        {
            for (int i = 0; i < HoBlinkKeyTable.Entries.Length; i++)
            {
                HoBlinkKeyEntry entry = HoBlinkKeyTable.Entries[i];
                if (entry.Semantic != semantic || entry.Side != side)
                {
                    continue;
                }

                if (constraint.KeyExists(entry.Name))
                {
                    return entry.Name;
                }
            }

            return null;
        }

        /// <summary>找不到就退回该语义的首选名（会显示成缺失，提示用户改）。</summary>
        private static string FindKeyOrPreferred(HoBlinkConstraint constraint, HoBlinkKeySemantic semantic, HoBlinkSide side = HoBlinkSide.Both, bool allowFallbackName = true)
        {
            string found = FindKey(constraint, semantic, side);
            if (!string.IsNullOrEmpty(found))
            {
                return found;
            }

            if (side == HoBlinkSide.Both)
            {
                found = FindKey(constraint, semantic, HoBlinkSide.Left);
                if (!string.IsNullOrEmpty(found))
                {
                    return found;
                }
            }

            if (!allowFallbackName)
            {
                return null;
            }

            for (int i = 0; i < HoBlinkKeyTable.Entries.Length; i++)
            {
                HoBlinkKeyEntry entry = HoBlinkKeyTable.Entries[i];
                if (entry.Semantic == semantic && (side == HoBlinkSide.Both || entry.Side == side))
                {
                    return entry.Name;
                }
            }

            return null;
        }

        private static string FirstNotEmpty(params string[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrEmpty(values[i]))
                {
                    return values[i];
                }
            }

            return string.Empty;
        }
    }
}
