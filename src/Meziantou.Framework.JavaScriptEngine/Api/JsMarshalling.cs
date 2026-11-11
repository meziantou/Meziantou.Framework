using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meziantou.Framework.JavaScriptEngine;

// The conversions between the .NET values and the JavaScript values. They test the types of the values: no reflection,
// so they work with trimming and Native AOT. They run on the thread of the engine, in a scope of the engine.
public sealed partial class JsRealm
{
    /// <summary>
    /// Converts a .NET value to a JavaScript value, creating the objects in this realm:
    /// <list type="bullet">
    /// <item><description>null is <c>null</c>; a <see cref="JsValue"/> and a <see cref="JsObject"/> are not converted;</description></item>
    /// <item><description>a boolean, a string and a char are a boolean and a string; the numeric types are a number (a <see cref="long"/> or a <see cref="decimal"/> may lose precision), a <see cref="BigInteger"/> is a BigInt;</description></item>
    /// <item><description>a <see cref="DateTimeOffset"/> and a <see cref="DateTime"/> are a Date (a <see cref="DateTime"/> whose kind is unspecified is a UTC date);</description></item>
    /// <item><description>a <see cref="JsonElement"/> and a <see cref="JsonNode"/> are the value that <c>JSON.parse</c> would create;</description></item>
    /// <item><description>an instance of a class marked with <see cref="JsExportAttribute"/> is an object of its JavaScript class (always the same object for the same instance, see <see cref="GetClass(JsClass)"/>);</description></item>
    /// <item><description>a dictionary whose keys are strings (<see cref="IDictionary{TKey, TValue}"/>, <see cref="IReadOnlyDictionary{TKey, TValue}"/> or <see cref="IDictionary"/>) is an ordinary object;</description></item>
    /// <item><description>any other <see cref="IEnumerable"/> is an array.</description></item>
    /// </list>
    /// The values of the collections are converted recursively.
    /// </summary>
    /// <exception cref="NotSupportedException">The value, or a value of a collection, has a type that cannot be converted (e.g. a delegate or a class: use <see cref="JsHostObject"/> or <see cref="CreateFunction(string, int, JsNativeFunction)"/>).</exception>
    /// <exception cref="InvalidOperationException">The value contains a cycle, or no engine is entered on the thread.</exception>
    public JsValue FromDotNet(object? value)
    {
        if (JsEngine.Current is null)
            JsObject.ThrowNoEngine();

        return new DotNetToJsConverter(this).Convert(value);
    }

    /// <summary>Converts a .NET value of type <typeparamref name="T"/> to a JavaScript value, as <see cref="FromDotNet(object?)"/> does (without boxing the primitive types).</summary>
    /// <exception cref="NotSupportedException">The value has a type that cannot be converted.</exception>
    /// <exception cref="InvalidOperationException">The value contains a cycle, or no engine is entered on the thread.</exception>
    public JsValue FromDotNet<T>(T value)
    {
        if (JsEngine.Current is null)
            JsObject.ThrowNoEngine();

        if (typeof(T) == typeof(JsValue))
            return (JsValue)(object)value!;
        if (typeof(T) == typeof(bool))
            return JsValue.FromBoolean((bool)(object)value!);
        if (typeof(T) == typeof(double))
            return new JsValue((double)(object)value!);
        if (typeof(T) == typeof(int))
            return new JsValue((int)(object)value!);
        if (typeof(T) == typeof(long))
            return new JsValue((long)(object)value!);
        if (typeof(T) == typeof(string))
            return value is null ? JsValue.Null : JsValue.FromString((string)(object)value);

        return FromDotNet((object?)value);
    }

    /// <summary>
    /// Creates a function that calls <paramref name="function"/>: the arguments are converted with
    /// <see cref="JsEngine.ToDotNet{T}(JsValue)"/>, and the result with <see cref="FromDotNet{T}(T)"/>. A conversion that fails
    /// throws a TypeError (or a RangeError) to the script; the other exceptions of the function are not JavaScript errors
    /// (throw a <see cref="JsException"/> to throw a JavaScript error).
    /// </summary>
    /// <exception cref="NotSupportedException">A type of the delegate is not supported by the conversions.</exception>
    public JsFunction CreateFunction<TResult>(string name, Func<TResult> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        DotNetConversions.EnsureSupported<TResult>();
        return CreateFunction(name, 0, (_, _) => FromDotNet(function()));
    }

    /// <inheritdoc cref="CreateFunction{TResult}(string, Func{TResult})"/>
    public JsFunction CreateFunction<T1, TResult>(string name, Func<T1, TResult> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        DotNetConversions.EnsureSupported<T1>();
        DotNetConversions.EnsureSupported<TResult>();
        return CreateFunction(name, 1, (_, arguments) => FromDotNet(function(Engine.ConvertArgument<T1>(arguments, 0))));
    }

    /// <inheritdoc cref="CreateFunction{TResult}(string, Func{TResult})"/>
    public JsFunction CreateFunction<T1, T2, TResult>(string name, Func<T1, T2, TResult> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        DotNetConversions.EnsureSupported<T1>();
        DotNetConversions.EnsureSupported<T2>();
        DotNetConversions.EnsureSupported<TResult>();
        return CreateFunction(name, 2, (_, arguments) => FromDotNet(function(Engine.ConvertArgument<T1>(arguments, 0), Engine.ConvertArgument<T2>(arguments, 1))));
    }

    /// <inheritdoc cref="CreateFunction{TResult}(string, Func{TResult})"/>
    public JsFunction CreateFunction<T1, T2, T3, TResult>(string name, Func<T1, T2, T3, TResult> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        DotNetConversions.EnsureSupported<T1>();
        DotNetConversions.EnsureSupported<T2>();
        DotNetConversions.EnsureSupported<T3>();
        DotNetConversions.EnsureSupported<TResult>();
        return CreateFunction(name, 3, (_, arguments) => FromDotNet(function(Engine.ConvertArgument<T1>(arguments, 0), Engine.ConvertArgument<T2>(arguments, 1), Engine.ConvertArgument<T3>(arguments, 2))));
    }

    /// <summary>Creates a function that calls <paramref name="action"/> and returns undefined (see <see cref="CreateFunction{TResult}(string, Func{TResult})"/>).</summary>
    /// <exception cref="NotSupportedException">A type of the delegate is not supported by the conversions.</exception>
    public JsFunction CreateFunction(string name, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return CreateFunction(name, 0, (_, _) =>
        {
            action();
            return JsValue.Undefined;
        });
    }

    /// <inheritdoc cref="CreateFunction(string, Action)"/>
    public JsFunction CreateFunction<T1>(string name, Action<T1> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        DotNetConversions.EnsureSupported<T1>();
        return CreateFunction(name, 1, (_, arguments) =>
        {
            action(Engine.ConvertArgument<T1>(arguments, 0));
            return JsValue.Undefined;
        });
    }

    /// <inheritdoc cref="CreateFunction(string, Action)"/>
    public JsFunction CreateFunction<T1, T2>(string name, Action<T1, T2> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        DotNetConversions.EnsureSupported<T1>();
        DotNetConversions.EnsureSupported<T2>();
        return CreateFunction(name, 2, (_, arguments) =>
        {
            action(Engine.ConvertArgument<T1>(arguments, 0), Engine.ConvertArgument<T2>(arguments, 1));
            return JsValue.Undefined;
        });
    }

    private readonly struct DotNetToJsConverter(JsRealm realm)
    {
        private readonly HashSet<object> _visiting = new(ReferenceEqualityComparer.Instance);

        public JsValue Convert(object? value)
        {
            switch (value)
            {
                case null:
                    return JsValue.Null;
                case JsValue jsValue:
                    return jsValue;
                case JsObject obj:
                    return obj;
                case string str:
                    return JsValue.FromString(str);
                case bool boolean:
                    return JsValue.FromBoolean(boolean);
                case char c:
                    return JsValue.FromString(c.ToString());
                case double number:
                    return number;
                case float number:
                    return number;
                case int number:
                    return number;
                case long number:
                    return number;
                case short number:
                    return number;
                case byte number:
                    return number;
                case sbyte number:
                    return number;
                case ushort number:
                    return number;
                case uint number:
                    return number;
                case ulong number:
                    return number;
                case decimal number:
                    return (double)number;
                case Half number:
                    return (double)number;
                case BigInteger bigInteger:
                    return JsValue.FromBigInt(bigInteger);
                case DateTimeOffset date:
                    return realm.CreateDate(date.ToUnixTimeMilliseconds());
                case DateTime date:
                    return realm.CreateDate(new DateTimeOffset(date.Kind is DateTimeKind.Unspecified ? DateTime.SpecifyKind(date, DateTimeKind.Utc) : date).ToUnixTimeMilliseconds());
                case JsonElement element:
                    return ConvertJson(element);
                case JsonNode node:
                    return ConvertJson(node);
                case IJsExportable exportable:
                    return realm.GetExportedObject(exportable);
                case Delegate:
                    throw new NotSupportedException("A delegate cannot be converted to a JavaScript value: use JsRealm.CreateFunction");
            }

            if (!_visiting.Add(value))
                throw new InvalidOperationException("The value contains a cycle: it cannot be converted to a JavaScript value");

            try
            {
                switch (value)
                {
                    case IDictionary<string, object?> dictionary:
                        return ConvertDictionary(dictionary);
                    case IReadOnlyDictionary<string, object?> dictionary:
                        return ConvertDictionary(dictionary);
                    case IDictionary dictionary:
                        var obj = realm.CreateObject();
                        foreach (DictionaryEntry entry in dictionary)
                        {
                            if (entry.Key is not string key)
                                throw new NotSupportedException("The keys of a dictionary converted to a JavaScript object must be strings");

                            obj.CreateDataPropertyOrThrow(new PropertyKey(key), Convert(entry.Value));
                        }

                        return obj;
                    case IEnumerable enumerable:
                        var array = JsArray.Create(realm, []);
                        foreach (var item in enumerable)
                        {
                            array.Push(Convert(item));
                        }

                        return array;
                }
            }
            finally
            {
                _visiting.Remove(value);
            }

            throw new NotSupportedException($"A value of type '{value.GetType().FullName}' cannot be converted to a JavaScript value");
        }

        private JsObject ConvertDictionary(IEnumerable<KeyValuePair<string, object?>> dictionary)
        {
            var obj = realm.CreateObject();
            foreach (var (key, item) in dictionary)
            {
                obj.CreateDataPropertyOrThrow(new PropertyKey(key), Convert(item));
            }

            return obj;
        }

        private JsValue ConvertJson(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    var obj = realm.CreateObject();
                    foreach (var property in element.EnumerateObject())
                    {
                        obj.CreateDataPropertyOrThrow(new PropertyKey(property.Name), ConvertJson(property.Value));
                    }

                    return obj;
                case JsonValueKind.Array:
                    var array = JsArray.Create(realm, []);
                    foreach (var item in element.EnumerateArray())
                    {
                        array.Push(ConvertJson(item));
                    }

                    return array;
                case JsonValueKind.String:
                    return JsValue.FromString(element.GetString()!);
                case JsonValueKind.Number:
                    return double.Parse(element.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture);
                case JsonValueKind.True:
                    return JsValue.True;
                case JsonValueKind.False:
                    return JsValue.False;
                default:
                    return JsValue.Null;
            }
        }

        private JsValue ConvertJson(JsonNode node)
        {
            switch (node)
            {
                case JsonObject jsonObject:
                    var obj = realm.CreateObject();
                    foreach (var (key, item) in jsonObject)
                    {
                        obj.CreateDataPropertyOrThrow(new PropertyKey(key), item is null ? JsValue.Null : ConvertJson(item));
                    }

                    return obj;
                case JsonArray jsonArray:
                    var array = JsArray.Create(realm, []);
                    foreach (var item in jsonArray)
                    {
                        array.Push(item is null ? JsValue.Null : ConvertJson(item));
                    }

                    return array;
                default:
                    return ConvertJson(node.GetValue<JsonElement>());
            }
        }
    }
}

public sealed partial class JsEngine
{
    /// <summary>
    /// Converts a JavaScript value to a .NET value: undefined and null are null; a boolean, a number, a string and a BigInt
    /// are a <see cref="bool"/>, a <see cref="double"/>, a <see cref="string"/> and a <see cref="BigInteger"/>; a Date is a
    /// <see cref="DateTimeOffset"/> (null for an invalid date); an object of a class marked with <see cref="JsExportAttribute"/>
    /// is its .NET object; an array is a <see cref="List{T}"/> of the converted elements; another object is a <see cref="Dictionary{TKey, TValue}"/> of its own enumerable properties whose key is a
    /// string (read with [[Get]], so the getters run).
    /// </summary>
    /// <exception cref="NotSupportedException">The value is, or contains, a symbol or a function.</exception>
    /// <exception cref="InvalidOperationException">The value contains a cycle.</exception>
    /// <exception cref="JsException">The value contains a Date outside the range of <see cref="DateTimeOffset"/> (a RangeError), or a getter threw an exception.</exception>
    public object? ToDotNet(JsValue value)
    {
        using var scope = Enter();
        return ToDotNetCore(value, visiting: null);
    }

    /// <summary>
    /// Converts a JavaScript value to a .NET value of type <typeparamref name="T"/>:
    /// <list type="bullet">
    /// <item><description><see cref="JsValue"/>: the value; <see cref="JsObject"/>: the object, or null for undefined and null;</description></item>
    /// <item><description><see cref="string"/>: ToString (null for undefined and null); <see cref="bool"/>: ToBoolean; <see cref="BigInteger"/>: ToBigInt;</description></item>
    /// <item><description><see cref="double"/> and <see cref="float"/>: ToNumber; the integer types: ToNumber, which must be an integer in the range of the type;</description></item>
    /// <item><description><see cref="DateTimeOffset"/> and <see cref="DateTime"/> (UTC): a valid Date in the range of <see cref="DateTimeOffset"/>;</description></item>
    /// <item><description><see cref="object"/>: <see cref="ToDotNet(JsValue)"/>;</description></item>
    /// <item><description>a class marked with <see cref="JsExportAttribute"/>: the .NET object of an object of its JavaScript class (or of a derived class), null for undefined and null;</description></item>
    /// <item><description>a nullable value type: null for undefined and null, the conversion of the underlying type otherwise.</description></item>
    /// </list>
    /// undefined and null cannot be converted to a value type that is not nullable, except <see cref="bool"/>.
    /// </summary>
    /// <exception cref="JsException">The value cannot be converted (a TypeError or a RangeError), or a conversion of JavaScript threw an exception.</exception>
    /// <exception cref="NotSupportedException"><typeparamref name="T"/> is not one of the supported types.</exception>
    public T ToDotNet<T>(JsValue value)
    {
        using var scope = Enter();
        return DotNetConversions.Convert<T>(this, value);
    }

    internal T ConvertArgument<T>(ReadOnlySpan<JsValue> arguments, int index) => DotNetConversions.Convert<T>(this, index < arguments.Length ? arguments[index] : JsValue.Undefined);

    internal object? ToDotNetCore(JsValue value, HashSet<JsObject>? visiting)
    {
        switch (value.Kind)
        {
            case JsValueKind.Undefined:
            case JsValueKind.Null:
                return null;
            case JsValueKind.Boolean:
                return value.AsBoolean();
            case JsValueKind.Number:
                return value.NumberValue;
            case JsValueKind.String:
                return value.AsString();
            case JsValueKind.BigInt:
                return value.AsBigInt();
            case JsValueKind.Symbol:
                throw new NotSupportedException("A symbol cannot be converted to a .NET value");
        }

        var obj = value.AsObject();
        if (obj.IsCallable)
            throw new NotSupportedException("A function cannot be converted to a .NET value");

        if (obj is JsDateObject date)
            return double.IsNaN(date.DateValue) ? null : DotNetConversions.ToDateTimeOffset(date.DateValue);

        if (obj is JsHostObject { Target: IJsExportable exportable })
            return exportable;

        visiting ??= new HashSet<JsObject>(ReferenceEqualityComparer.Instance);
        if (!visiting.Add(obj))
            throw new InvalidOperationException("The value contains a cycle: it cannot be converted to a .NET value");

        try
        {
            if (JsOperations.IsArray(value))
            {
                var length = JsOperations.LengthOfArrayLike(obj);
                var list = new List<object?>((int)Math.Min(length, 1024));
                for (long i = 0; i < length; i++)
                {
                    list.Add(ToDotNetCore(obj.Get(PropertyKey.FromInteger(i)), visiting));
                }

                return list;
            }

            var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var key in obj.OwnPropertyKeys())
            {
                if (key.IsSymbol || !obj.GetOwnProperty(key, out var descriptor) || !descriptor.Enumerable)
                    continue;

                dictionary[key.AsString()] = ToDotNetCore(obj.Get(key), visiting);
            }

            return dictionary;
        }
        finally
        {
            visiting.Remove(obj);
        }
    }
}

/// <summary>The conversions of the JavaScript values to the .NET types of the typed functions.</summary>
internal static class DotNetConversions
{
    public static void EnsureSupported<T>()
    {
        if (!IsSupported(typeof(T)))
            throw new NotSupportedException($"The type '{typeof(T).FullName}' is not supported by the conversions of the JavaScript values");
    }

    private static bool IsSupported(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlyingType)
            type = underlyingType;

        return type == typeof(JsValue) || type == typeof(JsObject) || type == typeof(object) || type == typeof(string) || type == typeof(bool)
            || type == typeof(double) || type == typeof(float) || type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)
            || type == typeof(sbyte) || type == typeof(ushort) || type == typeof(uint) || type == typeof(ulong) || type == typeof(decimal)
            || type == typeof(BigInteger) || type == typeof(DateTimeOffset) || type == typeof(DateTime) || typeof(IJsExportable).IsAssignableFrom(type);
    }

    public static T Convert<T>(JsEngine engine, JsValue value)
    {
        if (typeof(T) == typeof(JsValue))
            return (T)(object)value;
        if (typeof(T) == typeof(object))
            return (T)engine.ToDotNetCore(value, visiting: null)!;
        if (typeof(T) == typeof(string))
            return value.IsNullOrUndefined ? default! : (T)(object)JsOperations.ToString(value);
        if (typeof(T) == typeof(JsObject))
            return value.IsNullOrUndefined ? default! : value.Cell is JsObject obj ? (T)(object)obj : ThrowHelper.ThrowTypeError<T>(JsOperations.DescribeForError(value) + " is not an object");
        if (typeof(IJsExportable).IsAssignableFrom(typeof(T)))
            return value.IsNullOrUndefined ? default! : value.Cell is JsHostObject { Target: T target } ? target : ThrowHelper.ThrowTypeError<T>(JsOperations.DescribeForError(value) + " is not a " + typeof(T).Name);
        if (typeof(T) == typeof(bool))
            return (T)(object)JsOperations.ToBoolean(value);

        // The nullable value types: null for undefined and null
        if (default(T) is null)
        {
            if (value.IsNullOrUndefined)
                return default!;

            return (T)ConvertValueType(Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T), value);
        }

        if (typeof(T) == typeof(double))
            return (T)(object)ToNumber(value);
        if (typeof(T) == typeof(int))
            return (T)(object)(int)ToInteger(value, int.MinValue, int.MaxValue);

        return (T)ConvertValueType(typeof(T), value);
    }

    private static object ConvertValueType(Type type, JsValue value)
    {
        if (type == typeof(bool))
            return JsOperations.ToBoolean(value);
        if (type == typeof(double))
            return ToNumber(value);
        if (type == typeof(float))
            return (float)ToNumber(value);
        if (type == typeof(int))
            return (int)ToInteger(value, int.MinValue, int.MaxValue);
        if (type == typeof(long))
            return (long)ToInteger(value, -9007199254740991, 9007199254740991);
        if (type == typeof(short))
            return (short)ToInteger(value, short.MinValue, short.MaxValue);
        if (type == typeof(byte))
            return (byte)ToInteger(value, byte.MinValue, byte.MaxValue);
        if (type == typeof(sbyte))
            return (sbyte)ToInteger(value, sbyte.MinValue, sbyte.MaxValue);
        if (type == typeof(ushort))
            return (ushort)ToInteger(value, ushort.MinValue, ushort.MaxValue);
        if (type == typeof(uint))
            return (uint)ToInteger(value, uint.MinValue, uint.MaxValue);
        if (type == typeof(ulong))
            return (ulong)ToInteger(value, 0, 9007199254740991);
        if (type == typeof(decimal))
        {
            var number = ToNumber(value);
            // (double)decimal.MaxValue rounds up to 2^96, which is out of range
            return double.IsFinite(number) && Math.Abs(number) < (double)decimal.MaxValue ? (decimal)number : ThrowHelper.ThrowRangeError<decimal>("The number cannot be converted to a decimal");
        }

        if (type == typeof(BigInteger))
            return RequireValue(value) ? BigIntOperations.ToBigInt(value) : default;
        if (type == typeof(DateTimeOffset))
            return ToDate(value);
        if (type == typeof(DateTime))
            return ToDate(value).UtcDateTime;

        throw new NotSupportedException($"The type '{type.FullName}' is not supported by the conversions of the JavaScript values");
    }

    private static bool RequireValue(JsValue value)
    {
        if (value.IsNullOrUndefined)
            ThrowHelper.ThrowTypeError("A value is required: " + JsOperations.DescribeForError(value) + " cannot be converted");

        return true;
    }

    private static double ToNumber(JsValue value)
    {
        RequireValue(value);
        return JsOperations.ToNumber(value);
    }

    private static double ToInteger(JsValue value, double min, double max)
    {
        var number = ToNumber(value);
        if (double.IsNaN(number) || Math.Truncate(number) != number)
            return ThrowHelper.ThrowTypeError<double>(NumberFormatting.ToString(number) + " is not an integer");

        if (number < min || number > max)
            return ThrowHelper.ThrowRangeError<double>(NumberFormatting.ToString(number) + " is out of range");

        return number;
    }

    // A Date spans the years -271821 to 275760, a DateTimeOffset the years 1 to 9999
    internal static DateTimeOffset ToDateTimeOffset(double timeValue)
    {
        if (timeValue < MinDateTimeOffsetMilliseconds || timeValue > MaxDateTimeOffsetMilliseconds)
            return ThrowHelper.ThrowRangeError<DateTimeOffset>("The date is outside the range of DateTimeOffset");

        return DateTimeOffset.FromUnixTimeMilliseconds((long)timeValue);
    }

    private const double MinDateTimeOffsetMilliseconds = -62135596800000; // DateTimeOffset.MinValue
    private const double MaxDateTimeOffsetMilliseconds = 253402300799999; // DateTimeOffset.MaxValue

    private static DateTimeOffset ToDate(JsValue value)
    {
        if (value.Cell is JsDateObject date && !double.IsNaN(date.DateValue))
            return ToDateTimeOffset(date.DateValue);

        return ThrowHelper.ThrowTypeError<DateTimeOffset>(JsOperations.DescribeForError(value) + " is not a valid Date");
    }
}
