using System.Buffers;
using System.Globalization;
using System.Numerics;
using Meziantou.Framework.JavaScriptEngine;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.JavaScript;

/// <summary>
/// The conversions between the values of the engine (<see cref="JsValue"/>, a struct) and the values of the WebIDL bindings
/// (<see cref="object"/>): <c>undefined</c> is <see cref="UndefinedValue.Instance"/>, <c>null</c> is
/// <see cref="NullValue.Instance"/>, a boolean is a boxed <see cref="bool"/>, a number a boxed <see cref="double"/>, a string
/// a <see cref="string"/>, a BigInt a boxed <see cref="BigInteger"/>, a symbol a <see cref="JsSymbol"/> and an object its
/// <see cref="JsObject"/> (the same instance for the same object: the bindings rely on the identity of the objects).
/// </summary>
internal static class JavaScriptValues
{
    private static readonly object TrueValue = true;
    private static readonly object FalseValue = false;

    // The small integers are boxed once (indexes, lengths, enumeration values...)
    private static readonly object[] SmallIntegers = CreateSmallIntegers();

    private static object[] CreateSmallIntegers()
    {
        var result = new object[256];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = (double)i;
        }

        return result;
    }

    public static object FromBoolean(bool value) => value ? TrueValue : FalseValue;

    public static object FromNumber(double value)
    {
        // -0 is not a small integer (its sign is kept)
        if (value is >= 0 and < 256 && value == Math.Floor(value) && !double.IsNegative(value))
            return SmallIntegers[(int)value];

        return value;
    }

    /// <summary>Converts a value of the engine to a value of the bindings.</summary>
    public static object ToObject(JsValue value) => value.Kind switch
    {
        JsValueKind.Object => value.AsObject(),
        JsValueKind.String => value.AsString(),
        JsValueKind.Number => FromNumber(value.AsNumber()),
        JsValueKind.Undefined => UndefinedValue.Instance,
        JsValueKind.Null => NullValue.Instance,
        JsValueKind.Boolean => value.AsBoolean() ? TrueValue : FalseValue,
        JsValueKind.Symbol => value.AsSymbol(),
        JsValueKind.BigInt => value.AsBigInt(),
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    /// <summary>Converts a value of the bindings to a value of the engine.</summary>
    public static JsValue ToJs(object value) => value switch
    {
        JsObject obj => obj,
        string text => text,
        double number => number,
        UndefinedValue => JsValue.Undefined,
        NullValue => JsValue.Null,
        bool boolean => boolean,
        JsSymbol symbol => symbol,
        BigInteger bigInt => JsValue.FromBigInt(bigInt),
        int number => number,
        _ => throw new ArgumentException("The value is not a JavaScript value: " + value.GetType().FullName, nameof(value)),
    };

    /// <summary>Converts a property key of the bindings (a string or a symbol).</summary>
    public static JsPropertyKey ToKey(object key) => key switch
    {
        string name => new JsPropertyKey(name),
        JsSymbol symbol => new JsPropertyKey(symbol),

        // The bindings may use the indexes of the arrays as keys
        double number when number is >= 0 and <= uint.MaxValue && number == Math.Floor(number) && !double.IsNegative(number) => new JsPropertyKey(((uint)number).ToString(CultureInfo.InvariantCulture)),
        int number => new JsPropertyKey(number.ToString(CultureInfo.InvariantCulture)),
        _ => throw new ArgumentException("A property key is a string or a symbol", nameof(key)),
    };

    public static object FromKey(JsPropertyKey key) => key.IsSymbol ? key.AsSymbol() : key.AsString();

    public static object[] FromKeys(IReadOnlyList<JsPropertyKey> keys)
    {
        if (keys.Count == 0)
            return [];

        var result = new object[keys.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = FromKey(keys[i]);
        }

        return result;
    }

    public static JsPropertyDescriptor ToDescriptor(in JsPropertyDescriptor<object> descriptor)
    {
        var flags = (JsPropertyDescriptorFlags)descriptor.Flags;
        return new JsPropertyDescriptor(
            descriptor.Value is { } value ? ToJs(value) : default,
            descriptor.Getter is { } getter ? ToJs(getter) : default,
            descriptor.Setter is { } setter ? ToJs(setter) : default,
            flags);
    }

    public static JsPropertyDescriptor<object> FromDescriptor(in JsPropertyDescriptor descriptor)
    {
        var flags = descriptor.Flags;
        return JsPropertyDescriptor<object>.Create(
            (flags & JsPropertyDescriptorFlags.HasValue) != JsPropertyDescriptorFlags.None ? ToObject(descriptor.Value) : null,
            (flags & JsPropertyDescriptorFlags.HasGet) != JsPropertyDescriptorFlags.None ? ToObject(descriptor.Getter) : null,
            (flags & JsPropertyDescriptorFlags.HasSet) != JsPropertyDescriptorFlags.None ? ToObject(descriptor.Setter) : null,
            (JsPropertyFlags)flags);
    }

    /// <summary>The arguments of a call converted to the values of the engine, in a pooled array.</summary>
    public ref struct EngineArguments
    {
        private JsValue[]? _array;

        public EngineArguments(ReadOnlySpan<object> arguments)
        {
            if (arguments.Length == 0)
            {
                Span = [];
                return;
            }

            _array = ArrayPool<JsValue>.Shared.Rent(arguments.Length);
            for (var i = 0; i < arguments.Length; i++)
            {
                _array[i] = ToJs(arguments[i]);
            }

            Span = _array.AsSpan(0, arguments.Length);
        }

        public ReadOnlySpan<JsValue> Span { get; private set; }

        public void Dispose()
        {
            if (_array is { } array)
            {
                _array = null;
                array.AsSpan(0, Span.Length).Clear();
                ArrayPool<JsValue>.Shared.Return(array);
                Span = [];
            }
        }
    }

    /// <summary>The arguments of a call of a host function converted to the values of the bindings, in a pooled array.</summary>
    public ref struct BindingArguments
    {
        private object[]? _array;

        public BindingArguments(ReadOnlySpan<JsValue> arguments)
        {
            if (arguments.Length == 0)
            {
                Span = [];
                return;
            }

            _array = ArrayPool<object>.Shared.Rent(arguments.Length);
            for (var i = 0; i < arguments.Length; i++)
            {
                _array[i] = ToObject(arguments[i]);
            }

            Span = _array.AsSpan(0, arguments.Length);
        }

        public ReadOnlySpan<object> Span { get; private set; }

        public void Dispose()
        {
            if (_array is { } array)
            {
                _array = null;
                array.AsSpan(0, Span.Length).Clear();
                ArrayPool<object>.Shared.Return(array);
                Span = [];
            }
        }
    }

    /// <summary>Converts the items of an array of the bindings.</summary>
    public static JsValue[] ToJs(ReadOnlySpan<object> values)
    {
        if (values.Length == 0)
            return [];

        var result = new JsValue[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            result[i] = ToJs(values[i]);
        }

        return result;
    }
}
