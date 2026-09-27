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
        foreach (var row in middleware.outputs)
        {
            if (row == null) { problems.Add("(null output row)"); continue; }
            if (!names.Add(row.parameter)) problems.Add("duplicate output name: " + row.parameter);

            HoUnityTools.HoFaceExpression expression;
            string parseError;
            if (!HoUnityTools.HoFaceExpression.TryParse(row.expression, out expression, out parseError))
                problems.Add("expression does not parse: " + row.parameter + " -> " + parseError);

            if (row.curve == null || row.curve.length == 0)
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

        if (problems.Count > 0)
        {
            Console.WriteLine("problems (" + problems.Count + "):");
            for (int i = 0; i < problems.Count && i < 20; i++) Console.WriteLine("  " + problems[i]);
            return 1;
        }

        Console.WriteLine("no problems: every output row parsed, curves present, no duplicate names");
        return 0;
    }
}
