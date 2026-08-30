using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Rekindling.ModLoader
{
    /// <summary>
    /// Thrown when a <c>mod.json</c> is malformed. Carries a line and column so the author can
    /// find the problem without guessing.
    /// </summary>
    internal sealed class JsonException : Exception
    {
        public JsonException(string message, int line, int column)
            : base($"{message} (line {line}, column {column})")
        {
            Line = line;
            Column = column;
        }

        public int Line { get; }
        public int Column { get; }
    }

    internal enum JsonKind { Null, Bool, Number, String, Array, Object }

    /// <summary>
    /// A parsed JSON value.
    /// </summary>
    /// <remarks>
    /// Hand-rolled rather than pulled from NuGet so the loader ships as a single assembly plus
    /// Harmony. Manifests are small and the shape is fixed, so a full serializer would be more
    /// dependency than it is worth - and this way malformed-manifest errors can point at a line
    /// and column, which is the failure mode mod authors actually hit.
    /// </remarks>
    internal sealed class JsonValue
    {
        private readonly object _value;

        private JsonValue(JsonKind kind, object value)
        {
            Kind = kind;
            _value = value;
        }

        public JsonKind Kind { get; }

        public static readonly JsonValue Null = new JsonValue(JsonKind.Null, null);

        internal static JsonValue Bool(bool v) => new JsonValue(JsonKind.Bool, v);
        internal static JsonValue Number(double v) => new JsonValue(JsonKind.Number, v);
        internal static JsonValue Str(string v) => new JsonValue(JsonKind.String, v);
        internal static JsonValue Arr(List<JsonValue> v) => new JsonValue(JsonKind.Array, v);
        internal static JsonValue Obj(Dictionary<string, JsonValue> v) => new JsonValue(JsonKind.Object, v);

        public bool IsNull => Kind == JsonKind.Null;

        private static readonly JsonValue[] EmptyArray = new JsonValue[0];
        private static readonly Dictionary<string, JsonValue> EmptyObject = new Dictionary<string, JsonValue>();

        public IReadOnlyList<JsonValue> AsArray =>
            _value as List<JsonValue> ?? (IReadOnlyList<JsonValue>)EmptyArray;

        public IReadOnlyDictionary<string, JsonValue> AsObject =>
            _value as Dictionary<string, JsonValue> ?? EmptyObject;

        /// <summary>Member lookup; returns <see cref="Null"/> for anything missing.</summary>
        public JsonValue this[string key]
        {
            get
            {
                if (_value is Dictionary<string, JsonValue> dict && dict.TryGetValue(key, out JsonValue v))
                    return v;
                return Null;
            }
        }

        /// <summary>The value as a string, or <paramref name="fallback"/> when absent or not a string.</summary>
        public string AsString(string fallback = null)
            => Kind == JsonKind.String ? (string)_value : fallback;

        public bool AsBool(bool fallback = false)
            => Kind == JsonKind.Bool ? (bool)_value : fallback;

        public double AsNumber(double fallback = 0)
            => Kind == JsonKind.Number ? (double)_value : fallback;

        /// <summary>
        /// Reads a string list, tolerating a bare string where a list was expected -
        /// <c>"loadAfter": "some.mod"</c> is a common and harmless mistake.
        /// </summary>
        public List<string> AsStringList()
        {
            var result = new List<string>();
            if (Kind == JsonKind.String)
            {
                result.Add((string)_value);
            }
            else if (Kind == JsonKind.Array)
            {
                foreach (JsonValue item in AsArray)
                {
                    string s = item.AsString();
                    if (!string.IsNullOrWhiteSpace(s))
                        result.Add(s);
                }
            }
            return result;
        }

        public static JsonValue Parse(string text) => new JsonParser(text).ParseDocument();
    }

    internal sealed class JsonParser
    {
        private readonly string _text;
        private int _index;
        private int _line = 1;
        private int _lineStart;

        public JsonParser(string text) => _text = text ?? string.Empty;

        private int Column => _index - _lineStart + 1;

        public JsonValue ParseDocument()
        {
            SkipWhitespace();
            if (_index >= _text.Length)
                throw Error("The file is empty");

            JsonValue value = ParseValue();
            SkipWhitespace();
            if (_index < _text.Length)
                throw Error($"Unexpected trailing character '{_text[_index]}'");
            return value;
        }

        private JsonValue ParseValue()
        {
            SkipWhitespace();
            if (_index >= _text.Length)
                throw Error("Unexpected end of file");

            char c = _text[_index];
            switch (c)
            {
                case '{': return ParseObject();
                case '[': return ParseArray();
                case '"': return JsonValue.Str(ParseString());
                case 't': Expect("true"); return JsonValue.Bool(true);
                case 'f': Expect("false"); return JsonValue.Bool(false);
                case 'n': Expect("null"); return JsonValue.Null;
                default:
                    if (c == '-' || (c >= '0' && c <= '9'))
                        return ParseNumber();
                    throw Error($"Unexpected character '{c}'");
            }
        }

        private JsonValue ParseObject()
        {
            var result = new Dictionary<string, JsonValue>(StringComparer.OrdinalIgnoreCase);
            _index++; // consume '{'
            SkipWhitespace();

            if (Peek() == '}')
            {
                _index++;
                return JsonValue.Obj(result);
            }

            while (true)
            {
                SkipWhitespace();
                if (Peek() != '"')
                    throw Error("Expected a quoted property name");

                string key = ParseString();
                SkipWhitespace();
                if (Peek() != ':')
                    throw Error($"Expected ':' after property \"{key}\"");
                _index++;

                result[key] = ParseValue();
                SkipWhitespace();

                char c = Peek();
                if (c == ',')
                {
                    _index++;
                    SkipWhitespace();
                    // Tolerate a trailing comma before the closing brace.
                    if (Peek() == '}')
                    {
                        _index++;
                        return JsonValue.Obj(result);
                    }
                    continue;
                }

                if (c == '}')
                {
                    _index++;
                    return JsonValue.Obj(result);
                }

                throw Error("Expected ',' or '}'");
            }
        }

        private JsonValue ParseArray()
        {
            var result = new List<JsonValue>();
            _index++; // consume '['
            SkipWhitespace();

            if (Peek() == ']')
            {
                _index++;
                return JsonValue.Arr(result);
            }

            while (true)
            {
                result.Add(ParseValue());
                SkipWhitespace();

                char c = Peek();
                if (c == ',')
                {
                    _index++;
                    SkipWhitespace();
                    if (Peek() == ']')
                    {
                        _index++;
                        return JsonValue.Arr(result);
                    }
                    continue;
                }

                if (c == ']')
                {
                    _index++;
                    return JsonValue.Arr(result);
                }

                throw Error("Expected ',' or ']'");
            }
        }

        private string ParseString()
        {
            _index++; // consume opening quote
            var sb = new StringBuilder();

            while (true)
            {
                if (_index >= _text.Length)
                    throw Error("Unterminated string");

                char c = _text[_index];
                if (c == '"')
                {
                    _index++;
                    return sb.ToString();
                }

                if (c == '\\')
                {
                    _index++;
                    if (_index >= _text.Length)
                        throw Error("Unterminated escape sequence");

                    char esc = _text[_index];
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
                            if (_index + 4 >= _text.Length)
                                throw Error("Truncated unicode escape");
                            string hex = _text.Substring(_index + 1, 4);
                            if (!ushort.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort code))
                                throw Error($"Invalid unicode escape '{hex}'");
                            sb.Append((char)code);
                            _index += 4;
                            break;
                        default:
                            throw Error($"Unsupported escape character '{esc}'");
                    }

                    _index++;
                    continue;
                }

                if (c == '\n')
                    throw Error("Line break inside a string");

                sb.Append(c);
                _index++;
            }
        }

        private JsonValue ParseNumber()
        {
            int start = _index;
            if (Peek() == '-')
                _index++;

            while (_index < _text.Length)
            {
                char c = _text[_index];
                if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-')
                    _index++;
                else
                    break;
            }

            string raw = _text.Substring(start, _index - start);
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                throw Error($"Invalid number '{raw}'");
            return JsonValue.Number(value);
        }

        private char Peek() => _index < _text.Length ? _text[_index] : '\0';

        private void Expect(string literal)
        {
            if (_index + literal.Length > _text.Length ||
                string.CompareOrdinal(_text, _index, literal, 0, literal.Length) != 0)
                throw Error($"Expected '{literal}'");
            _index += literal.Length;
        }

        private void SkipWhitespace()
        {
            while (_index < _text.Length)
            {
                char c = _text[_index];
                if (c == '\n')
                {
                    _line++;
                    _index++;
                    _lineStart = _index;
                }
                else if (c == ' ' || c == '\t' || c == '\r')
                {
                    _index++;
                }
                else if (c == '/' && _index + 1 < _text.Length && _text[_index + 1] == '/')
                {
                    // Line comments are not legal JSON, but authors write them anyway.
                    while (_index < _text.Length && _text[_index] != '\n')
                        _index++;
                }
                else if (c == '/' && _index + 1 < _text.Length && _text[_index + 1] == '*')
                {
                    _index += 2;
                    while (_index + 1 < _text.Length && !(_text[_index] == '*' && _text[_index + 1] == '/'))
                    {
                        if (_text[_index] == '\n')
                        {
                            _line++;
                            _lineStart = _index + 1;
                        }
                        _index++;
                    }
                    _index = Math.Min(_index + 2, _text.Length);
                }
                else
                {
                    break;
                }
            }
        }

        private JsonException Error(string message) => new JsonException(message, _line, Column);
    }
}
