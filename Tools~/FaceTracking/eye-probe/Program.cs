// eye-probe -- 用**运行期那版**求值器（Runtime/FaceTracking/HoFaceExpression.cs，含 out() 回调）
// 按 profile 的行序把整条链算一遍，打印眼睑四根轴。用途：验 §5.7.35 的去污公式。
//
// 为什么不用 chain-probe：那份编的是 Warudo mod core 的求值器，而它的 TryResolve 里**没有 `out`**
// ⇒ 任何读 out() 的行（含眼睑 Form）在那份里恒为 0。这一版才和 Unity 里的行为一致。
//
// 用法：dotnet run --project Tools~/FaceTracking/eye-probe -- <profile.hoface.json> <scen1.txt> [scen2.txt ...]
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Hollow.HoUnityTools.FaceTracking;

internal static class EyeProbe
{
    private static readonly string[] Watch =
    {
        "Ho/Drive/Mouth/Form",
        "Ho/Drive/Lid/Left/BlinkWide", "Ho/Drive/Lid/Left/Form",
        "Ho/Drive/Lid/Right/BlinkWide", "Ho/Drive/Lid/Right/Form",
    };

    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("usage: eye-probe <profile.hoface.json> <scenario.txt> [...]");
            return 2;
        }

        HoFaceMiddleware profile;
        string error;
        if (!HoFaceProfile.TryParse(File.ReadAllText(args[0]), out profile, out error))
        {
            Console.WriteLine("profile parse FAILED: " + error);
            return 1;
        }

        // 行序：out("行名") 只能引用**前面**的行（第一版 Form 排在 Mouth/Form 前面 ⇒ 永远读默认值 0）。
        // 这条检查必须留在这——它就是当年那个 bug 的机器化门。Validate 每行返回一条（空串 = 这行没问题）。
        string[] orderErrors = HoFaceOutputOrder.Validate(profile.outputs);
        int orderBad = 0;
        foreach (string line in orderErrors ?? new string[0])
        {
            if (string.IsNullOrEmpty(line)) continue;
            orderBad++;
            Console.WriteLine("  !! " + line);
        }
        Console.WriteLine("row order problems: " + orderBad);
        Console.WriteLine();

        // 输入行：规范名 → 线名（同一个规范名多条线时取**最后声明**的那条 = 链条的覆盖规则）
        var inputRow = new Dictionary<string, HoFaceOutput>(StringComparer.Ordinal);
        foreach (HoFaceOutput row in profile.inputs) inputRow[row.parameter] = row;

        for (int i = 1; i < args.Length; i++)
        {
            Dictionary<string, float> raw = ReadDump(args[i]);
            var values = new Dictionary<string, float>(StringComparer.Ordinal);

            foreach (HoFaceOutput row in profile.outputs)
            {
                if (row == null || string.IsNullOrEmpty(row.parameter)) continue;
                HoFaceExpression expression;
                string parseError;
                if (!HoFaceExpression.TryParse(row.expression, out expression, out parseError))
                {
                    // 空表达式的行（门/契约行）= 由外部注入，这里当 0（和 chain-probe 一样）
                    values[row.parameter] = 0f;
                    continue;
                }

                float value = expression.Evaluate(
                    name =>
                    {
                        HoFaceOutput source;
                        string wire = inputRow.TryGetValue(name, out source) ? source.expression : name;
                        float rawValue;
                        if (!raw.TryGetValue(wire, out rawValue)) raw.TryGetValue(name, out rawValue);
                        if (source != null && source.curve != null) rawValue = HoFaceCurve.Transfer(source.curve, rawValue);
                        return rawValue;
                    },
                    name =>
                    {
                        float previous;
                        return values.TryGetValue(name, out previous) ? previous : 0f;
                    });

                values[row.parameter] = row.curve != null ? HoFaceCurve.Transfer(row.curve, value) : value;
            }

            Console.WriteLine("── " + Path.GetFileName(args[i]) + " ──");
            foreach (string key in Watch)
            {
                float value;
                Console.WriteLine("  {0,-30} {1,10:F4}", key, values.TryGetValue(key, out value) ? value : float.NaN);
            }
            string[] emo = { "Neutral", "Happy", "Anger", "Sad" };
            string[] clo = { "Closed", "Open", "Wide" };
            float[] w = new float[emo.Length];
            float[] c = new float[clo.Length];
            for (int k = 0; k < emo.Length; k++) w[k] = Get(values, "Ho/Drive/Lid/Left/Weight/" + emo[k]);
            for (int k = 0; k < clo.Length; k++) c[k] = Get(values, "Ho/Drive/Lid/Left/Closure/" + clo[k]);
            float se = w[0] + w[1] + w[2] + w[3];
            float sc = c[0] + c[1] + c[2];
            Console.WriteLine("  情绪 中性/喜/怒/悲 = {0:F3} {1:F3} {2:F3} {3:F3}   \u03a3={4:F6}", w[0], w[1], w[2], w[3], se);
            Console.WriteLine("  闭合 闭/睁/睁大   = {0:F3} {1:F3} {2:F3}   \u03a3={3:F6}   \u03a3(\u95e8)=\u03a3\u60c5\u7eea\u00d7\u03a3\u95ed\u5408={4:F6}", c[0], c[1], c[2], sc, se * sc);
            Console.WriteLine("  喜\u00d7\u7741\u5927 = {0:F6}\uff08\u5fc5\u987b\u6052\u4e3a 0\uff1a\u5426\u5219\u90a3\u4e2a\u4e0d\u5b58\u5728\u7684\u72b6\u6001\u70b9\u4f1a\u5403\u5230\u6743\u91cd\uff09", w[1] * c[2]);
        }
        return 0;
    }

    private static float Get(Dictionary<string, float> v, string k)
    {
        float x;
        return v.TryGetValue(k, out x) ? x : float.NaN;
    }

    private static Dictionary<string, float> ReadDump(string path)
    {
        var raw = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (string rawLine in File.ReadAllLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
            int split = line.IndexOf('=');
            if (split < 0) continue;
            string key = line.Substring(0, split).Trim();
            float value;
            float.TryParse(line.Substring(split + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            if (key.Length > 0) raw[key] = value;
        }
        return raw;
    }
}
