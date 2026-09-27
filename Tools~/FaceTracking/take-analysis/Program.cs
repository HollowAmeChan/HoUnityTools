// take-analysis -- 把「记 5 秒」打出来的 Console 统计变成"哪个特征最能分开动作"。
//
// 输入 1（takes）：Console 里那些 `[Ho 面捕统计] ...` 段落，整段粘贴即可。
//   · 每段前面的 `=== 标签` 行是**谁做的动作**（面板打的那条没有标签，只能人给）；
//   · 标签任意，但同一动作的三次要用**同一个前缀**（例如 `静置1` `静置2` `静置3`）。
// 输入 2（features）：一行一条候选式，语法**就是 profile 里那一套**：
//   target = 噘嘴            ← 哪一类标签算"必须触发"（子串匹配；其余都是"不许误触发"）
//   pucker                   ← 裸名字就是候选式的名字
//   严格a = pucker * clamp((pucker - 0.45) / 0.02, 0, 1)
//
// 输出：① 每类动作的逐行 min/avg/max（人看锚点）；② 每条候选式的**分离度**：
//   目标侧的**下界**（目标三次里该式的最小值）− 非目标侧的**上界**
//   （非目标所有段落里，逐变量取各自最大值 ⇒ 保守的"最坏一起发生"）。
//   分离度 > 0 = 存在一条触发线能同时"该触发时触得到、不该触发时不越线"；越大越稳。
//
// 用法：dotnet run -c Release [takes.txt] [features.txt]（默认 ..\takes.txt / ..\take-features.txt）
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Hollow.HoUnityTools.FaceTracking;

internal static class TakeAnalysis
{
    private sealed class Stat
    {
        public float Min = float.PositiveInfinity;
        public float Max = float.NegativeInfinity;
        public double Sum;
        public int Count;
        public float Span => Count == 0 ? 0f : Max - Min;
        public float Avg => Count == 0 ? 0f : (float)(Sum / Count);
        public void Add(float v)
        {
            if (v < Min) Min = v;
            if (v > Max) Max = v;
            Sum += v;
            Count++;
        }
    }

    private sealed class Take
    {
        public string Label = "";
        public readonly Dictionary<string, Stat> Rows = new Dictionary<string, Stat>(StringComparer.Ordinal);
    }

    private static int Main(string[] args)
    {
        string takesPath = args.Length > 0 ? args[0] : Path.Combine("..", "takes.txt");
        string featuresPath = args.Length > 1 ? args[1] : Path.Combine("..", "take-features.txt");

        if (!File.Exists(takesPath))
        {
            Console.WriteLine("没有 " + takesPath + " —— 把 Console 里那些 `[Ho 面捕统计]` 段落整段粘进去，");
            Console.WriteLine("每段前面加一行 `=== 标签`（同一动作的三次用同一个前缀，例如 静置1 / 静置2 / 静置3）。");
            return 0;
        }

        var takes = ParseTakes(File.ReadAllLines(takesPath));
        if (takes.Count == 0)
        {
            Console.WriteLine("没解析出任何段落 —— 段落要以 `[Ho 面捕统计]` 开头、以 `[Ho 面捕统计] 完` 结尾。");
            return 1;
        }

        string target = "噘嘴";
        var features = new List<(string Name, string Expression)>();
        if (File.Exists(featuresPath)) ReadFeatures(File.ReadAllLines(featuresPath), ref target, features);
        if (args.Length > 2 && args[2].Trim().Length > 0) target = args[2].Trim();   // 第三个参数覆盖 target
        if (features.Count == 0) features.Add(("pucker", "mouthPucker"));

        PrintTakeTable(takes, target);
        PrintFeatureRanking(takes, target, features);
        return 0;
    }

    private static List<Take> ParseTakes(string[] lines)
    {
        var takes = new List<Take>();
        Take current = null;
        foreach (string raw in lines)
        {
            string trimmed = raw.Trim();
            if (trimmed.StartsWith("===", StringComparison.Ordinal))
            {
                current = new Take { Label = trimmed.Substring(3).Trim() };
                takes.Add(current);
                continue;
            }
            if (trimmed.StartsWith("[Ho 面捕统计]", StringComparison.Ordinal))
            {
                if (trimmed.Contains(" 完")) current = null;   // 段落结束
                continue;                                       // 头一行（时间 / 窗口 / 采样）不用
            }
            if (current == null || trimmed.Length == 0) continue;
            if (trimmed.StartsWith("参数名", StringComparison.Ordinal)) continue;

            // 行数据：`名字  min  avg  max  波动`（名字里可能带空格；数值是 F4）。
            // 末尾的数值列**允许 3 个或 4 个**：4 个 = 面板原样（min/avg/max/波动），
            // 3 个 = 手抄时省掉了波动那一列（我们用不到它）。
            string[] parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            int numeric = 0;
            for (int i = parts.Length - 1; i >= 0 && numeric < 4 && TryFloat(parts[i], out _); i--) numeric++;
            if (numeric < 3) continue;
            float min, avg, max;
            if (!TryFloat(parts[parts.Length - numeric], out min)) continue;
            if (!TryFloat(parts[parts.Length - numeric + 1], out avg)) continue;
            if (!TryFloat(parts[parts.Length - numeric + 2], out max)) continue;
            string name = string.Join(" ", parts, 0, parts.Length - numeric).Trim();
            if (name.Length == 0) continue;

            Stat stat;
            if (!current.Rows.TryGetValue(name, out stat)) { stat = new Stat(); current.Rows[name] = stat; }
            stat.Add(min);
            stat.Add(max);
            for (int i = 0; i < 8; i++) stat.Add(avg);   // 权重随便，只要重建后的 Avg 落在三者附近
        }
        return takes;
    }

    private static bool TryFloat(string text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        || float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private static void ReadFeatures(string[] lines, ref string target, List<(string, string)> into)
    {
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
            int eq = line.IndexOf('=');
            string name = eq > 0 ? line.Substring(0, eq).Trim() : line;
            string expression = eq > 0 ? line.Substring(eq + 1).Trim() : line;
            if (name == "target") { target = expression; continue; }
            into.Add((name, expression));
        }
    }

    private static void PrintTakeTable(List<Take> takes, string target)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var take in takes)
            foreach (string name in take.Rows.Keys)
                if (seen.Add(name)) names.Add(name);
        names.Sort(StringComparer.Ordinal);

        Console.WriteLine("=== 每类动作的逐行锚点（" + takes.Count + " 段 / " + names.Count + " 行；目标 = " + target + "）");
        Console.WriteLine("行名  [静置类 min..max]  [目标类 min..max]   判定");
        foreach (string name in names)
        {
            var other = new List<Stat>();
            var goal = new List<Stat>();
            foreach (var take in takes)
            {
                Stat stat;
                if (!take.Rows.TryGetValue(name, out stat)) continue;
                (take.Label.Contains(target) ? goal : other).Add(stat);
            }
            float otherMax = float.NegativeInfinity, goalMin = float.PositiveInfinity;
            float otherAvg = 0f, goalAvg = 0f;
            int otherN = 0, goalN = 0;
            foreach (var s in other) { if (s.Max > otherMax) otherMax = s.Max; otherAvg += s.Avg; otherN++; }
            foreach (var s in goal) { if (s.Min < goalMin) goalMin = s.Min; goalAvg += s.Avg; goalN++; }
            if (otherN > 0) otherAvg /= otherN;
            if (goalN > 0) goalAvg /= goalN;
            string verdict = goalN == 0 || otherN == 0 ? "?"
                : goalMin > otherMax ? "✅ 单靠它就能分开（缝 " + (goalMin - otherMax).ToString("F4") + "）"
                : "❌ 重叠（差 " + (goalMin - otherMax).ToString("F4") + "）";
            Console.WriteLine(name.PadRight(22)
                + " [" + Fmt(otherMax == float.NegativeInfinity ? 0f : otherMax) + " 以下 · avg " + Fmt(otherAvg) + "]"
                + "  [" + Fmt(goalMin == float.PositiveInfinity ? 0f : goalMin) + " 以上 · avg " + Fmt(goalAvg) + "]  "
                + verdict);
        }
        Console.WriteLine();
    }

    private static void PrintFeatureRanking(List<Take> takes, string target, List<(string Name, string Expression)> features)
    {
        // target 允许逗号分隔（"左,双边" = 标签里含任意一个就算目标）—— 左右两条轴各自的正样例
        // 是"那一侧的单边 + 双边"，负样例才是剩下的全部（含**另一侧**的单边）。
        string[] targets = target.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        Func<string, bool> isTargetLabel = label =>
        {
            foreach (string t in targets) if (label.Contains(t.Trim())) return true;
            return false;
        };
        var results = new List<(string Name, float GoalLo, string GoalTake, float OtherHi, string OtherTake, float Gap, float GlobalGap, string Note)>();
        foreach (var feature in features)
        {
            HoFaceExpression parsed;
            string why;
            if (!HoFaceExpression.TryParse(feature.Expression, out parsed, out why))
            {
                Console.WriteLine("  ✗ " + feature.Name + " 解析不了：" + why);
                continue;
            }

            var refs = new List<string>();
            parsed.CollectVariables(refs);

            // **逐段**求值，三个作用域：
            //   lo  = 每个变量取该段的 min（段首段尾的爬升/回落都在里面 ⇒ 鼓嘴这种"从 0 爬上去"的动作会掉到 0）
            //   mid = 每个变量取该段的 avg（**5 秒保持的稳态** —— 目标侧就该按它算"够不够得着触发线"）
            //   hi  = 每个变量取该段的 max（非目标侧按它算"最坏能冲到哪"）
            // 报告：目标侧稳态下界（mid 的最小）− 非目标侧上界（hi 的最大）= 缝。
            // 另附目标侧的"谷底"（lo 的最小）—— 它说明这一段的爬升有多深，用来判断"动作是不是没做满"。
            float goalMid = float.PositiveInfinity, goalBottom = float.PositiveInfinity, otherHi = float.NegativeInfinity;
            string goalTake = "—", otherTake = "—";
            var goalScope = new Dictionary<string, float>(StringComparer.Ordinal);
            var otherScope = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var take in takes)
            {
                bool isTarget = isTargetLabel(take.Label);
                var loScope = new Dictionary<string, float>(StringComparer.Ordinal);
                var midScope = new Dictionary<string, float>(StringComparer.Ordinal);
                var hiScope = new Dictionary<string, float>(StringComparer.Ordinal);
                foreach (var pair in take.Rows)
                {
                    loScope[pair.Key] = pair.Value.Min;
                    midScope[pair.Key] = pair.Value.Avg;
                    hiScope[pair.Key] = pair.Value.Max;
                }
                float lo = parsed.Evaluate(name => Lookup(loScope, name));
                float mid = parsed.Evaluate(name => Lookup(midScope, name));
                float hi = parsed.Evaluate(name => Lookup(hiScope, name));
                if (isTarget)
                {
                    if (mid < goalMid) { goalMid = mid; goalTake = take.Label; }
                    if (lo < goalBottom) goalBottom = lo;
                    foreach (var pair in take.Rows)
                    {
                        float current;
                        if (!goalScope.TryGetValue(pair.Key, out current) || pair.Value.Min < current) goalScope[pair.Key] = pair.Value.Min;
                    }
                }
                else
                {
                    if (hi > otherHi) { otherHi = hi; otherTake = take.Label; }
                    foreach (var pair in take.Rows)
                    {
                        float current;
                        if (!otherScope.TryGetValue(pair.Key, out current) || pair.Value.Max > current) otherScope[pair.Key] = pair.Value.Max;
                    }
                }
            }

            var oneSided = new List<string>();
            foreach (string name in refs)
            {
                bool inTarget = false, inOther = false;
                foreach (var take in takes)
                {
                    if (!take.Rows.ContainsKey(name)) continue;
                    if (take.Label.Contains(target)) inTarget = true; else inOther = true;
                }
                if (!inTarget || !inOther) oneSided.Add(name);
            }

            float globalGap = parsed.Evaluate(name => Lookup(goalScope, name)) - parsed.Evaluate(name => Lookup(otherScope, name));
            string note = oneSided.Count == 0 ? ""
                : "（这些名字只在一侧出现：" + string.Join(",", oneSided.ToArray()) + "）";
            results.Add((feature.Name, goalMid, goalTake, otherHi, otherTake, goalMid - otherHi, globalGap,
                "目标谷底 " + Fmt(goalBottom) + "；" + note));
        }

        Console.WriteLine("=== 候选式排序（**逐段稳态**：目标侧取段内均值、非目标侧取段内最大；缝 = 目标稳态下界 − 非目标上界）");
        results.Sort((a, b) => b.Gap.CompareTo(a.Gap));
        foreach (var r in results)
            Console.WriteLine("  " + (r.Gap > 0 ? "✅" : "❌") + " " + r.Name.PadRight(22)
                + " 目标稳态 " + Fmt(r.GoalLo) + "（" + r.GoalTake + "）"
                + " · 非目标上界 " + Fmt(r.OtherHi) + "（" + r.OtherTake + "）"
                + " · 缝 " + (r.Gap >= 0 ? "+" : "") + Fmt(r.Gap)
                + "  [全局最坏 " + (r.GlobalGap >= 0 ? "+" : "") + Fmt(r.GlobalGap) + "] " + r.Note);
        Console.WriteLine();
    }

    private static float Lookup(Dictionary<string, float> table, string name)
    {
        float value;
        return table.TryGetValue(name, out value) ? value : 0f;
    }

    private static string Fmt(float value) => value.ToString("F4", CultureInfo.InvariantCulture);
}
