using System.Text;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>JSON.stringify: the JSON Serialization Record and SerializeJSONProperty (https://tc39.es/ecma262/#sec-json.stringify).</summary>
internal sealed class JsonSerializer
{
    private static readonly PropertyKey ToJsonKey = new("toJSON");

    private readonly JsEngine _engine;
    private readonly StringBuilder _builder = new();
    private readonly HashSet<JsObject> _stack = new(ReferenceEqualityComparer.Instance);
    private readonly JsObject? _replacerFunction;
    private readonly List<PropertyKey>? _propertyList;
    private readonly string _gap;
    private string _indent = "";

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
        if (!SerializeProperty(new PropertyKey(""), wrapper))
            return null;

        return _builder.ToString();
    }

    /// <summary>SerializeJSONProperty(state, key, holder): appends the JSON text of the property; false when the result is undefined.</summary>
    private bool SerializeProperty(in PropertyKey key, JsObject holder)
    {
        var value = holder.Get(key);
        if (value.Cell is JsObject or JsBigInt)
        {
            var toJson = JsOperations.GetV(value, ToJsonKey);
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
            _builder.Append(double.IsFinite(number) ? NumberFormatting.ToString(number) : "null");
            return true;
        }

        return false;
    }

    /// <summary>SerializeJSONObject(state, value)</summary>
    private void SerializeObject(JsObject value)
    {
        Enter(value);
        var stepback = _indent;
        _indent += _gap;
        var keys = _propertyList ?? JsonBuiltins.EnumerableOwnPropertyKeys(value);
        _builder.Append('{');
        var empty = true;
        foreach (var key in keys)
        {
            var mark = _builder.Length;
            if (!empty)
            {
                _builder.Append(',');
            }

            if (_gap.Length > 0)
            {
                _builder.Append('\n').Append(_indent);
            }

            QuoteJsonString(_builder, key.AsString());
            _builder.Append(':');
            if (_gap.Length > 0)
            {
                _builder.Append(' ');
            }

            if (SerializeProperty(key, value))
            {
                empty = false;
            }
            else
            {
                _builder.Length = mark;
            }
        }

        if (!empty && _gap.Length > 0)
        {
            _builder.Append('\n').Append(stepback);
        }

        _builder.Append('}');
        _stack.Remove(value);
        _indent = stepback;
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
                _builder.Append('\n').Append(_indent);
            }

            if (!SerializeProperty(PropertyKey.FromInteger(index), value))
            {
                _builder.Append("null");
            }

            CheckLength();
        }

        if (length > 0 && _gap.Length > 0)
        {
            _builder.Append('\n').Append(stepback);
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
        if (_builder.Length > JsString.MaxLength)
        {
            ThrowHelper.ThrowRangeError("Invalid string length");
        }
    }

    /// <summary>QuoteJSONString(value): the lone surrogates are escaped (well-formed JSON.stringify).</summary>
    public static void QuoteJsonString(StringBuilder builder, string value)
    {
        builder.Append('"');
        var start = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c >= 0x20 && c != '"' && c != '\\' && !char.IsSurrogate(c))
                continue;

            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;
                continue;
            }

            builder.Append(value, start, i - start);
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
                    builder.Append("\\u").Append(HexLower(c >> 12)).Append(HexLower((c >> 8) & 0xF)).Append(HexLower((c >> 4) & 0xF)).Append(HexLower(c & 0xF));
                    break;
            }
        }

        builder.Append(value, start, value.Length - start);
        builder.Append('"');
    }

    private static char HexLower(int value) => "0123456789abcdef"[value];
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
