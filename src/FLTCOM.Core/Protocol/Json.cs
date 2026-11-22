using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FLTCOM.Core.Protocol
{
    /// <summary>Thrown when a message does not match the protocol schema.</summary>
    public sealed class ProtocolException : Exception
    {
        public ProtocolException(string message)
            : base(message)
        {
        }
    }

    /// <summary>Thrown when a message carries a protocol version other than <see cref="ProtocolInfo.Version"/>.</summary>
    public sealed class ProtocolVersionException : Exception
    {
        public ProtocolVersionException(int received)
            : base("protocol version " + received.ToString(CultureInfo.InvariantCulture) + ", expected " + ProtocolInfo.Version.ToString(CultureInfo.InvariantCulture))
        {
            Received = received;
        }

        public int Received { get; }
    }

    internal static class Require
    {
        public static double Finite(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(name, value, "must be finite");
            }
            return value;
        }

        public static IReadOnlyList<T> Items<T>(IReadOnlyList<T> items, string name)
            where T : class
        {
            if (items is null)
            {
                throw new ArgumentNullException(name);
            }
            var copy = new T[items.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = items[i] ?? throw new ArgumentNullException(name, "item " + i.ToString(CultureInfo.InvariantCulture) + " is null");
            }
            return copy;
        }

        public static IReadOnlyList<T> Values<T>(IReadOnlyList<T> items, string name)
            where T : struct
        {
            if (items is null)
            {
                throw new ArgumentNullException(name);
            }
            var copy = new T[items.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = items[i];
            }
            return copy;
        }

        public static IReadOnlyList<T> UniqueBy<T, TKey>(IReadOnlyList<T> items, Func<T, TKey> key, string name)
        {
            int repeat = FirstRepeat(items, key);
            if (repeat >= 0)
            {
                throw new ArgumentException("item " + repeat.ToString(CultureInfo.InvariantCulture) + " repeats the key of an earlier item", name);
            }
            return items;
        }

        public static int FirstRepeat<T, TKey>(IReadOnlyList<T> items, Func<T, TKey> key)
        {
            var seen = new HashSet<TKey>();
            for (int i = 0; i < items.Count; i++)
            {
                if (!seen.Add(key(items[i])))
                {
                    return i;
                }
            }
            return -1;
        }

        public static T Defined<T>(T value, string name)
            where T : struct, Enum
        {
            if (!Enum.IsDefined(typeof(T), value))
            {
                throw new ArgumentOutOfRangeException(name, value, "not a defined value");
            }
            return value;
        }
    }

    internal enum JsonKind
    {
        Null,
        Bool,
        Number,
        String,
        Array,
        Object,
    }

    internal sealed class JsonNode
    {
        private readonly bool _bool;
        private readonly double _number;
        private readonly string? _string;
        private readonly List<JsonNode>? _items;
        private readonly Dictionary<string, JsonNode>? _fields;

        private JsonNode(JsonKind kind, bool boolean = false, double number = 0, string? text = null, List<JsonNode>? items = null, Dictionary<string, JsonNode>? fields = null)
        {
            Kind = kind;
            _bool = boolean;
            _number = number;
            _string = text;
            _items = items;
            _fields = fields;
        }

        public JsonKind Kind { get; }

        public static JsonNode Parse(string json)
        {
            var parser = new Parser(json);
            JsonNode node = parser.ParseValue();
            parser.ExpectEnd();
            return node;
        }

        public static JsonNode ParseMessage(string json, int version)
        {
            JsonNode node = Parse(json ?? throw new ArgumentNullException(nameof(json)));
            int received = node.Field("$", "protocolVersion").AsInt("$.protocolVersion");
            if (received != version)
            {
                throw new ProtocolVersionException(received);
            }
            return node;
        }

        public void ExpectFields(string path, string[] known)
        {
            if (Kind != JsonKind.Object)
            {
                throw Expected(path, "object");
            }
            foreach (string name in _fields!.Keys)
            {
                if (Array.IndexOf(known, name) < 0)
                {
                    throw new ProtocolException(path + "." + name + ": no such field");
                }
            }
        }

        public JsonNode Field(string path, string name)
        {
            if (Kind != JsonKind.Object)
            {
                throw new ProtocolException(path + ": expected object");
            }
            if (!_fields!.TryGetValue(name, out JsonNode? value))
            {
                throw new ProtocolException(path + "." + name + ": missing");
            }
            return value;
        }

        /// <summary>The field, or null when the object does not have it.</summary>
        public JsonNode? TryField(string path, string name)
        {
            if (Kind != JsonKind.Object)
            {
                throw new ProtocolException(path + ": expected object");
            }
            return _fields!.TryGetValue(name, out JsonNode? value) ? value : null;
        }

        public bool AsBool(string path) => Kind == JsonKind.Bool ? _bool : throw Expected(path, "boolean");

        public double AsNumber(string path) => Kind == JsonKind.Number && !double.IsInfinity(_number) ? _number : throw Expected(path, "finite number");

        public int AsInt(string path)
        {
            double value = AsNumber(path);
            if (value != Math.Floor(value) || value < int.MinValue || value > int.MaxValue)
            {
                throw Expected(path, "int32");
            }
            return (int)value;
        }

        public uint AsUInt(string path)
        {
            double value = AsNumber(path);
            if (value != Math.Floor(value) || value < 0 || value > uint.MaxValue)
            {
                throw Expected(path, "uint32");
            }
            return (uint)value;
        }

        public string AsString(string path) => Kind == JsonKind.String ? _string! : throw Expected(path, "string");

        public IReadOnlyList<T> AsArray<T>(string path, Func<JsonNode, int, T> read)
        {
            if (Kind != JsonKind.Array)
            {
                throw Expected(path, "array");
            }
            var result = new T[_items!.Count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = read(_items[i], i);
            }
            return result;
        }

        public static IReadOnlyList<T> UniqueBy<T, TKey>(IReadOnlyList<T> items, Func<T, TKey> key, string path, string keyName)
        {
            int repeat = Require.FirstRepeat(items, key);
            if (repeat >= 0)
            {
                throw new ProtocolException(path + "[" + repeat.ToString(CultureInfo.InvariantCulture) + "]." + keyName + ": expected unique value");
            }
            return items;
        }

        private ProtocolException Expected(string path, string type) => new ProtocolException(path + ": expected " + type + ", got " + Kind.ToString().ToLowerInvariant());

        private sealed class Parser
        {
            private readonly string _text;
            private int _at;
            private int _depth;

            public Parser(string text)
            {
                _text = text;
            }

            public void ExpectEnd()
            {
                SkipWhitespace();
                if (_at != _text.Length)
                {
                    throw Error("trailing characters");
                }
            }

            public JsonNode ParseValue()
            {
                SkipWhitespace();
                if (_at >= _text.Length)
                {
                    throw Error("unexpected end");
                }
                char c = _text[_at];
                switch (c)
                {
                    case '{':
                    case '[':
                        if (++_depth > ProtocolInfo.MaxDepth)
                        {
                            throw Error("nested deeper than " + ProtocolInfo.MaxDepth.ToString(CultureInfo.InvariantCulture));
                        }
                        JsonNode nested = c == '{' ? ParseObject() : ParseArray();
                        _depth--;
                        return nested;
                    case '"':
                        return new JsonNode(JsonKind.String, text: ParseString());
                    case 't':
                        Literal("true");
                        return new JsonNode(JsonKind.Bool, boolean: true);
                    case 'f':
                        Literal("false");
                        return new JsonNode(JsonKind.Bool, boolean: false);
                    case 'n':
                        Literal("null");
                        return new JsonNode(JsonKind.Null);
                    default:
                        return new JsonNode(JsonKind.Number, number: ParseNumber());
                }
            }

            private JsonNode ParseObject()
            {
                var fields = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
                _at++;
                SkipWhitespace();
                if (Peek() == '}')
                {
                    _at++;
                    return new JsonNode(JsonKind.Object, fields: fields);
                }
                while (true)
                {
                    SkipWhitespace();
                    if (Peek() != '"')
                    {
                        throw Error("expected property name");
                    }
                    string name = ParseString();
                    SkipWhitespace();
                    Expect(':');
                    fields[name] = ParseValue();
                    SkipWhitespace();
                    if (Peek() == ',')
                    {
                        _at++;
                        continue;
                    }
                    Expect('}');
                    return new JsonNode(JsonKind.Object, fields: fields);
                }
            }

            private JsonNode ParseArray()
            {
                var items = new List<JsonNode>();
                _at++;
                SkipWhitespace();
                if (Peek() == ']')
                {
                    _at++;
                    return new JsonNode(JsonKind.Array, items: items);
                }
                while (true)
                {
                    items.Add(ParseValue());
                    SkipWhitespace();
                    if (Peek() == ',')
                    {
                        _at++;
                        continue;
                    }
                    Expect(']');
                    return new JsonNode(JsonKind.Array, items: items);
                }
            }

            private string ParseString()
            {
                _at++;
                var builder = new StringBuilder();
                while (true)
                {
                    if (_at >= _text.Length)
                    {
                        throw Error("unterminated string");
                    }
                    char c = _text[_at++];
                    if (c == '"')
                    {
                        return builder.ToString();
                    }
                    if (c < ' ')
                    {
                        throw Error("control character in string");
                    }
                    if (c != '\\')
                    {
                        builder.Append(c);
                        continue;
                    }
                    if (_at >= _text.Length)
                    {
                        throw Error("unterminated escape");
                    }
                    char escape = _text[_at++];
                    switch (escape)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            if (_at + 4 > _text.Length || !int.TryParse(_text.Substring(_at, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code))
                            {
                                throw Error("bad unicode escape");
                            }
                            builder.Append((char)code);
                            _at += 4;
                            break;
                        default:
                            throw Error("bad escape");
                    }
                }
            }

            private double ParseNumber()
            {
                int start = _at;
                if (Peek() == '-')
                {
                    _at++;
                }
                if (Peek() == '0')
                {
                    _at++;
                }
                else if (!SkipDigits())
                {
                    _at = start;
                    throw Error("unexpected character");
                }
                if (Peek() == '.')
                {
                    _at++;
                    ExpectDigits();
                }
                if (Peek() == 'e' || Peek() == 'E')
                {
                    _at++;
                    if (Peek() == '+' || Peek() == '-')
                    {
                        _at++;
                    }
                    ExpectDigits();
                }
                if (!double.TryParse(_text.Substring(start, _at - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                {
                    throw Error("number out of range");
                }
                return value;
            }

            private bool SkipDigits()
            {
                int start = _at;
                while (_at < _text.Length && _text[_at] >= '0' && _text[_at] <= '9')
                {
                    _at++;
                }
                return _at > start;
            }

            private void ExpectDigits()
            {
                if (!SkipDigits())
                {
                    throw Error("expected digit");
                }
            }

            private void Literal(string word)
            {
                if (string.CompareOrdinal(_text, _at, word, 0, word.Length) != 0)
                {
                    throw Error("unexpected character");
                }
                _at += word.Length;
            }

            private void Expect(char c)
            {
                if (Peek() != c)
                {
                    throw Error("expected '" + c + "'");
                }
                _at++;
            }

            private char Peek() => _at < _text.Length ? _text[_at] : '\0';

            private void SkipWhitespace()
            {
                while (_at < _text.Length && (_text[_at] == ' ' || _text[_at] == '\t' || _text[_at] == '\n' || _text[_at] == '\r'))
                {
                    _at++;
                }
            }

            private ProtocolException Error(string what) => new ProtocolException("invalid JSON at " + _at.ToString(CultureInfo.InvariantCulture) + ": " + what);
        }
    }

    internal sealed class JsonWriter
    {
        private readonly StringBuilder _out = new StringBuilder();
        private bool _needComma;

        public override string ToString() => _out.ToString();

        public void BeginObject()
        {
            Separate();
            _out.Append('{');
            _needComma = false;
        }

        public void EndObject()
        {
            _out.Append('}');
            _needComma = true;
        }

        public void BeginArray()
        {
            Separate();
            _out.Append('[');
            _needComma = false;
        }

        public void EndArray()
        {
            _out.Append(']');
            _needComma = true;
        }

        public void Name(string name)
        {
            Separate();
            WriteString(name);
            _out.Append(':');
            _needComma = false;
        }

        public void Null()
        {
            Separate();
            _out.Append("null");
            _needComma = true;
        }

        public void Bool(bool value)
        {
            Separate();
            _out.Append(value ? "true" : "false");
            _needComma = true;
        }

        public void Number(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "JSON has no NaN or infinity");
            }
            Separate();
            _out.Append(value.ToString("G17", CultureInfo.InvariantCulture));
            _needComma = true;
        }

        public void Number(long value)
        {
            Separate();
            _out.Append(value.ToString(CultureInfo.InvariantCulture));
            _needComma = true;
        }

        public void String(string value)
        {
            Separate();
            WriteString(value);
            _needComma = true;
        }

        private void Separate()
        {
            if (_needComma)
            {
                _out.Append(',');
            }
        }

        private void WriteString(string value)
        {
            _out.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': _out.Append("\\\""); break;
                    case '\\': _out.Append("\\\\"); break;
                    case '\n': _out.Append("\\n"); break;
                    case '\r': _out.Append("\\r"); break;
                    case '\t': _out.Append("\\t"); break;
                    default:
                        if (c < ' ')
                        {
                            _out.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            _out.Append(c);
                        }
                        break;
                }
            }
            _out.Append('"');
        }
    }
}
