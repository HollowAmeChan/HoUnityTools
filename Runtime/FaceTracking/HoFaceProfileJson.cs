using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>
    /// 中间层配置（`*.hoface.json`）的 JSON 读写。
    ///
    /// <para>
    /// 读写走 <see cref="HoJsonReader"/>（我们自己的读取器），**不用 <c>JsonUtility</c>** ——
    /// 它在 Warudo 播放器里会静默丢掉 `List&lt;内部类&gt;` 字段，见 <see cref="HoJsonReader"/>
    /// 里记的那三次事故。这个类负责"配置长什么样"，语法细节交给读取器。
    /// </para>
    /// </summary>
    public static class HoFaceProfileJson
    {
        // ── 写 ──────────────────────────────────────────────────────────────────

        /// <summary>把一套中间层写成配置文件文本（2 空格缩进，可 diff）。</summary>
        public static string Write(HoFaceMiddleware middleware)
        {
            var text = new StringBuilder(4096);
            text.Append("{\n");
            text.Append("  \"format\": ").Append(Quote(HoFaceProfile.Format)).Append(",\n");
            text.Append("  \"version\": ").Append(HoFaceProfile.Version.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            text.Append("  \"displayName\": ").Append(Quote(middleware != null ? middleware.displayName : "")).Append(",\n");
            text.Append("  \"notes\": ").Append(Quote(middleware != null ? middleware.notes : "")).Append(",\n");
            text.Append("  \"inputs\": ");
            WriteRows(text, middleware != null ? middleware.inputs : null, 2);
            text.Append(",\n  \"outputs\": ");
            WriteRows(text, middleware != null ? middleware.outputs : null, 2);
            text.Append("\n}\n");
            return text.ToString();
        }

        private static void WriteRows(StringBuilder text, List<HoFaceOutput> rows, int indent)
        {
            if (rows == null || rows.Count == 0)
            {
                text.Append("[]");
                return;
            }

            string itemPad = new string(' ', indent + 2);
            string closePad = new string(' ', indent);
            text.Append("[\n");
            for (int i = 0; i < rows.Count; i++)
            {
                text.Append(itemPad);
                WriteRow(text, rows[i], indent + 2);
                text.Append(i < rows.Count - 1 ? ",\n" : "\n");
            }
            text.Append(closePad).Append(']');
        }

        private static void WriteRow(StringBuilder text, HoFaceOutput row, int indent)
        {
            if (row == null)
            {
                text.Append("null");
                return;
            }

            string inner = new string(' ', indent + 2);
            string close = new string(' ', indent);

            text.Append("{\n");
            text.Append(inner).Append("\"parameter\": ").Append(Quote(row.parameter)).Append(",\n");
            text.Append(inner).Append("\"expression\": ").Append(Quote(row.expression)).Append(",\n");
            // 默认值**每一行都写**（照 VBridger 的做法）：它是"没东西驱动它时是多少"，
            // 显式写出来才有可能被 diff / 被审 —— "全量默认值"要的就是这个。
            text.Append(inner).Append("\"defaultValue\": ").Append(Num(row.defaultValue)).Append(",\n");
            text.Append(inner).Append("\"notes\": ").Append(Quote(row.notes)).Append(",\n");

            text.Append(inner).Append("\"curve\": {");
            var keys = row.curve != null ? row.curve.keys : null;
            if (keys != null && keys.Length > 0)
            {
                text.Append("\n").Append(inner).Append("  \"keys\": [\n");
                for (int i = 0; i < keys.Length; i++)
                {
                    text.Append(inner).Append("    { \"t\": ").Append(Num(keys[i].time))
                        .Append(", \"v\": ").Append(Num(keys[i].value))
                        .Append(", \"inT\": ").Append(Num(keys[i].inTangent))
                        .Append(", \"outT\": ").Append(Num(keys[i].outTangent)).Append(" }");
                    text.Append(i < keys.Length - 1 ? ",\n" : "\n");
                }
                text.Append(inner).Append("  ]\n").Append(inner).Append("},\n");
            }
            else
            {
                text.Append(" \"keys\": [] },\n");
            }

            text.Append(inner).Append("\"modifiers\": ");
            WriteModifiers(text, row.modifiers, indent + 2);
            text.Append('\n').Append(close).Append('}');
        }

        private static void WriteModifiers(StringBuilder text, List<HoFaceModifier> modifiers, int indent)
        {
            if (modifiers == null || modifiers.Count == 0)
            {
                text.Append("[]");
                return;
            }

            string itemPad = new string(' ', indent + 2);
            string closePad = new string(' ', indent);
            text.Append("[\n");
            for (int i = 0; i < modifiers.Count; i++)
            {
                var modifier = modifiers[i];
                text.Append(itemPad);
                if (modifier == null)
                {
                    text.Append("null");
                }
                else
                {
                    text.Append("{ \"kind\": ").Append(Quote(KindName(modifier.kind)))
                        .Append(", \"seconds\": ").Append(Num(modifier.seconds)).Append(", \"steps\": ");
                    WriteSteps(text, modifier.steps, indent + 2);
                    text.Append(" }");
                }
                text.Append(i < modifiers.Count - 1 ? ",\n" : "\n");
            }
            text.Append(closePad).Append(']');
        }

        private static void WriteSteps(StringBuilder text, List<HoFaceStep> steps, int indent)
        {
            if (steps == null || steps.Count == 0)
            {
                text.Append("[]");
                return;
            }

            string itemPad = new string(' ', indent + 2);
            string closePad = new string(' ', indent);
            text.Append("[\n");
            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                text.Append(itemPad);
                if (step == null)
                {
                    text.Append("null");
                }
                else
                {
                    text.Append("{ \"trigger\": ").Append(Num(step.trigger))
                        .Append(", \"target\": ").Append(Num(step.target))
                        .Append(", \"hold\": ").Append(Num(step.hold))
                        .Append(", \"threshold\": ").Append(Num(step.threshold)).Append(" }");
                }
                text.Append(i < steps.Count - 1 ? ",\n" : "\n");
            }
            text.Append(closePad).Append(']');
        }

        private static string KindName(HoFaceModifierKind kind)
        {
            switch (kind)
            {
                case HoFaceModifierKind.Delay: return "delay";
                case HoFaceModifierKind.Steps: return "steps";
                default: return "smooth";
            }
        }

        /// <summary>JSON 字符串字面量。控制字符转义，非 ASCII 原样留着（文件是 UTF-8）。</summary>
        public static string Quote(string text)
        {
            if (string.IsNullOrEmpty(text)) return "\"\"";
            var quoted = new StringBuilder(text.Length + 2);
            quoted.Append('"');
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                switch (c)
                {
                    case '"': quoted.Append("\\\""); break;
                    case '\\': quoted.Append("\\\\"); break;
                    case '\b': quoted.Append("\\b"); break;
                    case '\f': quoted.Append("\\f"); break;
                    case '\n': quoted.Append("\\n"); break;
                    case '\r': quoted.Append("\\r"); break;
                    case '\t': quoted.Append("\\t"); break;
                    default:
                        if (c < ' ') quoted.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else quoted.Append(c);
                        break;
                }
            }
            quoted.Append('"');
            return quoted.ToString();
        }

        /// <summary>
        /// 数字：定点 6 位小数。**故意不用 "R"** —— 它在 Mono 上会写出 `0.100000001` 这种噪声，
        /// 而曲线关键点/时长的精度远用不到 6 位小数。明确用不变文化。
        /// </summary>
        public static string Num(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return "0";
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        // ── 读 ──────────────────────────────────────────────────────────────────

        /// <summary>读一份配置。失败时 <paramref name="error"/> 是中文说明（面板直接显示）。</summary>
        public static bool TryParse(string json, out HoFaceMiddleware middleware, out string error)
        {
            middleware = null;
            error = null;

            if (string.IsNullOrEmpty(json))
            {
                error = "配置文件是空的。";
                return false;
            }

            var result = new HoFaceMiddleware();
            var warnings = new List<string>();
            string format = null;

            try
            {
                var reader = new HoJsonReader(json);
                reader.ReadObject((key, r) =>
                {
                    switch (key)
                    {
                        case "format": format = r.ReadString(); break;
                        case "version": r.ReadNumber(); break;          // 保留字段，读进来不用
                        case "displayName": result.displayName = r.ReadString(); break;
                        case "notes": result.notes = r.ReadString(); break;
                        case "inputs": ParseRows(r, result.inputs, warnings); break;
                        case "outputs": ParseRows(r, result.outputs, warnings); break;
                        default: r.SkipValue(); break;                   // 未知字段：跳过（向前兼容）
                    }
                });
            }
            catch (Exception e)
            {
                error = "JSON 解析失败：" + e.Message;
                return false;
            }

            if (!string.IsNullOrEmpty(format) && format != HoFaceProfile.Format)
            {
                error = "这不是 Ho 的中间层配置（format = " + format + "）。";
                return false;
            }

            if (result.outputs.Count == 0)
            {
                error = "配置文件里一行输出都没有。";
                return false;
            }

            middleware = result;
            if (warnings.Count > 0) error = string.Join("；", warnings.ToArray());
            return true;
        }

        /// <summary>把一类行（输入行 / 输出行）读进来。两类行形状完全一样。</summary>
        private static void ParseRows(HoJsonReader reader, List<HoFaceOutput> into, List<string> warnings)
        {
            reader.ReadArray(item =>
            {
                var row = new HoFaceOutput();
                item.ReadObject((key, r) =>
                {
                    switch (key)
                    {
                        case "parameter": row.parameter = r.ReadString(); break;
                        case "expression": row.expression = r.ReadString(); break;
                        case "defaultValue": row.defaultValue = r.ReadFloat(); break;
                        case "notes": row.notes = r.ReadString(); break;
                        case "curve": ReadCurve(r, row); break;
                        case "modifiers": ReadModifiers(r, row, warnings); break;
                        default: r.SkipValue(); break;
                    }
                });
                if (!string.IsNullOrEmpty(row.parameter)) into.Add(row);
            });
        }

        private static void ReadCurve(HoJsonReader reader, HoFaceOutput row)
        {
            var keys = new List<Keyframe>();
            reader.ReadObject((key, r) =>
            {
                if (key != "keys") { r.SkipValue(); return; }
                r.ReadArray(item =>
                {
                    float time = 0f, value = 0f, inTangent = 0f, outTangent = 0f;
                    item.ReadObject((field, f) =>
                    {
                        switch (field)
                        {
                            case "t": time = f.ReadFloat(); break;
                            case "v": value = f.ReadFloat(); break;
                            case "inT": inTangent = f.ReadFloat(); break;
                            case "outT": outTangent = f.ReadFloat(); break;
                            default: f.SkipValue(); break;
                        }
                    });
                    keys.Add(new Keyframe(time, value, inTangent, outTangent));
                });
            });

            if (keys.Count > 0) row.curve = new AnimationCurve(keys.ToArray());
        }

        private static void ReadModifiers(HoJsonReader reader, HoFaceOutput row, List<string> warnings)
        {
            reader.ReadArray(item =>
            {
                // 先把字段收进局部量，最后再建对象 —— kind 认不出时可以直接丢掉整条，
                // 不会出现"对象建到一半发现不认识"的中间态。
                string kind = "smooth";
                float seconds = 0f;
                var steps = new List<HoFaceStep>();

                item.ReadObject((key, r) =>
                {
                    switch (key)
                    {
                        case "kind": kind = r.ReadString(); break;
                        case "seconds": seconds = r.ReadFloat(); break;
                        case "steps": r.ReadArray(step =>
                        {
                            var parsed = new HoFaceStep();
                            step.ReadObject((field, f) =>
                            {
                                switch (field)
                                {
                                    case "trigger": parsed.trigger = f.ReadFloat(); break;
                                    case "target": parsed.target = f.ReadFloat(); break;
                                    case "hold": parsed.hold = f.ReadFloat(); break;
                                    case "threshold": parsed.threshold = f.ReadFloat(); break;
                                    default: f.SkipValue(); break;
                                }
                            });
                            steps.Add(parsed);
                        });
                        break;
                        default: r.SkipValue(); break;
                    }
                });

                HoFaceModifierKind parsedKind;
                bool known = true;
                switch ((kind ?? "").ToLowerInvariant())
                {
                    case "smooth": parsedKind = HoFaceModifierKind.Smooth; break;
                    case "delay": parsedKind = HoFaceModifierKind.Delay; break;
                    case "steps": parsedKind = HoFaceModifierKind.Steps; break;
                    default:
                        warnings.Add("认不出的修饰符 kind = " + kind + "（" + row.parameter + "）");
                        parsedKind = HoFaceModifierKind.Smooth;
                        known = false;
                        break;
                }

                if (!known) return;
                var modifier = new HoFaceModifier { kind = parsedKind, seconds = seconds };
                modifier.steps = steps;
                row.modifiers.Add(modifier);
            });
        }
    }
}
