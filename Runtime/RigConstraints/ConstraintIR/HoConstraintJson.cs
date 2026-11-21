using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Hollow.HoUnityTools.RigConstraints.ConstraintIR
{
    public enum HoConstraintJsonKind { Null, Boolean, Number, String, Array, Object }

    /// <summary>A JSON DOM, including unknown fields and the original number tokens. No Unity DTO owns the source document.</summary>
    public sealed class HoConstraintJsonValue
    {
        public HoConstraintJsonKind Kind { get; private set; }
        public string Text { get; private set; }
        public bool Boolean { get; private set; }
        public List<HoConstraintJsonValue> Items { get; private set; }
        public Dictionary<string, HoConstraintJsonValue> Properties { get; private set; }

        private HoConstraintJsonValue(HoConstraintJsonKind kind) { Kind = kind; }
        public static HoConstraintJsonValue Null() { return new HoConstraintJsonValue(HoConstraintJsonKind.Null); }
        public static HoConstraintJsonValue String(string value) { return new HoConstraintJsonValue(HoConstraintJsonKind.String) { Text = value ?? "" }; }
        public static HoConstraintJsonValue Bool(bool value) { return new HoConstraintJsonValue(HoConstraintJsonKind.Boolean) { Boolean = value }; }
        internal static HoConstraintJsonValue NumberToken(string value) { return new HoConstraintJsonValue(HoConstraintJsonKind.Number) { Text = value }; }
        public static HoConstraintJsonValue Array() { return new HoConstraintJsonValue(HoConstraintJsonKind.Array) { Items = new List<HoConstraintJsonValue>() }; }
        public static HoConstraintJsonValue Object() { return new HoConstraintJsonValue(HoConstraintJsonKind.Object) { Properties = new Dictionary<string, HoConstraintJsonValue>(StringComparer.Ordinal) }; }
        public HoConstraintJsonValue Get(string key) { HoConstraintJsonValue value; return Kind == HoConstraintJsonKind.Object && Properties.TryGetValue(key, out value) ? value : null; }
        public string StringOr(string fallback = null) { return Kind == HoConstraintJsonKind.String ? Text : fallback; }
        public string StringAt(string key, string fallback = null) { var value = Get(key); return value == null ? fallback : value.StringOr(fallback); }
        public int Int32()
        {
            int value;
            if (Kind != HoConstraintJsonKind.Number || !int.TryParse(Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value))
                throw new FormatException("Expected a JSON int32, received " + Kind + ".");
            return value;
        }
        public float Single()
        {
            float value;
            if (Kind != HoConstraintJsonKind.Number || !float.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || float.IsNaN(value) || float.IsInfinity(value))
                throw new FormatException("Expected a finite single precision JSON number.");
            return value;
        }
        public HoConstraintJsonValue Clone() { return HoConstraintJson.Parse(HoConstraintJson.Serialize(this)); }
    }

    /// <summary>Strict standard-library JSON. Whitespace is normalized; values, unknown members, and number tokens are preserved.</summary>
    public static class HoConstraintJson
    {
        public static HoConstraintJsonValue Parse(string json) { return new Parser(json ?? "").Read(); }
        public static string Serialize(HoConstraintJsonValue value, bool sortKeys = false)
        {
            var text = new StringBuilder(); Write(text, value, sortKeys, 0); return text.ToString();
        }
        public static string Sha256(string text)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(new UTF8Encoding(false, true).GetBytes(text));
                var result = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) result.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }
        private static void Write(StringBuilder text, HoConstraintJsonValue value, bool sort, int depth)
        {
            if (value == null || depth > 256) throw new FormatException("Null DOM member or excessive JSON depth.");
            switch (value.Kind)
            {
                case HoConstraintJsonKind.Null: text.Append("null"); break;
                case HoConstraintJsonKind.Boolean: text.Append(value.Boolean ? "true" : "false"); break;
                case HoConstraintJsonKind.Number: text.Append(value.Text); break;
                case HoConstraintJsonKind.String: Quote(text, value.Text); break;
                case HoConstraintJsonKind.Array:
                    text.Append('[');
                    for (int i = 0; i < value.Items.Count; i++) { if (i > 0) text.Append(','); Write(text, value.Items[i], sort, depth + 1); }
                    text.Append(']'); break;
                case HoConstraintJsonKind.Object:
                    text.Append('{'); var keys = new List<string>(value.Properties.Keys);
                    if (sort) keys.Sort(CompareUnicode);
                    for (int i = 0; i < keys.Count; i++) { if (i > 0) text.Append(','); Quote(text, keys[i]); text.Append(':'); Write(text, value.Properties[keys[i]], sort, depth + 1); }
                    text.Append('}'); break;
            }
        }
        private static int CompareUnicode(string a, string b)
        {
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                int x = char.ConvertToUtf32(a, i), y = char.ConvertToUtf32(b, j);
                if (x != y) return x < y ? -1 : 1;
                i += x > 0xffff ? 2 : 1; j += y > 0xffff ? 2 : 1;
            }
            return (a.Length - i).CompareTo(b.Length - j);
        }
        private static void Quote(StringBuilder text, string value)
        {
            text.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': text.Append("\\\""); break; case '\\': text.Append("\\\\"); break;
                    case '\b': text.Append("\\b"); break; case '\f': text.Append("\\f"); break;
                    case '\n': text.Append("\\n"); break; case '\r': text.Append("\\r"); break; case '\t': text.Append("\\t"); break;
                    default:
                        if (c < 32) text.Append("\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else if (char.IsHighSurrogate(c))
                        {
                            if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1])) throw new FormatException("Unpaired high surrogate.");
                            text.Append(c); text.Append(value[++i]);
                        }
                        else if (char.IsLowSurrogate(c)) throw new FormatException("Unpaired low surrogate.");
                        else text.Append(c);
                        break;
                }
            }
            text.Append('"');
        }

        private sealed class Parser
        {
            private readonly string text; private int position;
            internal Parser(string text) { this.text = text; if (text.Length > 0 && text[0] == '\ufeff') position = 1; }
            private FormatException Error(string message) { return new FormatException(message + " At JSON character " + position + "."); }
            private void Space() { while (position < text.Length && (text[position] == ' ' || text[position] == '\t' || text[position] == '\r' || text[position] == '\n')) position++; }
            private bool Eat(char c) { Space(); if (position < text.Length && text[position] == c) { position++; return true; } return false; }
            private void Need(char c) { if (!Eat(c)) throw Error("Expected '" + c + "'"); }
            internal HoConstraintJsonValue Read() { var value = Value(0); Space(); if (position != text.Length) throw Error("Trailing JSON content"); return value; }
            private HoConstraintJsonValue Value(int depth)
            {
                Space(); if (depth > 256 || position >= text.Length) throw Error("Missing value or excessive depth");
                char c = text[position];
                if (c == '"') return HoConstraintJsonValue.String(ReadString());
                if (c == '{')
                {
                    position++; var result = HoConstraintJsonValue.Object(); if (Eat('}')) return result;
                    do { Space(); if (position >= text.Length || text[position] != '"') throw Error("Expected object key"); string key = ReadString(); Need(':'); if (result.Properties.ContainsKey(key)) throw Error("Duplicate object key " + key); result.Properties.Add(key, Value(depth + 1)); } while (Eat(','));
                    Need('}'); return result;
                }
                if (c == '[')
                {
                    position++; var result = HoConstraintJsonValue.Array(); if (Eat(']')) return result;
                    do { result.Items.Add(Value(depth + 1)); } while (Eat(',')); Need(']'); return result;
                }
                if (c == 't') { Literal("true"); return HoConstraintJsonValue.Bool(true); }
                if (c == 'f') { Literal("false"); return HoConstraintJsonValue.Bool(false); }
                if (c == 'n') { Literal("null"); return HoConstraintJsonValue.Null(); }
                if (c == '-' || c >= '0' && c <= '9') return ReadNumber();
                throw Error("Invalid JSON value");
            }
            private void Literal(string literal) { if (position + literal.Length > text.Length || string.CompareOrdinal(text, position, literal, 0, literal.Length) != 0) throw Error("Invalid literal"); position += literal.Length; }
            private bool Digit() { return position < text.Length && text[position] >= '0' && text[position] <= '9'; }
            private HoConstraintJsonValue ReadNumber()
            {
                int begin = position; if (text[position] == '-') position++;
                if (!Digit()) throw Error("Missing integer digits");
                if (text[position] == '0') position++; else while (Digit()) position++;
                if (position < text.Length && text[position] == '.') { position++; if (!Digit()) throw Error("Missing fractional digits"); while (Digit()) position++; }
                if (position < text.Length && (text[position] == 'e' || text[position] == 'E')) { position++; if (position < text.Length && (text[position] == '+' || text[position] == '-')) position++; if (!Digit()) throw Error("Missing exponent digits"); while (Digit()) position++; }
                return HoConstraintJsonValue.NumberToken(text.Substring(begin, position - begin));
            }
            private string ReadString()
            {
                Need('"'); var result = new StringBuilder();
                while (position < text.Length)
                {
                    char c = text[position++]; if (c == '"') { ValidateUnicode(result); return result.ToString(); }
                    if (c < 32) throw Error("Unescaped control character");
                    if (c != '\\') { result.Append(c); continue; }
                    if (position >= text.Length) throw Error("Incomplete escape");
                    switch (text[position++])
                    {
                        case '"': result.Append('"'); break; case '\\': result.Append('\\'); break; case '/': result.Append('/'); break;
                        case 'b': result.Append('\b'); break; case 'f': result.Append('\f'); break; case 'n': result.Append('\n'); break; case 'r': result.Append('\r'); break; case 't': result.Append('\t'); break;
                        case 'u':
                            if (position + 4 > text.Length) throw Error("Incomplete Unicode escape");
                            int value; if (!int.TryParse(text.Substring(position, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value)) throw Error("Invalid Unicode escape");
                            position += 4; result.Append((char)value); break;
                        default: throw Error("Invalid escape");
                    }
                }
                throw Error("Unterminated string");
            }
            private void ValidateUnicode(StringBuilder value)
            {
                for (int i = 0; i < value.Length; i++)
                    if (char.IsHighSurrogate(value[i])) { if (++i >= value.Length || !char.IsLowSurrogate(value[i])) throw Error("Unpaired high surrogate"); }
                    else if (char.IsLowSurrogate(value[i])) throw Error("Unpaired low surrogate");
            }
        }
    }
}
