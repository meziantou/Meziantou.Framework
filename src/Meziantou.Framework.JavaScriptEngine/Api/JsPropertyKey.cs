using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>A property key: a string or a symbol (https://tc39.es/ecma262/#sec-object-type).</summary>
public readonly struct JsPropertyKey : IEquatable<JsPropertyKey>
{
    internal JsPropertyKey(PropertyKey key) => Key = key;

    /// <summary>Creates a string key.</summary>
    public JsPropertyKey(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Key = new PropertyKey(name);
    }

    /// <summary>Creates a symbol key.</summary>
    public JsPropertyKey(JsSymbol symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        Key = new PropertyKey(symbol);
    }

    internal PropertyKey Key { get; }

    /// <summary>Gets whether the key is a symbol.</summary>
    public bool IsSymbol => Key.IsSymbol;

    /// <summary>Gets whether the key is a string.</summary>
    public bool IsString => !Key.IsSymbol;

    /// <summary>Gets whether the key is an array index (https://tc39.es/ecma262/#array-index): the canonical string of an integer below 2^32 - 1.</summary>
    /// <param name="index">The index, when the key is an array index.</param>
    public bool TryGetArrayIndex(out uint index)
    {
        if (Key.IsArrayIndex)
        {
            index = Key.ArrayIndex;
            return true;
        }

        index = 0;
        return false;
    }

    /// <summary>Gets the string of a string key.</summary>
    public string AsString() => Key.AsString();

    /// <summary>Gets the symbol of a symbol key.</summary>
    public JsSymbol AsSymbol() => Key.AsSymbol();

    /// <summary>Gets the key as a JavaScript value (a string or a symbol).</summary>
    public JsValue ToJsValue() => Key.ToJsValue();

    /// <summary>Creates a key from a string or a symbol value, without running code.</summary>
    /// <exception cref="ArgumentException">The value is not a string nor a symbol.</exception>
    public static JsPropertyKey FromValue(JsValue value)
    {
        if (value.IsSymbol)
            return new JsPropertyKey(value.AsSymbol());

        if (value.IsString)
            return new JsPropertyKey(value.AsString());

        throw new ArgumentException("A property key is a string or a symbol", nameof(value));
    }

    /// <inheritdoc/>
    public bool Equals(JsPropertyKey other) => Key.Equals(other.Key);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is JsPropertyKey other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Key.GetHashCode();

    /// <inheritdoc/>
    public override string ToString() => Key.ToString();

    /// <summary>Compares two keys.</summary>
    public static bool operator ==(JsPropertyKey left, JsPropertyKey right) => left.Equals(right);

    /// <summary>Compares two keys.</summary>
    public static bool operator !=(JsPropertyKey left, JsPropertyKey right) => !left.Equals(right);

    /// <summary>Creates a string key.</summary>
    public static implicit operator JsPropertyKey(string name) => new(name);

    /// <summary>Creates a symbol key.</summary>
    public static implicit operator JsPropertyKey(JsSymbol symbol) => new(symbol);
}
