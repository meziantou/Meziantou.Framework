using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.Scripting;

/// <summary>
/// A minimal JavaScript object model implementing <see cref="IJsAdapter{TValue}"/> without a JavaScript engine: ordinary
/// objects, arrays, host functions and the few intrinsics the WebIDL conversions use (the error constructors,
/// <c>Array.prototype[Symbol.iterator]</c>). It does not parse JavaScript: <see cref="Evaluate"/> only returns intrinsics.
/// </summary>
internal readonly struct InMemoryJsAdapter(InMemoryRealm realm) : IJsAdapter<InMemoryValue>
{
    public InMemoryRealm Realm { get; } = realm;

    public InMemoryValue Undefined => InMemoryValue.Undefined;

    public InMemoryValue Null => InMemoryValue.Null;

    public InMemoryValue FromBoolean(bool value) => value ? InMemoryValue.True : InMemoryValue.False;

    public InMemoryValue FromNumber(double value) => new InMemoryNumber(value);

    public InMemoryValue FromString(string value) => new InMemoryString(value);

    public InMemoryValue FromBigInt(BigInteger value) => new InMemoryBigInt(value);

    public bool IsUndefined(InMemoryValue value) => ReferenceEquals(value, InMemoryValue.Undefined);

    public bool IsNull(InMemoryValue value) => ReferenceEquals(value, InMemoryValue.Null);

    public bool IsBoolean(InMemoryValue value) => value is InMemoryBoolean;

    public bool IsNumber(InMemoryValue value) => value is InMemoryNumber;

    public bool IsBigInt(InMemoryValue value) => value is InMemoryBigInt;

    public bool IsString(InMemoryValue value) => value is InMemoryString;

    public bool IsSymbol(InMemoryValue value) => value is InMemorySymbol;

    public bool IsObject(InMemoryValue value) => value is InMemoryObject;

    public bool IsCallable(InMemoryValue value) => value is InMemoryObject { Function: not null };

    public bool IsConstructor(InMemoryValue value) => value is InMemoryObject { Constructor: not null };

    public bool IsArray(InMemoryValue value) => value is InMemoryObject { IsArray: true };

    public bool IsDate(InMemoryValue value) => value is InMemoryObject { DateValue: not null };

    public bool AsBoolean(InMemoryValue value) => ((InMemoryBoolean)value).Value;

    public double AsNumber(InMemoryValue value) => ((InMemoryNumber)value).Value;

    public string AsString(InMemoryValue value) => ((InMemoryString)value).Value;

    public double AsDate(InMemoryValue value) => ((InMemoryObject)value).DateValue!.Value;

    public InMemoryValue CreateDate(double timeValue) => new InMemoryObject(Realm.ObjectPrototype) { DateValue = timeValue };

    public double ToNumber(InMemoryValue value)
    {
        return value switch
        {
            InMemoryNumber number => number.Value,
            InMemoryBoolean boolean => boolean.Value ? 1 : 0,
            InMemoryString str => StringToNumber(str.Value),
            InMemoryObject obj => ToNumber(ToPrimitive(obj)),
            InMemorySymbol or InMemoryBigInt => throw Realm.Throw("TypeError", "Cannot convert the value to a number"),
            _ when IsNull(value) => 0,
            _ => double.NaN,
        };

        static double StringToNumber(string value)
        {
            value = value.Trim();
            if (value.Length == 0)
                return 0;

            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return long.TryParse(value.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var hex) ? hex : double.NaN;

            return value switch
            {
                "Infinity" or "+Infinity" => double.PositiveInfinity,
                "-Infinity" => double.NegativeInfinity,
                _ => double.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var result) ? result : double.NaN,
            };
        }
    }

    public string ToString(InMemoryValue value)
    {
        return value switch
        {
            InMemoryString str => str.Value,
            InMemoryNumber number => NumberToString(number.Value),
            InMemoryBoolean boolean => boolean.Value ? "true" : "false",
            InMemoryBigInt bigint => bigint.Value.ToString(CultureInfo.InvariantCulture),
            InMemoryObject obj => ToString(ToPrimitive(obj)),
            InMemorySymbol => throw Realm.Throw("TypeError", "Cannot convert a Symbol value to a string"),
            _ when IsNull(value) => "null",
            _ => "undefined",
        };

        static string NumberToString(double value)
        {
            if (double.IsNaN(value))
                return "NaN";

            if (double.IsInfinity(value))
                return value > 0 ? "Infinity" : "-Infinity";

            return value == 0 ? "0" : value.ToString("R", CultureInfo.InvariantCulture);
        }
    }

    public bool ToBoolean(InMemoryValue value) => value switch
    {
        InMemoryBoolean boolean => boolean.Value,
        InMemoryNumber number => number.Value is not 0 && !double.IsNaN(number.Value),
        InMemoryString str => str.Value.Length > 0,
        InMemoryBigInt bigint => !bigint.Value.IsZero,
        InMemoryObject or InMemorySymbol => true,
        _ => false,
    };

    public BigInteger ToBigInt(InMemoryValue value) => value switch
    {
        InMemoryBigInt bigint => bigint.Value,
        InMemoryBoolean boolean => boolean.Value ? BigInteger.One : BigInteger.Zero,
        InMemoryString str when BigInteger.TryParse(str.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var result) => result,
        InMemoryObject obj => ToBigInt(ToPrimitive(obj)),
        _ => throw Realm.Throw("TypeError", "Cannot convert the value to a BigInt"),
    };

    // OrdinaryToPrimitive with the hint "default": valueOf, then toString (https://tc39.es/ecma262/#sec-ordinarytoprimitive)
    private InMemoryValue ToPrimitive(InMemoryObject obj)
    {
        foreach (var name in (ReadOnlySpan<string>)["valueOf", "toString"])
        {
            var method = Get(obj, FromString(name));
            if (IsCallable(method))
            {
                var result = Call(method, obj, []);
                if (result is not InMemoryObject)
                    return result;
            }
        }

        if (obj.DateValue is { } date)
            return FromNumber(date);

        return obj.IsArray ? FromString(string.Join(',', obj.ArrayItems.Select(ToString))) : FromString("[object Object]");
    }

    public InMemoryValue Get(InMemoryValue obj, InMemoryValue key)
    {
        for (var current = obj as InMemoryObject; current is not null; current = current.Prototype)
        {
            if (GetOwnProperty(current, key, out var descriptor))
            {
                if (descriptor.IsAccessorDescriptor)
                    return descriptor.Getter is { } getter && IsCallable(getter) ? Call(getter, obj, []) : Undefined;

                return descriptor.Value ?? Undefined;
            }
        }

        return Undefined;
    }

    public void Set(InMemoryValue obj, InMemoryValue key, InMemoryValue value)
    {
        var target = (InMemoryObject)obj;
        if (target.Handler is { } handler && handler.TrySet(obj, key, value, out var handled))
        {
            if (!handled)
                throw Realm.Throw("TypeError", "Cannot set the property");

            return;
        }

        for (var current = target; current is not null; current = current.Prototype)
        {
            if (GetOwnProperty(current, key, out var descriptor))
            {
                if (descriptor.IsAccessorDescriptor)
                {
                    if (descriptor.Setter is not { } setter || !IsCallable(setter))
                        throw Realm.Throw("TypeError", "The property has no setter");

                    Call(setter, obj, [value]);
                    return;
                }

                if (!descriptor.Writable)
                    throw Realm.Throw("TypeError", "The property is read-only");

                break;
            }
        }

        if (!DefineOwnProperty(obj, key, JsPropertyDescriptor<InMemoryValue>.Data(value, writable: true, enumerable: true, configurable: true)))
            throw Realm.Throw("TypeError", "Cannot define the property");
    }

    public bool DefineOwnProperty(InMemoryValue obj, InMemoryValue key, in JsPropertyDescriptor<InMemoryValue> descriptor)
    {
        var target = (InMemoryObject)obj;
        if (target.Handler is { } handler && handler.TryDefineOwnProperty(obj, key, descriptor, out var result))
            return result;

        return OrdinaryDefineOwnProperty(obj, key, descriptor);
    }

    public bool GetOwnProperty(InMemoryValue obj, InMemoryValue key, out JsPropertyDescriptor<InMemoryValue> descriptor)
    {
        var target = (InMemoryObject)obj;
        if (target.Handler is { } handler && handler.TryGetOwnProperty(obj, key, out descriptor))
            return true;

        return OrdinaryGetOwnProperty(obj, key, out descriptor);
    }

    public bool HasProperty(InMemoryValue obj, InMemoryValue key)
    {
        for (var current = obj as InMemoryObject; current is not null; current = current.Prototype)
        {
            if (GetOwnProperty(current, key, out _))
                return true;
        }

        return false;
    }

    public bool Delete(InMemoryValue obj, InMemoryValue key)
    {
        var target = (InMemoryObject)obj;
        if (target.Handler is { } handler && handler.TryDelete(obj, key, out var result))
            return result;

        return OrdinaryDelete(obj, key);
    }

    public IReadOnlyList<InMemoryValue> OwnKeys(InMemoryValue obj)
    {
        var target = (InMemoryObject)obj;
        var keys = target.Handler?.GetOwnKeys(obj);
        return keys is null ? OrdinaryOwnKeys(obj) : [.. keys, .. OrdinaryOwnKeys(obj)];
    }

    public InMemoryValue GetPrototypeOf(InMemoryValue obj) => (InMemoryValue?)((InMemoryObject)obj).Prototype ?? Null;

    public bool SetPrototypeOf(InMemoryValue obj, InMemoryValue prototype)
    {
        ((InMemoryObject)obj).Prototype = prototype as InMemoryObject;
        return true;
    }

    public InMemoryValue CreateObject(InMemoryValue prototype) => new InMemoryObject(prototype as InMemoryObject);

    public InMemoryValue CreateArray(ReadOnlySpan<InMemoryValue> items)
    {
        var array = new InMemoryObject(Realm.ArrayPrototype) { IsArray = true };
        array.ArrayItems.AddRange(items);
        return array;
    }

    public InMemoryValue CreateFunction(string name, int length, JsHostFunction<InMemoryValue> function)
    {
        var result = new InMemoryObject(Realm.FunctionPrototype) { Function = function };
        result.Properties[FromString("name")] = JsPropertyDescriptor<InMemoryValue>.Data(FromString(name), writable: false, enumerable: false, configurable: true);
        result.Properties[FromString("length")] = JsPropertyDescriptor<InMemoryValue>.Data(FromNumber(length), writable: false, enumerable: false, configurable: true);
        return result;
    }

    public InMemoryValue Call(InMemoryValue function, InMemoryValue thisValue, ReadOnlySpan<InMemoryValue> arguments)
    {
        if (function is not InMemoryObject { Function: { } callable })
            throw Realm.Throw("TypeError", "The value is not a function");

        return callable(thisValue, arguments);
    }

    public InMemoryValue Construct(InMemoryValue constructor, ReadOnlySpan<InMemoryValue> arguments, InMemoryValue newTarget)
    {
        if (constructor is not InMemoryObject { Constructor: { } construct })
            throw Realm.Throw("TypeError", "The value is not a constructor");

        return construct(arguments, newTarget);
    }

    public InMemoryValue CreatePlatformObject(InMemoryValue prototype, object target, IJsHostObjectHandler<InMemoryValue>? handler)
        => new InMemoryObject(prototype as InMemoryObject) { PlatformObject = target, Handler = handler };

    public bool TryGetPlatformObject(InMemoryValue value, [NotNullWhen(true)] out object? target)
    {
        target = (value as InMemoryObject)?.PlatformObject;
        return target is not null;
    }

    public bool OrdinaryGetOwnProperty(InMemoryValue obj, InMemoryValue key, out JsPropertyDescriptor<InMemoryValue> descriptor)
    {
        var target = (InMemoryObject)obj;
        if (target.IsArray && key is InMemoryString { Value: var name })
        {
            if (name == "length")
            {
                descriptor = JsPropertyDescriptor<InMemoryValue>.Data(FromNumber(target.ArrayItems.Count), writable: true, enumerable: false, configurable: false);
                return true;
            }

            if (int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < target.ArrayItems.Count)
            {
                descriptor = JsPropertyDescriptor<InMemoryValue>.Data(target.ArrayItems[index], writable: true, enumerable: true, configurable: true);
                return true;
            }
        }

        return target.Properties.TryGetValue(key, out descriptor);
    }

    public bool OrdinaryDefineOwnProperty(InMemoryValue obj, InMemoryValue key, in JsPropertyDescriptor<InMemoryValue> descriptor)
    {
        var target = (InMemoryObject)obj;
        if (target.Properties.TryGetValue(key, out var existing) && !existing.Configurable)
            return false;

        target.Properties[key] = descriptor;
        return true;
    }

    public bool OrdinaryDelete(InMemoryValue obj, InMemoryValue key)
    {
        var target = (InMemoryObject)obj;
        if (target.Properties.TryGetValue(key, out var existing) && !existing.Configurable)
            return false;

        target.Properties.Remove(key);
        return true;
    }

    public IReadOnlyList<InMemoryValue> OrdinaryOwnKeys(InMemoryValue obj)
    {
        var target = (InMemoryObject)obj;
        var indices = Enumerable.Range(0, target.IsArray ? target.ArrayItems.Count : 0).Select(i => (InMemoryValue)new InMemoryString(i.ToString(CultureInfo.InvariantCulture)));
        return [.. indices, .. target.Properties.Keys.Where(k => k is InMemoryString), .. target.Properties.Keys.Where(k => k is InMemorySymbol)];
    }

    public Exception CreateException(InMemoryValue value) => new InMemoryJsException(value);

    public bool TryGetThrownValue(Exception exception, [NotNullWhen(true)] out InMemoryValue? value)
    {
        value = (exception as InMemoryJsException)?.Value;
        return value is not null;
    }

    public JsErrorInformation GetErrorInformation(InMemoryValue error) => new(ToString(Get(error, FromString("message"))), "", 0, 0);

    public object CurrentRealm => Realm;

    public InMemoryValue GetGlobalObject(object realm) => ((InMemoryRealm)realm).Global;

    public TResult RunInRealm<TResult>(object realm, Func<TResult> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return action();
    }

    public InMemoryValue Evaluate(object realm, string code) => code switch
    {
        "Object.prototype" => ((InMemoryRealm)realm).ObjectPrototype,
        "Function.prototype" => ((InMemoryRealm)realm).FunctionPrototype,
        "Array.prototype" => ((InMemoryRealm)realm).ArrayPrototype,
        _ => throw new NotSupportedException("The in-memory adapter does not evaluate JavaScript code: " + code),
    };

    public InMemoryValue GetWellKnownSymbol(JsWellKnownSymbol symbol) => Realm.WellKnownSymbols[symbol];
}

/// <summary>The intrinsics of a realm of <see cref="InMemoryJsAdapter"/>.</summary>
internal sealed class InMemoryRealm
{
    public InMemoryRealm()
    {
        foreach (var symbol in Enum.GetValues<JsWellKnownSymbol>())
        {
            WellKnownSymbols[symbol] = new InMemorySymbol("Symbol." + symbol);
        }

        var adapter = new InMemoryJsAdapter(this);
        ObjectPrototype = new InMemoryObject(prototype: null);
        FunctionPrototype = new InMemoryObject(ObjectPrototype);
        ArrayPrototype = new InMemoryObject(ObjectPrototype);
        Global = new InMemoryObject(ObjectPrototype);

        // Array.prototype[Symbol.iterator] (https://tc39.es/ecma262/#sec-array.prototype-%symbol.iterator%)
        var values = adapter.CreateFunction("values", 0, (thisValue, _) =>
        {
            var array = (InMemoryObject)thisValue;
            var index = 0;
            var iterator = new InMemoryObject(ObjectPrototype);
            adapter.Set(iterator, adapter.FromString("next"), adapter.CreateFunction("next", 0, (_, _) =>
            {
                var result = new InMemoryObject(ObjectPrototype);
                var done = index >= array.ArrayItems.Count;
                adapter.Set(result, adapter.FromString("done"), adapter.FromBoolean(done));
                adapter.Set(result, adapter.FromString("value"), done ? InMemoryValue.Undefined : array.ArrayItems[index++]);
                return result;
            }));
            return iterator;
        });
        adapter.DefineOwnProperty(ArrayPrototype, WellKnownSymbols[JsWellKnownSymbol.Iterator], JsPropertyDescriptor<InMemoryValue>.Data(values, writable: true, enumerable: false, configurable: true));

        foreach (var name in (ReadOnlySpan<string>)["Error", "TypeError", "RangeError", "SyntaxError"])
        {
            var prototype = new InMemoryObject(ObjectPrototype);
            adapter.Set(prototype, adapter.FromString("name"), adapter.FromString(name));
            var constructor = adapter.CreateFunction(name, 1, (_, _) => throw new NotSupportedException());
            ((InMemoryObject)constructor).Constructor = (arguments, _) =>
            {
                var error = new InMemoryObject(prototype);
                if (arguments.Length > 0)
                {
                    adapter.Set(error, adapter.FromString("message"), adapter.FromString(adapter.ToString(arguments[0])));
                }

                return error;
            };
            adapter.Set(Global, adapter.FromString(name), constructor);
        }
    }

    public Dictionary<JsWellKnownSymbol, InMemoryValue> WellKnownSymbols { get; } = [];

    public InMemoryObject ObjectPrototype { get; }

    public InMemoryObject FunctionPrototype { get; }

    public InMemoryObject ArrayPrototype { get; }

    public InMemoryObject Global { get; }

    /// <summary>Returns the exception that throws a new error of this realm.</summary>
    public Exception Throw(string constructorName, string message)
    {
        var adapter = new InMemoryJsAdapter(this);
        var constructor = adapter.Get(Global, adapter.FromString(constructorName));
        return new InMemoryJsException(adapter.Construct(constructor, [adapter.FromString(message)], constructor));
    }
}

/// <summary>A value of <see cref="InMemoryJsAdapter"/>.</summary>
internal abstract class InMemoryValue
{
    public static readonly InMemoryValue Undefined = new InMemoryOddball("undefined");
    public static readonly InMemoryValue Null = new InMemoryOddball("null");
    public static readonly InMemoryValue True = new InMemoryBoolean(value: true);
    public static readonly InMemoryValue False = new InMemoryBoolean(value: false);
}

internal sealed class InMemoryOddball(string name) : InMemoryValue
{
    public override string ToString() => name;
}

internal sealed class InMemoryBoolean(bool value) : InMemoryValue
{
    public bool Value { get; } = value;
}

internal sealed class InMemoryNumber(double value) : InMemoryValue
{
    public double Value { get; } = value;
}

internal sealed class InMemoryBigInt(BigInteger value) : InMemoryValue
{
    public BigInteger Value { get; } = value;
}

internal sealed class InMemorySymbol(string description) : InMemoryValue
{
    public override string ToString() => description;
}

/// <summary>A string. Strings are property keys: they are compared by value.</summary>
internal sealed class InMemoryString(string value) : InMemoryValue
{
    public string Value { get; } = value;

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is InMemoryString other && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
}

internal delegate InMemoryValue InMemoryConstructor(ReadOnlySpan<InMemoryValue> arguments, InMemoryValue newTarget);

internal sealed class InMemoryObject(InMemoryObject? prototype) : InMemoryValue
{
    public InMemoryObject? Prototype { get; set; } = prototype;

    /// <summary>Gets the properties, in insertion order.</summary>
    public OrderedDictionary<InMemoryValue, JsPropertyDescriptor<InMemoryValue>> Properties { get; } = [];

    public bool IsArray { get; init; }

    public List<InMemoryValue> ArrayItems { get; } = [];

    public double? DateValue { get; init; }

    public JsHostFunction<InMemoryValue>? Function { get; init; }

    public InMemoryConstructor? Constructor { get; set; }

    public object? PlatformObject { get; init; }

    public IJsHostObjectHandler<InMemoryValue>? Handler { get; init; }
}

/// <summary>The exception that throws a value of <see cref="InMemoryJsAdapter"/>.</summary>
#pragma warning disable CA1032 // The exception always carries a value
internal sealed class InMemoryJsException(InMemoryValue value) : Exception("A JavaScript value was thrown")
#pragma warning restore CA1032
{
    public InMemoryValue Value { get; } = value;
}
