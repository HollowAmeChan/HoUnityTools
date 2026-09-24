using System;
using System.Globalization;
using System.Text;

namespace Hollow.HoUnityTools.FaceTracking
{
    /// <summary>
    /// 极小的 JSON 读取器 —— 我们**所有**数据路径共用的那一份。
    ///
    /// <para>
    /// **为什么不用 <c>JsonUtility</c>（本机实测，血泪）**：它在 Warudo 播放器里会
    /// **静默丢掉 `List&lt;嵌套类/内部类&gt;` 字段**，而编辑器里一切正常。
    /// 已经被咬过三次：
    /// ① 写 profile —— 只剩头部四个字段，两个数组连空壳都不写；
    /// ② 读 profile —— 报"配置文件里一行输出都没有"；
    /// ③ 收 VTS 包 —— 12 个头眼分量全在，**52 个形态键全丢**（"解析成功"却少了最要紧的东西）。
    /// 三次的共同点：**症状离原因都很远**，所以规则是硬的：
    /// **我们的数据一律不用 `JsonUtility`。**
    /// </para>
    /// <para>
    /// 这里只追求两件事：**能读懂我们自己的数据**、**错在哪一眼看得见**（报错带字符位置）。
    /// 不做 DOM、不做泛型序列化、不做 Date/Unicode 代理对之类的完整实现。
    /// </para>
    /// </summary>
    public sealed class HoJsonReader
    {
        private readonly string _text;
        private int _pos;

        public HoJsonReader(string text)
        {
            _text = text ?? "";
            // 文件/包可能带 UTF-8 BOM（被解码成 U+FEFF），跳过它。
            if (_text.Length > 0 && _text[0] == '\uFEFF') _pos = 1;
        }

        /// <summary>当前位置（0 基），报错信息用。</summary>
        public int Position { get { return _pos; } }

        private char Peek { get { return _pos < _text.Length ? _text[_pos] : '\0'; } }

        public void Skip()
        {
            while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos])) _pos++;
        }

        public void Expect(char expected)
        {
            Skip();
            if (_pos >= _text.Length || _text[_pos] != expected)
                throw Error("期望 '" + expected + "'，实际是 " + Describe());
            _pos++;
        }

        public bool TryConsume(char expected)
        {
            Skip();
            if (_pos < _text.Length && _text[_pos] == expected) { _pos++; return true; }
            return false;
        }

        public string ReadString()
        {
            Skip();
            if (Peek != '"') throw Error("期望一个字符串，实际是 " + Describe());
            _pos++;

            var text = new StringBuilder();
            while (true)
            {
                if (_pos >= _text.Length) throw Error("字符串没有闭合");
                char c = _text[_pos++];
                if (c == '"') break;
                if (c != '\\') { text.Append(c); continue; }

                if (_pos >= _text.Length) throw Error("转义符后面没有东西");
                char escaped = _text[_pos++];
                switch (escaped)
                {
                    case '"': text.Append('"'); break;
                    case '\\': text.Append('\\'); break;
                    case '/': text.Append('/'); break;
                    case 'b': text.Append('\b'); break;
                    case 'f': text.Append('\f'); break;
                    case 'n': text.Append('\n'); break;
                    case 'r': text.Append('\r'); break;
                    case 't': text.Append('\t'); break;
                    case 'u':
                        if (_pos + 4 > _text.Length) throw Error("\\u 后面不足四个字符");
                        int code;
                        if (!int.TryParse(_text.Substring(_pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                            throw Error("\\u 后面的十六进制不对：" + _text.Substring(_pos, 4));
                        text.Append((char)code);
                        _pos += 4;
                        break;
                    default: throw Error("认不出的转义：\\" + escaped);
                }
            }
            return text.ToString();
        }

        /// <summary>
        /// 读一个数字。**明确用不变文化** —— 机器区域设置不能把 `0.5` 读成 5。
        /// 手机发的 `Timestamp` 是 13 位毫秒（float 存不下），所以这里走 double 再按需取整。
        /// </summary>
        public double ReadNumber()
        {
            Skip();
            int start = _pos;
            while (_pos < _text.Length && IsNumberChar(_text[_pos])) _pos++;
            if (_pos == start) throw Error("期望一个数字，实际是 " + Describe());

            string raw = _text.Substring(start, _pos - start);
            double value;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw Error("数字写错了：" + raw);
            return value;
        }

        public float ReadFloat() { return (float)ReadNumber(); }

        public long ReadLong() { return (long)ReadNumber(); }

        /// <summary>读 `true` / `false`（也容忍 `0` / `1`）。</summary>
        public bool ReadBool()
        {
            Skip();
            if (Peek == 't') { ExpectWord("true"); return true; }
            if (Peek == 'f') { ExpectWord("false"); return false; }
            return ReadNumber() != 0.0;
        }

        /// <summary>跳过一个值 —— 未知字段用它，向前兼容。</summary>
        public void SkipValue()
        {
            Skip();
            char c = Peek;

            if (c == '\0') throw Error("这里应该有个值");
            if (c == '"') { ReadString(); return; }

            if (c == '{' || c == '[')
            {
                char close = c == '{' ? '}' : ']';
                _pos++;
                while (true)
                {
                    Skip();
                    if (_pos >= _text.Length) throw Error("容器没有闭合");
                    char inner = _text[_pos];
                    if (inner == close) { _pos++; return; }
                    if (inner == ',' || inner == ':') { _pos++; continue; }
                    SkipValue();
                }
            }

            // true / false / null / 数字
            while (_pos < _text.Length && !IsDelimiter(_text[_pos])) _pos++;
        }

        // ── 冷路径（一份文件读一次）用的便捷走法 ──────────────────────────────
        // ⚠️ 它们用委托，会分配闭包 —— **逐帧的热路径（比如 60 FPS 的收包）别用**，
        //    热路径请直接用上面那些原语手写循环。

        /// <summary>把一个对象按 <c>键 → 处理</c> 走完（自动吃逗号与收尾的 `}`）。</summary>
        public void ReadObject(Action<string, HoJsonReader> onKey)
        {
            Expect('{');
            if (TryConsume('}')) return;
            while (true)
            {
                string key = ReadString();
                Expect(':');
                onKey(key, this);
                if (TryConsume(',')) continue;
                Expect('}');
                return;
            }
        }

        /// <summary>把一个数组按 <c>每一项</c> 走完（自动吃逗号与收尾的 `]`）。</summary>
        public void ReadArray(Action<HoJsonReader> onItem)
        {
            Expect('[');
            if (TryConsume(']')) return;
            while (true)
            {
                onItem(this);
                if (TryConsume(',')) continue;
                Expect(']');
                return;
            }
        }

        public FormatException Error(string message)
        {
            return new FormatException("第 " + (_pos + 1) + " 个字符处：" + message);
        }

        private void ExpectWord(string word)
        {
            if (_pos + word.Length > _text.Length || string.CompareOrdinal(_text, _pos, word, 0, word.Length) != 0)
                throw Error("期望 " + word + "，实际是 " + Describe());
            _pos += word.Length;
        }

        private static bool IsNumberChar(char c)
        {
            return (c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E';
        }

        private static bool IsDelimiter(char c)
        {
            return c == ',' || c == '}' || c == ']' || char.IsWhiteSpace(c);
        }

        private string Describe()
        {
            if (_pos >= _text.Length) return "内容结尾";
            return "'" + _text[_pos] + "'";
        }
    }
}
