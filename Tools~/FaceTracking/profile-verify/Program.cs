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

            // 曲线：**约束规则不需要**（用户 2026-09-29 定：「这种约束规则都直接不需要曲线，曲线由它本身的源定义」）。
            // 三类豁免：同名链（表达式里读自己）、纯转发（整条就是一个 out("…")）、**门行**（名字以 Gate 结尾，
            // 值是 0..1 的放行度）。运行时 `HoFaceCurve.Transfer` 里空曲线/没有曲线就是恒等。
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
