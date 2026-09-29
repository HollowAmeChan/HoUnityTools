// profile-verify -- parse a .hoface.json with the REAL shipping reader and report what came back.
//
// Why this exists: the profile we hand-edit for a test rig (D:\Unity_Project\BREAK_URP\...) is read
// by HoFaceProfile.TryParse at runtime. "Valid JSON" is not the same thing as "our reader accepts it"
// (curve key order, unknown fields, the curve shape ...), and a silently unreadable profile shows up
// as "the face does not move" -- the most expensive kind of failure to debug in a live test.
//
// Usage: dotnet run --project Tools~/FaceTracking/profile-verify -- "<path to hoface.json>"
using System;
using System.Collections.Generic;
using HoUnityTools = Hollow.HoUnityTools.FaceTracking;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("usage: profile-verify <path to *.hoface.json>");
            return 2;
        }

        string path = args[0];
        if (!System.IO.File.Exists(path))
        {
            Console.WriteLine("not found: " + path);
            return 2;
        }

        string text = System.IO.File.ReadAllText(path);

        HoUnityTools.HoFaceMiddleware middleware;
        string error;
        bool ok = HoUnityTools.HoFaceProfile.TryParse(text, out middleware, out error);
        Console.WriteLine("parse ok     : " + ok + (ok ? "" : "  error=" + error));
        if (!ok) return 1;

        Console.WriteLine("displayName  : " + middleware.displayName);
        Console.WriteLine("inputs       : " + middleware.inputs.Count);
        Console.WriteLine("outputs      : " + middleware.outputs.Count);

        // ── --eval：把中间层**真算一遍**（稳态）────────────────────────────────────────
        // 为什么要有它：「某条轴永远是正的」这类问题，靠人读表达式会读错 —— 加法开关、同名覆盖、
        // 曲线夹取、修饰符都能把符号吃掉。这里用**同一个读取器**、**同一个表达式求值器**、
        // **同一个 `HoFaceCurve.Transfer`** 按行序算一遍，把每一行的值打出来。
        //   · 输入行：`--eval` 给了就用给的，没给用该行的 `defaultValue`（= 设备一帧都没来过时的值）
        //   · 同名多行：**后写覆盖先写**（与运行时输出表一致）；`out("…")` 就是查这张表
        //   · 修饰符按**稳态**处理：`smooth` 收敛成恒等；`steps` 取"过触发值跳目标 / 掉到阈值下回 0"
        //     （真实现还有迟滞与进/退维持计时 —— 那是时间轴的事，不影响这里的稳态诊断）
        // 用法：profile-verify <profile> --eval "mouthFrownLeft=0.6,mouthFrownRight=0.6" [--rows Mouth/Form]
        string evalSpec = null;
        string rowFilter = null;
        string exprSpec = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--eval" && i + 1 < args.Length) evalSpec = args[i + 1];
            if (args[i] == "--rows" && i + 1 < args.Length) rowFilter = args[i + 1];
            if (args[i] == "--expr" && i + 1 < args.Length) exprSpec = args[i + 1];
        }
        if (evalSpec != null)
        {
            var given = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (string pair in evalSpec.Split(','))
            {
                if (pair.Trim().Length == 0) continue;
                int eq = pair.IndexOf('=');
                if (eq <= 0) { Console.WriteLine("bad --eval item: " + pair); return 2; }
                given[pair.Substring(0, eq).Trim()] = float.Parse(pair.Substring(eq + 1).Trim(),
                    System.Globalization.CultureInfo.InvariantCulture);
            }

            var value = new Dictionary<string, float>(StringComparer.Ordinal);
            Console.WriteLine("");
            Console.WriteLine("eval inputs（非 0 的 + --eval 指定的）:");
            foreach (var row in middleware.inputs)
            {
                if (row == null || string.IsNullOrEmpty(row.parameter)) continue;
                float v = given.TryGetValue(row.parameter, out var g) ? g : row.defaultValue;
                value[row.parameter] = v;
                if (v != 0f || given.ContainsKey(row.parameter))
                    Console.WriteLine("  " + row.parameter.PadRight(28) + " = " + v);
            }

            bool trace = Array.IndexOf(args, "--trace") >= 0;
            float Lookup(string name)
            {
                float got = value.TryGetValue(name, out var v) ? v : 0f;
                if (trace) Console.WriteLine("    lookup [" + name + "] -> " + got);
                return got;
            }

            // `--expr "<表达式>"`：用**同一个求值器**算一条式子（作用域同 --eval）——
            // 排查「某条覆盖行为什么是 0」时把式子拆成几段分别算，比读代码快。
            if (exprSpec != null)
            {
                HoUnityTools.HoFaceExpression probe;
                string why;
                if (!HoUnityTools.HoFaceExpression.TryParse(exprSpec, out probe, out why))
                {
                    Console.WriteLine("--expr 解析失败：" + why);
                    return 1;
                }
                Console.WriteLine("--expr = " + probe.Evaluate(Lookup, Lookup));
            }

            Console.WriteLine("eval outputs" + (rowFilter == null ? "（只打非 0）:" : "（--rows " + rowFilter + "）:"));
            for (int i = 0; i < middleware.outputs.Count; i++)
            {
                var row = middleware.outputs[i];
                if (row == null) continue;
                float v;
                if (string.IsNullOrWhiteSpace(row.expression))
                {
                    v = row.defaultValue;                      // 常量行（门控那种）
                }
                else
                {
                    HoUnityTools.HoFaceExpression parsed;
                    v = HoUnityTools.HoFaceExpression.TryParse(row.expression, out parsed, out _)
                        ? parsed.Evaluate(Lookup, Lookup)      // 变量 = 输入/已算出的行；out("…") = 同名最近写者
                        : row.defaultValue;
                }
                v = HoUnityTools.HoFaceCurve.Transfer(row.curve, v);
                if (row.modifiers != null)
                {
                    foreach (var mod in row.modifiers)
                    {
                        if (mod == null || mod.kind != HoUnityTools.HoFaceModifierKind.Steps || mod.steps == null) continue;
                        foreach (var step in mod.steps)
                        {
                            if (step == null) continue;
                            if (v >= step.trigger) v = step.target;
                            else if (v < step.trigger - step.threshold) v = 0f;
                        }
                    }
                }

                value[row.parameter] = v;                      // 后写覆盖先写
                if (rowFilter != null ? row.parameter.Contains(rowFilter) : v != 0f)
                    Console.WriteLine("  #" + i.ToString().PadRight(4) + row.parameter.PadRight(34) + " = " + v);
            }
            return 0;
        }

        var problems = new List<string>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var styleChains = new List<string>();
        foreach (var row in middleware.outputs)
        {
            if (row == null) { problems.Add("(null output row)"); continue; }

            // 同名多行**是有意的，当且仅当"后写的那一行读自己"**（`out(同名)`）—— 判据在
            // `HoFaceNaming.IsChainRow`：运行时的输出表缓存与发布都是"后写覆盖先写"，所以那就是**链**；
            // 同名却**没读自己**的才是覆盖（前一行白算），照旧算 problem。
            if (!names.Add(row.parameter))
            {
                if (HoUnityTools.HoFaceNaming.IsChainRow(row.parameter, row.expression)) styleChains.Add(row.parameter);
                else problems.Add("duplicate output name: " + row.parameter);
            }

            HoUnityTools.HoFaceExpression expression;
            string parseError;
            // 空表达式的**常量行**不是错：门控就靠它（面板 `IsConstantRow` 同一条口径）——
            // 它这一格每帧写 `defaultValue`，没有表达式可解析。
            bool isConstant = string.IsNullOrWhiteSpace(row.expression);
            if (!isConstant && !HoUnityTools.HoFaceExpression.TryParse(row.expression, out expression, out parseError))
                problems.Add("expression does not parse: " + row.parameter + " -> " + parseError);

            // 曲线：**约束规则不需要自己标定**（用户 2026-09-29 定：「这种约束规则都直接不需要曲线，
            // 曲线由它本身的源定义」）。三类豁免：同名链（表达式里读自己）、纯转发（整条就是一个 out("…")）、
            // **门行**（名字以 Gate 结尾，值是 0..1 的放行度）。
            // ⚠️⚠️ **但"不写曲线"≠"恒等"**：`HoFaceProfileJson.ReadCurve` 只在 `keys.Count > 0` 时才赋值，
            // 所以 `keys: []` 的行**留着 `HoFaceOutput.curve` 的默认值 `Linear(0,0,1,1)`** ⇒
            // `HoFaceCurve.Transfer` 会把值**夹到 [0,1]**。0..1 的权重无所谓，**带符号的轴会被吃掉负半边**
            // —— 2026-09-29 用户报的「sad 永远正」就是这么来的（下面那段"同名多行值域一致性"就是它的检查）。
            // **其余行照旧必须有曲线**（那是读数自己的标定，别漏）。
            string exprText = row.expression ?? "";
            string rowName = row.parameter ?? "";
            bool isChain = System.Text.RegularExpressions.Regex.IsMatch(
                exprText, "out\\(\\s*\"" + System.Text.RegularExpressions.Regex.Escape(rowName) + "\"\\s*\\)");
            bool isForward = System.Text.RegularExpressions.Regex.IsMatch(
                exprText, "^\\s*out\\(\\s*\"[^\"]+\"\\s*\\)\\s*$");
            bool isGate = rowName.EndsWith("Gate", StringComparison.Ordinal);
            if (!isChain && !isForward && !isGate && (row.curve == null || row.curve.length == 0))
            {
                problems.Add("row without a usable curve: " + row.parameter);
            }
        }

        // ── 同名多行的**值域一致性**（2026-09-29 加：用户报「sad 永远正」的真凶就在这里）──────────
        // 判据：同一个名字的写者里只要有一条的曲线覆盖负半边（首键 t < 0），**每一条写者都必须覆盖** ——
        // 否则最后写者会按它自己那条 [0,1] 曲线的 x 范围把负值夹成 0（后写覆盖先写 ⇒ 负半边直接消失）。
        var writersOf = new Dictionary<string, List<HoUnityTools.HoFaceOutput>>(StringComparer.Ordinal);
        foreach (var row in middleware.outputs)
        {
            if (row == null || string.IsNullOrEmpty(row.parameter)) continue;
            if (!writersOf.TryGetValue(row.parameter, out var list))
            {
                list = new List<HoUnityTools.HoFaceOutput>();
                writersOf[row.parameter] = list;
            }
            list.Add(row);
        }
        foreach (var pair in writersOf)
        {
            bool anySigned = false;
            foreach (var row in pair.Value)
            {
                var k = row.curve != null ? row.curve.keys : null;
                if (k != null && k.Length > 0 && k[0].time < -1e-6f) anySigned = true;
            }
            if (!anySigned) continue;
            foreach (var row in pair.Value)
            {
                var k = row.curve != null ? row.curve.keys : null;
                if (k == null || k.Length == 0)
                    problems.Add("signed row without an explicit curve (keys: [] 会留下默认的 0..1 曲线 ⇒ 负值被夹成 0): " + row.parameter);
                else if (k[0].time > -1e-6f)
                    problems.Add("signed row with a 0..1 curve (负半边被夹掉): " + row.parameter);
            }
        }

        // The rows this assembly added for the test controller: show the curve range, because a 0..1
        // curve on a signed axis silently clamps the whole negative half.
        Console.WriteLine("Ho/Drive rows:");
        foreach (var row in middleware.outputs)
        {
            if (row == null || row.parameter == null || !row.parameter.StartsWith("Ho/Drive/", StringComparison.Ordinal)) continue;
            var keys = row.curve != null ? row.curve.keys : null;
            string range = keys != null && keys.Length >= 2
                ? keys[0].time + ".." + keys[keys.Length - 1].time + " -> " + keys[0].value + ".." + keys[keys.Length - 1].value
                : "(no keys)";
            Console.WriteLine("  " + row.parameter.PadRight(32) + " = " + row.expression + "   curve " + range);
        }

        // 进/退维持（2026-09-29 加）：把真读取器看到的去抖设置列出来 ——
        // "面板显示 0 / 文件里有值"这类对不上时，先跑这一行看谁错。
        Console.WriteLine("");
        Console.WriteLine("steps 去抖（进维持 / 退维持）:");
        int dwellRows = 0;
        foreach (var row in middleware.outputs)
        {
            if (row == null || row.modifiers == null) continue;
            foreach (var mod in row.modifiers)
            {
                if (mod == null || mod.kind != HoUnityTools.HoFaceModifierKind.Steps || mod.steps == null) continue;
                for (int s = 0; s < mod.steps.Count; s++)
                {
                    var step = mod.steps[s];
                    if (step == null || (step.enterSeconds <= 0f && step.exitSeconds <= 0f)) continue;
                    Console.WriteLine("  " + row.parameter.PadRight(34) + " 档" + s
                        + "  trigger " + step.trigger + " · 迟滞 " + step.threshold
                        + " · 进维持 " + step.enterSeconds + "s · 退维持 " + step.exitSeconds + "s");
                    dwellRows++;
                }
            }
        }
        if (dwellRows == 0) Console.WriteLine("  （没有行设置进/退维持 —— 全是 0 = 老行为）");

        if (problems.Count > 0)
        {
            Console.WriteLine("problems (" + problems.Count + "):");
            for (int i = 0; i < problems.Count && i < 20; i++) Console.WriteLine("  " + problems[i]);
            return 1;
        }

        Console.WriteLine("no problems: every output row parsed, curves present (constraint rows exempt), "
            + styleChains.Count + " intentional style chain row(s): " + string.Join(", ", styleChains));
        return 0;
    }
}
