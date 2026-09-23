using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>
    /// 一条面捕输出行的**表达式**（数字 / 变量 / 函数，曲线之前的纯函数）。
    ///
    /// 语法照抄 VBridger —— 它自己建在 MIT 的 <c>akuukka/ExpressionSolver</c> 上、再补了几个 helper。
    /// 所以这里不"顺手优化"：变量名、函数名、单引号延迟求值都保持一致，作者从 VBridger 抄来的
    /// 方程式要能原样粘贴进来（见 docs/FACE_TRACKING_MIDDLE_LAYER.md）。
    ///
    /// 三条刻意的性质：
    /// <list type="bullet">
    /// <item>函数是否存在、参数个数、括号是否配对，全在 <see cref="TryParse"/> 的**解析期**定死
    /// （报错带位置）—— 逐帧的 <see cref="Evaluate"/> 里不再有"这条式子今天能不能算"的不确定性；</item>
    /// <item><see cref="Evaluate"/> **绝不抛异常**：这是每帧几十行的管线，一条式子把异常扔出去就是面捕掉线；</item>
    /// <item>一切非有限值（除零 / 非正数取 log 或 sqrt / NaN / Inf）就地折成 <c>0</c>，
    /// 见 <see cref="Finite"/>——一个 NaN 顺着参数写进控制器，之后整张脸都是 NaN，很难往回查。</item>
    /// </list>
    /// </summary>
    public sealed class HoFaceExpression
    {
        /// <summary>引号 / 括号的最大嵌套层数。防的是解析期的深递归 —— 爆栈是进程级的，接不住。</summary>
        private const int MaxDepth = 32;

        private enum NodeKind { Number, Variable, Nested, Unary, Binary, Call }

        private enum Op { Pos, Neg, Add, Sub, Mul, Div, Pow, Lt, Le, Gt, Ge, Eq, Ne, And, Or }

        /// <summary>内置函数。解析期就定下 ID，逐帧求值不再做字符串比较。</summary>
        private enum Builtin
        {
            Sin, Cos, Tan, Asin, Acos, Atan, Sinh, Cosh, Tanh, Abs, Sqrt, Log, Log10, Exp,
            Round, Floor, Ceil, Sign, Atan2, Min, Max, Clamp, Approx, Lerp, Rand, Time, If
        }

        /// <summary>节点树用同一个类装，不为每种运算建一个类型 —— 这里要的是解析一次、每帧只读。</summary>
        private sealed class Node
        {
            public NodeKind kind;
            public float number;
            public string name;
            public Op op;
            public Builtin builtin;
            public Node left;
            public Node right;
            public Node[] args;
            public HoFaceExpression nested;
        }

        private enum TokenKind
        {
            End = 0, Number, Identifier, Quoted,
            Plus, Minus, Star, Slash, Caret, LParen, RParen, Comma,
            Lt, Le, Gt, Ge, EqEq, NotEq, And, Or
        }

        private struct Token
        {
            public TokenKind kind;
            public float number;
            public string text;
            public int pos;
            public Node node;
        }

        /// <summary>解析期的失败信号。只在 TryParse 内部流动，不会漏给调用方。</summary>
        private sealed class ParseException : Exception
        {
            public ParseException(string message) : base(message) { }
        }

        private readonly string _text;
        private readonly Node _root;

        private HoFaceExpression(string text, Node root)
        {
            _text = text;
            _root = root;
        }

        /// <summary>time(i,m) 用的帧计数（由调用方每帧设置；默认 0）。</summary>
        public static int Frame { get; set; }

        /// <summary>原始文本（规范化前的原文）。</summary>
        public string Text { get { return _text; } }

        /// <summary>解析失败时 error 是中文说明（含出错位置）；成功时 expression 非空。</summary>
        public static bool TryParse(string text, out HoFaceExpression expression, out string error)
        {
            expression = null;
            error = null;
            if (string.IsNullOrEmpty(text))
            {
                error = "表达式为空";
                return false;
            }

            try
            {
                Node root = new Parser(text, 0).ParseAll();
                expression = new HoFaceExpression(text, root);
                return true;
            }
            catch (ParseException ex)
            {
                error = ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                // 兜底：解析期也不许把奇怪输入变成异常抛给面板。
                error = "解析失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>求值。变量由 variable(name) 提供；未知变量按 0 处理，绝不抛异常。</summary>
        public float Evaluate(Func<string, float> variable)
        {
            try
            {
                return Eval(_root, variable);
            }
            catch
            {
                // 每帧都跑：宁可变 0，也不能把异常扔进面捕管线。
                return 0f;
            }
        }

        /// <summary>这条表达式引用到的变量名（去重，按首次出现顺序）。</summary>
        public void CollectVariables(List<string> into)
        {
            if (into == null || _root == null) return;
            Collect(_root, into);
        }

        private static void Collect(Node node, List<string> into)
        {
            if (node == null) return;
            switch (node.kind)
            {
                case NodeKind.Variable:
                    // 顺手跳过调用方已经收过的名字：调用方通常是在累积很多行。
                    if (!string.IsNullOrEmpty(node.name) && !into.Contains(node.name)) into.Add(node.name);
                    return;
                case NodeKind.Nested:
                    // 引号里也是引用（if 的条件就写在引号里），漏了它 = 少读一个源键。
                    if (node.nested != null) Collect(node.nested._root, into);
                    return;
                case NodeKind.Unary:
                    Collect(node.left, into);
                    return;
                case NodeKind.Binary:
                    Collect(node.left, into);
                    Collect(node.right, into);
                    return;
                case NodeKind.Call:
                    if (node.args != null)
                        for (int i = 0; i < node.args.Length; i++) Collect(node.args[i], into);
                    return;
                default:
                    return;
            }
        }

        private static float Eval(Node node, Func<string, float> variable)
        {
            if (node == null) return 0f;
            switch (node.kind)
            {
                case NodeKind.Number:
                    return Finite(node.number);
                case NodeKind.Variable:
                    if (variable == null) return 0f;
                    try
                    {
                        return Finite(variable(node.name));
                    }
                    catch
                    {
                        return 0f; // 取变量的回调也不许把异常带进来
                    }
                case NodeKind.Nested:
                    // 引号里的式子**用到时才求值** —— if 的未选中分支永远走不到这里。
                    return node.nested != null ? node.nested.Evaluate(variable) : 0f;
                case NodeKind.Unary:
                {
                    float value = Eval(node.left, variable);
                    return node.op == Op.Neg ? -value : value; // value 已有限，取负不会变成非有限
                }
                case NodeKind.Binary:
                    return EvalBinary(node, variable);
                case NodeKind.Call:
                    return EvalCall(node, variable);
                default:
                    return 0f;
            }
        }

        private static float EvalBinary(Node node, Func<string, float> variable)
        {
            float a = Eval(node.left, variable);

            // 逻辑与比较都返回 1/0；&& 和 || 短路，右侧不白算。
            switch (node.op)
            {
                case Op.And: return a != 0f && Eval(node.right, variable) != 0f ? 1f : 0f;
                case Op.Or: return a != 0f || Eval(node.right, variable) != 0f ? 1f : 0f;
            }

            float b = Eval(node.right, variable);
            switch (node.op)
            {
                case Op.Add: return Finite(a + b);
                case Op.Sub: return Finite(a - b);
                case Op.Mul: return Finite(a * b);
                case Op.Div: return b == 0f ? 0f : Finite(a / b);
                case Op.Pow: return Finite(Mathf.Pow(a, b)); // 0^负数 = Inf → 0
                case Op.Lt: return a < b ? 1f : 0f;
                case Op.Le: return a <= b ? 1f : 0f;
                case Op.Gt: return a > b ? 1f : 0f;
                case Op.Ge: return a >= b ? 1f : 0f;
                case Op.Eq: return a == b ? 1f : 0f;
                case Op.Ne: return a != b ? 1f : 0f;
                default: return 0f;
            }
        }

        private static float EvalCall(Node node, Func<string, float> variable)
        {
            Node[] args = node.args;
            if (args == null || args.Length == 0) return 0f;

            switch (node.builtin)
            {
                case Builtin.If:
                    // 只算被选中的那一支：另一支里可能有 rand()/time()，算了既是副作用也是白费。
                    // 条件是引号里的嵌套表达式时，它在这里才第一次被求值 —— 这就是"引号延迟"的全部含义。
                    return Eval(args[0], variable) != 0f ? Eval(args[1], variable) : Eval(args[2], variable);
                case Builtin.Time:
                {
                    float step = Eval(args[0], variable);
                    float period = Eval(args[1], variable);
                    if (period <= 0f) return 0f; // 模数非正：没有周期可言（也算不到 NaN）
                    return Finite((step * Frame) % period);
                }
                case Builtin.Rand:
                    return Finite(UnityEngine.Random.Range(Eval(args[0], variable), Eval(args[1], variable)));
                case Builtin.Clamp:
                    // Mathf.Clamp 的下限大于上限也不抛，只是按顺序夹一次。
                    return Finite(Mathf.Clamp(Eval(args[0], variable), Eval(args[1], variable), Eval(args[2], variable)));
                case Builtin.Approx:
                {
                    float x = Eval(args[0], variable);
                    float y = Eval(args[1], variable);
                    float delta = Eval(args[2], variable);
                    return Mathf.Abs(x - y) <= delta ? 1f : 0f; // 和比较运算符一样返回 1/0
                }
                case Builtin.Lerp:
                {
                    float x = Eval(args[0], variable);
                    float y = Eval(args[1], variable);
                    float t = Eval(args[2], variable);
                    // 不学 Mathf.Lerp 把 t 截到 [0,1]：这里是数学上的直线，外推也是合法用法。
                    return Finite(x + (y - x) * t);
                }
            }

            float a = Eval(args[0], variable);
            switch (node.builtin)
            {
                // atan2(x,y) 与 ExpressionSolver 一致：第一个参数当作 y（即 Math.Atan2(p0,p1)）。
                case Builtin.Atan2: return Finite(Mathf.Atan2(a, Eval(args[1], variable)));
                case Builtin.Min: return Finite(Mathf.Min(a, Eval(args[1], variable)));
                case Builtin.Max: return Finite(Mathf.Max(a, Eval(args[1], variable)));
                case Builtin.Sin: return Finite(Mathf.Sin(a));
                case Builtin.Cos: return Finite(Mathf.Cos(a));
                case Builtin.Tan: return Finite(Mathf.Tan(a));
                case Builtin.Asin: return Finite(Mathf.Asin(a));
                case Builtin.Acos: return Finite(Mathf.Acos(a));
                case Builtin.Atan: return Finite(Mathf.Atan(a));
                case Builtin.Sinh: return Finite((float)Math.Sinh(a));
                case Builtin.Cosh: return Finite((float)Math.Cosh(a));
                case Builtin.Tanh: return Finite((float)Math.Tanh(a));
                case Builtin.Abs: return Finite(Mathf.Abs(a));
                case Builtin.Sqrt: return a < 0f ? 0f : Finite(Mathf.Sqrt(a));
                case Builtin.Log: return a <= 0f ? 0f : Finite(Mathf.Log(a));
                case Builtin.Log10: return a <= 0f ? 0f : Finite(Mathf.Log10(a));
                case Builtin.Exp: return Finite(Mathf.Exp(a));
                case Builtin.Round: return Finite(Mathf.Round(a));
                case Builtin.Floor: return Finite(Mathf.Floor(a));
                case Builtin.Ceil: return Finite(Mathf.Ceil(a));
                // sign(0) = 0（与 ExpressionSolver 的 Math.Sign 一致，别用 Mathf.Sign —— 它给 1）。
                case Builtin.Sign: return a > 0f ? 1f : (a < 0f ? -1f : 0f);
                default: return 0f;
            }
        }

        /// <summary>
        /// 把非有限值折成 0。管线每帧跑几十行，一个 NaN 会顺着参数写进控制器与动画器，
        /// 之后整张脸都是 NaN 且极难定位 —— 就地折 0 是这一步唯一能收住它的做法。
        /// </summary>
        private static float Finite(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }

        private static bool TryResolve(string name, out Builtin builtin, out int arity)
        {
            switch (name)
            {
                case "sin": builtin = Builtin.Sin; arity = 1; return true;
                case "cos": builtin = Builtin.Cos; arity = 1; return true;
                case "tan": builtin = Builtin.Tan; arity = 1; return true;
                case "asin": builtin = Builtin.Asin; arity = 1; return true;
                case "acos": builtin = Builtin.Acos; arity = 1; return true;
                case "atan": builtin = Builtin.Atan; arity = 1; return true;
                case "sinh": builtin = Builtin.Sinh; arity = 1; return true;
                case "cosh": builtin = Builtin.Cosh; arity = 1; return true;
                case "tanh": builtin = Builtin.Tanh; arity = 1; return true;
                case "abs": builtin = Builtin.Abs; arity = 1; return true;
                case "sqrt": builtin = Builtin.Sqrt; arity = 1; return true;
                case "log": builtin = Builtin.Log; arity = 1; return true;
                case "log10": builtin = Builtin.Log10; arity = 1; return true;
                case "exp": builtin = Builtin.Exp; arity = 1; return true;
                case "round": builtin = Builtin.Round; arity = 1; return true;
                case "floor": builtin = Builtin.Floor; arity = 1; return true;
                case "ceil": builtin = Builtin.Ceil; arity = 1; return true;
                case "sign": builtin = Builtin.Sign; arity = 1; return true;
                case "atan2": builtin = Builtin.Atan2; arity = 2; return true;
                case "min": builtin = Builtin.Min; arity = 2; return true;
                case "max": builtin = Builtin.Max; arity = 2; return true;
                case "rand": builtin = Builtin.Rand; arity = 2; return true;
                case "time": builtin = Builtin.Time; arity = 2; return true;
                case "clamp": builtin = Builtin.Clamp; arity = 3; return true;
                case "approx": builtin = Builtin.Approx; arity = 3; return true;
                case "lerp": builtin = Builtin.Lerp; arity = 3; return true;
                case "if": builtin = Builtin.If; arity = 3; return true;
                default: builtin = Builtin.Sin; arity = 0; return false;
            }
        }

        private static bool IsFunctionName(string name)
        {
            Builtin builtin;
            int arity;
            return TryResolve(name, out builtin, out arity);
        }

        private static Node Binary(Op op, Node left, Node right)
        {
            return new Node { kind = NodeKind.Binary, op = op, left = left, right = right };
        }

        private static Node Unary(Op op, Node operand)
        {
            return new Node { kind = NodeKind.Unary, op = op, left = operand };
        }

        /// <summary>
        /// 词法 + 递归下降。优先级由低到高：
        /// <c>||</c> → <c>&amp;&amp;</c> → 比较 → 加减 → 乘除 → 一元 ± → <c>^</c> → 基本项。
        /// <c>^</c> 比一元负号紧（<c>-2^2 == -4</c>），且右结合（<c>2^3^2 == 2^(3^2)</c>）。
        /// </summary>
        private sealed class Parser
        {
            private readonly string _text;
            private int _depth;
            private int _pos;
            private Token _token;

            public Parser(string text, int depth)
            {
                _text = text;
                _depth = depth;
                _pos = 0;
                _token = new Token();
                Next();
            }

            public Node ParseAll()
            {
                if (_token.kind == TokenKind.End) throw Error(_token, "表达式为空");
                Node root = ParseOr();
                if (_token.kind != TokenKind.End) throw Error(_token, "多余的内容：" + Describe(_token));
                return root;
            }

            private Node ParseOr()
            {
                Node left = ParseAnd();
                while (_token.kind == TokenKind.Or)
                {
                    Next();
                    left = Binary(Op.Or, left, ParseAnd());
                }
                return left;
            }

            private Node ParseAnd()
            {
                Node left = ParseComparison();
                while (_token.kind == TokenKind.And)
                {
                    Next();
                    left = Binary(Op.And, left, ParseComparison());
                }
                return left;
            }

            private Node ParseComparison()
            {
                Node left = ParseAdditive();
                while (true)
                {
                    Op op;
                    switch (_token.kind)
                    {
                        case TokenKind.Lt: op = Op.Lt; break;
                        case TokenKind.Le: op = Op.Le; break;
                        case TokenKind.Gt: op = Op.Gt; break;
                        case TokenKind.Ge: op = Op.Ge; break;
                        case TokenKind.EqEq: op = Op.Eq; break;
                        case TokenKind.NotEq: op = Op.Ne; break;
                        default: return left;
                    }
                    Next();
                    left = Binary(op, left, ParseAdditive());
                }
            }

            private Node ParseAdditive()
            {
                Node left = ParseMultiplicative();
                while (_token.kind == TokenKind.Plus || _token.kind == TokenKind.Minus)
                {
                    Op op = _token.kind == TokenKind.Plus ? Op.Add : Op.Sub;
                    Next();
                    left = Binary(op, left, ParseMultiplicative());
                }
                return left;
            }

            private Node ParseMultiplicative()
            {
                Node left = ParseUnary();
                while (_token.kind == TokenKind.Star || _token.kind == TokenKind.Slash)
                {
                    Op op = _token.kind == TokenKind.Star ? Op.Mul : Op.Div;
                    Next();
                    left = Binary(op, left, ParseUnary());
                }
                return left;
            }

            private Node ParseUnary()
            {
                if (_token.kind == TokenKind.Plus)
                {
                    Next();
                    return Unary(Op.Pos, ParseUnary());
                }
                if (_token.kind == TokenKind.Minus)
                {
                    Next();
                    return Unary(Op.Neg, ParseUnary());
                }
                return ParsePower();
            }

            private Node ParsePower()
            {
                Node left = ParsePrimary();
                if (_token.kind == TokenKind.Caret)
                {
                    Next();
                    return Binary(Op.Pow, left, ParseUnary()); // 指数侧允许一元负号，且右结合
                }
                return left;
            }

            private Node ParsePrimary()
            {
                Token token = _token;
                switch (token.kind)
                {
                    case TokenKind.Number:
                        Next();
                        return new Node { kind = NodeKind.Number, number = token.number };
                    case TokenKind.Quoted:
                        Next();
                        return token.node;
                    case TokenKind.Identifier:
                        Next();
                        if (_token.kind == TokenKind.LParen) return ParseCall(token);
                        if (IsFunctionName(token.text)) throw Error(token, "函数 " + token.text + "(…) 后面缺少括号");
                        return new Node { kind = NodeKind.Variable, name = token.text };
                    case TokenKind.LParen:
                    {
                        if (_depth + 1 > MaxDepth) throw Error(token, "括号嵌套超过 " + MaxDepth + " 层");
                        _depth++;
                        Next();
                        Node inner = ParseOr();
                        if (_token.kind != TokenKind.RParen) throw Error(_token, "缺少右括号 ')'");
                        _depth--;
                        Next();
                        return inner;
                    }
                    case TokenKind.End:
                        throw Error(token, "表达式不完整");
                    default:
                        throw Error(token, "这里需要数字、变量或 '('，实际是" + Describe(token));
                }
            }

            private Node ParseCall(Token nameToken)
            {
                Next(); // 吃掉 '('

                var args = new List<Node>();
                if (_token.kind != TokenKind.RParen)
                {
                    args.Add(ParseOr());
                    while (_token.kind == TokenKind.Comma)
                    {
                        Next();
                        args.Add(ParseOr());
                    }
                }
                if (_token.kind != TokenKind.RParen) throw Error(_token, "函数 " + nameToken.text + " 的括号没闭合");
                Next();

                Builtin builtin;
                int arity;
                if (!TryResolve(nameToken.text, out builtin, out arity))
                    throw Error(nameToken, "没有这个函数：" + nameToken.text);

                // 参数个数在解析期就定死：逐帧不再有"这条式子能不能算"的不确定性。
                if (args.Count != arity)
                    throw Error(nameToken, "函数 " + nameToken.text + " 要 " + arity + " 个参数，这里给了 " + args.Count + " 个");

                return new Node { kind = NodeKind.Call, builtin = builtin, args = args.ToArray() };
            }

            private void Next()
            {
                int length = _text.Length;
                while (_pos < length && char.IsWhiteSpace(_text[_pos])) _pos++;

                int start = _pos;
                if (start >= length)
                {
                    _token = new Token { kind = TokenKind.End, pos = start };
                    return;
                }

                char c = _text[start];
                if (IsDigit(c) || (c == '.' && start + 1 < length && IsDigit(_text[start + 1])))
                {
                    NextNumber(start);
                    return;
                }
                if (IsIdentStart(c))
                {
                    _pos = start + 1;
                    while (_pos < length && IsIdentChar(_text[_pos])) _pos++;
                    _token = new Token { kind = TokenKind.Identifier, text = _text.Substring(start, _pos - start), pos = start };
                    return;
                }
                if (c == '\'')
                {
                    NextQuoted(start);
                    return;
                }

                _pos = start + 1;
                switch (c)
                {
                    case '+': _token = Sym(TokenKind.Plus, start); return;
                    case '-': _token = Sym(TokenKind.Minus, start); return;
                    case '*': _token = Sym(TokenKind.Star, start); return;
                    case '/': _token = Sym(TokenKind.Slash, start); return;
                    case '^': _token = Sym(TokenKind.Caret, start); return;
                    case '(': _token = Sym(TokenKind.LParen, start); return;
                    case ')': _token = Sym(TokenKind.RParen, start); return;
                    case ',': _token = Sym(TokenKind.Comma, start); return;
                    case '<':
                        if (Peek('=')) { _pos++; _token = Sym(TokenKind.Le, start); }
                        else _token = Sym(TokenKind.Lt, start);
                        return;
                    case '>':
                        if (Peek('=')) { _pos++; _token = Sym(TokenKind.Ge, start); }
                        else _token = Sym(TokenKind.Gt, start);
                        return;
                    case '=':
                        if (Peek('=')) { _pos++; _token = Sym(TokenKind.EqEq, start); return; }
                        throw Error(Sym(TokenKind.End, start), "单个 '=' 不是运算符，相等比较要写 '=='");
                    case '!':
                        if (Peek('=')) { _pos++; _token = Sym(TokenKind.NotEq, start); return; }
                        throw Error(Sym(TokenKind.End, start), "'!' 不支持，非零即真");
                    case '&':
                        if (Peek('&')) { _pos++; _token = Sym(TokenKind.And, start); return; }
                        throw Error(Sym(TokenKind.End, start), "'&&' 要写两个 &");
                    case '|':
                        if (Peek('|')) { _pos++; _token = Sym(TokenKind.Or, start); return; }
                        throw Error(Sym(TokenKind.End, start), "'||' 要写两个 |");
                    default:
                        throw Error(Sym(TokenKind.End, start), "无法识别的字符 '" + c + "'");
                }
            }

            private void NextNumber(int start)
            {
                int length = _text.Length;
                _pos = start;
                while (_pos < length && IsDigit(_text[_pos])) _pos++;
                if (_pos < length && _text[_pos] == '.')
                {
                    _pos++;
                    while (_pos < length && IsDigit(_text[_pos])) _pos++;
                }
                if (_pos < length && (_text[_pos] == 'e' || _text[_pos] == 'E'))
                {
                    int exponentStart = _pos;
                    _pos++;
                    if (_pos < length && (_text[_pos] == '+' || _text[_pos] == '-')) _pos++;
                    if (_pos < length && IsDigit(_text[_pos]))
                        while (_pos < length && IsDigit(_text[_pos])) _pos++;
                    else
                        _pos = exponentStart; // "1e" 里的 e 不算指数，留给上层报"多余的内容"
                }

                string raw = _text.Substring(start, _pos - start);
                double value;
                // 明确用不变文化：作者写的 "0.5" 不能因为机器区域设置变成 5。
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    throw Error(Sym(TokenKind.Number, start), "数字写错了：" + raw);
                if (double.IsNaN(value) || value > float.MaxValue || value < float.MinValue)
                    throw Error(Sym(TokenKind.Number, start), "数字超出 float 范围：" + raw);
                _token = new Token { kind = TokenKind.Number, number = (float)value, pos = start };
            }

            private void NextQuoted(int start)
            {
                int close = _text.IndexOf('\'', start + 1);
                if (close < 0) throw Error(Sym(TokenKind.Quoted, start), "单引号没有闭合");
                string inner = _text.Substring(start + 1, close - start - 1);
                _pos = close + 1;

                if (_depth + 1 > MaxDepth)
                    throw Error(Sym(TokenKind.Quoted, start), "单引号嵌套超过 " + MaxDepth + " 层");

                // 引号里是**另一条表达式**：这里就解析好（错了能带位置报出来），
                // 但树的求值推迟到真正用到它的时候 —— if 的未选中分支因此连带副作用一起免掉。
                Node node;
                try
                {
                    node = new Parser(inner, _depth + 1).ParseAll();
                }
                catch (ParseException ex)
                {
                    throw Error(Sym(TokenKind.Quoted, start), "单引号里的表达式有错 —— " + ex.Message);
                }

                _token = new Token
                {
                    kind = TokenKind.Quoted,
                    text = inner,
                    pos = start,
                    node = new Node { kind = NodeKind.Nested, nested = new HoFaceExpression(inner, node) }
                };
            }

            private bool Peek(char expected)
            {
                return _pos < _text.Length && _text[_pos] == expected;
            }

            private static Token Sym(TokenKind kind, int pos)
            {
                return new Token { kind = kind, pos = pos };
            }

            private static bool IsDigit(char c)
            {
                return c >= '0' && c <= '9'; // 只认 ASCII 数字：别的语言的"数字"InvariantCulture 也解析不了
            }

            private static bool IsIdentStart(char c)
            {
                return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '_';
            }

            private static bool IsIdentChar(char c)
            {
                return IsIdentStart(c) || IsDigit(c);
            }

            private static ParseException Error(Token token, string message)
            {
                return new ParseException("第 " + (token.pos + 1) + " 个字符处：" + message);
            }

            private static string Describe(Token token)
            {
                switch (token.kind)
                {
                    case TokenKind.End: return "表达式结尾";
                    case TokenKind.Number: return "数字";
                    case TokenKind.Identifier: return "变量 " + token.text;
                    case TokenKind.Quoted: return "单引号表达式";
                    default: return "'" + OpText(token.kind) + "'";
                }
            }

            private static string OpText(TokenKind kind)
            {
                switch (kind)
                {
                    case TokenKind.Plus: return "+";
                    case TokenKind.Minus: return "-";
                    case TokenKind.Star: return "*";
                    case TokenKind.Slash: return "/";
                    case TokenKind.Caret: return "^";
                    case TokenKind.LParen: return "(";
                    case TokenKind.RParen: return ")";
                    case TokenKind.Comma: return ",";
                    case TokenKind.Lt: return "<";
                    case TokenKind.Le: return "<=";
                    case TokenKind.Gt: return ">";
                    case TokenKind.Ge: return ">=";
                    case TokenKind.EqEq: return "==";
                    case TokenKind.NotEq: return "!=";
                    case TokenKind.And: return "&&";
                    case TokenKind.Or: return "||";
                    default: return "?";
                }
            }
        }
    }
}
