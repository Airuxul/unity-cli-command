using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UnityCliConnector
{
    public static class SimpleJson
    {
        public static string Serialize(object value) => SerializeValue(value, new StringBuilder()).ToString();

        public static Dictionary<string, object> ParseObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new Dictionary<string, object>();
            var parser = new Parser(json.Trim());
            var value = parser.ReadValue();
            if (value is Dictionary<string, object> dict)
                return dict;
            return new Dictionary<string, object>();
        }

        private static StringBuilder SerializeValue(object value, StringBuilder sb)
        {
            if (value == null)
                return sb.Append("null");

            switch (value)
            {
                case string s:
                    return sb.Append('"').Append(Escape(s)).Append('"');
                case bool b:
                    return sb.Append(b ? "true" : "false");
                case int or long or float or double or decimal:
                    return sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                case IDictionary dict:
                    sb.Append('{');
                    var first = true;
                    foreach (DictionaryEntry entry in dict)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        sb.Append('"').Append(Escape(entry.Key.ToString())).Append("\":");
                        SerializeValue(entry.Value, sb);
                    }
                    return sb.Append('}');
                case IEnumerable list when value is not string:
                    sb.Append('[');
                    var f = true;
                    foreach (var item in list)
                    {
                        if (!f) sb.Append(',');
                        f = false;
                        SerializeValue(item, sb);
                    }
                    return sb.Append(']');
                default:
                    return sb.Append('"').Append(Escape(value.ToString())).Append('"');
            }
        }

        private static string Escape(string s) =>
            s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");

        private sealed class Parser
        {
            private readonly string _text;
            private int _i;

            public Parser(string text) => _text = text;

            public object ReadValue()
            {
                SkipWs();
                if (_i >= _text.Length)
                    return null;
                var c = _text[_i];
                if (c == '{') return ReadObject();
                if (c == '[') return ReadArray();
                if (c == '"') return ReadString();
                if (c == 't' || c == 'f' || c == 'n') return ReadLiteral();
                return ReadNumber();
            }

            private Dictionary<string, object> ReadObject()
            {
                var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                _i++;
                SkipWs();
                if (TryConsume('}'))
                    return dict;

                while (true)
                {
                    SkipWs();
                    var key = ReadString();
                    SkipWs();
                    Consume(':');
                    dict[key] = ReadValue();
                    SkipWs();
                    if (TryConsume('}'))
                        break;
                    Consume(',');
                }

                return dict;
            }

            private List<object> ReadArray()
            {
                var list = new List<object>();
                _i++;
                SkipWs();
                if (TryConsume(']'))
                    return list;

                while (true)
                {
                    list.Add(ReadValue());
                    SkipWs();
                    if (TryConsume(']'))
                        break;
                    Consume(',');
                }

                return list;
            }

            private string ReadString()
            {
                _i++;
                var sb = new StringBuilder();
                while (_i < _text.Length)
                {
                    var c = _text[_i++];
                    if (c == '"')
                        break;
                    if (c == '\\' && _i < _text.Length)
                    {
                        var esc = _text[_i++];
                        sb.Append(esc switch
                        {
                            '"' => '"',
                            '\\' => '\\',
                            'n' => '\n',
                            'r' => '\r',
                            't' => '\t',
                            _ => esc,
                        });
                    }
                    else sb.Append(c);
                }

                return sb.ToString();
            }

            private object ReadLiteral()
            {
                if (Match("true")) return true;
                if (Match("false")) return false;
                if (Match("null")) return null;
                return null;
            }

            private object ReadNumber()
            {
                var start = _i;
                while (_i < _text.Length && "0123456789+-.".IndexOf(_text[_i]) >= 0)
                    _i++;
                var num = _text.Substring(start, _i - start);
                if (num.Contains("."))
                    return double.Parse(num, CultureInfo.InvariantCulture);
                return long.Parse(num, CultureInfo.InvariantCulture);
            }

            private void SkipWs()
            {
                while (_i < _text.Length && char.IsWhiteSpace(_text[_i]))
                    _i++;
            }

            private void Consume(char expected)
            {
                SkipWs();
                if (_i >= _text.Length || _text[_i] != expected)
                    throw new FormatException($"Expected '{expected}' at {_i}");
                _i++;
            }

            private bool TryConsume(char expected)
            {
                SkipWs();
                if (_i < _text.Length && _text[_i] == expected)
                {
                    _i++;
                    return true;
                }
                return false;
            }

            private bool Match(string literal)
            {
                if (!_text.Substring(_i).StartsWith(literal, StringComparison.Ordinal))
                    return false;
                _i += literal.Length;
                return true;
            }
        }
    }
}
