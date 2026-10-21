using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// A property key: a string or a symbol (https://tc39.es/ecma262/#sec-object-type). The array indices (the canonical
/// numeric strings of the integers 0 to 2^32-2) are kept as numbers.
/// </summary>
[DebuggerDisplay("{ToString()}")]
internal readonly struct PropertyKey : IEquatable<PropertyKey>
{
    private const uint NotAnIndex = uint.MaxValue;

    // A string, a JsSymbol, or null for an array index
    private readonly object? _value;
    private readonly uint _index;

    private PropertyKey(object? value, uint index)
    {
        _value = value;
        _index = index;
    }

    public PropertyKey(string value)
    {
        if (TryParseArrayIndex(value, out var index))
        {
            _value = null;
            _index = index;
        }
        else
        {
            _value = value;
            _index = NotAnIndex;
        }
    }

    public PropertyKey(JsSymbol symbol)
    {
        _value = symbol;
        _index = NotAnIndex;
    }

    public PropertyKey(uint index)
    {
        Debug.Assert(index != NotAnIndex);
        _value = null;
        _index = index;
    }

    /// <summary>Creates the key of a property whose name is a non-negative integer (an index when it is less than 2^32-1).</summary>
    public static PropertyKey FromInteger(long value) => value is >= 0 and < NotAnIndex ? new PropertyKey((uint)value) : new PropertyKey(value.ToString(CultureInfo.InvariantCulture));

    public static PropertyKey FromDouble(double value)
    {
        if (value >= 0 && value < NotAnIndex && value == Math.Floor(value) && !(value == 0 && double.IsNegative(value)))
            return new PropertyKey((uint)value);

        return new PropertyKey(NumberFormatting.ToString(value));
    }

    public bool IsArrayIndex
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _index != NotAnIndex;
    }

    public uint ArrayIndex => _index;

    public bool IsSymbol => _value is JsSymbol;

    public bool IsString => _value is not JsSymbol;

    public JsSymbol AsSymbol() => (JsSymbol)_value!;

    /// <summary>Gets the string of a string key (an index is formatted).</summary>
    public string AsString() => _value as string ?? _index.ToString(CultureInfo.InvariantCulture);

    /// <summary>Gets the key as a JavaScript value (a string or a symbol).</summary>
    public JsValue ToJsValue() => _value is JsSymbol symbol ? new JsValue(symbol) : JsValue.FromString(AsString());

    public bool Equals(PropertyKey other)
    {
        if (_index != NotAnIndex || other._index != NotAnIndex)
            return _index == other._index;

        if (_value is string a)
            return other._value is string b && string.Equals(a, b, StringComparison.Ordinal);

        return ReferenceEquals(_value, other._value);
    }

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is PropertyKey other && Equals(other);

    public override int GetHashCode()
    {
        if (_index != NotAnIndex)
            return (int)_index;

        return _value is string value ? string.GetHashCode(value, StringComparison.Ordinal) : RuntimeHelpers.GetHashCode(_value!);
    }

    public static bool operator ==(PropertyKey left, PropertyKey right) => left.Equals(right);

    public static bool operator !=(PropertyKey left, PropertyKey right) => !left.Equals(right);

    public static implicit operator PropertyKey(string value) => new(value);

    public static implicit operator PropertyKey(JsSymbol value) => new(value);

    public override string ToString() => _value is JsSymbol symbol ? symbol.ToString() : AsString();

    /// <summary>Gets whether a string is the canonical form of an array index (https://tc39.es/ecma262/#array-index).</summary>
    public static bool TryParseArrayIndex(string value, out uint index)
    {
        index = 0;
        if (value.Length is 0 or > 10)
            return false;

        var first = value[0];
        if (first is < '0' or > '9' || (first == '0' && value.Length > 1))
            return false;

        ulong result = 0;
        foreach (var c in value)
        {
            if (c is < '0' or > '9')
                return false;

            result = (result * 10) + (uint)(c - '0');
        }

        if (result >= NotAnIndex)
            return false;

        index = (uint)result;
        return true;
    }
}
