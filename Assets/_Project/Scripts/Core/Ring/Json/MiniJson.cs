using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Game.Core.Ring
{
    /// <summary>Помилка розбору JSON із позицією (0-based) у тексті.</summary>
    public sealed class MiniJsonException : Exception
    {
        public int Position { get; }
        public MiniJsonException(string message, int position)
            : base(message + " (позиція " + position + ")") { Position = position; }
    }

    /// <summary>
    /// Мінімальний JSON-розбирач (System.Text.Json у netstandard2.0 ядра недоступний). Об'єкт — Dictionary&lt;string,object&gt;,
    /// масив — List&lt;object&gt;, число — double, а також string, bool, null.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            var p = new Parser(json);
            p.SkipWs();
            var v = p.ReadValue();
            p.SkipWs();
            if (!p.AtEnd) throw new MiniJsonException("зайвий текст після значення", p.Pos);
            return v;
        }

        public static Dictionary<string, object> ParseObject(string json)
        {
            var o = Parse(json) as Dictionary<string, object>;
            if (o == null) throw new MiniJsonException("очікувався об'єкт верхнього рівня", 0);
            return o;
        }

        private sealed class Parser
        {
            private readonly string _s;
            public int Pos;
            public Parser(string s) { _s = s; }
            public bool AtEnd => Pos >= _s.Length;

            public void SkipWs()
            {
                while (Pos < _s.Length && (_s[Pos] == ' ' || _s[Pos] == '\t' || _s[Pos] == '\n' || _s[Pos] == '\r')) Pos++;
            }

            private char Peek()
            {
                if (Pos >= _s.Length) throw new MiniJsonException("несподіваний кінець тексту", Pos);
                return _s[Pos];
            }

            public object ReadValue()
            {
                char c = Peek();
                if (c == '{') return ReadObject();
                if (c == '[') return ReadArray();
                if (c == '"') return ReadString();
                if (c == 't') return ReadLiteral("true", true);
                if (c == 'f') return ReadLiteral("false", false);
                if (c == 'n') return ReadLiteral("null", null);
                if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                throw new MiniJsonException("несподіваний символ '" + c + "'", Pos);
            }

            private object ReadLiteral(string word, object value)
            {
                if (string.CompareOrdinal(_s, Pos, word, 0, word.Length) != 0)
                    throw new MiniJsonException("очікувалось '" + word + "'", Pos);
                Pos += word.Length;
                return value;
            }

            private Dictionary<string, object> ReadObject()
            {
                var o = new Dictionary<string, object>();
                Pos++; // {
                SkipWs();
                if (Peek() == '}') { Pos++; return o; }
                while (true)
                {
                    SkipWs();
                    if (Peek() != '"') throw new MiniJsonException("очікувався ключ-рядок", Pos);
                    int keyPos = Pos;
                    string key = ReadString();
                    SkipWs();
                    if (Peek() != ':') throw new MiniJsonException("очікувалось ':'", Pos);
                    Pos++;
                    SkipWs();
                    var v = ReadValue();
                    if (o.ContainsKey(key)) throw new MiniJsonException("дубль ключа '" + key + "'", keyPos);
                    o[key] = v;
                    SkipWs();
                    char c = Peek();
                    Pos++;
                    if (c == ',') continue;
                    if (c == '}') return o;
                    throw new MiniJsonException("очікувалось ',' або '}'", Pos - 1);
                }
            }

            private List<object> ReadArray()
            {
                var a = new List<object>();
                Pos++; // [
                SkipWs();
                if (Peek() == ']') { Pos++; return a; }
                while (true)
                {
                    SkipWs();
                    a.Add(ReadValue());
                    SkipWs();
                    char c = Peek();
                    Pos++;
                    if (c == ',') continue;
                    if (c == ']') return a;
                    throw new MiniJsonException("очікувалось ',' або ']'", Pos - 1);
                }
            }

            private string ReadString()
            {
                Pos++; // "
                var sb = new StringBuilder();
                while (true)
                {
                    char c = Peek();
                    Pos++;
                    if (c == '"') return sb.ToString();
                    if (c < ' ') throw new MiniJsonException("керівний символ у рядку", Pos - 1);
                    if (c != '\\') { sb.Append(c); continue; }
                    char e = Peek();
                    Pos++;
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (Pos + 4 > _s.Length) throw new MiniJsonException("обірваний \\u", Pos);
                            int code;
                            if (!int.TryParse(_s.Substring(Pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                throw new MiniJsonException("неправильний \\u", Pos);
                            sb.Append((char)code);
                            Pos += 4;
                            break;
                        default: throw new MiniJsonException("невідома екранована послідовність \\" + e, Pos - 1);
                    }
                }
            }

            private double ReadNumber()
            {
                int start = Pos;
                if (_s[Pos] == '-') Pos++;
                while (Pos < _s.Length && ((_s[Pos] >= '0' && _s[Pos] <= '9') || _s[Pos] == '.' || _s[Pos] == 'e'
                                          || _s[Pos] == 'E' || _s[Pos] == '+' || _s[Pos] == '-')) Pos++;
                double d;
                if (!double.TryParse(_s.Substring(start, Pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                    throw new MiniJsonException("неправильне число", start);
                return d;
            }
        }
    }
}
