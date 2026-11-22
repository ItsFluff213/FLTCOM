using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FLTCOM.TestHarness
{
    internal static class Json
    {
        public static string Write(object? value)
        {
            var builder = new StringBuilder();
            Append(builder, value);
            return builder.ToString();
        }

        private static void Append(StringBuilder builder, object? value)
        {
            switch (value)
            {
                case null:
                    builder.Append("null");
                    break;
                case string text:
                    AppendString(builder, text);
                    break;
                case bool flag:
                    builder.Append(flag ? "true" : "false");
                    break;
                case double number:
                    AppendNumber(builder, number);
                    break;
                case float number:
                    AppendNumber(builder, number);
                    break;
                case int or long or uint or ulong:
                    builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
                case IDictionary<string, object?> map:
                    builder.Append('{');
                    var first = true;
                    foreach (var pair in map)
                    {
                        if (!first)
                        {
                            builder.Append(',');
                        }
                        first = false;
                        AppendString(builder, pair.Key);
                        builder.Append(':');
                        Append(builder, pair.Value);
                    }
                    builder.Append('}');
                    break;
                case IEnumerable list:
                    builder.Append('[');
                    var firstItem = true;
                    foreach (var item in list)
                    {
                        if (!firstItem)
                        {
                            builder.Append(',');
                        }
                        firstItem = false;
                        Append(builder, item);
                    }
                    builder.Append(']');
                    break;
                default:
                    AppendString(builder, value.ToString());
                    break;
            }
        }

        private static void AppendNumber(StringBuilder builder, double number)
        {
            if (double.IsNaN(number) || double.IsInfinity(number))
            {
                builder.Append("null");
                return;
            }
            builder.Append(number.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void AppendString(StringBuilder builder, string text)
        {
            builder.Append('"');
            foreach (var c in text)
            {
                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (c < ' ')
                        {
                            builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }
                        break;
                }
            }
            builder.Append('"');
        }

        public static Dictionary<string, string> ParseFlatObject(string line)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var position = 0;
            SkipWhitespace(line, ref position);
            Expect(line, ref position, '{');
            SkipWhitespace(line, ref position);
            if (Peek(line, position) == '}')
            {
                return result;
            }
            while (true)
            {
                SkipWhitespace(line, ref position);
                var key = ReadString(line, ref position);
                SkipWhitespace(line, ref position);
                Expect(line, ref position, ':');
                SkipWhitespace(line, ref position);
                result[key] = ReadScalar(line, ref position);
                SkipWhitespace(line, ref position);
                if (Peek(line, position) == ',')
                {
                    position++;
                    continue;
                }
                Expect(line, ref position, '}');
                return result;
            }
        }

        private static char Peek(string text, int position) => position < text.Length ? text[position] : '\0';

        private static void SkipWhitespace(string text, ref int position)
        {
            while (position < text.Length && char.IsWhiteSpace(text[position]))
            {
                position++;
            }
        }

        private static void Expect(string text, ref int position, char expected)
        {
            if (Peek(text, position) != expected)
            {
                throw new FormatException($"expected '{expected}' at column {position + 1}");
            }
            position++;
        }

        private static string ReadScalar(string text, ref int position)
        {
            if (Peek(text, position) == '"')
            {
                return ReadString(text, ref position);
            }
            var start = position;
            while (position < text.Length && text[position] != ',' && text[position] != '}' && !char.IsWhiteSpace(text[position]))
            {
                position++;
            }
            if (position == start)
            {
                throw new FormatException($"expected a value at column {start + 1}");
            }
            return text.Substring(start, position - start);
        }

        private static string ReadString(string text, ref int position)
        {
            Expect(text, ref position, '"');
            var builder = new StringBuilder();
            while (true)
            {
                if (position >= text.Length)
                {
                    throw new FormatException("unterminated string");
                }
                var c = text[position++];
                if (c == '"')
                {
                    return builder.ToString();
                }
                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }
                var escaped = Peek(text, position++);
                switch (escaped)
                {
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'u':
                        builder.Append((char)int.Parse(text.Substring(position, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        position += 4;
                        break;
                    default:
                        builder.Append(escaped);
                        break;
                }
            }
        }
    }
}
