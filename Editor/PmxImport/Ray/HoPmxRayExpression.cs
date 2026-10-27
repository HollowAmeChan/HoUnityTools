using System;
using System.Collections.Generic;
using System.Globalization;

namespace Hollow.HoUnityTools.Editor.Pmx
{
    // Deliberately small data-only expression reader. No shader code, script engine,
    // reflection or operating-system process is evaluated when reading FX settings.
    internal sealed class HoPmxRayExpression
    {
        private readonly string text;
        private readonly IDictionary<string, string> macros;
        private readonly bool undefinedIsZero;
        private readonly int expansionDepth;
        private int position;
        private int tokens;
        private string token;

        private HoPmxRayExpression(string text, IDictionary<string, string> macros, bool undefinedIsZero, int expansionDepth)
        {
            this.text = text;
            this.macros = macros;
            this.undefinedIsZero = undefinedIsZero;
            this.expansionDepth = expansionDepth;
            Next();
        }

        public static bool TryEvaluate(string expression, IDictionary<string, string> macros, bool undefinedIsZero, out double value)
        {
            return TryEvaluate(expression, macros, undefinedIsZero, 0, out value);
        }

        private static bool TryEvaluate(string expression, IDictionary<string, string> macros, bool undefinedIsZero, int depth, out double value)
        {
            value = 0;
            if (depth > 16 || expression == null || expression.Length > 4096) return false;
            try
            {
                var reader = new HoPmxRayExpression(expression, macros, undefinedIsZero, depth);
                value = reader.Conditional();
                return reader.token.Length == 0 && !double.IsNaN(value) && !double.IsInfinity(value);
            }
            catch (FormatException) { return false; }
            catch (OverflowException) { return false; }
        }

        private double Conditional()
        {
            double value = LogicalOr();
            if (Take("?"))
            {
                double yes = Conditional();
                Require(":");
                double no = Conditional();
                return value != 0 ? yes : no;
            }
            return value;
        }
        private double LogicalOr()
        {
            double value = LogicalAnd();
            while (Take("||")) { double right = LogicalAnd(); value = value != 0 || right != 0 ? 1 : 0; }
            return value;
        }
        private double LogicalAnd()
        {
            double value = BitOr();
            while (Take("&&")) { double right = BitOr(); value = value != 0 && right != 0 ? 1 : 0; }
            return value;
        }
        private double BitOr()
        {
            double value = BitXor();
            while (Take("|")) value = (long)value | (long)BitXor();
            return value;
        }
        private double BitXor()
        {
            double value = BitAnd();
            while (Take("^")) value = (long)value ^ (long)BitAnd();
            return value;
        }
        private double BitAnd()
        {
            double value = Equality();
            while (Take("&")) value = (long)value & (long)Equality();
            return value;
        }
        private double Equality()
        {
            double value = Relational();
            while (token == "==" || token == "!=")
            {
                string operation = token; Next();
                double right = Relational();
                value = (operation == "==" ? value == right : value != right) ? 1 : 0;
            }
            return value;
        }
        private double Relational()
        {
            double value = Shift();
            while (token == "<" || token == ">" || token == "<=" || token == ">=")
            {
                string operation = token; Next();
                double right = Shift();
                bool result = operation == "<" ? value < right : operation == ">" ? value > right : operation == "<=" ? value <= right : value >= right;
                value = result ? 1 : 0;
            }
            return value;
        }
        private double Shift()
        {
            double value = Add();
            while (token == "<<" || token == ">>")
            {
                string operation = token; Next();
                int right = (int)Add();
                if (right < 0 || right > 63) throw new FormatException();
                value = operation == "<<" ? (long)value << right : (long)value >> right;
            }
            return value;
        }
        private double Add()
        {
            double value = Multiply();
            while (token == "+" || token == "-")
            {
                string operation = token; Next();
                double right = Multiply();
                value = operation == "+" ? value + right : value - right;
            }
            return value;
        }
        private double Multiply()
        {
            double value = Unary();
            while (token == "*" || token == "/" || token == "%")
            {
                string operation = token; Next();
                double right = Unary();
                if ((operation == "/" || operation == "%") && right == 0) throw new FormatException();
                value = operation == "*" ? value * right : operation == "/" ? value / right : value % right;
            }
            return value;
        }
        private double Unary()
        {
            if (Take("!")) return Unary() == 0 ? 1 : 0;
            if (Take("~")) return ~(long)Unary();
            if (Take("-")) return -Unary();
            if (Take("+")) return Unary();
            if (Take("(")) { double value = Conditional(); Require(")"); return value; }
            if (Take("defined"))
            {
                bool parentheses = Take("(");
                string identifier = token;
                if (!Identifier(identifier)) throw new FormatException();
                Next();
                if (parentheses) Require(")");
                return macros.ContainsKey(identifier) ? 1 : 0;
            }
            string current = token;
            if (current.Length == 0) throw new FormatException();
            Next();
            if (current == "true") return 1;
            if (current == "false") return 0;
            if (Identifier(current))
            {
                string expression;
                if (!macros.TryGetValue(current, out expression))
                {
                    if (undefinedIsZero) return 0;
                    throw new FormatException();
                }
                double value;
                if (TryEvaluate(expression, macros, undefinedIsZero, expansionDepth + 1, out value)) return value;
                throw new FormatException();
            }
            current = current.TrimEnd('f', 'F', 'u', 'U', 'l', 'L');
            if (current.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return Convert.ToInt64(current.Substring(2), 16);
            double number;
            if (!double.TryParse(current, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) throw new FormatException();
            return number;
        }

        private static bool Identifier(string value)
        {
            return value.Length > 0 && (char.IsLetter(value[0]) || value[0] == '_');
        }
        private bool Take(string value)
        {
            if (token != value) return false;
            Next();
            return true;
        }
        private void Require(string value) { if (!Take(value)) throw new FormatException(); }

        private void Next()
        {
            if (++tokens > 512) throw new FormatException();
            while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
            if (position == text.Length) { token = ""; return; }
            int start = position;
            char first = text[position++];
            if (char.IsLetter(first) || first == '_')
            {
                while (position < text.Length && (char.IsLetterOrDigit(text[position]) || text[position] == '_')) position++;
            }
            else if (char.IsDigit(first) || first == '.')
            {
                bool hex = first == '0' && position < text.Length && (text[position] == 'x' || text[position] == 'X');
                if (hex) position++;
                while (position < text.Length)
                {
                    char c = text[position];
                    if (char.IsLetterOrDigit(c) || c == '.') { position++; continue; }
                    if (!hex && (c == '+' || c == '-') && position > start && (text[position - 1] == 'e' || text[position - 1] == 'E')) { position++; continue; }
                    break;
                }
            }
            else if (position < text.Length)
            {
                char next = text[position];
                if ((first == '|' && next == '|') || (first == '&' && next == '&') ||
                    (first == '<' && next == '<') || (first == '>' && next == '>') ||
                    ((first == '!' || first == '=' || first == '<' || first == '>') && next == '=')) position++;
            }
            token = text.Substring(start, position - start);
        }
    }
}
