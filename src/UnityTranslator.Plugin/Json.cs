using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UnityTranslator
{
    /// <summary>
    /// Small dependency-free JSON reader/writer. BepInEx plugins cannot rely on
    /// Newtonsoft.Json being present in every Unity game.
    /// </summary>
    internal static class Json
    {
        public static object Parse(string json)
        {
            if (json == null) throw new JsonException("JSON is null");
            return new JsonParser(json).ParseDocument();
        }

        public static string Escape(string value)
        {
            if (value == null) return "null";
            StringBuilder sb = new StringBuilder(value.Length + 16);
            sb.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }

    internal sealed class JsonException : Exception
    {
        public JsonException(string message) : base(message) { }
    }

    internal sealed class JsonParser
    {
        private readonly string _text;
        private int _index;

        public JsonParser(string text)
        {
            _text = text;
            _index = 0;
        }

        public object ParseDocument()
        {
            SkipWhitespace();
            object value = ParseValue();
            SkipWhitespace();
            if (_index != _text.Length)
                throw new JsonException("Unexpected trailing data at " + _index);
            return value;
        }

        private void SkipWhitespace()
        {
            while (_index < _text.Length)
            {
                char c = _text[_index];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') _index++;
                else break;
            }
        }

        private object ParseValue()
        {
            if (_index >= _text.Length) throw new JsonException("Unexpected end of JSON");
            char c = _text[_index];
            switch (c)
            {
                case '{': return ParseObject();
                case '[': return ParseArray();
                case '"': return ParseString();
                case 't': Expect("true"); return true;
                case 'f': Expect("false"); return false;
                case 'n': Expect("null"); return null;
                default:
                    if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                    throw new JsonException("Unexpected character '" + c + "' at " + _index);
            }
        }

        private void Expect(string word)
        {
            if (_index + word.Length > _text.Length ||
                string.CompareOrdinal(_text, _index, word, 0, word.Length) != 0)
                throw new JsonException("Expected '" + word + "' at " + _index);
            _index += word.Length;
        }

        private Dictionary<string, object> ParseObject()
        {
            _index++; // {
            Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.Ordinal);
            SkipWhitespace();
            if (_index < _text.Length && _text[_index] == '}')
            {
                _index++;
                return result;
            }

            while (true)
            {
                SkipWhitespace();
                if (_index >= _text.Length || _text[_index] != '"')
                    throw new JsonException("Expected object key at " + _index);
                string key = ParseString();
                SkipWhitespace();
                if (_index >= _text.Length || _text[_index] != ':')
                    throw new JsonException("Expected ':' at " + _index);
                _index++;
                SkipWhitespace();
                result[key] = ParseValue();
                SkipWhitespace();
                if (_index >= _text.Length) throw new JsonException("Unterminated object");
                char c = _text[_index++];
                if (c == '}') break;
                if (c != ',') throw new JsonException("Expected ',' or '}' at " + (_index - 1));
            }
            return result;
        }

        private List<object> ParseArray()
        {
            _index++; // [
            List<object> result = new List<object>();
            SkipWhitespace();
            if (_index < _text.Length && _text[_index] == ']')
            {
                _index++;
                return result;
            }

            while (true)
            {
                SkipWhitespace();
                result.Add(ParseValue());
                SkipWhitespace();
                if (_index >= _text.Length) throw new JsonException("Unterminated array");
                char c = _text[_index++];
                if (c == ']') break;
                if (c != ',') throw new JsonException("Expected ',' or ']' at " + (_index - 1));
            }
            return result;
        }

        private string ParseString()
        {
            _index++; // opening quote
            StringBuilder sb = new StringBuilder();
            while (_index < _text.Length)
            {
                char c = _text[_index++];
                if (c == '"') return sb.ToString();
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }

                if (_index >= _text.Length) throw new JsonException("Unterminated escape");
                char esc = _text[_index++];
                switch (esc)
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
                        if (_index + 4 > _text.Length) throw new JsonException("Bad \\u escape");
                        int code;
                        if (!int.TryParse(_text.Substring(_index, 4), NumberStyles.HexNumber,
                                          CultureInfo.InvariantCulture, out code))
                            throw new JsonException("Bad \\u escape");
                        sb.Append((char)code);
                        _index += 4;
                        break;
                    default:
                        throw new JsonException("Unknown escape '\\" + esc + "'");
                }
            }
            throw new JsonException("Unterminated string");
        }

        private double ParseNumber()
        {
            int start = _index;
            if (_text[_index] == '-') _index++;
            while (_index < _text.Length && char.IsDigit(_text[_index])) _index++;
            if (_index < _text.Length && _text[_index] == '.')
            {
                _index++;
                while (_index < _text.Length && char.IsDigit(_text[_index])) _index++;
            }
            if (_index < _text.Length && (_text[_index] == 'e' || _text[_index] == 'E'))
            {
                _index++;
                if (_index < _text.Length && (_text[_index] == '+' || _text[_index] == '-')) _index++;
                while (_index < _text.Length && char.IsDigit(_text[_index])) _index++;
            }
            return double.Parse(_text.Substring(start, _index - start), CultureInfo.InvariantCulture);
        }
    }

    internal static class JsonUtil
    {
        public static List<object> AsArray(object value)
        {
            return value as List<object>;
        }

        public static Dictionary<string, object> AsObject(object value)
        {
            return value as Dictionary<string, object>;
        }

        public static string AsString(object value)
        {
            return value as string;
        }

        public static object At(object value, int index)
        {
            List<object> array = AsArray(value);
            if (array == null || index < 0 || index >= array.Count) return null;
            return array[index];
        }

        public static string StringAt(object value, int index)
        {
            return AsString(At(value, index));
        }

        public static object Get(object value, string key)
        {
            Dictionary<string, object> obj = AsObject(value);
            object result;
            if (obj != null && obj.TryGetValue(key, out result)) return result;
            return null;
        }

        public static string GetString(object value, string key)
        {
            return AsString(Get(value, key));
        }
    }
}
