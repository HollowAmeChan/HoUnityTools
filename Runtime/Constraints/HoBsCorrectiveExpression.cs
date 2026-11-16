using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Hollow.HoUnityTools.FaceTracking;

namespace Hollow.HoUnityTools.Constraints
{
    /// <summary>The deliberately small, deterministic expression subset shared with HoTools BS drivers.</summary>
    public sealed class HoBsCorrectiveExpression
    {
        private static readonly Regex Token = new Regex(
            @"\G(?:\s+|(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?|[A-Za-z_][A-Za-z0-9_]*|\*\*|[+\-*/(),])",
            RegexOptions.CultureInvariant);
        private static readonly HashSet<string> Functions = new HashSet<string>(StringComparer.Ordinal)
        { "min", "max", "abs", "clamp" };
        private readonly HoFaceExpression expression;

        private HoBsCorrectiveExpression(HoFaceExpression expression) { this.expression = expression; }

        /// <summary>Compile once. Unknown names and functions are errors rather than silently reading zero.</summary>
        public static bool TryParse(string text, IEnumerable<string> variableNames,
            out HoBsCorrectiveExpression result, out string error)
        {
            result = null;
            error = null;
            if (string.IsNullOrWhiteSpace(text)) { error = "表达式为空"; return false; }
            if (text.Length > 1024) { error = "表达式超过 1024 个字符"; return false; }
            var names = new HashSet<string>(StringComparer.Ordinal) { "x", "y", "z", "pi" };
            if (variableNames != null)
                foreach (string name in variableNames)
                {
                    if (!IsValidVariableName(name) || !names.Add(name))
                    { error = "变量名称无效或重复：" + name; return false; }
                }
            var normalized = new StringBuilder();
            int position = 0, count = 0;
            while (position < text.Length)
            {
                Match match = Token.Match(text, position);
                if (!match.Success || match.Index != position)
                { error = "第 " + (position + 1) + " 个字符不在跨端表达式白名单中"; return false; }
                string token = match.Value;
                position += match.Length;
                if (char.IsWhiteSpace(token[0])) { normalized.Append(' '); continue; }
                if (++count > 256) { error = "表达式超过 256 个记号"; return false; }
                if (char.IsLetter(token[0]) || token[0] == '_')
                {
                    int next = position;
                    while (next < text.Length && char.IsWhiteSpace(text[next])) next++;
                    if (next < text.Length && text[next] == '(')
                    {
                        if (!Functions.Contains(token)) { error = "不支持的函数：" + token; return false; }
                    }
                    else if (!names.Contains(token)) { error = "未定义变量：" + token; return false; }
                }
                normalized.Append(token == "**" ? "^" : token == "pi" ?
                    Math.PI.ToString("R", CultureInfo.InvariantCulture) : token);
            }
            if (!HoFaceExpression.TryParse(normalized.ToString(), out HoFaceExpression parsed, out error)) return false;
            result = new HoBsCorrectiveExpression(parsed);
            return true;
        }

        public static bool IsValidVariableName(string name)
        {
            if (string.IsNullOrEmpty(name) || name == "x" || name == "y" || name == "z" || name == "pi" ||
                Functions.Contains(name) || !IsIdentifierStart(name[0])) return false;
            for (int i = 1; i < name.Length; i++)
                if (!IsIdentifierStart(name[i]) && (name[i] < '0' || name[i] > '9')) return false;
            return true;
        }

        private static bool IsIdentifierStart(char value) =>
            value >= 'a' && value <= 'z' || value >= 'A' && value <= 'Z' || value == '_';

        public float Evaluate(Func<string, float> readVariable) => expression.Evaluate(readVariable);
    }
}
