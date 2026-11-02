using System.Buffers;
using System.Globalization;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>JSON.stringify: the JSON Serialization Record and SerializeJSONProperty (https://tc39.es/ecma262/#sec-json.stringify).</summary>
internal sealed class JsonSerializer
{
    private static readonly PropertyKey ToJsonKey = new("toJSON");

    // The characters that QuoteJSONString escapes: the quote, the backslash, the control characters and the surrogates
    // (a lone surrogate is escaped; the search stops at every surrogate, then the pairs are copied)
    private static readonly SearchValues<char> QuotedSpecialCharacters = SearchValues.Create(CreateQuotedSpecialCharacters());

    private readonly JsEngine _engine;

    // The text: a buffer of the shared array pool (its previous sizes go back to the pool as it grows, so a large text does
    // not allocate a chain of chunks at each serialization)
    private readonly JsonTextWriter _builder = new();
    private readonly HashSet<JsObject> _stack = new(ReferenceEqualityComparer.Instance);
    private readonly JsObject? _replacerFunction;
    private readonly List<PropertyKey>? _propertyList;
    private readonly string _gap;

    // Two prototypes whose chains have no toJSON property (usually %Object.prototype% and %Array.prototype%), valid while
    // the epoch of the inline caches does not change (their objects are flagged: a change of their properties or of their
    // prototypes increments the epoch)
    private JsObject? _prototypeWithoutToJson1;
    private JsObject? _prototypeWithoutToJson2;
    private int _prototypesWithoutToJsonEpoch;
    private string _indent = "";
    private int _checkedLength;

    public JsonSerializer(JsEngine engine, JsObject? replacerFunction, List<PropertyKey>? propertyList, string gap)
    {
        _engine = engine;
        _replacerFunction = replacerFunction;
        _propertyList = propertyList;
        _gap = gap;
    }

    /// <summary>SerializeJSONProperty(state, "", wrapper): the JSON text, or null for undefined.</summary>
    public string? Serialize(JsObject wrapper)
    {
        try
        {
            if (!SerializeProperty(new PropertyKey(""), wrapper))
                return null;

            return _builder.ToString();
        }
        finally
        {
            _builder.Release();
        }
    }

    /// <summary>SerializeJSONProperty(state, key, holder): appends the JSON text of the property; false when the result is undefined.</summary>
    private bool SerializeProperty(in PropertyKey key, JsObject holder) => SerializeProperty(key, holder, holder.Get(key));

    /// <summary>SerializeJSONProperty(state, key, holder) after Get(holder, key) returned <paramref name="value"/>.</summary>
    private bool SerializeProperty(in PropertyKey key, JsObject holder, JsValue value)
    {
        if (value.Cell is JsObject or JsBigInt)
        {
            var toJson = value.Cell is JsObject valueObject ? GetToJson(valueObject) : JsOperations.GetV(value, ToJsonKey);
            if (toJson.IsCallable)
            {
                value = toJson.AsObject().Call(value, [key.ToJsValue()]);
            }
        }

        if (_replacerFunction is not null)
        {
            value = _replacerFunction.Call(holder, [key.ToJsValue(), value]);
        }

        if (value.Cell is JsObject obj)
        {
            switch (obj)
            {
                case JsRawJsonObject raw:
                    _builder.Append(raw.RawJson);
                    CheckLength();
                    return true;
                case { Class: ObjectClass.Number }:
                    value = JsOperations.ToNumber(value);
                    break;
                case { Class: ObjectClass.String }:
                    value = JsValue.FromJsString(JsOperations.ToJsString(value));
                    break;
                case JsPrimitiveObject { Class: ObjectClass.Boolean or ObjectClass.BigInt } primitive:
                    value = primitive.PrimitiveValue;
                    break;
            }
        }

        switch (value.Cell)
        {
            case NullCell:
                _builder.Append("null");
                return true;
            case BooleanCell boolean:
                _builder.Append(boolean.Value ? "true" : "false");
                return true;
            case JsString str:
                QuoteJsonString(_builder, str.ToString());
                CheckLength();
                return true;
            case JsBigInt:
                ThrowHelper.ThrowTypeError("Do not know how to serialize a BigInt");
                return false;
            case JsObject { IsCallable: false } container:
                if (JsOperations.IsArray(value))
                {
                    SerializeArray(container);
                }
                else
                {
                    SerializeObject(container);
                }

                return true;
        }

        if (value.IsNumber)
        {
            var number = value.NumberValue;
            if (number is >= 0 and <= int.MaxValue && number == Math.Floor(number))
            {
                // A non-negative integer (or -0, also "0"): the digits are appended without a string
                _builder.AppendInt32((int)number);
            }
            else
            {
                _builder.Append(double.IsFinite(number) ? NumberFormatting.ToString(number) : "null");
            }

            return true;
        }

        return false;
    }

    /// <summary>GetV(value, "toJSON") for an object: most objects have no toJSON property, and the same prototypes.</summary>
    private JsValue GetToJson(JsObject value)
    {
        // The own named properties of the object are ordinary (no proxy, no host object) and have no toJSON
        if (value.GetMapForCache(ToJsonKey) is not null && !value.TryFindOwnForCache(ToJsonKey, out _, out _))
        {
            var prototype = value.PrototypeSlot;
            if (prototype is null)
                return JsValue.Undefined;

            var epoch = _engine.InlineCacheEpoch;
            if (epoch == _prototypesWithoutToJsonEpoch && (ReferenceEquals(prototype, _prototypeWithoutToJson1) || ReferenceEquals(prototype, _prototypeWithoutToJson2)))
                return JsValue.Undefined;

            if (HasNoToJsonInChain(prototype))
            {
                if (epoch != _prototypesWithoutToJsonEpoch)
                {
                    _prototypesWithoutToJsonEpoch = epoch;
                    _prototypeWithoutToJson2 = null;
                }
                else
                {
                    _prototypeWithoutToJson2 = _prototypeWithoutToJson1;
                }

                _prototypeWithoutToJson1 = prototype;
                return JsValue.Undefined;
            }
        }

        return value.Get(ToJsonKey);

        static bool HasNoToJsonInChain(JsObject prototype)
        {
            for (var current = prototype; current is not null; current = current.PrototypeSlot)
            {
                current.MarkInlineCachePrototype();
                if (current.GetMapForCache(ToJsonKey) is null || current.TryFindOwnForCache(ToJsonKey, out _, out _))
                    return false;
            }

            return true;
        }
    }

    /// <summary>SerializeJSONObject(state, value)</summary>
    private void SerializeObject(JsObject value)
    {
        Enter(value);
        var stepback = _indent;
        _indent += _gap;
        _builder.Append('{');
        var empty = true;
        if (_propertyList is null && value.GetOrdinaryShapeForEnumeration() is { } shape)
        {
            // An ordinary object whose properties are all in its shape: the shape (immutable) is the list of its keys
            for (var i = 0; i < shape.Count; i++)
            {
                var key = shape.GetKey(i);
                var flags = shape.GetFlags(i);
                if (key.IsString && (flags & PropertyFlags.Enumerable) != 0)
                {
                    SerializeObjectProperty(value, key, shape, i, flags, ref empty);
                }
            }
        }
        else
        {
            foreach (var key in _propertyList ?? JsonBuiltins.EnumerableOwnPropertyKeys(value))
            {
                SerializeObjectProperty(value, key, shape: null, slot: -1, flags: default, ref empty);
            }
        }

        if (!empty && _gap.Length > 0)
        {
            _builder.Append('\n');
            _builder.Append(stepback);
        }

        _builder.Append('}');
        _stack.Remove(value);
        _indent = stepback;
    }

    /// <summary>
    /// Appends a member of an object. <paramref name="shape"/> is the shape whose keys are enumerated: while the object
    /// still has it (a toJSON method, a getter or the replacer may change the object), the value of a data property is
    /// the value of its slot.
    /// </summary>
    private void SerializeObjectProperty(JsObject value, in PropertyKey key, Shape? shape, int slot, PropertyFlags flags, ref bool empty)
    {
        var mark = _builder.Length;
        if (!empty)
        {
            _builder.Append(',');
        }

        if (_gap.Length > 0)
        {
            _builder.Append('\n');
            _builder.Append(_indent);
        }

        QuoteJsonString(_builder, key.AsString());
        _builder.Append(':');
        if (_gap.Length > 0)
        {
            _builder.Append(' ');
        }

        var serialized = shape is not null && ReferenceEquals(value.Map, shape) && (flags & PropertyFlags.Accessor) == 0
            ? SerializeProperty(key, value, value.Slots![slot])
            : SerializeProperty(key, value);
        if (serialized)
        {
            empty = false;
        }
        else
        {
            _builder.Length = mark;
        }

        CheckLength();
    }

    /// <summary>SerializeJSONArray(state, value)</summary>
    private void SerializeArray(JsObject value)
    {
        Enter(value);
        var stepback = _indent;
        _indent += _gap;
        var length = JsOperations.LengthOfArrayLike(value);
        _builder.Append('[');
        for (long index = 0; index < length; index++)
        {
            if (index > 0)
            {
                _builder.Append(',');
            }

            if (_gap.Length > 0)
            {
                _builder.Append('\n');
                _builder.Append(_indent);
            }

            var key = PropertyKey.FromInteger(index);
            var serialized = value is JsArray array && array.TryGetDenseElement((uint)index, out var element)
                ? SerializeProperty(key, value, element)
                : SerializeProperty(key, value);
            if (!serialized)
            {
                _builder.Append("null");
            }

            CheckLength();
        }

        if (length > 0 && _gap.Length > 0)
        {
            _builder.Append('\n');
            _builder.Append(stepback);
        }

        _builder.Append(']');
        _stack.Remove(value);
        _indent = stepback;
    }

    private void Enter(JsObject value)
    {
        _engine.CheckCallDepth();
        if (!_stack.Add(value))
        {
            ThrowHelper.ThrowTypeError("Converting circular structure to JSON");
        }
    }

    private void CheckLength()
    {
        var length = _builder.Length;
        if (length > JsString.MaxLength)
        {
            ThrowHelper.ThrowRangeError("Invalid string length");
        }

        // The work is the text written since the last check (the text of an undefined property is removed)
        _engine.CountWork(Math.Max(length - _checkedLength, 0) + 1);
        _checkedLength = length;
    }

    /// <summary>QuoteJSONString(value): the lone surrogates are escaped (well-formed JSON.stringify).</summary>
    private static void QuoteJsonString(JsonTextWriter builder, string value)
    {
        builder.Append('"');
        var start = 0;
        while (true)
        {
            var next = value.AsSpan(start).IndexOfAny(QuotedSpecialCharacters);
            if (next < 0)
                break;

            var i = start + next;
            var c = value[i];
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                // A surrogate pair is copied as is
                builder.Append(value.AsSpan(start, i + 2 - start));
                start = i + 2;
                continue;
            }

            // The characters copied, and an escape sequence of up to 6 characters
            JsEngine.CountCurrentWork(i - start + 6);
            builder.Append(value.AsSpan(start, i - start));
            start = i + 1;
            switch (c)
            {
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                default:
                    builder.Append("\\u");
                    builder.Append(HexLower(c >> 12));
                    builder.Append(HexLower((c >> 8) & 0xF));
                    builder.Append(HexLower((c >> 4) & 0xF));
                    builder.Append(HexLower(c & 0xF));
                    break;
            }
        }

        builder.Append(value.AsSpan(start));
        builder.Append('"');
    }

    private static string CreateQuotedSpecialCharacters()
    {
        var characters = new char[0x20 + 2 + 0x800];
        for (var i = 0; i < 0x20; i++)
        {
            characters[i] = (char)i;
        }

        characters[0x20] = '"';
        characters[0x21] = '\\';
        for (var i = 0; i < 0x800; i++)
        {
            characters[0x22 + i] = (char)(0xD800 + i);
        }

        return new string(characters);
    }

    private static char HexLower(int value) => "0123456789abcdef"[value];

    /// <summary>The text of a serialization, in a buffer rented from the shared array pool.</summary>
    private sealed class JsonTextWriter
    {
        private char[] _buffer = ArrayPool<char>.Shared.Rent(256);
        private int _length;

        public int Length
        {
            get => _length;
            set => _length = value;
        }

        public void Append(char value)
        {
            if (_length >= _buffer.Length)
            {
                Grow(1);
            }

            _buffer[_length++] = value;
        }

        public void Append(string value) => Append(value.AsSpan());

        public void Append(ReadOnlySpan<char> value)
        {
            if (value.Length > _buffer.Length - _length)
            {
                Grow(value.Length);
            }

            value.CopyTo(_buffer.AsSpan(_length));
            _length += value.Length;
        }

        public void AppendInt32(int value)
        {
            int written;
            while (!value.TryFormat(_buffer.AsSpan(_length), out written, provider: CultureInfo.InvariantCulture))
            {
                Grow(11);
            }

            _length += written;
        }

        public override string ToString() => new(_buffer, 0, _length);

        /// <summary>Returns the buffer to the pool.</summary>
        public void Release()
        {
            var buffer = _buffer;
            _buffer = [];
            _length = 0;
            if (buffer.Length > 0)
            {
                ArrayPool<char>.Shared.Return(buffer);
            }
        }

        private void Grow(int additional)
        {
            // CheckLength reports a RangeError when the text is longer than a string can be; the buffer only stops at the
            // length of an array
            var required = (long)_length + additional;
            if (required > Array.MaxLength)
            {
                ThrowHelper.ThrowRangeError("Invalid string length");
            }

            var newLength = (int)Math.Max(required, Math.Min((long)_buffer.Length * 2, Array.MaxLength));
            var grown = ArrayPool<char>.Shared.Rent(newLength);
            _buffer.AsSpan(0, _length).CopyTo(grown);
            ArrayPool<char>.Shared.Return(_buffer);
            _buffer = grown;
        }
    }
}

/// <summary>An object created by JSON.rawJSON: it has an [[IsRawJSON]] internal slot.</summary>
internal sealed class JsRawJsonObject : JsObject
{
    public JsRawJsonObject(string rawJson)
        : base(prototype: null)
    {
        RawJson = rawJson;

        // CreateDataPropertyOrThrow, then SetIntegrityLevel(frozen)
        SetOwnSlot("rawJSON", rawJson, PropertyFlags.Enumerable);
        Flags &= ~ObjectFlags.Extensible;
    }

    public string RawJson { get; }
}
