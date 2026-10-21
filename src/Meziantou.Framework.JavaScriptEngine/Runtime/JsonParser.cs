using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// A JSON Parse Record (https://tc39.es/proposal-json-parse-with-source/): the value of a JSON value, its source text
/// (primitive values) and the records of its elements or entries.
/// </summary>
internal sealed class JsonParseRecord(JsValue value, int sourceStart, int sourceLength)
{
    public JsValue Value { get; } = value;

    public int SourceStart { get; } = sourceStart;

    public int SourceLength { get; } = sourceLength;

    public List<JsonParseRecord>? Elements { get; set; }

    public Dictionary<PropertyKey, JsonParseRecord>? Entries { get; set; }
}

/// <summary>
/// Parses a JSON text (https://tc39.es/ecma262/#sec-json.parse, ECMA-404) into JavaScript values. The containers are
/// kept in an explicit stack, so the nesting depth is limited by the memory only.
/// </summary>
internal sealed class JsonParser
{
    private const int MaxCachedKeyLength = 32;
    private const int MaxCachedKeys = 4096;

    // The characters that end the fast scan of a string: the quote, the backslash and the control characters
    private static readonly SearchValues<char> StringSpecialCharacters = SearchValues.Create("\"\\\u0000\u0001\u0002\u0003\u0004\u0005\u0006\u0007\u0008\u0009\u000A\u000B\u000C\u000D\u000E\u000F\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001A\u001B\u001C\u001D\u001E\u001F");

    private readonly string _text;
    private readonly JsObject _objectPrototype;
    private readonly JsRealm _realm;
    private readonly bool _trackSource;
    private readonly List<JsValue> _elements = [];
    private readonly List<JsonParseRecord> _elementRecords = [];
    private readonly List<Frame> _stack = [];
    private Dictionary<string, PropertyKey>? _keyCache;
    private StringBuilder? _builder;
    private int _position;

    private JsonParser(JsRealm realm, string text, bool trackSource)
    {
        _realm = realm;
        _text = text;
        _objectPrototype = realm.Intrinsics.ObjectPrototype;
        _trackSource = trackSource;
    }

    /// <summary>ParseJSON(text): the value, and its parse record when <paramref name="record"/> is requested (a reviver is used).</summary>
    public static JsValue Parse(JsRealm realm, string text, bool trackSource, out JsonParseRecord? record)
    {
        var parser = new JsonParser(realm, text, trackSource);
        return parser.ParseText(out record);
    }

    /// <summary>Gets whether the text is a JSON text whose value is not an object nor an array (JSON.rawJSON); throws a SyntaxError otherwise.</summary>
    public static void ValidatePrimitive(JsRealm realm, string text)
    {
        var parser = new JsonParser(realm, text, trackSource: false);
        parser.SkipWhiteSpace();
        if (parser._position < text.Length && text[parser._position] is '{' or '[')
        {
            parser.ThrowUnexpectedToken();
        }

        parser.ParseText(out _);
    }

    private JsValue ParseText(out JsonParseRecord? record)
    {
        var value = ParseValue(out record);
        SkipWhiteSpace();
        if (_position < _text.Length)
        {
            ThrowSyntaxError("Unexpected non-whitespace character after JSON", inJson: false);
        }

        return value;
    }

    private JsValue ParseValue(out JsonParseRecord? rootRecord)
    {
        var text = _text;
        while (true)
        {
            // Read a value: a primitive value, or the start of a container
            SkipWhiteSpace();
            if (_position >= text.Length)
            {
                ThrowUnexpectedEnd();
            }

            JsValue value;
            JsonParseRecord? record = null;
            var start = _position;
            var c = text[_position];
            switch (c)
            {
                case '{':
                    _position++;
                    SkipWhiteSpace();
                    if (_position < text.Length && text[_position] == '}')
                    {
                        _position++;
                        value = new JsObject(_objectPrototype);
                        if (_trackSource)
                        {
                            record = new JsonParseRecord(value, start, _position - start) { Entries = [] };
                        }

                        break;
                    }

                    var obj = new JsObject(_objectPrototype);
                    _stack.Add(new Frame(obj, ParsePropertyName(), _trackSource ? [] : null));
                    continue;

                case '[':
                    _position++;
                    SkipWhiteSpace();
                    if (_position < text.Length && text[_position] == ']')
                    {
                        _position++;
                        value = new JsArray(_realm.Intrinsics.ArrayPrototype);
                        if (_trackSource)
                        {
                            record = new JsonParseRecord(value, start, _position - start) { Elements = [] };
                        }

                        break;
                    }

                    _stack.Add(new Frame(_elements.Count, _elementRecords.Count));
                    continue;

                case '"':
                    value = JsValue.FromJsString(JsString.Create(ParseString()));
                    break;

                case 't':
                    ExpectLiteral("true");
                    value = JsValue.True;
                    break;

                case 'f':
                    ExpectLiteral("false");
                    value = JsValue.False;
                    break;

                case 'n':
                    ExpectLiteral("null");
                    value = JsValue.Null;
                    break;

                default:
                    if (c == '-' || char.IsAsciiDigit(c))
                    {
                        value = ParseNumber();
                        break;
                    }

                    ThrowUnexpectedToken();
                    rootRecord = null;
                    return default;
            }

            if (_trackSource && record is null)
            {
                record = new JsonParseRecord(value, start, _position - start);
            }

            // Add the value to the enclosing containers, closing the containers that end after it
            while (true)
            {
                if (_stack.Count == 0)
                {
                    rootRecord = record;
                    return value;
                }

                var frameIndex = _stack.Count - 1;
                var frame = _stack[frameIndex];
                SkipWhiteSpace();
                if (frame.Object is { } container)
                {
                    // Duplicate keys: the last value wins, at the position of the first one
                    container.SetOwnSlot(frame.Key, value, PropertyFlags.Default);
                    if (frame.Entries is { } entries)
                    {
                        entries[frame.Key] = record!;
                    }

                    if (_position < text.Length && text[_position] == ',')
                    {
                        _position++;
                        SkipWhiteSpace();
                        frame.Key = ParsePropertyName();
                        _stack[frameIndex] = frame;
                        break;
                    }

                    if (_position < text.Length && text[_position] == '}')
                    {
                        _position++;
                        _stack.RemoveAt(frameIndex);
                        value = container;
                        if (_trackSource)
                        {
                            record = new JsonParseRecord(value, 0, 0) { Entries = frame.Entries };
                        }

                        continue;
                    }

                    ThrowExpected("',' or '}' after property value");
                }
                else
                {
                    _elements.Add(value);
                    if (_trackSource)
                    {
                        _elementRecords.Add(record!);
                    }

                    if (_position < text.Length && text[_position] == ',')
                    {
                        _position++;
                        break;
                    }

                    if (_position < text.Length && text[_position] == ']')
                    {
                        _position++;
                        _stack.RemoveAt(frameIndex);
                        var count = _elements.Count - frame.ElementsStart;
                        var array = JsArray.Create(_realm, CollectionsMarshal.AsSpan(_elements).Slice(frame.ElementsStart, count));
                        _elements.RemoveRange(frame.ElementsStart, count);
                        value = array;
                        if (_trackSource)
                        {
                            var elementRecords = _elementRecords.GetRange(frame.RecordsStart, count);
                            _elementRecords.RemoveRange(frame.RecordsStart, count);
                            record = new JsonParseRecord(value, 0, 0) { Elements = elementRecords };
                        }

                        continue;
                    }

                    ThrowExpected("',' or ']' after array element");
                }
            }
        }
    }

    /// <summary>Parses a property name and the colon that follows it.</summary>
    private PropertyKey ParsePropertyName()
    {
        if (_position >= _text.Length || _text[_position] != '"')
        {
            ThrowExpected("property name or '}'");
        }

        var key = ParseKey();
        SkipWhiteSpace();
        if (_position >= _text.Length || _text[_position] != ':')
        {
            ThrowExpected("':' after property name");
        }

        _position++;
        return key;
    }

    private PropertyKey ParseKey()
    {
        // Short keys without escapes are cached: the objects of an array often have the same keys
        var start = _position + 1;
        var end = _text.AsSpan(start).IndexOfAny(StringSpecialCharacters);
        if (end >= 0 && end <= MaxCachedKeyLength && _text[start + end] == '"')
        {
            var span = _text.AsSpan(start, end);
            _keyCache ??= new Dictionary<string, PropertyKey>(StringComparer.Ordinal);
            var lookup = _keyCache.GetAlternateLookup<ReadOnlySpan<char>>();
            if (!lookup.TryGetValue(span, out var cached))
            {
                var name = span.ToString();
                cached = new PropertyKey(name);
                if (_keyCache.Count < MaxCachedKeys)
                {
                    _keyCache.Add(name, cached);
                }
            }

            _position = start + end + 1;
            return cached;
        }

        return new PropertyKey(ParseString());
    }

    /// <summary>Parses a string: the current character is the opening quote.</summary>
    private string ParseString()
    {
        var text = _text;
        var start = _position + 1;
        var end = text.AsSpan(start).IndexOfAny(StringSpecialCharacters);
        if (end < 0)
        {
            _position = text.Length;
            ThrowSyntaxError("Unterminated string");
        }

        var position = start + end;
        if (text[position] == '"')
        {
            _position = position + 1;
            return text.Substring(start, end);
        }

        var builder = _builder ??= new StringBuilder();
        builder.Clear();
        builder.Append(text, start, end);
        while (true)
        {
            if (position >= text.Length)
            {
                _position = position;
                ThrowSyntaxError("Unterminated string");
            }

            var c = text[position];
            if (c == '"')
            {
                _position = position + 1;
                return builder.ToString();
            }

            if (c < 0x20)
            {
                _position = position;
                ThrowSyntaxError("Bad control character in string literal");
            }

            if (c != '\\')
            {
                // Copy the characters up to the next special character
                var next = text.AsSpan(position).IndexOfAny(StringSpecialCharacters);
                if (next < 0)
                {
                    _position = text.Length;
                    ThrowSyntaxError("Unterminated string");
                }

                builder.Append(text, position, next);
                position += next;
                continue;
            }

            position++;
            if (position >= text.Length)
            {
                _position = position;
                ThrowSyntaxError("Unterminated string");
            }

            switch (text[position])
            {
                case '"':
                    builder.Append('"');
                    break;
                case '\\':
                    builder.Append('\\');
                    break;
                case '/':
                    builder.Append('/');
                    break;
                case 'b':
                    builder.Append('\b');
                    break;
                case 'f':
                    builder.Append('\f');
                    break;
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
                    var value = 0;
                    for (var i = 1; i <= 4; i++)
                    {
                        var digit = position + i < text.Length ? GetHexValue(text[position + i]) : -1;
                        if (digit < 0)
                        {
                            _position = position + i;
                            ThrowSyntaxError("Bad Unicode escape");
                        }

                        value = (value << 4) | digit;
                    }

                    builder.Append((char)value);
                    position += 4;
                    break;
                default:
                    _position = position;
                    ThrowSyntaxError("Bad escaped character");
                    break;
            }

            position++;
        }
    }

    private static int GetHexValue(char c)
    {
        return c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => -1,
        };
    }

    /// <summary>Parses a number: -? (0 | [1-9][0-9]*) (. [0-9]+)? ([eE] [+-]? [0-9]+)?</summary>
    private JsValue ParseNumber()
    {
        var text = _text;
        var start = _position;
        var position = start;
        var negative = text[position] == '-';
        if (negative)
        {
            position++;
            if (position >= text.Length || !char.IsAsciiDigit(text[position]))
            {
                _position = position;
                ThrowSyntaxError("No number after minus sign");
            }
        }

        long integer = 0;
        var integerStart = position;
        if (text[position] == '0')
        {
            position++;
        }
        else
        {
            while (position < text.Length && char.IsAsciiDigit(text[position]))
            {
                integer = (integer * 10) + (text[position] - '0');
                position++;
                if (position - integerStart > 15)
                    break;
            }

            while (position < text.Length && char.IsAsciiDigit(text[position]))
            {
                position++;
            }
        }

        var isInteger = true;
        if (position < text.Length && text[position] == '.')
        {
            isInteger = false;
            position++;
            if (position >= text.Length || !char.IsAsciiDigit(text[position]))
            {
                _position = position;
                ThrowSyntaxError("Unterminated fractional number");
            }

            while (position < text.Length && char.IsAsciiDigit(text[position]))
            {
                position++;
            }
        }

        if (position < text.Length && (text[position] == 'e' || text[position] == 'E'))
        {
            isInteger = false;
            position++;
            if (position < text.Length && (text[position] == '+' || text[position] == '-'))
            {
                position++;
            }

            if (position >= text.Length || !char.IsAsciiDigit(text[position]))
            {
                _position = position;
                ThrowSyntaxError("Exponent part is missing a number");
            }

            while (position < text.Length && char.IsAsciiDigit(text[position]))
            {
                position++;
            }
        }

        _position = position;

        // At most 15 digits: the integer is exact
        if (isInteger && position - integerStart <= 15)
        {
            double result = integer;
            return negative ? -result : result;
        }

        return double.Parse(text.AsSpan(start, position - start), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture);
    }

    private void ExpectLiteral(string literal)
    {
        if (string.CompareOrdinal(_text, _position, literal, 0, literal.Length) == 0)
        {
            _position += literal.Length;
            return;
        }

        // Report the first character that does not match
        for (var i = 0; i < literal.Length; i++)
        {
            if (_position + i >= _text.Length)
            {
                _position += i;
                ThrowUnexpectedEnd();
            }

            if (_text[_position + i] != literal[i])
            {
                _position += i;
                ThrowUnexpectedToken();
            }
        }
    }

    private void SkipWhiteSpace()
    {
        var text = _text;
        var position = _position;
        while (position < text.Length && text[position] is ' ' or '\t' or '\n' or '\r')
        {
            position++;
        }

        _position = position;
    }

    [DoesNotReturn]
    private void ThrowExpected(string expected)
    {
        if (_position >= _text.Length)
        {
            ThrowUnexpectedEnd();
        }

        ThrowSyntaxError("Expected " + expected);
    }

    [DoesNotReturn]
    private void ThrowUnexpectedToken()
    {
        if (_position >= _text.Length)
        {
            ThrowUnexpectedEnd();
        }

        var c = _text[_position];
        ThrowSyntaxError(c < 0x20 || char.IsSurrogate(c) ? $"Unexpected character U+{(int)c:X4}" : $"Unexpected token '{c}'");
    }

    [DoesNotReturn]
    private static void ThrowUnexpectedEnd() => ThrowHelper.ThrowSyntaxError("Unexpected end of JSON input");

    /// <summary>Throws a SyntaxError whose message has the position, the line and the column of the error.</summary>
    [DoesNotReturn]
    private void ThrowSyntaxError(string message, bool inJson = true)
    {
        var line = 1;
        var lineStart = 0;
        var position = Math.Min(_position, _text.Length);
        for (var i = 0; i < position; i++)
        {
            if (_text[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }

        ThrowHelper.ThrowSyntaxError(string.Create(CultureInfo.InvariantCulture, $"{message}{(inJson ? " in JSON" : "")} at position {position} (line {line} column {position - lineStart + 1})"));
    }

    /// <summary>An array or an object being parsed.</summary>
    private struct Frame
    {
        public Frame(JsObject obj, PropertyKey key, Dictionary<PropertyKey, JsonParseRecord>? entries)
        {
            Object = obj;
            Key = key;
            Entries = entries;
        }

        public Frame(int elementsStart, int recordsStart)
        {
            ElementsStart = elementsStart;
            RecordsStart = recordsStart;
        }

        /// <summary>The object, or null for an array (its elements are at the end of the shared element list).</summary>
        public JsObject? Object;

        /// <summary>The key of the property whose value is being parsed.</summary>
        public PropertyKey Key;

        public Dictionary<PropertyKey, JsonParseRecord>? Entries;

        public int ElementsStart;

        public int RecordsStart;
    }
}
