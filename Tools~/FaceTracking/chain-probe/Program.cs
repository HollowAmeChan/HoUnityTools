// Throwaway probe: run the REAL parameter-layer chain (Core/HoFaceChain.cs, synced by sync.ps1)
// against the REAL shipped device profiles, and answer the user's question directly:
//
//     "原始值 vs 输出值 全部对不对得上"   (raw wire value  ==  exit parameter value)
//
// Exit key rules that matter (learned the hard way while writing this probe):
//   * `ARKit/<shape>`  -> canonical shape name, via HoFaceTrackingChannels
//   * anything else    -> the parameter kept VERBATIM, so the reserved names are literally
//                         `Head/RotX`, `Head/PosX`, ... (capital H, slash) -- NOT `headRotX`.
//                         Probing for `headRotX` reports a phantom 0 for every head row.
//   * The exit dictionary contains OUTPUT keys only. An input row's canonical name is not a
//     key of `Parameters`; converting one into the other is what produced the earlier bogus
//     "27 rows mismatch" table. Do not look input rows up in `Parameters`.
//
// Usage: chain-probe <profile.hoface.json> [wireDump.txt]
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Hollow.HoUnityTools.FaceTracking;

internal static class Program
{
    /// <summary>求解器认的保留名（Core/HoFaceSolver.cs 的 Targets）。写在这里为了钉住拼写。</summary>
    private static readonly string[] Reserved =
    {
        "Head/RotX", "Head/RotY", "Head/RotZ",
        "Head/PosX", "Head/PosY", "Head/PosZ",
        "Root/PosX", "Root/PosY", "Root/PosZ",
    };

    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("usage: chain-probe <profile.hoface.json> [wireDump.txt]");
            return 2;
        }

        string profilePath = args[0];
        if (!File.Exists(profilePath)) { Console.WriteLine("no such profile: " + profilePath); return 2; }

        HoFaceMiddleware profile;
        string error;
        if (!HoFaceProfile.TryParse(File.ReadAllText(profilePath), out profile, out error))
        {
            Console.WriteLine("profile parse FAILED: " + error);
            return 1;
        }

        bool synthetic = args.Length < 2;
        Dictionary<string, float> raw = synthetic ? Synthesise(profile) : ReadDump(args[1]);

        Console.WriteLine("profile   : " + Path.GetFileName(profilePath));
        Console.WriteLine("vector    : " + (synthetic ? "SYNTHETIC (distinct values per wire)" : args[1]));
        Console.WriteLine("raw wires : " + raw.Count);
        Console.WriteLine();

        // 输入行：规范名 -> 线名（一个规范名可能对应多条线，取最后声明的那条 = 链条的覆盖规则）
        var canonicalToWire = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (HoFaceOutput row in profile.inputs) canonicalToWire[row.parameter] = row.expression;

        var chain = new HoFaceChain(profile);
        chain.Evaluate(raw, 1f / 60f, 0d);

        // ① 出口键 = 线名（这些预设不做任何改名，见 docs/measurements/README.md 5.1）
        Console.WriteLine("── ① 出口键：必须就是设备线名（零改名）──");
        int renamed = 0, exitCount = 0;
        foreach (HoFaceOutput row in profile.inputs)
        {
            exitCount++;
            if (row.parameter != row.expression)
            {
                renamed++;
                if (renamed <= 5) Console.WriteLine("  !! 改了名：" + row.expression + " -> " + row.parameter);
            }
        }
        Console.WriteLine("  输入行 " + exitCount + " 条 · 改名 " + renamed + " 条");
        Console.WriteLine();

        // 出口键规则（Chain.OutputKey）：`ARKit/xxx` 剥掉前缀、经通道表换成**裸规范名**；
        // 其余参数**原样**（所以保留名就是 `Head/RotX` 这个拼写，求解器认的就是它）。
        Console.WriteLine("── ② 输出行：出口值 vs 原始线值（你的判据）──");
        Console.WriteLine("  {0,-24} {1,-14} {2,-14} {3,12} {4,12}  {5}",
            "parameter(配置)", "line", "wire", "raw", "exit", "verdict");

        int identical = 0, transformed = 0, noSource = 0, wireMissing = 0, keyMissing = 0;
        foreach (HoFaceOutput row in profile.outputs)
        {
            string key = ExitKey(row.parameter);
            float exit;
            bool keyPresent = chain.Parameters.TryGetValue(key, out exit);
            if (!keyPresent) keyMissing++;

            // 真源：**输出行的 expression** 指到的那条输入行（它才是线名）。别假设两者同名。
            HoFaceOutput inputRow = FindInputByParameter(profile, row.expression);
            float expected = 0f;
            bool hasSource = inputRow != null;

            string wire;
            float rawValue = 0f;
            bool hasWire = hasSource
                && (wire = inputRow.expression) != null
                && raw.TryGetValue(wire, out rawValue);

            string verdict;
            if (!hasSource) { noSource++; verdict = "(没有对应的输入行)"; }
            else if (!hasWire) { wireMissing++; verdict = "(线没来)"; }
            else if (!keyPresent) { verdict = "!! 出口字典里没有键 " + key; }
            else
            {
                // 期望值 = 原值过输入行的曲线，再过输出行的曲线（两层都会夹）
                expected = row.Transform(HoFaceCurve.Transfer(inputRow.curve, rawValue));
                if (Near(expected, exit)) { identical++; verdict = "逐位相等 ok"; }
                else { transformed++; verdict = "!! 差 " + (exit - expected).ToString("F4"); }
            }

            Console.WriteLine("  {0,-24} {1,-22} {2,12} {3,12}  {4}",
                row.parameter, hasSource ? inputRow.expression : "-",
                hasWire ? rawValue.ToString("F4") : "-", keyPresent ? exit.ToString("F4") : "-", verdict);
        }

        Console.WriteLine();
        Console.WriteLine("输出行 " + profile.outputs.Count + " 条：" +
            "逐位相等 " + identical + " · 被改 " + transformed +
            " · 无输入行 " + noSource + " · 线没来 " + wireMissing +
            " · 出口字典缺键 " + keyMissing);

        Sweep(profile);
        return 0;
    }

    private static HoFaceOutput FindInputByParameter(HoFaceMiddleware profile, string parameter)
    {
        foreach (HoFaceOutput row in profile.inputs)
            if (row.parameter == parameter) return row;
        return null;
    }

    /// <summary>
    /// 多条向量扫一遍：出口值必须等于"原值过输入行曲线、再过输出行曲线"。
    /// 顺带量一下范围外的值会不会被曲线夹掉。
    /// </summary>
    private static void Sweep(HoFaceMiddleware profile)
    {
        Console.WriteLine();
        Console.WriteLine("── ③ 扫描：多条向量 × 每条输出行，出口是否等于两层曲线算出来的值 ──");

        int checks = 0, bad = 0;
        string firstBad = null;

        for (int step = 0; step < 8; step++)
        {
            var raw = new Dictionary<string, float>(StringComparer.Ordinal);
            int n = 0;
            foreach (HoFaceOutput row in profile.inputs)
            {
                n++;
                raw[row.expression] = step == 0 ? ScalarFor(row.expression, n)
                    : InRangeValue(row.expression, n, step);
            }

            var chain = new HoFaceChain(profile);
            chain.Evaluate(raw, 1f / 60f, 0d);

            foreach (HoFaceOutput row in profile.outputs)
            {
                HoFaceOutput inputRow = FindInputByParameter(profile, row.expression);
                if (inputRow == null) continue;
                float rawValue;
                if (!raw.TryGetValue(inputRow.expression, out rawValue)) continue;
                float exit;
                if (!chain.Parameters.TryGetValue(ExitKey(row.parameter), out exit)) continue;

                checks++;
                float expected = row.Transform(HoFaceCurve.Transfer(inputRow.curve, rawValue));
                if (!Near(expected, exit))
                {
                    bad++;
                    if (firstBad == null)
                        firstBad = row.parameter + " raw=" + rawValue.ToString("F4")
                            + " expect=" + expected.ToString("F4") + " exit=" + exit.ToString("F4");
                }
            }
        }

        Console.WriteLine("  核对 " + checks + " 次 · 不相等 " + bad
            + (firstBad != null ? "  首个：" + firstBad : ""));

        // 范围外：曲线只夹不外推，超范围就会被夹住。这里量的是**夹的边界**。
        Console.WriteLine();
        Console.WriteLine("── ④ 给到范围外会不会被曲线夹掉（拿 Position_y / Rotation_x 试）──");
        foreach (string wire in new[] { "Position_x", "Position_y", "Position_z", "Rotation_x", "Rotation_y", "Rotation_z" })
        {
            var raw = SynthRaw(profile);
            raw[wire] = 250f;
            var c = new HoFaceChain(profile);
            c.Evaluate(raw, 1f / 60f, 0d);
            float exit;
            bool got = c.Parameters.TryGetValue(wire, out exit);
            Console.WriteLine("  {0,-12} = 250 → 出口 {1} = {2}", wire, wire, got ? exit.ToString("F4") : "(没有键)");
        }
    }

    /// <summary>照 Chain.OutputKey 复算出口键：ARKit/ 前缀剥掉并归一成规范名，其余原样。</summary>
    private static string ExitKey(string parameter)
    {
        if (string.IsNullOrEmpty(parameter)) return parameter;
        const string prefix = "ARKit/";
        if (!parameter.StartsWith(prefix, StringComparison.Ordinal)) return parameter;
        string shape = parameter.Substring(prefix.Length);
        int channel = HoFaceTrackingChannels.IndexOf(shape);
        return channel >= 0 ? HoFaceTrackingChannels.Names[channel] : shape;
    }

    private static Dictionary<string, float> SynthRaw(HoFaceMiddleware profile)
    {
        var raw = new Dictionary<string, float>(StringComparer.Ordinal);
        int n = 0;
        foreach (HoFaceOutput row in profile.inputs) { n++; raw[row.expression] = ScalarFor(row.expression, n); }
        return raw;
    }

    private static Dictionary<string, float> Synthesise(HoFaceMiddleware profile)
    {
        return SynthRaw(profile);
    }

    private static float ScalarFor(string wire, int index)
    {
        if (wire.StartsWith("Rotation_", StringComparison.Ordinal)) return 30f;
        if (wire.StartsWith("Position_", StringComparison.Ordinal)) return 5f;
        if (wire.StartsWith("Eye", StringComparison.Ordinal)) return 0.2f;
        if (wire == "FaceFound") return 1f;
        return index / 100f;
    }

    /// <summary>范围**内**的另一组值（用来证明不是端点巧合）。</summary>
    private static float InRangeValue(string wire, int index, int step)
    {
        if (wire.StartsWith("Rotation_", StringComparison.Ordinal)) return -60f + step * 20f;
        if (wire.StartsWith("Position_", StringComparison.Ordinal)) return -1f + step * 0.4f;
        if (wire.StartsWith("Eye", StringComparison.Ordinal)) return -0.6f + step * 0.2f;
        if (wire == "FaceFound") return 1f;
        return Math.Min(1f, 0.05f + (index + step * 11) / 100f);
    }

    private static Dictionary<string, float> ReadDump(string path)
    {
        var raw = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (string rawLine in File.ReadAllLines(path))
        {
            string line = rawLine.Trim().TrimEnd(',');
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

            string key = line;
            string valueText = null;

            int eq = line.IndexOf('=');
            int colon = line.IndexOf(':');
            int split = eq >= 0 ? eq : colon;
            if (split >= 0)
            {
                key = line.Substring(0, split).Trim().Trim('"');
                valueText = line.Substring(split + 1).Trim();
                if (valueText.EndsWith("}", StringComparison.Ordinal)) valueText = valueText.Substring(0, valueText.Length - 1).Trim();
            }
            if (key.Length == 0) continue;

            float value = 0f;
            if (valueText != null)
                float.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            raw[key] = value;
        }
        return raw;
    }

    private static bool Near(float a, float b) { return Math.Abs(a - b) < 0.0001f; }
}
