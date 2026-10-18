using System.Text;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>The extended attributes of the integer conversions (https://webidl.spec.whatwg.org/#abstract-opdef-converttoint).</summary>
[Flags]
internal enum IntegerConversion
{
    None = 0,
    EnforceRange = 1,
    Clamp = 2,
}

// The conversions of JavaScript values to IDL values (https://webidl.spec.whatwg.org/#es-type-mapping), used by the generated bindings
public sealed partial class WebIdlRealm<TAdapter, TValue>
{
    // The @@iterator method got by HasIterator, for the next ToSequence of the same value
    private TValue? _iteratorMethodTarget;
    private TValue? _iteratorMethod;

    // The keys of the members of the dictionaries (see GetDictionaryMember)
    private Dictionary<string, TValue>? _dictionaryMemberKeys;

    internal bool IsNullOrUndefined([NotNullWhen(false)] TValue? value) => value is null || Adapter.IsNull(value) || Adapter.IsUndefined(value);

    internal TValue Argument(ReadOnlySpan<TValue> arguments, int index) => index < arguments.Length ? arguments[index] : Adapter.Undefined;

    /// <summary>Whether an optional argument is missing (not passed, or undefined).</summary>
    internal bool IsMissing(ReadOnlySpan<TValue> arguments, int index) => index >= arguments.Length || Adapter.IsUndefined(arguments[index]);

    internal Exception NotEnoughArguments(string operationName, int required, int count)
        => CreateTypeError($"Failed to execute '{operationName}': {required} argument(s) required, but only {count} present.");

    internal bool ToBoolean(TValue value) => Adapter.ToBoolean(value);

    /// <summary>Throws a TypeError for an IDL type that the bindings do not support.</summary>
    internal object ThrowUnsupportedType(string type) => throw CreateTypeError($"The IDL type '{type}' is not supported by the bindings");

    /// <summary>
    /// Converts a value to a buffer source type (https://webidl.spec.whatwg.org/#js-buffer-source-types), or a union of
    /// them: <paramref name="allowedKinds"/> is the set of the <see cref="JsBufferKind"/> of the types (1 &lt;&lt; kind).
    /// Without [AllowShared], a view of a SharedArrayBuffer is a TypeError.
    /// </summary>
    internal JsBufferSource ToBufferSource(TValue value, uint allowedKinds, bool allowShared, string typeName)
        => TryToBufferSource(value, allowedKinds, allowShared) ?? throw CreateTypeError($"The value is not of type '{typeName}'");

    /// <summary>Converts a value to a buffer source type, or returns null when the value is not an object of one of the types (the union conversion continues).</summary>
    internal JsBufferSource? TryToBufferSource(TValue value, uint allowedKinds, bool allowShared)
    {
        if (!Adapter.IsObject(value) || !Adapter.TryGetBufferSource(value, out var kind, out var isShared, out var bytes))
            return null;

        if ((allowedKinds & (1u << (int)kind)) != 0)
        {
            if (isShared && kind != JsBufferKind.SharedArrayBuffer && !allowShared)
                throw CreateTypeError($"The {JsBufferSource.GetName(kind)} cannot view a SharedArrayBuffer");

            return JsBufferSource.FromScript(kind, bytes, value);
        }

        // A SharedArrayBuffer is an ArrayBuffer for an [AllowShared] ArrayBuffer type
        if (kind == JsBufferKind.SharedArrayBuffer && (allowedKinds & (1u << (int)JsBufferKind.ArrayBuffer)) != 0)
        {
            if (!allowShared)
                throw CreateTypeError("The value cannot be a SharedArrayBuffer");

            return JsBufferSource.FromScript(kind, bytes, value);
        }

        return null;
    }

    /// <summary>Creates an ArrayBuffer containing a copy of <paramref name="bytes"/>, or a typed array of <paramref name="kind"/> viewing it, in this realm.</summary>
    internal TValue FromBuffer(JsBufferKind kind, ReadOnlyMemory<byte> bytes)
    {
        return Adapter.RunInRealm(EngineRealm, () =>
        {
            var buffer = Adapter.CreateArrayBuffer(bytes.Span);
            if (kind is JsBufferKind.ArrayBuffer or JsBufferKind.SharedArrayBuffer)
                return buffer;

            var elementSize = JsBufferSource.GetElementSize(kind);
            return Adapter.CreateArrayBufferView(kind, buffer, 0, bytes.Length / elementSize);
        });
    }

    internal sbyte ToByte(TValue value, IntegerConversion conversion = IntegerConversion.None) => (sbyte)ConvertToInt(value, 8, signed: true, conversion);

    internal byte ToOctet(TValue value, IntegerConversion conversion = IntegerConversion.None) => (byte)ConvertToInt(value, 8, signed: false, conversion);

    internal short ToShort(TValue value, IntegerConversion conversion = IntegerConversion.None) => (short)ConvertToInt(value, 16, signed: true, conversion);

    internal ushort ToUnsignedShort(TValue value, IntegerConversion conversion = IntegerConversion.None) => (ushort)ConvertToInt(value, 16, signed: false, conversion);

    internal int ToLong(TValue value, IntegerConversion conversion = IntegerConversion.None) => (int)ConvertToInt(value, 32, signed: true, conversion);

    internal uint ToUnsignedLong(TValue value, IntegerConversion conversion = IntegerConversion.None) => (uint)ConvertToInt(value, 32, signed: false, conversion);

    internal long ToLongLong(TValue value, IntegerConversion conversion = IntegerConversion.None) => (long)ConvertToInt(value, 64, signed: true, conversion);

    internal ulong ToUnsignedLongLong(TValue value, IntegerConversion conversion = IntegerConversion.None) => (ulong)ConvertToInt(value, 64, signed: false, conversion);

    /// <summary>https://webidl.spec.whatwg.org/#js-bigint: ToBigInt (a number is a TypeError).</summary>
    internal System.Numerics.BigInteger ToBigInt(TValue value) => Adapter.ToBigInt(value);

    /// <summary>https://webidl.spec.whatwg.org/#abstract-opdef-converttoint</summary>
    internal double ConvertToInt(TValue value, int bitLength, bool signed, IntegerConversion conversion)
    {
        double lowerBound;
        double upperBound;
        if (bitLength == 64)
        {
            upperBound = Math.Pow(2, 53) - 1;
            lowerBound = signed ? -Math.Pow(2, 53) + 1 : 0;
        }
        else if (!signed)
        {
            lowerBound = 0;
            upperBound = Math.Pow(2, bitLength) - 1;
        }
        else
        {
            lowerBound = -Math.Pow(2, bitLength - 1);
            upperBound = Math.Pow(2, bitLength - 1) - 1;
        }

        var x = Adapter.ToNumber(value);
        if (x == 0)
        {
            x = 0;
        }

        if ((conversion & IntegerConversion.EnforceRange) != IntegerConversion.None)
        {
            if (!double.IsFinite(x))
                throw CreateTypeError("The value is not finite");

            x = Math.Truncate(x);
            if (x < lowerBound || x > upperBound)
                throw CreateTypeError("The value is outside of the range of the type");

            return x;
        }

        if (!double.IsNaN(x) && (conversion & IntegerConversion.Clamp) != IntegerConversion.None)
        {
            x = Math.Min(Math.Max(x, lowerBound), upperBound);
            x = Math.Round(x, MidpointRounding.ToEven);
            return x == 0 ? 0 : x;
        }

        if (!double.IsFinite(x) || x == 0)
            return 0;

        x = Math.Truncate(x);
        var modulo = Math.Pow(2, bitLength);
        x %= modulo;
        if (x < 0)
        {
            x += modulo;
        }

        if (signed && x >= Math.Pow(2, bitLength - 1))
            return x - modulo;

        return x;
    }

    /// <summary>https://webidl.spec.whatwg.org/#js-float and https://webidl.spec.whatwg.org/#js-double</summary>
    internal double ToDouble(TValue value, bool restricted)
    {
        var number = Adapter.ToNumber(value);
        if (restricted && !double.IsFinite(number))
            throw CreateTypeError("The provided value is non-finite");

        return number;
    }

    internal float ToFloat(TValue value, bool restricted)
    {
        var number = Adapter.ToNumber(value);
        if (restricted && !double.IsFinite(number))
            throw CreateTypeError("The provided value is non-finite");

        // A value that rounds to ±2^128 is out of range for a restricted float (https://webidl.spec.whatwg.org/#js-float)
        var result = (float)number;
        if (restricted && !float.IsFinite(result))
            throw CreateTypeError("The provided value is outside the range of a float");

        return result;
    }

    /// <summary>https://webidl.spec.whatwg.org/#js-DOMString</summary>
    public string ToDomString(TValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (Adapter.IsSymbol(value))
            throw CreateTypeError("Cannot convert a Symbol value to a string");

        return Adapter.ToString(value);
    }

    /// <summary>https://webidl.spec.whatwg.org/#js-DOMString with [LegacyNullToEmptyString].</summary>
    internal string ToDomString(TValue value, bool legacyNullToEmptyString)
    {
        if (legacyNullToEmptyString && Adapter.IsNull(value))
            return "";

        return ToDomString(value);
    }

    /// <summary>https://webidl.spec.whatwg.org/#js-USVString</summary>
    internal string ToUsvString(TValue value)
    {
        var s = ToDomString(value);
        StringBuilder? builder = null;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                builder?.Append(c).Append(s[i + 1]);
                i++;
            }
            else if (char.IsSurrogate(c))
            {
                builder ??= new StringBuilder(s, 0, i, s.Length);
                builder.Append((char)0xFFFD);
            }
            else
            {
                builder?.Append(c);
            }
        }

        return builder?.ToString() ?? s;
    }

    /// <summary>https://webidl.spec.whatwg.org/#js-ByteString</summary>
    internal string ToByteString(TValue value)
    {
        var s = ToDomString(value);
        foreach (var c in s)
        {
            if (c > 0xFF)
                throw CreateTypeError("The value is not a valid ByteString");
        }

        return s;
    }

    /// <summary>https://webidl.spec.whatwg.org/#js-enumeration</summary>
    internal string ToEnumerationValue(TValue value, string enumerationName, string[] values)
    {
        var s = ToDomString(value);
        if (Array.IndexOf(values, s) < 0)
            throw CreateTypeError($"The provided value '{s}' is not a valid enum value of type {enumerationName}");

        return s;
    }

    /// <summary>
    /// Converts the value assigned to an attribute of an enumeration type: null when it is not one of the values of the
    /// enumeration, in which case the assignment is ignored (https://webidl.spec.whatwg.org/#dfn-attribute-setter).
    /// </summary>
    internal string? ToEnumerationValueOrNull(TValue value, string[] values)
    {
        var s = ToDomString(value);
        return Array.IndexOf(values, s) < 0 ? null : s;
    }

    /// <summary>https://webidl.spec.whatwg.org/#js-object</summary>
    internal TValue ToObject(TValue value)
    {
        if (!Adapter.IsObject(value))
            throw CreateTypeError("The value is not an object");

        return value;
    }

    /// <summary>
    /// https://webidl.spec.whatwg.org/#js-any: the platform objects (including the global objects, whose .NET object is their
    /// window) are given to .NET as .NET objects, the strings as .NET strings, and undefined as null.
    /// </summary>
    internal object? ToAny(TValue value)
    {
        if (Adapter.TryGetPlatformObject(value, out var target))
            return target;

        if (Adapter.IsString(value))
            return Adapter.AsString(value);

        if (Adapter.IsObject(value) && Agent.GetRealmByGlobal(value) is { } realm)
            return realm.GlobalScope;

        return Adapter.IsUndefined(value) ? null : value;
    }

    /// <summary>https://webidl.spec.whatwg.org/#js-interface</summary>
    internal T ToInterface<T>(TValue value, string interfaceName)
        where T : class
    {
        if (Unwrap(value) is T target)
            return target;

        throw CreateTypeError($"The value is not of type '{interfaceName}'");
    }

    /// <summary>https://webidl.spec.whatwg.org/#js-interface for a nullable type.</summary>
    internal T? ToNullableInterface<T>(TValue value, string interfaceName)
        where T : class => IsNullOrUndefined(value) ? null : ToInterface<T>(value, interfaceName);

    /// <summary>https://webidl.spec.whatwg.org/#js-callback-interface</summary>
    internal JsCallbackInterface<TAdapter, TValue> ToCallbackInterface(TValue value, string interfaceName)
    {
        if (!Adapter.IsObject(value))
            throw CreateTypeError($"The value is not of type '{interfaceName}'");

        return Agent.CallbackInterfaces.GetValue(value, v => new JsCallbackInterface<TAdapter, TValue>(this, v));
    }

    /// <summary>
    /// https://webidl.spec.whatwg.org/#js-callback-function: returns the function, or null when it is not an object and
    /// <paramref name="treatNonObjectAsNull"/> is true ([LegacyTreatNonObjectAsNull]).
    /// </summary>
    internal TValue? ToCallbackFunction(TValue value, string callbackName, bool treatNonObjectAsNull)
    {
        if (treatNonObjectAsNull)
            return Adapter.IsObject(value) ? value : null;

        if (!Adapter.IsCallable(value))
            throw CreateTypeError($"The value is not of type '{callbackName}'");

        return value;
    }

    /// <summary>A date (https://html.spec.whatwg.org/multipage/input.html#dom-input-valueasdate): a Date object whose time value is NaN is null.</summary>
    internal DateTime? ToDate(TValue? value)
    {
        if (IsNullOrUndefined(value))
            return null;

        if (!Adapter.IsDate(value))
            throw CreateTypeError("The value is not a Date object");

        var time = Adapter.AsDate(value);
        return double.IsNaN(time) ? null : DateTime.UnixEpoch.AddMilliseconds(time);
    }

    /// <summary>https://webidl.spec.whatwg.org/#js-sequence</summary>
    internal List<T> ToSequence<T>(TValue value, Func<WebIdlRealm<TAdapter, TValue>, TValue, T> convert)
    {
        if (!Adapter.IsObject(value))
            throw CreateTypeError("The value is not iterable");

        // The overload resolution and the union conversions already got the method (@@iterator is got only once)
        var method = ReferenceEquals(value, _iteratorMethodTarget) ? _iteratorMethod! : Adapter.Get(value, Adapter.GetWellKnownSymbol(JsWellKnownSymbol.Iterator));
        _iteratorMethodTarget = null;
        _iteratorMethod = null;
        if (!Adapter.IsCallable(method))
            throw CreateTypeError("The value is not iterable");

        var iterator = Adapter.Call(method, value, []);
        var next = Adapter.Get(iterator, Key("next"));
        var result = new List<T>();
        while (true)
        {
            var step = Adapter.Call(next, iterator, []);
            if (!Adapter.IsObject(step))
                throw CreateTypeError("The iterator result is not an object");

            if (Adapter.ToBoolean(Adapter.Get(step, Key("done"))))
                break;

            result.Add(convert(this, Adapter.Get(step, Key("value"))));
        }

        return result;
    }

    /// <summary>https://webidl.spec.whatwg.org/#js-record</summary>
    internal List<KeyValuePair<string, T>> ToRecord<T>(TValue value, Func<WebIdlRealm<TAdapter, TValue>, TValue, string> convertKey, Func<WebIdlRealm<TAdapter, TValue>, TValue, T> convertValue)
    {
        if (!Adapter.IsObject(value))
            throw CreateTypeError("The value is not an object");

        var result = new List<KeyValuePair<string, T>>();
        foreach (var key in Adapter.OwnKeys(value))
        {
            if (!Adapter.GetOwnProperty(value, key, out var descriptor) || !descriptor.Enumerable)
                continue;

            var typedKey = convertKey(this, key);
            var typedValue = convertValue(this, Adapter.Get(value, key));
            var index = result.FindIndex(p => p.Key == typedKey);
            if (index >= 0)
            {
                result[index] = new(typedKey, typedValue);
            }
            else
            {
                result.Add(new(typedKey, typedValue));
            }
        }

        return result;
    }

    /// <summary>https://webidl.spec.whatwg.org/#js-dictionary: the value must be undefined, null or an object.</summary>
    internal void CheckDictionary(TValue value, string dictionaryName)
    {
        if (!IsNullOrUndefined(value) && !Adapter.IsObject(value))
            throw CreateTypeError($"The value is not of type '{dictionaryName}'");
    }

    /// <summary>Returns the value of a member of a dictionary (undefined when the dictionary is null or undefined).</summary>
    internal TValue GetDictionaryMember(TValue dictionary, string name)
    {
        if (IsNullOrUndefined(dictionary))
            return Adapter.Undefined;

        // The names of the members of the IDL dictionaries are a finite set: their keys are created once per realm
        _dictionaryMemberKeys ??= new(StringComparer.Ordinal);
        if (!_dictionaryMemberKeys.TryGetValue(name, out var key))
        {
            key = Key(name);
            _dictionaryMemberKeys.Add(name, key);
        }

        return Adapter.Get(dictionary, key);
    }

    internal Exception MissingRequiredDictionaryMember(string dictionaryName, string memberName)
        => CreateTypeError($"Failed to read the '{memberName}' property from '{dictionaryName}': Required member is undefined.");

    /// <summary>
    /// Whether the value has a callable @@iterator (a sequence in a union or an overload). The method is kept for the
    /// conversion of the value to a sequence (<see cref="ToSequence"/>), which must not get it again.
    /// </summary>
    internal bool HasIterator(TValue value)
    {
        if (!Adapter.IsObject(value))
            return false;

        var method = Adapter.Get(value, Adapter.GetWellKnownSymbol(JsWellKnownSymbol.Iterator));
        if (!Adapter.IsCallable(method))
            return false;

        _iteratorMethodTarget = value;
        _iteratorMethod = method;
        return true;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Overload resolution (https://webidl.spec.whatwg.org/#es-overloads)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Returns the index of the overload to invoke, or throws a TypeError.</summary>
    internal int SelectOverload(string operationName, OverloadInfo[] overloads, ReadOnlySpan<TValue> arguments)
    {
        if (overloads.Length == 1)
            return 0;

        var maxArguments = 0;
        foreach (var overload in overloads)
        {
            maxArguments = Math.Max(maxArguments, overload.IsVariadic ? int.MaxValue : overload.Arguments.Length);
        }

        var n = Math.Min(arguments.Length, maxArguments);
        List<int> candidates;
        while (true)
        {
            candidates = [];
            for (var i = 0; i < overloads.Length; i++)
            {
                var overload = overloads[i];
                if (overload.RequiredCount <= n && (n <= overload.Arguments.Length || overload.IsVariadic))
                {
                    candidates.Add(i);
                }
            }

            if (candidates.Count > 0 || n == 0)
                break;

            n--;
        }

        if (candidates.Count == 0)
        {
            var required = overloads.Min(o => o.RequiredCount);
            throw NotEnoughArguments(operationName, required, arguments.Length);
        }

        if (candidates.Count == 1)
            return candidates[0];

        // Find the distinguishing argument index
        for (var d = 0; d < n; d++)
        {
            var distinct = new HashSet<string>(StringComparer.Ordinal);
            foreach (var candidate in candidates)
            {
                distinct.Add(overloads[candidate].GetArgumentAt(d)?.TypeName ?? "");
            }

            if (distinct.Count <= 1)
                continue;

            var value = arguments[d];
            foreach (var predicate in GetOverloadPredicates(value))
            {
                foreach (var candidate in candidates)
                {
                    if (overloads[candidate].GetArgumentAt(d) is { } argument && predicate(argument))
                        return candidate;
                }
            }

            throw CreateTypeError($"Failed to execute '{operationName}': No overload matches the arguments");
        }

        return candidates.OrderBy(c => overloads[c].Arguments.Length).First();
    }

    /// <summary>
    /// Whether an IDL type (its name, with the typedefs resolved or not) has a member type of a buffer source kind
    /// (https://webidl.spec.whatwg.org/#es-overloads: the typed array types whose name is the [[TypedArrayName]] of the object).
    /// </summary>
    private static bool AcceptsBufferKind(string typeName, JsBufferKind kind)
    {
        var name = JsBufferSource.GetName(kind);
        foreach (var token in typeName.Split([' ', '(', ')', '?', '<', '>', ','], StringSplitOptions.RemoveEmptyEntries))
        {
            if (token == name || token is "BufferSource" or "AllowSharedBufferSource" || (token == "ArrayBufferView" && kind is not (JsBufferKind.ArrayBuffer or JsBufferKind.SharedArrayBuffer)))
                return true;
        }

        return false;
    }

    private IEnumerable<Func<OverloadArgument, bool>> GetOverloadPredicates(TValue value)
    {
        if (Adapter.IsUndefined(value))
        {
            yield return a => a.Optional;
        }

        if (IsNullOrUndefined(value))
        {
            yield return a => (a.Kinds & (OverloadTypeKinds.Nullable | OverloadTypeKinds.Dictionary)) != OverloadTypeKinds.None;
        }

        if (Unwrap(value) is { } platformObject)
        {
            yield return a => a.InterfaceTypes.Any(t => t.IsInstanceOfType(platformObject));
        }

        if (Adapter.IsObject(value))
        {
            if (Adapter.TryGetBufferSource(value, out var bufferKind, out _, out _))
            {
                // An entry whose type is the buffer source type of the object (e.g. a Uint8Array does not match a Uint8ClampedArray)
                yield return a => (a.Kinds & OverloadTypeKinds.BufferSource) != OverloadTypeKinds.None && AcceptsBufferKind(a.TypeName, bufferKind);
            }

            if (Adapter.IsCallable(value))
            {
                yield return a => (a.Kinds & OverloadTypeKinds.CallbackFunction) != OverloadTypeKinds.None;
            }

            var hasIterator = HasIterator(value);
            yield return a => (a.Kinds & OverloadTypeKinds.Sequence) != OverloadTypeKinds.None && hasIterator;
            yield return a => (a.Kinds & (OverloadTypeKinds.Record | OverloadTypeKinds.CallbackInterface | OverloadTypeKinds.Dictionary | OverloadTypeKinds.Object)) != OverloadTypeKinds.None;
        }

        if (Adapter.IsBoolean(value))
        {
            yield return a => (a.Kinds & OverloadTypeKinds.Boolean) != OverloadTypeKinds.None;
        }

        if (Adapter.IsNumber(value))
        {
            yield return a => (a.Kinds & OverloadTypeKinds.Numeric) != OverloadTypeKinds.None;
        }

        yield return a => (a.Kinds & OverloadTypeKinds.String) != OverloadTypeKinds.None;
        yield return a => (a.Kinds & OverloadTypeKinds.Numeric) != OverloadTypeKinds.None;
        yield return a => (a.Kinds & OverloadTypeKinds.Boolean) != OverloadTypeKinds.None;
        yield return a => (a.Kinds & OverloadTypeKinds.Any) != OverloadTypeKinds.None;
    }
}

/// <summary>The categories of an IDL type used by the overload resolution algorithm (the union of the categories of its flattened member types).</summary>
[Flags]
internal enum OverloadTypeKinds
{
    None = 0,
    Nullable = 1,
    Dictionary = 2,
    Interface = 4,
    CallbackFunction = 8,
    CallbackInterface = 16,
    Sequence = 32,
    Record = 64,
    Object = 128,
    Boolean = 256,
    Numeric = 512,
    String = 1024,
    Any = 2048,
    BufferSource = 4096,
}

/// <summary>An argument of an overload (https://webidl.spec.whatwg.org/#dfn-effective-overload-set).</summary>
internal sealed class OverloadArgument(string typeName, OverloadTypeKinds kinds, Type[] interfaceTypes, bool optional, bool variadic)
{
    /// <summary>The IDL type, used to find the distinguishing argument.</summary>
    public string TypeName { get; } = typeName;

    public OverloadTypeKinds Kinds { get; } = kinds;

    /// <summary>The .NET types of the interfaces of the type.</summary>
    public Type[] InterfaceTypes { get; } = interfaceTypes;

    public bool Optional { get; } = optional;

    public bool Variadic { get; } = variadic;
}

/// <summary>An overload of an operation or a constructor.</summary>
internal sealed class OverloadInfo(OverloadArgument[] arguments)
{
    public OverloadArgument[] Arguments { get; } = arguments;

    public bool IsVariadic => Arguments.Length > 0 && Arguments[^1].Variadic;

    public int RequiredCount => Arguments.Count(a => !a.Optional && !a.Variadic);

    public OverloadArgument? GetArgumentAt(int index)
    {
        if (index < Arguments.Length)
            return Arguments[index];

        return IsVariadic ? Arguments[^1] : null;
    }
}
